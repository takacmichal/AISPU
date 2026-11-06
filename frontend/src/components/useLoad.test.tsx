import { act, render, renderHook, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'
import { formatUpdatedAgo } from './formatUpdatedAgo'
import { UpdatedIndicator } from './UpdatedIndicator'
import { nextPollDelay, useLoad, type LoadContext } from './useLoad'

const BASE = 60_000

function setVisibility(state: 'visible' | 'hidden') {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state })
}

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms)
  })
}

describe('nextPollDelay', () => {
  it('uses the base interval without failures', () => {
    expect(nextPollDelay(BASE, 0)).toBe(BASE)
  })

  it('backs off exponentially with jitter, capped at 8x', () => {
    expect(nextPollDelay(BASE, 1, () => 1)).toBe(2 * BASE)
    expect(nextPollDelay(BASE, 2, () => 1)).toBe(4 * BASE)
    expect(nextPollDelay(BASE, 2, () => 0)).toBe(2 * BASE)
    expect(nextPollDelay(BASE, 10, () => 1)).toBe(8 * BASE)
  })
})

describe('useLoad polling', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    setVisibility('visible')
  })
  afterEach(() => {
    vi.useRealTimers()
    setVisibility('visible')
  })

  it('refreshes in the interval and keeps the last data visible meanwhile', async () => {
    let calls = 0
    let release: (value: number) => void = () => {}
    const load = vi.fn(() => {
      calls += 1
      if (calls === 1) return Promise.resolve(1)
      return new Promise<number>((resolve) => (release = resolve))
    })
    const { result } = renderHook(() => useLoad(load, 'k', { intervalMs: BASE }))
    await advance(0)
    expect(result.current.data).toBe(1)
    expect(result.current.updatedAt).not.toBeNull()

    await advance(BASE)
    expect(load).toHaveBeenCalledTimes(2)
    expect(result.current.loading).toBe(false)
    expect(result.current.data).toBe(1)

    await act(async () => release(2))
    expect(result.current.data).toBe(2)
  })

  it('passes background=true only for refreshes', async () => {
    const load = vi.fn((...args: [AbortSignal, LoadContext]) => Promise.resolve(args.length))
    renderHook(() => useLoad(load, 'k', { intervalMs: BASE }))
    await advance(0)
    await advance(BASE)
    expect(load.mock.calls.map((args) => args[1].background)).toEqual([false, true])
  })

  it('does not poll when disabled', async () => {
    const load = vi.fn(() => Promise.resolve(1))
    renderHook(() => useLoad(load, 'k', { intervalMs: BASE, enabled: false }))
    await advance(BASE * 10)
    expect(load).toHaveBeenCalledTimes(1)
  })

  it('does not overlap the first load or a running refresh', async () => {
    const slowFirst = vi.fn(() => new Promise<number>(() => {}))
    renderHook(() => useLoad(slowFirst, 'k', { intervalMs: BASE }))
    await advance(BASE * 5)
    expect(slowFirst).toHaveBeenCalledTimes(1)

    let calls = 0
    const slowRefresh = vi.fn(() => (++calls === 1 ? Promise.resolve(1) : new Promise<number>(() => {})))
    renderHook(() => useLoad(slowRefresh, 'k2', { intervalMs: BASE }))
    await advance(BASE * 5)
    expect(slowRefresh).toHaveBeenCalledTimes(2)
  })
  it('pauses while the tab is hidden and refreshes at once when it becomes visible', async () => {
    const load = vi.fn(() => Promise.resolve(1))
    renderHook(() => useLoad(load, 'k', { intervalMs: BASE }))
    await advance(0)
    setVisibility('hidden')
    await advance(BASE * 3)
    expect(load).toHaveBeenCalledTimes(1)

    setVisibility('visible')
    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
    })
    await advance(0)
    expect(load).toHaveBeenCalledTimes(2)
  })

  it.each(['focus', 'online'])('refreshes at once on %s', async (name) => {
    const load = vi.fn(() => Promise.resolve(1))
    renderHook(() => useLoad(load, 'k', { intervalMs: BASE }))
    await advance(0)
    await act(async () => {
      window.dispatchEvent(new Event(name))
    })
    await advance(0)
    expect(load).toHaveBeenCalledTimes(2)
  })

  it('keeps data after a failed refresh, backs off and recovers to the base interval', async () => {
    let fail = false
    const load = vi.fn(() => (fail ? Promise.reject(new Error('down')) : Promise.resolve(7)))
    const { result } = renderHook(() => useLoad(load, 'k', { intervalMs: BASE }))
    await advance(0)

    fail = true
    await advance(BASE)
    expect(load).toHaveBeenCalledTimes(2)
    expect(result.current.refreshError).toBe(true)
    expect(result.current.error).toBe(false)
    expect(result.current.data).toBe(7)

    // after one failure the delay is at least the base interval and at most 2x
    await advance(BASE - 1)
    expect(load).toHaveBeenCalledTimes(2)
    fail = false
    await advance(BASE + 1)
    expect(load).toHaveBeenCalledTimes(3)
    expect(result.current.refreshError).toBe(false)

    await advance(BASE)
    expect(load).toHaveBeenCalledTimes(4)
  })

  it('aborts the running request on unmount and stops polling', async () => {
    const signals: AbortSignal[] = []
    const load = vi.fn((signal: AbortSignal) => {
      signals.push(signal)
      return signals.length === 1 ? Promise.resolve(1) : new Promise<number>(() => {})
    })
    const { unmount } = renderHook(() => useLoad(load, 'k', { intervalMs: BASE }))
    await advance(BASE)
    expect(signals[1].aborted).toBe(false)
    unmount()
    expect(signals[1].aborted).toBe(true)
    await advance(BASE * 3)
    expect(load).toHaveBeenCalledTimes(2)
  })

  it('refresh() reloads in the background without clearing the data', async () => {
    const load = vi.fn(() => Promise.resolve(1))
    const { result } = renderHook(() => useLoad(load, 'k', { intervalMs: BASE }))
    await advance(0)
    act(() => result.current.refresh())
    expect(result.current.data).toBe(1)
    await advance(0)
    expect(load).toHaveBeenCalledTimes(2)
  })
})

describe('UpdatedIndicator', () => {
  it('formats the age of the last update', () => {
    const at = new Date('2026-11-05T10:00:00Z')
    expect(formatUpdatedAgo(at, new Date('2026-11-05T10:00:30Z'))).toBe('Aktualizované práve teraz')
    expect(formatUpdatedAgo(at, new Date('2026-11-05T10:05:10Z'))).toBe('Aktualizované pred 5 min')
    expect(formatUpdatedAgo(at, new Date('2026-11-05T12:10:00Z'))).toBe('Aktualizované pred 2 h')
  })

  it('shows the error and a retry button after a failed refresh', async () => {
    const onRetry = vi.fn()
    render(<UpdatedIndicator updatedAt={new Date()} refreshError onRetry={onRetry} />)
    await userEvent.click(screen.getByRole('button', { name: 'Skúsiť znova' }))
    expect(onRetry).toHaveBeenCalled()
  })

  it('renders nothing before the first load', () => {
    const { container } = render(<UpdatedIndicator updatedAt={null} refreshError={false} onRetry={() => {}} />)
    expect(container).toBeEmptyDOMElement()
  })
})
