import { useState, useEffect, useCallback } from 'react'
import { apiClient, apiFetchWithEtag } from '../lib/api'
import { formatVersionAsIfMatch, resolveEtag } from '../lib/etag'

// TASK-3109 (Phase 4d-3 Part 1 / S31). Per-employee admin hook for the
// authoritative employee_profiles store from TASK-3101..3107. Mirrors the
// S30 useEntitlementConfig shape but adapted for the simpler endpoint pair
// (GET + PUT per employee — no list, no POST, no DELETE in S31).
//
// ADR-019 D2 admin-strict If-Match optimistic concurrency: the PUT requires
// `If-Match: "<version>"` and the response carries ETag: "<newVersion>"; on
// 412 the backend returns a structured body with `expectedVersion` /
// `actualVersion` so the page can show the banner-with-retry message
// (S25/S29/S30 precedent — see ProfileEditor.tsx and EntitlementConfigEditor.tsx).

/**
 * Employee profile DTO — matches the backend GET / PUT response shape from
 * `EmployeeProfileEndpoints.cs`. `isPartTime` is server-derived from
 * `partTimeFraction < 1.0` and is read-only on the wire.
 */
export interface EmployeeProfile {
  employeeId: string
  weeklyNormHours: number
  partTimeFraction: number
  position: string | null
  isPartTime: boolean
  version: number
}

/**
 * PUT request body — matches the backend's
 * `UpdateEmployeeProfileRequest(decimal WeeklyNormHours, decimal PartTimeFraction, string? Position)`.
 * All three fields are required by the backend record; the editor never sends a
 * partial patch (S30 cycle-1 Step 7a Codex P1 fix #2: hook's request shape
 * must match backend's required-fields shape exactly — no missing fields).
 */
export interface EmployeeProfileUpdateRequest {
  weeklyNormHours: number
  partTimeFraction: number
  position: string | null
}

/**
 * Org-scoped user list entry — used by the editor's dropdown population. The
 * shape is the subset returned by `/api/admin/organizations/{orgId}/users`
 * that the editor needs. Avoids re-importing the org-admin hook's types.
 */
export interface AdminUser {
  userId: string
  username: string
  displayName: string
  primaryOrgId?: string
  agreementCode?: string
}

export interface AdminOrganization {
  orgId: string
  orgName: string
}

export type WithEtag<T> = T & { etag: string; version: number }

export interface EmployeeProfileMutationError extends Error {
  status: number
  body?: {
    error?: string
    expectedVersion?: number
    actualVersion?: number
  }
}

function makeMutationError(
  status: number,
  errorMsg: string,
  body: EmployeeProfileMutationError['body'],
): EmployeeProfileMutationError {
  const err = new Error(errorMsg) as EmployeeProfileMutationError
  err.status = status
  err.body = body
  return err
}

/**
 * Org list — populates the first dropdown so HR can drill from org -> users.
 * Mirrors the `UserManagement.tsx` two-step selector. Re-implemented as a hook
 * (not reused from `UserManagement.tsx`) because that page uses raw fetch and
 * defines its `useOrganizations` locally.
 */
export function useAdminOrganizations() {
  const [organizations, setOrganizations] = useState<AdminOrganization[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    async function load() {
      setLoading(true)
      setError(null)
      const result = await apiClient.get<AdminOrganization[]>('/api/admin/organizations')
      if (cancelled) return
      if (result.ok) {
        setOrganizations(result.data)
      } else {
        setError(result.error)
      }
      setLoading(false)
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [])

  return { organizations, loading, error }
}

/**
 * Users-in-org list — second-level dropdown. Re-fetches when `orgId` changes.
 * Returns the raw user array without ETag — listing is GET-only and the per-row
 * version is captured by the per-profile GET that follows.
 */
export function useAdminUsersInOrg(orgId: string | null) {
  const [users, setUsers] = useState<AdminUser[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchUsers = useCallback(async () => {
    if (!orgId) {
      setUsers([])
      return
    }
    setLoading(true)
    setError(null)
    const result = await apiClient.get<AdminUser[]>(
      `/api/admin/organizations/${encodeURIComponent(orgId)}/users`,
    )
    if (result.ok) {
      setUsers(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [orgId])

  useEffect(() => {
    void fetchUsers()
  }, [fetchUsers])

  return { users, loading, error, refetch: fetchUsers }
}

/**
 * Single-employee profile GET with ETag-header capture. The editor uses this
 * the moment HR picks a user from the dropdown so the next PUT can compose
 * `If-Match` against the freshest version.
 */
export function useEmployeeProfile(employeeId: string | null) {
  const [profile, setProfile] = useState<WithEtag<EmployeeProfile> | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchProfile = useCallback(async () => {
    if (!employeeId) {
      setProfile(null)
      setError(null)
      return
    }
    setLoading(true)
    setError(null)
    const result = await apiFetchWithEtag<EmployeeProfile>(
      `/api/admin/employee-profiles/${encodeURIComponent(employeeId)}`,
    )
    if (result.ok) {
      const { data, etag } = result.data
      const { etag: resolvedEtag } = resolveEtag(etag, data)
      setProfile({
        ...data,
        etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
      })
    } else {
      setProfile(null)
      setError(result.error)
    }
    setLoading(false)
  }, [employeeId])

  useEffect(() => {
    void fetchProfile()
  }, [fetchProfile])

  return { profile, loading, error, refetch: fetchProfile }
}

/**
 * Mutation hook — separated from the read hook so a successful PUT can update
 * the local profile state without re-entering the read effect.
 *
 * The backend PUT requires the full 3-field
 * `UpdateEmployeeProfileRequest(WeeklyNormHours, PartTimeFraction, Position)`
 * (per `EmployeeProfileEndpoints.cs:306-309`). Mismatched shapes — sending
 * fewer fields — would fail JSON binding or yield a 400; sending the full
 * 3-field shape is the contract.
 */
export function useEmployeeProfileActions() {
  function withResponseEtag(
    data: EmployeeProfile,
    etag: string | null,
  ): WithEtag<EmployeeProfile> {
    const { etag: resolvedEtag } = resolveEtag(etag, data)
    return {
      ...data,
      etag: resolvedEtag ?? formatVersionAsIfMatch(data.version),
    }
  }

  const updateProfile = async (
    employeeId: string,
    ifMatch: string,
    body: EmployeeProfileUpdateRequest,
  ): Promise<WithEtag<EmployeeProfile>> => {
    const result = await apiFetchWithEtag<EmployeeProfile>(
      `/api/admin/employee-profiles/${encodeURIComponent(employeeId)}`,
      {
        method: 'PUT',
        headers: { 'If-Match': ifMatch },
        body: JSON.stringify(body),
      },
    )
    if (!result.ok) {
      throw makeMutationError(
        result.status,
        result.error,
        result.body as EmployeeProfileMutationError['body'],
      )
    }
    return withResponseEtag(result.data.data, result.data.etag)
  }

  return { updateProfile }
}
