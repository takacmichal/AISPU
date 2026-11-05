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
 * The API returns the whole list, so state, phase, search and paging are applied on the client; customer and
 * "my installations" go to the API.
 */
export default function InstallationListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { roles } = useAuth()

  const state = STATES.find((s) => s === params.get('state')) ?? ''
  const group: GroupFilter = LIST_GROUPS.find((g) => g === params.get('phase')) ?? 'all'
  const q = params.get('q') ?? ''
  const customerId = params.get('customerId') ?? ''
  const mine = params.get('mine') === '1'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const isTechnician = roles.includes('Technician')

  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations', {
      params: { query: { customerId: customerId || undefined, assignedToMe: mine || undefined } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, JSON.stringify([customerId, mine]))

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

  const all = data ?? []
  const needle = q.trim().toLowerCase()
  const searched = all.filter(
    (i) =>
      (!state || i.state === state) &&
      (!needle || i.name.toLowerCase().includes(needle) || (customerNames.get(i.customerId) ?? '').toLowerCase().includes(needle)),
  )
  const counts = Object.fromEntries(LIST_GROUPS.map((g) => [g, searched.filter((i) => listGroup(i.state) === g).length])) as Record<
    ListGroup,
    number
  >
  const rows = group === 'all' ? searched : searched.filter((i) => listGroup(i.state) === group)
  const pageCount = Math.max(1, Math.ceil(rows.length / PAGE_SIZE))
  const safePage = Math.min(page, pageCount)
  const filtered = Boolean(state || customerId || mine || needle || group !== 'all')

  return (
    <section>
      <PageHeader title={T.list.title} subtitle={T.list.subtitle} />
      <div className="installations-groups">
        <SegmentedControl
          label={T.list.groupsLabel}
          value={group}
          onChange={(value) => update({ phase: value, page: null })}
          options={[
            { value: 'all', label: T.list.groups.all, count: searched.length },
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
        rows={rows.slice((safePage - 1) * PAGE_SIZE, safePage * PAGE_SIZE)}
        rowKey={(i) => i.id}
        page={safePage}
        pageSize={PAGE_SIZE}
        totalCount={rows.length}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        loading={loading}
        error={error ? T.list.error : null}
        emptyMessage={filtered ? T.list.emptyFiltered : T.list.empty}
        onRowClick={(i) => navigate(`/installations/${i.id}`)}
      />
    </section>
  )
}
