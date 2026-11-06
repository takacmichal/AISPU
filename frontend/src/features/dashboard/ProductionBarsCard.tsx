import { useState } from 'react'
import { SegmentedControl } from '../../components/SegmentedControl'
import { Skeleton } from '../../components/Skeleton'
import { DASHBOARD_POLL_MS, useLoad } from '../../components/useLoad'
import { formatNumber } from '../../lib/format'
import { fetchProduction } from '../monitoring/productionApi'
import { barLabel, rangeQuery, sumProduction, type Range } from './aggregate'
import { DASHBOARD_PARTS as T } from './texts'
import './dashboard.css'

const RANGES = [
  { value: '14d', label: T.production.ranges['14d'] },
  { value: '30d', label: T.production.ranges['30d'] },
  { value: '12m', label: T.production.ranges['12m'] },
] as const

/** Production of the plants in operation, summed per day or month (bars, the last one highlighted). */
export function ProductionBarsCard({ installationIds }: { installationIds: readonly string[] }) {
  const [range, setRange] = useState<Range>('14d')
  const { data, loading, error, reload } = useLoad(
    async (signal) => {
      const query = rangeQuery(range, new Date())
      const responses = await Promise.all(installationIds.map((id) => fetchProduction(id, query, signal)))
      return sumProduction(responses)
    },
    `${range}|${installationIds.join(',')}`,
    { intervalMs: DASHBOARD_POLL_MS, enabled: installationIds.length > 0 },
  )

  const noPlants = installationIds.length === 0
  const max = Math.max(1, ...(data ?? []).map((b) => b.kwh))
  const total = (data ?? []).reduce((sum, b) => sum + b.kwh, 0)

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
            {data.map((bar, index) => (
              <div key={bar.key} className="bars__col" title={`${barLabel(bar.start, range)} · ${formatNumber(bar.kwh, 0)} kWh`}>
                <div
                  className={`bars__bar${index === data.length - 1 ? ' bars__bar--current' : ''}`}
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
