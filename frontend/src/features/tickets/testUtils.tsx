export { json, problem, renderRoute, stubFetch } from '../installations/testHelpers'

export const ticketItem = (over: Record<string, unknown> = {}) => ({
  id: 't1',
  installationId: 'i1',
  customerId: 'c1',
  title: 'Invertor nesvieti',
  priority: 'Medium',
  origin: 'CustomerReport',
  state: 'New',
  createdAt: '2026-03-01T08:00:00Z',
  updatedAt: '2026-03-01T08:00:00Z',
  ...over,
})

export const ticketList = (items: unknown[], totalCount = items.length) => ({ items, page: 1, pageSize: 20, totalCount })

export const ticketDetail = (over: Record<string, unknown> = {}) => ({
  ...ticketItem(),
  description: 'Od rána nesvieti kontrolka.',
  allowedTransitions: ['Triaged'],
  attachments: [],
  createdBy: 'u1',
  ...over,
})
