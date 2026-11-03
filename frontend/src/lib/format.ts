/** Central sk-SK formatters. Use these instead of calling toLocaleString or Intl directly in components. */
export const LOCALE = 'sk-SK'
export const DEFAULT_CURRENCY = 'EUR'
const EMPTY = '–'

type DateInput = string | number | Date | null | undefined

function toDate(value: DateInput): Date | null {
  if (value === null || value === undefined || value === '') return null
  const date = value instanceof Date ? value : new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

/** 3. 11. 2026 */
export function formatDate(value: DateInput): string {
  const date = toDate(value)
  return date ? date.toLocaleDateString(LOCALE) : EMPTY
}

/** 3. 11. 2026, 14:05 */
export function formatDateTime(value: DateInput): string {
  const date = toDate(value)
  return date ? date.toLocaleString(LOCALE, { dateStyle: 'short', timeStyle: 'short' }) : EMPTY
}

/** 1 234,5 */
export function formatNumber(value: number | string | null | undefined, maximumFractionDigits = 2): string {
  if (value === null || value === undefined || value === '' || Number.isNaN(Number(value))) return EMPTY
  return new Intl.NumberFormat(LOCALE, { maximumFractionDigits }).format(Number(value))
}

/** 1 234,50 € */
export function formatCurrency(amount: number | string | null | undefined, currency: string = DEFAULT_CURRENCY): string {
  if (amount === null || amount === undefined || amount === '' || Number.isNaN(Number(amount))) return EMPTY
  return new Intl.NumberFormat(LOCALE, { style: 'currency', currency }).format(Number(amount))
}

/** 9,8 kWp */
export function formatKwp(value: number | string | null | undefined): string {
  const text = formatNumber(value)
  return text === EMPTY ? EMPTY : `${text}\u00a0kWp`
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
]

/** pred 5 minútami, včera, pred 2 týždňami (`now` is injectable for tests). */
export function formatRelative(value: DateInput, now: Date = new Date()): string {
  const date = toDate(value)
  if (!date) return EMPTY
  const seconds = Math.round((date.getTime() - now.getTime()) / 1000)
  const rtf = new Intl.RelativeTimeFormat(LOCALE, { numeric: 'auto' })
  for (const [unit, size] of RELATIVE_UNITS) {
    if (Math.abs(seconds) >= size) return rtf.format(Math.trunc(seconds / size), unit)
  }
  return rtf.format(0, 'second')
}
