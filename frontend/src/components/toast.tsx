/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import { MESSAGES } from '../lib/messages'

type ToastKind = 'success' | 'error' | 'info'

interface Toast {
  id: number
  message: string
  kind: ToastKind
}

export interface ToastApi {
  success: (message: string) => void
  error: (message: string) => void
  info: (message: string) => void
}

const ToastContext = createContext<ToastApi | null>(null)

const DISMISS_MS = 5000
/** Errors stay longer so they can be read (WCAG 2.2.1 timing). */
const ERROR_DISMISS_MS = 8000

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const nextId = useRef(1)

  const dismiss = useCallback((id: number) => setToasts((current) => current.filter((t) => t.id !== id)), [])

  const push = useCallback(
    (kind: ToastKind, message: string) => {
      const id = nextId.current++
      setToasts((current) => [...current, { id, kind, message }])
      setTimeout(() => dismiss(id), kind === 'error' ? ERROR_DISMISS_MS : DISMISS_MS)
    },
    [dismiss],
  )

  const api = useMemo<ToastApi>(
    () => ({ success: (m) => push('success', m), error: (m) => push('error', m), info: (m) => push('info', m) }),
    [push],
  )

  return (
    <ToastContext.Provider value={api}>
      {children}
      {/* The live region is always mounted so screen readers announce toasts added later. */}
      <div className="toasts" role="region" aria-label={MESSAGES.notifications} aria-live="polite" aria-relevant="additions">
        {toasts.map((toast) => (
          <div key={toast.id} role="status" className={`toast toast--${toast.kind}`}>
            <span>{toast.message}</span>
            <button type="button" className="toast__close" aria-label={MESSAGES.close} onClick={() => dismiss(toast.id)}>
              ×
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used inside ToastProvider')
  return ctx
}

/** Like useToast, but returns null outside a ToastProvider (for shared hooks that must also work without one). */
export function useOptionalToast(): ToastApi | null {
  return useContext(ToastContext)
}
