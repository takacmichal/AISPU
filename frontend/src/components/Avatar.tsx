import './ui.css'
import { initials } from './uiHelpers'

/** Round avatar with initials. Decorative: the name is always shown as text next to it. */
export function Avatar({ name, size = 'md' }: { name: string; size?: 'sm' | 'md' }) {
  return (
    <span className={`avatar avatar--${size}`} aria-hidden="true">
      {initials(name)}
    </span>
  )
}
