import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/problem'
import { FormField } from '../../components/forms'
import { forgotPassword } from './authApi'
import { AuthLayout, FormAlert } from './AuthLayout'
import { formErrorMessage, useFocusFirstInvalid } from './formHelpers'
import { AUTH_TEXTS } from './texts'
import { validateEmail } from './validation'

const T = AUTH_TEXTS

/**
 * Requests a reset link. Every accepted request shows the same neutral confirmation, so the page never
 * reveals whether an account exists (the API answers 202 in both cases).
 */
export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [emailError, setEmailError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const formRef = useFocusFirstInvalid(attempt)

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFormError(null)
    const invalid = validateEmail(email)
    setEmailError(invalid)
    if (invalid) {
      setAttempt((n) => n + 1)
      return
    }

    setBusy(true)
    try {
      await forgotPassword({ email: email.trim() })
      setSent(true)
    } catch (error) {
      if (error instanceof ApiError && error.status === 400 && error.fieldErrors.email) {
        setEmailError(T.validation.emailInvalid)
      } else {
        setFormError(formErrorMessage(error))
      }
      setAttempt((n) => n + 1)
      setBusy(false)
    }
  }

  return (
    <AuthLayout title={T.forgot.title} intro={sent ? undefined : T.forgot.intro}>
      <div role="status">{sent && <p className="auth-notice">{T.forgot.sent}</p>}</div>
      {!sent && (
        <form ref={formRef} onSubmit={onSubmit} className="auth-form" noValidate>
          <FormField label={T.email} errors={emailError ? [emailError] : []}>
            {(field) => (
              <input
                {...field}
                name="email"
                type="email"
                autoComplete="username"
                maxLength={256}
                value={email}
                onChange={(e) => {
                  setEmail(e.target.value)
                  setEmailError(null)
                }}
              />
            )}
          </FormField>
          <FormAlert message={formError} />
          <button type="submit" className="btn btn--primary" disabled={busy} aria-busy={busy}>
            {busy ? T.forgot.submitting : T.forgot.submit}
          </button>
        </form>
      )}
      <p className="auth-links">
        <Link to="/login">{T.backToLogin}</Link>
      </p>
    </AuthLayout>
  )
}
