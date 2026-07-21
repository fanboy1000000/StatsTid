// S118 / TASK-11801 — WIRE-level pins for the typed login op (PAT-012).
// AuthContext.login DELIBERATELY stays on raw `fetch` (apiClient's shared 401
// handler clears the token and RELOADS the page — on a wrong-password 401 that
// would destroy the login form's error display), with the request/response
// BINDINGS spec-derived. These pins hold both halves:
//
//  • the wire: POST /api/auth/login with the exact spec `LoginRequest` body
//    ({ username, password } — nothing else) and no precondition headers;
//  • the spec-shaped response (INCLUDING `orgId`, which the deleted
//    hand-written `LoginResponse` interface omitted — the S118 lie audit)
//    flows into the auth state;
//  • the behavior-preservation pin: a 401 throws 'Invalid credentials' and
//    does NOT reload the page (the reason the call is not on apiClient.post).
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import type { ReactNode } from 'react'
import { AuthProvider, useAuth } from '../AuthContext'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
})

/** A structurally valid JWT whose payload decodes via lib/jwt.ts. */
function fakeJwt(): string {
  const payload = {
    sub: 'EMP001',
    role: 'medarbejder',
    agreement_code: 'AC',
    org_id: 'STY01',
    scopes: '[]',
    exp: Math.floor(Date.now() / 1000) + 3600,
    iat: Math.floor(Date.now() / 1000),
  }
  return `header.${btoa(JSON.stringify(payload))}.sig`
}

const wrapper = ({ children }: { children: ReactNode }) => <AuthProvider>{children}</AuthProvider>

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

describe('AuthContext.login — the typed login wire pins', () => {
  it('POST /api/auth/login with EXACTLY the spec LoginRequest body; the spec response (incl. orgId) flows into state', async () => {
    const token = fakeJwt()
    let captured: { url: string; method: string; body: unknown } | null = null
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      captured = {
        url,
        method: init?.method ?? 'GET',
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      }
      // The spec `LoginResponse` — 5 members INCLUDING orgId (the field the
      // deleted hand-written interface omitted).
      const body = {
        token,
        expiresAt: '2026-07-20T12:00:00Z',
        employeeId: 'EMP001',
        role: 'medarbejder',
        orgId: 'STY01',
      }
      return { ok: true, status: 200, headers: new Headers(), json: async () => body }
    })

    const { result } = renderHook(() => useAuth(), { wrapper })
    await act(async () => { await result.current.login('anna', 'hemmelig') })

    expect(captured!.url).toBe('/api/auth/login')
    expect(captured!.method).toBe('POST')
    expect(captured!.body).toEqual({ username: 'anna', password: 'hemmelig' })
    expect(result.current.isAuthenticated).toBe(true)
    expect(result.current.user).toEqual({ employeeId: 'EMP001', role: 'medarbejder' })
    expect(result.current.orgId).toBe('STY01')
    expect(mockStorage['statstid_token']).toBe(token)
  })

  it('401 → throws "Invalid credentials" WITHOUT reloading (the raw-fetch behavior-preservation pin)', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 401, headers: new Headers() })
    const { result } = renderHook(() => useAuth(), { wrapper })
    await act(async () => {
      await expect(result.current.login('anna', 'forkert')).rejects.toThrow('Invalid credentials')
    })
    expect(mockReload).not.toHaveBeenCalled()
    expect(result.current.isAuthenticated).toBe(false)
  })
})
