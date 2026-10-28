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
