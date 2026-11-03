import { Suspense, lazy, useCallback, useState, type ReactNode } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError, userMessage } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { useLoad } from '../../components/useLoad'
import './installations.css'
import { FIELD_ROLES, PLAN_ROLES, STATE_LABELS, T, type InstallationState } from './messages'
import { StateBadge } from './StateBadge'

const OverviewTab = lazy(() => import('./tabs/OverviewTab'))
const DevicesTab = lazy(() => import('./tabs/DevicesTab'))
const PlanTab = lazy(() => import('./tabs/PlanTab'))
const StringsTab = lazy(() => import('./tabs/StringsTab'))

type Installation = components['schemas']['InstallationResponse']
type Detail = components['schemas']['InstallationDetailResponse']

export interface InstallationDetailPageProps {
  /**
   * SLOT for the connection stepper (`features/connection/ConnectionStepper`), rendered in the "Pripojenie" tab.
   * Wire it in the route element: `<InstallationDetailPage renderConnection={(i) => <ConnectionStepper installationId={i.id} />} />`.
   */
  renderConnection?: (installation: Installation) => ReactNode
  /** Link target of the production dashboard (no route exists yet); the link is hidden when omitted. */
  productionHref?: (installation: Installation) => string
}

const TAB_KEYS = ['overview', 'devices', 'strings', 'plan', 'connection'] as const
type TabKey = (typeof TAB_KEYS)[number]

/** `/installations/:id`: header with state and transitions, tabs Prehľad / Zariadenia / Stringy / Plán / Pripojenie. */
export default function InstallationDetailPage({ renderConnection, productionHref }: InstallationDetailPageProps) {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { roles } = useAuth()
  const toast = useToast()
  const confirm = useConfirm()
  const [latest, setLatest] = useState<{ id: string; detail: Detail } | null>(null)
  const [busy, setBusy] = useState(false)

  const canPlan = PLAN_ROLES.some((r) => roles.includes(r))
  const canField = FIELD_ROLES.some((r) => roles.includes(r))

  const { data, loading } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)

  const detail = latest?.id === id ? latest.detail : data
  const setDetail = useCallback((next: Detail) => setLatest({ id, detail: next }), [id])

  const tabs = TAB_KEYS
  const requested = params.get('tab')
  const active: TabKey = tabs.find((key) => key === requested) ?? 'overview'

  if (loading && !detail) return <Skeleton lines={4} label={T.detail.loading} />
  if (!detail)
    return (
      <p role="alert" className="alert">
        {T.detail.error}
      </p>
    )
  const { installation } = detail

  async function transition(target: InstallationState) {
    const ok = await confirm({
      title: T.detail.transitionTitle,
      message: T.detail.transitionMessage(STATE_LABELS[installation.state], STATE_LABELS[target]),
      confirmLabel: STATE_LABELS[target],
    })
    if (!ok) return
    setBusy(true)
    try {
      const { data, response, error } = await api.POST('/api/installations/{id}/transitions', {
        params: { path: { id } },
        body: { targetState: target },
      })
      if (!data) throw problemToApiError(response.status, error)
      setDetail(data)
      toast.success(T.detail.transitionDone(STATE_LABELS[target]))
    } catch (e) {
      toast.error(userMessage(e))
    } finally {
      setBusy(false)
    }
  }

  const needsPlan = !installation.scheduledStart || installation.technicianIds.length === 0
  const transitions = installation.allowedTransitions
  const dashboard = productionHref?.(installation)

  return (
    <section>
      <div className="installation-header">
        <h2>{installation.name}</h2>
        <StateBadge state={installation.state} />
        {dashboard && (
          <Link to={dashboard} className="btn btn--ghost">
            {T.detail.productionLink}
          </Link>
        )}
      </div>
      {canField && transitions.length > 0 && (
        <div className="btn-row installation-section" role="group" aria-label={T.detail.transitionsLabel}>
          {transitions.map((target, index) => {
            const blocked = target === 'Mounting' && needsPlan
            return (
              <button
                key={target}
                type="button"
                className={index === 0 && !blocked ? 'btn btn--primary' : 'btn btn--ghost'}
                disabled={busy || blocked}
                title={blocked ? T.detail.mountingBlocked : undefined}
                onClick={() => void transition(target)}
              >
                {STATE_LABELS[target]}
              </button>
            )
          })}
          {transitions.includes('Mounting') && needsPlan && <p role="note">{T.detail.mountingBlocked}</p>}
        </div>
      )}

      <div role="tablist" aria-label={T.detail.tabsLabel} className="tabs">
        {tabs.map((key) => (
          <button
            key={key}
            type="button"
            role="tab"
            id={`tab-${key}`}
            aria-selected={key === active}
            aria-controls="tab-panel"
            onClick={() => setParams({ tab: key }, { replace: true })}
          >
            {T.detail.tabs[key]}
          </button>
        ))}
      </div>
      <div role="tabpanel" id="tab-panel" aria-labelledby={`tab-${active}`}>
        <Suspense fallback={<Skeleton lines={3} />}>
          {active === 'overview' && <OverviewTab detail={detail} />}
          {active === 'devices' && <DevicesTab installationId={id} canField={canField} />}
          {active === 'strings' && <StringsTab installationId={id} />}
          {active === 'plan' && <PlanTab detail={detail} canPlan={canPlan} onChange={setDetail} />}
          {active === 'connection' &&
            (renderConnection ? (
              renderConnection(installation)
            ) : (
              <EmptyState title={T.detail.connectionPlaceholder} description={T.detail.connectionPlaceholderText} />
            ))}
        </Suspense>
      </div>
    </section>
  )
}
