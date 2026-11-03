import { MESSAGES } from '../lib/messages'

/** Inline spinner with an accessible, announced label. */
export function Spinner({ label = MESSAGES.loading }: { label?: string }) {
  return (
    <span className="spinner-wrap" role="status">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </span>
  )
}
