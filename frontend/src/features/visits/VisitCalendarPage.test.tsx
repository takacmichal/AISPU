import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { canAccess, NAVIGATION } from '../../auth/navigation'
import { json, problem, renderRoute, stubFetch, type Call } from '../installations/testHelpers'
import VisitCalendarPage from './VisitCalendarPage'

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date(2026, 10, 5, 7, 30))
})
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

const technicians = [
  { id: 'tech-a', userName: 'Adam', email: 'adam@test.sk' },
  { id: 'tech-b', userName: 'Beáta', email: 'beata@test.sk' },
]

const entry = (over: Record<string, unknown> = {}) => ({
  visitId: 'v1',
  ticketId: 't1',
  ticketTitle: 'Chyba striedača',
  priority: 'High',
  ticketState: 'VisitScheduled',
  state: 'Planned',
  start: new Date(2026, 10, 5, 10, 0).toISOString(),
  end: new Date(2026, 10, 5, 12, 0).toISOString(),
  estimatedMinutes: 120,
  installationId: 'i1',
  installationName: 'FVE Novák',
  address: null,
  customer: null,
  ...over,
})

type Extra = (c: Call) => Response | undefined

function api(visits: Record<string, unknown[]> = { 'tech-a': [entry()] }, extra?: Extra) {
  return (c: Call) => {
    const custom = extra?.(c)
    if (custom) return custom
    if (c.path === '/api/installations/technicians') return json(technicians)
    const match = c.path.match(/^\/api\/technicians\/([^/]+)\/visits$/)
    if (match) return json({ technicianId: match[1], from: '', to: '', visits: visits[match[1]] ?? [] })
    if (c.path === '/api/tickets') return json({ items: [{ id: 't9', title: 'Výpadok dataloggera', state: 'RemoteDiagnosis' }], page: 1, pageSize: 100, totalCount: 1 })
  }
}

const render = (route = '/visit-calendar') => renderRoute(<VisitCalendarPage />, route, '/visit-calendar', ['Operator'])
const calendarCalls = (calls: Call[], id: string) => calls.filter((c) => c.path === `/api/technicians/${id}/visits`)

describe('VisitCalendarPage', () => {
  it('shows the seven days of the week with visits of all technicians', async () => {
    const calls = stubFetch(api({ 'tech-a': [entry()], 'tech-b': [entry({ visitId: 'v2', ticketId: 't2', ticketTitle: 'Poistka', start: new Date(2026, 10, 6, 8, 0).toISOString(), end: new Date(2026, 10, 6, 9, 0).toISOString() })] }))
    render()

    const link = await screen.findByRole('link', { name: 'Chyba striedača' })
    expect(link).toHaveAttribute('href', '/tickets/t1')
    expect(screen.getByRole('link', { name: 'Poistka' })).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { level: 3 })).toHaveLength(7)
    expect(within(link.closest('li')!).getByText(/Adam/)).toBeInTheDocument()

    const search = new URLSearchParams(calendarCalls(calls, 'tech-a')[0].search)
    expect(new Date(search.get('from')!)).toEqual(new Date(2026, 10, 2))
    expect(new Date(search.get('to')!)).toEqual(new Date(2026, 10, 9))
  })

  it('filters by technician and keeps the filter in the URL', async () => {
    const calls = stubFetch(api())
    const { router } = render()
    await screen.findByRole('link', { name: 'Chyba striedača' })

    await userEvent.selectOptions(screen.getByLabelText('Technik'), 'tech-b')

    await waitFor(() => expect(screen.queryByRole('link', { name: 'Chyba striedača' })).not.toBeInTheDocument())
    expect(router.state.location.search).toContain('technician=tech-b')
    calls.length = 0
    await userEvent.selectOptions(screen.getByLabelText('Technik'), '')
    await screen.findByRole('link', { name: 'Chyba striedača' })
    expect(calendarCalls(calls, 'tech-a').length).toBeGreaterThan(0)
  })

  it('moves between weeks through the URL', async () => {
    const calls = stubFetch(api())
    const { router } = render()
    await screen.findByRole('link', { name: 'Chyba striedača' })

    await userEvent.click(screen.getByRole('button', { name: /Ďalší týždeň/ }))

    await waitFor(() => expect(router.state.location.search).toContain('week=2026-11-09'))
    await waitFor(() => expect(calendarCalls(calls, 'tech-a').at(-1)?.search).toContain(encodeURIComponent(new Date(2026, 10, 9).toISOString())))
  })

  it('marks colliding visits of a technician', async () => {
    stubFetch(api({ 'tech-a': [entry(), entry({ visitId: 'v2', ticketId: 't2', ticketTitle: 'Poistka', start: new Date(2026, 10, 5, 11, 0).toISOString(), end: new Date(2026, 10, 5, 13, 0).toISOString() })] }))
    render()

    const link = await screen.findByRole('link', { name: 'Poistka' })

    expect(within(link.closest('li')!).getByText('Kolízia')).toBeInTheDocument()
    expect(screen.getAllByText('Kolízia')).toHaveLength(2)
    expect(screen.getByRole('note')).toHaveTextContent('2')
  })

  it('plans a visit for a ticket on the chosen day', async () => {
    const calls = stubFetch(api(undefined, (c) => (c.path === '/api/tickets/t9/visits' && c.method === 'POST' ? json(entry()) : undefined)))
    render()
    await screen.findByRole('link', { name: 'Chyba striedača' })

    await userEvent.click(screen.getByRole('button', { name: /Naplánovať výjazd na .*6/ }))
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByRole('option', { name: 'Výpadok dataloggera' })
    await userEvent.selectOptions(within(dialog).getByLabelText('Tiket *'), 't9')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Naplánovať' }))

    await waitFor(() => expect(calls.find((c) => c.path === '/api/tickets/t9/visits')?.body).toEqual({ start: new Date(2026, 10, 6, 8, 0).toISOString(), estimatedMinutes: 120 }))
    expect(new URLSearchParams(calls.find((c) => c.path === '/api/tickets')!.search).get('state')).toBe('RemoteDiagnosis')
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('requires a ticket and explains a refused plan in Slovak', async () => {
    const calls = stubFetch(api(undefined, (c) => (c.path === '/api/tickets/t9/visits' ? problem(409, 'The technician already has a visit') : undefined)))
    render()
    await screen.findByRole('link', { name: 'Chyba striedača' })
    await userEvent.click(screen.getAllByRole('button', { name: /Naplánovať výjazd na/ })[0])
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByRole('option', { name: 'Výpadok dataloggera' })

    await userEvent.click(within(dialog).getByRole('button', { name: 'Naplánovať' }))
    expect(await within(dialog).findByText('Vyberte tiket.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await userEvent.selectOptions(within(dialog).getByLabelText('Tiket *'), 't9')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Naplánovať' }))

    const alert = await within(dialog).findByRole('alert')
    expect(alert).toHaveTextContent(/nemá priradeného technika alebo má technik/)
    expect(alert).not.toHaveTextContent('already')
  })

  it('moves a planned visit only with a reason', async () => {
    const calls = stubFetch(api(undefined, (c) => (c.path === '/api/visits/v1/schedule' ? json(entry()) : undefined)))
    render()

    await userEvent.click(await screen.findByRole('button', { name: 'Presunúť výjazd Chyba striedača' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Presunúť' }))
    expect(await within(dialog).findByText('Uveďte dôvod presunu.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)

    await userEvent.type(within(dialog).getByLabelText('Dôvod presunu *'), 'Zákazník nie je doma')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Presunúť' }))

    await waitFor(() =>
      expect(calls.find((c) => c.path === '/api/visits/v1/schedule')?.body).toEqual({
        start: new Date(2026, 10, 5, 10, 0).toISOString(),
        estimatedMinutes: 120,
        reason: 'Zákazník nie je doma',
      }),
    )
  })

  it('offers moving only for planned visits', async () => {
    stubFetch(api({ 'tech-a': [entry({ state: 'InProgress' })] }))
    render()
    await screen.findByRole('link', { name: 'Chyba striedača' })
    expect(screen.queryByRole('button', { name: /Presunúť výjazd/ })).not.toBeInTheDocument()
  })

  it('shows an error with retry', async () => {
    let fail = true
    stubFetch(api(undefined, (c) => (fail && c.path.includes('/visits') ? problem(500, 'boom') : undefined)))
    render()
    const alert = await screen.findByRole('alert')
    fail = false
    await userEvent.click(within(alert).getByRole('button'))
    expect(await screen.findByRole('link', { name: 'Chyba striedača' })).toBeInTheDocument()
  })
})

describe('calendar navigation', () => {
  it('is offered only to the roles that may read technician calendars', () => {
    const item = NAVIGATION.find((n) => n.path === '/visit-calendar')!
    expect(canAccess(item, ['Admin'])).toBe(true)
    expect(canAccess(item, ['Operator'])).toBe(true)
    for (const role of ['Sales', 'Designer', 'Technician', 'Customer']) expect(canAccess(item, [role])).toBe(false)
  })
})
