import { useMemo } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { DataTable, type Column } from '../../components/DataTable'
import { PageHeader } from '../../components/PageHeader'
import { FilterSelect, Toolbar } from '../../components/Toolbar'
import { useLoad } from '../../components/useLoad'
import { formatCurrency, formatDate } from '../../lib/format'
import './contracts.css'
import { CONTRACT_STATUS_LABELS, CONTRACT_TEXTS as T, DEPOSIT_STATUS_LABELS } from './texts'

type Contract = components['schemas']['ContractResponse']

const PAGE_SIZE = 20
const STATUSES = ['Draft', 'Signed'] as const

const columns: Column<Contract>[] = [
  {
    key: 'number',
    header: T.columns.number,
    render: (c) => (
      <Link to={`/contracts/${c.id}`} className="mono">
        {c.number}
      </Link>
    ),
  },
  { key: 'status', header: T.columns.status, render: (c) => <span className="badge">{CONTRACT_STATUS_LABELS[c.status] ?? c.status}</span> },
  { key: 'signedOn', header: T.columns.signedOn, render: (c) => formatDate(c.signedOn) },
  {
    key: 'deposit',
    header: T.columns.deposit,
    render: (c) => (
      <span className={c.depositStatus === 'Paid' ? 'badge badge--success' : 'badge'}>{DEPOSIT_STATUS_LABELS[c.depositStatus] ?? c.depositStatus}</span>
    ),
  },
  { key: 'total', header: T.columns.total, render: (c) => <span className="contract-num">{formatCurrency(c.grossTotal, c.currency)}</span> },
  { key: 'createdAt', header: T.columns.createdAt, render: (c) => formatDate(c.createdAt) },
]

/** Contract list; the status filter goes to the API, number search and paging are applied on the loaded list (the API is not paged). */
export default function ContractListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()

  const q = params.get('q') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const statusParam = params.get('status')
  const status = (STATUSES as readonly string[]).includes(statusParam ?? '') ? (statusParam as (typeof STATUSES)[number]) : null

  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/contracts', { params: { query: { status: status ?? undefined } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, status ?? '')

  const filtered = useMemo(() => {
    const needle = q.trim().toLowerCase()
    return (data ?? []).filter((c) => !needle || c.number.toLowerCase().includes(needle))
  }, [data, q])
  const rows = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
  }

  return (
    <section>
      <PageHeader title={T.listTitle} subtitle={T.listSubtitle} />
      <Toolbar>
        <FilterSelect
          label={T.statusFilter}
          value={status ?? ''}
          onChange={(value) => update({ status: value, page: null })}
          options={[{ value: '', label: T.statusAll }, ...STATUSES.map((s) => ({ value: s, label: CONTRACT_STATUS_LABELS[s] }))]}
        />
      </Toolbar>
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(c) => c.id}
        page={page}
        pageSize={PAGE_SIZE}
        totalCount={filtered.length}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        filter={q}
        onFilterChange={(value) => update({ q: value.trim(), page: null })}
        filterLabel={T.filterLabel}
        loading={loading}
        error={error ? T.listError : null}
        emptyMessage={q || status ? T.listEmptyFiltered : T.listEmpty}
        onRowClick={(c) => navigate(`/contracts/${c.id}`)}
      />
    </section>
  )
}
