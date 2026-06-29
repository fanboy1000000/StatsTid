// SPRINT-107 / TASK-10702 (Enhedsspor Phase 3b-1) — the LEFT "ORGANISATIONSSTRUKTUR"
// tree of the merged "Organisation & medarbejdere" admin page.
//
// The design's indented, expandable org-structure tree (MAO → Organisation →
// units, nested via `children`). Units only — no people in the sidebar. The
// forest is ALREADY scope-bounded server-side (ADR-038 D5): this component
// renders EXACTLY what `useForest` returns — there is NO client-side scope logic
// and MAO ancestors are read-only context.
//
// READ + NAVIGATE ONLY (S91 dead-button discipline): the ONLY interactive
// affordances are the expand carets and row selection. NO Tilføj/Rediger/Slet/
// Flyt/drawer — those mutations are S108.
//
// Styling is tokens-not-hardcoded: the per-type dot colours reference the
// --unit-accent-<type> CSS custom properties declared on the page root (`.app` in
// OrganisationOgMedarbejdere.module.css), so the hex never appears here.

import { useState } from 'react'
import type { ForestMaoNode } from '../../../hooks/useForest'
import type { UnitType } from './typeMaps'
import styles from './OrgStructureTree.module.css'

/** What the tree lifts to the page on selection (the right detail panel —
    TASK-10703 — consumes this; for now the page renders `name` as a stopgap). */
export interface SelectedNode {
  /** orgId (MAO/Organisation) or unitId (unit). */
  id: string
  kind: 'mao' | 'organisation' | 'unit'
  name: string
  /** The unit-type key used for the accent token / type maps. MAO →
      'ministeromrade', Organisation → 'organisation', unit → its own type. */
  type: UnitType
}

/** A normalised tree node — the forest's three entity tiers (MAO / Organisation /
    unit) unified into one recursive shape the renderer walks. */
interface TreeNode extends SelectedNode {
  /** Composite key (`${kind}:${id}`) — unique across tiers, drives React keys +
      the per-node expand state. */
  key: string
  /** Deep member count (this node + all descendant units). */
  count: number
  children: TreeNode[]
}

/** Map the scope-bounded forest into the normalised tree (MAO → Org → units). */
function buildTree(forest: ForestMaoNode[]): TreeNode[] {
  const unitNode = (u: ForestMaoNode['organisations'][number]['units'][number]): TreeNode => ({
    id: u.unitId,
    kind: 'unit',
    name: u.name,
    type: u.type,
    key: `unit:${u.unitId}`,
    count: u.memberCount,
    children: u.children.map(unitNode),
  })

  return forest.map((mao) => ({
    id: mao.orgId,
    kind: 'mao',
    name: mao.orgName,
    type: 'ministeromrade' as UnitType,
    key: `mao:${mao.orgId}`,
    count: mao.memberCount,
    children: mao.organisations.map((org) => ({
      id: org.orgId,
      kind: 'organisation' as const,
      name: org.orgName,
      type: 'organisation' as UnitType,
      key: `organisation:${org.orgId}`,
      count: org.memberCount,
      children: org.units.map(unitNode),
    })),
  }))
}

interface FlatRow {
  node: TreeNode
  depth: number
  hasChildren: boolean
  open: boolean
}

interface OrgStructureTreeProps {
  forest: ForestMaoNode[]
  loading: boolean
  error: string | null
  /** The currently-selected node id (orgId or unitId), or null. */
  selectedId: string | null
  onSelect: (node: SelectedNode) => void
}

export function OrgStructureTree({
  forest,
  loading,
  error,
  selectedId,
  onSelect,
}: OrgStructureTreeProps) {
  // Per-node expand state, keyed by the composite key. A node with no explicit
  // entry defaults to OPEN for the two org tiers (so the units are visible at a
  // glance) and CLOSED for unit nodes (the deep sub-tree expands on demand).
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})

  const isOpen = (node: TreeNode): boolean =>
    expanded[node.key] ?? node.kind !== 'unit'

  const toggle = (node: TreeNode) =>
    setExpanded((prev) => ({ ...prev, [node.key]: !isOpen(node) }))

  // Lift ONLY the SelectedNode contract (not the tree's internal key/count/
  // children) so the page/TASK-10703 consumes a stable, minimal shape.
  const select = (node: TreeNode) =>
    onSelect({ id: node.id, kind: node.kind, name: node.name, type: node.type })

  if (loading) {
    return <div className={styles.status} data-testid="tree-loading">Indlæser organisationsstruktur…</div>
  }
  if (error) {
    return (
      <div className={styles.statusError} role="alert" data-testid="tree-error">
        Kunne ikke indlæse organisationsstrukturen.
      </div>
    )
  }

  const tree = buildTree(forest)
  if (tree.length === 0) {
    return <div className={styles.status} data-testid="tree-empty">Ingen organisationer i afgrænsningen.</div>
  }

  // Flatten to an ordered list, honouring each node's expand state.
  const rows: FlatRow[] = []
  const walk = (node: TreeNode, depth: number) => {
    const hasChildren = node.children.length > 0
    const open = hasChildren && isOpen(node)
    rows.push({ node, depth, hasChildren, open })
    if (open) node.children.forEach((c) => walk(c, depth + 1))
  }
  tree.forEach((n) => walk(n, 0))

  return (
    <div role="tree" aria-label="Organisationsstruktur" data-testid="org-structure-tree">
      {rows.map(({ node, depth, hasChildren, open }) => {
        const selected = node.id === selectedId
        return (
          <div
            key={node.key}
            role="treeitem"
            aria-selected={selected}
            aria-expanded={hasChildren ? open : undefined}
            data-testid={`tree-row-${node.id}`}
            className={selected ? `${styles.row} ${styles.rowSelected}` : styles.row}
            style={{ paddingLeft: `${8 + depth * 15}px` }}
            onClick={() => select(node)}
          >
            {hasChildren ? (
              <button
                type="button"
                className={styles.caret}
                aria-label={open ? 'Skjul' : 'Udvid'}
                data-testid={`tree-caret-${node.id}`}
                onClick={(e) => {
                  e.stopPropagation()
                  toggle(node)
                }}
              >
                {open ? '▾' : '▸'}
              </button>
            ) : (
              <span className={styles.caretSpacer} aria-hidden="true" />
            )}

            <span
              className={styles.dot}
              aria-hidden="true"
              data-testid={`tree-dot-${node.id}`}
              style={{ backgroundColor: `var(--unit-accent-${node.type})` }}
            />

            <span className={selected ? `${styles.name} ${styles.nameSelected}` : styles.name}>
              {node.name}
            </span>

            <span className={styles.spacer} />

            <span className={styles.count} data-testid={`tree-count-${node.id}`}>
              {node.count}
            </span>
          </div>
        )
      })}
    </div>
  )
}
