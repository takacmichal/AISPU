import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { menuItems } from '../../auth/navigation'
import { json, problem, renderRoute, stubFetch, type Call } from '../installations/testHelpers'
import MyVisitsPage from './MyVisitsPage'

beforeEach(() => {
  localStorage.clear()
  vi.useFakeTimers({ toFake: ['Date'] })
  vi.setSystemTime(new Date(2026, 10, 5, 7, 30))
})
afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const entry = (over: Record<string, unknown> = {}) => ({
  visitId: 'v1',
  ticketId: 't1',
  ticketTitle: 'Chyba izolácie striedača',
  priority: 'High',
  ticketState: 'VisitScheduled',
  state: 'Planned',
  start: new Date(2026, 10, 5, 10, 30).toISOString(),
  end: new Date(2026, 10, 5, 12, 0).toISOString(),
  estimatedMinutes: 90,
  installationId: 'i1',
  installationName: 'FVE Novák',
  address: { street: 'Hlboká 12', city: 'Žilina', postalCode: '010 01', country: 'SK' },
  customer: { id: 'c1', name: 'Ján Novák', phone: '+421 905 123 456', email: 'jan@test.sk' },
  ...over,
})

const visit = (over: Record<string, unknown> = {}) => ({
  id: 'v1',
  ticketId: 't1',
  technicianId: 'me',
  state: 'Planned',
  plannedStart: new Date(2026, 10, 5, 10, 30).toISOString(),
  plannedEnd: new Date(2026, 10, 5, 12, 0).toISOString(),
  estimatedMinutes: 90,
  startedAt: null,
  report: null,
  reschedules: [],
  materials: [],
  deviceReplacements: [],
  ...over,
})

function handler(entries = [entry()], detail = visit(), extra?: (c: Call) => Response | undefined) {
  return (c: Call) => {
    const custom = extra?.(c)
    if (custom) return custom
    if (c.path === '/api/technicians/me/visits') return json({ technicianId: 'me', from: '', to: '', visits: entries })
    if (c.path === '/api/visits/v1') return json(detail)
  }
}

const render = (route = '/my-visits') => renderRoute(<MyVisitsPage />, route, '/my-visits', ['Technician'])

describe('MyVisitsPage list', () => {
  it("lists today's visits with time, customer, address and badges", async () => {
    const calls = stubFetch(handler())
    render()
    const card = await screen.findByRole('link', { name: 'Otvoriť výjazd Chyba izolácie striedača' })
    expect(within(card).getByText('10:30')).toBeInTheDocument()
    expect(within(card).getByText('FVE Novák · Ján Novák')).toBeInTheDocument()
    expect(within(card).getByText('Hlboká 12, 010 01 Žilina')).toBeInTheDocument()
    expect(within(card).getByText('Naplánovaný')).toBeInTheDocument()
    expect(within(card).getByText('Vysoká')).toBeInTheDocument()
    const search = new URLSearchParams(calls[0].search)
    expect(search.get('from')).toBe(new Date(2026, 10, 5).toISOString())
    expect(search.get('to')).toBe(new Date(2026, 10, 6).toISOString())
  })

  it('switches to the week and keeps the range in the URL', async () => {
    const calls = stubFetch(handler())
    const { router } = render()
    await screen.findByRole('link', { name: /Otvoriť výjazd/ })
    await userEvent.click(screen.getByRole('radio', { name: 'Týždeň' }))
    await waitFor(() => expect(calls.some((c) => new URLSearchParams(c.search).get('to') === new Date(2026, 10, 12).toISOString())).toBe(true))
    expect(router.state.location.search).toBe('?range=week')
  })

  it('shows the empty state and an error with retry', async () => {
    stubFetch(handler([]))
    const first = render()
    expect(await screen.findByText('V tomto období nemáte žiadny výjazd.')).toBeInTheDocument()
    first.unmount()
    stubFetch(() => problem(500, 'boom'))
    render()
    expect(await screen.findByText('Výjazdy sa nepodarilo načítať.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Skúsiť znova' })).toBeInTheDocument()
  })
})

describe('MyVisitsPage detail', () => {
  it('offers call, navigation, the ticket and a checklist', async () => {
    stubFetch(handler())
    render('/my-visits?visit=v1')
    expect(await screen.findByRole('heading', { name: 'Chyba izolácie striedača', level: 2 })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Zavolať' })).toHaveAttribute('href', 'tel:+421905123456')
    expect(screen.getByRole('link', { name: 'Navigovať' })).toHaveAttribute('href', expect.stringContaining('destination=Hlbok%C3%A1%2012'))
    expect(screen.getByRole('link', { name: 'Chyba izolácie striedača' })).toHaveAttribute('href', '/tickets/t1')
    const checklist = screen.getByRole('group', { name: 'Kontrolný zoznam' })
    await userEvent.click(within(checklist).getByLabelText('Odpojiť DC stranu'))
    expect(within(checklist).getByLabelText('Odpojiť DC stranu')).toBeChecked()
  })

  it('starts a planned visit', async () => {
    const calls = stubFetch(handler([entry()], visit(), (c) => (c.path === '/api/visits/v1/start' ? json(visit({ state: 'InProgress' })) : undefined)))
    render('/my-visits?visit=v1')
    await userEvent.click(await screen.findByRole('button', { name: 'Som na mieste' }))
    await waitFor(() => expect(calls.some((c) => c.path === '/api/visits/v1/start' && c.method === 'POST')).toBe(true))
    expect(await screen.findByText('Výjazd je začatý.')).toBeInTheDocument()
  })

  it('validates and sends the service protocol of a visit in progress', async () => {
    const started = new Date(2026, 10, 5, 7, 4).toISOString()
    const calls = stubFetch(
      handler([entry({ state: 'InProgress' })], visit({ state: 'InProgress', startedAt: started }), (c) =>
        c.path === '/api/visits/v1/report' ? json(visit({ state: 'Done' })) : undefined,
      ),
    )
    render('/my-visits?visit=v1')
    const form = await screen.findByRole('form', { name: 'Servisný protokol' })
    expect(within(form).getByLabelText(/Príchod/)).toHaveValue('2026-11-05T07:04')
    await userEvent.click(within(form).getByRole('button', { name: 'Odoslať protokol' }))
    expect(within(form).getAllByText('Pole je povinné.')).toHaveLength(3)
    expect(calls.some((c) => c.method === 'POST')).toBe(false)

    await userEvent.selectOptions(within(form).getByLabelText(/Príčina/), 'WiringFault')
    await userEvent.type(within(form).getByLabelText(/Zistenie/), 'Poškodený konektor')
    await userEvent.type(within(form).getByLabelText(/Vykonané práce/), 'Výmena konektora')
    await userEvent.click(within(form).getByRole('button', { name: 'Odoslať protokol' }))
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/api/visits/v1/report')?.body).toEqual({
        cause: 'WiringFault',
        findings: 'Poškodený konektor',
        workDone: 'Výmena konektora',
        arrivedAt: started,
        departedAt: new Date(2026, 10, 5, 7, 30).toISOString(),
      }),
    )
    expect(await screen.findByText('Protokol bol odoslaný.')).toBeInTheDocument()
  })

  it('tells when the visit is not in the selected period', async () => {
    stubFetch(handler([]))
    render('/my-visits?visit=v9')
    expect(await screen.findByText('Výjazd sa v tomto období nenašiel.')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'Všetky výjazdy' })[0]).toHaveAttribute('href', '/my-visits')
  })
})

describe('my visits navigation', () => {
  it('is in the Servis menu only for technicians', () => {
    expect(menuItems(['Technician']).find((i) => i.path === '/my-visits')?.group).toBe('Servis')
    for (const role of ['Admin', 'Operator', 'Customer', 'Sales']) expect(menuItems([role]).some((i) => i.path === '/my-visits')).toBe(false)
  })
})

describe('service protocol draft', () => {
  // Before the mocked current time, so the default departure is not earlier than the arrival.
  const started = new Date(2026, 10, 5, 7, 0).toISOString()
  const inProgress = (extra?: (c: Call) => Response | undefined) =>
    handler([entry({ state: 'InProgress' })], visit({ state: 'InProgress', startedAt: started }), extra)
  const open = () => render('/my-visits?visit=v1')
  const findings = () => screen.findByLabelText('Zistenie *')
  const reportCalls = (calls: Call[]) => calls.filter((c) => c.path === '/api/visits/v1/report')

  async function fill() {
    await userEvent.selectOptions(await screen.findByRole('combobox'), 'InverterFault')
    await userEvent.type(await findings(), 'Chyba izolácie')
    await userEvent.type(screen.getByLabelText('Vykonané práce *'), 'Výmena poistky')
  }

  it('restores what was typed after leaving the page and clears it after sending', async () => {
    const calls = stubFetch(inProgress((c) => (c.path === '/api/visits/v1/report' ? json(visit({ state: 'Done' })) : undefined)))
    const first = open()
    await fill()
    first.unmount()

    open()

    expect(await findings()).toHaveValue('Chyba izolácie')
    expect(screen.getByText(/Obnovili sme rozpracovaný protokol/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Odoslať protokol' }))

    await waitFor(() => expect(reportCalls(calls)).toHaveLength(1))
    await waitFor(() => expect(localStorage.getItem('solarops.visit-protocol.v1')).toBeNull())
  })

  it('starts over when the technician discards the draft', async () => {
    stubFetch(inProgress())
    const first = open()
    await fill()
    first.unmount()
    open()
    await findings()

    await userEvent.click(screen.getByRole('button', { name: 'Začať odznova' }))

    expect(await findings()).toHaveValue('')
    expect(localStorage.getItem('solarops.visit-protocol.v1')).toBeNull()
  })

  it('waits for the connection when offline and sends the protocol when it returns', async () => {
    const onLine = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    const calls = stubFetch(inProgress((c) => (c.path === '/api/visits/v1/report' ? json(visit({ state: 'Done' })) : undefined)))
    open()
    await fill()

    await userEvent.click(screen.getByRole('button', { name: 'Odoslať protokol' }))

    expect(await screen.findByText(/odošle sa po obnovení pripojenia/)).toHaveAttribute('role', 'status')
    expect(reportCalls(calls)).toHaveLength(0)
    expect(JSON.parse(localStorage.getItem('solarops.visit-protocol.v1')!).waiting).toBe(true)

    onLine.mockReturnValue(true)
    window.dispatchEvent(new Event('online'))

    await waitFor(() => expect(reportCalls(calls)).toHaveLength(1))
    expect(reportCalls(calls)[0].body).toMatchObject({ findings: 'Chyba izolácie', workDone: 'Výmena poistky', cause: 'InverterFault' })
  })

  it('sends a waiting protocol right after reopening the page with a connection', async () => {
    const onLine = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    const calls = stubFetch(inProgress((c) => (c.path === '/api/visits/v1/report' ? json(visit({ state: 'Done' })) : undefined)))
    const first = open()
    await fill()
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať protokol' }))
    await screen.findByText(/odošle sa po obnovení pripojenia/)
    first.unmount()

    onLine.mockReturnValue(true)
    open()

    await waitFor(() => expect(reportCalls(calls)).toHaveLength(1))
  })

  it('keeps the draft when the server refuses the protocol', async () => {
    stubFetch(inProgress((c) => (c.path === '/api/visits/v1/report' ? problem(409, 'Already reported') : undefined)))
    open()
    await fill()

    await userEvent.click(screen.getByRole('button', { name: 'Odoslať protokol' }))

    await waitFor(() => expect(screen.getAllByRole('alert').length).toBeGreaterThan(0))
    expect(localStorage.getItem('solarops.visit-protocol.v1')).not.toBeNull()
  })
})
