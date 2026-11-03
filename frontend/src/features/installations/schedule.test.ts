import { describe, expect, it } from 'vitest'
import type { components } from '../../api/generated/schema'
import { findCollisions, overlaps } from './schedule'

type I = components['schemas']['InstallationResponse']
const inst = (over: Partial<I>) => ({ id: 'x', scheduledStart: null, scheduledEnd: null, technicianIds: [], ...over }) as I

describe('schedule collisions', () => {
  it('detects overlapping windows including shared boundary days', () => {
    expect(overlaps('2026-11-01', '2026-11-03', '2026-11-03', '2026-11-05')).toBe(true)
    expect(overlaps('2026-11-01', '2026-11-02', '2026-11-03', '2026-11-05')).toBe(false)
  })

  it('reports only other installations sharing a technician in the window', () => {
    const all = [
      inst({ id: 'a', scheduledStart: '2026-11-02', scheduledEnd: '2026-11-04', technicianIds: ['t1'] }),
      inst({ id: 'b', scheduledStart: '2026-11-02', scheduledEnd: '2026-11-04', technicianIds: ['t2'] }),
      inst({ id: 'me', scheduledStart: '2026-11-02', scheduledEnd: '2026-11-04', technicianIds: ['t1'] }),
    ]
    expect(findCollisions(all, 'me', '2026-11-03', '2026-11-05', ['t1']).map((i) => i.id)).toEqual(['a'])
    expect(findCollisions(all, 'me', '', '', ['t1'])).toEqual([])
  })
})
