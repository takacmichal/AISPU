import type { SeriesPoint } from '../../components/charts/chartGeometry'

/**
 * Illustrative day for the landing page charts (half-hour points, 6:00–20:00). Not real telemetry.
 * With `dip` the production drops to 66 % of the prediction between 11:00 and 14:00.
 */
export function illustrativeDay(peakKw: number, dip = false): SeriesPoint[] {
  return Array.from({ length: 29 }, (_, i) => {
    const hour = 6 + i / 2
    const predicted = Math.max(0, peakKw * Math.sin((Math.PI * (hour - 6)) / 14))
    const ratio = dip && hour >= 11 && hour <= 14 ? 0.66 : 0.9 + ((i * 7) % 5) / 50
    const round = (value: number) => Math.round(value * 100) / 100
    return { label: `${Math.floor(hour)}:${hour % 1 ? '30' : '00'}`, actual: round(predicted * ratio), predicted: round(predicted) }
  })
}
