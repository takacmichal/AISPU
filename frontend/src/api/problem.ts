import { MESSAGES } from '../lib/messages'

export interface FieldErrors {
  [field: string]: string[]
}

/** Error raised for a non-2xx API response; carries RFC 7807 ProblemDetails and per-field validation errors. */
export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: FieldErrors

  constructor(status: number, message: string, fieldErrors: FieldErrors = {}) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

/** Server property names are PascalCase or camelCase; forms use camelCase, nested paths keep their dots. */
function normalizeField(name: string): string {
  return name.replace(/(^|\.)([A-Z])/g, (_, dot: string, c: string) => dot + c.toLowerCase())
}

interface ProblemBody {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

/** Builds an ApiError from an already parsed ProblemDetails body (e.g. `error` of the typed client). */
export function problemToApiError(status: number, body: unknown): ApiError {
  const problem = (typeof body === 'object' && body !== null ? body : {}) as ProblemBody
  const fieldErrors: FieldErrors = {}
  for (const [field, messages] of Object.entries(problem.errors ?? {})) {
    fieldErrors[normalizeField(field)] = messages
  }
  return new ApiError(status, problem.detail ?? problem.title ?? `HTTP ${status}`, fieldErrors)
}

export async function toApiError(response: Response): Promise<ApiError> {
  try {
    return problemToApiError(response.status, await response.json())
  } catch {
    return problemToApiError(response.status, null)
  }
}

/**
 * Slovak, user-visible message for any failure. 400 and 409 keep the server `detail` (it explains what is wrong),
 * 401/403/404/5xx use fixed texts so English or technical server messages never reach the user.
 */
export function userMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const { errors } = MESSAGES
    if (error.status === 400) return error.message.startsWith('HTTP ') ? errors.validation : error.message
    if (error.status === 401) return errors.unauthorized
    if (error.status === 403) return errors.forbidden
    if (error.status === 404) return errors.notFound
    if (error.status === 409) return error.message.startsWith('HTTP ') ? errors.conflict : error.message
    if (error.status >= 500) return errors.server
    return errors.generic
  }
  if (error instanceof TypeError) return MESSAGES.errors.network
  return MESSAGES.errors.generic
}

/** Statuses that need a toast on top of inline form feedback (401 is handled globally by the auth layer). */
export function shouldToast(error: unknown): boolean {
  if (!(error instanceof ApiError)) return true
  return error.status !== 401 && Object.keys(error.fieldErrors).length === 0
}
