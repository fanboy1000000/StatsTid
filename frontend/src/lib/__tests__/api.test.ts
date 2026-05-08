import { apiClient, apiFetchWithEtag } from '../api'

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
