// SPRINT-107 / TASK-10704 — the Afgrænsning (scope) narrowing for the merged
// "Organisation & medarbejdere" admin page.
//
// The Afgrænsning is a pure VIEW filter over the ALREADY-scope-bounded forest
// (ADR-038 D5 / P7): the server admits the MAX set (the actor's accessible orgs);
// the Afgrænsning only ever NARROWS that set client-side — it can never widen it.
// Its OPTION SOURCE is therefore the forest itself (see AfgraensningControl), NOT
// a separate org list, so a scoped HR never sees an out-of-scope org name.
//
// The selection is a Set<organisationId> (or null = "all / not yet customized").
// Filtering keeps only the selected Organisations and RECOMPUTES each MAO's
// rolled-up member count from the kept set — never showing the unfiltered total
// for hidden orgs (the Step-0b count-recompute requirement). Org/unit counts are
// untouched: a unit belongs to exactly one Organisation, so an Organisation is
// either wholly in or wholly out of view.

import type { ForestMaoNode } from '../../../hooks/useForest'

/** Every Organisation id in the (scope-bounded) forest — the full selectable set
    + the "all selected" reference size. */
export function collectOrgIds(forest: ForestMaoNode[]): string[] {
  const ids: string[] = []
  for (const mao of forest) for (const org of mao.organisations) ids.push(org.orgId)
  return ids
}

/** True when the selection covers every Organisation (null = "all"). Because the
    selection is always a subset of the forest's org ids, size-equality ⟺ all. */
export function isAllSelected(selected: Set<string> | null, allOrgIds: readonly string[]): boolean {
  return selected === null || selected.size >= allOrgIds.length
}

/** True when the view is actively narrowed (some-but-not-all, incl. none) — drives
    the search overlay's "begrænset til den valgte afgrænsning" footer note. */
export function isScoped(selected: Set<string> | null, allOrgIds: readonly string[]): boolean {
  return selected !== null && selected.size < allOrgIds.length
}

/** The trigger summary copy (verbatim from the design). */
export function summaryOf(selected: Set<string> | null, allOrgIds: readonly string[]): string {
  const total = allOrgIds.length
  const n = selected === null ? total : selected.size
  if (n >= total) return 'Alle organisationer'
  if (n === 0) return 'Intet valgt'
  return `${n} ${n === 1 ? 'organisation' : 'organisationer'}`
}

/** Narrow the forest to the selected Organisations, recomputing each kept MAO's
    rolled-up count from its kept children (null = no filter = the forest as-is). A
    MAO with no kept Organisation is dropped entirely. */
export function applyAfgraensning(
  forest: ForestMaoNode[],
  selected: Set<string> | null,
): ForestMaoNode[] {
  if (selected === null) return forest
  const out: ForestMaoNode[] = []
  for (const mao of forest) {
    const organisations = mao.organisations.filter((o) => selected.has(o.orgId))
    if (organisations.length === 0) continue
    const memberCount = organisations.reduce((sum, o) => sum + o.memberCount, 0)
    out.push({ ...mao, memberCount, organisations })
  }
  return out
}
