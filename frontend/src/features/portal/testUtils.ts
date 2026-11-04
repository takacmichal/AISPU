/** Fixture of a connection request as returned by GET /api/connection-requests. */
export const connectionRequest = (over: Record<string, unknown> = {}) => ({
  id: 'r1',
  installationId: 'i1',
  customerId: 'c1',
  supplyPointId: 's1',
  eicCode: '24ZSS1234567890A',
  ratedAcPowerKw: 8,
  mainBreakerAmps: 25,
  type: 'SmallSource',
  state: 'Submitted',
  attachments: [],
  createdAt: '2026-03-01T08:00:00Z',
  updatedAt: '2026-03-02T08:00:00Z',
  ...over,
})
