import type { ReactNode } from 'react'
import './ui.css'

export interface KeyValueItem {
  label: string
  value: ReactNode
}

/** Label/value rows ("Kontaktné údaje", "Realizácia", "Detaily") as a description list. */
export function KeyValueList({ items }: { items: readonly KeyValueItem[] }) {
  return (
    <dl className="kv-list">
      {items.map((item) => (
        <div key={item.label} className="kv-list__row">
          <dt>{item.label}</dt>
          <dd>{item.value}</dd>
        </div>
      ))}
    </dl>
  )
}
