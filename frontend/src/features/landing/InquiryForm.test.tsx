import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { InquiryForm } from './InquiryForm'
import { toInquiry, validateInquiry, EMPTY_INQUIRY } from './inquiry'

async function fillRequired() {
  await userEvent.type(screen.getByLabelText(/Meno a priezvisko/), 'Jana Kováčová')
  await userEvent.type(screen.getByLabelText(/E-mail/), 'jana@domena.sk')
  await userEvent.type(screen.getByLabelText(/Adresa odberného miesta/), 'Hlavná 1, Prešov')
}

describe('validateInquiry', () => {
  it('requires name, e-mail and address', () => {
    expect(Object.keys(validateInquiry(EMPTY_INQUIRY))).toEqual(['name', 'email', 'address'])
  })

  it('accepts consumption with spaces and rejects text', () => {
    const base = { ...EMPTY_INQUIRY, name: 'J', email: 'j@d.sk', address: 'A' }
    expect(validateInquiry({ ...base, consumption: '4 500' })).toEqual({})
    expect(validateInquiry({ ...base, consumption: 'veľa' })).toHaveProperty('consumption')
    expect(toInquiry({ ...base, consumption: '4 500', phone: '  ' })).toEqual({
      name: 'J',
      email: 'j@d.sk',
      address: 'A',
      yearlyConsumptionKwh: 4500,
      phone: undefined,
      message: undefined,
    })
  })
})

describe('InquiryForm', () => {
  it('shows a summary and field errors when required fields are empty', async () => {
    const send = vi.fn()
    render(<InquiryForm onSubmit={send} />)
    await userEvent.type(screen.getByLabelText(/E-mail/), 'jana.kovacova@')
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať dopyt' }))

    expect(screen.getByText('Formulár obsahuje 3 chyby. Opravte ich a odošlite znova.')).toBeInTheDocument()
    const email = screen.getByLabelText(/E-mail/)
    expect(email).toHaveAttribute('aria-invalid', 'true')
    expect(email).toHaveAccessibleDescription('Zadajte platný e-mail, napr. jana@domena.sk.')
    expect(screen.getByLabelText(/Meno a priezvisko/)).toHaveFocus()
    expect(send).not.toHaveBeenCalled()
  })

  it('sends a valid inquiry and shows the confirmation', async () => {
    const send = vi.fn().mockResolvedValue(undefined)
    render(<InquiryForm onSubmit={send} />)
    await fillRequired()
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať dopyt' }))

    expect(send).toHaveBeenCalledWith(expect.objectContaining({ name: 'Jana Kováčová', email: 'jana@domena.sk' }))
    expect(await screen.findByRole('heading', { name: 'Ďakujeme, dopyt sme prijali.' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Poslať ďalší dopyt' }))
    expect(screen.getByLabelText(/Meno a priezvisko/)).toHaveValue('')
  })

  it('keeps the values and shows an alert when sending fails', async () => {
    const send = vi.fn().mockRejectedValue(new Error('offline'))
    render(<InquiryForm onSubmit={send} />)
    await fillRequired()
    await userEvent.click(screen.getByRole('button', { name: 'Odoslať dopyt' }))

    expect(await screen.findByText('Dopyt sa nepodarilo odoslať. Skúste to znova o chvíľu.')).toBeInTheDocument()
    expect(screen.getByLabelText(/Meno a priezvisko/)).toHaveValue('Jana Kováčová')
    expect(screen.getByRole('button', { name: 'Odoslať dopyt' })).toBeEnabled()
  })
})
