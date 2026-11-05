import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'
import { HOME_PATH, canAccess, matchItem } from './navigation'

/** Redirects anonymous users to login (with returnUrl) and users without a matching role to 403. */
export function RequireAuth() {
  const { session, roles, signOutReason } = useAuth()
  const location = useLocation()

  if (!session) {
    // After an explicit logout the user does not return to the page they just left.
    if (signOutReason === 'logout') return <Navigate to="/login" replace />
    const returnUrl = location.pathname + location.search + location.hash
    return <Navigate to={`/login?returnUrl=${encodeURIComponent(returnUrl)}`} replace />
  }

  // The home path is decided by HomeRoute (dashboard or the first section the role may open).
  const item = location.pathname === HOME_PATH ? undefined : matchItem(location.pathname)
  // The 403 page names the section and the roles that may open it.
  if (item && !canAccess(item, roles)) return <Navigate to="/forbidden" replace state={{ from: location.pathname }} />

  return <Outlet />
}
