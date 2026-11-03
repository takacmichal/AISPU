import type { components } from '../../api/generated/schema'
import type { FieldErrors } from '../../api/problem'

export type CatalogComponent = components['schemas']['ComponentResponse']
export type ComponentType = components['schemas']['ComponentType']
type Request = components['schemas']['CreateComponentRequest']

export const COMPONENT_TYPES: readonly ComponentType[] = ['Panel', 'Inverter', 'Labor']

export const TYPE_LABELS: Record<ComponentType, string> = {
  Panel: 'Panel',
  Inverter: 'Invertor',
  Labor: 'Práca',
}

export interface ComponentFormValues {
  type: ComponentType
  code: string
  name: string
  manufacturer: string
  powerW: string
  unitPrice: string
}

export const emptyComponent: ComponentFormValues = { type: 'Panel', code: '', name: '', manufacturer: '', powerW: '', unitPrice: '' }

export function fromComponent(c: CatalogComponent): ComponentFormValues {
  return {
    type: c.type,
    code: c.code,
    name: c.name,
    manufacturer: c.manufacturer ?? '',
    powerW: c.powerW === null || c.powerW === undefined ? '' : String(c.powerW),
    unitPrice: String(c.unitPrice).replace('.', ','),
  }
}

/** Accepts "1 234,50" and "1234.5"; returns NaN for anything that is not a plain number. */
export function parsePrice(text: string): number {
  const normalized = text.replace(/\s+/g, '').replace(',', '.')
  return normalized === '' || !/^\d+(\.\d+)?$/.test(normalized) ? Number.NaN : Number(normalized)
}

export function toComponentRequest(v: ComponentFormValues): Request {
  const hasPower = v.type !== 'Labor'
  return {
    type: v.type,
    code: v.code.trim(),
    name: v.name.trim(),
    manufacturer: v.manufacturer.trim() || null,
    powerW: hasPower ? Number(v.powerW) : null,
    unitPrice: parsePrice(v.unitPrice),
  }
}

/** Same rules as the server (ComponentDetails.Validate). */
export function validateComponent(v: ComponentFormValues): FieldErrors {
  const errors: FieldErrors = {}
  const add = (field: string, message: string) => (errors[field] ??= []).push(message)

  if (v.code.trim() === '') add('code', 'Vyplňte kód.')
  else if (v.code.trim().length > 50) add('code', 'Kód môže mať najviac 50 znakov.')
  if (v.name.trim() === '') add('name', 'Vyplňte názov.')
  else if (v.name.trim().length > 200) add('name', 'Názov môže mať najviac 200 znakov.')
  if (v.manufacturer.trim().length > 100) add('manufacturer', 'Výrobca môže mať najviac 100 znakov.')

  if (v.type !== 'Labor') {
    const power = Number(v.powerW)
    if (v.powerW.trim() === '' || !Number.isInteger(power) || power <= 0) add('powerW', 'Zadajte výkon vo wattoch (celé číslo väčšie ako 0).')
  }

  const price = parsePrice(v.unitPrice)
  if (v.unitPrice.trim() === '') add('unitPrice', 'Vyplňte cenu.')
  else if (Number.isNaN(price)) add('unitPrice', 'Cena musí byť číslo, napr. 129,90.')
  else if (price > 1_000_000) add('unitPrice', 'Cena môže byť najviac 1 000 000 €.')
  else if (Math.round(price * 100) / 100 !== price) add('unitPrice', 'Cena môže mať najviac dve desatinné miesta.')
  return errors
}
