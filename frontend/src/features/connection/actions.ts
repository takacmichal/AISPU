import { authFetch } from '../../auth/http'
import { toApiError } from '../../api/problem'
import type { ConnectionRequest } from './model'

/**
 * Process transitions (submit, resubmit, review-conditions, send-contract, customer-signed, submit-signed-contract)
 * come from #86 (branch sprint1/86-connection-deadlines) and are not in the generated client yet.
 */
export async function postTransition(requestId: string, action: string): Promise<ConnectionRequest> {
  const response = await authFetch(`/api/connection-requests/${encodeURIComponent(requestId)}/${action}`, { method: 'POST' })
  if (!response.ok) throw await toApiError(response)
  return (await response.json()) as ConnectionRequest
}

/** Downloads an attachment through authFetch (the endpoint needs the Bearer token) and saves it as a file. */
export async function downloadAttachment(downloadUrl: string, fileName: string): Promise<void> {
  const response = await authFetch(downloadUrl)
  if (!response.ok) throw await toApiError(response)
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
