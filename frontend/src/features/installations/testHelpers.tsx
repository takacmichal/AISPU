import { render } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ConfirmProvider } from '../../components/confirm'
import { ToastProvider } from '../../components/toast'
import type { ReactNode } from 'react'

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

export const problem = (status: number, detail: string) =>
  new Response(JSON.stringify({ status, title: 'x', detail }), { status, headers: { 'Content-Type': 'application/problem+json' } })

export interface Call {
  method: string
  path: string
  search: string
  body?: unknown
}

/** Stubs fetch with a handler; every request is recorded in `calls`. Unhandled requests return 404. */
export function stubFetch(handler: (call: Call) => Response | Promise<Response> | undefined) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (url: string, init: RequestInit = {}) => {
      const parsed = new URL(String(url), 'http://localhost')
      const call: Call = {
        method: init.method ?? 'GET',
        path: parsed.pathname,
        search: parsed.search,
        body: init.body ? JSON.parse(String(init.body)) : undefined,
      }
      calls.push(call)
      return (await handler(call)) ?? new Response(null, { status: 404 })
    }),
  )
  return calls
}

export function renderRoute(element: ReactNode, route: string, path: string, roles: string[] = ['Admin']) {
  saveSession({ email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' })
  const router = createMemoryRouter(
    [
      {
        path,
        element: (
          <AuthProvider>
            <ToastProvider>
              <ConfirmProvider>{element}</ConfirmProvider>
            </ToastProvider>
          </AuthProvider>
        ),
      },
    ],
    { initialEntries: [route] },
  )
  return { router, ...render(<RouterProvider router={router} />) }
}

export const installation = (over: Record<string, unknown> = {}) => ({
  id: 'i1',
  customerId: 'c1',
  contractId: 'k1',
  supplyPointId: 's1',
  name: 'FVE Nitra',
  plannedPowerKwp: 9.6,
  note: null,
  state: 'Planned',
  allowedTransitions: ['ComponentsOrdered'],
  scheduledStart: null,
  scheduledEnd: null,
  technicianIds: [],
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  stateChangedAt: '2026-01-01T00:00:00Z',
  ...over,
})

export const detailOf = (over: Record<string, unknown> = {}) => ({
  installation: installation(over),
  stateHistory: [
    { fromState: null, toState: 'Planned', changedBy: 'u1', changedAt: '2026-01-01T00:00:00Z', note: null },
  ],
})

export const device = (over: Record<string, unknown> = {}) => ({
  id: 'd1',
  installationId: 'i1',
  type: 'Panel',
  serialNumber: 'JA2410-000123',
  manufacturer: 'JA Solar',
  model: 'JAM54S30-450',
  powerW: 450,
  status: 'Installed',
  registeredBy: 'u1',
  registeredAt: '2026-02-01T10:00:00Z',
  removedAt: null,
  removedBy: null,
  removalReason: null,
  replacedByDeviceId: null,
  replacesDeviceId: null,
  ...over,
})
