# ADR-035 — Flat authority model (org-tree inheritance removed from role-scope + approval authority)

| Field | Value |
|-------|-------|
| **Status** | accepted (umbrella; implementation sliced S92→S95) |
| **Sprint** | S92 (slice 1 — org-model flatten); S93 flat role-scope; S94 flat approval; S95 tree retirement |
| **Supersedes / amends** | ADR-027 (reporting-line hierarchy) D2/D9/D11/D13/D15/D18/D19 — staged per the disposition table (D6 below); narrows ADR-008 (materialized path) and ADR-009 (scope-in-JWT) |
| **Domains** | Security, Infrastructure, Backend, Data Model, Frontend |
| **Refinement** | `.claude/refinements/REFINEMENT-flat-authority-model.md` (owner-decided pivot 2026-06-22; dual-lens Step-4 ×2) |

## Context
S74–S91 built a designated-approver + same-tree-containment + `ORG_AND_DESCENDANTS` authority model on top of a deep MINISTRY→STYRELSE→AFDELING→TEAM org tree. The derived, mutable `tree_root_org_id` key and the subtree-inheritance scope semantics were the recurring source of the S76/S85/S91 bug class (mixed-role scope leaks, privilege escalation, secondary-principal containment holes). Owner pivot (2026-06-22): **flatten BOTH authority systems to explicit org-sets — nothing inherited from the org tree** — while pre-launch/greenfield, when the migration is cheap. The keystone realization: the org *structure* itself flattens to one real authority level (**Organisation**) plus flat metadata (**Enhed**), which makes "same Organisation" a flat attribute equality and renders the tree-root machinery meaningless rather than merely removable.

## Decision

**D1 — The flat model.** Authority follows an EXPLICIT set of one-or-more Organisations, never the org TREE. Keep the BOUNDING (an org-set), remove only the INHERITANCE (subtree/ancestor derivation). NOT "anyone approves anyone"; HR/Admin "always" = within their explicitly-scoped Organisations. CLAUDE.md Priority #7 stays intact — a re-shaping of the boundary, not a relaxation.

**D2 — The org taxonomy.** Three concepts replace the 4-tier tree:
- **Organisation** — *mandatory* on every user (one home org, e.g. "Økonomistyrelsen"); cannot be deleted while it holds users. **The single real unit of authority/containment.**
- **Enhed** — flexible, create/delete-able **metadata tag(s) on the user** (department/team); for findability only — **NOT an authority/scope boundary.**
- **MAO** (Ministeransvarsområde) — the ministry-responsibility wrapper above Organisation (grouping/display).
- Old→new mapping: MINISTRY→MAO; **STYRELSE→Organisation**; AFDELING + TEAM → **Enhed (user metadata)**.

**D3 — "Same Organisation" is the one containment rule.** A flat equality on the user's mandatory Organisation attribute (no ancestor walk). All containment collapses to it: a leader self-delegating a vikar → vikar.Organisation == leader.Organisation; an HR/Admin action → the actor's explicit Organisation-set contains the target's Organisation; approval → an effective reporting edge **OR** HR/Admin scoped over the employee's Organisation. (Owner OQ4: the unfloored leader-by-org-scope approval branch is RETIRED — a non-designated in-scope leader must hold the edge; only HR/Admin form the org-scope fallback. OQ8: new orgs are explicit-grant-only, no inheritance. OQ5: pure-OR HR/Admin fallback. OQ6: REQUIRED-mode enforcement RETIRED in S94.)

**D4 — Phasing (owner-confirmed; org-model flatten FIRST).**
- **S92** = org-model flatten (this slice — D5).
- **S93** = flat role-scope (drop `ORG_AND_DESCENDANTS`; coverage = explicit Organisation-set membership; `CoversOrg`/`OrgScopeValidator`/JWT/grant-revoke/FE picker).
- **S94** = flat approval (`CanApprove` = edge OR HR/Admin-over-Organisation; same-Organisation vikar; retire `reporting_line_tree_settings` + the REQUIRED-mode 428 gate).
- **S95** = retire the tree machinery (`tree_root_org_id`, `ResolveTreeRootOrgIdAsync`, `ValidateSameTreeAsync`, `CrossTreeAssignmentException`; re-key the advisory locks to the flat Organisation/employee key per the lock matrix in the refinement).
- **later** = the redesigned Organisation admin page + the structured/rich Enhed UX.

**D5 — The S92 slice (org-model flatten).** `org_type` CHECK → `{MAO, ORGANISATION}`; the 2-level MAO→Organisation tree; every user's `primary_org_id` re-pointed to an Organisation; former AFDELING/TEAM unit name → `employee_profiles.enhed_label` (display-only/inert per ADR-022/029; init.sql pre-seeds the moved users' profile rows with a CREATED audit row but NO outbox event — init.sql runs pre-boot and cannot enqueue, consistent with existing seed behaviour). The flatten has TWO authority effects:
- **rename** (MINISTRY→MAO, STYRELSE→ORGANISATION) — **identity-preserving** (the tree-root CTE + `CoversOrg` prefix resolve the same set);
- **level-collapse** (AFDELING/TEAM users move up to their Organisation) — **NOT identity-preserving, BY DESIGN**: an afdeling/team-level scope **coarsens** to its Organisation, because Enhed holds no authority and the Organisation is the smallest authority unit. `ORG_AND_DESCENDANTS` admin reach is PRESERVED; `ORG_ONLY`/exact-org scopes coarsen UP the path (widen-or-equal, **never narrow**). The role-scope MECHANISM is UNCHANGED in S92 (flattened in S93); only the granularity (afdeling→Organisation) coarsens. `role_assignments.org_id` (+ the `local_configurations`/`projects` FKs) re-pointed off the deleted AFDELING orgs.

**D6 — ADR-027 disposition table (staged).**
| ADR-027 item | S92 status | Final |
|---|---|---|
| D2 tree boundary per MINISTRY/STYRELSE | **transitional** — re-pointed to `('MAO','ORGANISATION')`; a user's Organisation IS the tree root | superseded by D3 same-Organisation (S94/S95) |
| D9 root invariant | transitional (re-pointed) | superseded (S95) |
| D11 REQUIRED-mode enforcement (`reporting_line_tree_settings`) | **kept (live)** | retired (S94, OQ6) |
| D13 designated-edge approve-authority within the styrelse | kept (live) | reshaped to `CanApprove` edge-OR-HR/Admin (S94) |
| D15 write-time integrity (cycle guard + locks) | kept (live) | lock matrix re-keyed to Organisation/employee (S95) |
| D18 in-lock authorization hardening (tree-root advisory) | kept (live) | re-keyed (S95) |
| D19 edge-auth serialization (persisted-root revoke anchor) | kept (live) | re-anchored (S95) |

## Consequences
- **The afdeling→Organisation coarsening is intended** (D5): some former afdeling-level admin scopes widen to the whole Organisation. Greenfield (seeds illustrative); S93 re-expresses every scope as an explicit Organisation-set. RED-on-old proof (`S92OrgFlattenTests`): coarsening only ever moves a scope UP the path (no narrowing); CHECK rejects AFDELING; tree-root resolves identically for the rename.
- **ADR-008 materialized-path role narrows** — after S95 the path is no longer an authority-derivation key (Organisation equality replaces prefix matching); it survives for MAO grouping/display.
- **No event-vocabulary change in S92**; org create/update events + audit unchanged.
- **Concurrency (S95)**: dropping `tree_root_org_id` removes a serialization domain, not just a mutable key — the refinement's lock matrix ("any mutation changing employee E's effective-approver holds E's lock; vikar fans out to E∈reports(M); cycle guard locks both endpoints id-sorted; the ≥3-node proof is a first-class deliverable") governs the re-key.
- This ADR is the umbrella; each slice (S93/S94/S95) amends D4/D5/D6 with its landed status.
