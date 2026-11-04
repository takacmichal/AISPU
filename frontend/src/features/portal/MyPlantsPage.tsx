import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useLoad } from '../../components/useLoad'
import { formatKwp } from '../../lib/format'
import { MESSAGES } from '../../lib/messages'
import type { ConnectionRequest } from '../connection/model'
import { StateBadge } from '../installations/StateBadge'
import { connectionSummary, latestRequestByInstallation, nextStep, type Installation } from './model'
import './portal.css'
import { PORTAL_TEXTS as T } from './texts'

/**
 * `/my-plants` (role Customer): cards of the customer's own installations. The backend scopes both lists to the
 * signed-in customer (CustomerScope), so no customer filter is sent.
 */
export default function MyPlantsPage() {
  const plants = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, 'my-plants')

  // Connection progress is best effort: the list stays usable when this call fails.
  const requests = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/connection-requests', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return latestRequestByInstallation(data as ConnectionRequest[])
  }, 'my-connection-requests')

  return (
    <section aria-labelledby="my-plants-title">
      <div className="page-header">
        <h2 id="my-plants-title">{T.list.title}</h2>
        <p className="page-intro">{T.list.intro}</p>
      </div>
      {plants.loading && <Skeleton lines={4} label={T.list.loading} />}
      {plants.error && (
        <div role="alert" className="alert portal-error">
          <p>{T.list.error}</p>
          <button type="button" className="btn btn--sm" onClick={plants.reload}>
            {MESSAGES.retry}
          </button>
        </div>
      )}
      {plants.data && plants.data.length === 0 && <EmptyState title={T.list.emptyTitle} description={T.list.emptyText} />}
      {plants.data && plants.data.length > 0 && (
        <ul className="plant-grid">
          {plants.data.map((plant) => (
            <li key={plant.id}>
              <PlantCard
                plant={plant}
                request={requests.data?.get(plant.id) ?? null}
                connectionLoading={requests.loading}
                connectionError={requests.error}
              />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

interface PlantCardProps {
  plant: Installation
  request: ConnectionRequest | null
  connectionLoading: boolean
  connectionError: boolean
}

function PlantCard({ plant, request, connectionLoading, connectionError }: PlantCardProps) {
  const titleId = `plant-${plant.id}`
  const step = connectionLoading ? null : nextStep(plant, request)
  return (
    <article className="card plant-card" aria-labelledby={titleId}>
      <header className="plant-card__header">
        <h3 id={titleId} className="plant-card__title">
          {/* The link covers the whole card (see .plant-card__link::after); its name is the plant name. */}
          <Link to={`/my-plants/${plant.id}`} className="plant-card__link">
            {plant.name}
          </Link>
        </h3>
        <StateBadge state={plant.state} />
      </header>
      <dl className="plant-facts">
        <div>
          <dt>{T.list.power}</dt>
          <dd>{formatKwp(plant.plannedPowerKwp)}</dd>
        </div>
        {request && (
          <div>
            <dt>{T.list.supplyPoint}</dt>
            <dd className="mono">{request.eicCode}</dd>
          </div>
        )}
        {request && (
          <div>
            <dt>{T.list.connection}</dt>
            <dd>{connectionSummary(request)}</dd>
          </div>
        )}
        {connectionError && (
          <div>
            <dt>{T.list.connection}</dt>
            <dd>{T.list.connectionUnavailable}</dd>
          </div>
        )}
      </dl>
      <div className="plant-card__next">
        <span className="plant-card__next-label">{T.list.nextStep}</span>
        {step === null ? <span className="skeleton" aria-hidden="true" /> : <p>{step}</p>}
      </div>
    </article>
  )
}
