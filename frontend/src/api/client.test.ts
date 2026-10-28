import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveSession } from '../auth/session'
import { api } from './client'

describe('typed API client', () => {
  beforeEach(() => localStorage.clear())
  afterEach(() => vi.unstubAllGlobals())

  it('calls typed endpoints with query params and the Bearer token', async () => {
    saveSession({ email: 'a@test.sk', roles: ['Admin'], accessToken: 'tok', refreshToken: 'r' })
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ items: [], page: 2, pageSize: 5, totalCount: 0 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const { data } = await api.GET('/api/customers', { params: { query: { q: 'novak', page: 2, pageSize: 5 } } })

    expect(data?.page).toBe(2)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toContain('/api/customers?q=novak&page=2&pageSize=5')
    expect((init.headers as Headers).get('Authorization')).toBe('Bearer tok')
  })

  it('exposes error responses without throwing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Nope' }), { status: 404, headers: { 'Content-Type': 'application/json' } })))
    const { data, response } = await api.GET('/api/customers/{id}', { params: { path: { id: '00000000-0000-0000-0000-000000000000' } } })
    expect(data).toBeUndefined()
    expect(response.status).toBe(404)
  })
})
