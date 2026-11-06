import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../auth/AuthContext'
import { json, mockApi, problem } from '../features/quotes/testUtils'
import { SideProductionCard } from './SideProductionCard'
import { useNavCounts } from './useShellData'

function Counts() {
  const counts = useNavCounts(true)
  return <output data-testid="counts">{JSON.stringify(counts)}</output>
}

function renderWithAuth(node: React.ReactNode) {
  return render(<AuthProvider initialSession={{ email: 'u@test.sk', roles: ['Admin'], accessToken: 't', refreshToken: 'r' }}>{node}</AuthProvider>)
}

const counts = () => JSON.parse(screen.getByTestId('counts').textContent ?? '{}') as Record<string, number>

describe('sidebar data', () => {
  beforeEach(() => vi.spyOn(console, 'error').mockImplementation(() => {}))
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('counts customers, open quotes and open tickets from the API', async () => {
    const fetch = mockApi({
      'GET /api/customers': json({ items: [], page: 1, pageSize: 1, totalCount: 11 }),
      'GET /api/quotes': json([{ status: 'Draft' }, { status: 'Sent' }, { status: 'Accepted' }]),
      'GET /api/tickets': json({
        items: [{ state: 'New' }, { state: 'Triaged' }, { state: 'Resolved' }, { state: 'Closed' }],
        page: 1,
        pageSize: 100,
        totalCount: 4,
      }),
    })
    renderWithAuth(<Counts />)

    await waitFor(() => expect(counts()).toEqual({ '/customers': 11, '/quotes': 2, '/tickets': 2 }))
    expect(fetch.mock.calls.some(([url]) => String(url).includes('/api/tickets'))).toBe(true)
  })

  it('shows no numbers instead of made-up ones when the API is empty or fails', async () => {
    mockApi({
      'GET /api/customers': json({ items: [], page: 1, pageSize: 1, totalCount: 0 }),
      'GET /api/quotes': problem(500, 'boom'),
      'GET /api/tickets': problem(500, 'boom'),
    })
    renderWithAuth(<Counts />)

    await waitFor(() => expect(counts()).toEqual({ '/customers': 0 }))
  })

  it('shows today production and the number of plants in operation, without a made-up target', async () => {
    mockApi({
      'GET /api/production/summary': json({ points: [], installationCount: 2 }),
    })
    renderWithAuth(<SideProductionCard live />)

    expect(await screen.findByText('2 elektrárne v prevádzke')).toBeInTheDocument()
    expect(screen.getByText('0 kWh')).toBeInTheDocument()
    expect(screen.queryByText(/online/)).not.toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
  })
})
