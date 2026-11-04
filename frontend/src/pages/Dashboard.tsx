import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { fetchSystemInfo, type SystemInfo } from '../api/system'
import { useAuth } from '../auth/AuthContext'
import { canAccess, findItem } from '../auth/navigation'
import { EmptyState } from '../components/EmptyState'
import { Skeleton } from '../components/Skeleton'
import { useLoad } from '../components/useLoad'
import { StateBadge } from '../features/installations/StateBadge'
import { STATES, type InstallationState } from '../features/installations/messages'
import { STATUS_BADGE, STATUS_LABELS, quoteNumber, type QuoteStatus } from '../features/quotes/quoteLabels'
import { formatNumber, formatRelative } from '../lib/format'
import { MESSAGES } from '../lib/messages'
import { DASHBOARD_TEXT as T } from './dashboardTexts'
import { PageHeader } from '../components/PageHeader'

/** The card is not shown: the role cannot use the module (navigation config) or the API answered 403. */
const HIDDEN = 'hidden'
type Hidden = typeof HIDDEN

async function unlessForbidden<D>(request: Promise<{ data?: D; response: Response }>): Promise<D | Hidden> {
  const { data, response } = await request
  if (response.status === 403) return HIDDEN
  if (!data) throw new Error(`HTTP ${response.status}`)
  return data
}

function allowed(path: string, roles: readonly string[]): boolean {
  const item = findItem(path)
  return item !== undefined && canAccess(item, roles)
}

/** Loads a dashboard source only when the role may use it; 403 hides it as well. */
function useSource<D>(path: string, roles: readonly string[], load: (signal: AbortSignal) => Promise<{ data?: D; response: Response }>) {
  const can = allowed(path, roles)
  const result = useLoad<D | Hidden>(async (signal) => (can ? unlessForbidden(load(signal)) : HIDDEN), `${path}|${can}`)
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

interface KpiCardProps<D> {
  source: Source<D>
  href: string
  label: string
  value: (data: D) => number
  detail: (data: D) => ReactNode
}

function KpiCard<D>({ source, href, label, value, detail }: KpiCardProps<D>) {
  if (source.hidden) return null
  return (
    <article className="card kpi" aria-busy={source.loading}>
      <h3 className="kpi__label">
        <Link to={href}>{label}</Link>
      </h3>
      {source.loading && <Skeleton lines={2} label={`${T.kpiLoading} ${label}`} />}
      {source.error && (
        <div className="kpi__error">
          <p>{T.kpiError}</p>
          <button type="button" className="btn btn--sm" onClick={source.reload}>
            {MESSAGES.retry}
          </button>
        </div>
      )}
      {source.data !== undefined && (
        <>
          <p className="kpi__value">{formatNumber(value(source.data), 0)}</p>
          <div className="kpi__detail">{detail(source.data)}</div>
        </>
      )}
    </article>
  )
}

const OPEN_QUOTE_STATUSES: readonly QuoteStatus[] = ['Draft', 'Sent']
const ACTIVE_STATES: readonly InstallationState[] = STATES.filter((s) => s !== 'HandedOver')
const PULSE_SIZE = 6

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

  const customers = useSource('/customers', roles, (signal) =>
    api.GET('/api/customers', { params: { query: { page: 1, pageSize: 1 } }, signal }),
  )
  const quotes = useSource('/quotes', roles, (signal) => api.GET('/api/quotes', { signal }))
  const contracts = useSource('/contracts', roles, (signal) => api.GET('/api/contracts', { signal }))
  const installations = useSource('/installations', roles, (signal) => api.GET('/api/installations', { signal }))

  const kpiVisible = [customers, quotes, contracts, installations].some((s) => !s.hidden)
  const pulseVisible = !quotes.hidden || !installations.hidden
  const pulseLoading = quotes.loading || installations.loading
  const pulseError = (!quotes.hidden && quotes.error) || (!installations.hidden && installations.error)

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
          <KpiCard
            source={customers}
            href="/customers"
            label={T.customers.label}
            value={(d) => Number(d.totalCount)}
            detail={() => T.customers.detail}
          />
          <KpiCard
            source={quotes}
            href="/quotes"
            label={T.openQuotes.label}
            value={(d) => d.filter((q) => OPEN_QUOTE_STATUSES.includes(q.status)).length}
            detail={(d) =>
              T.openQuotes.detail(d.filter((q) => q.status === 'Sent').length, d.filter((q) => q.status === 'Draft').length)
            }
          />
          <KpiCard
            source={contracts}
            href="/contracts"
            label={T.contracts.label}
            value={(d) => d.filter((c) => c.status === 'Draft').length}
            detail={() => T.contracts.detail}
          />
          <KpiCard
            source={installations}
            href="/installations"
            label={T.installations.label}
            value={(d) => d.filter((i) => ACTIVE_STATES.includes(i.state)).length}
            detail={(d) => <InstallationBreakdown states={d.map((i) => i.state)} />}
          />
        </div>
      ) : (
        <EmptyState
          title={T.noKpi.title}
          description={T.noKpi.description}
          action={
            allowed('/my-plants', roles) ? (
              <Link to="/my-plants" className="btn">
                {T.noKpi.myPlants}
              </Link>
            ) : undefined
          }
        />
      )}

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

      <ApiStatus />
    </section>
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
