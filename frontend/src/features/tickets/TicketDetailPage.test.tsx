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
    expect(screen.queryByText('Pridelenie technika')).not.toBeInTheDocument()
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
    expect(screen.getByText(/plánovaní výjazdov/)).toBeInTheDocument()
  })

  it('lists attachments with a download button', async () => {
    api(ticketDetail({ attachments: [{ id: 'a1', fileName: 'foto.png', contentType: 'image/png', sizeBytes: 2048, createdAt: '2026-03-01T08:00:00Z', downloadUrl: '/api/tickets/t1/attachments/a1' }] }))
    render()
    expect(await screen.findByRole('button', { name: 'Stiahnuť: foto.png' })).toBeInTheDocument()
  })
})
