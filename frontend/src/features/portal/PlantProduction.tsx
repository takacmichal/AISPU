import { EmptyState } from '../../components/EmptyState'
import { KeyValueList } from '../../components/KeyValueList'
import { Skeleton } from '../../components/Skeleton'
import { ProductionAreaChart } from '../../components/charts/ProductionAreaChart'
import { useLoad } from '../../components/useLoad'
import { formatNumber } from '../../lib/format'
import { MESSAGES } from '../../lib/messages'
import { rangeQuery } from '../dashboard/aggregate'
import { formatBucket } from '../monitoring/chartMath'
import { fetchProduction } from '../monitoring/productionApi'
import { PORTAL_TEXTS as T } from './texts'

/** Daily production of the customer's own plant over the last 30 days (#235); the API answers 404 for a foreign plant. */
export function PlantProduction({ installationId }: { installationId: string }) {
  const { data, loading, error, reload } = useLoad(
    (signal) => fetchProduction(installationId, rangeQuery('30d', new Date()), signal),
    `portal-plant-production|${installationId}`,
  )

  if (loading) return <Skeleton lines={4} label={T.detail.productionLoading} />
  if (error || !data)
    return (
      <div role="alert" className="alert portal-error">
        <p>{T.detail.productionError}</p>
        <button type="button" className="btn btn--sm" onClick={reload}>
          {MESSAGES.retry}
        </button>
      </div>
    )

  const total = data.points.reduce((sum, p) => sum + p.energyKwh, 0)
  if (data.points.length === 0 || total <= 0) return <EmptyState title={T.detail.productionEmpty} />

  const best = Math.max(...data.points.map((p) => p.energyKwh))
  const points = data.points.map((p) => ({
    label: formatBucket(Date.parse(p.periodStart), 'day', data.timeZone),
    actual: p.energyKwh,
    predicted: null,
  }))

  return (
    <>
      <KeyValueList
        items={[
          { label: T.detail.productionTotal, value: `${formatNumber(total, 0)} kWh` },
          { label: T.detail.productionBest, value: `${formatNumber(best, 0)} kWh` },
        ]}
      />
      <ProductionAreaChart
        title={T.detail.productionChart}
        summary={T.detail.productionSummary(formatNumber(total, 0))}
        unit="kWh"
        points={points}
        xLabelCount={6}
      />
    </>
  )
}
