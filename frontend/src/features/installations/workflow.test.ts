import { describe, expect, it } from 'vitest'
import { listGroup, nextAction, workflowSteps } from './workflow'

const change = (toState: string, changedAt: string) => ({ fromState: null, toState, changedBy: 'u1', changedAt, note: null }) as never

describe('workflowSteps', () => {
  const history = [change('Planned', '2026-10-15T08:00:00Z'), change('ComponentsOrdered', '2026-10-20T08:00:00Z'), change('Mounting', '2026-11-04T08:00:00Z')]

  it('maps 10 backend states to 6 steps with the exact state on the current step', () => {
    const steps = workflowSteps('ComponentsOrdered', history.slice(0, 2))
    expect(steps).toHaveLength(6)
    expect(steps[0]).toMatchObject({ label: 'Naplánovaná', status: 'current', meta: 'Komponenty objednané · 15. 10. 2026' })
    expect(steps.slice(1).every((s) => s.status === 'upcoming')).toBe(true)
  })

  it('marks earlier steps done with the date the phase was reached', () => {
    const steps = workflowSteps('Mounting', history)
    expect(steps[0]).toMatchObject({ status: 'done', meta: '15. 10. 2026' })
    expect(steps[1]).toMatchObject({ label: 'Montáž', status: 'current', meta: '4. 11. 2026' })
  })

  it('blocks the inspection step after a failed inspection', () => {
    expect(workflowSteps('InspectionFailed', [])[2]).toMatchObject({ label: 'Revízia', status: 'blocked', meta: 'Revízia nevyhovela' })
  })

  it('waits for the grid connection after a passed inspection and for the handover after commissioning', () => {
    expect(workflowSteps('InspectionPassed', []).map((s) => s.status)).toEqual(['done', 'done', 'done', 'current', 'upcoming', 'upcoming'])
    expect(workflowSteps('Commissioned', []).map((s) => s.status)).toEqual(['done', 'done', 'done', 'done', 'done', 'current'])
    expect(workflowSteps('HandedOver', []).every((s) => s.status === 'done')).toBe(true)
  })
})

describe('listGroup', () => {
  it('splits states into realization, waiting for the grid and operation', () => {
    expect(listGroup('Planned')).toBe('realization')
    expect(listGroup('InspectionFailed')).toBe('realization')
    expect(listGroup('InspectionPassed')).toBe('connection')
    expect(listGroup('Commissioned')).toBe('operation')
    expect(listGroup('HandedOver')).toBe('operation')
  })
})

describe('nextAction', () => {
  it('offers the next plain transition', () => {
    expect(nextAction('Planned', ['ComponentsOrdered'])).toEqual({ target: 'ComponentsOrdered', rework: false })
  })

  it('skips targets that need a revision report or acceptance protocol', () => {
    expect(nextAction('Inspection', ['InspectionPassed', 'InspectionFailed'])).toBeNull()
    expect(nextAction('InspectionPassed', ['Commissioned'])).toBeNull()
  })

  it('marks the return to mounting after a failed inspection as rework', () => {
    expect(nextAction('InspectionFailed', ['Mounting'])).toEqual({ target: 'Mounting', rework: true })
  })
})
