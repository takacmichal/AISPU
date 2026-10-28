import { useEffect, useState, type ReactNode } from 'react'

export type SortDirection = 'asc' | 'desc'
export interface SortState {
  key: string
  direction: SortDirection
}

export interface Column<T> {
  key: string
  header: string
  render: (row: T) => ReactNode
  sortable?: boolean
}

export interface DataTableProps<T> {
  columns: readonly Column<T>[]
  rows: readonly T[]
  rowKey: (row: T) => string
  /** 1-based page number. */
  page: number
  pageSize: number
  totalCount: number
  onPageChange: (page: number) => void
  sort?: SortState | null
  onSortChange?: (sort: SortState) => void
  /** Committed (debounced) filter value and its change handler. */
  filter?: string
  onFilterChange?: (value: string) => void
  filterLabel?: string
  filterDebounceMs?: number
  loading?: boolean
  error?: string | null
  emptyMessage?: string
  onRowClick?: (row: T) => void
}

/** Controlled table: the parent owns page, sort and filter (e.g. in URL params) and loads data server-side. */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  page,
  pageSize,
  totalCount,
  onPageChange,
  sort,
  onSortChange,
  filter,
  onFilterChange,
  filterLabel = 'Hľadať',
  filterDebounceMs = 300,
  loading = false,
  error = null,
  emptyMessage = 'Žiadne záznamy.',
  onRowClick,
}: DataTableProps<T>) {
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <div className="data-table">
      {onFilterChange && (
        <FilterInput label={filterLabel} value={filter ?? ''} debounceMs={filterDebounceMs} onChange={onFilterChange} />
      )}
      {error && <p role="alert">{error}</p>}
      <table aria-busy={loading}>
        <thead>
          <tr>
            {columns.map((column) => {
              const active = sort?.key === column.key
              const ariaSort = active ? (sort.direction === 'asc' ? 'ascending' : 'descending') : undefined
              return (
                <th key={column.key} scope="col" aria-sort={ariaSort}>
                  {column.sortable && onSortChange ? (
                    <button
                      type="button"
                      onClick={() => onSortChange({ key: column.key, direction: active && sort.direction === 'asc' ? 'desc' : 'asc' })}
                    >
                      {column.header}
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={rowKey(row)}
              onClick={onRowClick ? () => onRowClick(row) : undefined}
              className={onRowClick ? 'clickable' : undefined}
            >
              {columns.map((column) => (
                <td key={column.key}>{column.render(row)}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {loading && <p role="status">Načítavam…</p>}
      {!loading && !error && rows.length === 0 && <p>{emptyMessage}</p>}
      <nav aria-label="Stránkovanie" className="pager">
        <button type="button" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          Predchádzajúca
        </button>
        <span>
          Strana {page} z {pageCount} ({totalCount})
        </span>
        <button type="button" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>
          Ďalšia
        </button>
      </nav>
    </div>
  )
}

function FilterInput({
  label,
  value,
  debounceMs,
  onChange,
}: {
  label: string
  value: string
  debounceMs: number
  onChange: (value: string) => void
}) {
  const [text, setText] = useState(value)

  // Follow external changes (e.g. back navigation restoring the URL).
  const [previous, setPrevious] = useState(value)
  if (previous !== value) {
    setPrevious(value)
    setText(value)
  }

  useEffect(() => {
    if (text === value) return
    const timer = setTimeout(() => onChange(text), debounceMs)
    return () => clearTimeout(timer)
  }, [text, value, debounceMs, onChange])

  return (
    <label>
      {label}
      <input type="search" value={text} onChange={(event) => setText(event.target.value)} />
    </label>
  )
}
