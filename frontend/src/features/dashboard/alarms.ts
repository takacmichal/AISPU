import type { Alarm } from '../monitoring/alarmsApi'
import type { TicketListItem, TicketPriority } from '../tickets/ticketsApi'
import { DASHBOARD_PARTS as T } from './texts'

export type AlarmSeverity = 'critical' | 'warning' | 'info'

/** One row of the card, independent of where it comes from (tickets now, anomalies later). */
export interface AlarmRow {
  id: string
  href: string
  title: string
  meta: string
  severity: AlarmSeverity
  /** ISO timestamp, or a ready-made label such as "10:42" (sample data). */
  at: string
}

const SEVERITY_BY_PRIORITY: Record<TicketPriority, AlarmSeverity> = { High: 'critical', Medium: 'warning', Low: 'info' }
export const CLOSED = new Set(['Resolved', 'Closed'])

/** An ongoing underproduction alarm; it opens the monitoring of its installation. */
export function fromAlarm(alarm: Alarm): AlarmRow {
  return {
    id: alarm.id,
    href: `/monitoring?installation=${encodeURIComponent(alarm.installationId)}`,
    title: T.alarms.underproduction(alarm.installationName),
    meta: T.alarms.autoDetected,
    severity: SEVERITY_BY_PRIORITY[alarm.ticketPriority],
    at: alarm.lastUnderproductionAt,
  }
}

export function fromTicket(ticket: TicketListItem): AlarmRow {
  return {
    id: ticket.id,
    href: `/tickets/${ticket.id}`,
    title: ticket.title,
    meta: ticket.origin === 'AnomalyDetected' ? T.alarms.autoDetected : T.alarms.ticket,
    severity: SEVERITY_BY_PRIORITY[ticket.priority],
    at: ticket.updatedAt,
  }
}

