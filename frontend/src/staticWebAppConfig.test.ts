import { describe, expect, it } from 'vitest'
import raw from '../public/staticwebapp.config.json?raw'

const config = JSON.parse(raw) as {
  navigationFallback: { rewrite: string; exclude: string[] }
  globalHeaders: Record<string, string>
}

describe('staticwebapp.config.json', () => {
  it('serves index.html for deep links, but not for missing build files', () => {
    expect(config.navigationFallback.rewrite).toBe('/index.html')
    expect(config.navigationFallback.exclude).toContain('/assets/*')
  })

  it('sets the security headers', () => {
    expect(config.globalHeaders['X-Content-Type-Options']).toBe('nosniff')
    expect(config.globalHeaders['X-Frame-Options']).toBe('DENY')
  })

  it('allows scripts only from the app and calls only to the app and the API host', () => {
    const csp = config.globalHeaders['Content-Security-Policy']
    expect(csp).toContain("script-src 'self';")
    expect(csp).not.toContain('unsafe-inline')
    expect(csp).toContain("connect-src 'self' https://*.azurecontainerapps.io;")
    expect(csp).toContain("frame-ancestors 'none'")
  })
})
