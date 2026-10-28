/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { sessionEvents } from './http'
import { loadSession, login as loginRequest, logoutRequest, saveSession, type Session } from './session'

export type SignOutReason = 'logout' | 'expired' | null

interface AuthState {
  session: Session | null
  roles: readonly string[]
  /** Why the user is signed out; drives the login message and returnUrl behaviour. */
  signOutReason: SignOutReason
  signIn: (email: string, password: string) => Promise<void>
  signOut: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children, initialSession }: { children: ReactNode; initialSession?: Session | null }) {
  const [session, setSession] = useState<Session | null>(() => (initialSession !== undefined ? initialSession : loadSession()))
  const [signOutReason, setSignOutReason] = useState<SignOutReason>(null)

  useEffect(() => {
    const onRefreshed = (event: Event) => setSession((event as CustomEvent<Session>).detail)
    const onExpired = () => {
      setSession(null)
      setSignOutReason('expired')
    }
    sessionEvents.addEventListener('refreshed', onRefreshed)
    sessionEvents.addEventListener('expired', onExpired)
    return () => {
      sessionEvents.removeEventListener('refreshed', onRefreshed)
      sessionEvents.removeEventListener('expired', onExpired)
    }
  }, [])

  const signIn = useCallback(async (email: string, password: string) => {
    const next = await loginRequest(email, password)
    saveSession(next)
    setSignOutReason(null)
    setSession(next)
  }, [])

  const signOut = useCallback(() => {
    const current = loadSession()
    saveSession(null)
    setSignOutReason('logout')
    setSession(null)
    if (current) void logoutRequest(current.refreshToken)
  }, [])

  const value = useMemo<AuthState>(
    () => ({ session, roles: session?.roles ?? [], signOutReason, signIn, signOut }),
    [session, signOutReason, signIn, signOut],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
