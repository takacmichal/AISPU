import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ProgressBar } from './ProgressBar'
import './ui.css'

export type Tone = 'neutral' | 'success' | 'info' | 'warning' | 'error'

interface KpiCardProps {
  label: string
  value: ReactNode
  unit?: string
  detail?: ReactNode
  /** Opens the related list. */
  href?: string
  /** Decorative icon in the top left corner. */
  icon?: ReactNode
  /** Small chip in the top right corner, e.g. "+8 % vs. včera". */
  trend?: { text: string; tone?: Tone }
  /** Progress toward a target, e.g. performance ratio vs. goal. */
  target?: { value: number; max: number; label: string }
}

/** KPI card from the wireframe (K-02): icon, big value, label, detail and an optional trend or target. */
export function KpiCard({ label, value, unit, detail, href, icon, trend, target }: KpiCardProps) {
  return (
    <article className="card kpi-card">
      {(icon || trend) && (
        <div className="kpi-card__top">
          {icon && (
            <span className="kpi-card__icon" aria-hidden="true">
              {icon}
            </span>
          )}
          {trend && <span className={`badge${trend.tone && trend.tone !== 'neutral' ? ` badge--${trend.tone}` : ''}`}>{trend.text}</span>}
        </div>
      )}
      <p className="kpi-card__value">
        {value}
        {unit && <span className="kpi-card__unit">{unit}</span>}
      </p>
      <h3 className="kpi-card__label">{href ? <Link to={href}>{label}</Link> : label}</h3>
      {detail && <div className="kpi-card__detail">{detail}</div>}
      {target && <ProgressBar label={target.label} value={target.value} max={target.max} />}
    </article>
  )
}
