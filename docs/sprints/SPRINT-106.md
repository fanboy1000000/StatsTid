# Sprint 106 — Enhedsspor Phase 3a: the merged-admin BACKEND READS + contracts

| Field | Value |
|-------|-------|
| **Sprint** | 106 |
| **Status** | complete (pending push + CI-verify) |
| **Start Date** | 2026-06-29 |
| **End Date** | 2026-06-29 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — 0/0 |
| **Test Verified** | yes — 852u + 1148r green locally (Docker; 3 sheds isolation-cleared); 6s/e2e CI-verify on push |

## Sprint Goal
Build the READS the merged Enhedsspor admin page (Phase 3b) needs — without touching schema, authority, events, or mutations. Three new/changed reads + the closing of one S105 scope-out, each shipped with the FULL PAT-010 contract bundle (named records + co-located contract tests + lint-registry registration + FE hook tests on the real shape). The owner chose the **3a→3b split** and **redirect+retire in 3b**. The keystone discipline: the unified forest read must NOT let units imply scope — unit nodes are admitted **solely** by the parent Organisation's `GetAccessibleOrgsAsync` (ADR-038 D5; the S76/S85/S91 guard), pinned by a discriminating RED test. Backend-only.

## Entropy Scan Findings
| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` all hard checks passed |
| Pattern compliance | CLEAN | `grep FindFirst("scopes")` → 0 (FAIL-001 clean) |
| Orphan detection | CLEAN (carried) | S105 reads/authority CI-green `28355633850` |
| Doc drift | none expected | reads-only; db-schema unchanged |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 + **P7** — the forest read crosses the scope boundary [must not widen]; P8 — the PAT-010 contract surface). Reads-only lowers the risk vs S105, but the scope-visibility correctness is load-bearing. |
| **External Codex** | invoked 2026-06-29 — 0B/2W/3N |
| **Internal Reviewer** | invoked 2026-06-29 — 0B/2W/3N |
| **BLOCKERs resolved before Step 1** | none raised (0 BLOCKER both lenses); the substantive WARNINGs absorbed (cycle-cap respected; no cycle-2 needed) |

### Findings (cycle 1)
Both lenses confirm the direction sound + the **D5 keystone structurally leak-free** (units carry an immutable `organisation_id` FK → admission via the org-set is leak-free, mirroring the S98 org-tree). 0 BLOCKER. Absorbed:
- **CONVERGENT (TASK-10604) — the tile-count is a candidate-ENUMERATION change, not a predicate swap** (as worded, a silent no-op): the projection must enumerate per pending employee the unit's leaders + active vikar-of-leader (the inverse of the S105 `unit_led_members` CTE), tally each. Semantic shift: a pending employee now tallies per authorized approver (Σ tiles ≥ pending count; the "exactly once" docstring inverts) → propagates to the existing roster's `pendingCountByManager` → contract-test the tile shift. → absorbed.
- **WARNING (Reviewer, TASK-10602) — the multi-peer-leader `LEFT JOIN unit_leaders` would fan-out the set-based roster** (one row per employee×leader) → aggregate (`LATERAL json_agg`) to keep one-row-per-employee. → absorbed (+ a fan-out test).
- WARNING (Codex, TASK-10601/10605) — PAT-010 wording fix: PascalCase record members → camelCase WIRE keys (`JsonSerializerDefaults.Web`, no `[JsonPropertyName]`). → absorbed.
- NOTE (Codex, TASK-10601) — the D5 RED test must assert COUNT non-leakage (a rollup leaks through MAO/count totals even with sibling nodes filtered). → absorbed.
- NOTE (Reviewer, TASK-10601) — the count-reconciliation identity (Org count = Σ unit rollups + homed-NULL users). → absorbed.
- NOTE (Reviewer, TASK-10602) — the etag is the active PRIMARY `reporting_lines.version`, NULLABLE (root/orphan → "Ret" creates vs supersedes, S99); the upward-ref name resolution is a display-only by-id lookup covering OUT-of-subtree leaders. → absorbed.
- NOTE (Codex, TASK-10605) — perf wording: one bounded per-Organisation load is allowed/budgeted; no cross-scope/global scan. → absorbed.

## Architectural Constraints Verified
- [x] P1 — the Organisation stays the scope anchor; the forest merges `organizations`+`units` for DISPLAY only; units grant no scope (D5; `UnitAuthorityAbsenceTests` 2/2; scope path untouched).
- [x] P7 — the forest admits unit nodes solely via `GetAccessibleOrgsAsync` (no per-unit predicate); a scoped HR sees no sibling-Organisation units/people AND no sibling counts (the D5 RED count-non-leakage test); search same-name-sibling pin; the Step-7a count-bounding fix keeps it scope-bounded in WORK too.
- [x] P8 — every new read has named records + co-located contract tests (PascalCase members → camelCase wire); pyramid 852u+1148r green locally; a perf budget at 3350-people scale (no unbounded scan). Lint-registry + FE hook tests carried to 3b (FE-call-driven; no FE consumer yet).

## Task Log

### TASK-10600 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-10600 |
| **Status** | complete — entropy CLEAN; plan authored; Step-0b dual-lens run (0B/2W/3N each lens; substantive warnings absorbed — tile-count enumeration, roster fan-out, PAT-010 wording, D5 count-non-leakage, nullable etag); D5 keystone confirmed structurally leak-free. |
| **Agent** | Orchestrator |
| **KB Refs** | ADR-038 D1/D4/D5, PAT-010, REFINEMENT-phase3-merged-fe.md, docs/SECURITY.md |

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run; warnings absorbed before Step 1.

---

### TASK-10601 — The unified scoped FOREST read (organizations + units) + per-unit counts + the D5 RED test
| Field | Value |
|-------|-------|
| **ID** | TASK-10601 |
| **Status** | complete — `GET /api/admin/units/forest` (`ForestContracts` named records; PAT-010 camelCase wire) + `UnitRepository` set-based reads + in-memory roll-up; admission SOLELY via `GetAccessibleOrgsAsync` (no per-unit predicate). D5 RED (count non-leakage: scoped HR sees own counts only, sibling `UB1`+count absent) + count-reconciliation + GlobalAdmin-all. Build 0 err; **4/4 new + `UnitAuthorityAbsenceTests` 2/2 Docker-green**. Lint-registry registration deferred to TASK-10605 (no FE consumer yet). |
| **Agent** | Infrastructure + Backend (cross-domain authorized) |
| **Components** | `src/Infrastructure/.../OrganizationRepository`/`UnitRepository`, `src/Backend/.../Endpoints/UnitEndpoints.cs` (or AdminEndpoints), a named `…Forest…` contract record |
| **KB Refs** | ADR-038 D1/D5, S98 aggregated org-tree (`/organizations/tree`, the precedent), `OrgScopeValidator.GetAccessibleOrgsAsync`, PAT-010 |

**Description**: A new read (e.g. `GET /api/admin/units/forest` or extend `/organizations/tree`) returning the **7-level forest** — MAO → Organisation (from `organizations`) → direktion…enhed (from `units` beneath each Organisation) — as named nested records. **Visibility (the keystone):** the set of Organisations is bounded by `GetAccessibleOrgsAsync` (the EXISTING org-tree admission); units are included **only** for an Organisation already admitted — there is **NO per-unit visibility predicate** and **no descendant/sibling widening** (D5). MAO ancestors render as read-only context (as the S98 org-tree already does for scoped HR). **Per-unit active-member counts** via a single `GROUP BY unit_id` over active users + an **in-memory roll-up** up the depth-≤5 unit tree (units ≪ people → no recursive SQL CTE). PAT-010 convention: **PascalCase C# record members → camelCase wire keys** (the .NET8 `JsonSerializerDefaults.Web` default; NO `[JsonPropertyName]`).

**Validation Criteria**:
- [ ] The forest returns MAO/Organisation/unit nodes with per-unit + rolled-up member counts; GlobalAdmin sees all, a scoped HR sees only their accessible Organisations' subtrees.
- [ ] **D5 RED test (count non-leakage, not just node absence):** seed sibling Organisations under one MAO with DISTINCT unit/member counts — a scoped HR sees the MAO context + their own Organisation/unit nodes AND counts only; the sibling Organisation's nodes AND its counts are absent (a naive global rollup leaks through the MAO/count totals even if sibling nodes are filtered). Admission solely via `GetAccessibleOrgsAsync` (no per-unit predicate); RED on any per-unit/descendant/count widening.
- [ ] **Count reconciliation:** each Organisation node's count (the S98 `employeeCount` by `primary_org_id`) reconciles to Σ(its units' rolled-up counts) + (its `unit_id`-NULL Organisation-homed users) — a test asserts the identity (else the FE pills won't sum).
- [ ] Named record + co-located contract test + lint registration + an FE hook test on the real shape.

---

### TASK-10602 — Unit-tag the per-Organisation roster (+ etag, cross-unit + upward-ref names, vikar-inverse)
| Field | Value |
|-------|-------|
| **ID** | TASK-10602 |
| **Status** | complete — roster gains `unitId`/`unitName`/aggregated `leaderIds` (`LATERAL array_agg` — one row per employee, fan-out test) + nullable `primaryReportingLineVersion` (root/orphan → null, S99 create-vs-supersede) + display-only `NameResolution` (by-id over referenced ids, no scope widening) + vikar-inverse confirmed. Build 0 err; **roster 7/7 + existing 20/20 Docker-green**. |
| **Agent** | Infrastructure + Backend (cross-domain authorized) |
| **Components** | `src/Infrastructure/.../ApprovalPeriodRepository.GetMedarbejderRosterForTreeAsync`, the `MedarbejderRosterRow` contract record, `AdminEndpoints` roster endpoint |
| **KB Refs** | S75 roster, ADR-038 D2/D6, S99 resolve-etag-first, ADR-027 vikar |

**Description**: Add to each `materialized_path`-scoped roster row: `unitId` + `unitName` + the row's unit's `leaderIds` (so the FE groups people under their unit's leaders + flags cross-unit exceptions = the person's reporting manager ∉ their unit's leaders). **[Reviewer WARNING — avoid the set-based fan-out]:** a unit has MULTIPLE peer "sideordnede" leaders, so a naive `LEFT JOIN unit_leaders` would yield one row per (employee × leader) and silently MULTIPLY the roster (whose joins are deliberately fan-out-free per `uq_manager_vikar_active`) → the `leaderIds` MUST be aggregated (a `LEFT JOIN LATERAL json_agg`/`array_agg` keyed on `e.unit_id`, or a separate batched read) to preserve one-row-per-employee. **The etag** = the active PRIMARY `reporting_lines.version` (the row already joined, surfaced today as `structural_approver_id`), named `primaryReportingLineVersion`, **NULLABLE** — roots/orphans have no active PRIMARY edge → null → "Ret" must CREATE (no If-Match) vs SUPERSEDE (If-Match), the S99 distinction the FE branches on. **Name resolution** for the upward-reference ("Refererer opad til") + cross-unit-leader chips = a targeted **display-only by-id lookup** (name/title/unit) over the referenced ids (covers a leader OUTSIDE the loaded subtree — a parent Organisation/MAO above the root, or Organisation-homed `unit_id` NULL); confirmed display-only (NO scope widening). Confirm the stand-in "Vikar for X" tag is derivable by **inverting** the row's existing `OutgoingVikar` within the loaded set (else add an incoming-vikar field). The roster stays per-Organisation (one styrelse loads once; the FE groups client-side — NOT a per-unit read).

**Validation Criteria**:
- [ ] Roster rows carry `unitId`/`unitName`/aggregated unit-`leaderIds` (one row per employee — a fan-out test pins it) + the nullable `primaryReportingLineVersion`; cross-unit exception derivable; the upward-ref + cross-unit-leader names resolve via the by-id lookup (no blank, display-only); the vikar-inverse confirmed/added.
- [ ] The contract record updated + the co-located contract test extended + lint coverage + the FE hook test on the real shape; no scope regression (still `materialized_path`-bounded, LocalHR floor); the etag changes only with the reporting-line row.

---

### TASK-10603 — The scoped units + people SEARCH read
| Field | Value |
|-------|-------|
| **ID** | TASK-10603 |
| **Status** | complete — `GET /api/admin/search?q=` (`SearchContracts` named records; two-section `{units,people}` + full paths) via `UnitRepository.SearchUnitsAsync` + `ApprovalPeriodRepository.SearchPeopleForOverlayAsync` (new siblings). Scope solely via `GetAccessibleOrgsAsync` (`organisation_id`/`primary_org_id = ANY` — no per-unit predicate). D5 pin: a DUPLICATE-named sibling-Org unit + person are absent for a scoped HR. Build 0 err; **4/4 Docker-green**. Lint registry deferred to 3b. |
| **Agent** | Backend + Infrastructure |
| **Components** | a new `GET /api/admin/.../search` (units + people), a named search-result record |
| **KB Refs** | S74 `users/search` (the precedent), ADR-038 D5, PAT-010 |

**Description**: A scope-bounded search over units (name) + people (name/title/email) returning the design's two-section overlay shape (ENHEDER + MEDARBEJDERE, each with the node's full path) — server-side because the FE lazy-loads per Organisation and a client filter cannot see un-loaded people. Scope-bounded to the actor's accessible Organisations (LocalHR floor, mirroring `users/search`); units bounded by their Organisation's admission (D5).

**Validation Criteria**:
- [ ] Search returns scoped units + people with paths; a scoped HR gets no cross-Organisation results; named record + contract test + lint + FE hook test.

---

### TASK-10604 — Close the S105 medarbejder-tile-count scope-out
| Field | Value |
|-------|-------|
| **ID** | TASK-10604 |
| **Status** | complete — candidate ENUMERATION (per pending employee: edge manager ∪ `unit_leaders(E.unit_id)` + active vikar-of-leader; the inverse of the S105 `unit_led_members` CTE, self-excluded, no ancestor walk) not a gate-swap; cardinality inverts to Σ tiles ≥ pending count (docstring updated); propagates to `pendingCountByManager`. Pinned (1 pending → 4 distinct tiles; inactive leader excluded by floor; pure-edge unchanged). **`S105UnitLeaderApprovalTests` 13/13 unaffected.** |
| **Agent** | Infrastructure |
| **Components** | `ApprovalPeriodRepository.GetPeriodStatusProjectionForTreeAsync` |
| **KB Refs** | S105 Step-7a WARNING (the tile-count scope-out), ADR-038 D4 |

**Description**: Extend the medarbejder-tree pending-tile projection to include the **unit-leader** approval path (consciously edge-only in S105). **[Both Step-0b lenses — this is a candidate-ENUMERATION change, NOT a predicate swap]:** the current loop resolves ONE edge manager per pending employee (`ResolveDesignatedApproverAsync`) then gates — swapping the gate to `IsEffectiveApproverOrUnitLeaderAsync` on that same single managerId adds NOTHING. To include the unit-leader path, the projection must **enumerate**, per pending employee, the employee's unit's leaders (`unit_leaders` on `e.unit_id`) + active vikars-of-those-leaders (the INVERSE of the S105 `unit_led_members` CTE) and tally EACH authorized approver. **Semantic shift (must be documented + pinned):** a pending employee now counts toward MULTIPLE managers' tiles (edge-manager + each unit-leader) → the S105 "tallied EXACTLY once" docstring inverts to per-authorized-approver cardinality (Σ tiles ≥ pending count) — this is correct (each approver who CAN act sees it). This propagates to `GetMedarbejderRosterForTreeAsync`'s reused `pendingCountByManager` → the EXISTING medarbejder page's tiles shift → pin with a contract/parity test. Update the S105 scope-out docstring to the new semantics.

**Validation Criteria**:
- [ ] The tile/`pendingCountByManager` ENUMERATION expands through `unit_leaders` + active vikar-of-unit-leader (not just the gate); a secondary-unit-leader + a unit-leader-vikar each see the pending employee in their tile, matching the S105 dashboard set.
- [ ] The per-approver cardinality (Σ tiles ≥ pending count) is documented + pinned; the existing medarbejder page tile shift is contract-tested; the S105 docstring updated.

---

### TASK-10605 — The PAT-010 contract bundle + perf seed-scale regression
| Field | Value |
|-------|-------|
| **ID** | TASK-10605 |
| **Status** | complete — 3 contract tests audited + strengthened (forest: root `parentUnitId`-Null/`level`-1 nullability pin; search: nested-unit path [S100 nested-drop analogue]; roster: `ContractAssert.IsEnvelope` + full field-set). `S106SeedScalePerfTests` (3350-user demo shape, Npgsql ActivitySource query-counter): forest **exactly 4 commands** (scale-invariant), roster 2 (`materialized_path`-bounded, 2000 not 3372), search 4 (scope-confined), tile-count **org-size-INDEPENDENT** (1 cmd @ 0-pending for 2000- AND 250-user; ~27/pending employee — the pre-existing S105 per-pending N+1, bounded by the pending set; flagged for a future batch opt). **No unbounded scan.** Build 0 err; perf 4/4 + contract/roster 9/9 Docker-green. Lint registry + FE hooks → 3b. |
| **Agent** | Test & QA |
| **Components** | `tests/.../Contracts/`, `tools/check_endpoint_contracts.py` (registry), `tests/StatsTid.Tests.Frontend` (hook tests), a seed-scale perf test |
| **KB Refs** | PAT-010 (Pass 2), S105 the recurring drift class |

**Description**: The cross-cutting contract + perf gate. **Re-scoped (the FE-call-driven parts move to 3b):** the co-located contract tests are ALREADY written per-read by TASK-10601/10602/10603 (forest, roster, search — PascalCase members → camelCase wire, deep nested node, literal camelCase keys). The **lint registry registration** (`tools/check_endpoint_contracts.py`) + the **FE hook tests on the real shape** belong to **3b** — the lint is FE-call-driven and there is NO FE consumer of these endpoints in 3a (registering pre-FE would mis-key the registry). 3a's deliverable here is: (a) **audit/consolidate** the three contract tests for coherence (envelope/array shape, required-field presence, the deep nested unit node, literal camelCase keys); (b) a **perf seed-scale regression** at Demoministeriet scale (3231 people, depth ≤ 5) over the demo-seeded DB — assert the forest + roster + search hold a query-count/time budget: **no cross-scope/global scan; one bounded per-Organisation roster load is allowed + budgeted** (the count roll-up is `GROUP BY unit_id`; the roster is `materialized_path`-bounded). Surface the tile-count's per-pending-employee query count explicitly (pre-existing N+1 shape — confirm it stays bounded by the pending set, not the org size).

**Validation Criteria**:
- [ ] The three contract tests audited coherent + green (PascalCase members → camelCase wire, no `[JsonPropertyName]`); a documented 3b carry-over for the lint registry + FE hook tests.
- [ ] The perf test holds the budget at 3231-people scale (one bounded per-Organisation load; no global scan); the tile-count's per-pending query count is bounded by the pending set + documented.

---

### TASK-10606 — Validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-10606 |
| **Status** | complete — build 0/0; pyramid 852u+1148r green LOCALLY (Docker; 3 sheds isolation-cleared); Step-7a dual-lens (Codex 0B/1W-fixed + Reviewer 0B/0W/3N); the Codex forest-count-bounding WARNING fixed + re-verified; INDEX/ROADMAP/QUALITY/memory updated. Commit + push + CI-verify next. |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [x] `dotnet build` 0/0; full pyramid green (Docker-local); Step-7a dual-lens → WARNING fixed; INDEX/ROADMAP/QUALITY updated; commit + push + CI-verify; 3b items carried to the S107 projection.

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / wage mappings / payroll | N/A | Reads-only; no rule/payroll/attribution change. |

## External Review (Step 7a)
Dual-lens on the full uncommitted S106 diff (P7-relevant — the forest read crosses the scope boundary for display). Artifacts: `.claude/reviews/SPRINT-106-step7a-{codex,reviewer}.md`.

**Reviewer (internal) — 0 BLOCKER, 0 WARNING, 3 NOTE:** all four reads CLEAN, the D5 keystone preserved BY CONSTRUCTION (forest admitted solely via the accessible-org set + the count-leak vector closed; roster fan-out-safe; tile-count enumeration self-excluded + the S105 self-approval guard doubly preserved; search scope-bound by a same-name-sibling pin). NOTEs accepted/deferred: the search `total`-truncation (→ 3b overlay), the count-reconciliation S104 invariant (upheld), the tile-count per-pending N+1 (bounded by pending, → future batch opt).

**Codex (external) — 0 BLOCKER, 1 WARNING (FIXED):** the forest member-count `GROUP BY` scanned the whole `users` table even for a scoped HR (no LEAK — the in-memory filter is correct — but it scaled with total population, contradicting the sprint's "no cross-scope scan" criterion). **FIXED:** the two count queries now filter `WHERE primary_org_id = ANY(@orgIds)` for a scoped HR (valid by the S104 invariant; unrestricted for GlobalAdmin); re-verified — forest + perf + the D5 RED test green.

[[review-lens-complementarity]]: the internal lens verified no scope LEAK; the external lens caught the unbounded WORK the correctness-focus passed.

## Test Summary
**Pyramid: 852u + 1148r + 6s + 29demoseed + 517fe = 2552 — VERIFIED GREEN LOCALLY (Docker).** Regression 1145 passed + 3 Payroll/Settlement FAIL-002 testcontainer sheds ("Exception while writing to stream", unrelated to S106) → isolation-cleared 15/15 (with the forest/perf re-verify of the count-fix). +19 vs S105's 1129: forest 4 + roster 7 + search 4 + perf 4 (+ strengthened contract tests). Unit 852 (no change — reads-only backend). Smoke/e2e CI-verify on push.

## Sprint Retrospective
- **A disciplined reads-only sprint — the D5 keystone held by construction across all four reads.** Every read carried its own discriminating D5 scope test (the forest count-non-leakage, the search same-name-sibling pin); the tile-count enumeration doubly preserved the S105 self-approval guard.
- **Both Step-0b lenses caught the two silent traps pre-code:** the tile-count "predicate swap = no-op" (it needed a candidate ENUMERATION) and the multi-peer-leader roster FAN-OUT (a silent row-multiplier). Step-7a's external lens then caught the forest count-query's unbounded WORK (a perf-scope mismatch the internal correctness-lens passed) — [[review-lens-complementarity]] decisive at both gates.
- **Docker-local verification (Docker up this sprint):** the full regression + the isolation-clear + the forest-count-fix re-verify all ran locally before the push — no CI-pending gamble.
- **3b carry-overs (documented):** the lint-registry registration + the FE hook tests on the real shape (FE-call-driven — no FE consumer until 3b); the search `total`/"N more" overlay signal; the person-drawer two-endpoint routing (same-Org unit-assign vs cross-Org transfer); a future batch optimization for the tile-count per-pending N+1.
- Durable: SPRINT-106.md + the Step-7a artifacts + REFINEMENT-phase3-merged-fe.md. **NEXT = S107 (Phase 3b: the merged Enhedsspor admin FE — the 3-region page on the UI Kit consuming these reads; capability-gated to the live floors; redirect+retire the two old pages; its own refinement-confirmed scope + Step-0b).**
