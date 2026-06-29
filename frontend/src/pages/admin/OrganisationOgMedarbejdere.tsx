import { useCallback, useEffect, useMemo, useState } from 'react'
import { Button } from '../../components/ui'
import { useAuth } from '../../contexts/AuthContext'
import { hasMinRole } from '../../lib/roles'
import { useForest } from '../../hooks/useForest'
import { useRoster } from '../../hooks/useRoster'
import { AfgraensningControl } from './enhedsspor/AfgraensningControl'
import { SearchOverlay } from './enhedsspor/SearchOverlay'
import { applyAfgraensning, collectOrgIds } from './enhedsspor/afgraensning'
import { OrgStructureTree, type SelectedNode } from './enhedsspor/OrgStructureTree'
import { StrukturPanel } from './enhedsspor/StrukturPanel'
import { MaoCreateAction } from './enhedsspor/OrgStructureDialogs'
import styles from './OrganisationOgMedarbejdere.module.css'

// SPRINT-107 — the merged "Organisation & medarbejdere" admin page
// (design_handoff_org_medarbejdere "Model A — Enhedsspor"): the VIEW/navigate
// half. The 3-region layout (Afgrænsning scope header + the left org-structure
// tree + the recursive right "Struktur").
//
//   • TASK-10702: the left org-structure tree (the S106 forest read).
//   • TASK-10703: the right recursive "Struktur" detail panel (forest + the lazy
//     per-Organisation roster), READ-ONLY, with breadcrumb + back/forward.
//   • TASK-10704 (THIS): the Afgrænsning scope popover + the search overlay.
//
// The Afgrænsning is a pure VIEW filter (afgraensning: Set<orgId> | null, null =
// all): its OPTION SOURCE is the already-scope-bounded forest (NOT a new org
// fetch — a scoped HR never sees an out-of-scope org), and it only NARROWS the
// server-admitted set (ADR-038 D5 / P7). The filtered forest feeds the tree +
// Struktur, with each MAO's rolled-up count RECOMPUTED from the kept orgs.
//
// No mutations / no dead buttons (S91 discipline): the search-result row
// NAVIGATES the tree/panel ONLY (no edit drawer — S108); the Afgrænsning narrows
// the view. The create/edit/delete drawers + cross-unit "Ret" + leaderless
// "Tildel leder" + vikar-edit are all S108.
//
// The page lives behind the LocalHR route gate in App.tsx (capability context is
// read at LocalHR floor; the rendered view is scope-bounded SERVER-SIDE by the
// S106 forest/roster/search reads — the Afgrænsning is a client-side NARROWING
// only, never a widening).
export function OrganisationOgMedarbejdere() {
  const { forest, loading, error, fetchForest } = useForest()
  const { byOrg, loading: rosterLoading, loadRoster, refetchRoster } = useRoster()

  // SPRINT-108 / TASK-10803 — the capability-gating spine. The unit-structure
  // affordances (in the StrukturPanel title block) gate on LocalHR (the live S104
  // floor); the org/MAO structure mutations (LocalAdmin / GlobalAdmin) are a
  // separate task. The FE gate is UX — the backend re-checks every mutation.
  const { role } = useAuth()
  const canEditUnits = hasMinRole(role, 'LocalHR')
  // SPRINT-108 / TASK-10802 — the top-level "+ Ministerområde" affordance (a MAO is
  // parent-less, so it lives in the tree header, not the node action row).
  // GlobalAdmin-gated; the backend re-checks HasGlobalScope on the create POST.
  const canCreateMao = hasMinRole(role, 'GlobalAdmin')

  // After a successful unit mutation, re-pull the forest (tree + Struktur) and the
  // affected Organisation's roster (people). Wired to the panel ONLY for a
  // permitted actor — a read-only actor never reaches a mutation.
  const onUnitMutated = useCallback(
    async (organisationId: string | null) => {
      await fetchForest()
      if (organisationId) await refetchRoster(organisationId)
    },
    [fetchForest, refetchRoster],
  )

  // The Afgrænsning scope selection (null = all visible orgs). The option source
  // + the "all" reference size derive from the unfiltered (server-admitted)
  // forest; the selection only ever narrows it.
  const allOrgIds = useMemo(() => collectOrgIds(forest), [forest])
  const [afgraensning, setAfgraensning] = useState<Set<string> | null>(null)
  const filteredForest = useMemo(
    () => applyAfgraensning(forest, afgraensning),
    [forest, afgraensning],
  )

  const [searchOpen, setSearchOpen] = useState(false)

  // The navigation history of selected nodes (back/forward). A single state
  // object keeps the stack + index in lock-step. The tree's onSelect, the detail
  // panel's breadcrumb / "Åbn ›", and the search result all push through `navigate`.
  const [nav, setNav] = useState<{ stack: SelectedNode[]; index: number }>({ stack: [], index: -1 })
  const selected = nav.index >= 0 ? nav.stack[nav.index] : null

  const navigate = useCallback((node: SelectedNode) => {
    setNav((prev) => {
      if (prev.index >= 0 && prev.stack[prev.index].id === node.id) return prev
      const truncated = prev.stack.slice(0, prev.index + 1)
      truncated.push(node)
      return { stack: truncated, index: truncated.length - 1 }
    })
  }, [])
  const goBack = useCallback(() => setNav((p) => (p.index > 0 ? { ...p, index: p.index - 1 } : p)), [])
  const goForward = useCallback(
    () => setNav((p) => (p.index < p.stack.length - 1 ? { ...p, index: p.index + 1 } : p)),
    [],
  )

  // The `/` shortcut opens the search overlay — but NOT while the actor is typing
  // in a field (an input/textarea/select/contenteditable), so `/` types normally
  // there. Esc-to-close is owned by the overlay.
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key !== '/' || e.defaultPrevented || searchOpen) return
      const t = e.target as HTMLElement | null
      const tag = t?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || t?.isContentEditable) return
      e.preventDefault()
      setSearchOpen(true)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [searchOpen])

  return (
    <div className={styles.app}>
      {/* ── Header: logo + title + Afgrænsning popover + Søg overlay trigger ── */}
      <header className={styles.header}>
        <div className={styles.headerLeft}>
          <div className={styles.logo} aria-hidden="true">
            St
          </div>
          <div className={styles.titleBlock}>
            <div className={styles.title}>Organisation &amp; medarbejdere</div>
            <div className={styles.subtitle}>Enhedsspor — organisationen er rygraden</div>
          </div>

          {/* The Afgrænsning scope popover — options derive from the forest. */}
          <AfgraensningControl forest={forest} value={afgraensning} onChange={setAfgraensning} />
        </div>

        <div className={styles.headerRight}>
          <Button
            variant="primary"
            size="md"
            data-testid="soeg-button"
            onClick={() => setSearchOpen(true)}
          >
            <span className={styles.soegGlyph} aria-hidden="true">
              ⌕
            </span>{' '}
            Søg
          </Button>
        </div>
      </header>

      {/* ── Body: left org-structure tree + right detail panel ── */}
      <div className={styles.body}>
        {/* TASK-10702: the left org-structure tree (Afgrænsning-narrowed forest). */}
        <aside className={styles.sidebar} aria-label="Organisationsstruktur">
          <div className={styles.sidebarHeader}>
            <div className={styles.sidebarLabel}>ORGANISATIONSSTRUKTUR</div>
            {canCreateMao && <MaoCreateAction onCreated={fetchForest} />}
          </div>
          <div className={styles.treeContainer} data-testid="tree-placeholder">
            <OrgStructureTree
              forest={filteredForest}
              loading={loading}
              error={error}
              selectedId={selected?.id ?? null}
              onSelect={navigate}
            />
          </div>
        </aside>

        {/* TASK-10703: the recursive right "Struktur" (Afgrænsning-narrowed forest). */}
        <main className={styles.detail} data-testid="detail-placeholder">
          <div className={styles.detailInner}>
            <StrukturPanel
              forest={filteredForest}
              selected={selected}
              rosterByOrg={byOrg}
              rosterLoading={rosterLoading}
              onLoadRoster={loadRoster}
              onNavigate={navigate}
              canBack={nav.index > 0}
              canForward={nav.index < nav.stack.length - 1}
              onBack={goBack}
              onForward={goForward}
              onMutated={canEditUnits ? onUnitMutated : undefined}
            />
          </div>
        </main>
      </div>

      {/* ── The search overlay (Søg / `/`; Esc / scrim closes) ── */}
      {searchOpen && (
        <SearchOverlay
          open
          onClose={() => setSearchOpen(false)}
          onNavigate={navigate}
          selected={afgraensning}
          allOrgIds={allOrgIds}
        />
      )}
    </div>
  )
}
