import { describe, expect, it } from 'vitest'
import { deadlineInfo, nextAction, stepStatuses, timelineEvents, type ConnectionRequest } from './model'

const request = (over: Partial<ConnectionRequest> = {}): ConnectionRequest =>
  ({
    id: 'r1',
    installationId: 'i1',
    customerId: 'c1',
    supplyPointId: 's1',
    eicCode: '24ZZS00000000001',
    ratedAcPowerKw: 8,
    mainBreakerAmps: 25,
    type: 'SmallSource',
    state: 'Draft',
    attachments: [],
    createdAt: '2026-01-02T00:00:00Z',
    updatedAt: '2026-01-02T00:00:00Z',
    ...over,
  }) as ConnectionRequest

describe('stepStatuses', () => {
  it('marks the current step and the finished ones', () => {
    expect(stepStatuses('ContractOffered')).toEqual(['done', 'done', 'current', 'upcoming', 'upcoming', 'upcoming'])
  })
  it('keeps InfoRequested on the assessment step and blocks it when rejected', () => {
    expect(stepStatuses('InfoRequested')[1]).toBe('current')
    expect(stepStatuses('Rejected')[1]).toBe('blocked')
  })
  it('marks everything done when concluded', () => {
    expect(stepStatuses('Concluded').every((s) => s === 'done')).toBe(true)
  })
})

describe('deadlineInfo', () => {
  const today = new Date(2026, 0, 10)
  it('is empty outside the Submitted state', () => {
    expect(deadlineInfo(request({ state: 'Draft', proposalDueDate: '2026-01-12' }), today)).toBeNull()
  })
  it('warns three days or fewer before the deadline', () => {
    expect(deadlineInfo(request({ state: 'Submitted', proposalDueDate: '2026-01-12' }), today)).toMatchObject({ level: 'warning', days: 2 })
    expect(deadlineInfo(request({ state: 'Submitted', proposalDueDate: '2026-01-20' }), today)).toMatchObject({ level: 'ok', days: 10 })
  })
  it('reports a missed deadline from the server flag', () => {
    expect(deadlineInfo(request({ state: 'Submitted', proposalRemainingDays: -4, proposalOverdue: true }), today)).toMatchObject({
      level: 'overdue',
      days: 4,
    })
  })
})

describe('timelineEvents and nextAction', () => {
  it('orders events by time', () => {
    const events = timelineEvents(request({ submittedAt: '2026-01-03T00:00:00Z' }), {
      created: 'a',
      submitted: 'b',
      decided: 'c',
      conditionsReviewed: 'd',
      contractSent: 'e',
    })
    expect(events.map((e) => e.key)).toEqual(['created', 'submitted'])
  })
  it('walks through the contract offer sub-steps', () => {
    expect(nextAction(request({ state: 'ContractOffered' }))?.action).toBe('review-conditions')
    expect(nextAction(request({ state: 'ContractOffered', conditionsReviewedAt: 'x' }))?.action).toBe('send-contract')
    expect(nextAction(request({ state: 'ContractOffered', conditionsReviewedAt: 'x', contractSentAt: 'y' }))?.action).toBe('customer-signed')
    expect(nextAction(request({ state: 'Submitted' }))).toBeNull()
  })
})
