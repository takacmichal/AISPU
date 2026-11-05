import { useMemo } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import { DataTable, type Column } from '../../components/DataTable'
import { KpiCard } from '../../components/KpiCard'
import { PageHeader } from '../../components/PageHeader'
import { ProgressBar } from '../../components/ProgressBar'
import { SegmentedControl } from '../../components/SegmentedControl'
import { FilterSelect, SearchField, Toolbar } from '../../components/Toolbar'
import { useLoad } from '../../components/useLoad'
import { formatRelative } from '../../lib/format'
import './connection.css'
import { DEADLINE_WARNING_DAYS, deadlineInfo, deadlineProgress, isOpenRequest, type ConnectionRequest } from './model'
import { CONNECTION_TEXTS, STATE_LABELS, TYPE_LABELS } from './texts'

const T = CONNECTION_TEXTS.list
const PAGE_SIZE = 20
const DEADLINE_FILTERS = ['all', 'warning', 'overdue'] as const
type DeadlineFilter = (typeof DEADLINE_FILTERS)[number]

interface Row {
  request: ConnectionRequest
  installation: string
}

const STATE_BADGE: Record<string, string> = {
  Submitted: 'badge badge--info',
  InfoRequested: 'badge badge--attention',
  Rejected: 'badge badge--error',
  ContractOffered: 'badge badge--attention',
  ContractSigned: 'badge badge--info',
  ContractSubmitted: 'badge badge--info',
  Concluded: 'badge badge--success',
}

function Deadline({ request, today }: { request: ConnectionRequest; today: Date }) {
  const progress = deadlineProgress(request, today)
  const info = deadlineInfo(request, today)
  if (!progress || !info) return <span className="muted">{T.noDeadline}</span>
  const text = info.level === 'overdue' ? T.late(info.days) : T.left(info.days)
  return (
    <div className={`deadline deadline--${info.level}`}>
      <ProgressBar label={T.deadlineLabel(request.eicCode)} value={progress.elapsed} max={progress.total} valueText={text} />
    </div>
  )
}

/**
 * `/connection-requests`: all connection requests with the distributor deadline (#86) as a progress bar and KPIs
 * for open, soon due and overdue requests. Filters live in the URL; the API returns the whole list newest first.
 */
export default function ConnectionRequestListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const today = useMemo(() => new Date(), [])

  const q = params.get('q') ?? ''
  const state = params.get('state') ?? ''
  const deadline = (DEADLINE_FILTERS as readonly string[]).includes(params.get('deadline') ?? '') ? (params.get('deadline') as DeadlineFilter) : 'all'
  const page = Math.max(1, Number(params.get('page')) || 1)

  const requests = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/connection-requests', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data as ConnectionRequest[]
  }, 'connection-requests')

  // Names are a convenience: without them the list still shows the EIC.
  const installations = useLoad(async (signal) => {
    const { data } = await api.GET('/api/installations', { signal })
    return new Map((data ?? []).map((i) => [i.id, i.name]))
  }, 'connection-installations')

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '' || value === 'all') next.delete(key)
      else next.set(key, value)
    }
    setParams(next, { replace: true })
  }

  const all = requests.data ?? []
  const levels = all.map((r) => deadlineInfo(r, today)?.level)
  const open = all.filter(isOpenRequest).length
  const warning = levels.filter((l) => l === 'warning').length
  const overdue = levels.filter((l) => l === 'overdue').length

  const rows: Row[] = all
    .map((request) => ({ request, installation: installations.data?.get(request.installationId) ?? T.unknownInstallation }))
    .filter(({ request, installation }) => {
      if (state && request.state !== state) return false
      if (deadline !== 'all' && deadlineInfo(request, today)?.level !== deadline) return false
      const needle = q.trim().toLowerCase()
      return !needle || request.eicCode.toLowerCase().includes(needle) || installation.toLowerCase().includes(needle)
    })
  const pageCount = Math.max(1, Math.ceil(rows.length / PAGE_SIZE))
  const current = Math.min(page, pageCount)

  const columns: Column<Row>[] = [
    {
      key: 'installation',
      header: T.colInstallation,
      render: ({ request, installation }) => <Link to={`/connection-requests/${request.id}`}>{installation}</Link>,
    },
    { key: 'eic', header: T.colEic, render: ({ request }) => <span className="mono">{request.eicCode}</span> },
    { key: 'type', header: T.colType, render: ({ request }) => TYPE_LABELS[request.type] ?? request.type },
    {
      key: 'state',
      header: T.colState,
      render: ({ request }) => <span className={STATE_BADGE[request.state] ?? 'badge'}>{STATE_LABELS[request.state] ?? request.state}</span>,
    },
    { key: 'deadline', header: T.colDeadline, render: ({ request }) => <Deadline request={request} today={today} /> },
    {
      key: 'updated',
      header: T.colUpdated,
      render: ({ request }) => <time dateTime={request.updatedAt}>{formatRelative(request.updatedAt, today)}</time>,
    },
  ]

  const kpi = (label: string) => ({
    loading: requests.loading ? `${T.loading} ${label}` : undefined,
    error: requests.error ? { message: T.error, onRetry: requests.reload } : undefined,
  })

  return (
    <section>
      <PageHeader title={T.title} subtitle={T.subtitle} />
      <div className="kpi-grid connection-kpis" role="group" aria-label={T.kpiRegion}>
        <KpiCard label={T.open} {...kpi(T.open)} value={open} detail={T.openDetail} />
        <KpiCard
          label={T.warning}
          {...kpi(T.warning)}
          value={warning}
          detail={T.warningDetail(DEADLINE_WARNING_DAYS)}
          trend={warning > 0 ? { text: T.deadlineWarning, tone: 'warning' } : undefined}
        />
        <KpiCard
          label={T.overdue}
          {...kpi(T.overdue)}
          value={overdue}
          detail={T.overdueDetail}
          trend={overdue > 0 ? { text: T.deadlineOverdue, tone: 'error' } : undefined}
        />
      </div>
      <Toolbar
        aside={
          <SegmentedControl
            label={T.deadlineFilter}
            size="sm"
            value={deadline}
            onChange={(value) => update({ deadline: value, page: null })}
            options={[
              { value: 'all', label: T.deadlineAll },
              { value: 'warning', label: T.deadlineWarning, count: warning },
              { value: 'overdue', label: T.deadlineOverdue, count: overdue },
            ]}
          />
        }
      >
        <SearchField label={T.search} placeholder={T.searchPlaceholder} value={q} onChange={(value) => update({ q: value, page: null })} />
        <FilterSelect
          label={T.stateFilter}
          value={state}
          onChange={(value) => update({ state: value, page: null })}
          options={[{ value: '', label: T.allStates }, ...Object.entries(STATE_LABELS).map(([value, label]) => ({ value, label }))]}
        />
      </Toolbar>
      <DataTable
        columns={columns}
        rows={rows.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE)}
        rowKey={(row) => row.request.id}
        page={current}
        pageSize={PAGE_SIZE}
        totalCount={rows.length}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        loading={requests.loading}
        error={requests.error ? T.error : null}
        emptyMessage={q || state || deadline !== 'all' ? T.emptyFiltered : T.empty}
        onRowClick={(row) => navigate(`/connection-requests/${row.request.id}`)}
      />
    </section>
  )
}
