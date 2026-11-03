import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import InstallationDetailPage from './InstallationDetailPage'
import { device, detailOf, installation, json, problem, renderRoute, stubFetch, type Call } from './testHelpers'

afterEach(() => vi.unstubAllGlobals())

function render(roles: string[] = ['Admin'], route = '/installations/i1', props = {}) {
  return renderRoute(<InstallationDetailPage {...props} />, route, '/installations/:id', roles)
}

const base = (extra?: (call: Call) => Response | undefined) => (call: Call) => {
  if (call.path === '/api/installations/i1' && call.method === 'GET') return json(detailOf())
  if (call.path === '/api/customers/c1') return json({ id: 'c1', displayName: 'Ján Novák' })
  return extra?.(call)
}

describe('InstallationDetailPage', () => {
  it('shows header, state badge, overview and history', async () => {
    stubFetch(base())
    render()
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'FVE Nitra', level: 2 })).toBeInTheDocument()
    expect(screen.getAllByText('Naplánovaná').length).toBeGreaterThan(0)
    expect(await screen.findByText('Ján Novák')).toBeInTheDocument()
    expect(screen.getByText(/\(Vytvorená\)/)).toBeInTheDocument()
  })

  it('shows an error when the installation cannot be loaded', async () => {
    stubFetch(() => new Response(null, { status: 404 }))
    render()
    expect(await screen.findByText('Inštaláciu sa nepodarilo načítať.')).toBeInTheDocument()
  })

  it('asks for confirmation before a transition and posts the target state', async () => {
    const calls = stubFetch(
      base((c) => {
        if (c.path === '/api/installations/i1/transitions') {
          return json(detailOf({ state: 'ComponentsOrdered', allowedTransitions: ['ComponentsReceived'] }))
        }
      }),
    )
    render()
    await userEvent.click(await screen.findByRole('button', { name: 'Komponenty objednané' }))
    const dialog = await screen.findByRole('dialog')
    expect(calls.some((c) => c.path.endsWith('/transitions'))).toBe(false)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Komponenty objednané' }))
    await waitFor(() => expect(calls.find((c) => c.path.endsWith('/transitions'))?.body).toEqual({ targetState: 'ComponentsOrdered' }))
    expect(await screen.findByRole('button', { name: 'Komponenty prijaté' })).toBeInTheDocument()
  })

  it('does not post when the confirmation is cancelled', async () => {
    const calls = stubFetch(base())
    render()
    await userEvent.click(await screen.findByRole('button', { name: 'Komponenty objednané' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Zrušiť' }))
    expect(calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows a conflict from the server when the transition is rejected', async () => {
    stubFetch(base((c) => (c.path.endsWith('/transitions') ? problem(409, 'Neplatný prechod.') : undefined)))
    render()
    await userEvent.click(await screen.findByRole('button', { name: 'Komponenty objednané' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Komponenty objednané' }))
    expect(await screen.findByText('Neplatný prechod.')).toBeInTheDocument()
  })

  it('disables the Mounting transition until a schedule and technician exist', async () => {
    stubFetch((c) =>
      c.path === '/api/installations/i1'
        ? json(detailOf({ state: 'ComponentsReceived', allowedTransitions: ['Mounting'] }))
        : undefined,
    )
    render()
    expect(await screen.findByRole('button', { name: 'Montáž' })).toBeDisabled()
  })

  it('hides transitions and planning for roles without access', async () => {
    stubFetch(base())
    render(['Sales'])
    await screen.findByRole('heading', { name: 'FVE Nitra' })
    expect(screen.queryByRole('button', { name: 'Komponenty objednané' })).not.toBeInTheDocument()
  })

  it('renders the connection slot, or a placeholder when it is not wired', async () => {
    stubFetch(base())
    const { unmount } = render(['Admin'], '/installations/i1?tab=connection')
    expect(await screen.findByText('Sprievodca pripojením zatiaľ nie je zapojený.')).toBeInTheDocument()
    unmount()
    render(['Admin'], '/installations/i1?tab=connection', { renderConnection: (i: { id: string }) => <p>Stepper {i.id}</p> })
    expect(await screen.findByText('Stepper i1')).toBeInTheDocument()
  })

  it('shows the strings placeholder while the API of PR #342 is not enabled', async () => {
    stubFetch(base())
    render(['Admin'], '/installations/i1?tab=strings')
    expect(await screen.findByText(/PR #342/)).toBeInTheDocument()
  })

  describe('devices tab', () => {
    const devicesHandler = (extra?: (c: Call) => Response | undefined) =>
      base((c) => {
        if (c.path === '/api/installations/i1/devices') return json([device()])
        if (c.path === '/api/installations/i1/devices/d1/history')
          return json([
            device({ status: 'Replaced', removalReason: 'Chybná bunka', removedAt: '2026-03-01T10:00:00Z' }),
            device({ id: 'd2', serialNumber: 'JA2411-000456', replacesDeviceId: 'd1' }),
          ])
        return extra?.(c)
      })

    it('lists devices with monospace serial numbers and shows history', async () => {
      stubFetch(devicesHandler())
      render(['Admin'], '/installations/i1?tab=devices')
      const serial = await screen.findByText('JA2410-000123')
      expect(serial).toHaveClass('mono')
      await userEvent.click(screen.getByRole('button', { name: 'História JA2410-000123' }))
      const dialog = await screen.findByRole('dialog')
      expect(await within(dialog).findByText('JA2411-000456')).toBeInTheDocument()
      expect(within(dialog).getByText(/Chybná bunka/)).toBeInTheDocument()
    })

    it('replaces a device, validating required fields first', async () => {
      const calls = stubFetch(
        devicesHandler((c) => (c.path.endsWith('/replace') ? json({ replaced: device(), replacement: device({ id: 'd2' }) }) : undefined)),
      )
      render(['Technician'], '/installations/i1?tab=devices')
      await userEvent.click(await screen.findByRole('button', { name: 'Vymeniť JA2410-000123' }))
      const dialog = await screen.findByRole('dialog')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Vymeniť' }))
      expect(within(dialog).getAllByText('Pole je povinné.').length).toBe(2)
      await userEvent.type(within(dialog).getByLabelText('Sériové číslo nového zariadenia'), 'JA2411-000456')
      await userEvent.type(within(dialog).getByLabelText('Dôvod výmeny'), 'Chybná bunka')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Vymeniť' }))
      await waitFor(() =>
        expect(calls.find((c) => c.path.endsWith('/replace'))?.body).toMatchObject({
          serialNumber: 'JA2411-000456',
          reason: 'Chybná bunka',
          powerW: 450,
        }),
      )
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    })

    it('shows a duplicate serial conflict inline', async () => {
      stubFetch(devicesHandler((c) => (c.path.endsWith('/replace') ? problem(409, 'Sériové číslo už existuje.') : undefined)))
      render(['Admin'], '/installations/i1?tab=devices')
      await userEvent.click(await screen.findByRole('button', { name: 'Vymeniť JA2410-000123' }))
      const dialog = await screen.findByRole('dialog')
      await userEvent.type(within(dialog).getByLabelText('Sériové číslo nového zariadenia'), 'DUP')
      await userEvent.type(within(dialog).getByLabelText('Dôvod výmeny'), 'x')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Vymeniť' }))
      expect(await within(dialog).findByText('Sériové číslo už existuje.')).toBeInTheDocument()
    })

    it('hides replace for roles outside the field group', async () => {
      stubFetch(devicesHandler())
      render(['Sales'], '/installations/i1?tab=devices')
      await screen.findByText('JA2410-000123')
      expect(screen.queryByRole('button', { name: /Vymeniť/ })).not.toBeInTheDocument()
    })

    it('shows the empty state', async () => {
      stubFetch(base((c) => (c.path === '/api/installations/i1/devices' ? json([]) : undefined)))
      render(['Admin'], '/installations/i1?tab=devices')
      expect(await screen.findByText(/zatiaľ nie sú zaevidované žiadne zariadenia/)).toBeInTheDocument()
    })
  })

  describe('plan tab', () => {
    const planHandler = (extra?: (c: Call) => Response | undefined) =>
      base((c) => {
        if (c.path === '/api/users') return json({ items: [{ id: 't1', email: 'tech@test.sk', roles: ['Technician'] }, { id: 'a1', email: 'admin@test.sk', roles: ['Admin'] }], page: 1, pageSize: 100, totalCount: 2 })
        if (c.path === '/api/installations')
          return json([installation(), installation({ id: 'i2', name: 'FVE Trnava', scheduledStart: '2026-11-10', scheduledEnd: '2026-11-12', technicianIds: ['t1'] })])
        if (c.path === '/api/contracts/k1') return json({ id: 'k1', status: 'Draft', depositStatus: 'NotPaid' })
        return extra?.(c)
      })

    it('warns about unsigned contract and unpaid deposit', async () => {
      stubFetch(planHandler())
      render(['Admin'], '/installations/i1?tab=plan')
      expect(await screen.findByText('Upozornenie: zmluva nie je podpísaná.')).toBeInTheDocument()
      expect(screen.getByText('Upozornenie: záloha nie je zaplatená.')).toBeInTheDocument()
    })

    it('lists only technicians and warns about a technician collision', async () => {
      stubFetch(planHandler())
      render(['Admin'], '/installations/i1?tab=plan')
      await userEvent.click(await screen.findByLabelText('tech@test.sk'))
      expect(screen.queryByLabelText('admin@test.sk')).not.toBeInTheDocument()
      await userEvent.type(screen.getByLabelText('Začiatok montáže'), '2026-11-11')
      await userEvent.type(screen.getByLabelText('Koniec montáže'), '2026-11-13')
      expect(await screen.findByText(/Kolízia: technik je v rovnakom čase na inštalácii „FVE Trnava“/)).toBeInTheDocument()
    })

    it('saves schedule and technicians', async () => {
      const calls = stubFetch(
        planHandler((c) => {
          if (c.path.endsWith('/schedule')) return json(detailOf({ scheduledStart: '2026-11-04', scheduledEnd: '2026-11-05' }))
          if (c.path.endsWith('/technicians')) return json(detailOf({ scheduledStart: '2026-11-04', scheduledEnd: '2026-11-05', technicianIds: ['t1'] }))
        }),
      )
      render(['Admin'], '/installations/i1?tab=plan')
      await userEvent.click(await screen.findByLabelText('tech@test.sk'))
      await userEvent.type(screen.getByLabelText('Začiatok montáže'), '2026-11-04')
      await userEvent.type(screen.getByLabelText('Koniec montáže'), '2026-11-05')
      await userEvent.click(screen.getByRole('button', { name: 'Uložiť plán' }))
      await waitFor(() => expect(calls.find((c) => c.path.endsWith('/schedule'))?.body).toEqual({ scheduledStart: '2026-11-04', scheduledEnd: '2026-11-05' }))
      await waitFor(() => expect(calls.find((c) => c.path.endsWith('/technicians'))?.body).toEqual({ technicianIds: ['t1'] }))
    })

    it('requires a reason when an existing schedule is changed', async () => {
      stubFetch(
        (c) => {
          if (c.path === '/api/installations/i1') return json(detailOf({ scheduledStart: '2026-11-04', scheduledEnd: '2026-11-05', technicianIds: ['t1'] }))
          return planHandler()(c)
        },
      )
      render(['Admin'], '/installations/i1?tab=plan')
      const start = await screen.findByLabelText('Začiatok montáže')
      await userEvent.clear(start)
      await userEvent.type(start, '2026-11-06')
      await userEvent.click(screen.getByRole('button', { name: 'Uložiť plán' }))
      expect(await screen.findByText('Pri zmene termínu uveďte dôvod.')).toBeInTheDocument()
    })

    it('is read-only for technicians', async () => {
      stubFetch(planHandler())
      render(['Technician'], '/installations/i1?tab=plan')
      expect(await screen.findByText('Plán môžu meniť len plánovači.')).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Uložiť plán' })).not.toBeInTheDocument()
    })
  })
})
