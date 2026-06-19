// S86 / TASK-8601 — the inline (tree-row + orphan-card) "Godkendes af" write
// affordance. REUSES the drawer's ApproverSection verbatim as the single mutation
// core (no second save path): it owns the PersonPickerDialog + the assignManager /
// removeManager calls + its local optimistic mirror. This wrapper only:
//   1. LAZY-MOUNTS that core (the section, with its picker) on the row's
//      "Skift" / "+ Tildel godkender" trigger — the trigger itself renders
//      eagerly (cheap) so ~2000 rows don't each mount a section + picker.
//   2. RESOLVES the active PRIMARY reporting line's ETag via the shared
//      useResolveReportingLine BEFORE the section opens (the roster row has no
//      line version; first-assign → If-None-Match:*, reassign → If-Match) — the
//      same resolve the drawer does in LifecycleSections.
//
// The section's `local*` mirror is the source of truth between click and the
// roster refetch; on a successful mutation we bubble onChanged (→ the page
// refetches the roster) AND collapse back to the read-only trigger.
import { useCallback, useEffect, useMemo, useState } from 'react'
import { ApproverSection } from './ApproverSection'
import { useResolveReportingLine } from './useResolveReportingLine'

interface InlineApproverControlProps {
  employeeId: string
  personName: string
  /** The currently-assigned approver id + name (from the roster row / byId). */
  currentApproverId: string | null
  currentApproverName: string | null
  /** S86 — the cycle-prevention forbidden set (self + descendants), computed
      LAZILY only when the control is activated (the S77 O(n²) lesson — never
      build a child index per-row on every render of the ~2000-row tree). The page
      passes a thunk that runs `descendantsOf` once, on activation. */
  computeForbidden: () => Set<string>
  /** The trigger label/affordance — "Skift" (has approver) or "+ Tildel godkender". */
  trigger: 'change' | 'assign'
  /** Rendered after a successful assign/reassign/remove → the page refetches. */
  onChanged: () => void
  /** Rendered eagerly: the read-only trigger button (the affordance). */
  className?: string
}

export function InlineApproverControl({
  employeeId,
  personName,
  currentApproverId,
  currentApproverName,
  computeForbidden,
  trigger,
  onChanged,
  className,
}: InlineApproverControlProps) {
  const [active, setActive] = useState(false)
  const { resolve, resolved, resolving } = useResolveReportingLine()
  // Compute the forbidden set only once the control is active (lazy; S77 lesson).
  const forbidden = useMemo(
    () => (active ? computeForbidden() : new Set<string>()),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [active],
  )

  // On activation, resolve the active PRIMARY line's ETag (mirrors the drawer's
  // LifecycleSections resolve) before the section mounts.
  useEffect(() => {
    if (active) void resolve(employeeId)
  }, [active, employeeId, resolve])

  const handleChanged = useCallback(() => {
    onChanged()
    setActive(false)
  }, [onChanged])

  if (!active) {
    return (
      <button
        type="button"
        className={className}
        onClick={() => setActive(true)}
        data-testid={
          trigger === 'assign'
            ? `inline-approver-assign-${employeeId}`
            : `inline-approver-change-${employeeId}`
        }
      >
        {trigger === 'assign' ? '+ Tildel godkender' : 'Skift'}
      </button>
    )
  }

  // Lazy-mounted: the resolve is in flight until `resolved` is set.
  if (resolving || resolved === null) {
    return (
      <span className={className} data-testid={`inline-approver-loading-${employeeId}`}>
        Indlæser…
      </span>
    )
  }

  // Mount the SHARED ApproverSection with the resolved ETag + auto-open its
  // PersonPickerDialog (autoOpenPicker) so the row affordance is a SINGLE click:
  // "Skift"/"+ Tildel" → the picker opens directly. Dismissing the picker without
  // picking collapses back to the trigger (onPickerDismiss). The section owns the
  // assign/remove + its optimistic mirror; on success onChanged collapses + refetches.
  return (
    <span className={className} data-testid={`inline-approver-section-${employeeId}`}>
      <ApproverSection
        mode="edit"
        personName={personName}
        employeeId={employeeId}
        isRoot={false}
        currentApproverId={resolved.approverId ?? currentApproverId}
        currentApproverName={currentApproverName}
        currentReportingLineEtag={resolved.etag}
        forbidden={forbidden}
        onChanged={handleChanged}
        autoOpenPicker
        onPickerDismiss={() => setActive(false)}
      />
    </span>
  )
}
