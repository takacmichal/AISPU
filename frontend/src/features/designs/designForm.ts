import type { components } from '../../api/generated/schema'
import type { FieldErrors } from '../../api/problem'

type Design = components['schemas']['FveDesignResponse']
type Request = components['schemas']['UpdateDesignParametersRequest']

export const MAX_ROOF_PLANES = 20
export const NAME_MAX = 100
export const NOTES_MAX = 4000

export interface PlaneValues {
  name: string
  azimuthDeg: string
  tiltDeg: string
  usableAreaM2: string
  shadingLossPercent: string
  roofType: string
}

export interface ParametersValues {
  latitude: string
  longitude: string
  annualConsumptionKwh: string
  consumptionProfile: string
  roofPlanes: PlaneValues[]
}

export const emptyPlane: PlaneValues = {
  name: '',
  azimuthDeg: '0',
  tiltDeg: '30',
  usableAreaM2: '',
  shadingLossPercent: '0',
  roofType: 'PitchedTile',
}

export const emptyParameters: ParametersValues = {
  latitude: '',
  longitude: '',
  annualConsumptionKwh: '',
  consumptionProfile: 'Household',
  roofPlanes: [{ ...emptyPlane }],
}

const text = (value: number | string | null | undefined) => (value === null || value === undefined ? '' : String(value))

export function fromDesign(d: Design): ParametersValues {
  if (d.roofPlanes.length === 0 && d.latitude === null) return { ...emptyParameters, roofPlanes: [{ ...emptyPlane }] }
  return {
    latitude: text(d.latitude),
    longitude: text(d.longitude),
    annualConsumptionKwh: text(d.annualConsumptionKwh),
    consumptionProfile: d.consumptionProfile ?? 'Household',
    roofPlanes: d.roofPlanes.map((p) => ({
      name: p.name,
      azimuthDeg: text(p.azimuthDeg),
      tiltDeg: text(p.tiltDeg),
      usableAreaM2: text(p.usableAreaM2),
      shadingLossPercent: text(p.shadingLossPercent),
      roofType: p.roofType,
    })),
  }
}

/** Accepts both "12,5" and "12.5"; returns NaN for anything that is not a plain number. */
export function parseNumber(value: string): number {
  const normalized = value.trim().replace(',', '.')
  return normalized === '' ? NaN : Number(normalized)
}

export function toRequest(v: ParametersValues): Request {
  return {
    latitude: parseNumber(v.latitude),
    longitude: parseNumber(v.longitude),
    annualConsumptionKwh: parseNumber(v.annualConsumptionKwh),
    consumptionProfile: v.consumptionProfile as Request['consumptionProfile'],
    roofPlanes: v.roofPlanes.map((p) => ({
      name: p.name.trim(),
      azimuthDeg: parseNumber(p.azimuthDeg),
      tiltDeg: parseNumber(p.tiltDeg),
      usableAreaM2: parseNumber(p.usableAreaM2),
      shadingLossPercent: p.shadingLossPercent.trim() === '' ? 0 : parseNumber(p.shadingLossPercent),
      roofType: p.roofType as NonNullable<Request['roofPlanes']>[number]['roofType'],
    })),
  }
}

/** Mirrors UpdateDesignParametersRequest / RoofPlaneRequest DataAnnotations. Plane errors use `roofPlanes[i].field` keys, same as normalized server keys. */
export function validateParameters(v: ParametersValues): FieldErrors {
  const errors: FieldErrors = {}
  const add = (field: string, message: string) => (errors[field] ??= []).push(message)
  const range = (field: string, raw: string, min: number, max: number, unit: string, required = true) => {
    if (raw.trim() === '') {
      if (required) add(field, 'Vyplňte toto pole.')
      return
    }
    const n = parseNumber(raw)
    if (Number.isNaN(n)) add(field, 'Zadajte číslo.')
    else if (n < min || n > max) add(field, `Hodnota musí byť od ${min.toLocaleString('sk-SK')} do ${max.toLocaleString('sk-SK')}${unit}.`)
  }

  range('latitude', v.latitude, -90, 90, ' °')
  range('longitude', v.longitude, -180, 180, ' °')
  range('annualConsumptionKwh', v.annualConsumptionKwh, 0, 100_000_000, ' kWh')
  if (v.roofPlanes.length < 1) add('roofPlanes', 'Pridajte aspoň jednu strešnú plochu.')
  if (v.roofPlanes.length > MAX_ROOF_PLANES) add('roofPlanes', `Najviac ${MAX_ROOF_PLANES} strešných plôch.`)
  v.roofPlanes.forEach((p, i) => {
    const key = (name: string) => `roofPlanes[${i}].${name}`
    if (p.name.trim() === '') add(key('name'), 'Vyplňte toto pole.')
    else if (p.name.trim().length > NAME_MAX) add(key('name'), `Maximálna dĺžka je ${NAME_MAX} znakov.`)
    range(key('azimuthDeg'), p.azimuthDeg, -180, 180, ' °')
    range(key('tiltDeg'), p.tiltDeg, 0, 90, ' °')
    range(key('usableAreaM2'), p.usableAreaM2, 0.1, 100_000, ' m²')
    range(key('shadingLossPercent'), p.shadingLossPercent, 0, 100, ' %', false)
  })
  return errors
}
