import { authFetch } from '../../auth/http'
import { toApiError } from '../../api/problem'

/**
 * The PDF endpoint (POST /api/contracts/{id}/pdf, PR #341 branch sprint1/80-contract-pdf) is not in the generated
 * OpenAPI client yet, so it is called through authFetch. Remove this file once `pnpm api:generate` contains it.
 */
export class PdfUnavailableError extends Error {
  constructor() {
    super('Contract PDF endpoint is not available')
    this.name = 'PdfUnavailableError'
  }
}

/**
 * A missing route answers 404/405/501 without a ProblemDetails body, while "contract not found" is problem+json.
 * Only the first case means the server does not support PDFs yet.
 */
export async function fetchContractPdf(contractId: string, signal?: AbortSignal): Promise<Blob> {
  const response = await authFetch(`/api/contracts/${encodeURIComponent(contractId)}/pdf`, { method: 'POST', signal })
  if (response.ok) return response.blob()
  const isProblem = (response.headers.get('Content-Type') ?? '').includes('json')
  if ([404, 405, 501].includes(response.status) && !isProblem) throw new PdfUnavailableError()
  throw await toApiError(response)
}
