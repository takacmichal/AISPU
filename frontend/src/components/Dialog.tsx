import { useEffect, useId, useRef, type ReactNode } from 'react'

interface DialogProps {
  title: string
  onClose: () => void
  children: ReactNode
}

/** Modal dialog: Escape closes it, focus moves inside on open and returns to the opener on close. */
export function Dialog({ title, onClose, children }: DialogProps) {
  const titleId = useId()
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    ref.current?.querySelector<HTMLElement>('input, select, textarea, button')?.focus()
    return () => opener?.focus()
  }, [])

  return (
    <div className="dialog-backdrop">
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="dialog"
        onKeyDown={(event) => {
          if (event.key === 'Escape') onClose()
        }}
      >
        <h3 id={titleId}>{title}</h3>
        {children}
      </div>
    </div>
  )
}
