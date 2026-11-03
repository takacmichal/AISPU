import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useLoad } from '../../components/useLoad'
import { MESSAGES } from '../../lib/messages'
import { formatDateTime } from '../../lib/format'
import { ConfigurationSection } from './ConfigurationSection'
import './designs.css'
import { InfeasibleDialog } from './InfeasibleDialog'
import { STATUS_BADGES, STATUS_LABELS, TEXT } from './labels'
import { ParametersSection } from './ParametersSection'
import { SurveyForm } from './SurveySection'
import { VersionHistory, VersionResult } from './VersionsSection'

/** The backend allows writes for these roles only; the UI just hides the controls. */
const DESIGNER_ROLES = ['Admin', 'Designer']

/** Design detail (/designs/:id): survey, parameters, configuration with yield estimate, versions, infeasible. */
export default function DesignDetailPage() {
  const { id = '' } = useParams()
  const { roles } = useAuth()
  const canEdit = roles.some((r) => DESIGNER_ROLES.includes(r))
  const [infeasibleOpen, setInfeasibleOpen] = useState(false)

  const design = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/designs/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(response.status === 404 ? 'not-found' : `HTTP ${response.status}`)
    return data
  }, id)
  const versions = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/designs/{id}/versions', { params: { path: { id } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, id)

  function refresh() {
    design.reload()
    versions.reload()
  }

  if (design.loading) return <Skeleton lines={5} label={TEXT.loadingDetail} />
  if (design.error || !design.data)
    return (
      <div>
        <p role="alert" className="alert">
          {TEXT.detailError}
        </p>
        <div className="btn-row">
          <button type="button" className="btn btn--ghost" onClick={design.reload}>
            {MESSAGES.retry}
          </button>
          <Link to="/designs" className="btn btn--ghost">
            {TEXT.back}
          </Link>
        </div>
      </div>
    )

  const d = design.data
  const sorted = [...(versions.data ?? [])].sort((a, b) => Number(b.versionNumber) - Number(a.versionNumber))
  const showParameters = d.status === 'SiteSurveyDone' || d.status === 'InDesign'
  const showVersions = d.status === 'InDesign' || d.status === 'Rejected' || d.status === 'Approved'

  return (
    <section>
      <div className="toolbar">
        <h2>Návrh FVE</h2>
        <span className={`badge ${STATUS_BADGES[d.status] ?? ''}`}>{STATUS_LABELS[d.status] ?? d.status}</span>
      </div>
      <p>
        <Link to="/designs">{TEXT.back}</Link>
      </p>

      {d.requestNotes && (
        <p>
          <strong>{TEXT.requestNotes}:</strong> {d.requestNotes}
        </p>
      )}

      {d.status === 'Infeasible' && (
        <p role="status" className="alert">
          <strong>{TEXT.infeasibleShown}:</strong> {d.infeasibleReason ?? '–'}
        </p>
      )}

      <div className="design-section">
        <h3>{TEXT.surveyTitle}</h3>
        {d.siteSurvey ? (
          <div className="card">
            <p>{d.siteSurvey.notes}</p>
            <p>
              {TEXT.surveyDone} · {formatDateTime(d.siteSurvey.surveyedAt)}
            </p>
          </div>
        ) : d.status === 'Requested' && canEdit ? (
          <SurveyForm designId={d.id} onSaved={refresh} />
        ) : (
          <EmptyState title="Obhliadka zatiaľ nebola zaznamenaná." />
        )}
      </div>

      {showParameters && (
        <div className="design-section">
          <h3>{TEXT.paramsTitle}</h3>
          {canEdit ? (
            <ParametersSection key={String(d.updatedAt)} design={d} onSaved={refresh} primary={d.status === 'SiteSurveyDone'} />
          ) : (
            <p>{TEXT.paramsLocked}</p>
          )}
        </div>
      )}

      {d.status === 'InDesign' && canEdit && (
        <div className="design-section">
          <h3>{TEXT.configTitle}</h3>
          <ConfigurationSection designId={d.id} onSaved={refresh} />
        </div>
      )}

      {showVersions && (
        <div className="design-section">
          <h3>{TEXT.resultTitle}</h3>
          {versions.loading && <Skeleton lines={3} label="Načítavam verzie…" />}
          {versions.error && (
            <p role="alert" className="alert">
              {TEXT.historyError}{' '}
              <button type="button" className="btn btn--ghost btn--sm" onClick={versions.reload}>
                {MESSAGES.retry}
              </button>
            </p>
          )}
          {!versions.loading && !versions.error && sorted.length === 0 && <EmptyState title={TEXT.noVersion} />}
          {sorted.length > 0 && (
            <>
              <VersionResult version={sorted[0]} />
              <h3>{TEXT.historyTitle}</h3>
              <VersionHistory versions={sorted} />
            </>
          )}
        </div>
      )}

      {d.status === 'SiteSurveyDone' && canEdit && (
        <div className="btn-row">
          <button type="button" className="btn btn--danger" onClick={() => setInfeasibleOpen(true)}>
            {TEXT.infeasible}
          </button>
        </div>
      )}
      {infeasibleOpen && (
        <InfeasibleDialog
          designId={d.id}
          onClose={() => setInfeasibleOpen(false)}
          onDone={() => {
            setInfeasibleOpen(false)
            refresh()
          }}
        />
      )}
    </section>
  )
}
