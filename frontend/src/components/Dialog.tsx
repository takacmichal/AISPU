import { useEffect, useId, useRef, type KeyboardEvent, type ReactNode } from 'react'

interface DialogProps {
  title: string
  /** Short decorative mark above the title, e.g. "!" for destructive confirmations (K-06). */
  icon?: string
  onClose: () => void
  /** Clicking the backdrop calls `onClose`. Off by default so destructive confirmations need an explicit choice. */
  dismissOnBackdrop?: boolean
  children: ReactNode
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

/** Modal dialog: Escape closes it, Tab is trapped inside, focus moves in on open and returns to the opener on close. */
export function Dialog({ title, icon, onClose, dismissOnBackdrop = false, children }: DialogProps) {
  const titleId = useId()
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    ref.current?.querySelector<HTMLElement>('input, select, textarea, button')?.focus()
    return () => opener?.focus()
  }, [])

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape') {
      event.stopPropagation()
      onClose()
      return
    }
    if (event.key !== 'Tab' || !ref.current) return
    const items = Array.from(ref.current.querySelectorAll<HTMLElement>(FOCUSABLE))
    if (items.length === 0) {
      event.preventDefault()
      return
    }
    const first = items[0]
    const last = items[items.length - 1]
    const active = document.activeElement
    if (event.shiftKey && (active === first || !ref.current.contains(active))) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && (active === last || !ref.current.contains(active))) {
      event.preventDefault()
      first.focus()
    }
  }

  return (
    <div
      className="dialog-backdrop"
      onMouseDown={(event) => {
        if (dismissOnBackdrop && event.target === event.currentTarget) onClose()
      }}
    >
      <div ref={ref} role="dialog" aria-modal="true" aria-labelledby={titleId} className="dialog" onKeyDown={onKeyDown}>
        {icon && (
          <span className="dialog__icon" aria-hidden="true">
            {icon}
          </span>
        )}
        <h3 id={titleId}>{title}</h3>
        {children}
      </div>
    </div>
  )
}
