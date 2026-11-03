import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CUSTOMER_ID, QUOTE_ID, customersPage, json, makeQuote, mockApi, problem, renderQuotes } from './testUtils'

const component = (id: string, type: string, code: string, unitPrice: number, powerW: number | null = null) => ({
  id, type, code, name: `${code} name`, manufacturer: null, powerW, unitPrice, currency: 'EUR', isActive: true,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
})
const catalog = json([
  component('p1', 'Panel', 'PV-400', 100, 400),
  component('i1', 'Inverter', 'INV-5', 800, 5000),
  component('l1', 'Labor', 'LAB', 40),
])

async function add(label: string, qty: string) {
  await userEvent.selectOptions(screen.getByLabelText('Komponent z katalógu'), screen.getByRole('option', { name: new RegExp(label) }))
  const q = screen.getByLabelText(/^Množstvo( \(.*\))?$/)
  await userEvent.clear(q)
  await userEvent.type(q, qty)
  await userEvent.click(screen.getByRole('button', { name: 'Pridať položku' }))
}

describe('QuoteConfigurator (/quotes/new)', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  const base = { 'GET /api/components': catalog, 'GET /api/customers': customersPage }

  it('blocks Designer from creating quotes', async () => {
    mockApi(base)
    renderQuotes('/quotes/new', ['Designer'])
    expect(await screen.findByText('Na vytváranie ponúk nemáte oprávnenie.')).toBeInTheDocument()
  })

  it('shows an error when the catalog fails to load', async () => {
    mockApi({ ...base, 'GET /api/components': json({}, 500) })
    renderQuotes('/quotes/new')
    expect(await screen.findByText('Katalóg komponentov sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('adds, edits and removes lines and recomputes totals', async () => {
    mockApi(base)
    renderQuotes('/quotes/new')
    await screen.findByLabelText('Komponent z katalógu')
    expect(screen.getByText('Ponuka zatiaľ nemá žiadne položky.')).toBeInTheDocument()

    await add('PV-400', '10')
    await add('INV-5', '1')
    const totals = screen.getByRole('group', { name: 'Súčet ponuky' })
    expect(totals).toHaveTextContent(/1\s?800,00/)
    expect(totals).toHaveTextContent(/2\s?214,00/)

    const qty = screen.getByLabelText('Množstvo: PV-400 name')
    await userEvent.clear(qty)
    await userEvent.type(qty, '5')
    expect(totals).toHaveTextContent(/1\s?300,00/)

    await userEvent.click(screen.getByRole('button', { name: 'Odobrať položku INV-5 name' }))
    expect(totals).toHaveTextContent(/615,00/)
  })

  it('validates customer, required panel and inverter, and quantities', async () => {
    const fetchMock = mockApi(base)
    renderQuotes('/quotes/new')
    await screen.findByLabelText('Komponent z katalógu')

    await userEvent.click(screen.getByRole('button', { name: 'Vytvoriť ponuku' }))
    expect(screen.getByText('Vyberte zákazníka.')).toBeInTheDocument()
    expect(screen.getByText('Pridajte aspoň jednú položku.')).toBeInTheDocument()

    await add('PV-400', '2,5')
    await userEvent.click(screen.getByRole('button', { name: 'Vytvoriť ponuku' }))
    expect(screen.getByText('Ponuka musí obsahovať aspoň jeden invertor.')).toBeInTheDocument()
    expect(screen.getByText('Počet kusov musí byť celé číslo.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)
  })

  it('warns about an oversized panel array without blocking', async () => {
    mockApi(base)
    renderQuotes('/quotes/new')
    await screen.findByLabelText('Komponent z katalógu')
    await add('PV-400', '20')
    await add('INV-5', '1')
    expect(screen.getByText(/prevyšuje výkon invertora/)).toBeInTheDocument()
  })

  it('creates the quote and navigates to the detail', async () => {
    const fetchMock = mockApi({
      ...base,
      'POST /api/quotes': json(makeQuote(), 201),
      [`GET /api/quotes/${QUOTE_ID}`]: json(makeQuote()),
      [`GET /api/quotes/${QUOTE_ID}/history`]: json([]),
    })
    renderQuotes(`/quotes/new?customerId=${CUSTOMER_ID}`)
    await screen.findByLabelText('Komponent z katalógu')
    await add('PV-400', '10')
    await add('INV-5', '1')
    await userEvent.click(screen.getByRole('button', { name: 'Vytvoriť ponuku' }))

    await waitFor(() => expect(screen.getByTestId('loc')).toHaveTextContent(`/quotes/${QUOTE_ID}`))
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(JSON.parse(String(post[1]?.body))).toEqual({
      customerId: CUSTOMER_ID,
      vatRatePercent: 23,
      note: null,
      lines: [{ componentId: 'p1', quantity: 10 }, { componentId: 'i1', quantity: 1 }],
    })
  })

  it('maps server validation errors to the line and a 409 to the form', async () => {
    let call = 0
    mockApi({
      ...base,
      'POST /api/quotes': () =>
        ++call === 1
          ? problem(400, 'Validation failed', { 'Lines[0].Quantity': ['Quantity must be greater than 0.'] })
          : problem(409, 'Component is inactive.'),
    })
    renderQuotes(`/quotes/new?customerId=${CUSTOMER_ID}`)
    await screen.findByLabelText('Komponent z katalógu')
    await add('PV-400', '1')
    await add('INV-5', '1')
    await userEvent.click(screen.getByRole('button', { name: 'Vytvoriť ponuku' }))
    const row = (await screen.findByText('Quantity must be greater than 0.')).closest('tr')!
    expect(within(row).getByLabelText('Množstvo: PV-400 name')).toHaveAttribute('aria-invalid', 'true')

    await userEvent.click(screen.getByRole('button', { name: 'Vytvoriť ponuku' }))
    expect(await screen.findAllByText('Component is inactive.')).not.toHaveLength(0)
  })
})
