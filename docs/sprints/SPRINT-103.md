# Sprint 103 — Enhedsspor Phase 1a: Schema + Events + Greenfield Reseed + Enhed Cutover (ADR-038 D1/D2/D9/D10/D11)

| Field | Value |
|-------|-------|
| **Sprint** | 103 |
| **Status** | complete |
| **Start Date** | 2026-06-28 |
| **End Date** | 2026-06-28 |
| **Orchestrator Approved** | yes — 2026-06-28 |
| **Build Verified** | yes — `dotnet build` 0 errors (110 pre-existing warnings); frontend build 0 errors |
| **Test Verified** | yes (local) — 852 unit + 29 demoseed + 517 FE passing; Docker-gated regression + `UnitFoundationTests` **CI-pending** (Docker unavailable locally; verified by CI on push) |

## Sprint Goal
Land the **data + event foundation** for the Enhedsspor model (ADR-038 Phase 1a — the owner-chosen split of the Phase-1 plan): the `units` + `unit_leaders` + `users.unit_id` schema (greenfield, **replacing** `enheder`/`user_enheder`/`employee_profiles.enhed_label`), the new `Unit*`/leader/membership events, the greenfield demo reseed into the new shape, and the **complete enhed/EmployeeProfile consumer cutover** — leaving the build + the existing approval/roster path GREEN (on the retained `reporting_lines` + the Organisation-direct `primary_org_id`). **No units CRUD/endpoints, no runtime unit-move concurrency, no cross-Org transfer, no new approval paths, no FE redesign** — those are S104 (Phase 1b) + later.

**Split note (owner-decided 2026-06-28):** the original SPRINT-103 (the whole Phase-1) was Step-0b dual-lens-reviewed clean (2 cycles, 2 Codex BLOCKERs absorbed, 0 residual). On the reviewers' recommendation it is split: **this S103 = the foundation half** (schema/events/reseed/cutover); **S104 = the heavier half** (units CRUD repository + two-regime concurrency + the cross-Org-transfer extension [Codex BLOCKER-2's resolution] + admin endpoints + the held-lock interleave/cycle-guard tests), which gets its own Step-0b. The Step-0b findings below are the reviewed record for the work in THIS sprint.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` green at S102 close. |
| Pattern compliance spot-check | CLEAN | `grep FindFirst("scopes")` → 0 (FAIL-001 clean). |
| Orphan detection | CLEAN | No orphans from S100/S101. |
| Documentation drift | RESOLVED-BY-THIS-SPRINT | The `enhed_label` + `enheder`/`user_enheder` debt closes here (ADR-038 D9/D11). |
| Quality grade review | pending | Update at close. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 + P3 + P7 + schema migration). |
| **External Codex** | invoked 2026-06-28 — 2 cycles. Cycle 1: 2B/3W/1N; cycle 2: both BLOCKERs RESOLVED, 0 new. |
| **Internal Reviewer** | invoked 2026-06-28 — 1 cycle: 0B/3W/3N (all WARNINGs absorbed). |
| **BLOCKERs resolved before Step 1** | yes. **BLOCKER-1 (the deep `enhed_label`/`EmployeeProfile*` cutover) is resolved IN THIS sprint (TASK-10304).** BLOCKER-2 (cross-Org person-change must extend the users-transfer path) is carried to **S104** with the units CRUD. |

### Findings (cycle 1 — the reviewed whole-Phase-1 plan; the foundation-half items apply here, the units-CRUD items move to S104)
_Codex (external):_
- BLOCKER (cutover) — `enhed_label` threads through `EmployeeProfile*` events/DTOs/audit/repo, not just readers → drop `EnhedLabel` end-to-end. **[in S103, TASK-10304]**
- BLOCKER (cross-Org change) — must extend the existing users-transfer path, not a bare writer. **[deferred to S104]**
- WARNING — reseed must clear the canonical event/audit state. **[S103, TASK-10303]**
- WARNING — the cutover "zero consumers" criterion must exclude the retained name-keyed retired serializer mappings. **[S103, TASK-10304]**
- WARNING — pin the retained-approval-safety claim (approve/reject/reopen + my-reports 200 post-drop). **[S103, TASK-10305]**
- NOTE — split recommended → applied (this S103 + S104).

_Internal Reviewer:_
- WARNING — full cutover census (add `OrganizationRepository.GetActiveEnhederWithTagCountsAsync`, `/organizations/tree` `Enheder`+`BuildEnhedForest`, `EnhedBackfillSeeder`, `Contracts/*`, the transfer-tag-clear block). **[S103, TASK-10304]**
- WARNING — leader designate/remove must take the `unit-org-` advisory. **[deferred to S104 with leader-write logic]**
- WARNING — Phase-1 by-construction authority-absence guard (units absent from the scope/approval path; roster filter inside `accessibleOrgs`). **[S103, TASK-10305]**
- NOTE — cutover is approvals-RUNTIME-load-bearing (dashboard 500s), not compile-only; confirmed the live authority path is enhed-free (so "approvals keep working" holds); the `enhed_label` drop lands with the projection-write removal (same change).

### Resolution
All foundation-half BLOCKERs/WARNINGs absorbed into this plan's TASK-10301–10305. The units-CRUD/concurrency/cross-Org-transfer items (incl. Codex BLOCKER-2 + the Reviewer leader-advisory WARNING) are carried to S104 (its own Step-0b). The transfer-tag-clear block (`AdminEndpoints.cs:1913-1944`) is REMOVED in S103 (TASK-10304) since enhed tags cease to exist; the unit re-home that replaces it is S104.

## Architectural Constraints Verified
- [ ] P1 — Organisation stays the authority anchor (D1); `units` structure-only.
- [ ] P3 — new events registered (EventSerializer + audit mappers); greenfield reseed includes the event store/outbox (D10/D9).
- [ ] P4 — `units` carry `version`; the retained `reporting_lines`/`manager_vikar` substrate untouched (D6).
- [ ] P6 — the Organisation-direct `primary_org_id` keeps attribution working (D2) — attribution test (NULL-`unit_id` case; the demo gives units a seed-time derived anchor).
- [ ] P7 — by-construction authority-absence guard (units absent from `OrgScopeValidator`/`RoleScope.CoversOrg`/`DesignatedApproverAuthorizer`/`ValidateEmployeeAccessAsync`); the roster filter stays inside `accessibleOrgs`.
- [ ] P8 — build + full pyramid + the new tests green.

## Task Log

### TASK-10300 — Sprint open (re-scope + entropy + plan)
| Field | Value |
|-------|-------|
| **ID** | TASK-10300 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Orchestrator Approved** | yes — 2026-06-28 |

**Description**: Split the Step-0b-reviewed Phase-1 plan into S103 (foundation) + S104 (units CRUD), per the owner + both review lenses. Entropy scan recorded.

---

### TASK-10301 — Schema: `units` + `unit_leaders` + `users.unit_id`; drop `enheder`/`user_enheder`/`enhed_label` (ADR-038 D1/D2/D9)
| Field | Value |
|-------|-------|
| **ID** | TASK-10301 |
| **Status** | complete (DDL authored + db-schema.md regenerated + check_docs green; build/full-pyramid verification at close) |
| **Agent** | Orchestrator-approved schema change (init.sql) |
| **Components** | docker/postgres/init.sql, docs/generated/db-schema.md |
| **KB Refs** | ADR-038 D1/D2/D9, S100 (hierarchical enhed precedent) |

**Description**: `units` (`unit_id UUID PK`, `organisation_id TEXT FK→organizations` IMMUTABLE, `parent_unit_id UUID NULL FK→units`, `type TEXT CHECK IN (direktion,omrade,kontor,team,enhed)`, `name`, `deleted_at`, `version`, `created_at`; partial-unique `(organisation_id, parent_unit_id, lower(name)) WHERE deleted_at IS NULL` — sibling-scoped, case-insensitive; idx org + parent) + `unit_leaders` (`unit_id`,`user_id` PK; FK→units/users) + `users.unit_id UUID NULL FK→units`. **Drop** `enheder`, `user_enheder`, `employee_profiles.enhed_label` (the column drop coordinated with TASK-10304's projection-write removal). Regenerate `db-schema.md`.

**Validation Criteria**:
- [ ] New tables/columns created; `enheder`/`user_enheder`/`enhed_label` removed; FK integrity; sibling-scoped `lower(name)` partial-unique; `organisation_id` immutable.
- [ ] `db-schema.md` regenerated + `check_docs.py` green; table count adjusted.
- [ ] The `enhed_label` drop lands in the same change as TASK-10304's projection-write removal (no write references a dropped column).

---

### TASK-10302 — Events: `Unit*` / `UnitLeader*` / `UserUnitChanged` + serializer + audit mappers (ADR-038 D10)
| Field | Value |
|-------|-------|
| **ID** | TASK-10302 |
| **Status** | complete (7 events + name-keyed serializer + 7 `IAuditProjectionMapper` in `Backend.Api/AuditMappers` + Program.cs registrations; catalog rows added; build 0 errors. Stream convention `unit-{unitId}` / `user-{userId}` per Enhed* precedent. No writer yet — emitted in S104, intended.) |
| **Agent** | Data Model |
| **Components** | src/SharedKernel (Events), src/Infrastructure/EventSerializer.cs, audit-projection mappers + catalog |
| **KB Refs** | ADR-038 D10, DEP-003, ADR-026, PAT-004 |

**Description**: `UnitCreated/Renamed/Moved/Deleted`, `UnitLeaderDesignated/Removed`, `UserUnitChanged` — `DomainEventBase`; `EventSerializer` registration (DEP-003); `IAuditProjectionMapper` + registration + catalog rows (ADR-026; `target_org_id` via the derived `organisation_id`). Retired `Enhed*`/`UserEnhederChanged` **kept name-keyed**. (Events are registered now; the WRITERS that emit them ship in S104 with the units CRUD — registering ahead is safe and lets the audit catalog/contract land with the model.)

**Validation Criteria**:
- [ ] All new events registered (Constraint Validator DEP-003 parity); audit mappers + catalog rows present; retired types stay name-keyed.

---

### TASK-10303 — Greenfield reseed: demo unit tree + memberships + leaders + edges (ADR-038 D9)
| Field | Value |
|-------|-------|
| **ID** | TASK-10303 |
| **Status** | complete (demo unit tree under STY02: Direktion→Driftsområdet→IT-Drift→Team Infrastruktur; mgr01 leads IT-Drift [member-invariant holds]; emp002 in IT-Drift, emp005/emp010 in the team; rest unit_id NULL [D2 Organisation-home case]; fixed UUIDs for reseed determinism. Full design org tree + DemoSeeder cutover = follow-on.) |
| **Agent** | Orchestrator-approved seed (init.sql demo seed) + Infrastructure (SqlEmitter) |
| **Components** | docker/postgres/init.sql (demo seed), demo seed emitter |
| **KB Refs** | ADR-038 D9, `design_handoff_org_medarbejdere/org-data.js` |

**Description**: Author a coherent demo: `organizations` (MAO+Organisation, unchanged) + a `units` tree (direktion→…→enhed) under each Organisation + single `users.unit_id` memberships + `unit_leaders` designations + the existing `reporting_lines` primary edges — directly into the new shape (no migration). Org-level admins/directors home at the Organisation (`unit_id` NULL → `primary_org_id` direct).

**Validation Criteria**:
- [ ] Fresh DB boots FK-valid: complete unit tree + memberships + leaders + edges; no orphaned references.
- [ ] **Canonical event/audit state greenfield:** zero `Enhed*`/`UserEnhederChanged` rows in `events`/`event_streams`/`outbox_events`/`outbox_messages`/`audit_projection`; no audit row references the retired model.

---

### TASK-10304 — Enhed + EmployeeProfile consumer cutover (ADR-038 D11; Codex BLOCKER-1)
| Field | Value |
|-------|-------|
| **ID** | TASK-10304 |
| **Status** | complete (cutover done — `dotnet build` 0 errors + frontend build 0 + 517 vitests green; zero live enhed consumers; `EnhedLabel` removed end-to-end from `EmployeeProfile*`; Enhed* events/serializer/`UserEnhederChanged` mapper KEPT name-keyed; roster/search `enhedLabel` field kept [org-name / null] for FE shape stability. 3 dead backend enhed test files deleted. **Declared follow-ups → TASK-10305:** runtime-failing regression tests [`MedarbejderRosterReadTests`, `PeriodStatusAndPersonSearchReadsTests`, `Pass1EndpointContractTests` /enheder, S98/S92 enhed asserts]; the `check_endpoint_contracts.py` /enheder registry entry; the `StatsTid.DemoSeed` tool still emitting `enhed_label`.) |
| **Agent** | Backend API + Infrastructure + UX (cross-domain authorized) |
| **Components** | **Full census:** `ApprovalPeriodRepository` (roster/search `enhed_label` JOIN ~:658/:854 — runtime-load-bearing); `OrganizationRepository.GetActiveEnhederWithTagCountsAsync` (~:166) + `/organizations/tree` `Enheder`+`BuildEnhedForest` (`AdminEndpoints.cs` ~:613) + the Enhed CRUD endpoints + the transfer-tag-clear block (`AdminEndpoints.cs:1913-1944`); the **EmployeeProfile cutover** (`EmployeeProfileRepository.cs` ~:196/:285/:749/:791, `EmployeeProfileEndpoints.cs` ~:259, the shared profile model, the profile audit mapper, the `EmployeeProfile*` event `EnhedLabel` field); `EnhedRepository.cs`/`EnhedBackfillSeeder.cs` (removed); `Contracts/OrgTreeResponse.cs`+`EnhedListResponse.cs`; frontend `EnhederPanel`/`EnhedTagPicker`. |
| **KB Refs** | ADR-038 D11, ADR-022 (profile events), PAT-010 |

**Description**: Remove/repoint EVERY live consumer of `enheder`/`user_enheder`/`enhed_label` so the greenfield drop breaks neither build NOR runtime (the roster/dashboard `EnhedLabel` JOIN → a 500 at runtime). Drop `EnhedLabel` from `EmployeeProfile*` events/DTOs/audit/repo end-to-end (greenfield — no historical replay; carries no rule/payroll meaning). Repoint roster/search/org-tree off the enhed tables (the `?enhedId` filter + the tree `Enheder` nesting are removed this sprint — unit-based listing returns in S104/Phase 3). The transfer-tag-clear block is removed (enhed tags gone). FE = minimal-keep-working (remove dead enhed panels); the Enhedsspor UI is Phase 3.

**Validation Criteria**:
- [ ] **Zero LIVE SQL/DI/endpoint/FE consumers** of `enheder`/`user_enheder`/`enhed_label`/`EnhedRepository`/`EnhedBackfillSeeder` — EXCLUDING the retained name-keyed retired serializer mappings (TASK-10302).
- [ ] `EmployeeProfile*` events/DTOs/audit/repo carry no `EnhedLabel`; build 0/0; EmployeeProfile* replay green on a fresh DB.
- [ ] Frontend builds; no dead-button regression (vitest).

---

### TASK-10305 — Tests (foundation hooks)
| Field | Value |
|-------|-------|
| **ID** | TASK-10305 |
| **Status** | complete (survivor tests fixed [roster/search → org-name/null; Pass1 /enheder retired; S98 enhed asserts removed]; DemoSeed tool no longer emits enhed_label [29/29 demoseed green]; `check_endpoint_contracts.py` /enheder registry cleaned + green; 4 new foundation tests [`UnitAuthorityAbsenceTests` non-Docker passes; `UnitFoundationTests` Docker-gated: reseed-FK-valid + derived-anchor + retained-approval-no-500 + shared-unit-grants-nothing]. **dotnet build 0 errors; unit 852/852; demoseed 29/29.** Docker-gated regression compiles, NOT run locally [no Docker] → CI-pending. Orchestrator regenerated the stale `99-demo-seed.sql` overlay.) |
| **Agent** | Test & QA |
| **Components** | tests/** |
| **KB Refs** | ADR-038 (test hooks), FAIL-002 |

**Description**: Reseed determinism + FK integrity (fresh DB boots identically, complete tree); the **derived-anchor attribution** (payroll/scope read `primary_org_id` for the NULL-`unit_id` Organisation-homed case; the seed-time unit-derived case); the **retained-approval safety** (approve/reject/reopen + the my-reports dashboard reads return 200 post-drop; roster/search/org-tree no longer query `enheder`/`user_enheder`); the **by-construction authority-absence guard** (units/unit_id/unit_leaders structurally absent from `OrgScopeValidator`/`RoleScope.CoversOrg`/`DesignatedApproverAuthorizer`/`ValidateEmployeeAccessAsync`; the roster filter stays inside `accessibleOrgs`). (The D4 see==act + deep-tree-ancestor RED tests land in Phase 2; the held-lock interleave + cycle-guard land in S104 with the CRUD.)

**Validation Criteria**:
- [ ] Reseed-determinism + attribution + retained-approval-safety + authority-absence tests green (RED-on-old where applicable).
- [ ] Full pyramid green; FAIL-002 sheds isolation-cleared if any.

---

### TASK-10306 — Validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-10306 |
| **Status** | complete |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [x] `dotnet build` 0 errors; local pyramid green (852u + 29demoseed + 517fe); Docker tier CI-pending.
- [x] Constraint-Validator spot-check: DEP-003 (7 new events registered — verified by both lenses + build); no endpoint auth weakened (Step-7a confirmed); no FindFirst("scopes") added.
- [x] Step-7a dual-lens (high-risk schema) — 0 BLOCKER / 0 WARNING; 2 NOTEs → S104 follow-ups.
- [x] INDEX/ROADMAP updated (S104 promoted); commit + push + CI-verify (CI-pending Docker tier).

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / wage mappings / overtime / absence | N/A | No rule/payroll-logic change. |
| Retroactive recalculation stable | verified-by-test | The Organisation-direct `primary_org_id` keeps payroll/settlement attribution correct (TASK-10305 attribution test). |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes (high-risk: schema migration + auth-adjacent cutover) |
| **Sprint-start commit** | `5ee88a6` |
| **Command** | `codex review "..."` (prompt-alone, uncommitted) + Reviewer Agent |
| **Review Cycles** | 1 |
| **Findings** | **0 BLOCKER, 0 WARNING, 2 NOTE** |
| **Resolution** | clean — no fixes; 2 NOTEs recorded as S104 follow-ups |

Artifacts: `.claude/reviews/SPRINT-103-step7a-{codex,reviewer}.md`.
- Codex: "no blocking or actionable correctness issues"; independently ran `UnitAuthorityAbsenceTests` (2 passed).
- Reviewer: clean — D5 boundary preserved BY CONSTRUCTION (authority files unit-free + the domain model carries no `unit_id`); event/version substrate intact; FK-valid schema+reseed; cutover complete, no floor weakened.
- NOTE (→ S104): widen `UnitAuthorityAbsenceTests` regex to catch a future PascalCase `.UnitId` read before the unit-leader approval path is wired.
- NOTE (→ follow-up): DemoSeed `StructuralModels.cs`/`GenerateEnheder`/`EnhedFragments` carry inert enhed dead-code (harmless — emitter no longer references it; regeneration emits no dropped-table SQL).

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 852 | all passing (local; +2 `UnitAuthorityAbsenceTests`) |
| Regression (Docker-gated) | — | **CI-pending** (Docker unavailable locally; incl. the new `UnitFoundationTests` — reseed-FK / derived-anchor / retained-approval-no-500 / shared-unit-grants-nothing) |
| Smoke | — | CI-pending (Docker) |
| DemoSeed | 29 | all passing (local) |
| Frontend (vitest) | 517 | all passing (local; −49 vs S101 — removed enhed panel/picker/hook tests) |

Pyramid is CI-pending on the Docker tier; the local tiers (852 unit + 29 demoseed + 517 FE) are green. Final counts confirmed at CI backfill.

## Sprint Retrospective

**What went well**: The owner's split (S103 foundation / S104 CRUD) held up — the foundation landed with a green build + local tiers and a CLEAN Step-7a (0B/0W), the rarity for a schema-migration sprint. The keystone D5 boundary came out preserved BY CONSTRUCTION (the authority files never learned about units), which both lenses confirmed independently. The Step-0b BLOCKER about the `enhed_label`/`EmployeeProfile*` depth was real — the cutover agent found EnhedLabel threaded through 3 profile events + audit + repo, exactly as predicted, and removing it end-to-end was the bulk of the work. The 3-agent fan-out (events → cutover → tests) sequenced cleanly with no merge conflict.

**What to improve**: Docker unavailable locally → the Docker regression tier is CI-pending (1st CI-pending close since S101; not consecutive — S102 was design-only). The DemoSeed generator's inert enhed dead-code should have been caught in the cutover census.

**Knowledge produced**: no new ADR/PAT (implements ADR-038). Follow-ups → S104: the units CRUD + two-regime concurrency + the cross-Org-transfer extension (Codex S103-plan BLOCKER-2) + the unit-leader approval path (D4) + the see==act reads + the LOCKED-boundary RED test + the absence-guard regex widen; plus the DemoSeed dead-code cleanup.
