// S76b / TASK-7602 — the multi-PUT save-orchestration hook for the unified
// EditPersonDrawer. Ports the sequential, partial-failure-tolerant save from
// `UserManagement.handleEditSubmit` into a reusable hook, with per-section
// committed/failed state + an authoritative refetch after each successful PUT.
//
// Each endpoint is an INDEPENDENT call with its OWN precondition (NOT a uniform
// 412/428), per SPRINT-76 R3 / R6 / Codex c1-B3:
//   • users PUT             → admin-strict If-Match (412 stale / 428 missing) + staleConflict banner
//   • employee-profiles PUT → admin-strict If-Match (412 stale / 428 missing) + staleConflict banner
//   • birth-date PUT        → admin-strict If-Match (users.version)
//   • employment-start PUT  → admin-strict If-Match (users.version)
//   • CHILD_SICK PUT        → DISTINCT contract: If-None-Match:* on CREATE,
//                             409 on a create race, If-Match on UPDATE
//                             (ported verbatim from useEntitlementEligibility +
//                             UserManagement — NOT collapsed to 412/428).
//
// "Partial-failure honesty" (R3): a later PUT failing leaves the earlier
// commits in place; the orchestrator records each section's committed/failed
// state + re-stamps the live version (read-your-write) from the PUT response so
// a follow-up Save in the same session carries the bumped version. The HR
// sections may 403 for a non-HR actor — that surfaces honestly per-section.

import { useCallback, useState } from 'react'
import { useOrgUsers, type UserMutationError, type WithEtag, type User } from './useAdmin'
import {
  useEntitlementEligibility,
  type ChildSickEligibilitySnapshot,
} from './useEntitlementEligibility'
import {
  saveEmployeeProfile,
  type EmployeeProfileSnapshot,
} from '../pages/admin/editPerson/employeeProfileApi'
import {
  makeInitialSectionSaveMap,
  type SaveSectionKey,
  type SectionSaveMap,
  type ProfileFields,
  type EntitlementFields,
  type StamdataFields,
} from '../pages/admin/editPerson/types'

// S34 TASK-3409 (ADR-023 D8). UTC year-month-day matches the backend's
// `DateTime.UtcNow` reference for the same-day-only-edit validator.
export function todayIsoUtc(): string {
  return new Date().toISOString().slice(0, 10)
}

/** A status-tagged error (the shape thrown by the user/profile PUT helpers). */
interface StatusError extends Error {
  status?: number
  body?: {
    expectedVersion?: number
    actualVersion?: number
    currentVersion?: number
  }
}

/** Live state the edit-save needs: the captured ETags / versions per section. */
export interface EditLiveState {
  user: WithEtag<User>
  profile: EmployeeProfileSnapshot | null
  birthDateVersion: number | null
  birthDateInitial: string
  employmentStartVersion: number | null
  employmentStartInitial: string
  childSickRowExists: boolean
  childSickVersion: number | null
}

/** What the drawer passes to `saveEdit` — the form values + dirtiness flags. */
export interface EditSaveInput {
  stamdata: StamdataFields
  profile: ProfileFields
  entitlement: EntitlementFields
  childSickDirty: boolean
  /** True when the actor may write the HR sections (LocalHR+). */
  isHr: boolean
  /**
   * S109 / TASK-10902 — the CROSS-Organisation TRANSFER's landing unit, threaded
   * into the stamdata `PUT /users/{id}` body. Present (incl. `null` = home at the
   * new Organisation) ONLY on a transfer; the placement router (usePlacement)
   * leaves it `undefined` on a same-Organisation save so the unit is changed via
   * `PUT /users/{id}/unit` instead. On a non-transfer PUT the backend ignores it.
   */
  unitId?: string | null
}

export interface StaleConflict {
  expected?: number
  actual?: number
}

export interface SaveEditResult {
  ok: boolean
  // The refreshed live state (re-stamped versions) so the drawer can keep
  // editing without a reopen. Always returned (even on partial failure).
  live: EditLiveState
  // Populated when the users/profile PUT hit a 412 stale-version — drives the
  // "Genindlæs" banner.
  staleConflict: StaleConflict | null
  // S109 / TASK-10902 — the FIRST failed section's server message (e.g. the 422
  // cross-Organisation manager-with-active-reports transfer block on the stamdata
  // PUT). null when ok. The placement router surfaces it as the drawer's error so
  // the real backend reason reaches the user.
  error: string | null
}

export function useEditPerson() {
  // `useOrgUsers('')` gives us the user mutation helpers without binding to an
  // org list (the drawer doesn't own a roster — it's handed a user to edit).
  const { createUser, updateUser, fetchUser } = useOrgUsers('')
  const {
    setChildSick,
    fetchChildSickEligibility,
    setBirthDate,
    setEmploymentStartDate,
  } = useEntitlementEligibility()

  const [sections, setSections] = useState<SectionSaveMap>(makeInitialSectionSaveMap)
  const [saving, setSaving] = useState(false)

  const resetSections = useCallback(() => {
    setSections(makeInitialSectionSaveMap())
  }, [])

  const mark = (key: SaveSectionKey, status: 'committed' | 'failed', message?: string) => {
    setSections((prev) => ({ ...prev, [key]: { status, message } }))
  }

  /**
   * Sequential independent save. Mutates a working copy of `live` as each PUT
   * succeeds (read-your-write), marks each section committed/failed, and returns
   * the refreshed live state + any stale-conflict (for the banner). A 412 on the
   * users/profile PUT short-circuits to the banner (the classic stale path). Any
   * other section error is recorded per-section and the run CONTINUES so an
   * independent later section can still commit.
   */
  const saveEdit = useCallback(
    async (input: EditSaveInput, startLive: EditLiveState): Promise<SaveEditResult> => {
      setSaving(true)
      resetSections()
      let live: EditLiveState = startLive
      let staleConflict: StaleConflict | null = null
      let firstError: string | null = null
      let ok = true

      // (1) users PUT — admin-strict If-Match.
      try {
        const updated = await updateUser(
          live.user.userId,
          {
            effectiveFrom: todayIsoUtc(),
            displayName: input.stamdata.displayName,
            email: input.stamdata.email || undefined,
            primaryOrgId: input.stamdata.primaryOrgId,
            agreementCode: input.stamdata.agreementCode,
            // S109 / TASK-10902 — thread the transfer's landing unit ONLY when the
            // caller supplied it (a cross-Organisation transfer); omitted on a
            // same-Organisation save (the unit is changed via PUT /users/{id}/unit).
            ...(input.unitId !== undefined ? { unitId: input.unitId } : {}),
          },
          live.user.etag,
        )
        live = { ...live, user: updated }
        mark('stamdata', 'committed')
      } catch (err) {
        const e = err as UserMutationError
        ok = false
        if (e.status === 412) {
          staleConflict = {
            expected: e.body?.expectedVersion,
            actual: e.body?.actualVersion,
          }
        }
        const msg = e instanceof Error ? e.message : String(e)
        if (firstError === null) firstError = msg
        mark('stamdata', 'failed', msg)
        // A 412 stale on the primary row means every later If-Match is also
        // stale — stop and surface the banner so HR re-reads. (Matches the
        // UserManagement short-circuit: the catch fell through to the banner.)
        if (e.status === 412) {
          setSaving(false)
          return { ok, live, staleConflict, error: firstError }
        }
      }

      // (2) employee-profiles PUT — HR-gated, admin-strict
      // If-Match. Only attempted when (a) the actor is HR and (b) we captured a
      // profile snapshot (ETag). A non-HR actor's HR sections are hidden, but if
      // an HR PUT 403s it is recorded honestly here.
      if (input.isHr && live.profile) {
        try {
          const ptf = Number.parseFloat(input.profile.partTimeFraction)
          const parsedPtf = Number.isFinite(ptf) ? ptf : 1.0
          const positionTrimmed = input.profile.position.trim()
          const updatedProfile = await saveEmployeeProfile(live.profile.employeeId, live.profile.etag, {
            effectiveFrom: todayIsoUtc(),
            partTimeFraction: parsedPtf,
            position: positionTrimmed || null,
          })
          live = { ...live, profile: updatedProfile }
          mark('profile', 'committed')
        } catch (err) {
          const e = err as StatusError
          ok = false
          if (e.status === 412 && staleConflict === null) {
            staleConflict = {
              expected: e.body?.expectedVersion,
              actual: e.body?.actualVersion,
            }
          }
          const msg = e instanceof Error ? e.message : String(e)
          if (firstError === null) firstError = msg
          mark('profile', 'failed', msg)
        }
      }

      // (3)+(4) DOB + employment-start PUT — HR-gated, admin-strict If-Match.
      //
      // BLOCKER 1 (S76b fix-forward): the users PUT (2nd step), the DOB PUT, AND
      // the employment-start PUT all mutate the SAME `users` row → each one bumps
      // `users.version`. The DOB/employment-start versions captured at dialog-open
      // (`birthDateVersion`/`employmentStartVersion`) are STALE the moment the
      // users PUT commits, so a blind re-use would 412 the 2nd/3rd writes against
      // the REAL backend. The mock that accepted every PUT masked this.
      //
      // FIX — read-your-write version threading ACROSS the users-row sequence: the
      // users PUT ran first (step 1) and re-stamped `live.user.version` to the new
      // users.version; we thread THAT into the DOB write's If-Match, capture the
      // bumped version it returns, thread it into the employment-start write, and
      // re-stamp `live.user.version` again so a second in-session save also works.
      // A skipped write (untouched field) does NOT advance the version — the next
      // write inherits the latest committed users.version regardless.
      //
      // `live.user.version` is the AUTHORITATIVE running users.version after the
      // users PUT (committed-or-skipped). We seed the running cursor from it and
      // fall back to the dialog-open per-field capture only if the users row was
      // never fetched with a version (defensive; both come from users.version).
      let usersRowVersion: number | null =
        live.user.version ?? live.birthDateVersion ?? live.employmentStartVersion

      // (3) DOB PUT — only when HR changed it AND we have a users.version to lock.
      if (
        input.isHr &&
        input.entitlement.birthDate !== live.birthDateInitial &&
        usersRowVersion !== null
      ) {
        try {
          const savedDob = await setBirthDate(
            live.user.userId,
            input.entitlement.birthDate || null,
            usersRowVersion,
          )
          // The DOB write bumped users.version; thread it forward + re-stamp live.
          usersRowVersion = savedDob.version
          live = {
            ...live,
            birthDateInitial: savedDob.birthDate ?? '',
            birthDateVersion: savedDob.version,
            user: { ...live.user, version: savedDob.version, etag: `"${savedDob.version}"` },
          }
          mark('birthDate', 'committed')
        } catch (err) {
          ok = false
          const msg = err instanceof Error ? err.message : String(err)
          if (firstError === null) firstError = msg
          mark('birthDate', 'failed', msg)
        }
      }

      // (4) employment-start PUT — uses the LATEST users.version (post-DOB), not
      // the dialog-open capture.
      if (
        input.isHr &&
        input.entitlement.employmentStartDate !== live.employmentStartInitial &&
        usersRowVersion !== null
      ) {
        try {
          const savedStart = await setEmploymentStartDate(
            live.user.userId,
            input.entitlement.employmentStartDate || null,
            usersRowVersion,
          )
          usersRowVersion = savedStart.version
          live = {
            ...live,
            employmentStartInitial: savedStart.employmentStartDate ?? '',
            employmentStartVersion: savedStart.version,
            user: { ...live.user, version: savedStart.version, etag: `"${savedStart.version}"` },
          }
          mark('employmentStart', 'committed')
        } catch (err) {
          ok = false
          const msg = err instanceof Error ? err.message : String(err)
          if (firstError === null) firstError = msg
          mark('employmentStart', 'failed', msg)
        }
      }

      // (5) CHILD_SICK PUT — HR-gated, the DISTINCT read/create/update contract.
      // Only when HR touched the toggle. If-None-Match:* on CREATE (no live row);
      // If-Match on UPDATE; a 409 (create race) re-reads so HR can retry with
      // If-Match. Ported verbatim from UserManagement.handleEditSubmit.
      if (input.isHr && input.childSickDirty) {
        try {
          const savedElig = await setChildSick(
            live.user.userId,
            input.entitlement.childSickEligible,
            live.childSickRowExists,
            live.childSickVersion,
          )
          live = {
            ...live,
            childSickRowExists: savedElig.rowExists,
            childSickVersion: savedElig.version,
          }
          mark('childSick', 'committed')
        } catch (err) {
          ok = false
          const e = err as StatusError
          if (e.status === 409) {
            // Lost update — re-read so the toggle now carries rowExists+version,
            // and surface the message so HR can re-save with If-Match.
            try {
              const elig: ChildSickEligibilitySnapshot = await fetchChildSickEligibility(
                live.user.userId,
              )
              live = {
                ...live,
                childSickRowExists: elig.rowExists,
                childSickVersion: elig.version,
              }
            } catch {
              // Re-read failed too; the recorded message still tells HR to retry.
            }
          }
          const msg = e instanceof Error ? e.message : String(e)
          if (firstError === null) firstError = msg
          mark('childSick', 'failed', msg)
        }
      }

      setSaving(false)
      return { ok, live, staleConflict, error: firstError }
    },
    [
      updateUser,
      setBirthDate,
      setEmploymentStartDate,
      setChildSick,
      fetchChildSickEligibility,
      resetSections,
    ],
  )

  /**
   * Create a new person. `POST /api/admin/users` (LocalAdmin) creates the
   * profile with DEFAULTS (part-time=1.0 / position=null / enhed=null). A non-HR
   * actor cannot set the HR fields at create (they'd 403 on the follow-up profile
   * PUT) — the drawer hides them, so create only carries the stamdata + creds.
   *
   * S76b / TASK-7603 — the OPTIONAL `approverId` (the drawer's create-mode
   * approver picker) threads into the SAME create POST so the backend's S74 R9
   * atomic create+assign plants the PRIMARY reporting line in ONE tx (no orphan
   * window). Omitted ⇒ no reporting line is created. Throws on failure so the
   * drawer surfaces the message.
   */
  const createPerson = useCallback(
    async (body: {
      userId: string
      username: string
      password: string
      displayName: string
      email?: string
      primaryOrgId: string
      agreementCode: string
      // The backend CreateUserRequest REQUIRES OkVersion (AdminEndpoints.cs:452) —
      // the create POST 400s without it. The drawer derives it from the selected
      // org's `okVersion` (Organization carries it).
      okVersion: string
      approverId?: string
    }): Promise<WithEtag<User>> => {
      setSaving(true)
      try {
        return await createUser(body)
      } finally {
        setSaving(false)
      }
    },
    [createUser],
  )

  return {
    sections,
    saving,
    saveEdit,
    createPerson,
    fetchUser,
    resetSections,
  }
}
