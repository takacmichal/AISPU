import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { findItem } from '../auth/navigation'

export function LoginPage() {
  const { session, signIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (session) return <Navigate to="/" replace />

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    setBusy(true)
    setError(null)
    try {
      await signIn(String(form.get('email')), String(form.get('password')))
      navigate((location.state as { from?: string } | null)?.from ?? '/', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Prihlásenie zlyhalo.')
      setBusy(false)
    }
  }

  return (
    <main className="app">
      <h1>Prihlásenie</h1>
      <form onSubmit={onSubmit}>
        <label>
          E-mail
          <input name="email" type="email" required autoComplete="username" />
        </label>
        <label>
          Heslo
          <input name="password" type="password" required autoComplete="current-password" />
        </label>
        {error && <p role="alert">{error}</p>}
        <button type="submit" disabled={busy}>
          Prihlásiť
        </button>
      </form>
    </main>
  )
}

/** Placeholder for sections implemented by later stories. */
export function SectionPage({ path }: { path: string }) {
  return <h2>{findItem(path)?.label ?? path}</h2>
}

export function ForbiddenPage() {
  return (
    <section>
      <h2>403 – Nemáte prístup</h2>
      <p>Na túto sekciu nemáte oprávnenie.</p>
      <Link to="/">Späť na prehľad</Link>
    </section>
  )
}

export function NotFoundPage() {
  return (
    <section>
      <h2>404 – Stránka neexistuje</h2>
      <Link to="/">Späť na prehľad</Link>
    </section>
  )
}
