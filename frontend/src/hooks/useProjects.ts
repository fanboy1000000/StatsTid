import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../lib/api'
import type { Project } from '../types'

interface UseProjectsResult {
  projects: Project[]
  loading: boolean
  error: string | null
  refetch: () => void
  createProject: (data: { projectCode: string; projectName: string; sortOrder: number }) => Promise<boolean>
  updateProject: (projectId: string, data: { projectCode: string; projectName: string; sortOrder: number }) => Promise<boolean>
  deleteProject: (projectId: string) => Promise<boolean>
}

export function useProjects(orgId: string): UseProjectsResult {
  const [projects, setProjects] = useState<Project[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const fetchProjects = useCallback(async () => {
    if (!orgId) return
    setLoading(true)
    setError(null)
    const result = await apiClient.get<Project[]>(`/api/projects/${orgId}`)
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
    async (data: { projectCode: string; projectName: string; sortOrder: number }) => {
      const result = await apiClient.post<Project>(`/api/projects/${orgId}`, data)
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
    async (projectId: string, data: { projectCode: string; projectName: string; sortOrder: number }) => {
      const result = await apiClient.put<Project>(`/api/projects/${orgId}/${projectId}`, data)
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
      const result = await apiClient.delete<void>(`/api/projects/${orgId}/${projectId}`)
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
