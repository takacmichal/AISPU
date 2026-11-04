import { api } from '../api/client'
import { useLoad } from '../components/useLoad'
import { MOCK_NAV_COUNTS } from '../features/dashboard/mock'

let cached: Record<string, number> = {}

/**
 * Counters shown next to sidebar items; sample numbers when the API has nothing to count yet.
 * Loaded only while `enabled` (on the dashboard); on other pages the last values are reused without new requests. */
export function useNavCounts(enabled: boolean): Record<string, number> {
  const { data } = useLoad<Record<string, number>>(async (signal) => {
    if (!enabled) return cached
    try {
      const [customers, quotes] = await Promise.all([
        api.GET('/api/customers', { params: { query: { page: 1, pageSize: 1 } }, signal }),
        api.GET('/api/quotes', { signal }),
      ])
      const customerCount = Number(customers.data?.totalCount ?? 0)
      const openQuotes = (quotes.data ?? []).filter((q) => q.status === 'Draft' || q.status === 'Sent').length
      return (cached = {
        '/customers': customerCount || MOCK_NAV_COUNTS['/customers'],
        '/quotes': openQuotes || MOCK_NAV_COUNTS['/quotes'],
        '/tickets': MOCK_NAV_COUNTS['/tickets'],
      })
    } catch (error) {
      if (signal.aborted) throw error
      return (cached = MOCK_NAV_COUNTS)
    }
  }, 'nav-counts' + (enabled ? '|live' : '|cached'))
  return data ?? {}
}
