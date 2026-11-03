import { describe, expect, it } from 'vitest'
import { emptyParameters, parseNumber, toRequest, validateParameters, type ParametersValues } from './designForm'

const valid: ParametersValues = {
  latitude: '48,15',
  longitude: '17.1',
  annualConsumptionKwh: '4500',
  consumptionProfile: 'Household',
  roofPlanes: [{ name: 'Juh', azimuthDeg: '0', tiltDeg: '35', usableAreaM2: '40', shadingLossPercent: '5', roofType: 'PitchedTile' }],
}

describe('designForm', () => {
  it('accepts valid values and parses decimal commas', () => {
    expect(validateParameters(valid)).toEqual({})
    expect(parseNumber('48,15')).toBe(48.15)
    expect(toRequest(valid).latitude).toBe(48.15)
  })

  it('requires fields', () => {
    const errors = validateParameters(emptyParameters)
    expect(errors.latitude).toBeDefined()
    expect(errors['roofPlanes[0].name']).toBeDefined()
    expect(errors['roofPlanes[0].usableAreaM2']).toBeDefined()
  })

  it('enforces the backend ranges', () => {
    const errors = validateParameters({
      ...valid,
      latitude: '91',
      longitude: '-181',
      annualConsumptionKwh: '-1',
      roofPlanes: [{ ...valid.roofPlanes[0], tiltDeg: '91', azimuthDeg: '200', usableAreaM2: '0', shadingLossPercent: '101' }],
    })
    for (const key of ['latitude', 'longitude', 'annualConsumptionKwh', 'roofPlanes[0].tiltDeg', 'roofPlanes[0].azimuthDeg', 'roofPlanes[0].usableAreaM2', 'roofPlanes[0].shadingLossPercent']) {
      expect(errors[key], key).toBeDefined()
    }
  })

  it('rejects text instead of numbers', () => {
    expect(validateParameters({ ...valid, latitude: 'abc' }).latitude).toEqual(['Zadajte číslo.'])
  })
})
