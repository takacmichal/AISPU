import { useCallback, useState, type FormEvent } from 'react'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError, type FieldErrors } from '../../api/problem'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import {
  MAX_ROOF_PLANES,
  emptyPlane,
  fromDesign,
  toRequest,
  validateParameters,
  type ParametersValues,
  type PlaneValues,
} from './designForm'
import { PROFILE_LABELS, ROOF_TYPE_LABELS, TEXT } from './labels'

type Design = components['schemas']['FveDesignResponse']

interface Props {
  design: Design
  /** Called with the saved design so the page can refresh its state. */
  onSaved: () => void
  /** Only one primary button per view: the page decides whether this form owns it. */
  primary?: boolean
}

/** Roof and consumption parameters form (#239). Editable by Admin/Designer once the survey is done. */
export function ParametersSection({ design, onSaved, primary = true }: Props) {
  const toast = useToast()
  const [values, setValues] = useState<ParametersValues>(() => fromDesign(design))
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  const save = useCallback(async () => {
    const result = await api.PUT('/api/designs/{id}/parameters', { params: { path: { id: design.id } }, body: toRequest(values) })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    toast.success(TEXT.paramsSaved)
    onSaved()
  }, [values, design.id, toast, onSaved])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]
  function clearError(field: string) {
    setClientErrors((current) => {
      const rest = { ...current }
      delete rest[field]
      return rest
    })
    clearFieldError(field)
  }

  function setTop(name: keyof Omit<ParametersValues, 'roofPlanes'>, value: string) {
    setValues((current) => ({ ...current, [name]: value }))
    clearError(name)
  }
  function setPlane(index: number, name: keyof PlaneValues, value: string) {
    setValues((current) => ({
      ...current,
      roofPlanes: current.roofPlanes.map((p, i) => (i === index ? { ...p, [name]: value } : p)),
    }))
    clearError(`roofPlanes[${index}].${name}`)
  }
  function addPlane() {
    setValues((current) => ({ ...current, roofPlanes: [...current.roofPlanes, { ...emptyPlane }] }))
  }
  function removePlane(index: number) {
    setValues((current) => ({ ...current, roofPlanes: current.roofPlanes.filter((_, i) => i !== index) }))
    setClientErrors({})
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validateParameters(values)
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  const number = (name: keyof Omit<ParametersValues, 'roofPlanes'>, label: string) => (
    <FormField label={label} errors={errorsFor(name)}>
      {(props) => <input {...props} type="text" inputMode="decimal" value={values[name]} onChange={(e) => setTop(name, e.target.value)} />}
    </FormField>
  )

  return (
    <form onSubmit={onSubmit} noValidate aria-label={TEXT.paramsTitle}>
      <div className="design-grid">
        {number('latitude', TEXT.latitude)}
        {number('longitude', TEXT.longitude)}
        {number('annualConsumptionKwh', TEXT.consumption)}
        <FormField label={TEXT.profile} errors={errorsFor('consumptionProfile')}>
          {(props) => (
            <select {...props} value={values.consumptionProfile} onChange={(e) => setTop('consumptionProfile', e.target.value)}>
              {Object.entries(PROFILE_LABELS).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          )}
        </FormField>
      </div>

      {errorsFor('roofPlanes').length > 0 && (
        <p role="alert" className="form-error">
          {errorsFor('roofPlanes').join(' ')}
        </p>
      )}
      {values.roofPlanes.map((plane, i) => {
        const field = (name: keyof PlaneValues, label: string) => (
          <FormField label={`${label} (${i + 1})`} errors={errorsFor(`roofPlanes[${i}].${name}`)}>
            {(props) => (
              <input
                {...props}
                type="text"
                inputMode={name === 'name' ? 'text' : 'decimal'}
                value={plane[name]}
                onChange={(e) => setPlane(i, name, e.target.value)}
              />
            )}
          </FormField>
        )
        return (
          <fieldset key={i} className="design-plane">
            <legend>
              {TEXT.roofPlane} {i + 1}
            </legend>
            <div className="design-grid">
              {field('name', TEXT.planeName)}
              {field('azimuthDeg', TEXT.azimuth)}
              {field('tiltDeg', TEXT.tilt)}
              {field('usableAreaM2', TEXT.area)}
              {field('shadingLossPercent', TEXT.shading)}
              <FormField label={`${TEXT.roofType} (${i + 1})`} errors={errorsFor(`roofPlanes[${i}].roofType`)}>
                {(props) => (
                  <select {...props} value={plane.roofType} onChange={(e) => setPlane(i, 'roofType', e.target.value)}>
                    {Object.entries(ROOF_TYPE_LABELS).map(([value, label]) => (
                      <option key={value} value={value}>
                        {label}
                      </option>
                    ))}
                  </select>
                )}
              </FormField>
            </div>
            {values.roofPlanes.length > 1 && (
              <button type="button" className="btn btn--ghost btn--sm" onClick={() => removePlane(i)}>
                {TEXT.removePlane} {i + 1}
              </button>
            )}
          </fieldset>
        )
      })}
      <div className="btn-row">
        <button type="button" className="btn btn--ghost" onClick={addPlane} disabled={values.roofPlanes.length >= MAX_ROOF_PLANES}>
          {TEXT.addPlane}
        </button>
      </div>

      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
      <div className="btn-row">
        <button type="submit" className={`btn ${primary ? 'btn--primary' : 'btn--ghost'}`} disabled={busy}>
          {TEXT.paramsSave}
        </button>
      </div>
    </form>
  )
}
