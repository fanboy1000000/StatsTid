import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { Organization, User, RoleAssignment } from '../types'

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

  const createOrganization = async (body: { orgId: string; orgName: string; orgType: string; parentOrgId: string | null; agreementCode: string }) => {
    const result = await apiClient.post<Organization>('/api/admin/organizations', body)
    if (!result.ok) throw new Error(result.error)
    await fetchOrganizations()
    return result.data
  }

  return { organizations, loading, error, fetchOrganizations, createOrganization }
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

  const createUser = async (body: { username: string; displayName: string; email?: string; primaryOrgId: string; agreementCode: string; password: string }) => {
    const result = await apiClient.post<User>('/api/admin/users', body)
    if (!result.ok) throw new Error(result.error)
    await fetchUsers()
    return result.data
  }

  const updateUser = async (userId: string, body: { displayName?: string; email?: string; primaryOrgId?: string; agreementCode?: string }) => {
    const result = await apiClient.put<User>(`/api/admin/users/${userId}`, body)
    if (!result.ok) throw new Error(result.error)
    await fetchUsers()
    return result.data
  }

  return { users, loading, error, fetchUsers, createUser, updateUser }
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
