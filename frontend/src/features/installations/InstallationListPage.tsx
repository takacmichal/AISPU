import { useMemo } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { useAuth } from '../../auth/AuthContext'
import { DataTable, type Column } from '../../components/DataTable'
import { useLoad } from '../../components/useLoad'
import { formatDate, formatKwp } from '../../lib/format'
import './installations.css'
import { STATES, STATE_LABELS, T } from './messages'
import { StateBadge } from './StateBadge'

type Installation = components['schemas']['InstallationResponse']

const PAGE_SIZE = 20

/** `/installations`: list with state / customer filters in the URL. The API returns the whole list, so paging is client-side. */
export default function InstallationListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { roles } = useAuth()

  const state = STATES.find((s) => s === params.get('state')) ?? ''
  const customerId = params.get('customerId') ?? ''
  const mine = params.get('mine') === '1'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const isTechnician = roles.includes('Technician')

  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations', {
      params: { query: { state: state || undefined, customerId: customerId || undefined, assignedToMe: mine || undefined } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, JSON.stringify([state, customerId, mine]))

  // Customer names are best effort: the customers API is not readable for every role that can read installations.
  const customers = useLoad(async (signal) => {
    const { data } = await api.GET('/api/customers', { params: { query: { pageSize: 100, sortBy: 'name' } }, signal })
    return data?.items ?? []
  }, 'customers')
  const customerNames = useMemo(() => new Map((customers.data ?? []).map((c) => [c.id, c.displayName])), [customers.data])

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
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

  const rows = data ?? []
  const pageCount = Math.max(1, Math.ceil(rows.length / PAGE_SIZE))
  const safePage = Math.min(page, pageCount)
  const filtered = Boolean(state || customerId || mine)

  return (
    <section>
      <div className="toolbar">
        <h2>{T.list.title}</h2>
      </div>
      <div className="btn-row installations-filters">
        <label>
          {T.list.state}
          <select value={state} onChange={(e) => update({ state: e.target.value, page: null })}>
            <option value="">{T.list.allStates}</option>
            {STATES.map((s) => (
              <option key={s} value={s}>
                {STATE_LABELS[s]}
              </option>
            ))}
          </select>
        </label>
        {customerNames.size > 0 && (
          <label>
            {T.list.customer}
            <select value={customerId} onChange={(e) => update({ customerId: e.target.value, page: null })}>
              <option value="">{T.list.allCustomers}</option>
              {[...customerNames].map(([id, name]) => (
                <option key={id} value={id}>
                  {name}
                </option>
              ))}
            </select>
          </label>
        )}
        {isTechnician && (
          <label className="installations-filters__check">
            <input type="checkbox" checked={mine} onChange={(e) => update({ mine: e.target.checked ? '1' : null, page: null })} />
            {T.list.mine}
          </label>
        )}
      </div>
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
