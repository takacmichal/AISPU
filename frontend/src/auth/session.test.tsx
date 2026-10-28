import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import { AuthProvider } from './AuthContext'
import { authFetch } from './http'
import { loadSession, safeReturnUrl, saveSession, type Session } from './session'

const jwt = (claims: object) => `h.${btoa(JSON.stringify(claims))}.s`
const admin: Session = { email: 'a@test.sk', roles: ['Admin'], accessToken: 'old', refreshToken: 'r1' }
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

function renderApp(path: string, initial: Session | null) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider initialSession={initial}>
        <App />
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('session handling', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('logout clears tokens, calls the API and redirects to login', async () => {
    saveSession(admin)
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 503 }))
    vi.stubGlobal('fetch', fetchMock)
    vi.spyOn(console, 'error').mockImplementation(() => {})
    renderApp('/customers', admin)

    await userEvent.click(screen.getByRole('button', { name: 'Odhlásiť' }))

    expect(await screen.findByRole('heading', { name: 'Prihlásenie' })).toBeInTheDocument()
    expect(loadSession()).toBeNull()
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/auth/logout'),
      expect.objectContaining({ body: JSON.stringify({ refreshToken: 'r1' }) }),
    )
  })

  it('refreshes the token on 401 and retries the request', async () => {
    saveSession(admin)
    const newAccess = jwt({ email: 'a@test.sk', role: 'Admin' })
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(json({ accessToken: newAccess, expiresIn: 60, refreshToken: 'r2' }))
      .mockResolvedValueOnce(json({ ok: true }))
    vi.stubGlobal('fetch', fetchMock)

    const response = await authFetch('/api/x')

    expect(response.status).toBe(200)
    expect(loadSession()?.refreshToken).toBe('r2')
    const retryHeaders = fetchMock.mock.calls[2][1].headers as Headers
    expect(retryHeaders.get('Authorization')).toBe(`Bearer ${newAccess}`)
  })

  it('signs out with a message when refresh fails', async () => {
    saveSession(admin)
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string) => (url.includes('/api/system/info') ? new Response(null, { status: 503 }) : new Response(null, { status: 401 }))),
    )
    vi.spyOn(console, 'error').mockImplementation(() => {})
    renderApp('/', admin)

    await authFetch('/api/protected')

    expect(await screen.findByText(/Platnosť relácie vypršala/)).toBeInTheDocument()
    expect(loadSession()).toBeNull()
  })

  it('returns to the originally requested URL after login', async () => {
    const access = jwt({ email: 'a@test.sk', role: 'Admin' })
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(json({ accessToken: access, expiresIn: 60, refreshToken: 'r' })),
    )
    renderApp('/customers', null)

    await userEvent.type(await screen.findByLabelText('E-mail'), 'a@test.sk')
    await userEvent.type(screen.getByLabelText('Heslo'), 'Secret12345')
    await userEvent.click(screen.getByRole('button', { name: 'Prihlásiť' }))

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Zákazníci' })).toBeInTheDocument())
  })

  it('403 and 404 pages link back', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    renderApp('/nope', admin)
    expect(screen.getByRole('link', { name: 'Späť na prehľad' })).toHaveAttribute('href', '/')
  })

  it('rejects unsafe return URLs', () => {
    expect(safeReturnUrl('//evil.com')).toBe('/')
    expect(safeReturnUrl('https://evil.com')).toBe('/')
    expect(safeReturnUrl('/customers?page=2')).toBe('/customers?page=2')
    expect(safeReturnUrl(null)).toBe('/')
  })
})
