import type { components } from '../../../api/generated/schema'
import { api } from '../../../api/client'
import { useLoad } from '../../../components/useLoad'
import { formatDateTime, formatKwp } from '../../../lib/format'
import { STATE_LABELS, T } from '../messages'

type Detail = components['schemas']['InstallationDetailResponse']

export default function OverviewTab({ detail }: { detail: Detail }) {
  const { installation, stateHistory } = detail

  // Best effort: roles without access to the customers API simply do not get the row.
  const customer = useLoad(async (signal) => {
    const { data } = await api.GET('/api/customers/{id}', { params: { path: { id: installation.customerId } }, signal })
    return data?.displayName ?? null
  }, installation.customerId)

  const rows: [string, string | null | undefined][] = [
    [T.overview.name, installation.name],
    [T.overview.state, STATE_LABELS[installation.state]],
    [T.overview.customer, customer.data],
    [T.overview.power, installation.plannedPowerKwp === null ? null : formatKwp(installation.plannedPowerKwp)],
    [T.overview.note, installation.note],
    [T.overview.created, formatDateTime(installation.createdAt)],
    [T.overview.changed, formatDateTime(installation.stateChangedAt)],
  ]

  return (
    <div>
      <dl className="card installation-section">
        {rows
          .filter(([, value]) => value)
          .map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
      </dl>
      <h3>{T.overview.history}</h3>
      <ol className="installation-history">
        {stateHistory.map((entry) => (
          <li key={`${entry.changedAt}-${entry.toState}`}>
            <strong>{STATE_LABELS[entry.toState]}</strong>
            {entry.fromState ? ` (z „${STATE_LABELS[entry.fromState]}“)` : ` (${T.overview.initial})`} · {formatDateTime(entry.changedAt)}
            {entry.note ? ` · ${entry.note}` : ''}
          </li>
        ))}
      </ol>
    </div>
  )
}
