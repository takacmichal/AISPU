import { formatNumber } from '../../lib/format'

/** 12 345 kWh (non-breaking space before the unit). */
export function formatKwh(value: number | string | null | undefined): string {
  const text = formatNumber(value, 0)
  return text === '–' ? text : `${text}\u00a0kWh`
}

export function formatKw(value: number | string | null | undefined): string {
  const text = formatNumber(value)
  return text === '–' ? text : `${text}\u00a0kW`
}
