import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import LandingPage from './LandingPage'
import type { SendInquiry } from './inquiry'

function renderLanding(sendInquiry?: SendInquiry) {
  return render(
    <MemoryRouter>
      <LandingPage sendInquiry={sendInquiry} />
    </MemoryRouter>,
  )
}

describe('LandingPage', () => {
  it('has a skip link, one h1 and the sections from the design', () => {
    renderLanding()
    expect(screen.getByRole('link', { name: 'Preskočiť na obsah' })).toHaveAttribute('href', '#obsah')
    expect(screen.getAllByRole('heading', { level: 1 })).toHaveLength(1)
    const sections = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)
    expect(sections).toEqual([
      'SolarOps v číslach',
      'Jedna firma. Celá cesta vašej elektrárne.',
      'Ako to funguje',
      'Poruchu nájdeme skôr, ako si ju všimnete.',
      'Vaša elektráreň vo vrecku.',
      'Koľko vyrobí vaša strecha?',
      'Časté otázky',
      'Nezáväzný dopyt',
    ])
  })

  it('links sign in and registration to the app', () => {
    renderLanding()
    const nav = screen.getByRole('banner')
    expect(within(nav).getByRole('link', { name: 'Prihlásiť sa' })).toHaveAttribute('href', '/login')
    expect(within(screen.getByRole('contentinfo')).getByRole('link', { name: 'Registrácia' })).toHaveAttribute('href', '/register')
  })

  it('marks sample numbers as illustrative', () => {
    renderLanding()
    expect(screen.getAllByText('Ilustratívne údaje.').length).toBeGreaterThanOrEqual(1)
  })

  it('lists the seven steps in order', () => {
    renderLanding()
    const steps = screen.getByRole('list', { name: 'Ako to funguje' })
    expect(within(steps).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual([
      'Dopyt',
      'Ponuka',
      'Návrh FVE',
      'Zmluva',
      'Pripojenie',
      'Inštalácia',
      'Prevádzka a servis',
    ])
  })

  it('recalculates the yield estimate', async () => {
    renderLanding()
    expect(screen.getByText('9 890')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('radio', { name: 'Východ – západ' }))
    expect(screen.getByText('8 500')).toBeInTheDocument()
    await userEvent.selectOptions(screen.getByLabelText('Lokalita'), 'BA')
    expect(screen.getByText('9 080')).toBeInTheDocument()
    expect(screen.getByText(/Ide o odhad/)).toBeInTheDocument()
  })

  it('opens the mobile menu and closes it after choosing a section', async () => {
    renderLanding()
    const burger = screen.getByRole('button', { name: 'Otvoriť menu' })
    await userEvent.click(burger)
    const mobile = screen.getByRole('navigation', { name: 'Mobilná navigácia' })
    expect(screen.getByRole('button', { name: 'Zavrieť menu' })).toHaveAttribute('aria-expanded', 'true')
    await userEvent.click(within(mobile).getByRole('link', { name: 'Služby' }))
    expect(screen.queryByRole('navigation', { name: 'Mobilná navigácia' })).not.toBeInTheDocument()
  })

  it('leads to registration while inquiries cannot be sent', () => {
    renderLanding()
    const contact = screen.getByRole('region', { name: 'Nezáväzný dopyt' })
    expect(within(contact).getByRole('heading', { name: 'Online dopyt pripravujeme' })).toBeInTheDocument()
    expect(within(contact).queryByRole('button', { name: 'Odoslať dopyt' })).not.toBeInTheDocument()
    expect(within(contact).getByRole('link', { name: 'Vytvoriť účet' })).toHaveAttribute('href', '/register')
    expect(within(contact).queryByText(/Ozveme sa/)).not.toBeInTheDocument()
  })

  it('shows the inquiry form when sending is available', () => {
    renderLanding(vi.fn())
    const contact = screen.getByRole('region', { name: 'Nezáväzný dopyt' })
    expect(within(contact).getByRole('button', { name: 'Odoslať dopyt' })).toBeInTheDocument()
  })
})
