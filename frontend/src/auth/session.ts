import { apiUrl } from '../api/config'

export interface Session {
  email: string
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
  if (!response.ok) throw new Error(`Prihlásenie zlyhalo (HTTP ${response.status}).`)
  return sessionFromTokens((await response.json()) as TokenResponse)
}
