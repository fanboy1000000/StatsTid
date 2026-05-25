import { useCallback } from 'react'
import { apiClient, apiFetchWithEtag, type ApiResult } from '../lib/api'

// S48 TASK-4808. Reporting-line admin hooks following the useAdmin.ts pattern.
// Reads go through apiClient; writes that carry If-Match / return ETag use
// apiFetchWithEtag. Types declared locally (same convention as useAdmin.ts).

export interface ReportingLineEntry {
  reportingLineId: string
  employeeId: string
  managerId: string
  treeRootOrgId: string
  relationship: string  // 'PRIMARY' | 'ACTING'
  effectiveFrom: string
  effectiveTo: string | null
  source: string
  version: number
  createdBy: string
  createdAt: string
}

export interface ReportingLineTreeEntry extends ReportingLineEntry {
  employeeDisplayName: string
  managerDisplayName: string
}

export interface DirectReport extends ReportingLineEntry {
  employeeDisplayName: string
}

export function useReportingLines() {
  const fetchTree = useCallback(
    async (treeRootOrgId: string): Promise<ApiResult<ReportingLineTreeEntry[]>> => {
      return apiClient.get<ReportingLineTreeEntry[]>(
        `/api/admin/reporting-lines/tree/${encodeURIComponent(treeRootOrgId)}`,
      )
    },
    [],
  )

  const fetchEmployeeLines = useCallback(
    async (
      employeeId: string,
    ): Promise<ApiResult<{ active: ReportingLineEntry[]; history: ReportingLineEntry[] }>> => {
      return apiClient.get<{ active: ReportingLineEntry[]; history: ReportingLineEntry[] }>(
        `/api/admin/reporting-lines/${encodeURIComponent(employeeId)}`,
      )
    },
    [],
  )

  const fetchDirectReports = useCallback(
    async (managerId: string): Promise<ApiResult<DirectReport[]>> => {
      return apiClient.get<DirectReport[]>(
        `/api/admin/reporting-lines/${encodeURIComponent(managerId)}/reports`,
      )
    },
    [],
  )

  const assignManager = useCallback(
    async (
      body: { employeeId: string; managerId: string; effectiveFrom: string },
      ifMatch?: string,
    ): Promise<ApiResult<ReportingLineEntry>> => {
      const headers: Record<string, string> = {}
      if (ifMatch) {
        headers['If-Match'] = ifMatch
      } else {
        headers['If-None-Match'] = '*'
      }
      const result = await apiFetchWithEtag<ReportingLineEntry>(
        '/api/admin/reporting-lines',
        {
          method: 'POST',
          body: JSON.stringify(body),
          headers,
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: result.data.data }
    },
    [],
  )

  const removeManager = useCallback(
    async (
      employeeId: string,
      ifMatch: string,
    ): Promise<ApiResult<void>> => {
      const result = await apiFetchWithEtag<void>(
        `/api/admin/reporting-lines/${encodeURIComponent(employeeId)}`,
        {
          method: 'DELETE',
          headers: { 'If-Match': ifMatch },
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: undefined }
    },
    [],
  )

  const assignActingManager = useCallback(
    async (
      employeeId: string,
      body: { managerId: string; effectiveFrom: string },
    ): Promise<ApiResult<ReportingLineEntry>> => {
      const result = await apiFetchWithEtag<ReportingLineEntry>(
        `/api/admin/reporting-lines/${encodeURIComponent(employeeId)}/acting`,
        {
          method: 'POST',
          body: JSON.stringify(body),
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: result.data.data }
    },
    [],
  )

  const removeActingManager = useCallback(
    async (
      employeeId: string,
      ifMatch: string,
    ): Promise<ApiResult<void>> => {
      const result = await apiFetchWithEtag<void>(
        `/api/admin/reporting-lines/${encodeURIComponent(employeeId)}/acting`,
        {
          method: 'DELETE',
          headers: { 'If-Match': ifMatch },
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: undefined }
    },
    [],
  )

  const fetchTreeSettings = useCallback(
    async (treeRootOrgId: string): Promise<ApiResult<{ enforcementMode: string; version: number }>> => {
      return apiClient.get<{ enforcementMode: string; version: number }>(
        `/api/admin/reporting-lines/tree/${encodeURIComponent(treeRootOrgId)}/settings`,
      )
    },
    [],
  )

  const updateTreeSettings = useCallback(
    async (
      treeRootOrgId: string,
      body: { enforcementMode: string },
      ifMatch: string,
    ): Promise<ApiResult<{ enforcementMode: string; version: number }>> => {
      const result = await apiFetchWithEtag<{ enforcementMode: string; version: number }>(
        `/api/admin/reporting-lines/tree/${encodeURIComponent(treeRootOrgId)}/settings`,
        {
          method: 'PUT',
          body: JSON.stringify(body),
          headers: { 'If-Match': ifMatch },
        },
      )
      if (!result.ok) {
        return { ok: false, error: result.error, status: result.status, body: result.body }
      }
      return { ok: true, data: result.data.data }
    },
    [],
  )

  const importLines = useCallback(
    async (body: {
      treeRootOrgId: string
      rows: { employeeId: string; managerId: string; effectiveFrom: string }[]
    }): Promise<ApiResult<{ imported: number; superseded: number; skipped: number; total: number }>> => {
      return apiClient.post<{ imported: number; superseded: number; skipped: number; total: number }>(
        '/api/admin/reporting-lines/import',
        body,
      )
    },
    [],
  )

  return {
    fetchTree,
    fetchEmployeeLines,
    fetchDirectReports,
    assignManager,
    removeManager,
    assignActingManager,
    removeActingManager,
    importLines,
    fetchTreeSettings,
    updateTreeSettings,
  }
}
