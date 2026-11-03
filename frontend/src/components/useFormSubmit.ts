import { useCallback, useState } from 'react'
import { ApiError, shouldToast, userMessage, type FieldErrors } from '../api/problem'
import { MESSAGES } from '../lib/messages'
import { useOptionalToast } from './toast'

export interface SubmitState {
  busy: boolean
  /** Per-field messages from a 400 ValidationProblem response. */
  fieldErrors: FieldErrors
  /** Slovak message for errors not tied to a field (403, 404, 409, 5xx, network). */
  formError: string | null
  submit: () => Promise<boolean>
  clearFieldError: (field: string) => void
}

/**
 * Runs a submit action and maps server-side validation errors onto form fields (400).
 * Other failures set `formError` and, when a ToastProvider is present, also raise an error toast.
 * 401 is handled globally: authFetch refreshes the token or signs the user out and RequireAuth redirects to login.
 */
export function useFormSubmit(action: () => Promise<unknown>): SubmitState {
  const toast = useOptionalToast()
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
        toast?.error(MESSAGES.errors.validation)
      } else {
        const message = userMessage(error)
        setFormError(message)
        if (shouldToast(error)) toast?.error(message)
      }
      return false
    } finally {
      setBusy(false)
    }
  }, [action, toast])

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
