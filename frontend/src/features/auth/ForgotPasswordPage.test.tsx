import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AUTH_TEXTS } from './texts'
import { empty, json, renderAt, requestBody } from './testUtils'

const T = AUTH_TEXTS
const user = userEvent.setup()

async function request(email: string) {
  await user.type(screen.getByLabelText(T.email), email)
  await user.click(screen.getByRole('button', { name: T.forgot.submit }))
}

describe('ForgotPasswordPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('is reachable from the login page', async () => {
    renderAt('/login')
    await user.click(screen.getByRole('link', { name: T.login.forgotLink }))
    expect(screen.getByRole('heading', { name: T.forgot.title })).toBeInTheDocument()
  })

  it('validates the e-mail on the client', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/forgot-password')

    await user.click(screen.getByRole('button', { name: T.forgot.submit }))
    expect(screen.getByText(T.validation.emailRequired)).toBeInTheDocument()
    expect(screen.getByLabelText(T.email)).toHaveFocus()

    await request('nie-email')
    expect(screen.getByText(T.validation.emailInvalid)).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('shows the neutral confirmation after the request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(empty(202))
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/forgot-password')

    await request('jan@test.sk')

    expect(await screen.findByRole('status')).toHaveTextContent(T.forgot.sent)
    expect(screen.queryByRole('button', { name: T.forgot.submit })).not.toBeInTheDocument()
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/api\/auth\/forgot-password$/)
    expect(requestBody(fetchMock)).toEqual({ email: 'jan@test.sk' })
  })

  it('shows the same confirmation for an unknown e-mail (no account enumeration)', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(empty(202)))
    renderAt('/forgot-password')

    await request('nikto@test.sk')

    expect(await screen.findByRole('status')).toHaveTextContent(T.forgot.sent)
  })

  it('shows an error when the server is unavailable and keeps the form', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    renderAt('/forgot-password')

    await request('jan@test.sk')

    expect(await screen.findByText('Server je nedostupný. Skontrolujte pripojenie a skúste to znova.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: T.forgot.submit })).toBeEnabled()
    expect(screen.queryByText(T.forgot.sent)).not.toBeInTheDocument()
  })

  it('maps a 400 on the e-mail to the field', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ status: 400, errors: { Email: ['The Email field is required.'] } }, 400)))
    renderAt('/forgot-password')

    await request('jan@test.sk')

    expect(await screen.findByText(T.validation.emailInvalid)).toBeInTheDocument()
    expect(screen.queryByText(/field is required/)).not.toBeInTheDocument()
  })
})
