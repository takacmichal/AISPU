import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../auth/AuthContext'
import { json, makeQuote, mockApi, problem } from '../features/quotes/testUtils'
import Dashboard from './Dashboard'

const systemInfo = json({ name: 'SolarOps API', version: '1.2.3', environment: 'Development', serverTime: '2026-11-03T12:00:00Z' })

const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString()

const quotes = json([
  makeQuote({ id: 'aaaaaaaa-0000-0000-0000-000000000001', status: 'Draft', updatedAt: minutesAgo(5) }),
  makeQuote({ id: 'bbbbbbbb-0000-0000-0000-000000000002', status: 'Sent', updatedAt: minutesAgo(120) }),
  makeQuote({ id: 'cccccccc-0000-0000-0000-000000000003', status: 'Accepted', updatedAt: minutesAgo(3000) }),
])

const contracts = json([
  { id: 'k1', number: 'ZML-1', status: 'Draft' },
  { id: 'k2', number: 'ZML-2', status: 'Signed' },
  { id: 'k3', number: 'ZML-3', status: 'Draft' },
])

const installations = json([
  { id: 'i1', name: 'FVE Novák', state: 'Mounting', updatedAt: minutesAgo(30) },
  { id: 'i2', name: 'FVE Horváth', state: 'Mounting', updatedAt: minutesAgo(4000) },
  { id: 'i3', name: 'FVE Kováč', state: 'HandedOver', updatedAt: minutesAgo(9000) },
])

const ticket = (over: Record<string, unknown>) => ({
  id: 't1',
  installationId: 'i1',
  customerId: 'c1',
  title: 'Tiket',
  priority: 'Medium',
  origin: 'Manual',
  state: 'New',
  createdAt: minutesAgo(10),
  updatedAt: minutesAgo(10),
  ...over,
})

const tickets = json({
  items: [
    ticket({ id: 'a1', title: 'Pokles výkonu FVE Novák', origin: 'AnomalyDetected', priority: 'High', createdAt: minutesAgo(5) }),
    ticket({ id: 'a2', title: 'Podvýroba FVE Kováč', origin: 'AnomalyDetected', state: 'Triaged', createdAt: minutesAgo(60) }),
    ticket({ id: 'a3', title: 'Vyriešený alarm', origin: 'AnomalyDetected', state: 'Resolved', createdAt: minutesAgo(1) }),
    ticket({ id: 'm1', title: 'Hlásenie zákazníka', origin: 'CustomerReport', state: 'RemoteDiagnosis' }),
    ticket({ id: 'm2', title: 'Uzavretý tiket', state: 'Closed' }),
  ],
  page: 1,
  pageSize: 100,
  totalCount: 5,
})

function renderDashboard(...roles: string[]) {
  return render(
    <MemoryRouter>
      <AuthProvider initialSession={{ email: 'u@test.sk', roles, accessToken: 't', refreshToken: 'r' }}>
        <Dashboard />
      </AuthProvider>
    </MemoryRouter>,
  )
}

const kpis = () => screen.getByRole('group', { name: 'Kľúčové ukazovatele' })
const card = (name: string) => screen.getByRole('link', { name }).closest('article') as HTMLElement

describe('Dashboard', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('shows loading skeletons while KPIs load', () => {
    const pending = () => new Promise<Response>(() => {})
    mockApi({
      'GET /api/system/info': pending,
      'GET /api/customers': pending,
      'GET /api/quotes': pending,
      'GET /api/contracts': pending,
      'GET /api/installations': pending,
      'GET /api/tickets': pending,
    })
    renderDashboard('Admin')
    expect(screen.getByText('Načítavam ukazovateľ… Zákazníci')).toBeInTheDocument()
    expect(screen.getByText('Načítavam ukazovateľ… Inštalácie v realizácii')).toBeInTheDocument()
    expect(screen.getByText('Načítavam poslednú aktivitu…')).toBeInTheDocument()
  })

  it('renders KPI counts for Admin and the recent activity list', async () => {
    mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/customers': json({ items: [], page: 1, pageSize: 1, totalCount: 1234 }),
      'GET /api/quotes': quotes,
      'GET /api/contracts': contracts,
      'GET /api/installations': installations,
      'GET /api/tickets': tickets,
    })
    renderDashboard('Admin')

    expect(await within(kpis()).findByText(/^1\s234$/)).toBeInTheDocument()
    expect(within(card('Otvorené ponuky')).getByText('2')).toBeInTheDocument()
    expect(within(card('Otvorené ponuky')).getByText('Odoslané: 1 · Rozpracované: 1')).toBeInTheDocument()
    expect(within(card('Zmluvy na podpis')).getByText('2')).toBeInTheDocument()
    const inst = card('Inštalácie v realizácii')
    expect(within(inst).getByText('Montáž')).toBeInTheDocument()
    expect(within(inst).getByText('Odovzdané zákazníkom: 1')).toBeInTheDocument()

    const pulse = screen.getByRole('article', { name: 'Posledná aktivita' })
    const items = within(pulse).getAllByRole('listitem')
    expect(items).toHaveLength(6)
    expect(within(items[0]).getByRole('link', { name: '#AAAAAAAA' })).toHaveAttribute('href', '/quotes/aaaaaaaa-0000-0000-0000-000000000001')
    expect(within(items[0]).getByText('Rozpracovaná')).toBeInTheDocument()
    expect(within(items[0]).getByText('pred 5 minútami')).toBeInTheDocument()
    expect(within(items[1]).getByRole('link', { name: 'FVE Novák' })).toHaveAttribute('href', '/installations/i1')
    expect(within(items[1]).getByText('Montáž')).toBeInTheDocument()

    expect(await screen.findByText(/SolarOps API/)).toBeInTheDocument()
  })

  it('hides a KPI card when the API answers 403', async () => {
    mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/customers': problem(403, 'Forbidden'),
      'GET /api/quotes': quotes,
      'GET /api/contracts': contracts,
    })
    renderDashboard('Sales')

    expect(await within(kpis()).findByRole('link', { name: 'Otvorené ponuky' })).toBeInTheDocument()
    await screen.findByText('Odoslané: 1 · Rozpracované: 1')
    expect(screen.queryByRole('link', { name: 'Zákazníci' })).not.toBeInTheDocument()
    expect(screen.queryByText('Údaje sa nepodarilo načítať.')).not.toBeInTheDocument()
  })

  it('does not call APIs the role cannot use', async () => {
    const fetch = mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/installations': installations,
    })
    renderDashboard('Technician')

    expect(await within(kpis()).findByRole('link', { name: 'Inštalácie v realizácii' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Zákazníci' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Otvorené ponuky' })).not.toBeInTheDocument()
    const paths = fetch.mock.calls.map(([input]) => new URL(String(input instanceof Request ? input.url : input), 'http://x').pathname)
    expect(paths).not.toContain('/api/customers')
    expect(paths).not.toContain('/api/quotes')
    expect(paths).not.toContain('/api/contracts')
  })

  it('shows an error with retry when a KPI fails to load', async () => {
    let calls = 0
    mockApi({
      'GET /api/system/info': problem(503, 'down'),
      'GET /api/installations': () => (++calls === 1 ? problem(500, 'boom') : installations.clone()),
      'GET /api/tickets': tickets,
    })
    renderDashboard('Operator')

    const inst = () => card('Inštalácie v realizácii')
    expect(await within(inst()).findByText('Údaje sa nepodarilo načítať.')).toBeInTheDocument()
    expect(screen.getByText('Poslednú aktivitu sa nepodarilo načítať.')).toBeInTheDocument()
    expect(await screen.findByText('API je nedostupné.')).toBeInTheDocument()

    await userEvent.click(within(inst()).getByRole('button', { name: 'Skúsiť znova' }))
    expect(await within(inst()).findByText('Odovzdané zákazníkom: 1')).toBeInTheDocument()
  })

  it('shows active alarms from anomaly detection for Operator', async () => {
    mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/installations': installations,
      'GET /api/tickets': tickets,
    })
    renderDashboard('Operator')

    const alarms = await screen.findByRole('article', { name: 'Posledné alarmy' })
    const items = await within(alarms).findAllByRole('listitem')
    expect(items.map((li) => within(li).getByRole('link').textContent)).toEqual(['Pokles výkonu FVE Novák', 'Podvýroba FVE Kováč'])
    expect(within(items[0]).getByRole('link')).toHaveAttribute('href', '/tickets/a1')
    expect(within(items[0]).getByText('Vysoká')).toBeInTheDocument()
    expect(within(alarms).getByRole('link', { name: 'Všetky alarmy' })).toHaveAttribute('href', '/tickets?origin=AnomalyDetected')

    expect(within(card('Aktívne alarmy')).getByText('2')).toBeInTheDocument()
    expect(within(card('Aktívne alarmy')).getByText('Vysoká priorita: 1')).toBeInTheDocument()
    expect(within(card('Otvorené tikety')).getByText('3')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Zákazníci' })).not.toBeInTheDocument()
  })

  it('shows an empty alarm list when nothing is detected', async () => {
    mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/installations': installations,
      'GET /api/tickets': json({ items: [], page: 1, pageSize: 100, totalCount: 0 }),
    })
    renderDashboard('Technician')

    expect(await screen.findByText('Žiadne aktívne alarmy')).toBeInTheDocument()
  })

  it('hides tickets and alarms for Sales', async () => {
    const fetch = mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/customers': json({ items: [], page: 1, pageSize: 1, totalCount: 3 }),
      'GET /api/quotes': quotes,
      'GET /api/contracts': contracts,
    })
    renderDashboard('Sales')

    expect(await within(kpis()).findByRole('link', { name: 'Otvorené ponuky' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Aktívne alarmy' })).not.toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'Posledné alarmy' })).not.toBeInTheDocument()
    const paths = fetch.mock.calls.map(([input]) => new URL(String(input instanceof Request ? input.url : input), 'http://x').pathname)
    expect(paths).not.toContain('/api/tickets')
  })

  it('hides the alarms when the tickets API answers 403', async () => {
    mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/installations': installations,
      'GET /api/tickets': problem(403, 'Forbidden'),
    })
    renderDashboard('Technician')

    expect(await within(kpis()).findByText('Odovzdané zákazníkom: 1')).toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'Posledné alarmy' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Otvorené tikety' })).not.toBeInTheDocument()
  })

  it('shows the customer portal instead of internal KPIs to customers', async () => {
    const fetch = mockApi({
      'GET /api/system/info': systemInfo,
      'GET /api/installations': json([]),
      'GET /api/connection-requests': json([]),
      'GET /api/tickets': json({ items: [], page: 1, pageSize: 5, totalCount: 0 }),
    })
    renderDashboard('Customer')

    expect(screen.getByRole('heading', { level: 2, name: 'Dobrý deň' })).toBeInTheDocument()
    expect(await screen.findByText('Zatiaľ nemáte žiadnu elektráreň')).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Kľúčové ukazovatele' })).not.toBeInTheDocument()
    const paths = fetch.mock.calls.map(([input]) => new URL(String(input instanceof Request ? input.url : input), 'http://x').pathname)
    expect(paths).not.toContain('/api/customers')
    expect(paths).not.toContain('/api/quotes')
    expect(await screen.findByText(/SolarOps API/)).toBeInTheDocument()
  })
})
