import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { json, mockApi, problem } from '../features/quotes/testUtils'
import { SEEN_STORAGE_KEY } from '../features/notifications/notifications'
import { NotificationBell } from './NotificationBell'

const NOW = Date.now()
const minutesAgo = (m: number) => new Date(NOW - m * 60_000).toISOString()

const ITEMS = [
  { id: 'n1', kind: 'ticket', title: 'Chyba izolácie striedača', createdAt: minutesAgo(5), link: '/tickets/t1' },
  { id: 'n2', kind: 'connection_request', title: 'Žiadosť o pripojenie FVE-0231', createdAt: minutesAgo(60), link: '/connection-requests/c1' },
  { id: 'n3', kind: 'ticket', title: 'Výpadok komunikácie dataloggera', createdAt: minutesAgo(120), link: '/tickets/t3' },
] as const

function Location() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}</output>
}

function renderBell() {
  return render(
    <MemoryRouter initialEntries={['/monitoring']}>
      <NotificationBell />
      <Location />
    </MemoryRouter>,
  )
}

const bell = () => screen.getByRole('button', { name: /^Notifikácie/ })
const dialog = () => screen.getByRole('dialog', { name: 'Notifikácie' })
const queryDialog = () => screen.queryByRole('dialog', { name: 'Notifikácie' })
const links = () => within(dialog()).getAllByRole('link')
const closeButton = () => within(dialog()).getByRole('button', { name: 'Zavrieť' })
const badge = () => bell().querySelector('.topbar__badge')

function storedSeen(): string[] {
  return JSON.parse(localStorage.getItem(SEEN_STORAGE_KEY) ?? '[]')
}

describe('NotificationBell', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('shows the number of unseen notifications on the badge and in the accessible name', async () => {
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()

    expect(await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })).toBeInTheDocument()
    expect(badge()).toHaveTextContent('3')
    expect(badge()).toHaveAttribute('aria-hidden', 'true')
  })

  it('counts only items that are not in the seen list', async () => {
    localStorage.setItem(SEEN_STORAGE_KEY, JSON.stringify(['n1']))
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()

    await waitFor(() => expect(badge()).toHaveTextContent('2'))
  })

  it('shows no badge when there are no notifications', async () => {
    mockApi({ 'GET /api/notifications': json({ items: [] }) })
    renderBell()

    await waitFor(() => expect(screen.getByRole('button', { name: 'Notifikácie' })).toBeInTheDocument())
    expect(badge()).toBeNull()
  })

  it('opens a modal dialog labelled by its title', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    expect(bell()).toHaveAttribute('aria-haspopup', 'dialog')
    expect(bell()).toHaveAttribute('aria-expanded', 'false')
    expect(queryDialog()).toBeNull()

    await user.click(bell())

    expect(dialog()).toHaveAttribute('aria-modal', 'true')
    expect(dialog()).toHaveAccessibleName('Notifikácie')
    expect(bell()).toHaveAttribute('aria-expanded', 'true')
    expect(links()).toHaveLength(3)
  })

  it('moves focus into the dialog on open', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    await user.click(bell())

    expect(closeButton()).toHaveFocus()
  })

  it('closes on Escape and returns focus to the bell', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    await user.click(bell())
    expect(dialog()).toBeInTheDocument()

    await user.keyboard('{Escape}')
    expect(queryDialog()).toBeNull()
    expect(bell()).toHaveFocus()
    expect(bell()).toHaveAttribute('aria-expanded', 'false')
  })

  it('keeps Tab and Shift+Tab inside the dialog', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })
    await user.click(bell())

    // Focus starts on the close button (last focusable), so Tab wraps to the first link.
    await user.tab()
    expect(links()[0]).toHaveFocus()
    await user.tab({ shift: true })
    expect(closeButton()).toHaveFocus()

    for (let i = 0; i < 6; i += 1) {
      await user.tab()
      expect(dialog()).toContainElement(document.activeElement as HTMLElement)
    }
    expect(queryDialog()).toBeInTheDocument()
  })

  it('closes with the close button and returns focus to the bell', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    await user.click(bell())
    await user.click(closeButton())

    expect(queryDialog()).toBeNull()
    expect(bell()).toHaveFocus()
  })

  it('closes on a backdrop click but not on a click inside the dialog', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    await user.click(bell())
    await user.click(dialog())
    expect(queryDialog()).toBeInTheDocument()

    await user.click(dialog().parentElement as HTMLElement)
    expect(queryDialog()).toBeNull()
  })

  it('navigates to the item link and closes the dialog', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    await user.click(bell())
    await user.click(within(dialog()).getByRole('link', { name: /Žiadosť o pripojenie FVE-0231/ }))

    expect(screen.getByTestId('location')).toHaveTextContent('/connection-requests/c1')
    expect(queryDialog()).toBeNull()
  })

  it('marks the loaded items as seen when the dialog opens', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    await user.click(bell())

    await waitFor(() => expect(badge()).toBeNull())
    expect(bell()).toHaveAccessibleName('Notifikácie')
    expect(storedSeen()).toEqual(['n1', 'n2', 'n3'])
  })

  it('shows the kind and the relative time of each item', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: ITEMS }) })
    renderBell()
    await screen.findByRole('button', { name: 'Notifikácie, neprečítané: 3' })

    await user.click(bell())
    const first = links()[0]
    expect(within(first).getByText('Servisný tiket')).toBeInTheDocument()
    expect(first.querySelector('time')).toHaveAttribute('datetime', ITEMS[0].createdAt)
    expect(within(links()[1]).getByText('Žiadosť o pripojenie')).toBeInTheDocument()
  })

  it('shows the loading state until the first response arrives', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': () => new Promise<Response>(() => {}) })
    renderBell()

    await user.click(bell())
    expect(within(dialog()).getByRole('status')).toHaveTextContent('Načítavam notifikácie…')
  })

  it('shows the error state with a retry button that loads again', async () => {
    const user = userEvent.setup()
    let calls = 0
    mockApi({
      'GET /api/notifications': () => {
        calls += 1
        return calls === 1 ? problem(503, 'Unavailable') : json({ items: ITEMS })
      },
    })
    renderBell()

    await user.click(bell())
    expect(await within(dialog()).findByText('Notifikácie sa nepodarilo načítať.')).toBeInTheDocument()

    await user.click(within(dialog()).getByRole('button', { name: 'Skúsiť znova' }))
    expect(await within(dialog()).findAllByRole('link')).toHaveLength(3)
    expect(screen.queryByText('Notifikácie sa nepodarilo načítať.')).toBeNull()
  })

  it('shows the empty state when there are no notifications', async () => {
    const user = userEvent.setup()
    mockApi({ 'GET /api/notifications': json({ items: [] }) })
    renderBell()

    await user.click(bell())
    expect(await within(dialog()).findByText('Zatiaľ nemáte žiadne notifikácie.')).toBeInTheDocument()
  })

  it('polls every 60 seconds and keeps the last items when a refresh fails', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let calls = 0
    const mock = mockApi({
      'GET /api/notifications': () => {
        calls += 1
        return calls === 1 ? json({ items: ITEMS }) : problem(503, 'Unavailable')
      },
    })
    renderBell()
    await waitFor(() => expect(badge()).toHaveTextContent('3'))

    await vi.advanceTimersByTimeAsync(60_000)

    expect(mock).toHaveBeenCalledTimes(2)
    expect(badge()).toHaveTextContent('3')
  })

  it('keeps polling while the dialog is open and shows newly arrived items as seen', async () => {
    const user = userEvent.setup()
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let calls = 0
    const mock = mockApi({
      'GET /api/notifications': () => {
        calls += 1
        return calls === 1 ? json({ items: ITEMS.slice(0, 2) }) : json({ items: ITEMS })
      },
    })
    renderBell()
    await waitFor(() => expect(badge()).toHaveTextContent('2'))

    await user.click(bell())
    await vi.advanceTimersByTimeAsync(60_000)

    expect(mock).toHaveBeenCalledTimes(2)
    await waitFor(() => expect(within(dialog()).getAllByRole('link')).toHaveLength(3))
    expect(badge()).toBeNull()

    // Items that arrived while the dialog was open are written to storage when it closes.
    await user.keyboard('{Escape}')
    expect(storedSeen()).toEqual(['n1', 'n2', 'n3'])
  })
})
