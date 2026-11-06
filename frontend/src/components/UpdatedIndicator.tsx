import { useEffect, useState } from 'react'
import { UPDATED_TEXT as TEXT, formatUpdatedAgo } from './formatUpdatedAgo'

interface Props {
  updatedAt: Date | null
  refreshError: boolean
  onRetry: () => void
}

export function UpdatedIndicator({ updatedAt, refreshError, onRetry }: Props) {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 30_000)
    return () => clearInterval(timer)
  }, [])

  if (!updatedAt) return null
  return (
    <p className="updated-indicator" role="status" aria-live="polite">
      <span>{formatUpdatedAgo(updatedAt, now)}</span>
      {refreshError && (
        <>
          {' '}
          <span className="updated-indicator__error">{TEXT.failed}</span>{' '}
          <button type="button" className="btn btn--sm" onClick={onRetry}>
            {TEXT.retry}
          </button>
        </>
      )}
    </p>
  )
}
