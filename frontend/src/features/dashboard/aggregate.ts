import { addDays, toDateString } from '../monitoring/periods'
import type { Granularity, ProductionResponse } from '../monitoring/productionApi'

export type Range = '14d' | '30d' | '12m'

export interface RangeQuery {
  from: string
  to: string
  granularity: Granularity
}

/** Dates are local calendar dates; `to` is exclusive, so today is included. */
export function rangeQuery(range: Range, now: Date): RangeQuery {
  const today = toDateString(now)
  if (range === '12m') {
    const start = new Date(now.getFullYear(), now.getMonth() - 11, 1)
    return { from: toDateString(start), to: addDays(today, 1), granularity: 'month' }
  }
  const days = range === '14d' ? 14 : 30
  return { from: addDays(today, 1 - days), to: addDays(today, 1), granularity: 'day' }
}

export interface Bar {
  key: string
  /** ISO period start (local date part is enough for labels). */
  start: string
  kwh: number
}

/** Sums the production of several installations per period, sorted by time. */
export function sumProduction(responses: readonly ProductionResponse[]): Bar[] {
  const totals = new Map<string, number>()
  for (const response of responses) {
    for (const point of response.points) {
      totals.set(point.periodStart, (totals.get(point.periodStart) ?? 0) + point.energyKwh)
    }
  }
  return [...totals.entries()]
    .sort(([a], [b]) => Date.parse(a) - Date.parse(b))
    .map(([start, kwh]) => ({ key: start, start, kwh }))
}

/** "22." for days, "nov" for months. */
export function barLabel(start: string, range: Range): string {
  const date = new Date(start)
  return range === '12m'
    ? new Intl.DateTimeFormat('sk-SK', { month: 'short' }).format(date)
    : `${date.getDate()}.`
}
