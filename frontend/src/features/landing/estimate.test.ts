import { describe, expect, it } from 'vitest'
import { KWP_RANGE, REGIONS, estimateYield, findRegion } from './estimate'

describe('estimateYield', () => {
  it('multiplies power by the regional yield for a south roof', () => {
    // 9,6 kWp × 1 030 kWh/kWp (Prešovský kraj)
    expect(estimateYield({ kwp: 9.6, orientation: 'south', region: 'PO' }).yearlyKwh).toBeCloseTo(9888)
  })

  it('lowers the yield of an east-west roof to 86 %', () => {
    const south = estimateYield({ kwp: 10, orientation: 'south', region: 'BA' }).yearlyKwh
    const eastWest = estimateYield({ kwp: 10, orientation: 'east-west', region: 'BA' }).yearlyKwh
    expect(south).toBeCloseTo(11000)
    expect(eastWest).toBeCloseTo(9460)
  })

  it('values 60 % self-consumption at 0,20 € per kWh', () => {
    expect(estimateYield({ kwp: 10, orientation: 'south', region: 'BA' }).savingEur).toBeCloseTo(1320)
  })

  it('clamps the power to the slider range', () => {
    const below = estimateYield({ kwp: 0, orientation: 'south', region: 'BA' })
    const above = estimateYield({ kwp: 500, orientation: 'south', region: 'BA' })
    expect(below.yearlyKwh).toBeCloseTo(KWP_RANGE.min * 1100)
    expect(above.yearlyKwh).toBeCloseTo(KWP_RANGE.max * 1100)
  })

  it('falls back to the default region for an unknown code', () => {
    expect(findRegion('XX').id).toBe('PO')
  })

  it('lists all eight Slovak regions', () => {
    expect(REGIONS).toHaveLength(8)
    expect(new Set(REGIONS.map((r) => r.id)).size).toBe(8)
  })
})
