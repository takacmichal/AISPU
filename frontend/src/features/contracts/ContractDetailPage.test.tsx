import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ConfirmProvider } from '../../components/confirm'
import { ToastProvider } from '../../components/toast'
import ContractDetailPage from './ContractDetailPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const problem = (status: number, detail: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title: 'x', detail, errors }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })

const contract = (over: Record<string, unknown> = {}) => ({
  id: 'c1',
  number: 'Z-2026-0001',
  customerId: 'cu1',
  quoteId: 'q1',
  status: 'Draft',
  signedOn: null,
  installationId: null,
  depositStatus: 'NotPaid',
  depositPaidAmount: null,
  depositPaidOn: null,
  vatRatePercent: 23,
  currency: 'EUR',
  lines: [{ position: 1, componentId: 'k1', componentType: 'Panel', code: 'PAN-1', name: 'Panel 450 W', unitPrice: 100, quantity: 10, netPrice: 1000 }],
  netTotal: 1000,
  vatTotal: 230,
  grossTotal: 1230,
  createdBy: 'u',
  createdAt: '2026-01-02T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
  ...over,
})
const supplyPoint = {
  id: 'sp1',
  customerId: 'cu1',
  name: 'Dom',
  eicCode: '24ZZS00000000001',
  address: { street: 'Hlavná 1', city: 'Nitra', postalCode: '94901', country: 'SK' },
  mainBreakerAmps: 25,
}

type Handler = (url: string, init: RequestInit | undefined) => Response | undefined
function mockApi(state: { contract: ReturnType<typeof contract> }, extra: Handler = () => undefined) {
  const fetchMock = vi.fn().mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input instanceof Request ? input.url : input)
    const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase()
    const custom = extra(`${method} ${url}`, init)
    if (custom) return custom
    if (url.includes('/audit')) return json([{ action: 'Created', performedBy: 'u', performedAt: '2026-01-02T10:00:00Z', details: null }])
    if (url.includes('/supply-points')) return json([supplyPoint])
    if (url.includes('/api/connection-requests')) return json([])
    if (method === 'GET' && url.includes('/api/contracts/c1')) return json(state.contract)
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderPage(roles = ['Sales']) {
  saveSession({ email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' })
  const router = createMemoryRouter([{ path: '/contracts/:id', element: <ContractDetailPage /> }], { initialEntries: ['/contracts/c1'] })
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

const posts = (fetchMock: ReturnType<typeof vi.fn>, part: string) =>
  fetchMock.mock.calls.filter(([input, init]) => {
    const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase()
    return method === 'POST' && String(input instanceof Request ? input.url : input).includes(part)
  })

describe('ContractDetailPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows the contract with lines, totals in EUR and the audit trail', async () => {
    mockApi({ contract: contract() })
    renderPage()
    expect(await screen.findByRole('heading', { name: 'Zmluva Z-2026-0001' })).toBeInTheDocument()
    expect(screen.getByText('Panel 450 W')).toBeInTheDocument()
    expect(screen.getByText(/1\s?230,00\s?€/)).toBeInTheDocument()
    expect(await screen.findByText('Zmluva vytvorená')).toBeInTheDocument()
  })

  it('shows an error state when the contract cannot be loaded', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(problem(404, 'nie')))
    renderPage()
    expect(await screen.findByText('Zmluvu sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('hides write actions from roles that only read', async () => {
    mockApi({ contract: contract() })
    renderPage(['Designer'])
    await screen.findByRole('heading', { name: /Zmluva/ })
    expect(screen.queryByRole('button', { name: 'Podpísať zmluvu' })).not.toBeInTheDocument()
  })

  it('validates the supply point, then signs the contract', async () => {
    const user = userEvent.setup()
    const state = { contract: contract() }
    const fetchMock = mockApi(state, (key) => {
      if (key.startsWith('POST') && key.includes('/sign')) {
        state.contract = contract({ status: 'Signed', signedOn: '2026-03-01', installationId: 'i1' })
        return json(state.contract)
      }
    })
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Podpísať zmluvu' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Podpísať' }))
    expect(await within(dialog).findByText('Vyberte odberné miesto.')).toBeInTheDocument()
    expect(posts(fetchMock, '/sign')).toHaveLength(0)

    await user.selectOptions(await within(dialog).findByLabelText('Odberné miesto'), 'sp1')
    await user.click(within(dialog).getByRole('button', { name: 'Podpísať' }))

    await waitFor(() => expect(posts(fetchMock, '/sign')).toHaveLength(1))
    const [input, init] = posts(fetchMock, '/sign')[0]
    const body = JSON.parse(input instanceof Request ? await input.clone().text() : String(init?.body))
    expect(body.supplyPointId).toBe('sp1')
    expect(await screen.findByText('Zmluva bola podpísaná a inštalácia vytvorená.')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Zaznamenať zálohu' })).toBeInTheDocument()
  })

  it('shows the server message when signing conflicts', async () => {
    const user = userEvent.setup()
    mockApi({ contract: contract() }, (key) => (key.startsWith('POST') && key.includes('/sign') ? problem(409, 'Zmluva už je podpísaná.') : undefined))
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Podpísať zmluvu' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(await within(dialog).findByLabelText('Odberné miesto'), 'sp1')
    await user.click(within(dialog).getByRole('button', { name: 'Podpísať' }))
    expect((await within(dialog).findAllByText('Zmluva už je podpísaná.')).length).toBeGreaterThan(0)
  })

  it('records the deposit and unlocks the installation', async () => {
    const user = userEvent.setup()
    const state = { contract: contract({ status: 'Signed', signedOn: '2026-03-01', installationId: 'i1' }) }
    const fetchMock = mockApi(state, (key) => {
      if (key.startsWith('POST') && key.includes('/deposit/pay')) {
        state.contract = contract({ status: 'Signed', installationId: 'i1', depositStatus: 'Paid', depositPaidAmount: 300, depositPaidOn: '2026-03-02' })
        return json(state.contract)
      }
    })
    renderPage()
    expect(await screen.findByText(/Inštalácia sa odomkne po zaplatení zálohy/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Zaznamenať zálohu' }))
    const dialog = await screen.findByRole('dialog')

    await user.type(within(dialog).getByLabelText('Suma zálohy (EUR)'), '5000')
    await user.click(within(dialog).getByRole('button', { name: 'Uložiť zálohu' }))
    expect(await within(dialog).findByText(/Záloha nesmie presiahnuť/)).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Suma zálohy (EUR)'))
    await user.type(within(dialog).getByLabelText('Suma zálohy (EUR)'), '300,5')
    await user.click(within(dialog).getByRole('button', { name: 'Uložiť zálohu' }))

    await waitFor(() => expect(posts(fetchMock, '/deposit/pay')).toHaveLength(1))
    const [input, init] = posts(fetchMock, '/deposit/pay')[0]
    const body = JSON.parse(input instanceof Request ? await input.clone().text() : String(init?.body))
    expect(body.amount).toBe(300.5)
    expect(await screen.findByText(/Záloha je zaplatená, inštalácia je odomknutá\./)).toBeInTheDocument()
  })

  it('previews the PDF in an iframe and offers the download', async () => {
    const user = userEvent.setup()
    URL.createObjectURL = vi.fn(() => 'blob:pdf')
    URL.revokeObjectURL = vi.fn()
    mockApi({ contract: contract() }, (key) =>
      key.startsWith('POST') && key.includes('/pdf') ? new Response('%PDF', { status: 200, headers: { 'Content-Type': 'application/pdf' } }) : undefined,
    )
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Zobraziť náhľad PDF' }))
    expect(await screen.findByTitle('Náhľad PDF zmluvy')).toHaveAttribute('src', 'blob:pdf')
    expect(screen.getByRole('link', { name: 'Stiahnuť PDF' })).toHaveAttribute('download', 'zmluva-Z-2026-0001.pdf')
  })

  it('shows the server error when the PDF fails and lets the user try again', async () => {
    const user = userEvent.setup()
    URL.createObjectURL = vi.fn(() => 'blob:pdf')
    URL.revokeObjectURL = vi.fn()
    let failures = 0
    mockApi({ contract: contract() }, (key) => {
      if (!(key.startsWith('POST') && key.includes('/pdf'))) return undefined
      failures += 1
      if (failures === 1) return new Response(null, { status: 404 })
      return new Response('%PDF', { status: 200, headers: { 'Content-Type': 'application/pdf' } })
    })
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Zobraziť náhľad PDF' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('PDF zmluvy sa nepodarilo vygenerovať.')
    expect(screen.queryByText(/zatiaľ nie je na serveri dostupné/)).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Zobraziť náhľad PDF' }))
    expect(await screen.findByTitle('Náhľad PDF zmluvy')).toHaveAttribute('src', 'blob:pdf')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the reason from the server when the PDF cannot be generated', async () => {
    const user = userEvent.setup()
    mockApi({ contract: contract() }, (key) =>
      key.startsWith('POST') && key.includes('/pdf')
        ? new Response(JSON.stringify({ status: 409, title: 'Conflict', detail: 'Zmluva ešte nemá všetky údaje.' }), {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        : undefined,
    )
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Zobraziť náhľad PDF' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Zmluva ešte nemá všetky údaje.')
  })
})
