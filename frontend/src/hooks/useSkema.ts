import { useState, useEffect, useLayoutEffect, useCallback, useRef } from 'react'
import { apiClient } from '../lib/api'
import type { SkemaMonthData, SkemaRow, WorkTimeDay, WorkTimeInterval } from '../types'

/** S147 / QUAL-241 fix-up 2 — a failed ACTION (save / approve-send / reopen), as opposed to a
    failed month LOAD (`error`). The page words each kind in Danish and shows it in its own
    dismissible alert; each kind clears on the next success of the same kind. */
export type SkemaActionErrorKind = 'save' | 'approve' | 'reopen'
export interface SkemaActionError {
  kind: SkemaActionErrorKind
  message: string
  /** The month the failed action belonged to (so the page can say which month it is about). */
  year: number
  month: number
  /** For a failed save: the days it carried (YYYY-MM-DD), so the alert can name them. */
  dates?: string[]
  /** True when it is unknown whether the action took effect (a request that never answered). */
  uncertain?: boolean
}

/** S147 TASK-14720 / QUAL-241 — a save that was DROPPED (its month had left the screen when it
    failed, so it is never resent). One notice per month, with every dropped day merged into it.
    Kept apart from the single retryable `actionError` slot: nothing replaces it, and it is cleared
    only by dismissal or by a later successful save of the SAME month that covers every listed day. */
export interface DroppedSaveNotice {
  year: number
  month: number
  /** Every day (YYYY-MM-DD) whose changes were dropped, merged and sorted. */
  dates: string[]
  /** The server's message for the latest dropped failure. */
  message: string
}

/** A fully classified save: what to POST, and for which month. Built when the edit is QUEUED. */
export interface SaveBatch {
  year: number
  month: number
  entries: { date: string; projectCode: string; hours: number }[] | null
  absences: { date: string; absenceType: string; hours: number }[] | null
  workTime: WorkTimeDay[] | null
  /** Asked when the save FAILS: true when the page has meanwhile dropped this save's edits (the
      data it was sent under has been replaced), so the failure gets a persistent notice instead of
      the clearable slot. Absent = not dropped. */
  isDropped?: () => boolean
}

export interface QuotaError {
  absenceType: string
  remaining: number
  requested: number
}

/**
 * S73 R4 — the save-month outcome, discriminated so the page's `fireCellSave`
 * can split the rejected-save policy: a `rejected` (HTTP 422) delta REVERTS the
 * affected cells to server truth (with the overlap guard) and counts RESOLVED
 * for the flush; every `failed` (400/401/403/404/409/5xx/network) keeps the
 * local edits + the B2 re-queue/retry posture (401/403 are credential-shaped —
 * a mid-edit session expiry never discards typed cells).
 */
export type SaveMonthResult =
  | { status: 'ok' }
  | { status: 'rejected'; httpStatus: number }
  | { status: 'failed'; httpStatus: number; error: string }

/**
 * S73 R4 — the typed body of an `absence_full_day_only` 422 (TASK-7301's served
 * shape) and the `absence_type_not_eligible` family. Surfaced as a Danish alert.
 */
export interface AbsenceRuleError {
  error: 'absence_full_day_only' | 'absence_type_not_eligible'
  absenceType: string
  date?: string
  requiredHours?: number
  message?: string
}

/**
 * S123 / TASK-12303 — the typed body of the daily-norm ABSENCE cap 422
 * (`SkemaEndpoints.cs:740` — SUM(absence.Hours) > RoundBasis(norm)). Discriminated
 * by SHAPE (`maxHours` + `totalHours` present, NO `absenceType`) rather than the
 * fragile prose `error` string, mirroring the `absenceRuleError` inline pattern.
 * Without this branch the cap 422 fell through to `setError`, which the page
 * swallows once data is loaded (no alert).
 */
export interface AbsenceCapError {
  date?: string
  totalHours: number
  maxHours: number
}

/**
 * 422 from the send command — discriminated union. S127 / TASK-12703 collapsed
 * both employee-facing routes (`POST /api/approval/send` and the by-id
 * `POST /api/approval/{periodId}/employee-approve`) onto ONE backend command,
 * so both arms raise the same two shapes.
 */
export interface CoverageValidationError {
  kind: 'coverage'
  missingDays: string[]
  coveredDays: number
  totalWorkdays: number
}

export interface AllocationUnbalancedDay {
  date: string
  worked: number
  allocated: number
  direction: 'under' | 'over'
}

export interface AllocationValidationError {
  kind: 'allocation'
  unbalancedDays: AllocationUnbalancedDay[]
}

export type ApprovalValidationError = CoverageValidationError | AllocationValidationError

// S127 / TASK-12707 — `deriveOkVersion` DELETED. It existed solely to fill the
// retired `POST /api/approval/submit` body's `okVersion` field, and it was a
// second, client-side copy of a rule the server owns (`OkVersionResolver`).
// `POST /api/approval/send` takes only {employeeId, year, month} and resolves
// the OK version — and the agreement code — server-side on BOTH adapters, so the
// mirror is gone rather than left exported with no caller.

interface UseSkemaResult {
  data: SkemaMonthData | null
  loading: boolean
  /** The month LOAD failed (the only thing this field means). */
  error: string | null
  /** The last failed save / approve / send / reopen, or null. */
  actionError: SkemaActionError | null
  /** Saves dropped after their month left the screen — one persistent notice per month. */
  droppedSaves: DroppedSaveNotice[]
  dismissDroppedSave: (year: number, month: number) => void
  /** Let the page report a failure it detected itself (e.g. a save that never answered). */
  reportActionError: (error: SkemaActionError) => void
  /** A load answered with the wrong month even after the one automatic retry (recoverable via refetch). */
  loadMismatch: boolean
  clearActionError: () => void
  quotaError: QuotaError | null
  /** S73 R4 — the typed `absence_full_day_only` / `absence_type_not_eligible`
      422 body, surfaced as a Danish alert; null when none. */
  absenceRuleError: AbsenceRuleError | null
  /** S123 — the shape-discriminated daily-norm ABSENCE cap 422, surfaced as a
      Danish alert; null when none. */
  absenceCapError: AbsenceCapError | null
  approvalValidationError: ApprovalValidationError | null
  clearQuotaError: () => void
  clearAbsenceRuleError: () => void
  clearAbsenceCapError: () => void
  clearApprovalValidationError: () => void
  refetch: () => Promise<void>
  /** S73 R4 — returns the discriminated outcome so the page can split revert (422)
      vs retry (other non-2xx) inside `fireCellSave`. */
  saveMonth: (
    cells: { rowKey: string; date: string; hours: number | null }[],
    workTime?: WorkTimeDay[],
  ) => Promise<SaveMonthResult>
  /** S147 fix-up 3 — POST an already classified batch under ITS OWN month envelope. */
  saveBatch: (batch: SaveBatch) => Promise<SaveMonthResult>
  employeeApprove: (periodId: string) => Promise<void>
  /** S127 / TASK-12707 — ZERO arguments. `/api/approval/send` takes only
      {employeeId, year, month}; org, agreement code and OK version are all
      server-resolved, so the old `(orgId, agreementCode)` pair is gone. */
  submitAndApprove: () => Promise<void>
  reopenPeriod: (periodId: string, reason?: string) => Promise<void>
}

/** Parse a 422 approval-gate body into a discriminated ApprovalValidationError, or null. */
function parseApprovalValidationError(raw: string): ApprovalValidationError | null {
  try {
    const body = JSON.parse(raw)
    if (body.kind === 'allocation' && Array.isArray(body.unbalancedDays)) {
      return { kind: 'allocation', unbalancedDays: body.unbalancedDays }
    }
    if (Array.isArray(body.missingDays)) {
      return {
        kind: 'coverage',
        missingDays: body.missingDays,
        coveredDays: body.coveredDays ?? 0,
        totalWorkdays: body.totalWorkdays ?? 0,
      }
    }
  } catch {
    // Not valid JSON
  }
  return null
}

// ── S120 / TASK-12001 — THE GRADUATION (the S116 promise honored) ────────────
// The skema month GET + save POST were the file's last two GRANDFATHERED
// legacy explicit-T calls; the S120 backend drain gave both spec-declared
// responses (`SkemaMonthResponse` / `SkemaSaveResponse`), so they now ride the
// TYPED spec-keyed forms like the rest of the file. The S116
// `SKEMA_MONTH_PATH`/`SKEMA_SAVE_PATH` route-helper pins and BOTH eslint
// carve-out rules were DELETED — this file is on the FULL lint tier.

/** The distinct days a batch carries, sorted. */
function batchDates(batch: SaveBatch): string[] {
  const set = new Set<string>()
  for (const e of batch.entries ?? []) set.add(e.date)
  for (const a of batch.absences ?? []) set.add(a.date)
  for (const w of batch.workTime ?? []) set.add(w.date)
  return [...set].sort()
}

export function useSkema(
  employeeId: string,
  year: number,
  month: number,
  /** S147 fix-up 3 — resolves when the page's queued saves have settled (bounded). Every month
      load awaits it BEFORE its GET, so a reload can never show values older than a save the
      person has already made (March → April → March). */
  awaitSavesIdle?: () => Promise<void>,
): UseSkemaResult {
  const [data, setData] = useState<SkemaMonthData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<SkemaActionError | null>(null)
  const [quotaError, setQuotaError] = useState<QuotaError | null>(null)
  const [absenceRuleError, setAbsenceRuleError] = useState<AbsenceRuleError | null>(null)
  const [absenceCapError, setAbsenceCapError] = useState<AbsenceCapError | null>(null)
  const [approvalValidationError, setApprovalValidationError] = useState<ApprovalValidationError | null>(null)

  const [droppedSaves, setDroppedSaves] = useState<DroppedSaveNotice[]>([])
  // The identity of the month on screen NOW, read from a ref (written in a layout effect, which
  // runs before any passive effect or event handler).
  const identityRef = useRef({ employeeId, year, month })
  useLayoutEffect(() => {
    identityRef.current = { employeeId, year, month }
  }, [employeeId, year, month])

  /** Merge a dropped save into its month's notice (one per month, days unioned). */
  const addDroppedSave = useCallback((y: number, m: number, dates: string[], message: string) => {
    setDroppedSaves((prev) => {
      const existing = prev.find((n) => n.year === y && n.month === m)
      if (!existing) return [...prev, { year: y, month: m, dates: [...new Set(dates)].sort(), message }]
      return prev.map((n) =>
        n === existing ? { ...n, dates: [...new Set([...n.dates, ...dates])].sort(), message } : n,
      )
    })
  }, [])
  const dismissDroppedSave = useCallback(
    (y: number, m: number) => setDroppedSaves((prev) => prev.filter((n) => !(n.year === y && n.month === m))),
    [],
  )

  const failAction = useCallback(
    (kind: SkemaActionErrorKind, message: string, y: number, m: number, dates?: string[], dropped?: boolean) => {
      const here = identityRef.current
      // A save the page has DROPPED (the data it was sent under has since been replaced, or its
      // month has left the screen) is never resent: it gets a persistent notice of its own
      // instead of the one clearable slot.
      if (kind === 'save' && dates && dates.length > 0 && (dropped || here.year !== y || here.month !== m)) {
        addDroppedSave(y, m, dates, message)
        return
      }
      setActionError({ kind, message, year: y, month: m, dates })
    },
    [addDroppedSave],
  )
  const actionErrorRef = useRef(actionError)
  useLayoutEffect(() => {
    actionErrorRef.current = actionError
  }, [actionError])
  // The "uvist" report overwrites the one slot. ALWAYS move a days-carrying save warning out of it
  // first (rather than only when its edits are certain to be dropped): the next hydration drops
  // re-queued edits anyway, and a notice kept a little longer (until a covering success or
  // dismissal) costs less than a lost-save warning that silently vanished.
  const reportActionError = useCallback(
    (e: SkemaActionError) => {
      const prev = actionErrorRef.current
      if (e.uncertain && prev?.kind === 'save' && !prev.uncertain && prev.dates && prev.dates.length > 0) {
        addDroppedSave(prev.year, prev.month, prev.dates, prev.message)
      }
      setActionError(e)
    },
    [addDroppedSave],
  )
  const [loadMismatch, setLoadMismatch] = useState(false)
  const awaitSavesIdleRef = useRef(awaitSavesIdle)
  useLayoutEffect(() => {
    awaitSavesIdleRef.current = awaitSavesIdle
  }, [awaitSavesIdle])
  // A failure clears only on a later success of the same kind FOR THE SAME MONTH (or when the
  // person dismisses it): a success in another month says nothing about the month that failed.
  const succeedAction = useCallback(
    (kind: SkemaActionErrorKind, y: number, m: number, coveredDates?: string[]) => {
      setActionError((prev) => (prev?.kind === kind && prev.year === y && prev.month === m ? null : prev))
      if (kind === 'save' && coveredDates) {
        // A dropped-save notice goes only when this success covers EVERY day it lists.
        const covered = new Set(coveredDates)
        setDroppedSaves((prev) =>
          prev.filter((n) => !(n.year === y && n.month === m && n.dates.every((d) => covered.has(d)))),
        )
      }
    },
    [],
  )
  // A retryable save failure whose month the person has since left is DROPPED by the page (its
  // pending edits are cleared when the other month hydrates): move it to the persistent notices
  // instead of leaving it in the one slot any later failure would overwrite.
  useEffect(() => {
    if (
      actionError?.kind === 'save' && !actionError.uncertain && actionError.dates && actionError.dates.length > 0 &&
      (actionError.year !== year || actionError.month !== month)
    ) {
      addDroppedSave(actionError.year, actionError.month, actionError.dates, actionError.message)
      const moved = actionError
      setActionError((p) => (p === moved ? null : p)) // clear only the error acted on
    }
  }, [actionError, year, month, addDroppedSave])
  // Every month load that lands re-hydrates the page, which drops the edits a failed save had
  // re-queued. So a save-failure warning that carries days (one the person could still act on, not
  // an "uvist" one) must not stay in the clearable slot across a hydration, even for the SAME month
  // (March → April → March while the save was still in flight): it becomes a persistent notice then.
  useEffect(() => {
    const e = actionErrorRef.current
    if (data && e?.kind === 'save' && !e.uncertain && e.dates && e.dates.length > 0) {
      addDroppedSave(e.year, e.month, e.dates, e.message)
      setActionError((p) => (p === e ? null : p)) // clear only the error acted on
    }
  }, [data, addDroppedSave])
  const clearActionError = useCallback(() => setActionError(null), [])

  const latestDataRequestId = useRef(0)
  /** The identity the one automatic wrong-month retry has already been spent on. */
  const backstopTriedRef = useRef<string | null>(null)
  /** True from the moment a load is issued until the LATEST load settles (set synchronously). */
  const loadInFlightRef = useRef(false)

  // S147 / QUAL-241 fix-up 2 — a (re)load targets the identity that is CURRENT when it is
  // called, read from a ref, never the identity captured when the callback was created. The
  // reloads after approve / send / reopen / preference writes are issued from callbacks that
  // were made for the month the action started in; if the person has navigated meanwhile, a
  // captured identity would reload the OLD month, supersede the new month's own load (one
  // request counter) and leave the old month's data under the new month's title. The ref is
  // written in a layout effect, which runs before the passive effect that triggers the load.
  const fetchData = useCallback(async () => {
    const requestId = ++latestDataRequestId.current
    loadInFlightRef.current = true
    setLoading(true)
    setError(null)
    setLoadMismatch(false)
    // Saves the person has already made are ordered BEFORE this read.
    if (awaitSavesIdleRef.current) await awaitSavesIdleRef.current()
    // The identity is read AFTER the wait, so the load targets the month that is current now.
    const { employeeId: emp, year: y, month: m } = identityRef.current
    const result = await apiClient.get('/api/skema/{employeeId}/month', {
      params: { path: { employeeId: emp } },
      query: { year: y, month: m },
    })
    // S126 / F2 — a newer request superseded this one while it was in flight; drop it.
    if (requestId !== latestDataRequestId.current) return
    loadInFlightRef.current = false
    if (result.ok) {
      setData(result.data)
      // Decided HERE, not from a later render: a fast answer can leave the state unchanged (so
      // no render follows). A wrong-month answer after the retry has been spent is reported.
      if ((result.data.year !== y || result.data.month !== m) && backstopTriedRef.current === `${emp}:${y}-${m}`) {
        setLoadMismatch(true)
      }
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  useEffect(() => {
    fetchData()
  }, [fetchData, employeeId, year, month])

  // Backstop: if a load has settled successfully but the data held is NOT the month asked for
  // (a stray reload for another month), load the month that is asked for — once per month, so
  // a server that really answers with another month cannot cause a request loop. Declared after
  // the load effect, which has already marked a load in flight in the render right after a
  // month change (when `loading` is still false), so that render does not double-fetch.
  useEffect(() => {
    if (loading || error || loadInFlightRef.current || !data) return
    if (data.year === year && data.month === month) return
    const key = `${employeeId}:${year}-${month}`
    if (backstopTriedRef.current === key) {
      // The one automatic retry has already been spent: say so (the page offers "Prøv igen").
      setLoadMismatch(true)
      return
    }
    backstopTriedRef.current = key
    void fetchData()
  }, [data, loading, error, employeeId, year, month, fetchData])

  const clearQuotaError = useCallback(() => setQuotaError(null), [])
  const clearAbsenceRuleError = useCallback(() => setAbsenceRuleError(null), [])
  const clearAbsenceCapError = useCallback(() => setAbsenceCapError(null), [])
  const clearApprovalValidationError = useCallback(() => setApprovalValidationError(null), [])

  const absenceTypesRef = useRef<Set<string>>(new Set())
  useEffect(() => {
    absenceTypesRef.current = new Set(data?.absenceTypes?.map(a => a.type) ?? [])
  }, [data])

  const saveBatch = useCallback(
    async (batch: SaveBatch): Promise<SaveMonthResult> => {
      setQuotaError(null)
      setAbsenceRuleError(null)
      setAbsenceCapError(null)

      const result = await apiClient.post('/api/skema/{employeeId}/save', {
        params: { path: { employeeId } },
        body: {
          year: batch.year,
          month: batch.month,
          entries: batch.entries && batch.entries.length > 0 ? batch.entries : null,
          absences: batch.absences && batch.absences.length > 0 ? batch.absences : null,
          workTime: batch.workTime && batch.workTime.length > 0 ? batch.workTime : null,
        },
      })
      if (result.ok) {
        succeedAction('save', batch.year, batch.month, batchDates(batch))
        return { status: 'ok' }
      }
      // S73 R4 — a 422 is a server REJECTION (revert-and-resolve), discriminated
      // from every other non-2xx (retry). The 422 body carries one of three
      // shapes; parse each into its alert state. A non-JSON / unrecognised 422
      // still counts as a rejection (the cell reverts) but surfaces the raw text.
      if (result.status === 422) {
        try {
          const body = JSON.parse(result.error)
          if (body.absenceType && body.remaining !== undefined && body.requested !== undefined) {
            setQuotaError({
              absenceType: body.absenceType,
              remaining: body.remaining,
              requested: body.requested,
            })
          } else if (body.error === 'absence_full_day_only' || body.error === 'absence_type_not_eligible') {
            setAbsenceRuleError({
              error: body.error,
              absenceType: body.absenceType,
              date: body.date,
              requiredHours: body.requiredHours,
              message: body.message,
            })
          } else if (
            body.error === 'Total absence hours exceed norm day' &&
            body.maxHours !== undefined &&
            body.totalHours !== undefined
          ) {
            // S123 — the daily-norm ABSENCE cap 422. Anchored on the SPECIFIC
            // backend error string, not just the {maxHours,totalHours} shape: the
            // >24h work-time rejection (`work_time_exceeds_day`) carries the SAME
            // shape (maxHours:24, totalHours, no absenceType), so a shape-only match
            // mis-surfaced a work-time reject as the absence-cap alert ("fravær …
            // overstiger normtiden 24 t" — nonsense). Step-7a Reviewer WARNING. The
            // work-time 422 now falls through to the raw-text branch (its prior home).
            setAbsenceCapError({
              date: body.date,
              totalHours: body.totalHours,
              maxHours: body.maxHours,
            })
          } else {
            failAction('save', result.error, batch.year, batch.month, batchDates(batch), batch.isDropped?.())
          }
        } catch {
          // Non-JSON 422 — still a rejection; surface the raw text.
          failAction('save', result.error, batch.year, batch.month, batchDates(batch), batch.isDropped?.())
        }
        return { status: 'rejected', httpStatus: 422 }
      }
      // Everything else (400/401/403/404/409/5xx/network) — keep local edits, retry.
      failAction('save', result.error, batch.year, batch.month, batchDates(batch), batch.isDropped?.())
      return { status: 'failed', httpStatus: result.status, error: result.error }
    },
    [employeeId, failAction, succeedAction]
  )

  const saveMonth = useCallback(
    async (
      cells: { rowKey: string; date: string; hours: number | null }[],
      workTime?: WorkTimeDay[],
    ): Promise<SaveMonthResult> => {
      const absenceTypeSet = absenceTypesRef.current

      // Built via explicit narrowing loops (not filter+map) so the typed spec
      // request's `hours: number` holds without assertions — the emitted rows,
      // their key sets and their order are byte-identical to the legacy build.
      const entries: { date: string; projectCode: string; hours: number }[] = []
      const absences: { date: string; absenceType: string; hours: number }[] = []
      for (const c of cells) {
        if (c.hours == null || c.hours === 0) continue
        if (absenceTypeSet.has(c.rowKey)) {
          absences.push({ date: c.date, absenceType: c.rowKey, hours: c.hours })
        } else {
          entries.push({ date: c.date, projectCode: c.rowKey, hours: c.hours })
        }
      }
      return saveBatch({ year, month, entries, absences, workTime: workTime ?? null })
    },
    [saveBatch, year, month],
  )

  const employeeApprove = useCallback(
    async (periodId: string) => {
      const { year: ay, month: am } = identityRef.current
      // S116 typed switch — NAMED REQUEST DELTA: the legacy call sent a literal
      // `{}` body; the op binds NO request DTO (the handler takes only the
      // periodId route param — verified against ApprovalEndpoints.cs:1344), so
      // the typed form sends NO body. The backend never read it.
      const result = await apiClient.post('/api/approval/{periodId}/employee-approve', {
        params: { path: { periodId } },
      })
      if (result.ok) {
        setApprovalValidationError(null)
        succeedAction('approve', ay, am)
        await fetchData()
      } else {
        if (result.status === 422) {
          const validationError = parseApprovalValidationError(result.error)
          if (validationError) {
            setApprovalValidationError(validationError)
            await fetchData()
            return
          }
        }
        failAction('approve', result.error, ay, am)
        await fetchData()
      }
    },
    [fetchData, failAction, succeedAction]
  )

  /**
   * S127 / TASK-12707 — ONE call, was two.
   *
   * `POST /api/approval/submit` is RETIRED. `POST /api/approval/send` takes
   * {employeeId, year, month}, derives the month range server-side (the
   * caller-supplied range was a defect: this hook computed its own period
   * boundaries), validates coverage and allocation, and transitions straight to
   * `EMPLOYEE_APPROVED` inside one transaction.
   *
   * THE STRANDING BRANCH IS GONE, not merely its comment. The old flow could
   * create a `SUBMITTED` row and then fail the approve leg, leaving the month
   * half-sent — hence the old "period was created but not approved" note and the
   * defensive refetch that went with it. A single atomic command cannot produce
   * that state, so the condition it described no longer exists.
   *
   * THE 422 PARSE LIVES HERE NOW (AC-15). Previously the create leg's failure
   * path was a bare `setError(submitResult.error)` with NO parse: a coverage or
   * allocation rejection raised while creating the period never reached
   * `approvalValidationError`, so the page showed a raw JSON string instead of
   * the missing-days / unbalanced-days surface. With one call there is one
   * failure path, and both 422 shapes — `{kind:"allocation", unbalancedDays}`
   * and `{missingDays}` — route through `parseApprovalValidationError`, the same
   * parse `employeeApprove` uses.
   */
  const submitAndApprove = useCallback(
    async () => {
      setApprovalValidationError(null)

      const result = await apiClient.post('/api/approval/send', {
        body: { employeeId, year, month },
      })
      if (result.ok) {
        succeedAction('approve', year, month)
        await fetchData()
        return
      }
      if (result.status === 422) {
        const validationError = parseApprovalValidationError(result.error)
        if (validationError) {
          setApprovalValidationError(validationError)
          await fetchData()
          return
        }
      }
      failAction('approve', result.error, year, month)
      // Refetch on every failure: the send is atomic, but a 409 means someone
      // else moved the month (a concurrent send, or a manager decision), so the
      // grid must re-read rather than keep showing the pre-attempt state.
      await fetchData()
    },
    [employeeId, year, month, fetchData, failAction, succeedAction]
  )

  const reopenPeriod = useCallback(
    async (periodId: string, reason?: string) => {
      const { year: ry, month: rm } = identityRef.current
      const result = await apiClient.post('/api/approval/{periodId}/reopen', {
        params: { path: { periodId } },
        body: { reason: reason ?? 'Genåbnet af medarbejder' },
      })
      if (result.ok) {
        succeedAction('reopen', ry, rm)
        await fetchData()
      } else {
        failAction('reopen', result.error, ry, rm)
      }
    },
    [fetchData, failAction, succeedAction]
  )

  return { data, loading, error, actionError, droppedSaves, dismissDroppedSave, reportActionError, loadMismatch, clearActionError, quotaError, absenceRuleError, absenceCapError, approvalValidationError, clearQuotaError, clearAbsenceRuleError, clearAbsenceCapError, clearApprovalValidationError, refetch: fetchData, saveMonth, saveBatch, employeeApprove, submitAndApprove, reopenPeriod }
}

// ════════════════════════════════════════════════════════════════════════════
// S72 / TASK-7203 — pure work-time helpers (SPRINT-72 R16: useSkema is the single
// owner of the workTime payload builder and the day panel's period arithmetic).
// Everything below is a PURE module-level export — unit-testable without DOM.
//
// NOTE (R16 transition): SkemaPage.tsx still carries its own private
// buildWorkTimePayload callback; that file is 7205-owned and must compile
// UNCHANGED this sprint, so the hook export below is the PARALLEL single owner
// that 7205 swaps the page onto. The R7 pin (manualHours carry-forward) lives
// and is tested HERE.
// ════════════════════════════════════════════════════════════════════════════

/**
 * R7 — manualHours preservation. The ADR-028 work-time save is a latest-wins
 * whole-day replace (`{date, intervals, manualHours}`), so EVERY write that
 * round-trips a day's workTime MUST carry the day's EXISTING manualHours or the
 * write silently clobbers it. This builder is the one place that contract is
 * enforced: callers pass the dirty dates plus the CURRENT (server-hydrated)
 * interval/manual maps, and each emitted day carries
 * `manualHours: <the day's existing value>` (0 when the day has none — the save
 * contract's "absent" value, byte-identical to the shipped page behavior; R5/R17
 * freeze the contract itself).
 */
export function buildWorkTimePayload(
  dates: Iterable<string>,
  workIntervals: ReadonlyMap<string, readonly WorkTimeInterval[]>,
  manualHours: ReadonlyMap<string, number>,
): WorkTimeDay[] {
  return [...dates].map((date) => ({
    date,
    intervals: (workIntervals.get(date) ?? []).map((iv) => ({ start: iv.start, end: iv.end })),
    manualHours: manualHours.get(date) ?? 0,
  }))
}

/** A raw fra–til clock period as typed in the day panel (step 1). */
export interface ClockPeriod {
  from: string
  to: string
}

/** R6 intra-day warning threshold: worked > 9 t (handoff README:66). */
export const LONG_DAY_WARNING_HOURS = 9

/** S58 advisory mirror: a day's combined work time may not exceed 24 h. The
    backend 422 guard stays authoritative — this is inline advisory only. */
export const MAX_DAY_HOURS = 24

/** §J / SYSTEM_TARGET minimum daily rest between two working days, in hours. */
export const REST_MINIMUM_HOURS = 11

/**
 * Parse a clock-time string to minutes since midnight, or null when invalid.
 * Accepts the panel's raw typing forms ("9:05", "09:05", "09.05", "0905" —
 * prototype parseHM verbatim) plus the served "HH:mm:ss" interval format.
 */
function parseClockTime(value: string): number | null {
  const m = String(value).trim().match(/^(\d{1,2})[.:]?(\d{2})(?::(\d{2}))?$/)
  if (!m) return null
  const h = Number(m[1])
  const mi = Number(m[2])
  const s = m[3] ? Number(m[3]) : 0
  if (h > 23 || mi > 59 || s > 59) return null
  return h * 60 + mi + s / 60
}

function round2(n: number): number {
  return Math.round(n * 100) / 100
}

/**
 * EXACT single-period hours at 2-decimal rounding, or null when the period is
 * unparsable, reversed, or zero-length ("ugyldig" in the panel). R5: the
 * handoff's 0,1-rounding is DISPLAY-only — comparisons against the 0.005
 * allocation tolerance always use this exact 2-decimal value.
 */
export function periodHours(p: ClockPeriod): number | null {
  const f = parseClockTime(p.from)
  const t = parseClockTime(p.to)
  if (f === null || t === null || t <= f) return null
  return round2((t - f) / 60)
}

/**
 * EXACT day total over the VALID periods: minutes summed exactly, ONE 2-decimal
 * rounding at the end (matches the grid's calcIntervalHours basis — never a sum
 * of per-row-rounded values).
 */
export function periodsTotalHours(periods: readonly ClockPeriod[]): number {
  let totalMin = 0
  for (const p of periods) {
    const f = parseClockTime(p.from)
    const t = parseClockTime(p.to)
    if (f !== null && t !== null && t > f) totalMin += t - f
  }
  return round2(totalMin / 60)
}

/**
 * The day's worked total = clock periods + the day's existing manual hours
 * (owner ruling D-B: the manual-entry UI is dropped but existing manual_hours
 * keep counting in worked totals). Same basis as the grid's workedPerDay.
 */
export function dayWorkedHours(periods: readonly ClockPeriod[], manualHours: number): number {
  return round2(periodsTotalHours(periods) + manualHours)
}

/**
 * S58 mirror — indices of period rows that OVERLAP another valid row. Touching
 * boundaries (next.from === prev.to) are allowed; invalid/reversed rows are
 * ignored (they are flagged "ugyldig" separately). Mirrors the backend guard;
 * the backend 422 stays authoritative.
 */
export function overlappingPeriodIndices(periods: readonly ClockPeriod[]): Set<number> {
  const parsed: Array<{ i: number; f: number; t: number }> = []
  periods.forEach((p, i) => {
    const f = parseClockTime(p.from)
    const t = parseClockTime(p.to)
    if (f !== null && t !== null && t > f) parsed.push({ i, f, t })
  })
  parsed.sort((a, b) => a.f - b.f)
  const out = new Set<number>()
  if (parsed.length < 2) return out
  let maxEnd = parsed[0].t
  let maxEndIdx = parsed[0].i
  for (let k = 1; k < parsed.length; k++) {
    if (parsed[k].f < maxEnd) {
      out.add(parsed[k].i)
      out.add(maxEndIdx)
    }
    if (parsed[k].t > maxEnd) {
      maxEnd = parsed[k].t
      maxEndIdx = parsed[k].i
    }
  }
  return out
}

/** One §J adjacent-day rest violation (R6 trigger ii). */
export interface RestPeriodWarning {
  /** The exact rest gap in hours (2-decimal). */
  gapHours: number
  /** The earlier day — whose LAST interval end starts the rest gap ("YYYY-MM-DD"). */
  fromDate: string
  /** The later day — whose FIRST interval start ends the rest gap ("YYYY-MM-DD"). */
  toDate: string
}

function addDays(date: string, delta: number): string {
  const d = new Date(date + 'T00:00:00')
  d.setDate(d.getDate() + delta)
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${dd}`
}

/** First interval start / last interval end (minutes) over a day's VALID intervals. */
function dayClockBounds(wt: WorkTimeDay | undefined): { firstStart: number; lastEnd: number } | null {
  if (!wt) return null
  let firstStart = Infinity
  let lastEnd = -Infinity
  for (const iv of wt.intervals ?? []) {
    const s = parseClockTime(iv.start)
    const e = parseClockTime(iv.end)
    if (s !== null && e !== null && e > s) {
      if (s < firstStart) firstStart = s
      if (e > lastEnd) lastEnd = e
    }
  }
  return firstStart === Infinity ? null : { firstStart, lastEnd }
}

/**
 * R6 (trigger ii) — the SYSTEM_TARGET §J adjacent-day 11-hour rest analysis,
 * client-side and WARNING-ONLY (the rule engine stays definitive; the in-context
 * VoluntaryUnsocialHours choice is a recorded follow-up, deliberately absent).
 *
 * Checks the gap between the PREVIOUS calendar day's last interval end and the
 * given day's first interval start, and symmetrically toward the NEXT day. At
 * month edges the adjacent day resolves from the served `boundaryWorkTime`
 * (prev-month last day / next-month first day — month entries win on duplicate
 * dates). Days with no interval data impose no constraint; a day with no own
 * intervals returns null (nothing to anchor).
 *
 * Returns the violations (1–2 entries, previous-gap first) or null when none.
 */
export function analyzeRestPeriods(
  monthWorkTime: readonly WorkTimeDay[],
  boundaryWorkTime: readonly WorkTimeDay[] | undefined,
  date: string,
): RestPeriodWarning[] | null {
  const byDate = new Map<string, WorkTimeDay>()
  for (const wt of boundaryWorkTime ?? []) byDate.set(wt.date, wt)
  for (const wt of monthWorkTime) byDate.set(wt.date, wt) // month entries win
  const today = dayClockBounds(byDate.get(date))
  if (!today) return null

  const warnings: RestPeriodWarning[] = []
  const minutesInDay = 24 * 60
  const restMinimumMinutes = REST_MINIMUM_HOURS * 60

  const prevDate = addDays(date, -1)
  const prev = dayClockBounds(byDate.get(prevDate))
  if (prev) {
    const gapMin = minutesInDay - prev.lastEnd + today.firstStart
    if (gapMin < restMinimumMinutes) {
      warnings.push({ gapHours: round2(gapMin / 60), fromDate: prevDate, toDate: date })
    }
  }

  const nextDate = addDays(date, 1)
  const next = dayClockBounds(byDate.get(nextDate))
  if (next) {
    const gapMin = minutesInDay - today.lastEnd + next.firstStart
    if (gapMin < restMinimumMinutes) {
      warnings.push({ gapHours: round2(gapMin / 60), fromDate: date, toDate: nextDate })
    }
  }

  return warnings.length > 0 ? warnings : null
}

// ════════════════════════════════════════════════════════════════════════════
// S72 Step-7a fix-forward (B1 + W1) — the R3/R12 row basis and the R2 diff
// arithmetic each get ONE pure owner here, consumed by SkemaPage,
// ApprovalDetailPanel, SkemaGrid and useBalanceSummary.
// ════════════════════════════════════════════════════════════════════════════

/** S120 — the tolerant structural input for `deriveSkemaRowBasis`: the spec
    `SkemaMonthData` is assignable, and so are the pre-7201 fallback shapes the
    function has always handled (no `catalogs`, flag-less absence-type rows) —
    the tolerance that was previously encoded via the hand-written type's
    optional members now lives HERE instead of lying on the wire type. */
export interface SkemaRowBasisInput {
  projects?: readonly { projectCode: string; projectName: string }[]
  absenceTypes?: readonly { type: string; label: string; fullDayOnly?: boolean }[]
  entries?: readonly { projectCode: string | null }[]
  absences?: readonly { absenceType: string }[]
  catalogs?: {
    projects: readonly { projectCode: string; projectName: string }[]
    absenceTypes: readonly { type: string; label: string; fullDayOnly?: boolean }[]
  }
}

/** The R3/R12 ARITHMETIC/row basis derived from a served month (Step-7a B1). */
export interface SkemaRowBasis {
  /** ALL rows the arithmetic spans (projects first, then absences), render-ready
      for the no-preferences (R12 full-record) surfaces. */
  rows: SkemaRow[]
  /** ALL project keys in the basis (R3 — visibility-independent). */
  projectKeys: ReadonlySet<string>
  /** ALL absence-type keys in the basis (R3). */
  absenceKeys: ReadonlySet<string>
}

/**
 * S72 Step-7a B1 — R3/R12: the arithmetic/row basis is the UNION of the full
 * served `catalogs` and every key present in the month's served
 * `entries`/`absences`. The legacy `projects`/`absenceTypes` fields are the
 * VISIBLE selection for configured users (container-aware since 7201) and must
 * NEVER be the arithmetic basis on their own — hidden rows carrying hours would
 * vanish from I alt / remainder / panel-Resterende / gate mirroring. They are
 * folded in only as the pre-7201 fallback (no `catalogs` served) and as a belt.
 * Keys present in the served data but absent from every catalog (deactivated
 * projects with historical hours) are labeled by their code. RENDERING
 * visibility stays driven by `rowPreferences` — the grid filters rendering only.
 */
export function deriveSkemaRowBasis(data: SkemaRowBasisInput): SkemaRowBasis {
  const projectRows: SkemaRow[] = []
  const projectKeys = new Set<string>()
  const addProject = (key: string, label: string) => {
    if (projectKeys.has(key)) return
    projectKeys.add(key)
    projectRows.push({ type: 'project', key, label })
  }
  for (const p of data.catalogs?.projects ?? []) addProject(p.projectCode, p.projectName)
  for (const p of data.projects ?? []) addProject(p.projectCode, p.projectName)
  // Deactivated projects with historical hours: absent from every catalog —
  // they still carry served hours, so they enter the basis labeled by code.
  // S120 — the spec revealed `entries[].projectCode` NULLABLE (mapped from the
  // nullable time-entry taskId); the degenerate null key normalizes to '' HERE
  // and at the page's cell-map build (SkemaPage) — the SAME key at both sites,
  // so the day/total arithmetic still spans those hours.
  for (const e of data.entries ?? []) addProject(e.projectCode ?? '', e.projectCode ?? '')

  const absenceRows: SkemaRow[] = []
  const absenceKeys = new Set<string>()
  // S73 / TASK-7302 — the served full-day-only flag threads onto the absence row
  // (R3/R5): true → an entry SNAPS to the day's consumption basis + the "hele
  // dage" note renders. Sourced from the served absence-type DTOs (catalogs first,
  // then the visible-selection field). dedup keeps the FIRST-seen flag.
  const addAbsence = (key: string, label: string, fullDayOnly?: boolean) => {
    if (absenceKeys.has(key)) return
    absenceKeys.add(key)
    // Only attach the flag when TRUE — the emitted row shape stays byte-identical
    // to the pre-S73 contract for ordinary types (the S72 deriveSkemaRowBasis
    // pins toEqual exact objects). `undefined` reads falsy at the snap/note sites.
    absenceRows.push(
      fullDayOnly ? { type: 'absence', key, label, fullDayOnly: true } : { type: 'absence', key, label },
    )
  }
  for (const a of data.catalogs?.absenceTypes ?? []) addAbsence(a.type, a.label, a.fullDayOnly)
  for (const a of data.absenceTypes ?? []) addAbsence(a.type, a.label, a.fullDayOnly)
  // Served-history rows absent from every catalog: no DTO carries the flag → false.
  for (const a of data.absences ?? []) addAbsence(a.absenceType, a.absenceType)

  return { rows: [...projectRows, ...absenceRows], projectKeys, absenceKeys }
}

/** Inputs for the R2 month diff arithmetic (W1 — the ONE computation). */
export interface MonthDiffInputs {
  year: number
  month: number
  /** All cells, keyed `${rowKey}:${YYYY-MM-DD}` — the grid's `cellValues` basis. */
  cellValues: ReadonlyMap<string, number>
  /** ALL served project keys (R3 — visibility-independent; deriveSkemaRowBasis). */
  projectKeys: ReadonlySet<string>
  /** ALL served absence-type keys (R3). */
  absenceKeys: ReadonlySet<string>
  workIntervals: ReadonlyMap<string, readonly WorkTimeInterval[]>
  manualHours: ReadonlyMap<string, number>
  /** Served per-day norm (null = academic ANNUAL_ACTIVITY → the day is skipped, R1). */
  dailyNorm: ReadonlyMap<string, number | null>
}

/**
 * S72 Step-7a W1 — R2's SINGLE computation owner. Diff(day) = (workTime periods
 * + manualHours + absence hours) − served norm. A day is ABSENT from the result
 * (renders BLANK) when its norm is null/missing (R1) or when it carries NO
 * registration at all — no workTime, no absence, no allocation (the R2 pinned
 * pro-handoff change). Per-day values round to 2 decimals. The grid's Diff row,
 * the grid's trailing total AND the Flex card's "Denne måned"
 * (useBalanceSummary.computeMonthFlexDelta) all consume THIS computation —
 * there is no copy to drift.
 */
export function computeDayDiffs(inputs: MonthDiffInputs): Map<string, number> {
  const diffs = new Map<string, number>()
  const daysInMonth = new Date(inputs.year, inputs.month, 0).getDate()
  for (let d = 1; d <= daysInMonth; d++) {
    const dateKey = `${inputs.year}-${String(inputs.month).padStart(2, '0')}-${String(d).padStart(2, '0')}`
    const norm = inputs.dailyNorm.get(dateKey)
    if (norm === null || norm === undefined) continue
    const intervals = inputs.workIntervals.get(dateKey) ?? []
    const worked = round2(
      periodsTotalHours(intervals.map((iv) => ({ from: iv.start, to: iv.end }))) +
        (inputs.manualHours.get(dateKey) ?? 0),
    )
    let absence = 0
    for (const key of inputs.absenceKeys) {
      absence += inputs.cellValues.get(`${key}:${dateKey}`) ?? 0
    }
    absence = round2(absence)
    let allocated = 0
    for (const key of inputs.projectKeys) {
      allocated += inputs.cellValues.get(`${key}:${dateKey}`) ?? 0
    }
    allocated = round2(allocated)
    if (worked === 0 && absence === 0 && allocated === 0) continue
    diffs.set(dateKey, round2(worked + absence - norm))
  }
  return diffs
}

/** R2 — the trailing Diff total / the Flex card's "Denne måned": Σ of the
    RENDERED per-day diffs (absent days contribute nothing), 2-decimal. */
export function computeMonthDiffTotal(diffs: ReadonlyMap<string, number>): number {
  let sum = 0
  for (const d of diffs.values()) sum += d
  return round2(sum)
}
