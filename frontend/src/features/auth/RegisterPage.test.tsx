import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AUTH_TEXTS } from './texts'
import { json, renderAt, requestBody } from './testUtils'
import { passwordProblems } from './validation'

const T = AUTH_TEXTS
const user = userEvent.setup()

async function fill(email: string, password: string, confirm = password) {
  await user.type(screen.getByLabelText(T.email), email)
  await user.type(screen.getByLabelText(T.password), password)
  await user.type(screen.getByLabelText(T.confirmPassword), confirm)
}

const submit = () => user.click(screen.getByRole('button', { name: T.register.submit }))

describe('RegisterPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('is reachable from the login page', async () => {
    renderAt('/login')
    await user.click(screen.getByRole('link', { name: T.login.registerLink }))
    expect(screen.getByRole('heading', { name: T.register.title })).toBeInTheDocument()
  })

  it('validates on the client, focuses the first invalid field and sends nothing', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/register')

    await submit()
    expect(screen.getByText(T.validation.emailRequired)).toBeInTheDocument()
    expect(screen.getByText(T.validation.passwordRequired)).toBeInTheDocument()
    expect(screen.getByText(T.validation.confirmRequired)).toBeInTheDocument()
    expect(screen.getByLabelText(T.email)).toHaveFocus()
    expect(screen.getByLabelText(T.email)).toHaveAttribute('aria-invalid', 'true')

    await fill('jan@test.sk', 'kratke1A', 'ine')
    await submit()
    expect(screen.queryByText(T.validation.emailRequired)).not.toBeInTheDocument()
    expect(screen.getByText(new RegExp(T.validation.passwordWeak))).toBeInTheDocument()
    expect(screen.getByText(T.validation.confirmMismatch)).toBeInTheDocument()
    expect(screen.getByLabelText(T.password)).toHaveFocus()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('mirrors every rule of the server password policy', () => {
    expect(passwordProblems('Silneheslo1')).toEqual([])
    expect(passwordProblems('Kratke1a')).toContain('length')
    expect(passwordProblems('bezvelkeho1')).toEqual(['uppercase'])
    expect(passwordProblems('BEZMALEHO12')).toEqual(['lowercase'])
    expect(passwordProblems('BezCislicee')).toEqual(['digit'])
    expect(passwordProblems('Aa1Aa1Aa1Aa1')).toEqual(['unique'])
  })

  it('rejects a password that breaks only the unique-characters rule', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/register')

    await fill('jan@test.sk', 'Aa1Aa1Aa1Aa1')
    await submit()
    expect(screen.getByText(new RegExp(T.validation.passwordWeak))).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('registers and goes to login with a success message and the e-mail prefilled', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json({ id: '1', email: 'jan@test.sk', roles: ['Customer'] }, 201))
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/register')

    await fill('  jan@test.sk ', 'Silneheslo1')
    await submit()

    expect(await screen.findByRole('heading', { name: 'Prihlásenie' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(T.register.success)
    expect(screen.getByLabelText('E-mail')).toHaveValue('jan@test.sk')
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/api\/auth\/register$/)
    expect(requestBody(fetchMock)).toEqual({ email: 'jan@test.sk', password: 'Silneheslo1' })
  })

  it('maps 409 (existing account) to the e-mail field in Slovak', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(json({ status: 409, title: 'Conflict', detail: 'An account with this e-mail cannot be created.' }, 409)),
    )
    renderAt('/register')

    await fill('jan@test.sk', 'Silneheslo1')
    await submit()

    expect(await screen.findByText(T.register.emailTaken)).toBeInTheDocument()
    expect(screen.queryByText(/An account/)).not.toBeInTheDocument()
    expect(screen.getByLabelText(T.email)).toHaveFocus()
    expect(screen.getByRole('button', { name: T.register.submit })).toBeEnabled()
  })

  it('maps 400 field errors to Slovak messages next to the fields', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        json({ status: 400, errors: { Password: ['Passwords must have at least one digit.'], Email: ['Invalid.'] } }, 400),
      ),
    )
    renderAt('/register')

    await fill('jan@test.sk', 'Silneheslo1')
    await submit()

    expect(await screen.findByText(new RegExp(T.validation.passwordWeak))).toBeInTheDocument()
    expect(screen.getByText(T.validation.emailInvalid)).toBeInTheDocument()
    expect(screen.queryByText(/Passwords must/)).not.toBeInTheDocument()
  })

  it('shows a form error for server failures and disables submit while saving', async () => {
    let resolve: (r: Response) => void = () => {}
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(new Promise<Response>((r) => (resolve = r))))
    renderAt('/register')

    await fill('jan@test.sk', 'Silneheslo1')
    await submit()
    expect(screen.getByRole('button', { name: T.register.submitting })).toBeDisabled()

    resolve(json({ status: 500, title: 'Internal' }, 500))
    expect(await screen.findByText('Na serveri nastala chyba. Skúste to prosím neskôr.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: T.register.submit })).toBeEnabled())
  })
})
