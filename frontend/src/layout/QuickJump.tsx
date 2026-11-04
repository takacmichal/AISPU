import { useId, useMemo, useState, type KeyboardEvent } from 'react'
import type { NavItem } from '../auth/navigation'
import { Dialog } from '../components/Dialog'
import { LAYOUT_TEXTS as T } from './texts'
import { NavIcon } from './NavIcon'

/** Lowercase without diacritics, so "zak" finds "Zákazníci". */
const fold = (value: string) => value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()

/** A module matches when one of its words starts with the query ("pon" → Ponuky, not "Katalóg komponentov"). */
const matches = (label: string, query: string) => {
  const q = fold(query.trim())
  return q === '' || fold(label).split(/\s+/).some((word) => word.startsWith(q)) || fold(label).startsWith(q)
}

interface QuickJumpProps {
  /** Modules the user may open (already filtered by role). */
  items: readonly NavItem[]
  onSelect: (path: string) => void
  onClose: () => void
}

/** "Prejsť na modul" dialog opened by Ctrl/⌘+K: a combobox filtering the user's modules. */
export function QuickJump({ items, onSelect, onClose }: QuickJumpProps) {
  const listId = useId()
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const results = useMemo(() => items.filter((item) => matches(item.label, query)), [items, query])
  const current = Math.min(active, Math.max(0, results.length - 1))

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      if (results.length === 0) return
      const step = event.key === 'ArrowDown' ? 1 : -1
      setActive((current + step + results.length) % results.length)
    } else if (event.key === 'Enter') {
      event.preventDefault()
      const target = results[current]
      if (target) onSelect(target.path)
    }
  }

  return (
    <Dialog title={T.quickJump.title} onClose={onClose}>
      <div className="quick-jump">
        <input
          type="text"
          role="combobox"
          aria-label={T.quickJump.inputLabel}
          aria-expanded="true"
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={results[current] ? `${listId}-${current}` : undefined}
          placeholder={T.quickJump.placeholder}
          value={query}
          onChange={(event) => {
            setQuery(event.target.value)
            setActive(0)
          }}
          onKeyDown={onKeyDown}
        />
        <ul id={listId} role="listbox" aria-label={T.quickJump.listLabel} className="quick-jump__list">
          {results.map((item, index) => (
            <li
              key={item.path}
              id={`${listId}-${index}`}
              role="option"
              aria-selected={index === current}
              className="quick-jump__option"
              onMouseEnter={() => setActive(index)}
              onClick={() => onSelect(item.path)}
            >
              <NavIcon path={item.path} />
              {item.label}
            </li>
          ))}
        </ul>
        {results.length === 0 && <p className="quick-jump__empty">{T.quickJump.empty}</p>}
        <p className="quick-jump__hint">{T.quickJump.hint}</p>
      </div>
    </Dialog>
  )
}
