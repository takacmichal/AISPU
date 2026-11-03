import { useCallback, useState } from 'react'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError } from '../../api/problem'
import { FormField } from '../../components/forms'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { formatCurrency, formatKwp, formatNumber } from '../../lib/format'
import { TEXT } from './labels'
import { formatKw, formatKwh } from './units'

type Preview = components['schemas']['DesignConfigurationPreviewResponse']
type CatalogItem = components['schemas']['ComponentResponse']

const MIN_COUNT = 1
const MAX_COUNT = 1000

/** Panel and inverter picker with a compatibility check and yield estimate; saving creates a new version. */
export function ConfigurationSection({ designId, onSaved }: { designId: string; onSaved: () => void }) {
  const toast = useToast()
  const catalog = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/components', { params: { query: { active: true } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, 'components')

  const [panelId, setPanelId] = useState('')
  const [inverterId, setInverterId] = useState('')
  const [count, setCount] = useState('')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [preview, setPreview] = useState<{ key: string; result: Preview } | null>(null)

  const parsed = Number(count)
  const key = JSON.stringify([panelId, inverterId, count])

  function validate(): boolean {
    const next: Record<string, string> = {}
    if (!panelId) next.panelId = TEXT.required
    if (!inverterId) next.inverterId = TEXT.required
    if (count.trim() === '') next.panelCount = TEXT.required
    else if (!Number.isInteger(parsed) || parsed < MIN_COUNT || parsed > MAX_COUNT) {
      next.panelCount = `Zadajte celé číslo od ${MIN_COUNT} do ${formatNumber(MAX_COUNT, 0)}.`
    }
    setErrors(next)
    return Object.keys(next).length === 0
  }

  const runPreview = useCallback(async () => {
    const result = await api.POST('/api/designs/{id}/configuration/preview', { params: { path: { id: designId } }, body: { panelId, inverterId, panelCount: parsed } })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    setPreview({ key, result: result.data })
  }, [designId, key, panelId, inverterId, parsed])
  const previewSubmit = useFormSubmit(runPreview)

  const saveVersion = useCallback(async () => {
    const result = await api.POST('/api/designs/{id}/versions', { params: { path: { id: designId } }, body: { panelId, inverterId, panelCount: parsed } })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    toast.success(TEXT.versionSaved)
    setPreview(null)
    onSaved()
  }, [designId, panelId, inverterId, parsed, toast, onSaved])
  const saveSubmit = useFormSubmit(saveVersion)

  if (catalog.loading) return <Skeleton lines={3} label="Načítavam katalóg…" />
  if (catalog.error)
    return (
      <p role="alert" className="alert">
        {TEXT.componentsError}{' '}
        <button type="button" className="btn btn--ghost btn--sm" onClick={catalog.reload}>
          Skúsiť znova
        </button>
      </p>
    )
  const panels = (catalog.data ?? []).filter((c) => c.type === 'Panel')
  const inverters = (catalog.data ?? []).filter((c) => c.type === 'Inverter')
  if (panels.length === 0 || inverters.length === 0) return <p className="alert">{TEXT.noComponents}</p>

  const fresh = preview?.key === key ? preview.result : null
  const busy = previewSubmit.busy || saveSubmit.busy
  const describe = (c: CatalogItem) => `${c.name} (${c.code}${c.powerW ? `, ${formatNumber(c.powerW, 0)} W` : ''}, ${formatCurrency(c.unitPrice, c.currency)})`
  const serverErrors = (field: string) => previewSubmit.fieldErrors[field] ?? saveSubmit.fieldErrors[field] ?? []
  const fieldErrors = (field: string) => [...(errors[field] ? [errors[field]] : []), ...serverErrors(field)]
  const formError = previewSubmit.formError ?? saveSubmit.formError

  async function onPreview() {
    if (validate()) await previewSubmit.submit()
  }
  async function onSave() {
    if (validate()) await saveSubmit.submit()
  }

  return (
    <div>
      <div className="design-grid">
        <FormField label={TEXT.panel} errors={fieldErrors('panelId')}>
          {(props) => (
            <select {...props} value={panelId} onChange={(e) => setPanelId(e.target.value)}>
              <option value="">{TEXT.choose}</option>
              {panels.map((c) => (
                <option key={c.id} value={c.id}>
                  {describe(c)}
                </option>
              ))}
            </select>
          )}
        </FormField>
        <FormField label={TEXT.panelCount} errors={fieldErrors('panelCount')}>
          {(props) => <input {...props} type="text" inputMode="numeric" value={count} onChange={(e) => setCount(e.target.value)} />}
        </FormField>
        <FormField label={TEXT.inverter} errors={fieldErrors('inverterId')}>
          {(props) => (
            <select {...props} value={inverterId} onChange={(e) => setInverterId(e.target.value)}>
              <option value="">{TEXT.choose}</option>
              {inverters.map((c) => (
                <option key={c.id} value={c.id}>
                  {describe(c)}
                </option>
              ))}
            </select>
          )}
        </FormField>
      </div>

      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}

      {fresh && (
        <div className="card design-section" aria-live="polite">
          <p>
            <span className={`badge ${fresh.compatibility.isCompatible ? 'badge--success' : 'badge--error'}`}>
              {fresh.compatibility.isCompatible ? TEXT.compatible : TEXT.incompatible}
            </span>
          </p>
          <dl className="design-metrics">
            <div>
              <dt>{TEXT.dcPower}</dt>
              <dd>{formatKwp(fresh.compatibility.dcPowerKwp)}</dd>
            </div>
            <div>
              <dt>{TEXT.acPower}</dt>
              <dd>{formatKw(fresh.compatibility.acPowerKw)}</dd>
            </div>
            <div>
              <dt>{TEXT.ratio}</dt>
              <dd>{formatNumber(fresh.compatibility.dcAcRatio)}</dd>
            </div>
            {fresh.estimate && (
              <div className="design-metric--lead">
                <dt>{TEXT.annualYield}</dt>
                <dd>{formatKwh(fresh.estimate.annualKwh)}</dd>
              </div>
            )}
          </dl>
          {fresh.compatibility.errors.length > 0 && (
            <ul role="alert" className="form-error">
              {fresh.compatibility.errors.map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          )}
          {fresh.compatibility.warnings.length > 0 && (
            <ul aria-label={TEXT.warnings}>
              {fresh.compatibility.warnings.map((m) => (
                <li key={m}>{m}</li>
              ))}
            </ul>
          )}
        </div>
      )}

      <div className="btn-row">
        <button type="button" className="btn btn--ghost" disabled={busy} onClick={onPreview}>
          {TEXT.preview}
        </button>
        <button
          type="button"
          className="btn btn--primary"
          disabled={busy || (fresh !== null && !fresh.compatibility.isCompatible)}
          onClick={onSave}
        >
          {TEXT.saveVersion}
        </button>
      </div>
    </div>
  )
}
