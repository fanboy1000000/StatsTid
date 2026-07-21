import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { components } from '../lib/api-types'

// S119 / TASK-11901 (Typed API Contract retrofit Pass 6, PAT-012) — the admin
// project CRUD rides the TYPED spec-keyed forms. The hook no longer consumes
// the hand-written `types.ts Project` interface (which carried the PHANTOM
// `isActive` field — prod bug #7: no endpoint emits it, so the admin table
// rendered "Inaktiv" for every row; `types.ts Project` is skema-owned by usage
// until the Pass-7 skema drain). The rows are the GENERATED spec type.
//
// The update PUT body drops ONLY the never-bound `projectCode` key (the
// backend `UpdateProjectRequest` never had the member — the S112
// accepted-delta class); every other mutation's key set is byte-unchanged.
// The project family is UNCONDITIONED — no If-Match/If-None-Match anywhere.

/** The GENERATED spec row (list element + 201 create — ONE shared record). */
export type ProjectItem =
  components['schemas']['StatsTid.Backend.Api.Contracts.ProjectResponse']

// The spec marks `sortOrder` optional (a non-`required` C# value-type member —
// the PAT-012 request-side honest boundary), but the FE contract has always
// required it; the intersection keeps the S119 byte-unchanged request-key
// guarantee enforced at the hook boundary (Step-7a Codex W1).
export type ProjectCreateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.ProjectEndpoints.CreateProjectRequest'] & {
    sortOrder: number
  }

export type ProjectUpdateRequest =
  components['schemas']['StatsTid.Backend.Api.Endpoints.ProjectEndpoints.UpdateProjectRequest'] & {
    sortOrder: number
  }

interface UseProjectsResult {
  projects: ProjectItem[]
  loading: boolean
  error: string | null
  refetch: () => void
  createProject: (data: ProjectCreateRequest) => Promise<boolean>
  updateProject: (projectId: string, data: ProjectUpdateRequest) => Promise<boolean>
  deleteProject: (projectId: string) => Promise<boolean>
}

export function useProjects(orgId: string): UseProjectsResult {
  const [projects, setProjects] = useState<ProjectItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchProjects = useCallback(async () => {
    if (!orgId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get('/api/projects/{orgId}', {
      params: { path: { orgId } },
    })
    if (result.ok) {
      setProjects(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [orgId])

  useEffect(() => {
    fetchProjects()
  }, [fetchProjects])

  const createProject = useCallback(
    async (data: ProjectCreateRequest) => {
      const result = await apiClient.post('/api/projects/{orgId}', {
        params: { path: { orgId } },
        body: data,
      })
      if (result.ok) {
        await fetchProjects()
        return true
      }
      setError(result.error)
      return false
    },
    [orgId, fetchProjects]
  )

  const updateProject = useCallback(
    async (projectId: string, data: ProjectUpdateRequest) => {
      // The body is built explicitly so the wire carries EXACTLY the two bound
      // keys (projectName + sortOrder) — the never-bound `projectCode` key is
      // dropped here even if a caller's object carries extra members.
      const result = await apiClient.put('/api/projects/{orgId}/{projectId}', {
        params: { path: { orgId, projectId } },
        body: { projectName: data.projectName, sortOrder: data.sortOrder },
      })
      if (result.ok) {
        await fetchProjects()
        return true
      }
      setError(result.error)
      return false
    },
    [orgId, fetchProjects]
  )

  const deleteProject = useCallback(
    async (projectId: string) => {
      const result = await apiClient.delete('/api/projects/{orgId}/{projectId}', {
        params: { path: { orgId, projectId } },
      })
      if (result.ok) {
        await fetchProjects()
        return true
      }
      setError(result.error)
      return false
    },
    [orgId, fetchProjects]
  )

  return { projects, loading, error, refetch: fetchProjects, createProject, updateProject, deleteProject }
}
