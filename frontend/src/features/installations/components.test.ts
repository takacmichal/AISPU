import { describe, expect, it } from 'vitest'
import { componentSummaries } from './components'

const d = (type: string, model: string, powerW: number, status = 'Installed') => ({ type, manufacturer: 'JA Solar', model, powerW, status }) as never

describe('componentSummaries', () => {
  it('groups installed devices by type with count, main model and total power', () => {
    const result = componentSummaries([
      d('Inverter', 'SUN-10K', 10000),
      d('Panel', 'JAM-450', 450),
      d('Panel', 'JAM-450', 450),
      d('Panel', 'JAM-410', 410),
      d('Panel', 'JAM-450', 450, 'Replaced'),
    ])
    expect(result.map((r) => r.type)).toEqual(['Panel', 'Inverter'])
    expect(result[0]).toEqual({ type: 'Panel', count: 3, model: 'JA Solar JAM-450', otherModels: 1, totalKw: 1.31 })
    expect(result[1]).toMatchObject({ count: 1, totalKw: 10 })
  })

  it('returns nothing without installed devices', () => {
    expect(componentSummaries([d('Panel', 'JAM-450', 450, 'Replaced')])).toEqual([])
  })
})
