import type { CalendarVisit } from '../tickets/visitsApi'

export const DAYS_IN_WEEK = 7

/** Monday 00:00 local time of the week containing `date`. */
export function startOfWeek(date: Date): Date {
  const daysSinceMonday = (date.getDay() + 6) % DAYS_IN_WEEK
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() - daysSinceMonday)
}

export const addDays = (date: Date, days: number) => new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)

/** Local date as `YYYY-MM-DD`; used in the URL and to group visits by day. */
export function dayKey(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** Week start from the `?week=` value; anything that is not a real date falls back to the current week. */
export function parseWeek(value: string | null, now: Date): Date {
  const match = value?.match(/^(\d{4})-(\d{2})-(\d{2})$/)
  if (!match) return startOfWeek(now)
  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]))
  return dayKey(date) === value ? startOfWeek(date) : startOfWeek(now)
}

export const weekDays = (monday: Date) => Array.from({ length: DAYS_IN_WEEK }, (_, index) => addDays(monday, index))

export interface TechnicianVisit extends CalendarVisit {
  technicianId: string
}

const isActive = (visit: TechnicianVisit) => visit.state === 'Planned' || visit.state === 'InProgress'

/** Ids of active visits overlapping another active visit of the same technician; touching end and start is no collision. */
export function findCollisions(visits: readonly TechnicianVisit[]): ReadonlySet<string> {
  const active = visits.filter(isActive)
  const colliding = new Set<string>()
  active.forEach((visit, index) => {
    for (const other of active.slice(index + 1)) {
      if (visit.technicianId === other.technicianId && visit.start < other.end && other.start < visit.end) {
        colliding.add(visit.visitId)
        colliding.add(other.visitId)
      }
    }
  })
  return colliding
}
