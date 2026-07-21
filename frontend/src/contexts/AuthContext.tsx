import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react'
import type { AuthUser } from '../types'
import type { components } from '../lib/api-types'
import { decodeJwt, parseScopes, isTokenExpired, type RoleScope } from '../lib/jwt'

// S118 / TASK-11801 (PAT-012) — the login op is typed from the GENERATED spec.
// The hand-written `LoginResponse` in types.ts was DELETED: it OMITTED the
// `orgId: string | null` member the backend serves (a field-omission lie).
// The call DELIBERATELY stays on raw `fetch` rather than the typed
// `apiClient.post` form: `apiClient`'s shared 401 handler clears the token and
// RELOADS the page — on a wrong-password 401 that would destroy the login
// form's error display (a behavior change). The request/response BINDINGS are
// spec-derived below, so a shape drift is still a `tsc` error.
type LoginRequest = components['schemas']['StatsTid.Backend.Api.Contracts.LoginRequest']
type LoginResponse = components['schemas']['StatsTid.Backend.Api.Contracts.LoginResponse']

const TOKEN_KEY = 'statstid_token'
const USER_KEY = 'statstid_user'

export interface AuthState {
  token: string | null
  user: AuthUser | null
  role: string | null
  orgId: string | null
  agreementCode: string | null
  scopes: RoleScope[]
  isAuthenticated: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function getStoredUser(): AuthUser | null {
  const raw = localStorage.getItem(USER_KEY)
  if (!raw) return null
  try { return JSON.parse(raw) as AuthUser } catch { return null }
}

interface AuthProviderProps {
  children: ReactNode
}

export function AuthProvider({ children }: AuthProviderProps) {
  const [token, setToken] = useState<string | null>(getStoredToken)
  const [user, setUser] = useState<AuthUser | null>(getStoredUser)
  const [role, setRole] = useState<string | null>(null)
  const [orgId, setOrgId] = useState<string | null>(null)
  const [agreementCode, setAgreementCode] = useState<string | null>(null)
  const [scopes, setScopes] = useState<RoleScope[]>([])

  // On mount, decode existing token if present
  useEffect(() => {
    if (token) {
      try {
        const payload = decodeJwt(token)
        if (isTokenExpired(payload)) {
          // Token expired, clear everything
          localStorage.removeItem(TOKEN_KEY)
          localStorage.removeItem(USER_KEY)
          setToken(null)
          setUser(null)
          setRole(null)
          setOrgId(null)
          setAgreementCode(null)
          setScopes([])
          return
        }
        setRole(payload.role)
        setOrgId(payload.org_id)
        setAgreementCode(payload.agreement_code)
        setScopes(parseScopes(payload.scopes))
        // Ensure user is set from token if not in localStorage
        if (!user) {
          const authUser: AuthUser = { employeeId: payload.sub, role: payload.role }
          setUser(authUser)
        }
      } catch {
        // Invalid token, clear everything
        localStorage.removeItem(TOKEN_KEY)
        localStorage.removeItem(USER_KEY)
        setToken(null)
        setUser(null)
        setRole(null)
        setOrgId(null)
        setAgreementCode(null)
        setScopes([])
      }
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const login = useCallback(async (username: string, password: string) => {
    const body: LoginRequest = { username, password }
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (!res.ok) {
      throw new Error(res.status === 401 ? 'Invalid credentials' : `Login failed: HTTP ${res.status}`)
    }
    const data: LoginResponse = await res.json()

    // Store token and user
    localStorage.setItem(TOKEN_KEY, data.token)
    const authUser: AuthUser = { employeeId: data.employeeId, role: data.role }
    localStorage.setItem(USER_KEY, JSON.stringify(authUser))

    // Decode JWT to extract scopes
    const payload = decodeJwt(data.token)

    setToken(data.token)
    setUser(authUser)
    setRole(payload.role)
    setOrgId(payload.org_id)
    setAgreementCode(payload.agreement_code)
    setScopes(parseScopes(payload.scopes))
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    setToken(null)
    setUser(null)
    setRole(null)
    setOrgId(null)
    setAgreementCode(null)
    setScopes([])
  }, [])

  const value: AuthState = {
    token,
    user,
    role,
    orgId,
    agreementCode,
    scopes,
    isAuthenticated: !!token,
    login,
    logout,
  }

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
