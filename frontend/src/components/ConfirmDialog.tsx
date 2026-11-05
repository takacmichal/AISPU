import type { ReactNode } from 'react'
import { MESSAGES } from '../lib/messages'
import { Dialog } from './Dialog'

export interface ConfirmDialogProps {
  title: string
  message: ReactNode
  confirmLabel?: string
  cancelLabel?: string
  /** Destructive action: confirm button is styled as danger. */
  danger?: boolean
  /** Disables both buttons while the confirmed action runs. */
  busy?: boolean
  /** Error from the confirmed action, shown inside the dialog. */
  error?: string | null
  onConfirm: () => void
  onCancel: () => void
}

/** Confirmation dialog. Cancel is first in DOM order so it receives initial focus (safe default). */
export function ConfirmDialog({
  title,
  message,
  confirmLabel = MESSAGES.confirm,
  cancelLabel = MESSAGES.cancel,
  danger = false,
  busy = false,
  error = null,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  return (
    <Dialog title={title} onClose={onCancel} icon={danger ? '!' : undefined}>
      <p className="dialog__message">{message}</p>
      {error && (
        <p role="alert" className="form-error">
          {error}
        </p>
      )}
      <div className="dialog__actions">
        <button type="button" className="btn btn--ghost" onClick={onCancel}>
          {cancelLabel}
        </button>
        <button type="button" className={`btn ${danger ? 'btn--danger' : 'btn--primary'}`} disabled={busy} onClick={onConfirm}>
          {confirmLabel}
        </button>
      </div>
    </Dialog>
  )
}
