import { EmptyState } from '../../../components/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import { useLoad } from '../../../components/useLoad'
import { formatNumber } from '../../../lib/format'
import { T } from '../messages'
import { STRINGS_API_ENABLED, fetchStringLayout, type StringPanel } from '../stringsApi'

/** Panel layout by strings (#243). Isolated: only this file and stringsApi.ts depend on the unmerged PR #342 API. */
export default function StringsTab({ installationId, enabled = STRINGS_API_ENABLED }: { installationId: string; enabled?: boolean }) {
  if (!enabled) return <EmptyState title={T.strings.title} description={T.strings.disabled} />
  return <Layout installationId={installationId} />
}

function Layout({ installationId }: { installationId: string }) {
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
        <section key={s.id} className="card installation-section" aria-label={T.strings.string(s.number)}>
          <h3>
            {T.strings.string(s.number)}
            {s.name ? ` – ${s.name}` : ''}
          </h3>
          <p>
            {s.mpptInput ? `${T.strings.mppt(s.mpptInput)} · ` : ''}
            {T.strings.panels(s.panelCount)} · {formatNumber(s.totalPowerW)}&nbsp;W
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
