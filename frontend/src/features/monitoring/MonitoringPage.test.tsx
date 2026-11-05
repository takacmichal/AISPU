import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { menuItems } from '../../auth/navigation'
import { installation, json, problem, renderRoute, stubFetch, type Call } from '../installations/testHelpers'
import MonitoringPage from './MonitoringPage'

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const production = {
  installationId: 'i1',
  from: '2026-06-14T22:00:00Z',
  to: '2026-06-21T22:00:00Z',
  granularity: 'day',
  timeZone: 'Europe/Bratislava',
  totalEnergyKwh: 70,
  peakPowerKw: 6.5,
  points: [
    { periodStart: '2026-06-15T22:00:00Z', energyKwh: 30, peakPowerKw: 5, sampleCount: 100 },
    { periodStart: '2026-06-16T22:00:00Z', energyKwh: 40, peakPowerKw: 6.5, sampleCount: 120 },
  ],
}
const comparison = (available: boolean) => ({
  ...production,
  predictionAvailable: available,
  modelVersion: available ? 'm1' : null,
  actualEnergyKwh: 70,
  predictedEnergyKwh: available ? 100 : null,
  points: production.points.map((p) => ({
    periodStart: p.periodStart,
    actualEnergyKwh: p.energyKwh,
    predictedEnergyKwh: available ? 50 : null,
    deviationKwh: null,
    ratio: null,
  })),
})

function handler(over: (call: Call) => Response | undefined = () => undefined, predicted = false) {
  return (call: Call) => {
    const custom = over(call)
    if (custom) return custom
    if (call.path === '/api/installations') return json([installation()])
    if (call.path === '/api/installations/i1/production') return json(production)
    if (call.path === '/api/installations/i1/production/comparison') return json(comparison(predicted))
  }
}

const render = (roles = ['Admin'], route = '/monitoring') => renderRoute(<MonitoringPage />, route, '/monitoring', roles)

describe('MonitoringPage', () => {
  it('shows skeletons, then KPI cards and the chart with a table alternative', async () => {
    stubFetch(handler())
    render()
    expect(await screen.findByText('70 kWh')).toBeInTheDocument()
    expect(screen.getByText('Špička 6,5 kW')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Obdobia podvýroby' })).toBeInTheDocument()
    expect(screen.getAllByText('Predikcia nie je dostupná').length).toBeGreaterThan(0)
    expect(screen.getByRole('group', { name: /Graf výroby/ })).toBeInTheDocument()
    expect(screen.getByText('Predikcia výroby zatiaľ nie je dostupná, zobrazuje sa len skutočná výroba.')).toBeInTheDocument()
    await userEvent.click(screen.getByText('Zobraziť ako tabuľku'))
    expect(await screen.findByRole('columnheader', { name: 'Skutočná (kWh)' })).toBeInTheDocument()
  })

  it('shows the prediction and tooltip on keyboard focus when predictions exist', async () => {
    stubFetch(handler(() => undefined, true))
    render()
    const chart = await screen.findByRole('group', { name: /Graf výroby/ })
    expect(screen.getByText('100 kWh')).toBeInTheDocument()
    expect(screen.getByText('-30 %')).toBeInTheDocument()
    chart.focus()
    await userEvent.keyboard('{ArrowRight}')
    expect(screen.getAllByText(/Odchýlka: -40 %/).length).toBeGreaterThan(0)
  })

  it('lists underproduction periods that open a prefilled ticket', async () => {
    stubFetch(handler(() => undefined, true))
    render(['Operator'])
    const chip = await screen.findByRole('link', { name: /Vytvoriť servisný tiket pre podvýrobu/ })
    const url = new URL(chip.getAttribute('href') ?? '', 'http://x')
    expect(url.pathname).toBe('/tickets/new')
    expect(url.searchParams.get('installationId')).toBe('i1')
    expect(url.searchParams.get('description')).toContain('FVE Nitra')
    expect(chip).toHaveTextContent('70 %')
    expect(within(screen.getByRole('group', { name: 'Ukazovatele výroby' })).getByText('1')).toBeInTheDocument()
  })

  it('shows the periods without a ticket link to roles that cannot create tickets', async () => {
    stubFetch(handler(() => undefined, true))
    render(['Designer'])
    expect(await screen.findByText(/· 70 %/)).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Vytvoriť servisný tiket/ })).not.toBeInTheDocument()
  })

  it('turns the underproduction highlight off and keeps it in the URL', async () => {
    stubFetch(handler(() => undefined, true))
    const { router } = render(['Operator'])
    await screen.findByRole('link', { name: /Vytvoriť servisný tiket/ })
    await userEvent.click(screen.getByRole('switch', { name: 'Zvýrazniť podvýrobu' }))
    expect(screen.queryByRole('link', { name: /Vytvoriť servisný tiket/ })).not.toBeInTheDocument()
    expect(router.state.location.search).toContain('under=0')
  })

  it('opens the installation from the URL', async () => {
    stubFetch((c) => {
      if (c.path === '/api/installations') return json([installation(), installation({ id: 'i2', name: 'FVE Trnava' })])
      if (c.path.startsWith('/api/installations/i2/production')) return json({ ...production, installationId: 'i2' })
    })
    render(['Admin'], '/monitoring?installation=i2')
    expect(await screen.findByLabelText('Inštalácia')).toHaveValue('i2')
    expect(await screen.findByText('70 kWh')).toBeInTheDocument()
  })

  it('shows the empty state without data', async () => {
    stubFetch(handler((c) => (c.path === '/api/installations/i1/production' ? json({ ...production, totalEnergyKwh: 0, peakPowerKw: null, points: [] }) : undefined)))
    render()
    expect(await screen.findByText('Za zvolené obdobie nie sú žiadne údaje o výrobe.')).toBeInTheDocument()
  })

  it('shows an error with retry when production fails', async () => {
    stubFetch(handler((c) => (c.path === '/api/installations/i1/production' ? new Response(null, { status: 500 }) : undefined)))
    render()
    expect(await screen.findByText('Výrobu sa nepodarilo načítať.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Skúsiť znova' })).toBeInTheDocument()
  })

  it('shows the server message of a 400 range error', async () => {
    stubFetch(handler((c) => (c.path === '/api/installations/i1/production' ? problem(400, 'Obdobie je príliš dlhé.') : undefined)))
    render()
    expect(await screen.findByText('Obdobie je príliš dlhé.')).toBeInTheDocument()
  })

  it('validates a custom range next to the fields', async () => {
    stubFetch(handler())
    render()
    await screen.findByText('70 kWh')
    await userEvent.click(screen.getByRole('radio', { name: 'Vlastné' }))
    expect(await screen.findByText('Zadajte začiatok aj koniec obdobia.')).toBeInTheDocument()
  })

  it('exports the CSV with the chosen separator and downloads it', async () => {
    const calls = stubFetch(
      handler((c) =>
        c.path === '/api/installations/i1/production/export'
          ? new Response('a;b', { headers: { 'Content-Type': 'text/csv', 'Content-Disposition': 'attachment; filename="vyroba.csv"' } })
          : undefined,
      ),
    )
    URL.createObjectURL = vi.fn(() => 'blob:x')
    URL.revokeObjectURL = vi.fn()
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    render()
    await screen.findByText('70 kWh')
    await userEvent.selectOptions(screen.getByLabelText('Oddeľovač'), 'tab')
    await userEvent.click(screen.getByRole('button', { name: 'Exportovať CSV' }))
    await waitFor(() => expect(click).toHaveBeenCalled())
    const call = calls.find((c) => c.path.endsWith('/export'))
    expect(call?.search).toContain('separator=tab')
    expect(call?.search).toContain('granularity=day')
  })

  it('reports an export failure', async () => {
    stubFetch(handler((c) => (c.path.endsWith('/export') ? problem(400, 'Neplatné obdobie.') : undefined)))
    render()
    await screen.findByText('70 kWh')
    await userEvent.click(screen.getByRole('button', { name: 'Exportovať CSV' }))
    expect(await screen.findByText('Neplatné obdobie.')).toBeInTheDocument()
  })

  it('shows the anomalies placeholder while the API is disabled', async () => {
    stubFetch(handler())
    render()
    expect(await screen.findByText('Pripravuje sa')).toBeInTheDocument()
  })

  it('shows an empty state without installations', async () => {
    stubFetch((c) => (c.path === '/api/installations' ? json([]) : undefined))
    render()
    expect(await screen.findByText('Zatiaľ nie sú evidované žiadne inštalácie.')).toBeInTheDocument()
  })
})

describe('monitoring navigation', () => {
  it('is visible only to Admin, Operator, Technician and Designer', () => {
    const visible = (role: string) => menuItems([role]).some((i) => i.path === '/monitoring')
    for (const role of ['Admin', 'Operator', 'Technician', 'Designer']) expect(visible(role)).toBe(true)
    for (const role of ['Sales', 'Customer']) expect(visible(role)).toBe(false)
  })
})
