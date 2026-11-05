import { describe, expect, it } from 'vitest'
import { addHours, alarmChartQuery, alarmPeriod, alarmTicketHref, filterAlarms, kwhText, ratioPercent } from './alarmLogic'

const alarm = { start: '2026-06-21T10:00:00Z', lastUnderproductionAt: '2026-06-21T14:00:00Z' }

describe('alarm logic', () => {
  it('starts the period one hour before the end-labelled start', () => {
    expect(alarmPeriod(alarm)).toEqual({ from: '2026-06-21T09:00:00.000Z', to: alarm.lastUnderproductionAt })
  })

  it('shows hours with context around a short event', () => {
    expect(alarmChartQuery(alarm)).toEqual({
      from: '2026-06-21T03:00:00.000Z',
      to: '2026-06-21T20:00:00.000Z',
      granularity: 'hour',
    })
  })

  it('shows days for an event longer than two weeks', () => {
    const long = { ...alarm, lastUnderproductionAt: addHours(alarm.start, 24 * 20) }
    expect(alarmChartQuery(long).granularity).toBe('day')
  })

  it('filters by severity and state together', () => {
    const list = [
      { id: 1, severity: 'High', ongoing: true },
      { id: 2, severity: 'High', ongoing: false },
      { id: 3, severity: 'Low', ongoing: true },
    ] as const
    expect(filterAlarms(list, 'all', 'all').map((a) => a.id)).toEqual([1, 2, 3])
    expect(filterAlarms(list, 'High', 'all').map((a) => a.id)).toEqual([1, 2])
    expect(filterAlarms(list, 'all', 'ongoing').map((a) => a.id)).toEqual([1, 3])
    expect(filterAlarms(list, 'High', 'ended').map((a) => a.id)).toEqual([2])
  })

  it('accepts decimals sent as strings', () => {
    expect(ratioPercent('0.62')).toBe(62)
    expect(ratioPercent(0.5)).toBe(50)
    expect(kwhText('2.4')).toBe('2,4 kWh')
  })

  it('prefills a ticket with the installation and the numbers of the alarm', () => {
    const href = alarmTicketHref({ ...alarm, installationId: 'i1', installationName: 'FVE Nitra', medianRatio: 0.6, estimatedLostKwh: 2.4 })
    const params = new URLSearchParams(href.split('?')[1])
    expect(href.startsWith('/tickets/new?')).toBe(true)
    expect(params.get('installationId')).toBe('i1')
    expect(params.get('title')).toMatch(/^Podvýroba /)
    expect(params.get('description')).toContain('FVE Nitra')
    expect(params.get('description')).toContain('60 %')
    expect(params.get('description')).toContain('2,4 kWh')
  })
})
