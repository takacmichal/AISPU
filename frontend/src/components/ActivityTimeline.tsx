import type { ReactNode } from 'react'
import './ui.css'

export interface ActivityItem {
  id: string
  title: ReactNode
  /** Who and when, e.g. "Lucia Horváthová · dnes 09:12". */
  meta?: ReactNode
}

/** Vertical list of events (customer activity, ticket history), newest first as given by the caller. */
export function ActivityTimeline({ label, items }: { label: string; items: readonly ActivityItem[] }) {
  return (
    <ol className="activity" aria-label={label}>
      {items.map((item) => (
        <li key={item.id} className="activity__item">
          <span className="activity__title">{item.title}</span>
          {item.meta && <span className="activity__meta">{item.meta}</span>}
        </li>
      ))}
    </ol>
  )
}
