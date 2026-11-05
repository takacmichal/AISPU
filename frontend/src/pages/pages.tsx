import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { Link, Navigate, useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { HOME_PATH, ROLE_LABELS, findItem, matchItem, primaryRole } from '../auth/navigation'
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

interface ErrorStateProps {
  code: string
  title: string
  text: string
  details?: ReactNode
}

/** Error page in the new style: big muted code, title, explanation and the way back. */
function ErrorState({ code, title, text, details }: ErrorStateProps) {
  const navigate = useNavigate()
  return (
    <section className="error-page" aria-labelledby="error-page-title">
      <div className="card error-page__card">
        <p className="error-page__code" aria-hidden="true">
          {code}
        </p>
        <h2 id="error-page-title" className="error-page__title">
          <span className="sr-only">{code}: </span>
          {title}
        </h2>
        <p className="error-page__text">{text}</p>
        {details}
        <div className="error-page__actions">
          <button type="button" className="btn btn--ghost" onClick={() => navigate(-1)}>
            {MESSAGES.errorPages.back}
          </button>
          <Link to={HOME_PATH} className="btn btn--primary">
            {MESSAGES.errorPages.home}
          </Link>
        </div>
      </div>
    </section>
  )
}

export function ForbiddenPage() {
  const { roles } = useAuth()
  const from = (useLocation().state as { from?: string } | null)?.from
  const item = from ? matchItem(from) : undefined
  const role = primaryRole(roles)
  const T = MESSAGES.errorPages
  return (
    <ErrorState
      code="403"
      title={T.forbiddenTitle}
      text={item ? T.forbiddenSection(item.label) : T.forbiddenText}
      details={
        <ul className="error-page__details">
          {role && <li>{T.yourRole(ROLE_LABELS[role] ?? role)}</li>}
          {item && item.roles.length > 0 && <li>{T.allowedRoles(item.roles.map((r) => ROLE_LABELS[r] ?? r).join(', '))}</li>}
        </ul>
      }
    />
  )
}

export function NotFoundPage() {
  const { pathname } = useLocation()
  const T = MESSAGES.errorPages
  return <ErrorState code="404" title={T.notFoundTitle} text={T.notFoundText} details={<p className="error-page__path mono">{pathname}</p>} />
}
