import { useCallback, useState, type FormEvent } from 'react'
import { api } from '../../api/client'
import { problemToApiError } from '../../api/problem'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { NOTES_MAX } from './designForm'
import { TEXT } from './labels'

/** Site survey result form (Requested to SiteSurveyDone). */
export function SurveyForm({ designId, onSaved }: { designId: string; onSaved: () => void }) {
  const toast = useToast()
  const [notes, setNotes] = useState('')
  const [clientError, setClientError] = useState<string | null>(null)

  const save = useCallback(async () => {
    const result = await api.POST('/api/designs/{id}/site-survey', { params: { path: { id: designId } }, body: { notes: notes.trim() } })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    toast.success(TEXT.surveySaved)
    onSaved()
  }, [designId, notes, toast, onSaved])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const trimmed = notes.trim()
    const error = trimmed === '' ? TEXT.required : trimmed.length > NOTES_MAX ? `Maximálna dĺžka je ${NOTES_MAX} znakov.` : null
    setClientError(error)
    if (!error) await submit()
  }

  const errors = [...(clientError ? [clientError] : []), ...(fieldErrors.notes ?? [])]
  return (
    <form onSubmit={onSubmit} noValidate aria-label={TEXT.surveyTitle}>
      <FormField label={TEXT.surveyNotes} errors={errors}>
        {(props) => (
          <textarea
            {...props}
            value={notes}
            onChange={(e) => {
              setNotes(e.target.value)
              setClientError(null)
              clearFieldError('notes')
            }}
          />
        )}
      </FormField>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <div className="btn-row">
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {TEXT.surveySave}
        </button>
      </div>
    </form>
  )
}
