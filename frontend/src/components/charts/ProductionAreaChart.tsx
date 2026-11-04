import { useId } from 'react'
import { formatNumber } from '../../lib/format'
import { MESSAGES } from '../../lib/messages'
import { DEFAULT_UNDER_RATIO, areaPaths, linePath, niceMax, underRanges, type SeriesPoint } from './chartGeometry'
import './charts.css'

const W = 800
const H = 260
const M = { top: 28, right: 12, bottom: 28, left: 48 }

interface ProductionAreaChartProps {
  /** Names the chart (image and table). */
  title: string
  points: readonly SeriesPoint[]
  /** Unit of the values, e.g. "kW" or "kWh". */
  unit: string
  /** Text alternative of what the chart shows; read as the image description. */
  summary?: string
  /** Dark variant for the dark band on the landing page. */
  variant?: 'light' | 'dark'
  /** Share of the prediction below which a point is underproduction (default 85 %, ADR #194). */
  underRatio?: number
  /** Minimum number of consecutive points for an underproduction range. */
  minUnderPoints?: number
  /** How many X labels to show (evenly spaced). */
  xLabelCount?: number
}

/**
 * Actual production (area) vs. prediction (dashed line) with underproduction ranges highlighted.
 * Shared by the landing page, the customer portal and monitoring (ADR #224: own SVG behind a stable props API,
 * so the implementation can be swapped for Recharts later without touching the pages).
 */
export function ProductionAreaChart({
  title,
  points,
  unit,
  summary,
  variant = 'light',
  underRatio = DEFAULT_UNDER_RATIO,
  minUnderPoints = 1,
  xLabelCount = 4,
}: ProductionAreaChartProps) {
  const id = useId()
  const T = MESSAGES.chart
  const innerW = W - M.left - M.right
  const innerH = H - M.top - M.bottom
  const max = niceMax(Math.max(0, ...points.flatMap((p) => [p.actual ?? 0, p.predicted ?? 0])))
  const step = points.length > 1 ? innerW / (points.length - 1) : 0
  const x = (i: number) => M.left + i * step
  const y = (v: number) => M.top + innerH - (v / max) * innerH
  const baseline = M.top + innerH
  const ranges = underRanges(points, underRatio, minUnderPoints)
  const ticks = [0, max / 2, max]
  const labelEvery = Math.max(1, Math.round((points.length - 1) / Math.max(1, xLabelCount - 1)))

  return (
    <figure className={`area-chart area-chart--${variant}`}>
      <ul className="area-chart__legend" aria-hidden="true">
        <li>
          <span className="area-chart__swatch area-chart__swatch--actual" />
          {T.actual}
        </li>
        <li>
          <span className="area-chart__swatch area-chart__swatch--predicted" />
          {T.predicted}
        </li>
        {ranges.length > 0 && (
          <li>
            <span className="area-chart__swatch area-chart__swatch--under" />
            {T.under}
          </li>
        )}
      </ul>
      <svg
        className="area-chart__svg"
        viewBox={`0 0 ${W} ${H}`}
        role="img"
        aria-label={title}
        aria-describedby={summary ? `${id}-summary` : undefined}
      >
        {ticks.map((t) => (
          <g key={t}>
            <line className="area-chart__grid" x1={M.left} x2={W - M.right} y1={y(t)} y2={y(t)} />
            <text className="area-chart__tick" x={M.left - 8} y={y(t) + 4} textAnchor="end">
              {t === 0 ? '0' : `${formatNumber(t, 1)} ${unit}`}
            </text>
          </g>
        ))}
        {ranges.map((r) => {
          const x1 = Math.max(M.left, x(r.start) - step / 2)
          const x2 = Math.min(W - M.right, x(r.end) + step / 2)
          return (
            <g key={r.start}>
              <rect className="area-chart__under" x={x1} y={M.top} width={Math.max(2, x2 - x1)} height={innerH} />
              <text className="area-chart__under-label" x={x1 + 6} y={M.top - 8}>
                {T.under} · {points[r.start].label}–{points[r.end].label} · {Math.round(r.ratio * 100)} %
              </text>
            </g>
          )
        })}
        {areaPaths(
          points.map((p) => p.actual),
          x,
          y,
          baseline,
        ).map((d) => (
          <path key={d} className="area-chart__area" d={d} />
        ))}
        <path className="area-chart__actual" d={linePath(points.map((p) => p.actual), x, y)} />
        <path className="area-chart__predicted" d={linePath(points.map((p) => p.predicted), x, y)} />
        {points.map((p, i) =>
          i % labelEvery === 0 ? (
            <text key={i} className="area-chart__tick" x={x(i)} y={H - 6} textAnchor={i === 0 ? 'start' : 'middle'}>
              {p.label}
            </text>
          ) : null,
        )}
      </svg>
      {summary && (
        <figcaption id={`${id}-summary`} className="sr-only">
          {summary}
        </figcaption>
      )}
      <table className="sr-only">
        <caption>{title}</caption>
        <thead>
          <tr>
            <th scope="col">{T.time}</th>
            <th scope="col">
              {T.actual} ({unit})
            </th>
            <th scope="col">
              {T.predicted} ({unit})
            </th>
          </tr>
        </thead>
        <tbody>
          {points.map((p, i) => (
            <tr key={i}>
              <th scope="row">{p.label}</th>
              <td>{p.actual === null ? '–' : formatNumber(p.actual, 2)}</td>
              <td>{p.predicted === null ? '–' : formatNumber(p.predicted, 2)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  )
}
