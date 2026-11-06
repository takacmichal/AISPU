import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { canAccess, findItem } from '../auth/navigation'
import { EmptyState } from '../components/EmptyState'
import { KpiCard } from '../components/KpiCard'
import { PageHeader } from '../components/PageHeader'
import { UpdatedIndicator } from '../components/UpdatedIndicator'
import { useDashboardSummary, useOngoingAlarms, useTicketSummary, useTodayProduction } from '../features/dashboard/data'
import { ProductionBarsCard } from '../features/dashboard/ProductionBarsCard'
import { RecentAlarmsCard } from '../features/dashboard/RecentAlarmsCard'
import PortalHome from '../features/portal/PortalHome'
import { BellIcon } from '../layout/NavIcon'
import { NavIcon } from '../layout/NavIcon'
import { formatCurrency, formatNumber } from '../lib/format'
import { DASHBOARD_TEXT as T } from './dashboardTexts'
import '../features/dashboard/dashboard.css'

function allowed(path: string, roles: readonly string[]): boolean {
  const item = findItem(path)
  return item !== undefined && canAccess(item, roles)
}

/** Customers get the portal home with their own plants instead of company KPIs. */
const isCustomerOnly = (roles: readonly string[]) => roles.length > 0 && roles.every((r) => r === 'Customer')

function trendOf(today: number, yesterday: number) {
  if (yesterday <= 0) return undefined
  const pct = Math.round(((today - yesterday) / yesterday) * 100)
  return { text: `${pct >= 0 ? '+' : ''}${pct} % vs. včera`, tone: pct >= 0 ? ('success' as const) : ('warning' as const) }
}

function DateLine() {
  const text = new Intl.DateTimeFormat('sk-SK', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).format(new Date())
  return <>{T.subtitle(text)}</>
}

/** Oldest of the loads that finished, so the indicator never claims more than every card has. */
function latestUpdate(times: (Date | null)[]): Date | null {
  const done = times.filter((t): t is Date => t !== null)
  return done.length === 0 ? null : new Date(Math.min(...done.map((t) => t.getTime())))
}

const countBy = (alarms: readonly { ticketPriority: string }[], priority: string) => alarms.filter((a) => a.ticketPriority === priority).length

export default function Dashboard() {
  const { roles } = useAuth()
  const canMonitor = allowed('/monitoring', roles)
  // Customers see their own plants and tickets in the portal, not the company-wide dashboard.
  const canTickets = allowed('/tickets', roles) && roles.some((role) => role !== 'Customer')
  const canQuotes = allowed('/quotes', roles)
  const production = useTodayProduction(true, canMonitor)
  const tickets = useTicketSummary(canTickets)
  const summary = useDashboardSummary(canTickets || canQuotes)
  // The alarms link to monitoring, so only roles that can open it see them.
  const alarms = useOngoingAlarms(canMonitor)
  const ongoing = alarms.data ?? []

  if (isCustomerOnly(roles)) return <PortalHome />

  if (!canMonitor && !canTickets && !canQuotes) {
    return (
      <section className="dashboard">
        <PageHeader title={T.title} subtitle={<DateLine />} />
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
      </section>
    )
  }

  const p = production.data
  const summaryTickets = summary.data?.tickets
  const t = summaryTickets ? { open: Number(summaryTickets.open), waitingTriage: Number(summaryTickets.new) } : null
  const q = summary.data?.quotes ? { waiting: Number(summary.data.quotes.sent), valueGross: Number(summary.data.quotes.sentGrossTotal), open: Number(summary.data.quotes.open) } : null

  return (
    <section className="dashboard">
      <PageHeader
        title={T.title}
        subtitle={<DateLine />}
        actions={
          <>
            {canMonitor && (
              <Link to="/monitoring" className="btn">
                {T.export}
              </Link>
            )}
            {canQuotes && (
              <Link to="/quotes" className="btn btn--primary">
                + {T.newQuote}
              </Link>
            )}
          </>
        }
      />
      <UpdatedIndicator
        updatedAt={latestUpdate([production.updatedAt, tickets.updatedAt, summary.updatedAt])}
        refreshError={production.refreshError || tickets.refreshError || summary.refreshError}
        onRetry={() => {
          production.refresh()
          tickets.refresh()
          summary.refresh()
        }}
      />

      <div className="kpi-grid" role="group" aria-label={T.kpiRegion}>
        {canMonitor && (
          <KpiCard
            icon={<NavIcon path="/monitoring" />}
            trend={p ? trendOf(p.todayKwh, p.yesterdayKwh) : undefined}
            value={p ? formatNumber(p.todayKwh, 0) : ''}
            unit=" kWh"
            label={T.production.label}
            href="/monitoring"
            detail={p ? T.production.detail(p.operating, p.asOf) : undefined}
            loading={production.loading ? T.kpiLoading : undefined}
            error={production.error ? { message: T.kpiError, onRetry: production.reload } : undefined}
          />
        )}
        {canMonitor && (
          <KpiCard
            icon={<BellIcon />}
            value={alarms.data ? ongoing.length : ''}
            label={T.alarms.label}
            href="/monitoring"
            detail={alarms.data ? T.alarms.detail(countBy(ongoing, 'High'), countBy(ongoing, 'Medium'), countBy(ongoing, 'Low')) : undefined}
            loading={alarms.loading ? T.kpiLoading : undefined}
            error={alarms.error ? { message: T.kpiError, onRetry: alarms.reload } : undefined}
          />
        )}
        {canTickets && (
          <KpiCard
            icon={<NavIcon path="/tickets" />}
            value={t ? t.open : ''}
            label={T.tickets.label}
            href="/tickets"
            detail={t ? T.tickets.detail(t.waitingTriage) : undefined}
            loading={summary.loading ? T.kpiLoading : undefined}
            error={summary.error ? { message: T.kpiError, onRetry: summary.reload } : undefined}
          />
        )}
        {canQuotes && (
          <KpiCard
            icon={<NavIcon path="/quotes" />}
            value={q ? q.waiting : ''}
            label={T.quotes.label}
            href="/quotes"
            detail={q ? T.quotes.detail(formatCurrency(q.valueGross, 'EUR')) : undefined}
            loading={summary.loading ? T.kpiLoading : undefined}
            error={summary.error ? { message: T.kpiError, onRetry: summary.reload } : undefined}
          />
        )}
      </div>

      <div className="dashboard__grid">
        {canMonitor && <ProductionBarsCard />}
        {canTickets && <RecentAlarmsCard summary={tickets.loading ? undefined : { items: tickets.data?.items ?? [] }} alarms={ongoing} />}
      </div>
    </section>
  )
}
