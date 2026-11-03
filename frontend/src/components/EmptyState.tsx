import type { ReactNode } from 'react'

interface EmptyStateProps {
  title: string
  description?: string
  /** Optional call to action, e.g. a link button. */
  action?: ReactNode
}

/** Friendly "nothing here yet" block for lists and tabs. */
export function EmptyState({ title, description, action }: EmptyStateProps) {
  return (
    <div className="empty-state">
      <p className="empty-state__title">{title}</p>
      {description && <p className="empty-state__text">{description}</p>}
      {action}
    </div>
  )
}
