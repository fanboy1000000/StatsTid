// S25 / TASK-2506 banner-with-retry test for AgreementConfigEditor.
//
// Mocks fetch to:
//   1. Return a loaded DRAFT config on the initial GET.
//   2. Return 412 with { expectedVersion, actualVersion, currentState } on PUT.
// Asserts the stale-state banner renders with the version pair, then asserts
// that clicking "Genindlaes" triggers a refetch and clears the banner.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { AgreementConfigEditor } from '../AgreementConfigEditor'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const mockStorage: Record<string, string> = {}
vi.stubGlobal('localStorage', {
  getItem: (key: string) => mockStorage[key] ?? null,
  setItem: (key: string, val: string) => { mockStorage[key] = val },
  removeItem: (key: string) => { delete mockStorage[key] },
})

const mockReload = vi.fn()
Object.defineProperty(window, 'location', {
  value: { reload: mockReload },
  writable: true,
})

const draftConfig = {
  configId: '11111111-1111-1111-1111-111111111111',
  agreementCode: 'AC',
  okVersion: 'OK24',
  status: 'DRAFT' as const,
  version: 3,
  weeklyNormHours: 37,
  normPeriodWeeks: 1,
  normModel: 'WEEKLY_HOURS',
  annualNormHours: 1924,
  maxFlexBalance: 150,
  flexCarryoverMax: 37,
  hasOvertime: false,
  hasMerarbejde: false,
  overtimeThreshold50: 0,
  overtimeThreshold100: 0,
  eveningSupplementEnabled: false,
  nightSupplementEnabled: false,
  weekendSupplementEnabled: false,
  holidaySupplementEnabled: false,
  eveningStart: 17,
  eveningEnd: 23,
  nightStart: 23,
  nightEnd: 6,
  eveningRate: 0,
  nightRate: 0,
  weekendSaturdayRate: 0,
  weekendSundayRate: 0,
  holidayRate: 0,
  onCallDutyEnabled: false,
  onCallDutyRate: 0,
  callInWorkEnabled: false,
  callInMinimumHours: 3,
  callInRate: 1,
  travelTimeEnabled: false,
  workingTravelRate: 1,
  nonWorkingTravelRate: 0.5,
  createdBy: 'admin',
  createdAt: '2026-04-01T00:00:00Z',
  updatedAt: '2026-04-01T00:00:00Z',
  publishedAt: null,
  archivedAt: null,
  clonedFromId: null,
  description: null,
}

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
})

function renderEditor() {
  return render(
    <MemoryRouter initialEntries={['/global/overenskomster/11111111-1111-1111-1111-111111111111']}>
      <Routes>
        <Route path="/global/overenskomster/:configId" element={<AgreementConfigEditor />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('AgreementConfigEditor — 412 banner-with-retry', () => {
  it('renders the stale-conflict banner on 412 with expected/actual version pair', async () => {
    // Initial GET — header + body version 3.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"3"' }),
      json: async () => draftConfig,
    })
    // PUT save → 412 stale.
    const stalePayload = {
      error: 'Concurrency precondition failed',
      expectedVersion: 3,
      actualVersion: 7,
      currentState: { ...draftConfig, version: 7 },
    }
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify(stalePayload),
    })

    renderEditor()

    // Wait for the form to load — the page header shows AC / OK24.
    await waitFor(() => {
      expect(screen.getByText('AC / OK24')).toBeDefined()
    })

    // Click "Gem" — the first button (primary save).
    const saveBtn = screen.getByText('Gem')
    fireEvent.click(saveBtn)

    // Banner shows up with the expected/actual pair.
    await waitFor(() => {
      const banner = screen.getByTestId('stale-conflict-banner')
      expect(banner).toBeDefined()
      expect(banner.textContent).toContain('Forventet version 3')
      expect(banner.textContent).toContain('aktuel version 7')
    })
  })

  it('refresh button triggers refetch and clears the banner', async () => {
    // Initial GET — version 3.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"3"' }),
      json: async () => draftConfig,
    })
    // PUT save → 412.
    const stalePayload = {
      error: 'Concurrency precondition failed',
      expectedVersion: 3,
      actualVersion: 7,
      currentState: { ...draftConfig, version: 7 },
    }
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify(stalePayload),
    })
    // Refetch on Genindlaes → returns version 7.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers({ ETag: '"7"' }),
      json: async () => ({ ...draftConfig, version: 7 }),
    })

    renderEditor()

    await waitFor(() => {
      expect(screen.getByText('AC / OK24')).toBeDefined()
    })

    fireEvent.click(screen.getByText('Gem'))

    await waitFor(() => {
      expect(screen.getByTestId('stale-conflict-banner')).toBeDefined()
    })

    fireEvent.click(screen.getByText(/Genindlaes/i))

    // Banner disappears after refetch resolves.
    await waitFor(() => {
      expect(screen.queryByTestId('stale-conflict-banner')).toBeNull()
    })

    // Confirm a third fetch happened (initial + PUT + refetch).
    expect(mockFetch).toHaveBeenCalledTimes(3)
  })
})
