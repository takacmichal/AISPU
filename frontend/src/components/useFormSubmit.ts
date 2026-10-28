import { useCallback, useState } from 'react'
import { ApiError, type FieldErrors } from '../api/problem'

export interface SubmitState {
  busy: boolean
  /** Per-field messages from a 400 ValidationProblem response. */
  fieldErrors: FieldErrors
  /** Message for errors not tied to a field (409, 5xx, network). */
  formError: string | null
  submit: () => Promise<boolean>
  clearFieldError: (field: string) => void
}

/** Runs a submit action and maps server-side validation errors onto form fields. */
export function useFormSubmit(action: () => Promise<unknown>): SubmitState {
  const [busy, setBusy] = useState(false)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)

  const submit = useCallback(async () => {
    setBusy(true)
    setFieldErrors({})
    setFormError(null)
    try {
      await action()
      return true
    } catch (error) {
      if (error instanceof ApiError && Object.keys(error.fieldErrors).length > 0) {
        setFieldErrors(error.fieldErrors)
      } else {
        setFormError(error instanceof Error ? error.message : 'Operácia zlyhala.')
      }
      return false
    } finally {
      setBusy(false)
    }
  }, [action])

  const clearFieldError = useCallback((field: string) => {
    setFieldErrors((current) => {
      if (!(field in current)) return current
      const rest = { ...current }
      delete rest[field]
      return rest
    })
  }, [])

  return { busy, fieldErrors, formError, submit, clearFieldError }
}
