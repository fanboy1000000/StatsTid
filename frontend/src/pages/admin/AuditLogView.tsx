import { useState, useEffect, useCallback } from 'react'
import { apiClient } from '../../lib/api'
import styles from './AuditLogView.module.css'

interface AuditRow {
  projectionId: string
  eventId: string
  eventType: string
  visibilityScope: string
  targetOrgId: string | null
  targetResourceId: string | null
  actorId: string | null
  actorPrimaryOrgId: string | null
  occurredAt: string
  correlationId: string | null
  details: string
  projectedAt: string
}

interface AuditResponse {
  rows: AuditRow[]
  totalCount: number
  page: number
  pageSize: number
}

const PAGE_SIZE = 50

function formatTimestamp(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString('da-DK', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  } catch {
    return iso
  }
}

export function AuditLogView() {
  const [rows, setRows] = useState<AuditRow[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Filter state
  const [eventTypes, setEventTypes] = useState('')
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [actorId, setActorId] = useState('')
  const [targetOrgId, setTargetOrgId] = useState('')

  // Expanded details rows
  const [expandedRows, setExpandedRows] = useState<Set<string>>(new Set())

  const fetchAudit = useCallback(
    async (p: number) => {
      setLoading(true)
      setError(null)
      try {
        const params = new URLSearchParams()
        params.set('page', String(p))
        params.set('pageSize', String(PAGE_SIZE))
        if (eventTypes.trim()) params.set('eventTypes', eventTypes.trim())
        if (dateFrom) params.set('from', dateFrom)
        if (dateTo) params.set('to', dateTo)
        if (actorId.trim()) params.set('actorId', actorId.trim())
        if (targetOrgId.trim()) params.set('targetOrgId', targetOrgId.trim())

        const result = await apiClient.get<AuditResponse>(
          `/api/admin/audit?${params.toString()}`
        )
        if (!result.ok) {
          throw new Error(result.error)
        }
        setRows(result.data.rows)
        setTotalCount(result.data.totalCount)
        setPage(result.data.page)
      } catch (err) {
        setError(err instanceof Error ? err.message : String(err))
      } finally {
        setLoading(false)
      }
    },
    [eventTypes, dateFrom, dateTo, actorId, targetOrgId]
  )

  useEffect(() => {
    void fetchAudit(1)
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const handleSearch = () => {
    setExpandedRows(new Set())
    void fetchAudit(1)
  }

  const handlePrev = () => {
    if (page > 1) {
      setExpandedRows(new Set())
      void fetchAudit(page - 1)
    }
  }

  const handleNext = () => {
    const totalPages = Math.ceil(totalCount / PAGE_SIZE)
    if (page < totalPages) {
      setExpandedRows(new Set())
      void fetchAudit(page + 1)
    }
  }

  const toggleExpanded = (projectionId: string) => {
    setExpandedRows((prev) => {
      const next = new Set(prev)
      if (next.has(projectionId)) {
        next.delete(projectionId)
      } else {
        next.add(projectionId)
      }
      return next
    })
  }

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Auditlog</h1>
      </div>

      <div className={styles.filterBar}>
        <div className={styles.filterField}>
          <label className={styles.filterLabel} htmlFor="eventTypes">
            Hændelsestype
          </label>
          <input
            className={styles.filterInput}
            id="eventTypes"
            type="text"
            placeholder="f.eks. TimeRegistered,AbsenceCreated"
            value={eventTypes}
            onChange={(e) => setEventTypes(e.target.value)}
          />
        </div>
        <div className={styles.filterField}>
          <label className={styles.filterLabel} htmlFor="dateFrom">
            Fra dato
          </label>
          <input
            className={styles.filterInput}
            id="dateFrom"
            type="date"
            value={dateFrom}
            onChange={(e) => setDateFrom(e.target.value)}
          />
        </div>
        <div className={styles.filterField}>
          <label className={styles.filterLabel} htmlFor="dateTo">
            Til dato
          </label>
          <input
            className={styles.filterInput}
            id="dateTo"
            type="date"
            value={dateTo}
            onChange={(e) => setDateTo(e.target.value)}
          />
        </div>
        <div className={styles.filterField}>
          <label className={styles.filterLabel} htmlFor="actorId">
            Aktør-ID
          </label>
          <input
            className={styles.filterInput}
            id="actorId"
            type="text"
            placeholder="Bruger-ID"
            value={actorId}
            onChange={(e) => setActorId(e.target.value)}
          />
        </div>
        <div className={styles.filterField}>
          <label className={styles.filterLabel} htmlFor="targetOrgId">
            Mål-org-ID
          </label>
          <input
            className={styles.filterInput}
            id="targetOrgId"
            type="text"
            placeholder="Organisations-ID"
            value={targetOrgId}
            onChange={(e) => setTargetOrgId(e.target.value)}
          />
        </div>
        <button
          className={styles.filterBtn}
          onClick={handleSearch}
          disabled={loading}
        >
          {loading ? 'Søger...' : 'Søg'}
        </button>
      </div>

      {error && <div className={styles.alert}>{error}</div>}

      {loading && <div className={styles.spinner}>Henter auditlog...</div>}

      {!loading && !error && rows.length === 0 && (
        <div className={styles.emptyState}>Ingen rækker fundet</div>
      )}

      {!loading && rows.length > 0 && (
        <>
          <table className={styles.table}>
            <thead>
              <tr>
                <th>Tidspunkt</th>
                <th>Hændelsestype</th>
                <th>Aktør</th>
                <th>Mål-org</th>
                <th>Mål-ressource</th>
                <th>Synlighed</th>
                <th>Detaljer</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const isExpanded = expandedRows.has(row.projectionId)
                return (
                  <tr key={row.projectionId}>
                    <td>{formatTimestamp(row.occurredAt)}</td>
                    <td>
                      <span className={styles.badge}>{row.eventType}</span>
                    </td>
                    <td>{row.actorId ?? '—'}</td>
                    <td>{row.targetOrgId ?? '—'}</td>
                    <td>{row.targetResourceId ?? '—'}</td>
                    <td>{row.visibilityScope}</td>
                    <td
                      className={
                        isExpanded
                          ? styles.detailsCellExpanded
                          : styles.detailsCell
                      }
                      onClick={() => toggleExpanded(row.projectionId)}
                      title={isExpanded ? '' : row.details}
                    >
                      {row.details}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>

          <div className={styles.pagination}>
            <button
              className={styles.paginationBtn}
              onClick={handlePrev}
              disabled={page <= 1}
            >
              Forrige
            </button>
            <span className={styles.paginationInfo}>
              Side {page} af {totalPages}
            </span>
            <button
              className={styles.paginationBtn}
              onClick={handleNext}
              disabled={page >= totalPages}
            >
              Næste
            </button>
          </div>
        </>
      )}
    </div>
  )
}
