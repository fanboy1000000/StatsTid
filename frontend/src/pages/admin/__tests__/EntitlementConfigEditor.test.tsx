// S73 / TASK-7302 — the admin-editor fullDayOnly round-trip pin (R2 / Step-0b B2).
//
// The editor PUTs the FULL config shape; the full-day-only flag MUST travel
// round-trip or an unrelated admin edit produces a successor whose flag is
// reset (the S68-B1 uniform-by-construction lesson). The flag is displayed
// read-only (no free toggle — flipping it is a deliberate schema/owner change)
// but is carried in the request body sourced from the predecessor row.
//
// Component-level: fetch mocked; the assertion reads the captured PUT body.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { EntitlementConfigEditor } from '../EntitlementConfigEditor'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => {
    mockStorage[key] = val
  },
  removeItem: (key: string) => {
    delete mockStorage[key]
  },
})

const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
})

// A CARE_DAY config row carrying the full-day-only flag (the S73 rule).
const careDayConfig = {
  configId: '22222222-2222-2222-2222-222222222222',
  entitlementType: 'CARE_DAY',
  agreementCode: 'AC',
  okVersion: 'OK24',
  annualQuota: 2,
  accrualModel: 'IMMEDIATE',
  resetMonth: 1,
  carryoverMax: 0,
  proRateByPartTime: false,
  isPerEpisode: false,
  minAge: null,
  description: null,
  fullDayOnly: true,
  version: 4,
  effectiveFrom: '2026-04-01',
  effectiveTo: null,
}

let putBodies: Record<string, unknown>[]

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
  putBodies = []
  mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET'
    if (url.includes('/api/admin/entitlement-configs') && method === 'GET') {
      return {
        ok: true,
        status: 200,
        headers: new Headers(),
        json: async () => [careDayConfig],
        text: async () => JSON.stringify([careDayConfig]),
      }
    }
    if (url.includes('/api/admin/entitlement-configs/') && method === 'PUT') {
      putBodies.push(JSON.parse(String(init?.body)))
      const updated = { ...careDayConfig, version: careDayConfig.version + 1 }
      return {
        ok: true,
        status: 200,
        headers: new Headers({ ETag: '"5"' }),
        json: async () => updated,
        text: async () => JSON.stringify(updated),
      }
    }
    // refetch after the update
    return {
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => [careDayConfig],
      text: async () => JSON.stringify([careDayConfig]),
    }
  })
})

function renderEditor() {
  return render(<EntitlementConfigEditor />)
}

describe('EntitlementConfigEditor — fullDayOnly round-trip (S73 R2 / Step-0b B2)', () => {
  it('editing an UNRELATED field (årlig kvote) PUTs a body that still carries fullDayOnly:true (the flag survives the edit)', async () => {
    renderEditor()
    // The list row loads (CARE_DAY label resolves via TYPE_LABELS).
    await waitFor(() => expect(screen.getByText('AC')).toBeInTheDocument())

    // Open the edit dialog.
    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    await screen.findByText('Rediger berettigelse')

    // The flag renders read-only as "Ja" (carried, not freely editable).
    expect(screen.getByDisplayValue('Ja')).toBeInTheDocument()

    // Change ONLY the annual-quota field, then save.
    const quota = screen.getByLabelText(/Aarlig kvote/) as HTMLInputElement
    fireEvent.change(quota, { target: { value: '3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))

    await waitFor(() => expect(putBodies).toHaveLength(1))
    // The PUT carries the edited quota AND the preserved fullDayOnly flag.
    expect(putBodies[0].annualQuota).toBe(3)
    expect(putBodies[0].fullDayOnly).toBe(true)
  })

  it('S121 ruling #1 — the PUT body OMITS effectiveFrom (the server defaults to today; the client-computed date is gone) while fullDayOnly still travels', async () => {
    renderEditor()
    await waitFor(() => expect(screen.getByText('AC')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'Rediger' }))
    await screen.findByText('Rediger berettigelse')
    fireEvent.click(screen.getByRole('button', { name: 'Gem' }))

    await waitFor(() => expect(putBodies).toHaveLength(1))
    // The alignment pin: no client-side `effectiveFrom` (the midnight race
    // against the server's same-day validator is closed — the server owns
    // today), and the binder-required `fullDayOnly` is present.
    expect(putBodies[0]).not.toHaveProperty('effectiveFrom')
    expect(putBodies[0].fullDayOnly).toBe(true)
  })

  it('the CREATE request carries the fullDayOnly checkbox value', async () => {
    renderEditor()
    await waitFor(() => expect(screen.getByText('AC')).toBeInTheDocument())

    let postBody: Record<string, unknown> | null = null
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      if (url.includes('/api/admin/entitlement-configs') && method === 'POST') {
        postBody = JSON.parse(String(init?.body))
        return {
          ok: true,
          status: 201,
          headers: new Headers({ ETag: '"1"' }),
          json: async () => ({ ...careDayConfig, configId: 'new' }),
          text: async () => JSON.stringify({ ...careDayConfig, configId: 'new' }),
        }
      }
      return {
        ok: true,
        status: 200,
        headers: new Headers(),
        json: async () => [careDayConfig],
        text: async () => JSON.stringify([careDayConfig]),
      }
    })

    fireEvent.click(screen.getByRole('button', { name: 'Opret ny' }))
    await screen.findByText('Opret berettigelse')

    fireEvent.change(screen.getByLabelText(/Aftale/), { target: { value: 'AC' } })
    fireEvent.change(screen.getByLabelText(/OK-version/), { target: { value: 'OK24' } })
    fireEvent.click(screen.getByLabelText(/Kun hele dage/))
    fireEvent.click(screen.getByRole('button', { name: 'Opret' }))

    await waitFor(() => expect(postBody).not.toBeNull())
    expect(postBody!.fullDayOnly).toBe(true)
  })
})
