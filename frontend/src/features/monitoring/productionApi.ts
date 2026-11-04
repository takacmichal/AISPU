import { authFetch } from '../../auth/http'
import { toApiError } from '../../api/problem'

/**
 * Production API (feature #28: #100, #101, #102), implemented in the backend branch with
 * Features/Production. It is NOT in the generated OpenAPI client yet, so these types mirror
 * ProductionContracts.cs and requests go through authFetch.
 * After the backend is merged and `pnpm api:generate` is run, replace this file with the typed client.
 */
export type Granularity = 'fiveMinutes' | 'hour' | 'day' | 'month'
export type CsvSeparator = 'semicolon' | 'comma' | 'tab'

export interface ProductionPoint {
  periodStart: string
  energyKwh: number
  peakPowerKw: number
  sampleCount: number
}

export interface ProductionResponse {
  installationId: string
  from: string
  to: string
  granularity: Granularity
  timeZone: string
  totalEnergyKwh: number
  peakPowerKw: number | null
  points: ProductionPoint[]
}

export interface ComparisonPoint {
  periodStart: string
  actualEnergyKwh: number | null
  predictedEnergyKwh: number | null
  deviationKwh: number | null
  ratio: number | null
}

export interface ComparisonResponse {
  installationId: string
  from: string
  to: string
  granularity: Granularity
  timeZone: string
  predictionAvailable: boolean
  modelVersion: string | null
  actualEnergyKwh: number
  predictedEnergyKwh: number | null
  points: ComparisonPoint[]
}

/** `from` is inclusive, `to` exclusive; both are dates (YYYY-MM-DD, local midnight in the API time zone). */
export interface ProductionQuery {
  from: string
  to: string
  granularity: Granularity
}

function queryString(query: ProductionQuery, extra: Record<string, string> = {}): string {
  return new URLSearchParams({ from: query.from, to: query.to, granularity: query.granularity, ...extra }).toString()
}

async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await authFetch(path, { signal })
  if (!response.ok) throw await toApiError(response)
  return (await response.json()) as T
}

export function fetchProduction(installationId: string, query: ProductionQuery, signal?: AbortSignal) {
  return getJson<ProductionResponse>(`/api/installations/${encodeURIComponent(installationId)}/production?${queryString(query)}`, signal)
}

export function fetchComparison(installationId: string, query: ProductionQuery, signal?: AbortSignal) {
  return getJson<ComparisonResponse>(
    `/api/installations/${encodeURIComponent(installationId)}/production/comparison?${queryString(query)}`,
    signal,
  )
}

export interface CsvFile {
  blob: Blob
  fileName: string
}

export async function fetchProductionCsv(
  installationId: string,
  query: ProductionQuery,
  separator: CsvSeparator,
  signal?: AbortSignal,
): Promise<CsvFile> {
  const response = await authFetch(
    `/api/installations/${encodeURIComponent(installationId)}/production/export?${queryString(query, { separator })}`,
    { signal },
  )
  if (!response.ok) throw await toApiError(response)
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)
  const fileName = match ? decodeURIComponent(match[1]) : `vyroba-${query.from}-${query.to}.csv`
  return { blob: await response.blob(), fileName }
}

/** Saves a blob through a temporary anchor. */
export function downloadBlob(file: CsvFile): void {
  const url = URL.createObjectURL(file.blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = file.fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}
