import { useEffect, useEffectEvent, useMemo, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { userMessage } from '../../api/problem'
import { EmptyState } from '../../components/EmptyState'
import { KeyValueList } from '../../components/KeyValueList'
import { PageHeader } from '../../components/PageHeader'
import { SegmentedControl } from '../../components/SegmentedControl'
import { Skeleton } from '../../components/Skeleton'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { MESSAGES } from '../../lib/messages'
import { CAUSE_LABELS, T as TICKET_TEXTS, VISIT_STATE_LABELS, VISIT_STATE_TONES } from '../tickets/messages'
import { PriorityBadge } from '../tickets/TicketBadges'
import {
  VISIT_CAUSES,
  VISIT_TEXT_MAX,
  getMyVisits,
  getVisit,
  reportVisit,
  startVisit,
  type CalendarVisit,
  type Visit,
  type VisitCause,
  type VisitState,
} from '../tickets/visitsApi'
import { RANGES, dayOf, formatAddress, navigationHref, phoneHref, rangeWindow, timeOf, toLocalInput, type Range } from './myVisits'
import { clearProtocolDraft, readProtocolDraft, writeProtocolDraft, type ProtocolValues } from './protocolDraft'
import './visits.css'

const T = TICKET_TEXTS.visits

function VisitBadge({ state }: { state: VisitState }) {
  const tone = VISIT_STATE_TONES[state]
  return <span className={tone ? `badge badge--${tone}` : 'badge'}>{VISIT_STATE_LABELS[state]}</span>
}

/**
 * `/my-visits` (wireframe 9a to 9c, mobile first): the technician's visits for today, tomorrow or the week,
 * the visit detail with call, navigation and a checklist, and the service protocol form. `?range=` and `?visit=` are in the URL.
 */
export default function MyVisitsPage() {
  const [params, setParams] = useSearchParams()
  const range: Range = RANGES.find((r) => r === params.get('range')) ?? 'today'
  const selectedId = params.get('visit')
  const [now] = useState(() => new Date())
  const period = useMemo(() => rangeWindow(range, now), [range, now])

  const calendar = useLoad((signal) => getMyVisits(period.from, period.to, signal), `${range}|${period.from.toISOString()}`)
  const visits = calendar.data?.visits ?? []
  const selected = selectedId ? visits.find((v) => v.visitId === selectedId) : undefined

  const listHref = range === 'today' ? '/my-visits' : `/my-visits?range=${range}`
  const visitHref = (id: string) => `/my-visits?${new URLSearchParams({ ...(range === 'today' ? {} : { range }), visit: id }).toString()}`

  if (selectedId) {
    if (calendar.loading) return <Skeleton lines={5} label={T.loading} />
    return (
      <section className="my-visits">
        <p className="my-visits__back">
          <Link to={listHref}>← {T.back}</Link>
        </p>
        {selected ? (
          <VisitDetail entry={selected} onChanged={calendar.reload} />
        ) : (
          <EmptyState title={T.notFound} action={<Link to={listHref}>{T.back}</Link>} />
        )}
      </section>
    )
  }

  // Group by day so the week view reads like a calendar.
  const days = visits.reduce<{ day: string; items: CalendarVisit[] }[]>((groups, visit) => {
    const day = dayOf(visit.start)
    const last = groups[groups.length - 1]
    if (last?.day === day) last.items.push(visit)
    else groups.push({ day, items: [visit] })
    return groups
  }, [])

  return (
    <section className="my-visits">
      <PageHeader title={T.title} subtitle={T.subtitle} />
      <SegmentedControl<Range>
        label={T.rangeLabel}
        value={range}
        onChange={(next) => setParams(next === 'today' ? {} : { range: next }, { replace: true })}
        options={RANGES.map((r) => ({ value: r, label: T.ranges[r] }))}
      />
      {calendar.loading && <Skeleton lines={4} label={T.loading} />}
      {calendar.error && (
        <div role="alert" className="kpi__error">
          <p>{T.error}</p>
          <button type="button" className="btn btn--sm" onClick={calendar.reload}>
            {MESSAGES.retry}
          </button>
        </div>
      )}
      {calendar.data && visits.length === 0 && <EmptyState title={T.empty} />}
      {days.map((group) => (
        <section key={group.day} className="my-visits__day" aria-label={group.day}>
          {range === 'week' && <h3 className="my-visits__day-title">{group.day}</h3>}
          <ul className="visit-cards">
            {group.items.map((visit) => (
              <li key={visit.visitId}>
                <Link to={visitHref(visit.visitId)} className={`visit-card visit-card--${visit.state.toLowerCase()}`} aria-label={T.open(visit.ticketTitle)}>
                  <span className="visit-card__time">{timeOf(visit.start)}</span>
                  <span className="visit-card__body">
                    <span className="visit-card__title">{visit.ticketTitle}</span>
                    <span className="visit-card__meta">{[visit.installationName, visit.customer?.name].filter(Boolean).join(' · ')}</span>
                    {visit.address && <span className="visit-card__meta">{formatAddress(visit.address)}</span>}
                  </span>
                  <span className="visit-card__badges">
                    <VisitBadge state={visit.state} />
                    <PriorityBadge priority={visit.priority} />
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </section>
  )
}

function VisitDetail({ entry, onChanged }: { entry: CalendarVisit; onChanged: () => void }) {
  const toast = useToast()
  const [checked, setChecked] = useState<boolean[]>(() => T.checklistItems.map(() => false))
  const [starting, setStarting] = useState(false)
  const visit = useLoad((signal) => getVisit(entry.visitId, signal), entry.visitId)
  const state = visit.data?.state ?? entry.state
  const needsProtocol = state === 'InProgress' && !visit.data?.report

  async function start() {
    setStarting(true)
    try {
      await startVisit(entry.visitId)
      toast.success(T.started)
      visit.reload()
      onChanged()
    } catch (e) {
      toast.error(userMessage(e))
    } finally {
      setStarting(false)
    }
  }

  return (
    <>
      <PageHeader
        title={entry.ticketTitle}
        status={<VisitBadge state={state} />}
        subtitle={`${dayOf(entry.start)} · ${timeOf(entry.start)} – ${timeOf(entry.end)}`}
      />
      <div className="visit-detail">
        <article className="card visit-detail__card" aria-labelledby="visit-customer-title">
          <h3 id="visit-customer-title" className="visit-detail__title">
            {T.customer}
          </h3>
          {entry.customer && <p className="visit-detail__lead">{entry.customer.name}</p>}
          {entry.address && <p className="visit-detail__muted">{formatAddress(entry.address)}</p>}
          <div className="visit-detail__actions">
            {entry.customer?.phone && (
              <a className="btn" href={phoneHref(entry.customer.phone)}>
                {T.call}
              </a>
            )}
            {entry.address && (
              <a className="btn" href={navigationHref(entry.address)} target="_blank" rel="noreferrer">
                {T.navigate}
              </a>
            )}
          </div>
          {entry.customer?.email && (
            <p className="visit-detail__muted">
              {T.email}: <a href={`mailto:${entry.customer.email}`}>{entry.customer.email}</a>
            </p>
          )}
        </article>

        <article className="card visit-detail__card" aria-labelledby="visit-device-title">
          <h3 id="visit-device-title" className="visit-detail__title">
            {T.device}
          </h3>
          <KeyValueList
            items={[
              ...(entry.installationName ? [{ label: TICKET_TEXTS.detail.installation, value: entry.installationName }] : []),
              { label: T.ticket, value: <Link to={`/tickets/${entry.ticketId}`}>{entry.ticketTitle}</Link> },
              { label: TICKET_TEXTS.detail.priority, value: <PriorityBadge priority={entry.priority} /> },
            ]}
          />
        </article>

        <fieldset className="card visit-detail__card visit-checklist">
          <legend className="visit-detail__title">{T.checklist}</legend>
          <p className="visit-detail__muted">{T.checklistHint}</p>
          {T.checklistItems.map((item, index) => (
            <label key={item} className="visit-checklist__item">
              <input type="checkbox" checked={checked[index]} onChange={(e) => setChecked((c) => c.map((v, i) => (i === index ? e.target.checked : v)))} />
              {item}
            </label>
          ))}
        </fieldset>

        {visit.data && needsProtocol && (
          <ProtocolForm
            visit={visit.data}
            onDone={() => {
              visit.reload()
              onChanged()
            }}
          />
        )}
      </div>

      {!needsProtocol && (
        <div className="visit-actionbar">
          {state === 'Planned' && (
            <button type="button" className="btn btn--primary" disabled={starting} aria-busy={starting} onClick={() => void start()}>
              {T.start}
            </button>
          )}
          {state === 'Done' && <p className="visit-actionbar__note">{T.done}</p>}
          {state === 'Cancelled' && <p className="visit-actionbar__note">{T.cancelled}</p>}
          {state === 'InProgress' && !visit.data && <Skeleton lines={1} label={T.loading} />}
        </div>
      )}
    </>
  )
}

function validateProtocol(values: ProtocolValues): Record<string, string[]> {
  const errors: Record<string, string[]> = {}
  for (const key of ['cause', 'arrivedAt', 'departedAt', 'findings', 'workDone'] as const) {
    if (!values[key].trim()) errors[key] = [T.required]
  }
  for (const key of ['findings', 'workDone'] as const) {
    if (values[key].trim().length > VISIT_TEXT_MAX) errors[key] = [T.tooLong]
  }
  if (values.arrivedAt && values.departedAt && values.departedAt < values.arrivedAt) errors.departedAt = [T.departBeforeArrive]
  return errors
}

function ProtocolForm({ visit, onDone }: { visit: Visit; onDone: () => void }) {
  const toast = useToast()
  const [saved] = useState(() => readProtocolDraft(visit.id))
  const emptyValues = (): ProtocolValues => ({
    cause: '',
    arrivedAt: toLocalInput(new Date(visit.startedAt ?? visit.plannedStart)),
    departedAt: toLocalInput(new Date()),
    findings: '',
    workDone: '',
  })
  const [values, setValues] = useState<ProtocolValues>(() => saved?.values ?? emptyValues())
  const [restored, setRestored] = useState(saved !== null)
  const [waiting, setWaiting] = useState(saved?.waiting ?? false)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    await reportVisit(visit.id, {
      cause: values.cause as VisitCause,
      findings: values.findings.trim(),
      workDone: values.workDone.trim(),
      arrivedAt: new Date(values.arrivedAt).toISOString(),
      departedAt: new Date(values.departedAt).toISOString(),
    })
    clearProtocolDraft(visit.id)
    toast.success(T.submitted)
    onDone()
  })

  function set(key: keyof ProtocolValues, value: string) {
    const next = { ...values, [key]: value }
    setValues(next)
    writeProtocolDraft(visit.id, { values: next, waiting: false })
  }
  const errorsFor = (key: string) => [...(errors[key] ?? []), ...(fieldErrors[key] ?? [])]

  function waitForConnection() {
    writeProtocolDraft(visit.id, { values, waiting: true })
    setWaiting(true)
  }

  /** Sends the protocol, or keeps it for later when the phone has no connection (also when the connection drops mid-request). */
  async function send() {
    if (!navigator.onLine) {
      waitForConnection()
      return
    }
    const sent = await submit()
    if (!sent && !navigator.onLine) waitForConnection()
  }

  const sendWaiting = useEffectEvent(() => {
    setWaiting(false)
    writeProtocolDraft(visit.id, { values, waiting: false })
    void send()
  })

  useEffect(() => {
    if (!waiting) return
    if (navigator.onLine) {
      const timer = setTimeout(sendWaiting)
      return () => clearTimeout(timer)
    }
    window.addEventListener('online', sendWaiting)
    return () => window.removeEventListener('online', sendWaiting)
  }, [waiting])

  function discardDraft() {
    clearProtocolDraft(visit.id)
    setValues(emptyValues())
    setRestored(false)
    setWaiting(false)
    setErrors({})
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const next = validateProtocol(values)
    setErrors(next)
    if (Object.keys(next).length === 0) await send()
  }

  return (
    <form id="visit-protocol" onSubmit={onSubmit} noValidate className="card visit-detail__card" aria-labelledby="visit-protocol-title">
      <h3 id="visit-protocol-title" className="visit-detail__title">
        {T.protocolTitle}
      </h3>
      <p className="visit-detail__muted">{T.protocolHint}</p>
      {restored && (
        <p className="visit-protocol__notice">
          {T.draftRestored}{' '}
          <button type="button" className="btn btn--ghost btn--sm" onClick={discardDraft}>
            {T.draftDiscard}
          </button>
        </p>
      )}
      <FormField label={T.cause} required errors={errorsFor('cause')}>
        {(props) => (
          <select {...props} required value={values.cause} onChange={(e) => set('cause', e.target.value)}>
            <option value="">{T.causePlaceholder}</option>
            {VISIT_CAUSES.map((c) => (
              <option key={c} value={c}>
                {CAUSE_LABELS[c]}
              </option>
            ))}
          </select>
        )}
      </FormField>
      <div className="visit-protocol__times">
        <FormField label={T.arrivedAt} required errors={errorsFor('arrivedAt')}>
          {(props) => <input {...props} required type="datetime-local" value={values.arrivedAt} onChange={(e) => set('arrivedAt', e.target.value)} />}
        </FormField>
        <FormField label={T.departedAt} required errors={errorsFor('departedAt')}>
          {(props) => <input {...props} required type="datetime-local" value={values.departedAt} onChange={(e) => set('departedAt', e.target.value)} />}
        </FormField>
      </div>
      <FormField label={T.findings} required errors={errorsFor('findings')}>
        {(props) => <textarea {...props} required rows={3} value={values.findings} onChange={(e) => set('findings', e.target.value)} />}
      </FormField>
      <FormField label={T.workDone} required errors={errorsFor('workDone')}>
        {(props) => <textarea {...props} required rows={3} value={values.workDone} onChange={(e) => set('workDone', e.target.value)} />}
      </FormField>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      {waiting && (
        <p role="status" className="visit-protocol__notice">
          {T.waitingForConnection}
        </p>
      )}
      <div className="visit-actionbar">
        <button type="submit" className="btn btn--primary" disabled={busy || waiting} aria-busy={busy}>
          {T.submit}
        </button>
      </div>
    </form>
  )
}
