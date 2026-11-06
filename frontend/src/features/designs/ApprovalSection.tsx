import { useCallback, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError, userMessage } from '../../api/problem'
import { Dialog } from '../../components/Dialog'
import { FormField } from '../../components/forms'
import { useConfirm } from '../../components/confirm'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { formatDateTime } from '../../lib/format'
import { MESSAGES } from '../../lib/messages'
import { NOTES_MAX } from './designForm'
import { TEXT } from './labels'

type Design = components['schemas']['FveDesignResponse']

interface ApprovalSectionProps {
  design: Design
  /** Number of the newest saved version, or null when none exists yet. */
  latestVersion: number | null
  onChanged: () => void
}

/** Approval of the design (#240): InDesign → Approved, a draft quote from the approved version, or rework. */
export function ApprovalSection({ design, latestVersion, onChanged }: ApprovalSectionProps) {
  const toast = useToast()
  const confirm = useConfirm()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [reworkOpen, setReworkOpen] = useState(false)
  const approved = design.status === 'Approved'

  async function approve() {
    const ok = await confirm({
      title: TEXT.approveTitle,
      message: TEXT.approveMessage(latestVersion ?? 0),
      confirmLabel: TEXT.approveConfirm,
    })
    if (!ok) return
    await run(async () => {
      const { data, response, error } = await api.POST('/api/designs/{id}/approve', { params: { path: { id: design.id } } })
      if (!data) throw problemToApiError(response.status, error)
      toast.success(TEXT.approved)
      onChanged()
    })
  }

  async function createQuote() {
    await run(async () => {
      const { data, response, error } = await api.POST('/api/designs/{id}/quote', { params: { path: { id: design.id } } })
      if (!data) throw problemToApiError(response.status, error)
      toast.success(TEXT.quoteCreated)
      navigate(`/quotes/${data.id}`)
    })
  }

  async function run(action: () => Promise<void>) {
    setBusy(true)
    try {
      await action()
    } catch (e) {
      toast.error(userMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="design-section">
      <h3>{TEXT.approvalTitle}</h3>
      {approved && design.approval && (
        <p role="status">{TEXT.approvedInfo(Number(design.approval.versionNumber), formatDateTime(design.approval.approvedAt))}</p>
      )}
      {!approved && latestVersion === null && <p>{TEXT.approveNeedsVersion}</p>}
      <div className="btn-row">
        {!approved && latestVersion !== null && (
          <button type="button" className="btn btn--primary" disabled={busy} onClick={() => void approve()}>
            {TEXT.approve}
          </button>
        )}
        {approved && (
          <button type="button" className="btn btn--primary" disabled={busy} onClick={() => void createQuote()}>
            {TEXT.createQuote}
          </button>
        )}
        {(approved || latestVersion !== null) && (
          <button type="button" className="btn btn--ghost" disabled={busy} onClick={() => setReworkOpen(true)}>
            {TEXT.rework}
          </button>
        )}
      </div>
      {reworkOpen && (
        <ReworkDialog
          designId={design.id}
          onClose={() => setReworkOpen(false)}
          onDone={() => {
            setReworkOpen(false)
            onChanged()
          }}
        />
      )}
    </div>
  )
}

/** Sends the design back to the designer; the server requires a reason (ReworkDesignRequest). */
function ReworkDialog({ designId, onClose, onDone }: { designId: string; onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [reason, setReason] = useState('')
  const [clientError, setClientError] = useState<string | null>(null)

  const save = useCallback(async () => {
    const result = await api.POST('/api/designs/{id}/rework', { params: { path: { id: designId } }, body: { reason: reason.trim() } })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    toast.success(TEXT.reworkDone)
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
    <Dialog title={TEXT.reworkTitle} onClose={onClose}>
      <p>{TEXT.reworkHint}</p>
      <FormField label={TEXT.reworkReason} errors={[...(clientError ? [clientError] : []), ...(fieldErrors.reason ?? [])]}>
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
        <button type="button" className="btn btn--primary" disabled={busy} onClick={onConfirm}>
          {TEXT.reworkConfirm}
        </button>
      </div>
    </Dialog>
  )
}
