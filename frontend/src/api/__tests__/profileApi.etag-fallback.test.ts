import { getCurrentProfile, saveProfile } from '../profileApi'

// Mock fetch globally — same shape as lib/__tests__/api.test.ts.
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => {
    mockStorage[k] = v
  },
  removeItem: (k: string) => {
    delete mockStorage[k]
  },
})

beforeEach(() => {
  mockFetch.mockReset()
  Object.keys(mockStorage).forEach(k => delete mockStorage[k])
})

// Helper: build a Headers-like object that mimics cross-origin
// "ETag not exposed" behaviour by returning null even though the server sent it.
function headersWithoutEtag() {
  return new Headers()
}

function headersWithEtag(value: string) {
  const h = new Headers()
  h.set('ETag', value)
  return h
}

const profile = {
  profileId: '11111111-2222-3333-4444-555555555555',
  orgId: 'STY02',
  agreementCode: 'HK',
  okVersion: 'OK24',
  effectiveFrom: '2026-05-04',
  effectiveTo: null,
  weeklyNormHours: 37,
  maxFlexBalance: null,
  flexCarryoverMax: null,
  maxOvertimeHoursPerPeriod: null,
  overtimeRequiresPreApproval: null,
  createdBy: 'admin1',
  createdAt: '2026-05-04T12:00:00Z',
  version: 5,
}

// ─── getCurrentProfile ───────────────────────────────────────────────────

describe('getCurrentProfile ETag fallback', () => {
  it('uses the ETag header when present', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => profile,
      headers: headersWithEtag('"5"'),
    })

    const result = await getCurrentProfile('STY02', 'HK', 'OK24')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.etag).toBe('"5"')
    expect(result.version).toBe(5)
  })

  it('falls back to body.version when ETag header is missing', async () => {
    // Cross-origin case: server sent ETag but it was filtered by the browser
    // because Access-Control-Expose-Headers did not include it.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => profile,
      headers: headersWithoutEtag(),
    })

    const result = await getCurrentProfile('STY02', 'HK', 'OK24')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.etag).toBe('"5"')
    expect(result.version).toBe(5)
  })

  it('returns null etag when header AND body.version both invalid', async () => {
    // Defensive shape: malformed body without a usable version.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ ...profile, version: 'not-a-number' as unknown }),
      headers: headersWithoutEtag(),
    })

    const result = await getCurrentProfile('STY02', 'HK', 'OK24')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    // Critical: no `"undefined"` token synthesized.
    expect(result.etag).toBeNull()
    expect(result.version).toBeNull()
  })

  it('returns null etag when body.version is below 1', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ ...profile, version: 0 }),
      headers: headersWithoutEtag(),
    })

    const result = await getCurrentProfile('STY02', 'HK', 'OK24')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.etag).toBeNull()
    expect(result.version).toBeNull()
  })

  it('returns 404 path as profile-null without invoking fallback', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 404,
      json: async () => ({}),
      headers: headersWithoutEtag(),
      text: async () => '',
    })

    const result = await getCurrentProfile('STY02', 'HK', 'OK24')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.profile).toBeNull()
    expect(result.etag).toBeNull()
    expect(result.version).toBeNull()
  })
})

// ─── saveProfile ─────────────────────────────────────────────────────────

const saveBody = {
  effectiveFrom: '2026-05-04',
  weeklyNormHours: 37,
  maxFlexBalance: null,
  flexCarryoverMax: null,
  maxOvertimeHoursPerPeriod: null,
  overtimeRequiresPreApproval: null,
}

describe('saveProfile ETag fallback', () => {
  it('uses the ETag header on the PUT response when present', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ ...profile, version: 6 }),
      headers: headersWithEtag('"6"'),
      text: async () => '',
    })

    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, '"5"')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.newEtag).toBe('"6"')
    expect(result.newVersion).toBe(6)
  })

  it('falls back to response body.version when ETag header is missing', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ ...profile, version: 6 }),
      headers: headersWithoutEtag(),
      text: async () => '',
    })

    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, '"5"')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.newEtag).toBe('"6"')
    expect(result.newVersion).toBe(6)
  })

  it('returns null new* when both header and body.version are invalid', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ ...profile, version: undefined as unknown }),
      headers: headersWithoutEtag(),
      text: async () => '',
    })

    const result = await saveProfile('STY02', 'HK', 'OK24', saveBody, '"5"')

    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.newEtag).toBeNull()
    expect(result.newVersion).toBeNull()
  })
})
