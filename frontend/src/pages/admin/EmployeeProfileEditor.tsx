import { useEffect, useState, type ChangeEvent, type FormEvent } from 'react'
import {
  useAdminOrganizations,
  useAdminUsersInOrg,
  useEmployeeProfile,
  useEmployeeProfileActions,
  type EmployeeProfile,
  type EmployeeProfileUpdateRequest,
} from '../../hooks/useEmployeeProfile'
import styles from './EntitlementConfigEditor.module.css'

// TASK-3109 / Phase 4d-3 Part 1 (S31). HR-only admin page for the
// authoritative employee_profiles store from TASK-3101..3107. Mirrors S30
// EntitlementConfigEditor for the 412 banner-with-retry shape; mirrors S29
// ProfileEditor for the same-day-only-edit ergonomics (no `effectiveFrom`
// picker exposed — the backend stamps it).
//
// Two-level navigation (org -> user -> profile) mirrors UserManagement.tsx
// because the S31 backend deliberately ships no /api/admin/employee-profiles
// collection endpoint (per-employee GET + PUT only). History view deferred
// to S32 per plan stretch.
//
// Critical contracts:
//   - `parseFloat` for decimal fields (NOT parseInt) — backend WeeklyNormHours
//     + PartTimeFraction are NUMERIC. S30 cycle-2 deferred a parseInt
//     truncation bug on EntitlementConfigEditor; do not repeat it here.
//   - Full 3-field PUT body (weeklyNormHours, partTimeFraction, position) —
//     matches backend's UpdateEmployeeProfileRequest record exactly. S30
//     Step 7a Codex P1 fix #2 documented this discipline.
//   - HROrAbove RBAC — page is HR+; the route guard in App.tsx enforces it.

interface ProfileFormState {
  weeklyNormHours: string
  partTimeFraction: string
  position: string
}

const emptyProfileForm: ProfileFormState = {
  weeklyNormHours: '37',
  partTimeFraction: '1.00',
  position: '',
}

function parseFloatField(value: string, fallback: number): number {
  // S31 decimal-field discipline — backend uses NUMERIC for WeeklyNormHours +
  // PartTimeFraction. parseFloat handles fractional input ("0.75", "32.5") that
  // parseInt would silently truncate. Do not change to parseInt without
  // re-running the wage-type-mapping S30 cycle-2 regression scenario.
  const parsed = Number.parseFloat(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function profileToForm(p: EmployeeProfile): ProfileFormState {
  return {
    weeklyNormHours: String(p.weeklyNormHours),
    partTimeFraction: p.partTimeFraction.toFixed(2),
    position: p.position ?? '',
  }
}

function formToUpdateRequest(f: ProfileFormState): EmployeeProfileUpdateRequest {
  return {
    // Backend NUMERIC columns — parseFloat preserves fractional input.
    weeklyNormHours: parseFloatField(f.weeklyNormHours, 0),
    partTimeFraction: parseFloatField(f.partTimeFraction, 1.0),
    // Trim + null-on-empty so an empty text field clears Position rather than
    // storing a blank string. Backend column is NULL-able.
    position: f.position.trim() ? f.position.trim() : null,
  }
}

export function EmployeeProfileEditor() {
  const { organizations, loading: orgsLoading, error: orgsError } = useAdminOrganizations()
  const [selectedOrgId, setSelectedOrgId] = useState<string>('')
  const { users, loading: usersLoading, error: usersError } = useAdminUsersInOrg(
    selectedOrgId || null,
  )
  const [selectedEmployeeId, setSelectedEmployeeId] = useState<string>('')
  const { profile, loading: profileLoading, error: profileError, refetch: refetchProfile } =
    useEmployeeProfile(selectedEmployeeId || null)
  const { updateProfile } = useEmployeeProfileActions()

  const [form, setForm] = useState<ProfileFormState>(emptyProfileForm)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  // S25/S29/S30 banner-with-retry precedent. 412 from PUT sets `staleConflict`;
  // the "Genindlaes" button refetches the profile (server returns the current
  // ETag) and clears the banner — HR can then re-save.
  const [staleConflict, setStaleConflict] = useState<{
    expected?: number
    actual?: number
  } | null>(null)

  // Default-select the first org once loaded.
  useEffect(() => {
    if (organizations.length > 0 && !selectedOrgId) {
      setSelectedOrgId(organizations[0].orgId)
    }
  }, [organizations, selectedOrgId])

  // Reset the employee selection when the org changes so we never hand a stale
  // employeeId to the per-profile fetch.
  useEffect(() => {
    setSelectedEmployeeId('')
  }, [selectedOrgId])

  // Sync the form whenever a fresh profile loads (initial select + post-412
  // refetch). `profile` includes `etag` from the response — saved into form
  // submission via the editing-profile object below.
  useEffect(() => {
    if (profile) {
      setForm(profileToForm(profile))
    } else {
      setForm(emptyProfileForm)
    }
  }, [profile])

  const handleMutationError = (err: unknown) => {
    const e = err as Error & {
      status?: number
      body?: {
        expectedVersion?: number
        actualVersion?: number
        error?: string
      }
    }
    if (e.status === 412) {
      setStaleConflict({
        expected: e.body?.expectedVersion,
        actual: e.body?.actualVersion,
      })
    } else {
      setFormError(err instanceof Error ? err.message : String(err))
    }
  }

  const handleStaleRefresh = async () => {
    setStaleConflict(null)
    await refetchProfile()
  }

  const setField =
    <K extends keyof ProfileFormState>(field: K) =>
    (e: ChangeEvent<HTMLInputElement>) => {
      const value = e.target.value as ProfileFormState[K]
      setForm((f) => ({ ...f, [field]: value }))
    }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!profile || !selectedEmployeeId) return
    setSubmitting(true)
    setFormError(null)
    try {
      const body = formToUpdateRequest(form)
      const updated = await updateProfile(selectedEmployeeId, profile.etag, body)
      // Sync the form to the freshly-returned profile (carries new ETag) so a
      // second save reuses the new version without re-fetching.
      setForm(profileToForm(updated))
      // Refetch updates the hook's local profile (and its etag) so a third
      // save composes If-Match against the latest version.
      await refetchProfile()
    } catch (err) {
      handleMutationError(err)
    } finally {
      setSubmitting(false)
    }
  }

  const selectedUser = users.find((u) => u.userId === selectedEmployeeId)

  // Derived display — mirrors backend's server-side `IsPartTime = PartTimeFraction < 1.0`.
  const liveIsPartTime: boolean | null = profile ? profile.isPartTime : null
  // Form-side preview so HR sees the derived value update as they edit before
  // saving. Falls back to live value when the input is empty.
  const formPartTimeFraction = parseFloatField(form.partTimeFraction, 1.0)
  const previewIsPartTime = formPartTimeFraction < 1.0

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Medarbejderprofiler</h1>
      </div>

      {orgsError && <div className={styles.alert}>{orgsError}</div>}
      {usersError && <div className={styles.alert}>{usersError}</div>}

      {staleConflict && (
        <div className={styles.alert} role="alert" data-testid="stale-conflict-banner">
          Profilen er aendret af en anden administrator siden du indlaeste den.
          {staleConflict.expected !== undefined && staleConflict.actual !== undefined && (
            <> {' '}(Forventet version {staleConflict.expected}, aktuel version {staleConflict.actual}.)</>
          )}
          {' '}
          <button type="button" className={styles.actionBtn} onClick={handleStaleRefresh}>
            Genindlaes
          </button>
        </div>
      )}

      <div className={styles.formGrid} style={{ marginBottom: 24 }}>
        <div className={styles.formField}>
          <label className={styles.formLabel} htmlFor="ep-org">
            Organisation
          </label>
          {orgsLoading ? (
            <div className={styles.spinner}>Henter...</div>
          ) : (
            <select
              className={styles.input}
              id="ep-org"
              value={selectedOrgId}
              onChange={(e) => setSelectedOrgId(e.target.value)}
            >
              {organizations.map((org) => (
                <option key={org.orgId} value={org.orgId}>
                  {org.orgName} ({org.orgId})
                </option>
              ))}
            </select>
          )}
        </div>

        <div className={styles.formField}>
          <label className={styles.formLabel} htmlFor="ep-user">
            Medarbejder
          </label>
          {usersLoading ? (
            <div className={styles.spinner}>Henter...</div>
          ) : (
            <select
              className={styles.input}
              id="ep-user"
              value={selectedEmployeeId}
              onChange={(e) => setSelectedEmployeeId(e.target.value)}
              disabled={users.length === 0}
            >
              <option value="">{users.length === 0 ? 'Ingen medarbejdere' : 'Vaelg medarbejder...'}</option>
              {users.map((u) => (
                <option key={u.userId} value={u.userId}>
                  {u.displayName} ({u.userId})
                </option>
              ))}
            </select>
          )}
        </div>
      </div>

      {profileError && <div className={styles.alert}>{profileError}</div>}

      {selectedEmployeeId && profileLoading && (
        <div className={styles.spinner}>Henter profil...</div>
      )}

      {selectedEmployeeId && !profileLoading && profile && (
        <form onSubmit={handleSubmit}>
          <div className={styles.dialogInfo}>
            {selectedUser?.displayName ?? profile.employeeId} — version {profile.version}.
            {' '}Aendringer gemmes som en in-place opdatering (samme raekke). Versioneret historik tilfoejes i S32.
          </div>

          <div className={styles.formGrid}>
            <div className={styles.formField}>
              <label className={styles.formLabel} htmlFor="ep-weekly-norm">
                Ugentlig normtid (timer) <span className={styles.required}>*</span>
              </label>
              <input
                className={styles.input}
                id="ep-weekly-norm"
                type="number"
                required
                min={0}
                max={50}
                step={0.25}
                value={form.weeklyNormHours}
                onChange={setField('weeklyNormHours')}
              />
            </div>

            <div className={styles.formField}>
              <label className={styles.formLabel} htmlFor="ep-part-time">
                Deltidsbroek <span className={styles.required}>*</span>
              </label>
              <input
                className={styles.input}
                id="ep-part-time"
                type="number"
                required
                min={0.1}
                max={1.0}
                step={0.01}
                value={form.partTimeFraction}
                onChange={setField('partTimeFraction')}
              />
            </div>

            <div className={styles.formField}>
              <label className={styles.formLabel} htmlFor="ep-position">
                Stilling (valgfri)
              </label>
              <input
                className={styles.input}
                id="ep-position"
                type="text"
                maxLength={100}
                value={form.position}
                onChange={setField('position')}
                placeholder="f.eks. Fuldmaegtig"
              />
            </div>

            <div className={styles.formField}>
              <label className={styles.formLabel}>
                Deltid
                <span className={styles.frozenHint}>(udledt af deltidsbroek)</span>
              </label>
              <input
                className={`${styles.input} ${styles.readOnly}`}
                type="text"
                value={previewIsPartTime ? 'Ja' : 'Nej'}
                readOnly
                aria-readonly="true"
                title={
                  liveIsPartTime === null
                    ? 'Beregnes serverside som deltidsbroek < 1,0'
                    : liveIsPartTime === previewIsPartTime
                      ? 'Beregnes serverside som deltidsbroek < 1,0'
                      : 'Aendres ved naeste gem — beregnes serverside som deltidsbroek < 1,0'
                }
              />
            </div>
          </div>

          {formError && <div className={styles.alert}>{formError}</div>}

          <div className={styles.dialogActions}>
            <button type="submit" className={styles.createBtn} disabled={submitting}>
              {submitting ? 'Gemmer...' : 'Gem'}
            </button>
          </div>
        </form>
      )}

      {selectedEmployeeId && !profileLoading && !profile && !profileError && (
        <div className={styles.emptyState}>
          Ingen profil fundet for denne medarbejder. Profilen oprettes automatisk naar brugeren oprettes.
        </div>
      )}

      {!selectedEmployeeId && !orgsLoading && !usersLoading && (
        <div className={styles.emptyState}>
          Vaelg en organisation og en medarbejder for at redigere profilen.
        </div>
      )}
    </div>
  )
}
