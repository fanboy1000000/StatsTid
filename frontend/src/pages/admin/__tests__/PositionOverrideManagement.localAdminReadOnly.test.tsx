// AC-C2w (ruling 31; SEC-032). Arm 1 is kind F, arm 2 is kind R.
// Red conditions (arm 1): before the change the page offers "Opret ny", "Rediger" and "Deaktiver" to
// a LocalAdmin although every write is GlobalAdminOnly on the server (a 403), so the queryBy...
// assertions are not null and the note text is missing. Arm 2 (R): a GlobalAdmin keeps all four
// actions and sees no note.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import { PositionOverrideManagement } from '../PositionOverrideManagement'
import { renderWithCalendar } from '../../../test/renderWithCalendar'
import type { PositionOverrideConfig } from '../../../hooks/usePositionOverrides'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const auth = vi.hoisted(() => ({ role: 'LocalAdmin' as string | null }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ role: auth.role }),
}))

const NOTE = 'Tilpasningerne kan kun ændres af en global administrator.'

const row = (overrideId: string, positionCode: string, status: 'ACTIVE' | 'INACTIVE'): PositionOverrideConfig => ({
  overrideId,
  agreementCode: 'AC',
  okVersion: 'OK24',
  positionCode,
  status,
  version: 1,
  maxFlexBalance: 200,
  normPeriodWeeks: 4,
  weeklyNormHours: null,
  createdBy: 'SYSTEM_SEED',
  createdAt: '2026-03-08T00:00:00Z',
  updatedAt: '2026-03-08T00:00:00Z',
  appliesFrom: '2026-11-02',
  description: null,
})

beforeEach(() => {
  mockFetch.mockReset()
  mockFetch.mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers(),
    json: async () => [row('1', 'DEPARTMENT_HEAD', 'ACTIVE'), row('2', 'RESEARCHER', 'INACTIVE')],
  })
})

describe('PositionOverrideManagement — write actions follow the role', () => {
  it('a LocalAdmin sees the list and the note, and none of the four write buttons (F)', async () => {
    auth.role = 'LocalAdmin'
    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)
    await waitFor(() => expect(screen.getByText('DEPARTMENT_HEAD')).toBeDefined())
    expect(screen.getByText('RESEARCHER')).toBeDefined()
    expect(screen.getByText(NOTE)).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Opret ny' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Rediger' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Deaktiver' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Aktiver' })).toBeNull()
  })

  it('a GlobalAdmin sees all four write buttons and no note (R)', async () => {
    auth.role = 'GlobalAdmin'
    renderWithCalendar('2026-11-12', <PositionOverrideManagement />)
    await waitFor(() => expect(screen.getByText('DEPARTMENT_HEAD')).toBeDefined())
    expect(screen.queryByText(NOTE)).toBeNull()
    expect(screen.getByRole('button', { name: 'Opret ny' })).toBeDefined()
    expect(screen.getAllByRole('button', { name: 'Rediger' })).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Deaktiver' })).toBeDefined()
    expect(screen.getByRole('button', { name: 'Aktiver' })).toBeDefined()
  })
})
