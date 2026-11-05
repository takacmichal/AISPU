import type { components } from '../../../api/generated/schema'
import { api } from '../../../api/client'
import { ActivityTimeline } from '../../../components/ActivityTimeline'
import { EmptyState } from '../../../components/EmptyState'
import { KeyValueList, type KeyValueItem } from '../../../components/KeyValueList'
import { Skeleton } from '../../../components/Skeleton'
import { useLoad } from '../../../components/useLoad'
import { formatDate, formatDateTime, formatKwp, formatNumber } from '../../../lib/format'
import { STATE_LABELS, T } from '../messages'
import { StateBadge } from '../StateBadge'
import { componentSummaries } from '../components'

type Detail = components['schemas']['InstallationDetailResponse']

interface OverviewTabProps {
  detail: Detail
  /** Loaded by the page; null when the role cannot read customers. */
  customerName: string | null
  /** Opens the devices tab filtered to one type. */
  onShowDevices: (type: string) => void
}

/** Component cards (panels, inverters) with serial number counts, the "Realizácia" card and the state history. */
export default function OverviewTab({ detail, customerName, onShowDevices }: OverviewTabProps) {
  const { installation, stateHistory } = detail

  const devices = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations/{installationId}/devices', {
      params: { path: { installationId: installation.id } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, installation.id)

  const summaries = componentSummaries(devices.data ?? [])

  const info: KeyValueItem[] = [
    ...(customerName ? [{ label: T.overview.customer, value: customerName }] : []),
    ...(installation.plannedPowerKwp === null ? [] : [{ label: T.overview.power, value: formatKwp(installation.plannedPowerKwp) }]),
    {
      label: T.overview.schedule,
      value: installation.scheduledStart
        ? `${formatDate(installation.scheduledStart)} – ${formatDate(installation.scheduledEnd)}`
        : T.list.notScheduled,
    },
    { label: T.overview.technicians, value: T.overview.techniciansCount(installation.technicianIds.length) },
    ...(installation.note ? [{ label: T.overview.note, value: installation.note }] : []),
    { label: T.overview.created, value: formatDateTime(installation.createdAt) },
    { label: T.overview.changed, value: formatDateTime(installation.stateChangedAt) },
  ]

  return (
    <div className="installation-overview">
      <section className="installation-components" aria-label={T.overview.components}>
        {devices.loading && <Skeleton lines={3} label={T.overview.componentsLoading} />}
        {devices.error && (
          <p role="alert" className="alert">
            {T.overview.componentsError}
          </p>
        )}
        {devices.data && summaries.length === 0 && <EmptyState title={T.overview.componentsEmpty} />}
        {summaries.map((s) => (
          <article key={s.type} className="card installation-card component-card" aria-labelledby={`component-${s.type}`}>
            <div className="component-card__top">
              <h3 id={`component-${s.type}`} className="component-card__type">
                {T.overview.cardTitles[s.type] ?? s.type}
              </h3>
              <span className="badge badge--success">{T.overview.installed}</span>
            </div>
            <p className="component-card__count">{T.overview.pieces(s.count)}</p>
            <div>
              <p className="component-card__model">{s.model}</p>
              <p className="component-card__detail">
                {[s.type === 'Panel' ? formatKwp(s.totalKw) : `${formatNumber(s.totalKw)}\u00a0kW`, s.otherModels > 0 ? T.overview.otherModels(s.otherModels) : null].filter(Boolean).join(' · ')}
              </p>
            </div>
            <button type="button" className="btn btn--ghost btn--sm component-card__link" onClick={() => onShowDevices(s.type)}>
              {T.overview.serials(s.count)}
            </button>
          </article>
        ))}
      </section>

      <div className="installation-overview__row">
        <article className="card installation-card" aria-labelledby="installation-realization-title">
          <h3 id="installation-realization-title" className="installation-card__title">
            {T.overview.realization}
          </h3>
          <KeyValueList items={info} />
        </article>

        <article className="card installation-card" aria-labelledby="installation-history-title">
          <h3 id="installation-history-title" className="installation-card__title">
            {T.overview.history}
          </h3>
          <ActivityTimeline
            label={T.overview.history}
            items={[...stateHistory].reverse().map((entry, index) => ({
              id: `${entry.changedAt}-${index}`,
              title: <StateBadge state={entry.toState} />,
              meta: (
                <>
                  <time dateTime={entry.changedAt}>{formatDateTime(entry.changedAt)}</time>
                  {' · '}
                  {entry.fromState ? T.overview.from(STATE_LABELS[entry.fromState]) : T.overview.initial}
                  {entry.note ? ` · ${entry.note}` : ''}
                </>
              ),
            }))}
          />
        </article>
      </div>
    </div>
  )
}
