import { useState, useEffect, useMemo, useCallback } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { apiClient } from '../lib/api'
import { Dialog } from './ui/Dialog'
import { Button } from './ui/Button'
import { Spinner } from './ui/Spinner'
import styles from './ProjectPicker.module.css'

interface AvailableProject {
  projectId: string
  projectCode: string
  projectName: string
  sortOrder: number
  selected: boolean
}

interface ProjectPickerProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  onSelectionChanged: () => void
}

export function ProjectPicker({ open, onOpenChange, onSelectionChanged }: ProjectPickerProps) {
  const { orgId } = useAuth()
  const [projects, setProjects] = useState<AvailableProject[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [togglingIds, setTogglingIds] = useState<Set<string>>(new Set())
  const [dirty, setDirty] = useState(false)

  // Fetch available projects when dialog opens
  useEffect(() => {
    if (!open || !orgId) return

    let cancelled = false
    setLoading(true)
    setError(null)

    apiClient.get<AvailableProject[]>(`/api/projects/${orgId}/available`).then((result) => {
      if (cancelled) return
      setLoading(false)
      if (result.ok) {
        setProjects(result.data)
      } else {
        setError(result.error)
      }
    })

    return () => { cancelled = true }
  }, [open, orgId])

  // Reset state when dialog closes
  useEffect(() => {
    if (!open) {
      setSearch('')
      setDirty(false)
    }
  }, [open])

  // Filter projects by search term (client-side)
  const filteredProjects = useMemo(() => {
    if (!search.trim()) return projects
    const term = search.trim().toLowerCase()
    return projects.filter(
      (p) =>
        p.projectName.toLowerCase().includes(term) ||
        p.projectCode.toLowerCase().includes(term)
    )
  }, [projects, search])

  const selectedCount = useMemo(
    () => projects.filter((p) => p.selected).length,
    [projects]
  )

  const handleToggle = useCallback(
    async (project: AvailableProject) => {
      if (!orgId) return

      setTogglingIds((prev) => new Set(prev).add(project.projectId))

      const wasSelected = project.selected
      const result = wasSelected
        ? await apiClient.delete(`/api/projects/${orgId}/select/${project.projectId}`)
        : await apiClient.post(`/api/projects/${orgId}/select/${project.projectId}`)

      setTogglingIds((prev) => {
        const next = new Set(prev)
        next.delete(project.projectId)
        return next
      })

      if (result.ok) {
        setProjects((prev) =>
          prev.map((p) =>
            p.projectId === project.projectId
              ? { ...p, selected: !wasSelected }
              : p
          )
        )
        setDirty(true)
      }
    },
    [orgId]
  )

  const handleClose = useCallback(
    (nextOpen: boolean) => {
      if (!nextOpen && dirty) {
        onSelectionChanged()
      }
      onOpenChange(nextOpen)
    },
    [dirty, onOpenChange, onSelectionChanged]
  )

  return (
    <Dialog
      open={open}
      onOpenChange={handleClose}
      title="Administrer projekter"
      description="Vaelg hvilke projekter der skal vises i dit skema."
    >
      {loading ? (
        <div className={styles.loadingContainer}>
          <Spinner size="md" />
          <span>Indlaeser projekter...</span>
        </div>
      ) : error ? (
        <p className={styles.errorText}>Kunne ikke indlaese projekter: {error}</p>
      ) : (
        <>
          <input
            type="text"
            className={styles.searchInput}
            placeholder="Soeg projekt..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            autoFocus
          />

          <p className={styles.selectedCount}>
            Valgte projekter: {selectedCount} af {projects.length}
          </p>

          {filteredProjects.length === 0 ? (
            <p className={styles.emptyText}>
              {search.trim()
                ? 'Ingen projekter matcher din soegning.'
                : 'Ingen projekter tilgaengelige.'}
            </p>
          ) : (
            <ul className={styles.projectList} role="list">
              {filteredProjects.map((project) => {
                const isToggling = togglingIds.has(project.projectId)
                return (
                  <li
                    key={project.projectId}
                    className={`${styles.projectItem} ${isToggling ? styles.togglingItem : ''}`}
                  >
                    <input
                      type="checkbox"
                      id={`project-${project.projectId}`}
                      checked={project.selected}
                      disabled={isToggling}
                      onChange={() => handleToggle(project)}
                      aria-label={`${project.selected ? 'Fjern' : 'Tilfoej'} ${project.projectName}`}
                    />
                    <span className={styles.projectCode}>{project.projectCode}</span>
                    <span className={styles.projectName}>{project.projectName}</span>
                  </li>
                )
              })}
            </ul>
          )}

          <div className={styles.footer}>
            <Button variant="primary" size="sm" onClick={() => handleClose(false)}>
              Gem og luk
            </Button>
          </div>
        </>
      )}
    </Dialog>
  )
}
