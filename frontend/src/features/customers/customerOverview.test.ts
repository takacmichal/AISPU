import { describe, expect, it } from 'vitest'
import { customerActivity, nextStep } from './customerOverview'

const quote = (id: string, status: string, updatedAt = '2026-10-01T00:00:00Z') =>
  ({ id, status, createdAt: updatedAt, updatedAt, validUntil: null }) as never
const contract = (id: string, quoteId: string, status: string, updatedAt = '2026-10-02T00:00:00Z') =>
  ({ id, number: `ZML-${id}`, quoteId, status, createdAt: updatedAt, updatedAt, signedOn: null }) as never
const installation = (id: string, state: string, updatedAt = '2026-10-03T00:00:00Z') =>
  ({ id, name: `FVE ${id}`, state, createdAt: updatedAt, updatedAt }) as never

describe('nextStep', () => {
  it('prefers an unsigned contract', () => {
    const step = nextStep({ quotes: [quote('aaaaaaaa1', 'Draft')], contracts: [contract('1', 'x', 'Draft')] })
    expect(step).toMatchObject({ title: 'Podpis zmluvy ZML-1', href: '/contracts/1' })
  })

  it('asks for a contract when a quote was accepted without one', () => {
    const step = nextStep({ quotes: [quote('aaaaaaaa1', 'Accepted')], contracts: [] })
    expect(step?.title).toBe('Pripraviť zmluvu k ponuke #AAAAAAAA')
  })

  it('skips accepted quotes that already have a contract', () => {
    const step = nextStep({ quotes: [quote('aaaaaaaa1', 'Accepted'), quote('bbbbbbbb2', 'Sent')], contracts: [contract('1', 'aaaaaaaa1', 'Signed')] })
    expect(step?.title).toBe('Odpoveď na ponuku #BBBBBBBB')
  })

  it('falls back to a running installation and to nothing', () => {
    expect(nextStep({ installations: [installation('1', 'Mounting')] })?.title).toBe('Inštalácia FVE 1')
    expect(nextStep({ installations: [installation('1', 'HandedOver')] })).toBeNull()
    expect(nextStep({})).toBeNull()
  })
})

describe('customerActivity', () => {
  it('merges records newest first and limits the list', () => {
    const items = customerActivity(
      {
        quotes: [quote('aaaaaaaa1', 'Sent', '2026-10-01T00:00:00Z')],
        contracts: [contract('1', 'aaaaaaaa1', 'Signed', '2026-10-05T00:00:00Z')],
        installations: [installation('1', 'Mounting', '2026-10-03T00:00:00Z')],
      },
      2,
    )
    expect(items.map((i) => i.title)).toEqual(['Zmluva ZML-1: podpísaná', 'Inštalácia FVE 1: Montáž'])
  })
})
