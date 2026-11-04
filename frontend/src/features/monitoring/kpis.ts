import type { ComparisonResponse, ProductionResponse } from './productionApi'

export interface Kpis {
  energyKwh: number
  peakPowerKw: number | null
  avgPerDayKwh: number
  periodsWithData: number
}

const DAY_MS = 86_400_000

export function computeKpis(production: ProductionResponse): Kpis {
  const energyKwh = production.points.length > 0 ? production.points.reduce((sum, p) => sum + p.energyKwh, 0) : production.totalEnergyKwh
  const peakPowerKw =
    production.peakPowerKw ?? (production.points.length > 0 ? Math.max(...production.points.map((p) => p.peakPowerKw)) : null)
  const days = Math.max(1, (Date.parse(production.to) - Date.parse(production.from)) / DAY_MS)
  return { energyKwh, peakPowerKw, avgPerDayKwh: energyKwh / days, periodsWithData: production.points.length }
}

/** Ratio below which a bucket counts as underproduction (ADR #194). */
export const TOLERANCE_RATIO = 0.85

export interface ChartPoint {
  start: number
  actual: number | null
  predicted: number | null
  peakKw: number | null
}

/** Merges measured buckets with predicted ones by bucket start. */
export function mergeChartPoints(production: ProductionResponse, comparison?: ComparisonResponse | null): ChartPoint[] {
  const map = new Map<number, ChartPoint>()
  for (const p of production.points) {
    const start = Date.parse(p.periodStart)
    map.set(start, { start, actual: p.energyKwh, predicted: null, peakKw: p.peakPowerKw })
  }
  if (comparison?.predictionAvailable) {
    for (const p of comparison.points) {
      const start = Date.parse(p.periodStart)
      const existing = map.get(start)
      if (existing) existing.predicted = p.predictedEnergyKwh
      else map.set(start, { start, actual: p.actualEnergyKwh, predicted: p.predictedEnergyKwh, peakKw: null })
    }
  }
  return [...map.values()].sort((a, b) => a.start - b.start)
}

export function deviationPercent(point: ChartPoint): number | null {
  if (point.actual === null || point.predicted === null || point.predicted === 0) return null
  return ((point.actual - point.predicted) / point.predicted) * 100
}

export function isUnderproduction(point: ChartPoint): boolean {
  return point.actual !== null && point.predicted !== null && point.predicted > 0 && point.actual / point.predicted < TOLERANCE_RATIO
}
