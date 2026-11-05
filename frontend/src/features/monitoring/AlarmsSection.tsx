import { useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, userMessage } from '../../api/problem'
import { ProductionAreaChart } from '../../components/charts/ProductionAreaChart'
import { DataTable, type Column } from '../../components/DataTable'
import { Dialog } from '../../components/Dialog'
import { KeyValueList } from '../../components/KeyValueList'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/toast'
import { FilterSelect, Toolbar } from '../../components/Toolbar'
import { UpdatedIndicator } from '../../components/UpdatedIndicator'
import { ALARMS_POLL_MS, useLoad } from '../../components/useLoad'
import {
  SEVERITY_TONE,
  alarmChartQuery,
  alarmPeriodText,
  alarmTicketHref,
  filterAlarms,
  kwhText,
  ratioPercent,
  type AlarmSeverityFilter,
  type AlarmStateFilter,
} from './alarmLogic'
import { fetchInstallationAlarms, runAnalysis, type Alarm } from './alarmsApi'
import { formatBucket } from './chartMath'
import { TOLERANCE_RATIO, mergeChartPoints } from './kpis'
import { fetchComparison, fetchProduction } from './productionApi'
import { T } from './texts'

const PAGE_SIZE = 10

function SeverityBadge({ alarm }: { alarm: Alarm }) {
  return <span className={`badge badge--${SEVERITY_TONE[alarm.severity]}`}>{T.alarms.severity[alarm.severity]}</span>
}

function StateBadge({ alarm }: { alarm: Alarm }) {
  return <span className={`badge ${alarm.ongoing ? 'badge--attention' : 'badge--muted'}`}>{alarm.ongoing ? T.alarms.ongoing : T.alarms.ended}</span>
}

const COLUMNS: readonly Column<Alarm>[] = [
  { key: 'severity', header: T.alarms.columns.severity, render: (a) => <SeverityBadge alarm={a} /> },
  { key: 'period', header: T.alarms.columns.period, render: alarmPeriodText },
  { key: 'state', header: T.alarms.columns.state, render: (a) => <StateBadge alarm={a} /> },
  { key: 'ratio', header: T.alarms.columns.ratio, render: (a) => `${ratioPercent(a.medianRatio)} %` },
  { key: 'lost', header: T.alarms.columns.lost, render: (a) => kwhText(a.estimatedLostKwh) },
]

/** The production around the alarm, so the event can be judged in context (#252). */
function AlarmChart({ alarm }: { alarm: Alarm }) {
  const query = alarmChartQuery(alarm)
  const { data, loading, error, reload } = useLoad(
    async (signal) => {
      const [production, comparison] = await Promise.all([
        fetchProduction(alarm.installationId, query, signal),
        fetchComparison(alarm.installationId, query, signal),
      ])
      return { timeZone: production.timeZone, points: mergeChartPoints(production, comparison) }
    },
    JSON.stringify([alarm.id, query]),
  )
  if (loading) return <Skeleton lines={4} label={T.loading} />
  if (error)
    return (
      <div className="kpi__error" role="alert">
        <p>{T.error}</p>
        <button type="button" className="btn btn--sm" onClick={reload}>
          {T.retry}
        </button>
      </div>
    )
  if (!data || data.points.length === 0) return <p className="kpi__detail">{T.empty}</p>
  return (
    <ProductionAreaChart
      title={T.alarms.detail.chart}
      unit={T.units.kwh}
      points={data.points.map((p) => ({ label: formatBucket(p.start, query.granularity, data.timeZone), actual: p.actual, predicted: p.predicted }))}
      underRatio={TOLERANCE_RATIO}
      xLabelCount={5}
      summary={T.alarms.detail.summary(alarmPeriodText(alarm), ratioPercent(alarm.medianRatio))}
    />
  )
}

function AlarmDetail({ alarm, canOpenTicket, canCreateTicket, onClose }: { alarm: Alarm; canOpenTicket: boolean; canCreateTicket: boolean; onClose: () => void }) {
  return (
    <Dialog title={T.alarms.detail.title} onClose={onClose}>
      <div className="alarm-detail">
        <p>{alarm.reason}</p>
        <AlarmChart alarm={alarm} />
        <KeyValueList
          items={[
            { label: T.alarms.detail.installation, value: alarm.installationName },
            { label: T.alarms.columns.period, value: alarmPeriodText(alarm) },
            { label: T.alarms.columns.severity, value: <SeverityBadge alarm={alarm} /> },
            { label: T.alarms.columns.state, value: <StateBadge alarm={alarm} /> },
            { label: T.alarms.detail.ratio, value: `${ratioPercent(alarm.medianRatio)} %` },
            { label: T.alarms.detail.expected, value: kwhText(alarm.expectedKwh) },
            { label: T.alarms.detail.actual, value: kwhText(alarm.actualKwh) },
            { label: T.alarms.columns.lost, value: kwhText(alarm.estimatedLostKwh) },
          ]}
        />
        <div className="alarm-detail__actions">
          <button type="button" className="btn" onClick={onClose}>
            {T.alarms.detail.close}
          </button>
          {alarm.ticketId
            ? canOpenTicket && (
                <Link className="btn btn--primary" to={`/tickets/${alarm.ticketId}`}>
                  {T.alarms.detail.openTicket}
                </Link>
              )
            : canCreateTicket && (
                <Link className="btn btn--primary" to={alarmTicketHref(alarm)}>
                  {T.alarms.detail.createTicket}
                </Link>
              )}
        </div>
      </div>
    </Dialog>
  )
}

interface Props {
  installationId: string
  severity: AlarmSeverityFilter
  state: AlarmStateFilter
  onFilterChange: (changes: { severity?: AlarmSeverityFilter; state?: AlarmStateFilter }) => void
  /** Admin and Operator may run the analysis (policy ProductionAnalyze). */
  canAnalyze: boolean
  canCreateTicket: boolean
  /** The role can open the ticket of an alarm. */
  canOpenTicket: boolean
  /** New predictions were stored, so the production chart should load again. */
  onAnalyzed: () => void
}

/** Underproduction alarms of one installation (#251, #252); refreshed every minute (ADR #222). Filters stay in the URL. */
export function AlarmsSection({ installationId, severity, state, onFilterChange, canAnalyze, canCreateTicket, canOpenTicket, onAnalyzed }: Props) {
  const toast = useToast()
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)
  const [analyzing, setAnalyzing] = useState(false)
  const { data, loading, error, reload, updatedAt, refreshError, refresh } = useLoad(
    (signal) => fetchInstallationAlarms(installationId, signal),
    installationId,
    { intervalMs: ALARMS_POLL_MS },
  )

  const filtered = filterAlarms(data ?? [], severity, state)
  // A refresh can shrink the list below the current page.
  const lastPage = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const currentPage = Math.min(page, lastPage)
  const rows = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE)
  const opened = data?.find((a) => a.id === openId)

  function changeFilter(changes: { severity?: AlarmSeverityFilter; state?: AlarmStateFilter }) {
    setPage(1)
    onFilterChange(changes)
  }

  async function analyze() {
    setAnalyzing(true)
    try {
      const result = await runAnalysis(installationId)
      toast.success(T.alarms.analyzed(Number(result.predictedHours), Number(result.alarms), Number(result.ticketsCreated)))
      reload()
      onAnalyzed()
    } catch (e) {
      // 502 and 503 say the ML service is the problem, not the rest of the system.
      if (e instanceof ApiError && (e.status === 502 || e.status === 503)) toast.error(T.alarms.serviceUnavailable)
      else toast.error(e instanceof ApiError ? userMessage(e) : T.alarms.analyzeFailed)
    } finally {
      setAnalyzing(false)
    }
  }

  return (
    <section className="card prod-anomalies" aria-labelledby="alarms-title">
      <h3 id="alarms-title" className="prod-chart-card__title">
        {T.alarms.title}
      </h3>
      <Toolbar
        aside={
          canAnalyze && (
            <button type="button" className="btn" disabled={analyzing} aria-busy={analyzing} onClick={analyze}>
              {analyzing ? T.alarms.analyzing : T.alarms.analyze}
            </button>
          )
        }
      >
        <FilterSelect
          label={T.alarms.columns.severity}
          value={severity}
          onChange={(value) => changeFilter({ severity: value as AlarmSeverityFilter })}
          options={[
            { value: 'all', label: T.alarms.all },
            { value: 'High', label: T.alarms.severity.High },
            { value: 'Medium', label: T.alarms.severity.Medium },
            { value: 'Low', label: T.alarms.severity.Low },
          ]}
        />
        <FilterSelect
          label={T.alarms.columns.state}
          value={state}
          onChange={(value) => changeFilter({ state: value as AlarmStateFilter })}
          options={[
            { value: 'all', label: T.alarms.all },
            { value: 'ongoing', label: T.alarms.ongoing },
            { value: 'ended', label: T.alarms.ended },
          ]}
        />
      </Toolbar>
      {data && <UpdatedIndicator updatedAt={updatedAt} refreshError={refreshError} onRetry={refresh} />}
      {error ? (
        <div className="kpi__error" role="alert">
          <p>{T.alarms.error}</p>
          <button type="button" className="btn btn--sm" onClick={reload}>
            {T.retry}
          </button>
        </div>
      ) : (
        <DataTable
          columns={COLUMNS}
          rows={rows}
          rowKey={(a) => a.id}
          page={currentPage}
          pageSize={PAGE_SIZE}
          totalCount={filtered.length}
          onPageChange={setPage}
          loading={loading}
          emptyMessage={(data?.length ?? 0) > 0 ? T.alarms.emptyFiltered : T.alarms.empty}
          onRowClick={(a) => setOpenId(a.id)}
        />
      )}
      {opened && <AlarmDetail alarm={opened} canOpenTicket={canOpenTicket} canCreateTicket={canCreateTicket} onClose={() => setOpenId(null)} />}
    </section>
  )
}
