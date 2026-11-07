import { describe, expect, it } from 'vitest'
import conf from '../nginx/default.conf.template?raw'

const header = (name: string) => conf.match(new RegExp(`add_header ${name} "([^"]*)" always;`))?.[1]

describe('nginx default.conf.template', () => {
  it('serves index.html for deep links, but not for missing build files', () => {
    expect(conf).toMatch(/location \/ \{\s*try_files \$uri \/index\.html;/)
    expect(conf).toMatch(/location \/assets\/ \{\s*try_files \$uri =404;/)
  })

  it('caches hashed build files and makes the browser revalidate index.html', () => {
    expect(conf).toMatch(/location \/assets\/ \{[^}]*expires 1y;/)
    expect(conf).toMatch(/location = \/index\.html \{[^}]*expires -1;/)
  })

  it('sets the security headers on every response', () => {
    expect(header('X-Content-Type-Options')).toBe('nosniff')
    expect(header('X-Frame-Options')).toBe('DENY')
    expect(header('Referrer-Policy')).toBe('no-referrer')
  })

  it('keeps add_header out of locations, which would drop the security headers there', () => {
    const locations = conf.split(/\n\s*location /).slice(1)
    expect(locations.length).toBeGreaterThan(0)
    for (const block of locations) expect(block).not.toContain('add_header')
  })

  it('allows scripts only from the app and calls only to the app and the API origin', () => {
    const csp = header('Content-Security-Policy')
    expect(csp).toContain("script-src 'self';")
    expect(csp).not.toContain('unsafe-inline')
    expect(csp).toContain("connect-src 'self' ${API_ORIGIN};")
    expect(csp).toContain("frame-ancestors 'none'")
  })

  it('answers the Container Apps health probe', () => {
    expect(conf).toMatch(/location = \/healthz \{[^}]*return 200/)
  })
})
