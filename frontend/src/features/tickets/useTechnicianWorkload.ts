import { useLoad } from '../../components/useLoad'
import { getTechnicianVisits, type Technician } from './visitsApi'

export const WORKLOAD_DAYS = 7

/**
 * Active visits of each technician in the next days, shown when choosing a technician. It is only a hint for the planner:
 * a failed request leaves the technician without a number and never blocks the assignment.
 */
export function useTechnicianWorkload(technicians: readonly Technician[] | null): ReadonlyMap<string, number> {
  const ids = (technicians ?? []).map((t) => t.id)
  const { data } = useLoad(async (signal) => {
    const from = new Date()
    const to = new Date(from.getTime() + WORKLOAD_DAYS * 24 * 60 * 60 * 1000)
    const counts = await Promise.all(
      ids.map(async (id): Promise<[string, number] | null> => {
        try {
          const { visits } = await getTechnicianVisits(id, from, to, signal)
          return [id, visits.filter((v) => v.state === 'Planned' || v.state === 'InProgress').length]
        } catch {
          return null
        }
      }),
    )
    return new Map(counts.filter((entry) => entry !== null))
  }, `technician-workload|${ids.join(',')}`)
  return data ?? new Map<string, number>()
}
