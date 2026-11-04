/**
 * Anomalies and alerts (#251, #252): there is no backend yet. The types are a typed placeholder;
 * flip the flag and implement `fetchAnomalies` once the API exists (then replace with the generated client).
 */
export const ANOMALIES_API_ENABLED = false

export type AnomalySeverity = 'info' | 'warning' | 'critical'

export interface Anomaly {
  id: string
  installationId: string
  detectedAt: string
  severity: AnomalySeverity
  title: string
  cause: string | null
  ticketId: string | null
}

export async function fetchAnomalies(_installationId: string, _signal?: AbortSignal): Promise<Anomaly[]> {
  void _installationId
  void _signal
  throw new Error('Anomalies API is not available yet')
}
