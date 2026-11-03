import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { DataTable, type Column } from '../../components/DataTable'
import { useLoad } from '../../components/useLoad'
import { formatDate, formatNumber } from '../../lib/format'
import './designs.css'
import { STATUS_BADGES, STATUS_LABELS, TEXT } from './labels'
import { formatKwh } from './units'

type Design = components['schemas']['FveDesignResponse']
type Status = components['schemas']['FveDesignStatus']

const PAGE_SIZE = 20

const columns: Column<Design>[] = [
  {
    key: 'status',
    header: 'Stav',
    render: (d) => (
      <Link to={`/designs/${d.id}`} onClick={(e) => e.stopPropagation()}>
        <span className={`badge ${STATUS_BADGES[d.status] ?? ''}`}>{STATUS_LABELS[d.status] ?? d.status}</span>
      </Link>
    ),
  },
  { key: 'consumption', header: 'Ročná spotreba', render: (d) => formatKwh(d.annualConsumptionKwh) },
  { key: 'planes', header: 'Strešné plochy', render: (d) => formatNumber(d.roofPlanes.length, 0) },
  { key: 'notes', header: 'Poznámka', render: (d) => d.requestNotes ?? '–' },
  { key: 'createdAt', header: 'Vytvorený', render: (d) => formatDate(d.createdAt) },
  { key: 'updatedAt', header: 'Upravený', render: (d) => formatDate(d.updatedAt) },
]

/** Design list (/designs); the status filter and page live in the URL. The API returns the full list, paging is client side. */
export default function DesignListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const status = (Object.keys(STATUS_LABELS) as Status[]).find((s) => s === params.get('status'))
  const page = Math.max(1, Number(params.get('page')) || 1)

  const { data, loading, error } = useLoad(async (signal) => {
    const { data: items, response } = await api.GET('/api/designs', { params: { query: { status } }, signal })
    if (!items) throw new Error(`HTTP ${response.status}`)
    return items
  }, status ?? 'all')

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
  }

  const all = data ?? []
  const pageCount = Math.max(1, Math.ceil(all.length / PAGE_SIZE))
  const current = Math.min(page, pageCount)
  const rows = all.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE)

  return (
    <section>
      <div className="toolbar">
        <h2>{TEXT.listTitle}</h2>
        <label className="data-table__filter">
          {TEXT.statusFilter}
          <select value={status ?? ''} onChange={(e) => update({ status: e.target.value, page: null })}>
            <option value="">{TEXT.allStatuses}</option>
            {Object.entries(STATUS_LABELS).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
      </div>
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(d) => d.id}
        page={current}
        pageSize={PAGE_SIZE}
        totalCount={all.length}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        loading={loading}
        error={error ? TEXT.listError : null}
        emptyMessage={status ? TEXT.listEmptyFiltered : TEXT.listEmpty}
        onRowClick={(d) => navigate(`/designs/${d.id}`)}
      />
    </section>
  )
}
