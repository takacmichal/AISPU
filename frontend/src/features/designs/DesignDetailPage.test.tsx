import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { baseDesign, json, renderAt, stubFetch, type Call } from './testUtils'

const plane = { name: 'Juh', azimuthDeg: 0, tiltDeg: 35, usableAreaM2: 40, shadingLossPercent: 5, roofType: 'PitchedTile' }
const inDesign = {
  ...baseDesign,
  status: 'InDesign',
  siteSurvey: { notes: 'Strecha v poriadku', surveyedBy: 'u1', surveyedAt: '2026-01-02T10:00:00Z' },
  latitude: 48.15,
  longitude: 17.1,
  annualConsumptionKwh: 4500,
  consumptionProfile: 'Household',
  roofPlanes: [plane],
}
const version = (n: number, count: number, yieldKwh: number) => ({
  id: `v${n}`,
  designId: 'd1',
  versionNumber: n,
  panelId: 'p1',
  panelCode: 'P-1',
  panelName: 'Panel 450',
  panelPowerW: 450,
  panelUnitPrice: 120,
  panelCount: count,
  inverterId: 'i1',
  inverterCode: 'I-1',
  inverterName: 'Menič 5',
  inverterPowerW: 5000,
  inverterUnitPrice: 900,
  dcPowerKwp: (count * 450) / 1000,
  acPowerKw: 5,
  dcAcRatio: 1.1,
  warnings: [],
  annualYieldKwh: yieldKwh,
  specificYieldKwhPerKwp: 1000,
  estimateSource: 'simple',
  createdBy: 'u1',
  createdAt: `2026-01-0${n + 2}T10:00:00Z`,
})
const catalog = [
  { id: 'p1', type: 'Panel', code: 'P-1', name: 'Panel 450', manufacturer: null, powerW: 450, unitPrice: 120, currency: 'EUR', isActive: true },
  { id: 'i1', type: 'Inverter', code: 'I-1', name: 'Menič 5', manufacturer: null, powerW: 5000, unitPrice: 900, currency: 'EUR', isActive: true },
]

function api(design: unknown, versions: unknown[] = [], extra?: (c: Call) => Response | undefined) {
  return stubFetch((c) => {
    const custom = extra?.(c)
    if (custom) return custom
    if (c.path === '/api/designs/d1' && c.method === 'GET') return json(design)
    if (c.path === '/api/designs/d1/versions' && c.method === 'GET') return json(versions)
    if (c.path === '/api/components') return json(catalog)
    return undefined
  })
}

describe('DesignDetailPage', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('shows a loading state and an error state with retry', async () => {
    stubFetch(() => new Response(null, { status: 500 }))
    renderAt('/designs/d1')
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(await screen.findByRole('alert')).toHaveTextContent('Návrh sa nepodarilo načítať.')
    expect(screen.getByRole('button', { name: 'Skúsiť znova' })).toBeInTheDocument()
  })

  it('records the site survey of a requested design', async () => {
    const calls = api(baseDesign, [], (c) => (c.method === 'POST' && c.path.endsWith('/site-survey') ? json({ ...baseDesign, status: 'SiteSurveyDone' }) : undefined))
    renderAt('/designs/d1')
    await userEvent.click(await screen.findByRole('button', { name: 'Uložiť obhliadku' }))
    expect(screen.getByText('Vyplňte toto pole.')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Poznámky z obhliadky'), 'Hotovo')
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť obhliadku' }))
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/site-survey'))?.body).toEqual({ notes: 'Hotovo' }))
  })

  it('validates parameters on the client and sends the request when valid', async () => {
    const survey = { notes: 'ok', surveyedBy: 'u1', surveyedAt: '2026-01-02T10:00:00Z' }
    const calls = api({ ...baseDesign, status: 'SiteSurveyDone', siteSurvey: survey }, [], (c) =>
      c.method === 'PUT' ? json({ ...inDesign }) : undefined,
    )
    renderAt('/designs/d1')
    const save = await screen.findByRole('button', { name: 'Uložiť parametre' })
    await userEvent.click(save)
    expect(screen.getAllByText('Vyplňte toto pole.').length).toBeGreaterThanOrEqual(4)
    expect(calls.some((c) => c.method === 'PUT')).toBe(false)

    await userEvent.type(screen.getByLabelText('Zemepisná šírka (°)'), '95')
    await userEvent.click(save)
    expect(screen.getByText(/Hodnota musí byť od -90 do 90/)).toBeInTheDocument()

    await userEvent.clear(screen.getByLabelText('Zemepisná šírka (°)'))
    await userEvent.type(screen.getByLabelText('Zemepisná šírka (°)'), '48,15')
    await userEvent.type(screen.getByLabelText('Zemepisná dĺžka (°)'), '17.1')
    await userEvent.type(screen.getByLabelText('Ročná spotreba (kWh)'), '4500')
    await userEvent.type(screen.getByLabelText('Názov (1)'), 'Juh')
    await userEvent.type(screen.getByLabelText('Využiteľná plocha (m²) (1)'), '40')
    await userEvent.click(save)

    await waitFor(() => expect(calls.some((c) => c.method === 'PUT')).toBe(true))
    const body = calls.find((c) => c.method === 'PUT')!.body as { latitude: number; roofPlanes: { name: string; usableAreaM2: number }[] }
    expect(body.latitude).toBe(48.15)
    expect(body.roofPlanes[0]).toMatchObject({ name: 'Juh', usableAreaM2: 40 })
  })

  it('maps server field errors onto roof plane fields', async () => {
    const survey = { notes: 'ok', surveyedBy: 'u1', surveyedAt: '2026-01-02T10:00:00Z' }
    api({ ...inDesign, status: 'SiteSurveyDone', siteSurvey: survey }, [], (c) =>
      c.method === 'PUT'
        ? json({ title: 'x', status: 400, errors: { 'RoofPlanes[0].TiltDeg': ['Sklon je mimo rozsahu.'] } }, 400)
        : undefined,
    )
    renderAt('/designs/d1')
    await userEvent.click(await screen.findByRole('button', { name: 'Uložiť parametre' }))
    expect(await screen.findByText('Sklon je mimo rozsahu.')).toBeInTheDocument()
  })

  it('previews a configuration and shows kWp and yearly kWh', async () => {
    const calls = api(inDesign, [], (c) =>
      c.path.endsWith('/configuration/preview')
        ? json({
            compatibility: { isCompatible: true, dcPowerKwp: 5.4, acPowerKw: 5, dcAcRatio: 1.08, errors: [], warnings: ['DC/AC je vysoký.'] },
            estimate: { annualKwh: 5400, specificYieldKwhPerKwp: 1000, source: 'simple' },
          })
        : undefined,
    )
    renderAt('/designs/d1')
    await userEvent.selectOptions(await screen.findByLabelText('Panel'), 'p1')
    await userEvent.selectOptions(screen.getByLabelText('Menič'), 'i1')
    await userEvent.type(screen.getByLabelText('Počet panelov'), '12')
    await userEvent.click(screen.getByRole('button', { name: 'Skontrolovať a odhadnúť výrobu' }))
    expect(await screen.findByText('Konfigurácia je kompatibilná.')).toBeInTheDocument()
    expect(screen.getByText(/5\s400\skWh/)).toBeInTheDocument()
    expect(screen.getByText(/5,4\skWp/)).toBeInTheDocument()
    expect(screen.getByText('DC/AC je vysoký.')).toBeInTheDocument()
    expect(calls.find((c) => c.path.endsWith('/preview'))?.body).toEqual({ panelId: 'p1', inverterId: 'i1', panelCount: 12 })
  })

  it('shows an incompatible configuration and blocks saving', async () => {
    api(inDesign, [], (c) =>
      c.path.endsWith('/configuration/preview')
        ? json({ compatibility: { isCompatible: false, dcPowerKwp: 90, acPowerKw: 5, dcAcRatio: 18, errors: ['Príliš veľa panelov.'], warnings: [] }, estimate: null })
        : undefined,
    )
    renderAt('/designs/d1')
    await userEvent.selectOptions(await screen.findByLabelText('Panel'), 'p1')
    await userEvent.selectOptions(screen.getByLabelText('Menič'), 'i1')
    await userEvent.type(screen.getByLabelText('Počet panelov'), '200')
    await userEvent.click(screen.getByRole('button', { name: 'Skontrolovať a odhadnúť výrobu' }))
    expect(await screen.findByText('Príliš veľa panelov.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Uložiť ako novú verziu' })).toBeDisabled()
  })

  it('shows the latest version and compares two versions', async () => {
    api(inDesign, [version(1, 10, 4500), version(2, 12, 5400)])
    renderAt('/designs/d1')
    expect(await screen.findByText(/Odhad ročnej výroby/)).toBeInTheDocument()
    expect(screen.getAllByText(/5\s400\skWh/).length).toBeGreaterThan(0)
    await userEvent.click(screen.getByLabelText('Vybrať verziu 1'))
    await userEvent.click(screen.getByLabelText('Vybrať verziu 2'))
    const compare = screen.getByRole('table', { name: 'Porovnanie verzií' })
    expect(within(compare).getByText('10 × Panel 450')).toBeInTheDocument()
    expect(within(compare).getByText('12 × Panel 450')).toBeInTheDocument()
  })

  it('shows an empty state when no version exists', async () => {
    api({ ...inDesign, status: 'Approved' }, [])
    renderAt('/designs/d1')
    expect(await screen.findByText('Zatiaľ nebola uložená žiadna verzia návrhu.')).toBeInTheDocument()
  })

  it('marks a design infeasible after confirming with a reason', async () => {
    const survey = { notes: 'ok', surveyedBy: 'u1', surveyedAt: '2026-01-02T10:00:00Z' }
    const calls = api({ ...baseDesign, status: 'SiteSurveyDone', siteSurvey: survey }, [], (c) =>
      c.path.endsWith('/mark-infeasible') ? json({ ...baseDesign, status: 'Infeasible' }) : undefined,
    )
    renderAt('/designs/d1')
    await userEvent.click(await screen.findByRole('button', { name: 'Označiť ako nerealizovateľný' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Označiť' }))
    expect(within(dialog).getByText('Vyplňte toto pole.')).toBeInTheDocument()
    expect(calls.some((c) => c.path.endsWith('/mark-infeasible'))).toBe(false)
    await userEvent.type(within(dialog).getByLabelText('Dôvod'), 'Zatienená strecha')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Označiť' }))
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/mark-infeasible'))?.body).toEqual({ reason: 'Zatienená strecha' }))
  })

  it('shows a 409 conflict message from the server in the dialog', async () => {
    const survey = { notes: 'ok', surveyedBy: 'u1', surveyedAt: '2026-01-02T10:00:00Z' }
    api({ ...baseDesign, status: 'SiteSurveyDone', siteSurvey: survey }, [], (c) =>
      c.path.endsWith('/mark-infeasible') ? json({ title: 'Conflict', status: 409, detail: 'Návrh už nie je v stave obhliadky.' }, 409) : undefined,
    )
    renderAt('/designs/d1')
    await userEvent.click(await screen.findByRole('button', { name: 'Označiť ako nerealizovateľný' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.type(within(dialog).getByLabelText('Dôvod'), 'x')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Označiť' }))
    expect(await within(dialog).findByText('Návrh už nie je v stave obhliadky.')).toBeInTheDocument()
  })

  it('hides editing controls from Sales', async () => {
    api(inDesign, [version(1, 10, 4500)])
    renderAt('/designs/d1', 'Sales')
    expect(await screen.findByText(/Odhad ročnej výroby/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Uložiť parametre' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Skontrolovať a odhadnúť výrobu' })).not.toBeInTheDocument()
  })
})
