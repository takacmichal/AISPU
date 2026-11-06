import { useState, type FormEvent } from 'react'
import type { components } from '../../../api/generated/schema'
import { userMessage, type FieldErrors } from '../../../api/problem'
import { KeyValueList } from '../../../components/KeyValueList'
import { Skeleton } from '../../../components/Skeleton'
import { FormField } from '../../../components/forms'
import { useToast } from '../../../components/toast'
import { useFormSubmit } from '../../../components/useFormSubmit'
import { useLoad } from '../../../components/useLoad'
import { formatDate } from '../../../lib/format'
import { toDateString } from '../../monitoring/periods'
import { toBase64 } from '../../tickets/ticketForm'
import { T } from '../messages'
import {
  REVISION_CONTENT_TYPES,
  REVISION_MAX_BYTES,
  commission,
  downloadHandoverProtocol,
  downloadRevisionReport,
  getDocumentation,
  getInstallation,
  listRevisionReports,
  recordRevisionReport,
  type RevisionReport,
} from '../workflowApi'

type Detail = components['schemas']['InstallationDetailResponse']

interface RevisionTabProps {
  detail: Detail
  /** Role may record revisions and commission (FIELD_ROLES). */
  canField: boolean
  onChange: (detail: Detail) => void
}

/** Revision report, commissioning and the documentation package (#246, #247). */
export default function RevisionTab({ detail, canField, onChange }: RevisionTabProps) {
  const { installation } = detail
  const id = installation.id
  const state = installation.state
  const operating = state === 'Commissioned' || state === 'HandedOver'
  // The state is part of the key, so recording a report (which changes the state) reloads the list.
  const reports = useLoad((signal) => listRevisionReports(id, signal), `revisions:${id}:${state}`)

  return (
    <div className="installation-workflow">
      {operating && <DocumentationSection installationId={id} name={installation.name} state={state} />}
      {canField && state === 'Inspection' && <RevisionForm installationId={id} onChange={onChange} />}
      {canField && state === 'InspectionPassed' && <CommissionForm installationId={id} onChange={onChange} />}
      <ReportList installationId={id} loading={reports.loading} reports={reports.data} failed={state === 'InspectionFailed'} />
    </div>
  )
}

function ReportList({
  installationId,
  loading,
  reports,
  failed,
}: {
  installationId: string
  loading: boolean
  reports: RevisionReport[] | undefined
  failed: boolean
}) {
  const toast = useToast()
  return (
    <article className="card installation-card installation-section" aria-labelledby="revision-reports-title">
      <h3 id="revision-reports-title" className="installation-card__title">
        {T.revision.reportsTitle}
      </h3>
      {failed && <p role="note" className="installation-card__note">{T.revision.failedHint}</p>}
      {loading && <Skeleton lines={2} label={T.revision.loading} />}
      {!loading && !reports && <p role="alert">{T.revision.error}</p>}
      {reports?.length === 0 && <p>{T.revision.empty}</p>}
      {reports && reports.length > 0 && (
        <ul className="revision-list">
          {[...reports].reverse().map((r) => (
            <li key={r.id} className="revision-list__item">
              <div className="revision-list__head">
                <strong>{formatDate(r.inspectedOn)}</strong>
                <span className={`badge ${r.result === 'Passed' ? 'badge--success' : 'badge--error'}`}>{T.revision.results[r.result]}</span>
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  aria-label={T.revision.downloadLabel(r.fileName)}
                  onClick={() => downloadRevisionReport(installationId, r).catch((e) => toast.error(userMessage(e)))}
                >
                  {T.revision.download}
                </button>
              </div>
              <KeyValueList
                items={[
                  { label: T.revision.inspector, value: r.inspectorName },
                  ...(r.inspectorCertificateNumber ? [{ label: T.revision.certificateShort, value: r.inspectorCertificateNumber }] : []),
                  ...(r.validUntil ? [{ label: T.revision.validUntil, value: formatDate(r.validUntil) }] : []),
                  ...(r.findings ? [{ label: T.revision.findings, value: r.findings }] : []),
                ]}
              />
            </li>
          ))}
        </ul>
      )}
    </article>
  )
}

interface RevisionValues {
  inspectedOn: string
  inspectorName: string
  certificate: string
  result: 'Passed' | 'Failed'
  validUntil: string
  findings: string
  file: File | null
}

function validateRevision(v: RevisionValues): FieldErrors {
  const errors: FieldErrors = {}
  if (!v.inspectedOn) errors.inspectedOn = [T.revision.dateRequired]
  if (!v.inspectorName.trim()) errors.inspectorName = [T.revision.inspectorRequired]
  if (v.result === 'Passed' && (!v.validUntil || (v.inspectedOn && v.validUntil <= v.inspectedOn))) errors.validUntil = [T.revision.validUntilRequired]
  if (v.result === 'Failed' && !v.findings.trim()) errors.findings = [T.revision.findingsRequired]
  if (!v.file) errors.fileName = [T.revision.fileRequired]
  else if (!(REVISION_CONTENT_TYPES as readonly string[]).includes(v.file.type)) errors.fileName = [T.revision.fileType]
  else if (v.file.size > REVISION_MAX_BYTES) errors.fileName = [T.revision.fileSize]
  return errors
}

function RevisionForm({ installationId, onChange }: { installationId: string; onChange: (detail: Detail) => void }) {
  const toast = useToast()
  const [values, setValues] = useState<RevisionValues>({
    inspectedOn: toDateString(new Date()),
    inspectorName: '',
    certificate: '',
    result: 'Passed',
    validUntil: '',
    findings: '',
    file: null,
  })
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})
  const set = <K extends keyof RevisionValues>(key: K, value: RevisionValues[K]) => setValues((cur) => ({ ...cur, [key]: value }))

  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    const file = values.file as File
    await recordRevisionReport(installationId, {
      inspectedOn: values.inspectedOn,
      inspectorName: values.inspectorName.trim(),
      inspectorCertificateNumber: values.certificate.trim() || null,
      result: values.result,
      validUntil: values.result === 'Passed' ? values.validUntil : values.validUntil || null,
      findings: values.findings.trim() || null,
      fileName: file.name,
      contentType: file.type,
      contentBase64: await toBase64(file),
    })
    toast.success(T.revision.recorded)
    onChange(await getInstallation(installationId))
  })

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validateRevision(values)
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  const errorsFor = (name: string) => [...(clientErrors[name] ?? []), ...(fieldErrors[name] ?? [])]

  return (
    <form className="card installation-card installation-section" aria-label={T.revision.formTitle} onSubmit={onSubmit} noValidate>
      <h3 className="installation-card__title">{T.revision.formTitle}</h3>
      <FormField label={T.revision.inspectedOn} required errors={errorsFor('inspectedOn')}>
        {(props) => <input {...props} type="date" required value={values.inspectedOn} onChange={(e) => set('inspectedOn', e.target.value)} />}
      </FormField>
      <FormField label={T.revision.inspector} required errors={errorsFor('inspectorName')}>
        {(props) => <input {...props} type="text" required value={values.inspectorName} onChange={(e) => set('inspectorName', e.target.value)} />}
      </FormField>
      <FormField label={T.revision.certificate} errors={errorsFor('inspectorCertificateNumber')}>
        {(props) => <input {...props} type="text" value={values.certificate} onChange={(e) => set('certificate', e.target.value)} />}
      </FormField>
      <fieldset className="installation-workflow__choice">
        <legend>{T.revision.result}</legend>
        {(['Passed', 'Failed'] as const).map((result) => (
          <label key={result}>
            <input type="radio" name="revision-result" checked={values.result === result} onChange={() => set('result', result)} />
            {T.revision.results[result]}
          </label>
        ))}
      </fieldset>
      {values.result === 'Passed' ? (
        <FormField label={T.revision.validUntil} required errors={errorsFor('validUntil')}>
          {(props) => <input {...props} type="date" required value={values.validUntil} onChange={(e) => set('validUntil', e.target.value)} />}
        </FormField>
      ) : (
        <FormField label={T.revision.findings} required errors={errorsFor('findings')}>
          {(props) => <textarea {...props} required rows={3} value={values.findings} onChange={(e) => set('findings', e.target.value)} />}
        </FormField>
      )}
      <FormField label={T.revision.file} required errors={[...errorsFor('fileName'), ...errorsFor('contentBase64'), ...errorsFor('contentType')]}>
        {(props) => (
          <input {...props} type="file" required accept={REVISION_CONTENT_TYPES.join(',')} onChange={(e) => set('file', e.target.files?.[0] ?? null)} />
        )}
      </FormField>
      <p className="form-hint">{T.revision.fileHint}</p>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <button type="submit" className="btn btn--primary" disabled={busy}>
        {T.revision.submit}
      </button>
    </form>
  )
}

function CommissionForm({ installationId, onChange }: { installationId: string; onChange: (detail: Detail) => void }) {
  const toast = useToast()
  const [date, setDate] = useState(toDateString(new Date()))
  const [acceptedBy, setAcceptedBy] = useState('')
  const [note, setNote] = useState('')
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  const { busy, fieldErrors, formError, submit } = useFormSubmit(async () => {
    const next = await commission(installationId, { commissionedOn: date, acceptedBy: acceptedBy.trim(), note: note.trim() || null })
    toast.success(T.commission.done)
    onChange(next)
  })

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors: FieldErrors = {}
    if (!date) errors.commissionedOn = [T.commission.dateRequired]
    if (!acceptedBy.trim()) errors.acceptedBy = [T.commission.acceptedByRequired]
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  const errorsFor = (name: string) => [...(clientErrors[name] ?? []), ...(fieldErrors[name] ?? [])]

  return (
    <form className="card installation-card installation-section" aria-label={T.commission.formTitle} onSubmit={onSubmit} noValidate>
      <h3 className="installation-card__title">{T.commission.formTitle}</h3>
      <p className="installation-card__note">{T.commission.hint}</p>
      <FormField label={T.commission.date} required errors={errorsFor('commissionedOn')}>
        {(props) => <input {...props} type="date" required value={date} onChange={(e) => setDate(e.target.value)} />}
      </FormField>
      <FormField label={T.commission.acceptedBy} required errors={errorsFor('acceptedBy')}>
        {(props) => <input {...props} type="text" required value={acceptedBy} onChange={(e) => setAcceptedBy(e.target.value)} />}
      </FormField>
      <FormField label={T.commission.note} errors={errorsFor('note')}>
        {(props) => <textarea {...props} rows={2} value={note} onChange={(e) => setNote(e.target.value)} />}
      </FormField>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <button type="submit" className="btn btn--primary" disabled={busy}>
        {T.commission.submit}
      </button>
    </form>
  )
}

function DocumentationSection({ installationId, name, state }: { installationId: string; name: string; state: string }) {
  const toast = useToast()
  const docs = useLoad((signal) => getDocumentation(installationId, signal), `documentation:${installationId}:${state}`)
  const data = docs.data

  return (
    <section className="card installation-card installation-section" aria-labelledby="documentation-title">
      <h3 id="documentation-title" className="installation-card__title">
        {T.documentation.title}
      </h3>
      {docs.loading && <Skeleton lines={3} label={T.documentation.loading} />}
      {docs.error && <p role="alert">{T.documentation.error}</p>}
      {data && (
        <>
          <KeyValueList
            items={[
              { label: T.documentation.commissionedOn, value: formatDate(data.protocol.commissionedOn) },
              { label: T.documentation.acceptedBy, value: data.protocol.acceptedBy },
              {
                label: T.documentation.monitoring,
                value: data.protocol.monitoringEnabled ? T.documentation.monitoringOn : T.documentation.monitoringOff,
              },
              {
                label: T.documentation.revision,
                value: T.documentation.revisionValue(
                  T.revision.results[data.revisionReport.result],
                  data.revisionReport.validUntil ? formatDate(data.revisionReport.validUntil) : '–',
                ),
              },
              { label: T.documentation.devices, value: String(data.devices.length) },
              { label: T.documentation.downloads, value: String(data.downloads.length) },
            ]}
          />
          <button
            type="button"
            className="btn"
            onClick={() =>
              downloadHandoverProtocol(installationId, T.documentation.handoverFile(slug(name))).catch((e) => toast.error(userMessage(e)))
            }
          >
            {T.documentation.handover}
          </button>
        </>
      )}
    </section>
  )
}

/** File-name friendly form of the installation name (no diacritics or spaces). */
function slug(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-zA-Z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
    .toLowerCase()
}
