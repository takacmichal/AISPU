import { useCallback, useMemo, useState, type FormEvent } from 'react'
import { api } from '../../../api/client'
import type { components } from '../../../api/generated/schema'
import { problemToApiError, type FieldErrors } from '../../../api/problem'
import { FormField } from '../../../components/forms'
import { useToast } from '../../../components/toast'
import { useFormSubmit } from '../../../components/useFormSubmit'
import { useLoad } from '../../../components/useLoad'
import { formatDate } from '../../../lib/format'
import { T } from '../messages'
import { findCollisions } from '../schedule'

type Detail = components['schemas']['InstallationDetailResponse']

interface PlanTabProps {
  detail: Detail
  canPlan: boolean
  onChange: (detail: Detail) => void
}

export default function PlanTab({ detail, canPlan, onChange }: PlanTabProps) {
  const { installation } = detail
  const toast = useToast()
  const [start, setStart] = useState(installation.scheduledStart ?? '')
  const [end, setEnd] = useState(installation.scheduledEnd ?? '')
  const [reason, setReason] = useState('')
  const [selected, setSelected] = useState<string[]>(installation.technicianIds)
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  // Technicians come from the users API (Admin only on the backend); other roles fall back to the assigned ids.
  const users = useLoad(async (signal) => {
    const { data } = await api.GET('/api/users', { params: { query: { pageSize: 100 } }, signal })
    return data ? data.items.filter((u) => u.roles.includes('Technician')) : null
  }, 'technicians')

  // Used for the collision check; best effort.
  const all = useLoad(async (signal) => {
    const { data } = await api.GET('/api/installations', { signal })
    return data ?? []
  }, 'all-installations')

  const contract = useLoad(async (signal) => {
    const { data } = await api.GET('/api/contracts/{id}', { params: { path: { id: installation.contractId } }, signal })
    return data ?? null
  }, installation.contractId)

  const options = useMemo(() => {
    const loaded = (users.data ?? []).map((u) => ({ id: u.id, label: u.email }))
    const known = new Set(loaded.map((o) => o.id))
    const extra = [...new Set([...installation.technicianIds, ...selected])].filter((id) => !known.has(id)).map((id) => ({ id, label: id }))
    return [...loaded, ...extra]
  }, [users.data, installation.technicianIds, selected])
  const technicianListUnavailable = !users.loading && users.data === null

  const collisions = useMemo(
    () => findCollisions(all.data ?? [], installation.id, start, end, selected),
    [all.data, installation.id, start, end, selected],
  )

  const warnings: string[] = []
  if (contract.data && contract.data.status !== 'Signed') warnings.push(T.plan.contractNotSigned)
  if (contract.data && contract.data.depositStatus !== 'Paid') warnings.push(T.plan.depositNotPaid)
  for (const c of collisions) warnings.push(T.plan.collision(c.name, formatDate(c.scheduledStart), formatDate(c.scheduledEnd)))

  const scheduleChanged = Boolean(installation.scheduledStart) && (start !== installation.scheduledStart || end !== installation.scheduledEnd)
  const techniciansChanged = [...selected].sort().join() !== [...installation.technicianIds].sort().join()

  const save = useCallback(async () => {
    let latest = detail
    if (start !== (installation.scheduledStart ?? '') || end !== (installation.scheduledEnd ?? '')) {
      const { data, response, error } = await api.PUT('/api/installations/{id}/schedule', {
        params: { path: { id: installation.id } },
        body: { scheduledStart: start, scheduledEnd: end },
      })
      if (!data) throw problemToApiError(response.status, error)
      latest = data
      onChange(data)
    }
    if (techniciansChanged) {
      const { data, response, error } = await api.PUT('/api/installations/{id}/technicians', {
        params: { path: { id: installation.id } },
        body: { technicianIds: selected },
      })
      if (!data) throw problemToApiError(response.status, error)
      latest = data
      onChange(data)
    }
    setReason('')
    toast.success(T.plan.saved)
    return latest
  }, [detail, start, end, selected, techniciansChanged, installation, onChange, toast])

  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors: FieldErrors = {}
    if (!start || !end) errors.scheduledStart = [T.plan.required]
    else if (end < start) errors.scheduledEnd = [T.plan.endBeforeStart]
    if (scheduleChanged && !reason.trim()) errors.reason = [T.plan.reasonRequired]
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  const errorsFor = (name: string) => [...(clientErrors[name] ?? []), ...(fieldErrors[name] ?? [])]

  if (!canPlan) {
    return (
      <div>
        <p>{T.plan.readOnly}</p>
        <dl className="card">
          <dt>{T.plan.scheduleLabel}</dt>
          <dd>{installation.scheduledStart ? `${formatDate(installation.scheduledStart)} – ${formatDate(installation.scheduledEnd)}` : '–'}</dd>
          <dt>{T.plan.technicians}</dt>
          <dd>{installation.technicianIds.length}</dd>
        </dl>
      </div>
    )
  }

  return (
    <form onSubmit={onSubmit} noValidate>
      {warnings.map((w) => (
        <p key={w} role="status" className="installation-warning">
          {w}
        </p>
      ))}
      <FormField label={T.plan.start} errors={errorsFor('scheduledStart')}>
        {(props) => (
          <input
            {...props}
            type="date"
            value={start}
            onChange={(e) => {
              setStart(e.target.value)
              clearFieldError('scheduledStart')
            }}
          />
        )}
      </FormField>
      <FormField label={T.plan.end} errors={errorsFor('scheduledEnd')}>
        {(props) => (
          <input
            {...props}
            type="date"
            value={end}
            onChange={(e) => {
              setEnd(e.target.value)
              clearFieldError('scheduledEnd')
            }}
          />
        )}
      </FormField>
      {scheduleChanged && (
        <FormField label={T.plan.reason} errors={errorsFor('reason')}>
          {(props) => <input {...props} type="text" value={reason} onChange={(e) => setReason(e.target.value)} />}
        </FormField>
      )}
      {scheduleChanged && <p className="form-hint">{T.plan.reasonHint}</p>}

      <fieldset>
        <legend>{T.plan.technicians}</legend>
        {technicianListUnavailable && <p>{T.plan.techniciansUnavailable}</p>}
        {options.length === 0 && !users.loading && <p>{T.plan.noTechnicians}</p>}
        <div className="card technician-list">
          {options.map((o) => (
            <label key={o.id}>
              <input
                type="checkbox"
                checked={selected.includes(o.id)}
                onChange={(e) => setSelected((cur) => (e.target.checked ? [...cur, o.id] : cur.filter((id) => id !== o.id)))}
              />
              {o.label}
            </label>
          ))}
        </div>
      </fieldset>

      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <button type="submit" className="btn" disabled={busy}>
        {T.plan.save}
      </button>
    </form>
  )
}
