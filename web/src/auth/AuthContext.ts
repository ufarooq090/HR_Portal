import { createContext, useContext } from 'react'
import type { AuthMode } from '../config'

export interface AuthContextValue {
  mode: AuthMode
  signOut: () => void
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>')
  return value
}
