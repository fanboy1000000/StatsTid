// SPRINT-140 / TASK-14007 (refinement B5, plan cell TASK-14007) — the backdate
// worklist's first-ever screen. S138 (TASK-13803) built the API and the derived
// "has this moved since?" flags (PAT-026); until this task the only frontend
// trace was a generated type and a type-contract test. This component is a
// PIECE, not a page: TASK-14006 (the HR landing page, a later wave) imports it
// as `WorklistList` (default export) and passes `onResolved` so the landing
// page's tile counts can refresh after a write here. It renders no route of its
// own and no page chrome — App.tsx / Sidebar.tsx are untouched by this file.
//
// WHAT HR SEES: every open (unresolved) row from GET /api/hr/backdate-worklist,
// org-wide (HROrAbove + org scope — the backend 403s an empty scope; this
// component surfaces that 403 as the fetch error, it does not paper over it).
// Two kinds: an EXPORTED_MONTH row (a payroll month already sent, now stale)
// or a SETTLED_YEAR row (a holiday year already settled, now stale). Both carry
// the triggers that caused them and the "since" flags PAT-026 defines: whether
// the underlying export/settlement has ALREADY MOVED since the row's own
// baseline — NOT how long the row has waited. The row carries no age at all
// (a deliberate omission of the underlying process, not a bug), so this screen
// never shows a "days old" number for it.
//
// THE ONE WRITE: Resolve (RECALCULATED | DISMISSED + a reason), admin-strict
// If-Match on the row's `version`. 412 = someone else changed the row since
// this list was fetched → refetch + tell the user. 428 = this client failed to
// send a usable precondition header — a BUG (the hook always composes one from
// the row's version), surfaced as a loud, distinctly-worded notice, never
// silently retried or folded into the generic error text. 409 = already
// resolved by someone else → refetch + tell the user.
//
// THE RECALCULATE ACTION (owner ruling OQ-2 (a) THEN OQ-7 (a) — the gate tracks
// THE REMEDY, not the screen): an EXPORTED_MONTH row's fix is POST
// /api/payroll/recalculate — that endpoint lives on the PAYROLL host
// (GlobalAdminOnly), which the browser cannot reach (the frontend proxies only
// to Backend.Api, `vite.config.ts:10,21`; Payroll has no CORS; `api-types.ts`
// is generated from Backend.Api's spec only). So ONLY a Global Admin sees the
// labelled, NON-CALLING instruction card naming the process, the row's
// identity, and the Payroll-host contract the operator assembles by hand
// (Payroll `Program.cs:548-575`) — it renders NO payload, because a worklist
// row cannot know one; every other role sees a one-line hint instead. A
// SETTLED_YEAR row's fix is the settlement REVERSAL, `POST
// /api/admin/employees/{employeeId}/settlement-reversal`, which is
// `HROrAbove` (`SettlementReversalEndpoints.cs:243`) — so OQ-7 (a) (owner
// ruling, 2026-09-09, raised by this task rather than decided unilaterally)
// lets ANY HR-capable actor mark a SETTLED_YEAR row "Recalculated" once they
// have performed that reversal themselves; the row still shows the reversal
// route as inert TEXT ONLY (no form — OQ-4), and the instruction CARD stays
// Global-Admin-only and network-call-free regardless of kind. When the row's
// `recalcBlockedBy` is non-empty (a trigger strictly inside the exported
// month — QUAL-149/150 — recalculating would yield wrong wage-type codes) the
// card/hint is REPLACED by a blocked notice and only Dismiss remains, for
// EVERY role and EVERY kind — recalculating produces a wrong result
// regardless of who is asking, so this check is never bypassed by the
// per-kind gate above. This is the specific defect the plan review pinned, so
// the two states never render at once.
import { useCallback, useEffect, useState } from 'react'
import { useAuth } from '../../../contexts/AuthContext'
import { hasMinRole } from '../../../lib/roles'
import { Alert, Badge, Button, Card, Spinner, Textarea } from '../../../components/ui'
import { useToast } from '../../../components/ui/Toast'
import {
  useHrBackdateWorklist,
  WorklistKinds,
  WorklistResolutions,
  type BackdateWorklistRow,
  type WorklistResolution,
} from '../../../hooks/useHrBackdateWorklist'
import styles from './WorklistList.module.css'

export interface WorklistListProps {
  /** Fired after a successful Resolve so the host (TASK-14006's landing page) can refresh its tile counts. */
  onResolved: () => void
}

type RowNotice = { kind: 'warning' | 'bug'; message: string }

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('da-DK')
  } catch {
    return iso
  }
}

function formatDateTime(iso: string): string {
  try {
    return new Date(iso).toLocaleString('da-DK', {
      year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
    })
  } catch {
    return iso
  }
}

function kindLabel(kind: string): string {
  return kind === WorklistKinds.SettledYear ? 'Afsluttet ferieår' : 'Eksporteret måned'
}

function triggerKindLabel(kind: string): string {
  switch (kind) {
    case 'PROFILE_CHANGE': return 'Profilændring'
    case 'AGREEMENT_CODE_CHANGE': return 'Overenskomstkode-ændring'
    case 'EMPLOYMENT_CATEGORY_CHANGE': return 'Ansættelseskategori-ændring'
    default: return kind
  }
}

function resolutionLabel(resolution: string | null): string {
  if (resolution === WorklistResolutions.Recalculated) return 'Genberegnet/reverseret'
  if (resolution === WorklistResolutions.Dismissed) return 'Afvist'
  return resolution ?? '–'
}

/** Ja/Nej/– — deliberately never "X dage" (PAT-026: this is a MOVED flag, not an age). */
function movedSinceLabel(value: boolean | null): string {
  return value === null ? '–' : value ? 'Ja' : 'Nej'
}

/**
 * The settlement-reversal endpoint a SETTLED_YEAR row points an operator at.
 *
 * DECLARED DEVIATION: the refinement/plan text describes this as a
 * `reversalEndpoint` field the row carries — no such field exists on the
 * generated `BackdateWorklistRow` (verified against both `api-types.ts:5195-
 * 5223` and the C# record in `BackdateWorklistResponses.cs`). The route itself
 * is real, static and already shipped (`SettlementReversalEndpoints.cs:91`,
 * the same string `EmploymentDateEndpoints.cs:585` composes for an analogous
 * "where do I go" pointer) — HROrAbove, not Global-Admin-gated — so it is
 * reconstructed here from the row's own `employeeId` rather than invented from
 * nothing. Flagged to the Orchestrator as a spec/contract mismatch; not fixed
 * here (backend is out of this task's scope).
 */
function reversalEndpointFor(employeeId: string): string {
  return `/api/admin/employees/${employeeId}/settlement-reversal`
}

function periodLabel(row: BackdateWorklistRow): string {
  if (row.kind === WorklistKinds.SettledYear) {
    return `${row.entitlementType ?? '–'} ${row.entitlementYear ?? '–'}`
  }
  return `${row.month ?? '–'}/${row.year ?? '–'}`
}

export default function WorklistList({ onResolved }: WorklistListProps) {
  const { role } = useAuth()
  const isGlobalAdmin = hasMinRole(role, 'GlobalAdmin')
  const { fetchWorklist, resolveWorklistItem } = useHrBackdateWorklist()
  const { toast } = useToast()

  const [rows, setRows] = useState<BackdateWorklistRow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [openReasonFor, setOpenReasonFor] = useState<string | null>(null)
  const [reasonDraft, setReasonDraft] = useState('')
  const [resolvingId, setResolvingId] = useState<string | null>(null)
  const [rowNotice, setRowNotice] = useState<Record<string, RowNotice>>({})

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    const result = await fetchWorklist()
    if (result.ok) {
      setRows(result.data)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [fetchWorklist])

  useEffect(() => {
    void load()
  }, [load])

  const startResolve = (worklistId: string) => {
    setOpenReasonFor(worklistId)
    setReasonDraft('')
    setRowNotice((prev) => {
      if (!(worklistId in prev)) return prev
      const next = { ...prev }
      delete next[worklistId]
      return next
    })
  }

  const cancelResolve = () => {
    setOpenReasonFor(null)
    setReasonDraft('')
  }

  const submitResolve = async (row: BackdateWorklistRow, resolution: WorklistResolution) => {
    const reason = reasonDraft.trim()
    if (!reason) {
      setRowNotice((prev) => ({ ...prev, [row.worklistId]: { kind: 'warning', message: 'Begrundelse er påkrævet.' } }))
      return
    }
    setResolvingId(row.worklistId)
    const result = await resolveWorklistItem(row.worklistId, row.version, resolution, reason)
    setResolvingId(null)

    if (result.ok) {
      toast({
        title: 'Løst',
        description: resolution === WorklistResolutions.Recalculated ? 'Markeret som genberegnet/reverseret.' : 'Afvist.',
        variant: 'success',
      })
      setOpenReasonFor(null)
      setReasonDraft('')
      await load()
      onResolved()
      return
    }

    if (result.status === 412) {
      setRowNotice((prev) => ({
        ...prev,
        [row.worklistId]: { kind: 'warning', message: 'Rækken er ændret af en anden bruger, siden listen blev hentet. Listen genindlæses — prøv igen.' },
      }))
      await load()
      return
    }
    if (result.status === 409) {
      setRowNotice((prev) => ({
        ...prev,
        [row.worklistId]: { kind: 'warning', message: 'Sagen er allerede løst af en anden. Listen genindlæses.' },
      }))
      await load()
      return
    }
    if (result.status === 428) {
      // This should never happen — resolveWorklistItem always composes If-Match from the row's
      // own version. Surfaced loudly and distinctly (never folded into the generic error path,
      // never silently retried) so a real occurrence is impossible to miss.
      // eslint-disable-next-line no-console
      console.error('BUG: backdate-worklist resolve sent without a usable If-Match header', result)
      setRowNotice((prev) => ({
        ...prev,
        [row.worklistId]: {
          kind: 'bug',
          message: 'Intern fejl (428): anmodningen manglede en versionsangivelse. Dette er en fejl i klienten, ikke i sagen — kontakt udvikling.',
        },
      }))
      return
    }
    setRowNotice((prev) => ({ ...prev, [row.worklistId]: { kind: 'warning', message: `Kunne ikke gemme: ${result.error}` } }))
  }

  return (
    <div className={styles.root}>
      <h2 className={styles.heading}>Bagudrettede rettelser</h2>
      <p className={styles.subheading}>
        Måneder allerede sendt til løn, og ferieår allerede afsluttet, som en bagudrettet rettelse i personens historik
        har gjort forældede.
      </p>

      {loading && (
        <div className={styles.spinner}>
          <Spinner size="lg" />
        </div>
      )}

      {!loading && error && (
        <div data-testid="worklist-load-error">
          <Alert variant="error">{error}</Alert>
        </div>
      )}

      {!loading && !error && rows.length === 0 && (
        <div className={styles.empty} data-testid="worklist-empty">Ingen åbne sager.</div>
      )}

      {!loading && !error && rows.length > 0 && (
        <ul className={styles.list}>
          {rows.map((row) => {
            const isExportedMonth = row.kind === WorklistKinds.ExportedMonth
            const isSettledYear = row.kind === WorklistKinds.SettledYear
            const blocked = row.recalcBlockedBy.length > 0
            const alreadyResolved = row.resolvedAt != null
            // OQ-7 (a): the gate tracks the REMEDY. EXPORTED_MONTH's remedy is the
            // GlobalAdminOnly payroll recalculate — stays Global-Admin-only.
            // SETTLED_YEAR's remedy is the settlement reversal, which is HROrAbove
            // (SettlementReversalEndpoints.cs:243) — any HR-capable actor may mark
            // it. A blocked row (wrong wage codes if recalculated) overrides BOTH,
            // for every role and every kind.
            const canMarkRecalculated = !blocked && (isSettledYear || isGlobalAdmin)
            const notice = rowNotice[row.worklistId]
            const resolving = resolvingId === row.worklistId

            return (
              <li key={row.worklistId} className={styles.row} data-testid={`worklist-row-${row.worklistId}`}>
                <Card
                  header={
                    <div className={styles.rowHeader}>
                      <span className={styles.kind}>{kindLabel(row.kind)}</span>
                      <span className={styles.period}>{periodLabel(row)}</span>
                      {row.exportId && <span className={styles.muted}>Eksport-id: {row.exportId}</span>}
                    </div>
                  }
                >
                  <div className={styles.meta}>
                    Oprettet {formatDateTime(row.createdAt)} af {row.createdBy}
                  </div>

                  <div className={styles.triggers}>
                    <div className={styles.sectionLabel}>Udløsere</div>
                    <ul className={styles.triggerList}>
                      {row.triggers.map((t) => (
                        <li key={t.eventId}>
                          {triggerKindLabel(t.kind)} — effektiv fra {formatDate(t.effectiveFrom)}, tilføjet{' '}
                          {formatDateTime(t.appendedAt)} af {t.actorId}
                        </li>
                      ))}
                    </ul>
                  </div>

                  <div className={styles.movedSince}>
                    {isExportedMonth && (
                      <div>Lønoplysningerne er ændret siden denne rettelse: {movedSinceLabel(row.recalculatedSince)}</div>
                    )}
                    {isSettledYear && (
                      <div>Afregningen er ændret siden denne rettelse: {movedSinceLabel(row.reversedSince)}</div>
                    )}
                    <div className={styles.caveat}>
                      Viser om de underliggende data er ændret siden — IKKE hvor længe sagen har ventet (sagen har ingen
                      alderskolonne).
                    </div>
                  </div>

                  {alreadyResolved ? (
                    <div className={styles.resolved} data-testid={`worklist-resolved-${row.worklistId}`}>
                      Løst som {resolutionLabel(row.resolution)} af {row.resolvedBy} ({row.resolvedAt ? formatDateTime(row.resolvedAt) : '–'})
                      {row.resolutionReason ? `: ${row.resolutionReason}` : ''}
                    </div>
                  ) : (
                    <>
                      {/* Blocked overrides the per-kind gate below for EVERY role and EVERY
                          kind — recalculating a blocked row yields wrong wage codes no
                          matter who is asking (structurally this can only be non-empty on
                          an EXPORTED_MONTH row today, but the check does not assume that). */}
                      {blocked && (
                        <div data-testid={`worklist-blocked-${row.worklistId}`}>
                          <Alert variant="warning">
                            Genberegning blokeret — {row.recalcBlockedBy.join(', ')}
                          </Alert>
                        </div>
                      )}
                      {isExportedMonth && !blocked && isGlobalAdmin && (
                        <div className={styles.recalcCard} data-testid={`worklist-recalc-card-${row.worklistId}`}>
                          <div className={styles.recalcTitle}>Kør genberegning via Payroll-API</div>
                          <ul className={styles.recalcFacts}>
                            <li>Proces: HRP-003 — manuel genberegning af en allerede eksporteret måned (ADR-013: ingen automatisk kaskade).</li>
                            <li>Sag: medarbejder {row.employeeId}, periode {row.month}/{row.year}, eksport-id {row.exportId ?? '–'}.</li>
                            <li>
                              Endpoint: <code>POST /api/payroll/recalculate</code> på Payroll-tjenesten (ikke Backend-API'et) —
                              kræver rollen Global Administrator.
                            </li>
                            <li>
                              Kontrakt (<code>RecalculateRequest</code>): Profile, Entries, Absences, PeriodStart/PeriodEnd,
                              PreviousFlexBalance, Reason — operatøren samler selv anmodningen. Denne side viser INGEN udfyldt
                              anmodning: en sag på denne liste kender ikke payload'en, og et gæt her ville vildlede.
                            </li>
                            <li>Kaldet foretages IKKE herfra — browseren kan ikke nå Payroll-tjenesten direkte.</li>
                          </ul>
                        </div>
                      )}
                      {isExportedMonth && !blocked && !isGlobalAdmin && (
                        <div className={styles.hint} data-testid={`worklist-recalc-hint-${row.worklistId}`}>
                          Genberegning kræver en Global Administrator.
                        </div>
                      )}
                      {/* OQ-4: text only, no reversal form — unconditional (unaffected by
                          role or by the blocked check above, which is an EXPORTED_MONTH
                          re-plan concept and never applies here). */}
                      {isSettledYear && (
                        <div className={styles.reversalPointer} data-testid={`worklist-reversal-${row.worklistId}`}>
                          Reversér via API: <code>POST {reversalEndpointFor(row.employeeId)}</code>
                        </div>
                      )}

                      {openReasonFor === row.worklistId ? (
                        <div className={styles.resolveForm}>
                          <Textarea
                            id={`worklist-reason-${row.worklistId}`}
                            value={reasonDraft}
                            onChange={(e) => setReasonDraft(e.target.value)}
                            placeholder="Begrundelse (påkrævet)"
                            data-testid={`worklist-reason-${row.worklistId}`}
                            disabled={resolving}
                          />
                          {notice && (
                            <div data-testid={`worklist-notice-${row.worklistId}`}>
                              <Alert variant={notice.kind === 'bug' ? 'error' : 'warning'}>{notice.message}</Alert>
                            </div>
                          )}
                          <div className={styles.resolveActions}>
                            {canMarkRecalculated && (
                              <Button
                                size="sm"
                                onClick={() => void submitResolve(row, WorklistResolutions.Recalculated)}
                                disabled={resolving}
                                data-testid={`worklist-mark-recalculated-${row.worklistId}`}
                              >
                                {resolving ? '...' : isSettledYear ? 'Marker som reverseret' : 'Marker som genberegnet'}
                              </Button>
                            )}
                            <Button
                              size="sm"
                              variant="secondary"
                              onClick={() => void submitResolve(row, WorklistResolutions.Dismissed)}
                              disabled={resolving}
                              data-testid={`worklist-dismiss-${row.worklistId}`}
                            >
                              {resolving ? '...' : 'Afvis'}
                            </Button>
                            <Button size="sm" variant="ghost" onClick={cancelResolve} disabled={resolving}>
                              Annullér
                            </Button>
                          </div>
                        </div>
                      ) : (
                        <Button
                          size="sm"
                          variant="secondary"
                          onClick={() => startResolve(row.worklistId)}
                          data-testid={`worklist-resolve-open-${row.worklistId}`}
                        >
                          Løs...
                        </Button>
                      )}
                    </>
                  )}

                  {row.version != null && (
                    <Badge variant="default">v{row.version}</Badge>
                  )}
                </Card>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}
