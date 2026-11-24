import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor, fireEvent } from '@testing-library/react'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import { PositionOverrideManagement } from '../PositionOverrideManagement'
import type { PositionOverrideConfig } from '../../../hooks/usePositionOverrides'

// Mock fetch globally
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// S148 T23: the page reads the role (write buttons are for a GlobalAdmin) and the Copenhagen today.
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: 'GlobalAdmin' }),
}))

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

const mockOverrides: PositionOverrideConfig[] = [
  {
    overrideId: '11111111-1111-1111-1111-111111111111',
    agreementCode: 'AC',
    okVersion: 'OK24',
    positionCode: 'DEPARTMENT_HEAD',
    status: 'ACTIVE',
    version: 1,
    maxFlexBalance: 200,
    normPeriodWeeks: 4,
    weeklyNormHours: null,
    createdBy: 'SYSTEM_SEED',
    createdAt: '2026-03-08T00:00:00Z',
    updatedAt: '2026-03-08T00:00:00Z',
    appliesFrom: '0001-01-01',
    description: 'Department head override',
  },
  {
    overrideId: '22222222-2222-2222-2222-222222222222',
    agreementCode: 'AC',
    okVersion: 'OK24',
    positionCode: 'RESEARCHER',
    status: 'ACTIVE',
    version: 1,
    maxFlexBalance: null,
    normPeriodWeeks: 4,
    weeklyNormHours: null,
    createdBy: 'SYSTEM_SEED',
    createdAt: '2026-03-08T00:00:00Z',
    updatedAt: '2026-03-08T00:00:00Z',
    appliesFrom: '0001-01-01',
    description: null,
  },
]

beforeEach(() => {
  mockFetch.mockReset()
  mockReload.mockReset()
  Object.keys(mockStorage).forEach(k => delete mockStorage[k])
})

describe('PositionOverrideManagement', () => {
  it('renders the page title', () => {
    // Return a pending promise so loading stays true
    mockFetch.mockReturnValue(new Promise(() => {}))
    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)
    expect(screen.getByText('Positionstilpasninger')).toBeDefined()
  })

  it('shows loading state initially', () => {
    mockFetch.mockReturnValue(new Promise(() => {}))
    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)
    expect(screen.getByRole('status')).toBeDefined()
  })

  it('renders table with data after loading', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockOverrides,
    })

    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)

    await waitFor(() => {
      expect(screen.getByText('DEPARTMENT_HEAD')).toBeDefined()
    })

    expect(screen.getByText('RESEARCHER')).toBeDefined()
    expect(screen.getByText('Department head override')).toBeDefined()
  })
})

// S25 / TASK-2506 banner-with-retry test for PositionOverrideManagement.
//
// Mocks fetch to:
//   1. Return the loaded list on initial GET.
//   2. Return 412 with { expectedVersion, actualVersion } when the user clicks
//      "Deaktiver" (the deactivate POST goes through `apiFetchWithEtag`).
// Asserts the stale-state banner renders with the version pair and the
// "Genindlaes" button triggers a refetch + clears the banner.
describe('PositionOverrideManagement — 412 banner-with-retry', () => {
  beforeEach(() => {
    mockFetch.mockReset()
    mockReload.mockReset()
    Object.keys(mockStorage).forEach((k) => delete mockStorage[k])
  })

  it('shows the stale-conflict banner on 412 with expected/actual version pair', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => mockOverrides,
    })
    // Deactivate POST → 412 stale.
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify({
        error: 'Concurrency precondition failed',
        expectedVersion: 1,
        actualVersion: 4,
        currentState: { ...mockOverrides[0], version: 4 },
      }),
    })

    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)

    await waitFor(() => {
      expect(screen.getByText('DEPARTMENT_HEAD')).toBeDefined()
    })

    // Find and click the "Deaktiver" button on the first row.
    const deactivateBtns = screen.getAllByText('Deaktiver')
    fireEvent.click(deactivateBtns[0])
    // S148 T23: the switch asks for its date first; the row's confirm button is the first "Deaktiver" now.
    fireEvent.click(screen.getAllByText('Deaktiver')[0])

    await waitFor(() => {
      const banner = screen.getByTestId('stale-conflict-banner')
      expect(banner).toBeDefined()
      expect(banner.textContent).toContain('Forventet version 1')
      expect(banner.textContent).toContain('aktuel version 4')
    })
  })

  it('Genindlaes button clears the banner and refetches', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => mockOverrides,
    })
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 412,
      headers: new Headers(),
      text: async () => JSON.stringify({
        error: 'Concurrency precondition failed',
        expectedVersion: 1,
        actualVersion: 4,
      }),
    })
    // Refetch on Genindlaes.
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => mockOverrides.map((o) => ({ ...o, version: 4 })),
    })

    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)

    await waitFor(() => {
      expect(screen.getByText('DEPARTMENT_HEAD')).toBeDefined()
    })

    fireEvent.click(screen.getAllByText('Deaktiver')[0])
    // S148 T23: second click confirms the switch (the date step comes first).
    fireEvent.click(screen.getAllByText('Deaktiver')[0])

    await waitFor(() => {
      expect(screen.getByTestId('stale-conflict-banner')).toBeDefined()
    })

    fireEvent.click(screen.getByText(/Genindlaes/i))

    await waitFor(() => {
      expect(screen.queryByTestId('stale-conflict-banner')).toBeNull()
    })

    // Initial + deactivate + refetch.
    expect(mockFetch).toHaveBeenCalledTimes(3)
  })
})
