import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import TicketListPage from './TicketListPage'
import { json, problem, renderRoute, stubFetch, ticketItem, ticketList } from './testUtils'

afterEach(() => vi.unstubAllGlobals())

const render = (roles = ['Admin'], route = '/tickets') => renderRoute(<TicketListPage />, route, '/tickets', roles)

describe('TicketListPage table', () => {
  it('shows rows with state and priority badges', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem({ priority: 'High' })])) : undefined))
    render()
    expect(await screen.findByRole('link', { name: 'Invertor nesvieti' })).toHaveAttribute('href', '/tickets/t1')
    expect(screen.getByRole('cell', { name: 'Nový' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Vysoká' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Hlásenie zákazníka' })).toBeInTheDocument()
  })

  it('shows empty and error states', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([])) : undefined))
    const first = render()
    expect(await screen.findByText('Zatiaľ nie sú evidované žiadne servisné tikety.')).toBeInTheDocument()
    first.unmount()
    stubFetch(() => problem(500, 'boom'))
    render()
    expect(await screen.findByText('Zoznam tiketov sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('sends filters to the API and keeps them in the URL', async () => {
    const calls = stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem()])) : undefined))
    const { router } = render()
    await screen.findByText('Invertor nesvieti')
    await userEvent.selectOptions(screen.getByLabelText('Priorita'), 'High')
    await waitFor(() => expect(calls.some((c) => c.search.includes('priority=High'))).toBe(true))
    expect(router.state.location.search).toContain('priority=High')
  })

  it('lets a customer report a fault', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([])) : undefined))
    render(['Customer'])
    expect(await screen.findByRole('link', { name: 'Nahlásiť poruchu' })).toHaveAttribute('href', '/tickets/new')
  })
})

describe('TicketListPage Kanban', () => {
  const board = (roles = ['Admin']) =>
    render(roles, '/tickets?view=board')

  it('groups cards by state', async () => {
    stubFetch(({ path }) =>
      path === '/api/tickets'
        ? json(ticketList([ticketItem(), ticketItem({ id: 't2', title: 'Poistka', state: 'Resolved', priority: 'Low' })]))
        : undefined,
    )
    board()
    const region = await screen.findByRole('region', { name: 'Nástenka tiketov podľa stavu' })
    const nový = within(region).getByRole('heading', { name: /Nový/ }).closest('section')!
    expect(within(nový).getByText('Invertor nesvieti')).toBeInTheDocument()
    const done = within(region).getByRole('heading', { name: /Vyriešený/ }).closest('section')!
    expect(within(done).getByText('Poistka')).toBeInTheDocument()
    expect(within(done).getByText('Nízka')).toBeInTheDocument()
  })

  it('moves a card through a keyboard-operable button after confirmation', async () => {
    const calls = stubFetch(({ path, method }) => {
      if (path === '/api/tickets' && method === 'GET') return json(ticketList([ticketItem()]))
      if (path === '/api/tickets/t1/transition') return json({ ...ticketItem({ state: 'Triaged' }), allowedTransitions: [], attachments: [] })
    })
    board()
    await userEvent.click(await screen.findByRole('button', { name: /Presunúť do stavu „Zatriedený“/ }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Zatriedený' }))
    await waitFor(() => expect(calls.find((c) => c.path === '/api/tickets/t1/transition')?.body).toEqual({ state: 'Triaged' }))
    expect(await screen.findByText('Stav tiketu bol zmenený na „Zatriedený“.')).toBeInTheDocument()
  })

  it('does not call the API when the confirmation is cancelled', async () => {
    const calls = stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem()])) : undefined))
    board()
    await userEvent.click(await screen.findByRole('button', { name: /Presunúť do stavu/ }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Zrušiť' }))
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows a Slovak message on 409', async () => {
    stubFetch(({ path, method }) => {
      if (path === '/api/tickets' && method === 'GET') return json(ticketList([ticketItem()]))
      if (path === '/api/tickets/t1/transition') return problem(409, 'Invalid transition')
    })
    board()
    await userEvent.click(await screen.findByRole('button', { name: /Presunúť do stavu/ }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Zatriedený' }))
    expect(await screen.findByText('Tento prechod stavu nie je možný. Obnovte stránku a skúste to znova.')).toBeInTheDocument()
  })

  it('offers no move buttons to roles that cannot manage tickets', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem()])) : undefined))
    board(['Technician'])
    await screen.findByText('Invertor nesvieti')
    expect(screen.queryByRole('button', { name: /Presunúť do stavu/ })).not.toBeInTheDocument()
  })

  it('does not offer visit-driven states', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem({ state: 'RemoteDiagnosis' })])) : undefined))
    board()
    await screen.findByText('Invertor nesvieti')
    expect(screen.getByRole('button', { name: /Vyriešený/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Výjazd naplánovaný/ })).not.toBeInTheDocument()
  })
})
