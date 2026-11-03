import { api } from '../../api/client'
import { useLoad } from '../../components/useLoad'

export interface CustomerOption {
  id: string
  name: string
  active: boolean
}

/** Loads up to 100 customers for pickers and name lookup; roles without customer access (403) get an empty list. */
export function useCustomerOptions() {
  const { data, loading } = useLoad<CustomerOption[]>(async (signal) => {
    try {
      const { data } = await api.GET('/api/customers', {
        params: { query: { page: 1, pageSize: 100, sortBy: 'name', sortDir: 'asc' } },
        signal,
      })
      return (data?.items ?? []).map((c) => ({ id: c.id, name: c.displayName, active: c.status === 'Active' }))
    } catch {
      return []
    }
  }, 'customers')
  return { customers: data ?? [], loading }
}
