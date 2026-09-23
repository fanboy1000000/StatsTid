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
import { useCalendarToday } from '../contexts/CalendarContext'

// S34 TASK-3409 (ADR-023 D8) introduced this call as `todayIsoUtc()`, matching the backend's
// then-UTC `same-day-only-edit` reference. S142 / TASK-14209 renamed it `todayIso()` and pointed it
// at `copenhagenDate.ts`'s Europe/Copenhagen day. S143 / TASK-14313 (Step-7a review) found that
// `todayIso()` was ITSELF one of the five remaining sites reading the DEVICE's clock for a value
// that gets STORED (this hook's own headline defect: the `effective_from` on a profile edit) — the
// zone was right, but the authority was still the browser, not the server (ADR-042). `todayIso()`
// is deleted; both its callers now read `useCalendarToday()` directly, since that hook already
// returns exactly the `YYYY-MM-DD` string `todayIso()` used to compute, just sourced from the
// server-confirmed day (`contexts/CalendarContext.tsx`) instead of a client-side `Intl` call — a
// wrapper adding nothing of its own would just be another layer of indirection:
//   - THIS file's own use (three lines below, in `saveEdit`) — the value stamped as the write's
//     `effective_from` when the caller omits one.
//   - `PersonDrawer.tsx`'s render-time default (see that file's own S143 comment) — moved in the
//     SAME commit, since it depended on this file's now-deleted export.
//
// `useCalendarToday()` is a HOOK, unlike the plain function it replaces — it MUST be called during
// render (top level of a component or another hook), never inside an event-handler callback. That
// is why `today` below is read once at the TOP of `useEditPerson()` (this hook's own render), not
// inside `saveEdit`'s body, even though `saveEdit`'s fallback is the only place that consumes it:
// `saveEdit` is a `useCallback`-wrapped function that RUNS LATER, on a user's Save click — well
// after this hook's render has finished — so it closes over the already-resolved `today` instead of
// asking the hook for it itself.
//
// This also retires the old "NO DEGRADED MODE" throw for this path: `copenhagenToday()` could throw
// if the runtime could not resolve Europe/Copenhagen (a client-side `Intl` lookup, per-call).
// `useCalendarToday()` throws for a DIFFERENT reason — a missing `CalendarContext` provider, i.e. a
// wiring bug, not a runtime fact about this browser — and by construction (`CalendarContext.tsx`'s
// own doc comment) it never happens here: `PersonDrawer` (this hook's only production caller, via
// `usePlacement`) renders only under `RequireAuth`, which does not mount its children until the
// calendar gate is `ready`.

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
  /**
   * S141 / OQ-6 (a) — set ONLY when a scheduled AGREEMENT-CODE change exists
   * AND HR is editing the agreement code today: `true` = also carry the
   * edited value into the scheduled row (the backend touches only the
   * field(s) that actually differ from today's); omitted/`false` = the
   * default "apply until the scheduled change" behaviour. Threaded into the
   * users PUT (step 1) — the agreement code is a second dated field on that
   * same request.
   */
  stamdataCarryForward?: boolean
  /** S141 / OQ-6 (a) — the PROFILE-section counterpart of the above
      (partTimeFraction / position / employmentCategory), threaded into the
      employee-profiles PUT (step 2). */
  profileCarryForward?: boolean
  /**
   * S141 / TASK-14111 — the effective date for BOTH of this save's dated
   * writes (the users PUT below, which carries the agreement-code field, and
   * the employee-profiles PUT) — the drawer's new effective-date picker,
   * threaded through as ONE date for the whole save rather than one per
   * write, because HR makes one save decision, not two. Optional so every
   * existing caller (and this file's own BLOCKER-regression test) keeps
   * working unchanged: omitted defaults to today, exactly the previous
   * hardcoded behaviour.
   */
  effectiveFrom?: string
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
  // S143 / TASK-14313 — the server-confirmed Europe/Copenhagen day (ADR-042), read ONCE per render
  // here (a hook call must happen during render, never inside `saveEdit`'s callback body below,
  // which runs later on a Save click). `saveEdit` closes over this value as its `effectiveFrom`
  // fallback — see this file's own header comment for why the old `todayIso()` wrapper is gone.
  const today = useCalendarToday()

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

      // S141 / TASK-14111 — ONE effective date for the whole save (both
      // dated writes below use this SAME value): the drawer's effective-date
      // picker, or today when the caller omits it (every pre-S141 caller,
      // and the default the picker itself opens on). S143 / TASK-14313: `today`
      // is the render-time `useCalendarToday()` read captured above, not a
      // fresh clock read at save time — see this file's header comment.
      const effectiveFrom = input.effectiveFrom ?? today

      // (1) users PUT — admin-strict If-Match.
      try {
        const updated = await updateUser(
          live.user.userId,
          {
            effectiveFrom,
            displayName: input.stamdata.displayName,
            email: input.stamdata.email || undefined,
            primaryOrgId: input.stamdata.primaryOrgId,
            agreementCode: input.stamdata.agreementCode,
            // S109 / TASK-10902 — thread the transfer's landing unit ONLY when the
            // caller supplied it (a cross-Organisation transfer); omitted on a
            // same-Organisation save (the unit is changed via PUT /users/{id}/unit).
            ...(input.unitId !== undefined ? { unitId: input.unitId } : {}),
            // S141 / OQ-6 (a) — only sent when the drawer detected a scheduled
            // agreement-code change (see the field's own doc comment).
            ...(input.stamdataCarryForward !== undefined
              ? { carryForwardToScheduledChange: input.stamdataCarryForward }
              : {}),
          },
          live.user.etag,
        )
        // S112 / TASK-11203 — the PUT response is the spec `UserUpdatedResponse`
        // (NO `username`), so MERGE over the previous snapshot rather than
        // replace it. (Previously the hand-written type claimed `username` came
        // back and the replace silently dropped it from live state at runtime.)
        live = { ...live, user: { ...live.user, ...updated } }
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
      //
      // S141 / OQ-3 (a) — BLOCKER FIX (Step-0b; the third specification, after
      // two wrong ones — see SPRINT-141.md for the full history of why the
      // first two were wrong). The profile row's concurrency token is no
      // longer its own `ep.version`: it is now `users.version`, the ONE
      // aggregate token for the whole employee record (the same choice
      // `user_agreement_codes` already made — see the running-cursor comment
      // below at (3)+(4)). Step 1 (the users PUT, just above) bumps
      // `users.version` UNCONDITIONALLY, even when nothing in it changed. So:
      //   - the If-Match sent here MUST be the POST-STEP-1 token
      //     (`live.user.etag`), never the ETag captured at dialog-open
      //     (`live.profile.etag` — that number is now ORPHANED: nothing
      //     compares against it any more, and re-using it 412s every time).
      //   - on success this PUT bumps `users.version` AGAIN (S141: every
      //     write to the employee's token bumps it, even a no-op edit, or the
      //     token would not detect anything) — so the response's `version` /
      //     ETag must be re-stamped onto BOTH `live.user` (version + etag) AND
      //     `live.profile`. Re-stamping only `live.profile.etag` — the
      //     obvious move, and what this code did before — leaves the DOB /
      //     employment-start writes below (which share the SAME running
      //     cursor) and `usePlacement`'s post-save unit-assign
      //     (`usePlacement.ts` — reads `live.user.version` AFTER this whole
      //     sequence) holding a superseded number: every one of THOSE writes
      //     would then 412 instead, which looks like a different bug rather
      //     than the same one moved one step later.
      if (input.isHr && live.profile) {
        try {
          const ptf = Number.parseFloat(input.profile.partTimeFraction)
          const parsedPtf = Number.isFinite(ptf) ? ptf : 1.0
          const positionTrimmed = input.profile.position.trim()
          const updatedProfile = await saveEmployeeProfile(live.profile.employeeId, live.user.etag, {
            effectiveFrom,
            partTimeFraction: parsedPtf,
            position: positionTrimmed || null,
            // S141 / OQ-6 (a) — only sent when the drawer detected a
            // scheduled profile change (see the field's own doc comment).
            ...(input.profileCarryForward !== undefined
              ? { carryForwardToScheduledChange: input.profileCarryForward }
              : {}),
          })
          live = {
            ...live,
            profile: updatedProfile,
            user: { ...live.user, version: updatedProfile.version, etag: updatedProfile.etag },
          }
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
      // BLOCKER 1 (S76b fix-forward, joined by the profile PUT at S141 / OQ-3(a)):
      // the users PUT (step 1), the employee-profiles PUT (step 2, above), the DOB
      // PUT, AND the employment-start PUT ALL share ONE running concurrency token —
      // `users.version`, the employee's single aggregate token — and each write
      // bumps it. The DOB/employment-start versions captured at dialog-open
      // (`birthDateVersion`/`employmentStartVersion`) are STALE the moment ANY
      // earlier write in this sequence commits, so a blind re-use would 412 a later
      // write against the REAL backend. The mock that accepted every PUT masked
      // this originally; S141 re-broke it by adding a SECOND writer (the profile
      // PUT) to the same token without re-threading it — see that step's comment.
      //
      // FIX — read-your-write version threading ACROSS the ENTIRE users-row
      // sequence, one running cursor for all four writes: each step re-stamps
      // `live.user.version`/`etag` from its own response before the next step
      // reads it, so every write always sends the token the PREVIOUS write in
      // THIS save actually produced. A skipped write (untouched field) does NOT
      // advance the version — the next write inherits the latest committed
      // users.version regardless.
      //
      // `live.user.version` is the AUTHORITATIVE running users.version after
      // steps (1) and (2) (committed-or-skipped). We seed the running cursor
      // from it and fall back to the dialog-open per-field capture only if the
      // users row was never fetched with a version (defensive; both come from
      // users.version).
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
      today,
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
      // SPRINT-140 / TASK-14007 (HRP-016) — optional hire date, forwarded
      // verbatim to the spec `CreateUserRequest.employmentStartDate`.
      employmentStartDate?: string
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
