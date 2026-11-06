import { useEffect, useId, useMemo, useState, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { components } from '../api/generated/schema'
import type { NavItem } from '../auth/navigation'
import { Dialog } from '../components/Dialog'
import { LAYOUT_TEXTS as T } from './texts'

type SearchResponse = components['schemas']['SearchResponse']
type SearchHit = components['schemas']['SearchHit']
type Group = { key: keyof SearchResponse; label: string; path: (hit: SearchHit) => string }
const GROUPS: Group[] = [
  { key: 'customers', label: T.globalSearch.groups.customers, path: (hit) => `/customers/${hit.id}` },
  { key: 'quotes', label: T.globalSearch.groups.quotes, path: (hit) => `/quotes/${hit.id}` },
  { key: 'installations', label: T.globalSearch.groups.installations, path: (hit) => `/installations/${hit.id}` },
  { key: 'tickets', label: T.globalSearch.groups.tickets, path: (hit) => `/tickets/${hit.id}` },
]

/** API-backed global search opened by Ctrl/⌘+K. */
export function QuickJump({ items, onClose, onSelect }: { items: readonly NavItem[]; onClose: () => void; onSelect: (path: string) => void }) {
  const listId = useId()
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [response, setResponse] = useState<SearchResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(false)
  const [active, setActive] = useState(0)
  const normalized = query.trim()
  useEffect(() => {
    if (normalized.length < 2) {
      return
    }
    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      setLoading(true)
      setError(false)
      try {
        const result = await api.GET('/api/search', { params: { query: { q: normalized } }, signal: controller.signal })
        if (!result.data) throw new Error(`HTTP ${result.response.status}`)
        setResponse(result.data)
      } catch {
        if (!controller.signal.aborted) setError(true)
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    }, 250)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [normalized])

  const moduleResults = items.filter((item) => !normalized || item.label.toLowerCase().includes(normalized.toLowerCase()))
  const entityResults = useMemo(() => normalized.length < 2 ? [] : GROUPS.flatMap((group) => (response?.[group.key] ?? []).map((hit) => ({ ...hit, group, path: group.path(hit) }))), [normalized.length, response])
  const results = [...moduleResults.map((item) => ({ kind: 'module' as const, id: item.path, title: item.label, subtitle: '', label: T.globalSearch.groups.modules, path: item.path })), ...entityResults.map((item) => ({ kind: 'entity' as const, ...item, label: item.group.label }))]
  const current = Math.min(active, Math.max(0, results.length - 1))
  function select(index: number) {
    const target = results[index]
    if (target) { onClose(); (target.kind === 'module' ? onSelect : navigate)(target.path) }
  }
  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (!results.length) return
      const step = event.key === 'ArrowDown' ? 1 : -1
      setActive((current + step + results.length) % results.length)
    } else if (event.key === 'Enter') { event.preventDefault(); select(current) }
  }

  return (
    <Dialog title={T.globalSearch.title} onClose={onClose}>
      <div className="quick-jump">
        <input type="search" role="combobox" aria-label={T.globalSearch.inputLabel} aria-expanded="true" aria-controls={listId} aria-autocomplete="list" aria-activedescendant={results[current] ? `${listId}-${current}` : undefined} placeholder={T.globalSearch.placeholder} value={query} onChange={(event) => { const value = event.target.value; setQuery(value); setActive(0); if (value.trim().length < 2) { setResponse(null); setLoading(false); setError(false) } else setResponse(null) }} onKeyDown={onKeyDown} />
        <ul id={listId} role="listbox" aria-label={T.globalSearch.listLabel} className="quick-jump__list">
          {results.map((result, index) => <li key={`${result.kind}-${result.id}`} id={`${listId}-${index}`} role="option" aria-selected={index === current} className="quick-jump__option" onMouseEnter={() => setActive(index)} onClick={() => select(index)}>
            <span className="quick-jump__result"><strong>{result.title}</strong><span>{result.label}{result.subtitle ? ` · ${result.subtitle}` : ''}</span></span>
          </li>)}
        </ul>
        {normalized.length < 2 && <p className="quick-jump__empty">{T.globalSearch.minimum}</p>}
        {loading && <p role="status" className="quick-jump__empty">{T.globalSearch.loading}</p>}
        {error && <p role="alert" className="quick-jump__empty">{T.globalSearch.error}</p>}
        {!loading && !error && normalized.length >= 2 && results.length === 0 && response && <p className="quick-jump__empty">{T.globalSearch.empty}</p>}
        <p className="quick-jump__hint">{T.globalSearch.hint}</p>
      </div>
    </Dialog>
  )
}
