import { useMemo, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/problem'
import { Dialog } from '../../components/Dialog'
import { EmptyState } from '../../components/EmptyState'
import { FormField } from '../../components/forms'
import { PageHeader } from '../../components/PageHeader'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/toast'
import { FilterSelect, Toolbar } from '../../components/Toolbar'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { MESSAGES } from '../../lib/messages'
import { T as TICKET_TEXTS, VISIT_STATE_LABELS, VISIT_STATE_TONES } from '../tickets/messages'
import { listTickets } from '../tickets/ticketsApi'
import {
  MAX_VISIT_MINUTES,
  MIN_VISIT_MINUTES,
  getTechnicianVisits,
  listTechnicians,
  planVisit,
  rescheduleVisit,
  technicianName,
} from '../tickets/visitsApi'
import { addDays, dayKey, findCollisions, parseWeek, weekDays, DAYS_IN_WEEK, type TechnicianVisit } from './calendarLogic'
import { dayOf, timeOf, toLocalInput } from './myVisits'
import './visits.css'

const T = TICKET_TEXTS.calendar
const DEFAULT_MINUTES = '120'
const DEFAULT_START_HOUR = 8

/** Rethrows a 409 with a Slovak text; the server explains the conflict in English. */
async function withConflictMessage(action: () => Promise<unknown>, message: string) {
  try {
    await action()
  } catch (error) {
    throw error instanceof ApiError && error.status === 409 ? new ApiError(409, message) : error
  }
}

function validateSlot(start: string, minutes: string): Record<string, string[]> {
  const errors: Record<string, string[]> = {}
  if (!start) errors.start = [T.startRequired]
  const value = Number(minutes)
  if (!Number.isInteger(value) || value < MIN_VISIT_MINUTES || value > MAX_VISIT_MINUTES) errors.estimatedMinutes = [T.minutesRange]
  return errors
}

interface SlotFieldsProps {
  start: string
  minutes: string
  onStart: (value: string) => void
  onMinutes: (value: string) => void
  errorsFor: (key: string) => string[]
}

function SlotFields({ start, minutes, onStart, onMinutes, errorsFor }: SlotFieldsProps) {
  return (
    <>
      <FormField label={T.start} required errors={errorsFor('start')}>
        {(props) => <input {...props} required type="datetime-local" value={start} onChange={(e) => onStart(e.target.value)} />}
      </FormField>
      <FormField label={T.minutes} required errors={errorsFor('estimatedMinutes')}>
        {(props) => (
          <input {...props} required type="number" min={MIN_VISIT_MINUTES} max={MAX_VISIT_MINUTES} step={15} value={minutes} onChange={(e) => onMinutes(e.target.value)} />
        )}
      </FormField>
    </>
  )
}

function DialogActions({ busy, submitLabel, onClose }: { busy: boolean; submitLabel: string; onClose: () => void }) {
  return (
    <div className="visit-calendar__dialog-actions">
      <button type="button" className="btn" onClick={onClose}>
        {T.cancel}
      </button>
      <button type="submit" className="btn btn--primary" disabled={busy} aria-busy={busy}>
        {submitLabel}
      </button>
    </div>
  )
}

function PlanDialog({ day, onClose, onDone }: { day: Date; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [ticketId, setTicketId] = useState('')
  const [start, setStart] = useState(() => toLocalInput(new Date(day.getFullYear(), day.getMonth(), day.getDate(), DEFAULT_START_HOUR)))
  const [minutes, setMinutes] = useState(DEFAULT_MINUTES)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  // A visit can be planned only from remote diagnosis (BPMN #67), so the other tickets are not offered.
  const tickets = useLoad((signal) => listTickets({ state: 'RemoteDiagnosis', page: 1, pageSize: 100 }, signal), 'plannable-tickets')
  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    await withConflictMessage(() => planVisit(ticketId, new Date(start).toISOString(), Number(minutes)), T.planConflict)
    toast.success(T.planned)
    onDone()
  })
  const errorsFor = (key: string) => [...(errors[key] ?? []), ...(fieldErrors[key] ?? [])]
  const options = tickets.data?.items ?? []

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const next = validateSlot(start, minutes)
    if (!ticketId) next.ticket = [T.ticketRequired]
    setErrors(next)
    if (Object.keys(next).length === 0) await submit()
  }

  return (
    <Dialog title={T.planTitle} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate className="visit-calendar__form">
        {tickets.error && <p role="alert">{T.ticketsError}</p>}
        {!tickets.loading && !tickets.error && options.length === 0 && <p>{T.noTickets}</p>}
        <FormField label={T.ticket} required errors={errorsFor('ticket')}>
          {(props) => (
            <select {...props} required value={ticketId} onChange={(e) => setTicketId(e.target.value)}>
              <option value="">{T.ticketPlaceholder}</option>
              {options.map((ticket) => (
                <option key={ticket.id} value={ticket.id}>
                  {ticket.title}
                </option>
              ))}
            </select>
          )}
        </FormField>
        <SlotFields start={start} minutes={minutes} onStart={setStart} onMinutes={setMinutes} errorsFor={errorsFor} />
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <DialogActions busy={busy} submitLabel={T.planSubmit} onClose={onClose} />
      </form>
    </Dialog>
  )
}

function RescheduleDialog({ visit, onClose, onDone }: { visit: TechnicianVisit; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [start, setStart] = useState(() => toLocalInput(new Date(visit.start)))
  const [minutes, setMinutes] = useState(String(visit.estimatedMinutes))
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    await withConflictMessage(
      () => rescheduleVisit(visit.visitId, { start: new Date(start).toISOString(), estimatedMinutes: Number(minutes), reason: reason.trim() }),
      T.moveConflict,
    )
    toast.success(T.moved)
    onDone()
  })
  const errorsFor = (key: string) => [...(errors[key] ?? []), ...(fieldErrors[key] ?? [])]

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const next = validateSlot(start, minutes)
    if (!reason.trim()) next.reason = [T.reasonRequired]
    setErrors(next)
    if (Object.keys(next).length === 0) await submit()
  }

  return (
    <Dialog title={T.moveTitle(visit.ticketTitle)} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate className="visit-calendar__form">
        <p>{T.moveCurrent(`${dayOf(visit.start)} ${timeOf(visit.start)}`)}</p>
        <SlotFields start={start} minutes={minutes} onStart={setStart} onMinutes={setMinutes} errorsFor={errorsFor} />
        <FormField label={T.reason} required errors={errorsFor('reason')}>
          {(props) => <textarea {...props} required rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />}
        </FormField>
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <DialogActions busy={busy} submitLabel={T.moveSubmit} onClose={onClose} />
      </form>
    </Dialog>
  )
}

/**
 * `/visit-calendar`: week of planned visits for planners (Admin, Operator), optionally of one technician.
 * Plans a visit on a chosen day and moves a planned one; `?week=` and `?technician=` are in the URL.
 */
export default function VisitCalendarPage() {
  const [params, setParams] = useSearchParams()
  const [now] = useState(() => new Date())
  const weekParam = params.get('week')
  const monday = useMemo(() => parseWeek(weekParam, now), [weekParam, now])
  const days = useMemo(() => weekDays(monday), [monday])
  const [planDay, setPlanDay] = useState<Date | null>(null)
  const [moving, setMoving] = useState<TechnicianVisit | null>(null)

  const technicians = useLoad((signal) => listTechnicians(signal), 'calendar-technicians')
  const all = technicians.data ?? []
  const selectedId = all.some((t) => t.id === params.get('technician')) ? params.get('technician') : null
  const shown = selectedId ? all.filter((t) => t.id === selectedId) : all
  const shownIds = shown.map((t) => t.id).join(',')

  const calendar = useLoad(
    async (signal) => {
      const to = addDays(monday, DAYS_IN_WEEK)
      const calendars = await Promise.all(shown.map((t) => getTechnicianVisits(t.id, monday, to, signal)))
      return calendars.flatMap((c) => c.visits.map((visit): TechnicianVisit => ({ ...visit, technicianId: c.technicianId })))
    },
    `${dayKey(monday)}|${shownIds}`,
  )
  const visits = useMemo(() => calendar.data ?? [], [calendar.data])
  const collisions = useMemo(() => findCollisions(visits), [visits])
  const names = new Map(all.map((t) => [t.id, technicianName(t)]))

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    setParams(next, { replace: true })
  }

  const closeAndReload = () => {
    setPlanDay(null)
    setMoving(null)
    calendar.reload()
  }

  const loading = technicians.loading || calendar.loading
  const failed = technicians.error || calendar.error

  return (
    <section className="visit-calendar">
      <PageHeader title={T.title} subtitle={T.subtitle} />
      <Toolbar
        aside={
          <div className="visit-calendar__week" role="group" aria-label={T.weekLabel}>
            <button type="button" className="btn btn--sm" onClick={() => update({ week: dayKey(addDays(monday, -DAYS_IN_WEEK)) })}>
              ← {T.previousWeek}
            </button>
            <button type="button" className="btn btn--sm" onClick={() => update({ week: null })}>
              {T.thisWeek}
            </button>
            <button type="button" className="btn btn--sm" onClick={() => update({ week: dayKey(addDays(monday, DAYS_IN_WEEK)) })}>
              {T.nextWeek} →
            </button>
          </div>
        }
      >
        <FilterSelect
          label={T.technician}
          value={selectedId ?? ''}
          onChange={(value) => update({ technician: value })}
          options={[{ value: '', label: T.allTechnicians }, ...all.map((t) => ({ value: t.id, label: technicianName(t) }))]}
        />
      </Toolbar>

      {loading && <Skeleton lines={5} label={T.loading} />}
      {failed && (
        <div role="alert" className="kpi__error">
          <p>{T.error}</p>
          <button
            type="button"
            className="btn btn--sm"
            onClick={() => {
              technicians.reload()
              calendar.reload()
            }}
          >
            {MESSAGES.retry}
          </button>
        </div>
      )}
      {!loading && !failed && all.length === 0 && <EmptyState title={T.noTechnicians} />}
      {collisions.size > 0 && (
        <p role="note" className="visit-calendar__warning">
          {T.collisionsFound(collisions.size)}
        </p>
      )}

      {!loading && !failed && all.length > 0 && (
        <div className="visit-calendar__days">
          {days.map((day) => {
            const key = dayKey(day)
            const label = dayOf(day.toISOString())
            const dayVisits = visits.filter((v) => dayKey(new Date(v.start)) === key).sort((a, b) => a.start.localeCompare(b.start))
            return (
              <section key={key} className="visit-calendar__day" aria-label={label}>
                <h3 className="visit-calendar__day-title">{label}</h3>
                {dayVisits.length === 0 && <p className="kanban__empty">{T.noVisits}</p>}
                <ul className="visit-calendar__visits">
                  {dayVisits.map((visit) => {
                    const tone = VISIT_STATE_TONES[visit.state]
                    return (
                      <li key={visit.visitId} className={`visit-calendar__visit${collisions.has(visit.visitId) ? ' visit-calendar__visit--collision' : ''}`}>
                        <span className="visit-card__time">
                          {timeOf(visit.start)} – {timeOf(visit.end)}
                        </span>
                        <Link to={`/tickets/${visit.ticketId}`}>{visit.ticketTitle}</Link>
                        <span className="visit-card__meta">{[names.get(visit.technicianId), visit.installationName].filter(Boolean).join(' · ')}</span>
                        <span className="visit-card__badges">
                          <span className={tone ? `badge badge--${tone}` : 'badge'}>{VISIT_STATE_LABELS[visit.state]}</span>
                          {collisions.has(visit.visitId) && <span className="badge badge--error">{T.collision}</span>}
                        </span>
                        {visit.state === 'Planned' && (
                          <button type="button" className="btn btn--ghost btn--sm" aria-label={T.moveLabel(visit.ticketTitle)} onClick={() => setMoving(visit)}>
                            {T.move}
                          </button>
                        )}
                      </li>
                    )
                  })}
                </ul>
                <button type="button" className="btn btn--ghost btn--sm" aria-label={T.planOn(label)} onClick={() => setPlanDay(day)}>
                  + {T.plan}
                </button>
              </section>
            )
          })}
        </div>
      )}

      {planDay && <PlanDialog day={planDay} onClose={() => setPlanDay(null)} onDone={closeAndReload} />}
      {moving && <RescheduleDialog visit={moving} onClose={() => setMoving(null)} onDone={closeAndReload} />}
    </section>
  )
}
