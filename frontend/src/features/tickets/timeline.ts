import type { TicketComment, TicketStateChange } from './ticketsApi'

export type TimelineEntry =
  | { kind: 'state'; at: string; change: TicketStateChange }
  | { kind: 'comment'; at: string; comment: TicketComment }

/** Merges history and comments into one chronological list (oldest first, history first on equal times). */
export function buildTimeline(history: readonly TicketStateChange[], comments: readonly TicketComment[]): TimelineEntry[] {
  const entries: TimelineEntry[] = [
    ...history.map((change) => ({ kind: 'state' as const, at: change.changedAt, change })),
    ...comments.map((comment) => ({ kind: 'comment' as const, at: comment.createdAt, comment })),
  ]
  return entries.sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
}
