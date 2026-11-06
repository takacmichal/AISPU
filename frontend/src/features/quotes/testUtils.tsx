/* eslint-disable react-refresh/only-export-components */
import { render } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ConfirmProvider } from '../../components/confirm'
import { ToastProvider } from '../../components/toast'
import QuoteDetailPage from './QuoteDetailPage'
import QuoteListPage from './QuoteListPage'

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' } })

export const problem = (status: number, detail: string, errors?: Record<string, string[]>) =>
  json({ status, title: 'Error', detail, errors }, status)

export const CUSTOMER_ID = '11111111-1111-1111-1111-111111111111'
export const QUOTE_ID = 'abcdef12-0000-0000-0000-000000000001'

export const customersPage = json({
  items: [{ id: CUSTOMER_ID, displayName: 'Ján Novák', status: 'Active' }],
  page: 1,
  pageSize: 100,
  totalCount: 1,
})

export function makeQuote(overrides: Record<string, unknown> = {}) {
  return {
    id: QUOTE_ID,
    customerId: CUSTOMER_ID,
    status: 'Draft',
    allowedTransitions: ['Sent'],
    validUntil: null,
    vatRatePercent: 23,
    currency: 'EUR',
    note: null,
    lines: [
      { position: 1, componentId: 'p1', componentType: 'Panel', code: 'PV-400', name: 'Panel 400 W', unitPrice: 100, quantity: 10, netPrice: 1000 },
    ],
    netTotal: 1000,
    vatTotal: 230,
    grossTotal: 1230,
    createdBy: 'u',
    createdAt: '2026-11-03T10:00:00Z',
    updatedAt: '2026-11-03T10:00:00Z',
    ...overrides,
  }
}

type Handler = (url: string, init?: RequestInit) => Response | Promise<Response>

/** Routes fetch calls by `METHOD path` prefix; unknown calls fail the test loudly. */
export function mockApi(routes: Record<string, Handler | Response>) {
  const mock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input instanceof Request ? input.url : input)
    const method = (init?.method ?? (input instanceof Request ? input.method : 'GET')).toUpperCase()
    const path = new URL(url, 'http://x').pathname
    const key = Object.keys(routes).find((k) => k === `${method} ${path}`) ?? (path === '/api/quotes/paged' ? Object.keys(routes).find((k) => k === `${method} /api/quotes`) : undefined)
    if (!key && path === '/api/installations/paged') {
      const legacy = routes['GET /api/installations']
      if (!legacy) return json({ items: [], page: 1, pageSize: 1, totalCount: 0, statusCounts: {} })
      const response = legacy instanceof Response ? legacy.clone() : await legacy(url, init)
      if (!response.ok) return response
      const rows = await response.json() as { state: string }[]
      const statusCounts = Object.fromEntries([...new Set(rows.map((item) => item.state))].map((state) => [state, rows.filter((item) => item.state === state).length]))
      const params = new URL(url, 'http://x').searchParams
      const page = Number(params.get('page') ?? 1)
      const pageSize = Number(params.get('pageSize') ?? 20)
      return json({ items: rows.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: rows.length, statusCounts })
    }
    if (!key && path === '/api/dashboard/summary') {
      const getBody = async (route: string) => {
        const match = routes[`GET ${route}`]
        if (!match) return null
        const res = match instanceof Response ? match.clone() : await match(url, init)
        if (!res.ok) throw res
        return res.json() as Promise<unknown>
      }
      try {
        const [quoteRaw, ticketRaw] = await Promise.all([getBody('/api/quotes'), getBody('/api/tickets')])
        const quotes = Array.isArray(quoteRaw) ? quoteRaw : []
        const ticketPage = (ticketRaw ?? { items: [] }) as { items?: { state: string; priority: string }[] }
        const open = (ticketPage.items ?? []).filter((item) => item.state !== 'Resolved' && item.state !== 'Closed')
        const sent = quotes.filter((item) => (item as { status: string }).status === 'Sent')
        return json({ generatedAt: new Date().toISOString(), tickets: ticketRaw === null ? null : { open: open.length, new: open.filter((item) => item.state === 'New').length, byPriority: { high: open.filter((item) => item.priority === 'High').length, medium: open.filter((item) => item.priority === 'Medium').length, low: open.filter((item) => item.priority === 'Low').length } }, quotes: quoteRaw === null ? null : { sent: sent.length, sentGrossTotal: sent.reduce((sum, item) => sum + Number((item as { grossTotal?: number }).grossTotal ?? 0), 0), open: quotes.filter((item) => ['Draft', 'Sent'].includes((item as { status: string }).status)).length } })
      } catch { return json({}, 500) }
    }
    if (!key && path === '/api/production/summary') {
      const match = routes['GET /api/installations']
      if (!match) return json({ installationCount: 0, points: [] })
      const res = match instanceof Response ? match.clone() : await match(url, init)
      if (!res.ok) return res
      const installations = await res.json() as { id: string; state: string }[]
      const operating = installations.filter((item) => ['Commissioned', 'HandedOver'].includes(item.state))
      const search = new URL(url, 'http://x').searchParams
      const from = search.get('From') ?? new Date().toISOString().slice(0, 10)
      const to = search.get('To') ?? from
      const totals = new Map<string, number>()
      for (const item of operating) {
        const productionRoute = `GET /api/installations/${item.id}/production`
        const handler = routes[productionRoute]
        if (!handler) continue
        const response = handler instanceof Response ? handler.clone() : await handler(url, init)
        if (!response.ok) continue
        const data = await response.json() as { points?: { periodStart: string; energyKwh: number }[] }
        for (const point of data.points ?? []) totals.set(point.periodStart, (totals.get(point.periodStart) ?? 0) + Number(point.energyKwh))
      }
      const points = [...totals].map(([periodStart, energyKwh]) => ({ periodStart, energyKwh, peakPowerKw: 0 }))
      return json({ from, to, granularity: 'Day', timeZone: 'Europe/Bratislava', installationCount: operating.length, totalEnergyKwh: points.reduce((sum, item) => sum + item.energyKwh, 0), peakPowerKw: null, points })
    }
    if (!key) throw new Error(`Unmocked ${method} ${path}`)
    const handler = routes[key]
    const response = handler instanceof Response ? handler.clone() : await handler(url, init)
    if (path === '/api/quotes/paged' && response.ok) {
      const body: unknown = await response.clone().json()
      if (Array.isArray(body)) {
        const params = new URL(url, 'http://x').searchParams
        const status = params.get('status')
        const filtered = body.filter((item) => !status || (item as { status?: string }).status === status)
        const page = Number(params.get('page') ?? 1)
        const pageSize = Number(params.get('pageSize') ?? 20)
        const statusCounts = Object.fromEntries([...new Set(body.map((item) => (item as { status: string }).status))].map((item) => [item, body.filter((row) => (row as { status: string }).status === item).length]))
        return json({ items: filtered.slice((page - 1) * pageSize, page * pageSize), page, pageSize, totalCount: filtered.length, statusCounts })
      }
    }
    return response
  })
  vi.stubGlobal('fetch', mock)
  return mock
}

function Location() {
  const l = useLocation()
  return <output data-testid="loc">{l.pathname + l.search}</output>
}

export function renderQuotes(path: string, roles: string[] = ['Sales']) {
  saveSession({ email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' })
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <ToastProvider>
          <ConfirmProvider>
            <Routes>
              <Route path="/quotes" element={<QuoteListPage />} />
              <Route path="/quotes/:id" element={<QuoteDetailPage />} />
              <Route path="*" element={null} />
            </Routes>
            <Location />
          </ConfirmProvider>
        </ToastProvider>
      </AuthProvider>
    </MemoryRouter>,
  )
}
