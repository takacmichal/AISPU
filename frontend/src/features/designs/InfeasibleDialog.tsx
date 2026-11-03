import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import { problemToApiError } from '../../api/problem'
import { Dialog } from '../../components/Dialog'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { MESSAGES } from '../../lib/messages'
import { NOTES_MAX } from './designForm'
import { TEXT } from './labels'

/** Confirmation with a mandatory reason; the server requires it (MarkInfeasibleRequest). */
export function InfeasibleDialog({ designId, onClose, onDone }: { designId: string; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [reason, setReason] = useState('')
  const [clientError, setClientError] = useState<string | null>(null)

  const save = useCallback(async () => {
    const result = await api.POST('/api/designs/{id}/mark-infeasible', { params: { path: { id: designId } }, body: { reason: reason.trim() } })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    toast.success(TEXT.infeasibleDone)
    onDone()
  }, [designId, reason, toast, onDone])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  async function onConfirm() {
    const trimmed = reason.trim()
    const error = trimmed === '' ? TEXT.required : trimmed.length > NOTES_MAX ? `Maximálna dĺžka je ${NOTES_MAX} znakov.` : null
    setClientError(error)
    if (!error) await submit()
  }

  return (
    <Dialog title={TEXT.infeasibleTitle} onClose={onClose}>
      <p>Návrh sa uzavrie a nebude ho možné ďalej upravovať.</p>
      <FormField label={TEXT.infeasibleReason} errors={[...(clientError ? [clientError] : []), ...(fieldErrors.reason ?? [])]}>
        {(props) => (
          <textarea
            {...props}
            value={reason}
            onChange={(e) => {
              setReason(e.target.value)
              setClientError(null)
              clearFieldError('reason')
            }}
          />
        )}
      </FormField>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <div className="dialog__actions">
        <button type="button" className="btn btn--ghost" onClick={onClose}>
          {MESSAGES.cancel}
        </button>
        <button type="button" className="btn btn--danger" disabled={busy} onClick={onConfirm}>
          {TEXT.infeasibleConfirm}
        </button>
      </div>
    </Dialog>
  )
}
