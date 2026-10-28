import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, toApiError } from '../api/problem'
import { DataTable, type Column } from './DataTable'
import { FormField } from './forms'
import { useFormSubmit } from './useFormSubmit'

interface Row {
  id: string
  name: string
}
const columns: Column<Row>[] = [
  { key: 'name', header: 'Meno', render: (r) => r.name, sortable: true },
  { key: 'id', header: 'ID', render: (r) => r.id },
]
const rows: Row[] = [
  { id: '1', name: 'Anna' },
  { id: '2', name: 'Boris' },
]

function table(props: Partial<React.ComponentProps<typeof DataTable<Row>>> = {}) {
  return (
    <DataTable
      columns={columns}
      rows={rows}
      rowKey={(r) => r.id}
      page={2}
      pageSize={10}
      totalCount={35}
      onPageChange={() => {}}
      {...props}
    />
  )
}

describe('DataTable', () => {
  afterEach(() => vi.useRealTimers())

  it('pages through results', async () => {
    const onPageChange = vi.fn()
    render(table({ onPageChange }))
    expect(screen.getByText('Strana 2 z 4 (35)')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Ďalšia' }))
    await userEvent.click(screen.getByRole('button', { name: 'Predchádzajúca' }))
    expect(onPageChange).toHaveBeenNthCalledWith(1, 3)
    expect(onPageChange).toHaveBeenNthCalledWith(2, 1)
  })

  it('disables paging at the edges', () => {
    render(table({ page: 1, totalCount: 5 }))
    expect(screen.getByRole('button', { name: 'Predchádzajúca' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Ďalšia' })).toBeDisabled()
  })

  it('toggles sort direction and exposes aria-sort', async () => {
    const onSortChange = vi.fn()
    render(table({ sort: { key: 'name', direction: 'asc' }, onSortChange }))
    expect(screen.getByRole('columnheader', { name: 'Meno' })).toHaveAttribute('aria-sort', 'ascending')
    await userEvent.click(screen.getByRole('button', { name: 'Meno' }))
    expect(onSortChange).toHaveBeenCalledWith({ key: 'name', direction: 'desc' })
  })

  it('debounces the filter', () => {
    vi.useFakeTimers()
    const onFilterChange = vi.fn()
    function Host() {
      const [value, setValue] = useState('')
      return table({
        filter: value,
        onFilterChange: (v) => {
          onFilterChange(v)
          setValue(v)
        },
        filterDebounceMs: 300,
      })
    }
    render(<Host />)
    const input = screen.getByLabelText('Hľadať')
    act(() => {
      input.focus()
    })
    act(() => {
      // simulate typing without user-event timers
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!
      setter.call(input, 'ann')
      input.dispatchEvent(new Event('input', { bubbles: true }))
    })
    expect(onFilterChange).not.toHaveBeenCalled()
    act(() => {
      vi.advanceTimersByTime(300)
    })
    expect(onFilterChange).toHaveBeenCalledWith('ann')
  })

  it('shows empty state, loading and error', () => {
    const { rerender } = render(table({ rows: [], totalCount: 0, page: 1, emptyMessage: 'Nič tu nie je.' }))
    expect(screen.getByText('Nič tu nie je.')).toBeInTheDocument()
    rerender(table({ rows: [], totalCount: 0, page: 1, loading: true }))
    expect(screen.getByRole('status')).toHaveTextContent('Načítavam')
    rerender(table({ rows: [], totalCount: 0, page: 1, error: 'Načítanie zlyhalo.' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Načítanie zlyhalo.')
    expect(screen.queryByText('Žiadne záznamy.')).not.toBeInTheDocument()
  })

  it('calls onRowClick', async () => {
    const onRowClick = vi.fn()
    render(table({ onRowClick }))
    await userEvent.click(within(screen.getAllByRole('row')[1]).getByText('Anna'))
    expect(onRowClick).toHaveBeenCalledWith(rows[0])
  })
})

describe('server validation errors', () => {
  it('parses ProblemDetails errors into camelCase fields', async () => {
    const response = new Response(
      JSON.stringify({ title: 'Validation', errors: { Email: ['Neplatný e-mail.'], 'Address.City': ['Povinné.'] } }),
      { status: 400 },
    )
    const error = await toApiError(response)
    expect(error.status).toBe(400)
    expect(error.fieldErrors).toEqual({ email: ['Neplatný e-mail.'], 'address.city': ['Povinné.'] })
  })

  it('falls back to status text for non-JSON bodies', async () => {
    const error = await toApiError(new Response('oops', { status: 502 }))
    expect(error.message).toBe('HTTP 502')
  })

  it('FormField shows server errors accessibly', async () => {
    function Form({ fail }: { fail: () => Promise<unknown> }) {
      const { fieldErrors, formError, submit } = useFormSubmit(fail)
      return (
        <form onSubmit={(e) => { e.preventDefault(); void submit() }}>
          <FormField label="E-mail" errors={fieldErrors.email}>
            {(props) => <input {...props} />}
          </FormField>
          {formError && <p role="alert">{formError}</p>}
          <button type="submit">Uložiť</button>
        </form>
      )
    }
    const fail = vi.fn().mockRejectedValue(new ApiError(400, 'Validation', { email: ['Neplatný e-mail.'] }))
    render(<Form fail={fail} />)
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Neplatný e-mail.')
    expect(screen.getByLabelText('E-mail')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows a form level error for non-validation failures', async () => {
    function Form() {
      const { formError, submit } = useFormSubmit(() => Promise.reject(new ApiError(409, 'Zákazník už existuje.')))
      return (
        <>
          <button type="button" onClick={() => void submit()}>Uložiť</button>
          {formError && <p role="alert">{formError}</p>}
        </>
      )
    }
    render(<Form />)
    await userEvent.click(screen.getByRole('button', { name: 'Uložiť' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Zákazník už existuje.')
  })
})
