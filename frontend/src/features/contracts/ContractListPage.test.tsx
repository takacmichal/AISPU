import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ToastProvider } from '../../components/toast'
import ContractListPage from './ContractListPage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const row = (id: string, number: string, over: Record<string, unknown> = {}) => ({
  id,
  number,
  status: 'Draft',
  signedOn: null,
  depositStatus: 'NotPaid',
  grossTotal: 1230,
  currency: 'EUR',
  createdAt: '2026-01-02T00:00:00Z',
  ...over,
})

function renderPage(path = '/contracts') {
  saveSession({ email: 'a@test.sk', roles: ['Sales'], accessToken: 't', refreshToken: 'r' })
  const router = createMemoryRouter(
    [
      { path: '/contracts', element: <ContractListPage /> },
      { path: '/contracts/:id', element: <p>detail</p> },
    ],
    { initialEntries: [path] },
  )
  render(
    <AuthProvider>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </AuthProvider>,
  )
  return router
}

describe('ContractListPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('lists contracts with status, deposit and total', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json([row('1', 'Z-1'), row('2', 'Z-2', { status: 'Signed', depositStatus: 'Paid' })])))
    renderPage()
    expect(await screen.findByText('Z-1')).toBeInTheDocument()
    const table = within(screen.getByRole('table'))
    expect(table.getAllByText('Podpísaná')).toHaveLength(2) // column header and status badge
    expect(table.getByText('Zaplatená')).toBeInTheDocument()
    expect(screen.getAllByText(/1\s?230,00\s?€/)).toHaveLength(2)
  })

  it('passes the status filter to the API', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => json([row('1', 'Z-1', { status: 'Signed' })]))
    vi.stubGlobal('fetch', fetchMock)
    renderPage('/contracts?status=Signed')
    await screen.findByText('Z-1')
    expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain('status=Signed')
  })

  it('filters by number and opens the detail on click', async () => {
    const user = userEvent.setup()
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json([row('1', 'Z-100'), row('2', 'Z-200')])))
    const router = renderPage()
    await user.type(await screen.findByLabelText('Hľadať podľa čísla zmluvy'), '200')
    expect(await screen.findByText('Z-200')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByText('Z-100')).not.toBeInTheDocument())
    await user.click(screen.getByText('Z-200'))
    expect(router.state.location.pathname).toBe('/contracts/2')
  })

  it('shows the empty and the error state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json([])))
    renderPage()
    expect(await screen.findByText(/Zatiaľ nie sú evidované žiadne zmluvy/)).toBeInTheDocument()
  })

  it('shows the error state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => new Response(null, { status: 500 })))
    renderPage()
    expect(await screen.findByText('Zoznam zmlúv sa nepodarilo načítať.')).toBeInTheDocument()
  })
})
