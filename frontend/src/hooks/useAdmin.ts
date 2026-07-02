import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import type { components } from '../lib/api-types'
import { coerceApiResponse, type Assert, type AssertFieldsInSpec } from '../lib/apiNarrow'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// S35 TASK-3507 (Phase 4d ETag propagation). Admin user-management hooks
// migrated from raw `apiClient` to `apiFetchWithEtag<T>` per the S25 admin-
// strict If-Match contract; PUT/POST/GET now carry the row-version through
// ETag headers and the consuming page (UserManagement.tsx) renders the
// banner-with-retry shape on 412. Types are declared locally because the
// previous `import { Organization, User, RoleAssignment } from '../types'`
// pointed at non-existent exports (the project's `types.ts` does not define
// admin entities) — this fixes the pre-existing baseline compile error.
//
// S112 / TASK-11203 — the user/org/role slice call-sites switched to the TYPED
// structured forms (PAT-012): reads via the spec-keyed `apiClient.get` /
// `apiFetchWithEtag(pathKey, { method: 'GET', params })`, mutations via the
// typed `apiClient.post/put` and the typed etag overload (`ifMatch` takes the
// ready RFC 7232 string). Responses are DERIVED from the spec and re-narrowed to
// the FE-strict interfaces via `coerceApiResponse`, each guarded by an
// `AssertFieldsInSpec` drift assert. ONE read stays on the explicit-`T`
// fallback: `GET /api/admin/organizations/{orgId}/users` is still undeclared in
// the spec (`content?: never`, grandfathered) — see `fetchUsers`.

export interface Organization {
  orgId: string
  orgName: string
  orgType: string
  parentOrgId: string | null
  materializedPath?: string
  agreementCode: string
  okVersion?: string
}

// S111 / TASK-11102 — compile-time drift guard: every field `Organization` reads
// must exist in the spec's `OrgListItem` (the `GET /api/admin/organizations`
// element). A renamed/removed backend field → `tsc` error here.
export type _OrgDrift = Assert<
  AssertFieldsInSpec<Organization, 'StatsTid.Backend.Api.Contracts.OrgListItem'>
>
// S112 — the org create/rename responses serve the spec `OrganizationResponse`.
export type _OrgResponseDrift = Assert<
  AssertFieldsInSpec<Organization, 'StatsTid.Backend.Api.Contracts.OrganizationResponse'>
>

export interface User {
  userId: string
  username: string
  displayName: string
  email: string | null
  primaryOrgId: string
  agreementCode: string
  // S35 TASK-3506/3507 (ADR-019 D2 admin-strict If-Match). Required by the
  // backend's row-version concurrency token. GET returns the current version
  // as both an `ETag` header and a `version` body field; POST returns
  // `version: 1`; PUT returns the new post-update version. The frontend
  // captures it via `resolveEtag` and composes `If-Match` on the next PUT.
  version: number
}

// S112 — drift guards: `User` is read from BOTH the per-user GET
// (`UserDetailResponse`) and the create POST (`UserCreatedResponse`); every FE
// field must exist in both spec schemas.
export type _UserDetailDrift = Assert<
  AssertFieldsInSpec<User, 'StatsTid.Backend.Api.Contracts.UserDetailResponse'>
>
export type _UserCreatedDrift = Assert<
  AssertFieldsInSpec<User, 'StatsTid.Backend.Api.Contracts.UserCreatedResponse'>
>

/**
 * S112 / TASK-11203 — the honest PUT `/api/admin/users/{userId}` response shape
 * (the spec `UserUpdatedResponse`): unlike the GET/POST shapes it carries NO
 * `username`. The previous hand-written `apiFetchWithEtag<User>` claimed it did —
 * a silent lie the typed switch surfaced (consumers merge over the previous user
 * snapshot to retain `username`; see `useEditPerson.saveEdit`).
 */
export interface UserUpdated {
  userId: string
  displayName: string
  email: string | null
  primaryOrgId: string
  agreementCode: string
  version: number
}

export type _UserUpdatedDrift = Assert<
  AssertFieldsInSpec<UserUpdated, 'StatsTid.Backend.Api.Contracts.UserUpdatedResponse'>
>

// S112 / TASK-11203 — CORRECTED to the REAL wire shape (the spec
// `UserRoleAssignmentItem`). The previous hand-written interface claimed
// `userId` / `grantedAt` / `grantedBy` — fields the backend NEVER served (it
// serves `assignedAt` / `assignedBy` and no per-item `userId`), so the roles
// table rendered blank cells (the S97→S100 wrong-expectation class, caught by
// the typed switch + drift guard).
export interface RoleAssignment {
  assignmentId: string
  roleId: string
  orgId: string | null
  scopeType: string
  expiresAt: string | null
  assignedAt: string
  assignedBy: string
}

export type _RoleAssignmentDrift = Assert<
  AssertFieldsInSpec<RoleAssignment, 'StatsTid.Backend.Api.Contracts.UserRoleAssignmentItem'>
>
// The grant POST returns the spec `RoleGrantResponse`, a superset carrying the
// same field names (`assignedBy` nullable there) — guarded so the shared FE
// interface stays readable from both.
export type _RoleGrantDrift = Assert<
  AssertFieldsInSpec<RoleAssignment, 'StatsTid.Backend.Api.Contracts.RoleGrantResponse'>
>

/**
 * Standard wrapper for response entities that carry a row-version concurrency
 * token. Mirrors `useEmployeeProfile.ts` shape so consumers can compose the
 * `If-Match` header from `etag` and re-stamp local state with `version` after
 * each successful mutation.
 */
export type WithEtag<T> = T & { etag: string; version: number }

/**
 * Status-tagged error thrown by `createOrganization` (S99 Step-7a FIX 2) so the
 * merged "Organisation & medarbejdere" admin page's create dialog can branch on
 * `status` (409 -> friendly "name already exists" copy) without re-reading the
 * response. Mirrors the shape of `OrgStructureError`.
 */
export interface OrgMutationError extends Error {
  status: number
}

function makeOrgMutationError(status: number, message: string): OrgMutationError {
  // Object.assign (not an `as` cast) — this file is on the S112 no-`as` surface.
  return Object.assign(new Error(message), { status })
}

export function useOrganizations() {
  const [organizations, setOrganizations] = useState<Organization[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchOrganizations = useCallback(async () => {
    setLoading(true)
    setError(null)
    // S111 / TASK-11102 — typed via the OpenAPI path key (response type DERIVED
    // from the spec; no hand-written `T`). `result.data` is the spec `OrgListItem[]`;
    // `coerceApiResponse` re-narrows it to the FE-strict `Organization[]` (the spec
    // is all-optional). `_OrgDrift` fails `tsc` if a field the FE reads is dropped
    // from the spec (the S97→S100 drift class).
    const result = await apiClient.get('/api/admin/organizations')
    if (result.ok) {
      setOrganizations(coerceApiResponse<Organization[]>(result.data))
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => { fetchOrganizations() }, [fetchOrganizations])

  // S99 / TASK-9901 — name-only create. The S99 backend adaptation lets the
  // POST accept a NAME-ONLY body (`orgId` generated server-side; `agreementCode`
  // / `okVersion` defaulted server-side — they are vestigial, NOT a property of
  // the org tree per the handoff). `orgType` + `parentOrgId` come from the dialog
  // context (MAO = root, no parent; ORGANISATION needs a MAO parent). The old
  // explicit-orgId/agreement form still works (backward-compat) — both fields are
  // optional here.
  const createOrganization = async (body: {
    orgName: string
    orgType: string
    parentOrgId?: string | null
    orgId?: string
    agreementCode?: string
  }) => {
    // S112 — typed structured POST (201 → the spec `OrganizationResponse`,
    // re-narrowed to the FE-strict `Organization`; `_OrgResponseDrift`).
    const result = await apiClient.post('/api/admin/organizations', { body })
    // S99 Step-7a FIX 2 — carry the HTTP status so the merged-admin page's create
    // dialog's 409 branch (`dupOrMessage`) can fire (mirrors
    // useOrganizationStructure). For ORGANISATION/MAO a 409 is effectively
    // unreachable (server-generated orgId + no name-uniqueness).
    if (!result.ok) throw makeOrgMutationError(result.status, result.error)
    await fetchOrganizations()
    return coerceApiResponse<Organization>(result.data)
  }

  // S99 — name-only rename. The backend COALESCEs null agreement/ok to the
  // existing values (`AdminEndpoints.cs:254-256`), so a name-only PUT is SAFE
  // (it keeps the existing agreement/ok) — we deliberately do NOT surface or
  // re-send overenskomst/OK-version (the handoff forbids them on the org tree).
  const updateOrganization = async (orgId: string, body: { orgName: string }) => {
    const result = await apiClient.put('/api/admin/organizations/{orgId}', {
      params: { path: { orgId } },
      body,
    })
    if (!result.ok) throw new Error(result.error)
    await fetchOrganizations()
    return coerceApiResponse<Organization>(result.data)
  }

  return { organizations, loading, error, fetchOrganizations, createOrganization, updateOrganization }
}

/**
 * Structured error thrown by `updateUser` so the page can branch on `status`
 * (412 -> banner-with-retry) vs generic failure. Mirrors the shape used by
 * `useEmployeeProfile.ts` so the two admin pages share the same handler
 * skeleton.
 */
export interface UserMutationError extends Error {
  status: number
  body?: {
    error?: string
    expectedVersion?: number
    actualVersion?: number
  }
}

function makeUserMutationError(
  status: number,
  errorMsg: string,
  body: UserMutationError['body'],
): UserMutationError {
  // Object.assign (not an `as` cast) — this file is on the S112 no-`as` surface.
  return Object.assign(new Error(errorMsg), { status, body })
}

// S112 — generalized over the response entity (the PUT response is the narrower
// `UserUpdated`, the GET/POST responses are the full `User`).
function withResponseEtag<T extends { version: number }>(data: T, etag: string | null): WithEtag<T> {
  const { etag: resolvedEtag } = resolveEtag(etag, data)
  return {
    ...data,
    etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
    version: data.version,
  }
}

export function useOrgUsers(orgId: string) {
  const [users, setUsers] = useState<User[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchUsers = useCallback(async () => {
    if (!orgId) return
    setLoading(true)
    setError(null)
    // NOT switched (S112): this GET is still UNDECLARED in the OpenAPI spec
    // (`content?: never`, one of the ~130 grandfathered ops) — typing it here
    // would relocate the false-green rather than close it. It stays on the
    // explicit-`T` fallback until its backend response is spec-typed.
    const result = await apiClient.get<User[]>(`/api/admin/organizations/${orgId}/users`)
    if (result.ok) {
      setUsers(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [orgId])

  useEffect(() => { fetchUsers() }, [fetchUsers])

  /**
   * Per-user GET — captures the ETag header at fetch time so the next PUT
   * can compose `If-Match`. Mirrors `useEmployeeProfile.useEmployeeProfile`
   * (S31 TASK-3109) shape. Consumes the new S35 TASK-3506 GET endpoint
   * `/api/admin/users/{userId}` which returns the current row-version as
   * both an `ETag` header and a `version` body field.
   */
  const fetchUser = async (userId: string): Promise<WithEtag<User>> => {
    // S112 — typed etag GET (the spec `UserDetailResponse`; `_UserDetailDrift`).
    const result = await apiFetchWithEtag('/api/admin/users/{userId}', {
      method: 'GET',
      params: { path: { userId } },
    })
    if (!result.ok) throw new Error(result.error)
    const { data, etag } = result.data
    return withResponseEtag(coerceApiResponse<User>(data), etag)
  }

  /**
   * POST `/api/admin/users` (S35 TASK-3506). The backend returns
   * `ETag: "1"` for the newly-minted row plus `version: 1` in the body so
   * a follow-up edit can compose `If-Match` against version 1 without an
   * intermediate GET.
   *
   * S112 — the body is the spec `CreateUserRequest` verbatim (it REQUIRES
   * `userId` / `okVersion`, which the previous hand-written signature falsely
   * marked optional — every live caller already supplied both; the create
   * would 400 without them). Response = the spec `UserCreatedResponse`
   * (`_UserCreatedDrift`).
   */
  const createUser = async (
    body: components['schemas']['StatsTid.Backend.Api.Endpoints.AdminEndpoints.CreateUserRequest'],
  ): Promise<WithEtag<User>> => {
    const result = await apiFetchWithEtag('/api/admin/users', {
      method: 'POST',
      body,
    })
    if (!result.ok) throw new Error(result.error)
    await fetchUsers()
    const { data, etag } = result.data
    return withResponseEtag(coerceApiResponse<User>(data), etag)
  }

  /**
   * PUT `/api/admin/users/{userId}` (S35 TASK-3506). Carries
   * `If-Match: "<version>"` from the most recent GET / POST / prior PUT.
   * On 412 stale-version the backend returns a structured body with
   * `expectedVersion` / `actualVersion`; this hook bubbles the structured
   * error (status + body) so `UserManagement.tsx` can render the
   * banner-with-retry per the S25/S29/S30 precedent.
   *
   * S34 TASK-3409 (ADR-023 D8). `effectiveFrom` remains required on the wire —
   * the backend `UpdateUserRequest` DTO (TASK-3407) carries a non-nullable
   * `DateOnly EffectiveFrom` validated against `DateTime.UtcNow`. Frontend
   * stamps today (UTC) at the call site so the validator passes.
   */
  const updateUser = async (
    userId: string,
    // S109 / TASK-10902 — `unitId` is the CROSS-Organisation TRANSFER's atomic
    // landing unit (the backend `UpdateUserRequest.UnitId`, applied IFF the PUT is
    // a transfer = `primaryOrgId` changed; ignored on a non-transfer PUT). The
    // placement router (usePlacement) sets it ONLY on a transfer so the move
    // re-anchors edges + applies the unit in ONE call; a same-Organisation unit
    // change goes through `PUT /users/{id}/unit` instead (never here). Omitted ⇒
    // not serialized (the same-Org path).
    body: { effectiveFrom: string; displayName?: string; email?: string; primaryOrgId?: string; agreementCode?: string; unitId?: string | null },
    ifMatch: string,
  ): Promise<WithEtag<UserUpdated>> => {
    // S112 — typed etag PUT. NOTE the honest return type: the PUT response is
    // the spec `UserUpdatedResponse` (NO `username`) — see `UserUpdated`.
    const result = await apiFetchWithEtag('/api/admin/users/{userId}', {
      method: 'PUT',
      params: { path: { userId } },
      ifMatch,
      body,
    })
    if (!result.ok) {
      throw makeUserMutationError(
        result.status,
        result.error,
        // The 412 body is an (undeclared) error payload — re-narrowed at the
        // same trust boundary as the response coercions.
        coerceApiResponse<UserMutationError['body']>(result.body),
      )
    }
    await fetchUsers()
    const { data, etag } = result.data
    return withResponseEtag(coerceApiResponse<UserUpdated>(data), etag)
  }

  return { users, loading, error, fetchUsers, fetchUser, createUser, updateUser }
}

export function useUserRoles(userId: string) {
  const [roles, setRoles] = useState<RoleAssignment[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchRoles = useCallback(async () => {
    if (!userId) return
    setLoading(true)
    setError(null)
    // S112 — typed via the spec path key (bare `UserRoleAssignmentItem[]`;
    // `_RoleAssignmentDrift` pins the FE field-set).
    const result = await apiClient.get('/api/admin/users/{userId}/roles', {
      params: { path: { userId } },
    })
    if (result.ok) {
      setRoles(coerceApiResponse<RoleAssignment[]>(result.data))
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [userId])

  useEffect(() => { fetchRoles() }, [fetchRoles])

  const grantRole = async (body: { userId: string; roleId: string; orgId?: string; scopeType: string; expiresAt?: string }) => {
    // S112 — typed structured POST (201 → the spec `RoleGrantResponse`).
    const result = await apiClient.post('/api/admin/roles/grant', { body })
    if (!result.ok) throw new Error(result.error)
    await fetchRoles()
    return coerceApiResponse<RoleAssignment>(result.data)
  }

  const revokeRole = async (body: { userId: string; assignmentId: string }) => {
    // S112 — the spec `RevokeRoleRequest` carries ONLY { assignmentId, reason? }.
    // The previously-sent `userId` was never part of the backend contract
    // (ignored server-side) and is no longer serialized; the hook signature
    // keeps it so callers stay untouched.
    const result = await apiClient.post('/api/admin/roles/revoke', {
      body: { assignmentId: body.assignmentId },
    })
    if (!result.ok) throw new Error(result.error)
    await fetchRoles()
  }

  return { roles, loading, error, fetchRoles, grantRole, revokeRole }
}
