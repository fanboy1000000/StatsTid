import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { PositionOverrideManagement } from '../PositionOverrideManagement'
import type { PositionOverrideConfig } from '../../../hooks/usePositionOverrides'

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

const mockOverrides: PositionOverrideConfig[] = [
  {
    overrideId: '11111111-1111-1111-1111-111111111111',
    agreementCode: 'AC',
    okVersion: 'OK24',
    positionCode: 'DEPARTMENT_HEAD',
    status: 'ACTIVE',
    maxFlexBalance: 200,
    flexCarryoverMax: null,
    normPeriodWeeks: 4,
    weeklyNormHours: null,
    createdBy: 'SYSTEM_SEED',
    createdAt: '2026-03-08T00:00:00Z',
    updatedAt: '2026-03-08T00:00:00Z',
    description: 'Department head override',
  },
  {
    overrideId: '22222222-2222-2222-2222-222222222222',
    agreementCode: 'AC',
    okVersion: 'OK24',
    positionCode: 'RESEARCHER',
    status: 'ACTIVE',
    maxFlexBalance: null,
    flexCarryoverMax: null,
    normPeriodWeeks: 4,
    weeklyNormHours: null,
    createdBy: 'SYSTEM_SEED',
    createdAt: '2026-03-08T00:00:00Z',
    updatedAt: '2026-03-08T00:00:00Z',
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
    render(<PositionOverrideManagement />)
    expect(screen.getByText('Positionstilpasninger')).toBeDefined()
  })

  it('shows loading state initially', () => {
    mockFetch.mockReturnValue(new Promise(() => {}))
    render(<PositionOverrideManagement />)
    expect(screen.getByText('Henter positionstilpasninger...')).toBeDefined()
  })

  it('renders table with data after loading', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => mockOverrides,
    })

    render(<PositionOverrideManagement />)

    await waitFor(() => {
      expect(screen.getByText('DEPARTMENT_HEAD')).toBeDefined()
    })

    expect(screen.getByText('RESEARCHER')).toBeDefined()
    expect(screen.getByText('Department head override')).toBeDefined()
  })
})
