import { useState, useCallback } from 'react'
import type { LoginResponse, AuthUser } from '../types'

const API_BASE = 'http://localhost:5100'
const TOKEN_KEY = 'statstid_token'
const USER_KEY = 'statstid_user'

function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

function getStoredUser(): AuthUser | null {
  const raw = localStorage.getItem(USER_KEY)
  if (!raw) return null
  try { return JSON.parse(raw) } catch { return null }
}

export function useAuth() {
  const [token, setToken] = useState<string | null>(getStoredToken)
  const [user, setUser] = useState<AuthUser | null>(getStoredUser)

  const login = useCallback(async (username: string, password: string) => {
    const res = await fetch(`${API_BASE}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    })
    if (!res.ok) {
      throw new Error(res.status === 401 ? 'Invalid credentials' : `Login failed: HTTP ${res.status}`)
    }
    const data: LoginResponse = await res.json()
    const authUser: AuthUser = { employeeId: data.employeeId, role: data.role }
    localStorage.setItem(TOKEN_KEY, data.token)
    localStorage.setItem(USER_KEY, JSON.stringify(authUser))
    setToken(data.token)
    setUser(authUser)
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(USER_KEY)
    setToken(null)
    setUser(null)
  }, [])

  return { token, user, isAuthenticated: !!token, login, logout }
}
