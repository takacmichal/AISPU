import { Suspense, lazy, useCallback, useState, type ReactNode } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError, userMessage } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { canAccess, findItem } from '../../auth/navigation'
import { EmptyState } from '../../components/EmptyState'
import { PageHeader } from '../../components/PageHeader'
import { Skeleton } from '../../components/Skeleton'
import { Stepper } from '../../components/Stepper'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { useLoad } from '../../components/useLoad'
import { formatDate, formatKwp } from '../../lib/format'
import './installations.css'
import { FIELD_ROLES, PLAN_ROLES, STATE_LABELS, T, type InstallationState } from './messages'
import { StateBadge } from './StateBadge'
import { DEDICATED_TARGETS, nextAction, workflowSteps } from './workflow'

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
}

const TAB_KEYS = ['overview', 'devices', 'strings', 'plan', 'connection'] as const
type TabKey = (typeof TAB_KEYS)[number]

/**
 * `/installations/:id` (wireframe "Detail inštalácie"): header with the main action, the 6-step workflow,
 * and tabs Prehľad / Zariadenia / Stringy / Plán / Pripojenie.
 */
export default function InstallationDetailPage({ renderConnection }: InstallationDetailPageProps) {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { roles } = useAuth()
  const toast = useToast()
  const confirm = useConfirm()
  const [latest, setLatest] = useState<{ id: string; detail: Detail } | null>(null)
  const [busy, setBusy] = useState(false)

  const canPlan = PLAN_ROLES.some((r) => roles.includes(r))
  const canField = FIELD_ROLES.some((r) => roles.includes(r))
  const monitoring = findItem('/monitoring')
  const canMonitor = monitoring ? canAccess(monitoring, roles) : false

  const { data, loading } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)

  const detail = latest?.id === id ? latest.detail : data
  const setDetail = useCallback((next: Detail) => setLatest({ id, detail: next }), [id])
  const customerId = detail?.installation.customerId ?? ''

  // Best effort: roles without access to the customers API simply do not see the name.
  const customer = useLoad(async (signal) => {
    if (!customerId) return null
    const { data } = await api.GET('/api/customers/{id}', { params: { path: { id: customerId } }, signal })
    return data?.displayName ?? null
  }, customerId)

  const requested = params.get('tab')
  const active: TabKey = TAB_KEYS.find((key) => key === requested) ?? 'overview'
  const openTab = useCallback(
    (tab: TabKey, extra: Record<string, string> = {}) => setParams({ tab, ...extra }, { replace: true }),
    [setParams],
  )

  if (loading && !detail) return <Skeleton lines={4} label={T.detail.loading} />
  if (!detail)
    return (
      <p role="alert" className="alert">
        {T.detail.error}
      </p>
    )
  const { installation, stateHistory } = detail

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
  const action = canField ? nextAction(installation.state, installation.allowedTransitions) : null
  const blocked = action?.target === 'Mounting' && needsPlan
  const onlyDedicated =
    canField && !action && installation.allowedTransitions.some((t) => DEDICATED_TARGETS.includes(t))

  const subtitle = [
    customer.data,
    installation.plannedPowerKwp === null ? null : formatKwp(installation.plannedPowerKwp),
    installation.scheduledStart
      ? T.detail.mountingWindow(formatDate(installation.scheduledStart), formatDate(installation.scheduledEnd))
      : null,
  ].filter((part): part is string => Boolean(part))

  return (
    <section>
      <PageHeader
        title={installation.name}
        status={<StateBadge state={installation.state} />}
        subtitle={subtitle.length > 0 ? T.detail.subtitle(subtitle) : undefined}
        actions={
          <>
            {canMonitor && (
              <Link to={`/monitoring?installation=${installation.id}`} className="btn btn--ghost">
                {T.detail.productionLink}
              </Link>
            )}
            {action && (
              <button
                type="button"
                className="btn btn--primary"
                disabled={busy || blocked}
                aria-describedby={blocked ? 'installation-blocked' : 'installation-next'}
                onClick={() => void transition(action.target)}
              >
                {action.rework ? T.detail.rework : T.detail.next}
              </button>
            )}
          </>
        }
      />

      <article className="card installation-card installation-section" aria-labelledby="installation-workflow-title">
        <h3 id="installation-workflow-title" className="installation-card__title">
          {T.detail.workflow}
        </h3>
        <Stepper label={T.detail.workflow} steps={workflowSteps(installation.state, stateHistory)} />
        {action && !blocked && (
          <p id="installation-next" className="installation-card__note">
            {T.detail.nextStep(STATE_LABELS[action.target])}
          </p>
        )}
        {blocked && (
          <p id="installation-blocked" role="note" className="installation-card__note">
            {T.detail.mountingBlocked}
          </p>
        )}
        {onlyDedicated && (
          <p role="note" className="installation-card__note">
            {T.detail.dedicatedStep}
          </p>
        )}
      </article>

      <div role="tablist" aria-label={T.detail.tabsLabel} className="tabs">
        {TAB_KEYS.map((key) => (
          <button
            key={key}
            type="button"
            role="tab"
            id={`tab-${key}`}
            aria-selected={key === active}
            aria-controls="tab-panel"
            onClick={() => openTab(key)}
          >
            {T.detail.tabs[key]}
          </button>
        ))}
      </div>
      <div role="tabpanel" id="tab-panel" aria-labelledby={`tab-${active}`}>
        <Suspense fallback={<Skeleton lines={3} />}>
          {active === 'overview' && (
            <OverviewTab detail={detail} customerName={customer.data ?? null} onShowDevices={(type) => openTab('devices', { type })} />
          )}
          {active === 'devices' && <DevicesTab installationId={id} canField={canField} initialType={params.get('type') ?? ''} />}
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
