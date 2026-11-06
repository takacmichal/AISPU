import { useTodayProduction } from '../features/dashboard/data'
import { formatNumber } from '../lib/format'
import { plants } from '../pages/dashboardTexts'
import '../features/dashboard/dashboard.css'

/** "Výroba dnes" card at the bottom of the sidebar: today's production of the plants in operation. */
export function SideProductionCard({ live }: { live: boolean }) {
  const { data } = useTodayProduction(live)
  if (!data) return null
  return (
    <div className="side-card">
      <div className="side-card__row">
        <strong>Výroba dnes</strong>
        <span>{formatNumber(data.todayKwh, 0)} kWh</span>
      </div>
      <p className="side-card__note">{plants(data.operating)} v prevádzke</p>
    </div>
  )
}
