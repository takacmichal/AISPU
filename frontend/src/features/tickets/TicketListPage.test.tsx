import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import TicketListPage from './TicketListPage'
import { json, problem, renderRoute, stubFetch, ticketItem, ticketList } from './testUtils'

afterEach(() => vi.unstubAllGlobals())

const render = (roles = ['Admin'], route = '/tickets?view=list') => renderRoute(<TicketListPage />, route, '/tickets', roles)

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

  it('starts on the Kanban for planners and on the list for everybody else', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem()])) : undefined))
    const first = render(['Operator'], '/tickets')
    expect(await screen.findByRole('region', { name: 'Nástenka tiketov podľa stavu' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Kanban' })).toBeChecked()
    first.unmount()
    render(['Customer'], '/tickets')
    expect(await screen.findByRole('cell', { name: 'Nový' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Zoznam' })).toBeChecked()
  })

  it('switches the view with the segmented control and keeps it in the URL', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem()])) : undefined))
    const { router } = render(['Admin'])
    await screen.findByRole('cell', { name: 'Nový' })
    await userEvent.click(screen.getByRole('radio', { name: 'Kanban' }))
    expect(await screen.findByRole('region', { name: 'Nástenka tiketov podľa stavu' })).toBeInTheDocument()
    expect(router.state.location.search).toContain('view=board')
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
    stubFetch(({ path }) => {
      if (path === '/api/installations') return json([{ id: 'i1', name: 'FVE Nitra' }])
      if (path === '/api/tickets')
        return json(
          ticketList([
            ticketItem({ priority: 'High', origin: 'AnomalyDetected' }),
            ticketItem({ id: 't2', title: 'Poistka', state: 'Resolved', priority: 'Low' }),
          ]),
        )
    })
    board()
    const region = await screen.findByRole('region', { name: 'Nástenka tiketov podľa stavu' })
    expect(within(region).getAllByRole('heading', { level: 3 })).toHaveLength(8)
    const nový = within(region).getByRole('heading', { name: /Nový/ }).closest('section')!
    expect(within(nový).getByText('Invertor nesvieti')).toBeInTheDocument()
    expect(within(nový).getByText('Vysoká')).toBeInTheDocument()
    expect(within(nový).getByText('Automatická detekcia')).toBeInTheDocument()
    expect(await within(nový).findByText('FVE Nitra')).toBeInTheDocument()
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

describe('TicketListPage Kanban drag and drop', () => {
  const dataTransfer = { setData: vi.fn(), effectAllowed: '' }
  const column = (name: RegExp) => screen.getByRole('heading', { name }).closest('section')!
  const card = (title: string) => screen.getByRole('link', { name: title }).closest('li')!

  function drag(title: string, target: RegExp) {
    fireEvent.dragStart(card(title), { dataTransfer })
    const over = column(target)
    const accepted = !fireEvent.dragOver(over, { dataTransfer })
    fireEvent.drop(over, { dataTransfer })
    return accepted
  }

  const board = async (roles = ['Admin']) => {
    render(roles, '/tickets?view=board')
    await screen.findByRole('link', { name: 'Invertor nesvieti' })
  }

  it('moves the card at once, before the server answers, and without a dialog', async () => {
    let answer: (response: Response) => void = () => {}
    const calls = stubFetch(({ path, method }) => {
      if (path === '/api/tickets' && method === 'GET') return json(ticketList([ticketItem()]))
      if (path === '/api/tickets/t1/transition') return new Promise<Response>((resolve) => (answer = resolve))
    })
    await board()

    expect(drag('Invertor nesvieti', /Zatriedený/)).toBe(true)

    expect(within(column(/Zatriedený/)).getByRole('link', { name: 'Invertor nesvieti' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await waitFor(() => expect(calls.find((c) => c.path === '/api/tickets/t1/transition')?.body).toEqual({ state: 'Triaged' }))

    answer(json({ ...ticketItem({ state: 'Triaged' }), allowedTransitions: [], attachments: [] }))

    expect(await screen.findByText('Stav tiketu bol zmenený na „Zatriedený“.')).toBeInTheDocument()
    expect(within(column(/Zatriedený/)).getByRole('link', { name: 'Invertor nesvieti' })).toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/tickets' && c.method === 'GET')).toHaveLength(1)
  })

  it('returns the card to its column when the server refuses the move', async () => {
    stubFetch(({ path, method }) => {
      if (path === '/api/tickets' && method === 'GET') return json(ticketList([ticketItem()]))
      if (path === '/api/tickets/t1/transition') return problem(409, 'Invalid transition')
    })
    await board()

    drag('Invertor nesvieti', /Zatriedený/)

    expect(await screen.findByText('Tento prechod stavu nie je možný. Obnovte stránku a skúste to znova.')).toBeInTheDocument()
    expect(within(column(/Nový/)).getByRole('link', { name: 'Invertor nesvieti' })).toBeInTheDocument()
  })

  it('refuses columns the ticket cannot move to', async () => {
    const calls = stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem()])) : undefined))
    await board()

    expect(drag('Invertor nesvieti', /Vyriešený/)).toBe(false)

    expect(calls.some((c) => c.method === 'POST')).toBe(false)
    expect(within(column(/Nový/)).getByRole('link', { name: 'Invertor nesvieti' })).toBeInTheDocument()
  })

  it('refuses the visit-driven column', async () => {
    const calls = stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem({ state: 'RemoteDiagnosis' })])) : undefined))
    await board()

    expect(drag('Invertor nesvieti', /Výjazd naplánovaný/)).toBe(false)

    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('asks for confirmation before closing a ticket', async () => {
    const calls = stubFetch(({ path, method }) => {
      if (path === '/api/tickets' && method === 'GET') return json(ticketList([ticketItem({ state: 'Resolved' })]))
      if (path === '/api/tickets/t1/transition') return json({ ...ticketItem({ state: 'Closed' }), allowedTransitions: [], attachments: [] })
    })
    await board()

    drag('Invertor nesvieti', /Uzavretý/)

    const dialog = await screen.findByRole('dialog')
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Uzavretý' }))
    await waitFor(() => expect(calls.find((c) => c.path === '/api/tickets/t1/transition')?.body).toEqual({ state: 'Closed' }))
  })

  it('does not let roles without the manage permission drag cards', async () => {
    stubFetch(({ path }) => (path === '/api/tickets' ? json(ticketList([ticketItem()])) : undefined))
    await board(['Technician'])

    expect(card('Invertor nesvieti')).toHaveAttribute('draggable', 'false')
    expect(screen.queryByText(/Kartu presuniete myšou/)).not.toBeInTheDocument()
  })
})
