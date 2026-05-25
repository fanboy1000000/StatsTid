import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { useOrganizations } from '../../hooks/useAdmin'
import { useReportingLines, type ReportingLineTreeEntry } from '../../hooks/useReportingLines'
import { useToast } from '../../components/ui/Toast'
import { Spinner } from '../../components/ui'
import { formatVersionAsIfMatch } from '../../lib/etag'
import styles from './ReportingLineTree.module.css'

// S48 TASK-4809. Reporting-line tree admin page following the OrgManagement.tsx
// flat-table-with-indentation pattern. Tree root selector filters to MINISTRY /
// STYRELSE org types. Depth map is computed from the tree data at render time.

interface AssignForm {
  employeeId: string
  managerId: string
  effectiveFrom: string
}

/** Build a depth map from tree entries. Managers who never appear as employees
 *  in the tree are depth 0 (root). Their direct reports are depth 1, etc. */
function buildDepthMap(entries: ReportingLineTreeEntry[]): Map<string, number> {
  const depths = new Map<string, number>()
  // Collect all employee IDs that appear as employeeId
  const employeeIds = new Set(entries.map((e) => e.employeeId))
  // Collect manager->employees adjacency
  const managerToEmployees = new Map<string, string[]>()
  for (const entry of entries) {
    const list = managerToEmployees.get(entry.managerId) ?? []
    list.push(entry.employeeId)
    managerToEmployees.set(entry.managerId, list)
  }
  // Roots: managerIds that do NOT appear as employeeIds in the tree
  const rootManagerIds = new Set<string>()
  for (const entry of entries) {
    if (!employeeIds.has(entry.managerId)) {
      rootManagerIds.add(entry.managerId)
    }
  }
  // Assign depth 0 to root managers
  for (const rootId of rootManagerIds) {
    depths.set(rootId, 0)
  }
  // BFS to assign depths
  const queue = [...rootManagerIds]
  while (queue.length > 0) {
    const current = queue.shift()!
    const currentDepth = depths.get(current) ?? 0
    const children = managerToEmployees.get(current) ?? []
    for (const childId of children) {
      if (!depths.has(childId)) {
        depths.set(childId, currentDepth + 1)
        queue.push(childId)
      }
    }
  }
  // Assign depth 0 to any employee not yet in the map (orphans)
  for (const entry of entries) {
    if (!depths.has(entry.employeeId)) {
      depths.set(entry.employeeId, 0)
    }
  }
  return depths
}

/** Sort entries so that each manager's subtree is contiguous — manager row
 *  first, then all direct reports (recursively). Root managers come first. */
function sortByTree(
  entries: ReportingLineTreeEntry[],
): ReportingLineTreeEntry[] {
  const managerToEntries = new Map<string, ReportingLineTreeEntry[]>()
  for (const entry of entries) {
    const list = managerToEntries.get(entry.managerId) ?? []
    list.push(entry)
    managerToEntries.set(entry.managerId, list)
  }
  const employeeIds = new Set(entries.map((e) => e.employeeId))
  const rootManagerIds: string[] = []
  for (const entry of entries) {
    if (!employeeIds.has(entry.managerId) && !rootManagerIds.includes(entry.managerId)) {
      rootManagerIds.push(entry.managerId)
    }
  }
  const sorted: ReportingLineTreeEntry[] = []
  const visited = new Set<string>()
  function walk(managerId: string) {
    const children = managerToEntries.get(managerId) ?? []
    for (const child of children) {
      if (visited.has(child.reportingLineId)) continue
      visited.add(child.reportingLineId)
      sorted.push(child)
      walk(child.employeeId)
    }
  }
  for (const rootId of rootManagerIds) {
    walk(rootId)
  }
  // Append any remaining entries not reachable from root managers
  for (const entry of entries) {
    if (!visited.has(entry.reportingLineId)) {
      sorted.push(entry)
    }
  }
  return sorted
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

export function ReportingLineTree() {
  const { organizations, loading: orgsLoading } = useOrganizations()
  const {
    fetchTree,
    assignManager,
    removeManager,
    removeActingManager,
  } = useReportingLines()
  const { toast } = useToast()

  const [selectedTreeRoot, setSelectedTreeRoot] = useState('')
  const [treeEntries, setTreeEntries] = useState<ReportingLineTreeEntry[]>([])
  const [treeLoading, setTreeLoading] = useState(false)
  const [treeError, setTreeError] = useState<string | null>(null)

  const [dialogOpen, setDialogOpen] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [form, setForm] = useState<AssignForm>({
    employeeId: '',
    managerId: '',
    effectiveFrom: todayIso(),
  })

  // Filter to MINISTRY/STYRELSE for tree root selection
  const treeRootOrgs = organizations.filter(
    (o) => o.orgType === 'MINISTRY' || o.orgType === 'STYRELSE',
  )

  // Default to first tree root org once loaded
  useEffect(() => {
    if (treeRootOrgs.length > 0 && !selectedTreeRoot) {
      setSelectedTreeRoot(treeRootOrgs[0].orgId)
    }
  }, [treeRootOrgs, selectedTreeRoot])

  const loadTree = useCallback(async () => {
    if (!selectedTreeRoot) return
    setTreeLoading(true)
    setTreeError(null)
    const result = await fetchTree(selectedTreeRoot)
    if (result.ok) {
      setTreeEntries(result.data)
    } else {
      setTreeError(result.error)
    }
    setTreeLoading(false)
  }, [selectedTreeRoot, fetchTree])

  useEffect(() => {
    loadTree()
  }, [loadTree])

  const depthMap = buildDepthMap(treeEntries)
  const sortedEntries = sortByTree(treeEntries)

  // Collect root managers (appear as managerId but not as employeeId)
  const employeeIds = new Set(treeEntries.map((e) => e.employeeId))
  const rootManagers = new Map<string, ReportingLineTreeEntry>()
  for (const entry of treeEntries) {
    if (!employeeIds.has(entry.managerId) && !rootManagers.has(entry.managerId)) {
      rootManagers.set(entry.managerId, entry)
    }
  }

  const handleOpenAssign = () => {
    setForm({ employeeId: '', managerId: '', effectiveFrom: todayIso() })
    setFormError(null)
    setDialogOpen(true)
  }

  const handleCloseAssign = () => {
    setDialogOpen(false)
    setFormError(null)
  }

  const handleAssignSubmit = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    setFormError(null)
    try {
      const result = await assignManager({
        employeeId: form.employeeId,
        managerId: form.managerId,
        effectiveFrom: form.effectiveFrom,
      })
      if (!result.ok) {
        setFormError(result.error)
      } else {
        toast({ title: 'Tildelt', description: 'Leder tildelt', variant: 'success' })
        handleCloseAssign()
        await loadTree()
      }
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  const handleRemove = async (entry: ReportingLineTreeEntry) => {
    const ifMatch = formatVersionAsIfMatch(entry.version)
    const result = entry.relationship === 'ACTING'
      ? await removeActingManager(entry.employeeId, ifMatch)
      : await removeManager(entry.employeeId, ifMatch)
    if (result.ok) {
      toast({ title: 'Fjernet', description: 'Ledelseslinje fjernet', variant: 'success' })
      await loadTree()
    } else {
      toast({ title: 'Fejl', description: result.error, variant: 'error' })
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Ledelseslinjer</h1>
        <button className={styles.createBtn} onClick={handleOpenAssign}>
          Tildel leder
        </button>
      </div>

      <div className={styles.treeSelector}>
        <label className={styles.treeSelectorLabel} htmlFor="treeRootSelect">
          Organisationsrod
        </label>
        {orgsLoading ? (
          <div className={styles.spinner}><Spinner size="lg" /></div>
        ) : (
          <select
            className={styles.select}
            id="treeRootSelect"
            value={selectedTreeRoot}
            onChange={(e) => setSelectedTreeRoot(e.target.value)}
            style={{ maxWidth: 400 }}
          >
            {treeRootOrgs.map((org) => (
              <option key={org.orgId} value={org.orgId}>
                {org.orgName} ({org.orgId})
              </option>
            ))}
          </select>
        )}
      </div>

      {treeError && <div className={styles.alert}>{treeError}</div>}

      {treeLoading && (
        <div className={styles.spinner}><Spinner size="lg" /></div>
      )}

      {!treeLoading && !treeError && treeEntries.length === 0 && selectedTreeRoot && (
        <div className={styles.emptyState}>
          Ingen ledelseslinjer fundet for denne organisation
        </div>
      )}

      {!treeLoading && sortedEntries.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Medarbejder</th>
              <th>Leder</th>
              <th>Forhold</th>
              <th>Gyldig fra</th>
              <th>Handlinger</th>
            </tr>
          </thead>
          <tbody>
            {/* Root manager rows (virtual — no reporting line row for these) */}
            {Array.from(rootManagers.entries()).map(([managerId, entry]) => (
              <tr key={`root-${managerId}`}>
                <td>
                  <span className={styles.indentedName}>
                    {entry.managerDisplayName}
                  </span>
                </td>
                <td>{'—'}</td>
                <td><span className={styles.badgeRoot}>Rod</span></td>
                <td>{'—'}</td>
                <td>{'—'}</td>
              </tr>
            ))}
            {/* Actual reporting line entries */}
            {sortedEntries.map((entry) => {
              const depth = depthMap.get(entry.employeeId) ?? 0
              return (
                <tr key={entry.reportingLineId}>
                  <td>
                    <span
                      className={styles.indentedName}
                      style={{ marginLeft: depth * 24 }}
                    >
                      {entry.employeeDisplayName}
                    </span>
                  </td>
                  <td>{entry.managerDisplayName}</td>
                  <td>
                    <span className={styles.badge}>
                      {entry.relationship === 'ACTING' ? 'Vikarierende' : 'Primaer'}
                    </span>
                  </td>
                  <td>{entry.effectiveFrom}</td>
                  <td>
                    <button
                      className={styles.dangerBtn}
                      onClick={() => handleRemove(entry)}
                    >
                      Fjern
                    </button>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}

      {/* Assign manager dialog */}
      {dialogOpen && (
        <div className={styles.overlay} onClick={handleCloseAssign}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Tildel leder</h2>
            <form onSubmit={handleAssignSubmit}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="assignEmployeeId">
                  Medarbejder-ID <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="assignEmployeeId"
                  type="text"
                  required
                  value={form.employeeId}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, employeeId: e.target.value }))
                  }
                  placeholder="f.eks. EMP001"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="assignManagerId">
                  Leder-ID <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="assignManagerId"
                  type="text"
                  required
                  value={form.managerId}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, managerId: e.target.value }))
                  }
                  placeholder="f.eks. EMP002"
                />
              </div>

              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="assignEffectiveFrom">
                  Gyldig fra <span className={styles.required}>*</span>
                </label>
                <input
                  className={styles.input}
                  id="assignEffectiveFrom"
                  type="date"
                  required
                  value={form.effectiveFrom}
                  onChange={(e) =>
                    setForm((f) => ({ ...f, effectiveFrom: e.target.value }))
                  }
                />
              </div>

              {formError && <div className={styles.alert}>{formError}</div>}

              <div className={styles.dialogActions}>
                <button
                  type="button"
                  className={styles.cancelBtn}
                  onClick={handleCloseAssign}
                >
                  Annuller
                </button>
                <button
                  type="submit"
                  className={styles.createBtn}
                  disabled={submitting}
                >
                  {submitting ? 'Tildeler...' : 'Tildel'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
