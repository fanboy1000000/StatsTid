// S76b / TASK-7602 — shared types for the unified EditPersonDrawer + its
// per-section field modules and the save-orchestration hook (useEditPerson).
//
// The drawer is built as a SHELL + per-section modules (Codex/Reviewer W: do
// NOT recreate the UserManagement god-component). These types are the contract
// between the shell, the sections, and the hook.

import type { Organization } from '../../../hooks/useAdmin'

/** Agreement codes offered in the stamdata section (mirrors UserManagement). */
export const AGREEMENT_CODES = ['AC', 'HK', 'PROSA'] as const

/**
 * The actor's coarse capability for the HR-gated sections (Profile +
 * Entitlement). OQ-5b: a non-HR LocalAdmin sees neither the HR sections nor the
 * HR create fields; an honest 403 from any HR PUT still surfaces per-section.
 */
export interface ActorCapabilities {
  /** HR-capable — may read/write the employee-profile + entitlement fields. */
  isHr: boolean
}

/**
 * Whether an actor role is HR-capable for the employee-profile + entitlement
 * endpoints = **LocalHR and above** (the `HROrAbove` policy set). The role ladder
 * ranks LocalAdmin (level 2) ABOVE LocalHR (level 3) and `IsAtLeast(actual, req)
 * = level(actual) <= level(req)`, so `IsAtLeast(LocalAdmin, LocalHR)` is TRUE —
 * a LocalAdmin's scope DOES satisfy the S76 B1 LocalHR floor and a LocalAdmin
 * CAN edit the HR fields (for persons in its scope). The only DENY is the
 * per-target OUT-OF-SCOPE / mixed-role case, which the server enforces with an
 * honest 403 (OQ-5b) — NOT a reason to hide the HR sections from every admin.
 * Mirrors `hasMinRole(role, 'LocalHR')` (HR-or-above) used by UserManagement.
 */
const HR_CAPABLE_ROLES = new Set(['GlobalAdmin', 'LocalAdmin', 'LocalHR'])

export function isHrCapable(role: string | null): boolean {
  return role !== null && HR_CAPABLE_ROLES.has(role)
}

/** The stamdata (users-row) form fields — always editable (LocalAdmin floor). */
export interface StamdataFields {
  displayName: string
  email: string
  primaryOrgId: string
  agreementCode: string
}

/** The employee-profile form fields — HR-gated. */
export interface ProfileFields {
  // String-typed for the controlled number input; parsed at save (parseFloat
  // discipline per S31 EmployeeProfileEditor).
  partTimeFraction: string
  position: string
  // S74 enhed display label (free text; '' → null on the wire).
  enhedLabel: string
}

/** The per-employee entitlement form fields — HR-gated. */
export interface EntitlementFields {
  // ISO yyyy-MM-dd strings ('' = none).
  birthDate: string
  employmentStartDate: string
  childSickEligible: boolean
}

/** Create-only credentials (the backend REQUIRES username+password at create). */
export interface CreateCredentials {
  userId: string
  username: string
  password: string
}

/**
 * Per-section save outcome. `idle` = not attempted (e.g. unchanged, or skipped
 * because no row/version was captured); `committed` = the PUT succeeded;
 * `failed` = the PUT failed (the message is the honest Danish/server text). The
 * orchestrator stamps these AFTER each independent PUT so a later failure leaves
 * the earlier commits visible (partial-failure honesty — NOT a silent half-save).
 */
export type SectionSaveStatus = 'idle' | 'committed' | 'failed'

export interface SectionSaveState {
  status: SectionSaveStatus
  message?: string
}

/** The five independently-saved sections (stamdata = the users PUT). */
export type SaveSectionKey =
  | 'stamdata'
  | 'profile'
  | 'birthDate'
  | 'employmentStart'
  | 'childSick'

export type SectionSaveMap = Record<SaveSectionKey, SectionSaveState>

export const INITIAL_SECTION_SAVE: SectionSaveState = { status: 'idle' }

export function makeInitialSectionSaveMap(): SectionSaveMap {
  return {
    stamdata: { ...INITIAL_SECTION_SAVE },
    profile: { ...INITIAL_SECTION_SAVE },
    birthDate: { ...INITIAL_SECTION_SAVE },
    employmentStart: { ...INITIAL_SECTION_SAVE },
    childSick: { ...INITIAL_SECTION_SAVE },
  }
}

export type { Organization }
