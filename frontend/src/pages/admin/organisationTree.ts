// S99 / TASK-9902 — pure tree helpers for the Organisation page. The server
// serves an aggregated tree (MAO → Organisation → a NESTED Enhed sub-tree);
// these helpers flatten it to render rows, drive the level control + search, and
// supply the type-scoped move targets. No authority/scope logic lives here — an
// Enhed is PURE display metadata with ZERO authority (ADR-036); S100 makes the
// enheder HIERARCHICAL (`parentEnhedId` + a derived `level`), but the hierarchy
// never enters any scope/approval path.

import type {
  TreeMaoNode,
  TreeOrganisationNode,
  TreeEnhedNode,
} from '../../hooks/useOrganizationTree'

export type NodeType = 'MAO' | 'ORGANISATION' | 'ENHED'

/** A normalised, render-ready node — the union of the three tier shapes flattened
    to one row contract. `count` is the rolled-up employee count (own + descendants
    for MAO/Organisation) or the Enhed's `taggedUserCount`. `parentId` is the
    immediate parent's id (null for a MAO root). `hasChildren` drives the chevron.
    S100: `depth` continues past the Organisation for nested enheder; `level` is
    the Enhed's own depth within its Organisation (root enhed = 1, undefined for
    MAO/Organisation); `parentEnhedId` is the enhed's parent (null = a root enhed). */
export interface OrgRow {
  id: string
  name: string
  type: NodeType
  depth: number
  count: number
  parentId: string | null
  /** The owning Organisation id for an Enhed (used to resolve its version). */
  organisationId?: string
  /** The Enhed's parent enhed id (null = a root enhed); undefined for non-Enhed. */
  parentEnhedId?: string | null
  /** The Enhed's derived depth within its Organisation (root enhed = 1). */
  level?: number
  hasChildren: boolean
}

export const TYPE_LABEL: Record<NodeType, string> = {
  MAO: 'Ministeransvarsområde',
  ORGANISATION: 'Organisation',
  ENHED: 'Enhed',
}

export function maoRow(mao: TreeMaoNode): OrgRow {
  return {
    id: mao.orgId,
    name: mao.orgName,
    type: 'MAO',
    depth: 0,
    count: mao.employeeCount,
    parentId: null,
    hasChildren: (mao.organisations?.length ?? 0) > 0,
  }
}

export function organisationRow(org: TreeOrganisationNode): OrgRow {
  return {
    id: org.orgId,
    name: org.orgName,
    type: 'ORGANISATION',
    depth: 1,
    count: org.employeeCount,
    parentId: org.parentOrgId,
    hasChildren: (org.enheder?.length ?? 0) > 0,
  }
}

/** Render-row for one Enhed node. `depth` continues past the Organisation:
    Organisation = depth 1, a root enhed (level 1) = depth 2, a child (level 2) =
    depth 3, … (depth = 1 + level). `parentId` is the immediate parent — the
    parent enhed when nested, else the owning Organisation. */
export function enhedRow(enhed: TreeEnhedNode, org: TreeOrganisationNode): OrgRow {
  return {
    id: enhed.enhedId,
    name: enhed.name,
    type: 'ENHED',
    depth: 1 + enhed.level,
    count: enhed.taggedUserCount,
    parentId: enhed.parentEnhedId ?? org.orgId,
    organisationId: org.orgId,
    parentEnhedId: enhed.parentEnhedId ?? null,
    level: enhed.level,
    hasChildren: (enhed.children?.length ?? 0) > 0,
  }
}

/** Recursively push an enhed sub-tree's VISIBLE rows. An enhed's children show
    only when the enhed itself is expanded (same chevron discipline as the org
    tiers). */
function pushEnhedRows(
  enhed: TreeEnhedNode,
  org: TreeOrganisationNode,
  expanded: Set<string>,
  out: OrgRow[],
): void {
  out.push(enhedRow(enhed, org))
  if (!expanded.has(enhed.enhedId)) return
  for (const child of enhed.children ?? []) {
    pushEnhedRows(child, org, expanded, out)
  }
}

/** Flatten the tree to the rows currently VISIBLE given the expanded set. A MAO
    is always shown; its organisations show only when the MAO is expanded; an
    organisation's enheder show only when the organisation is expanded; a nested
    enhed's children show only when that enhed is expanded. */
export function visibleRows(
  tree: TreeMaoNode[],
  expanded: Set<string>,
): OrgRow[] {
  const out: OrgRow[] = []
  for (const mao of tree) {
    out.push(maoRow(mao))
    if (!expanded.has(mao.orgId)) continue
    for (const org of mao.organisations ?? []) {
      out.push(organisationRow(org))
      if (!expanded.has(org.orgId)) continue
      for (const enhed of org.enheder ?? []) {
        pushEnhedRows(enhed, org, expanded, out)
      }
    }
  }
  return out
}

/** Collect every enhed id in a sub-forest that has children (expandable). */
function expandableEnhedIds(enheder: TreeEnhedNode[] | undefined, out: string[]): void {
  for (const e of enheder ?? []) {
    if ((e.children?.length ?? 0) > 0) {
      out.push(e.enhedId)
      expandableEnhedIds(e.children, out)
    }
  }
}

/** Every expandable node id, flattened (for "expand to Enhed level" = expand all
    MAOs, organisations and intermediate enheder). */
export function allExpandableIds(tree: TreeMaoNode[]): string[] {
  const ids: string[] = []
  for (const mao of tree) {
    if ((mao.organisations?.length ?? 0) > 0) ids.push(mao.orgId)
    for (const org of mao.organisations ?? []) {
      if ((org.enheder?.length ?? 0) > 0) ids.push(org.orgId)
      expandableEnhedIds(org.enheder, ids)
    }
  }
  return ids
}

export type Level = 'MAO' | 'ORGANISATION' | 'ENHED'

/** The expanded set that realises a "Vis til niveau" depth:
      MAO          → roots only (collapse all)
      ORGANISATION → expand every MAO (show organisations) — the default
      ENHED        → expand everything (organisations + every intermediate enhed,
                     so the whole enhed sub-tree is visible) */
export function expandedForLevel(tree: TreeMaoNode[], level: Level): Set<string> {
  if (level === 'MAO') return new Set()
  const set = new Set<string>()
  for (const mao of tree) {
    set.add(mao.orgId)
    if (level === 'ENHED') {
      for (const org of mao.organisations ?? []) {
        set.add(org.orgId)
        const enhedIds: string[] = []
        expandableEnhedIds(org.enheder, enhedIds)
        for (const id of enhedIds) set.add(id)
      }
    }
  }
  return set
}

/** Recursively flatten an enhed sub-tree (search/flat mode, no expansion gate). */
function flattenEnheder(
  enheder: TreeEnhedNode[] | undefined,
  org: TreeOrganisationNode,
  out: OrgRow[],
): void {
  for (const enhed of enheder ?? []) {
    out.push(enhedRow(enhed, org))
    flattenEnheder(enhed.children, org, out)
  }
}

/** Flatten EVERY node (search mode renders a flat filtered list, no chevrons). */
export function flattenAll(tree: TreeMaoNode[]): OrgRow[] {
  const out: OrgRow[] = []
  for (const mao of tree) {
    out.push(maoRow(mao))
    for (const org of mao.organisations ?? []) {
      out.push(organisationRow(org))
      flattenEnheder(org.enheder, org, out)
    }
  }
  return out
}

/** Case-insensitive substring search over the flattened node names. */
export function searchRows(tree: TreeMaoNode[], query: string): OrgRow[] {
  const q = query.trim().toLowerCase()
  if (!q) return []
  return flattenAll(tree).filter((r) => r.name.toLowerCase().includes(q))
}

/** Active MAO targets for a move: every MAO EXCEPT the moving org's current
    parent (pre-excluded so the picker never offers a guaranteed-no-op 400). */
export function moveTargets(
  tree: TreeMaoNode[],
  currentParentId: string | null,
): { orgId: string; orgName: string }[] {
  return tree
    .filter((mao) => mao.orgId !== currentParentId)
    .map((mao) => ({ orgId: mao.orgId, orgName: mao.orgName }))
}

// ── S100: Enhed move-target computation ──────────────────────────────────────

/** Locate one Organisation node anywhere in the tree by its id. */
export function findOrganisation(
  tree: TreeMaoNode[],
  organisationId: string,
): TreeOrganisationNode | null {
  for (const mao of tree) {
    for (const org of mao.organisations ?? []) {
      if (org.orgId === organisationId) return org
    }
  }
  return null
}

/** The set of enhed ids that are the moving enhed ITSELF or any of its
    descendants (an invalid move target — moving under one would form a cycle).
    Computed by locating the enhed in the Organisation's nested forest. */
export function enhedSelfAndDescendantIds(
  enheder: TreeEnhedNode[] | undefined,
  enhedId: string,
): Set<string> {
  const out = new Set<string>()
  const collect = (node: TreeEnhedNode) => {
    out.add(node.enhedId)
    for (const c of node.children ?? []) collect(c)
  }
  const find = (nodes: TreeEnhedNode[] | undefined): boolean => {
    for (const n of nodes ?? []) {
      if (n.enhedId === enhedId) {
        collect(n)
        return true
      }
      if (find(n.children)) return true
    }
    return false
  }
  find(enheder)
  return out
}

/** One selectable move target for an enhed: another enhed in the SAME
    Organisation that is neither the enhed itself nor one of its descendants.
    `depth` is the target's enhed `level` (used to indent the option label). */
export interface EnhedMoveTarget {
  enhedId: string
  name: string
  level: number
}

/** The valid re-parent targets for `movingEnhedId` within `org`: every other
    active enhed in the Organisation EXCLUDING the enhed itself + its descendants
    (a cycle) AND excluding its CURRENT parent (a guaranteed no-op). The caller
    prepends a "→ root" option separately. */
export function enhedMoveTargets(
  org: TreeOrganisationNode | null,
  movingEnhedId: string,
  currentParentEnhedId: string | null,
): EnhedMoveTarget[] {
  if (!org) return []
  const excluded = enhedSelfAndDescendantIds(org.enheder, movingEnhedId)
  const out: EnhedMoveTarget[] = []
  const walk = (nodes: TreeEnhedNode[] | undefined) => {
    for (const n of nodes ?? []) {
      if (!excluded.has(n.enhedId) && n.enhedId !== currentParentEnhedId) {
        out.push({ enhedId: n.enhedId, name: n.name, level: n.level })
      }
      walk(n.children)
    }
  }
  walk(org.enheder)
  return out
}
