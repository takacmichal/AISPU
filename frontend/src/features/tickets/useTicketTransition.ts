import { useState } from 'react'
import { ApiError, userMessage } from '../../api/problem'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { STATE_LABELS, T } from './messages'
import { transitionTicket, type Ticket, type TicketState } from './ticketsApi'

/** Confirm, call the transition endpoint and report the result; 409 gets a fixed Slovak message. */
export function useTicketTransition(onDone: (ticket: Ticket) => void) {
  const confirm = useConfirm()
  const toast = useToast()
  const [busy, setBusy] = useState(false)

  async function move(ticketId: string, from: TicketState, to: TicketState) {
    const ok = await confirm({
      title: T.transition.title,
      message: T.transition.message(STATE_LABELS[from], STATE_LABELS[to]),
      confirmLabel: STATE_LABELS[to],
    })
    if (!ok) return
    setBusy(true)
    try {
      const ticket = await transitionTicket(ticketId, to)
      toast.success(T.transition.done(STATE_LABELS[to]))
      onDone(ticket)
    } catch (error) {
      toast.error(error instanceof ApiError && error.status === 409 ? T.transition.conflict : userMessage(error))
    } finally {
      setBusy(false)
    }
  }

  return { move, busy }
}
