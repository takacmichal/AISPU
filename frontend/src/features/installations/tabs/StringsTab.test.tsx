import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import StringsTab from './StringsTab'
import { json, stubFetch } from '../testHelpers'

afterEach(() => vi.unstubAllGlobals())

describe('StringsTab (PR #342 API, behind a flag)', () => {
  it('shows a placeholder and makes no request while disabled', () => {
    const calls = stubFetch(() => undefined)
    render(<StringsTab installationId="i1" enabled={false} />)
    expect(screen.getByText(/PR #342/)).toBeInTheDocument()
    expect(calls).toHaveLength(0)
  })

  it('renders strings with panel serial numbers when enabled', async () => {
    stubFetch(() =>
      json({
        installationId: 'i1',
        strings: [{ id: 's1', installationId: 'i1', number: 1, name: 'Juh', mpptInput: 1, panelCount: 1, totalPowerW: 450, panels: [{ id: 'p1', serialNumber: 'SN-1', manufacturer: 'JA', model: 'M', powerW: 450 }] }],
        unassignedPanels: [{ id: 'p2', serialNumber: 'SN-2', manufacturer: 'JA', model: 'M', powerW: 450 }],
        assignedPowerW: 450,
        totalPanelPowerW: 900,
      }),
    )
    render(<StringsTab installationId="i1" enabled />)
    expect(await screen.findByText('SN-1')).toHaveClass('mono')
    expect(screen.getByText('SN-2')).toBeInTheDocument()
    expect(screen.getByText('Nepriradené panely')).toBeInTheDocument()
  })

  it('shows an error state', async () => {
    stubFetch(() => new Response(null, { status: 500 }))
    render(<StringsTab installationId="i1" enabled />)
    expect(await screen.findByText('Rozloženie stringov sa nepodarilo načítať.')).toBeInTheDocument()
  })
})
