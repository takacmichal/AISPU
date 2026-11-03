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
    const key = Object.keys(routes).find((k) => k === `${method} ${path}`)
    if (!key) throw new Error(`Unmocked ${method} ${path}`)
    const handler = routes[key]
    return handler instanceof Response ? handler.clone() : handler(url, init)
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
