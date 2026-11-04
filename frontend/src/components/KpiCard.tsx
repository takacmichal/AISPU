import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { MESSAGES } from '../lib/messages'
import { ProgressBar } from './ProgressBar'
import { Skeleton } from './Skeleton'
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
  /** While set, a skeleton with this accessible label replaces the value. */
  loading?: string
  /** Replaces the value with the message and a "Skúsiť znova" button (the other cards stay usable). */
  error?: { message: string; onRetry: () => void }
}

/** KPI card from the wireframe (K-02): icon, big value, label, detail and an optional trend or target. */
export function KpiCard({ label, value, unit, detail, href, icon, trend, target, loading, error }: KpiCardProps) {
  const ready = !loading && !error
  return (
    <article className="card kpi-card" aria-busy={Boolean(loading)}>
      {(icon || trend) && (
        <div className="kpi-card__top">
          {icon && (
            <span className="kpi-card__icon" aria-hidden="true">
              {icon}
            </span>
          )}
          {trend && ready && <span className={`badge${trend.tone && trend.tone !== 'neutral' ? ` badge--${trend.tone}` : ''}`}>{trend.text}</span>}
        </div>
      )}
      {loading && <Skeleton lines={2} label={loading} />}
      {error && (
        <div className="kpi-card__error">
          <p>{error.message}</p>
          <button type="button" className="btn btn--sm" onClick={error.onRetry}>
            {MESSAGES.retry}
          </button>
        </div>
      )}
      {ready && (
        <p className="kpi-card__value">
          {value}
          {unit && <span className="kpi-card__unit">{unit}</span>}
        </p>
      )}
      <h3 className="kpi-card__label">{href ? <Link to={href}>{label}</Link> : label}</h3>
      {ready && detail && <div className="kpi-card__detail">{detail}</div>}
      {ready && target && <ProgressBar label={target.label} value={target.value} max={target.max} />}
    </article>
  )
}
