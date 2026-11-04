import type { Granularity } from './productionApi'

export type Preset = 'today' | '7d' | '30d' | 'year' | 'custom'
export const PRESETS: readonly Preset[] = ['today', '7d', '30d', 'year', 'custom']

export interface PeriodState {
  preset: Preset
  /** Custom range, inclusive dates YYYY-MM-DD. */
  from: string
  to: string
  granularity: Granularity | 'auto'
}

export const DEFAULT_PERIOD: PeriodState = { preset: '7d', from: '', to: '', granularity: 'auto' }

export const GRANULARITIES: readonly Granularity[] = ['fiveMinutes', 'hour', 'day', 'month']

/** Server limits in days (ProductionQuery.MaxRange); the comparison endpoint has no 5-minute buckets. */
const MAX_DAYS: Record<Granularity, number> = { fiveMinutes: 31, hour: 366, day: 3660, month: 3660 }
export const MAX_PERIOD_DAYS = 3660
export const EXPORT_MAX_DAYS: Record<Granularity, number> = { fiveMinutes: 366, hour: 3660, day: 3660, month: 3660 }

const DAY_MS = 86_400_000

export function toDateString(date: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${p(date.getMonth() + 1)}-${p(date.getDate())}`
}

function parseDate(value: string): number {
  const [y, m, d] = value.split('-').map(Number)
  return Date.UTC(y, m - 1, d)
}

export function addDays(value: string, days: number): string {
  return new Date(parseDate(value) + days * DAY_MS).toISOString().slice(0, 10)
}

export function daysBetween(from: string, to: string): number {
  return Math.round((parseDate(to) - parseDate(from)) / DAY_MS)
}

export interface ResolvedPeriod {
  /** Inclusive start date. */
  from: string
  /** Exclusive end date (what the API expects). */
  to: string
  days: number
}

/** Converts the UI state into the API range [from, to); returns null for an incomplete or reversed custom range. */
export function resolvePeriod(state: PeriodState, now: Date): ResolvedPeriod | null {
  const today = toDateString(now)
  const tomorrow = addDays(today, 1)
  let from: string
  let to = tomorrow
  switch (state.preset) {
    case 'today':
      from = today
      break
    case '7d':
      from = addDays(today, -6)
      break
    case '30d':
      from = addDays(today, -29)
      break
    case 'year':
      from = addDays(today, -364)
      break
    default:
      if (!state.from || !state.to || state.to < state.from) return null
      from = state.from
      to = addDays(state.to, 1)
  }
  return { from, to, days: daysBetween(from, to) }
}

export function allowedGranularities(days: number, withPrediction = false): Granularity[] {
  return GRANULARITIES.filter((g) => days <= MAX_DAYS[g] && !(withPrediction && g === 'fiveMinutes'))
}

/** Sensible bucket size for a range: hours up to 2 days, days up to a year, months beyond. */
export function autoGranularity(days: number): Granularity {
  if (days <= 2) return 'hour'
  if (days <= 366) return 'day'
  return 'month'
}

/** User choice if the server accepts it for this range, otherwise the automatic one. */
export function effectiveGranularity(state: PeriodState, days: number): Granularity {
  if (state.granularity !== 'auto' && allowedGranularities(days).includes(state.granularity)) return state.granularity
  return autoGranularity(days)
}

export function customRangeError(state: PeriodState): 'missing' | 'reversed' | 'tooLong' | null {
  if (state.preset !== 'custom') return null
  if (!state.from || !state.to) return 'missing'
  if (state.to < state.from) return 'reversed'
  if (daysBetween(state.from, addDays(state.to, 1)) > MAX_PERIOD_DAYS) return 'tooLong'
  return null
}

const PERIOD_KEYS = ['period', 'from', 'to', 'granularity'] as const

export function periodFromParams(params: URLSearchParams): PeriodState {
  const preset = PRESETS.find((p) => p === params.get('period')) ?? DEFAULT_PERIOD.preset
  const granularity = GRANULARITIES.find((g) => g === params.get('granularity')) ?? 'auto'
  return { preset, from: params.get('from') ?? '', to: params.get('to') ?? '', granularity }
}

export function periodToParams(params: URLSearchParams, state: PeriodState): URLSearchParams {
  const next = new URLSearchParams(params)
  for (const key of PERIOD_KEYS) next.delete(key)
  if (state.preset !== DEFAULT_PERIOD.preset) next.set('period', state.preset)
  if (state.preset === 'custom') {
    if (state.from) next.set('from', state.from)
    if (state.to) next.set('to', state.to)
  }
  if (state.granularity !== 'auto') next.set('granularity', state.granularity)
  return next
}
