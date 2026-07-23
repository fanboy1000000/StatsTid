import {
  apiClient,
  apiFetchWithEtag,
  putSkemaRowPreferences,
  toRowPreferencesPutBody,
} from '../api'
import type { SkemaRowPreferences } from '../../types'

// Mock fetch globally
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// Mock localStorage
const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

// Prevent actual reload
const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
})

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach(k => delete mockStorage[k])
})

describe('apiClient.get', () => {
  it('returns data on success', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ value: 42 }),
    })
    const result = await apiClient.get<{ value: number }>('/api/test')
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data.value).toBe(42)
  })

  it('includes auth header when token exists', async () => {
    mockStorage['statstid_token'] = 'mytoken'
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({}),
    })
    await apiClient.get('/api/test')
    expect(mockFetch).toHaveBeenCalledWith('/api/test', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer mytoken' }),
    }))
  })

  it('handles 401 by clearing storage', async () => {
    mockStorage['statstid_token'] = 'expired'
    mockStorage['statstid_user'] = 'someone'
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      text: async () => 'Unauthorized',
    })
    const result = await apiClient.get('/api/test')
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.status).toBe(401)
    expect(mockStorage['statstid_token']).toBeUndefined()
    expect(mockStorage['statstid_user']).toBeUndefined()
  })

  it('handles 403 without clearing storage', async () => {
    mockStorage['statstid_token'] = 'valid'
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 403,
      text: async () => 'Forbidden',
    })
    const result = await apiClient.get('/api/test')
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.status).toBe(403)
    expect(mockStorage['statstid_token']).toBe('valid')
  })

  it('handles 204 No Content', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 204,
    })
    const result = await apiClient.get('/api/test')
    expect(result.ok).toBe(true)
  })

  it('handles network errors', async () => {
    mockFetch.mockRejectedValueOnce(new Error('Network failure'))
    const result = await apiClient.get('/api/test')
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(0)
      expect(result.error).toContain('Network failure')
    }
  })
})

describe('apiClient.post', () => {
  it('sends body as JSON', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ id: 1 }),
    })
    await apiClient.post('/api/test', { name: 'test' })
    expect(mockFetch).toHaveBeenCalledWith('/api/test', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ name: 'test' }),
    }))
  })
})

// S25 / TASK-2506 (ADR-019 pending): header-aware fetch variant.
describe('apiFetchWithEtag', () => {
  it('returns data + ETag header on 200', async () => {
    const headers = new Headers({ ETag: '"5"' })
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers,
      json: async () => ({ id: 'cfg-1', version: 5 }),
    })
    const result = await apiFetchWithEtag<{ id: string; version: number }>('/api/test')
    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.data.data.id).toBe('cfg-1')
      expect(result.data.data.version).toBe(5)
      expect(result.data.etag).toBe('"5"')
      expect(result.data.status).toBe(200)
    }
  })

  it('returns null etag on 204 No Content', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 204,
      headers: new Headers(),
    })
    const result = await apiFetchWithEtag('/api/test', { method: 'DELETE' })
    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.data.etag).toBeNull()
      expect(result.data.status).toBe(204)
      expect(result.data.data).toBeUndefined()
    }
  })

  it('parses 412 body for stale-state details', async () => {
    const stalePayload = {
      error: 'Concurrency precondition failed',
      expectedVersion: 3,
      actualVersion: 5,
      currentState: { id: 'cfg-1', version: 5 },
    }
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify(stalePayload),
    })
    const result = await apiFetchWithEtag('/api/test', {
      method: 'PUT',
      headers: { 'If-Match': '"3"' },
      body: JSON.stringify({}),
    })
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(412)
      expect(result.body).toEqual(stalePayload)
    }
  })

  it('surfaces 428 missing-If-Match hint', async () => {
    const payload = { error: "If-Match header required (e.g. 'If-Match: \"<version>\"')" }
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 428,
      headers: new Headers(),
      text: async () => JSON.stringify(payload),
    })
    const result = await apiFetchWithEtag('/api/test', { method: 'PUT' })
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(428)
      expect(result.body).toEqual(payload)
    }
  })

  it('handles 401 by clearing storage', async () => {
    mockStorage['statstid_token'] = 'expired'
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 401,
      headers: new Headers(),
      text: async () => 'Unauthorized',
    })
    const result = await apiFetchWithEtag('/api/test')
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(401)
    }
    expect(mockStorage['statstid_token']).toBeUndefined()
  })

  it('returns status 0 on network error', async () => {
    mockFetch.mockRejectedValueOnce(new Error('Network unreachable'))
    const result = await apiFetchWithEtag('/api/test')
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(0)
      expect(result.error).toContain('Network unreachable')
    }
  })

  it('threads caller-supplied If-Match header onto the request', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"6"' }),
      json: async () => ({ id: 'cfg-1', version: 6 }),
    })
    await apiFetchWithEtag('/api/test', {
      method: 'PUT',
      headers: { 'If-Match': '"5"' },
      body: JSON.stringify({ a: 1 }),
    })
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/test',
      expect.objectContaining({
        method: 'PUT',
        headers: expect.objectContaining({ 'If-Match': '"5"' }),
      }),
    )
  })

  it('includes auth header when token exists', async () => {
    mockStorage['statstid_token'] = 'mytoken'
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({}),
    })
    await apiFetchWithEtag('/api/test')
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/test',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer mytoken' }),
      }),
    )
  })
})

// S72 / TASK-7204 — typed row-preferences PUT wrapper (SPRINT-72 R4/R13/R16).
describe('putSkemaRowPreferences', () => {
  const BODY = {
    projects: [
      { projectId: 'p-sag', sortOrder: 0 },
      { projectId: 'p-borger', sortOrder: 1 },
    ],
    absenceTypes: [{ absenceType: 'VACATION', sortOrder: 0 }],
  }

  // S120 mock re-anchoring: `fullDayOnly` is REQUIRED on the spec
  // row-preference absence rows — the fixture carries it explicitly.
  const EFFECTIVE: SkemaRowPreferences = {
    configured: true,
    projects: [
      { projectId: 'p-sag', projectCode: 'ØS-1042', projectName: 'Sagsbehandling', sortOrder: 0 },
      { projectId: 'p-borger', projectCode: 'DIG-2207', projectName: 'Borger.dk', sortOrder: 1 },
    ],
    absenceTypes: [{ type: 'VACATION', label: 'Ferie', fullDayOnly: false, sortOrder: 0 }],
  }

  it('PUTs the exact body to /api/skema/{employeeId}/row-preferences with the auth header', async () => {
    mockStorage['statstid_token'] = 'mytoken'
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => EFFECTIVE,
    })
    await putSkemaRowPreferences('emp-1', BODY)
    expect(mockFetch).toHaveBeenCalledWith(
      '/api/skema/emp-1/row-preferences',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify(BODY),
        headers: expect.objectContaining({
          Authorization: 'Bearer mytoken',
          'Content-Type': 'application/json',
        }),
      }),
    )
  })

  it('returns the new EFFECTIVE rowPreferences shape on 200 (the refetch-free apply for the modal)', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => EFFECTIVE,
    })
    const result = await putSkemaRowPreferences('emp-1', BODY)
    expect(result.ok).toBe(true)
    if (result.ok) expect(result.data).toEqual(EFFECTIVE)
  })

  it('preserves the 422 row_preferences_invalid payload TYPED via `invalid` (offender rendering)', async () => {
    const payload = {
      error: 'row_preferences_invalid',
      invalidProjectIds: ['p-ukendt'],
      invalidAbsenceTypes: ['UNKNOWN_TYPE'],
      duplicateProjectIds: ['p-sag'],
      duplicateAbsenceTypes: ['VACATION'],
      message: 'Row preferences validation failed.',
    }
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 422,
      text: async () => JSON.stringify(payload),
    })
    const result = await putSkemaRowPreferences('emp-1', BODY)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(422)
      expect(result.invalid).toEqual(payload)
    }
  })

  it('a non-JSON 422 body yields no `invalid` but keeps the raw error text', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 422,
      text: async () => 'not json',
    })
    const result = await putSkemaRowPreferences('emp-1', BODY)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(422)
      expect(result.invalid).toBeUndefined()
      expect(result.error).toBe('not json')
    }
  })

  it('a 422-status body of a DIFFERENT error shape is not surfaced as `invalid`', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 422,
      text: async () => JSON.stringify({ error: 'something_else', message: 'nope' }),
    })
    const result = await putSkemaRowPreferences('emp-1', BODY)
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.invalid).toBeUndefined()
  })

  it('a 403 self-only refusal returns status 403 without `invalid`', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 403,
      text: async () => 'Forbidden',
    })
    const result = await putSkemaRowPreferences('emp-2', BODY)
    expect(result.ok).toBe(false)
    if (!result.ok) {
      expect(result.status).toBe(403)
      expect(result.invalid).toBeUndefined()
    }
  })
})

// S72 / TASK-7204 — the dense-renumbering owner for the PUT body derivation.
describe('toRowPreferencesPutBody', () => {
  it('derives sortOrder from the submitted ORDER, dense 0..n-1, ignoring stale sparse input values', () => {
    const body = toRowPreferencesPutBody(
      [
        { projectId: 'p-b', projectCode: 'B', projectName: 'Beta', sortOrder: 7 },
        { projectId: 'p-a', projectCode: 'A', projectName: 'Alfa', sortOrder: 2 },
        { projectId: 'p-c', projectCode: 'C', projectName: 'Gamma', sortOrder: 11 },
      ],
      [],
    )
    expect(body.projects).toEqual([
      { projectId: 'p-b', sortOrder: 0 },
      { projectId: 'p-a', sortOrder: 1 },
      { projectId: 'p-c', sortOrder: 2 },
    ])
  })

  it('maps the absence `type` field to the wire field `absenceType`', () => {
    const body = toRowPreferencesPutBody(
      [],
      [
        { type: 'CARE_DAY', label: 'Omsorgsdag', fullDayOnly: false, sortOrder: 4 },
        { type: 'VACATION', label: 'Ferie', fullDayOnly: false, sortOrder: 0 },
      ],
    )
    expect(body.absenceTypes).toEqual([
      { absenceType: 'CARE_DAY', sortOrder: 0 },
      { absenceType: 'VACATION', sortOrder: 1 },
    ])
  })

  it('empty selections produce empty arrays (R4: configured-empty is a legal full replacement)', () => {
    expect(toRowPreferencesPutBody([], [])).toEqual({ projects: [], absenceTypes: [] })
  })
})
