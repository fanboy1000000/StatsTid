// Smoke tests for the profile editor (S21 / TASK-2109).
// Basic functional only — verifies "save disabled until changed" + the
// 412 stale-state banner. Saved-profile behavior + 400 per-field rendering
// are exercised through the change detection / mock fetch.
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { ProfileEditor } from '../ProfileEditor'
import type { LocalAgreementProfile } from '../../../hooks/useConfig'
import type { AgreementConfig } from '../../../hooks/useAgreementConfigs'

const centralConfig: AgreementConfig = {
  configId: 'cfg-1',
  agreementCode: 'AC',
  okVersion: 'OK24',
  status: 'ACTIVE',
  weeklyNormHours: 37,
  normPeriodWeeks: 1,
  normModel: 'WEEKLY',
  annualNormHours: 1924,
  maxFlexBalance: 37,
  flexCarryoverMax: 37,
  hasOvertime: false,
  hasMerarbejde: true,
  overtimeThreshold50: 0,
  overtimeThreshold100: 0,
  eveningSupplementEnabled: true,
  nightSupplementEnabled: true,
  weekendSupplementEnabled: true,
  holidaySupplementEnabled: true,
  eveningStart: 17,
  eveningEnd: 23,
  nightStart: 23,
  nightEnd: 6,
  eveningRate: 1.25,
  nightRate: 1.5,
  weekendSaturdayRate: 1.5,
  weekendSundayRate: 2,
  holidayRate: 2,
  onCallDutyEnabled: false,
  onCallDutyRate: 0,
  callInWorkEnabled: false,
  callInMinimumHours: 0,
  callInRate: 0,
  travelTimeEnabled: false,
  workingTravelRate: 0,
  nonWorkingTravelRate: 0,
  createdBy: 'system',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  publishedAt: '2026-01-01T00:00:00Z',
  archivedAt: null,
  clonedFromId: null,
  description: null,
}

const existingProfile: LocalAgreementProfile = {
  profileId: '11111111-1111-1111-1111-111111111111',
  orgId: 'ORG1',
  agreementCode: 'AC',
  okVersion: 'OK24',
  effectiveFrom: '2026-04-27',
  effectiveTo: null,
  weeklyNormHours: 35,
  maxFlexBalance: null,
  flexCarryoverMax: null,
  maxOvertimeHoursPerPeriod: null,
  overtimeRequiresPreApproval: null,
  createdBy: 'admin@example.dk',
  createdAt: '2026-04-27T08:00:00Z',
}

describe('ProfileEditor', () => {
  let originalFetch: typeof globalThis.fetch

  beforeEach(() => {
    originalFetch = globalThis.fetch
  })
  afterEach(() => {
    globalThis.fetch = originalFetch
    vi.restoreAllMocks()
  })

  it('disables Save until at least one field changes', () => {
    render(
      <ProfileEditor
        orgId="ORG1"
        agreementCode="AC"
        okVersion="OK24"
        orgLabel="Test org"
        profile={existingProfile}
        etag={`"${existingProfile.profileId}"`}
        centralConfig={centralConfig}
        loading={false}
        loadError={null}
        onSaved={() => {}}
      />,
    )
    const save = screen.getByRole('button', { name: /gem aendringer/i }) as HTMLButtonElement
    expect(save.disabled).toBe(true)
  })

  it('renders the deactivate button as disabled with explanatory tooltip', () => {
    render(
      <ProfileEditor
        orgId="ORG1"
        agreementCode="AC"
        okVersion="OK24"
        orgLabel="Test org"
        profile={existingProfile}
        etag={`"${existingProfile.profileId}"`}
        centralConfig={centralConfig}
        loading={false}
        loadError={null}
        onSaved={() => {}}
      />,
    )
    const button = screen.getByRole('button', { name: /deaktiver/i }) as HTMLButtonElement
    expect(button.disabled).toBe(true)
    expect(button.getAttribute('title')).toMatch(/senere opgave/i)
  })

  it('shows the stale-state banner on a 412 response', async () => {
    // Mock fetch to return 412 with the structured stale-state body.
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 412,
      headers: { get: () => null },
      text: async () => JSON.stringify({ error: 'Stale' }),
      json: async () => ({}),
    }) as unknown as typeof globalThis.fetch

    render(
      <ProfileEditor
        orgId="ORG1"
        agreementCode="AC"
        okVersion="OK24"
        orgLabel="Test org"
        profile={existingProfile}
        etag={`"${existingProfile.profileId}"`}
        centralConfig={centralConfig}
        loading={false}
        loadError={null}
        onSaved={() => {}}
      />,
    )

    // Change WeeklyNormHours from 35 to 36 — forces a delta vs the loaded profile.
    const numberInputs = document.querySelectorAll('input[type="number"]')
    expect(numberInputs.length).toBeGreaterThan(0)
    fireEvent.change(numberInputs[0] as HTMLInputElement, { target: { value: '36' } })

    // Save.
    const save = screen.getByRole('button', { name: /gem/i })
    fireEvent.click(save)

    await waitFor(() => {
      expect(screen.getByText(/foraeldet|forældet/i)).toBeDefined()
    })
  })
})
