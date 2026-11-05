import { ProgressBar } from '../components/ProgressBar'
import { useTodayProduction } from '../features/dashboard/data'
import { formatNumber } from '../lib/format'
import '../features/dashboard/dashboard.css'

/** "Výroba dnes" card at the bottom of the sidebar: today's production against the daily target. */
export function SideProductionCard({ live }: { live: boolean }) {
  const { data } = useTodayProduction(live)
  if (!data) return null
  return (
    <div className="side-card">
      <div className="side-card__row">
        <strong>Výroba dnes</strong>
        <span>
          {formatNumber(data.todayKwh, 0)} / {formatNumber(data.targetKwh, 0)} kWh
        </span>
      </div>
      <ProgressBar label="Plnenie dennej výroby" value={data.todayKwh} max={data.targetKwh} />
      <p className="side-card__note">
        {data.online} inštalácií online · {data.offline} offline
      </p>
    </div>
  )
}
