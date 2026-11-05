import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../auth/AuthContext'
import { json, makeQuote, mockApi, problem } from '../features/quotes/testUtils'
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

  it('falls back to sample data (marked) where there is no backend data', async () => {
    mockApi({
      'GET /api/installations': json([]),
      'GET /api/quotes': json([]),
      'GET /api/tickets': json({ items: [], page: 1, pageSize: 100, totalCount: 0 }),
    })
    renderDashboard('Admin')

    expect(await within(kpis()).findByText(/^1\s284$/)).toBeInTheDocument()
    expect(within(card('Aktívne alarmy')).getByText('5')).toBeInTheDocument()
    expect(within(card('Otvorené tikety')).getByText('18')).toBeInTheDocument()
    expect(within(card('Ponuky čakajú na odpoveď')).getByText('7')).toBeInTheDocument()
    expect(screen.getAllByText('Ukážkové dáta').length).toBeGreaterThanOrEqual(4)
    expect(await screen.findByText('Chyba izolácie striedača')).toBeInTheDocument()
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
    expect(within(card('Aktívne alarmy')).getByText('1')).toBeInTheDocument()
    expect(within(card('Aktívne alarmy')).getByText('1 nový')).toBeInTheDocument()
    const alarm = await screen.findByRole('link', { name: 'Chyba striedača' })
    expect(alarm).toHaveAttribute('href', '/tickets/t1')
    expect(screen.getByText('Kritický')).toBeInTheDocument()
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

  it('keeps working when an API fails (sample data instead of an error)', async () => {
    mockApi({ 'GET /api/installations': problem(500, 'boom'), 'GET /api/quotes': problem(500, 'boom'), 'GET /api/tickets': problem(500, 'boom') })
    renderDashboard('Admin')

    expect(await within(kpis()).findByText(/^1\s284$/)).toBeInTheDocument()
    expect(screen.queryByText('Údaje sa nepodarilo načítať.')).not.toBeInTheDocument()
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

  it('counts only anomaly tickets as alarms', async () => {
    mockApi({
      'GET /api/installations': json([]),
      'GET /api/tickets': json({
        items: [ticket({ id: 'a1' }), ticket({ id: 'm1', origin: 'Manual' })],
        totalCount: 2,
        page: 1,
        pageSize: 100,
      }),
    })
    renderDashboard('Operator')

    await waitFor(() => expect(within(card('Otvorené tikety')).getByText('2')).toBeInTheDocument())
    expect(within(card('Aktívne alarmy')).getByText('1')).toBeInTheDocument()
  })
})
