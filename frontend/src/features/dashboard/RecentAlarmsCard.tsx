import { Link } from 'react-router-dom'
import { Skeleton } from '../../components/Skeleton'
import { formatRelative } from '../../lib/format'
import type { Alarm } from '../monitoring/alarmsApi'
import { fromAlarm, fromTicket } from './alarms'
import type { TicketSummary } from './data'
import { MOCK_ALARMS } from './mock'
import { DASHBOARD_PARTS as T } from './texts'
import './dashboard.css'

/** `summary` comes from the dashboard (one request shared with the KPI cards); undefined while loading. */
export function RecentAlarmsCard({ summary, alarms = [] }: { summary: TicketSummary | null | undefined; alarms?: readonly Alarm[] }) {
  const loading = summary === undefined
  const now = new Date()
  // Real alarms win over sample data; they come first because they are what needs attention now.
  const mock = Boolean(summary?.mock) && alarms.length === 0
  // A ticket created from an alarm is the same event, so it is listed once, as the alarm.
  const linked = new Set(alarms.map((a) => a.ticketId))
  const tickets = summary?.items.filter((t) => !linked.has(t.id)) ?? []
  const data = summary && (mock ? MOCK_ALARMS : [...alarms.map(fromAlarm), ...tickets.map(fromTicket)].slice(0, 5))
  return (
    <article className="card dash-card" aria-labelledby="dash-alarms-title">
      <header className="dash-card__header">
        <div>
          <h3 id="dash-alarms-title" className="dash-card__title">
            {T.alarms.title}
          </h3>
          <p className="dash-card__subtitle">
            {T.alarms.subtitle}
            {mock && <span className="mock-tag">{T.mock}</span>}
          </p>
        </div>
        <Link to="/tickets" className="dash-card__link">
          {T.alarms.all} →
        </Link>
      </header>
      {loading && <Skeleton lines={4} label={T.alarms.loading} />}
      {data && data.length === 0 && <p className="dash-card__empty">{T.alarms.empty}</p>}
      {data && data.length > 0 && (
        <ul className="alarms">
          {data.map((row) => (
            <li key={row.id} className="alarms__item">
              <div className="alarms__text">
                <Link to={row.href} className="alarms__title">
                  {row.title}
                </Link>
                <span className="alarms__meta">
                  {row.meta} · {Number.isNaN(Date.parse(row.at)) ? row.at : formatRelative(row.at, now)}
                </span>
              </div>
              <span className={`alarm-badge alarm-badge--${row.severity}`}>{T.alarms.severity[row.severity]}</span>
            </li>
          ))}
        </ul>
      )}
    </article>
  )
}
