import { PRIORITY_LABELS, PRIORITY_TONES, STATE_LABELS, STATE_TONES } from './messages'
import type { TicketPriority, TicketState } from './ticketsApi'

const cls = (tone: string) => (tone ? `badge badge--${tone}` : 'badge')

export function StateBadge({ state }: { state: TicketState }) {
  return <span className={cls(STATE_TONES[state])}>{STATE_LABELS[state] ?? state}</span>
}

export function PriorityBadge({ priority }: { priority: TicketPriority }) {
  return <span className={cls(PRIORITY_TONES[priority])}>{PRIORITY_LABELS[priority] ?? priority}</span>
}
