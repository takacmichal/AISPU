import type { components } from '../../api/generated/schema'
import type { Step } from '../../components/Stepper'
import { formatDate } from '../../lib/format'
import type { QuoteStatus } from './quoteLabels'

type StateChange = components['schemas']['QuoteStateChangeResponse']
type Contract = Pick<components['schemas']['ContractResponse'], 'status' | 'signedOn' | 'createdAt'>

const reachedOn = (history: readonly StateChange[], status: QuoteStatus) => {
  const change = [...history].reverse().find((c) => c.toStatus === status)
  return change ? formatDate(change.changedAt) : undefined
}

/**
 * "Priebeh ponuky" from the real quote states (no internal approval: the API has none):
 * Vytvorená → Odoslaná → Rozhodnutie zákazníka → Zmluva. A rejected or expired quote blocks the decision step.
 * `contract` is undefined when the role cannot see contracts and null when there is none yet.
 */
export function quoteSteps(status: QuoteStatus, createdAt: string, history: readonly StateChange[], contract: Contract | null | undefined): Step[] {
  const sent = status !== 'Draft'
  const decision: Step =
    status === 'Accepted'
      ? { label: 'Prijatá zákazníkom', status: 'done', meta: reachedOn(history, 'Accepted') }
      : status === 'Rejected'
        ? { label: 'Zamietnutá zákazníkom', status: 'blocked', meta: reachedOn(history, 'Rejected') }
        : status === 'Expired'
          ? { label: 'Platnosť vypršala', status: 'blocked', meta: reachedOn(history, 'Expired') }
          : { label: 'Rozhodnutie zákazníka', status: sent ? 'current' : 'upcoming' }

  let contractStep: Step = { label: 'Zmluva', status: 'upcoming' }
  if (status === 'Accepted') {
    if (contract?.status === 'Signed') contractStep = { label: 'Zmluva podpísaná', status: 'done', meta: contract.signedOn ? formatDate(contract.signedOn) : undefined }
    else if (contract) contractStep = { label: 'Zmluva na podpis', status: 'current', meta: formatDate(contract.createdAt) }
    else contractStep = { label: 'Príprava zmluvy', status: 'current' }
  }

  return [
    { label: 'Vytvorená', status: 'done', meta: formatDate(createdAt) },
    { label: 'Odoslaná zákazníkovi', status: sent ? 'done' : 'current', meta: reachedOn(history, 'Sent') },
    decision,
    contractStep,
  ]
}
