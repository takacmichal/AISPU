import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { detailOf, device, json, problem, renderRoute, stubFetch, type Call } from '../installations/testHelpers'
import MyPlantDetailPage from './MyPlantDetailPage'
import { connectionRequest } from './testUtils'

afterEach(() => vi.unstubAllGlobals())

function render(route = '/my-plants/i1') {
  return renderRoute(<MyPlantDetailPage />, route, '/my-plants/:id', ['Customer'])
}

const production = (points: { periodStart: string; energyKwh: number }[]) => ({
  installationId: 'i1',
  from: '2026-10-08',
  to: '2026-11-07',
  granularity: 'day',
  timeZone: 'Europe/Bratislava',
  totalEnergyKwh: points.reduce((s, p) => s + p.energyKwh, 0),
  peakPowerKw: 6.1,
  points: points.map((p) => ({ ...p, peakPowerKw: 6.1, sampleCount: 288 })),
})

const twoDays = () =>
  json(
    production([
      { periodStart: '2026-11-05T00:00:00+01:00', energyKwh: 30.4 },
      { periodStart: '2026-11-06T00:00:00+01:00', energyKwh: 41.6 },
    ]),
  )

const handedOver = () => ({
  ...detailOf({ state: 'HandedOver', note: 'Interná poznámka technika', technicianIds: ['t1'] }),
  stateHistory: [
    { fromState: null, toState: 'Planned', changedBy: 'u1', changedAt: '2026-01-01T09:00:00Z', note: 'interné' },
    { fromState: 'Commissioned', toState: 'Commissioned', changedBy: 'u2', changedAt: '2026-05-10T09:00:00Z', note: null },
    { fromState: 'Commissioned', toState: 'HandedOver', changedBy: 'u2', changedAt: '2026-05-20T09:00:00Z', note: null },
  ],
})

function handler(over: Partial<Record<string, () => Response>> = {}) {
  return (call: Call) => {
    if (call.path === '/api/installations/i1') return over.detail?.() ?? json(handedOver())
    if (call.path === '/api/installations/i1/production') return over.production?.() ?? twoDays()
    if (call.path === '/api/installations/i1/devices')
      return over.devices?.() ?? json([device(), device({ id: 'd2', type: 'Inverter', serialNumber: 'HW-INV-77', manufacturer: 'Huawei', model: 'SUN2000-8K', powerW: 8000 })])
    if (call.path === '/api/connection-requests')
      return (
        over.requests?.() ??
        json([
          connectionRequest({
            state: 'Concluded',
            attachments: [
              {
                id: 'a1',
                kind: 'SingleLineDiagram',
                fileName: 'schema.pdf',
                contentType: 'application/pdf',
                sizeBytes: 1000,
                createdAt: '2026-03-01T08:00:00Z',
                hasContent: true,
                downloadUrl: '/api/connection-requests/r1/attachments/a1',
              },
            ],
          }),
        ])
      )
  }
}

describe('MyPlantDetailPage', () => {
  it('shows overview, progress, connection, devices, documents and production', async () => {
    const calls = stubFetch(handler())
    render()
    expect(await screen.findByRole('heading', { name: 'FVE Nitra', level: 2 })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Späť na Moje elektrárne' })).toHaveAttribute('href', '/my-plants')

    const overview = screen.getByRole('region', { name: 'Prehľad' })
    expect(within(overview).getByText(/^9,6\skWp$/)).toBeInTheDocument()
    expect(within(overview).getByText('10. 5. 2026')).toBeInTheDocument()
    expect(await within(overview).findByText('24ZSS1234567890A')).toBeInTheDocument()

    expect(within(screen.getByRole('region', { name: 'Priebeh inštalácie' })).getAllByRole('listitem')).toHaveLength(3)
    expect(within(screen.getByRole('region', { name: 'Pripojenie k distribučnej sieti' })).getByRole('list', { name: /./ })).toBeInTheDocument()

    const devices = screen.getByRole('region', { name: 'Zariadenia' })
    expect(await within(devices).findByText('JA2410-000123')).toBeInTheDocument()
    expect(within(devices).getByText('HW-INV-77')).toBeInTheDocument()
    expect(within(devices).getByText('Panely: 1, invertory: 1')).toBeInTheDocument()

    const documents = screen.getByRole('region', { name: 'Dokumenty a odovzdanie' })
    expect(within(documents).getByText('Elektráreň bola odovzdaná 20. 5. 2026.')).toBeInTheDocument()
    expect(within(documents).getByRole('button', { name: 'Stiahnuť schema.pdf' })).toBeInTheDocument()

    const output = screen.getByRole('region', { name: 'Výroba' })
    expect(await within(output).findByRole('img', { name: 'Výroba za posledných 30 dní' })).toBeInTheDocument()
    expect(within(output).getByText(/^72\skWh$/)).toBeInTheDocument()
    expect(within(output).getByText(/^42\skWh$/)).toBeInTheDocument()
    expect(within(output).queryByText('Pripravuje sa')).not.toBeInTheDocument()

    // Internal data is not shown and no back-office API is called.
    expect(screen.queryByText('Interná poznámka technika')).not.toBeInTheDocument()
    expect(screen.queryByText('interné')).not.toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/connection-requests')?.search).toBe('?installationId=i1')
    expect(calls.some((c) => /customers|contracts|telemetry/.test(c.path))).toBe(false)
    const query = new URLSearchParams(calls.find((c) => c.path === '/api/installations/i1/production')?.search)
    expect(query.get('granularity')).toBe('day')
  })

  it('does not load production before the plant is in operation', async () => {
    const calls = stubFetch(handler({ detail: () => json(detailOf({ state: 'Mounting' })) }))
    render()
    const output = await screen.findByRole('region', { name: 'Výroba' })
    expect(within(output).getByText('Výrobu uvidíte po uvedení elektrárne do prevádzky.')).toBeInTheDocument()
    expect(calls.some((c) => c.path.endsWith('/production'))).toBe(false)
  })

  it('shows an empty state when no production was measured yet', async () => {
    stubFetch(handler({ production: () => json(production([])) }))
    render()
    const output = await screen.findByRole('region', { name: 'Výroba' })
    expect(await within(output).findByText('Za posledných 30 dní zatiaľ nie sú namerané údaje.')).toBeInTheDocument()
    expect(within(output).queryByRole('img')).not.toBeInTheDocument()
  })

  it('shows a production error with retry', async () => {
    let fail = true
    stubFetch(handler({ production: () => (fail ? problem(500, 'boom') : twoDays()) }))
    render()
    const output = await screen.findByRole('region', { name: 'Výroba' })
    expect(await within(output).findByText('Výrobu sa nepodarilo načítať.')).toBeInTheDocument()
    fail = false
    await userEvent.click(within(output).getByRole('button', { name: 'Skúsiť znova' }))
    expect(await within(output).findByRole('img', { name: 'Výroba za posledných 30 dní' })).toBeInTheDocument()
  })

  it('shows a foreign or missing plant as not found and loads nothing else', async () => {
    const calls = stubFetch((call) => (call.path === '/api/installations/x9' ? problem(404, 'Not found') : undefined))
    render('/my-plants/x9')
    expect(await screen.findByText('Elektráreň sa nenašla')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Späť na Moje elektrárne' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(calls.map((c) => c.path)).toEqual(['/api/installations/x9'])
  })

  it('shows an error with retry for other failures', async () => {
    let fail = true
    stubFetch(handler({ detail: () => (fail ? new Response(null, { status: 500 }) : json(handedOver())) }))
    render()
    expect(await screen.findByRole('alert')).toHaveTextContent('Elektráreň sa nepodarilo načítať.')
    fail = false
    await userEvent.click(screen.getByRole('button', { name: 'Skúsiť znova' }))
    expect(await screen.findByRole('heading', { name: 'FVE Nitra', level: 2 })).toBeInTheDocument()
  })

  it('shows empty states when there is no connection request and no device yet', async () => {
    stubFetch(handler({ detail: () => json(detailOf()), requests: () => json([]), devices: () => json([]) }))
    render()
    expect(await screen.findByText('Žiadosť o pripojenie zatiaľ nebola založená.')).toBeInTheDocument()
    expect(await screen.findByText('Zariadenia sa zaevidujú počas montáže.')).toBeInTheDocument()
    expect(screen.getByText('Zatiaľ tu nie sú žiadne dokumenty.')).toBeInTheDocument()
    expect(screen.getByText('Pripravujeme termín montáže.')).toBeInTheDocument()
  })
})
