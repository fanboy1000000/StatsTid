// SPRINT-109 / TASK-10902 (Enhedsspor Phase 3b-2b) — the PLACEMENT routing wrapper
// for the merged-admin Person drawer. THE load-bearing part of S109.
//
// The reused `useEditPerson` only sends `primaryOrgId` (stamdata); it CANNOT, on
// its own, place a person in a unit. Both `PUT /users/{id}` (stamdata) and
// `PUT /users/{id}/unit` key If-Match on the SAME `users.version`, and the
// cross-Organisation transfer (`PUT /users/{id}` with `primaryOrgId`) ALREADY
// accepts `unitId` atomically (a cross-Org unit-assign 422s by design). So the
// routing is a precise 4-case MATRIX, not a 2-endpoint switch (both Step-0b lenses):
//
//   1. CREATE (+ Medarbejder): POST /users homes at the Organisation (NO unit) →
//      if a Placering is chosen, THEN PUT /users/{id}/unit with the create's
//      returned version (v=1). Promote (→ unit_leaders) runs AFTER the assign.
//   2. EDIT, Organisation CHANGED (± unit): ONE PUT /users/{id} carrying BOTH
//      primaryOrgId AND unitId|null (the transfer applies the unit atomically +
//      re-anchors reporting/vikar + 422-blocks a manager-with-active-reports).
//      NOT "transfer then unit-assign" (a cross-Org unit-assign 422s).
//   3. EDIT, SAME-Org Placering change: PUT /users/{id}/unit (If-Match). The
//      version-threading trap — `saveEdit` ALWAYS PUTs stamdata first (even when
//      unchanged) → bumps users.version → a follow-up unit-assign with the
//      PRE-read etag 412s EVERY time. So we thread the FINAL users.version
//      (`result.live.user.version`, AFTER ALL of saveEdit's sub-writes — DOB /
//      employment-start also bump it) into the unit-assign's If-Match.
//   4. move+promote ordering: the same-Org unit-assign STRIPS all of the user's
//      leaderships (RemoveAllLeadershipForUserAsync) → run the unit-assign FIRST,
//      then designateLeader (else the move wipes the just-added leadership).
//
// P7: every branch hits the EXISTING server endpoint (the backend re-checks the
// floor/scope/concurrency); NO new authority path. The decision is unit-tested
// (the 4 RED cases — mis-route / the 412 double-write / the leadership-wipe).

import { useCallback } from 'react'
import { useEditPerson, type EditSaveInput, type EditLiveState } from './useEditPerson'
import { useUnitMutations } from './useUnitMutations'
import type { WithEtag, User } from './useAdmin'

/** The create-mode placement intent. */
export interface CreatePlacementArgs {
  mode: 'create'
  /** The create POST body (threaded straight to `createPerson`). */
  createBody: {
    userId: string
    username: string
    password: string
    displayName: string
    email?: string
    primaryOrgId: string
    agreementCode: string
    okVersion: string
    approverId?: string
  }
  /** The chosen Placering unit (null = home directly at the Organisation). When a
      unit is chosen, the create POST is followed by PUT /users/{id}/unit (v=1). */
  targetUnitId: string | null
  /** When set, designate the new person as a leader of `targetUnitId` AFTER the
      unit-assign (the promote checkbox; only meaningful with a non-null unit). */
  designateUnitId: string | null
}

/** The edit-mode placement intent. */
export interface EditPlacementArgs {
  mode: 'edit'
  userId: string
  /** The saveEdit input (stamdata/profile/entitlement) WITHOUT `unitId` — the
      router injects `unitId` itself on the transfer branch. */
  editInput: EditSaveInput
  live: EditLiveState
  /** Did the Organisation change? → the cross-Org transfer branch (case 2). */
  orgChanged: boolean
  /** The chosen Placering unit (null = home at the Organisation). */
  targetUnitId: string | null
  /** Same-Org only: did the unit actually change vs the person's current unit? →
      whether to fire PUT /users/{id}/unit (case 3). Ignored on a transfer (the
      transfer always applies `targetUnitId`). */
  unitChanged: boolean
  /** Promote: designate the person as a leader of this unit AFTER placement
      (null = no promote). */
  designateUnitId: string | null
  /** Demote: remove the person's leadership of this unit (null = no demote). Only
      needed when NO unit change happened (a same-Org move auto-strips leadership
      server-side; a transfer re-anchors edges). */
  removeLeaderUnitId: string | null
}

export type PlacementArgs = CreatePlacementArgs | EditPlacementArgs

export interface PlacementResult {
  ok: boolean
  userId: string | null
  /** The final users.version after the whole sequence (for read-your-write). */
  version: number | null
  /** The real backend message on failure (the 422 transfer block / 412 stale /
      the invalid-placement reason), null on success. */
  error: string | null
}

export function usePlacement() {
  const { createPerson, saveEdit } = useEditPerson()
  const { assignUserUnit, designateLeader, removeLeader } = useUnitMutations()

  const savePlacement = useCallback(
    async (args: PlacementArgs): Promise<PlacementResult> => {
      // ── CASE 1 — CREATE ──────────────────────────────────────────────────────
      if (args.mode === 'create') {
        let created: WithEtag<User>
        try {
          created = await createPerson(args.createBody)
        } catch (err) {
          return { ok: false, userId: null, version: null, error: err instanceof Error ? err.message : String(err) }
        }
        let version = created.version

        // POST homes at the Organisation (no unit). A chosen Placering is applied
        // SECOND via the unit-assign with the create's returned version (v=1).
        if (args.targetUnitId !== null) {
          const assign = await assignUserUnit(created.userId, args.targetUnitId, version)
          if (!assign.ok) return { ok: false, userId: created.userId, version, error: assign.error }
          version = assign.version ?? version
        }

        // Promote LAST (a leader must already be a member of the unit — they are,
        // post-assign).
        if (args.designateUnitId !== null) {
          const promote = await designateLeader(args.designateUnitId, created.userId)
          if (!promote.ok) return { ok: false, userId: created.userId, version, error: promote.error }
        }
        return { ok: true, userId: created.userId, version, error: null }
      }

      // ── CASE 2 — EDIT, Organisation CHANGED (the cross-Org transfer) ──────────
      if (args.orgChanged) {
        // ONE PUT /users/{id} carrying primaryOrgId AND unitId|null — the transfer
        // applies the unit atomically + re-anchors edges + 422-blocks a
        // manager-with-active-reports. NEVER a follow-up unit-assign (it would 422).
        const result = await saveEdit({ ...args.editInput, unitId: args.targetUnitId }, args.live)
        if (!result.ok) {
          return {
            ok: false,
            userId: args.userId,
            version: result.live.user.version,
            error: result.error ?? 'Overflytningen mislykkedes.',
          }
        }
        let version = result.live.user.version

        // A promote/demote after the transfer (the new home unit is in the new Org).
        if (args.designateUnitId !== null) {
          const promote = await designateLeader(args.designateUnitId, args.userId)
          if (!promote.ok) return { ok: false, userId: args.userId, version, error: promote.error }
        } else if (args.removeLeaderUnitId !== null) {
          const demote = await removeLeader(args.removeLeaderUnitId, args.userId)
          if (!demote.ok) return { ok: false, userId: args.userId, version, error: demote.error }
        }
        return { ok: true, userId: args.userId, version, error: null }
      }

      // ── CASE 3 — EDIT, SAME-Organisation ─────────────────────────────────────
      // saveEdit FIRST (stamdata + HR sub-writes) → it bumps users.version. We MUST
      // thread the FINAL version into the unit-assign's If-Match (the 412 trap).
      const result = await saveEdit(args.editInput, args.live)
      if (!result.ok) {
        return {
          ok: false,
          userId: args.userId,
          version: result.live.user.version,
          error: result.error ?? 'Ændringerne kunne ikke gemmes.',
        }
      }
      let version = result.live.user.version

      if (args.unitChanged) {
        // The FINAL users.version (AFTER all of saveEdit's sub-writes), NOT the
        // pre-read etag — else this 412s every time.
        const assign = await assignUserUnit(args.userId, args.targetUnitId, version)
        if (!assign.ok) return { ok: false, userId: args.userId, version, error: assign.error }
        version = assign.version ?? version
      }

      // ── CASE 4 — move+promote ordering ───────────────────────────────────────
      // The unit-assign above STRIPS leaderships → designate AFTER it. (When no
      // unit change happened, an explicit demote may be needed; a unit change
      // already auto-stripped, so a demote is a no-op there.)
      if (args.designateUnitId !== null) {
        const promote = await designateLeader(args.designateUnitId, args.userId)
        if (!promote.ok) return { ok: false, userId: args.userId, version, error: promote.error }
      } else if (!args.unitChanged && args.removeLeaderUnitId !== null) {
        const demote = await removeLeader(args.removeLeaderUnitId, args.userId)
        if (!demote.ok) return { ok: false, userId: args.userId, version, error: demote.error }
      }
      return { ok: true, userId: args.userId, version, error: null }
    },
    [createPerson, saveEdit, assignUserUnit, designateLeader, removeLeader],
  )

  return { savePlacement }
}
