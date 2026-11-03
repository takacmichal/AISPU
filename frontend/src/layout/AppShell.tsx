import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useEffect, useRef } from 'react'
import { useAuth } from '../auth/AuthContext'
import { breadcrumbs, menuItems } from '../auth/navigation'
import { MESSAGES } from '../lib/messages'

export function AppShell() {
  const { session, roles, signOut } = useAuth()
  const { pathname } = useLocation()
  const trail = breadcrumbs(pathname)
  const mainRef = useRef<HTMLElement>(null)

  // Move focus to the content after route changes so keyboard and screen reader users start at the new page.
  const first = useRef(true)
  useEffect(() => {
    if (first.current) {
      first.current = false
      return
    }
    mainRef.current?.focus()
  }, [pathname])

  return (
    <div className="shell">
      <a href="#main-content" className="skip-link">
        {MESSAGES.skipToContent}
      </a>
      <aside className="shell__side">
        <h1 className="shell__brand">SolarOps</h1>
        <nav aria-label="Hlavná navigácia">
          <ul>
            {menuItems(roles).map((item) => (
              <li key={item.path}>
                <NavLink to={item.path} end={item.path === '/'}>
                  {item.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <p className="shell__user">
          {session?.email}
          <button type="button" className="btn btn--ghost btn--sm" onClick={signOut}>
            Odhlásiť
          </button>
        </p>
      </aside>
      <main id="main-content" ref={mainRef} tabIndex={-1} className="shell__main">
        {trail.length > 1 && (
          <nav aria-label="Breadcrumbs" className="breadcrumbs">
            <ol>
              {trail.map((item, index) => (
                <li key={item.path}>
                  {index === trail.length - 1 ? (
                    <span aria-current="page">{item.label}</span>
                  ) : (
                    <Link to={item.path}>{item.label}</Link>
                  )}
                </li>
              ))}
            </ol>
          </nav>
        )}
        <Outlet />
      </main>
    </div>
  )
}
