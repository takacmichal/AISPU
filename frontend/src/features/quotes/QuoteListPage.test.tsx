import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CUSTOMER_ID, customersPage, json, makeQuote, mockApi, renderQuotes } from './testUtils'

describe('QuoteListPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows a loading state, then quotes with status badges and EUR totals', async () => {
    mockApi({
      'GET /api/quotes': json([makeQuote(), makeQuote({ id: 'bbbbbbbb-0', status: 'Accepted', grossTotal: 2460.5 })]),
      'GET /api/customers': customersPage,
    })
    renderQuotes('/quotes')

    expect(screen.getAllByText('Načítavam…').length).toBeGreaterThan(0)
    expect(await screen.findByText('#ABCDEF12')).toBeInTheDocument()
    expect(screen.getAllByText('Ján Novák').length).toBeGreaterThan(0)
    expect(screen.getByText('Rozpracovaná', { selector: '.badge' })).toBeInTheDocument()
    expect(screen.getByText('Prijatá', { selector: '.badge' })).toBeInTheDocument()
    expect(screen.getByText(/2\s?460,50/)).toBeInTheDocument()
  })

  it('shows the empty state', async () => {
    mockApi({ 'GET /api/quotes': json([]), 'GET /api/customers': customersPage })
    renderQuotes('/quotes')
    expect(await screen.findByText('Zatiaľ nie sú vytvorené žiadne ponuky.')).toBeInTheDocument()
  })

  it('shows an error when loading fails', async () => {
    mockApi({ 'GET /api/quotes': json({}, 500), 'GET /api/customers': customersPage })
    renderQuotes('/quotes')
    expect(await screen.findByText('Zoznam ponúk sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('sends status and customer filters to the API and stores them in the URL', async () => {
    const fetchMock = mockApi({ 'GET /api/quotes': json([makeQuote()]), 'GET /api/customers': customersPage })
    renderQuotes('/quotes')
    await screen.findByText('#ABCDEF12')

    await userEvent.selectOptions(screen.getByLabelText('Stav'), 'Sent')
    await waitFor(() => expect(screen.getByTestId('loc')).toHaveTextContent('status=Sent'))
    await waitFor(() => expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain('status=Sent'))

    await userEvent.selectOptions(screen.getByLabelText('Zákazník'), CUSTOMER_ID)
    await waitFor(() => expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain(`customerId=${CUSTOMER_ID}`))
  })

  it('pages locally', async () => {
    const many = Array.from({ length: 25 }, (_, i) => makeQuote({ id: `${String(i).padStart(8, '0')}-0000` }))
    mockApi({ 'GET /api/quotes': json(many), 'GET /api/customers': customersPage })
    renderQuotes('/quotes')
    await screen.findByText('#00000000')
    expect(screen.queryByText('#00000021')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Ďalšia' }))
    expect(await screen.findByText('#00000021')).toBeInTheDocument()
    expect(screen.getByTestId('loc')).toHaveTextContent('page=2')
  })

  it('navigates to the detail on row click', async () => {
    mockApi({ 'GET /api/quotes': json([makeQuote()]), 'GET /api/customers': customersPage })
    renderQuotes('/quotes')
    await userEvent.click(await screen.findByText('#ABCDEF12'))
    expect(screen.getByTestId('loc')).toHaveTextContent('/quotes/abcdef12')
  })

  it('offers creating a quote to Sales but not to Designer', async () => {
    mockApi({ 'GET /api/quotes': json([]), 'GET /api/customers': json({}, 403) })
    const { unmount } = renderQuotes('/quotes', ['Sales'])
    expect(await screen.findByRole('link', { name: 'Nová ponuka' })).toHaveAttribute('href', '/quotes/new')
    unmount()
    renderQuotes('/quotes', ['Designer'])
    await screen.findByText('Zatiaľ nie sú vytvorené žiadne ponuky.')
    expect(screen.queryByRole('link', { name: 'Nová ponuka' })).not.toBeInTheDocument()
  })
})
