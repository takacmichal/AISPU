import { authFetch } from '../../auth/http'
import { toApiError } from '../../api/problem'

/**
 * Tickets API (feature #35, branch with Features/Tickets). It is NOT in the generated OpenAPI client yet, so the
 * types below mirror TicketContracts.cs / ServiceTicket.cs and requests go through authFetch.
 * After the backend is merged and `pnpm api:generate` is run, replace this file with the typed client.
 */
export const TICKET_STATES = ['New', 'Triaged', 'RemoteDiagnosis', 'VisitScheduled', 'OnSite', 'Verified', 'Resolved', 'Closed'] as const
export type TicketState = (typeof TICKET_STATES)[number]
export const TICKET_PRIORITIES = ['Low', 'Medium', 'High'] as const
export type TicketPriority = (typeof TICKET_PRIORITIES)[number]
export const TICKET_ORIGINS = ['CustomerReport', 'Manual', 'AnomalyDetected'] as const
export type TicketOrigin = (typeof TICKET_ORIGINS)[number]

export const MAX_ATTACHMENTS = 5
export const MAX_ATTACHMENT_BYTES = 5 * 1024 * 1024
export const ALLOWED_CONTENT_TYPES = ['image/jpeg', 'image/png', 'application/pdf'] as const
export const TITLE_MAX = 200
export const DESCRIPTION_MAX = 4000
export const NOTE_MAX = 2000
export const COMMENT_MAX = 4000

/** States driven by visits (#36, #37); the generic transition endpoint answers 409 for them. */
export const VISIT_WORKFLOW_STATES: readonly TicketState[] = ['VisitScheduled', 'OnSite', 'Verified']

/** Mirror of ServiceTicket.AllowedTransitionMap; the list endpoint does not return allowedTransitions per row. */
export const TRANSITION_MAP: Record<TicketState, readonly TicketState[]> = {
  New: ['Triaged'],
  Triaged: ['RemoteDiagnosis'],
  RemoteDiagnosis: ['Resolved', 'VisitScheduled'],
  VisitScheduled: ['OnSite'],
  OnSite: ['Verified'],
  Verified: ['Resolved'],
  Resolved: ['Closed'],
  Closed: [],
}

export interface TicketAttachmentRequest {
  fileName: string
  contentType: string
  contentBase64: string
}

export interface CreateTicketRequest {
  installationId: string
  title: string
  description: string
  priority?: TicketPriority
  attachments?: TicketAttachmentRequest[]
}

export interface TicketAttachment {
  id: string
  fileName: string
  contentType: string
  sizeBytes: number
  createdAt: string
  downloadUrl: string
}

export interface Ticket {
  id: string
  installationId: string
  customerId: string
  title: string
  description: string
  priority: TicketPriority
  origin: TicketOrigin
  state: TicketState
  allowedTransitions: TicketState[]
  attachments: TicketAttachment[]
  createdBy: string
  createdAt: string
  updatedAt: string
}

export interface TicketListItem {
  id: string
  installationId: string
  customerId: string
  title: string
  priority: TicketPriority
  origin: TicketOrigin
  state: TicketState
  createdAt: string
  updatedAt: string
}

export interface TicketList {
  items: TicketListItem[]
  page: number
  pageSize: number
  totalCount: number
}

export interface TicketStateChange {
  id: string
  fromState: TicketState | null
  toState: TicketState
  changedBy: string
  changedAt: string
  /** Null for customers (internal note). */
  note: string | null
}

export interface TicketComment {
  id: string
  authorId: string
  body: string
  isPublic: boolean
  createdAt: string
}

export interface TicketListQuery {
  state?: TicketState
  priority?: TicketPriority
  origin?: TicketOrigin
  page?: number
  pageSize?: number
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  if (init.body) headers.set('Content-Type', 'application/json')
  const response = await authFetch(path, { ...init, headers })
  if (!response.ok) throw await toApiError(response)
  return (await response.json()) as T
}

const post = <T>(path: string, body: unknown) => request<T>(path, { method: 'POST', body: JSON.stringify(body) })

export function listTickets(query: TicketListQuery, signal?: AbortSignal): Promise<TicketList> {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== '') params.set(key, String(value))
  const search = params.toString()
  return request<TicketList>(`/api/tickets${search ? `?${search}` : ''}`, { signal })
}

export const getTicket = (id: string, signal?: AbortSignal) => request<Ticket>(`/api/tickets/${id}`, { signal })
export const getTicketHistory = (id: string, signal?: AbortSignal) => request<TicketStateChange[]>(`/api/tickets/${id}/history`, { signal })
export const getTicketComments = (id: string, signal?: AbortSignal) => request<TicketComment[]>(`/api/tickets/${id}/comments`, { signal })

export const createTicket = (body: CreateTicketRequest) => post<Ticket>('/api/tickets', body)
export const transitionTicket = (id: string, state: TicketState, note?: string) =>
  post<Ticket>(`/api/tickets/${id}/transition`, { state, note: note || undefined })
export const addTicketComment = (id: string, body: string, isPublic: boolean) =>
  post<TicketComment>(`/api/tickets/${id}/comments`, { body, isPublic })

export function triageTicket(id: string, priority: TicketPriority, note?: string): Promise<Ticket> {
  return request<Ticket>(`/api/tickets/${id}/triage`, { method: 'PUT', body: JSON.stringify({ priority, note: note || undefined }) })
}

/** Downloads an attachment with the Bearer token and saves it through a temporary link. */
export async function downloadAttachment(attachment: TicketAttachment): Promise<void> {
  const response = await authFetch(attachment.downloadUrl)
  if (!response.ok) throw await toApiError(response)
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = attachment.fileName
  link.click()
  URL.revokeObjectURL(url)
}

/** Installation options for the create form (a customer sees only their own installations). */
export interface InstallationOption {
  id: string
  name: string
}
export const listInstallationOptions = (signal?: AbortSignal) => request<InstallationOption[]>('/api/installations', { signal })
