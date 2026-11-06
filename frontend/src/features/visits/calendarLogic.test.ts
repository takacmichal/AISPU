import { describe, expect, it } from 'vitest'
import { dayKey, findCollisions, parseWeek, startOfWeek, weekDays, type TechnicianVisit } from './calendarLogic'

const visit = (visitId: string, technicianId: string, start: string, end: string, state = 'Planned') =>
  ({ visitId, technicianId, start: `2026-11-05T${start}:00.000Z`, end: `2026-11-05T${end}:00.000Z`, state }) as TechnicianVisit

describe('week helpers', () => {
  it('starts the week on Monday, also for a Sunday', () => {
    expect(dayKey(startOfWeek(new Date(2026, 10, 5)))).toBe('2026-11-02')
    expect(dayKey(startOfWeek(new Date(2026, 10, 8)))).toBe('2026-11-02')
    expect(dayKey(startOfWeek(new Date(2026, 10, 2)))).toBe('2026-11-02')
  })

  it('lists seven days across a month boundary', () => {
    const days = weekDays(new Date(2026, 10, 30)).map(dayKey)
    expect(days).toHaveLength(7)
    expect(days[0]).toBe('2026-11-30')
    expect(days[6]).toBe('2026-12-06')
  })

  it('reads the week from the URL and falls back on invalid values', () => {
    const now = new Date(2026, 10, 5)
    expect(dayKey(parseWeek('2026-12-09', now))).toBe('2026-12-07')
    for (const invalid of [null, '', 'abc', '2026-13-01', '2026-02-31']) {
      expect(dayKey(parseWeek(invalid, now))).toBe('2026-11-02')
    }
  })
})

describe('findCollisions', () => {
  it('flags overlapping visits of one technician', () => {
    const result = findCollisions([visit('a', 't1', '08:00', '10:00'), visit('b', 't1', '09:00', '11:00'), visit('c', 't1', '12:00', '13:00')])
    expect([...result].sort()).toEqual(['a', 'b'])
  })

  it('does not flag different technicians or back-to-back visits', () => {
    expect(findCollisions([visit('a', 't1', '08:00', '10:00'), visit('b', 't2', '09:00', '11:00')]).size).toBe(0)
    expect(findCollisions([visit('a', 't1', '08:00', '10:00'), visit('b', 't1', '10:00', '11:00')]).size).toBe(0)
  })

  it('ignores finished and cancelled visits', () => {
    expect(findCollisions([visit('a', 't1', '08:00', '10:00', 'Done'), visit('b', 't1', '09:00', '11:00')]).size).toBe(0)
    expect(findCollisions([visit('a', 't1', '08:00', '10:00', 'Cancelled'), visit('b', 't1', '09:00', '11:00')]).size).toBe(0)
  })
})
