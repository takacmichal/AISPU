import { useState } from 'react'
import { ApiError, userMessage } from '../../api/problem'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { STATE_LABELS, T } from './messages'
import { transitionTicket, type Ticket, type TicketState } from './ticketsApi'

/** Shows a card in its new column right away and returns it if the server refuses the move. */
export interface OptimisticMove {
  apply: () => void
  rollback: () => void
}

/** Confirm, call the transition endpoint and report the result; 409 gets a fixed Slovak message. */
export function useTicketTransition(onDone: (ticket: Ticket) => void) {
  const confirm = useConfirm()
  const toast = useToast()
  const [busy, setBusy] = useState(false)

  const confirmMove = (from: TicketState, to: TicketState) =>
    confirm({
      title: T.transition.title,
      message: T.transition.message(STATE_LABELS[from], STATE_LABELS[to]),
      confirmLabel: STATE_LABELS[to],
    })

  async function send(ticketId: string, to: TicketState, onSuccess?: (ticket: Ticket) => void, onFailure?: () => void) {
    setBusy(true)
    try {
      const ticket = await transitionTicket(ticketId, to)
      toast.success(T.transition.done(STATE_LABELS[to]))
      onSuccess?.(ticket)
    } catch (error) {
      onFailure?.()
      toast.error(error instanceof ApiError && error.status === 409 ? T.transition.conflict : userMessage(error))
    } finally {
      setBusy(false)
    }
  }

  async function move(ticketId: string, from: TicketState, to: TicketState) {
    if (await confirmMove(from, to)) await send(ticketId, to, onDone)
  }

  /** Drag and drop: the drop itself is the decision, only closing a ticket still asks for confirmation. */
  async function moveOptimistic(ticketId: string, from: TicketState, to: TicketState, { apply, rollback }: OptimisticMove) {
    if (to === 'Closed' && !(await confirmMove(from, to))) return
    apply()
    await send(ticketId, to, undefined, rollback)
  }

  return { move, moveOptimistic, busy }
}
