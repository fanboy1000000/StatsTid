// SPRINT-107 / TASK-10701 — Enhedsspor domain maps, ported VERBATIM from the
// design handoff (design_handoff_org_medarbejdere/org-data.js). These maps
// encode DOMAIN RULES — the fixed, ordered unit-type hierarchy (ORD), the
// valid-child chain (CHILD), the Danish type labels (LABEL/SHORT), and the
// per-type accent/tint colours (ACCENT/TINT). Do NOT change the values without
// updating the design source.
//
// NOTE: ACCENT/TINT are ALSO mirrored as CSS custom properties
// (--unit-accent-<type> / --unit-tint-<type>) on the page root in
// OrganisationOgMedarbejdere.module.css, so the tree/Struktur subcomponents
// (TASK-10702/10703) style via tokens (tokens-not-hardcoded) rather than reading
// hex out of this module. Keep the two in sync.

export type UnitType =
  | 'ministeromrade'
  | 'organisation'
  | 'direktion'
  | 'omrade'
  | 'kontor'
  | 'team'
  | 'enhed'

/** Danish display label per unit type. */
export const LABEL: Record<UnitType, string> = {
  ministeromrade: 'Ministerområde',
  organisation: 'Organisation',
  direktion: 'Direktion',
  omrade: 'Område',
  kontor: 'Kontor',
  team: 'Team',
  enhed: 'Enhed',
}

/** Short Danish label per unit type. */
export const SHORT: Record<UnitType, string> = {
  ministeromrade: 'Min.',
  organisation: 'Org.',
  direktion: 'Dir.',
  omrade: 'Område',
  kontor: 'Kontor',
  team: 'Team',
  enhed: 'Enhed',
}

/** The single valid child type for each unit type (null at the leaf `enhed`). */
export const CHILD: Record<UnitType, UnitType | null> = {
  ministeromrade: 'organisation',
  organisation: 'direktion',
  direktion: 'omrade',
  omrade: 'kontor',
  kontor: 'team',
  team: 'enhed',
  enhed: null,
}

/** Per-type accent colour (mirrored as --unit-accent-<type> in the module CSS). */
export const ACCENT: Record<UnitType, string> = {
  ministeromrade: '#55565a',
  organisation: '#066b43',
  direktion: '#1a6a86',
  omrade: '#0f766e',
  kontor: '#8a6a00',
  team: '#5a6b86',
  enhed: '#86705a',
}

/** Per-type tint colour (mirrored as --unit-tint-<type> in the module CSS). */
export const TINT: Record<UnitType, string> = {
  ministeromrade: '#ececed',
  organisation: '#e1efe9',
  direktion: '#e3eef2',
  omrade: '#e2efed',
  kontor: '#f4eed8',
  team: '#eaedf3',
  enhed: '#f2ece6',
}

/** Sort order of the fixed hierarchy (ministerområde is contextual, hence -1). */
export const ORD: Record<UnitType, number> = {
  ministeromrade: -1,
  organisation: 0,
  direktion: 1,
  omrade: 2,
  kontor: 3,
  team: 4,
  enhed: 5,
}
