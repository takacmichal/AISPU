import { formatDateTime, formatNumber } from '../../lib/format'
import type { Granularity, ProductionQuery } from './productionApi'

/** The fields of an alarm the screen logic needs (generated type: UnderproductionAlarmResponse). */
export interface AlarmTimes {
  start: string
  lastUnderproductionAt: string
}

export type AlarmSeverity = 'Low' | 'Medium' | 'High'
export type AlarmStateFilter = 'all' | 'ongoing' | 'ended'
export type AlarmSeverityFilter = 'all' | AlarmSeverity

export const SEVERITY_TONE: Record<AlarmSeverity, string> = { High: 'error', Medium: 'warning', Low: 'info' }

/** Hours of context around the alarm in the detail chart. */
const CONTEXT_HOURS = 6
/** Hourly points stay readable up to about two weeks; a longer event is shown by days. */
const MAX_HOURLY_RANGE_MS = 14 * 86_400_000

export function addHours(iso: string, hours: number): string {
  return new Date(Date.parse(iso) + hours * 3_600_000).toISOString()
}

/** The detection labels an hour by its end, so the event starts one hour before `start`. */
export function alarmPeriod(alarm: AlarmTimes): { from: string; to: string } {
  return { from: addHours(alarm.start, -1), to: alarm.lastUnderproductionAt }
}

export function alarmPeriodText(alarm: AlarmTimes): string {
  const { from, to } = alarmPeriod(alarm)
  return `${formatDateTime(from)} – ${formatDateTime(to)}`
}

/** Query of the detail chart: the event with a few hours of normal production on both sides. */
export function alarmChartQuery(alarm: AlarmTimes): ProductionQuery {
  const { from, to } = alarmPeriod(alarm)
  const granularity: Granularity = Date.parse(to) - Date.parse(from) > MAX_HOURLY_RANGE_MS ? 'day' : 'hour'
  return { from: addHours(from, -CONTEXT_HOURS), to: addHours(to, CONTEXT_HOURS), granularity }
}

export function filterAlarms<T extends { severity: AlarmSeverity; ongoing: boolean }>(
  alarms: readonly T[],
  severity: AlarmSeverityFilter,
  state: AlarmStateFilter,
): T[] {
  return alarms.filter(
    (a) => (severity === 'all' || a.severity === severity) && (state === 'all' || a.ongoing === (state === 'ongoing')),
  )
}

/** The API may send decimals as strings; a ratio of 0.62 is shown as 62 %. */
export const ratioPercent = (ratio: number | string) => Math.round(Number(ratio) * 100)

export const kwhText = (value: number | string) => `${formatNumber(Number(value), 1)} kWh`

interface TicketAlarm extends AlarmTimes {
  installationId: string
  installationName: string
  medianRatio: number | string
  estimatedLostKwh: number | string
}

/** `/tickets/new` link with the installation and a prefilled title and description of the alarm. */
export function alarmTicketHref(alarm: TicketAlarm): string {
  const when = alarmPeriodText(alarm)
  const description =
    `Automatická detekcia zistila podvýrobu elektrárne ${alarm.installationName} v období ${when}: ` +
    `výroba je ${ratioPercent(alarm.medianRatio)} % očakávanej, odhadovaná strata ${kwhText(alarm.estimatedLostKwh)}. Prosím o preverenie.`
  const params = new URLSearchParams({ installationId: alarm.installationId, title: `Podvýroba ${when}`, description })
  return `/tickets/new?${params.toString()}`
}
