import type { ReactNode } from 'react'
import './ui.css'

interface PageHeaderProps {
  title: string
  /** Status badge next to the title (detail pages). */
  status?: ReactNode
  subtitle?: ReactNode
  /** Secondary actions first, then the one primary action (it ends up on the right). */
  actions?: ReactNode
}

/** Page title block from the wireframe; breadcrumbs are rendered above it by the app shell. */
export function PageHeader({ title, status, subtitle, actions }: PageHeaderProps) {
  return (
    <header className="page-header">
      <div className="page-header__main">
        <div className="page-header__title-row">
          <h2 className="page-header__title">{title}</h2>
          {status}
        </div>
        {subtitle && <p className="page-header__subtitle">{subtitle}</p>}
      </div>
      {actions && <div className="page-header__actions">{actions}</div>}
    </header>
  )
}
