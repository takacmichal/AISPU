import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { userMessage } from '../../api/problem'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/toast'
import { useLoad } from '../../components/useLoad'
import { formatDate, formatKwp, formatNumber } from '../../lib/format'
import { MESSAGES } from '../../lib/messages'
import { downloadAttachment } from '../connection/actions'
import { ConnectionSteps } from '../connection/ConnectionStepper'
import type { ConnectionRequest } from '../connection/model'
import { ATTACHMENT_KIND_LABELS } from '../connection/texts'
import { STATE_LABELS } from '../installations/messages'
import { StateBadge } from '../installations/StateBadge'
import { isOperating, nextStep, reachedAt, type InstallationDetail } from './model'
import { PlantProduction } from './PlantProduction'
import './portal.css'
import { PORTAL_TEXTS as T } from './texts'

const NOT_FOUND = 'not-found'

/**
 * `/my-plants/:id` (role Customer): read-only view of one own installation. A foreign id gives 404 from the backend
 * (CustomerScope) and is shown exactly like a missing one. Internal fields (notes, technicians, who changed a state)
 * are deliberately not shown.
 */
export default function MyPlantDetailPage() {
  const { id = '' } = useParams()
  const { data, loading, error, reload } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations/{id}', { params: { path: { id } }, signal })
    if (response.status === 404) return NOT_FOUND
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)

  const back = (
    <Link to="/my-plants" className="btn btn--ghost btn--sm portal-back">
      {T.detail.back}
    </Link>
  )

  if (loading) return <Skeleton lines={5} label={T.detail.loading} />
  if (data === NOT_FOUND)
    return <EmptyState title={T.detail.notFoundTitle} description={T.detail.notFoundText} action={back} />
  if (error || !data)
    return (
      <div role="alert" className="alert portal-error">
        <p>{T.detail.error}</p>
        <button type="button" className="btn btn--sm" onClick={reload}>
          {MESSAGES.retry}
        </button>
      </div>
    )

  return <PlantDetail detail={data} back={back} />
}

function PlantDetail({ detail, back }: { detail: InstallationDetail; back: ReactNode }) {
  const { installation } = detail
  const connection = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/connection-requests', {
      params: { query: { installationId: installation.id } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return ((data as ConnectionRequest[])[0] ?? null) as ConnectionRequest | null
  }, installation.id)
  const request = connection.data ?? null
  const commissioned = reachedAt(detail, 'Commissioned')

  return (
    <article aria-labelledby="plant-title">
      {back}
      <header className="plant-detail__header">
        <h2 id="plant-title">{installation.name}</h2>
        <StateBadge state={installation.state} />
      </header>
      {!connection.loading && <p className="page-intro">{nextStep(installation, request)}</p>}

      <section className="card portal-section" aria-labelledby="plant-overview">
        <h3 id="plant-overview">{T.detail.overview}</h3>
        <dl className="plant-facts">
          <div>
            <dt>{T.detail.power}</dt>
            <dd>{formatKwp(installation.plannedPowerKwp)}</dd>
          </div>
          <div>
            <dt>{T.detail.state}</dt>
            <dd>{STATE_LABELS[installation.state]}</dd>
          </div>
          <div>
            <dt>{T.detail.schedule}</dt>
            <dd>
              {installation.scheduledStart
                ? `${formatDate(installation.scheduledStart)} – ${formatDate(installation.scheduledEnd)}`
                : T.detail.notScheduled}
            </dd>
          </div>
          {request && (
            <div>
              <dt>{T.detail.supplyPoint}</dt>
              <dd className="mono">{request.eicCode}</dd>
            </div>
          )}
          {commissioned && (
            <div>
              <dt>{T.detail.commissioned}</dt>
              <dd>{formatDate(commissioned)}</dd>
            </div>
          )}
        </dl>
      </section>

      <section className="card portal-section" aria-labelledby="plant-progress">
        <h3 id="plant-progress">{T.detail.progress}</h3>
        {detail.stateHistory.length === 0 ? (
          <p className="page-intro">{T.detail.progressEmpty}</p>
        ) : (
          <ol className="plant-timeline">
            {detail.stateHistory.map((change, index) => (
              <li key={`${change.toState}-${change.changedAt}-${index}`}>
                <span>{STATE_LABELS[change.toState] ?? change.toState}</span>
                <time dateTime={change.changedAt}>{formatDate(change.changedAt)}</time>
              </li>
            ))}
          </ol>
        )}
      </section>

      <section className="card portal-section" aria-labelledby="plant-connection" aria-busy={connection.loading}>
        <h3 id="plant-connection">{T.detail.connection}</h3>
        {connection.loading && <Skeleton lines={2} label={T.detail.connectionLoading} />}
        {connection.error && (
          <div role="alert" className="alert portal-error">
            <p>{T.detail.connectionError}</p>
            <button type="button" className="btn btn--sm" onClick={connection.reload}>
              {MESSAGES.retry}
            </button>
          </div>
        )}
        {!connection.loading && !connection.error && !request && (
          <EmptyState title={T.detail.connectionEmpty} description={T.detail.connectionEmptyText} />
        )}
        {request && <ConnectionSteps request={request} />}
      </section>

      <Devices installationId={installation.id} />

      <section className="card portal-section" aria-labelledby="plant-documents">
        <h3 id="plant-documents">{T.detail.documents}</h3>
        <HandOver detail={detail} />
        {!connection.loading && !connection.error && <Documents request={request} />}
      </section>

      <section className="card portal-section" aria-labelledby="plant-production">
        <h3 id="plant-production">{T.detail.production}</h3>
        {isOperating(installation.state) ? (
          <PlantProduction installationId={installation.id} />
        ) : (
          <p className="page-intro">{T.detail.productionPending}</p>
        )}
      </section>
    </article>
  )
}

function HandOver({ detail }: { detail: InstallationDetail }) {
  const handedOver = detail.installation.state === 'HandedOver' ? reachedAt(detail, 'HandedOver') : null
  return <p>{handedOver ? T.detail.handOverDone(formatDate(handedOver)) : T.detail.handOverPending}</p>
}

function Documents({ request }: { request: ConnectionRequest | null }) {
  const toast = useToast()
  const attachments = request?.attachments ?? []
  if (attachments.length === 0) return <p className="page-intro">{T.detail.documentsEmpty}</p>
  return (
    <ul className="plant-documents">
      {attachments.map((a) => (
        <li key={a.id}>
          <span>
            {ATTACHMENT_KIND_LABELS[a.kind] ?? a.kind}: {a.fileName}
          </span>
          {a.hasContent && a.downloadUrl ? (
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              aria-label={`${T.detail.download} ${a.fileName}`}
              onClick={() => downloadAttachment(a.downloadUrl!, a.fileName).catch((e) => toast.error(userMessage(e)))}
            >
              {T.detail.download}
            </button>
          ) : (
            <span className="badge">{T.detail.notDownloadable}</span>
          )}
        </li>
      ))}
    </ul>
  )
}

function Devices({ installationId }: { installationId: string }) {
  const { data, loading, error, reload } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations/{installationId}/devices', {
      params: { path: { installationId } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, installationId)

  return (
    <section className="card portal-section" aria-labelledby="plant-devices" aria-busy={loading}>
      <h3 id="plant-devices">{T.detail.devices}</h3>
      {loading && <Skeleton lines={3} label={T.detail.devicesLoading} />}
      {error && (
        <div role="alert" className="alert portal-error">
          <p>{T.detail.devicesError}</p>
          <button type="button" className="btn btn--sm" onClick={reload}>
            {MESSAGES.retry}
          </button>
        </div>
      )}
      {data && data.length === 0 && <p className="page-intro">{T.detail.devicesEmpty}</p>}
      {data && data.length > 0 && (
        <>
          <p className="page-intro">
            {T.detail.devicesSummary(
              data.filter((d) => d.type === 'Panel').length,
              data.filter((d) => d.type === 'Inverter').length,
            )}
          </p>
          <div className="data-table__scroll">
            <table>
              <caption className="sr-only">{T.detail.devices}</caption>
              <thead>
                <tr>
                  <th scope="col">{T.detail.colType}</th>
                  <th scope="col">{T.detail.colSerial}</th>
                  <th scope="col">{T.detail.colMaker}</th>
                  <th scope="col">{T.detail.colPower}</th>
                </tr>
              </thead>
              <tbody>
                {data.map((d) => (
                  <tr key={d.id}>
                    <td>{T.detail.deviceTypes[d.type] ?? d.type}</td>
                    <td className="mono">{d.serialNumber}</td>
                    <td>{`${d.manufacturer} ${d.model}`}</td>
                    <td>{formatNumber(Number(d.powerW))}&nbsp;W</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </section>
  )
}
