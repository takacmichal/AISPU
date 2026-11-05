import type { components } from '../../api/generated/schema'

type Device = Pick<components['schemas']['DeviceResponse'], 'type' | 'manufacturer' | 'model' | 'powerW' | 'status'>

export interface ComponentSummary {
  type: string
  /** Installed pieces = serial numbers on the card. */
  count: number
  /** Most common "manufacturer model". */
  model: string
  otherModels: number
  totalKw: number
}

const TYPE_ORDER = ['Panel', 'Inverter']

/** One card per device type from the installed devices (replaced ones are history, not part of the plant). */
export function componentSummaries(devices: readonly Device[]): ComponentSummary[] {
  const byType = new Map<string, Device[]>()
  for (const device of devices) {
    if (device.status !== 'Installed') continue
    byType.set(device.type, [...(byType.get(device.type) ?? []), device])
  }
  return [...byType.entries()]
    .sort(([a], [b]) => (TYPE_ORDER.indexOf(a) + 1 || 99) - (TYPE_ORDER.indexOf(b) + 1 || 99))
    .map(([type, list]) => {
      const models = new Map<string, number>()
      for (const d of list) {
        const name = `${d.manufacturer} ${d.model}`
        models.set(name, (models.get(name) ?? 0) + 1)
      }
      const [model] = [...models.entries()].sort((a, b) => b[1] - a[1])[0]
      const totalW = list.reduce((sum, d) => sum + Number(d.powerW), 0)
      return { type, count: list.length, model, otherModels: models.size - 1, totalKw: Math.round(totalW / 10) / 100 }
    })
}
