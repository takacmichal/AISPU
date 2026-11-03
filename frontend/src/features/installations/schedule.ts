import type { components } from '../../api/generated/schema'

type Installation = components['schemas']['InstallationResponse']

/** Dates are plain `YYYY-MM-DD` strings, which compare correctly as text (no timezone shifts). */
export function overlaps(aStart: string, aEnd: string, bStart: string, bEnd: string): boolean {
  return aStart <= bEnd && bStart <= aEnd
}

/** Other installations scheduled in the same window that share at least one of the selected technicians. */
export function findCollisions(
  all: readonly Installation[],
  currentId: string,
  start: string,
  end: string,
  technicianIds: readonly string[],
): Installation[] {
  if (!start || !end || technicianIds.length === 0) return []
  return all.filter(
    (other) =>
      other.id !== currentId &&
      other.scheduledStart &&
      other.scheduledEnd &&
      overlaps(start, end, other.scheduledStart, other.scheduledEnd) &&
      other.technicianIds.some((id) => technicianIds.includes(id)),
  )
}
