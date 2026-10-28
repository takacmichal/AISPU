import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../../App'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ToastProvider } from '../../components/toast'
import { validateSupplyPoint, emptySupplyPoint } from './supplyPointForm'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

const customer = {
  id: 'c1',
  type: 'Person',
  status: 'Active',
  displayName: 'Ján Novák',
  email: 'jan@test.sk',
  phone: '+421 900 123 456',
  address: { street: 'Hlavná 12', city: 'Bratislava', postalCode: '811 01', country: 'SK' },
  createdAt: '2026-01-01T00:00:00Z',
}
const sp = (id: string, eic: string) => ({
  id,
  customerId: 'c1',
  name: 'Dom',
  eicCode: eic,
  address: { street: 'Dlhá 1', city: 'Nitra', postalCode: '949 01', country: 'SK' },
  mainBreakerAmps: 25,
})

interface Api {
  supplyPoints: ReturnType<typeof sp>[]
  calls: { method: string; url: string; body?: unknown }[]
  loadError?: boolean
  mutate?: (method: string, url: string) => Response | undefined
}

function stubApi(state: Api) {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (url: string, init: RequestInit = {}) => {
      const method = init.method ?? 'GET'
      const path = new URL(String(url)).pathname
      state.calls.push({ method, url: String(url), body: init.body ? JSON.parse(String(init.body)) : undefined })
      if (path === '/api/customers/c1') return json(customer)
      if (path.endsWith('/supply-points') && method === 'GET') return state.loadError ? new Response(null, { status: 500 }) : json(state.supplyPoints)
      const custom = state.mutate?.(method, path)
      if (custom) return custom
      if (path.endsWith('/supply-points') && method === 'POST') return json(sp('new', 'X'), 201)
      if (method === 'PUT') return json(sp('s1', 'X'))
      if (method === 'DELETE') return new Response(null, { status: 204 })
      if (path === '/api/quotes') return json([{ id: 'q1', customerId: 'c1', status: 'Sent', validUntil: null, currency: 'EUR', grossTotal: 1200, createdAt: '2026-02-01T00:00:00Z' }])
      if (path === '/api/contracts') return json([])
      return json([])
    }),
  )
}

function renderAt(path: string, roles: string[] = ['Sales']) {
  saveSession({ email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' })
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

describe('CustomerDetailPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows the data tab first and only loads other tabs on demand', async () => {
    const state: Api = { supplyPoints: [], calls: [] }
    stubApi(state)
    renderAt('/customers/c1')

    expect(await screen.findByRole('heading', { name: 'Ján Novák' })).toBeInTheDocument()
    expect(screen.getByText('jan@test.sk')).toBeInTheDocument()
    expect(state.calls.some((c) => c.url.includes('supply-points'))).toBe(false)

    await user.click(screen.getByRole('tab', { name: 'Odberné miesta' }))
    expect(await screen.findByText('Zákazník zatiaľ nemá žiadne odberné miesto.')).toBeInTheDocument()
    expect(state.calls.some((c) => c.url.includes('supply-points'))).toBe(true)
  })

  it('shows a loading state per tab and a load error', async () => {
    const state: Api = { supplyPoints: [], calls: [], loadError: true }
    stubApi(state)
    renderAt('/customers/c1?tab=supply-points')

    expect(await screen.findByText('Načítavam odberné miesta…')).toBeInTheDocument()
    expect(await screen.findByText('Odberné miesta sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('hides tabs the role cannot access', async () => {
    stubApi({ supplyPoints: [], calls: [] })
    renderAt('/customers/c1', ['Sales'])

    await screen.findByRole('heading', { name: 'Ján Novák' })
    expect(screen.getByRole('tab', { name: 'Zmluvy' })).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Inštalácie' })).not.toBeInTheDocument()
  })

  it('links quotes to their detail', async () => {
    stubApi({ supplyPoints: [], calls: [] })
    const router = renderAt('/customers/c1?tab=quotes')

    await user.click(await screen.findByRole('link', { name: /Ponuka z/ }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/quotes/q1'))
    expect(screen.getByRole('link', { name: 'Zákazníci' })).toBeInTheDocument()
  })

  it('adds a supply point in a dialog', async () => {
    const state: Api = { supplyPoints: [], calls: [] }
    stubApi(state)
    renderAt('/customers/c1?tab=supply-points')

    await user.click(await screen.findByRole('button', { name: 'Pridať odberné miesto' }))
    const dialog = screen.getByRole('dialog', { name: 'Nové odberné miesto' })
    await user.click(within(dialog).getByRole('button', { name: 'Uložiť' }))
    expect(within(dialog).getByText('Vyplňte EIC kód.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('EIC kód'), '24zss1234567890a')
    await user.type(within(dialog).getByLabelText('Ulica a číslo'), 'Dlhá 1')
    await user.type(within(dialog).getByLabelText('Mesto'), 'Nitra')
    await user.type(within(dialog).getByLabelText('PSČ'), '949 01')
    await user.type(within(dialog).getByLabelText('Hlavný istič (A)'), '25')
    await user.click(within(dialog).getByRole('button', { name: 'Uložiť' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Odberné miesto bolo uložené.')).toBeInTheDocument()
    const post = state.calls.find((c) => c.method === 'POST')!
    expect(post.url).toContain('/api/customers/c1/supply-points')
    expect(post.body).toMatchObject({ eicCode: '24zss1234567890a', mainBreakerAmps: 25, address: { postalCode: '949 01' } })
  })

  it('shows server validation errors in the dialog', async () => {
    const state: Api = {
      supplyPoints: [],
      calls: [],
      mutate: (method) => (method === 'POST' ? json({ title: 'Validation', errors: { EicCode: ['EIC už existuje.'] } }, 400) : undefined),
    }
    stubApi(state)
    renderAt('/customers/c1?tab=supply-points')

    await user.click(await screen.findByRole('button', { name: 'Pridať odberné miesto' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText('EIC kód'), '24ZSS1234567890A')
    await user.type(within(dialog).getByLabelText('Ulica a číslo'), 'Dlhá 1')
    await user.type(within(dialog).getByLabelText('Mesto'), 'Nitra')
    await user.type(within(dialog).getByLabelText('PSČ'), '949 01')
    await user.type(within(dialog).getByLabelText('Hlavný istič (A)'), '25')
    await user.click(within(dialog).getByRole('button', { name: 'Uložiť' }))

    expect(await within(dialog).findByText('EIC už existuje.')).toBeInTheDocument()
  })

  it('edits a supply point', async () => {
    const state: Api = { supplyPoints: [sp('s1', '24ZSS1234567890A')], calls: [] }
    stubApi(state)
    renderAt('/customers/c1?tab=supply-points')

    await user.click(await screen.findByRole('button', { name: 'Upraviť' }))
    const dialog = screen.getByRole('dialog', { name: 'Upraviť odberné miesto' })
    expect(within(dialog).getByLabelText('EIC kód')).toHaveValue('24ZSS1234567890A')
    await user.clear(within(dialog).getByLabelText('Hlavný istič (A)'))
    await user.type(within(dialog).getByLabelText('Hlavný istič (A)'), '40')
    await user.click(within(dialog).getByRole('button', { name: 'Uložiť' }))

    await waitFor(() => expect(state.calls.some((c) => c.method === 'PUT')).toBe(true))
    expect(state.calls.find((c) => c.method === 'PUT')!.url).toContain('/supply-points/s1')
    expect(state.calls.find((c) => c.method === 'PUT')!.body).toMatchObject({ mainBreakerAmps: 40 })
  })

  it('deletes a supply point after confirmation and reports a conflict', async () => {
    let conflict = true
    const state: Api = {
      supplyPoints: [sp('s1', '24ZSS1234567890A')],
      calls: [],
      mutate: (method) =>
        method === 'DELETE' && conflict
          ? json({ title: 'Conflict', detail: 'The supply point has an installation and cannot be deleted.' }, 409)
          : undefined,
    }
    stubApi(state)
    renderAt('/customers/c1?tab=supply-points')

    await user.click(await screen.findByRole('button', { name: 'Zmazať' }))
    let dialog = screen.getByRole('dialog', { name: 'Zmazať odberné miesto' })
    await user.click(within(dialog).getByRole('button', { name: 'Zmazať' }))
    expect(await within(dialog).findByText(/has an installation/)).toBeInTheDocument()

    conflict = false
    dialog = screen.getByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Zmazať' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByText('Odberné miesto bolo zmazané.')).toBeInTheDocument()
  })

  it('closes a dialog with Escape', async () => {
    stubApi({ supplyPoints: [], calls: [] })
    renderAt('/customers/c1?tab=supply-points')

    await user.click(await screen.findByRole('button', { name: 'Pridať odberné miesto' }))
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('validates the EIC format and breaker range like the server', () => {
    const errors = validateSupplyPoint({ ...emptySupplyPoint, eicCode: '24ZSS123', mainBreakerAmps: '5000', street: 'a', city: 'b', postalCode: '12' })
    expect(Object.keys(errors).sort()).toEqual(['address.postalCode', 'eicCode', 'mainBreakerAmps'])
  })
})
