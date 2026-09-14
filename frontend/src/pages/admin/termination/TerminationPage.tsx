// SPRINT-141 / TASK-14108 (C1, refinement `.claude/refinements/REFINEMENT-s141-increment4-and-
// the-settlement-anchor.md` section C1) — the termination screen. Recording that an employee is
// leaving was, until this task, only possible through the API. Termination is the one employment
// change HR cannot undo by typing a different value afterwards: it closes registration windows,
// can trigger a holiday settlement, and can strand payroll months if done carelessly — so this
// screen's job is to show HR what a save will do BEFORE they do it, and to explain every one of
// the endpoint's refusals as an instruction (what to do next), not as a restatement of an HTTP
// status.
//
// WHAT THIS SCREEN CALLS: PUT /api/admin/employees/{employeeId}/employment-end-date — a
// PRE-EXISTING endpoint (S70/S71/S136), not new this sprint. The concurrency token it sends as
// `If-Match` comes ONLY from the terminated-inclusive GET on the SAME endpoint — the one read
// that still answers once an employee is already terminated (the ordinary employment-start-date
// GET and the general user-detail GET are both ACTIVE-ONLY and 404 in that state). Two of the
// PUT's refusal shapes are untyped anonymous bodies by deliberate backend decision — hand-written
// and pinned in `hooks/__tests__/useTermination.test.ts`, since the generated contract cannot
// help here (see `useTermination.ts`'s file banner for the exact citations).
//
// THE B0 OBLIGATION THIS SCREEN CARRIES (owner requirement, S141): a scheduled change must be
// visible wherever a profile value is shown. This screen shows profile values (position,
// part-time fraction, agreement code) for context, so it surfaces BOTH scheduled-change fields
// the reads can carry — the profile's `scheduled` and the identity read's `scheduledAgreementCode`
// — prominently, because terminating someone with a promotion scheduled for next month is exactly
// the moment they need to see it: this screen's write does NOT cancel it (only a profile DELETE
// retires a scheduled profile change; a termination is a different aggregate).
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Alert, Badge, Button, Card, Dialog, FormField, Input, Spinner } from '../../../components/ui'
import { useToast } from '../../../components/ui/Toast'
import { useAuth } from '../../../contexts/AuthContext'
import {
  useTermination,
  classifyTerminationOutcome,
  type EmployeeProfileResponse,
  type EmploymentEndDateSnapshot,
  type EmploymentStartDateResponse,
  type TerminationOutcomeKind,
  type UserDetailResponse,
} from '../../../hooks/useTermination'
import { classifyRefusal, formatDateOnly, successCopy, type RefusalState } from './terminationCopy'
import styles from './TerminationPage.module.css'

function fractionLabel(fraction: number): string {
  return `${Math.round(fraction * 100)}%`
}

export function TerminationPage() {
  const { employeeId } = useParams<{ employeeId: string }>()
  const navigate = useNavigate()
  const { user } = useAuth()
  const { toast } = useToast()
  const {
    fetchEmploymentEndDate,
    fetchEmploymentStartDate,
    fetchProfileContext,
    fetchIdentity,
    setEmploymentEndDate,
  } = useTermination()

  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [endDateSnapshot, setEndDateSnapshot] = useState<EmploymentEndDateSnapshot | null>(null)
  const [startDate, setStartDate] = useState<EmploymentStartDateResponse | null>(null)
  const [profile, setProfile] = useState<EmployeeProfileResponse | null>(null)
  const [identity, setIdentity] = useState<UserDetailResponse | null>(null)

  const [draftDate, setDraftDate] = useState('')
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [saving, setSaving] = useState(false)
  const [refusal, setRefusal] = useState<RefusalState | null>(null)
  const [successOutcome, setSuccessOutcome] = useState<{ kind: TerminationOutcomeKind; date: string | null } | null>(
    null,
  )

  const isSelf = user?.employeeId === employeeId

  const load = useCallback(async () => {
    if (!employeeId) return
    setLoading(true)
    setLoadError(null)
    // NOTE: `successOutcome` is deliberately NOT cleared here. `load()` runs both on mount and
    // right after a successful save (to refresh the snapshot for the next edit) — clearing it on
    // that second run would erase the very confirmation message the save just produced, before
    // the user ever saw it render. It is cleared instead at the point a NEW attempt begins (the
    // date field changes, or another save starts).
    const endDateResult = await fetchEmploymentEndDate(employeeId)
    if (!endDateResult.ok) {
      setLoadError(endDateResult.error)
      setEndDateSnapshot(null)
      setLoading(false)
      return
    }
    setEndDateSnapshot(endDateResult.data)
    setDraftDate(endDateResult.data.employmentEndDate ?? '')

    // Best-effort context reads — all three can legitimately fail (404) once the employee is
    // already terminated (they are ACTIVE-ONLY / covering-row-anchored reads); a failure here is
    // swallowed, never treated as blocking the screen.
    const [identityData, startDateData, profileData] = await Promise.all([
      fetchIdentity(employeeId),
      fetchEmploymentStartDate(employeeId),
      fetchProfileContext(employeeId),
    ])
    setIdentity(identityData)
    setStartDate(startDateData)
    setProfile(profileData)
    setLoading(false)
  }, [employeeId, fetchEmploymentEndDate, fetchIdentity, fetchEmploymentStartDate, fetchProfileContext])

  useEffect(() => {
    void load()
  }, [load])

  const requestedDate = draftDate.trim() === '' ? null : draftDate
  const hasScheduledProfileChange = profile?.scheduled != null
  const hasScheduledAgreementChange = identity?.scheduledAgreementCode != null
  const dirty = endDateSnapshot != null && requestedDate !== (endDateSnapshot.employmentEndDate ?? null)

  const actionLabel = useMemo(() => {
    if (!endDateSnapshot) return 'Gem'
    if (requestedDate === null) return 'Fortryd fratrædelse'
    return endDateSnapshot.employmentEndDate ? 'Ret fratrædelsesdato' : 'Registrér fratrædelse'
  }, [endDateSnapshot, requestedDate])

  const openConfirm = () => {
    if (!endDateSnapshot || !dirty) return
    setRefusal(null)
    setSuccessOutcome(null)
    setConfirmOpen(true)
  }

  const confirmAndSubmit = async () => {
    if (!endDateSnapshot || !employeeId) return
    setSaving(true)
    const result = await setEmploymentEndDate(employeeId, endDateSnapshot.etag, requestedDate)
    setSaving(false)
    setConfirmOpen(false)

    if (result.ok) {
      const kind = classifyTerminationOutcome(endDateSnapshot, result.data, requestedDate)
      setSuccessOutcome({ kind, date: result.data.employmentEndDate })
      const copy = successCopy(kind, result.data.employmentEndDate)
      toast({ title: copy.title, description: copy.description, variant: 'success' })
      await load()
      return
    }

    const classified = classifyRefusal(result.status, result.body, result.error)
    setRefusal(classified)
    // 412 (stale token): the page's own data is out of date, not just the write — refetch so the
    // next attempt has a current snapshot, mirroring the WorklistList convention for this status.
    if (classified.kind === 'concurrency') {
      await load()
    }
  }

  if (!employeeId) {
    return (
      <div className={styles.root}>
        <Alert variant="error">Intet medarbejder-id angivet i adressen.</Alert>
      </div>
    )
  }

  return (
    <div className={styles.root} data-testid="termination-page">
      <button type="button" className={styles.back} onClick={() => navigate(-1)}>
        ‹ Tilbage
      </button>
      <h1 className={styles.heading}>Fratrædelse</h1>

      {loading && (
        <div className={styles.spinner} data-testid="termination-loading">
          <Spinner size="lg" />
        </div>
      )}

      {!loading && loadError && (
        <div data-testid="termination-load-error">
          <Alert variant="error">
            Kunne ikke hente medarbejderens fratrædelsesoplysninger: {loadError}
          </Alert>
        </div>
      )}

      {!loading && !loadError && endDateSnapshot && (
        <>
          <Card
            header={
              <div className={styles.identityHeader}>
                <span className={styles.identityName}>
                  {identity?.displayName ?? `Medarbejder ${employeeId}`}
                </span>
                <Badge variant={endDateSnapshot.isActive ? 'success' : 'error'}>
                  {endDateSnapshot.isActive ? 'Aktiv' : 'Inaktiv'}
                </Badge>
              </div>
            }
          >
            {!identity && (
              <p className={styles.muted} data-testid="termination-identity-fallback">
                {endDateSnapshot.isActive
                  ? 'Kunne ikke hente medarbejderens navn.'
                  : 'Visningsnavn er ikke tilgængeligt for en allerede fratrådt medarbejder — kun medarbejder-id vises.'}
              </p>
            )}
            <dl className={styles.factList}>
              <div>
                <dt>Ansat siden</dt>
                <dd>
                  {startDate?.employmentStartDate
                    ? formatDateOnly(startDate.employmentStartDate)
                    : endDateSnapshot.isActive
                      ? 'Ukendt'
                      : 'Ikke tilgængelig for en fratrådt medarbejder'}
                </dd>
              </div>
              <div>
                <dt>Registreret fratrædelsesdato</dt>
                <dd data-testid="termination-current-end-date">
                  {endDateSnapshot.employmentEndDate ? formatDateOnly(endDateSnapshot.employmentEndDate) : 'Ingen'}
                </dd>
              </div>
              {profile && (
                <div>
                  <dt>Stilling / beskæftigelsesgrad</dt>
                  <dd>
                    {profile.position ?? '–'} ({fractionLabel(profile.partTimeFraction)})
                  </dd>
                </div>
              )}
            </dl>
          </Card>

          {(hasScheduledProfileChange || hasScheduledAgreementChange) && (
            <div data-testid="termination-scheduled-banner" className={styles.scheduledBanner}>
              <Alert variant="warning">
                <strong>Denne medarbejder har en planlagt ændring, som fratrædelse IKKE automatisk annullerer:</strong>
                <ul className={styles.scheduledList}>
                  {profile?.scheduled && (
                    <li data-testid="termination-scheduled-profile">
                      Fra {formatDateOnly(profile.scheduled.effectiveFrom)}
                      {profile.scheduled.effectiveTo ? ` til ${formatDateOnly(profile.scheduled.effectiveTo)}` : ''}:{' '}
                      {profile.scheduled.position ?? '–'} ({fractionLabel(profile.scheduled.partTimeFraction)}
                      {profile.scheduled.employmentCategory ? `, ${profile.scheduled.employmentCategory}` : ''})
                    </li>
                  )}
                  {identity?.scheduledAgreementCode && (
                    <li data-testid="termination-scheduled-agreement">
                      Overenskomstkode ændres til {identity.scheduledAgreementCode.agreementCode} fra{' '}
                      {formatDateOnly(identity.scheduledAgreementCode.effectiveFrom)}
                      {identity.scheduledAgreementCode.effectiveTo
                        ? ` til ${formatDateOnly(identity.scheduledAgreementCode.effectiveTo)}`
                        : ''}
                    </li>
                  )}
                </ul>
                Overvej at annullere den planlagte ændring separat, hvis den ikke længere er relevant.
              </Alert>
            </div>
          )}

          {isSelf ? (
            <div data-testid="termination-self-block">
              <Alert variant="error">
                Du kan ikke afslutte din egen ansættelse her. Bed en anden HR-administrator om at gøre dette.
              </Alert>
            </div>
          ) : (
            <Card header={<span>{actionLabel}</span>}>
              {successOutcome && (
                <div data-testid="termination-success">
                  <Alert variant="success">{successCopy(successOutcome.kind, successOutcome.date).description}</Alert>
                </div>
              )}

              {refusal && (
                <div data-testid={`termination-refusal-${refusal.kind}`}>
                  <Alert variant="error">{refusal.message}</Alert>
                  {refusal.kind === 'settlement-conflict' && (
                    <ul className={styles.refusalDetail} data-testid="termination-refusal-settlement-rows">
                      {refusal.body.blockingSettlements.map((row) => (
                        <li key={`${row.entitlementType}-${row.entitlementYear}-${row.sequence}`}>
                          {row.entitlementType} {row.entitlementYear} — {row.settlementState.toLowerCase()}
                        </li>
                      ))}
                    </ul>
                  )}
                  {refusal.kind === 'strand-conflict' && (
                    <ul className={styles.refusalDetail} data-testid="termination-refusal-stranded-rows">
                      {refusal.body.strandedMonths.map((row) => (
                        <li key={row.month}>
                          {row.month}: {row.timeEntryCount} tid, {row.absenceCount} fravær, {row.workTimeCount}{' '}
                          arbejdstid
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}

              <FormField label="Sidste ansættelsesdag (fratrædelsesdato)" htmlFor="termination-date">
                <Input
                  id="termination-date"
                  type="date"
                  value={draftDate}
                  min={startDate?.employmentStartDate ?? undefined}
                  onChange={(e) => {
                    setDraftDate(e.target.value)
                    setRefusal(null)
                    setSuccessOutcome(null)
                  }}
                  data-testid="termination-date-input"
                  disabled={saving}
                />
              </FormField>
              <p className={styles.hint}>
                En dato i dag eller tilbage i tiden gør medarbejderen inaktiv med det samme. En dato frem i tiden
                planlægger fratrædelsen — medarbejderen forbliver aktiv indtil da. Ryd feltet og gem for at fortryde
                en registreret fratrædelse.
              </p>

              <div className={styles.actions}>
                <Button
                  variant="danger"
                  onClick={openConfirm}
                  disabled={!dirty || saving}
                  data-testid="termination-submit"
                >
                  {actionLabel}
                </Button>
              </div>
            </Card>
          )}
        </>
      )}

      {endDateSnapshot && (
        <Dialog
          open={confirmOpen}
          onOpenChange={setConfirmOpen}
          title="Bekræft ændring"
          description="Gennemgå konsekvensen af denne ændring, før den gennemføres."
        >
          <div data-testid="termination-confirm-dialog">
            <p>
              {requestedDate === null ? (
                <>
                  Den registrerede fratrædelsesdato ({formatDateOnly(endDateSnapshot.employmentEndDate)}) fjernes, og
                  medarbejderens ansættelse fortsætter.
                </>
              ) : (
                <>
                  Ansættelsen registreres som afsluttet pr. <strong>{formatDateOnly(requestedDate)}</strong>. En dato i
                  dag eller tidligere gør medarbejderen inaktiv med det samme; en fremtidig dato planlægger
                  fratrædelsen uden at ændre status nu.
                </>
              )}
            </p>
            <p>
              Dette kan udløse en feriesaldo-afregning og kan ikke fortrydes ved blot at indtaste en anden dato bagefter
              — en eventuel rettelse efter en afregning kræver at afregningen reverseres først.
            </p>
            {(hasScheduledProfileChange || hasScheduledAgreementChange) && (
              <p>
                <strong>Bemærk:</strong> denne medarbejder har en planlagt profil- eller overenskomstændring, som
                denne handling ikke annullerer.
              </p>
            )}
            <div className={styles.actions}>
              <Button variant="danger" onClick={() => void confirmAndSubmit()} disabled={saving} data-testid="termination-confirm-submit">
                {saving ? 'Gemmer...' : 'Bekræft'}
              </Button>
              <Button variant="ghost" onClick={() => setConfirmOpen(false)} disabled={saving}>
                Annullér
              </Button>
            </div>
          </div>
        </Dialog>
      )}
    </div>
  )
}
