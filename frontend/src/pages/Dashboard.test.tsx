import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../auth/AuthContext'
import { json, makeQuote, mockApi, problem } from '../features/quotes/testUtils'
import { toDateString } from '../features/monitoring/periods'
import Dashboard from './Dashboard'

const minutesAgo = (m: number) => new Date(Date.now() - m * 60_000).toISOString()

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
const card = (name: string) => within(kpis()).getByRole('link', { name }).closest('article') as HTMLElement

const sentQuote = makeQuote({ id: 'aaaaaaaa-0000-0000-0000-000000000001', status: 'Sent', grossTotal: 1230 })
const ticket = (overrides: Record<string, unknown> = {}) => ({
  id: 't1',
  installationId: 'i1',
  customerId: 'c1',
  title: 'Chyba striedača',
  priority: 'High',
  origin: 'AnomalyDetected',
  state: 'New',
  createdAt: minutesAgo(30),
  updatedAt: minutesAgo(30),
  ...overrides,
})

describe('Dashboard', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('shows the header with the date and the actions', () => {
    mockApi({})
    renderDashboard('Admin')

    expect(screen.getByRole('heading', { name: 'Dashboard', level: 2 })).toBeInTheDocument()
    expect(screen.getByText(/^Prehľad za dnes · /)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Exportovať' })).toHaveAttribute('href', '/monitoring')
    expect(screen.getByRole('link', { name: '+ Nová ponuka' })).toHaveAttribute('href', '/quotes')
  })

  it('shows real zeros and empty states when there is no data (no sample data)', async () => {
    mockApi({
      'GET /api/installations': json([]),
      'GET /api/quotes': json([]),
      'GET /api/tickets': json({ items: [], page: 1, pageSize: 100, totalCount: 0 }),
      'GET /api/underproduction-alarms': json({ items: [] }),
    })
    renderDashboard('Admin')

    await waitFor(() => expect(within(card('Výroba dnes')).getByText('0')).toBeInTheDocument())
    expect(within(card('Výroba dnes')).getByText(/0 elektrární v prevádzke/)).toBeInTheDocument()
    expect(within(card('Aktívne alarmy')).getByText('0')).toBeInTheDocument()
    expect(within(card('Otvorené tikety')).getByText('0')).toBeInTheDocument()
    expect(await within(card('Ponuky čakajú na odpoveď')).findByText('0')).toBeInTheDocument()
    expect(screen.queryByText('Ukážkové dáta')).not.toBeInTheDocument()
    expect(screen.queryByText(/SLA/)).not.toBeInTheDocument()
    expect(await screen.findByText('Žiadne otvorené hlásenia.')).toBeInTheDocument()
    expect(await screen.findByText('Zatiaľ nie sú dostupné žiadne inštalácie.')).toBeInTheDocument()
  })

  it('sums today production of plants in operation only', async () => {
    const today = toDateString(new Date())
    const production = {
      installationId: 'i1',
      from: today,
      to: today,
      granularity: 'day',
      timeZone: 'Europe/Bratislava',
      totalEnergyKwh: 42,
      peakPowerKw: 5,
      points: [{ periodStart: `${today}T00:00:00+01:00`, energyKwh: 42, peakPowerKw: 5, sampleCount: 100 }],
    }
    const fetch = mockApi({
      'GET /api/installations': json([
        { id: 'i1', name: 'FVE Nitra', state: 'Commissioned' },
        { id: 'i2', name: 'FVE Trnava', state: 'Planned' },
      ]),
      'GET /api/installations/i1/production': json(production),
      'GET /api/quotes': json([]),
      'GET /api/tickets': json({ items: [], page: 1, pageSize: 100, totalCount: 0 }),
      'GET /api/underproduction-alarms': json({ items: [] }),
    })
    renderDashboard('Admin')

    await waitFor(() => expect(within(card('Výroba dnes')).getByText('42')).toBeInTheDocument())
    expect(within(card('Výroba dnes')).getByText(/1 elektráreň v prevádzke/)).toBeInTheDocument()
    expect(fetch.mock.calls.some(([url]) => String(url).includes('/api/installations/i2/production'))).toBe(false)
  })

  it('shows how many open tickets wait for triage', async () => {
    mockApi({
      'GET /api/installations': json([]),
      'GET /api/tickets': json({
        items: [ticket({ id: 'a', origin: 'Manual' }), ticket({ id: 'b', origin: 'Manual', state: 'Triaged' })],
        page: 1,
        pageSize: 100,
        totalCount: 2,
      }),
      'GET /api/underproduction-alarms': json({ items: [] }),
    })
    renderDashboard('Operator')

    await waitFor(() => expect(within(card('Otvorené tikety')).getByText('2')).toBeInTheDocument())
    expect(within(card('Otvorené tikety')).getByText('1 čaká na triáž')).toBeInTheDocument()
  })

  it('uses real quotes and tickets when the API has them', async () => {
    mockApi({
      'GET /api/installations': json([]),
      'GET /api/quotes': json([sentQuote]),
      'GET /api/tickets': json({ items: [ticket()], page: 1, pageSize: 100, totalCount: 1 }),
    })
    renderDashboard('Admin')

    const quotes = await waitFor(() => card('Ponuky čakajú na odpoveď'))
    await within(quotes).findByText(/1\s230/)
    expect(within(quotes).queryByText('Ukážkové dáta')).not.toBeInTheDocument()
    expect(within(card('Aktívne alarmy')).getByText('0')).toBeInTheDocument()
    const alarm = await screen.findByRole('link', { name: 'Chyba striedača' })
    expect(alarm).toHaveAttribute('href', '/tickets/t1')
    expect(screen.getByText('Kritický')).toBeInTheDocument()
  })

  describe('ongoing underproduction alarms', () => {
    const alarm = {
      id: 'a1',
      installationId: 'i1',
      installationName: 'FVE Nitra',
      lastUnderproductionAt: minutesAgo(30),
      severity: 'High',
      ticketPriority: 'High',
    }

    it('count as alarms and open the monitoring of the installation', async () => {
      const fetch = mockApi({
        'GET /api/installations': json([]),
        'GET /api/quotes': json([]),
        'GET /api/tickets': json({ items: [], page: 1, pageSize: 100, totalCount: 0 }),
        'GET /api/underproduction-alarms': json({ items: [alarm] }),
      })
      renderDashboard('Admin')

      const link = await screen.findByRole('link', { name: 'Podvýroba: FVE Nitra' })
      expect(link).toHaveAttribute('href', '/monitoring?installation=i1')
      expect(within(card('Aktívne alarmy')).getByText('1')).toBeInTheDocument()
      expect(within(card('Aktívne alarmy')).queryByText('Ukážkové dáta')).not.toBeInTheDocument()
      expect(fetch.mock.calls.some(([url]) => String(url).includes('/api/underproduction-alarms?ongoing=true'))).toBe(true)
    })

    it('list a ticket created from an alarm only once', async () => {
      mockApi({
        'GET /api/installations': json([]),
        'GET /api/quotes': json([]),
        'GET /api/tickets': json({ items: [ticket({ id: 't9', title: 'Podvýroba z tiketu' }), ticket({ id: 't2', title: 'Iný tiket' })], page: 1, pageSize: 100, totalCount: 2 }),
        'GET /api/underproduction-alarms': json({ items: [{ ...alarm, ticketId: 't9' }] }),
      })
      renderDashboard('Admin')

      expect(await screen.findByRole('link', { name: 'Podvýroba: FVE Nitra' })).toBeInTheDocument()
      expect(await screen.findByRole('link', { name: 'Iný tiket' })).toBeInTheDocument()
      expect(screen.queryByRole('link', { name: 'Podvýroba z tiketu' })).not.toBeInTheDocument()
    })

    it('are not asked for by a role that cannot open monitoring', async () => {
      const fetch = mockApi({ 'GET /api/quotes': json([]) })
      renderDashboard('Sales')

      await waitFor(() => expect(card('Ponuky čakajú na odpoveď')).toBeInTheDocument())
      expect(fetch.mock.calls.some(([url]) => String(url).includes('underproduction-alarms'))).toBe(false)
    })
  })

  it('does not call APIs the role cannot use', async () => {
    const fetch = mockApi({
      'GET /api/installations': json([]),
      'GET /api/tickets': json({ items: [], page: 1, pageSize: 100, totalCount: 0 }),
    })
    renderDashboard('Technician')

    await within(kpis()).findByRole('link', { name: 'Otvorené tikety' })
    expect(screen.queryByRole('link', { name: 'Ponuky čakajú na odpoveď' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: '+ Nová ponuka' })).not.toBeInTheDocument()
    const paths = fetch.mock.calls.map(([input]) => new URL(String(input instanceof Request ? input.url : input), 'http://x').pathname)
    expect(paths).not.toContain('/api/quotes')
    expect(paths).not.toContain('/api/customers')
  })

  it('shows an error in the KPI cards when an API fails (no sample data)', async () => {
    mockApi({
      'GET /api/installations': problem(500, 'boom'),
      'GET /api/quotes': problem(500, 'boom'),
      'GET /api/tickets': problem(500, 'boom'),
      'GET /api/underproduction-alarms': json({ items: [] }),
    })
    renderDashboard('Admin')

    await waitFor(() => expect(within(kpis()).getAllByText('Údaje sa nepodarilo načítať.')).toHaveLength(3))
    expect(screen.queryByText(/1\s284/)).not.toBeInTheDocument()
    expect(screen.queryByText('Ukážkové dáta')).not.toBeInTheDocument()
  })

  it('shows the customer portal home to customers', () => {
    mockApi({ 'GET /api/installations': json([]), 'GET /api/tickets': json({ items: [], totalCount: 0, page: 1, pageSize: 100 }) })
    renderDashboard('Customer')

    expect(screen.getByRole('heading', { level: 2, name: 'Dobrý deň' })).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Kľúčové ukazovatele' })).not.toBeInTheDocument()
  })

  it('shows an empty state for users without dashboard modules', () => {
    mockApi({})
    renderDashboard()

    expect(screen.getByText('Vitajte v SolarOps')).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Kľúčové ukazovatele' })).not.toBeInTheDocument()
  })

  it('does not treat generic tickets or ticket priorities as underproduction alarms', async () => {
    mockApi({
      'GET /api/dashboard/summary': json({ tickets: { open: 2, new: 1, byPriority: { high: 2, medium: 0, low: 0 } }, quotes: null }),
      'GET /api/underproduction-alarms': json({ items: [] }),
    })
    renderDashboard('Operator')

    await waitFor(() => expect(within(card('Otvorené tikety')).getByText('2')).toBeInTheDocument())
    expect(within(card('Aktívne alarmy')).getByText('0')).toBeInTheDocument()
    expect(within(card('Aktívne alarmy')).getByText('0 kritický · 0 varovania · 0 info')).toBeInTheDocument()
  })
})
