import { Link } from 'react-router-dom'
import { api } from '../../api/client'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useLoad } from '../../components/useLoad'
import { formatDate } from '../../lib/format'
import './connection.css'
import { deadlineInfo, stepStatuses, type ConnectionRequest } from './model'
import { CONNECTION_TEXTS as T, STEP_LABELS, STEP_STATUS_TEXT } from './texts'

/** Deadline notice: warns when the proposal deadline is near or missed. */
export function DeadlineNotice({ request }: { request: ConnectionRequest }) {
  const info = deadlineInfo(request)
  if (!info) return null
  const text =
    info.level === 'overdue' ? T.deadlineOverdue(info.days) : info.level === 'warning' ? T.deadlineWarning(info.days) : T.deadlineOk(info.days)
  return (
    <p role={info.level === 'ok' ? 'status' : 'alert'} className={`connection-alert connection-alert--${info.level}`}>
      {text}
      {info.dueDate && ` ${T.dueDate(formatDate(info.dueDate))}.`}
    </p>
  )
}

function stepDate(request: ConnectionRequest, index: number): string | null {
  const value = [request.createdAt, request.submittedAt, request.decidedAt, null, null, request.state === 'Concluded' ? request.updatedAt : null][index]
  return value ? formatDate(value) : null
}

/** Presentational stepper: an ordered list of the process steps with the current one marked via aria-current. */
export function ConnectionSteps({ request }: { request: ConnectionRequest }) {
  const statuses = stepStatuses(request.state)
  const sideNote =
    request.state === 'InfoRequested' ? T.infoRequestedNote : request.state === 'Rejected' ? T.rejectedNote : null
  return (
    <div>
      <ol className="stepper" aria-label={T.stepperLabel}>
        {STEP_LABELS.map((label, index) => {
          const status = statuses[index]
          const date = status === 'upcoming' ? null : stepDate(request, index)
          return (
            <li
              key={label}
              className={`stepper__step stepper__step--${status}`}
              aria-current={status === 'current' || status === 'blocked' ? 'step' : undefined}
            >
              <span className="stepper__label">{label}</span>
              <span className="stepper__meta">
                {STEP_STATUS_TEXT[status]}
                {date && ` · ${date}`}
              </span>
              {index === 1 && sideNote && status !== 'done' && <span className="stepper__meta">{sideNote}</span>}
            </li>
          )
        })}
      </ol>
      <DeadlineNotice request={request} />
    </div>
  )
}

interface ConnectionStepperProps {
  /** Installation whose connection request is shown (newest request wins). */
  installationId?: string | null
  /** Alternatively a contract; its installation is resolved first. */
  contractId?: string
}

/**
 * Loads the connection request of an installation (or of a contract's installation) and shows its stepper.
 * Reusable in the installation and contract detail.
 */
export function ConnectionStepper({ installationId, contractId }: ConnectionStepperProps) {
  const { data, loading, error } = useLoad(
    async (signal) => {
      let target = installationId ?? null
      if (!target && contractId) {
        const { data: contract, response } = await api.GET('/api/contracts/{id}', { params: { path: { id: contractId } }, signal })
        if (!contract) throw new Error(`HTTP ${response.status}`)
        target = contract.installationId
      }
      if (!target) return { request: null, missingInstallation: true }
      const { data: list, response } = await api.GET('/api/connection-requests', {
        params: { query: { installationId: target } },
        signal,
      })
      if (!list) throw new Error(`HTTP ${response.status}`)
      return { request: (list[0] as ConnectionRequest | undefined) ?? null, missingInstallation: false }
    },
    `${installationId ?? ''}|${contractId ?? ''}`,
  )

  if (loading) return <Skeleton lines={2} label={T.stepperLoading} />
  if (error || !data)
    return (
      <p role="alert" className="alert">
        {T.stepperError}
      </p>
    )
  if (data.missingInstallation) return <EmptyState title={T.stepperNoInstallation} />
  if (!data.request) return <EmptyState title={T.stepperEmpty} />
  return (
    <div>
      <ConnectionSteps request={data.request} />
      <p>
        <Link to={`/connection-requests/${data.request.id}`}>{T.openRequest}</Link>
      </p>
    </div>
  )
}
