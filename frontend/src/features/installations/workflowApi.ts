import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError, toApiError } from '../../api/problem'
import { authFetch } from '../../auth/http'
import { downloadBlob } from '../monitoring/productionApi'

export type Checklist = components['schemas']['ChecklistResponse']
export type ChecklistStep = components['schemas']['ChecklistStepResponse']
export type RevisionReport = components['schemas']['RevisionReportResponse']
export type RecordRevisionRequest = components['schemas']['RecordRevisionReportRequest']
export type CommissionRequest = components['schemas']['CommissionInstallationRequest']
export type Documentation = components['schemas']['InstallationDocumentationResponse']
type Detail = components['schemas']['InstallationDetailResponse']

/** File types and size the backend accepts for a revision report (RevisionReport.AllowedContentTypes, MaxFileBytes). */
export const REVISION_CONTENT_TYPES = ['application/pdf', 'image/png', 'image/jpeg'] as const
export const REVISION_MAX_BYTES = 5 * 1024 * 1024

const path = (id: string) => ({ params: { path: { id } } })

export async function getChecklist(id: string, signal?: AbortSignal): Promise<Checklist> {
  const { data, response, error } = await api.GET('/api/installations/{id}/checklist', { ...path(id), signal })
  if (!data) throw problemToApiError(response.status, error)
  return data
}

export async function setChecklistStep(id: string, stepCode: string, done: boolean): Promise<Checklist> {
  const { data, response, error } = await api.PUT('/api/installations/{id}/checklist/{stepCode}', {
    params: { path: { id, stepCode } },
    body: { done },
  })
  if (!data) throw problemToApiError(response.status, error)
  return data
}

export async function listRevisionReports(id: string, signal?: AbortSignal): Promise<RevisionReport[]> {
  const { data, response, error } = await api.GET('/api/installations/{id}/revision-reports', { ...path(id), signal })
  if (!data) throw problemToApiError(response.status, error)
  return data as RevisionReport[]
}

export async function recordRevisionReport(id: string, body: RecordRevisionRequest): Promise<RevisionReport> {
  const { data, response, error } = await api.POST('/api/installations/{id}/revision-reports', { ...path(id), body })
  if (!data) throw problemToApiError(response.status, error)
  return data
}

export async function commission(id: string, body: CommissionRequest): Promise<Detail> {
  const { data, response, error } = await api.POST('/api/installations/{id}/commission', { ...path(id), body })
  if (!data) throw problemToApiError(response.status, error)
  return data
}

/** Documentation exists from commissioning on; before that the API answers 409. */
export async function getDocumentation(id: string, signal?: AbortSignal): Promise<Documentation> {
  const { data, response, error } = await api.GET('/api/installations/{id}/documentation', { ...path(id), signal })
  if (!data) throw problemToApiError(response.status, error)
  return data
}

export async function getInstallation(id: string): Promise<Detail> {
  const { data, response, error } = await api.GET('/api/installations/{id}', path(id))
  if (!data) throw problemToApiError(response.status, error)
  return data
}

/** Binary downloads (the API records each one) go through authFetch, the typed client parses JSON only. */
async function download(url: string, fileName: string): Promise<void> {
  const response = await authFetch(url)
  if (!response.ok) throw await toApiError(response)
  downloadBlob({ blob: await response.blob(), fileName })
}

export const downloadRevisionReport = (id: string, report: RevisionReport) =>
  download(`/api/installations/${encodeURIComponent(id)}/revision-reports/${encodeURIComponent(report.id)}/file`, report.fileName)

export const downloadHandoverProtocol = (id: string, fileName: string) =>
  download(`/api/installations/${encodeURIComponent(id)}/documentation/handover-protocol`, fileName)
