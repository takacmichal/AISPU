import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { EmptyState } from '../../components/EmptyState'
import { KpiCard } from '../../components/KpiCard'
import { PageHeader } from '../../components/PageHeader'
import { Skeleton } from '../../components/Skeleton'
import { Stepper } from '../../components/Stepper'
import { useLoad } from '../../components/useLoad'
import { formatKwp, formatNumber, formatRelative } from '../../lib/format'
import { MESSAGES } from '../../lib/messages'
import type { ConnectionRequest } from '../connection/model'
import { StateBadge } from '../installations/StateBadge'
import { addDays, toDateString } from '../monitoring/periods'
import { fetchProduction } from '../monitoring/productionApi'
import { StateBadge as TicketStateBadge } from '../tickets/TicketBadges'
import { isOpenTicket, listTickets } from '../tickets/ticketsApi'
import { currentPowerKw, installationSteps, isOperating, latestRequestByInstallation, nextStep, type Installation } from './model'
import './portal.css'
import { PORTAL_TEXTS } from './texts'

const T = PORTAL_TEXTS.home
const TICKET_LIST_SIZE = 5

interface PlantProduction {
  todayKwh: number
  powerKw: number | null
}

/** Today's production of each operating plant; a failed plant is `null`, so one failure does not hide the others. */
async function loadProduction(plants: readonly Installation[], now: Date, signal: AbortSignal) {
  const from = toDateString(now)
  const query = { from, to: addDays(from, 1), granularity: 'fiveMinutes' as const }
  const entries = await Promise.all(
    plants.map(async (plant): Promise<[string, PlantProduction | null]> => {
      try {
        const response = await fetchProduction(plant.id, query, signal)
        return [plant.id, { todayKwh: response.totalEnergyKwh, powerKw: currentPowerKw(response.points, now) }]
      } catch {
        return [plant.id, null]
      }
    }),
  )
  return new Map(entries)
}

/**
 * Home of the customer portal on /app (wireframe "Portál"): greeting, KPIs, plant cards with the current power
 * (or the installation stepper before commissioning) and the customer's tickets. The API scopes every list to the
 * signed-in customer (CustomerScope), so no customer filter is sent.
 */
export default function PortalHome({ footer }: { footer?: ReactNode }) {
  const now = new Date()
  const plants = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, 'portal-plants')

  const operating = (plants.data ?? []).filter((p) => isOperating(p.state))
  const production = useLoad(
    (signal) => loadProduction(operating, now, signal),
    `portal-production|${operating.map((p) => p.id).join(',')}`,
  )

  // Best effort: without connection requests the next step falls back to the installation state.
  const requests = useLoad(async (signal) => {
    const { data } = await api.GET('/api/connection-requests', { signal })
    return latestRequestByInstallation((data ?? []) as ConnectionRequest[])
  }, 'portal-connection-requests')

  const tickets = useLoad((signal) => listTickets({ pageSize: TICKET_LIST_SIZE }, signal), 'portal-tickets')

  const values = [...(production.data?.values() ?? [])].filter((v): v is PlantProduction => v !== null)
  const powers = values.map((v) => v.powerKw).filter((v): v is number => v !== null)
  const productionPending = plants.loading || (operating.length > 0 && production.loading)
  const productionKpi = (label: string) => ({
    loading: productionPending ? `${T.kpiLoading} ${label}` : undefined,
    error: plants.error ? { message: T.kpiError, onRetry: plants.reload } : undefined,
  })

  return (
    <section className="portal-home">
      <PageHeader
        title={T.greeting}
        subtitle={plants.data ? T.subtitle(plants.data.length) : T.subtitleLoading}
        actions={
          <>
            <Link to="/my-plants" className="btn">
              {T.myPlants}
            </Link>
            <Link to="/tickets/new" className="btn btn--primary">
              {T.report}
            </Link>
          </>
        }
      />

      <div className="kpi-grid" role="group" aria-label={T.kpiRegion}>
        <KpiCard
          label={T.today}
          {...productionKpi(T.today)}
          value={values.length > 0 ? formatNumber(values.reduce((sum, v) => sum + v.todayKwh, 0), 1) : '–'}
          unit={values.length > 0 ? 'kWh' : undefined}
          detail={T.todayDetail(operating.length)}
        />
        <KpiCard
          label={T.power}
          {...productionKpi(T.power)}
          value={powers.length > 0 ? formatNumber(powers.reduce((a, b) => a + b, 0), 1) : '–'}
          unit={powers.length > 0 ? 'kW' : undefined}
          detail={T.powerDetail}
        />
        <KpiCard
          label={T.plants}
          href="/my-plants"
          loading={plants.loading ? `${T.kpiLoading} ${T.plants}` : undefined}
          error={plants.error ? { message: T.kpiError, onRetry: plants.reload } : undefined}
          value={plants.data?.length ?? 0}
          detail={T.plantsDetail(operating.length, (plants.data?.length ?? 0) - operating.length)}
        />
        <KpiCard
          label={T.openTickets}
          href="/tickets"
          loading={tickets.loading ? `${T.kpiLoading} ${T.openTickets}` : undefined}
          error={tickets.error ? { message: T.kpiError, onRetry: tickets.reload } : undefined}
          value={tickets.data?.items.filter(isOpenTicket).length ?? 0}
          detail={T.openTicketsDetail}
        />
      </div>

      <div className="portal-home__columns">
        <section aria-labelledby="portal-plants-title">
          <h3 id="portal-plants-title" className="portal-home__title">
            {T.plantsTitle}
          </h3>
          {plants.loading && <Skeleton lines={4} label={PORTAL_TEXTS.list.loading} />}
          {plants.error && (
            <div role="alert" className="alert portal-error">
              <p>{PORTAL_TEXTS.list.error}</p>
              <button type="button" className="btn btn--sm" onClick={plants.reload}>
                {MESSAGES.retry}
              </button>
            </div>
          )}
          {plants.data && plants.data.length === 0 && (
            <EmptyState title={PORTAL_TEXTS.list.emptyTitle} description={PORTAL_TEXTS.list.emptyText} />
          )}
          {plants.data && plants.data.length > 0 && (
            <ul className="portal-home__plants">
              {plants.data.map((plant) => (
                <li key={plant.id}>
                  <PlantSummary
                    plant={plant}
                    production={production.data?.get(plant.id)}
                    productionLoading={production.loading}
                    request={requests.data?.get(plant.id) ?? null}
                  />
                </li>
              ))}
            </ul>
          )}
        </section>

        <article className="card portal-tickets" aria-labelledby="portal-tickets-title">
          <header className="portal-tickets__header">
            <h3 id="portal-tickets-title" className="portal-home__title">
              {T.ticketsTitle}
            </h3>
            <Link to="/tickets">{T.ticketsAll}</Link>
          </header>
          {tickets.loading && <Skeleton lines={3} label={T.ticketsLoading} />}
          {tickets.error && <p className="portal-tickets__empty">{T.ticketsError}</p>}
          {tickets.data && tickets.data.items.length === 0 && (
            <EmptyState
              title={T.ticketsEmptyTitle}
              description={T.ticketsEmptyText}
              action={
                <Link to="/tickets/new" className="btn btn--primary">
                  {T.report}
                </Link>
              }
            />
          )}
          {tickets.data && tickets.data.items.length > 0 && (
            <ul className="portal-tickets__list">
              {tickets.data.items.map((ticket) => (
                <li key={ticket.id}>
                  <div className="portal-tickets__text">
                    <Link to={`/tickets/${ticket.id}`}>{ticket.title}</Link>
                    <time dateTime={ticket.createdAt}>{formatRelative(ticket.createdAt, now)}</time>
                  </div>
                  <TicketStateBadge state={ticket.state} />
                </li>
              ))}
            </ul>
          )}
        </article>
      </div>
      {footer}
    </section>
  )
}

interface PlantSummaryProps {
  plant: Installation
  /** undefined while loading or for plants out of operation, null when the production could not be loaded. */
  production: PlantProduction | null | undefined
  productionLoading: boolean
  request: ConnectionRequest | null
}

function PlantSummary({ plant, production, productionLoading, request }: PlantSummaryProps) {
  const titleId = `portal-plant-${plant.id}`
  const operating = isOperating(plant.state)
  return (
    <article className="card plant-card portal-plant" aria-labelledby={titleId}>
      <header className="plant-card__header">
        <div>
          <h4 id={titleId} className="plant-card__title">
            <Link to={`/my-plants/${plant.id}`} className="plant-card__link">
              {plant.name}
            </Link>
          </h4>
          {plant.plannedPowerKwp != null && <p className="portal-plant__meta">{formatKwp(plant.plannedPowerKwp)}</p>}
        </div>
        <StateBadge state={plant.state} />
      </header>
      {operating ? (
        <>
          {productionLoading && <Skeleton lines={2} label={T.productionLoading} />}
          {!productionLoading && production === null && <p className="portal-plant__note">{T.productionError}</p>}
          {!productionLoading && production && (
            <>
              <p className="portal-plant__power">
                {production.powerKw === null ? (
                  <span className="portal-plant__note">{T.noCurrentPower}</span>
                ) : (
                  <>
                    {formatNumber(production.powerKw, 1)} kW <span>{T.currentPower}</span>
                  </>
                )}
              </p>
              <dl className="plant-facts">
                <div>
                  <dt>{T.producedToday}</dt>
                  <dd>{formatNumber(production.todayKwh, 1)} kWh</dd>
                </div>
              </dl>
            </>
          )}
        </>
      ) : (
        <>
          <Stepper label={T.progressLabel(plant.name)} steps={installationSteps(plant.state)} orientation="vertical" />
          <div className="plant-card__next">
            <span className="plant-card__next-label">{PORTAL_TEXTS.list.nextStep}</span>
            <p>{nextStep(plant, request)}</p>
          </div>
        </>
      )}
    </article>
  )
}
