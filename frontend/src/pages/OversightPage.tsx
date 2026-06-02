import { useState, useCallback, useMemo } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { useBalanceSummary } from '../hooks/useBalanceSummary'
import { useAccrualSeries } from '../hooks/useAccrualSeries'
import { useCompliance } from '../hooks/useCompliance'
import { useSkema } from '../hooks/useSkema'
import { ComplianceWarnings } from '../components/ComplianceWarnings'
import { LeaveOverview } from '../components/LeaveOverview'
import { AccrualTrend } from '../components/AccrualTrend'
import { Card } from '../components/ui/Card'
import { Badge } from '../components/ui/Badge'
import { Button } from '../components/ui/Button'
import { Spinner } from '../components/ui/Spinner'
import { formatMonthLabel, formatDanishNumber } from '../lib/locale'
import styles from './OversightPage.module.css'

/** Approval status -> Danish label + Badge variant (same labels as SkemaPage). */
const APPROVAL_LABELS: Record<string, { label: string; variant: 'default' | 'info' | 'success' | 'error' }> = {
  DRAFT: { label: 'Kladde', variant: 'default' },
  SUBMITTED: { label: 'Indsendt', variant: 'info' },
  EMPLOYEE_APPROVED: { label: 'Indsendt', variant: 'info' },
  APPROVED: { label: 'Godkendt', variant: 'success' },
  REJECTED: { label: 'Afvist', variant: 'error' },
}

function formatFlex(hours: number): string {
  return `${hours >= 0 ? '+' : ''}${formatDanishNumber(hours)} t`
}

function formatDelta(delta: number): string {
  if (delta > 0) return `▲ +${formatDanishNumber(delta)}`
  if (delta < 0) return `▼ ${formatDanishNumber(delta)}`
  return `● ${formatDanishNumber(delta)}`
}

/** Format an ISO date/datetime string as da-DK (day month year). */
function formatDeadline(dateStr: string | null): string {
  if (!dateStr) return ''
  try {
    const d = new Date(dateStr)
    return d.toLocaleDateString('da-DK', { day: 'numeric', month: 'long', year: 'numeric' })
  } catch {
    return dateStr
  }
}

export function OversightPage() {
  const { user } = useAuth()
  const employeeId = user?.employeeId ?? ''

  const now = new Date()
  const [year, setYear] = useState(now.getFullYear())
  const [month, setMonth] = useState(now.getMonth() + 1)

  const { data: balance, loading: balanceLoading, error: balanceError } = useBalanceSummary(employeeId, year, month)
  const { data: accrual, loading: accrualLoading } = useAccrualSeries(employeeId, year, month)
  const { result: compliance, loading: complianceLoading } = useCompliance(employeeId, year, month)
  const { data: skema, loading: skemaLoading } = useSkema(employeeId, year, month)

  const goToPrevMonth = useCallback(() => {
    setMonth((prev) => {
      if (prev === 1) {
        setYear((y) => y - 1)
        return 12
      }
      return prev - 1
    })
  }, [])

  const goToNextMonth = useCallback(() => {
    setMonth((prev) => {
      if (prev === 12) {
        setYear((y) => y + 1)
        return 1
      }
      return prev + 1
    })
  }, [])

  // Ferieår the leave/curve covers — derived from the first accrual series entry.
  const ferieaarLabel = useMemo(() => {
    const start = accrual?.series.find((s) => s.ferieaarStart)?.ferieaarStart
    if (!start || start.length < 4) return null
    const startYear = Number(start.slice(0, 4))
    if (Number.isNaN(startYear)) return null
    return `Ferieår ${startYear}/${String((startYear + 1) % 100).padStart(2, '0')}`
  }, [accrual])

  // Compliance "clean" = loaded, no violations and no warnings.
  const complianceClean =
    !complianceLoading && !!compliance && compliance.violations.length === 0 && compliance.warnings.length === 0

  const approval = skema?.approval ?? null
  const approvalMeta = approval ? APPROVAL_LABELS[approval.status] ?? { label: approval.status, variant: 'default' as const } : null

  // Only MONTHLY_ACCRUAL types appear in the accrual series → AccrualTrend.
  const accrualSeries = accrual?.series

  // Full-page loading only when the primary balance call is in flight with no data.
  if (balanceLoading && !balance) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="lg" />
        <p>Indlæser oversigt…</p>
      </div>
    )
  }

  return (
    <div className={styles.page}>
      {/* A. Header — month nav + ferieår */}
      <div className={styles.header}>
        <div className={styles.monthNav}>
          <Button variant="ghost" size="sm" onClick={goToPrevMonth}>
            &larr; Forrige
          </Button>
          <h2 className={styles.monthTitle}>{formatMonthLabel(year, month)}</h2>
          <Button variant="ghost" size="sm" onClick={goToNextMonth}>
            Næste &rarr;
          </Button>
        </div>
        {ferieaarLabel && <Badge variant="default">{ferieaarLabel}</Badge>}
      </div>

      {balanceError && !balance && (
        <Card>
          <p className={styles.errorText}>Kunne ikke indlæse saldi: {balanceError}</p>
        </Card>
      )}

      {/* B. Saldi (balance cards) */}
      {balance && (
        <section className={styles.section} aria-labelledby="oversigt-saldi">
          <h3 id="oversigt-saldi" className={styles.sectionTitle}>Saldi</h3>
          <div className={styles.cardGrid}>
            <div className={styles.card}>
              <p className={styles.cardLabel}>Flex saldo</p>
              <p className={styles.cardValue}>{formatFlex(balance.flexBalance)}</p>
              <p className={styles.cardDelta}>{formatDelta(balance.flexDelta)}</p>
            </div>

            <div className={styles.card}>
              <p className={styles.cardLabel}>Normtimer</p>
              <p className={styles.cardValue}>
                {formatDanishNumber(balance.normHoursActual)} / {formatDanishNumber(balance.normHoursExpected)} t
              </p>
              <p className={styles.cardSub}>
                Diff fra normtid: {formatFlex(balance.normHoursActual - balance.normHoursExpected)}
              </p>
            </div>

            <div className={styles.card}>
              <p className={styles.cardLabel}>{balance.hasMerarbejde ? 'Merarbejde' : 'Overarbejde'}</p>
              <p className={styles.cardValue}>{formatDanishNumber(balance.overtimeHours)} t</p>
            </div>

            {/* Null-safe overtime/afspadsering balance card */}
            <div className={styles.card}>
              <p className={styles.cardLabel}>Overtidssaldo</p>
              {balance.overtimeBalance ? (
                <>
                  <p className={styles.cardValue}>{formatDanishNumber(balance.overtimeBalance.remaining)} t</p>
                  <dl className={styles.miniStats}>
                    <div className={styles.miniStat}>
                      <dt>Akkumuleret</dt>
                      <dd>{formatDanishNumber(balance.overtimeBalance.accumulated)} t</dd>
                    </div>
                    <div className={styles.miniStat}>
                      <dt>Afspadsering brugt</dt>
                      <dd>{formatDanishNumber(balance.overtimeBalance.afspadseringUsed)} t</dd>
                    </div>
                    <div className={styles.miniStat}>
                      <dt>Udbetalt</dt>
                      <dd>{formatDanishNumber(balance.overtimeBalance.paidOut)} t</dd>
                    </div>
                    <div className={styles.miniStat}>
                      <dt>Model</dt>
                      <dd>{balance.overtimeBalance.compensationModel}</dd>
                    </div>
                  </dl>
                </>
              ) : (
                <p className={styles.cardMuted}>—</p>
              )}
            </div>
          </div>
        </section>
      )}

      {/* C. Ferie & fravær — the centerpiece */}
      <section className={styles.section} aria-labelledby="oversigt-ferie">
        <h3 id="oversigt-ferie" className={styles.sectionTitle}>Ferie &amp; fravær</h3>
        <LeaveOverview
          entitlements={balance?.entitlements}
          series={accrualSeries}
          loading={balanceLoading}
        />
      </section>

      {/* D. Arbejdstidskontrol */}
      <section className={styles.section} aria-labelledby="oversigt-kontrol">
        <h3 id="oversigt-kontrol" className={styles.sectionTitle}>Arbejdstidskontrol</h3>
        {/* hideTitle: the section <h3> above is the single heading; suppress the component's own. */}
        <ComplianceWarnings result={compliance} loading={complianceLoading} hideTitle />
        {complianceClean && (
          <div className={styles.cleanBanner}>
            <Badge variant="success">Ingen advarsler</Badge>
            <span className={styles.cleanText}>Ingen overtrædelser eller advarsler for perioden.</span>
          </div>
        )}
        {complianceLoading && !compliance && (
          <p className={styles.muted}>Indlæser arbejdstidskontrol…</p>
        )}
      </section>

      {/* E. Godkendelsesstatus (read-only) */}
      <section className={styles.section} aria-labelledby="oversigt-status">
        <h3 id="oversigt-status" className={styles.sectionTitle}>Godkendelsesstatus</h3>
        <Card>
          {skemaLoading && !skema ? (
            <p className={styles.muted}>Indlæser status…</p>
          ) : approval && approvalMeta ? (
            <div className={styles.statusBody}>
              <div className={styles.statusRow}>
                <Badge variant={approvalMeta.variant}>{approvalMeta.label}</Badge>
                {approval.employeeApprovedAt && (
                  <span className={styles.statusMeta}>
                    Indsendt {formatDeadline(approval.employeeApprovedAt)}
                  </span>
                )}
              </div>
              <dl className={styles.statusStats}>
                {approval.employeeDeadline && (
                  <div className={styles.statusStat}>
                    <dt>Medarbejderfrist</dt>
                    <dd>{formatDeadline(approval.employeeDeadline)}</dd>
                  </div>
                )}
                {approval.managerDeadline && (
                  <div className={styles.statusStat}>
                    <dt>Lederfrist</dt>
                    <dd>{formatDeadline(approval.managerDeadline)}</dd>
                  </div>
                )}
              </dl>
              {approval.status === 'REJECTED' && (
                <p className={styles.rejection}>
                  Afvist: {approval.rejectionReason ?? 'Ingen begrundelse'}
                </p>
              )}
              <Link to="/tid/mine-perioder" className={styles.periodsLink}>
                Mine perioder &rarr;
              </Link>
            </div>
          ) : (
            <div className={styles.statusBody}>
              <Badge variant="default">Kladde</Badge>
              <span className={styles.statusMeta}>Ingen periode oprettet for måneden endnu.</span>
              <Link to="/tid/mine-perioder" className={styles.periodsLink}>
                Mine perioder &rarr;
              </Link>
            </div>
          )}
        </Card>
      </section>

      {/* G. Udvikling — accrual trend mini bar-charts */}
      <section className={styles.section} aria-labelledby="oversigt-udvikling">
        <h3 id="oversigt-udvikling" className={styles.sectionTitle}>Udvikling</h3>
        <AccrualTrend series={accrualSeries} loading={accrualLoading} />
      </section>
    </div>
  )
}
