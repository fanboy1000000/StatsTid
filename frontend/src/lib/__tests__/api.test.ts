import { apiClient } from '../api'

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
