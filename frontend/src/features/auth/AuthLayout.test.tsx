import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { AUTH_TEXTS } from './texts'
import { renderAt } from './testUtils'

const T = AUTH_TEXTS
const user = userEvent.setup()

describe('AuthLayout', () => {
  beforeEach(() => localStorage.clear())

  it('switches between login and registration', async () => {
    renderAt('/login')
    const tabs = screen.getByRole('navigation', { name: T.layout.tabsLabel })
    expect(within(tabs).getByRole('link', { name: T.layout.loginTab })).toHaveAttribute('aria-current', 'page')

    await user.click(within(tabs).getByRole('link', { name: T.layout.registerTab }))
    expect(screen.getByRole('heading', { level: 1, name: T.register.title })).toBeInTheDocument()
    expect(within(screen.getByRole('navigation', { name: T.layout.tabsLabel })).getByRole('link', { name: T.layout.registerTab })).toHaveAttribute(
      'aria-current',
      'page',
    )
  })

  it('shows the FVE lifecycle beside the form', () => {
    renderAt('/login')
    const aside = screen.getByRole('complementary', { name: T.layout.lifecycleTitle })
    expect(within(aside).getAllByRole('listitem')).toHaveLength(5)
  })

  it('links the brand to the landing page', () => {
    renderAt('/register')
    expect(screen.getByRole('link', { name: T.layout.homeLabel })).toHaveAttribute('href', '/')
  })

  it('offers only what the API supports (no SSO, no remember me)', () => {
    renderAt('/login')
    expect(screen.queryByText(/SSO/)).not.toBeInTheDocument()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: T.login.forgotLink })).toHaveAttribute('href', '/forgot-password')
  })

  it('uses the same layout without the switch on password screens', () => {
    renderAt('/forgot-password')
    expect(screen.getByRole('heading', { level: 1, name: T.forgot.title })).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: T.layout.tabsLabel })).not.toBeInTheDocument()
    expect(screen.getByRole('complementary')).toBeInTheDocument()
  })
})
