import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import InstallationListPage from './InstallationListPage'
import { installation, json, renderRoute, stubFetch } from './testHelpers'

afterEach(() => vi.unstubAllGlobals())

const customers = { items: [{ id: 'c1', displayName: 'Ján Novák' }], totalCount: 1 }

function render(roles = ['Admin'], route = '/installations') {
  return renderRoute(<InstallationListPage />, route, '/installations', roles)
}

describe('InstallationListPage', () => {
  it('shows a loading state, then rows with state badge and customer name', async () => {
    stubFetch(({ path }) => {
      if (path === '/api/installations') return json([installation({ state: 'Mounting' })])
      if (path === '/api/customers') return json(customers)
    })
    render()
    expect(screen.getByRole('status', { name: '' })).toBeInTheDocument()
    expect(await screen.findByText('FVE Nitra')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Montáž' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Ján Novák' })).toBeInTheDocument()
    expect(screen.getByText('Neplánované')).toBeInTheDocument()
  })

  it('shows the empty state', async () => {
    stubFetch(({ path }) => (path === '/api/installations' ? json([]) : undefined))
    render()
    expect(await screen.findByText('Zatiaľ nie sú evidované žiadne inštalácie.')).toBeInTheDocument()
  })

  it('shows an error when the list fails', async () => {
    stubFetch(({ path }) => (path === '/api/installations' ? new Response(null, { status: 500 }) : undefined))
    render()
    expect(await screen.findByText('Zoznam inštalácií sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('sends the state filter to the API and keeps it in the URL', async () => {
    const calls = stubFetch(({ path }) => (path === '/api/installations' ? json([installation()]) : undefined))
    const { router } = render()
    await screen.findByText('FVE Nitra')
    await userEvent.selectOptions(screen.getByLabelText('Stav'), 'Mounting')
    await waitFor(() => expect(calls.some((c) => c.path === '/api/installations' && c.search.includes('state=Mounting'))).toBe(true))
    expect(router.state.location.search).toContain('state=Mounting')
  })

  it('pages the list on the client', async () => {
    const many = Array.from({ length: 25 }, (_, i) => installation({ id: `i${i}`, name: `FVE ${String(i).padStart(2, '0')}` }))
    stubFetch(({ path }) => (path === '/api/installations' ? json(many) : undefined))
    render()
    await screen.findByText('FVE 00')
    expect(screen.queryByText('FVE 24')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Ďalšia' }))
    expect(await screen.findByText('FVE 24')).toBeInTheDocument()
  })

  it('offers "my installations" only to technicians', async () => {
    stubFetch(({ path }) => (path === '/api/installations' ? json([]) : undefined))
    const { unmount } = render(['Admin'])
    await screen.findByText('Zatiaľ nie sú evidované žiadne inštalácie.')
    expect(screen.queryByLabelText('Len moje inštalácie')).not.toBeInTheDocument()
    unmount()
    render(['Technician'])
    expect(await screen.findByLabelText('Len moje inštalácie')).toBeInTheDocument()
  })
})
