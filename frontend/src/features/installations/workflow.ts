import type { components } from '../../api/generated/schema'
import type { Step } from '../../components/Stepper'
import { formatDate } from '../../lib/format'
import { STATE_LABELS, STATES, type InstallationState } from './messages'

type StateChange = components['schemas']['InstallationStateChangeResponse']

interface Phase {
  label: string
  /** Backend states that belong to the phase (used for the date the phase was reached). */
  states: readonly InstallationState[]
}

/** The 6 steps of the wireframe workflow, built from the 10 backend states. */
export const PHASES: readonly Phase[] = [
  { label: 'Naplánovaná', states: ['Planned', 'ComponentsOrdered', 'ComponentsReceived'] },
  { label: 'Montáž', states: ['Mounting', 'MountingDone'] },
  { label: 'Revízia', states: ['Inspection', 'InspectionFailed'] },
  { label: 'Pripojenie DS', states: ['InspectionPassed'] },
  { label: 'Uvedenie do prevádzky', states: ['Commissioned'] },
  { label: 'Odovzdaná', states: ['HandedOver'] },
]

/**
 * Index of the step being worked on. A passed inspection waits for the grid connection, a commissioned plant
 * waits for the handover, and a handed over plant has every step done (index past the last step).
 */
const CURRENT_STEP: Record<InstallationState, number> = {
  Planned: 0,
  ComponentsOrdered: 0,
  ComponentsReceived: 0,
  Mounting: 1,
  MountingDone: 1,
  Inspection: 2,
  InspectionFailed: 2,
  InspectionPassed: 3,
  Commissioned: 5,
  HandedOver: 6,
}

function reachedOn(history: readonly StateChange[], states: readonly InstallationState[]): string | undefined {
  const first = history.find((change) => states.includes(change.toState))
  return first ? formatDate(first.changedAt) : undefined
}

/** Steps for the Stepper; the current step shows the exact backend state when it differs from the phase name. */
export function workflowSteps(state: InstallationState, history: readonly StateChange[]): Step[] {
  const current = CURRENT_STEP[state]
  return PHASES.map((phase, index) => {
    const date = reachedOn(history, phase.states)
    if (index < current) return { label: phase.label, status: 'done', meta: date }
    if (index > current) return { label: phase.label, status: 'upcoming' }
    const detail = STATE_LABELS[state] !== phase.label && phase.states.includes(state) ? STATE_LABELS[state] : undefined
    return {
      label: phase.label,
      status: state === 'InspectionFailed' ? 'blocked' : 'current',
      meta: [detail, date].filter(Boolean).join(' · ') || undefined,
    }
  })
}

/** Groups of the list tabs (wireframe: Všetky, V realizácii, Čaká na DS, V prevádzke). */
export const LIST_GROUPS = ['realization', 'connection', 'operation'] as const
export type ListGroup = (typeof LIST_GROUPS)[number]

export function listGroup(state: InstallationState): ListGroup {
  if (state === 'InspectionPassed') return 'connection'
  if (state === 'Commissioned' || state === 'HandedOver') return 'operation'
  return 'realization'
}

/**
 * Targets that the backend sets only through their own step with extra data (revision report, acceptance
 * protocol); the plain transition endpoint answers 409 for them. Mirrors `Installation.DedicatedSteps`.
 */
export const DEDICATED_TARGETS: readonly InstallationState[] = ['InspectionPassed', 'InspectionFailed', 'Commissioned']

export interface NextAction {
  target: InstallationState
  /** A move back in the flow (rework after a failed inspection). */
  rework: boolean
}

/** The one transition offered as the main action, or null when every allowed move needs a dedicated step. */
export function nextAction(state: InstallationState, allowed: readonly InstallationState[]): NextAction | null {
  const target = allowed.find((t) => !DEDICATED_TARGETS.includes(t))
  if (!target) return null
  return { target, rework: STATES.indexOf(target) < STATES.indexOf(state) }
}
