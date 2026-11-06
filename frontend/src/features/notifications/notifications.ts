import { useEffect, useEffectEvent, useState } from 'react'
import { api } from '../../api/client'
import type { paths } from '../../api/generated/schema'

/** Interval of the background refresh; the badge needs fresh data even while the panel is closed. */
export const NOTIFICATIONS_POLL_MS = 60_000

/** localStorage key with the ids of notifications the user has already seen (JSON array of strings). */
export const SEEN_STORAGE_KEY = 'solarops.notifications.seen'
const SEEN_LIMIT = 200

type NotificationsResponse = NonNullable<paths['/api/notifications']['get']['responses']['200']['content']['application/json']>

export type NotificationItem = NotificationsResponse['items'][number]

/** Reads seen ids; a missing, corrupt or non-array value counts as "nothing seen yet". */
export function readSeenIds(): string[] {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(SEEN_STORAGE_KEY) ?? '[]')
    return Array.isArray(parsed) ? parsed.filter((id): id is string => typeof id === 'string') : []
  } catch {
    return []
  }
}

/** Writes seen ids, keeping only the most recent ones so the storage does not grow forever. */
export function writeSeenIds(ids: string[]): void {
  try {
    localStorage.setItem(SEEN_STORAGE_KEY, JSON.stringify(ids.slice(-SEEN_LIMIT)))
  } catch {
    // Storage disabled or full: the badge will just show the items again on the next visit.
  }
}

interface NotificationsState {
  items?: NotificationItem[]
  failed: boolean
}

/**
 * Loads GET /api/notifications on mount and every `pollMs`. A failed refresh keeps the last loaded items,
 * so the badge and an open panel do not flash back to the loading state.
 */
export function useNotifications(pollMs = NOTIFICATIONS_POLL_MS) {
  const [state, setState] = useState<NotificationsState>({ failed: false })
  const [attempt, setAttempt] = useState(0)

  const fetchOnce = useEffectEvent(async (signal: AbortSignal) => {
    try {
      const { data } = await api.GET('/api/notifications', { signal })
      if (signal.aborted) return
      if (data && Array.isArray(data.items)) setState({ items: data.items, failed: false })
      else setState((previous) => ({ ...previous, failed: true }))
    } catch {
      if (!signal.aborted) setState((previous) => ({ ...previous, failed: true }))
    }
  })

  useEffect(() => {
    const controller = new AbortController()
    const run = () => void fetchOnce(controller.signal)
    run()
    const timer = window.setInterval(run, pollMs)
    return () => {
      controller.abort()
      window.clearInterval(timer)
    }
  }, [pollMs, attempt])

  return {
    items: state.items,
    failed: state.failed,
    reload: () => setAttempt((value) => value + 1),
  }
}
