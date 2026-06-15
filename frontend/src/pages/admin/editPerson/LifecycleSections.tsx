// S76b / TASK-7603 — the ledelseslinje / vikariering / fjern-medarbejder slot
// content for the unified EditPersonDrawer. Composes ApproverSection +
// VikarSection + DangerSection and supplies them their wiring context.
//
// EDIT mode: it RESOLVES the person's current PRIMARY approver (+ the line ETag
// for If-Match), whether they approve others (→ render the vikar section), and
// the active vikar — from the reporting-lines reads. A caller (7604, the tree)
// MAY pass a richer `context` (display names + descendants) which takes precedence
// so names render without a second lookup.
//
// CREATE mode: only the ApproverSection renders (a draft approver threaded into
// the create POST); vikar + delete are edit-only.
import { useCallback, useEffect, useState } from 'react'
import { useReportingLines } from '../../../hooks/useReportingLines'
import { ApproverSection } from './ApproverSection'
import { VikarSection, type ActiveVikar } from './VikarSection'
import { DangerSection } from './DangerSection'

/** Optional richer context the page (7604) can supply from the tree roster so
    names render immediately + the forbidden set is the exact descendant set. */
export interface LifecycleContext {
  isRoot?: boolean
  currentApproverId?: string | null
  currentApproverName?: string | null
  /** Whether the person approves anyone (→ show the vikar section). */
  approvesOthers?: boolean
  activeVikar?: ActiveVikar | null
  /** Self + descendants (the cycle-prevention forbidden set). */
  descendantIds?: Set<string>
}

interface LifecycleSectionsProps {
  mode: 'create' | 'edit'
  /** Edit mode — the person whose lifecycle is managed. */
  employeeId?: string
  personName: string
  /** Optional tree-supplied context (names + descendants). */
  context?: LifecycleContext
  /** Create mode — the draft approver state (threaded into the create POST). */
  draftApproverId?: string | null
  draftApproverName?: string | null
  onDraftApproverChange?: (id: string | null, name: string | null) => void
  /** Fired after an in-place mutation (assign/reassign/remove approver, vikar
      create/end) so the caller refetches the roster; the drawer stays open. */
  onMutated?: () => void
  /** Fired after the person is REMOVED from the afgrænsning — the caller should
      refetch AND close the drawer (the edited person no longer exists in scope). */
  onPersonRemoved?: () => void
  disabled?: boolean
}

export function LifecycleSections({
  mode,
  employeeId,
  personName,
  context,
  draftApproverId,
  draftApproverName,
  onDraftApproverChange,
  onMutated,
  onPersonRemoved,
  disabled = false,
}: LifecycleSectionsProps) {
  const { fetchEmployeeLines, fetchDirectReports, fetchActiveVikar } = useReportingLines()

  // Self-resolved edit-mode state (used when `context` does not supply it).
  const [resolvedApproverId, setResolvedApproverId] = useState<string | null>(null)
  const [resolvedEtag, setResolvedEtag] = useState<string | null>(null)
  const [resolvedApprovesOthers, setResolvedApprovesOthers] = useState(false)
  // BLOCKER 3 — the self-resolved active vikar, hydrated from the single-manager
  // GET when the tree `context.activeVikar` is absent (the UserManagement-list
  // entry point). `undefined` = not yet resolved; `null` = resolved-to-none.
  const [resolvedVikar, setResolvedVikar] = useState<ActiveVikar | null | undefined>(undefined)
  const [resolvedReady, setResolvedReady] = useState(false)

  const forbidden =
    context?.descendantIds ??
    (employeeId ? new Set<string>([employeeId]) : new Set<string>())

  // The tree caller may supply `activeVikar` directly; only self-resolve it when
  // absent (the UserManagement-list entry point). Captured outside the effect so
  // the dep array stays primitive-stable.
  const contextSuppliesVikar = context?.activeVikar !== undefined

  useEffect(() => {
    if (mode !== 'edit' || !employeeId) {
      setResolvedReady(true)
      return
    }
    let cancelled = false
    setResolvedReady(false)
    async function resolve() {
      const [lines, reports] = await Promise.all([
        fetchEmployeeLines(employeeId!),
        fetchDirectReports(employeeId!),
      ])
      if (cancelled) return
      if (lines.ok) {
        const primary = lines.data.active.find((l) => l.relationship === 'PRIMARY')
        setResolvedApproverId(primary?.managerId ?? null)
        setResolvedEtag(primary ? `"${primary.version}"` : null)
      }
      const approves = reports.ok && reports.data.some((r) => r.relationship === 'PRIMARY')
      if (reports.ok) setResolvedApprovesOthers(approves)
      // BLOCKER 3 — hydrate the active vikar from the single-manager GET when the
      // tree context did NOT supply it, but only if the person approves ≥1 report
      // (a non-approver has no vikar section, so the read would be wasted).
      if (!contextSuppliesVikar && approves) {
        const vikarResult = await fetchActiveVikar(employeeId!)
        if (cancelled) return
        setResolvedVikar(vikarResult.ok ? vikarResult.data.activeVikar : null)
      }
      setResolvedReady(true)
    }
    void resolve()
    return () => {
      cancelled = true
    }
  }, [mode, employeeId, contextSuppliesVikar, fetchEmployeeLines, fetchDirectReports, fetchActiveVikar])

  // Bubble a mutation to the caller AND re-resolve the local edit-mode context so
  // a follow-up action in the same session uses the fresh approver/etag/reports.
  const [refreshNonce, setRefreshNonce] = useState(0)
  const handleMutated = useCallback(() => {
    onMutated?.()
    setRefreshNonce((n) => n + 1)
  }, [onMutated])

  // Re-resolve on a mutation (edit mode, when self-resolving).
  useEffect(() => {
    if (refreshNonce === 0) return
    if (mode !== 'edit' || !employeeId) return
    let cancelled = false
    async function reresolve() {
      const [lines, reports] = await Promise.all([
        fetchEmployeeLines(employeeId!),
        fetchDirectReports(employeeId!),
      ])
      if (cancelled) return
      if (lines.ok) {
        const primary = lines.data.active.find((l) => l.relationship === 'PRIMARY')
        setResolvedApproverId(primary?.managerId ?? null)
        setResolvedEtag(primary ? `"${primary.version}"` : null)
      }
      const approves = reports.ok && reports.data.some((r) => r.relationship === 'PRIMARY')
      if (reports.ok) setResolvedApprovesOthers(approves)
      // BLOCKER 3 — re-hydrate the vikar after a mutation (e.g. an Afslut) so the
      // row reflects the fresh server state when self-resolving.
      if (!contextSuppliesVikar && approves) {
        const vikarResult = await fetchActiveVikar(employeeId!)
        if (cancelled) return
        setResolvedVikar(vikarResult.ok ? vikarResult.data.activeVikar : null)
      }
    }
    void reresolve()
    return () => {
      cancelled = true
    }
  }, [refreshNonce, mode, employeeId, contextSuppliesVikar, fetchEmployeeLines, fetchDirectReports, fetchActiveVikar])

  // `context` provides display sugar (name / isRoot / approvesOthers / descendants)
  // but the STRUCTURAL approver id + the line ETag (the If-Match token) ALWAYS come
  // from the reporting-lines read — the tree roster row carries no line version.
  const isRoot = context?.isRoot ?? false
  const approverId = resolvedApproverId
  const approverName = context?.currentApproverName ?? null
  const approvesOthers = context?.approvesOthers ?? resolvedApprovesOthers
  // BLOCKER 3 — the tree context wins when it supplied a vikar (even null); else
  // fall back to the self-resolved read (`undefined` until it resolves → null).
  const activeVikar = contextSuppliesVikar
    ? context?.activeVikar ?? null
    : resolvedVikar ?? null

  return (
    <>
      <ApproverSection
        mode={mode}
        personName={personName}
        employeeId={employeeId}
        isRoot={isRoot}
        currentApproverId={approverId}
        currentApproverName={approverName}
        currentReportingLineEtag={resolvedEtag}
        forbidden={forbidden}
        draftApproverId={draftApproverId}
        draftApproverName={draftApproverName}
        onDraftApproverChange={onDraftApproverChange}
        onChanged={handleMutated}
        disabled={disabled}
      />

      {mode === 'edit' && employeeId && resolvedReady && approvesOthers && (
        <VikarSection
          managerId={employeeId}
          managerName={personName}
          activeVikar={activeVikar}
          forbidden={forbidden}
          onChanged={handleMutated}
          disabled={disabled}
        />
      )}

      {mode === 'edit' && employeeId && (
        <DangerSection
          employeeId={employeeId}
          personName={personName}
          forbidden={forbidden}
          onRemoved={onPersonRemoved ?? onMutated}
          disabled={disabled}
        />
      )}
    </>
  )
}
