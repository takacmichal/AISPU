import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { saveSession } from '../auth/session'
import { QuickJump } from './QuickJump'

function Location() {
  return <output data-testid="location">{useLocation().pathname}</output>
}

function mount(body: unknown) {
  saveSession({ email: 'user@example.test', roles: ['Admin'], accessToken: 'token', refreshToken: 'refresh' })
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    void input
    return new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })
  })
  vi.stubGlobal('fetch', fetchMock)
  render(<MemoryRouter><QuickJump items={[]} onClose={() => {}} onSelect={() => {}} /><Location /></MemoryRouter>)
  return fetchMock
}

describe('QuickJump global search', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('debounces the API query and navigates to a grouped result on Enter', async () => {
    const fetchMock = mount({ customers: null, quotes: [], installations: [{ id: 'i-1', title: 'FVE Nitra', subtitle: 'Montáž' }], tickets: [] })
    const input = screen.getByRole('combobox', { name: /Hľadať zákazníka/ })
    fireEvent.change(input, { target: { value: 'FVE' } })
    expect(await screen.findByText('FVE Nitra')).toBeInTheDocument()
    await waitFor(() => {
      const input = fetchMock.mock.calls[0]?.[0]
      expect(new URL(input instanceof Request ? input.url : String(input), 'http://x').searchParams.get('q')).toBe('FVE')
    })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(screen.getByTestId('location')).toHaveTextContent('/installations/i-1')
  })

  it('keeps role-hidden groups null and reports no results for accessible groups', async () => {
    mount({ customers: null, quotes: null, installations: [], tickets: [] })
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'missing' } })
    expect(await screen.findByText('Nenašli sa žiadne výsledky.')).toBeInTheDocument()
  })
})
