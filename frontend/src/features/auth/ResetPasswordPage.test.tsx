import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { loadSession, saveSession } from '../../auth/session'
import { AUTH_TEXTS } from './texts'
import { empty, json, renderAt, requestBody } from './testUtils'

const T = AUTH_TEXTS
const user = userEvent.setup()

// Identity tokens are base64 and contain +, / and =; the API escapes them with Uri.EscapeDataString.
const TOKEN = 'CfDJ8+abc/def=='
const LINK = `/reset-password?email=${encodeURIComponent('jan+fve@test.sk')}&token=${encodeURIComponent(TOKEN)}`

async function setPassword(password: string, confirm = password) {
  await user.type(screen.getByLabelText(T.newPassword), password)
  await user.type(screen.getByLabelText(T.confirmPassword), confirm)
  await user.click(screen.getByRole('button', { name: T.reset.submit }))
}

describe('ResetPasswordPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it.each(['/reset-password', '/reset-password?email=jan%40test.sk', `/reset-password?token=${encodeURIComponent(TOKEN)}`])(
    'shows the invalid link state when a parameter is missing (%s)',
    (path) => {
      renderAt(path)
      expect(screen.getByRole('alert')).toHaveTextContent(T.reset.invalidLink)
      expect(screen.getByRole('link', { name: T.reset.requestNew })).toHaveAttribute('href', '/forgot-password')
      expect(screen.queryByLabelText(T.newPassword)).not.toBeInTheDocument()
    },
  )

  it('validates the new password and the confirmation on the client', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    renderAt(LINK)

    expect(screen.getByText('jan+fve@test.sk')).toBeInTheDocument()
    await setPassword('slabe', 'ine')

    expect(screen.getByText(new RegExp(T.validation.passwordWeak))).toBeInTheDocument()
    expect(screen.getByText(T.validation.confirmMismatch)).toBeInTheDocument()
    expect(screen.getByLabelText(T.newPassword)).toHaveFocus()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('sends the decoded e-mail and token and goes to login with a message', async () => {
    const fetchMock = vi.fn().mockResolvedValue(empty(204))
    vi.stubGlobal('fetch', fetchMock)
    renderAt(LINK)

    await setPassword('NoveHeslo123')

    expect(await screen.findByRole('heading', { name: 'Prihlásenie' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(T.reset.success)
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/api\/auth\/reset-password$/)
    expect(requestBody(fetchMock)).toEqual({ email: 'jan+fve@test.sk', token: TOKEN, newPassword: 'NoveHeslo123' })
  })

  it('drops a local session after the reset because the API revoked it', async () => {
    saveSession({ email: 'jan+fve@test.sk', roles: ['Customer'], accessToken: 'a', refreshToken: 'r' })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(empty(204)))
    renderAt(LINK)

    await setPassword('NoveHeslo123')

    expect(await screen.findByRole('heading', { name: 'Prihlásenie' })).toBeInTheDocument()
    expect(loadSession()).toBeNull()
  })

  it('shows the invalid link state for a wrong or expired token', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(json({ status: 400, errors: { Token: ['Invalid or expired token.'] } }, 400)),
    )
    renderAt(LINK)

    await setPassword('NoveHeslo123')

    expect(await screen.findByText(T.reset.invalidLink)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: T.reset.requestNew })).toBeInTheDocument()
    expect(screen.queryByText(/Invalid or expired/)).not.toBeInTheDocument()
  })

  it('maps a weak password rejected by the server to the field', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(json({ status: 400, errors: { NewPassword: ['Passwords must use at least 4 different characters.'] } }, 400)),
    )
    renderAt(LINK)

    await setPassword('NoveHeslo123')

    expect(await screen.findByText(new RegExp(T.validation.passwordWeak))).toBeInTheDocument()
    expect(screen.getByLabelText(T.newPassword)).toHaveFocus()
    expect(screen.queryByText(/Passwords must/)).not.toBeInTheDocument()
  })
})
