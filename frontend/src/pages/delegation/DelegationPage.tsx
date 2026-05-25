import { useState, useEffect, useCallback, type FormEvent } from 'react'
import { useDelegation, type DelegationStatus } from '../../hooks/useDelegation'
import { useToast } from '../../components/ui/Toast'
import { Spinner } from '../../components/ui'
import styles from './DelegationPage.module.css'

// S51 TASK-5107. Self-service delegation page for leaders. Two states:
// (1) active delegation — card showing details + cancel button
// (2) no delegation — form to create one (acting manager ID + return date)

function todayIso(): string {
  return new Date().toISOString().slice(0, 10)
}

export function DelegationPage() {
  const { fetchStatus, createDelegation, cancelDelegation } = useDelegation()
  const { toast } = useToast()

  const [status, setStatus] = useState<DelegationStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Form state
  const [actingManagerId, setActingManagerId] = useState('')
  const [effectiveTo, setEffectiveTo] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)

  // Cancel state
  const [cancelling, setCancelling] = useState(false)

  const loadStatus = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await fetchStatus()
    if (result.ok) {
      setStatus(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [fetchStatus])

  useEffect(() => {
    loadStatus()
  }, [loadStatus])

  const handleCreate = async (e: FormEvent) => {
    e.preventDefault()
    setSubmitting(true)
    setFormError(null)
    try {
      const result = await createDelegation({
        actingManagerId,
        effectiveTo,
      })
      if (!result.ok) {
        // 400 — scope validation (uncovered employees); 409 — already active
        if (result.status === 409) {
          setFormError('Der er allerede en aktiv uddelegering. Annuller den foerst.')
        } else {
          setFormError(result.error)
        }
      } else {
        toast({
          title: 'Uddelegeret',
          description: `${result.data.delegatedCount} medarbejdere uddelegeret til ${result.data.actingManagerId}`,
          variant: 'success',
        })
        setActingManagerId('')
        setEffectiveTo('')
        await loadStatus()
      }
    } catch (err) {
      setFormError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  const handleCancel = async () => {
    setCancelling(true)
    try {
      const result = await cancelDelegation()
      if (result.ok) {
        toast({
          title: 'Annulleret',
          description: 'Uddelegering er annulleret',
          variant: 'success',
        })
        await loadStatus()
      } else {
        toast({
          title: 'Fejl',
          description: result.error,
          variant: 'error',
        })
      }
    } catch (err) {
      toast({
        title: 'Fejl',
        description: err instanceof Error ? err.message : String(err),
        variant: 'error',
      })
    } finally {
      setCancelling(false)
    }
  }

  return (
    <div className={styles.page}>
      <div className={styles.header}>
        <h1 className={styles.title}>Vikariering</h1>
      </div>

      {loading && (
        <div className={styles.spinner}><Spinner size="lg" /></div>
      )}

      {error && <div className={styles.alert}>{error}</div>}

      {!loading && !error && status?.active && (
        <div className={styles.card}>
          <h2 className={styles.cardTitle}>Aktiv uddelegering</h2>

          <div className={styles.field}>
            <span className={styles.fieldLabel}>Vikarierende leder:</span>
            {status.actingManagerId}
          </div>

          <div className={styles.field}>
            <span className={styles.fieldLabel}>Gyldig fra:</span>
            {status.effectiveFrom}
          </div>

          <div className={styles.field}>
            <span className={styles.fieldLabel}>Gyldig til:</span>
            {status.effectiveTo}
          </div>

          {status.delegatedEmployees.length > 0 && (
            <div className={styles.field}>
              <span className={styles.fieldLabel}>Uddelegerede medarbejdere:</span>
              <ul className={styles.employeeList}>
                {status.delegatedEmployees.map((emp) => (
                  <li key={emp.employeeId}>
                    {emp.displayName} ({emp.employeeId})
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className={styles.formActions}>
            <button
              className={styles.dangerBtn}
              onClick={handleCancel}
              disabled={cancelling}
            >
              {cancelling ? 'Annullerer...' : 'Annuller uddelegering'}
            </button>
          </div>
        </div>
      )}

      {!loading && !error && status && !status.active && (
        <div className={styles.card}>
          <h2 className={styles.cardTitle}>Uddeleger godkendelser</h2>
          <form onSubmit={handleCreate}>
            <div className={styles.formField}>
              <label className={styles.formLabel} htmlFor="actingManagerId">
                Vikarierende leder (ID) <span className={styles.required}>*</span>
              </label>
              <input
                className={styles.input}
                id="actingManagerId"
                type="text"
                required
                value={actingManagerId}
                onChange={(e) => setActingManagerId(e.target.value)}
                placeholder="f.eks. EMP002"
              />
            </div>

            <div className={styles.formField}>
              <label className={styles.formLabel} htmlFor="effectiveTo">
                Returdato <span className={styles.required}>*</span>
              </label>
              <input
                className={styles.input}
                id="effectiveTo"
                type="date"
                required
                min={todayIso()}
                value={effectiveTo}
                onChange={(e) => setEffectiveTo(e.target.value)}
              />
            </div>

            {formError && <div className={styles.alert}>{formError}</div>}

            <div className={styles.formActions}>
              <button
                type="submit"
                className={styles.primaryBtn}
                disabled={submitting}
              >
                {submitting ? 'Uddelegerer...' : 'Uddeleger'}
              </button>
            </div>
          </form>
        </div>
      )}

      {!loading && !error && !status && (
        <div className={styles.emptyState}>
          Kunne ikke hente uddelegeringsstatus
        </div>
      )}
    </div>
  )
}
