// SPRINT-107 / TASK-10703 (Enhedsspor Phase 3b-1) — the RIGHT detail panel of the
// merged "Organisation & medarbejdere" admin page: the recursive, foldable
// "Struktur" (units → leaders → their employees → child units), the title block,
// breadcrumb, back/forward, and the derived "Refererer opad til" strip.
//
// DATA = the S106 forest (structure + deep counts, already loaded by the page)
// MERGED with the lazy per-Organisation roster (people). The roster is grouped
// CLIENT-SIDE: per unit, its members are split into leaders (userId ∈ the unit's
// leaderIds) and their direct reports (members whose structuralApproverId == that
// leader); cross-unit exceptions, leaderless units and vikar are surfaced
// READ-ONLY.
//
// READ + NAVIGATE ONLY (S91 dead-button discipline). The ONLY interactive
// affordances here are: expansion carets (child unit / MEDARBEJDERE group / a
// leader's reports), the two VIEW toggles (Vis org. / Vis medarbejdere),
// breadcrumb + back/forward navigation, and "Åbn ›" unit navigation. There is NO
// mutation affordance — no + Medarbejder / + <ChildType> / Rediger / Slet /
// per-row Rediger › / person-name edit link / cross-unit "Ret" / leaderless
// "Tildel leder" / vikar-edit / drawer mount (all S108). Person & leader rows
// render but are NOT clickable-to-edit.
//
// Styling is tokens-not-hardcoded: per-type accent/tint come from the inherited
// --unit-accent-<type> / --unit-tint-<type> page-root vars; the design's amber /
// vikar status colours are declared as scoped CSS vars on .panel.

import { useEffect, useMemo, useState } from 'react'
import { Button } from '../../../components/ui'
import type { ForestMaoNode } from '../../../hooks/useForest'
import type {
  RosterResponse,
  RosterRow,
  RosterNameResolutionEntry,
} from '../../../hooks/useRoster'
import {
  buildForestIndex,
  descendantUnitIds,
  pathOf,
  type StrukturNode,
} from './forestIndex'
import type { SelectedNode } from './OrgStructureTree'
import { LABEL, type UnitType } from './typeMaps'
import styles from './StrukturPanel.module.css'

interface StrukturPanelProps {
  forest: ForestMaoNode[]
  selected: SelectedNode | null
  /** The lazy per-Organisation roster cache (useRoster). */
  rosterByOrg: Record<string, RosterResponse>
  rosterLoading: boolean
  /** Ask the page to ensure the roster for an Organisation is loaded (lazy). */
  onLoadRoster: (organisationId: string) => void
  /** Navigate to a node (pushes the back/forward history in the page). */
  onNavigate: (node: SelectedNode) => void
  canBack: boolean
  canForward: boolean
  onBack: () => void
  onForward: () => void
}

const MONTHS_DA = ['jan', 'feb', 'mar', 'apr', 'maj', 'jun', 'jul', 'aug', 'sep', 'okt', 'nov', 'dec']

function formatDate(iso: string | null): string {
  if (!iso) return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso)
  if (!m) return iso
  const day = parseInt(m[3], 10)
  const mon = MONTHS_DA[parseInt(m[2], 10) - 1] ?? ''
  return `${day}. ${mon} ${m[1]}`
}

function initials(name: string): string {
  const parts = (name || '').trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[parts.length - 1]?.[0] ?? '')).toUpperCase()
}

function shortName(name: string): string {
  const parts = (name || '').trim().split(/\s+/)
  return parts.length > 1 ? `${parts[0]} ${parts[parts.length - 1]}` : name
}

const ORG_MEMBER_KEY = '__ORG__'

/** The derived per-roster lookups the recursion needs. */
interface RosterIndex {
  /** rows keyed by unitId, plus ORG_MEMBER_KEY for the Organisation-homed
      (unit-less) rows. */
  rowsByUnit: Map<string, RosterRow[]>
  rowById: Map<string, RosterRow>
  /** a unit's aggregated designated leader ids (from any member row). */
  leaderIdsByUnit: Map<string, string[]>
  /** userId → the display names of the leaders this person stands in for
      (the inverse of outgoingVikar within the loaded set). */
  coveringByUser: Map<string, string[]>
  nameResolution: Record<string, RosterNameResolutionEntry>
}

function buildRosterIndex(roster: RosterResponse | undefined): RosterIndex {
  const rowsByUnit = new Map<string, RosterRow[]>()
  const rowById = new Map<string, RosterRow>()
  const leaderIdsByUnit = new Map<string, string[]>()
  const coveringByUser = new Map<string, string[]>()

  for (const row of roster?.employees ?? []) {
    const key = row.unitId ?? ORG_MEMBER_KEY
    const bucket = rowsByUnit.get(key)
    if (bucket) bucket.push(row)
    else rowsByUnit.set(key, [row])

    rowById.set(row.employeeId, row)
    if (row.unitId) leaderIdsByUnit.set(row.unitId, row.leaderIds)

    if (row.outgoingVikar) {
      const covered = coveringByUser.get(row.outgoingVikar.vikarUserId)
      if (covered) covered.push(row.displayName)
      else coveringByUser.set(row.outgoingVikar.vikarUserId, [row.displayName])
    }
  }

  return { rowsByUnit, rowById, leaderIdsByUnit, coveringByUser, nameResolution: roster?.nameResolution ?? {} }
}

// ── The flat ordered render-node list (the design's walkUnit) ──────────────────
type RenderNode =
  | { t: 'unit'; key: string; node: StrukturNode; depth: number; open: boolean; hasContent: boolean; leaderNames: string; count: number }
  | { t: 'medHeader'; key: string; unitId: string; depth: number; count: number; closed: boolean }
  | { t: 'leader'; key: string; row: RosterRow; depth: number; reportCount: number; collapsed: boolean; hasReports: boolean; coveringNames: string[] }
  | { t: 'employee'; key: string; row: RosterRow; depth: number; variant: 'report' | 'flat' | 'external'; externalLeaderName?: string; coveringNames: string[] }
  | { t: 'note'; key: string; depth: number; text: string }

function indent(depth: number, extra = 0): number {
  return 14 + depth * 20 + extra
}

export function StrukturPanel({
  forest,
  selected,
  rosterByOrg,
  rosterLoading,
  onLoadRoster,
  onNavigate,
  canBack,
  canForward,
  onBack,
  onForward,
}: StrukturPanelProps) {
  const index = useMemo(() => buildForestIndex(forest), [forest])
  const selectedNode = selected ? index.byId.get(selected.id) ?? null : null
  const organisationId = selectedNode?.organisationId ?? null

  // Per-view UI state (independent pieces — the design's treeOpen / medClosed /
  // lCollapse / showPeople). All pure view-state, never mutations.
  const [treeOpen, setTreeOpen] = useState<Record<string, boolean>>({})
  const [medClosed, setMedClosed] = useState<Record<string, boolean>>({})
  const [lCollapse, setLCollapse] = useState<Record<string, boolean>>({})
  const [showPeople, setShowPeople] = useState(true)

  // Lazily ensure the selected node's Organisation roster is loaded.
  useEffect(() => {
    if (organisationId) onLoadRoster(organisationId)
  }, [organisationId, onLoadRoster])

  const roster = organisationId ? rosterByOrg[organisationId] : undefined
  const rosterIndex = useMemo(() => buildRosterIndex(roster), [roster])

  if (!selectedNode) {
    return (
      <div className={styles.panel} data-testid="struktur-panel">
        <p className={styles.empty}>Vælg en enhed i strukturen til venstre.</p>
      </div>
    )
  }

  // ── resolvers ───────────────────────────────────────────────────────────────
  const resolveName = (id: string): string =>
    rosterIndex.rowById.get(id)?.displayName ?? rosterIndex.nameResolution[id]?.displayName ?? '—'

  const resolveWhere = (id: string): string => {
    const r = rosterIndex.rowById.get(id)
    if (r) return [r.position, r.unitName].filter(Boolean).join(' · ')
    const nr = rosterIndex.nameResolution[id]
    if (nr) return [nr.position, nr.unitName].filter(Boolean).join(' · ')
    return ''
  }

  const membersOf = (node: StrukturNode): RosterRow[] =>
    rosterIndex.rowsByUnit.get(node.kind === 'unit' ? node.id : ORG_MEMBER_KEY) ?? []

  const leaderIdsOf = (node: StrukturNode): string[] =>
    node.kind === 'unit' ? rosterIndex.leaderIdsByUnit.get(node.id) ?? [] : []

  const leaderNamesOf = (node: StrukturNode): string => {
    const lset = new Set(leaderIdsOf(node))
    const names = membersOf(node)
      .filter((m) => lset.has(m.employeeId))
      .map((m) => shortName(m.displayName))
    return names.length ? names.join(', ') : '—'
  }

  const toSelected = (node: StrukturNode): SelectedNode => ({
    id: node.id,
    kind: node.kind,
    name: node.name,
    type: node.type,
  })

  // ── the recursive node list ──────────────────────────────────────────────────
  const nodes: RenderNode[] = []
  const walkUnit = (node: StrukturNode, depth: number) => {
    const members = membersOf(node)
    const leaderIdSet = new Set(leaderIdsOf(node))
    const leaderRows = members.filter((m) => leaderIdSet.has(m.employeeId))
    const nonLeaders = members.filter((m) => !leaderIdSet.has(m.employeeId))

    if (showPeople && members.length) {
      const closed = !!medClosed[node.id]
      nodes.push({ t: 'medHeader', key: `med:${node.id}`, unitId: node.id, depth, count: members.length, closed })

      if (!closed) {
        const reported = new Set<string>()
        for (const L of leaderRows) {
          const reps = nonLeaders.filter((m) => m.structuralApproverId === L.employeeId)
          reps.forEach((r) => reported.add(r.employeeId))
          const collapsed = !!lCollapse[L.employeeId]
          nodes.push({
            t: 'leader',
            key: `l:${L.employeeId}`,
            row: L,
            depth: depth + 1,
            reportCount: reps.length,
            collapsed,
            hasReports: reps.length > 0,
            coveringNames: rosterIndex.coveringByUser.get(L.employeeId) ?? [],
          })
          if (!collapsed) {
            reps.forEach((r) =>
              nodes.push({
                t: 'employee',
                key: `e:${r.employeeId}`,
                row: r,
                depth: depth + 1,
                variant: 'report',
                coveringNames: rosterIndex.coveringByUser.get(r.employeeId) ?? [],
              }),
            )
          }
        }

        const remaining = nonLeaders.filter((m) => !reported.has(m.employeeId))
        if (leaderRows.length === 0) {
          // Leaderless unit (members but no designated leaders) → READ-ONLY amber
          // note (NO "Tildel leder" — S108). Only for real units, not the
          // Organisation-homed group.
          if (node.kind === 'unit') {
            const upNames = [
              ...new Set(
                remaining
                  .map((m) => m.structuralApproverId)
                  .filter((id): id is string => !!id)
                  .map(resolveName),
              ),
            ]
            const n = remaining.length
            const refers = n === 1 ? 'medarbejder refererer' : 'medarbejdere refererer'
            const til = upNames.length ? ` til ${upNames.join(', ')}` : ''
            nodes.push({
              t: 'note',
              key: `note:${node.id}`,
              depth: depth + 1,
              text: `Ingen leder i enheden — ${n} ${refers} opad${til}.`,
            })
          }
          remaining.forEach((r) =>
            nodes.push({
              t: 'employee',
              key: `e:${r.employeeId}`,
              row: r,
              depth: depth + 1,
              variant: 'flat',
              coveringNames: rosterIndex.coveringByUser.get(r.employeeId) ?? [],
            }),
          )
        } else {
          remaining.forEach((m) => {
            const external = m.structuralApproverId != null && !leaderIdSet.has(m.structuralApproverId)
            nodes.push({
              t: 'employee',
              key: `e:${m.employeeId}`,
              row: m,
              depth: depth + 1,
              variant: external ? 'external' : 'flat',
              externalLeaderName: external ? resolveName(m.structuralApproverId!) : undefined,
              coveringNames: rosterIndex.coveringByUser.get(m.employeeId) ?? [],
            })
          })
        }
      }
    }

    for (const child of node.childUnits) {
      const hasContent = child.childUnits.length > 0 || child.memberCount > 0
      const open = !!treeOpen[child.id]
      nodes.push({
        t: 'unit',
        key: `u:${child.id}`,
        node: child,
        depth,
        open,
        hasContent,
        leaderNames: leaderNamesOf(child),
        count: child.memberCount,
      })
      if (open) walkUnit(child, depth + 1)
    }
  }
  walkUnit(selectedNode, 0)

  // ── derived header bits ──────────────────────────────────────────────────────
  const path = pathOf(index, selectedNode.id)
  const isMao = selectedNode.kind === 'mao'

  // "Refererer opad til" — the selected unit's leaders' distinct upward refs.
  const selLeaderIdSet = new Set(leaderIdsOf(selectedNode))
  const upRefIds = [
    ...new Set(
      membersOf(selectedNode)
        .filter((m) => selLeaderIdSet.has(m.employeeId))
        .map((m) => m.structuralApproverId)
        .filter((id): id is string => !!id),
    ),
  ]

  const childCount = selectedNode.childUnits.length
  const strTitle = isMao ? 'Organisationer' : 'Struktur'
  const strCountLabel = isMao
    ? `${childCount} ${childCount === 1 ? 'organisation' : 'organisationer'}`
    : `${selectedNode.memberCount} ${selectedNode.memberCount === 1 ? 'medarbejder' : 'medarbejdere'} · ` +
      `${childCount} ${childCount === 1 ? 'underenhed' : 'underenheder'}`

  const descendants = descendantUnitIds(selectedNode)
  const anyOpen = descendants.some((id) => treeOpen[id])
  const expandLabel = anyOpen ? 'Skjul org.' : 'Vis org.'
  const expandDisabled = descendants.length === 0
  const peopleLabel = showPeople ? 'Skjul medarbejdere' : 'Vis medarbejdere'

  const toggleExpandAll = () => {
    setTreeOpen((prev) => {
      const next = { ...prev }
      for (const id of descendants) {
        if (anyOpen) delete next[id]
        else next[id] = true
      }
      return next
    })
  }

  const waitingForRoster = !!organisationId && !roster && rosterLoading

  const chipStyle = (type: UnitType) => ({
    color: `var(--unit-accent-${type})`,
    background: `var(--unit-tint-${type})`,
  })

  // ── render ───────────────────────────────────────────────────────────────────
  return (
    <div className={styles.panel} data-testid="struktur-panel">
      {/* a. back / forward */}
      <div className={styles.navRow}>
        <Button variant="ghost" size="sm" disabled={!canBack} onClick={onBack} data-testid="nav-back">
          ‹ Tilbage
        </Button>
        <Button variant="ghost" size="sm" disabled={!canForward} onClick={onForward} data-testid="nav-forward">
          Frem ›
        </Button>
      </div>

      {/* b. breadcrumb path (navigation only) */}
      <nav className={styles.breadcrumb} aria-label="Sti" data-testid="breadcrumb">
        {path.map((node, i) => {
          const isLast = i === path.length - 1
          return (
            <span key={node.id} className={styles.crumbWrap}>
              {i > 0 && <span className={styles.crumbSep} aria-hidden="true"> › </span>}
              {isLast ? (
                <span className={styles.crumbCurrent} aria-current="page">{node.name}</span>
              ) : (
                <button
                  type="button"
                  className={styles.crumb}
                  data-testid={`crumb-${node.id}`}
                  onClick={() => onNavigate(toSelected(node))}
                >
                  {node.name}
                </button>
              )}
            </span>
          )
        })}
      </nav>

      {/* c. title block (type chip + name) — NO Rediger/Slet/+ actions (S108) */}
      <div className={styles.titleBlock}>
        <span className={styles.typeChip} style={chipStyle(selectedNode.type)} data-testid="title-type-chip">
          {LABEL[selectedNode.type]}
        </span>
        <h1 className={styles.title} data-testid="title-name">{selectedNode.name}</h1>
      </div>

      {/* d. "Refererer opad til" — READ-ONLY chips */}
      {upRefIds.length > 0 && (
        <div className={styles.upRef} data-testid="up-ref">
          <span className={styles.upRefLabel}>Refererer opad til</span>
          {upRefIds.map((id) => (
            <div key={id} className={styles.upRefChip} data-testid={`up-ref-${id}`}>
              <span className={styles.upRefAvatar} aria-hidden="true">{initials(resolveName(id))}</span>
              <span className={styles.upRefBody}>
                <span className={styles.upRefName}>{resolveName(id)}</span>
                <span className={styles.upRefWhere}>{resolveWhere(id)}</span>
              </span>
            </div>
          ))}
        </div>
      )}

      {/* e. the recursive Struktur */}
      <section className={styles.struktur}>
        <div className={styles.toolbar}>
          <div className={styles.toolbarTitle}>
            <span className={styles.strTitle}>{strTitle}</span>
            <span className={styles.strCount} data-testid="str-count">{strCountLabel}</span>
          </div>
          <div className={styles.toolbarActions}>
            <Button
              variant="ghost"
              size="sm"
              disabled={expandDisabled}
              onClick={toggleExpandAll}
              data-testid="toggle-expand-all"
            >
              {expandLabel}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setShowPeople((v) => !v)}
              data-testid="toggle-people"
            >
              {peopleLabel}
            </Button>
          </div>
        </div>

        <div className={styles.nodeList}>
          {waitingForRoster && (
            <div className={styles.status} data-testid="struktur-loading">Indlæser medarbejdere…</div>
          )}
          {!waitingForRoster && nodes.length === 0 && (
            <div className={styles.status} data-testid="struktur-empty">
              {isMao ? 'Ingen organisationer i afgrænsningen.' : 'Ingen medarbejdere eller underenheder endnu.'}
            </div>
          )}

          {nodes.map((n) => {
            if (n.t === 'unit') {
              return (
                <div
                  key={n.key}
                  className={styles.unitRow}
                  style={{ paddingLeft: `${indent(n.depth)}px` }}
                  data-testid={`unit-row-${n.node.id}`}
                >
                  {n.hasContent ? (
                    <button
                      type="button"
                      className={styles.caret}
                      aria-label={n.open ? 'Skjul' : 'Udvid'}
                      data-testid={`caret-unit-${n.node.id}`}
                      onClick={() => setTreeOpen((p) => ({ ...p, [n.node.id]: !n.open }))}
                    >
                      {n.open ? '▾' : '▸'}
                    </button>
                  ) : (
                    <span className={styles.caretSpacer} aria-hidden="true" />
                  )}
                  <span className={styles.dot} aria-hidden="true" style={{ background: `var(--unit-accent-${n.node.type})` }} />
                  <span className={styles.unitName}>{n.node.name}</span>
                  <span className={styles.typeChipSm} style={chipStyle(n.node.type)}>{LABEL[n.node.type]}</span>
                  <span className={styles.unitLeaders}>{n.leaderNames}</span>
                  <span className={styles.count}>{n.count}</span>
                  <button
                    type="button"
                    className={styles.openLink}
                    data-testid={`open-unit-${n.node.id}`}
                    onClick={() => onNavigate(toSelected(n.node))}
                  >
                    Åbn ›
                  </button>
                </div>
              )
            }

            if (n.t === 'medHeader') {
              return (
                <button
                  key={n.key}
                  type="button"
                  className={styles.medRow}
                  style={{ paddingLeft: `${indent(n.depth, 6)}px` }}
                  data-testid={`caret-med-${n.unitId}`}
                  onClick={() => setMedClosed((p) => ({ ...p, [n.unitId]: !n.closed }))}
                >
                  <span className={styles.caret} aria-hidden="true">{n.closed ? '▸' : '▾'}</span>
                  <span className={styles.medLabel}>Medarbejdere</span>
                  <span className={styles.count}>{n.count}</span>
                </button>
              )
            }

            if (n.t === 'leader') {
              const v = n.row.outgoingVikar
              return (
                <div
                  key={n.key}
                  className={styles.leaderRow}
                  style={{ paddingLeft: `${indent(n.depth, 6)}px` }}
                  data-testid={`leader-${n.row.employeeId}`}
                >
                  {n.hasReports ? (
                    <button
                      type="button"
                      className={styles.caret}
                      aria-label={n.collapsed ? 'Vis medarbejdere' : 'Skjul medarbejdere'}
                      data-testid={`caret-leader-${n.row.employeeId}`}
                      onClick={() => setLCollapse((p) => ({ ...p, [n.row.employeeId]: !n.collapsed }))}
                    >
                      {n.collapsed ? '▸' : '▾'}
                    </button>
                  ) : (
                    <span className={styles.caretSpacer} aria-hidden="true" />
                  )}
                  <span className={styles.leaderAvatar} aria-hidden="true">{initials(n.row.displayName)}</span>
                  <span className={styles.personBody}>
                    <span className={styles.personNameRow}>
                      <span className={styles.personName}>{n.row.displayName}</span>
                      {v && <span className={styles.fravBadge} data-testid={`fravaerende-${n.row.employeeId}`}>Fraværende</span>}
                    </span>
                    {n.row.position && <span className={styles.personTitle}>{n.row.position}</span>}
                    {v && (
                      <span className={styles.vikarLine} data-testid={`vikar-line-${n.row.employeeId}`}>
                        Vikar: {v.vikarDisplayName} · til {formatDate(v.untilDate)}
                      </span>
                    )}
                  </span>
                  <span className={styles.lederBadge}>Leder</span>
                  {n.coveringNames.length > 0 && (
                    <span className={styles.vikarForTag} data-testid={`vikar-for-${n.row.employeeId}`}>
                      Vikar for {n.coveringNames.join(', ')}
                    </span>
                  )}
                  <span className={styles.reportCount}>{n.reportCount} medarb.</span>
                </div>
              )
            }

            if (n.t === 'employee') {
              return (
                <div
                  key={n.key}
                  className={n.variant === 'external' ? `${styles.employeeRow} ${styles.employeeExternal}` : styles.employeeRow}
                  style={{ paddingLeft: `${indent(n.depth, n.variant === 'report' ? 34 : 6)}px` }}
                  data-testid={`employee-${n.row.employeeId}`}
                >
                  <span className={styles.employeeAvatar} aria-hidden="true">{initials(n.row.displayName)}</span>
                  <span className={styles.personBody}>
                    <span className={styles.personName}>{n.row.displayName}</span>
                    {n.row.position && <span className={styles.personTitle}>{n.row.position}</span>}
                  </span>
                  {n.variant === 'external' && (
                    <span className={styles.externalTag} data-testid={`external-${n.row.employeeId}`}>
                      Leder uden for enheden: {n.externalLeaderName}
                    </span>
                  )}
                  {n.coveringNames.length > 0 && (
                    <span className={styles.vikarForTag} data-testid={`vikar-for-${n.row.employeeId}`}>
                      Vikar for {n.coveringNames.join(', ')}
                    </span>
                  )}
                </div>
              )
            }

            // note (leaderless)
            return (
              <div
                key={n.key}
                className={styles.noteRow}
                style={{ paddingLeft: `${indent(n.depth, 6)}px` }}
                data-testid="leaderless-note"
              >
                {n.text}
              </div>
            )
          })}
        </div>
      </section>
    </div>
  )
}
