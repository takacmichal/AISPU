import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { QUOTE_ID, customersPage, json, makeQuote, mockApi, problem, renderQuotes } from './testUtils'

const history = json([
  { fromStatus: null, toStatus: 'Draft', changedBy: 'u', changedAt: '2026-11-03T10:00:00Z', note: null },
  { fromStatus: 'Draft', toStatus: 'Sent', changedBy: 'u', changedAt: '2026-11-03T11:00:00Z', note: 'Poslané e-mailom' },
])
const path = `/quotes/${QUOTE_ID}`

describe('QuoteDetailPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows lines, totals and the history timeline', async () => {
    mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote({ status: 'Sent', allowedTransitions: ['Accepted', 'Rejected'] })),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
    })
    renderQuotes(path)

    expect(screen.getByText('Načítavam ponuku…')).toBeInTheDocument()
    expect(await screen.findByText('PV-400 – Panel 400 W')).toBeInTheDocument()
    expect(screen.getByText(/1\s?230,00/)).toBeInTheDocument()
    expect(screen.getByText(/DPH \(23 %\)/)).toBeInTheDocument()
    const timeline = await screen.findByRole('list', { name: 'História stavov' })
    expect(within(timeline).getAllByRole('listitem')).toHaveLength(2)
    expect(within(timeline).getByText('Poslané e-mailom')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Ján Novák' })).toBeInTheDocument()
  })

  it('shows an error when the quote is missing', async () => {
    mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: problem(404, 'Quote not found'),
      [`GET /api/quotes/${QUOTE_ID}/history`]: problem(404, 'x'),
      'GET /api/customers': customersPage,
    })
    renderQuotes(path)
    expect(await screen.findByText('Ponuku sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('sends a draft quote with validity days and reloads', async () => {
    let status = 'Draft'
    const fetchMock = mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: () => json(makeQuote({ status, allowedTransitions: status === 'Draft' ? ['Sent'] : ['Accepted', 'Rejected'] })),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
      [`POST /api/quotes/${QUOTE_ID}/send`]: () => {
        status = 'Sent'
        return json(makeQuote({ status: 'Sent' }))
      },
    })
    renderQuotes(path)
    await userEvent.click(await screen.findByRole('button', { name: 'Odoslať ponuku' }))
    const dialog = screen.getByRole('dialog')
    const days = within(dialog).getByLabelText('Platnosť (dni)')
    await userEvent.clear(days)
    await userEvent.type(days, '14')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Odoslať' }))

    expect(await screen.findByText('Ponuka bola odoslaná.')).toBeInTheDocument()
    const post = fetchMock.mock.calls.find(([u]) => String(u).endsWith('/send'))!
    expect(JSON.parse(String(post[1]?.body))).toMatchObject({ validityDays: 14 })
    await waitFor(() => expect(document.querySelector('.quote-meta .badge')).toHaveTextContent('Odoslaná'))
  })

  it('validates the validity days before sending', async () => {
    const fetchMock = mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote()),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
    })
    renderQuotes(path)
    await userEvent.click(await screen.findByRole('button', { name: 'Odoslať ponuku' }))
    const dialog = screen.getByRole('dialog')
    const days = within(dialog).getByLabelText('Platnosť (dni)')
    await userEvent.clear(days)
    await userEvent.type(days, '400')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Odoslať' }))
    expect(within(dialog).getByText('Platnosť musí byť celé číslo od 1 do 365 dní.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([u]) => String(u).endsWith('/send'))).toBe(false)
  })

  it('accepts a sent quote only after confirmation', async () => {
    const fetchMock = mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote({ status: 'Sent', allowedTransitions: ['Accepted', 'Rejected'] })),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
      [`POST /api/quotes/${QUOTE_ID}/accept`]: json(makeQuote({ status: 'Accepted', allowedTransitions: [] })),
    })
    renderQuotes(path)
    await userEvent.click(await screen.findByRole('button', { name: 'Označiť ako prijatú' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Zrušiť' }))
    expect(fetchMock.mock.calls.some(([u]) => String(u).endsWith('/accept'))).toBe(false)

    await userEvent.click(screen.getByRole('button', { name: 'Označiť ako prijatú' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Prijať' }))
    expect(await screen.findByText('Ponuka bola označená ako prijatá.')).toBeInTheDocument()
  })

  it('rejects a sent quote after confirmation', async () => {
    mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote({ status: 'Sent', allowedTransitions: ['Accepted', 'Rejected'] })),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
      [`POST /api/quotes/${QUOTE_ID}/reject`]: json(makeQuote({ status: 'Rejected', allowedTransitions: [] })),
    })
    renderQuotes(path)
    await userEvent.click(await screen.findByRole('button', { name: 'Označiť ako zamietnutú' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Zamietnuť' }))
    expect(await screen.findByText('Ponuka bola označená ako zamietnutá.')).toBeInTheDocument()
  })

  it('shows a Slovak message for an invalid transition (409) and reloads the quote', async () => {
    const fetchMock = mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote({ status: 'Sent', allowedTransitions: ['Accepted', 'Rejected'] })),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
      [`POST /api/quotes/${QUOTE_ID}/accept`]: problem(409, 'Quote cannot move from Rejected to Accepted.'),
    })
    renderQuotes(path)
    await userEvent.click(await screen.findByRole('button', { name: 'Označiť ako prijatú' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Prijať' }))
    expect(await screen.findByText('Zmenu stavu nie je možné vykonať, ponuka je už v inom stave.')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock.mock.calls.filter(([u]) => String(u).endsWith(`/quotes/${QUOTE_ID}`)).length).toBeGreaterThan(1))
  })

  it('shows the expired message when accepting after validity ended', async () => {
    mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote({ status: 'Sent', allowedTransitions: ['Accepted', 'Rejected'] })),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
      [`POST /api/quotes/${QUOTE_ID}/accept`]: problem(409, 'The quote has expired.'),
    })
    renderQuotes(path)
    await userEvent.click(await screen.findByRole('button', { name: 'Označiť ako prijatú' }))
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Prijať' }))
    expect(await screen.findByText('Platnosť ponuky vypršala, ponuku už nie je možné prijať.')).toBeInTheDocument()
  })

  it('offers the contract step for an accepted quote', async () => {
    mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote({ status: 'Accepted', allowedTransitions: [] })),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': customersPage,
    })
    renderQuotes(path)
    expect(await screen.findByRole('link', { name: 'Pripraviť zmluvu' })).toHaveAttribute('href', `/contracts?quoteId=${QUOTE_ID}`)
    expect(screen.queryByRole('button', { name: 'Odoslať ponuku' })).not.toBeInTheDocument()
  })

  it('hides all actions for read-only roles', async () => {
    mockApi({
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote()),
      [`GET /api/quotes/${QUOTE_ID}/history`]: history,
      'GET /api/customers': json({}, 403),
    })
    renderQuotes(path, ['Designer'])
    await screen.findByText('PV-400 – Panel 400 W')
    expect(screen.queryByRole('button', { name: 'Odoslať ponuku' })).not.toBeInTheDocument()
  })
})
