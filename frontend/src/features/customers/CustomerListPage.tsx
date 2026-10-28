import { useEffect, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { DataTable, type Column, type SortState } from '../../components/DataTable'

type Customer = components['schemas']['CustomerResponse']

const PAGE_SIZE = 20
const SORT_KEYS = ['name', 'email', 'createdAt'] as const

const TYPE_LABELS: Record<string, string> = { Person: 'Fyzická osoba', Company: 'Právnická osoba' }
const STATUS_LABELS: Record<string, string> = { Active: 'Aktívny', Archived: 'Archivovaný' }

const columns: Column<Customer>[] = [
  { key: 'name', header: 'Meno / firma', render: (c) => c.displayName, sortable: true },
  { key: 'email', header: 'E-mail', render: (c) => c.email, sortable: true },
  { key: 'phone', header: 'Telefón', render: (c) => c.phone ?? '–' },
  { key: 'ico', header: 'IČO', render: (c) => c.ico ?? '–' },
  { key: 'type', header: 'Typ', render: (c) => TYPE_LABELS[c.type] ?? c.type },
  { key: 'status', header: 'Stav', render: (c) => STATUS_LABELS[c.status] ?? c.status },
  { key: 'createdAt', header: 'Vytvorený', render: (c) => new Date(c.createdAt).toLocaleDateString('sk-SK'), sortable: true },
]

type Result = { key: string; items: Customer[]; totalCount: number; error: boolean }

/** Customer list; filter, sort and page live in the URL so the view is shareable and survives reload. */
export default function CustomerListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()

  const q = params.get('q') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)
  const sortParam = params.get('sort')
  const sortBy = (SORT_KEYS as readonly string[]).includes(sortParam ?? '') ? (sortParam as (typeof SORT_KEYS)[number]) : 'name'
  const sortDir = params.get('dir') === 'desc' ? 'desc' : 'asc'

  const requestKey = JSON.stringify([q, page, sortBy, sortDir])
  const [result, setResult] = useState<Result | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    api
      .GET('/api/customers', {
        params: { query: { q: q || undefined, page, pageSize: PAGE_SIZE, sortBy, sortDir } },
        signal: controller.signal,
      })
      .then(({ data }) => {
        if (controller.signal.aborted) return
        setResult({ key: requestKey, items: data?.items ?? [], totalCount: Number(data?.totalCount ?? 0), error: !data })
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ key: requestKey, items: [], totalCount: 0, error: true })
      })
    return () => controller.abort()
  }, [q, page, sortBy, sortDir, requestKey])

  const loading = result?.key !== requestKey
  const current = loading ? null : result

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
  }

  const sort: SortState = { key: sortBy, direction: sortDir }

  return (
    <section>
      <h2>Zákazníci</h2>
      <p>
        <Link to="/customers/new">Nový zákazník</Link>
      </p>
      <DataTable
        columns={columns}
        rows={current?.items ?? []}
        rowKey={(c) => c.id}
        page={page}
        pageSize={PAGE_SIZE}
        totalCount={current?.totalCount ?? 0}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        sort={sort}
        onSortChange={(next) => update({ sort: next.key, dir: next.direction, page: null })}
        filter={q}
        onFilterChange={(value) => update({ q: value.trim(), page: null })}
        filterLabel="Hľadať podľa mena, IČO alebo e-mailu"
        loading={loading}
        error={current?.error ? 'Zoznam zákazníkov sa nepodarilo načítať.' : null}
        emptyMessage={q ? 'Nenašli sme žiadneho zákazníka.' : 'Zatiaľ nie sú evidovaní žiadni zákazníci.'}
        onRowClick={(c) => navigate(`/customers/${c.id}`)}
      />
    </section>
  )
}
