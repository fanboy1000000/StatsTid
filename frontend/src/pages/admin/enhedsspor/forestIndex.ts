// SPRINT-107 / TASK-10703 — a normalised index over the S106 scoped forest, used
// by the right "Struktur" panel to resolve the selected node's ancestry
// (breadcrumb), its child units (recursion), and the Organisation a node belongs
// to (which roster to load).
//
// The forest's three tiers (MAO → Organisation → unit sub-tree) are unified into
// one recursive StrukturNode shape keyed by id. Ids are disjoint across tiers
// (orgId is a code string like "STY02"; unitId a GUID), so a single id-keyed map
// is unambiguous. READ-ONLY: this is pure derivation, no scope logic — the forest
// is already scope-bounded server-side (ADR-038 D5).

import type { ForestMaoNode, ForestUnitNode } from '../../../hooks/useForest'
import type { UnitType } from './typeMaps'

export interface StrukturNode {
  /** orgId (MAO/Organisation) or unitId (unit). */
  id: string
  kind: 'mao' | 'organisation' | 'unit'
  /** The unit-type key for the accent token / LABEL map. */
  type: UnitType
  name: string
  parentId: string | null
  /** The Organisation this node belongs to (→ which roster to load). null for a
      MAO (a MAO spans multiple Organisations, each its own roster). */
  organisationId: string | null
  /** The unit's optimistic-concurrency version (the If-Match etag source for
      rename / move / delete). Units only — null for MAO / Organisation (their
      structure mutations are a separate task with their own concurrency token). */
  version: number | null
  /** The deep (rolled-up) member count from the forest. */
  memberCount: number
  /** Child UNITS for recursion: a MAO's Organisations, an Organisation's
      top-level units, or a unit's sub-units. */
  childUnits: StrukturNode[]
}

export interface ForestIndex {
  byId: Map<string, StrukturNode>
}

function unitNode(
  u: ForestUnitNode,
  parentId: string,
  organisationId: string,
  byId: Map<string, StrukturNode>,
): StrukturNode {
  const childUnits = u.children.map((c) => unitNode(c, u.unitId, organisationId, byId))
  const node: StrukturNode = {
    id: u.unitId,
    kind: 'unit',
    type: u.type,
    name: u.name,
    parentId,
    organisationId,
    version: u.version,
    memberCount: u.memberCount,
    childUnits,
  }
  byId.set(node.id, node)
  return node
}

/** Build the id-keyed index from the scoped forest. */
export function buildForestIndex(forest: ForestMaoNode[]): ForestIndex {
  const byId = new Map<string, StrukturNode>()

  for (const mao of forest) {
    const orgNodes: StrukturNode[] = mao.organisations.map((org) => {
      const topUnits = org.units.map((u) => unitNode(u, org.orgId, org.orgId, byId))
      const orgNode: StrukturNode = {
        id: org.orgId,
        kind: 'organisation',
        type: 'organisation',
        name: org.orgName,
        parentId: mao.orgId,
        organisationId: org.orgId,
        version: null,
        memberCount: org.memberCount,
        childUnits: topUnits,
      }
      byId.set(orgNode.id, orgNode)
      return orgNode
    })

    const maoNode: StrukturNode = {
      id: mao.orgId,
      kind: 'mao',
      type: 'ministeromrade',
      name: mao.orgName,
      parentId: null,
      organisationId: null,
      version: null,
      memberCount: mao.memberCount,
      childUnits: orgNodes,
    }
    byId.set(maoNode.id, maoNode)
  }

  return { byId }
}

/** The ancestry chain (root MAO → … → the node itself), for the breadcrumb. */
export function pathOf(index: ForestIndex, id: string): StrukturNode[] {
  const out: StrukturNode[] = []
  let cur: StrukturNode | null = index.byId.get(id) ?? null
  while (cur) {
    out.unshift(cur)
    cur = cur.parentId ? index.byId.get(cur.parentId) ?? null : null
  }
  return out
}

/** All descendant child-unit ids (depth-first), for expand-/collapse-all. */
export function descendantUnitIds(node: StrukturNode): string[] {
  const out: string[] = []
  const rec = (n: StrukturNode) => {
    for (const c of n.childUnits) {
      out.push(c.id)
      rec(c)
    }
  }
  rec(node)
  return out
}

/** Every UNIT node within one Organisation (TASK-10801 — the move-target source).
    A unit's `organisationId` is immutable, so this enumerates the candidate
    re-parent targets before the self / descendant / type-rank filtering. */
export function unitsInOrg(index: ForestIndex, organisationId: string): StrukturNode[] {
  const out: StrukturNode[] = []
  for (const node of index.byId.values()) {
    if (node.kind === 'unit' && node.organisationId === organisationId) out.push(node)
  }
  return out
}
