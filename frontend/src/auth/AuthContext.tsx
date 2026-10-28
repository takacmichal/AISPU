/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import { loadSession, login as loginRequest, saveSession, type Session } from './session'

interface AuthState {
  session: Session | null
  roles: readonly string[]
  signIn: (email: string, password: string) => Promise<void>
  signOut: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children, initialSession }: { children: ReactNode; initialSession?: Session | null }) {
  const [session, setSession] = useState<Session | null>(() => (initialSession !== undefined ? initialSession : loadSession()))

  const signIn = useCallback(async (email: string, password: string) => {
    const next = await loginRequest(email, password)
    saveSession(next)
    setSession(next)
  }, [])

  const signOut = useCallback(() => {
    saveSession(null)
    setSession(null)
  }, [])

  const value = useMemo<AuthState>(
    () => ({ session, roles: session?.roles ?? [], signIn, signOut }),
    [session, signIn, signOut],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
