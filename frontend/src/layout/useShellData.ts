import { api } from '../api/client'
import { useLoad } from '../components/useLoad'
import { CLOSED } from '../features/dashboard/alarms'
import { listTickets } from '../features/tickets/ticketsApi'

let cached: Record<string, number> = {}

/**
 * Counters shown next to sidebar items: customers, open quotes and open tickets from the API. A count whose request
 * fails is left out, so the sidebar never shows a made-up number.
 * Loaded only while `enabled` (on the dashboard); on other pages the last values are reused without new requests. */
export function useNavCounts(enabled: boolean): Record<string, number> {
  const { data } = useLoad<Record<string, number>>(async (signal) => {
    if (!enabled) return cached
    const [customers, quotes, tickets] = await Promise.allSettled([
      api.GET('/api/customers', { params: { query: { page: 1, pageSize: 1 } }, signal }).then(({ data }) => data ?? Promise.reject()),
      api.GET('/api/quotes', { signal }).then(({ data }) => data ?? Promise.reject()),
      listTickets({ pageSize: 100 }, signal),
    ])
    if (signal.aborted) throw new DOMException('Aborted', 'AbortError')
    const counts: Record<string, number> = {}
    if (customers.status === 'fulfilled') counts['/customers'] = Number(customers.value.totalCount)
    if (quotes.status === 'fulfilled') counts['/quotes'] = quotes.value.filter((q) => q.status === 'Draft' || q.status === 'Sent').length
    if (tickets.status === 'fulfilled') counts['/tickets'] = tickets.value.items.filter((t) => !CLOSED.has(t.state)).length
    return (cached = counts)
  }, 'nav-counts' + (enabled ? '|live' : '|cached'))
  return data ?? {}
}
