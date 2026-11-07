import { api } from '../../api/client'
import { ALARMS_POLL_MS, DASHBOARD_POLL_MS, useLoad } from '../../components/useLoad'
import { fetchOngoingAlarms, type Alarm } from '../monitoring/alarmsApi'
import { addDays, toDateString } from '../monitoring/periods'
import { listTickets, type TicketListItem } from '../tickets/ticketsApi'
import { CLOSED } from './alarms'

export interface TodayProduction {
  todayKwh: number
  yesterdayKwh: number
  /** Plants in operation (Commissioned or HandedOver). */
  operating: number
  asOf: string
}

const hhmm = (d: Date) => `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`

let cachedToday: TodayProduction | null = null

/**
 * Today's and yesterday's production of the plants in operation. A failure is an error of the load (the card shows it),
 * never sample data. With `live` false (sidebar card on other pages) the last loaded value is reused without a request.
 */
export function useTodayProduction(live = true, enabled = true) {
  return useLoad<TodayProduction | null>(
    async (signal) => {
      if (!enabled) return null
      if (!live) return cachedToday
      const result = await loadToday(signal)
      cachedToday = result
      return result
    },
    `dashboard-today|${live}|${enabled}`,
    { intervalMs: DASHBOARD_POLL_MS, enabled: live && enabled },
  )
}

async function loadToday(signal: AbortSignal): Promise<TodayProduction> {
  const now = new Date()
  const [production, installations] = await Promise.all([
    api.GET('/api/production/summary', { params: { query: { From: addDays(toDateString(now), -1), To: addDays(toDateString(now), 1), Granularity: 'day' } }, signal }),
    api.GET('/api/installations/paged', { params: { query: { page: 1, pageSize: 1 } }, signal }),
  ])
  if (!production.data) throw new Error(`HTTP ${production.response.status}`)
  if (!installations.data) throw new Error(`HTTP ${installations.response.status}`)
  const day = toDateString(now)
  // periodStart is a UTC instant (the local day starts at 23:00Z), so the local calendar day is taken in the plant time zone.
  const localDay = (iso: string) => new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Bratislava' }).format(new Date(iso))
  const sumFor = (key: string) => production.data.points.filter((p) => localDay(p.periodStart) === key).reduce((sum, p) => sum + Number(p.energyKwh), 0)
  const counts = installations.data.statusCounts
  const operating = Number(counts.Commissioned ?? 0) + Number(counts.HandedOver ?? 0)
  return { todayKwh: sumFor(day), yesterdayKwh: sumFor(addDays(day, -1)), operating, asOf: hhmm(now) }
}

export interface TicketItems { items: TicketListItem[] }

/** Open service tickets; the alarm KPI counts only those raised by anomaly detection. */
export function useTicketSummary(enabled = true) {
  return useLoad<TicketItems | null>(
    async (signal) => {
      if (!enabled) return null
      const list = await listTickets({ page: 1, pageSize: 10 }, signal)
      const items = list.items.filter((t) => !CLOSED.has(t.state)).sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
      return { items }
    },
    `dashboard-tickets|${enabled}`,
    { intervalMs: DASHBOARD_POLL_MS, enabled },
  )
}

/** Ongoing underproduction alarms of all installations, refreshed every minute (ADR #222). Without access the list is empty. */
export function useOngoingAlarms(enabled = true) {
  return useLoad<Alarm[]>(
    async (signal, { background }) => {
      if (!enabled) return []
      try {
        return await fetchOngoingAlarms(signal)
      } catch (error) {
        if (signal.aborted || background) throw error
        return []
      }
    },
    `dashboard-alarms|${enabled}`,
    { intervalMs: ALARMS_POLL_MS, enabled },
  )
}

export function useDashboardSummary(enabled = true) {
  return useLoad(async (signal) => {
    if (!enabled) return null
    const { data, response } = await api.GET('/api/dashboard/summary', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, `dashboard-summary|${enabled}`, { intervalMs: DASHBOARD_POLL_MS, enabled })
}
