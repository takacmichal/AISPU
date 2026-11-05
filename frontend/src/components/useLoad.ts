import { useEffect, useEffectEvent, useRef, useState } from 'react'

interface Loaded<T> {
  key: string
  data?: T
  error: boolean
}

/** Automatic refresh of loaded data (ADR #222): runs while the tab is visible, never overlaps requests. */
export interface PollOptions {
  intervalMs: number
  /** False pauses polling, e.g. for a historical period that never changes. */
  enabled?: boolean
}

/** Passed to the loader; `background` is true for polling refreshes, which must keep the last data on failure. */
export interface LoadContext {
  background: boolean
}

export const DASHBOARD_POLL_MS = 5 * 60_000
const MAX_BACKOFF_FACTOR = 8

/** Base interval after a success; after failures it doubles up to 8x, with jitter so tabs do not retry in lockstep. */
export function nextPollDelay(baseMs: number, failures: number, random: () => number = Math.random): number {
  if (failures <= 0) return baseMs
  return Math.min(baseMs * 2 ** failures, baseMs * MAX_BACKOFF_FACTOR) * (0.5 + random() / 2)
}

/**
 * Loads data when `key` changes; `loading` is derived, so every key change shows its own loading state.
 * With `poll` the data is refreshed in the background: the last data stays visible, `refreshError` flags a failed
 * refresh and `updatedAt` is the time of the last successful load.
 */
export function useLoad<T>(load: (signal: AbortSignal, context: LoadContext) => Promise<T>, key: string, poll?: PollOptions) {
  const [version, setVersion] = useState(0)
  const [result, setResult] = useState<Loaded<T> | null>(null)
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null)
  const [refreshError, setRefreshError] = useState(false)
  const fullKey = `${key}#${version}`
  const run = useEffectEvent((signal: AbortSignal, background: boolean) => load(signal, { background }))
  const intervalMs = poll?.intervalMs
  const pollEnabled = intervalMs !== undefined && poll?.enabled !== false
  const settledKey = useRef('')
  const refreshRef = useRef<() => void>(() => {})

  useEffect(() => {
    const controller = new AbortController()
    run(controller.signal, false)
      .then((data) => {
        if (controller.signal.aborted) return
        settledKey.current = fullKey
        setResult({ key: fullKey, data, error: false })
        setUpdatedAt(new Date())
        setRefreshError(false)
      })
      .catch(() => {
        if (controller.signal.aborted) return
        settledKey.current = fullKey
        setResult({ key: fullKey, error: true })
      })
    return () => controller.abort()
  }, [fullKey])

  useEffect(() => {
    if (!pollEnabled || intervalMs === undefined) return
    const visible = () => document.visibilityState !== 'hidden'
    let timer: ReturnType<typeof setTimeout> | undefined
    let inFlight: AbortController | null = null
    let failures = 0
    let stopped = false

    const schedule = () => {
      clearTimeout(timer)
      if (!stopped && visible()) timer = setTimeout(refresh, nextPollDelay(intervalMs, failures))
    }
    function refresh() {
      clearTimeout(timer)
      if (stopped || !visible()) return
      // The first load of this key is still running: wait for it instead of overlapping.
      if (inFlight || settledKey.current !== fullKey) {
        if (!inFlight) schedule()
        return
      }
      const controller = new AbortController()
      inFlight = controller
      run(controller.signal, true)
        .then((data) => {
          if (controller.signal.aborted) return
          failures = 0
          setResult({ key: fullKey, data, error: false })
          setUpdatedAt(new Date())
          setRefreshError(false)
        })
        .catch(() => {
          if (controller.signal.aborted) return
          failures += 1
          setRefreshError(true)
        })
        .finally(() => {
          if (controller.signal.aborted) return
          inFlight = null
          schedule()
        })
    }
    refreshRef.current = refresh
    const wake = () => {
      if (visible()) refresh()
    }

    document.addEventListener('visibilitychange', wake)
    window.addEventListener('focus', wake)
    window.addEventListener('online', wake)
    schedule()
    return () => {
      stopped = true
      clearTimeout(timer)
      inFlight?.abort()
      refreshRef.current = () => {}
      document.removeEventListener('visibilitychange', wake)
      window.removeEventListener('focus', wake)
      window.removeEventListener('online', wake)
    }
  }, [fullKey, pollEnabled, intervalMs])

  const loading = result?.key !== fullKey
  return {
    loading,
    data: loading ? undefined : result?.data,
    error: !loading && (result?.error ?? false),
    refreshError,
    updatedAt,
    /** Background refresh that keeps the current data visible (no-op when polling is off or a request is running). */
    refresh: () => refreshRef.current(),
    reload: () => setVersion((v) => v + 1),
  }
}
