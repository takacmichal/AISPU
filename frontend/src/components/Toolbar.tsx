import type { ReactNode } from 'react'
import './ui.css'

/** Row above a table (K-01): search and filters on the left, extra info or bulk actions on the right. */
export function Toolbar({ children, aside }: { children: ReactNode; aside?: ReactNode }) {
  return (
    <div className="list-toolbar">
      <div className="list-toolbar__main">{children}</div>
      {aside && <div className="list-toolbar__aside">{aside}</div>}
    </div>
  )
}

interface SearchFieldProps {
  /** Accessible name (the field shows only a placeholder). */
  label: string
  value: string
  onChange: (value: string) => void
  placeholder?: string
}

export function SearchField({ label, value, onChange, placeholder }: SearchFieldProps) {
  return (
    <div className="search-field">
      <svg className="search-field__icon" width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
        <circle cx="7" cy="7" r="4.75" />
        <path d="m10.5 10.5 3 3" strokeLinecap="round" />
      </svg>
      <input type="search" aria-label={label} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

interface FilterSelectProps {
  /** Shown as a prefix in the chip ("Stav: Všetky") and used as the accessible name. */
  label: string
  value: string
  options: readonly { value: string; label: string }[]
  onChange: (value: string) => void
}

/** Filter as a pill-shaped native select, so it stays keyboard and screen reader friendly. */
export function FilterSelect({ label, value, options, onChange }: FilterSelectProps) {
  return (
    <label className="filter-chip">
      <span className="filter-chip__label">{label}:</span>
      <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  )
}
