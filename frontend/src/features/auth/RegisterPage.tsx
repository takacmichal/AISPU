import { useId, useState, type FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { ApiError } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { FormField } from '../../components/forms'
import { register } from './authApi'
import { AuthLayout, FormAlert } from './AuthLayout'
import { describedBy, formErrorMessage, useFocusFirstInvalid, type LoginNotice } from './formHelpers'
import { AUTH_TEXTS } from './texts'
import { collect, validateConfirmation, validateEmail, validatePassword, withoutField, type FieldErrors } from './validation'
import { HOME_PATH } from '../../auth/navigation'

const T = AUTH_TEXTS

/** Maps a failed registration to field messages; null when the error is not tied to a field. */
function serverFieldErrors(error: unknown): FieldErrors | null {
  if (!(error instanceof ApiError)) return null
  // Duplicate e-mail is a 409 without field errors; the wording does not confirm that the account exists.
  if (error.status === 409) return { email: [T.register.emailTaken] }
  if (error.status !== 400) return null
  const errors: FieldErrors = {}
  if (error.fieldErrors.email) errors.email = [T.validation.emailInvalid]
  if (error.fieldErrors.password) errors.password = [`${T.validation.passwordWeak} ${T.passwordRules}`]
  return Object.keys(errors).length > 0 ? errors : null
}

export default function RegisterPage() {
  const { session } = useAuth()
  const navigate = useNavigate()
  const rulesId = useId()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const formRef = useFocusFirstInvalid(attempt)

  if (session) return <Navigate to={HOME_PATH} replace />

  function clear(field: string) {
    setErrors((current) => withoutField(current, field))
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFormError(null)
    const found = collect({
      email: validateEmail(email),
      password: validatePassword(password),
      confirm: validateConfirmation(password, confirm),
    })
    setErrors(found)
    if (Object.keys(found).length > 0) {
      setAttempt((n) => n + 1)
      return
    }

    setBusy(true)
    try {
      await register({ email: email.trim(), password })
      const state: LoginNotice = { notice: T.register.success, email: email.trim() }
      navigate('/login', { replace: true, state })
    } catch (error) {
      const fieldErrors = serverFieldErrors(error)
      if (fieldErrors) setErrors(fieldErrors)
      else setFormError(formErrorMessage(error))
      setAttempt((n) => n + 1)
      setBusy(false)
    }
  }

  return (
    <AuthLayout title={T.register.title} intro={T.register.intro}>
      <form ref={formRef} onSubmit={onSubmit} className="card auth-form" noValidate>
        <FormField label={T.email} errors={errors.email}>
          {(field) => (
            <input
              {...field}
              name="email"
              type="email"
              autoComplete="email"
              maxLength={256}
              value={email}
              onChange={(e) => {
                setEmail(e.target.value)
                clear('email')
              }}
            />
          )}
        </FormField>
        <FormField label={T.password} errors={errors.password}>
          {(field) => (
            <>
              <input
                {...field}
                aria-describedby={describedBy(field['aria-describedby'], rulesId)}
                name="password"
                type="password"
                autoComplete="new-password"
                maxLength={128}
                value={password}
                onChange={(e) => {
                  setPassword(e.target.value)
                  clear('password')
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
          {busy ? T.register.submitting : T.register.submit}
        </button>
      </form>
      <p className="auth-links">
        <span>
          {T.register.haveAccount} <Link to="/login">{T.register.loginLink}</Link>
        </span>
      </p>
    </AuthLayout>
  )
}
