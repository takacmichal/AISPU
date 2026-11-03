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
    })
    renderDashboard('Operator')

    const inst = () => card('Inštalácie v realizácii')
    expect(await within(inst()).findByText('Údaje sa nepodarilo načítať.')).toBeInTheDocument()
    expect(screen.getByText('Poslednú aktivitu sa nepodarilo načítať.')).toBeInTheDocument()
    expect(await screen.findByText('API je nedostupné.')).toBeInTheDocument()

    await userEvent.click(within(inst()).getByRole('button', { name: 'Skúsiť znova' }))
    expect(await within(inst()).findByText('Odovzdané zákazníkom: 1')).toBeInTheDocument()
  })

  it('shows an empty state for roles without KPIs', async () => {
    mockApi({ 'GET /api/system/info': systemInfo })
    renderDashboard('Customer')

    expect(screen.getByText('Vitajte v SolarOps')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Moje elektrárne' })).toHaveAttribute('href', '/my-plants')
    expect(screen.queryByRole('group', { name: 'Kľúčové ukazovatele' })).not.toBeInTheDocument()
    expect(screen.queryByRole('article', { name: 'Posledná aktivita' })).not.toBeInTheDocument()
    expect(await screen.findByText(/SolarOps API/)).toBeInTheDocument()
  })
})
