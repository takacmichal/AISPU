import { useEffect, useEffectEvent, useState } from 'react'

interface Loaded<T> {
  key: string
  data?: T
  error: boolean
}

/** Loads data when `key` changes; `loading` is derived, so every key change shows its own loading state. */
export function useLoad<T>(load: (signal: AbortSignal) => Promise<T>, key: string) {
  const [version, setVersion] = useState(0)
  const [result, setResult] = useState<Loaded<T> | null>(null)
  const fullKey = `${key}#${version}`
  const run = useEffectEvent((signal: AbortSignal) => load(signal))

  useEffect(() => {
    const controller = new AbortController()
    run(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) setResult({ key: fullKey, data, error: false })
      })
      .catch(() => {
        if (!controller.signal.aborted) setResult({ key: fullKey, error: true })
      })
    return () => controller.abort()
  }, [fullKey])

  const loading = result?.key !== fullKey
  return {
    loading,
    data: loading ? undefined : result?.data,
    error: !loading && (result?.error ?? false),
    reload: () => setVersion((v) => v + 1),
  }
}
