import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App'

describe('App', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renders title and API info', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({ name: 'SolarOps API', version: '1.0.0', environment: 'Development', serverTime: '' }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
      ),
    )

    render(<App />)

    expect(screen.getByRole('heading', { name: 'SolarOps' })).toBeInTheDocument()
    expect(await screen.findByText(/SolarOps API v1\.0\.0/)).toBeInTheDocument()
  })

  it('shows error when API is unavailable', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 503 })))
    vi.spyOn(console, 'error').mockImplementation(() => {})

    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent('API je nedostupné.')
  })
})
