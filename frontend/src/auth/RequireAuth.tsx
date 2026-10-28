import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'
import { canAccess, findItem } from './navigation'

/** Redirects anonymous users to login and users without a matching role to 403. */
export function RequireAuth() {
  const { session, roles } = useAuth()
  const location = useLocation()

  if (!session) return <Navigate to="/login" replace state={{ from: location.pathname }} />

  const item = findItem(location.pathname)
  if (item && !canAccess(item, roles)) return <Navigate to="/forbidden" replace />

  return <Outlet />
}
