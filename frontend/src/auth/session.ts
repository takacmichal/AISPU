import { MESSAGES } from '../lib/messages'
import { apiUrl } from '../api/config'
import { HOME_PATH } from './navigation'

export interface Session {
  email: string
  userId?: string
  roles: string[]
  accessToken: string
  refreshToken: string
}

const STORAGE_KEY = 'solarops.session'

interface TokenResponse {
  accessToken: string
  expiresIn: number
  refreshToken: string
}

function decodePayload(token: string): Record<string, unknown> {
  const payload = token.split('.')[1] ?? ''
  const base64 = payload.replace(/-/g, '+').replace(/_/g, '/')
  const json = decodeURIComponent(
    atob(base64)
      .split('')
      .map((c) => '%' + c.charCodeAt(0).toString(16).padStart(2, '0'))
      .join(''),
  )
  return JSON.parse(json) as Record<string, unknown>
}

export function sessionFromTokens(tokens: { accessToken: string; refreshToken: string }): Session {
  const claims = decodePayload(tokens.accessToken)
  const role = claims.role
  const roles = Array.isArray(role) ? (role as string[]) : typeof role === 'string' ? [role] : []
  return {
    email: typeof claims.email === 'string' ? claims.email : '',
    userId: typeof claims.sub === 'string' ? claims.sub : undefined,
    roles,
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
  }
}

export function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as Session) : null
  } catch {
    return null
  }
}

export function saveSession(session: Session | null): void {
  if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
  else localStorage.removeItem(STORAGE_KEY)
}

export async function login(email: string, password: string): Promise<Session> {
  const response = await fetch(`${apiUrl}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password }),
  })
  if (response.status === 401) throw new Error('Nesprávny e-mail alebo heslo.')
  if (response.status === 429) throw new Error(MESSAGES.errors.tooManyRequests)
  if (!response.ok) throw new Error(`Prihlásenie zlyhalo (HTTP ${response.status}).`)
  return sessionFromTokens((await response.json()) as TokenResponse)
}

/** The server refused the refresh for now (429); the refresh token is still valid, so the session stays. */
export const REFRESH_LIMITED = 'limited'

export async function refreshTokens(refreshToken: string): Promise<Session | null | typeof REFRESH_LIMITED> {
  try {
    const response = await fetch(`${apiUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
    if (response.status === 429) return REFRESH_LIMITED
    if (!response.ok) return null
    return sessionFromTokens((await response.json()) as TokenResponse)
  } catch {
    return null
  }
}

/** Best effort: the local session is cleared even if the server cannot be reached. */
export async function logoutRequest(refreshToken: string): Promise<void> {
  try {
    await fetch(`${apiUrl}/api/auth/logout`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
  } catch {
    // ignored on purpose
  }
}

/** Only same-origin relative paths are accepted as a post-login target; the public root falls back to the dashboard. */
export function safeReturnUrl(value: string | null | undefined): string {
  const ok = value && value !== '/' && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/login')
  return ok ? value : HOME_PATH
}
