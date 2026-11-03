import { useEffect, useRef, type RefObject } from 'react'
import { ApiError, userMessage } from '../../api/problem'
import { MESSAGES } from '../../lib/messages'

/**
 * Moves focus to the first invalid field after each failed submit.
 * `attempt` changes only on submit, so clearing an error while typing does not move focus.
 */
export function useFocusFirstInvalid(attempt: number): RefObject<HTMLFormElement | null> {
  const formRef = useRef<HTMLFormElement>(null)
  useEffect(() => {
    if (attempt === 0) return
    formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
  }, [attempt])
  return formRef
}

/**
 * Slovak message for failures not mapped to a field. Server `detail` texts are English, so 400/409
 * use fixed texts instead of `userMessage`, which would pass the detail through.
 */
export function formErrorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 400) return MESSAGES.errors.validation
  if (error instanceof ApiError && error.status === 409) return MESSAGES.errors.conflict
  return userMessage(error)
}

/** Joins aria-describedby ids, skipping empty ones. */
export function describedBy(...ids: (string | undefined)[]): string | undefined {
  const joined = ids.filter(Boolean).join(' ')
  return joined || undefined
}

/** Router state passed to /login after a successful registration or password reset. */
export interface LoginNotice {
  notice: string
  email?: string
}
