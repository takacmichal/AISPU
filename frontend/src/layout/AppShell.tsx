import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useEffect, useRef, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { HOME_PATH, ROLE_LABELS, breadcrumbs, menuGroups, menuItems, primaryRole } from '../auth/navigation'
import { MESSAGES } from '../lib/messages'
import { BrandGlyph, CollapseIcon, HelpIcon, MenuIcon, NavIcon, SearchIcon } from './NavIcon'
import { NotificationBell } from './NotificationBell'
import { SideProductionCard } from './SideProductionCard'
import { useNavCounts } from './useShellData'
import { canAccess, findItem } from '../auth/navigation'
import { QuickJump } from './QuickJump'
import { UserMenu } from './UserMenu'
import { LAYOUT_TEXTS as T } from './texts'

const COLLAPSED_KEY = 'solarops.sidebar.collapsed'
const SIDEBAR_ID = 'app-sidebar'
const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.userAgent)

/**
 * App shell from the wireframe: sidebar with role-based groups (collapsible to icons, a drawer on mobile),
 * top bar with quick jump (Ctrl/⌘+K) and the user menu, breadcrumbs above the page.
 */
export function AppShell() {
  const { session, roles, signOut } = useAuth()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const trail = breadcrumbs(pathname)
  const mainRef = useRef<HTMLElement>(null)
  const email = session?.email ?? ''
  const role = primaryRole(roles)
  const roleLabel = role ? (ROLE_LABELS[role] ?? role) : undefined

  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSED_KEY) === '1')
  // The mobile drawer is open only for the page it was opened on, so navigating closes it without an effect.
  const [navOpenPath, setNavOpenPath] = useState<string | null>(null)
  const navOpen = navOpenPath === pathname
  const closeNav = () => setNavOpenPath(null)
  const [quickJump, setQuickJump] = useState(false)
  const monitoringItem = findItem('/monitoring')
  const canMonitor = monitoringItem !== undefined && canAccess(monitoringItem, roles)
  const counts = useNavCounts(pathname === HOME_PATH && roles.length > 0 && !roles.every((r) => r === 'Customer'))

  function toggleCollapsed() {
    setCollapsed((value) => {
      localStorage.setItem(COLLAPSED_KEY, value ? '0' : '1')
      return !value
    })
  }

  // Move focus to the content after route changes so keyboard and screen reader users start at the new page.
  const first = useRef(true)
  useEffect(() => {
    if (first.current) {
      first.current = false
      return
    }
    mainRef.current?.focus()
  }, [pathname])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        setQuickJump(true)
      } else if (event.key === 'Escape') {
        setNavOpenPath(null)
      }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const classes = ['shell', collapsed && 'shell--collapsed', navOpen && 'shell--nav-open'].filter(Boolean).join(' ')

  return (
    <div className={classes}>
      <a href="#main-content" className="skip-link">
        {MESSAGES.skipToContent}
      </a>
      <aside id={SIDEBAR_ID} className="shell__side">
        <div className="shell__brand">
          <Link to={HOME_PATH} className="shell__brand-link">
            <BrandGlyph />
            <span className="shell__brand-text">
              <span className="shell__brand-name">SolarOps</span>
              <span className="shell__brand-sub">{T.brandSubtitle(roles.length > 0 && roles.every((r) => r === 'Customer'))}</span>
            </span>
          </Link>
          <button
            type="button"
            className="icon-btn shell__collapse"
            aria-controls={SIDEBAR_ID}
            aria-expanded={!collapsed}
            aria-label={collapsed ? T.expand : T.collapse}
            title={collapsed ? T.expand : T.collapse}
            onClick={toggleCollapsed}
          >
            <CollapseIcon />
          </button>
        </div>
        <nav aria-label={T.mainNav} className="side-nav">
          {menuGroups(roles).map((section) => {
            const groupLabel = section.group ?? T.topGroup
            const labelId = `nav-group-${groupLabel}`
            return (
              <div key={section.group ?? 'top'} className="side-nav__section">
                <p id={labelId} className="side-nav__label">
                  {groupLabel}
                </p>
                <ul aria-labelledby={labelId}>
                  {section.items.map((item) => (
                    <li key={item.path}>
                      <NavLink
                        to={item.path}
                        end={item.path === HOME_PATH}
                        className="side-nav__link"
                        title={collapsed ? item.label : undefined}
                      >
                        <NavIcon path={item.path} />
                        <span className="side-nav__text">{item.label}</span>
                        {counts[item.path] !== undefined && counts[item.path] > 0 && (
                          <span className="side-nav__count" aria-hidden="true">
                            {counts[item.path]}
                          </span>
                        )}
                      </NavLink>
                    </li>
                  ))}
                </ul>
              </div>
            )
          })}
        </nav>
        {canMonitor && !collapsed && <SideProductionCard live={pathname === HOME_PATH} />}
      </aside>
      {navOpen && <div className="shell__backdrop" aria-hidden="true" onClick={closeNav} />}
      <div className="shell__body">
        <header className="topbar">
          <button
            type="button"
            className="icon-btn topbar__burger"
            aria-controls={SIDEBAR_ID}
            aria-expanded={navOpen}
            aria-label={T.openMenu}
            onClick={() => setNavOpenPath(navOpen ? null : pathname)}
          >
            <MenuIcon />
          </button>
          <button type="button" className="topbar__jump" onClick={() => setQuickJump(true)}>
            <SearchIcon />
            <span className="topbar__jump-text">{T.quickJumpButton}</span>
            <kbd className="topbar__kbd">{isMac ? '⌘K' : 'Ctrl K'}</kbd>
          </button>
          <div className="topbar__spacer" />
          <a
            className="topbar__round"
            href="https://github.com/takacmichal/AISPU#readme"
            target="_blank"
            rel="noreferrer"
            aria-label={T.help}
            title={T.help}
          >
            <HelpIcon />
          </a>
          <NotificationBell />
          <UserMenu name={email} role={roleLabel} onSignOut={signOut} />
        </header>
        <main id="main-content" ref={mainRef} tabIndex={-1} className="shell__main">
          <div className="shell__content">
            {trail.length > 1 && (
              <nav aria-label={T.breadcrumbs} className="breadcrumbs">
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
      {quickJump && (
        <QuickJump
          items={menuItems(roles)}
          onClose={() => setQuickJump(false)}
          onSelect={(path) => {
            setQuickJump(false)
            navigate(path)
          }}
        />
      )}
    </div>
  )
}
