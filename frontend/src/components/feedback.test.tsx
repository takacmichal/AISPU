import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, userMessage } from '../api/problem'
import { ConfirmDialog } from './ConfirmDialog'
import { ConfirmProvider, useConfirm } from './confirm'
import { DataTable } from './DataTable'
import { Dialog } from './Dialog'
import { EmptyState } from './EmptyState'
import { Skeleton } from './Skeleton'
import { Spinner } from './Spinner'
import { ToastProvider, useToast } from './toast'
import { useFormSubmit } from './useFormSubmit'

describe('loading and empty states', () => {
  it('Skeleton announces its label and hides the bars', () => {
    render(<Skeleton lines={2} label="Načítavam zákazníkov…" />)
    expect(screen.getByRole('status')).toHaveTextContent('Načítavam zákazníkov…')
    expect(document.querySelectorAll('.skeleton')).toHaveLength(2)
  })

  it('Spinner announces its label', () => {
    render(<Spinner />)
    expect(screen.getByRole('status')).toHaveTextContent('Načítavam…')
  })

  it('EmptyState shows title, description and action', () => {
    render(<EmptyState title="Nič tu nie je" description="Pridajte prvý záznam." action={<button>Pridať</button>} />)
    expect(screen.getByText('Nič tu nie je')).toBeInTheDocument()
    expect(screen.getByText('Pridajte prvý záznam.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pridať' })).toBeInTheDocument()
  })

  it('DataTable renders skeleton rows while loading without data', () => {
    render(
      <DataTable columns={[{ key: 'a', header: 'A', render: () => 'x' }]} rows={[]} rowKey={() => 'k'} page={1} pageSize={10} totalCount={0} onPageChange={() => {}} loading />,
    )
    expect(document.querySelectorAll('tbody .skeleton')).toHaveLength(5)
    expect(screen.getByRole('status')).toHaveTextContent('Načítavam')
  })
})

describe('Dialog', () => {
  it('closes on Escape and traps Tab inside', async () => {
    const onClose = vi.fn()
    render(
      <>
        <button>Mimo</button>
        <Dialog title="Test" onClose={onClose}>
          <button>Prvé</button>
          <button>Posledné</button>
        </Dialog>
      </>,
    )
    expect(screen.getByRole('button', { name: 'Prvé' })).toHaveFocus()
    await userEvent.tab({ shift: true })
    expect(screen.getByRole('button', { name: 'Posledné' })).toHaveFocus()
    await userEvent.tab()
    expect(screen.getByRole('button', { name: 'Prvé' })).toHaveFocus()
    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})

describe('ConfirmDialog', () => {
  it('focuses cancel first and reports confirm, cancel and error', async () => {
    const onConfirm = vi.fn()
    const onCancel = vi.fn()
    render(<ConfirmDialog title="Zmazať?" message="Naozaj?" confirmLabel="Zmazať" danger error="Konflikt." onConfirm={onConfirm} onCancel={onCancel} />)
    expect(screen.getByRole('dialog', { name: 'Zmazať?' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Zrušiť' })).toHaveFocus()
    expect(screen.getByRole('alert')).toHaveTextContent('Konflikt.')
    await userEvent.click(screen.getByRole('button', { name: 'Zmazať' }))
    expect(onConfirm).toHaveBeenCalled()
    await userEvent.keyboard('{Escape}')
    expect(onCancel).toHaveBeenCalled()
  })

  it('useConfirm resolves true on confirm and false on cancel', async () => {
    const results: boolean[] = []
    function Host() {
      const confirm = useConfirm()
      return <button onClick={async () => results.push(await confirm({ title: 'Potvrdiť?', message: 'Naozaj?', confirmLabel: 'Áno' }))}>Spustiť</button>
    }
    render(
      <ConfirmProvider>
        <Host />
      </ConfirmProvider>,
    )
    await userEvent.click(screen.getByRole('button', { name: 'Spustiť' }))
    await userEvent.click(screen.getByRole('button', { name: 'Áno' }))
    await userEvent.click(screen.getByRole('button', { name: 'Spustiť' }))
    await userEvent.click(screen.getByRole('button', { name: 'Zrušiť' }))
    expect(results).toEqual([true, false])
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})

describe('toasts', () => {
  afterEach(() => vi.useRealTimers())

  it('announces in a polite live region, can be dismissed and auto-expires', () => {
    vi.useFakeTimers()
    function Host() {
      const toast = useToast()
      return (
        <>
          <button onClick={() => toast.success('Uložené.')}>ok</button>
          <button onClick={() => toast.error('Chyba.')}>err</button>
        </>
      )
    }
    render(
      <ToastProvider>
        <Host />
      </ToastProvider>,
    )
    expect(screen.getByRole('region', { name: 'Oznámenia' })).toHaveAttribute('aria-live', 'polite')
    act(() => screen.getByText('ok').click())
    act(() => screen.getByText('err').click())
    expect(screen.getAllByRole('status')).toHaveLength(2)
    act(() => screen.getAllByRole('button', { name: 'Zavrieť' })[0].click())
    expect(screen.getAllByRole('status')).toHaveLength(1)
    act(() => void vi.advanceTimersByTime(9000))
    expect(screen.queryAllByRole('status')).toHaveLength(0)
  })
})

describe('global error handling', () => {
  it('maps statuses to Slovak messages and keeps 400/409 details', () => {
    expect(userMessage(new ApiError(401, 'x'))).toMatch(/relácie vypršala/)
    expect(userMessage(new ApiError(403, 'Forbidden'))).toMatch(/oprávnenie/)
    expect(userMessage(new ApiError(404, 'Not Found'))).toMatch(/nenašiel/)
    expect(userMessage(new ApiError(409, 'Zákazník už existuje.'))).toBe('Zákazník už existuje.')
    expect(userMessage(new ApiError(409, 'HTTP 409'))).toMatch(/konflikte/)
    expect(userMessage(new ApiError(500, 'boom'))).toMatch(/chyba/)
    expect(userMessage(new TypeError('Failed to fetch'))).toMatch(/nedostupný/)
  })

  it('useFormSubmit raises a toast for 403/404/409/5xx and uses the Slovak message', async () => {
    function Host({ error }: { error: Error }) {
      const { formError, submit } = useFormSubmit(() => Promise.reject(error))
      return (
        <>
          <button onClick={() => void submit()}>go</button>
          {formError && <p role="alert">{formError}</p>}
        </>
      )
    }
    for (const [status, text] of [
      [403, /oprávnenie/],
      [404, /nenašiel/],
      [500, /chyba/],
    ] as const) {
      const view = render(
        <ToastProvider>
          <Host error={new ApiError(status, 'x')} />
        </ToastProvider>,
      )
      await userEvent.click(screen.getByText('go'))
      expect(await screen.findByRole('status')).toHaveTextContent(text)
      view.unmount()
    }
  })
})
