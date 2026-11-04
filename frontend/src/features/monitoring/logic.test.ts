import { describe, expect, it } from 'vitest'
import { computeKpis, deviationPercent, isUnderproduction, mergeChartPoints } from './kpis'
import {
  allowedGranularities,
  autoGranularity,
  customRangeError,
  effectiveGranularity,
  periodFromParams,
  periodToParams,
  resolvePeriod,
  type PeriodState,
} from './periods'
import type { ComparisonResponse, ProductionResponse } from './productionApi'

const NOW = new Date(2026, 5, 21, 12, 0)
const base: PeriodState = { preset: '7d', from: '', to: '', granularity: 'auto' }

const production: ProductionResponse = {
  installationId: 'i1',
  from: '2026-06-14T22:00:00Z',
  to: '2026-06-21T22:00:00Z',
  granularity: 'day',
  timeZone: 'Europe/Bratislava',
  totalEnergyKwh: 70,
  peakPowerKw: 6.5,
  points: [
    { periodStart: '2026-06-15T22:00:00Z', energyKwh: 30, peakPowerKw: 5, sampleCount: 100 },
    { periodStart: '2026-06-16T22:00:00Z', energyKwh: 40, peakPowerKw: 6.5, sampleCount: 120 },
  ],
}

describe('periods', () => {
  it('resolves presets to [from, to) dates', () => {
    expect(resolvePeriod({ ...base, preset: 'today' }, NOW)).toEqual({ from: '2026-06-21', to: '2026-06-22', days: 1 })
    expect(resolvePeriod(base, NOW)).toEqual({ from: '2026-06-15', to: '2026-06-22', days: 7 })
    expect(resolvePeriod({ ...base, preset: '30d' }, NOW)?.days).toBe(30)
    expect(resolvePeriod({ ...base, preset: 'year' }, NOW)?.days).toBe(365)
  })

  it('makes the custom end date inclusive and rejects invalid ranges', () => {
    const custom: PeriodState = { ...base, preset: 'custom', from: '2026-06-01', to: '2026-06-30' }
    expect(resolvePeriod(custom, NOW)).toEqual({ from: '2026-06-01', to: '2026-07-01', days: 30 })
    expect(resolvePeriod({ ...custom, to: '2026-05-01' }, NOW)).toBeNull()
    expect(customRangeError({ ...custom, to: '' })).toBe('missing')
    expect(customRangeError({ ...custom, to: '2026-05-01' })).toBe('reversed')
    expect(customRangeError({ ...custom, from: '2010-01-01' })).toBe('tooLong')
    expect(customRangeError(custom)).toBeNull()
  })

  it('chooses granularity within server limits', () => {
    expect(autoGranularity(1)).toBe('hour')
    expect(autoGranularity(30)).toBe('day')
    expect(autoGranularity(400)).toBe('month')
    expect(allowedGranularities(40)).toEqual(['hour', 'day', 'month'])
    expect(allowedGranularities(400)).toEqual(['day', 'month'])
    expect(allowedGranularities(7, true)).not.toContain('fiveMinutes')
  })

  it('keeps an allowed override and falls back to auto otherwise', () => {
    expect(effectiveGranularity({ ...base, granularity: 'fiveMinutes' }, 7)).toBe('fiveMinutes')
    expect(effectiveGranularity({ ...base, granularity: 'fiveMinutes' }, 90)).toBe('day')
  })

  it('round-trips the period through URL params', () => {
    const state: PeriodState = { preset: 'custom', from: '2026-06-01', to: '2026-06-30', granularity: 'day' }
    const params = periodToParams(new URLSearchParams('installation=i1'), state)
    expect(params.get('installation')).toBe('i1')
    expect(periodFromParams(params)).toEqual(state)
    expect(periodToParams(new URLSearchParams(), base).toString()).toBe('')
  })
})

describe('kpis', () => {
  it('computes energy, peak, daily average and periods with data', () => {
    expect(computeKpis(production)).toEqual({ energyKwh: 70, peakPowerKw: 6.5, avgPerDayKwh: 10, periodsWithData: 2 })
  })

  it('handles an empty period', () => {
    const kpis = computeKpis({ ...production, totalEnergyKwh: 0, peakPowerKw: null, points: [] })
    expect(kpis).toEqual({ energyKwh: 0, peakPowerKw: null, avgPerDayKwh: 0, periodsWithData: 0 })
  })

  it('merges predictions and flags underproduction', () => {
    const comparison: ComparisonResponse = {
      installationId: 'i1', from: production.from, to: production.to, granularity: 'day', timeZone: 'UTC',
      predictionAvailable: true, modelVersion: 'm1', actualEnergyKwh: 70, predictedEnergyKwh: 100,
      points: [
        { periodStart: '2026-06-15T22:00:00Z', actualEnergyKwh: 30, predictedEnergyKwh: 50, deviationKwh: -20, ratio: 0.6 },
        { periodStart: '2026-06-16T22:00:00Z', actualEnergyKwh: 40, predictedEnergyKwh: 40, deviationKwh: 0, ratio: 1 },
        { periodStart: '2026-06-17T22:00:00Z', actualEnergyKwh: null, predictedEnergyKwh: 20, deviationKwh: null, ratio: null },
      ],
    }
    const points = mergeChartPoints(production, comparison)
    expect(points).toHaveLength(3)
    expect(isUnderproduction(points[0])).toBe(true)
    expect(isUnderproduction(points[1])).toBe(false)
    expect(deviationPercent(points[0])).toBeCloseTo(-40)
    expect(deviationPercent(points[2])).toBeNull()
    expect(mergeChartPoints(production, { ...comparison, predictionAvailable: false })).toHaveLength(2)
  })
})
