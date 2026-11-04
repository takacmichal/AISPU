export const ROLES = ['Admin', 'Sales', 'Designer', 'Technician', 'Operator', 'Customer'] as const
export type Role = (typeof ROLES)[number]

/** Slovak role names for the UI (user menu, admin). */
export const ROLE_LABELS: Record<string, string> = {
  Admin: 'Administrátor',
  Sales: 'Obchod',
  Designer: 'Projektant',
  Technician: 'Technik',
  Operator: 'Operátor',
  Customer: 'Zákazník',
}

/** The role shown next to the user's name when they have several (most privileged first). */
export function primaryRole(userRoles: readonly string[]): string | undefined {
  return ROLES.find((role) => userRoles.includes(role)) ?? userRoles[0]
}

/** Dashboard of signed-in users. `/` belongs to the public landing page. */
export const HOME_PATH = '/app'

/** Sidebar section labels in display order (wireframe); items without a group are shown on top. */
export const NAV_GROUPS = ['Obchod', 'Realizácia', 'Servis', 'Systém'] as const
export type NavGroup = (typeof NAV_GROUPS)[number]

export interface NavItem {
  path: string
  label: string
  /** Roles allowed to see and open the route. Empty = any signed-in user. */
  roles: readonly Role[]
  /** Path of the parent item used for breadcrumbs. */
  parent?: string
  /** Items with `menu: false` appear only in breadcrumbs. */
  menu?: boolean
  /** Sidebar section; omitted for top-level items such as Prehľad. */
  group?: NavGroup
}

/** Single source of truth for menu, route guards and breadcrumbs. */
export const NAVIGATION: readonly NavItem[] = [
  { path: HOME_PATH, label: 'Prehľad', roles: [] },
  { path: '/my-plants', label: 'Moje elektrárne', roles: ['Customer'] },
  { path: '/customers', label: 'Zákazníci', roles: ['Admin', 'Sales'], group: 'Obchod' },
  { path: '/customers/new', label: 'Nový zákazník', roles: ['Admin', 'Sales'], parent: '/customers', menu: false },
  { path: '/designs', label: 'Návrhy FVE', roles: ['Admin', 'Sales', 'Designer'], group: 'Obchod' },
  { path: '/quotes', label: 'Ponuky', roles: ['Admin', 'Sales', 'Designer'], group: 'Obchod' },
  { path: '/contracts', label: 'Zmluvy', roles: ['Admin', 'Sales'], group: 'Obchod' },
  { path: '/installations', label: 'Inštalácie', roles: ['Admin', 'Designer', 'Technician', 'Operator'], group: 'Realizácia' },
  { path: '/monitoring', label: 'Monitoring výroby', roles: ['Admin', 'Operator', 'Technician', 'Designer'], group: 'Realizácia' },
  { path: '/tickets', label: 'Servisné tikety', roles: ['Admin', 'Technician', 'Operator', 'Customer'], group: 'Servis' },
  { path: '/tickets/new', label: 'Nahlásiť poruchu', roles: ['Admin', 'Technician', 'Operator', 'Customer'], parent: '/tickets', menu: false },
  { path: '/admin', label: 'Administrácia', roles: ['Admin'], menu: false },
  { path: '/admin/users', label: 'Používatelia a roly', roles: ['Admin'], parent: '/admin', group: 'Systém' },
  { path: '/catalog', label: 'Katalóg komponentov', roles: ['Admin', 'Sales', 'Designer'], group: 'Systém' },
]

export function canAccess(item: NavItem, userRoles: readonly string[]): boolean {
  return item.roles.length === 0 || item.roles.some((role) => userRoles.includes(role))
}

export function menuItems(userRoles: readonly string[]): NavItem[] {
  return NAVIGATION.filter((item) => item.menu !== false && canAccess(item, userRoles))
}

export interface MenuSection {
  group?: NavGroup
  items: NavItem[]
}

/** Menu items split into sidebar sections: ungrouped items first, then NAV_GROUPS order; empty sections are dropped. */
export function menuGroups(userRoles: readonly string[]): MenuSection[] {
  const items = menuItems(userRoles)
  const sections: MenuSection[] = [{ items: items.filter((item) => !item.group) }]
  for (const group of NAV_GROUPS) sections.push({ group, items: items.filter((item) => item.group === group) })
  return sections.filter((section) => section.items.length > 0)
}

export function findItem(path: string): NavItem | undefined {
  return NAVIGATION.find((item) => item.path === path)
}

/** Exact match, otherwise the longest item whose path is a parent of `path` (so /customers/123 inherits /customers). */
export function matchItem(path: string): NavItem | undefined {
  const exact = findItem(path)
  if (exact) return exact
  return NAVIGATION.filter((item) => item.path !== HOME_PATH && path.startsWith(item.path + '/')).sort(
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
  const home = findItem(HOME_PATH)
  if (home && trail[0] !== home) trail.unshift(home)
  return trail
}
