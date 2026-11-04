import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { installation, json, renderRoute, stubFetch, type Call } from '../installations/testHelpers'
import MyPlantsPage from './MyPlantsPage'
import { connectionRequest } from './testUtils'

afterEach(() => vi.unstubAllGlobals())

const render = () => renderRoute(<MyPlantsPage />, '/my-plants', '/my-plants', ['Customer'])

const plants = [
  installation(),
  installation({ id: 'i2', name: 'FVE Chata', plannedPowerKwp: 4.5, state: 'HandedOver' }),
]

function handler(over: Partial<Record<string, () => Response>> = {}) {
  return (call: Call) => {
    if (call.path === '/api/installations') return over.installations?.() ?? json(plants)
    if (call.path === '/api/connection-requests') return over.requests?.() ?? json([connectionRequest()])
  }
}

describe('MyPlantsPage', () => {
  it('shows a card per own plant with power, state, supply point, connection progress and a detail link', async () => {
    const calls = stubFetch(handler())
    render()
    expect(screen.getByRole('status')).toHaveTextContent('Načítavam vaše elektrárne…')

    const nitra = await screen.findByRole('article', { name: 'FVE Nitra' })
    expect(within(nitra).getByRole('link', { name: 'FVE Nitra' })).toHaveAttribute('href', '/my-plants/i1')
    expect(within(nitra).getByText('Naplánovaná')).toBeInTheDocument()
    expect(within(nitra).getByText(/^9,6\skWp$/)).toBeInTheDocument()
    expect(await within(nitra).findByText('24ZSS1234567890A')).toBeInTheDocument()
    expect(within(nitra).getByText('krok 2 z 6: Posúdenie distribučkou')).toBeInTheDocument()
    expect(within(nitra).getByText('Pripravujeme termín montáže.')).toBeInTheDocument()

    const chata = screen.getByRole('article', { name: 'FVE Chata' })
    expect(within(chata).getByText('Odovzdaná')).toBeInTheDocument()
    expect(within(chata).getByText('Elektráreň je v prevádzke.')).toBeInTheDocument()
    expect(within(chata).queryByText('Odberné miesto (EIC)')).not.toBeInTheDocument()

    // Own data only: the backend scopes the lists, the UI sends no customer filter and calls no internal APIs.
    expect(calls.filter((c) => c.path === '/api/installations')).toHaveLength(1)
    expect(calls.filter((c) => c.path === '/api/connection-requests')).toHaveLength(1)
    expect(calls.every((c) => !c.search.includes('customerId'))).toBe(true)
    expect(calls.some((c) => /customers|contracts|quotes|supply-points/.test(c.path))).toBe(false)
  })

  it('asks the customer to sign the connection contract when it was sent', async () => {
    stubFetch(handler({ requests: () => json([connectionRequest({ state: 'ContractOffered', contractSentAt: '2026-04-01T08:00:00Z' })]) }))
    render()
    const nitra = await screen.findByRole('article', { name: 'FVE Nitra' })
    expect(await within(nitra).findByText('Podpíšte zmluvu o pripojení, ktorú sme vám poslali.')).toBeInTheDocument()
  })

  it('shows an empty state when the customer has no plant', async () => {
    stubFetch(handler({ installations: () => json([]), requests: () => json([]) }))
    render()
    expect(await screen.findByText('Zatiaľ nemáte žiadnu elektráreň')).toBeInTheDocument()
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })

  it('shows an error with retry when the plants cannot be loaded', async () => {
    let fail = true
    const calls = stubFetch(handler({ installations: () => (fail ? new Response(null, { status: 500 }) : json(plants)) }))
    render()
    expect(await screen.findByRole('alert')).toHaveTextContent('Zoznam elektrární sa nepodarilo načítať.')
    fail = false
    await userEvent.click(screen.getByRole('button', { name: 'Skúsiť znova' }))
    expect(await screen.findByRole('article', { name: 'FVE Nitra' })).toBeInTheDocument()
    expect(calls.filter((c) => c.path === '/api/installations')).toHaveLength(2)
  })

  it('keeps the cards when the connection progress fails', async () => {
    stubFetch(handler({ requests: () => new Response(null, { status: 500 }) }))
    render()
    const nitra = await screen.findByRole('article', { name: 'FVE Nitra' })
    expect(await within(nitra).findByText('Stav pripojenia sa nepodarilo načítať.')).toBeInTheDocument()
    expect(within(nitra).getByText('Pripravujeme termín montáže.')).toBeInTheDocument()
  })
})
