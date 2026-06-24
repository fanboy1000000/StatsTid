// S97 / TASK-9705 — tests for the org-scoped Enheder management panel. Mocks
// fetch (the real useEnheder hook drives the CRUD). Asserts: it lists the
// selected Organisation's enheder; create POSTs + refetches; rename PUTs with
// If-Match; delete DELETEs (after confirm); a MAO shows the honest empty note;
// switching the panel org reloads; a 409 dup surfaces an honest toast error.
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { ToastProvider } from '../../../../components/ui/Toast'
import { EnhederPanel } from '../EnhederPanel'
import type { Organization } from '../../../../hooks/useAdmin'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)
const mockStorage: Record<string, string> = { statstid_token: 'test-token' }
vi.stubGlobal('localStorage', {
  getItem: (k: string) => mockStorage[k] ?? null,
  setItem: (k: string, v: string) => { mockStorage[k] = v },
  removeItem: (k: string) => { delete mockStorage[k] },
})

function ok(json: unknown, etag?: string) {
  return { ok: true, status: 200, headers: new Headers(etag ? { ETag: etag } : {}), json: async () => json, text: async () => JSON.stringify(json) }
}
function err(status: number, body: unknown = {}) {
  return { ok: false, status, headers: new Headers(), json: async () => body, text: async () => JSON.stringify(body) }
}

const orgs: Organization[] = [
  { orgId: 'MIN1', orgName: 'Finansministeriet', orgType: 'MAO', parentOrgId: null, agreementCode: 'AC' },
  { orgId: 'STY1', orgName: 'Moderniseringsstyrelsen', orgType: 'ORGANISATION', parentOrgId: 'MIN1', agreementCode: 'AC' },
  { orgId: 'STY2', orgName: 'Digitaliseringsstyrelsen', orgType: 'ORGANISATION', parentOrgId: 'MIN1', agreementCode: 'AC' },
]

const STY1_ENHEDER = [
  { enhedId: 'E1', organisationId: 'STY1', name: 'Netværk', version: 1 },
  { enhedId: 'E2', organisationId: 'STY1', name: 'Drift', version: 2 },
]

/** A fetch router keyed by url+method; defaults to a benign STY1 list. */
function router(overrides: Record<string, (init?: RequestInit) => unknown> = {}) {
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    for (const [key, factory] of Object.entries(overrides)) {
      const [m, frag] = key.split(' ')
      if (url.includes(frag) && (init?.method ?? 'GET') === m) return factory(init)
    }
    // default list — the backend serves `{ enheder: [...] }` (object envelope).
    if (url.includes('/api/admin/enheder?organisationId=STY1')) return ok({ enheder: STY1_ENHEDER })
    if (url.includes('/api/admin/enheder?organisationId=STY2')) return ok({ enheder: [] })
    if (url.includes('/api/admin/enheder?organisationId=MIN1')) return err(400, { error: 'MAO' })
    return err(404, { error: 'nf' })
  })
}

function renderPanel(selectedOrgId = 'STY1') {
  return render(
    <ToastProvider>
      <EnhederPanel organisations={orgs} selectedOrgId={selectedOrgId} />
    </ToastProvider>,
  )
}

let confirmSpy: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  mockFetch.mockReset()
  router()
  confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
})
afterEach(() => {
  confirmSpy.mockRestore()
})

describe('EnhederPanel', () => {
  it('lists the selected Organisation enheder', async () => {
    renderPanel('STY1')
    await waitFor(() => {
      expect(screen.getByTestId('enheder-list')).toBeDefined()
    })
    expect(screen.getByTestId('enheder-name-E1').textContent).toBe('Netværk')
    expect(screen.getByTestId('enheder-name-E2').textContent).toBe('Drift')
  })

  it('a MAO org shows the honest empty note, not the create row', async () => {
    renderPanel('MIN1')
    await waitFor(() => {
      expect(screen.getByTestId('enheder-mao-note')).toBeDefined()
    })
    // The create input is hidden for a MAO (it holds no enheder).
    expect(screen.queryByTestId('enheder-new-name')).toBeNull()
  })

  it('an ORGANISATION with no enheder shows the empty state + the create row', async () => {
    renderPanel('STY2')
    await waitFor(() => {
      expect(screen.getByTestId('enheder-empty')).toBeDefined()
    })
    expect(screen.getByTestId('enheder-new-name')).toBeDefined()
  })

  it('create POSTs {organisationId, name} then refetches the list', async () => {
    const posts: Array<{ body: unknown }> = []
    let created = false
    router({
      'POST /api/admin/enheder': (init) => {
        posts.push({ body: init?.body ? JSON.parse(init.body as string) : undefined })
        created = true
        return ok({ enhedId: 'E9', organisationId: 'STY1', name: 'Sikkerhed', version: 1 }, '"1"')
      },
      'GET /api/admin/enheder?organisationId=STY1': () =>
        created
          ? ok({ enheder: [...STY1_ENHEDER, { enhedId: 'E9', organisationId: 'STY1', name: 'Sikkerhed', version: 1 }] })
          : ok({ enheder: STY1_ENHEDER }),
    })
    renderPanel('STY1')
    await waitFor(() => expect(screen.getByTestId('enheder-list')).toBeDefined())

    fireEvent.change(screen.getByTestId('enheder-new-name'), { target: { value: 'Sikkerhed' } })
    fireEvent.click(screen.getByTestId('enheder-create'))

    await waitFor(() => {
      expect(screen.getByTestId('enheder-name-E9')).toBeDefined()
    })
    expect(posts[0].body).toEqual({ organisationId: 'STY1', name: 'Sikkerhed' })
  })

  it('a 409 dup on create surfaces an honest error toast (no crash, list intact)', async () => {
    router({
      'POST /api/admin/enheder': () => err(409, { error: 'dup' }),
    })
    renderPanel('STY1')
    await waitFor(() => expect(screen.getByTestId('enheder-list')).toBeDefined())

    fireEvent.change(screen.getByTestId('enheder-new-name'), { target: { value: 'Netværk' } })
    fireEvent.click(screen.getByTestId('enheder-create'))

    await waitFor(() => {
      expect(screen.getByText(/findes allerede en aktiv enhed/i)).toBeDefined()
    })
    // The list is still there (the create failed gracefully).
    expect(screen.getByTestId('enheder-name-E1')).toBeDefined()
  })

  it('rename PUTs {name} with the row If-Match then refetches', async () => {
    const puts: Array<{ ifMatch?: string; body: unknown }> = []
    let renamed = false
    router({
      'PUT /api/admin/enheder/E1': (init) => {
        const h = init?.headers as Record<string, string> | undefined
        puts.push({ ifMatch: h?.['If-Match'], body: init?.body ? JSON.parse(init.body as string) : undefined })
        renamed = true
        return ok({ enhedId: 'E1', organisationId: 'STY1', name: 'Netværk II', version: 2 }, '"2"')
      },
      'GET /api/admin/enheder?organisationId=STY1': () =>
        renamed
          ? ok({ enheder: [{ enhedId: 'E1', organisationId: 'STY1', name: 'Netværk II', version: 2 }, STY1_ENHEDER[1]] })
          : ok({ enheder: STY1_ENHEDER }),
    })
    renderPanel('STY1')
    await waitFor(() => expect(screen.getByTestId('enheder-name-E1')).toBeDefined())

    fireEvent.click(screen.getByTestId('enheder-rename-E1'))
    const input = screen.getByTestId('enheder-rename-input-E1')
    fireEvent.change(input, { target: { value: 'Netværk II' } })
    fireEvent.click(screen.getByTestId('enheder-rename-save-E1'))

    await waitFor(() => {
      expect(screen.getByTestId('enheder-name-E1').textContent).toBe('Netværk II')
    })
    // The row's If-Match was its own version ("1").
    expect(puts[0].ifMatch).toBe('"1"')
    expect(puts[0].body).toEqual({ name: 'Netværk II' })
  })

  it('delete DELETEs with If-Match after a confirm, then refetches', async () => {
    const deletes: Array<{ ifMatch?: string }> = []
    let deleted = false
    router({
      'DELETE /api/admin/enheder/E2': (init) => {
        const h = init?.headers as Record<string, string> | undefined
        deletes.push({ ifMatch: h?.['If-Match'] })
        deleted = true
        return { ok: true, status: 204, headers: new Headers(), text: async () => '' }
      },
      'GET /api/admin/enheder?organisationId=STY1': () =>
        deleted ? ok({ enheder: [STY1_ENHEDER[0]] }) : ok({ enheder: STY1_ENHEDER }),
    })
    renderPanel('STY1')
    await waitFor(() => expect(screen.getByTestId('enheder-name-E2')).toBeDefined())

    fireEvent.click(screen.getByTestId('enheder-delete-E2'))

    await waitFor(() => {
      expect(screen.queryByTestId('enheder-name-E2')).toBeNull()
    })
    expect(confirmSpy).toHaveBeenCalled()
    // E2's version was 2 → If-Match "2".
    expect(deletes[0].ifMatch).toBe('"2"')
    // E1 survives.
    expect(screen.getByTestId('enheder-name-E1')).toBeDefined()
  })

  it('switching the panel Organisation selector reloads that org enheder', async () => {
    router({
      'GET /api/admin/enheder?organisationId=STY2': () =>
        ok({ enheder: [{ enhedId: 'X1', organisationId: 'STY2', name: 'Arkitektur', version: 1 }] }),
    })
    renderPanel('STY1')
    await waitFor(() => expect(screen.getByTestId('enheder-name-E1')).toBeDefined())

    fireEvent.change(screen.getByTestId('enheder-org-select'), { target: { value: 'STY2' } })

    await waitFor(() => {
      expect(screen.getByTestId('enheder-name-X1')).toBeDefined()
    })
    // The STY1 rows are gone.
    expect(screen.queryByTestId('enheder-name-E1')).toBeNull()
  })
})
