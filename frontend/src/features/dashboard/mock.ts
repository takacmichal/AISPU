/**
 * Sample data for the dashboard parts whose backend does not exist yet or has no data (telemetry, alarms/anomalies,
 * SLA). Everything shown from here is marked "Ukážkové dáta" in the UI; real data wins as soon as the API returns some.
 */
import type { AlarmRow } from './alarms'
import type { Bar } from './aggregate'

export const MOCK_TODAY = {
  todayKwh: 1284,
  yesterdayKwh: 1189,
  targetKwh: 1600,
  online: 62,
  offline: 2,
  asOf: '11:00',
}

export const MOCK_ALARM_SUMMARY = { critical: 1, warning: 3, info: 1, fresh: 1 }
export const MOCK_TICKET_SUMMARY = { open: 18, overSla: 2 }
export const MOCK_QUOTE_SUMMARY = { waiting: 7, valueGross: 214_600 }
export const MOCK_NAV_COUNTS: Record<string, number> = { '/customers': 248, '/quotes': 3, '/tickets': 18 }

const DAILY_14 = [1180, 1310, 900, 1440, 1500, 560, 790, 1590, 1690, 1250, 1340, 1490, 860, 1240]

/** Deterministic bars ending today: the 14 daily values repeat for 30 days; 12 months are scaled sums. */
export function mockBars(range: '14d' | '30d' | '12m', now: Date): Bar[] {
  const bars: Bar[] = []
  const count = range === '14d' ? 14 : range === '30d' ? 30 : 12
  for (let i = 0; i < count; i++) {
    const back = count - 1 - i
    const date =
      range === '12m'
        ? new Date(now.getFullYear(), now.getMonth() - back, 1)
        : new Date(now.getFullYear(), now.getMonth(), now.getDate() - back)
    const base = DAILY_14[i % DAILY_14.length]
    const kwh = range === '12m' ? Math.round(base * 24 + ((i * 37) % 5) * 300) : base
    bars.push({ key: date.toISOString(), start: date.toISOString(), kwh })
  }
  return bars
}

export const MOCK_ALARMS: AlarmRow[] = [
  { id: 'm1', href: '/tickets', title: 'Chyba izolácie striedača', meta: 'FVE-0231 · Ján Novák', severity: 'critical', at: '10:42' },
  { id: 'm2', href: '/tickets', title: 'Výkon stringu S2 pod priemerom', meta: 'FVE-0231 · Ján Novák', severity: 'warning', at: '09:15' },
  { id: 'm3', href: '/tickets', title: 'Výpadok komunikácie dataloggera', meta: 'FVE-0198 · Solar Agro', severity: 'warning', at: '08:50' },
  { id: 'm4', href: '/tickets', title: 'Odpojenie od siete – prepätie', meta: 'FVE-0172 · Hotel Tatra', severity: 'warning', at: '07:31' },
  { id: 'm5', href: '/tickets', title: 'Aktualizácia firmvéru dokončená', meta: 'FVE-0150 · Lietavská Lúčka', severity: 'info', at: '06:00' },
]
