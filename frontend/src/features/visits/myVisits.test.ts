import { describe, expect, it } from 'vitest'
import { navigationHref, phoneHref, rangeWindow, toLocalInput } from './myVisits'

describe('rangeWindow', () => {
  const now = new Date(2026, 10, 5, 14, 30)

  it('covers today, tomorrow and the next 7 days in local time', () => {
    expect(rangeWindow('today', now)).toEqual({ from: new Date(2026, 10, 5), to: new Date(2026, 10, 6) })
    expect(rangeWindow('tomorrow', now)).toEqual({ from: new Date(2026, 10, 6), to: new Date(2026, 10, 7) })
    expect(rangeWindow('week', now)).toEqual({ from: new Date(2026, 10, 5), to: new Date(2026, 10, 12) })
  })
})

describe('links', () => {
  it('builds a dialer and a route planner link', () => {
    expect(phoneHref('+421 905 123 456')).toBe('tel:+421905123456')
    expect(navigationHref({ street: 'Hlboká 12', city: 'Žilina', postalCode: '010 01', country: 'SK' })).toBe(
      'https://www.google.com/maps/dir/?api=1&destination=Hlbok%C3%A1%2012%2C%20010%2001%20%C5%BDilina',
    )
  })

  it('formats a datetime-local value', () => {
    expect(toLocalInput(new Date(2026, 10, 5, 8, 4))).toBe('2026-11-05T08:04')
  })
})
