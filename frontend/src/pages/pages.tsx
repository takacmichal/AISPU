import { useId, useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { HOME_PATH, findItem } from '../auth/navigation'
import { safeReturnUrl } from '../auth/session'
import type { LoginNotice } from '../features/auth/formHelpers'
import { AUTH_TEXTS } from '../features/auth/texts'
import { AuthLayout, FormAlert } from '../features/auth/AuthLayout'
import { MESSAGES } from '../lib/messages'
import { PageHeader } from '../components/PageHeader'

export function LoginPage() {
  const { session, signIn, signOutReason } = useAuth()
  const navigate = useNavigate()
  const notice = useLocation().state as LoginNotice | null
  const [params] = useSearchParams()
  const returnUrl = safeReturnUrl(params.get('returnUrl'))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const id = useId()
  const T = AUTH_TEXTS

  if (session) return <Navigate to={returnUrl} replace />

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setBusy(true)
    setError(null)
    try {
      await signIn(String(form.get('email')), String(form.get('password')))
      navigate(returnUrl, { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : T.login.failed)
      setBusy(false)
    }
  }

  return (
    <AuthLayout title={T.login.title} intro={T.login.intro} tab="login">
      {signOutReason === 'expired' && (
        <p role="status" className="auth-notice auth-notice--info">
          {MESSAGES.errors.unauthorized}
        </p>
      )}
      {notice?.notice && (
        <p role="status" className="auth-notice">
          {notice.notice}
        </p>
      )}
      <form onSubmit={onSubmit} className="auth-form">
        <div className="form-field">
          <label htmlFor={`${id}-email`}>{T.email}</label>
          <input id={`${id}-email`} name="email" type="email" required autoComplete="username" defaultValue={notice?.email} />
        </div>
        <div className="form-field">
          {/* The link sits next to the label, not inside it, so the label stays a plain name of the field. */}
          <div className="auth-label-row">
            <label htmlFor={`${id}-password`}>{T.password}</label>
            <Link to="/forgot-password">{T.login.forgotLink}</Link>
          </div>
          <input id={`${id}-password`} name="password" type="password" required autoComplete="current-password" />
        </div>
        <FormAlert message={error} />
        <button type="submit" className="btn btn--primary" disabled={busy} aria-busy={busy}>
          {busy ? T.login.submitting : T.login.submit}
        </button>
      </form>
      <p className="auth-links">
        <span>
          {T.login.registerPrompt} <Link to="/register">{T.login.registerLink}</Link>
        </span>
      </p>
    </AuthLayout>
  )
}

/** Placeholder for sections implemented by later stories. */
export function SectionPage({ path }: { path: string }) {
  return <PageHeader title={findItem(path)?.label ?? path} />
}

export function ForbiddenPage() {
  return (
    <section>
      <PageHeader title="403 – Nemáte prístup" subtitle="Na túto sekciu nemáte oprávnenie." />
      <Link to={HOME_PATH} className="btn btn--ghost">
        Späť na prehľad
      </Link>
    </section>
  )
}

export function NotFoundPage() {
  return (
    <section>
      <PageHeader title="404 – Stránka neexistuje" subtitle="Adresa neexistuje alebo bola presunutá." />
      <Link to={HOME_PATH} className="btn btn--ghost">
        Späť na prehľad
      </Link>
    </section>
  )
}
