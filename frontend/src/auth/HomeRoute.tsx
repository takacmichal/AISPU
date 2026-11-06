import { Navigate } from 'react-router-dom'
import Dashboard from '../pages/Dashboard'
import { useAuth } from './AuthContext'
import { HOME_PATH, canAccess, menuItems, NAVIGATION } from './navigation'

/**
 * Home page per role: the company dashboard for roles that may open it,
 * otherwise the first section the role may open (e.g. customers land on their plants).
 */
export function HomeRoute() {
  const { roles } = useAuth()
  const dashboard = NAVIGATION.find((item) => item.path === HOME_PATH)
  if (!dashboard || canAccess(dashboard, roles)) return <Dashboard />
  const first = menuItems(roles).find((item) => item.path !== HOME_PATH)
  return <Navigate to={first?.path ?? '/forbidden'} replace />
}
