import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { baseDesign, json, renderAt, stubFetch } from './testUtils'

describe('DesignListPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows a loading state and then the designs', async () => {
    stubFetch(() => json([{ ...baseDesign, status: 'InDesign', annualConsumptionKwh: 4500, roofPlanes: [{}] }]))
    renderAt('/designs')
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(await screen.findByText('V návrhu')).toBeInTheDocument()
    expect(screen.getByText(/4\s?500/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'V návrhu' })).toHaveAttribute('href', '/designs/d1')
  })

  it('shows an empty state', async () => {
    stubFetch(() => json([]))
    renderAt('/designs')
    expect(await screen.findByText('Zatiaľ nie sú žiadne návrhy.')).toBeInTheDocument()
  })

  it('shows an error state', async () => {
    stubFetch(() => new Response(null, { status: 500 }))
    renderAt('/designs')
    expect(await screen.findByRole('alert')).toHaveTextContent('Zoznam návrhov sa nepodarilo načítať.')
  })

  it('filters by status through the API query', async () => {
    const calls = stubFetch(() => json([]))
    renderAt('/designs')
    await screen.findByText('Zatiaľ nie sú žiadne návrhy.')
    await userEvent.selectOptions(screen.getByLabelText('Stav návrhu'), 'Approved')
    expect(await screen.findByText('Žiadny návrh nezodpovedá filtru.')).toBeInTheDocument()
    expect(calls.some((c) => c.path === '/api/designs')).toBe(true)
  })
})
