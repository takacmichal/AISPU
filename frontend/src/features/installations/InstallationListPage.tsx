import { useMemo } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { useAuth } from '../../auth/AuthContext'
import { DataTable, type Column } from '../../components/DataTable'
import { PageHeader } from '../../components/PageHeader'
import { SegmentedControl } from '../../components/SegmentedControl'
import { FilterSelect, SearchField, Toolbar } from '../../components/Toolbar'
import { useLoad } from '../../components/useLoad'
import { formatDate, formatKwp } from '../../lib/format'
import './installations.css'
import { STATES, STATE_LABELS, T } from './messages'
import { StateBadge } from './StateBadge'
import { LIST_GROUPS, listGroup, type ListGroup } from './workflow'

type Installation = components['schemas']['InstallationResponse']
type GroupFilter = ListGroup | 'all'

const PAGE_SIZE = 20

/**
 * `/installations`: list with phase tabs (Všetky / V realizácii / Čaká na DS / V prevádzke), search and filters in the URL.
 * The API applies search, state, customer and technician filters and returns a page with state counts.
 */
export default function InstallationListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { roles, session } = useAuth()

  const state = STATES.find((s) => s === params.get('state')) ?? ''
  const group: GroupFilter = LIST_GROUPS.find((g) => g === params.get('phase')) ?? 'all'
  const q = params.get('q') ?? ''
  const customerId = params.get('customerId') ?? ''
  const mine = params.get('mine') === '1'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const isTechnician = roles.includes('Technician')

  const { data, loading, error } = useLoad(async (signal) => {
    const query = { q: q.trim() || undefined, customerId: customerId || undefined, technicianId: mine ? session?.userId : undefined }
    if (group === 'all') {
      const { data, response } = await api.GET('/api/installations/paged', { params: { query: { ...query, page, pageSize: PAGE_SIZE, state: state || undefined } }, signal })
      if (!data) throw new Error(`HTTP ${response.status}`)
      return { ...data, items: data.items, phaseTotal: Number(data.totalCount) }
    }
    const memberStates = STATES.filter((item) => listGroup(item) === group && (!state || state === item))
    const pageResults = await Promise.all(memberStates.map(async (item) => {
      const target = page * PAGE_SIZE
      const requests = Array.from({ length: Math.ceil(target / 100) }, (_, index) =>
        api.GET('/api/installations/paged', { params: { query: { ...query, state: item, page: index + 1, pageSize: 100 } }, signal }),
      )
      const responses = await Promise.all(requests)
      const first = responses[0]
      if (!first?.data) throw new Error(`HTTP ${first?.response.status ?? 500}`)
      for (const result of responses) if (!result.data) throw new Error(`HTTP ${result.response.status}`)
      return { items: responses.flatMap((result) => result.data?.items ?? []), totalCount: Number(first.data.totalCount), statusCounts: first.data.statusCounts }
    }))
    const items = pageResults.flatMap((result) => result.items).sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
    const phaseTotal = pageResults.reduce((sum, result) => sum + result.totalCount, 0)
    const mergedCounts = pageResults[0]?.statusCounts ?? {}
    return { items: items.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE), totalCount: phaseTotal, phaseTotal, statusCounts: mergedCounts }
  }, JSON.stringify([page, q, state, customerId, mine, session?.userId, group]))

  // Customer names are best effort: the customers API is not readable for every role that can read installations.
  const customers = useLoad(async (signal) => {
    const { data } = await api.GET('/api/customers', { params: { query: { pageSize: 100, sortBy: 'name' } }, signal })
    return data?.items ?? []
  }, 'customers')
  const customerNames = useMemo(() => new Map((customers.data ?? []).map((c) => [c.id, c.displayName])), [customers.data])

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '' || value === 'all') next.delete(key)
      else next.set(key, value)
    }
    setParams(next, { replace: true })
  }

  const columns: Column<Installation>[] = [
    { key: 'name', header: T.list.colName, render: (i) => i.name },
    { key: 'customer', header: T.list.colCustomer, render: (i) => customerNames.get(i.customerId) ?? '–' },
    { key: 'state', header: T.list.colState, render: (i) => <StateBadge state={i.state} /> },
    { key: 'power', header: T.list.colPower, render: (i) => formatKwp(i.plannedPowerKwp) },
    {
      key: 'schedule',
      header: T.list.colSchedule,
      render: (i) => (i.scheduledStart ? `${formatDate(i.scheduledStart)} – ${formatDate(i.scheduledEnd)}` : T.list.notScheduled),
    },
    { key: 'technicians', header: T.list.colTechnicians, render: (i) => i.technicianIds.length },
    { key: 'changed', header: T.list.colChanged, render: (i) => formatDate(i.stateChangedAt) },
  ]

  const statusCounts = data?.statusCounts ?? {}
  const counts = Object.fromEntries(LIST_GROUPS.map((g) => [g, Object.entries(statusCounts).reduce((sum, [s, count]) => sum + (listGroup(s as Installation['state']) === g ? Number(count) : 0), 0)])) as Record<ListGroup, number>
  const totalCount = data?.totalCount ?? 0
  const filtered = Boolean(state || customerId || mine || q.trim() || group !== 'all')

  return (
    <section>
      <PageHeader title={T.list.title} subtitle={T.list.subtitle} />
      <div className="installations-groups">
        <SegmentedControl
          label={T.list.groupsLabel}
          value={group}
          onChange={(value) => update({ phase: value, page: null })}
          options={[
            { value: 'all', label: T.list.groups.all, count: Number(group === 'all' ? totalCount : LIST_GROUPS.reduce((sum, g) => sum + counts[g], 0)) },
            ...LIST_GROUPS.map((g) => ({ value: g, label: T.list.groups[g], count: counts[g] })),
          ]}
        />
      </div>
      <Toolbar
        aside={
          isTechnician && (
            <label className="installations-filters__check">
              <input type="checkbox" checked={mine} onChange={(e) => update({ mine: e.target.checked ? '1' : null, page: null })} />
              {T.list.mine}
            </label>
          )
        }
      >
        <SearchField label={T.list.search} placeholder={T.list.searchPlaceholder} value={q} onChange={(value) => update({ q: value, page: null })} />
        <FilterSelect
          label={T.list.state}
          value={state}
          onChange={(value) => update({ state: value, page: null })}
          options={[{ value: '', label: T.list.allStates }, ...STATES.map((s) => ({ value: s, label: STATE_LABELS[s] }))]}
        />
        {customerNames.size > 0 && (
          <FilterSelect
            label={T.list.customer}
            value={customerId}
            onChange={(value) => update({ customerId: value, page: null })}
            options={[{ value: '', label: T.list.allCustomers }, ...[...customerNames].map(([value, label]) => ({ value, label }))]}
          />
        )}
      </Toolbar>
      <DataTable
        columns={columns}
        rows={data?.items ?? []}
        rowKey={(i) => i.id}
        page={page}
        pageSize={PAGE_SIZE}
        totalCount={Number(data?.phaseTotal ?? (group === 'all' ? totalCount : counts[group]))}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        loading={loading}
        error={error ? T.list.error : null}
        emptyMessage={filtered ? T.list.emptyFiltered : T.list.empty}
        onRowClick={(i) => navigate(`/installations/${i.id}`)}
      />
    </section>
  )
}
