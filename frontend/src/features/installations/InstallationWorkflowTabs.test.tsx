import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, onTestFinished, vi } from 'vitest'
import InstallationDetailPage from './InstallationDetailPage'
import { detailOf, json, problem, renderRoute, stubFetch, type Call } from './testHelpers'

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

/** Transitions the backend allows in each state (only the ones these tests need). */
const ALLOWED: Record<string, string[]> = {
  Mounting: ['MountingDone'],
  Inspection: ['InspectionPassed', 'InspectionFailed'],
  InspectionPassed: ['Commissioned'],
  Commissioned: ['HandedOver'],
}

const PLAN = { scheduledStart: '2026-11-04', scheduledEnd: '2026-11-05', technicianIds: ['t1'] }

const step = (code: string, position: number, title: string, required: boolean, done = false) => ({
  code,
  position,
  title,
  required,
  done,
  note: null,
  changedBy: done ? 'u1' : null,
  changedAt: done ? '2026-11-04T08:00:00Z' : null,
})

const checklist = (over: Record<string, unknown> = {}) => ({
  installationId: 'i1',
  editable: true,
  complete: false,
  missingRequiredSteps: ['DcCabling'],
  steps: [
    step('SiteSafety', 1, 'Zabezpečenie pracoviska a BOZP', true, true),
    step('DcCabling', 2, 'Zapojenie stringov a DC kabeláž', true),
    step('SiteCleanup', 3, 'Upratanie pracoviska', false),
  ],
  ...over,
})

const report = (over: Record<string, unknown> = {}) => ({
  id: 'r1',
  installationId: 'i1',
  inspectedOn: '2026-11-05',
  inspectorName: 'Ing. Peter Revízny',
  inspectorCertificateNumber: 'REV-123',
  result: 'Passed',
  validUntil: '2030-11-05',
  findings: null,
  fileName: 'revizia.pdf',
  contentType: 'application/pdf',
  sizeBytes: 1200,
  sha256: 'abc',
  recordedBy: 'u1',
  recordedAt: '2026-11-05T10:00:00Z',
  ...over,
})

function setup(state: string, extra?: (call: Call) => Response | undefined, roles = ['Admin'], tab = '') {
  let current = detailOf({ state, allowedTransitions: ALLOWED[state] ?? [], ...PLAN })
  const calls = stubFetch((call) => {
    const custom = extra?.(call)
    if (custom) return custom
    if (call.path === '/api/installations/i1' && call.method === 'GET') return json(current)
    if (call.path === '/api/customers/c1') return json({ id: 'c1', displayName: 'Ján Novák' })
    if (call.path === '/api/installations/i1/devices') return json([])
    return undefined
  })
  const setState = (next: Record<string, unknown>) => {
    current = detailOf({ ...PLAN, ...next })
  }
  const view = renderRoute(<InstallationDetailPage />, `/installations/i1${tab ? `?tab=${tab}` : ''}`, '/installations/:id', roles)
  return { calls, setState, ...view }
}

describe('installation checklist tab', () => {
  it('lists steps with required markers and progress of required steps', async () => {
    setup('Mounting', (c) => (c.path === '/api/installations/i1/checklist' ? json(checklist()) : undefined), ['Admin'], 'checklist')
    // First test in this file: on a busy machine the page takes longer than the default 1 s to render.
    const list = await screen.findByRole('list', { name: 'Kroky montáže' }, { timeout: 4000 })
    expect(within(list).getAllByRole('checkbox')).toHaveLength(3)
    expect(screen.getByLabelText(/Zabezpečenie pracoviska a BOZP/)).toBeChecked()
    expect(screen.getByLabelText(/Upratanie pracoviska/)).not.toBeChecked()
    expect(screen.getByText('Povinné kroky: 1 z 2')).toBeInTheDocument()
    expect(within(list).getAllByText('povinný')).toHaveLength(2)
  })

  it('marks a step done through the API and shows the returned state', async () => {
    const calls = setup(
      'Mounting',
      (c) => {
        if (c.path === '/api/installations/i1/checklist') return json(checklist())
        if (c.path === '/api/installations/i1/checklist/DcCabling' && c.method === 'PUT')
          return json(
            checklist({
              complete: true,
              missingRequiredSteps: [],
              steps: [
                step('SiteSafety', 1, 'Zabezpečenie pracoviska a BOZP', true, true),
                step('DcCabling', 2, 'Zapojenie stringov a DC kabeláž', true, true),
                step('SiteCleanup', 3, 'Upratanie pracoviska', false),
              ],
            }),
          )
      },
      ['Technician'],
      'checklist',
    ).calls
    await userEvent.click(await screen.findByLabelText(/Zapojenie stringov a DC kabeláž/))
    await waitFor(() => expect(screen.getByLabelText(/Zapojenie stringov a DC kabeláž/)).toBeChecked())
    expect(calls.find((c) => c.method === 'PUT')?.body).toEqual({ done: true })
    expect(screen.getByText('Povinné kroky: 2 z 2')).toBeInTheDocument()
  })

  it('is read only outside mounting and for roles without field access', async () => {
    setup('Inspection', (c) => (c.path === '/api/installations/i1/checklist' ? json(checklist({ editable: false })) : undefined), ['Admin'], 'checklist')
    expect(await screen.findByText('Checklist sa dá upravovať len počas montáže.')).toBeInTheDocument()
    for (const box of screen.getAllByRole('checkbox')) expect(box).toBeDisabled()
  })

  it('points to the checklist while mounting', async () => {
    const { router } = setup('Mounting')
    await userEvent.click(await screen.findByRole('button', { name: 'Otvoriť checklist' }))
    expect(router.state.location.search).toContain('tab=checklist')
  })
})

describe('installation revision and handover tab', () => {
  it('offers recording the revision report from the header during inspection', async () => {
    const { router } = setup('Inspection', (c) => (c.path === '/api/installations/i1/revision-reports' ? json([]) : undefined))
    await userEvent.click(await screen.findByRole('button', { name: 'Zapísať revíznu správu' }))
    expect(router.state.location.search).toContain('tab=revision')
    expect(await screen.findByRole('form', { name: 'Revízna správa' })).toBeInTheDocument()
  })

  it('validates the revision form before sending', async () => {
    const { calls } = setup('Inspection', (c) => (c.path === '/api/installations/i1/revision-reports' ? json([]) : undefined), ['Admin'], 'revision')
    const form = await screen.findByRole('form', { name: 'Revízna správa' })
    await userEvent.click(within(form).getByRole('radio', { name: 'Nevyhovela' }))
    await userEvent.click(within(form).getByRole('button', { name: 'Zapísať revíziu' }))
    expect(await within(form).findByText('Zadajte meno revízneho technika.')).toBeInTheDocument()
    expect(within(form).getByText('Pri nevyhovujúcej revízii popíšte zistené nedostatky.')).toBeInTheDocument()
    expect(within(form).getByText('Priložte revíznu správu (PDF, PNG alebo JPEG).')).toBeInTheDocument()
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('records a passed revision with the file and reloads the installation', async () => {
    let reports: unknown[] = []
    const { calls, setState } = setup(
      'Inspection',
      (c) => {
        if (c.path === '/api/installations/i1/revision-reports' && c.method === 'GET') return json(reports)
        if (c.path === '/api/installations/i1/revision-reports' && c.method === 'POST') {
          reports = [report()]
          setState({ state: 'InspectionPassed', allowedTransitions: [] })
          return json(report(), 201)
        }
      },
      ['Technician'],
      'revision',
    )
    const form = await screen.findByRole('form', { name: 'Revízna správa' })
    await userEvent.clear(within(form).getByLabelText(/^Dátum revízie/))
    await userEvent.type(within(form).getByLabelText(/^Dátum revízie/), '2026-11-05')
    await userEvent.type(within(form).getByLabelText(/^Revízny technik/), 'Ing. Peter Revízny')
    await userEvent.type(within(form).getByLabelText(/Číslo osvedčenia/), 'REV-123')
    await userEvent.type(within(form).getByLabelText(/^Platnosť do/), '2030-11-05')
    const file = new File(['%PDF-1.4 x'], 'revizia.pdf', { type: 'application/pdf' })
    await userEvent.upload(within(form).getByLabelText(/^Súbor revíznej správy/), file)
    await userEvent.click(within(form).getByRole('button', { name: 'Zapísať revíziu' }))

    expect(await screen.findByText('Revízna správa bola zapísaná.')).toBeInTheDocument()
    const post = calls.find((c) => c.method === 'POST')
    expect(post?.body).toMatchObject({
      inspectedOn: '2026-11-05',
      inspectorName: 'Ing. Peter Revízny',
      inspectorCertificateNumber: 'REV-123',
      result: 'Passed',
      validUntil: '2030-11-05',
      fileName: 'revizia.pdf',
      contentType: 'application/pdf',
    })
    expect((post?.body as { contentBase64: string }).contentBase64).toBe(btoa('%PDF-1.4 x'))
    expect(await screen.findByText('Ing. Peter Revízny')).toBeInTheDocument()
    expect(await screen.findByRole('form', { name: 'Uvedenie do prevádzky' })).toBeInTheDocument()
  })

  it('shows a server conflict on the revision form', async () => {
    setup(
      'Inspection',
      (c) => {
        if (c.path === '/api/installations/i1/revision-reports' && c.method === 'GET') return json([])
        if (c.path === '/api/installations/i1/revision-reports' && c.method === 'POST') return problem(409, 'Revízia nie je možná v tomto stave.')
      },
      ['Admin'],
      'revision',
    )
    const form = await screen.findByRole('form', { name: 'Revízna správa' })
    await userEvent.type(within(form).getByLabelText(/^Revízny technik/), 'Ing. Peter Revízny')
    await userEvent.type(within(form).getByLabelText(/^Platnosť do/), '2099-01-01')
    await userEvent.upload(within(form).getByLabelText(/^Súbor revíznej správy/), new File(['%PDF'], 'r.pdf', { type: 'application/pdf' }))
    await userEvent.click(within(form).getByRole('button', { name: 'Zapísať revíziu' }))
    expect(await within(form).findByText('Revízia nie je možná v tomto stave.')).toBeInTheDocument()
  })

  it('commissions an installation with a passed revision', async () => {
    const { calls } = setup(
      'InspectionPassed',
      (c) => {
        if (c.path === '/api/installations/i1/revision-reports') return json([report()])
        if (c.path === '/api/installations/i1/commission')
          return json(detailOf({ ...PLAN, state: 'Commissioned', allowedTransitions: ['HandedOver'] }))
        if (c.path === '/api/installations/i1/documentation') return problem(409, 'not yet')
      },
      ['Admin'],
      'revision',
    )
    const form = await screen.findByRole('form', { name: 'Uvedenie do prevádzky' })
    await userEvent.type(within(form).getByLabelText(/^Prevzal \(zákazník\)/), 'Ján Novák')
    await userEvent.click(within(form).getByRole('button', { name: 'Uviesť do prevádzky' }))
    expect(await screen.findByText('Elektráreň bola uvedená do prevádzky.')).toBeInTheDocument()
    expect(calls.find((c) => c.path.endsWith('/commission'))?.body).toMatchObject({ acceptedBy: 'Ján Novák' })
    expect(screen.getAllByText('Uvedená do prevádzky').length).toBeGreaterThan(0)
  })

  it('offers commissioning from the header after a passed revision', async () => {
    setup('InspectionPassed', (c) => (c.path === '/api/installations/i1/revision-reports' ? json([report()]) : undefined))
    await userEvent.click(await screen.findByRole('button', { name: 'Uviesť do prevádzky' }))
    expect(await screen.findByRole('form', { name: 'Uvedenie do prevádzky' })).toBeInTheDocument()
  })

  it('shows the documentation package and downloads the hand-over protocol', async () => {
    // jsdom has no object URLs; set them for this test only.
    const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL }
    const createObjectURL = vi.fn(() => 'blob:x')
    URL.createObjectURL = createObjectURL
    URL.revokeObjectURL = vi.fn()
    onTestFinished(() => {
      URL.createObjectURL = original.create
      URL.revokeObjectURL = original.revoke
    })
    const { calls } = setup(
      'Commissioned',
      (c) => {
        if (c.path === '/api/installations/i1/revision-reports') return json([report()])
        if (c.path === '/api/installations/i1/documentation')
          return json({
            installationId: 'i1',
            installationName: 'FVE Nitra',
            state: 'Commissioned',
            protocol: {
              commissionedOn: '2026-11-06',
              acceptedBy: 'Ján Novák',
              note: null,
              commissionedBy: 'u1',
              monitoringEnabled: true,
              monitoringEnabledAt: '2026-11-06T10:00:00Z',
            },
            revisionReport: report(),
            devices: [{ type: 'Panel', serialNumber: 'SN-1', manufacturer: 'JA', model: 'M', powerW: 450 }],
            downloads: [],
          })
        if (c.path === '/api/installations/i1/documentation/handover-protocol') return new Response('%PDF', { status: 200, headers: { 'Content-Type': 'application/pdf' } })
      },
      ['Operator'],
      'revision',
    )
    const docs = await screen.findByRole('region', { name: 'Dokumentácia' })
    expect(await within(docs).findByText('Ján Novák')).toBeInTheDocument()
    expect(within(docs).getByText('Zapnutý')).toBeInTheDocument()
    expect(within(docs).getByText('1')).toBeInTheDocument()
    await userEvent.click(within(docs).getByRole('button', { name: 'Stiahnuť odovzdávací protokol' }))
    await waitFor(() => expect(createObjectURL).toHaveBeenCalled())
    expect(calls.some((c) => c.path.endsWith('/handover-protocol'))).toBe(true)
  })

  it('hides the forms for roles without field access', async () => {
    setup('Inspection', (c) => (c.path === '/api/installations/i1/revision-reports' ? json([]) : undefined), ['Sales'], 'revision')
    expect(await screen.findByText('Zatiaľ nie je zapísaná žiadna revízna správa.')).toBeInTheDocument()
    expect(screen.queryByRole('form', { name: 'Revízna správa' })).not.toBeInTheDocument()
  })
})
