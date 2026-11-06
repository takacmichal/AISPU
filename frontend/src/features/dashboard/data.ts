import { api } from '../../api/client'
import { ALARMS_POLL_MS, DASHBOARD_POLL_MS, useLoad } from '../../components/useLoad'
import { fetchOngoingAlarms, type Alarm } from '../monitoring/alarmsApi'
import { addDays, toDateString } from '../monitoring/periods'
import { fetchProduction } from '../monitoring/productionApi'
import { listTickets, type TicketListItem } from '../tickets/ticketsApi'
import { CLOSED } from './alarms'

/** Installation states with a running plant; telemetry and production exist only for these. */
export const OPERATING_STATES: ReadonlySet<string> = new Set(['Commissioned', 'HandedOver'])

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
  const { data, response } = await api.GET('/api/installations', { signal })
  if (!data) throw new Error(`HTTP ${response.status}`)
  const ids = data.filter((i) => OPERATING_STATES.has(i.state)).map((i) => i.id)
  const today = toDateString(now)
  const query = { from: addDays(today, -1), to: addDays(today, 1), granularity: 'day' as const }
  const responses = await Promise.all(ids.map((id) => fetchProduction(id, query, signal)))
  const sumFor = (day: string) =>
    responses.reduce(
      (sum, r) => sum + r.points.filter((p) => p.periodStart.slice(0, 10) === day).reduce((s, p) => s + p.energyKwh, 0),
      0,
    )
  return { todayKwh: sumFor(today), yesterdayKwh: sumFor(addDays(today, -1)), operating: ids.length, asOf: hhmm(now) }
}

export interface TicketSummary {
  open: number
  /** Open tickets in state New, waiting for triage. */
  waitingTriage: number
  fresh: number
  critical: number
  warning: number
  info: number
  items: TicketListItem[]
}

/** Open service tickets; the alarm KPI counts only those raised by anomaly detection. */
export function useTicketSummary(enabled = true) {
  return useLoad<TicketSummary | null>(
    async (signal) => {
      if (!enabled) return null
      const list = await listTickets({ pageSize: 100 }, signal)
      const items = list.items.filter((t) => !CLOSED.has(t.state)).sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
      const alarms = items.filter((t) => t.origin === 'AnomalyDetected')
      const count = (p: TicketListItem['priority']) => alarms.filter((t) => t.priority === p).length
      return {
        open: items.length,
        waitingTriage: items.filter((t) => t.state === 'New').length,
        fresh: alarms.filter((t) => t.state === 'New').length,
        critical: count('High'),
        warning: count('Medium'),
        info: count('Low'),
        items,
      }
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

export interface QuoteSummary {
  waiting: number
  valueGross: number
  open: number
}

/** Quotes sent to customers and waiting for an answer. */
export function useQuoteSummary(enabled = true) {
  return useLoad<QuoteSummary | null>(
    async (signal) => {
      if (!enabled) return null
      const { data, response } = await api.GET('/api/quotes', { signal })
      if (!data) throw new Error(`HTTP ${response.status}`)
      const sent = data.filter((q) => q.status === 'Sent')
      const open = data.filter((q) => q.status === 'Draft' || q.status === 'Sent').length
      return { waiting: sent.length, valueGross: sent.reduce((s, q) => s + Number(q.grossTotal ?? 0), 0), open }
    },
    `dashboard-quotes|${enabled}`,
    { intervalMs: DASHBOARD_POLL_MS, enabled },
  )
}
