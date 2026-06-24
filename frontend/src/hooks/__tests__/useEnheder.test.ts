// S97 / TASK-9705 — tests for the structured-Enhed FE data layer (useEnheder).
// The hook exposes closures (no React state) → exercise them directly against a
// stubbed fetch (same pattern as useEntitlementEligibility.test.ts). Asserts:
//   • fetchEnheder lists ACTIVE enheder + composes each row's If-Match from its
//     own `version` (the list GET carries no collection ETag) + carries
//     parentEnhedId / level (S100 hierarchy)
//   • createEnhed POSTs {organisationId, name[, parentEnhedId]} + surfaces a
//     status-tagged error on 409 (dup) / 400 (MAO org) / 422 (dead parent)
//   • renameEnhed PUTs {name} with the supplied If-Match + 409 (dup) / 412 (stale)
//   • moveEnhed PUTs {newParentEnhedId|null} to …/move with the If-Match + 422 cycle
//   • deleteEnhed DELETEs with the supplied If-Match
//   • setUserEnheder PUTs {enhedIds} to /users/{id}/enheder (no If-Match)
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useEnheder } from '../useEnheder'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

function hook() {
  return renderHook(() => useEnheder()).result.current
}

function ok(json: unknown, etag?: string) {
  return {
    ok: true,
    status: 200,
    headers: new Headers(etag ? { ETag: etag } : {}),
    json: async () => json,
    text: async () => JSON.stringify(json),
  }
}

function err(status: number, body: unknown) {
  return {
    ok: false,
    status,
    headers: new Headers(),
    json: async () => body,
    text: async () => JSON.stringify(body),
  }
}

beforeEach(() => {
  mockFetch.mockReset()
})

describe('useEnheder — fetchEnheder (list)', () => {
  it('lists ACTIVE enheder + composes each row If-Match + carries parentEnhedId/level', async () => {
    // The backend serves an OBJECT envelope `{ enheder: [...] }`, not a bare array.
    // S100: the flat rows now carry parentEnhedId (null = root) + the derived level.
    mockFetch.mockResolvedValue(
      ok({
        enheder: [
          { enhedId: 'E1', organisationId: 'STY1', name: 'Netværk', version: 3, parentEnhedId: null, level: 1 },
          { enhedId: 'E2', organisationId: 'STY1', name: 'Drift', version: 1, parentEnhedId: 'E1', level: 2 },
        ],
      }),
    )
    const result = await hook().fetchEnheder('STY1')
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.data).toEqual([
      { enhedId: 'E1', organisationId: 'STY1', name: 'Netværk', version: 3, parentEnhedId: null, level: 1, etag: '"3"' },
      { enhedId: 'E2', organisationId: 'STY1', name: 'Drift', version: 1, parentEnhedId: 'E1', level: 2, etag: '"1"' },
    ])
    // org id is query-encoded on the list GET.
    expect(mockFetch.mock.calls[0][0]).toBe('/api/admin/enheder?organisationId=STY1')
  })

  it('defaults a missing parentEnhedId to null (back-compat / greenfield root)', async () => {
    mockFetch.mockResolvedValue(
      ok({ enheder: [{ enhedId: 'E1', organisationId: 'STY1', name: 'Netværk', version: 3 }] }),
    )
    const result = await hook().fetchEnheder('STY1')
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.data[0].parentEnhedId).toBeNull()
  })

  it('returns the raw non-ok ApiResult on a 403 (out-of-scope)', async () => {
    mockFetch.mockResolvedValue(err(403, { error: 'forbidden' }))
    const result = await hook().fetchEnheder('STY9')
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.status).toBe(403)
  })
})

describe('useEnheder — createEnhed', () => {
  it('POSTs {organisationId, name} (no parentEnhedId → root) and returns the created Enhed', async () => {
    mockFetch.mockResolvedValue(
      ok({ enhedId: 'E9', organisationId: 'STY1', name: 'Sikkerhed', version: 1, parentEnhedId: null }, '"1"'),
    )
    const created = await hook().createEnhed('STY1', 'Sikkerhed')
    expect(created).toEqual({
      enhedId: 'E9', organisationId: 'STY1', name: 'Sikkerhed', version: 1, parentEnhedId: null, level: undefined, etag: '"1"',
    })
    const [url, init] = mockFetch.mock.calls[0]
    expect(url).toBe('/api/admin/enheder')
    expect(init.method).toBe('POST')
    // A root create omits parentEnhedId entirely from the wire body.
    expect(JSON.parse(init.body)).toEqual({ organisationId: 'STY1', name: 'Sikkerhed' })
  })

  it('POSTs {organisationId, name, parentEnhedId} when a parent is given (child create)', async () => {
    mockFetch.mockResolvedValue(
      ok({ enhedId: 'E9', organisationId: 'STY1', name: 'Sikkerhed', version: 1, parentEnhedId: 'E1', level: 2 }, '"1"'),
    )
    const created = await hook().createEnhed('STY1', 'Sikkerhed', 'E1')
    expect(created.parentEnhedId).toBe('E1')
    const [, init] = mockFetch.mock.calls[0]
    expect(JSON.parse(init.body)).toEqual({ organisationId: 'STY1', name: 'Sikkerhed', parentEnhedId: 'E1' })
  })

  it('throws a status-tagged error on a 409 (active-name dup)', async () => {
    mockFetch.mockResolvedValue(err(409, { error: 'dup' }))
    await expect(hook().createEnhed('STY1', 'Netværk')).rejects.toMatchObject({ status: 409 })
  })

  it('throws a status-tagged error on a 400 (org is a MAO)', async () => {
    mockFetch.mockResolvedValue(err(400, { error: 'enhed must belong to an ORGANISATION' }))
    await expect(hook().createEnhed('MIN1', 'Netværk')).rejects.toMatchObject({ status: 400 })
  })

  it('throws a status-tagged error on a 422 (cross-org / dead parent)', async () => {
    mockFetch.mockResolvedValue(err(422, { error: 'dead parent' }))
    await expect(hook().createEnhed('STY1', 'Netværk', 'DEAD')).rejects.toMatchObject({ status: 422 })
  })
})

describe('useEnheder — moveEnhed', () => {
  it('PUTs {newParentEnhedId} to …/move with the supplied If-Match', async () => {
    mockFetch.mockResolvedValue(
      ok({ enhedId: 'E2', organisationId: 'STY1', name: 'Drift', version: 5, parentEnhedId: 'E3' }, '"5"'),
    )
    const moved = await hook().moveEnhed('E2', 'E3', '"4"')
    expect(moved.parentEnhedId).toBe('E3')
    expect(moved.etag).toBe('"5"')
    const [url, init] = mockFetch.mock.calls[0]
    expect(url).toBe('/api/admin/enheder/E2/move')
    expect(init.method).toBe('PUT')
    expect((init.headers as Record<string, string>)['If-Match']).toBe('"4"')
    expect(JSON.parse(init.body)).toEqual({ newParentEnhedId: 'E3' })
  })

  it('PUTs {newParentEnhedId: null} to make the enhed a root', async () => {
    mockFetch.mockResolvedValue(
      ok({ enhedId: 'E2', organisationId: 'STY1', name: 'Drift', version: 5, parentEnhedId: null }, '"5"'),
    )
    await hook().moveEnhed('E2', null, '"4"')
    const [, init] = mockFetch.mock.calls[0]
    expect(JSON.parse(init.body)).toEqual({ newParentEnhedId: null })
  })

  it('throws a status-tagged error on a 422 (cycle: self/descendant)', async () => {
    mockFetch.mockResolvedValue(err(422, { error: 'cycle' }))
    await expect(hook().moveEnhed('E1', 'E2', '"3"')).rejects.toMatchObject({ status: 422 })
  })

  it('throws on a 412 (stale version)', async () => {
    mockFetch.mockResolvedValue(err(412, { error: 'stale' }))
    await expect(hook().moveEnhed('E1', 'E2', '"1"')).rejects.toMatchObject({ status: 412 })
  })
})

describe('useEnheder — renameEnhed', () => {
  it('PUTs {name} with the supplied If-Match', async () => {
    mockFetch.mockResolvedValue(
      ok({ enhedId: 'E1', organisationId: 'STY1', name: 'Netværk II', version: 4 }, '"4"'),
    )
    const renamed = await hook().renameEnhed('E1', 'Netværk II', '"3"')
    expect(renamed.name).toBe('Netværk II')
    expect(renamed.etag).toBe('"4"')
    const [url, init] = mockFetch.mock.calls[0]
    expect(url).toBe('/api/admin/enheder/E1')
    expect(init.method).toBe('PUT')
    expect((init.headers as Record<string, string>)['If-Match']).toBe('"3"')
    expect(JSON.parse(init.body)).toEqual({ name: 'Netværk II' })
  })

  it('throws on a 412 (stale version)', async () => {
    mockFetch.mockResolvedValue(err(412, { error: 'stale' }))
    await expect(hook().renameEnhed('E1', 'X', '"1"')).rejects.toMatchObject({ status: 412 })
  })

  it('throws on a 409 (rename collides with an active name)', async () => {
    mockFetch.mockResolvedValue(err(409, { error: 'dup' }))
    await expect(hook().renameEnhed('E1', 'Drift', '"3"')).rejects.toMatchObject({ status: 409 })
  })
})

describe('useEnheder — deleteEnhed', () => {
  it('DELETEs with the supplied If-Match (soft-delete)', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, headers: new Headers(), text: async () => '' })
    await expect(hook().deleteEnhed('E1', '"3"')).resolves.toBeUndefined()
    const [url, init] = mockFetch.mock.calls[0]
    expect(url).toBe('/api/admin/enheder/E1')
    expect(init.method).toBe('DELETE')
    expect((init.headers as Record<string, string>)['If-Match']).toBe('"3"')
  })

  it('throws on a 412 (stale)', async () => {
    mockFetch.mockResolvedValue(err(412, { error: 'stale' }))
    await expect(hook().deleteEnhed('E1', '"1"')).rejects.toMatchObject({ status: 412 })
  })
})

describe('useEnheder — setUserEnheder', () => {
  it('PUTs {enhedIds} to /users/{id}/enheder with NO If-Match', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, headers: new Headers(), json: async () => undefined, text: async () => '' })
    await expect(hook().setUserEnheder('EMP001', ['E1', 'E2'])).resolves.toBeUndefined()
    const [url, init] = mockFetch.mock.calls[0]
    expect(url).toBe('/api/admin/users/EMP001/enheder')
    expect(init.method).toBe('PUT')
    expect(JSON.parse(init.body)).toEqual({ enhedIds: ['E1', 'E2'] })
    // No If-Match — the backend FOR-UPDATE-locks the user row (latest-wins).
    const headers = init.headers as Record<string, string>
    expect(headers['If-Match']).toBeUndefined()
  })

  it('throws a status-tagged error on a 400 (dead/foreign enhed in the set)', async () => {
    mockFetch.mockResolvedValue(err(400, { error: 'enhed not in user org' }))
    await expect(hook().setUserEnheder('EMP001', ['DEAD'])).rejects.toMatchObject({ status: 400 })
  })
})
