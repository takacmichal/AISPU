import { describe, expect, it } from 'vitest'
import type { ChartPoint } from './kpis'
import { underPeriods, underproductionTicketHref } from './underproduction'

const p = (day: number, actual: number | null, predicted: number | null): ChartPoint => ({ start: day, actual, predicted, peakKw: null })
const label = (start: number) => `${start}. 11. 2026`

describe('underPeriods', () => {
  it('returns runs below 85 % of the prediction with their sums', () => {
    const periods = underPeriods([p(1, 50, 50), p(2, 30, 50), p(3, 40, 50), p(4, 50, 50), p(5, 10, 50)], label)
    expect(periods).toEqual([
      { start: 1, end: 2, from: '2. 11. 2026', to: '3. 11. 2026', ratio: 0.7, actualKwh: 70, predictedKwh: 100 },
      { start: 4, end: 4, from: '5. 11. 2026', to: '5. 11. 2026', ratio: 0.2, actualKwh: 10, predictedKwh: 50 },
    ])
  })

  it('ignores buckets without a prediction', () => {
    expect(underPeriods([p(1, 1, null), p(2, 0, 0)], label)).toEqual([])
  })
})

describe('underproductionTicketHref', () => {
  it('prefills installation, title and description of a new ticket', () => {
    const [period] = underPeriods([p(2, 30, 50), p(3, 40, 50)], label)
    const url = new URL(underproductionTicketHref('i1', 'FVE Nitra', period), 'http://x')
    expect(url.pathname).toBe('/tickets/new')
    expect(url.searchParams.get('installationId')).toBe('i1')
    expect(url.searchParams.get('title')).toBe('Podvýroba 2. 11. 2026 – 3. 11. 2026')
    expect(url.searchParams.get('description')).toContain('FVE Nitra')
    expect(url.searchParams.get('description')).toContain('70 % očakávanej (100 kWh)')
  })
})
