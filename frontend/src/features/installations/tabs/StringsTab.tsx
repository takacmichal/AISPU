import { EmptyState } from '../../../components/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import { useLoad } from '../../../components/useLoad'
import { formatNumber } from '../../../lib/format'
import { T } from '../messages'
import { fetchStringLayout, type StringPanel } from '../stringsApi'

/** Panel layout by strings (#243), read only: strings in the order of their number with the serial numbers of their panels. */
export default function StringsTab({ installationId }: { installationId: string }) {
  const { data, loading, error } = useLoad((signal) => fetchStringLayout(installationId, signal), installationId)

  if (loading) return <Skeleton lines={4} label={T.strings.loading} />
  if (error || !data)
    return (
      <p role="alert" className="alert">
        {T.strings.error}
      </p>
    )
  if (data.strings.length === 0 && data.unassignedPanels.length === 0) return <EmptyState title={T.strings.empty} />

  return (
    <div>
      {data.strings.map((s) => (
        <section key={s.id} className="card installation-section" aria-label={T.strings.string(Number(s.number))}>
          <h3>
            {T.strings.string(Number(s.number))}
            {s.name ? ` – ${s.name}` : ''}
          </h3>
          <p>
            {s.mpptInput ? `${T.strings.mppt(Number(s.mpptInput))} · ` : ''}
            {T.strings.panels(Number(s.panelCount))} · {formatNumber(s.totalPowerW)}&nbsp;W
          </p>
          <PanelRow panels={s.panels} />
        </section>
      ))}
      {data.unassignedPanels.length > 0 && (
        <section className="card installation-section" aria-label={T.strings.unassigned}>
          <h3>{T.strings.unassigned}</h3>
          <PanelRow panels={data.unassignedPanels} />
        </section>
      )}
      <p>
        {T.strings.total}: {formatNumber(data.assignedPowerW)}&nbsp;W / {formatNumber(data.totalPanelPowerW)}&nbsp;W
      </p>
    </div>
  )
}

function PanelRow({ panels }: { panels: readonly StringPanel[] }) {
  return (
    <ul className="string-row">
      {panels.map((p) => (
        <li key={p.id} className="string-panel mono" title={`${p.manufacturer} ${p.model}, ${p.powerW} W`}>
          {p.serialNumber}
        </li>
      ))}
    </ul>
  )
}
