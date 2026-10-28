import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { breadcrumbs, menuItems } from '../auth/navigation'

export function AppShell() {
  const { session, roles, signOut } = useAuth()
  const { pathname } = useLocation()
  const trail = breadcrumbs(pathname)

  return (
    <div className="shell">
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
          <button type="button" onClick={signOut}>
            Odhlásiť
          </button>
        </p>
      </aside>
      <main className="shell__main">
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
