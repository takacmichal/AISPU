import { MESSAGES } from '../lib/messages'

interface SkeletonProps {
  /** Number of placeholder lines. */
  lines?: number
  /** Announced to screen readers; the placeholder bars themselves are hidden. */
  label?: string
}

/** Content placeholder shown while data loads. Announces `label` once via role="status". */
export function Skeleton({ lines = 3, label = MESSAGES.loading }: SkeletonProps) {
  return (
    <div className="skeleton-group" role="status" aria-busy="true">
      <span className="sr-only">{label}</span>
      {Array.from({ length: lines }, (_, index) => (
        <span key={index} className="skeleton" aria-hidden="true" style={{ width: index === lines - 1 ? '60%' : '100%' }} />
      ))}
    </div>
  )
}

/** Placeholder rows for a table body (purely visual; announce loading elsewhere). */
export function SkeletonRows({ columns, rows = 5 }: { columns: number; rows?: number }) {
  return (
    <>
      {Array.from({ length: rows }, (_, row) => (
        <tr key={row} aria-hidden="true">
          {Array.from({ length: columns }, (_, column) => (
            <td key={column}>
              <span className="skeleton" />
            </td>
          ))}
        </tr>
      ))}
    </>
  )
}
