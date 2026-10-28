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

export async function toApiError(response: Response): Promise<ApiError> {
  let title = `HTTP ${response.status}`
  const fieldErrors: FieldErrors = {}
  try {
    const body = (await response.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> }
    title = body.detail ?? body.title ?? title
    for (const [field, messages] of Object.entries(body.errors ?? {})) {
      fieldErrors[normalizeField(field)] = messages
    }
  } catch {
    // body is not JSON; keep the status based message
  }
  return new ApiError(response.status, title, fieldErrors)
}
