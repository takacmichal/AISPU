import { screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { installation, json, renderRoute, stubFetch, type Call } from '../installations/testHelpers'
import { currentPowerKw, installationSteps } from './model'
import PortalHome from './PortalHome'

const NOW = new Date('2026-11-04T10:00:00Z')
const at = (minutesAgo: number) => new Date(NOW.getTime() - minutesAgo * 60_000).toISOString()

const plants = [
  installation({ id: 'i1', name: 'Rodinný dom, Žilina', plannedPowerKwp: 9.9, state: 'HandedOver' }),
  installation({ id: 'i2', name: 'Chata, Terchová', plannedPowerKwp: 4.5, state: 'Inspection' }),
]

const production = {
  installationId: 'i1',
  from: '2026-11-04',
  to: '2026-11-05',
  granularity: 'fiveMinutes',
  timeZone: 'Europe/Bratislava',
  totalEnergyKwh: 12.4,
  peakPowerKw: 4.6,
  points: [
    { periodStart: at(10), energyKwh: 0.35, peakPowerKw: 4.4, sampleCount: 5 },
    { periodStart: at(5), energyKwh: 0.35, peakPowerKw: 4.2, sampleCount: 5 },
  ],
}

const tickets = {
  items: [
    { id: 't1', installationId: 'i1', customerId: 'c1', title: 'Striedač hlási chybu', priority: 'High', origin: 'CustomerReport', state: 'New', createdAt: at(60), updatedAt: at(60) },
    { id: 't2', installationId: 'i1', customerId: 'c1', title: 'Výmena panelu', priority: 'Low', origin: 'Manual', state: 'Closed', createdAt: at(9000), updatedAt: at(9000) },
  ],
  page: 1,
  pageSize: 5,
  totalCount: 2,
}

function handler(over: Partial<Record<string, () => Response>> = {}) {
  return (call: Call) => {
    if (call.path === '/api/installations') return over.installations?.() ?? json(plants)
    if (call.path === '/api/installations/i1/production') return over.production?.() ?? json(production)
    if (call.path === '/api/connection-requests') return json([])
    if (call.path === '/api/tickets') return over.tickets?.() ?? json(tickets)
  }
}

const render = () => renderRoute(<PortalHome />, '/app', '/app', ['Customer'])

describe('PortalHome', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(NOW)
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('greets the customer and sums production of operating plants', async () => {
    stubFetch(handler())
    render()

    expect(screen.getByRole('heading', { level: 2, name: 'Dobrý deň' })).toBeInTheDocument()
    expect(await screen.findByText('Váš zákaznícky portál · 2 elektrárne')).toBeInTheDocument()
    const kpis = screen.getByRole('group', { name: 'Moje ukazovatele' })
    expect(await within(kpis).findByText('12,4')).toBeInTheDocument()
    expect(within(kpis).getByText('4,2')).toBeInTheDocument()
    expect(within(kpis).getByText('V prevádzke: 1 · V realizácii: 1')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Nahlásiť poruchu' })).toHaveAttribute('href', '/tickets/new')
  })

  it('shows the current power of an operating plant and the stepper of one under construction', async () => {
    stubFetch(handler())
    render()

    const operating = await screen.findByRole('article', { name: 'Rodinný dom, Žilina' })
    expect(await within(operating).findByText(/4,2 kW/)).toBeInTheDocument()
    expect(within(operating).getByText('12,4 kWh')).toBeInTheDocument()
    expect(within(operating).queryByRole('list', { name: /Stav montáže/ })).not.toBeInTheDocument()

    const building = screen.getByRole('article', { name: 'Chata, Terchová' })
    const steps = within(building).getByRole('list', { name: 'Stav montáže – Chata, Terchová' })
    const current = within(steps).getAllByRole('listitem').find((li) => li.getAttribute('aria-current') === 'step')
    expect(current).toHaveTextContent('Revízia')
    expect(within(building).getByText('Prebieha revízia elektroinštalácie.')).toBeInTheDocument()
  })

  it('lists my tickets and counts only open ones', async () => {
    stubFetch(handler())
    render()

    const box = screen.getByRole('article', { name: 'Moje tikety' })
    expect(await within(box).findByRole('link', { name: 'Striedač hlási chybu' })).toHaveAttribute('href', '/tickets/t1')
    expect(within(box).getByText('Nový')).toBeInTheDocument()
    const openCard = screen.getByRole('link', { name: 'Otvorené tikety' }).closest('article') as HTMLElement
    expect(within(openCard).getByText('1')).toBeInTheDocument()
  })

  it('offers to report a fault when there are no tickets', async () => {
    stubFetch(handler({ tickets: () => json({ items: [], page: 1, pageSize: 5, totalCount: 0 }) }))
    render()

    const box = screen.getByRole('article', { name: 'Moje tikety' })
    expect(await within(box).findByText('Zatiaľ žiadne servisné tikety')).toBeInTheDocument()
    expect(within(box).getByRole('link', { name: 'Nahlásiť poruchu' })).toHaveAttribute('href', '/tickets/new')
  })

  it('keeps the plant card when its production fails to load', async () => {
    stubFetch(handler({ production: () => new Response(null, { status: 500 }) }))
    render()

    const operating = await screen.findByRole('article', { name: 'Rodinný dom, Žilina' })
    expect(await within(operating).findByText('Výrobu sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('asks only for the customer’s own data', async () => {
    const calls = stubFetch(handler())
    render()
    await screen.findByRole('article', { name: 'Rodinný dom, Žilina' })
    await screen.findByText('12,4 kWh')

    const paths = calls.map((c) => c.path)
    expect(new Set(paths)).toEqual(new Set(['/api/installations', '/api/installations/i1/production', '/api/connection-requests', '/api/tickets']))
    // No customer filter: the API scopes the lists to the signed-in customer (CustomerScope).
    expect(calls.every((c) => !c.search.includes('customerId'))).toBe(true)
  })
})

describe('portal model', () => {
  it('maps installation states to five steps', () => {
    expect(installationSteps('Mounting').map((s) => s.status)).toEqual(['done', 'current', 'upcoming', 'upcoming', 'upcoming'])
    expect(installationSteps('InspectionFailed')[2].status).toBe('blocked')
    expect(installationSteps('HandedOver').every((s) => s.status === 'done')).toBe(true)
  })

  it('treats production older than 15 minutes as no current power', () => {
    const point = (minutesAgo: number) => ({ periodStart: at(minutesAgo), energyKwh: 0.3, peakPowerKw: 3.1, sampleCount: 5 })
    expect(currentPowerKw([point(5)], NOW)).toBe(3.1)
    expect(currentPowerKw([point(40)], NOW)).toBeNull()
    expect(currentPowerKw([], NOW)).toBeNull()
  })
})
