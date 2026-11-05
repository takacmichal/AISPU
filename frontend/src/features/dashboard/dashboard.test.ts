import { describe, expect, it } from 'vitest'
import type { ProductionResponse } from '../monitoring/productionApi'
import type { TicketListItem } from '../tickets/ticketsApi'
import { fromTicket } from './alarms'
import { rangeQuery, sumProduction } from './aggregate'

const response = (points: [string, number][]): ProductionResponse => ({
  installationId: 'i',
  from: '',
  to: '',
  granularity: 'day',
  timeZone: 'Europe/Bratislava',
  totalEnergyKwh: 0,
  peakPowerKw: null,
  points: points.map(([periodStart, energyKwh]) => ({ periodStart, energyKwh, peakPowerKw: 0, sampleCount: 1 })),
})

describe('rangeQuery', () => {
  const now = new Date(2026, 10, 4, 12)
  it('covers the last 14 days including today', () => {
    expect(rangeQuery('14d', now)).toEqual({ from: '2026-10-22', to: '2026-11-05', granularity: 'day' })
  })
  it('uses months for 12 months', () => {
    expect(rangeQuery('12m', now)).toEqual({ from: '2025-12-01', to: '2026-11-05', granularity: 'month' })
  })
})

describe('sumProduction', () => {
  it('sums installations per period and sorts by time', () => {
    const bars = sumProduction([
      response([['2026-11-02T00:00:00Z', 5], ['2026-11-01T00:00:00Z', 2]]),
      response([['2026-11-02T00:00:00Z', 7]]),
    ])
    expect(bars.map((b) => b.kwh)).toEqual([2, 12])
  })
})

describe('fromTicket', () => {
  const ticket = (priority: TicketListItem['priority']) =>
    ({ id: 't1', title: 'Porucha', priority, origin: 'AnomalyDetected', updatedAt: '2026-11-04T08:00:00Z' }) as TicketListItem
  it('maps priority to severity and links to the ticket', () => {
    expect(fromTicket(ticket('High'))).toMatchObject({ severity: 'critical', href: '/tickets/t1', meta: 'Automatická detekcia' })
    expect(fromTicket(ticket('Low')).severity).toBe('info')
  })
})
