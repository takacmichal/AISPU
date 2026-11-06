import { useState } from 'react'
import { userMessage } from '../../../api/problem'
import { Skeleton } from '../../../components/Skeleton'
import { useToast } from '../../../components/toast'
import { useLoad } from '../../../components/useLoad'
import { formatDateTime } from '../../../lib/format'
import { T } from '../messages'
import { getChecklist, setChecklistStep, type Checklist } from '../workflowApi'

interface ChecklistTabProps {
  installationId: string
  /** Role may edit field work (FIELD_ROLES); the server also allows edits only while mounting. */
  canField: boolean
}

/** Assembly checklist (#246): required steps must be done before the mounting can be completed. */
export default function ChecklistTab({ installationId, canField }: ChecklistTabProps) {
  const toast = useToast()
  const [latest, setLatest] = useState<Checklist | null>(null)
  const [pending, setPending] = useState<string | null>(null)
  const { data, loading } = useLoad((signal) => getChecklist(installationId, signal), `checklist:${installationId}`)

  const checklist = latest?.installationId === installationId ? latest : data
  if (loading && !checklist) return <Skeleton lines={4} label={T.checklist.loading} />
  if (!checklist)
    return (
      <p role="alert" className="alert">
        {T.checklist.error}
      </p>
    )

  const editable = checklist.editable && canField
  const required = checklist.steps.filter((s) => s.required)
  const requiredDone = required.filter((s) => s.done).length

  async function toggle(code: string, done: boolean) {
    setPending(code)
    try {
      setLatest(await setChecklistStep(installationId, code, done))
    } catch (e) {
      toast.error(userMessage(e))
    } finally {
      setPending(null)
    }
  }

  return (
    <article className="card installation-card installation-section" aria-labelledby="checklist-title">
      <h3 id="checklist-title" className="installation-card__title">
        {T.checklist.title}
      </h3>
      <p className="installation-card__note">{T.checklist.progress(requiredDone, required.length)}</p>
      {!checklist.editable && <p role="note" className="installation-card__note">{T.checklist.readOnly}</p>}
      {checklist.editable && checklist.complete && <p role="status" className="installation-card__note">{T.checklist.complete}</p>}
      <ul className="checklist" aria-labelledby="checklist-title">
        {[...checklist.steps]
          .sort((a, b) => Number(a.position) - Number(b.position))
          .map((s) => (
            <li key={s.code} className="checklist__step">
              <label>
                <input
                  type="checkbox"
                  checked={s.done}
                  disabled={!editable || pending !== null}
                  onChange={(e) => void toggle(s.code, e.target.checked)}
                />
                <span>{s.title}</span>
              </label>
              {s.required && <span className="badge badge--muted">{T.checklist.required}</span>}
              {s.done && s.changedAt && <span className="checklist__meta">{T.checklist.changedAt(formatDateTime(s.changedAt))}</span>}
            </li>
          ))}
      </ul>
    </article>
  )
}
