import type { Address } from '../tickets/visitsApi'

export const RANGES = ['today', 'tomorrow', 'week'] as const
export type Range = (typeof RANGES)[number]

const startOfDay = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate())
const plusDays = (date: Date, days: number) => new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)

/** Local-time window [from, to) of a tab: today, tomorrow, or the next 7 days starting today. */
export function rangeWindow(range: Range, now: Date): { from: Date; to: Date } {
  const today = startOfDay(now)
  if (range === 'tomorrow') return { from: plusDays(today, 1), to: plusDays(today, 2) }
  if (range === 'week') return { from: today, to: plusDays(today, 7) }
  return { from: today, to: plusDays(today, 1) }
}

export const formatAddress = (a: Address) => `${a.street}, ${a.postalCode} ${a.city}`

/** Route planner link; opens the maps app on phones. */
export const navigationHref = (a: Address) => `https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(formatAddress(a))}`

/** `tel:` link without spaces, which some dialers do not accept. */
export const phoneHref = (phone: string) => `tel:${phone.replace(/[^\d+]/g, '')}`

/** Value for `<input type="datetime-local">` in local time. */
export function toLocalInput(date: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${p(date.getMonth() + 1)}-${p(date.getDate())}T${p(date.getHours())}:${p(date.getMinutes())}`
}

export const timeOf = (iso: string) => new Date(iso).toLocaleTimeString('sk-SK', { hour: '2-digit', minute: '2-digit' })
export const dayOf = (iso: string) => new Date(iso).toLocaleDateString('sk-SK', { weekday: 'long', day: 'numeric', month: 'numeric' })
