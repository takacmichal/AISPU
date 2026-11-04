import type { components } from '../../api/generated/schema'

export type Quote = components['schemas']['QuoteResponse']
export type QuoteStatus = components['schemas']['QuoteStatus']
export type CatalogComponent = components['schemas']['ComponentResponse']

/** Slovak UI strings of the quotes feature kept in one place for review. */
export const STATUS_LABELS: Record<QuoteStatus, string> = {
  Draft: 'Rozpracovaná',
  Sent: 'Odoslaná',
  Accepted: 'Prijatá',
  Rejected: 'Zamietnutá',
  Expired: 'Expirovaná',
}

export const STATUS_BADGE: Record<QuoteStatus, string> = {
  Draft: 'badge',
  Sent: 'badge badge--info',
  Accepted: 'badge badge--success',
  Rejected: 'badge badge--error',
  Expired: 'badge',
}

export const STATUSES = Object.keys(STATUS_LABELS) as QuoteStatus[]

export const TYPE_LABELS: Record<string, string> = { Panel: 'Panel', Inverter: 'Invertor', Labor: 'Práca' }

export const UNIT_LABELS: Record<string, string> = { Panel: 'ks', Inverter: 'ks', Labor: 'hod' }

export const TEXT = {
  listTitle: 'Ponuky',
  newQuote: 'Nová ponuka',
  listError: 'Zoznam ponúk sa nepodarilo načítať.',
  listEmpty: 'Zatiaľ nie sú vytvorené žiadne ponuky.',
  listEmptyFiltered: 'Filtru nevyhovuje žiadna ponuka.',
  detailError: 'Ponuku sa nepodarilo načítať.',
  detailLoading: 'Načítavam ponuku…',
  historyError: 'Históriu stavov sa nepodarilo načítať.',
  historyEmpty: 'História stavov je prázdna.',
  linesEmpty: 'Ponuka neobsahuje žiadne položky.',
  statusFilter: 'Stav',
  customerFilter: 'Zákazník',
  all: 'Všetky',
  unknownCustomer: 'Neznámy zákazník',
  validUntil: 'Platná do',
  vat: 'DPH',
  net: 'Bez DPH',
  gross: 'Spolu s DPH',
  send: 'Odoslať ponuku',
  accept: 'Označiť ako prijatú',
  reject: 'Označiť ako zamietnutú',
  createContract: 'Pripraviť zmluvu',
  history: 'História stavov',
  created: 'Vytvorená',
  progress: 'Priebeh ponuky',
  budget: 'Rozpočet',
  sideLabel: 'Zákazník a zmluva',
  customer: 'Zákazník',
  contract: 'Zmluva',
  contractLoading: 'Načítavam zmluvu…',
  contractError: 'Zmluvu sa nepodarilo načítať.',
  contractReady: 'Ponuka je prijatá, zmluvu môžete pripraviť.',
  contractAfterAccept: 'Zmluva sa pripraví po prijatí ponuky zákazníkom.',
  contractSigned: 'Podpísaná',
  contractDraft: 'Na podpis',
  contractSignedOn: 'Podpísaná dňa',
  sendSuccess: 'Ponuka bola odoslaná.',
  acceptSuccess: 'Ponuka bola označená ako prijatá.',
  rejectSuccess: 'Ponuka bola označená ako zamietnutá.',
  createSuccess: 'Ponuka bola vytvorená.',
  transitionConflict: 'Zmenu stavu nie je možné vykonať, ponuka je už v inom stave.',
  expiredConflict: 'Platnosť ponuky vypršala, ponuku už nie je možné prijať.',
} as const

/** Server 409 details are English; map the known transition messages and keep other details as they are. */
export function conflictMessage(message: string): string {
  if (/cannot move from/i.test(message)) return TEXT.transitionConflict
  if (/has expired/i.test(message)) return TEXT.expiredConflict
  return message
}

/** Roles allowed to create quotes and change their status (QuotesModule.WriteRoles); others only read. */
export const WRITE_ROLES: readonly string[] = ['Admin', 'Sales']

export const quoteNumber = (id: string) => `#${id.slice(0, 8).toUpperCase()}`
