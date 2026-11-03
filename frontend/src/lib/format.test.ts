import { describe, expect, it } from 'vitest'
import { formatCurrency, formatDate, formatDateTime, formatKwp, formatNumber, formatRelative } from './format'

const plain = (s: string) => s.replace(/[\u00a0\u202f]/g, ' ')

describe('format', () => {
  it('formats dates in sk-SK and falls back to a dash', () => {
    expect(plain(formatDate('2026-11-03T10:00:00Z'))).toBe('3. 11. 2026')
    expect(formatDate(null)).toBe('–')
    expect(formatDate('not a date')).toBe('–')
  })

  it('formats date and time', () => {
    expect(plain(formatDateTime(new Date(2026, 10, 3, 14, 5)))).toMatch(/3\. 11\. 2026,? 14:05/)
  })

  it('formats numbers with a decimal comma and thin grouping', () => {
    expect(plain(formatNumber(1234.5))).toBe('1 234,5')
    expect(formatNumber(undefined)).toBe('–')
  })

  it('formats EUR by default', () => {
    expect(plain(formatCurrency(1234.5))).toBe('1 234,50 €')
    expect(plain(formatCurrency('10', 'EUR'))).toBe('10,00 €')
    expect(formatCurrency(null)).toBe('–')
  })

  it('formats kWp', () => {
    expect(plain(formatKwp(9.8))).toBe('9,8 kWp')
    expect(formatKwp(null)).toBe('–')
  })

  it('formats relative time in Slovak', () => {
    const now = new Date('2026-11-03T12:00:00Z')
    expect(formatRelative('2026-11-03T11:55:00Z', now)).toBe('pred 5 minútami')
    expect(formatRelative('2026-11-02T12:00:00Z', now)).toBe('včera')
    expect(formatRelative('2026-11-03T11:59:50Z', now)).toBe('teraz')
    expect(formatRelative(null, now)).toBe('–')
  })
})
