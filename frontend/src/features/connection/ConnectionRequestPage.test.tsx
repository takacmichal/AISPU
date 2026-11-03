import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ConfirmProvider } from '../../components/confirm'
import { ToastProvider } from '../../components/toast'
import ConnectionRequestPage from './ConnectionRequestPage'
import { ConnectionStepper } from './ConnectionStepper'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const request = (over: Record<string, unknown> = {}) => ({
  id: 'r1',
  installationId: 'i1',
  customerId: 'c1',
  supplyPointId: 's1',
  eicCode: '24ZZS00000000001',
  ratedAcPowerKw: 8,
  mainBreakerAmps: 25,
  type: 'SmallSource',
  state: 'Draft',
  attachments: [
    { id: 'a1', kind: 'SingleLineDiagram', fileName: 'schema.pdf', contentType: 'application/pdf', sizeBytes: 10, createdAt: '2026-01-02T00:00:00Z', hasContent: true, downloadUrl: '/api/connection-requests/r1/attachments/a1' },
    { id: 'a2', kind: 'OwnerConsent', fileName: 'suhlas.pdf', contentType: 'application/pdf', sizeBytes: 10, createdAt: '2026-01-02T00:00:00Z', hasContent: false, downloadUrl: null },
  ],
  createdAt: '2026-01-02T08:00:00Z',
  updatedAt: '2026-01-02T08:00:00Z',
  ...over,
})

function renderAt(ui: { path: string; element: React.ReactNode }, roles = ['Designer']) {
  saveSession({ email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' })
  const router = createMemoryRouter([{ path: ui.path, element: ui.element }], { initialEntries: [ui.path.replace(':id', 'r1')] })
  render(
    <AuthProvider>
      <ToastProvider>
        <ConfirmProvider>
          <RouterProvider router={router} />
        </ConfirmProvider>
      </ToastProvider>
    </AuthProvider>,
  )
}
const page = (roles?: string[]) => renderAt({ path: '/connection-requests/:id', element: <ConnectionRequestPage /> }, roles)

describe('ConnectionRequestPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('renders an accessible stepper with the current step marked', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(request({ state: 'ContractOffered' }))))
    page()
    const list = await screen.findByRole('list', { name: 'Priebeh žiadosti o pripojenie' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(6)
    expect(items[2]).toHaveAttribute('aria-current', 'step')
    expect(items[2]).toHaveTextContent('aktuálny krok')
    expect(items[0]).toHaveTextContent('dokončené')
  })

  it('warns about a missed deadline', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        json(request({ state: 'Submitted', submittedAt: '2026-01-03T08:00:00Z', proposalDueDate: '2026-01-17', proposalRemainingDays: -2, proposalOverdue: true })),
      ),
    )
    page()
    expect(await screen.findByRole('alert')).toHaveTextContent('Distribučka mešká s návrhom zmluvy o 2 dni.')
  })

  it('warns when the deadline is near', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(json(request({ state: 'Submitted', submittedAt: '2026-01-03T08:00:00Z', proposalRemainingDays: 1, proposalOverdue: false }))),
    )
    page()
    expect(await screen.findByRole('alert')).toHaveTextContent('Blíži sa lehota distribučky na návrh zmluvy, zostáva 1 deň.')
  })

  it('shows the history as a timeline', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(json(request({ state: 'ContractOffered', submittedAt: '2026-01-03T08:00:00Z', decidedAt: '2026-01-10T08:00:00Z', decisionNote: 'Podmienky v prílohe' }))),
    )
    page()
    expect(await screen.findByText('Rozhodnutie distribučky')).toBeInTheDocument()
    expect(screen.getByText('Podmienky v prílohe')).toBeInTheDocument()
    expect(screen.getByText('Žiadosť podaná distribučke')).toBeInTheDocument()
  })

  it('lists attachments and marks metadata-only ones', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(request())))
    page()
    expect(await screen.findByText(/schema\.pdf/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Stiahnuť/ })).toBeInTheDocument()
    expect(screen.getByText('bez obsahu')).toBeInTheDocument()
  })

  it('submits a draft after confirmation', async () => {
    const user = userEvent.setup()
    let current = request()
    const fetchMock = vi.fn().mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).endsWith('/submit') && init?.method === 'POST') {
        current = request({ state: 'Submitted', submittedAt: '2026-01-03T08:00:00Z', proposalRemainingDays: 14, proposalOverdue: false })
        return json(current)
      }
      return json(current)
    })
    vi.stubGlobal('fetch', fetchMock)
    page()
    await user.click(await screen.findByRole('button', { name: 'Podať distribučke' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Podať distribučke' }))
    await waitFor(() => expect(fetchMock.mock.calls.some(([u]) => String(u).endsWith('/r1/submit'))).toBe(true))
    expect(await screen.findByText('Žiadosť bola podaná distribučke.')).toBeInTheDocument()
    expect((await screen.findAllByText('Podaná distribučke')).length).toBeGreaterThan(0)
  })

  it('shows the server message when a transition conflicts', async () => {
    const user = userEvent.setup()
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) =>
        String(input).endsWith('/submit') && init?.method === 'POST'
          ? new Response(JSON.stringify({ detail: 'Žiadosť už bola podaná.' }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } })
          : json(request()),
      ),
    )
    page()
    await user.click(await screen.findByRole('button', { name: 'Podať distribučke' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Podať distribučke' }))
    expect((await screen.findAllByText('Žiadosť už bola podaná.')).length).toBeGreaterThan(0)
  })

  it('hides the next step from roles that cannot do it', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(request())))
    page(['Sales'])
    await screen.findByRole('list', { name: 'Priebeh žiadosti o pripojenie' })
    expect(screen.queryByRole('button', { name: 'Podať distribučke' })).not.toBeInTheDocument()
  })

  it('shows the error state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 404 })))
    page()
    expect(await screen.findByText('Žiadosť o pripojenie sa nepodarilo načítať.')).toBeInTheDocument()
  })
})

describe('ConnectionStepper', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())
  const stepper = (props: { installationId?: string; contractId?: string }) =>
    renderAt({ path: '/connection-requests/:id', element: <ConnectionStepper {...props} /> })

  it('loads the newest request of an installation and links to it', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json([request({ state: 'Submitted' })]))
    vi.stubGlobal('fetch', fetchMock)
    stepper({ installationId: 'i1' })
    expect(await screen.findByRole('list', { name: 'Priebeh žiadosti o pripojenie' })).toBeInTheDocument()
    expect(String(fetchMock.mock.calls[0][0])).toContain('installationId=i1')
    expect(screen.getByRole('link', { name: 'Otvoriť žiadosť o pripojenie' })).toHaveAttribute('href', '/connection-requests/r1')
  })

  it('shows the empty state when there is no request', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json([])))
    stepper({ installationId: 'i1' })
    expect(await screen.findByText('Žiadosť o pripojenie zatiaľ nebola vytvorená.')).toBeInTheDocument()
  })

  it('resolves the installation from a contract', async () => {
    const fetchMock = vi.fn().mockImplementation(async (input: RequestInfo | URL) =>
      String(input instanceof Request ? input.url : input).includes('/api/contracts/') ? json({ id: 'c1', installationId: 'i7' }) : json([request()]),
    )
    vi.stubGlobal('fetch', fetchMock)
    stepper({ contractId: 'c1' })
    expect(await screen.findByRole('list', { name: 'Priebeh žiadosti o pripojenie' })).toBeInTheDocument()
    expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain('installationId=i7')
  })

  it('shows the error state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })))
    stepper({ installationId: 'i1' })
    expect(await screen.findByText('Priebeh pripojenia sa nepodarilo načítať.')).toBeInTheDocument()
  })
})
