import { useId, useState } from 'react'
import { ApiError, userMessage } from '../../api/problem'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useLoad } from '../../components/useLoad'
import { formatDateTime } from '../../lib/format'
import { T } from './messages'
import { REOPEN_WINDOW_DAYS, confirmTicket, getTicketHistory, reopenTicket } from './ticketsApi'

const DAY_MS = 86_400_000

/**
 * The customer's answer to a closed ticket (#260): confirm that the fault is fixed, or reopen the ticket within
 * REOPEN_WINDOW_DAYS of closing. The closing time comes from the state history; the API does not return whether the
 * ticket was confirmed before, so a repeated confirmation (409) is shown as "already confirmed".
 */
export function ConfirmationCard({ ticketId, onReopened }: { ticketId: string; onReopened: () => void }) {
  const toast = useToast()
  const titleId = useId()
  const [comment, setComment] = useState('')
  const [clientError, setClientError] = useState<string | null>(null)
  const [result, setResult] = useState<'confirmed' | 'already' | null>(null)
  const [busy, setBusy] = useState(false)

  // The window is evaluated when the history loads (not during render, which must stay pure).
  const reopenWindow = useLoad(async (signal) => {
    const history = await getTicketHistory(ticketId, signal)
    const closed = history.filter((c) => c.toState === 'Closed').map((c) => Date.parse(c.changedAt))
    if (closed.length === 0) return { reopenUntil: null, canReopen: false }
    const reopenUntil = Math.max(...closed) + REOPEN_WINDOW_DAYS * DAY_MS
    return { reopenUntil, canReopen: Date.now() <= reopenUntil }
  }, `ticket-closed|${ticketId}`)
  const reopenUntil = reopenWindow.data?.reopenUntil ?? null
  const canReopen = reopenWindow.data?.canReopen ?? false

  async function confirm() {
    setClientError(null)
    setBusy(true)
    try {
      await confirmTicket(ticketId, comment.trim())
      setResult('confirmed')
    } catch (e) {
      // The ticket is closed, so a conflict means it was confirmed before.
      if (e instanceof ApiError && e.status === 409) setResult('already')
      else toast.error(userMessage(e))
    } finally {
      setBusy(false)
    }
  }

  async function reopen() {
    if (!comment.trim()) {
      setClientError(T.detail.reopenReasonRequired)
      return
    }
    setClientError(null)
    setBusy(true)
    try {
      await reopenTicket(ticketId, comment.trim())
      toast.success(T.detail.reopenDone)
      onReopened()
    } catch (e) {
      toast.error(e instanceof ApiError && e.status === 409 ? T.detail.reopenExpired : userMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card ticket-card" aria-labelledby={titleId}>
      <h3 id={titleId} className="ticket-card__title">
        {T.detail.confirmTitle}
      </h3>
      {result ? (
        <p role="status">{result === 'confirmed' ? T.detail.confirmDone : T.detail.confirmAlready}</p>
      ) : (
        <>
          <p className="ticket-card__muted">{T.detail.confirmHint}</p>
          <FormField label={T.detail.confirmComment} errors={clientError ? [clientError] : []}>
            {(props) => (
              <textarea
                {...props}
                rows={3}
                value={comment}
                onChange={(e) => {
                  setComment(e.target.value)
                  setClientError(null)
                }}
              />
            )}
          </FormField>
          <div className="btn-row">
            <button type="button" className="btn btn--primary" disabled={busy} onClick={() => void confirm()}>
              {T.detail.confirmSubmit}
            </button>
            {canReopen && (
              <button type="button" className="btn btn--ghost" disabled={busy} onClick={() => void reopen()}>
                {T.detail.reopenSubmit}
              </button>
            )}
          </div>
          {canReopen && reopenUntil !== null && <p className="ticket-card__muted">{T.detail.reopenUntil(formatDateTime(reopenUntil))}</p>}
          {!reopenWindow.loading && !canReopen && <p className="ticket-card__muted">{T.detail.reopenWindowOver}</p>}
        </>
      )}
    </section>
  )
}
