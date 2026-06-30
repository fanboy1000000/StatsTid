import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// S35 TASK-3507 (Phase 4d ETag propagation). Admin user-management hooks
// migrated from raw `apiClient` to `apiFetchWithEtag<T>` per the S25 admin-
// strict If-Match contract; PUT/POST/GET now carry the row-version through
// ETag headers and the consuming page (UserManagement.tsx) renders the
// banner-with-retry shape on 412. Types are declared locally because the
// previous `import { Organization, User, RoleAssignment } from '../types'`
// pointed at non-existent exports (the project's `types.ts` does not define
// admin entities) — this fixes the pre-existing baseline compile error.

export interface Organization {
  orgId: string
  orgName: string
  orgType: string
  parentOrgId: string | null
  materializedPath?: string
  agreementCode: string
  okVersion?: string
}

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

export interface RoleAssignment {
  assignmentId: string
  userId: string
  roleId: string
  orgId: string | null
  scopeType: string
  expiresAt: string | null
  grantedAt: string
  grantedBy: string
}

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
  const err = new Error(message) as OrgMutationError
  err.status = status
  return err
}

export function useOrganizations() {
  const [organizations, setOrganizations] = useState<Organization[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchOrganizations = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await apiClient.get<Organization[]>('/api/admin/organizations')
    if (result.ok) {
      setOrganizations(result.data)
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
    const result = await apiClient.post<Organization>('/api/admin/organizations', body)
    // S99 Step-7a FIX 2 — carry the HTTP status so the merged-admin page's create
    // dialog's 409 branch (`dupOrMessage`) can fire (mirrors
    // useOrganizationStructure). For ORGANISATION/MAO a 409 is effectively
    // unreachable (server-generated orgId + no name-uniqueness).
    if (!result.ok) throw makeOrgMutationError(result.status, result.error)
    await fetchOrganizations()
    return result.data
  }

  // S99 — name-only rename. The backend COALESCEs null agreement/ok to the
  // existing values (`AdminEndpoints.cs:254-256`), so a name-only PUT is SAFE
  // (it keeps the existing agreement/ok) — we deliberately do NOT surface or
  // re-send overenskomst/OK-version (the handoff forbids them on the org tree).
  const updateOrganization = async (orgId: string, body: { orgName: string }) => {
    const result = await apiClient.put<Organization>(
      `/api/admin/organizations/${encodeURIComponent(orgId)}`,
      body,
    )
    if (!result.ok) throw new Error(result.error)
    await fetchOrganizations()
    return result.data
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
  const err = new Error(errorMsg) as UserMutationError
  err.status = status
  err.body = body
  return err
}

function withResponseEtag(data: User, etag: string | null): WithEtag<User> {
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
    const result = await apiFetchWithEtag<User>(
      `/api/admin/users/${encodeURIComponent(userId)}`,
    )
    if (!result.ok) throw new Error(result.error)
    const { data, etag } = result.data
    return withResponseEtag(data, etag)
  }

  /**
   * POST `/api/admin/users` (S35 TASK-3506). The backend returns
   * `ETag: "1"` for the newly-minted row plus `version: 1` in the body so
   * a follow-up edit can compose `If-Match` against version 1 without an
   * intermediate GET.
   */
  const createUser = async (body: { userId?: string; username: string; displayName: string; email?: string; primaryOrgId: string; agreementCode: string; okVersion?: string; password: string; approverId?: string }): Promise<WithEtag<User>> => {
    const result = await apiFetchWithEtag<User>('/api/admin/users', {
      method: 'POST',
      body: JSON.stringify(body),
    })
    if (!result.ok) throw new Error(result.error)
    await fetchUsers()
    const { data, etag } = result.data
    return withResponseEtag(data, etag)
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
  ): Promise<WithEtag<User>> => {
    const result = await apiFetchWithEtag<User>(
      `/api/admin/users/${encodeURIComponent(userId)}`,
      {
        method: 'PUT',
        body: JSON.stringify(body),
        headers: { 'If-Match': ifMatch },
      },
    )
    if (!result.ok) {
      throw makeUserMutationError(
        result.status,
        result.error,
        result.body as UserMutationError['body'],
      )
    }
    await fetchUsers()
    const { data, etag } = result.data
    return withResponseEtag(data, etag)
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
    const result = await apiClient.get<RoleAssignment[]>(`/api/admin/users/${userId}/roles`)
    if (result.ok) {
      setRoles(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [userId])

  useEffect(() => { fetchRoles() }, [fetchRoles])

  const grantRole = async (body: { userId: string; roleId: string; orgId?: string; scopeType: string; expiresAt?: string }) => {
    const result = await apiClient.post<RoleAssignment>('/api/admin/roles/grant', body)
    if (!result.ok) throw new Error(result.error)
    await fetchRoles()
    return result.data
  }

  const revokeRole = async (body: { userId: string; assignmentId: string }) => {
    const result = await apiClient.post<void>('/api/admin/roles/revoke', body)
    if (!result.ok) throw new Error(result.error)
    await fetchRoles()
  }

  return { roles, loading, error, fetchRoles, grantRole, revokeRole }
}
