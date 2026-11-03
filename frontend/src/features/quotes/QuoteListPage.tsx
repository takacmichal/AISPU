import './quotes.css'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { DataTable, type Column } from '../../components/DataTable'
import { useLoad } from '../../components/useLoad'
import { formatCurrency, formatDate } from '../../lib/format'
import { STATUSES, STATUS_BADGE, STATUS_LABELS, TEXT, WRITE_ROLES, quoteNumber, type Quote, type QuoteStatus } from './quoteLabels'
import { useCustomerOptions } from './useCustomerNames'

const PAGE_SIZE = 20

/** Quote list; status, customer and page live in the URL. The API returns the full list, so paging is done here. */
export default function QuoteListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { roles } = useAuth()
  const canWrite = roles.some((r) => WRITE_ROLES.includes(r))
  const { customers } = useCustomerOptions()

  const statusParam = params.get('status') ?? ''
  const status = (STATUSES as string[]).includes(statusParam) ? (statusParam as QuoteStatus) : ''
  const customerId = params.get('customerId') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)

  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/quotes', {
      params: { query: { status: status || undefined, customerId: customerId || undefined } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, `${status}|${customerId}`)

  const names = new Map(customers.map((c) => [c.id, c.name]))
  const columns: Column<Quote>[] = [
    { key: 'number', header: 'Ponuka', render: (q) => quoteNumber(q.id) },
    { key: 'customer', header: 'Zákazník', render: (q) => names.get(q.customerId) ?? TEXT.unknownCustomer },
    { key: 'status', header: 'Stav', render: (q) => <span className={STATUS_BADGE[q.status]}>{STATUS_LABELS[q.status]}</span> },
    { key: 'validUntil', header: TEXT.validUntil, render: (q) => formatDate(q.validUntil) },
    { key: 'gross', header: TEXT.gross, render: (q) => formatCurrency(q.grossTotal, q.currency) },
    { key: 'createdAt', header: 'Vytvorená', render: (q) => formatDate(q.createdAt) },
  ]

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (!value) next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
  }

  const rows = data ?? []
  const pageRows = rows.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  const filtered = status !== '' || customerId !== ''

  return (
    <section>
      <div className="toolbar">
        <h2>{TEXT.listTitle}</h2>
        {canWrite && (
          <Link to={customerId ? `/quotes/new?customerId=${customerId}` : '/quotes/new'} className="btn btn--primary">
            {TEXT.newQuote}
          </Link>
        )}
      </div>
      <div className="quote-filters">
        <label>
          {TEXT.statusFilter}
          <select value={status} onChange={(e) => update({ status: e.target.value, page: null })}>
            <option value="">{TEXT.all}</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {STATUS_LABELS[s]}
              </option>
            ))}
          </select>
        </label>
        <label>
          {TEXT.customerFilter}
          <select value={customerId} onChange={(e) => update({ customerId: e.target.value, page: null })}>
            <option value="">{TEXT.all}</option>
            {customers.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
      </div>
      <DataTable
        columns={columns}
        rows={pageRows}
        rowKey={(q) => q.id}
        page={page}
        pageSize={PAGE_SIZE}
        totalCount={rows.length}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        loading={loading}
        error={error ? TEXT.listError : null}
        emptyMessage={filtered ? TEXT.listEmptyFiltered : TEXT.listEmpty}
        onRowClick={(q) => navigate(`/quotes/${q.id}`)}
      />
    </section>
  )
}
