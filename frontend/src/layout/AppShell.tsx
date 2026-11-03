import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useEffect, useRef } from 'react'
import { useAuth } from '../auth/AuthContext'
import { breadcrumbs, menuGroups } from '../auth/navigation'
import { MESSAGES } from '../lib/messages'
import { BrandGlyph, NavIcon, SignOutIcon } from './NavIcon'

export function AppShell() {
  const { session, roles, signOut } = useAuth()
  const { pathname } = useLocation()
  const trail = breadcrumbs(pathname)
  const mainRef = useRef<HTMLElement>(null)
  const email = session?.email ?? ''

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
        <h1 className="shell__brand">
          <BrandGlyph />
          SolarOps
        </h1>
        <nav aria-label="Hlavná navigácia" className="side-nav">
          {menuGroups(roles).map((section) => {
            const labelId = section.group ? `nav-group-${section.group}` : undefined
            return (
              <div key={section.group ?? 'top'} className="side-nav__section">
                {section.group && (
                  <p id={labelId} className="side-nav__label">
                    {section.group}
                  </p>
                )}
                <ul aria-labelledby={labelId}>
                  {section.items.map((item) => (
                    <li key={item.path}>
                      <NavLink to={item.path} end={item.path === '/'} className="side-nav__link">
                        <NavIcon path={item.path} />
                        <span>{item.label}</span>
                      </NavLink>
                    </li>
                  ))}
                </ul>
              </div>
            )
          })}
        </nav>
        <div className="shell__user">
          <span className="shell__avatar" aria-hidden="true">
            {email.charAt(0).toUpperCase()}
          </span>
          <span className="shell__email" title={email}>
            {email}
          </span>
          <button type="button" className="icon-btn" onClick={signOut} aria-label="Odhlásiť" title="Odhlásiť">
            <SignOutIcon />
          </button>
        </div>
      </aside>
      <main id="main-content" ref={mainRef} tabIndex={-1} className="shell__main">
        <div className="shell__content">
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
        </div>
      </main>
    </div>
  )
}
