import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { Skeleton } from '../../components/Skeleton'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { formatDateTime } from '../../lib/format'
import { COMMENT_ROLES, MANAGE_ROLES, ORIGIN_LABELS, PRIORITY_LABELS, STATE_LABELS, T } from './messages'
import { PriorityBadge, StateBadge } from './TicketBadges'
import './tickets.css'
import {
  COMMENT_MAX,
  NOTE_MAX,
  TICKET_PRIORITIES,
  VISIT_WORKFLOW_STATES,
  addTicketComment,
  downloadAttachment,
  getTicket,
  getTicketComments,
  getTicketHistory,
  triageTicket,
  type Ticket,
  type TicketAttachment,
  type TicketPriority,
} from './ticketsApi'
import { buildTimeline } from './timeline'
import { useTicketTransition } from './useTicketTransition'

/** `/tickets/:id`: header, transitions, triage, attachments and a timeline with comments. */
export default function TicketDetailPage() {
  const { id = '' } = useParams()
  const { roles } = useAuth()
  const [latest, setLatest] = useState<Ticket | null>(null)
  const canManage = MANAGE_ROLES.some((r) => roles.includes(r))
  const canComment = COMMENT_ROLES.some((r) => roles.includes(r))

  const { data, loading, reload } = useLoad((signal) => getTicket(id, signal), id)
  const timeline = useLoad(async (signal) => {
    const [history, comments] = await Promise.all([getTicketHistory(id, signal), getTicketComments(id, signal)])
    return buildTimeline(history, comments)
  }, id)

  const mover = useTicketTransition((ticket) => {
    setLatest(ticket)
    timeline.reload()
  })

  const ticket = latest?.id === id ? latest : data
  if (loading && !ticket) return <Skeleton lines={4} label={T.detail.loading} />
  if (!ticket)
    return (
      <p role="alert" className="alert">
        {T.detail.error}
      </p>
    )

  const transitions = ticket.allowedTransitions.filter((s) => !VISIT_WORKFLOW_STATES.includes(s))
  const visitBlocked = ticket.allowedTransitions.some((s) => VISIT_WORKFLOW_STATES.includes(s))
  const changed = (next: Ticket) => {
    setLatest(next)
    timeline.reload()
    reload()
  }

  return (
    <section>
      <p>
        <Link to="/tickets">{T.detail.back}</Link>
      </p>
      <div className="ticket-header">
        <h2>{ticket.title}</h2>
        <StateBadge state={ticket.state} />
        <PriorityBadge priority={ticket.priority} />
      </div>

      <dl className="ticket-meta">
        <dt>{T.detail.origin}</dt>
        <dd>{ORIGIN_LABELS[ticket.origin]}</dd>
        <dt>{T.detail.priority}</dt>
        <dd>{PRIORITY_LABELS[ticket.priority]}</dd>
        <dt>{T.detail.created}</dt>
        <dd>{formatDateTime(ticket.createdAt)}</dd>
        <dt>{T.detail.updated}</dt>
        <dd>{formatDateTime(ticket.updatedAt)}</dd>
      </dl>

      <div className="ticket-section">
        <h3>{T.detail.description}</h3>
        <p className="ticket-description">{ticket.description}</p>
      </div>

      {canManage && ticket.state === 'New' && <TriageForm ticket={ticket} onDone={changed} />}

      {canManage && (transitions.length > 0 || visitBlocked) && (
        <div className="ticket-section">
          <div className="btn-row" role="group" aria-label={T.transition.groupLabel}>
            {transitions.map((target) => (
              <button
                key={target}
                type="button"
                className="btn btn--ghost"
                disabled={mover.busy}
                onClick={() => void mover.move(ticket.id, ticket.state, target)}
              >
                {STATE_LABELS[target]}
              </button>
            ))}
          </div>
          {visitBlocked && <p className="slot-note">{T.transition.visitSlot}</p>}
        </div>
      )}

      {canManage && (
        // SLOT: technician assignment. The backend has no endpoint yet (visits, #36); render the picker here later.
        <div className="ticket-section" data-slot="technician-assignment">
          <h3>{T.detail.assignment}</h3>
          <p className="slot-note">{T.detail.assignmentSlot}</p>
        </div>
      )}

      <div className="ticket-section">
        <h3>{T.detail.attachments}</h3>
        <Attachments items={ticket.attachments} />
      </div>

      <div className="ticket-section">
        <h3>{T.detail.timeline}</h3>
        {timeline.loading && <Skeleton lines={3} label={T.detail.loading} />}
        {timeline.error && (
          <p role="alert" className="alert">
            {T.detail.timelineError}
          </p>
        )}
        {timeline.data && timeline.data.length === 0 && <p>{T.detail.timelineEmpty}</p>}
        {timeline.data && timeline.data.length > 0 && (
          <ol className="timeline">
            {timeline.data.map((entry) =>
              entry.kind === 'state' ? (
                <li key={`s-${entry.change.id}`} className="timeline__item">
                  <div className="timeline__head">
                    <strong>
                      {T.detail.stateChange(entry.change.fromState ? STATE_LABELS[entry.change.fromState] : null, STATE_LABELS[entry.change.toState])}
                    </strong>
                    <time dateTime={entry.at}>{formatDateTime(entry.at)}</time>
                    <span className="mono">{T.detail.author(entry.change.changedBy)}</span>
                  </div>
                  {entry.change.note && <p className="timeline__body">{entry.change.note}</p>}
                </li>
              ) : (
                <li key={`c-${entry.comment.id}`} className="timeline__item timeline__item--comment">
                  <div className="timeline__head">
                    <span className={entry.comment.isPublic ? 'badge badge--success' : 'badge'}>
                      {entry.comment.isPublic ? T.detail.public : T.detail.internal}
                    </span>
                    <time dateTime={entry.at}>{formatDateTime(entry.at)}</time>
                    <span className="mono">{T.detail.author(entry.comment.authorId)}</span>
                  </div>
                  <p className="timeline__body">{entry.comment.body}</p>
                </li>
              ),
            )}
          </ol>
        )}
        {canComment && <CommentForm ticketId={ticket.id} onAdded={() => timeline.reload()} />}
      </div>
    </section>
  )
}

function Attachments({ items }: { items: readonly TicketAttachment[] }) {
  const toast = useToast()
  if (items.length === 0) return <p>{T.detail.noAttachments}</p>
  return (
    <ul className="file-list">
      {items.map((a) => (
        <li key={a.id}>
          <span>{a.fileName}</span>
          <span className="mono">{Math.max(1, Math.round(a.sizeBytes / 1024))} kB</span>
          <button
            type="button"
            className="btn btn--ghost btn--sm"
            aria-label={`${T.detail.download}: ${a.fileName}`}
            onClick={() => downloadAttachment(a).catch(() => toast.error(T.detail.downloadFailed))}
          >
            {T.detail.download}
          </button>
        </li>
      ))}
    </ul>
  )
}

function TriageForm({ ticket, onDone }: { ticket: Ticket; onDone: (ticket: Ticket) => void }) {
  const toast = useToast()
  const [priority, setPriority] = useState<TicketPriority>(ticket.priority)
  const [note, setNote] = useState('')
  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    const next = await triageTicket(ticket.id, priority, note.trim())
    toast.success(T.detail.triaged)
    onDone(next)
  })

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    await submit()
  }

  return (
    <form onSubmit={onSubmit} noValidate className="ticket-section card">
      <h3>{T.detail.triage}</h3>
      <p>{T.detail.triageHint}</p>
      <FormField label={T.detail.priority} errors={fieldErrors.priority}>
        {(props) => (
          <select {...props} value={priority} onChange={(e) => setPriority(e.target.value as TicketPriority)}>
            {TICKET_PRIORITIES.map((p) => (
              <option key={p} value={p}>
                {PRIORITY_LABELS[p]}
              </option>
            ))}
          </select>
        )}
      </FormField>
      <FormField label={T.detail.triageNote} errors={fieldErrors.note}>
        {(props) => <textarea {...props} maxLength={NOTE_MAX} value={note} onChange={(e) => setNote(e.target.value)} />}
      </FormField>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <button type="submit" className="btn btn--primary" disabled={busy}>
        {T.detail.triageSubmit}
      </button>
    </form>
  )
}

function CommentForm({ ticketId, onAdded }: { ticketId: string; onAdded: () => void }) {
  const toast = useToast()
  const [body, setBody] = useState('')
  const [isPublic, setIsPublic] = useState(false)
  const [clientError, setClientError] = useState<string[]>([])
  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    await addTicketComment(ticketId, body.trim(), isPublic)
    setBody('')
    setIsPublic(false)
    toast.success(T.detail.commentAdded)
    onAdded()
  })

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const text = body.trim()
    if (!text) return setClientError([T.detail.commentRequired])
    if (text.length > COMMENT_MAX) return setClientError([T.detail.commentTooLong])
    setClientError([])
    await submit()
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      <FormField label={T.detail.commentLabel} errors={[...clientError, ...(fieldErrors.body ?? [])]}>
        {(props) => <textarea {...props} value={body} onChange={(e) => setBody(e.target.value)} />}
      </FormField>
      <label className="check-field">
        <input type="checkbox" checked={isPublic} onChange={(e) => setIsPublic(e.target.checked)} />
        {T.detail.commentPublic}
      </label>
      {!isPublic && <p className="kanban__empty">{T.detail.commentInternalHint}</p>}
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <div className="btn-row">
        <button type="submit" className="btn btn--ghost" disabled={busy}>
          {T.detail.commentSubmit}
        </button>
      </div>
    </form>
  )
}
