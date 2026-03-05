import { useState, useCallback } from 'react'
import { useAuth } from '../../contexts/AuthContext'
import { useProjects } from '../../hooks/useProjects'
import { Card } from '../../components/ui/Card'
import { Button } from '../../components/ui/Button'
import { Input } from '../../components/ui/Input'
import { Alert } from '../../components/ui/Alert'
import { Spinner } from '../../components/ui/Spinner'
import styles from './ProjectManagement.module.css'

interface ProjectFormData {
  projectCode: string
  projectName: string
  sortOrder: number
}

const emptyForm: ProjectFormData = { projectCode: '', projectName: '', sortOrder: 0 }

export function ProjectManagement() {
  const { orgId } = useAuth()
  const { projects, loading, error, createProject, updateProject, deleteProject } = useProjects(orgId ?? '')

  const [showForm, setShowForm] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [formData, setFormData] = useState<ProjectFormData>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null)

  const handleOpenCreate = useCallback(() => {
    setEditingId(null)
    setFormData(emptyForm)
    setFormError(null)
    setShowForm(true)
  }, [])

  const handleOpenEdit = useCallback((projectId: string) => {
    const project = projects.find((p) => p.projectId === projectId)
    if (!project) return
    setEditingId(projectId)
    setFormData({
      projectCode: project.projectCode,
      projectName: project.projectName,
      sortOrder: project.sortOrder,
    })
    setFormError(null)
    setShowForm(true)
  }, [projects])

  const handleCancel = useCallback(() => {
    setShowForm(false)
    setEditingId(null)
    setFormData(emptyForm)
    setFormError(null)
  }, [])

  const handleSubmit = useCallback(async () => {
    if (!formData.projectCode.trim() || !formData.projectName.trim()) {
      setFormError('Projektkode og projektnavn er paakraevet')
      return
    }

    let success: boolean
    if (editingId) {
      success = await updateProject(editingId, formData)
    } else {
      success = await createProject(formData)
    }

    if (success) {
      handleCancel()
    } else {
      setFormError('Kunne ikke gemme projekt')
    }
  }, [editingId, formData, createProject, updateProject, handleCancel])

  const handleDelete = useCallback(
    async (projectId: string) => {
      await deleteProject(projectId)
      setConfirmDeleteId(null)
    },
    [deleteProject]
  )

  if (loading && projects.length === 0) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="lg" />
      </div>
    )
  }

  return (
    <div className={styles.page}>
      <Card
        header={
          <div className={styles.cardHeader}>
            <h2 className={styles.title}>Projektstyring</h2>
            <Button variant="primary" size="sm" onClick={handleOpenCreate}>
              Tilfoej projekt
            </Button>
          </div>
        }
      >
        {error && (
          <Alert variant="error">{error}</Alert>
        )}

        {/* Form (inline) */}
        {showForm && (
          <div className={styles.form}>
            <h3 className={styles.formTitle}>
              {editingId ? 'Rediger projekt' : 'Nyt projekt'}
            </h3>
            {formError && <Alert variant="error">{formError}</Alert>}
            <div className={styles.formFields}>
              <div className={styles.formField}>
                <label className={styles.label}>Projektkode</label>
                <Input
                  value={formData.projectCode}
                  onChange={(e) =>
                    setFormData((prev) => ({ ...prev, projectCode: e.target.value }))
                  }
                  placeholder="F.eks. PROJ-001"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.label}>Projektnavn</label>
                <Input
                  value={formData.projectName}
                  onChange={(e) =>
                    setFormData((prev) => ({ ...prev, projectName: e.target.value }))
                  }
                  placeholder="F.eks. Systemudvikling"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.label}>Sortering</label>
                <Input
                  type="number"
                  value={String(formData.sortOrder)}
                  onChange={(e) =>
                    setFormData((prev) => ({
                      ...prev,
                      sortOrder: parseInt(e.target.value, 10) || 0,
                    }))
                  }
                />
              </div>
            </div>
            <div className={styles.formActions}>
              <Button variant="primary" size="sm" onClick={handleSubmit}>
                {editingId ? 'Gem aendringer' : 'Opret'}
              </Button>
              <Button variant="ghost" size="sm" onClick={handleCancel}>
                Annuller
              </Button>
            </div>
          </div>
        )}

        {/* Table */}
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Projektkode</th>
              <th>Projektnavn</th>
              <th>Sortering</th>
              <th>Status</th>
              <th>Handlinger</th>
            </tr>
          </thead>
          <tbody>
            {projects.length === 0 ? (
              <tr>
                <td colSpan={5} className={styles.emptyRow}>
                  Ingen projekter oprettet endnu
                </td>
              </tr>
            ) : (
              projects.map((project) => (
                <tr key={project.projectId}>
                  <td>{project.projectCode}</td>
                  <td>{project.projectName}</td>
                  <td>{project.sortOrder}</td>
                  <td>{project.isActive ? 'Aktiv' : 'Inaktiv'}</td>
                  <td className={styles.actions}>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleOpenEdit(project.projectId)}
                    >
                      Rediger
                    </Button>
                    {confirmDeleteId === project.projectId ? (
                      <>
                        <Button
                          variant="danger"
                          size="sm"
                          onClick={() => handleDelete(project.projectId)}
                        >
                          Bekraeft
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setConfirmDeleteId(null)}
                        >
                          Annuller
                        </Button>
                      </>
                    ) : (
                      <Button
                        variant="danger"
                        size="sm"
                        onClick={() => setConfirmDeleteId(project.projectId)}
                      >
                        Deaktiver
                      </Button>
                    )}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </Card>
    </div>
  )
}
