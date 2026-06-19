// S86 / TASK-8601 — the inline (tree-row) "+ Vikar" / "Afslut" write affordance.
// REUSES the drawer's VikarSection verbatim as the single mutation core (no second
// save path): it owns the createVikar / endVikar calls + the inline VikarForm +
// its local optimistic mirror. This wrapper only LAZY-MOUNTS that core on the
// row's eager trigger:
//   - mode 'create': a not-away manager → "+ Vikar" trigger → mounts the section
//     with its create form auto-opened (autoOpenForm).
//   - mode 'end': an away manager's inline vikar line → "Afslut" trigger →
//     mounts the section in its active-vikar state (it renders the "Afslut" button).
// On success the section bubbles onChanged (→ the page refetches the roster) and
// we collapse back to the trigger.
import { useCallback, useMemo, useState } from 'react'
import { VikarSection, type ActiveVikar } from './VikarSection'

interface InlineVikarControlProps {
  managerId: string
  managerName: string
  /** S86 — the cycle-prevention forbidden set (self + descendants), computed
      LAZILY only when the control is activated (the S77 O(n²) lesson — never build
      a child index per-row on every render of the ~2000-row tree). */
  computeForbidden: () => Set<string>
  /** 'create' → "+ Vikar"; 'end' → "Afslut" on the active vikar line. */
  mode: 'create' | 'end'
  /** Required for mode 'end' — the active vikar (from the roster's outgoingVikar). */
  activeVikar?: ActiveVikar | null
  onChanged: () => void
  className?: string
  /** Optional override for the trigger label (else "+ Vikar" / "Afslut"). */
  label?: string
}

export function InlineVikarControl({
  managerId,
  managerName,
  computeForbidden,
  mode,
  activeVikar,
  onChanged,
  className,
  label,
}: InlineVikarControlProps) {
  const [active, setActive] = useState(false)
  const forbidden = useMemo(
    () => (active ? computeForbidden() : new Set<string>()),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [active],
  )

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
          mode === 'create'
            ? `inline-vikar-add-${managerId}`
            : `inline-vikar-end-${managerId}`
        }
      >
        {label ?? (mode === 'create' ? '+ Vikar' : 'Afslut')}
      </button>
    )
  }

  return (
    <span data-testid={`inline-vikar-section-${managerId}`}>
      <VikarSection
        managerId={managerId}
        managerName={managerName}
        activeVikar={mode === 'end' ? activeVikar ?? null : null}
        forbidden={forbidden}
        onChanged={handleChanged}
        autoOpenForm={mode === 'create'}
        onCancel={() => setActive(false)}
      />
    </span>
  )
}
