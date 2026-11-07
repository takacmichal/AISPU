/** What the technician typed into the service protocol; a field can be empty while the form is unfinished. */
export interface ProtocolValues {
  cause: string
  arrivedAt: string
  departedAt: string
  findings: string
  workDone: string
}

/** A saved protocol; `waiting` means the technician already submitted it and it goes out when the connection returns. */
export interface ProtocolDraft {
  values: ProtocolValues
  waiting: boolean
}

const FIELDS = ['cause', 'arrivedAt', 'departedAt', 'findings', 'workDone'] as const

const storageKey = (visitId: string) => `solarops.visit-protocol.${visitId}`

function isDraft(value: unknown): value is ProtocolDraft {
  if (typeof value !== 'object' || value === null) return false
  const { values, waiting } = value as Partial<ProtocolDraft>
  if (typeof waiting !== 'boolean' || typeof values !== 'object' || values === null) return false
  return FIELDS.every((field) => typeof values[field] === 'string')
}

/** Reads the draft of a visit; a missing, corrupt or unreadable value counts as no draft. */
export function readProtocolDraft(visitId: string): ProtocolDraft | null {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey(visitId)) ?? 'null')
    return isDraft(parsed) ? parsed : null
  } catch {
    return null
  }
}

/** Saves the draft. With storage disabled or full the form still works, it just cannot be restored later. */
export function writeProtocolDraft(visitId: string, draft: ProtocolDraft): void {
  try {
    localStorage.setItem(storageKey(visitId), JSON.stringify(draft))
  } catch {
    // Nothing to do: the values stay in the open form.
  }
}

export function clearProtocolDraft(visitId: string): void {
  try {
    localStorage.removeItem(storageKey(visitId))
  } catch {
    // Storage disabled: there is nothing to clear either.
  }
}
