import type { components } from '../../api/generated/schema'
import type { FieldErrors } from '../../api/problem'

type Customer = components['schemas']['CustomerResponse']
type Request = components['schemas']['CreateCustomerRequest']

export interface CustomerFormValues {
  type: 'Person' | 'Company'
  firstName: string
  lastName: string
  companyName: string
  ico: string
  dic: string
  icDph: string
  email: string
  phone: string
  street: string
  city: string
  postalCode: string
  country: string
  note: string
}

export const emptyValues: CustomerFormValues = {
  type: 'Person',
  firstName: '',
  lastName: '',
  companyName: '',
  ico: '',
  dic: '',
  icDph: '',
  email: '',
  phone: '',
  street: '',
  city: '',
  postalCode: '',
  country: 'SK',
  note: '',
}

export function fromCustomer(c: Customer): CustomerFormValues {
  return {
    type: c.type === 'Company' ? 'Company' : 'Person',
    firstName: c.firstName ?? '',
    lastName: c.lastName ?? '',
    companyName: c.companyName ?? '',
    ico: c.ico ?? '',
    dic: c.dic ?? '',
    icDph: c.icDph ?? '',
    email: c.email,
    phone: c.phone ?? '',
    street: c.address.street ?? '',
    city: c.address.city ?? '',
    postalCode: c.address.postalCode ?? '',
    country: c.address.country ?? 'SK',
    note: c.note ?? '',
  }
}

const blankToNull = (value: string) => (value.trim() === '' ? null : value.trim())

export function toRequest(v: CustomerFormValues): Request {
  const company = v.type === 'Company'
  return {
    type: v.type,
    firstName: company ? null : blankToNull(v.firstName),
    lastName: company ? null : blankToNull(v.lastName),
    companyName: company ? blankToNull(v.companyName) : null,
    ico: company ? blankToNull(v.ico) : null,
    dic: company ? blankToNull(v.dic) : null,
    icDph: company ? blankToNull(v.icDph) : null,
    email: v.email.trim(),
    phone: blankToNull(v.phone),
    address: { street: v.street.trim(), city: v.city.trim(), postalCode: v.postalCode.trim(), country: v.country.trim() || 'SK' },
    note: blankToNull(v.note),
  }
}

/** Mirrors the server rules (CustomerDetails.Validate) so most mistakes are caught before the request. */
export function isValidIco(ico: string): boolean {
  if (!/^\d{8}$/.test(ico)) return false
  let sum = 0
  for (let i = 0; i < 7; i++) sum += Number(ico[i]) * (8 - i)
  return Number(ico[7]) === (11 - (sum % 11)) % 10
}

const strip = (value: string) => value.replace(/\s+/g, '').toUpperCase()

export function validateCustomer(v: CustomerFormValues): FieldErrors {
  const errors: FieldErrors = {}
  const add = (field: string, message: string) => (errors[field] ??= []).push(message)
  const required = (field: string, value: string, message = 'Vyplňte toto pole.') => {
    if (value.trim() === '') add(field, message)
  }

  if (v.type === 'Person') {
    required('firstName', v.firstName)
    required('lastName', v.lastName)
  } else {
    required('companyName', v.companyName)
    required('ico', v.ico)
  }
  if (v.firstName.length > 100) add('firstName', 'Maximálna dĺžka je 100 znakov.')
  if (v.lastName.length > 100) add('lastName', 'Maximálna dĺžka je 100 znakov.')
  if (v.companyName.length > 200) add('companyName', 'Maximálna dĺžka je 200 znakov.')

  if (v.type === 'Company') {
    if (v.ico.trim() !== '' && !isValidIco(strip(v.ico))) add('ico', 'IČO musí mať 8 číslic so správnou kontrolnou číslicou.')
    if (v.dic.trim() !== '' && !/^\d{10}$/.test(strip(v.dic))) add('dic', 'DIČ musí mať 10 číslic.')
    if (v.icDph.trim() !== '' && !/^SK\d{10}$/.test(strip(v.icDph))) add('icDph', 'IČ DPH musí mať tvar SK a 10 číslic.')
  }

  const email = v.email.trim()
  if (email === '') add('email', 'Vyplňte e-mail.')
  else if (email.length > 254 || !/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) add('email', 'E-mail nemá platný tvar.')

  if (v.phone.trim() !== '' && !/^\+?[0-9 ]{6,19}$/.test(v.phone.trim())) {
    add('phone', 'Telefón môže obsahovať len číslice, medzery a úvodné +.')
  }

  required('address.street', v.street)
  required('address.city', v.city)
  required('address.postalCode', v.postalCode)
  const country = v.country.trim().toUpperCase() || 'SK'
  if (v.postalCode.trim() !== '' && country === 'SK' && !/^\d{3} ?\d{2}$/.test(v.postalCode.trim())) {
    add('address.postalCode', 'PSČ musí mať 5 číslic, napr. 811 01.')
  }
  if (!/^[A-Za-z]{2}$/.test(v.country.trim() || 'SK')) add('address.country', 'Krajina musí byť 2-písmenový kód, napr. SK.')
  if (v.note.length > 2000) add('note', 'Maximálna dĺžka je 2000 znakov.')

  return errors
}
