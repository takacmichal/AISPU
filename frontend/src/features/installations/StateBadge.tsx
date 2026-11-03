import { STATE_LABELS, STATE_TONES, type InstallationState } from './messages'

export function StateBadge({ state }: { state: InstallationState }) {
  const tone = STATE_TONES[state]
  return <span className={tone ? `badge badge--${tone}` : 'badge'}>{STATE_LABELS[state] ?? state}</span>
}
