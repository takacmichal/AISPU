import { underRanges, type SeriesPoint } from '../../components/charts/chartGeometry'
import { formatNumber } from '../../lib/format'
import { TOLERANCE_RATIO, type ChartPoint } from './kpis'

export interface UnderPeriod {
  /** Inclusive point indexes in the chart series. */
  start: number
  end: number
  /** Long labels of the first and last bucket, e.g. "4. 11. 2026". */
  from: string
  to: string
  /** Actual / predicted in the period (0.72 = 72 %). */
  ratio: number
  actualKwh: number
  predictedKwh: number
}

/** Underproduction periods: runs of buckets below 85 % of the prediction (same rule as the chart, ADR #194). */
export function underPeriods(points: readonly ChartPoint[], longLabel: (start: number) => string): UnderPeriod[] {
  const series: SeriesPoint[] = points.map((p) => ({ label: '', actual: p.actual, predicted: p.predicted }))
  return underRanges(series, TOLERANCE_RATIO).map((range) => {
    const slice = points.slice(range.start, range.end + 1)
    return {
      start: range.start,
      end: range.end,
      from: longLabel(points[range.start].start),
      to: longLabel(points[range.end].start),
      ratio: range.ratio,
      actualKwh: slice.reduce((sum, p) => sum + (p.actual ?? 0), 0),
      predictedKwh: slice.reduce((sum, p) => sum + (p.predicted ?? 0), 0),
    }
  })
}

export const periodText = (period: Pick<UnderPeriod, 'from' | 'to'>) => (period.from === period.to ? period.from : `${period.from} – ${period.to}`)

/** `/tickets/new` link with the installation and a prefilled title and description of the underproduction. */
export function underproductionTicketHref(installationId: string, installationName: string, period: UnderPeriod): string {
  const when = periodText(period)
  const description =
    `Monitoring výroby zistil podvýrobu elektrárne ${installationName} v období ${when}: ` +
    `skutočná výroba ${formatNumber(period.actualKwh, 1)} kWh je ${Math.round(period.ratio * 100)} % ` +
    `očakávanej (${formatNumber(period.predictedKwh, 1)} kWh). Prosím o preverenie.`
  const params = new URLSearchParams({ installationId, title: `Podvýroba ${when}`, description })
  return `/tickets/new?${params.toString()}`
}
