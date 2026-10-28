import type { components } from '../../api/generated/schema'
import type { FieldErrors } from '../../api/problem'

type SupplyPoint = components['schemas']['SupplyPointResponse']
type Request = components['schemas']['CreateSupplyPointRequest']

export interface SupplyPointFormValues {
  name: string
  eicCode: string
  street: string
  city: string
  postalCode: string
  country: string
  mainBreakerAmps: string
}

export const emptySupplyPoint: SupplyPointFormValues = {
  name: '',
  eicCode: '',
  street: '',
  city: '',
  postalCode: '',
  country: 'SK',
  mainBreakerAmps: '',
}

export function fromSupplyPoint(sp: SupplyPoint): SupplyPointFormValues {
  return {
    name: sp.name ?? '',
    eicCode: sp.eicCode,
    street: sp.address.street ?? '',
    city: sp.address.city ?? '',
    postalCode: sp.address.postalCode ?? '',
    country: sp.address.country ?? 'SK',
    mainBreakerAmps: String(sp.mainBreakerAmps),
  }
}

export function toSupplyPointRequest(v: SupplyPointFormValues): Request {
  return {
    name: v.name.trim() || null,
    eicCode: v.eicCode.trim(),
    address: { street: v.street.trim(), city: v.city.trim(), postalCode: v.postalCode.trim(), country: v.country.trim() || 'SK' },
    mainBreakerAmps: Number(v.mainBreakerAmps),
  }
}

/** Same rules as the server (SupplyPointRequest.Validate). */
export function validateSupplyPoint(v: SupplyPointFormValues): FieldErrors {
  const errors: FieldErrors = {}
  const add = (field: string, message: string) => (errors[field] ??= []).push(message)

  const eic = v.eicCode.replace(/\s+/g, '').toUpperCase()
  if (eic === '') add('eicCode', 'Vyplňte EIC kód.')
  else if (!/^\d{2}[A-Z][A-Z0-9-]{12}[A-Z0-9]$/.test(eic)) {
    add('eicCode', 'EIC musí mať 16 znakov: 2 číslice, písmeno, 12 znakov a kontrolný znak.')
  }

  const amps = Number(v.mainBreakerAmps)
  if (v.mainBreakerAmps.trim() === '') add('mainBreakerAmps', 'Vyplňte hodnotu hlavného ističa.')
  else if (!Number.isInteger(amps) || amps < 1 || amps > 1000) add('mainBreakerAmps', 'Hlavný istič musí byť celé číslo od 1 do 1000 A.')

  if (v.name.length > 100) add('name', 'Maximálna dĺžka je 100 znakov.')
  if (v.street.trim() === '') add('address.street', 'Vyplňte toto pole.')
  if (v.city.trim() === '') add('address.city', 'Vyplňte toto pole.')
  if (v.postalCode.trim() === '') add('address.postalCode', 'Vyplňte toto pole.')
  else if ((v.country.trim().toUpperCase() || 'SK') === 'SK' && !/^\d{3} ?\d{2}$/.test(v.postalCode.trim())) {
    add('address.postalCode', 'PSČ musí mať 5 číslic, napr. 811 01.')
  }
  if (!/^[A-Za-z]{2}$/.test(v.country.trim() || 'SK')) add('address.country', 'Krajina musí byť 2-písmenový kód, napr. SK.')
  return errors
}
