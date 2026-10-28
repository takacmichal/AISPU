import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ToastProvider } from '../../components/toast'
import { isValidIco } from './customerForm'

const session = { email: 'a@test.sk', roles: ['Sales'], accessToken: 't', refreshToken: 'r' }
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function renderAt(path: string) {
  saveSession(session)
  const router = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <AuthProvider>
            <ToastProvider>
              <App />
            </ToastProvider>
          </AuthProvider>
        ),
      },
    ],
    { initialEntries: [path] },
  )
  render(<RouterProvider router={router} />)
  return router
}

const user = userEvent.setup()

async function fillPerson(email = 'jan@test.sk') {
  await user.type(screen.getByLabelText('Meno'), 'Ján')
  await user.type(screen.getByLabelText('Priezvisko'), 'Novák')
  await user.type(screen.getByLabelText('E-mail'), email)
  await user.type(screen.getByLabelText('Ulica a číslo'), 'Hlavná 12')
  await user.type(screen.getByLabelText('Mesto'), 'Bratislava')
  await user.type(screen.getByLabelText('PSČ'), '811 01')
}

describe('CustomerFormPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('validates required fields, e-mail and company identifiers on the client', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/customers/new')

    await user.click(screen.getByRole('button', { name: 'Uložiť' }))
    expect(screen.getAllByText('Vyplňte toto pole.').length).toBeGreaterThanOrEqual(5)
    expect(screen.getByText('Vyplňte e-mail.')).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Typ zákazníka'), 'Company')
    await user.type(screen.getByLabelText('IČO'), '12345678')
    await user.type(screen.getByLabelText('DIČ'), '123')
    await user.type(screen.getByLabelText('E-mail'), 'nie-email')
    await user.click(screen.getByRole('button', { name: 'Uložiť' }))

    expect(screen.getByText(/IČO musí mať 8 číslic/)).toBeInTheDocument()
    expect(screen.getByText('DIČ musí mať 10 číslic.')).toBeInTheDocument()
    expect(screen.getByText('E-mail nemá platný tvar.')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('checks the IČO check digit like the server', () => {
    expect(isValidIco('35763469')).toBe(true)
    expect(isValidIco('35763460')).toBe(false)
  })

  it('creates a customer, shows a toast and goes to the detail', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => json({ id: 'new-id', displayName: 'Ján Novák' }, 201))
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt('/customers/new')

    await fillPerson()
    await user.click(screen.getByRole('button', { name: 'Uložiť' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/customers/new-id'))
    expect(await screen.findByText('Zákazník bol uložený.')).toBeInTheDocument()
    const [url, init] = fetchMock.mock.calls[0]
    expect(String(url)).toContain('/api/customers')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body)).toMatchObject({ type: 'Person', firstName: 'Ján', email: 'jan@test.sk', address: { postalCode: '811 01' } })
  })

  it('shows server validation errors next to the fields', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(async () => json({ title: 'Validation', errors: { Email: ['E-mail už existuje.'], 'Address.City': ['Neplatné mesto.'] } }, 400)),
    )
    renderAt('/customers/new')

    await fillPerson()
    await user.click(screen.getByRole('button', { name: 'Uložiť' }))

    expect(await screen.findByText('E-mail už existuje.')).toBeInTheDocument()
    expect(screen.getByText('Neplatné mesto.')).toBeInTheDocument()
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('aria-invalid', 'true')
  })

  it('loads and updates an existing customer', async () => {
    const existing = {
      id: 'abc',
      type: 'Person',
      status: 'Active',
      displayName: 'Ján Novák',
      firstName: 'Ján',
      lastName: 'Novák',
      email: 'jan@test.sk',
      phone: null,
      address: { street: 'Hlavná 12', city: 'Bratislava', postalCode: '811 01', country: 'SK' },
      createdAt: '2026-01-01T00:00:00Z',
    }
    const fetchMock = vi.fn().mockImplementation(async (_url: string, init: RequestInit) =>
      init.method === 'PUT' ? json(existing) : json(existing),
    )
    vi.stubGlobal('fetch', fetchMock)
    const router = renderAt('/customers/abc/edit')

    expect(await screen.findByDisplayValue('Novák')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Mesto'))
    await user.type(screen.getByLabelText('Mesto'), 'Košice')
    await user.click(screen.getByRole('button', { name: 'Uložiť' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/customers/abc'))
    const put = fetchMock.mock.calls.find(([, init]) => init.method === 'PUT')!
    expect(String(put[0])).toContain('/api/customers/abc')
    expect(JSON.parse(put[1].body).address.city).toBe('Košice')
  })

  it('warns about unsaved changes when leaving', async () => {
    vi.stubGlobal('fetch', vi.fn())
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    const router = renderAt('/customers/new')

    await user.type(screen.getByLabelText('Meno'), 'Ján')
    await user.click(screen.getAllByRole('link', { name: 'Zákazníci' })[0])

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(router.state.location.pathname).toBe('/customers/new')

    confirm.mockReturnValue(true)
    await user.click(screen.getAllByRole('link', { name: 'Zákazníci' })[0])
    await waitFor(() => expect(router.state.location.pathname).toBe('/customers'))
  })

  it('does not warn when nothing changed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async () => json({ items: [], page: 1, pageSize: 20, totalCount: 0 })))
    const confirm = vi.spyOn(window, 'confirm')
    const router = renderAt('/customers/new')

    await user.click(screen.getAllByRole('link', { name: 'Zákazníci' })[0])

    await waitFor(() => expect(router.state.location.pathname).toBe('/customers'))
    expect(confirm).not.toHaveBeenCalled()
  })
})
