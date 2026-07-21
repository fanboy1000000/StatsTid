// S119 / TASK-11901 — WIRE-level pins for the Pass-6 bucket-B typed switch
// (PAT-012): the constraints read + the project CRUD. These exercise the REAL
// typed apiClient path (stubbed fetch, no hook mock), pinning:
//
//  • the constraints read hits the exact legacy URL and the spec rows flow
//    through (13-member ConfigConstraintResponse);
//  • the project family is UNCONDITIONED — no If-Match and no If-None-Match
//    on ANY call;
//  • URLs byte-identical to the legacy template strings;
//  • the create POST key set byte-unchanged (projectCode + projectName +
//    sortOrder);
//  • THE ACCEPTED-DELTA PIN: the update PUT sends EXACTLY projectName +
//    sortOrder — the never-bound `projectCode` key is DROPPED (the backend
//    UpdateProjectRequest never had the member — the S112 accepted-delta
//    class);
//  • the delete sends NO body and accepts the declared 204.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { useProjects, type ProjectItem } from '../useProjects'
import { useConfigConstraints, type ConfigConstraint } from '../useConfig'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

type Captured = {
  url: string
  method: string
  body: unknown
  headers: Record<string, string>
}

function toHeaderRecord(headers: HeadersInit | undefined): Record<string, string> {
  const record: Record<string, string> = {}
  if (!headers) return record
  if (headers instanceof Headers) {
    headers.forEach((v, k) => { record[k] = v })
  } else if (Array.isArray(headers)) {
    for (const [k, v] of headers) record[k] = v
  } else {
    for (const [k, v] of Object.entries(headers)) record[k] = v
  }
  return record
}

function captureCalls(
  respond: (url: string, method: string) => { status?: number; body?: unknown } = () => ({}),
) {
  const calls: Captured[] = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    calls.push({
      url,
      method: init?.method ?? 'GET',
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined,
      headers: toHeaderRecord(init?.headers),
    })
    const { status = 200, body = [] } = respond(url, init?.method ?? 'GET')
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: new Headers(),
      json: async () => body,
      text: async () => JSON.stringify(body),
    }
  })
  return calls
}

beforeEach(() => {
  mockFetch.mockReset()
})

// ── the constraints read ─────────────────────────────────────────────────────

/** A full 13-member spec row (ConfigConstraintResponse). */
const constraintRow: ConfigConstraint = {
  agreementCode: 'AC',
  okVersion: 'OK24',
  weeklyNormHours: 37,
  maxFlexBalance: 150,
  flexCarryoverMax: 37,
  hasOvertime: false,
  hasMerarbejde: true,
  eveningSupplementEnabled: false,
  nightSupplementEnabled: false,
  weekendSupplementEnabled: false,
  holidaySupplementEnabled: false,
  onCallDutyEnabled: false,
  onCallDutyRate: 0,
}

describe('useConfigConstraints — the typed spec-keyed read', () => {
  it('list → GET /api/config/constraints; the 13-member spec rows flow through', async () => {
    const calls = captureCalls(() => ({ body: [constraintRow] }))
    const { result } = renderHook(() => useConfigConstraints())
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(calls[0].url).toBe('/api/config/constraints')
    expect(calls[0].method).toBe('GET')
    expect(result.current.constraints).toEqual([constraintRow])
    expect(result.current.constraints[0].onCallDutyRate).toBe(0)
  })
})

// ── the project CRUD ─────────────────────────────────────────────────────────

/** A full 4-member spec row (ProjectResponse — NO isActive; prod bug #7). */
const projectRow: ProjectItem = {
  projectId: '33333333-3333-3333-3333-333333333333',
  projectCode: 'PROJ-001',
  projectName: 'Systemudvikling',
  sortOrder: 0,
}

function projectCalls() {
  return captureCalls((_url, method) => {
    if (method === 'GET') return { body: [projectRow] }
    if (method === 'DELETE') return { status: 204 }
    if (method === 'POST') return { status: 201, body: projectRow }
    return { body: { projectId: projectRow.projectId, updated: true } }
  })
}

async function mount() {
  const rendered = renderHook(() => useProjects('STY02'))
  await waitFor(() => expect(rendered.result.current.loading).toBe(false))
  return rendered
}

describe('useProjects — typed CRUD, UNCONDITIONED, exact legacy URLs', () => {
  it('list → GET /api/projects/{orgId}; the 4-member spec rows flow through (no phantom isActive)', async () => {
    const calls = projectCalls()
    const { result } = await mount()
    expect(calls[0].url).toBe('/api/projects/STY02')
    expect(calls[0].method).toBe('GET')
    expect(result.current.projects).toEqual([projectRow])
    expect(result.current.projects[0]).not.toHaveProperty('isActive')
  })

  it('create → POST /api/projects/{orgId}, UNCONDITIONED, key set byte-unchanged', async () => {
    const calls = projectCalls()
    const { result } = await mount()
    await act(async () => {
      await result.current.createProject({
        projectCode: 'PROJ-002',
        projectName: 'Drift',
        sortOrder: 1,
      })
    })
    const post = calls.find((c) => c.method === 'POST')!
    expect(post.url).toBe('/api/projects/STY02')
    expect(post.headers['If-Match']).toBeUndefined()
    expect(post.headers['If-None-Match']).toBeUndefined()
    expect(post.body).toEqual({ projectCode: 'PROJ-002', projectName: 'Drift', sortOrder: 1 })
    expect(Object.keys(post.body as Record<string, unknown>).sort()).toEqual([
      'projectCode', 'projectName', 'sortOrder',
    ])
  })

  it('update → PUT /api/projects/{orgId}/{projectId}, UNCONDITIONED; THE PINNED POST-DROP KEY SET: exactly projectName + sortOrder (projectCode never bound — dropped)', async () => {
    const calls = projectCalls()
    const { result } = await mount()
    await act(async () => {
      await result.current.updateProject(projectRow.projectId, {
        projectName: 'Systemudvikling 2',
        sortOrder: 3,
      })
    })
    const put = calls.find((c) => c.method === 'PUT')!
    expect(put.url).toBe(`/api/projects/STY02/${projectRow.projectId}`)
    expect(put.headers['If-Match']).toBeUndefined()
    expect(put.headers['If-None-Match']).toBeUndefined()
    expect(put.body).toEqual({ projectName: 'Systemudvikling 2', sortOrder: 3 })
    expect(Object.keys(put.body as Record<string, unknown>).sort()).toEqual([
      'projectName', 'sortOrder',
    ])
    expect(put.body).not.toHaveProperty('projectCode')
  })

  it('delete → DELETE /api/projects/{orgId}/{projectId}, UNCONDITIONED, NO body, declared 204', async () => {
    const calls = projectCalls()
    const { result } = await mount()
    let ok = false
    await act(async () => {
      ok = await result.current.deleteProject(projectRow.projectId)
    })
    const del = calls.find((c) => c.method === 'DELETE')!
    expect(del.url).toBe(`/api/projects/STY02/${projectRow.projectId}`)
    expect(del.headers['If-Match']).toBeUndefined()
    expect(del.headers['If-None-Match']).toBeUndefined()
    expect(del.body).toBeUndefined()
    expect(ok).toBe(true)
  })
})
