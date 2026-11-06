import { authFetch } from '../../auth/http'
import { toApiError } from '../../api/problem'

/**
 * Generates the contract PDF (POST /api/contracts/{id}/pdf). The typed client parses JSON only, so the binary
 * response goes through authFetch; any failure is a regular ApiError with the server's reason.
 */
export async function fetchContractPdf(contractId: string, signal?: AbortSignal): Promise<Blob> {
  const response = await authFetch(`/api/contracts/${encodeURIComponent(contractId)}/pdf`, { method: 'POST', signal })
  if (!response.ok) throw await toApiError(response)
  return response.blob()
}
