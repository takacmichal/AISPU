import './quotes.css'
import { useCallback, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { ApiError, problemToApiError, userMessage } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { Dialog } from '../../components/Dialog'
import { EmptyState } from '../../components/EmptyState'
import { FormField } from '../../components/forms'
import { Skeleton } from '../../components/Skeleton'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { MESSAGES } from '../../lib/messages'
import { formatCurrency, formatDate, formatDateTime, formatNumber } from '../../lib/format'
import QuoteConfigurator from './QuoteConfigurator'
import {
  STATUS_BADGE,
  STATUS_LABELS,
  TEXT,
  TYPE_LABELS,
  UNIT_LABELS,
  WRITE_ROLES,
  conflictMessage,
  quoteNumber,
  type Quote,
  type QuoteStatus,
} from './quoteLabels'
import { useCustomerOptions } from './useCustomerNames'

/** /quotes/:id shows a quote; the reserved id "new" opens the configurator. */
export default function QuoteDetailPage() {
  const { id = '' } = useParams()
  return id === 'new' ? <QuoteConfigurator /> : <QuoteDetail id={id} />
}

type Action = 'accept' | 'reject'

function QuoteDetail({ id }: { id: string }) {
  const { roles } = useAuth()
  const canWrite = roles.some((r) => WRITE_ROLES.includes(r))
  const toast = useToast()
  const confirm = useConfirm()
  const { customers } = useCustomerOptions()
  const [sending, setSending] = useState(false)
  const [busy, setBusy] = useState(false)

  const quote = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/quotes/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)
  const history = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/quotes/{id}/history', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)

  const refresh = useCallback(() => {
    quote.reload()
    history.reload()
  }, [quote, history])

  async function transition(action: Action) {
    const accept = action === 'accept'
    const ok = await confirm({
      title: accept ? 'Prijať ponuku' : 'Zamietnuť ponuku',
      message: accept
        ? 'Zaznamenať, že zákazník ponuku prijal? Túto zmenu už nie je možné vrátiť.'
        : 'Zaznamenať, že zákazník ponuku zamietol? Túto zmenu už nie je možné vrátiť.',
      confirmLabel: accept ? 'Prijať' : 'Zamietnuť',
      danger: !accept,
    })
    if (!ok) return
    setBusy(true)
    try {
      const result = await api.POST(accept ? '/api/quotes/{id}/accept' : '/api/quotes/{id}/reject', {
        params: { path: { id } },
        body: {},
      })
      if (!result.data) throw problemToApiError(result.response.status, result.error)
      toast.success(accept ? TEXT.acceptSuccess : TEXT.rejectSuccess)
    } catch (error) {
      toast.error(transitionMessage(error))
    } finally {
      setBusy(false)
      refresh()
    }
  }

  if (quote.loading) return <Skeleton lines={5} label={TEXT.detailLoading} />
  if (quote.error || !quote.data)
    return (
      <p role="alert" className="alert">
        {TEXT.detailError}
      </p>
    )

  const q = quote.data
  const customerName = customers.find((c) => c.id === q.customerId)?.name
  const can = (target: QuoteStatus) => canWrite && q.allowedTransitions.includes(target)

  return (
    <section>
      <div className="toolbar">
        <h2>{`Ponuka ${quoteNumber(q.id)}`}</h2>
        <div className="btn-row">
          {can('Sent') && (
            <button type="button" className="btn btn--primary" disabled={busy} onClick={() => setSending(true)}>
              {TEXT.send}
            </button>
          )}
          {can('Accepted') && (
            <button type="button" className="btn btn--primary" disabled={busy} onClick={() => transition('accept')}>
              {TEXT.accept}
            </button>
          )}
          {can('Rejected') && (
            <button type="button" className="btn btn--danger" disabled={busy} onClick={() => transition('reject')}>
              {TEXT.reject}
            </button>
          )}
          {canWrite && q.status === 'Accepted' && (
            <Link to={`/contracts?quoteId=${q.id}`} className="btn btn--primary">
              {TEXT.createContract}
            </Link>
          )}
        </div>
      </div>

      <div className="quote-meta">
        <span className={STATUS_BADGE[q.status]}>{STATUS_LABELS[q.status]}</span>
        <span>
          Zákazník:{' '}
          {customerName ? <Link to={`/customers/${q.customerId}`}>{customerName}</Link> : TEXT.unknownCustomer}
        </span>
        <span>{`${TEXT.validUntil}: ${formatDate(q.validUntil)}`}</span>
        <span>{`Vytvorená: ${formatDateTime(q.createdAt)}`}</span>
      </div>
      {q.note && <p className="card card--subtle">{q.note}</p>}

      {q.lines.length === 0 ? (
        <EmptyState title={TEXT.linesEmpty} />
      ) : (
        <div className="data-table__scroll">
          <table>
            <caption className="sr-only">Položky ponuky</caption>
            <thead>
              <tr>
                <th scope="col">Komponent</th>
                <th scope="col">Typ</th>
                <th scope="col" className="quote-num">Množstvo</th>
                <th scope="col" className="quote-num">Jednotková cena</th>
                <th scope="col" className="quote-num">Spolu</th>
              </tr>
            </thead>
            <tbody>
              {q.lines.map((line) => (
                <tr key={line.position}>
                  <td>{`${line.code} – ${line.name}`}</td>
                  <td>{TYPE_LABELS[line.componentType] ?? line.componentType}</td>
                  <td className="quote-num">{`${formatNumber(line.quantity)} ${UNIT_LABELS[line.componentType] ?? ''}`}</td>
                  <td className="quote-num">{formatCurrency(line.unitPrice, q.currency)}</td>
                  <td className="quote-num">{formatCurrency(line.netPrice, q.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <dl className="card quote-totals" role="group" aria-label="Súčet ponuky">
        <div>
          <dt>{TEXT.net}</dt>
          <dd>{formatCurrency(q.netTotal, q.currency)}</dd>
        </div>
        <div>
          <dt>{`${TEXT.vat} (${formatNumber(q.vatRatePercent)} %)`}</dt>
          <dd>{formatCurrency(q.vatTotal, q.currency)}</dd>
        </div>
        <div className="quote-totals__gross">
          <dt>{TEXT.gross}</dt>
          <dd>{formatCurrency(q.grossTotal, q.currency)}</dd>
        </div>
      </dl>

      <h3>{TEXT.history}</h3>
      {history.loading && <Skeleton lines={3} label="Načítavam históriu…" />}
      {history.error && (
        <p role="alert" className="alert">
          {TEXT.historyError}
        </p>
      )}
      {history.data && history.data.length === 0 && <EmptyState title={TEXT.historyEmpty} />}
      {history.data && history.data.length > 0 && (
        <ol className="quote-timeline" aria-label={TEXT.history}>
          {history.data.map((change, index) => (
            <li key={`${change.changedAt}-${index}`} aria-current={index === history.data!.length - 1 ? 'step' : undefined}>
              <span className={STATUS_BADGE[change.toStatus]}>{STATUS_LABELS[change.toStatus]}</span>
              <time dateTime={change.changedAt}>{formatDateTime(change.changedAt)}</time>
              {change.note && <p>{change.note}</p>}
            </li>
          ))}
        </ol>
      )}

      {sending && (
        <SendDialog
          quote={q}
          onClose={() => setSending(false)}
          onDone={() => {
            setSending(false)
            toast.success(TEXT.sendSuccess)
            refresh()
          }}
          onConflict={refresh}
        />
      )}
    </section>
  )
}

function transitionMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 409) return conflictMessage(userMessage(error))
  return userMessage(error)
}

function SendDialog({
  quote,
  onClose,
  onDone,
  onConflict,
}: {
  quote: Quote
  onClose: () => void
  onDone: () => void
  onConflict: () => void
}) {
  const [days, setDays] = useState('30')
  const [note, setNote] = useState('')
  const [clientError, setClientError] = useState<string | null>(null)

  const send = useCallback(async () => {
    try {
      const result = await api.POST('/api/quotes/{id}/send', {
        params: { path: { id: quote.id } },
        body: { validityDays: Number(days), note: note.trim() || null },
      })
      if (!result.data) throw problemToApiError(result.response.status, result.error)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        onConflict()
        throw new ApiError(409, conflictMessage(userMessage(error)))
      }
      throw error
    }
    onDone()
  }, [quote.id, days, note, onDone, onConflict])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(send)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const value = Number(days)
    if (!Number.isInteger(value) || value < 1 || value > 365) {
      setClientError('Platnosť musí byť celé číslo od 1 do 365 dní.')
      return
    }
    setClientError(null)
    await submit()
  }

  const daysErrors = [...(clientError ? [clientError] : []), ...(fieldErrors.validityDays ?? [])]
  return (
    <Dialog title="Odoslať ponuku" onClose={onClose}>
      <form onSubmit={onSubmit} noValidate>
        <p>{`Ponuka ${quoteNumber(quote.id)} bude odoslaná zákazníkovi a už ju nebude možné upraviť.`}</p>
        <FormField label="Platnosť (dni)" errors={daysErrors}>
          {(props) => (
            <input
              {...props}
              type="text"
              inputMode="numeric"
              value={days}
              onChange={(e) => {
                setDays(e.target.value)
                setClientError(null)
                clearFieldError('validityDays')
              }}
            />
          )}
        </FormField>
        <FormField label="Poznámka" errors={fieldErrors.note ?? []}>
          {(props) => <textarea {...props} value={note} onChange={(e) => setNote(e.target.value)} />}
        </FormField>
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <div className="dialog__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            {MESSAGES.cancel}
          </button>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            Odoslať
          </button>
        </div>
      </form>
    </Dialog>
  )
}
