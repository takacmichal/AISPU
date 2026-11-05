import type { Granularity } from './productionApi'

export function formatBucket(ms: number, granularity: Granularity, timeZone: string, long = false): string {
  const options: Intl.DateTimeFormatOptions =
    granularity === 'month'
      ? { month: long ? 'long' : 'short', year: 'numeric' }
      : granularity === 'day'
        ? { day: 'numeric', month: 'numeric', ...(long ? { year: 'numeric' } : {}) }
        : { day: 'numeric', month: 'numeric', hour: '2-digit', minute: '2-digit' }
  return new Intl.DateTimeFormat('sk-SK', { ...options, timeZone }).format(ms)
}
