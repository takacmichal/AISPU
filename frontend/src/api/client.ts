import createClient from 'openapi-fetch'
import { authFetch } from '../auth/http'
import { apiUrl } from './config'
import type { paths } from './generated/schema'

const base = new URL(apiUrl || window.location.origin)
const basePath = base.pathname.replace(/\/$/, '')

/** Routes typed requests through authFetch so they get the Bearer token and refresh handling. */
async function fetchWithAuth(request: Request): Promise<Response> {
  const url = new URL(request.url)
  const hasBody = request.method !== 'GET' && request.method !== 'HEAD'
  return authFetch(url.pathname.slice(basePath.length) + url.search, {
    method: request.method,
    headers: request.headers,
    body: hasBody ? await request.text() : undefined,
    signal: request.signal,
  })
}

/** Typed API client generated from openapi/openapi.json (run pnpm api:generate after API changes). */
export const api = createClient<paths>({ baseUrl: base.origin + basePath, fetch: fetchWithAuth })
