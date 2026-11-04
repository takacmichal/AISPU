import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { ApiError } from '../api/problem'
import { fetchSystemInfo, type SystemInfo } from '../api/system'
import { useAuth } from '../auth/AuthContext'
import { canAccess, findItem } from '../auth/navigation'
import { EmptyState } from '../components/EmptyState'
import { KpiCard } from '../components/KpiCard'
import { PageHeader } from '../components/PageHeader'
import { Skeleton } from '../components/Skeleton'
import { useLoad } from '../components/useLoad'
import { StateBadge } from '../features/installations/StateBadge'
import { STATES, type InstallationState } from '../features/installations/messages'
import PortalHome from '../features/portal/PortalHome'
import { STATUS_BADGE, STATUS_LABELS, quoteNumber, type QuoteStatus } from '../features/quotes/quoteLabels'
import { PriorityBadge } from '../features/tickets/TicketBadges'
import { isOpenTicket, listTickets, type TicketList } from '../features/tickets/ticketsApi'
import { formatNumber, formatRelative } from '../lib/format'
import { activeAlarms, isCustomerOnly } from './dashboardModel'
import { DASHBOARD_TEXT as T } from './dashboardTexts'
import './dashboard.css'

/** The card is not shown: the role cannot use the module (navigation config) or the API answered 403. */
const HIDDEN = 'hidden'
type Hidden = typeof HIDDEN

async function unlessForbidden<D>(request: Promise<{ data?: D; response: Response }>): Promise<D | Hidden> {
  const { data, response } = await request
  if (response.status === 403) return HIDDEN
  if (!data) throw new Error(`HTTP ${response.status}`)
  return data
}

/** Same for the hand-written clients that throw ApiError (tickets). */
async function unlessForbiddenError<D>(request: Promise<D>): Promise<D | Hidden> {
  try {
    return await request
  } catch (error) {
    if (error instanceof ApiError && error.status === 403) return HIDDEN
    throw error
  }
}

function allowed(path: string, roles: readonly string[]): boolean {
  const item = findItem(path)
  return item !== undefined && canAccess(item, roles)
}

/** Loads a dashboard source only when the role may use it; 403 hides it as well. */
function useSource<D>(path: string, roles: readonly string[], load: (signal: AbortSignal) => Promise<D | Hidden>) {
  const can = allowed(path, roles)
  const result = useLoad<D | Hidden>(async (signal) => (can ? load(signal) : HIDDEN), `${path}|${can}`)
  const hidden = !can || result.data === HIDDEN
  return {
    hidden,
    loading: !hidden && result.loading,
    error: result.error,
    data: hidden ? undefined : (result.data as D | undefined),
    reload: result.reload,
  }
}

type Source<D> = ReturnType<typeof useSource<D>>

interface SourceKpiProps<D> {
  source: Source<D>
  href: string
  label: string
  value: (data: D) => number
  detail: (data: D) => ReactNode
}

/** KPI card fed by one API source: skeleton while loading, retry on error, hidden for the wrong role. */
function SourceKpi<D>({ source, href, label, value, detail }: SourceKpiProps<D>) {
  if (source.hidden) return null
  const data = source.data
  return (
    <KpiCard
      href={href}
      label={label}
      loading={source.loading ? `${T.kpiLoading} ${label}` : undefined}
      error={source.error ? { message: T.kpiError, onRetry: source.reload } : undefined}
      value={data === undefined ? null : formatNumber(value(data), 0)}
      detail={data === undefined ? null : detail(data)}
    />
  )
}

const OPEN_QUOTE_STATUSES: readonly QuoteStatus[] = ['Draft', 'Sent']
const ACTIVE_STATES: readonly InstallationState[] = STATES.filter((s) => s !== 'HandedOver')
const PULSE_SIZE = 6
const ALARM_LIST_SIZE = 5
/** Largest page the tickets API returns (TicketService.MaxPageSize); the API sorts newest first. */
const TICKET_SAMPLE = 100

interface PulseItem {
  key: string
  href: string
  kind: string
  title: string
  badge: ReactNode
  updatedAt: string
}

function ApiStatus() {
  const [state, setState] = useState<{ status: 'loading' } | { status: 'error' } | { status: 'ok'; info: SystemInfo }>({
    status: 'loading',
  })

  useEffect(() => {
    const controller = new AbortController()
    fetchSystemInfo(controller.signal)
      .then((info) => setState({ status: 'ok', info }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.error(error)
          setState({ status: 'error' })
        }
      })
    return () => controller.abort()
  }, [])

  return (
    <p className="dashboard__footer" role="status">
      <span className="sr-only">{T.api.label}: </span>
      {state.status === 'loading' && T.api.loading}
      {state.status === 'error' && <span className="dashboard__footer-error">{T.api.error}</span>}
      {state.status === 'ok' && (
        <>
          <span className="status-dot" aria-hidden="true" />
          {state.info.name} <span className="mono">v{state.info.version}</span> · {state.info.environment}
        </>
      )}
    </p>
  )
}

export default function Dashboard() {
  const { roles } = useAuth()
  if (isCustomerOnly(roles)) return <PortalHome footer={<ApiStatus />} />
  return <InternalDashboard roles={roles} />
}

function InternalDashboard({ roles }: { roles: readonly string[] }) {
  const customers = useSource('/customers', roles, (signal) =>
    unlessForbidden(api.GET('/api/customers', { params: { query: { page: 1, pageSize: 1 } }, signal })),
  )
  const quotes = useSource('/quotes', roles, (signal) => unlessForbidden(api.GET('/api/quotes', { signal })))
  const contracts = useSource('/contracts', roles, (signal) => unlessForbidden(api.GET('/api/contracts', { signal })))
  const installations = useSource('/installations', roles, (signal) => unlessForbidden(api.GET('/api/installations', { signal })))
  const tickets = useSource<TicketList>('/tickets', roles, (signal) =>
    unlessForbiddenError(listTickets({ pageSize: TICKET_SAMPLE }, signal)),
  )

  const kpiVisible = [tickets, customers, quotes, contracts, installations].some((s) => !s.hidden)
  const pulseVisible = !quotes.hidden || !installations.hidden
  const pulseLoading = quotes.loading || installations.loading
  const pulseError = (!quotes.hidden && quotes.error) || (!installations.hidden && installations.error)
  const partial = (list: TicketList) => (list.totalCount > list.items.length ? <p>{T.tickets.partial(list.items.length)}</p> : null)

  const now = new Date()
  const pulse: PulseItem[] = [
    ...(quotes.data ?? []).map((q) => ({
      key: `q-${q.id}`,
      href: `/quotes/${q.id}`,
      kind: T.pulse.quote,
      title: quoteNumber(q.id),
      badge: <span className={STATUS_BADGE[q.status]}>{STATUS_LABELS[q.status]}</span>,
      updatedAt: q.updatedAt,
    })),
    ...(installations.data ?? []).map((i) => ({
      key: `i-${i.id}`,
      href: `/installations/${i.id}`,
      kind: T.pulse.installation,
      title: i.name,
      badge: <StateBadge state={i.state} />,
      updatedAt: i.updatedAt,
    })),
  ]
    .sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
    .slice(0, PULSE_SIZE)

  return (
    <section className="dashboard">
      <PageHeader title={T.title} subtitle={T.subtitle} />

      {kpiVisible ? (
        <div className="kpi-grid" role="group" aria-label={T.kpiRegion}>
          <SourceKpi
            source={tickets}
            href="/tickets?origin=AnomalyDetected"
            label={T.alarms.label}
            value={(d) => activeAlarms(d.items).length}
            detail={(d) => (
              <>
                <p>{T.alarms.detail(activeAlarms(d.items).filter((t) => t.priority === 'High').length)}</p>
                {partial(d)}
              </>
            )}
          />
          <SourceKpi
            source={tickets}
            href="/tickets"
            label={T.tickets.label}
            value={(d) => d.items.filter(isOpenTicket).length}
            detail={(d) => (
              <>
                <p>{T.tickets.detail(d.items.filter((t) => t.state === 'New').length)}</p>
                {partial(d)}
              </>
            )}
          />
          <SourceKpi
            source={quotes}
            href="/quotes"
            label={T.openQuotes.label}
            value={(d) => d.filter((q) => OPEN_QUOTE_STATUSES.includes(q.status)).length}
            detail={(d) =>
              T.openQuotes.detail(d.filter((q) => q.status === 'Sent').length, d.filter((q) => q.status === 'Draft').length)
            }
          />
          <SourceKpi
            source={contracts}
            href="/contracts"
            label={T.contracts.label}
            value={(d) => d.filter((c) => c.status === 'Draft').length}
            detail={() => T.contracts.detail}
          />
          <SourceKpi
            source={installations}
            href="/installations"
            label={T.installations.label}
            value={(d) => d.filter((i) => ACTIVE_STATES.includes(i.state)).length}
            detail={(d) => <InstallationBreakdown states={d.map((i) => i.state)} />}
          />
          <SourceKpi
            source={customers}
            href="/customers"
            label={T.customers.label}
            value={(d) => Number(d.totalCount)}
            detail={() => T.customers.detail}
          />
        </div>
      ) : (
        <EmptyState title={T.noKpi.title} description={T.noKpi.description} />
      )}

      {(pulseVisible || !tickets.hidden) && (
        <div className="dashboard__columns">
          {pulseVisible && (
            <article className="card pulse" aria-labelledby="pulse-title">
              <header className="pulse__header">
                <h3 id="pulse-title" className="pulse__title">
                  {T.pulse.title}
                </h3>
                <p className="pulse__subtitle">{T.pulse.subtitle}</p>
              </header>
              {pulseLoading ? (
                <Skeleton lines={4} label={T.pulse.loading} />
              ) : pulse.length > 0 ? (
                <ul className="pulse__list">
                  {pulse.map((item) => (
                    <li key={item.key} className="pulse__item">
                      <span className="pulse__kind">{item.kind}</span>
                      <Link to={item.href} className="pulse__link">
                        {item.title}
                      </Link>
                      {item.badge}
                      <time className="pulse__time" dateTime={item.updatedAt}>
                        {formatRelative(item.updatedAt, now)}
                      </time>
                    </li>
                  ))}
                </ul>
              ) : pulseError ? (
                <p className="pulse__empty">{T.pulse.error}</p>
              ) : (
                <p className="pulse__empty">{T.pulse.empty}</p>
              )}
            </article>
          )}
          {!tickets.hidden && <AlarmList source={tickets} now={now} />}
        </div>
      )}

      <ApiStatus />
    </section>
  )
}

/** Side block "Posledné alarmy": open tickets created by anomaly detection. */
function AlarmList({ source, now }: { source: Source<TicketList>; now: Date }) {
  const A = T.alarmList
  const alarms = source.data ? activeAlarms(source.data.items).slice(0, ALARM_LIST_SIZE) : []
  return (
    <article className="card pulse alarm-list" aria-labelledby="alarms-title">
      <header className="pulse__header alarm-list__header">
        <div>
          <h3 id="alarms-title" className="pulse__title">
            {A.title}
          </h3>
          <p className="pulse__subtitle">{A.subtitle}</p>
        </div>
        <Link to="/tickets?origin=AnomalyDetected" className="alarm-list__all">
          {A.all}
        </Link>
      </header>
      {source.loading && <Skeleton lines={3} label={A.loading} />}
      {source.error && <p className="pulse__empty">{A.error}</p>}
      {source.data && alarms.length === 0 && <EmptyState title={A.emptyTitle} description={A.emptyText} />}
      {alarms.length > 0 && (
        <ul className="alarm-list__items">
          {alarms.map((alarm) => (
            <li key={alarm.id} className="alarm-list__item">
              <span className="alarm-list__icon" aria-hidden="true" />
              <div className="alarm-list__text">
                <Link to={`/tickets/${alarm.id}`} className="pulse__link">
                  {alarm.title}
                </Link>
                <time className="pulse__time" dateTime={alarm.createdAt}>
                  {formatRelative(alarm.createdAt, now)}
                </time>
              </div>
              <PriorityBadge priority={alarm.priority} />
            </li>
          ))}
        </ul>
      )}
    </article>
  )
}

/** Installation counts per in-progress state, most frequent first. */
function InstallationBreakdown({ states }: { states: InstallationState[] }) {
  const counts = new Map<InstallationState, number>()
  for (const state of states) if (state !== 'HandedOver') counts.set(state, (counts.get(state) ?? 0) + 1)
  const handedOver = states.filter((s) => s === 'HandedOver').length
  const rows = [...counts.entries()].sort((a, b) => b[1] - a[1]).slice(0, 3)
  return (
    <>
      {rows.length > 0 && (
        <ul className="kpi__breakdown">
          {rows.map(([state, count]) => (
            <li key={state}>
              <StateBadge state={state} /> <span className="kpi__count">{count}</span>
            </li>
          ))}
        </ul>
      )}
      <p>{T.installations.detail(handedOver)}</p>
    </>
  )
}
