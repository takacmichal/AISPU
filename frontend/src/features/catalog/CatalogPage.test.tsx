import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ConfirmProvider } from '../../components/confirm'
import { ToastProvider } from '../../components/toast'
import CatalogPage from './CatalogPage'

const component = (over: Record<string, unknown> = {}) => ({
  id: 'c1',
  type: 'Panel',
  code: 'PAN-400',
  name: 'Panel 400 W',
  manufacturer: 'Jinko',
  powerW: 400,
  unitPrice: 129.9,
  currency: 'EUR',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  ...over,
})
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })

function renderPage(roles: string[], path = '/catalog') {
  const session = { email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' }
  saveSession(session)
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider initialSession={session}>
        <ToastProvider>
          <ConfirmProvider>
            <CatalogPage />
          </ConfirmProvider>
        </ToastProvider>
      </AuthProvider>
    </MemoryRouter>,
  )
}

const calls = (fetchMock: ReturnType<typeof vi.fn>, method: string) =>
  fetchMock.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === method) as [string, RequestInit][]

describe('CatalogPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows loading and then the components with formatted price', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json([component()])))
    renderPage(['Sales'])
    expect(screen.getAllByText('Načítavam…').length).toBeGreaterThan(0)
    expect(await screen.findByText('PAN-400')).toBeInTheDocument()
    expect(screen.getByText(/129,90/)).toBeInTheDocument()
    expect(screen.getByText('Aktívny')).toBeInTheDocument()
  })

  it('shows an empty state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json([])))
    renderPage(['Sales'])
    expect(await screen.findByText('Katalóg zatiaľ neobsahuje žiadne komponenty.')).toBeInTheDocument()
  })

  it('shows an error state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json({ title: 'x' }, 500)))
    renderPage(['Sales'])
    expect(await screen.findByText('Katalóg sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('sends the type filter to the API', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => json([component()]))
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Sales'])
    await screen.findByText('PAN-400')
    await userEvent.selectOptions(screen.getByLabelText('Typ'), 'Inverter')
    await waitFor(() => expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain('type=Inverter'))
  })

  it('hides mutations from Designer', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json([component()])))
    renderPage(['Designer'])
    await screen.findByText('PAN-400')
    expect(screen.queryByRole('button', { name: 'Nový komponent' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Upraviť' })).not.toBeInTheDocument()
  })

  it('denies roles outside the catalog', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Customer'])
    expect(screen.getByRole('alert')).toHaveTextContent('nemáte oprávnenie')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('validates the create form before sending', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => json([]))
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Admin'])
    await userEvent.click(await screen.findByRole('button', { name: 'Nový komponent' }))
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť' }))
    expect(await screen.findByText('Vyplňte kód.')).toBeInTheDocument()
    expect(screen.getByText('Vyplňte názov.')).toBeInTheDocument()
    expect(screen.getByText('Zadajte výkon vo wattoch (celé číslo väčšie ako 0).')).toBeInTheDocument()
    expect(screen.getByText('Vyplňte cenu.')).toBeInTheDocument()
    expect(calls(fetchMock, 'POST')).toHaveLength(0)
  })

  it('creates a component', async () => {
    const fetchMock = vi.fn().mockImplementation(async (_url: string, init?: RequestInit) => (init?.method === 'POST' ? json(component(), 201) : json([])))
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Sales'])
    await userEvent.click(await screen.findByRole('button', { name: 'Nový komponent' }))
    await userEvent.type(screen.getByLabelText('Kód'), 'PAN-400')
    await userEvent.type(screen.getByLabelText('Názov'), 'Panel 400 W')
    await userEvent.type(screen.getByLabelText('Výkon (W)'), '400')
    await userEvent.type(screen.getByLabelText(/Cena/), '129,90')
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť' }))
    await waitFor(() => expect(calls(fetchMock, 'POST')).toHaveLength(1))
    const body = JSON.parse(String(calls(fetchMock, 'POST')[0][1].body))
    expect(body).toMatchObject({ type: 'Panel', code: 'PAN-400', powerW: 400, unitPrice: 129.9 })
    expect(await screen.findByText('Komponent bol uložený.')).toBeInTheDocument()
  })

  it('maps a 409 conflict to a message in the dialog', async () => {
    const fetchMock = vi.fn().mockImplementation(async (_url: string, init?: RequestInit) =>
      init?.method === 'POST' ? json({ title: 'Conflict', detail: 'Kód už existuje.' }, 409) : json([]),
    )
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Sales'])
    await userEvent.click(await screen.findByRole('button', { name: 'Nový komponent' }))
    await userEvent.type(screen.getByLabelText('Kód'), 'X')
    await userEvent.type(screen.getByLabelText('Názov'), 'X')
    await userEvent.type(screen.getByLabelText('Výkon (W)'), '1')
    await userEvent.type(screen.getByLabelText(/Cena/), '1')
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť' }))
    expect(await within(screen.getByRole('dialog')).findByText('Kód už existuje.')).toBeInTheDocument()
  })

  it('requires confirmation of a price change', async () => {
    const fetchMock = vi.fn().mockImplementation(async (_url: string, init?: RequestInit) => (init?.method === 'PUT' ? json(component({ unitPrice: 140 })) : json([component()])))
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Admin'])
    await userEvent.click(await screen.findByRole('button', { name: 'Upraviť' }))
    const price = screen.getByLabelText(/Cena/)
    await userEvent.clear(price)
    await userEvent.type(price, '140')
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť' }))
    expect(await screen.findByText('Zmena ceny')).toBeInTheDocument()
    expect(calls(fetchMock, 'PUT')).toHaveLength(0)
    await userEvent.click(screen.getByRole('button', { name: 'Zmeniť cenu' }))
    await waitFor(() => expect(calls(fetchMock, 'PUT')).toHaveLength(1))
  })

  it('does not save when the price confirmation is cancelled', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => json([component()]))
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Admin'])
    await userEvent.click(await screen.findByRole('button', { name: 'Upraviť' }))
    const price = screen.getByLabelText(/Cena/)
    await userEvent.clear(price)
    await userEvent.type(price, '140')
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť' }))
    const confirmDialog = (await screen.findByText('Zmena ceny')).closest('[role="dialog"]') as HTMLElement
    await userEvent.click(within(confirmDialog).getByRole('button', { name: 'Zrušiť' }))
    expect(calls(fetchMock, 'PUT')).toHaveLength(0)
  })

  it('activates an inactive component without confirmation and deactivates after confirmation', async () => {
    const fetchMock = vi.fn().mockImplementation(async (_url: string, init?: RequestInit) =>
      init?.method === 'POST' ? json(component()) : json([component({ isActive: false }), component({ id: 'c2', code: 'INV-1', type: 'Inverter' })]),
    )
    vi.stubGlobal('fetch', fetchMock)
    renderPage(['Sales'])
    await userEvent.click(await screen.findByRole('button', { name: 'Aktivovať' }))
    await waitFor(() => expect(String(calls(fetchMock, 'POST')[0][0])).toContain('/api/components/c1/activate'))

    await userEvent.click(await screen.findByRole('button', { name: 'Deaktivovať' }))
    expect(calls(fetchMock, 'POST')).toHaveLength(1)
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Deaktivovať' }))
    await waitFor(() => expect(calls(fetchMock, 'POST')).toHaveLength(2))
  })
})
