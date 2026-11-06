import { api } from '../../api/client'
import { problemToApiError } from '../../api/problem'
import type { components } from '../../api/generated/schema'

export type Alarm = components['schemas']['UnderproductionAlarmResponse']
export type AnalysisResult = components['schemas']['MlAnalysisResponse']

/** Alarms stored by the last analyses of one installation; the ML service is not called. */
export async function fetchInstallationAlarms(installationId: string, signal?: AbortSignal): Promise<Alarm[]> {
  const { data, error, response } = await api.GET('/api/installations/{installationId}/underproduction-alarms', {
    params: { path: { installationId } },
    signal,
  })
  if (!data) throw problemToApiError(response.status, error)
  return [...data.items]
}

/** Ongoing alarms of all visible installations; one light request, so the dashboard can poll it every minute (ADR #222). */
export async function fetchOngoingAlarms(signal?: AbortSignal): Promise<Alarm[]> {
  const { data, error, response } = await api.GET('/api/underproduction-alarms', {
    params: { query: { ongoing: true } },
    signal,
  })
  if (!data) throw problemToApiError(response.status, error)
  return [...data.items]
}

/** Runs the ML prediction and detection of an installation; 503 when the service is unavailable. */
export async function runAnalysis(installationId: string): Promise<AnalysisResult> {
  const { data, error, response } = await api.POST('/api/installations/{installationId}/analysis', {
    params: { path: { installationId } },
  })
  if (!data) throw problemToApiError(response.status, error)
  return data
}
