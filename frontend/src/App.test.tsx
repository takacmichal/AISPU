import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { AuthProvider } from './auth/AuthContext'
import { ConfirmProvider } from './components/confirm'
import { ToastProvider } from './components/toast'
import { breadcrumbs, menuGroups, menuItems } from './auth/navigation'
import type { Session } from './auth/session'

function session(...roles: string[]): Session {
  return { email: 'user@solarops.test', roles, accessToken: 'a', refreshToken: 'r' }
}

function renderAt(path: string, s: Session | null) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider initialSession={s}>
        <ToastProvider>
          <ConfirmProvider>
            <App />
          </ConfirmProvider>
        </ToastProvider>
      </AuthProvider>
    </MemoryRouter>,
  )
}

describe('role navigation', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })
  afterEach(() => vi.unstubAllGlobals())

  it('config yields different menus per role', () => {
    const labels = (role: string) => menuItems([role]).map((i) => i.label)
    expect(labels('Sales')).toContain('Zákazníci')
    expect(labels('Sales')).not.toContain('Servisné tikety')
    expect(labels('Technician')).toContain('Servisné tikety')
    expect(labels('Customer')).toEqual(['Prehľad', 'Moje elektrárne', 'Servisné tikety'])
    expect(labels('Admin')).toContain('Používatelia a roly')
    expect(labels('Admin')).not.toContain('Administrácia')
  })

  it('groups menu items into sections and drops empty ones', () => {
    const sections = (role: string) => menuGroups([role]).map((s) => [s.group ?? '', s.items.map((i) => i.label)])
    expect(sections('Admin').map(([group]) => group)).toEqual(['', 'Obchod', 'Realizácia', 'Servis', 'Systém'])
    expect(sections('Sales')).toEqual([
      ['', ['Prehľad']],
      ['Obchod', ['Zákazníci', 'Návrhy FVE', 'Ponuky', 'Zmluvy']],
      ['Realizácia', ['Pripojenie k distribučke']],
      ['Systém', ['Katalóg komponentov']],
    ])
    expect(sections('Technician')).toEqual([
      ['', ['Prehľad']],
      ['Realizácia', ['Inštalácie', 'Pripojenie k distribučke', 'Monitoring výroby']],
      ['Servis', ['Servisné tikety']],
    ])
    expect(sections('Customer')).toEqual([
      ['', ['Prehľad', 'Moje elektrárne']],
      ['Servis', ['Servisné tikety']],
    ])
  })

  it('renders labelled sidebar sections with links', () => {
    renderAt('/app', session('Admin'))
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    const sales = within(nav).getByRole('list', { name: 'Obchod' })
    expect(within(sales).getByRole('link', { name: 'Zákazníci' })).toBeInTheDocument()
    expect(within(sales).queryByRole('link', { name: 'Inštalácie' })).not.toBeInTheDocument()
    const delivery = within(nav).getByRole('list', { name: 'Realizácia' })
    expect(within(delivery).getByRole('link', { name: 'Inštalácie' })).toBeInTheDocument()
    expect(within(nav).getByRole('list', { name: 'Servis' })).toBeInTheDocument()
    expect(within(nav).getByRole('list', { name: 'Systém' })).toBeInTheDocument()
  })

  it('signs out from the user menu in the top bar', async () => {
    renderAt('/app', session('Admin'))
    const trigger = screen.getByRole('button', { name: /user@solarops\.test/ })
    expect(trigger).toHaveTextContent('Administrátor')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    await userEvent.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Odhlásiť' }))
    expect(await screen.findByRole('heading', { name: 'Prihlásenie' })).toBeInTheDocument()
  })

  it('collapses the sidebar to icons and remembers it', async () => {
    renderAt('/app', session('Admin'))
    const toggle = screen.getByRole('button', { name: 'Zbaliť menu' })
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    await userEvent.click(toggle)
    expect(screen.getByRole('button', { name: 'Rozbaliť menu' })).toHaveAttribute('aria-expanded', 'false')
    expect(localStorage.getItem('solarops.sidebar.collapsed')).toBe('1')
    // Links keep their accessible names when only icons are visible.
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(nav).getByRole('link', { name: 'Zákazníci' })).toBeInTheDocument()
  })

  it('jumps to a module from the quick jump dialog (Ctrl+K)', async () => {
    renderAt('/app', session('Sales'))
    await userEvent.keyboard('{Control>}k{/Control}')
    const dialog = screen.getByRole('dialog', { name: 'Prejsť na modul' })
    const input = within(dialog).getByRole('combobox', { name: 'Názov modulu' })
    await userEvent.type(input, 'pon')
    expect(within(dialog).getAllByRole('option').map((o) => o.textContent)).toEqual(['Ponuky'])
    await userEvent.keyboard('{Enter}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(nav).getByRole('link', { name: 'Ponuky' })).toHaveAttribute('aria-current', 'page')
  })

  it('offers only modules the role may open in the quick jump', async () => {
    renderAt('/app', session('Sales'))
    await userEvent.click(screen.getByRole('button', { name: /Prejsť na/ }))
    const options = within(screen.getByRole('listbox')).getAllByRole('option').map((o) => o.textContent)
    expect(options).toContain('Zákazníci')
    expect(options).not.toContain('Servisné tikety')
  })

  it('opens and closes the mobile menu', async () => {
    renderAt('/app', session('Admin'))
    const burger = screen.getByRole('button', { name: 'Otvoriť menu' })
    expect(burger).toHaveAttribute('aria-expanded', 'false')
    await userEvent.click(burger)
    expect(burger).toHaveAttribute('aria-expanded', 'true')
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    await userEvent.click(within(nav).getByRole('link', { name: 'Zákazníci' }))
    expect(screen.getByRole('button', { name: 'Otvoriť menu' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('shows the public landing page on / to visitors', async () => {
    renderAt('/', null)
    expect(await screen.findByRole('heading', { level: 1, name: /Fotovoltika/ })).toBeInTheDocument()
    expect(within(screen.getByRole('banner')).getByRole('link', { name: 'Prihlásiť sa' })).toHaveAttribute('href', '/login')
  })

  it('redirects / to the dashboard on /app', async () => {
    renderAt('/', session('Admin'))
    expect(await screen.findByRole('heading', { name: 'Prehľad', level: 2 })).toBeInTheDocument()
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(nav).getByRole('link', { name: 'Prehľad' })).toHaveAttribute('href', '/app')
  })

  it('hides empty sidebar sections for the role', () => {
    renderAt('/app', session('Technician'))
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(nav).queryByRole('list', { name: 'Obchod' })).not.toBeInTheDocument()
    expect(within(nav).getByRole('list', { name: 'Realizácia' })).toBeInTheDocument()
  })

  it('shows only allowed items in the menu', () => {
    renderAt('/app', session('Sales'))
    const nav = screen.getByRole('navigation', { name: 'Hlavná navigácia' })
    expect(within(nav).getByRole('link', { name: 'Zákazníci' })).toBeInTheDocument()
    expect(within(nav).queryByRole('link', { name: 'Servisné tikety' })).not.toBeInTheDocument()
  })

  it('guards unauthorized routes with 403', () => {
    renderAt('/tickets', session('Sales'))
    expect(screen.getByRole('heading', { name: /403/ })).toBeInTheDocument()
  })

  it('wires the customer portal routes', async () => {
    renderAt('/my-plants', session('Customer'))
    expect(await screen.findByText('Zoznam elektrární sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('guards the customer portal detail with 403 for internal roles', () => {
    renderAt('/my-plants/i1', session('Admin'))
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
    expect(within(crumbs).getByRole('link', { name: 'Prehľad' })).toHaveAttribute('href', '/app')
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
    renderAt('/app', session('Admin'))
    expect(await screen.findByText('API je nedostupné.')).toBeInTheDocument()
  })
})
