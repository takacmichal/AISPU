import { api } from '../../api/client'
import { useLoad } from '../../components/useLoad'
import { addDays, toDateString } from '../monitoring/periods'
import { fetchProduction } from '../monitoring/productionApi'
import { listTickets, type TicketListItem } from '../tickets/ticketsApi'
import { CLOSED } from './alarms'
import { MOCK_QUOTE_SUMMARY, MOCK_TICKET_SUMMARY, MOCK_TODAY } from './mock'

export interface TodayProduction {
  todayKwh: number
  yesterdayKwh: number
  targetKwh: number
  online: number
  offline: number
  asOf: string
  mock: boolean
}

const hhmm = (d: Date) => `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`

let cachedToday: TodayProduction | null = null

/**
 * Today's and yesterday's production of all installations; sample data when there is no telemetry or no access.
 * With `live` false (sidebar card on other pages) the last loaded value is reused without a request.
 */
export function useTodayProduction(live = true, enabled = true) {
  return useLoad<TodayProduction | null>(async (signal) => {
    if (!enabled) return null
    if (!live) return cachedToday
    const result = await loadToday(signal)
    cachedToday = result
    return result
  }, `dashboard-today|${live}|${enabled}`)
}

async function loadToday(signal: AbortSignal): Promise<TodayProduction> {
  const now = new Date()
  try {
    const { data } = await api.GET('/api/installations', { signal })
    const ids = (data ?? []).map((i) => i.id)
    const today = toDateString(now)
    const query = { from: addDays(today, -1), to: addDays(today, 1), granularity: 'day' as const }
    const responses = await Promise.all(ids.map((id) => fetchProduction(id, query, signal)))
    const sumFor = (day: string) =>
      responses.reduce(
        (sum, r) => sum + r.points.filter((p) => p.periodStart.slice(0, 10) === day).reduce((s, p) => s + p.energyKwh, 0),
        0,
      )
    const todayKwh = sumFor(today)
    if (ids.length === 0 || todayKwh <= 0) return { ...MOCK_TODAY, mock: true }
    return {
      todayKwh,
      yesterdayKwh: sumFor(addDays(today, -1)),
      targetKwh: Math.max(todayKwh * 1.25, 1),
      online: ids.length,
      offline: 0,
      asOf: hhmm(now),
      mock: false,
    }
  } catch (error) {
    if (signal.aborted) throw error
    return { ...MOCK_TODAY, mock: true }
  }
}

export interface TicketSummary {
  open: number
  overSla: number
  fresh: number
  critical: number
  warning: number
  info: number
  items: TicketListItem[]
  mock: boolean
}

/** Open service tickets (also the source of the alarm KPI); sample numbers when there are none. */
export function useTicketSummary(enabled = true) {
  return useLoad<TicketSummary | null>(async (signal) => {
    if (!enabled) return null
    try {
      const list = await listTickets({ pageSize: 100 }, signal)
      const items = list.items.filter((t) => !CLOSED.has(t.state)).sort((a, b) => Date.parse(b.updatedAt) - Date.parse(a.updatedAt))
      if (items.length === 0) return { ...emptyTickets(), mock: true }
      const count = (p: TicketListItem['priority']) => items.filter((t) => t.priority === p).length
      return {
        open: items.length,
        overSla: 0,
        fresh: items.filter((t) => t.state === 'New').length,
        critical: count('High'),
        warning: count('Medium'),
        info: count('Low'),
        items,
        mock: false,
      }
    } catch (error) {
      if (signal.aborted) throw error
      return { ...emptyTickets(), mock: true }
    }
  }, `dashboard-tickets|${enabled}`)
}

function emptyTickets(): Omit<TicketSummary, 'mock'> {
  return { open: MOCK_TICKET_SUMMARY.open, overSla: MOCK_TICKET_SUMMARY.overSla, fresh: 1, critical: 1, warning: 3, info: 1, items: [] }
}

export interface QuoteSummary {
  waiting: number
  valueGross: number
  open: number
  mock: boolean
}

/** Quotes sent to customers and waiting for an answer. */
export function useQuoteSummary(enabled = true) {
  return useLoad<QuoteSummary | null>(async (signal) => {
    if (!enabled) return null
    try {
      const { data } = await api.GET('/api/quotes', { signal })
      const quotes = data ?? []
      const sent = quotes.filter((q) => q.status === 'Sent')
      const open = quotes.filter((q) => q.status === 'Draft' || q.status === 'Sent').length
      if (sent.length === 0) return { ...MOCK_QUOTE_SUMMARY, open, mock: true }
      return { waiting: sent.length, valueGross: sent.reduce((s, q) => s + Number(q.grossTotal ?? 0), 0), open, mock: false }
    } catch (error) {
      if (signal.aborted) throw error
      return { ...MOCK_QUOTE_SUMMARY, open: 0, mock: true }
    }
  }, `dashboard-quotes|${enabled}`)
}
