import type { components } from '../../api/generated/schema'
import type { FieldErrors } from '../../api/problem'
import type { CatalogComponent } from './quoteLabels'

export const MAX_LINES = 200
export const MAX_QUANTITY = 10_000
export const DEFAULT_VAT = 23
/** DC/AC ratio outside this range is flagged as a likely mismatch (a hint only, the backend does not check it). */
export const DC_AC_RANGE = [0.8, 1.5] as const

export interface DraftLine {
  /** Local key so rows keep identity while editing. */
  key: string
  component: CatalogComponent
  /** Raw input; accepts a decimal comma. */
  quantity: string
}

export function parseQuantity(text: string): number {
  const normalized = text.trim().replace(',', '.')
  return normalized === '' ? NaN : Number(normalized)
}

const round2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100

export function lineNet(line: DraftLine): number {
  const quantity = parseQuantity(line.quantity)
  return Number.isFinite(quantity) ? round2(Number(line.component.unitPrice) * quantity) : 0
}

/** Mirrors QuotePricing on the server: line nets are rounded, VAT is rounded from the net sum. */
export function calculateTotals(lines: readonly DraftLine[], vatRate: number) {
  const net = round2(lines.reduce((sum, line) => sum + lineNet(line), 0))
  const vat = round2((net * (Number.isFinite(vatRate) ? vatRate : 0)) / 100)
  return { net, vat, gross: round2(net + vat) }
}

export interface DraftValues {
  customerId: string
  vatRate: string
  note: string
  lines: readonly DraftLine[]
}

export function validateDraft(values: DraftValues): FieldErrors {
  const errors: FieldErrors = {}
  if (!values.customerId) errors.customerId = ['Vyberte zákazníka.']

  const vat = parseQuantity(values.vatRate)
  if (!Number.isFinite(vat) || vat < 0 || vat > 100) errors.vatRatePercent = ['Sadzba DPH musí byť medzi 0 a 100 %.']

  if (values.lines.length === 0) errors.lines = ['Pridajte aspoň jednú položku.']
  else {
    const types = new Set(values.lines.map((l) => l.component.type))
    const missing = [!types.has('Panel') && 'panel', !types.has('Inverter') && 'invertor'].filter(Boolean)
    if (missing.length > 0) errors.lines = [`Ponuka musí obsahovať aspoň jeden ${missing.join(' a jeden ')}.`]
  }

  values.lines.forEach((line, index) => {
    const quantity = parseQuantity(line.quantity)
    const key = `lines[${index}].quantity`
    if (!Number.isFinite(quantity) || quantity <= 0 || quantity > MAX_QUANTITY)
      errors[key] = [`Množstvo musí byť väčšie ako 0 a najviac ${MAX_QUANTITY.toLocaleString('sk-SK')}.`]
    else if (line.component.type !== 'Labor' && !Number.isInteger(quantity)) errors[key] = ['Počet kusov musí byť celé číslo.']
    else if (Math.round(quantity * 100) / 100 !== quantity) errors[key] = ['Množstvo môže mať najviac dve desatinné miesta.']
  })
  return errors
}

/** Non-blocking hints, e.g. panel array power versus inverter power. */
export function compatibilityWarnings(lines: readonly DraftLine[]): string[] {
  let panelW = 0
  let inverterW = 0
  for (const line of lines) {
    const quantity = parseQuantity(line.quantity)
    const power = Number(line.component.powerW ?? 0)
    if (!Number.isFinite(quantity) || !power) continue
    if (line.component.type === 'Panel') panelW += power * quantity
    if (line.component.type === 'Inverter') inverterW += power * quantity
  }
  if (panelW === 0 || inverterW === 0) return []
  const ratio = panelW / inverterW
  const [min, max] = DC_AC_RANGE
  if (ratio > max) return ['Výkon panelov výrazne prevyšuje výkon invertora, zvážte silnejší invertor.']
  if (ratio < min) return ['Invertor je oproti výkonu panelov naddimenzovaný, zvážte slabší invertor.']
  return []
}

export function toRequest(values: DraftValues): components['schemas']['CreateQuoteRequest'] {
  return {
    customerId: values.customerId,
    vatRatePercent: parseQuantity(values.vatRate),
    note: values.note.trim() || null,
    lines: values.lines.map((l) => ({ componentId: l.component.id, quantity: parseQuantity(l.quantity) })),
  }
}
