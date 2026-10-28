export const ROLES = ['Admin', 'Sales', 'Designer', 'Technician', 'Operator', 'Customer'] as const
export type Role = (typeof ROLES)[number]

export interface NavItem {
  path: string
  label: string
  /** Roles allowed to see and open the route. Empty = any signed-in user. */
  roles: readonly Role[]
  /** Path of the parent item used for breadcrumbs. */
  parent?: string
  /** Items with `menu: false` appear only in breadcrumbs. */
  menu?: boolean
}

/** Single source of truth for menu, route guards and breadcrumbs. */
export const NAVIGATION: readonly NavItem[] = [
  { path: '/', label: 'Prehľad', roles: [] },
  { path: '/customers', label: 'Zákazníci', roles: ['Admin', 'Sales'] },
  { path: '/customers/new', label: 'Nový zákazník', roles: ['Admin', 'Sales'], parent: '/customers', menu: false },
  { path: '/catalog', label: 'Katalóg', roles: ['Admin', 'Sales', 'Designer'] },
  { path: '/quotes', label: 'Ponuky', roles: ['Admin', 'Sales', 'Designer'] },
  { path: '/contracts', label: 'Zmluvy', roles: ['Admin', 'Sales'] },
  { path: '/installations', label: 'Inštalácie', roles: ['Admin', 'Designer', 'Technician', 'Operator'] },
  { path: '/tickets', label: 'Servisné tikety', roles: ['Admin', 'Technician', 'Operator'] },
  { path: '/my-plants', label: 'Moje elektrárne', roles: ['Customer'] },
  { path: '/admin', label: 'Administrácia', roles: ['Admin'], menu: false },
  { path: '/admin/users', label: 'Používatelia a roly', roles: ['Admin'], parent: '/admin' },
]

export function canAccess(item: NavItem, userRoles: readonly string[]): boolean {
  return item.roles.length === 0 || item.roles.some((role) => userRoles.includes(role))
}

export function menuItems(userRoles: readonly string[]): NavItem[] {
  return NAVIGATION.filter((item) => item.menu !== false && canAccess(item, userRoles))
}

export function findItem(path: string): NavItem | undefined {
  return NAVIGATION.find((item) => item.path === path)
}

/** Exact match, otherwise the longest item whose path is a parent of `path` (so /customers/123 inherits /customers). */
export function matchItem(path: string): NavItem | undefined {
  const exact = findItem(path)
  if (exact) return exact
  return NAVIGATION.filter((item) => item.path !== '/' && path.startsWith(item.path + '/')).sort(
    (a, b) => b.path.length - a.path.length,
  )[0]
}

/** Breadcrumb trail for a path: Prehľad › parent › item. */
export function breadcrumbs(path: string): NavItem[] {
  const trail: NavItem[] = []
  let current = matchItem(path)
  while (current) {
    trail.unshift(current)
    current = current.parent ? findItem(current.parent) : undefined
  }
  const home = findItem('/')
  if (home && trail[0] !== home) trail.unshift(home)
  return trail
}
