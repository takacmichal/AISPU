import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useLoad } from '../../components/useLoad'
import { formatDateTime } from '../../lib/format'
import { ANOMALIES_API_ENABLED, fetchAnomalies, type Anomaly } from './anomaliesApi'
import { T } from './texts'

/** Anomaly detail (#252). Not routed yet; rendered by the list when an item is opened once the backend exists. */
export function AnomalyDetail({ anomaly }: { anomaly: Anomaly }) {
  return (
    <article className="card">
      <h3>{T.anomalies.detailTitle}: {anomaly.title}</h3>
      <dl>
        <dt>{T.anomalies.detectedAt}</dt>
        <dd>{formatDateTime(anomaly.detectedAt)}</dd>
        <dt>{T.anomalies.cause}</dt>
        <dd>{anomaly.cause ?? T.anomalies.causeUnknown}</dd>
        <dt>{T.anomalies.ticket}</dt>
        <dd>{anomaly.ticketId ?? T.anomalies.noTicket}</dd>
      </dl>
    </article>
  )
}

function AnomalyList({ installationId }: { installationId: string }) {
  const { data, loading, error, reload } = useLoad((signal) => fetchAnomalies(installationId, signal), installationId)
  if (loading) return <Skeleton lines={3} />
  if (error)
    return (
      <div className="kpi__error" role="alert">
        <p>{T.error}</p>
        <button type="button" className="btn btn--sm" onClick={reload}>{T.retry}</button>
      </div>
    )
  if (!data || data.length === 0) return <EmptyState title={T.anomalies.empty} />
  return (
    <ul>
      {data.map((a) => (
        <li key={a.id}>
          <strong>{T.anomalies.severity[a.severity]}</strong> {a.title} ({formatDateTime(a.detectedAt)})
        </li>
      ))}
    </ul>
  )
}

/** Anomaly list and alerts (#251). Shows a "Pripravuje sa" placeholder while ANOMALIES_API_ENABLED is false. */
export function AnomalySection({ installationId }: { installationId: string }) {
  return (
    <section className="card prod-anomalies" aria-labelledby="anomalies-title">
      <h3 id="anomalies-title" className="prod-chart-card__title">{T.anomalies.title}</h3>
      {ANOMALIES_API_ENABLED ? (
        <AnomalyList installationId={installationId} />
      ) : (
        <EmptyState title={T.anomalies.soon} description={T.anomalies.soonText} />
      )}
    </section>
  )
}
