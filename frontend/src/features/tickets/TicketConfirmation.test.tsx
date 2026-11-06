import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import TicketDetailPage from './TicketDetailPage'
import type { Call } from '../installations/testHelpers'
import { json, problem, renderRoute, stubFetch, ticketDetail } from './testUtils'

afterEach(() => vi.unstubAllGlobals())

const daysAgo = (days: number) => new Date(Date.now() - days * 86_400_000).toISOString()

const closedHistory = (closedDaysAgo: number) => [
  { id: 'h1', fromState: null, toState: 'New', changedBy: 'u1', changedAt: daysAgo(closedDaysAgo + 3), note: null },
  { id: 'h2', fromState: 'Verified', toState: 'Resolved', changedBy: 'u2', changedAt: daysAgo(closedDaysAgo), note: null },
  { id: 'h3', fromState: 'Resolved', toState: 'Closed', changedBy: 'u2', changedAt: daysAgo(closedDaysAgo), note: null },
]

function api(state: string, closedDaysAgo: number, extra?: (call: Call) => Response | undefined) {
  let ticket = ticketDetail({ state, allowedTransitions: [] })
  const calls = stubFetch((call) => {
    const custom = extra?.(call)
    if (custom) return custom
    if (call.path === '/api/tickets/t1/reopen' && call.method === 'POST') {
      ticket = ticketDetail({ state: 'RemoteDiagnosis', allowedTransitions: [] })
      return json({ ticketId: 't1', decision: 'Reopened', comment: 'x', decidedAt: daysAgo(0), ticketState: 'RemoteDiagnosis' })
    }
    if (call.path === '/api/tickets/t1' && call.method === 'GET') return json(ticket)
    if (call.path === '/api/tickets/t1/history') return json(closedHistory(closedDaysAgo))
    if (call.path === '/api/tickets/t1/comments') return json([])
    if (call.path === '/api/tickets/t1/visits') return json({ ticketId: 't1', assignedTechnicianId: null, visits: [] })
    if (call.path === '/api/installations') return json([])
    return undefined
  })
  return calls
}

const render = (roles = ['Customer']) => renderRoute(<TicketDetailPage />, '/tickets/t1', '/tickets/:id', roles)
const card = () => screen.findByRole('region', { name: 'Bola porucha odstránená?' })

describe('customer confirmation of a closed ticket', () => {
  it('confirms the fix with an optional comment', async () => {
    const calls = api('Closed', 2, (c) =>
      c.path === '/api/tickets/t1/confirm'
        ? json({ ticketId: 't1', decision: 'Confirmed', comment: 'Ďakujem', decidedAt: daysAgo(0), ticketState: 'Closed' })
        : undefined,
    )
    render()
    const region = await card()
    await userEvent.type(within(region).getByLabelText('Komentár (nepovinné pri potvrdení)'), 'Ďakujem')
    await userEvent.click(within(region).getByRole('button', { name: 'Potvrdiť opravu' }))
    expect(await within(region).findByText('Ďakujeme, opravu ste potvrdili.')).toBeInTheDocument()
    expect(calls.find((c) => c.path === '/api/tickets/t1/confirm')?.body).toEqual({ comment: 'Ďakujem' })
    expect(within(region).queryByRole('button', { name: 'Potvrdiť opravu' })).not.toBeInTheDocument()
  })

  it('treats a repeated confirmation as already confirmed', async () => {
    api('Closed', 2, (c) => (c.path === '/api/tickets/t1/confirm' ? problem(409, 'The ticket has already been confirmed.') : undefined))
    render()
    const region = await card()
    await userEvent.click(within(region).getByRole('button', { name: 'Potvrdiť opravu' }))
    expect(await within(region).findByText('Opravu ste už potvrdili.')).toBeInTheDocument()
    expect(screen.queryByText(/already been confirmed/)).not.toBeInTheDocument()
  })

  it('reopens the ticket within 7 days with a required reason', async () => {
    const calls = api('Closed', 2)
    render()
    const region = await card()
    await userEvent.click(await within(region).findByRole('button', { name: 'Znovu otvoriť tiket' }))
    expect(within(region).getByText('Pri znovuotvorení napíšte, čo stále nefunguje.')).toBeInTheDocument()
    expect(calls.some((c) => c.path.endsWith('/reopen'))).toBe(false)
    await userEvent.type(within(region).getByLabelText('Komentár (nepovinné pri potvrdení)'), 'Stále nevyrába')
    await userEvent.click(within(region).getByRole('button', { name: 'Znovu otvoriť tiket' }))
    await waitFor(() => expect(calls.find((c) => c.path === '/api/tickets/t1/reopen')?.body).toEqual({ reason: 'Stále nevyrába' }))
    expect(await screen.findByText('Tiket bol znovu otvorený.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Bola porucha odstránená?' })).not.toBeInTheDocument())
  })

  it('does not offer reopening after 7 days', async () => {
    api('Closed', 9)
    render()
    const region = await card()
    expect(await within(region).findByText(/Znovu otvoriť tiket možno len do 7 dní od uzavretia/)).toBeInTheDocument()
    expect(within(region).getByRole('button', { name: 'Potvrdiť opravu' })).toBeInTheDocument()
    expect(within(region).queryByRole('button', { name: 'Znovu otvoriť tiket' })).not.toBeInTheDocument()
  })

  it('is shown only to the customer and only for a closed ticket', async () => {
    api('Closed', 2)
    const { unmount } = render(['Operator'])
    expect(await screen.findByRole('heading', { name: 'Invertor nesvieti', level: 2 })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Bola porucha odstránená?' })).not.toBeInTheDocument()
    unmount()
    api('RemoteDiagnosis', 2)
    render()
    expect(await screen.findByRole('heading', { name: 'Invertor nesvieti', level: 2 })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Bola porucha odstránená?' })).not.toBeInTheDocument()
  })
})
