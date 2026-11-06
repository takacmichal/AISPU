import { render } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ConfirmProvider } from '../../components/confirm'
import { ToastProvider } from '../../components/toast'
import DesignDetailPage from './DesignDetailPage'
import DesignListPage from './DesignListPage'

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

export function renderAt(path: string, role = 'Designer') {
  const session = { email: 'a@test.sk', roles: [role], accessToken: 't', refreshToken: 'r' }
  saveSession(session)
  return render(
    <AuthProvider initialSession={session}>
      <ToastProvider>
        <ConfirmProvider>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/designs" element={<DesignListPage />} />
              <Route path="/designs/:id" element={<DesignDetailPage />} />
              {/* Target of the "create quote" navigation in tests (quote id q1). */}
              <Route path="/quotes/q1" element={<h2>Ponuka q1</h2>} />
            </Routes>
          </MemoryRouter>
        </ConfirmProvider>
      </ToastProvider>
    </AuthProvider>,
  )
}

export interface Call {
  method: string
  path: string
  body?: unknown
}

/** Stubs fetch; `handler` may return a Response for a call, otherwise `fallback` answers by path. */
export function stubFetch(handler: (call: Call) => Response | undefined) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (url: string, init: RequestInit = {}) => {
      const call: Call = {
        method: init.method ?? 'GET',
        path: new URL(String(url), 'http://localhost').pathname,
        body: init.body ? JSON.parse(String(init.body)) : undefined,
      }
      calls.push(call)
      return handler(call) ?? json({}, 404)
    }),
  )
  return calls
}

export const baseDesign = {
  id: 'd1',
  customerId: 'c1',
  supplyPointId: 's1',
  status: 'Requested',
  requestNotes: 'Rodinný dom',
  siteSurvey: null,
  infeasibleReason: null,
  latitude: null,
  longitude: null,
  annualConsumptionKwh: null,
  consumptionProfile: null,
  roofPlanes: [],
  createdBy: 'u1',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
}
