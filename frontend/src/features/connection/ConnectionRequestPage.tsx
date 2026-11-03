import { useCallback, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { userMessage } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { formatDateTime, formatKwp } from '../../lib/format'
import { downloadAttachment, postTransition } from './actions'
import './connection.css'
import { ConnectionSteps } from './ConnectionStepper'
import { nextAction, timelineEvents, type ConnectionRequest } from './model'
import { ATTACHMENT_KIND_LABELS, CONNECTION_TEXTS as T, STATE_LABELS, TIMELINE_LABELS, TRANSITIONS, TYPE_LABELS } from './texts'

/** Detail of one connection request: stepper, deadline, data, attachments, status timeline and the next step. */
export default function ConnectionRequestPage() {
  const { id = '' } = useParams()
  const { roles } = useAuth()

  const { data, loading, error, reload } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/connection-requests/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data as ConnectionRequest
  }, id)

  if (loading) return <Skeleton lines={5} label={T.loading} />
  if (error || !data)
    return (
      <p role="alert" className="alert">
        {T.loadError}
      </p>
    )

  const next = nextAction(data)
  const allowed = next !== null && next.roles.some((role) => roles.includes(role))
  const events = timelineEvents(data, TIMELINE_LABELS)

  return (
    <section>
      <div className="toolbar">
        <h2>{T.title}</h2>
        <span className="badge badge--info">{STATE_LABELS[data.state] ?? data.state}</span>
      </div>

      <ConnectionSteps request={data} />

      {allowed && next && (
        <div className="connection-section">
          <h3>{T.actions}</h3>
          <TransitionButton key={next.action + data.updatedAt} requestId={data.id} action={next.action} onDone={reload} />
        </div>
      )}

      <div className="connection-section">
        <h3>{T.data}</h3>
        <dl className="card">
          {(
            [
              [T.eic, data.eicCode],
              [T.power, formatKwp(data.ratedAcPowerKw)],
              [T.breaker, data.mainBreakerAmps === null ? null : `${data.mainBreakerAmps} A`],
              [T.type, TYPE_LABELS[data.type] ?? data.type],
              [T.state, STATE_LABELS[data.state] ?? data.state],
            ] as [string, string | null | undefined][]
          )
            .filter(([, value]) => value)
            .map(([label, value]) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{value}</dd>
              </div>
            ))}
        </dl>
      </div>

      <div className="connection-section">
        <h3>{T.attachments}</h3>
        {data.attachments.length === 0 ? <EmptyState title={T.attachmentsEmpty} /> : <Attachments request={data} />}
      </div>

      <div className="connection-section">
        <h3>{T.history}</h3>
        <ol className="timeline">
          {events.map((event) => (
            <li key={event.key}>
              <time dateTime={event.at}>{formatDateTime(event.at)}</time>
              {event.label}
              {event.note && <p className="timeline__note">{event.note}</p>}
            </li>
          ))}
        </ol>
      </div>

      <p>
        <Link to={`/installations/${data.installationId}`}>{T.installation}</Link>
      </p>
    </section>
  )
}

function Attachments({ request }: { request: ConnectionRequest }) {
  const toast = useToast()
  return (
    <ul className="card">
      {request.attachments.map((a) => (
        <li key={a.id}>
          {ATTACHMENT_KIND_LABELS[a.kind] ?? a.kind}: {a.fileName}{' '}
          {a.hasContent && a.downloadUrl ? (
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              aria-label={`${T.download} ${a.fileName}`}
              onClick={() => downloadAttachment(a.downloadUrl!, a.fileName).catch((e) => toast.error(userMessage(e)))}
            >
              {T.download}
            </button>
          ) : (
            <span className="badge">{T.attachmentMetadataOnly}</span>
          )}
        </li>
      ))}
    </ul>
  )
}

/** The one primary action of the view: runs a process transition after confirmation. */
function TransitionButton({ requestId, action, onDone }: { requestId: string; action: string; onDone: () => void }) {
  const def = TRANSITIONS[action]
  const confirm = useConfirm()
  const toast = useToast()
  const run = useCallback(() => postTransition(requestId, action), [requestId, action])
  const { busy, formError, submit } = useFormSubmit(run)
  const [done, setDone] = useState(false)

  async function onClick() {
    if (!(await confirm({ title: def.label, message: def.confirm, confirmLabel: def.label }))) return
    if (await submit()) {
      setDone(true)
      toast.success(def.success)
      onDone()
    }
  }

  return (
    <div>
      <button type="button" className="btn btn--primary" disabled={busy || done} onClick={onClick}>
        {def.label}
      </button>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
    </div>
  )
}
