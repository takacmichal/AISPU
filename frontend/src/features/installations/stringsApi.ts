import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError } from '../../api/problem'

export type StringLayout = components['schemas']['StringLayoutResponse']
export type StringPanel = components['schemas']['StringPanelResponse']

/** Panel layout of an installation: strings with their panels and the panels not assigned yet (#84). */
export async function fetchStringLayout(installationId: string, signal?: AbortSignal): Promise<StringLayout> {
  const { data, response, error } = await api.GET('/api/installations/{installationId}/strings', {
    params: { path: { installationId } },
    signal,
  })
  if (!data) throw problemToApiError(response.status, error)
  return data
}
