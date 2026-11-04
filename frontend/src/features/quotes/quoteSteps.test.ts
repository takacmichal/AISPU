import { describe, expect, it } from 'vitest'
import { quoteSteps } from './quoteSteps'

const created = '2026-11-01T10:00:00Z'
const history = [
  { fromStatus: null, toStatus: 'Draft', changedBy: 'u', changedAt: created, note: null },
  { fromStatus: 'Draft', toStatus: 'Sent', changedBy: 'u', changedAt: '2026-11-02T10:00:00Z', note: null },
] as never[]
const statuses = (steps: ReturnType<typeof quoteSteps>) => steps.map((s) => s.status)

describe('quoteSteps', () => {
  it('starts with sending the draft', () => {
    expect(statuses(quoteSteps('Draft', created, [], undefined))).toEqual(['done', 'current', 'upcoming', 'upcoming'])
  })

  it('waits for the customer after sending and shows the date', () => {
    const steps = quoteSteps('Sent', created, history, undefined)
    expect(statuses(steps)).toEqual(['done', 'done', 'current', 'upcoming'])
    expect(steps[1].meta).toBe('2. 11. 2026')
  })

  it('blocks the decision when the quote was rejected or expired', () => {
    expect(quoteSteps('Rejected', created, history, null)[2]).toMatchObject({ label: 'Zamietnutá zákazníkom', status: 'blocked' })
    expect(quoteSteps('Expired', created, history, null)[2].status).toBe('blocked')
    expect(quoteSteps('Rejected', created, history, null)[3].status).toBe('upcoming')
  })

  it('follows the contract after acceptance', () => {
    expect(quoteSteps('Accepted', created, history, null)[3]).toMatchObject({ label: 'Príprava zmluvy', status: 'current' })
    expect(quoteSteps('Accepted', created, history, { status: 'Draft', signedOn: null, createdAt: created })[3].label).toBe('Zmluva na podpis')
    expect(quoteSteps('Accepted', created, history, { status: 'Signed', signedOn: '2026-11-05', createdAt: created })[3]).toMatchObject({
      status: 'done',
      meta: '5. 11. 2026',
    })
  })
})
