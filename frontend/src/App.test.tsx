import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { AuthProvider } from './auth/AuthContext'
import { breadcrumbs, menuItems } from './auth/navigation'
import type { Session } from './auth/session'

function session(...roles: string[]): Session {
  return { email: 'user@solarops.test', roles, accessToken: 'a', refreshToken: 'r' }
}

function renderAt(path: string, s: Session | null) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider initialSession={s}>
        <App />
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('role navigation', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })
  afterEach(() => vi.unstubAllGlobals())

  it('config yields different menus per role', () => {
    const labels = (role: string) => menuItems([role]).map((i) => i.label)
    expect(labels('Sales')).toContain('Zákazníci')
    expect(labels('Sales')).not.toContain('Servisné tikety')
    expect(labels('Technician')).toContain('Servisné tikety')
    expect(labels('Customer')).toEqual(['Prehľad', 'Moje elektrárne'])
    expect(labels('Admin')).toContain('Používatelia a roly')
    expect(labels('Admin')).not.toContain('Administrácia')
  })

  it('shows only allowed items in the menu', () => {
    renderAt('/', session('Sales'))
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(nav).getByRole('link', { name: 'Zákazníci' })).toBeInTheDocument()
    expect(within(nav).queryByRole('link', { name: 'Servisné tikety' })).not.toBeInTheDocument()
  })

  it('guards unauthorized routes with 403', () => {
    renderAt('/tickets', session('Sales'))
    expect(screen.getByRole('heading', { name: /403/ })).toBeInTheDocument()
  })

  it('redirects anonymous users to login', () => {
    renderAt('/customers', null)
    expect(screen.getByRole('heading', { name: 'Prihlásenie' })).toBeInTheDocument()
  })

  it('renders breadcrumbs for nested routes', () => {
    renderAt('/admin/users', session('Admin'))
    const crumbs = screen.getByRole('navigation', { name: 'Breadcrumbs' })
    expect(within(crumbs).getByText('Používatelia a roly')).toHaveAttribute('aria-current', 'page')
    expect(within(crumbs).getByRole('link', { name: 'Prehľad' })).toBeInTheDocument()
    expect(breadcrumbs('/admin/users').map((i) => i.label)).toEqual(['Prehľad', 'Administrácia', 'Používatelia a roly'])
  })

  it('marks the current menu link active', () => {
    renderAt('/customers', session('Admin'))
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(nav).getByRole('link', { name: 'Zákazníci' })).toHaveAttribute('aria-current', 'page')
  })

  it('shows 404 for unknown routes', () => {
    renderAt('/nope', session('Admin'))
    expect(screen.getByRole('heading', { name: /404/ })).toBeInTheDocument()
  })

  it('renders the dashboard API status', async () => {
    renderAt('/', session('Admin'))
    expect(await screen.findByRole('alert')).toHaveTextContent('API je nedostupné.')
  })
})
