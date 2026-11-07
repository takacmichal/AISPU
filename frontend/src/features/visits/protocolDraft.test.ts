import { beforeEach, describe, expect, it } from 'vitest'
import { clearProtocolDraft, readProtocolDraft, writeProtocolDraft, type ProtocolDraft } from './protocolDraft'

const draft: ProtocolDraft = {
  values: { cause: 'InverterFault', arrivedAt: '2026-11-05T10:30', departedAt: '', findings: 'Chyba', workDone: '' },
  waiting: false,
}

describe('protocol draft storage', () => {
  beforeEach(() => localStorage.clear())

  it('keeps a draft for each visit separately', () => {
    writeProtocolDraft('v1', draft)

    expect(readProtocolDraft('v1')).toEqual(draft)
    expect(readProtocolDraft('v2')).toBeNull()
  })

  it('removes a cleared draft', () => {
    writeProtocolDraft('v1', draft)

    clearProtocolDraft('v1')

    expect(readProtocolDraft('v1')).toBeNull()
  })

  it.each(['not json', '42', '{"waiting":false}', '{"values":{"cause":"x"},"waiting":false}', '{"values":{},"waiting":"no"}'])(
    'treats the corrupt value %s as no draft',
    (stored) => {
      localStorage.setItem('solarops.visit-protocol.v1', stored)

      expect(readProtocolDraft('v1')).toBeNull()
    },
  )
})
