import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ConfirmProvider } from '../../components/confirm'
import { ToastProvider } from '../../components/toast'
import { deadlineProgress, isOpenRequest } from './model'

const NOW = new Date('2026-11-10T12:00:00')

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const request = (over: Record<string, unknown>) => ({
  id: 'r1',
  installationId: 'i1',
  customerId: 'c1',
  supplyPointId: 's1',
  eicCode: '24ZZS00000000001',
  ratedAcPowerKw: 8,
  mainBreakerAmps: 25,
  type: 'SmallSource',
  state: 'Draft',
  attachments: [],
  createdAt: '2026-11-01T08:00:00Z',
  updatedAt: '2026-11-09T08:00:00Z',
  ...over,
})

const requests = [
  request({ id: 'r1', installationId: 'i1', eicCode: '24ZZS00000000001', state: 'Submitted', submittedAt: '2026-11-01T08:00:00Z', proposalDueDate: '2026-11-21', proposalRemainingDays: 11, proposalOverdue: false }),
  request({ id: 'r2', installationId: 'i2', eicCode: '24ZZS00000000002', state: 'Submitted', submittedAt: '2026-10-30T08:00:00Z', proposalDueDate: '2026-11-12', proposalRemainingDays: 2, proposalOverdue: false }),
  request({ id: 'r3', installationId: 'i3', eicCode: '24ZZS00000000003', state: 'Submitted', submittedAt: '2026-10-20T08:00:00Z', proposalDueDate: '2026-11-05', proposalRemainingDays: -5, proposalOverdue: true }),
  request({ id: 'r4', installationId: 'i1', eicCode: '24ZZS00000000004', state: 'Concluded' }),
]

function stub() {
  const calls: string[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (url: string) => {
      const path = new URL(String(url), 'http://localhost').pathname
      calls.push(path)
      if (path === '/api/connection-requests') return json(requests)
      if (path === '/api/installations')
        return json([
          { id: 'i1', name: 'FVE Novák' },
          { id: 'i2', name: 'FVE Horváth' },
          { id: 'i3', name: 'FVE Kováč' },
        ])
      return json([])
    }),
  )
  return calls
}

function renderAt(path: string, roles: string[]) {
  saveSession({ email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' })
  const router = createMemoryRouter(
    [{ path: '*', element: <AuthProvider><ToastProvider><ConfirmProvider><App /></ConfirmProvider></ToastProvider></AuthProvider> }],
    { initialEntries: [path] },
  )
  render(<RouterProvider router={router} />)
  return router
}

describe('ConnectionRequestListPage', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(NOW)
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('shows KPIs for open, soon due and overdue requests', async () => {
    stub()
    renderAt('/connection-requests', ['Designer'])

    expect(screen.getByRole('heading', { level: 2, name: 'Pripojenie k distribučke' })).toBeInTheDocument()
    const kpis = screen.getByRole('group', { name: 'Stav žiadostí' })
    const value = (label: string) => within(within(kpis).getByRole('heading', { name: label }).closest('article') as HTMLElement)
    expect(await value('Otvorené žiadosti').findByText('3')).toBeInTheDocument()
    expect(value('Blíži sa lehota').getByText('1')).toBeInTheDocument()
    expect(value('Po lehote').getByText('1')).toBeInTheDocument()
  })

  it('draws the distributor deadline as a progress bar', async () => {
    stub()
    renderAt('/connection-requests', ['Sales'])

    const ok = await screen.findByRole('progressbar', { name: 'Lehota distribučky pre 24ZZS00000000001' })
    expect(ok).toHaveAttribute('aria-valuemax', '20')
    expect(ok).toHaveAttribute('aria-valuenow', '9')
    expect(ok).toHaveAttribute('aria-valuetext', 'zostáva 11 dní')
    const late = screen.getByRole('progressbar', { name: 'Lehota distribučky pre 24ZZS00000000003' })
    expect(late).toHaveAttribute('aria-valuetext', 'po lehote 5 dní')
    expect(screen.getByRole('link', { name: 'FVE Kováč' })).toHaveAttribute('href', '/connection-requests/r3')
  })

  it('filters by deadline, state and search in the URL', async () => {
    stub()
    const router = renderAt('/connection-requests', ['Operator'])
    await screen.findByRole('link', { name: 'FVE Kováč' })

    await userEvent.click(screen.getByRole('radio', { name: /Po lehote/ }))
    await waitFor(() => expect(router.state.location.search).toBe('?deadline=overdue'))
    expect(screen.getByRole('link', { name: 'FVE Kováč' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'FVE Horváth' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('radio', { name: 'Všetky' }))
    await userEvent.selectOptions(screen.getByLabelText('Stav'), 'Concluded')
    expect(screen.getAllByRole('link', { name: 'FVE Novák' })).toHaveLength(1)

    await userEvent.selectOptions(screen.getByLabelText('Stav'), '')
    await userEvent.type(screen.getByRole('searchbox', { name: 'Hľadať podľa EIC alebo inštalácie' }), '0002')
    expect(screen.getByRole('link', { name: 'FVE Horváth' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'FVE Kováč' })).not.toBeInTheDocument()
  })

  it('answers 403 for a customer and does not load the list', async () => {
    const calls = stub()
    renderAt('/connection-requests', ['Customer'])

    expect(screen.getByRole('heading', { name: /403/ })).toBeInTheDocument()
    expect(calls).not.toContain('/api/connection-requests')
  })

  it('is in the Realizácia menu for internal roles', async () => {
    stub()
    renderAt('/connection-requests', ['Sales'])
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(within(nav).getByRole('list', { name: 'Realizácia' })).getByRole('link', { name: 'Pripojenie k distribučke' })).toHaveAttribute(
      'aria-current',
      'page',
    )
  })
})

describe('connection model', () => {
  it('measures the elapsed part of the proposal period', () => {
    const r = request({ state: 'Submitted', submittedAt: '2026-11-01T08:00:00Z', proposalDueDate: '2026-11-21', proposalRemainingDays: 11 })
    expect(deadlineProgress(r as never, NOW)).toEqual({ elapsed: 9, total: 20 })
    expect(deadlineProgress(request({ state: 'Concluded' }) as never, NOW)).toBeNull()
    expect(isOpenRequest({ state: 'Rejected' } as never)).toBe(false)
    expect(isOpenRequest({ state: 'Submitted' } as never)).toBe(true)
  })
})
