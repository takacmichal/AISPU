import { collect, validateEmail, type FieldErrors } from '../auth/validation'
import { LANDING_TEXTS } from './texts'

const E = LANDING_TEXTS.inquiry.errors
const MESSAGE_MAX_LENGTH = 2000

/** What a visitor sends from the landing page; it becomes a lead for Sales once the API accepts it. */
export interface Inquiry {
  name: string
  email: string
  phone?: string
  yearlyConsumptionKwh?: number
  address: string
  message?: string
}

export type InquiryDraft = Record<'name' | 'email' | 'phone' | 'consumption' | 'address' | 'message', string>

export const EMPTY_INQUIRY: InquiryDraft = { name: '', email: '', phone: '', consumption: '', address: '', message: '' }

/** Sends an inquiry; rejects when it could not be delivered. */
export type SendInquiry = (inquiry: Inquiry) => Promise<void>

/** "4 500" and "4500" are both fine; spaces are thousand separators. */
function parseConsumption(value: string): number | null {
  const digits = value.replace(/\s/g, '')
  return /^\d{1,7}$/.test(digits) ? Number(digits) : null
}

export function validateInquiry(draft: InquiryDraft): FieldErrors {
  const consumption = draft.consumption.trim()
  return collect({
    name: draft.name.trim() ? null : E.nameRequired,
    email: validateEmail(draft.email) ? E.emailExample : null,
    phone: draft.phone.trim() && !/^\+?[\d\s]{6,20}$/.test(draft.phone.trim()) ? E.phoneInvalid : null,
    consumption: consumption && parseConsumption(consumption) === null ? E.consumptionInvalid : null,
    address: draft.address.trim() ? null : E.addressRequired,
    message: draft.message.length > MESSAGE_MAX_LENGTH ? E.messageTooLong : null,
  })
}

/** Trimmed request body; empty optional fields are left out. Call only with a valid draft. */
export function toInquiry(draft: InquiryDraft): Inquiry {
  const optional = (value: string) => value.trim() || undefined
  return {
    name: draft.name.trim(),
    email: draft.email.trim(),
    phone: optional(draft.phone),
    yearlyConsumptionKwh: parseConsumption(draft.consumption.trim()) ?? undefined,
    address: draft.address.trim(),
    message: optional(draft.message),
  }
}
