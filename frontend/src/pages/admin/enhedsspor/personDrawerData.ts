// SPRINT-109 / TASK-10901 — the forest-derived option sources for the merged-admin
// Person drawer. The drawer's Organisation Select + the Placering (unit) Select
// both assemble from the ALREADY scope-bounded S106 forest (the page never fetches
// a separate org list — a scoped HR only ever sees in-scope orgs). No scope logic
// here (ADR-038 D5): pure derivation off the server-admitted forest.

import type { ForestMaoNode, ForestUnitNode } from '../../../hooks/useForest'
import type { Organization } from '../../../hooks/useAdmin'

/** Flatten every visible ORGANISATION node (across all MAOs) into the `Organization`
    shape the reused StamdataSection + the create POST need (the forest carries
    `agreementCode` + `okVersion`, so create can derive the required OkVersion). */
export function orgsFromForest(forest: ForestMaoNode[]): Organization[] {
  const out: Organization[] = []
  for (const mao of forest) {
    for (const org of mao.organisations) {
      out.push({
        orgId: org.orgId,
        orgName: org.orgName,
        orgType: org.orgType,
        parentOrgId: org.parentOrgId,
        materializedPath: org.materializedPath,
        agreementCode: org.agreementCode,
        okVersion: org.okVersion,
      })
    }
  }
  return out
}

/** A flat, depth-ordered Placering option for one Organisation. `unitId` null is
    the synthetic "home directly at the Organisation" option (a person with no
    unit). `depth` (1-based for real units, 0 for the org-home option) drives the
    indent so the nested unit tree reads as a hierarchy in a flat <select>. */
export interface PlacementOption {
  unitId: string | null
  name: string
  depth: number
}

/** The Placering options for the chosen Organisation: the synthetic org-home
    option (null) first, then every unit in that Organisation in depth-first,
    name-sorted order (mirroring the forest's own ordering). Reloaded by the drawer
    whenever the Organisation changes. */
export function unitOptionsForOrg(forest: ForestMaoNode[], orgId: string | null | undefined): PlacementOption[] {
  const options: PlacementOption[] = [{ unitId: null, name: 'Direkte under organisationen', depth: 0 }]
  if (!orgId) return options

  let units: ForestUnitNode[] | null = null
  for (const mao of forest) {
    const org = mao.organisations.find((o) => o.orgId === orgId)
    if (org) {
      units = org.units
      break
    }
  }
  if (!units) return options

  const walk = (nodes: ForestUnitNode[], depth: number) => {
    for (const u of nodes) {
      options.push({ unitId: u.unitId, name: u.name, depth })
      if (u.children.length) walk(u.children, depth + 1)
    }
  }
  walk(units, 1)
  return options
}
