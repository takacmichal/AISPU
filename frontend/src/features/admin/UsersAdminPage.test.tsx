import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ToastProvider } from '../../components/toast'
import UsersAdminPage from './UsersAdminPage'

const ROLES = ['Admin', 'Sales', 'Designer', 'Technician', 'Operator', 'Customer']
const user = (id: string, email: string, roles: string[]) => ({ id, email, roles })
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })
const list = (items: unknown[], totalCount = items.length) => json({ items, page: 1, pageSize: 20, totalCount })

function renderPage(roles: string[], path = '/admin/users') {
  const session = { email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' }
  saveSession(session)
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider initialSession={session}>
        <ToastProvider>
          <UsersAdminPage />
        </ToastProvider>
      </AuthProvider>
    </MemoryRouter>,
  )
}

type Handlers = { put?: () => Response; users?: () => Response }
function mockApi({ put, users }: Handlers = {}) {
  const fetchMock = vi.fn().mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') return put ? put() : json(user('u1', 'jozef@test.sk', ['Sales']))
    if (String(url).includes('/api/roles')) return json(ROLES)
    return users ? users() : list([user('u1', 'jozef@test.sk', ['Sales', 'Designer'])])
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}
const puts = (fetchMock: ReturnType<typeof vi.fn>) => fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT') as [string, RequestInit][]

describe('UsersAdminPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows users with localized role badges', async () => {
    mockApi()
    renderPage(['Admin'])
    expect(screen.getAllByText('Načítavam…').length).toBeGreaterThan(0)
    expect(await screen.findByText('jozef@test.sk')).toBeInTheDocument()
    expect(screen.getByText('Obchod')).toBeInTheDocument()
    expect(screen.getByText('Projektant')).toBeInTheDocument()
  })

  it('shows an empty state', async () => {
    mockApi({ users: () => list([]) })
    renderPage(['Admin'])
    expect(await screen.findByText('Zatiaľ nie sú evidovaní žiadni používatelia.')).toBeInTheDocument()
  })

  it('shows an error state', async () => {
    mockApi({ users: () => json({ title: 'x' }, 500) })
    renderPage(['Admin'])
    expect(await screen.findByText('Zoznam používateľov sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('denies non-admins without calling the API', () => {
    const fetchMock = mockApi()
    renderPage(['Sales'])
    expect(screen.getByRole('alert')).toHaveTextContent('nemáte oprávnenie')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('sends the search query', async () => {
    const fetchMock = mockApi()
    renderPage(['Admin'], '/admin/users?q=joz&page=2')
    await screen.findByText('jozef@test.sk')
    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain('q=joz')
    expect(url).toContain('page=2')
  })

  it('saves the changed set of roles', async () => {
    const fetchMock = mockApi()
    renderPage(['Admin'])
    await userEvent.click(await screen.findByRole('button', { name: /Upraviť roly/ }))
    const dialog = screen.getByRole('dialog')
    const technician = await within(dialog).findByRole('checkbox', { name: 'Technik' })
    expect(within(dialog).getByRole('checkbox', { name: 'Obchod' })).toBeChecked()
    await userEvent.click(technician)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Uložiť' }))
    await waitFor(() => expect(puts(fetchMock)).toHaveLength(1))
    expect(puts(fetchMock)[0][0]).toContain('/api/users/u1/roles')
    expect(JSON.parse(String(puts(fetchMock)[0][1].body)).roles.sort()).toEqual(['Designer', 'Sales', 'Technician'])
    expect(await screen.findByText('Roly boli uložené.')).toBeInTheDocument()
  })

  it('requires at least one role', async () => {
    const fetchMock = mockApi()
    renderPage(['Admin'])
    await userEvent.click(await screen.findByRole('button', { name: /Upraviť roly/ }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(await within(dialog).findByRole('checkbox', { name: 'Obchod' }))
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Projektant' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Uložiť' }))
    expect(await within(dialog).findByText('Vyberte aspoň jednu rolu.')).toBeInTheDocument()
    expect(puts(fetchMock)).toHaveLength(0)
  })

  it('shows the server message when removing the last admin (409)', async () => {
    mockApi({
      users: () => list([user('u1', 'root@test.sk', ['Admin'])]),
      put: () => json({ title: 'Conflict', detail: 'Posledný administrátor nemôže stratiť rolu Admin.' }, 409),
    })
    renderPage(['Admin'])
    await userEvent.click(await screen.findByRole('button', { name: /Upraviť roly/ }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(await within(dialog).findByRole('checkbox', { name: 'Obchod' }))
    await userEvent.click(within(dialog).getByRole('checkbox', { name: 'Administrátor' }))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Uložiť' }))
    expect(await within(dialog).findByText('Posledný administrátor nemôže stratiť rolu Admin.')).toBeInTheDocument()
  })
})
