import { useState, useCallback, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAgreementConfigs, useAgreementConfigActions } from '../../hooks/useAgreementConfigs'
import type { AgreementConfig, WithEtag } from '../../hooks/useAgreementConfigs'
import styles from './AgreementConfigList.module.css'

type StatusFilter = '' | 'DRAFT' | 'ACTIVE' | 'ARCHIVED'

const STATUS_TABS: { label: string; value: StatusFilter }[] = [
  { label: 'Alle', value: '' },
  { label: 'Kladde', value: 'DRAFT' },
  { label: 'Aktiv', value: 'ACTIVE' },
  { label: 'Arkiveret', value: 'ARCHIVED' },
]

function formatDate(dateStr: string): string {
  try {
    return new Date(dateStr).toLocaleDateString('da-DK')
  } catch {
    return dateStr
  }
}

function statusBadgeClass(status: string): string {
  switch (status) {
    case 'ACTIVE':
      return `${styles.badge} ${styles.badgeActive}`
    case 'ARCHIVED':
      return `${styles.badge} ${styles.badgeArchived}`
    default:
      return styles.badge
  }
}

const STATUS_LABELS: Record<string, string> = {
  DRAFT: 'Kladde',
  ACTIVE: 'Aktiv',
  ARCHIVED: 'Arkiveret',
}

export function AgreementConfigList() {
  const navigate = useNavigate()
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('')
  const { configs, loading, error, refetch } = useAgreementConfigs(statusFilter || undefined)
  const { cloneConfig } = useAgreementConfigActions()

  const [cloneDialogOpen, setCloneDialogOpen] = useState(false)
  const [cloneSource, setCloneSource] = useState<WithEtag<AgreementConfig> | null>(null)
  const [cloneAgreementCode, setCloneAgreementCode] = useState('')
  const [cloneOkVersion, setCloneOkVersion] = useState('')
  const [cloneSubmitting, setCloneSubmitting] = useState(false)
  const [cloneError, setCloneError] = useState<string | null>(null)
  // S25 / TASK-2506 (ADR-019 pending) banner-with-retry precedent
  // (mirrors ProfileEditor.tsx:135). The list page has no If-Match-bearing
  // mutations today (clone is first-create, not If-Match), so the banner state
  // is wired for surface consistency — a future list-row publish/archive
  // transition would set it on 412.
  const [staleConflict, setStaleConflict] = useState<{ expected?: number; actual?: number } | null>(null)

  const handleRowClick = useCallback((configId: string) => {
    navigate(`/admin/agreements/${configId}`)
  }, [navigate])

  const handleCloneOpen = useCallback((e: React.MouseEvent, config: WithEtag<AgreementConfig>) => {
    e.stopPropagation()
    setCloneSource(config)
    setCloneAgreementCode(config.agreementCode)
    setCloneOkVersion('')
    setCloneError(null)
    setCloneDialogOpen(true)
  }, [])

  const handleCloneClose = useCallback(() => {
    setCloneDialogOpen(false)
    setCloneSource(null)
    setCloneError(null)
  }, [])

  const handleCloneSubmit = async (e: FormEvent) => {
    e.preventDefault()
    if (!cloneSource) return
    setCloneSubmitting(true)
    setCloneError(null)
    try {
      const result = await cloneConfig(
        cloneSource.configId,
        cloneAgreementCode || undefined,
        cloneOkVersion || undefined,
      )
      handleCloneClose()
      await refetch()
      navigate(`/admin/agreements/${result.configId}`)
    } catch (err) {
      // S25 / TASK-2506 banner-with-retry pattern: 412 surfaces stale
      // expectedVersion/actualVersion via the thrown ConfigMutationError.
      const e = err as Error & { status?: number; body?: { expectedVersion?: number; actualVersion?: number } }
      if (e.status === 412) {
        setStaleConflict({ expected: e.body?.expectedVersion, actual: e.body?.actualVersion })
      } else {
        setCloneError(err instanceof Error ? err.message : String(err))
      }
    } finally {
      setCloneSubmitting(false)
    }
  }

  const handleStaleRefresh = async () => {
    setStaleConflict(null)
    await refetch()
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Overenskomster</h1>
        <button
          className={styles.createBtn}
          onClick={() => navigate('/admin/agreements/new')}
        >
          Opret ny
        </button>
      </div>

      <div className={styles.filterTabs}>
        {STATUS_TABS.map((tab) => (
          <button
            key={tab.value}
            className={`${styles.filterTab} ${statusFilter === tab.value ? styles.filterTabActive : ''}`}
            onClick={() => setStatusFilter(tab.value)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {staleConflict && (
        <div className={styles.alert} role="alert" data-testid="stale-conflict-banner">
          Din handling var baseret paa en foraeldet tilstand. Listen er blevet opdateret siden.
          {staleConflict.expected !== undefined && staleConflict.actual !== undefined && (
            <> {' '}(Forventet version {staleConflict.expected}, aktuel version {staleConflict.actual}.)</>
          )}
          {' '}
          <button type="button" className={styles.cloneBtn} onClick={handleStaleRefresh}>
            Genindlaes
          </button>
        </div>
      )}
      {error && <div className={styles.alert}>{error}</div>}

      {loading && (
        <div className={styles.spinner}>Henter overenskomster...</div>
      )}

      {!loading && !error && configs.length === 0 && (
        <div className={styles.emptyState}>Ingen overenskomster fundet</div>
      )}

      {!loading && configs.length > 0 && (
        <table className={styles.table}>
          <thead>
            <tr>
              <th>Overenskomst</th>
              <th>OK-version</th>
              <th>Status</th>
              <th>Ugentlig norm</th>
              <th>Oprettet</th>
              <th>Beskrivelse</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {configs.map((config) => (
              <tr
                key={config.configId}
                className={styles.clickableRow}
                onClick={() => handleRowClick(config.configId)}
              >
                <td>{config.agreementCode}</td>
                <td>{config.okVersion}</td>
                <td>
                  <span className={statusBadgeClass(config.status)}>
                    {STATUS_LABELS[config.status] ?? config.status}
                  </span>
                </td>
                <td>{config.weeklyNormHours} t</td>
                <td>{formatDate(config.createdAt)}</td>
                <td className={styles.descriptionCell}>
                  {config.description ?? '\u2014'}
                </td>
                <td>
                  <button
                    className={styles.cloneBtn}
                    onClick={(e) => handleCloneOpen(e, config)}
                  >
                    Klon
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {cloneDialogOpen && cloneSource && (
        <div className={styles.overlay} onClick={handleCloneClose}>
          <div className={styles.dialog} onClick={(e) => e.stopPropagation()}>
            <h2 className={styles.dialogTitle}>Klon overenskomst</h2>
            <div className={styles.dialogInfo}>
              Kilde: {cloneSource.agreementCode} / {cloneSource.okVersion} ({STATUS_LABELS[cloneSource.status] ?? cloneSource.status})
            </div>
            <form onSubmit={handleCloneSubmit}>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="cloneAgreementCode">
                  Overenskomstkode (valgfri tilsidesaettelse)
                </label>
                <input
                  className={styles.input}
                  id="cloneAgreementCode"
                  type="text"
                  value={cloneAgreementCode}
                  onChange={(e) => setCloneAgreementCode(e.target.value)}
                  placeholder="f.eks. AC"
                />
              </div>
              <div className={styles.formField}>
                <label className={styles.formLabel} htmlFor="cloneOkVersion">
                  Ny OK-version (valgfri)
                </label>
                <input
                  className={styles.input}
                  id="cloneOkVersion"
                  type="text"
                  value={cloneOkVersion}
                  onChange={(e) => setCloneOkVersion(e.target.value)}
                  placeholder="f.eks. OK26"
                />
              </div>

              {cloneError && <div className={styles.alert}>{cloneError}</div>}

              <div className={styles.dialogActions}>
                <button
                  type="button"
                  className={styles.cancelBtn}
                  onClick={handleCloneClose}
                >
                  Annuller
                </button>
                <button
                  type="submit"
                  className={styles.createBtn}
                  disabled={cloneSubmitting}
                >
                  {cloneSubmitting ? 'Kloner...' : 'Klon'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
