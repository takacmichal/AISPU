import { authFetch } from '../../auth/http'
import { problemToApiError } from '../../api/problem'

/**
 * Panel layout API from PR #342 (branch sprint1/84-strings-panel-layout). It is NOT in the generated
 * OpenAPI client yet, so these types mirror StringDtos.cs and the request goes through authFetch.
 * After the PR is merged and `pnpm api:generate` is run, replace this file with the typed client.
 */
export interface StringPanel {
  id: string
  serialNumber: string
  manufacturer: string
  model: string
  powerW: number
}

export interface PanelString {
  id: string
  installationId: string
  number: number
  name: string | null
  mpptInput: number | null
  panelCount: number
  totalPowerW: number
  panels: StringPanel[]
}

export interface StringLayout {
  installationId: string
  strings: PanelString[]
  unassignedPanels: StringPanel[]
  assignedPowerW: number
  totalPanelPowerW: number
}

/** Flip to true once the backend of PR #342 is deployed; the "Stringy" tab is hidden while it is false. */
export const STRINGS_API_ENABLED = false

export async function fetchStringLayout(installationId: string, signal?: AbortSignal): Promise<StringLayout> {
  const response = await authFetch(`/api/installations/${installationId}/strings`, { signal })
  if (!response.ok) throw problemToApiError(response.status, await response.json().catch(() => null))
  return (await response.json()) as StringLayout
}
