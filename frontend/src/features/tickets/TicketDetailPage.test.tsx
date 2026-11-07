import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import TicketDetailPage from './TicketDetailPage'
import { buildTimeline } from './timeline'
import { json, problem, renderRoute, stubFetch, ticketDetail } from './testUtils'

afterEach(() => vi.unstubAllGlobals())

const history = [
  { id: 'h1', fromState: null, toState: 'New', changedBy: 'aaaaaaaa-1', changedAt: '2026-03-01T08:00:00Z', note: null },
  { id: 'h2', fromState: 'New', toState: 'Triaged', changedBy: 'bbbbbbbb-2', changedAt: '2026-03-01T10:00:00Z', note: 'Vysoká priorita' },
]
const comments = [
  { id: 'c1', authorId: 'bbbbbbbb-2', body: 'Interná poznámka', isPublic: false, createdAt: '2026-03-01T09:00:00Z' },
  { id: 'c2', authorId: 'bbbbbbbb-2', body: 'Technik sa ozve', isPublic: true, createdAt: '2026-03-01T11:00:00Z' },
]

function api(ticket = ticketDetail(), extra?: (path: string, method: string) => Response | undefined) {
  return stubFetch(({ path, method }) => {
    if (path === '/api/tickets/t1' && method === 'GET') return json(ticket)
    if (path === '/api/tickets/t1/history') return json(history)
    if (path === '/api/tickets/t1/comments' && method === 'GET') return json(comments)
    return extra?.(path, method)
  })
}
const report = {
  cause: 'InverterFault',
  findings: 'Poškodená izolácia konektora',
  workDone: 'Výmena konektora MC4',
  arrivedAt: '2026-11-03T08:04:00Z',
  departedAt: '2026-11-03T10:26:00Z',
  reportedBy: 'tech1',
  reportedAt: '2026-11-03T10:30:00Z',
}
const visit = (over: Record<string, unknown> = {}) => ({
  id: 'v1',
  ticketId: 't1',
  technicianId: 'tech1',
  state: 'Planned',
  plannedStart: '2026-11-03T08:00:00Z',
  plannedEnd: '2026-11-03T10:00:00Z',
  estimatedMinutes: 120,
  startedAt: null,
  report: null,
  reschedules: [],
  materials: [{ id: 'm1', name: 'Konektor MC4', quantity: 2, unit: 'ks' }],
  deviceReplacements: [],
  ...over,
})
const render = (roles = ['Admin']) => renderRoute(<TicketDetailPage />, '/tickets/t1', '/tickets/:id', roles)

describe('buildTimeline', () => {
  it('merges history and comments chronologically', () => {
    const order = buildTimeline(history as never, comments as never).map((e) => (e.kind === 'state' ? e.change.id : e.comment.id))
    expect(order).toEqual(['h1', 'c1', 'h2', 'c2'])
  })
})

describe('TicketDetailPage', () => {
  it('shows header, merged timeline and public/internal markers', async () => {
    api()
    render()
    expect(await screen.findByRole('heading', { name: 'Invertor nesvieti' })).toBeInTheDocument()
    const items = await screen.findAllByRole('listitem')
    const texts = items.map((i) => i.textContent ?? '')
    const idx = (s: string) => texts.findIndex((t) => t.includes(s))
    expect(idx('Tiket vytvorený')).toBeLessThan(idx('Interná poznámka'))
    expect(idx('Interná poznámka')).toBeLessThan(idx('Zmena stavu: Nový → Zatriedený'))
    expect(idx('Zmena stavu: Nový → Zatriedený')).toBeLessThan(idx('Technik sa ozve'))
    expect(screen.getByText('Interný')).toBeInTheDocument()
    expect(screen.getByText('Verejný')).toBeInTheDocument()
    expect(screen.getByText('Vysoká priorita')).toBeInTheDocument()
  })

  it('shows an error when the ticket cannot be loaded', async () => {
    stubFetch(() => problem(404, 'x'))
    render()
    expect(await screen.findByText('Tiket sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('posts an internal comment by default and a public one when switched', async () => {
    const calls = api(ticketDetail(), (path, method) => (path === '/api/tickets/t1/comments' && method === 'POST' ? json(comments[0], 201) : undefined))
    render(['Technician'])
    const box = await screen.findByLabelText('Nový komentár')
    await userEvent.type(box, 'Prvý')
    await userEvent.click(screen.getByRole('button', { name: 'Pridať komentár' }))
    await waitFor(() => expect(calls.filter((c) => c.method === 'POST')).toHaveLength(1))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ body: 'Prvý', isPublic: false })
    await screen.findByText('Komentár bol pridaný.')
    await userEvent.type(screen.getByLabelText('Nový komentár'), 'Druhý')
    await userEvent.click(screen.getByLabelText(/Verejný komentár/))
    await userEvent.click(screen.getByRole('button', { name: 'Pridať komentár' }))
    await waitFor(() => expect(calls.filter((c) => c.method === 'POST')).toHaveLength(2))
    expect(calls.filter((c) => c.method === 'POST')[1].body).toEqual({ body: 'Druhý', isPublic: true })
  })

  it('validates an empty comment', async () => {
    const calls = api()
    render()
    await userEvent.click(await screen.findByRole('button', { name: 'Pridať komentár' }))
    expect(screen.getByText('Napíšte text komentára.')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('hides comment form, triage, transitions and assignment from a customer', async () => {
    api()
    render(['Customer'])
    await screen.findByRole('heading', { name: 'Invertor nesvieti' })
    expect(screen.queryByLabelText('Nový komentár')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Zatriediť tiket' })).not.toBeInTheDocument()
    expect(screen.queryByText('Priradený technik')).not.toBeInTheDocument()
  })

  it('never shows internal comments to a customer', async () => {
    api()
    render(['Customer'])
    expect(await screen.findByText('Technik sa ozve')).toBeInTheDocument()
    expect(screen.queryByText('Interná poznámka')).not.toBeInTheDocument()
    expect(screen.queryByText('Interný')).not.toBeInTheDocument()
  })

  it("shows an error instead of another customer's ticket", async () => {
    stubFetch(({ path }) => (path.startsWith('/api/tickets/t9') ? problem(404, 'Not found') : undefined))
    renderRoute(<TicketDetailPage />, '/tickets/t9', '/tickets/:id', ['Customer'])
    expect(await screen.findByText('Tiket sa nepodarilo načítať.')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 2 })).not.toBeInTheDocument()
  })

  it('assigns a technician from the cards', async () => {
    const calls = api(ticketDetail({ state: 'Triaged', allowedTransitions: ['RemoteDiagnosis'] }), (path, method) => {
      if (path === '/api/installations/technicians') return json([{ id: 'tech1', userName: 'Tomáš Varga', email: 'tomas@test.sk' }, { id: 'tech2', userName: null, email: 'juraj@test.sk' }])
      if (path === '/api/tickets/t1/visits') return json({ ticketId: 't1', assignedTechnicianId: 'tech2', visits: [] })
      if (path === '/api/tickets/t1/technician' && method === 'PUT') return json({ ticketId: 't1', technicianId: 'tech1' })
    })
    render(['Operator'])
    const picker = await screen.findByRole('group', { name: 'Priradený technik' })
    expect(await within(picker).findByRole('radio', { name: 'Priradiť technika juraj@test.sk' })).toBeChecked()
    await userEvent.click(within(picker).getByRole('radio', { name: 'Priradiť technika Tomáš Varga' }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ technicianId: 'tech1' }))
    expect(await screen.findByText('Tiket priradený: Tomáš Varga')).toBeInTheDocument()
  })

  it('shows the workload of technicians and still assigns when it cannot be loaded', async () => {
    const calls = stubFetch(({ path, method }) => {
      if (path === '/api/tickets/t1' ) return json(ticketDetail({ state: 'Triaged' }))
      if (path === '/api/installations/technicians') return json([{ id: 'tech1', userName: 'Tomáš Varga', email: null }, { id: 'tech2', userName: 'Eva Malá', email: null }])
      if (path === '/api/tickets/t1/visits') return json({ ticketId: 't1', assignedTechnicianId: null, visits: [] })
      if (path === '/api/technicians/tech1/visits')
        return json({ technicianId: 'tech1', from: '', to: '', visits: [{ state: 'Planned' }, { state: 'InProgress' }, { state: 'Done' }, { state: 'Cancelled' }] })
      if (path === '/api/technicians/tech2/visits') return problem(500, 'boom')
      if (path === '/api/tickets/t1/technician' && method === 'PUT') return json({ ticketId: 't1', technicianId: 'tech2' })
      if (path === '/api/tickets/t1/history' || path === '/api/tickets/t1/comments') return json([])
    })
    render()

    const first = await screen.findByRole('radio', { name: /Tomáš Varga/ })
    expect(await within(first.closest('label')!).findByText('Aktívne výjazdy na 7 dní: 2')).toBeInTheDocument()
    const second = screen.getByRole('radio', { name: /Eva Malá/ })
    expect(within(second.closest('label')!).queryByText(/Aktívne výjazdy/)).not.toBeInTheDocument()

    await userEvent.click(second)

    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ technicianId: 'tech2' }))
  })

  it('plans a visit for the assigned technician', async () => {
    const calls = api(ticketDetail({ state: 'RemoteDiagnosis', allowedTransitions: ['Resolved', 'VisitScheduled'] }), (path, method) => {
      if (path === '/api/installations/technicians') return json([{ id: 'tech1', userName: 'Tomáš Varga', email: null }])
      if (path === '/api/tickets/t1/visits' && method === 'GET') return json({ ticketId: 't1', assignedTechnicianId: 'tech1', visits: [] })
      if (path === '/api/tickets/t1/visits' && method === 'POST') return json(visit(), 201)
    })
    render(['Operator'])
    await userEvent.type(await screen.findByLabelText('Začiatok výjazdu'), '2026-11-05T08:00')
    await userEvent.clear(screen.getByLabelText('Odhadované trvanie (min)'))
    await userEvent.type(screen.getByLabelText('Odhadované trvanie (min)'), '5')
    await userEvent.click(screen.getByRole('button', { name: 'Naplánovať výjazd' }))
    expect(await screen.findByText('Trvanie musí byť 15 až 720 minút.')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Odhadované trvanie (min)'), '0')
    await userEvent.click(screen.getByRole('button', { name: 'Naplánovať výjazd' }))
    await waitFor(() =>
      expect(calls.find((c) => c.path === '/api/tickets/t1/visits' && c.method === 'POST')?.body).toEqual({
        start: new Date(2026, 10, 5, 8, 0).toISOString(),
        estimatedMinutes: 50,
      }),
    )
  })

  it('shows visits, the service protocol and lets the technician verify production', async () => {
    const calls = api(ticketDetail({ state: 'OnSite', allowedTransitions: ['Verified'] }), (path, method) => {
      if (path === '/api/tickets/t1/visits') return json({ ticketId: 't1', assignedTechnicianId: null, visits: [visit({ state: 'Done', report })] })
      if (path === '/api/tickets/t1/verify' && method === 'POST') return json({ ticketId: 't1', state: 'Verified' })
    })
    render(['Technician'])
    const visits = await screen.findByRole('article', { name: 'Výjazdy' })
    expect(await within(visits).findByText('Dokončený')).toBeInTheDocument()
    const protocol = screen.getByRole('article', { name: 'Servisný protokol' })
    expect(within(protocol).getByText('Porucha striedača')).toBeInTheDocument()
    expect(within(protocol).getByText('Poškodená izolácia konektora')).toBeInTheDocument()
    expect(within(protocol).getByText('Konektor MC4 2 ks')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Overiť obnovenie výroby' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Overiť obnovenie výroby' }))
    await waitFor(() => expect(calls.some((c) => c.path === '/api/tickets/t1/verify' && c.method === 'POST')).toBe(true))
  })

  it('triages a new ticket for Operator', async () => {
    const calls = api(ticketDetail(), (path, method) =>
      path === '/api/tickets/t1/triage' && method === 'PUT' ? json(ticketDetail({ state: 'Triaged', priority: 'High', allowedTransitions: ['RemoteDiagnosis'] })) : undefined,
    )
    render(['Operator'])
    await userEvent.selectOptions(await screen.findByLabelText('Priorita', { selector: 'select' }), 'High')
    await userEvent.click(screen.getByRole('button', { name: 'Zatriediť tiket' }))
    await waitFor(() => expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ priority: 'High' }))
  })

  it('offers only transitions from allowedTransitions and explains the visit slot', async () => {
    api(ticketDetail({ state: 'RemoteDiagnosis', allowedTransitions: ['Resolved', 'VisitScheduled'] }))
    render()
    const group = await screen.findByRole('group', { name: 'Zmena stavu' })
    expect(within(group).getByRole('button', { name: 'Vyriešený' })).toBeInTheDocument()
    expect(within(group).queryByRole('button', { name: 'Výjazd naplánovaný' })).not.toBeInTheDocument()
    expect(screen.getByText(/plánovaním výjazdu/)).toBeInTheDocument()
  })

  it('lists attachments with a download button', async () => {
    api(ticketDetail({ attachments: [{ id: 'a1', fileName: 'foto.png', contentType: 'image/png', sizeBytes: 2048, createdAt: '2026-03-01T08:00:00Z', downloadUrl: '/api/tickets/t1/attachments/a1' }] }))
    render()
    expect(await screen.findByRole('button', { name: 'Stiahnuť: foto.png' })).toBeInTheDocument()
  })
})
