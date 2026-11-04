import type { ReactNode } from 'react'
import './auth.css'

/** Centered single-card layout shared by the public account screens. */
export function AuthLayout({ title, intro, children }: { title: string; intro?: string; children: ReactNode }) {
  return (
    <main className="app">
      <h1>{title}</h1>
      {intro && <p className="page-intro">{intro}</p>}
      {children}
    </main>
  )
}

/** Form-level error in a live region that stays mounted, so screen readers announce changes. */
export function FormAlert({ message }: { message: string | null }) {
  return <div role="alert">{message && <p className="alert">{message}</p>}</div>
}
