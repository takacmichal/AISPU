import type { components } from '../../api/generated/schema'
import { STATE_LABELS as INSTALLATION_LABELS } from '../installations/messages'
import { STATUS_LABELS as QUOTE_LABELS, quoteNumber } from '../quotes/quoteLabels'

type Quote = Pick<components['schemas']['QuoteResponse'], 'id' | 'status' | 'createdAt' | 'updatedAt' | 'validUntil'>
type Contract = Pick<components['schemas']['ContractResponse'], 'id' | 'number' | 'quoteId' | 'status' | 'createdAt' | 'updatedAt' | 'signedOn'>
type Installation = Pick<components['schemas']['InstallationResponse'], 'id' | 'name' | 'state' | 'createdAt' | 'updatedAt'>

/** What the customer's lists contain; a list the role cannot read is undefined. */
export interface CustomerRecords {
  quotes?: readonly Quote[]
  contracts?: readonly Contract[]
  installations?: readonly Installation[]
}

export interface NextStep {
  title: string
  detail: string
  href: string
  action: string
}

const newestFirst = <T extends { updatedAt: string }>(items: readonly T[]) =>
  [...items].sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))

/**
 * The one thing Sales should do next with the customer, derived from real states (no deadlines or owners in the
 * API yet): an unsigned contract, an accepted quote without a contract, a draft or sent quote, then installation.
 */
export function nextStep({ quotes = [], contracts = [], installations = [] }: CustomerRecords): NextStep | null {
  const draftContract = newestFirst(contracts).find((c) => c.status === 'Draft')
  if (draftContract)
    return { title: `Podpis zmluvy ${draftContract.number}`, detail: 'Zmluva je pripravená a čaká na podpis.', href: `/contracts/${draftContract.id}`, action: 'Otvoriť zmluvu' }

  const contracted = new Set(contracts.map((c) => c.quoteId))
  const accepted = newestFirst(quotes).find((q) => q.status === 'Accepted' && !contracted.has(q.id))
  if (accepted)
    return { title: `Pripraviť zmluvu k ponuke ${quoteNumber(accepted.id)}`, detail: 'Zákazník ponuku prijal.', href: `/quotes/${accepted.id}`, action: 'Otvoriť ponuku' }

  const draft = newestFirst(quotes).find((q) => q.status === 'Draft')
  if (draft)
    return { title: `Odoslať ponuku ${quoteNumber(draft.id)}`, detail: 'Ponuka je rozpracovaná.', href: `/quotes/${draft.id}`, action: 'Otvoriť ponuku' }

  const sent = newestFirst(quotes).find((q) => q.status === 'Sent')
  if (sent)
    return { title: `Odpoveď na ponuku ${quoteNumber(sent.id)}`, detail: 'Ponuka je odoslaná a čaká na rozhodnutie zákazníka.', href: `/quotes/${sent.id}`, action: 'Otvoriť ponuku' }

  const running = newestFirst(installations).find((i) => i.state !== 'HandedOver')
  if (running)
    return { title: `Inštalácia ${running.name}`, detail: `Stav: ${INSTALLATION_LABELS[running.state] ?? running.state}.`, href: `/installations/${running.id}`, action: 'Otvoriť inštaláciu' }

  return null
}

export interface ActivityEntry {
  id: string
  title: string
  at: string
  href: string
}

/** Latest changes across the customer's quotes, contracts and installations, newest first. */
export function customerActivity({ quotes = [], contracts = [], installations = [] }: CustomerRecords, limit = 6): ActivityEntry[] {
  return [
    ...quotes.map((q) => ({ id: `q-${q.id}`, title: `Ponuka ${quoteNumber(q.id)}: ${QUOTE_LABELS[q.status]}`, at: q.updatedAt, href: `/quotes/${q.id}` })),
    ...contracts.map((c) => ({
      id: `c-${c.id}`,
      title: `Zmluva ${c.number}: ${c.status === 'Signed' ? 'podpísaná' : 'pripravená'}`,
      at: c.updatedAt,
      href: `/contracts/${c.id}`,
    })),
    ...installations.map((i) => ({
      id: `i-${i.id}`,
      title: `Inštalácia ${i.name}: ${INSTALLATION_LABELS[i.state] ?? i.state}`,
      at: i.updatedAt,
      href: `/installations/${i.id}`,
    })),
  ]
    .sort((a, b) => Date.parse(b.at) - Date.parse(a.at))
    .slice(0, limit)
}
