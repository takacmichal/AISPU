import { useId, useState, type KeyboardEvent, type MouseEvent } from 'react'
import { formatNumber } from '../../lib/format'
import { TOLERANCE_RATIO, deviationPercent, isUnderproduction, type ChartPoint } from './kpis'
import type { Granularity } from './productionApi'
import { T } from './texts'
import { formatBucket, niceMax } from './chartMath'

const W = 720
const H = 280
const M = { top: 12, right: 12, bottom: 30, left: 52 }
const TABLE_LIMIT = 500
const BUCKET_MS: Record<Granularity, number> = { fiveMinutes: 300_000, hour: 3_600_000, day: 86_400_000, month: 30 * 86_400_000 }

interface Props {
  points: ChartPoint[]
  from: number
  to: number
  granularity: Granularity
  timeZone: string
  predictionAvailable: boolean
}

/** Bars = measured energy, line = prediction, band = tolerance. One focusable region with arrow-key navigation. */
export function ProductionChart({ points, from, to, granularity, timeZone, predictionAvailable }: Props) {
  const id = useId()
  const [active, setActive] = useState<number | null>(null)
  const span = Math.max(1, to - from)
  const innerW = W - M.left - M.right
  const innerH = H - M.top - M.bottom
  const maxValue = niceMax(Math.max(0, ...points.map((p) => Math.max(p.actual ?? 0, p.predicted ?? 0))))
  const x = (t: number) => M.left + ((t - from) / span) * innerW
  const y = (v: number) => M.top + innerH - (v / maxValue) * innerH
  const barW = Math.max(1, Math.min((BUCKET_MS[granularity] / span) * innerW - 1, innerW))
  const yTicks = [0, 1, 2, 3, 4].map((i) => (maxValue / 4) * i)
  const xTicks = Array.from({ length: 6 }, (_, i) => from + (span / 5) * i)
  const predictedLine = points.filter((p) => p.predicted !== null)
  const current = active !== null ? points[active] : null

  function describe(p: ChartPoint): string {
    const dev = deviationPercent(p)
    return [
      formatBucket(p.start, granularity, timeZone, true),
      `${T.chart.actual}: ${p.actual === null ? T.chart.noValue : `${formatNumber(p.actual)} ${T.units.kwh}`}`,
      predictionAvailable && `${T.chart.predicted}: ${p.predicted === null ? T.chart.noValue : `${formatNumber(p.predicted)} ${T.units.kwh}`}`,
      dev !== null && `${T.chart.deviation}: ${formatNumber(dev, 1)} %`,
    ]
      .filter(Boolean)
      .join('. ')
  }

  function onKeyDown(event: KeyboardEvent) {
    if (points.length === 0) return
    const last = points.length - 1
    const keys: Record<string, number> = {
      ArrowRight: Math.min(last, (active ?? -1) + 1),
      ArrowLeft: Math.max(0, (active ?? 1) - 1),
      Home: 0,
      End: last,
    }
    if (event.key === 'Escape') return setActive(null)
    if (event.key in keys) {
      event.preventDefault()
      setActive(keys[event.key])
    }
  }

  function onMove(event: MouseEvent<HTMLDivElement>) {
    const rect = event.currentTarget.getBoundingClientRect()
    if (rect.width === 0 || points.length === 0) return
    const t = from + (((event.clientX - rect.left) / rect.width) * W - M.left) / innerW * span
    let best = 0
    points.forEach((p, i) => {
      if (Math.abs(p.start - t) < Math.abs(points[best].start - t)) best = i
    })
    setActive(best)
  }

  return (
    <div
      className="prod-chart"
      tabIndex={0}
      role="group"
      aria-label={T.chart.label}
      onKeyDown={onKeyDown}
      onMouseMove={onMove}
      onMouseLeave={() => setActive(null)}
      onBlur={() => setActive(null)}
    >
      <svg viewBox={`0 0 ${W} ${H}`} className="prod-chart__svg" role="img" aria-labelledby={`${id}-t`}>
        <title id={`${id}-t`}>{T.chart.title}</title>
        {yTicks.map((v) => (
          <g key={v}>
            <line x1={M.left} x2={W - M.right} y1={y(v)} y2={y(v)} className="prod-chart__grid" />
            <text x={M.left - 6} y={y(v) + 4} textAnchor="end" className="prod-chart__tick">
              {formatNumber(v, 1)}
            </text>
          </g>
        ))}
        <text x={4} y={10} className="prod-chart__tick">
          {T.units.kwh}
        </text>
        {xTicks.map((t) => (
          <text key={t} x={x(t)} y={H - 8} textAnchor="middle" className="prod-chart__tick">
            {formatBucket(t, granularity, timeZone)}
          </text>
        ))}
        {predictionAvailable &&
          predictedLine.map((p) => (
            <rect
              key={`b${p.start}`}
              x={x(p.start)}
              width={barW}
              y={y(p.predicted!)}
              height={Math.max(0, y(p.predicted! * TOLERANCE_RATIO) - y(p.predicted!))}
              className="prod-chart__band"
            />
          ))}
        {points.map((p, i) =>
          p.actual === null ? null : (
            <rect
              key={p.start}
              x={x(p.start)}
              width={barW}
              y={y(p.actual)}
              height={Math.max(0, y(0) - y(p.actual))}
              className={isUnderproduction(p) ? 'prod-chart__bar prod-chart__bar--under' : 'prod-chart__bar'}
              data-active={i === active || undefined}
            />
          ),
        )}
        {predictionAvailable && predictedLine.length > 1 && (
          <polyline
            className="prod-chart__line"
            fill="none"
            points={predictedLine.map((p) => `${x(p.start) + barW / 2},${y(p.predicted!)}`).join(' ')}
          />
        )}
        {current && <line x1={x(current.start) + barW / 2} x2={x(current.start) + barW / 2} y1={M.top} y2={M.top + innerH} className="prod-chart__cursor" />}
      </svg>
      <ul className="prod-chart__legend" aria-hidden="true">
        <li><span className="prod-chart__swatch prod-chart__swatch--actual" />{T.chart.actual}</li>
        {predictionAvailable && (
          <>
            <li><span className="prod-chart__swatch prod-chart__swatch--predicted" />{T.chart.predicted}</li>
            <li><span className="prod-chart__swatch prod-chart__swatch--band" />{T.chart.tolerance}</li>
            <li><span className="prod-chart__swatch prod-chart__swatch--under" />{T.chart.under}</li>
          </>
        )}
      </ul>
      <div className="sr-only" aria-live="polite">{current ? describe(current) : ''}</div>
      {current && (
        <div className="prod-chart__tooltip" role="presentation" style={{ left: `${(x(current.start) / W) * 100}%` }}>
          <strong>{formatBucket(current.start, granularity, timeZone, true)}</strong>
          <span>{T.chart.actual}: {current.actual === null ? T.chart.noValue : `${formatNumber(current.actual)} ${T.units.kwh}`}</span>
          {predictionAvailable && (
            <span>{T.chart.predicted}: {current.predicted === null ? T.chart.noValue : `${formatNumber(current.predicted)} ${T.units.kwh}`}</span>
          )}
          {deviationPercent(current) !== null && <span>{T.chart.deviation}: {formatNumber(deviationPercent(current), 1)} %</span>}
        </div>
      )}
    </div>
  )
}

/** Accessible table alternative; rendered only while the <details> is open (5-minute data can be large). */
export function ProductionTable({ points, granularity, timeZone, predictionAvailable }: Omit<Props, 'from' | 'to'>) {
  const [open, setOpen] = useState(false)
  const shown = points.slice(0, TABLE_LIMIT)
  return (
    <details className="prod-table" onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary>{T.chart.table}</summary>
      {open && (
        <div className="data-table__scroll">
          <table className="data-table">
            <caption className="sr-only">{T.chart.tableCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.chart.colPeriod}</th>
                <th scope="col">{T.chart.colActual}</th>
                {predictionAvailable && <th scope="col">{T.chart.colPredicted}</th>}
                {predictionAvailable && <th scope="col">{T.chart.colDeviation}</th>}
                <th scope="col">{T.chart.colPeak}</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((p) => {
                const dev = deviationPercent(p)
                return (
                  <tr key={p.start}>
                    <td>{formatBucket(p.start, granularity, timeZone, true)}</td>
                    <td>{formatNumber(p.actual)}</td>
                    {predictionAvailable && <td>{formatNumber(p.predicted)}</td>}
                    {predictionAvailable && <td>{dev === null ? '–' : `${formatNumber(dev, 1)} %`}</td>}
                    <td>{formatNumber(p.peakKw)}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          {points.length > TABLE_LIMIT && <p className="kpi__detail">{T.chart.tableTruncated(TABLE_LIMIT, points.length)}</p>}
        </div>
      )}
    </details>
  )
}
