import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'

const session = { email: 'a@test.sk', roles: ['Sales'], accessToken: 't', refreshToken: 'r' }
const customer = (id: string, name: string) => ({
  id,
  type: 'Person',
  status: 'Active',
  displayName: name,
  email: `${id}@test.sk`,
  phone: null,
  ico: null,
  createdAt: '2026-01-02T00:00:00Z',
})
const page = (items: unknown[], totalCount = items.length) =>
  new Response(JSON.stringify({ items, page: 1, pageSize: 20, totalCount }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })

function Location() {
  const l = useLocation()
  return <output data-testid="loc">{l.pathname + l.search}</output>
}

function renderAt(path: string) {
  saveSession(session)
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <App />
        <Location />
      </AuthProvider>
    </MemoryRouter>,
  )
}

const lastUrl = (fetchMock: ReturnType<typeof vi.fn>) => String(fetchMock.mock.calls.at(-1)?.[0])

describe('CustomerListPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('loads the page and sort from the URL', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => page([customer('1', 'Adamec')]))
    vi.stubGlobal('fetch', fetchMock)

    renderAt('/customers?q=ad&page=2&sort=email&dir=desc')

    expect(await screen.findByText('Adamec')).toBeInTheDocument()
    const url = lastUrl(fetchMock)
    expect(url).toContain('q=ad')
    expect(url).toContain('page=2')
    expect(url).toContain('sortBy=email')
    expect(url).toContain('sortDir=desc')
  })

  it('stores sort and page in the URL', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => page([customer('1', 'Adamec')], 45)))
    renderAt('/customers')
    await screen.findByText('Adamec')

    await userEvent.click(screen.getByRole('button', { name: 'E-mail' }))
    await waitFor(() => expect(screen.getByTestId('loc')).toHaveTextContent('sort=email&dir=asc'))

    await userEvent.click(await screen.findByRole('button', { name: 'Ďalšia' }))
    await waitFor(() => expect(screen.getByTestId('loc')).toHaveTextContent('page=2'))
  })

  it('debounces the search and resets to the first page', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => page([customer('1', 'Adamec')]))
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/customers?page=3')
    await screen.findByText('Adamec')
    const callsBefore = fetchMock.mock.calls.length

    await userEvent.type(screen.getByLabelText(/Hľadať/), 'novak')

    await waitFor(() => expect(screen.getByTestId('loc')).toHaveTextContent('/customers?q=novak'))
    expect(screen.getByTestId('loc')).not.toHaveTextContent('page=')
    expect(fetchMock.mock.calls.length - callsBefore).toBe(1)
  })

  it('shows empty state and load error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => page([])))
    const { unmount } = renderAt('/customers')
    expect(await screen.findByText('Zatiaľ nie sú evidovaní žiadni zákazníci.')).toBeInTheDocument()
    unmount()

    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => new Response(null, { status: 500 })))
    renderAt('/customers')
    expect(await screen.findByRole('alert')).toHaveTextContent('Zoznam zákazníkov sa nepodarilo načítať.')
  })

  it('opens the customer detail on row click', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(async (url: string) =>
        String(url).includes('/api/customers/abc')
          ? new Response(JSON.stringify({ ...customer('abc', 'Adamec'), address: { street: 'Hlavná 1', city: 'Nitra', postalCode: '949 01', country: 'SK' } }), {
              status: 200,
              headers: { 'Content-Type': 'application/json' },
            })
          : page([customer('abc', 'Adamec')]),
      ),
    )
    renderAt('/customers')

    await userEvent.click(await screen.findByText('Adamec'))

    expect(await screen.findByRole('heading', { name: 'Adamec' })).toBeInTheDocument()
    expect(screen.getByTestId('loc')).toHaveTextContent('/customers/abc')
  })

  it('guards the detail route with the role of the parent section', () => {
    saveSession({ ...session, roles: ['Technician'] })
    render(
      <MemoryRouter initialEntries={['/customers/abc']}>
        <AuthProvider>
          <App />
        </AuthProvider>
      </MemoryRouter>,
    )
    expect(screen.getByRole('heading', { name: /403/ })).toBeInTheDocument()
  })
})
