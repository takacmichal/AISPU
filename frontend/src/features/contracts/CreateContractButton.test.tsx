import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../../auth/AuthContext'
import { saveSession } from '../../auth/session'
import { ToastProvider } from '../../components/toast'
import { CreateContractButton } from './CreateContractButton'

function renderButton(status: string, roles = ['Sales']) {
  saveSession({ email: 'a@test.sk', roles, accessToken: 't', refreshToken: 'r' })
  const router = createMemoryRouter(
    [
      { path: '/quotes/q1', element: <CreateContractButton quoteId="q1" quoteStatus={status} /> },
      { path: '/contracts/:id', element: <p>contract detail</p> },
    ],
    { initialEntries: ['/quotes/q1'] },
  )
  render(
    <AuthProvider>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </AuthProvider>,
  )
}

describe('CreateContractButton', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('is hidden unless the quote is accepted', () => {
    renderButton('Sent')
    expect(screen.queryByRole('button', { name: 'Vytvoriť zmluvu' })).not.toBeInTheDocument()
  })

  it('is hidden for roles that cannot create contracts', () => {
    renderButton('Accepted', ['Designer'])
    expect(screen.queryByRole('button', { name: 'Vytvoriť zmluvu' })).not.toBeInTheDocument()
  })

  it('creates the contract and opens it', async () => {
    const user = userEvent.setup()
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'c9' }), { status: 201, headers: { 'Content-Type': 'application/json' } })))
    renderButton('Accepted')
    await user.click(screen.getByRole('button', { name: 'Vytvoriť zmluvu' }))
    expect(await screen.findByText('contract detail')).toBeInTheDocument()
  })

  it('shows the conflict message', async () => {
    const user = userEvent.setup()
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(JSON.stringify({ detail: 'K ponuke už existuje zmluva.' }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } })),
    )
    renderButton('Accepted')
    await user.click(screen.getByRole('button', { name: 'Vytvoriť zmluvu' }))
    expect((await screen.findAllByText('K ponuke už existuje zmluva.')).length).toBeGreaterThan(0)
  })
})
