import { apiUrl } from '../api/config'
import { loadSession, refreshTokens, saveSession, type Session } from './session'

/** Notifies the AuthProvider about session changes made outside of React (token refresh, expiry). */
export const sessionEvents = new EventTarget()

let pendingRefresh: Promise<Session | null> | null = null

function refreshOnce(current: Session): Promise<Session | null> {
  pendingRefresh ??= refreshTokens(current.refreshToken)
    .then((next) => {
      if (next) {
        saveSession(next)
        sessionEvents.dispatchEvent(new CustomEvent('refreshed', { detail: next }))
      } else {
        saveSession(null)
        sessionEvents.dispatchEvent(new Event('expired'))
      }
      return next
    })
    .finally(() => {
      pendingRefresh = null
    })
  return pendingRefresh
}

/** fetch with Bearer token; on 401 refreshes the token once and retries, or signs the user out. */
export async function authFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const send = (session: Session | null) => {
    const headers = new Headers(init.headers)
    if (session) headers.set('Authorization', `Bearer ${session.accessToken}`)
    return fetch(`${apiUrl}${path}`, { ...init, headers })
  }

  const session = loadSession()
  const response = await send(session)
  if (response.status !== 401 || !session) return response

  const next = await refreshOnce(session)
  return next ? send(next) : response
}
