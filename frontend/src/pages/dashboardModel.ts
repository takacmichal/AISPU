import { isOpenTicket, type TicketListItem } from '../features/tickets/ticketsApi'

/** Open tickets created by anomaly detection (ADR #194), newest first. */
export function activeAlarms(tickets: readonly TicketListItem[]): TicketListItem[] {
  return tickets
    .filter((t) => t.origin === 'AnomalyDetected' && isOpenTicket(t))
    .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
}

/** Roles that see only the customer portal (no internal KPIs). */
export const isCustomerOnly = (roles: readonly string[]) => roles.length > 0 && roles.every((r) => r === 'Customer')
