// SPRINT-107 / TASK-10704 (Enhedsspor Phase 3b-1) — the AFGRÆNSNING scope control
// (the header trigger + its 340px popover) for the merged "Organisation &
// medarbejdere" admin page.
//
// [Step-0b — the OPTION SOURCE]: the ministerområde/organisation options derive
// from the ALREADY-scope-bounded `forest` the page loaded — NOT a fresh
// GET /api/admin/organizations(/tree). A scoped LocalHR therefore sees ONLY the
// orgs the forest admitted; the Afgrænsning can only NARROW that set, never widen
// it (ADR-038 D5 / P7). It is a pure VIEW filter.
//
// Tri-state ministerområde checkbox (✓ all / – some / empty none) over its
// organisations (indented checkboxes); "Vælg alle" / "Ryd" / "Anvend". The draft
// selection is local until "Anvend" applies it (or an outside-click / Esc reverts
// it), so an in-progress edit never filters the page mid-click.
//
// READ-only affordance: this NARROWS the view — it is not a mutation. Styling is
// tokens-not-hardcoded (per-type dots via the inherited --unit-accent-<type>
// page-root vars); square corners; Danish copy verbatim.

import { useEffect, useMemo, useRef, useState } from 'react'
import { Button } from '../../../components/ui'
import type { ForestMaoNode } from '../../../hooks/useForest'
import { collectOrgIds, summaryOf } from './afgraensning'
import styles from './AfgraensningControl.module.css'

interface AfgraensningControlProps {
  forest: ForestMaoNode[]
  /** The applied selection (null = all). */
  value: Set<string> | null
  /** Apply a new selection — normalized to null when it covers every org. */
  onChange: (next: Set<string> | null) => void
}

interface MaoOption {
  id: string
  name: string
  orgs: { id: string; name: string }[]
}

/** Derive the popover options straight from the scope-bounded forest (MAO → its
    Organisations) — the load-bearing "option source = the forest" rule. */
function maoOptions(forest: ForestMaoNode[]): MaoOption[] {
  return forest.map((mao) => ({
    id: mao.orgId,
    name: mao.orgName,
    orgs: mao.organisations.map((o) => ({ id: o.orgId, name: o.orgName })),
  }))
}

export function AfgraensningControl({ forest, value, onChange }: AfgraensningControlProps) {
  const allOrgIds = useMemo(() => collectOrgIds(forest), [forest])
  const options = useMemo(() => maoOptions(forest), [forest])

  const [open, setOpen] = useState(false)
  // The local working selection — initialized from the applied value each time the
  // popover opens, so an un-applied edit can be reverted by closing.
  const [draft, setDraft] = useState<Set<string>>(() => new Set(value ?? allOrgIds))
  const wrapRef = useRef<HTMLDivElement>(null)

  const openPopover = () => {
    setDraft(new Set(value ?? allOrgIds))
    setOpen(true)
  }

  // Outside-click + Esc close (reverting the un-applied draft).
  useEffect(() => {
    if (!open) return
    const onDocMouseDown = (e: MouseEvent) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setOpen(false)
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDocMouseDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('mousedown', onDocMouseDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  const toggleOrg = (orgId: string) =>
    setDraft((prev) => {
      const next = new Set(prev)
      if (next.has(orgId)) next.delete(orgId)
      else next.add(orgId)
      return next
    })

  const toggleMao = (mao: MaoOption) =>
    setDraft((prev) => {
      const next = new Set(prev)
      const allOn = mao.orgs.length > 0 && mao.orgs.every((o) => next.has(o.id))
      for (const o of mao.orgs) {
        if (allOn) next.delete(o.id)
        else next.add(o.id)
      }
      return next
    })

  const selectAll = () => setDraft(new Set(allOrgIds))
  const clear = () => setDraft(new Set())

  const apply = () => {
    onChange(draft.size >= allOrgIds.length ? null : new Set(draft))
    setOpen(false)
  }

  const summary = summaryOf(value, allOrgIds)
  const disabled = allOrgIds.length === 0

  return (
    <div className={styles.wrap} ref={wrapRef}>
      <button
        type="button"
        className={styles.trigger}
        data-testid="afgraensning-trigger"
        aria-haspopup="dialog"
        aria-expanded={open}
        disabled={disabled}
        onClick={() => (open ? setOpen(false) : openPopover())}
      >
        <span className={styles.triggerBody}>
          <span className={styles.triggerLabel}>Afgrænsning</span>
          <span className={styles.triggerSummary} data-testid="afgraensning-summary">
            {summary}
          </span>
        </span>
        <span className={styles.triggerCaret} aria-hidden="true">
          ▾
        </span>
      </button>

      {open && (
        <div className={styles.popover} role="dialog" aria-label="Vælg afgrænsning" data-testid="afgraensning-popover">
          <div className={styles.popHeader}>
            <span className={styles.popTitle}>Vælg afgrænsning</span>
            <div className={styles.popActions}>
              <button type="button" className={styles.linkPrimary} data-testid="afg-select-all" onClick={selectAll}>
                Vælg alle
              </button>
              <button type="button" className={styles.linkMuted} data-testid="afg-clear" onClick={clear}>
                Ryd
              </button>
            </div>
          </div>

          <div className={styles.popBody}>
            {options.map((mao) => {
              const cc = mao.orgs.filter((o) => draft.has(o.id)).length
              const all = mao.orgs.length > 0 && cc === mao.orgs.length
              const some = cc > 0 && !all
              const mark = all ? '✓' : some ? '–' : ''
              return (
                <div key={mao.id}>
                  <div
                    className={styles.maoRow}
                    role="checkbox"
                    aria-checked={all ? 'true' : some ? 'mixed' : 'false'}
                    tabIndex={0}
                    data-testid={`afg-mao-${mao.id}`}
                    onClick={() => toggleMao(mao)}
                    onKeyDown={(e) => {
                      if (e.key === ' ' || e.key === 'Enter') {
                        e.preventDefault()
                        toggleMao(mao)
                      }
                    }}
                  >
                    <span className={`${styles.box} ${all || some ? styles.boxOn : ''}`} aria-hidden="true">
                      {mark}
                    </span>
                    <span className={styles.maoDot} aria-hidden="true" style={{ background: 'var(--unit-accent-ministeromrade)' }} />
                    <span className={styles.maoName}>{mao.name}</span>
                  </div>

                  {mao.orgs.map((org) => {
                    const on = draft.has(org.id)
                    return (
                      <div
                        key={org.id}
                        className={styles.orgRow}
                        role="checkbox"
                        aria-checked={on}
                        tabIndex={0}
                        data-testid={`afg-org-${org.id}`}
                        onClick={() => toggleOrg(org.id)}
                        onKeyDown={(e) => {
                          if (e.key === ' ' || e.key === 'Enter') {
                            e.preventDefault()
                            toggleOrg(org.id)
                          }
                        }}
                      >
                        <span className={`${styles.boxSm} ${on ? styles.boxOn : ''}`} aria-hidden="true">
                          {on ? '✓' : ''}
                        </span>
                        <span className={styles.orgDot} aria-hidden="true" style={{ background: 'var(--unit-accent-organisation)' }} />
                        <span className={styles.orgName}>{org.name}</span>
                      </div>
                    )
                  })}
                </div>
              )
            })}
          </div>

          <div className={styles.popFooter}>
            <Button variant="primary" size="sm" onClick={apply} data-testid="afg-apply">
              Anvend
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
