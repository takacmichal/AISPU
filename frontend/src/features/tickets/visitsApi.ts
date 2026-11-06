import { authFetch } from '../../auth/http'
import { toApiError } from '../../api/problem'
import type { TicketPriority, TicketState } from './ticketsApi'

/**
 * Visits API (Features/Tickets/VisitsModule, stories #124 to #128). Like `ticketsApi.ts` it is not in the generated
 * OpenAPI client yet, so the types mirror VisitContracts.cs and requests go through authFetch.
 */
export const VISIT_STATES = ['Planned', 'InProgress', 'Done', 'Cancelled'] as const
export type VisitState = (typeof VISIT_STATES)[number]
export const VISIT_CAUSES = ['InverterFault', 'PanelFault', 'WiringFault', 'GridIssue', 'Shading', 'Monitoring', 'NoFaultFound', 'Other'] as const
export type VisitCause = (typeof VISIT_CAUSES)[number]

export const MIN_VISIT_MINUTES = 15
export const MAX_VISIT_MINUTES = 12 * 60
export const VISIT_TEXT_MAX = 4000

/** Ticket states in which a technician can be assigned (the technician cannot change while a visit is active). */
export const ASSIGNABLE_STATES: readonly TicketState[] = ['Triaged', 'RemoteDiagnosis', 'VisitScheduled']

export interface VisitReport {
  cause: VisitCause
  /** Internal; null for customers. */
  findings: string | null
  workDone: string
  arrivedAt: string
  departedAt: string
  reportedBy: string
  reportedAt: string
}

export interface VisitMaterial {
  id: string
  name: string
  quantity: number
  unit: string
}

export interface Visit {
  id: string
  ticketId: string
  /** Null for customers. */
  technicianId: string | null
  state: VisitState
  plannedStart: string
  plannedEnd: string
  estimatedMinutes: number
  startedAt: string | null
  report: VisitReport | null
  reschedules: unknown[]
  materials: VisitMaterial[]
  deviceReplacements: unknown[]
}

export interface TicketVisits {
  ticketId: string
  /** Null for customers and when nobody is assigned. */
  assignedTechnicianId: string | null
  visits: Visit[]
}

export interface Technician {
  id: string
  userName: string | null
  email: string | null
}

export interface Address {
  street: string
  city: string
  postalCode: string
  country: string
}

export interface CalendarVisit {
  visitId: string
  ticketId: string
  ticketTitle: string
  priority: TicketPriority
  ticketState: TicketState
  state: VisitState
  start: string
  end: string
  estimatedMinutes: number
  installationId: string
  installationName: string | null
  address: Address | null
  customer: { id: string; name: string; phone: string | null; email: string } | null
}

export interface TechnicianCalendar {
  technicianId: string
  from: string
  to: string
  visits: CalendarVisit[]
}

export interface VisitReportRequest {
  cause: VisitCause
  findings: string
  workDone: string
  arrivedAt: string
  departedAt: string
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  const response = await authFetch(path, { ...init, headers })
  if (!response.ok) throw await toApiError(response)
  return (await response.json()) as T
}

const send = <T>(method: string, path: string, body: unknown = {}) => request<T>(path, { method, body: JSON.stringify(body) })

export const getTicketVisits = (ticketId: string, signal?: AbortSignal) => request<TicketVisits>(`/api/tickets/${ticketId}/visits`, { signal })
export const getVisit = (id: string, signal?: AbortSignal) => request<Visit>(`/api/visits/${id}`, { signal })

export const assignTechnician = (ticketId: string, technicianId: string) => send<unknown>('PUT', `/api/tickets/${ticketId}/technician`, { technicianId })
export const planVisit = (ticketId: string, start: string, estimatedMinutes: number) =>
  send<Visit>('POST', `/api/tickets/${ticketId}/visits`, { start, estimatedMinutes })
export const verifyTicket = (ticketId: string) => send<unknown>('POST', `/api/tickets/${ticketId}/verify`)

export const startVisit = (id: string) => send<Visit>('POST', `/api/visits/${id}/start`)
export const reportVisit = (id: string, body: VisitReportRequest) => send<Visit>('POST', `/api/visits/${id}/report`, body)
export const rescheduleVisit = (id: string, body: { start: string; estimatedMinutes: number; reason: string }) =>
  send<Visit>('PUT', `/api/visits/${id}/schedule`, body)

/** Technicians that can be assigned (planner roles: Admin, Operator, Sales, Designer). */
export const listTechnicians = (signal?: AbortSignal) => request<Technician[]>('/api/installations/technicians', { signal })

/** Visits of the signed-in technician starting in [from, to). */
export function getMyVisits(from: Date, to: Date, signal?: AbortSignal): Promise<TechnicianCalendar> {
  const params = new URLSearchParams({ from: from.toISOString(), to: to.toISOString() })
  return request<TechnicianCalendar>(`/api/technicians/me/visits?${params.toString()}`, { signal })
}

/** Visits of any technician starting in [from, to) (planner roles: Admin, Operator). */
export function getTechnicianVisits(technicianId: string, from: Date, to: Date, signal?: AbortSignal): Promise<TechnicianCalendar> {
  const params = new URLSearchParams({ from: from.toISOString(), to: to.toISOString() })
  return request<TechnicianCalendar>(`/api/technicians/${technicianId}/visits?${params.toString()}`, { signal })
}

export const technicianName = (t: Pick<Technician, 'userName' | 'email' | 'id'>) => t.userName || t.email || t.id.slice(0, 8)

/** The visit the ticket is about now: the active one, otherwise the latest planned or done. */
export function currentVisit(visits: readonly Visit[]): Visit | undefined {
  const active = visits.find((v) => v.state === 'InProgress') ?? visits.find((v) => v.state === 'Planned')
  if (active) return active
  return [...visits].filter((v) => v.state !== 'Cancelled').sort((a, b) => b.plannedStart.localeCompare(a.plannedStart))[0]
}
