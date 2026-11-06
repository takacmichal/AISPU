import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import StringsTab from './StringsTab'
import { json, stubFetch } from '../testHelpers'

afterEach(() => vi.unstubAllGlobals())

describe('StringsTab', () => {
  it('renders strings with panel serial numbers from the layout API', async () => {
    const calls = stubFetch(() =>
      json({
        installationId: 'i1',
        strings: [{ id: 's1', installationId: 'i1', number: 1, name: 'Juh', mpptInput: 1, panelCount: 1, totalPowerW: 450, panels: [{ id: 'p1', serialNumber: 'SN-1', manufacturer: 'JA', model: 'M', powerW: 450 }] }],
        unassignedPanels: [{ id: 'p2', serialNumber: 'SN-2', manufacturer: 'JA', model: 'M', powerW: 450 }],
        assignedPowerW: 450,
        totalPanelPowerW: 900,
      }),
    )
    render(<StringsTab installationId="i1" />)
    expect(await screen.findByText('SN-1')).toHaveClass('mono')
    expect(calls[0].path).toBe('/api/installations/i1/strings')
    expect(screen.getByRole('heading', { name: 'String 1 – Juh' })).toBeInTheDocument()
    expect(screen.getByText('SN-2')).toBeInTheDocument()
    expect(screen.getByText('Nepriradené panely')).toBeInTheDocument()
  })

  it('shows an error state', async () => {
    stubFetch(() => new Response(null, { status: 500 }))
    render(<StringsTab installationId="i1" />)
    expect(await screen.findByText('Rozloženie stringov sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('shows an empty state when there are no strings and no panels', async () => {
    stubFetch(() => json({ installationId: 'i1', strings: [], unassignedPanels: [], assignedPowerW: 0, totalPanelPowerW: 0 }))
    render(<StringsTab installationId="i1" />)
    expect(await screen.findByText('Zatiaľ nie sú definované žiadne stringy.')).toBeInTheDocument()
  })
})
