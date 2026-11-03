import { AUTH_TEXTS } from './texts'

const V = AUTH_TEXTS.validation

/** Mirrors the ASP.NET Identity password policy of the API (AuthModule.ConfigureIdentity). */
export const PASSWORD_POLICY = {
  minLength: 10,
  maxLength: 128,
  requiredUniqueChars: 4,
} as const

const EMAIL_MAX_LENGTH = 256
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+$/

export type FieldErrors = Record<string, string[]>

export function validateEmail(value: string): string | null {
  const email = value.trim()
  if (!email) return V.emailRequired
  if (email.length > EMAIL_MAX_LENGTH) return V.emailTooLong
  if (!EMAIL_PATTERN.test(email)) return V.emailInvalid
  return null
}

/** Names of the policy rules the password breaks (empty = valid). */
export function passwordProblems(value: string): string[] {
  const problems: string[] = []
  if (value.length < PASSWORD_POLICY.minLength) problems.push('length')
  if (!/[a-z]/.test(value)) problems.push('lowercase')
  if (!/[A-Z]/.test(value)) problems.push('uppercase')
  if (!/[0-9]/.test(value)) problems.push('digit')
  if (new Set(value).size < PASSWORD_POLICY.requiredUniqueChars) problems.push('unique')
  return problems
}

export function validatePassword(value: string): string | null {
  if (!value) return V.passwordRequired
  if (value.length > PASSWORD_POLICY.maxLength) return V.passwordTooLong
  if (passwordProblems(value).length > 0) return `${V.passwordWeak} ${AUTH_TEXTS.passwordRules}`
  return null
}

export function validateConfirmation(password: string, confirmation: string): string | null {
  if (!confirmation) return V.confirmRequired
  if (password !== confirmation) return V.confirmMismatch
  return null
}

/** Copy of the error map without one field (returns the same object when the field has no error). */
export function withoutField(errors: FieldErrors, field: string): FieldErrors {
  if (!(field in errors)) return errors
  const rest = { ...errors }
  delete rest[field]
  return rest
}

/** Collects non-null messages into a field error map. */
export function collect(entries: Record<string, string | null>): FieldErrors {
  const errors: FieldErrors = {}
  for (const [field, message] of Object.entries(entries)) {
    if (message) errors[field] = [message]
  }
  return errors
}
