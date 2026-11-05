import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { BrandGlyph } from '../../layout/NavIcon'
import { AUTH_TEXTS } from './texts'
import './auth.css'

const L = AUTH_TEXTS.layout

interface AuthLayoutProps {
  title: string
  intro?: string
  /** Shows the Prihlásenie / Registrácia switch with this screen selected. */
  tab?: 'login' | 'register'
  children: ReactNode
}

/** Login, registration and password screens (wireframe): form card on the left, FVE lifecycle on the right. */
export function AuthLayout({ title, intro, tab, children }: AuthLayoutProps) {
  return (
    <main className="auth-layout">
      <div className="auth-layout__card">
        <div className="auth-layout__form">
          <Link to="/" className="auth-brand" aria-label={L.homeLabel}>
            <BrandGlyph />
            <span className="auth-brand__text">
              <span className="auth-brand__name">SolarOps</span>
              <span className="auth-brand__sub">{L.brandSubtitle}</span>
            </span>
          </Link>
          <div>
            <h1 className="auth-layout__title">{title}</h1>
            {intro && <p className="auth-layout__intro">{intro}</p>}
          </div>
          {tab && (
            <nav aria-label={L.tabsLabel} className="auth-tabs">
              <Link to="/login" className="auth-tabs__link" aria-current={tab === 'login' ? 'page' : undefined}>
                {L.loginTab}
              </Link>
              <Link to="/register" className="auth-tabs__link" aria-current={tab === 'register' ? 'page' : undefined}>
                {L.registerTab}
              </Link>
            </nav>
          )}
          {children}
        </div>
        <aside className="auth-layout__aside" aria-labelledby="auth-lifecycle-title">
          <p className="auth-layout__eyebrow">{L.lifecycleEyebrow}</p>
          <h2 id="auth-lifecycle-title" className="auth-layout__aside-title">
            {L.lifecycleTitle}
          </h2>
          <ol className="auth-lifecycle">
            {L.lifecycle.map((step, index) => (
              <li key={step.title}>
                <span className="auth-lifecycle__number" aria-hidden="true">
                  {index + 1}
                </span>
                <span>
                  <span className="auth-lifecycle__title">{step.title}</span>
                  <span className="auth-lifecycle__text">{step.text}</span>
                </span>
              </li>
            ))}
          </ol>
        </aside>
      </div>
    </main>
  )
}

/** Form-level error in a live region that stays mounted, so screen readers announce changes. */
export function FormAlert({ message }: { message: string | null }) {
  return <div role="alert">{message && <p className="alert">{message}</p>}</div>
}
