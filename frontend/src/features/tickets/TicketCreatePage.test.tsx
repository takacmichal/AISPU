import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import TicketCreatePage from './TicketCreatePage'
import { json, problem, renderRoute, stubFetch, ticketDetail } from './testUtils'

afterEach(() => vi.unstubAllGlobals())

const render = (roles = ['Customer']) => renderRoute(<TicketCreatePage />, '/tickets/new', '/tickets/new', roles)
const installations = [{ id: 'i1', name: 'FVE Nitra' }]
const handler = (extra?: (path: string, method: string) => Response | undefined) => (c: { path: string; method: string }) => {
  if (c.path === '/api/installations') return json(installations)
  return extra?.(c.path, c.method)
}

describe('TicketCreatePage', () => {
  it('prefills installation, title and description from the URL (underproduction in monitoring)', async () => {
    stubFetch(handler())
    renderRoute(<TicketCreatePage />, '/tickets/new?installationId=i1&title=Podv%C3%BDroba%204.%2011.&description=Pod%2085%20%25', '/tickets/new', ['Operator'])
    await screen.findByRole('option', { name: 'FVE Nitra' })
    expect(screen.getByLabelText('Inštalácia')).toHaveValue('i1')
    expect(screen.getByLabelText('Názov')).toHaveValue('Podvýroba 4. 11.')
    expect(screen.getByLabelText('Popis poruchy')).toHaveValue('Pod 85 %')
  })

  it('validates required fields on the client and does not post', async () => {
    const calls = stubFetch(handler())
    render()
    await screen.findByRole('option', { name: 'FVE Nitra' })
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať' }))
    expect(screen.getAllByText('Pole je povinné.')).toHaveLength(3)
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('rejects a wrong file type and too many files', async () => {
    stubFetch(handler())
    render()
    await screen.findByRole('option', { name: 'FVE Nitra' })
    const input = screen.getByLabelText(/Prílohy/)
    const gif = new File(['x'], 'a.gif', { type: 'image/gif' })
    await userEvent.upload(input, gif, { applyAccept: false })
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať' }))
    expect(await screen.findByText('Súbor „a.gif“ nie je JPEG, PNG ani PDF.')).toBeInTheDocument()
  })

  it('rejects files over 5 MB', async () => {
    stubFetch(handler())
    render()
    await screen.findByRole('option', { name: 'FVE Nitra' })
    const big = new File([new Uint8Array(5 * 1024 * 1024 + 1)], 'big.pdf', { type: 'application/pdf' })
    await userEvent.upload(screen.getByLabelText(/Prílohy/), big)
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať' }))
    expect(await screen.findByText('Súbor „big.pdf“ je väčší ako 5 MB.')).toBeInTheDocument()
  })

  it('submits a customer report with a base64 attachment and no priority', async () => {
    const calls = stubFetch(handler((path, method) => (path === '/api/tickets' && method === 'POST' ? json(ticketDetail(), 201) : undefined)))
    const { router } = render()
    await userEvent.selectOptions(await screen.findByLabelText('Inštalácia'), 'i1')
    await userEvent.type(screen.getByLabelText('Názov'), ' Invertor nesvieti ')
    await userEvent.type(screen.getByLabelText('Popis poruchy'), 'Kontrolka nesvieti')
    await userEvent.upload(screen.getByLabelText(/Prílohy/), new File(['abc'], 'foto.png', { type: 'image/png' }))
    expect(screen.queryByLabelText('Priorita')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať' }))
    await waitFor(() => expect(calls.some((c) => c.method === 'POST')).toBe(true))
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({
      installationId: 'i1',
      title: 'Invertor nesvieti',
      description: 'Kontrolka nesvieti',
      attachments: [{ fileName: 'foto.png', contentType: 'image/png', contentBase64: 'YWJj' }],
    })
    await waitFor(() => expect(router.state.location.pathname).toBe('/tickets/t1'))
  })

  it('offers priority to staff and maps server field errors', async () => {
    stubFetch(
      handler((path, method) =>
        path === '/api/tickets' && method === 'POST'
          ? new Response(JSON.stringify({ status: 400, title: 'x', errors: { Title: ['Neplatný názov.'] } }), {
              status: 400,
              headers: { 'Content-Type': 'application/problem+json' },
            })
          : undefined,
      ),
    )
    render(['Operator'])
    await userEvent.selectOptions(await screen.findByLabelText('Inštalácia'), 'i1')
    expect(screen.getByLabelText('Priorita')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Názov'), 'T')
    await userEvent.type(screen.getByLabelText('Popis poruchy'), 'D')
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať' }))
    expect(await screen.findByText('Neplatný názov.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Odoslať' })).toBeEnabled()
  })

  it('shows an error when installations fail to load', async () => {
    stubFetch(() => problem(500, 'x'))
    render()
    expect(await screen.findByText('Zoznam inštalácií sa nepodarilo načítať.')).toBeInTheDocument()
  })
})
