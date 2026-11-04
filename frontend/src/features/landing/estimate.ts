/**
 * Rough yearly production and saving for the landing page calculator. It is a marketing estimate,
 * not the designer's yield model: power × regional yield × orientation factor.
 */
export type Orientation = 'south' | 'east-west'

export interface Region {
  id: string
  name: string
  /** Average yearly yield in kWh per installed kWp. */
  yieldPerKwp: number
}

export const REGIONS: readonly Region[] = [
  { id: 'BA', name: 'Bratislavský kraj', yieldPerKwp: 1100 },
  { id: 'TT', name: 'Trnavský kraj', yieldPerKwp: 1110 },
  { id: 'NR', name: 'Nitriansky kraj', yieldPerKwp: 1130 },
  { id: 'TN', name: 'Trenčiansky kraj', yieldPerKwp: 1050 },
  { id: 'ZA', name: 'Žilinský kraj', yieldPerKwp: 1000 },
  { id: 'BB', name: 'Banskobystrický kraj', yieldPerKwp: 1070 },
  { id: 'PO', name: 'Prešovský kraj', yieldPerKwp: 1030 },
  { id: 'KE', name: 'Košický kraj', yieldPerKwp: 1080 },
]

export const DEFAULT_REGION = 'PO'
export const KWP_RANGE = { min: 3, max: 20, step: 0.2, initial: 9.6 } as const

const ORIENTATION_FACTOR: Record<Orientation, number> = { south: 1, 'east-west': 0.86 }
/** Share of the production used at home and the price of electricity it replaces. */
export const SELF_CONSUMPTION = 0.6
export const PRICE_EUR_PER_KWH = 0.2

export function findRegion(id: string): Region {
  return REGIONS.find((r) => r.id === id) ?? REGIONS.find((r) => r.id === DEFAULT_REGION)!
}

export interface YieldInput {
  kwp: number
  orientation: Orientation
  region: string
}

export function estimateYield({ kwp, orientation, region }: YieldInput): { yearlyKwh: number; savingEur: number } {
  const power = Math.min(KWP_RANGE.max, Math.max(KWP_RANGE.min, kwp))
  const yearlyKwh = power * findRegion(region).yieldPerKwp * ORIENTATION_FACTOR[orientation]
  return { yearlyKwh, savingEur: yearlyKwh * SELF_CONSUMPTION * PRICE_EUR_PER_KWH }
}
