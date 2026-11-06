import { useState } from 'react'
import { SegmentedControl } from '../../components/SegmentedControl'
import { Skeleton } from '../../components/Skeleton'
import { DASHBOARD_POLL_MS, useLoad } from '../../components/useLoad'
import { formatNumber } from '../../lib/format'
import { barLabel, rangeQuery, type Range } from './aggregate'
import { api } from '../../api/client'
import { DASHBOARD_PARTS as T } from './texts'
import './dashboard.css'

const RANGES = [
  { value: '14d', label: T.production.ranges['14d'] },
  { value: '30d', label: T.production.ranges['30d'] },
  { value: '12m', label: T.production.ranges['12m'] },
] as const

/** Production of all visible installations, summed per day or month (bars, the last one highlighted). */
export function ProductionBarsCard() {
  const [range, setRange] = useState<Range>('14d')
  const { data, loading, error, reload } = useLoad(
    async (signal) => {
      const query = rangeQuery(range, new Date())
      const production = await api.GET('/api/production/summary', { params: { query: { From: query.from, To: query.to, Granularity: query.granularity } }, signal })
      if (!production.data) throw new Error(`HTTP ${production.response.status}`)
      return { bars: production.data.points.map((point) => ({ key: point.periodStart, start: point.periodStart, kwh: Number(point.energyKwh) })), installations: Number(production.data.installationCount) }
    },
    range,
    { intervalMs: DASHBOARD_POLL_MS },
  )

  const noPlants = Boolean(data && data.installations === 0)
  const bars = data?.bars ?? []
  const max = Math.max(1, ...bars.map((b) => b.kwh))
  const total = bars.reduce((sum, b) => sum + b.kwh, 0)

  return (
    <article className="card dash-card" aria-labelledby="dash-production-title">
      <header className="dash-card__header">
        <div>
          <h3 id="dash-production-title" className="dash-card__title">
            {T.production.title}
          </h3>
          <p className="dash-card__subtitle">
            {T.production.subtitle()}
          </p>
        </div>
        <SegmentedControl label={T.production.range} options={RANGES} value={range} onChange={setRange} size="sm" />
      </header>
      {noPlants && <p className="dash-card__empty">{T.production.noInstallations}</p>}
      {!noPlants && loading && <Skeleton lines={4} label={T.production.loading} />}
      {error && (
        <p className="dash-card__empty">
          {T.production.error}{' '}
          <button type="button" className="btn btn--sm" onClick={reload}>
            {T.retry}
          </button>
        </p>
      )}
      {!noPlants && data && total <= 0 && <p className="dash-card__empty">{T.production.empty}</p>}
      {!noPlants && data && total > 0 && (
        <figure className="bars">
          <figcaption className="sr-only">{T.production.summary(formatNumber(total, 0))}</figcaption>
          <div className="bars__plot" role="img" aria-label={T.production.summary(formatNumber(total, 0))}>
            {bars.map((bar, index) => (
              <div key={bar.key} className="bars__col" title={`${barLabel(bar.start, range)} · ${formatNumber(bar.kwh, 0)} kWh`}>
                <div
                  className={`bars__bar${index === bars.length - 1 ? ' bars__bar--current' : ''}`}
                  style={{ height: `${Math.max(2, (bar.kwh / max) * 100)}%` }}
                />
                <span className="bars__label" aria-hidden="true">
                  {index % 2 === 0 ? barLabel(bar.start, range) : ''}
                </span>
              </div>
            ))}
          </div>
        </figure>
      )}
    </article>
  )
}
