import type { components } from '../../api/generated/schema'

type Generated = components['schemas']['ConnectionRequestResponse']

/**
 * Fields added by the deadline story (#86, branch sprint1/86-connection-deadlines). They are not in the generated
 * schema yet, so they are optional here; drop this extension after `pnpm api:generate` includes them.
 */
interface DeadlineFields {
  submittedAt?: string | null
  proposalDueDate?: string | null
  proposalRemainingDays?: number | null
  proposalOverdue?: boolean
  decidedAt?: string | null
  decisionNote?: string | null
  conditionsReviewedAt?: string | null
  contractSentAt?: string | null
}

export type ConnectionRequest = Generated & DeadlineFields
export type ConnectionState = Generated['state']

/** Happy path of the process; InfoRequested and Rejected are side branches of the second step. */
export const STEP_STATES: readonly ConnectionState[] = [
  'Draft',
  'Submitted',
  'ContractOffered',
  'ContractSigned',
  'ContractSubmitted',
  'Concluded',
]

export type StepStatus = 'done' | 'current' | 'upcoming' | 'blocked'

/** Days before the proposal deadline at which the warning is raised. */
export const DEADLINE_WARNING_DAYS = 3

export function stepIndex(state: ConnectionState): number {
  if (state === 'InfoRequested' || state === 'Rejected') return 1
  return STEP_STATES.indexOf(state)
}

export function stepStatuses(state: ConnectionState): StepStatus[] {
  const index = stepIndex(state)
  return STEP_STATES.map((_, i) => {
    if (state === 'Concluded') return 'done'
    if (i < index) return 'done'
    if (i === index) return state === 'Rejected' ? 'blocked' : 'current'
    return 'upcoming'
  })
}

export interface DeadlineInfo {
  level: 'ok' | 'warning' | 'overdue'
  /** Days left; for 'overdue' the number of days past the deadline (positive). */
  days: number
  dueDate: string | null
}

function daysUntil(isoDate: string, today: Date): number {
  const [y, m, d] = isoDate.split('-').map(Number)
  const due = Date.UTC(y, m - 1, d)
  const now = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())
  return Math.round((due - now) / 86_400_000)
}

/** Deadline status; only meaningful while the request waits for the distributor (state Submitted). */
export function deadlineInfo(request: ConnectionRequest, today: Date = new Date()): DeadlineInfo | null {
  if (request.state !== 'Submitted') return null
  const remaining =
    request.proposalRemainingDays ?? (request.proposalDueDate ? daysUntil(request.proposalDueDate, today) : null)
  if (remaining === null && !request.proposalOverdue) return null
  const days = remaining ?? 0
  const dueDate = request.proposalDueDate ?? null
  if (request.proposalOverdue || days < 0) return { level: 'overdue', days: Math.abs(days), dueDate }
  return { level: days <= DEADLINE_WARNING_DAYS ? 'warning' : 'ok', days, dueDate }
}

export interface TimelineEvent {
  key: string
  at: string
  label: string
  note?: string | null
}

/** The API has no status-history endpoint; the timeline is built from the timestamps on the request. */
export function timelineEvents(
  request: ConnectionRequest,
  labels: { created: string; submitted: string; decided: string; conditionsReviewed: string; contractSent: string },
): TimelineEvent[] {
  const events: TimelineEvent[] = [{ key: 'created', at: request.createdAt, label: labels.created }]
  if (request.submittedAt) events.push({ key: 'submitted', at: request.submittedAt, label: labels.submitted })
  if (request.decidedAt) events.push({ key: 'decided', at: request.decidedAt, label: labels.decided, note: request.decisionNote })
  if (request.conditionsReviewedAt)
    events.push({ key: 'reviewed', at: request.conditionsReviewedAt, label: labels.conditionsReviewed })
  if (request.contractSentAt) events.push({ key: 'sent', at: request.contractSentAt, label: labels.contractSent })
  return events.sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
}

export interface NextAction {
  action: string
  roles: readonly string[]
}

const DESIGN = ['Admin', 'Designer']
const BACK_OFFICE = ['Admin', 'Operator', 'Sales', 'Designer']

/** The single next step of the process for the state (mirrors the backend role lists; the backend decides). */
export function nextAction(request: ConnectionRequest): NextAction | null {
  switch (request.state) {
    case 'Draft':
      return { action: 'submit', roles: DESIGN }
    case 'InfoRequested':
      return { action: 'resubmit', roles: BACK_OFFICE }
    case 'ContractOffered':
      if (!request.conditionsReviewedAt) return { action: 'review-conditions', roles: DESIGN }
      if (!request.contractSentAt) return { action: 'send-contract', roles: BACK_OFFICE }
      return { action: 'customer-signed', roles: BACK_OFFICE }
    case 'ContractSigned':
      return { action: 'submit-signed-contract', roles: DESIGN }
    default:
      return null
  }
}

/** Still in progress: neither concluded nor rejected. */
export const isOpenRequest = (request: Pick<ConnectionRequest, 'state'>) => request.state !== 'Concluded' && request.state !== 'Rejected'

/**
 * How much of the distributor's proposal period has passed, in days (for the deadline progress bar).
 * Null when the request is not waiting for the distributor or the API did not send the dates.
 * When overdue, `elapsed` is larger than `total`.
 */
export function deadlineProgress(request: ConnectionRequest, today: Date = new Date()): { elapsed: number; total: number } | null {
  const info = deadlineInfo(request, today)
  if (!info || !request.submittedAt || !request.proposalDueDate) return null
  const total = Math.max(1, daysUntil(request.proposalDueDate, new Date(request.submittedAt)))
  const elapsed = info.level === 'overdue' ? total + info.days : total - info.days
  return { elapsed: Math.max(0, elapsed), total }
}
