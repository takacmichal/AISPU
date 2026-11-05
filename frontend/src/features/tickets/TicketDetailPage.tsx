import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { userMessage } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { canAccess, findItem } from '../../auth/navigation'
import { Avatar } from '../../components/Avatar'
import { EmptyState } from '../../components/EmptyState'
import { KeyValueList } from '../../components/KeyValueList'
import { PageHeader } from '../../components/PageHeader'
import { Skeleton } from '../../components/Skeleton'
import { FormField } from '../../components/forms'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { formatDateTime } from '../../lib/format'
import {
  CAUSE_LABELS,
  COMMENT_ROLES,
  MANAGE_ROLES,
  ORIGIN_LABELS,
  PRIORITY_LABELS,
  STATE_LABELS,
  T,
  VISIT_STATE_LABELS,
  VISIT_STATE_TONES,
} from './messages'
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
  listInstallationOptions,
  triageTicket,
  type Ticket,
  type TicketAttachment,
  type TicketPriority,
} from './ticketsApi'
import { buildTimeline } from './timeline'
import { useTicketTransition } from './useTicketTransition'
import {
  ASSIGNABLE_STATES,
  MAX_VISIT_MINUTES,
  MIN_VISIT_MINUTES,
  assignTechnician,
  getTicketVisits,
  listTechnicians,
  planVisit,
  technicianName,
  verifyTicket,
  type Technician,
  type TicketVisits,
  type Visit,
} from './visitsApi'

/** Roles that may verify restored production after the visit (TicketsField). */
const FIELD_ROLES: readonly string[] = ['Admin', 'Operator', 'Technician']

const timeRange = (from: string, to: string) => `${formatDateTime(from)} – ${new Date(to).toLocaleTimeString('sk-SK', { hour: '2-digit', minute: '2-digit' })}`

/**
 * `/tickets/:id` (wireframe "Detail tiketu"): description with attachments, visits, service protocol and comments
 * on the left; details, triage, assigned technician and the installation on the right.
 */
export default function TicketDetailPage() {
  const { id = '' } = useParams()
  const { roles } = useAuth()
  const toast = useToast()
  const confirm = useConfirm()
  const [latest, setLatest] = useState<Ticket | null>(null)
  const [verifying, setVerifying] = useState(false)
  const canManage = MANAGE_ROLES.some((r) => roles.includes(r))
  const canComment = COMMENT_ROLES.some((r) => roles.includes(r))
  const canField = FIELD_ROLES.some((r) => roles.includes(r))
  const isCustomer = roles.includes('Customer') && !canComment

  const { data, loading, reload } = useLoad((signal) => getTicket(id, signal), id)
  const timeline = useLoad(async (signal) => {
    const [history, comments] = await Promise.all([getTicketHistory(id, signal), getTicketComments(id, signal)])
    // The API already hides internal comments from customers; filter again so an internal note never shows up.
    return buildTimeline(history, isCustomer ? comments.filter((c) => c.isPublic) : comments)
  }, `${id}|${isCustomer}`)
  const visits = useLoad((signal) => getTicketVisits(id, signal), id)
  const technicians = useLoad(async (signal) => (canManage ? listTechnicians(signal) : null), `technicians|${canManage}`)
  const installations = useLoad(async (signal) => listInstallationOptions(signal).catch(() => []), 'ticket-installations')

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

  const refreshAll = () => {
    setLatest(null)
    reload()
    timeline.reload()
    visits.reload()
  }
  const changed = (next: Ticket) => {
    setLatest(next)
    timeline.reload()
    reload()
  }

  const transitions = canManage ? ticket.allowedTransitions.filter((s) => !VISIT_WORKFLOW_STATES.includes(s)) : []
  const visitBlocked = canManage && ticket.allowedTransitions.some((s) => VISIT_WORKFLOW_STATES.includes(s))
  const visitList = visits.data?.visits ?? []
  const hasProtocol = visitList.some((v) => v.state === 'Done' && v.report)
  const canVerify = canField && ticket.state === 'OnSite' && hasProtocol

  async function verify() {
    const ok = await confirm({ title: T.detail.verifyTitle, message: T.detail.verifyMessage, confirmLabel: T.detail.verify })
    if (!ok) return
    setVerifying(true)
    try {
      await verifyTicket(id)
      toast.success(T.detail.verified)
      refreshAll()
    } catch (e) {
      toast.error(userMessage(e))
    } finally {
      setVerifying(false)
    }
  }

  const installation = installations.data?.find((i) => i.id === ticket.installationId)
  const installationRoute = findItem('/installations')
  const monitoringRoute = findItem('/monitoring')
  const technicianNames = new Map((technicians.data ?? []).map((t) => [t.id, technicianName(t)]))

  return (
    <section className="ticket-page">
      <PageHeader
        title={ticket.title}
        status={<StateBadge state={ticket.state} />}
        subtitle={`${PRIORITY_LABELS[ticket.priority]} priorita · ${ORIGIN_LABELS[ticket.origin]} · ${T.detail.created} ${formatDateTime(ticket.createdAt)}`}
        actions={
          (transitions.length > 0 || canVerify) && (
            <div className="btn-row" role="group" aria-label={T.transition.groupLabel}>
              {transitions.map((target, index) => (
                <button
                  key={target}
                  type="button"
                  className={index === 0 && !canVerify ? 'btn btn--primary' : 'btn btn--ghost'}
                  disabled={mover.busy}
                  onClick={() => void mover.move(ticket.id, ticket.state, target)}
                >
                  {STATE_LABELS[target]}
                </button>
              ))}
              {canVerify && (
                <button type="button" className="btn btn--primary" disabled={verifying} onClick={() => void verify()}>
                  {T.detail.verify}
                </button>
              )}
            </div>
          )
        }
      />
      {visitBlocked && !canVerify && <p className="ticket-note">{T.transition.visitSlot}</p>}

      <div className="ticket-detail">
        <div className="ticket-detail__main">
          <article className="card ticket-card" aria-labelledby="ticket-description-title">
            <h3 id="ticket-description-title" className="ticket-card__title">
              {T.detail.description}
            </h3>
            <p className="ticket-description">{ticket.description}</p>
            <h4 className="ticket-card__subtitle">{T.detail.attachments}</h4>
            <Attachments items={ticket.attachments} />
          </article>

          <VisitsCard
            ticket={ticket}
            visits={visits.data}
            loading={visits.loading}
            failed={Boolean(visits.error)}
            canPlan={canManage}
            technicianNames={technicianNames}
            onPlanned={refreshAll}
          />

          <ProtocolCard visits={visitList} />

          <article className="card ticket-card" aria-labelledby="ticket-timeline-title">
            <h3 id="ticket-timeline-title" className="ticket-card__title">
              {T.detail.timeline}
            </h3>
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
          </article>
        </div>

        <aside className="ticket-detail__side" aria-label={T.detail.sideLabel}>
          <article className="card ticket-card" aria-labelledby="ticket-details-title">
            <h3 id="ticket-details-title" className="ticket-card__title">
              {T.detail.details}
            </h3>
            <KeyValueList
              items={[
                { label: T.detail.state, value: <StateBadge state={ticket.state} /> },
                { label: T.detail.priority, value: <PriorityBadge priority={ticket.priority} /> },
                { label: T.detail.origin, value: ORIGIN_LABELS[ticket.origin] },
                { label: T.detail.created, value: formatDateTime(ticket.createdAt) },
                { label: T.detail.updated, value: formatDateTime(ticket.updatedAt) },
              ]}
            />
          </article>

          {canManage && ticket.state === 'New' && <TriageForm ticket={ticket} onDone={changed} />}

          {canManage && (
            <TechnicianPicker
              ticket={ticket}
              technicians={technicians.data ?? null}
              loading={technicians.loading}
              failed={Boolean(technicians.error)}
              assignedId={visits.data?.assignedTechnicianId ?? null}
              onAssigned={() => visits.reload()}
            />
          )}

          <article className="card ticket-card" aria-labelledby="ticket-installation-title">
            <h3 id="ticket-installation-title" className="ticket-card__title">
              {T.detail.installationCard}
            </h3>
            <p className="ticket-card__lead">{installation?.name ?? '–'}</p>
            <div className="btn-row">
              {installationRoute && canAccess(installationRoute, roles) && (
                <Link to={`/installations/${ticket.installationId}`} className="btn btn--ghost btn--sm">
                  {T.detail.installationOpen}
                </Link>
              )}
              {monitoringRoute && canAccess(monitoringRoute, roles) && (
                <Link to={`/monitoring?installation=${ticket.installationId}`} className="btn btn--ghost btn--sm">
                  {T.detail.monitoringOpen}
                </Link>
              )}
            </div>
          </article>
        </aside>
      </div>
    </section>
  )
}

function VisitsCard({
  ticket,
  visits,
  loading,
  failed,
  canPlan,
  technicianNames,
  onPlanned,
}: {
  ticket: Ticket
  visits: TicketVisits | undefined
  loading: boolean
  failed: boolean
  canPlan: boolean
  technicianNames: Map<string, string>
  onPlanned: () => void
}) {
  const list = [...(visits?.visits ?? [])].sort((a, b) => b.plannedStart.localeCompare(a.plannedStart))
  const showPlan = canPlan && ticket.state === 'RemoteDiagnosis'
  return (
    <article className="card ticket-card" aria-labelledby="ticket-visits-title">
      <h3 id="ticket-visits-title" className="ticket-card__title">
        {T.detail.visits}
      </h3>
      {loading && <Skeleton lines={2} label={T.detail.loading} />}
      {failed && (
        <p role="alert" className="alert">
          {T.detail.visitsError}
        </p>
      )}
      {visits && list.length === 0 && <p className="ticket-card__muted">{T.detail.visitsEmpty}</p>}
      {list.length > 0 && (
        <ul className="visit-list">
          {list.map((visit) => {
            const tone = VISIT_STATE_TONES[visit.state]
            const technician = visit.technicianId ? technicianNames.get(visit.technicianId) : undefined
            return (
              <li key={visit.id} className="visit-list__item">
                <div>
                  <p className="visit-list__time">{timeRange(visit.plannedStart, visit.plannedEnd)}</p>
                  <p className="ticket-card__muted">
                    {[technician, T.detail.visitDuration(visit.estimatedMinutes)].filter(Boolean).join(' · ')}
                  </p>
                </div>
                <span className={tone ? `badge badge--${tone}` : 'badge'}>{VISIT_STATE_LABELS[visit.state]}</span>
              </li>
            )
          })}
        </ul>
      )}
      {showPlan && visits && <PlanVisitForm ticketId={ticket.id} hasTechnician={Boolean(visits.assignedTechnicianId)} onPlanned={onPlanned} />}
    </article>
  )
}

function PlanVisitForm({ ticketId, hasTechnician, onPlanned }: { ticketId: string; hasTechnician: boolean; onPlanned: () => void }) {
  const toast = useToast()
  const [start, setStart] = useState('')
  const [minutes, setMinutes] = useState('120')
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    await planVisit(ticketId, new Date(start).toISOString(), Number(minutes))
    toast.success(T.detail.planned)
    onPlanned()
  })

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const next: Record<string, string[]> = {}
    if (!start) next.start = [T.detail.planStartRequired]
    const value = Number(minutes)
    if (!Number.isInteger(value) || value < MIN_VISIT_MINUTES || value > MAX_VISIT_MINUTES) next.estimatedMinutes = [T.detail.planMinutesRange]
    setErrors(next)
    if (Object.keys(next).length === 0) await submit()
  }

  if (!hasTechnician) return <p className="slot-note">{T.detail.planNeedsTechnician}</p>

  return (
    <form onSubmit={onSubmit} noValidate className="visit-plan">
      <h4 className="ticket-card__subtitle">{T.detail.planTitle}</h4>
      <p className="ticket-card__muted">{T.detail.planHint}</p>
      <div className="visit-plan__fields">
        <FormField label={T.detail.planStart} errors={[...(errors.start ?? []), ...(fieldErrors.start ?? [])]}>
          {(props) => <input {...props} type="datetime-local" value={start} onChange={(e) => setStart(e.target.value)} />}
        </FormField>
        <FormField label={T.detail.planMinutes} errors={[...(errors.estimatedMinutes ?? []), ...(fieldErrors.estimatedMinutes ?? [])]}>
          {(props) => (
            <input {...props} type="number" min={MIN_VISIT_MINUTES} max={MAX_VISIT_MINUTES} step={15} value={minutes} onChange={(e) => setMinutes(e.target.value)} />
          )}
        </FormField>
      </div>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <button type="submit" className="btn" disabled={busy} aria-busy={busy}>
        {T.detail.planSubmit}
      </button>
    </form>
  )
}

function ProtocolCard({ visits }: { visits: readonly Visit[] }) {
  const done = [...visits].filter((v) => v.report).sort((a, b) => (b.report?.reportedAt ?? '').localeCompare(a.report?.reportedAt ?? ''))[0]
  const report = done?.report
  return (
    <article className="card ticket-card" aria-labelledby="ticket-protocol-title">
      <h3 id="ticket-protocol-title" className="ticket-card__title">
        {T.detail.protocol}
      </h3>
      {!report ? (
        <p className="ticket-card__muted">{T.detail.protocolEmpty}</p>
      ) : (
        <KeyValueList
          items={[
            { label: T.detail.protocolCause, value: CAUSE_LABELS[report.cause] ?? report.cause },
            { label: T.detail.protocolTime, value: timeRange(report.arrivedAt, report.departedAt) },
            { label: T.detail.protocolWork, value: report.workDone },
            ...(report.findings ? [{ label: T.detail.protocolFindings, value: report.findings }] : []),
            {
              label: T.detail.protocolMaterial,
              value: done.materials.length > 0 ? done.materials.map((m) => `${m.name} ${m.quantity} ${m.unit}`).join(', ') : T.detail.protocolNoMaterial,
            },
          ]}
        />
      )}
    </article>
  )
}

function TechnicianPicker({
  ticket,
  technicians,
  loading,
  failed,
  assignedId,
  onAssigned,
}: {
  ticket: Ticket
  technicians: Technician[] | null
  loading: boolean
  failed: boolean
  assignedId: string | null
  onAssigned: () => void
}) {
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const assignable = ASSIGNABLE_STATES.includes(ticket.state)

  async function choose(technician: Technician) {
    setBusy(true)
    try {
      await assignTechnician(ticket.id, technician.id)
      toast.success(T.detail.assigned(technicianName(technician)))
      onAssigned()
    } catch (e) {
      toast.error(userMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <article className="card ticket-card" aria-labelledby="ticket-technician-title">
      <h3 id="ticket-technician-title" className="ticket-card__title">
        {T.detail.assignment}
      </h3>
      {loading && <Skeleton lines={2} label={T.detail.loading} />}
      {failed && <p className="ticket-card__muted">{T.detail.assignmentUnavailable}</p>}
      {!assignable && <p className="ticket-card__muted">{T.detail.assignmentClosed}</p>}
      {technicians && technicians.length > 0 && (
        <fieldset className="technician-picker" disabled={!assignable || busy}>
          <legend className="sr-only">{T.detail.assignment}</legend>
          {technicians.map((t) => {
            const name = technicianName(t)
            const checked = t.id === assignedId
            return (
              <label key={t.id} className={checked ? 'technician-option technician-option--checked' : 'technician-option'}>
                <input type="radio" name="technician" checked={checked} onChange={() => void choose(t)} aria-label={T.detail.assignmentChoose(name)} />
                <Avatar name={name} size="sm" />
                <span className="technician-option__name">
                  {name}
                  {t.email && t.email !== name && <span className="ticket-card__muted">{t.email}</span>}
                </span>
                {checked && <span className="badge badge--info">{T.detail.current}</span>}
              </label>
            )
          })}
        </fieldset>
      )}
      {technicians && technicians.length === 0 && <EmptyState title={T.detail.assignmentNone} />}
      {!technicians && !loading && assignedId && <p>{T.detail.assignmentAssigned}</p>}
    </article>
  )
}

function Attachments({ items }: { items: readonly TicketAttachment[] }) {
  const toast = useToast()
  if (items.length === 0) return <p className="ticket-card__muted">{T.detail.noAttachments}</p>
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
    <form onSubmit={onSubmit} noValidate className="card ticket-card">
      <h3 className="ticket-card__title">{T.detail.triage}</h3>
      <p className="ticket-card__muted">{T.detail.triageHint}</p>
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
    <form onSubmit={onSubmit} noValidate className="ticket-comment-form">
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
