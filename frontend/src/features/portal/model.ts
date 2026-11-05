import type { components } from '../../api/generated/schema'
import { STEP_STATES, stepIndex, type ConnectionRequest } from '../connection/model'
import { STEP_LABELS } from '../connection/texts'
import type { Step } from '../../components/Stepper'
import { formatDate } from '../../lib/format'
import type { ProductionPoint } from '../monitoring/productionApi'
import { PORTAL_TEXTS as T } from './texts'

export type Installation = components['schemas']['InstallationResponse']
export type InstallationDetail = components['schemas']['InstallationDetailResponse']
export type Device = components['schemas']['DeviceResponse']

/** The API lists connection requests newest first, so the first one per installation is the current one. */
export function latestRequestByInstallation(requests: readonly ConnectionRequest[]): Map<string, ConnectionRequest> {
  const map = new Map<string, ConnectionRequest>()
  for (const request of requests) if (!map.has(request.installationId)) map.set(request.installationId, request)
  return map
}

/** Short connection progress for a card, e.g. "krok 2 z 6: Posúdenie distribučkou". */
export function connectionSummary(request: ConnectionRequest): string {
  if (request.state === 'Concluded') return T.connectionDone
  const index = stepIndex(request.state)
  return T.connectionStep(index + 1, STEP_STATES.length, STEP_LABELS[index])
}

const AFTER_INSPECTION: readonly Installation['state'][] = ['InspectionPassed', 'Commissioned', 'HandedOver']

/**
 * Customer-facing next step. A pending action of the customer (signing the connection contract) and problems
 * of the connection request win; otherwise the installation state decides.
 */
export function nextStep(installation: Installation, request: ConnectionRequest | null | undefined): string {
  if (request) {
    if (request.state === 'ContractOffered' && request.contractSentAt) return T.next.signContract
    if (request.state === 'InfoRequested') return T.next.infoRequested
    if (request.state === 'Rejected') return T.next.rejected
    if (request.state !== 'Concluded' && AFTER_INSPECTION.includes(installation.state) && installation.state !== 'HandedOver')
      return T.next.connection(STEP_LABELS[stepIndex(request.state)])
  }
  switch (installation.state) {
    case 'Planned':
    case 'ComponentsOrdered':
    case 'ComponentsReceived':
      return installation.scheduledStart
        ? T.next.scheduled(formatDate(installation.scheduledStart), formatDate(installation.scheduledEnd))
        : T.next.waitingSchedule
    case 'Mounting':
      return T.next.mounting
    case 'MountingDone':
    case 'Inspection':
      return T.next.inspection
    case 'InspectionFailed':
      return T.next.inspectionFailed
    case 'InspectionPassed':
      return T.next.commissioning
    case 'Commissioned':
      return T.next.handOver
    case 'HandedOver':
      return T.next.done
  }
}

/** When the installation last entered `state` (from its state history), or null. */
export function reachedAt(detail: InstallationDetail, state: Installation['state']): string | null {
  const changes = detail.stateHistory.filter((c) => c.toState === state)
  return changes.length > 0 ? changes[changes.length - 1].changedAt : null
}

const OPERATING: readonly Installation['state'][] = ['Commissioned', 'HandedOver']

/** The plant produces electricity (commissioned or handed over). */
export const isOperating = (state: Installation['state']) => OPERATING.includes(state)

/** Index of the current step in PORTAL_TEXTS.steps; equal to the length when everything is done. */
const STEP_OF_STATE: Record<Installation['state'], number> = {
  Planned: 0,
  ComponentsOrdered: 0,
  ComponentsReceived: 0,
  Mounting: 1,
  MountingDone: 2,
  Inspection: 2,
  InspectionFailed: 2,
  InspectionPassed: 3,
  Commissioned: 4,
  HandedOver: 5,
}

/** Installation progress in five customer-facing steps; a failed inspection blocks the Revízia step. */
export function installationSteps(state: Installation['state']): Step[] {
  const current = STEP_OF_STATE[state]
  return T.steps.map((label, index) => ({
    label,
    status: index < current ? 'done' : index > current ? 'upcoming' : state === 'InspectionFailed' ? 'blocked' : 'current',
  }))
}

const CURRENT_POWER_MAX_AGE_MS = 15 * 60_000

/** Power of the latest 5-minute point when it is at most 15 minutes old, otherwise null (no fresh data). */
export function currentPowerKw(points: readonly ProductionPoint[], now: Date): number | null {
  const latest = [...points].reverse().find((p) => p.sampleCount > 0)
  if (!latest) return null
  const age = now.getTime() - Date.parse(latest.periodStart)
  return age >= 0 && age <= CURRENT_POWER_MAX_AGE_MS ? latest.peakPowerKw : null
}
