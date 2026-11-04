import { useId, useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { FormField } from '../../components/forms'
import { resetPassword } from './authApi'
import { AuthLayout, FormAlert } from './AuthLayout'
import { describedBy, formErrorMessage, useFocusFirstInvalid, type LoginNotice } from './formHelpers'
import { AUTH_TEXTS } from './texts'
import { collect, validateConfirmation, validatePassword, withoutField, type FieldErrors } from './validation'

const T = AUTH_TEXTS

/** Shown for a link without email/token and when the API rejects the token (unknown e-mail, wrong or expired). */
function InvalidLink() {
  return (
    <>
      <p className="alert" role="alert">
        {T.reset.invalidLink}
      </p>
      <p className="auth-links">
        <Link to="/forgot-password">{T.reset.requestNew}</Link>
        <Link to="/login">{T.backToLogin}</Link>
      </p>
    </>
  )
}

/** Target of the e-mailed link `/reset-password?email=…&token=…` (PasswordResetOptions.ResetUrl in the API). */
export default function ResetPasswordPage() {
  const [params] = useSearchParams()
  const email = params.get('email')?.trim() ?? ''
  const token = params.get('token') ?? ''
  const { session, signOut } = useAuth()
  const navigate = useNavigate()
  const rulesId = useId()
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [tokenInvalid, setTokenInvalid] = useState(false)
  const [busy, setBusy] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const formRef = useFocusFirstInvalid(attempt)

  if (!email || !token || tokenInvalid) {
    return (
      <AuthLayout title={T.reset.title}>
        <InvalidLink />
      </AuthLayout>
    )
  }

  function clear(field: string) {
    setErrors((current) => withoutField(current, field))
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFormError(null)
    const found = collect({
      newPassword: validatePassword(password),
      confirm: validateConfirmation(password, confirm),
    })
    setErrors(found)
    if (Object.keys(found).length > 0) {
      setAttempt((n) => n + 1)
      return
    }

    setBusy(true)
    try {
      await resetPassword({ email, token, newPassword: password })
      // The API revoked every refresh token of the account, so a local session is no longer usable.
      if (session) signOut()
      const state: LoginNotice = { notice: T.reset.success, email }
      navigate('/login', { replace: true, state })
    } catch (error) {
      setBusy(false)
      if (error instanceof ApiError && error.status === 400 && error.fieldErrors.token) {
        setTokenInvalid(true)
        return
      }
      if (error instanceof ApiError && error.status === 400 && error.fieldErrors.newPassword) {
        setErrors({ newPassword: [`${T.validation.passwordWeak} ${T.passwordRules}`] })
      } else {
        setFormError(formErrorMessage(error))
      }
      setAttempt((n) => n + 1)
    }
  }

  return (
    <AuthLayout title={T.reset.title} intro={T.reset.intro}>
      <form ref={formRef} onSubmit={onSubmit} className="auth-form" noValidate>
        <p className="auth-email">
          {T.email}: <strong>{email}</strong>
        </p>
        {/* Lets password managers store the new password under the right account. */}
        <input type="hidden" name="username" autoComplete="username" value={email} />
        <FormField label={T.newPassword} errors={errors.newPassword}>
          {(field) => (
            <>
              <input
                {...field}
                aria-describedby={describedBy(field['aria-describedby'], rulesId)}
                name="newPassword"
                type="password"
                autoComplete="new-password"
                maxLength={128}
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value)
                  clear('newPassword')
                }}
              />
              <p id={rulesId} className="auth-hint">
                {T.passwordRules}
              </p>
            </>
          )}
        </FormField>
        <FormField label={T.confirmPassword} errors={errors.confirm}>
          {(field) => (
            <input
              {...field}
              name="confirm"
              type="password"
              autoComplete="new-password"
              maxLength={128}
              value={confirm}
              onChange={(e) => {
                setConfirm(e.target.value)
                clear('confirm')
              }}
            />
          )}
        </FormField>
        <FormAlert message={formError} />
        <button type="submit" className="btn btn--primary" disabled={busy} aria-busy={busy}>
          {busy ? T.reset.submitting : T.reset.submit}
        </button>
      </form>
      <p className="auth-links">
        <Link to="/login">{T.backToLogin}</Link>
      </p>
    </AuthLayout>
  )
}
