# Sprint 30 — Phase 4d-2: Entitlement-Policy Versioned History + S29 Test-Harness Unblock

## Sprint Header

| Field | Value |
|-------|-------|
| **Sprint** | 30 |
| **Title** | Phase 4d-2: Entitlement-Policy Versioned History + S29 Test-Harness Unblock |
| **Status** | DRAFT (Step 0b pending) |
| **Start Date** | 2026-05-14 |
| **Projected End Date** | 2026-05-17 (4 days; slightly stretched over S29's 2 days because of new frontend page + new ADR; mitigated by Phase 1 parallelism after schema lands) |
| **Sprint-start base commit** | `41b6e89` (S29 sprint close) |
| **Sprint type** | IMPLEMENTATION — implements Phase 4d-2 backend versioned history + new admin CRUD + new admin UI page, against new ADR-021 (drafted in-sprint) |
| **Refinement** | `.claude/refinements/REFINEMENT-s30-scope.md` (Step 3 proposal + Step 4 cycles 1+2 dual-lens; both lenses converged with 0 BLOCKERs at cycle 2 — second cycle-2-converging-finite case after S29) |
| **Agents involved** | Data Model, Backend API (cross-domain authorized), Payroll Integration (consultation only — read-path migration), Frontend, Test & QA, Orchestrator-direct (KB writes + sprint plumbing) |
| **KB entries planned** | New: ADR-021 "Entitlement-Policy Versioned History" (in-sprint scope). Amendments: ADR-016 D5b extension paragraph (consumption-time-lookup variant — fifth pattern). Updates: INDEX.md, sprints/INDEX.md, ROADMAP Phase 4d-2 entry → COMPLETE, QUALITY.md re-grade for Entitlement domain. |

## Sprint Goal

Implement Phase 4d-2 — entitlement-policy versioned history — extending the ADR-020 patterns proven in S29 (planner-enrollment ✗ does not apply; D2 3-case routing ✓ applies; D3 seed idempotency ✓ applies) onto the entitlement-config surface, with a *consumption-time-lookup* variant (NOT export-time-lookup like S29) because entitlements are read by HTTP endpoints (Skema POST + Balance summary), not by `PeriodCalculationService` segment expansion. Build admin CRUD endpoints + admin UI page (no admin surface exists today; S15 left this as schema-only forward-compat per ROADMAP L312 / init.sql L1419). File new **ADR-021** as a sibling to ADR-020 — NOT a D4 amendment — per ADR-020 §122 explicit anticipation.

**Marquee invariant**: `EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig` — when an admin edits VACATION quota from 25 → 27 today and an employee submits an absence today for an already-started entitlement year (year-Y), both the Skema quota validation AND the Balance summary report year-Y-start's value (25), not the live value (27). Fails without versioned history; passes with it.

**Folded prerequisite (gated mini-task)**: TASK-3001 — diagnose `WebApplicationFactory<Program>` connection-override timing defect (blocks 8 deferred S29 HTTP-level D-tests + `PublisherStallReadYourWriteTests`). 1-day timebox; split outcome per refinement Step 5.

## Phase Decomposition

Replicates S29's pattern: Phase 1 sequential plumbing (commit-before-dispatch) → Phase 2 parallel endpoint+frontend+consumer (file-disjoint, no worktree-base-mismatch) → Phase 3 D-tests → Phase 5 docs + sprint close. TASK-3001 (harness diagnosis) runs Phase 0 ahead of everything else with 1-day timebox.

### Phase 0 — Sprint-Open Plumbing + Harness Diagnosis (1 commit each; TASK-3000 then TASK-3001 sequential)

**TASK-3000** creates `SPRINT-30.md` from the template + provisional INDEX.md row FIRST — this gives TASK-3001 a place to write its disposition row and TASK-3010 a place to read it from at Phase 3 dispatch time (resolves the cycle-2 contradiction about SPRINT-30.md lifecycle).

**TASK-3001** runs at sprint open immediately after TASK-3000, 1-day timebox. If outcome is trivial → fix lands in-sprint as TASK-3001b plumbing → HTTP-level admin-CRUD D-tests in Phase 3 use `WebApplicationFactory<Program>` (S25 pattern). If outcome is non-trivial → harness work splits to S31; S30 Phase 3 D-tests use direct-orchestration harness (S29 marquee pattern); 8 S29-blocked tests stay deferred. Either way, the Phase 4d-2 marquee D-test does NOT depend on the harness fix — it is a direct-orchestration test by design.

### Phase 1 — Plumbing (sequential, 6 commits)

Schema, audit-table CREATE, repository extensions, new event types, fixture DDL drift, seed-INSERT rewrite. Mirrors S29 Phase 1 task count.

**Critical**: Commit each Phase 1 task before dispatching the next that depends on it. **Commit ALL Phase 1 tasks before dispatching Phase 2 worktrees** to prevent the S24/S26 worktree-base-mismatch pattern. S29 followed this discipline cleanly — carry it forward verbatim.

### Phase 2 — Endpoint + Consumer + Frontend (parallel-dispatchable after Phase 1 commit, 3 commits)

Three file-disjoint tasks **dispatched in parallel without worktree isolation** (NOT `isolation: "worktree"`) per S29 Phase 2 precedent (`SPRINT-29.md:361`) — file-disjoint scopes are verified by the Orchestrator at dispatch time, so worktree isolation adds friction without safety. (a) admin-CRUD endpoints (TASK-3007), (b) consumption-site migration on Skema + Balance (TASK-3008), (c) frontend admin page (TASK-3009). S24 + S26 worktree-base-mismatch lessons apply: commit Phase 1 ledger fully before this dispatch. "Worktree-eligible" column in task table renamed to "Parallel-dispatchable" for clarity.

### Phase 3 — D-tests (sequential after Phase 2 commit, 1 commit)

Marquee + ADR-020-D2-equivalent 3-case + ETag/If-Match contract + seed-idempotency 4-case + admin-CRUD shape tests. Test-count target depends on TASK-3001 outcome (dispatch rule in TASK-3010 reads from SPRINT-30.md sprint log).

### Phase 4 — Validation (Orchestrator-direct, no new commit unless fixes)

Per WORKFLOW.md Step 4: `dotnet build` + run full test suites (unit + plain regression + Docker-gated + frontend vitest). Apply the **sprint-test-validation** skill at sprint validation time (previous + delta = current arithmetic). If failures surface beyond expected delta, dispatch fix agents and re-run. If clean, proceed to Step 7a per "Step 7a Strategy" section (required pre-commit unless user grants documented exit).

### Phase 5 — Documentation + sprint close (Orchestrator-direct, 2 commits)

New ADR-021 + ADR-016 D5b extension paragraph + INDEX update + sprint plumbing.

---

## Task Decomposition (13 declared tasks + 1 conditional TASK-3001b)

| ID | Name | Owner | Files in scope | Depends on | Parallel-dispatchable |
|----|------|-------|----------------|------------|-------------------|
| **TASK-3000** | Sprint-open plumbing — create `SPRINT-30.md` from `TEMPLATE.md` + INDEX.md provisional row | Orchestrator-direct (KB / sprint log writes per WORKFLOW.md L48-49) | `docs/sprints/SPRINT-30.md` (new — initial sections only: metadata + goal + task IDs reserved; close sections written by TASK-3012), `docs/sprints/INDEX.md` (provisional row with status=in-progress) | none | no (Phase 0 root — runs BEFORE TASK-3001 so the disposition can be written into the sprint log live) |
| **TASK-3001** | WebApplicationFactory diagnosis (1-day timebox) — disposition written into `SPRINT-30.md` TASK-3001 row | Test & QA | (read-only diagnosis — write findings to `SPRINT-30.md` TASK-3001 row created by TASK-3000) | TASK-3000 | no (Phase 0 gate) |
| **TASK-3001b** | WAF<Program> fix (CONDITIONAL — only if TASK-3001 finds trivial cause) | Test & QA or Backend API (cross-domain authorized) per finding | `tests/StatsTid.Tests.Regression/Infrastructure/` or `tests/.../WebApplicationFactory/`; possibly `src/Backend/StatsTid.Backend.Api/Program.cs` if config-line | TASK-3001 | no |
| **TASK-3002** | Schema migration `s30-d2-ec-effective-dating` + `entitlement_config_audit` CREATE | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 | `docker/postgres/init.sql` (CREATE TABLE entitlement_configs base shape at L1109-1124 + new audit table CREATE block + `schema_migrations` guarded ALTER block mirroring s29-d1 pattern at L1419+) | none | no (Phase 1 root) |
| **TASK-3003** | `EntitlementConfigRepository` extensions | Data Model (extended into Infrastructure, cross-domain authorized) — AGENTS.md L50 form-1; S29 precedent on `WageTypeMappingRepository` | `src/Infrastructure/StatsTid.Infrastructure/EntitlementConfigRepository.cs` | TASK-3002 | no (sequential plumbing) |
| **TASK-3004** | New events: `EntitlementConfigCreated/Superseded/SoftDeleted` + EventSerializer registration | Data Model | `src/SharedKernel/StatsTid.SharedKernel/Events/` (3 new records) + `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` (3 new entries; 48 → 51) | none | no (parallel-eligible with TASK-3002/3003 in principle, but cheap enough to sequence) |
| **TASK-3005** | Test fixture DDL drift coordinated with TASK-3002 | Test & QA | Mirror sites used for WTM in S29 — `tests/StatsTid.Tests.Regression/Outbox/ForcedRollbackHarness.cs`, `Segmentation/TestFixtures.cs`, `Infrastructure/TxContractTests.cs`, any entitlement-touching fixture (verify at decompose time via Grep — likely 1–3 sites; if 0 sites today, task collapses to a no-op assertion) | TASK-3002 | no |
| **TASK-3006** | Init.sql seed rewrite (ADR-020 D3 pattern — 30 entitlement seed rows) | Data Model (extended into Database Schema, cross-domain authorized) — AGENTS.md L50 form-1 | `docker/postgres/init.sql` L1140-1170-ish (the 30 INSERT VALUES tuple block) | TASK-3002 | no |
| **TASK-3007** | `EntitlementConfigEndpoints.cs` new file — admin CRUD + ADR-020 D2 3-case routing + cycle-3 same-day-only-edit validator + `reset_month` / `accrual_model` 422 guard + RBAC `.RequireAuthorization("GlobalAdminOnly")` per S29 `WageTypeMappingEndpoints.cs:29,44,345,515,629` precedent | Backend API (cross-domain authorized) | `src/Backend/StatsTid.Backend.Api/Endpoints/EntitlementConfigEndpoints.cs` (new file) + `Program.cs:124-ish` (registration line) + small `SaveEntitlementConfigResult` record | TASK-3002, TASK-3003, TASK-3004 | parallel-dispatchable (file-disjoint from TASK-3008 + TASK-3009; non-worktree dispatch per S29 precedent) |
| **TASK-3008** | Consumption-site migration — Skema + Balance two-step pattern | Backend API (cross-domain authorized) | `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:313` + `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs:120` (live-row read for `ResetMonth` → derive year-start → dated read `GetByTypeAtAsync(asOfDate=entitlementYearStart)`) | TASK-3003 | parallel-dispatchable (file-disjoint from TASK-3007 + TASK-3009; non-worktree dispatch) |
| **TASK-3009** | Frontend admin page + hook + sidebar entry | Frontend | `frontend/src/pages/admin/EntitlementConfigEditor.tsx` (new), `frontend/src/hooks/useEntitlementConfig.ts` (new), TypeScript types (`EntitlementConfig`, `EntitlementConfigPatch`) co-located in the page file or `frontend/src/types/` per S25 precedent, sidebar registration (likely `frontend/src/App.tsx` or `frontend/src/components/admin/AdminSidebar.tsx` — verify at decompose), API types in `frontend/src/lib/api.ts` (the `apiFetchWithEtag` extension already exists per S25) | TASK-3007 (HTTP shape locks first) | parallel-dispatchable (file-disjoint from TASK-3007 + TASK-3008; non-worktree dispatch) |
| **TASK-3010** | D-tests suite — marquee + 3-case + ETag/If-Match + seed-idempotency 4-case + admin-CRUD shape (5 endpoints: POST/PUT/DELETE/GET-list/GET-by-key) | Test & QA | `tests/StatsTid.Tests.Regression/Config/EntitlementConfigSupersessionTests.cs` (new file, mirrors S29 `ProfileSupersessionTests.cs` location) + `tests/StatsTid.Tests.Regression/Config/EntitlementConfigEndpointTests.cs` (new, HTTP-level — **dispatch rule: agent reads TASK-3001 disposition from `SPRINT-30.md`; trivial outcome → use `WebApplicationFactory<Program>` per S25 pattern; split outcome → direct-orchestration harness mirroring `ProfileSupersessionTests.cs`**) + marquee placement TBD (likely a new dedicated file `EntitlementQuotaCheckUsesYearStartTests.cs` mirroring S29 marquee placement in `ReplayDeterminismTests.cs` — but as a NEW file because entitlements don't have a pre-existing "replay determinism" container) | TASK-3001..3009 | no (depends on full implementation) |
| **TASK-3011** | New ADR-021 + ADR-016 D5b extension paragraph + INDEX.md | Orchestrator-direct (KB writes are Orchestrator-only per WORKFLOW.md L48) | `docs/knowledge-base/decisions/ADR-021-entitlement-policy-versioned-history.md` (new) + `docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md` (D5b extension paragraph appended) + `docs/knowledge-base/INDEX.md` | Phase 1 + Phase 2 + Phase 3 complete | no |
| **TASK-3012** | Sprint-close plumbing — fill `SPRINT-30.md` close sections + INDEX.md final row + ROADMAP + QUALITY.md + MEMORY.md | Orchestrator-direct | `docs/sprints/SPRINT-30.md` (close sections: retrospective, sprint-end HEAD, status=complete — base file created by TASK-3000), `docs/sprints/INDEX.md` (final row), `ROADMAP.md` (Phase 4d-2 entry → COMPLETE), `docs/QUALITY.md` (Entitlement domain re-grade), `~/.claude/projects/C--StatsTid/memory/` (MEMORY.md S30 line) | TASK-3011 | no |

**Validation criteria per task** are deferred to the SPRINT-30.md sprint log (created at sprint open by TASK-3000; close sections filled by TASK-3012) — the criteria mirror S29's structure verbatim, with the refinement file as authoritative source for SQL shapes, endpoint contracts, and D-test specifications.

## Critical-Path Callouts

1. **TASK-3001 gates the test-count projection but NOT the marquee delivery.** The marquee D-test is direct-orchestration by design — it inserts entitlement configs via the repository, calls Skema POST handler via the existing test fixtures, and asserts on the result. It does NOT need `WebApplicationFactory<Program>` to spin up. This means even if TASK-3001 reveals a multi-day rabbit hole, S30 still ships Phase 4d-2 cleanly. Run TASK-3001 first so the outcome is known before Phase 3 D-test design.

2. **The marquee D-test is the load-bearing Step-7a-equivalent harness** per S29 precedent. S29's marquee caught both in-flight defects (init.sql ordering bug + JsonElement round-trip) before sprint close — so external Codex/Reviewer Step 7a was skipped per user adjudication. Replicate the disposition: marquee covers the invariant, Step 7a external review is optional unless the marquee fails to surface defects.

3. **ADR-021 is NEW, not a D4 amendment to ADR-020.** Per refinement Assumption #4 + ADR-020 §122 explicit anticipation. The patterns differ — ADR-020 was tailored to WTM's export-time-lookup (planner-routed via `SegmentManifest`); entitlements use consumption-time-lookup (HTTP-endpoint-direct). Filing as a sibling ADR keeps the architectural lineage honest. ADR-016 D5b adds a *fifth pattern* paragraph (extending S29's fourth-pattern addition) documenting the consumption-time-lookup variant.

4. **Schema migration is one-shot guarded via `schema_migrations` ledger** per S25/S29 pattern at init.sql L1419+. The migration ID is `s30-d2-ec-effective-dating`. The base `CREATE TABLE entitlement_configs` at L1109-1124 stays re-runnable (greenfield-friendly per S29 TASK-2912 fix 1b lesson: pre-bake the S30 shape into the CREATE TABLE so fresh `docker compose down -v && up` produces the migrated schema without going through the ALTER ledger block — the ledger block exists to migrate *legacy* DBs, not greenfield).

5. **Audit table CREATE** mirrors `wage_type_mapping_audit` (singular) **post-S25 migration shape** (base DDL at `init.sql:1082` lacks `version_before/version_after`; those are added by the S25 migration block at `init.sql:1436`; `correlation_id` is NOT present on `wage_type_mapping_audit` — drop the claim). New table: **`entitlement_config_audit`** (singular). Columns: `audit_id BIGSERIAL PK`, `config_id UUID NOT NULL` (FK to `entitlement_configs.config_id` is *not* enforced because supersession + soft-delete create FK-invalidating histories — match `wage_type_mapping_audit` which similarly does not FK), `entitlement_type / agreement_code / ok_version` denormalized for audit-query convenience, `action TEXT CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED'))`, `previous_data JSONB`, `new_data JSONB`, `version_before BIGINT NULL` (ADR-019 D8 — created in same DDL block, not via second migration), `version_after BIGINT NULL` (ADR-019 D8), `actor_id TEXT NOT NULL`, `actor_role TEXT NOT NULL`, `timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW()`. **No `correlation_id` column** — verified absent from `wage_type_mapping_audit`. If future audit-chain work requires it, add via separate migration.

6. **Stream naming = natural-key**: `entitlement-config-{entitlement_type}-{agreement_code}-{ok_version}` (NOT per-row UUID). One stream per supersession lineage, mirrors S29 WTM precedent at `WageTypeMappingEndpoints.cs:481,602`. Collision check confirmed clean (refinement Reviewer cycle 2 NOTE): `EntitlementConfigSeeded` is registered in EventSerializer but the seeder does direct INSERTs without emitting it, so no live stream exists today.

7. **Two-step consumption pattern** is the load-bearing semantic for Skema + Balance migration: (a) read live (open) row to obtain `ResetMonth`; (b) derive `entitlementYearStart` from `ResetMonth` + the relevant date (per-absence in Skema; per-month in Balance); (c) issue dated read `GetByTypeAtAsync(asOfDate=entitlementYearStart)` for the quota fields. Under sub-fork (i) (`reset_month` immutable per natural-key), no version-skew between step 1 and step 3.

8. **`reset_month` + `accrual_model` frozen from admin scope** per Q1 sub-fork (i). The endpoint validator at TASK-3007 inspects the request payload; if it contains either field with a value differing from the predecessor live row, return **422** with error body `{ error: "reset_month and accrual_model are agreement-defining and cannot be edited via admin CRUD; create a new ok_version row instead", supplied: { reset_month?, accrual_model? }, immutable: ["reset_month", "accrual_model"] }`. Document in ADR-021. Note: this is enforced at endpoint, NOT at repository (repository accepts whatever the endpoint passes; this matches S29's separation-of-concerns where same-day-only-edit was endpoint-level only).

## Test-Count Projection

S29 ended at **807** (526 unit + 35 plain regression + 158 Docker-gated passing + 88 frontend; with 8 deferred Docker-gated HTTP-level tests).

S30 projected end-of-sprint floor (assumes TASK-3001 = trivial outcome; HTTP-level tests land):

| Suite | S29 close | S30 close (split — floor) | S30 close (trivial — stretch) | Notes |
|-------|-----------|-----------|-----------|-------|
| Unit | 526 | 526 | 526 | No `Plan()`-equivalent signature change this sprint; no new unit test required |
| Plain regression | 35 | 35 | 35 | |
| Docker-gated (passing) | 158 | 174 (+16) | 182 (+16 new + 8 unblocked) | Breakdown of +16 new: +1 marquee, +3 D2-3case, +3 ETag/If-Match (412 stale, 428 missing, 23505-vs-412 distinction), +4 seed-idempotency (fresh, re-run, post-edit, post-soft-delete), +5 admin-CRUD shape (POST/PUT/DELETE/GET-list/GET-by-key — 5 endpoints per TASK-3007); +8 unblocked = the 8 S29-deferred HTTP-level tests that come back online with TASK-3001b |
| Docker-gated (deferred/blocked) | 8 | 8 (WAF splits — same as S29) | 0 (WAF trivial — all unblocked) | Conditional on TASK-3001 outcome |
| Frontend vitest | 88 | 88 | 88+4 (+4 stretch) | Optional: a handful of vitest cases for `useEntitlementConfig` + the new editor page render; if scope-bloating, defer to S31 polish |
| **Total** | **807** | **823** (split floor) | **831** (trivial) → **835** (trivial + frontend stretch) | Floor commitment: +16 net new passing regardless of TASK-3001 outcome (823); stretch: +24 if WAF fix trivial (831) or +28 if frontend vitest also lands (835) |

**Acceptance floor**: total passing ≥ **823** by sprint close (split outcome). **Stretch**: 831 if WAF fix lands; 835 if frontend vitest also added.

## Risk Register

| # | Risk | Likelihood | Mitigation |
|---|------|-----------|------------|
| R1 | TASK-3001 turns into 4-day diagnostic rabbit hole | medium | 1-day timebox enforced; non-trivial outcome → split to S31; marquee D-test ships regardless via direct-orchestration |
| R2 | Skema quota-check semantic change (today → entitlement-year-start) breaks an existing regression test | low-medium | In-sprint find-and-fix; the failing test (if any) was likely encoding the wrong invariant pre-S30 |
| R3 | ADR-020 D2 3-case routing doesn't fit consumption-time-lookup cleanly | low | Refinement cycle 2 verified pattern transfers as-is; D2 is about supersede-vs-update routing inside the repo, independent of how *consumers* read |
| R4 | Frontend new-page work runs longer than backend (S29 had no frontend; S25 frontend was ~1 commit for 4 pages; per-page work is real) | medium | Scope-trim Step 6 to "tune annual_quota + carryover_max + description only"; defer accrual_model/pro_rate_by_part_time toggles to S31 polish; the `reset_month`/`accrual_model` 422 guard at the endpoint enforces this freeze anyway |
| R5 | Cross-domain dependency — S27 atomic-outbox flow on Skema must keep working after consumption-time-lookup change | medium | Verify marquee `PublisherStallReadYourWriteTests` (which is currently WAF-blocked anyway) doesn't regress in shape; the consumption-time change is read-only and shouldn't touch outbox-write paths |
| R6 | EventSerializer entry count discrepancy — refinement said "current S29 count + 3, verified at plan-mode" — verified count is 48, so target is 51 | low | Captured in TASK-3004 description; S18 reflection-coverage test catches misses |
| R7 | Worktree-base-mismatch from dispatching Phase 2 before Phase 1 fully committed (historical S24/S26 pattern) | low | Carry S29 discipline forward: explicit "commit ALL Phase 1 tasks before dispatching Phase 2 parallel agents" callout; verified at Phase 1 close before Phase 2 dispatch. NB: S30 Phase 2 uses **non-worktree** parallel dispatch (no `isolation: "worktree"`) per S29 precedent — file-disjoint scopes verified at Orchestrator dispatch time. The "Worktree-Base-Mismatch" risk name is preserved for historical-pattern continuity, even though the mitigation moves the discipline upstream of worktree-creation itself |
| R8 | ADR-016 D5b extension paragraph conflicts with S29's D5b reconciliation (fourth pattern) | low | TASK-3011 explicitly adds the *fifth pattern* paragraph as an additive extension; ADR-016 stays additive within ACCEPTED family (no Status bump) |
| R9 | **Test-fixture DDL drift** — pre-baking S30 columns into the base `entitlement_configs` CREATE TABLE means any test fixture that builds a DDL by hand (vs. reading `init.sql`) will be missing the new columns and tests will fail at runtime | medium | TASK-3005 Grep-verifies at decompose-time (`ForcedRollbackHarness.cs`, `Segmentation/TestFixtures.cs`, `Infrastructure/TxContractTests.cs`, any entitlement-touching fixture); if 0 sites today, task collapses to assertion. ForcedRollbackHarness DDL inlining precedent from S25 TASK-2508 / ef9ec91 carries forward |

## Explicit Exclusions (deferred-out)

| Item | Disposition | Why deferred |
|------|-------------|--------------|
| `MONTHLY_ACCRUAL` enum value at `EntitlementConfig.cs:15` | Document as dead code in ADR-021 footnote; defer activation to Phase 5+ | Under Q1 sub-fork (i), `accrual_model` is frozen from admin scope. The IMMEDIATE-only path is the only live one. Activating MONTHLY_ACCRUAL would require accrual-calculation logic (proportional monthly grant) that is out of scope for a versioned-history sprint |
| ADR-018 D13 composite ordering scheme | Phase 4e proper | Does not fire under pre-launch posture (ROADMAP L369); production-readiness fix only |
| Employee-profile versioned history (Phase 4d-3) | Future sprint | ROADMAP L356; "may itself decompose into multiple sprints"; not in S30 scope |
| Frontend toggles for `pro_rate_by_part_time` + `is_per_episode` + `min_age` edits | S31 polish (or never, if the freeze argument extends to these fields too) | Scope-trim risk R4 mitigation |
| Recompute existing `entitlement_balances` rows on admin edit | Out of scope (Q2 recommendation) | `entitlement_balances` is authoritative for "consumed and remaining"; admin edits propagate forward-only |
| Backfill `effective_from` for the 30 seed rows from "agreement start date" lookup | Use simple anchor `'0001-01-01'` OR sprint-open choice | Pre-launch posture; no production data; choice locked at sprint-open commit (TASK-3002) — recommendation: `'0001-01-01'` matching S29's `'2020-01-01'` anchor style (sentinel date, not real date) for clarity that this is a pre-launch seed anchor not a real effective date |
| Step 7a external Codex/Reviewer review | Required pre-commit by default per WORKFLOW.md L38; user may grant documented exit when marquee substantively serves Step-7a-equivalent purpose | S29 precedent: user adjudication waived Step 7a because marquee caught both in-flight defects. NOT presumed for S30 — user decides at sprint close based on marquee outcome |
| `MondayDatePicker` analogue for entitlement editor | Use plain DatePicker | Q4 recommendation — entitlements are annual-cycle, no weekday constraint |

## Worktree-Base-Mismatch Prevention

**Explicit discipline** (S24 + S26 burned us, S29 cleanly avoided): **Commit all 5 Phase 1 tasks (TASK-3002 through TASK-3006) to `main` BEFORE dispatching the Phase 2 parallel agents.** TASK-3000 / TASK-3001 / TASK-3001b run separately at Phase 0 and may commit independently. The Phase 2 dispatch in a single message contains 3 file-disjoint tasks (TASK-3007 endpoints, TASK-3008 consumers, TASK-3009 frontend) **dispatched as parallel general-purpose agents WITHOUT `isolation: "worktree"`** per S29 precedent — file-disjoint scopes verified before dispatch:

- TASK-3007 → `src/Backend/StatsTid.Backend.Api/Endpoints/EntitlementConfigEndpoints.cs` (new) + `Program.cs` (1 registration line)
- TASK-3008 → `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` + `BalanceEndpoints.cs` (existing, edit-only)
- TASK-3009 → `frontend/src/pages/admin/EntitlementConfigEditor.tsx` (new) + hooks + sidebar

`Program.cs` is the only file that *could* see a multi-agent edit (TASK-3007 adds 1 line). Mitigation: TASK-3008 has no `Program.cs` writes; TASK-3009 has no `Program.cs` writes. Single-edit on `Program.cs` by TASK-3007 only — no merge conflict possible.

## Step 7a Strategy

Per WORKFLOW.md, Step 7a (external dual-lens review at sprint close) is required pre-commit. **User may grant a documented exit** when the marquee D-test substantively serves the Step-7a-equivalent purpose — this is the S29 precedent (`SPRINT-29.md` close: "Step 7a external review skipped per user adjudication; marquee D-test caught both in-flight defects"). The plan does NOT presume the exit; the Orchestrator dispatches Step 7a at sprint close UNLESS the user explicitly waives it citing the marquee outcome. If the marquee surfaces a defect class Step 7a would normally catch (e.g., a same-day routing edge case that only appears under HTTP), this strengthens the waiver argument; if the marquee runs clean and the user wants reassurance, Step 7a runs with cycle-cap discipline (≤2 cycles per lens per `feedback_thrash_defer_real_world.md`).

## Sprint Close Criteria

- [ ] `dotnet build` clean (0 errors; preserve the 19 pre-existing CS0618 warnings unchanged)
- [ ] All Phase 1 tasks committed before Phase 2 dispatch (worktree-base-mismatch prevention)
- [ ] Schema migration `s30-d2-ec-effective-dating` ledger-tracked + idempotent under `docker compose down -v && up`
- [ ] `entitlement_config_audit` table created (singular) with ADR-019 D8 version-transition columns + CHECK including SUPERSEDED
- [ ] `EntitlementConfigRepository` has `GetByTypeAtAsync` + `GetByAgreementAtAsync` + `(conn, tx)` overload + `SupersedeAndCreateAsync` + `SoftDeleteAsync`
- [ ] `EntitlementConfigEndpoints.cs` exists + registered in `Program.cs` + RBAC GlobalAdmin; ADR-019 admin-strict If-Match + ADR-020 D2 3-case routing + cycle-3 same-day-only-edit validator + `reset_month`/`accrual_model` 422 guard
- [ ] EventSerializer count 48 → 51; S18 reflection-coverage test passes
- [ ] `SkemaEndpoints.cs:313` + `BalanceEndpoints.cs:120` migrated to two-step pattern (live ResetMonth → derive year-start → dated read)
- [ ] Marquee D-test `EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig` passes
- [ ] ADR-020 D2 3-case D-tests pass; ETag/If-Match D-tests pass; seed-idempotency 4-case D-test passes
- [ ] Admin-CRUD HTTP-level D-tests pass (if TASK-3001 trivial) OR direct-orchestration equivalents pass (if TASK-3001 splits)
- [ ] Frontend `EntitlementConfigEditor.tsx` + `useEntitlementConfig` hook + admin sidebar entry; banner-with-retry on 412; passes vitest if frontend tests added
- [ ] TASK-3001 outcome documented in sprint log (trivial → fixed + cited; non-trivial → ADR-flag + S31 carry-forward)
- [ ] New **ADR-021 "Entitlement-Policy Versioned History"** filed at `docs/knowledge-base/decisions/ADR-021-...md`
- [ ] ADR-016 D5b extended with "fifth pattern" paragraph (consumption-time-lookup variant)
- [ ] Total tests ≥ **823** passing (807 baseline + 16 floor; stretch 831 if WAF trivial; 835 if WAF + frontend vitest)
- [ ] No `entitlement_balances` rows retroactively recomputed by admin edit; forward-only propagation verified via D-test (Q2 explicit-exclusion assertion per Reviewer N8)
- [ ] SPRINT-30.md status = complete; sprint-end HEAD recorded; Sprint Retrospective written; INDEX.md updated; ROADMAP Phase 4d-2 → COMPLETE; QUALITY.md re-graded for Entitlement domain
- [ ] MEMORY.md S30 line added (separate edit outside repo)

---

## Step 0b Plan Review

### Cycle 1 — Dual-Lens

**External (Codex):** 1 BLOCKER + 3 WARNINGs + 4 NOTEs (verification).
- BLOCKER: Domain-ownership labels — TASK-3002/3003/3006 said "Data Model" but those files (`init.sql` + Infrastructure repo) are outside Data Model scope per AGENTS.md L14-16. **FIXED** — relabeled to "Data Model (cross-domain authorized — ...)" form-2 label per AGENTS.md L51.
- WARNING: Audit-table "mirror" claim wrong — `wage_type_mapping_audit` base DDL lacks `version_before/version_after/correlation_id` (added by S25 migration; `correlation_id` absent entirely). **FIXED** — Callout 5 rewritten to "post-S25 migration shape" + dropped `correlation_id` column.
- WARNING: RBAC name wrong — should be `"GlobalAdminOnly"` policy not "GlobalAdmin." **FIXED** — TASK-3007 row + Sprint Close Criteria reference the policy name explicitly.
- WARNING: Test-count math inconsistent (807+15=822 but stretch said 830) + admin-CRUD undercounted GET-by-key. **FIXED** — projection table rewritten with explicit split-floor (823) and trivial-stretch (831/835) columns; admin-CRUD bucket bumped to +5 endpoints.

**Internal (Reviewer Agent):** 0 BLOCKER + 4 WARNINGs + 9 NOTEs (verification).
- W1: Phase 4 (Validation) missing from phase decomposition. **FIXED** — Phase 4 explicitly added between Phase 3 and Phase 5.
- W2: "Worktree-eligible" column misleading vs. S29's non-worktree parallel dispatch precedent. **FIXED** — column renamed to "Parallel-dispatchable"; Phase 2 description tightened.
- W3: TASK-3010 conditional dispatch rule needs explicit resolution. **FIXED** — TASK-3010 row now says "agent reads TASK-3001 disposition from SPRINT-30.md."
- W4: Test-fixture DDL drift risk should be explicit risk row. **FIXED** — added R9 with TASK-3005 mitigation citation.
- NOTE-8: Missing `entitlement_balances` non-recomputation close criterion. **FIXED** — added to Sprint Close Criteria.
- All other NOTEs are confirmatory verifications (PeriodPlanner unchanged, EventSerializer count 48 verified, consumption sites at SkemaEndpoints.cs:313 + BalanceEndpoints.cs:120 verified, ADR-021 framing defensible, CS0618=19 verified).

### Cycle 2 — Codex Re-Review

Codex cycle 2 invocation result: 1 BLOCKER + 2 WARNINGs + 2 NOTEs (verifications clean).
- BLOCKER: Owner-relabel wording still nonconforming — needed strict AGENTS.md L50 form-1 `<primary agent> (extended into <other scope>, cross-domain authorized)` wording. **FIXED** — TASK-3002/3006 use `Data Model (extended into Database Schema, cross-domain authorized)`; TASK-3003 uses `Data Model (extended into Infrastructure, cross-domain authorized)`.
- WARNING: Step 7a treated as optional/skippable conflicts with WORKFLOW.md L38 (required pre-commit unless user grants documented exit). **FIXED** — "Step 7a Strategy" section rewritten: required pre-commit by default; user may grant documented exit per S29 precedent when marquee substantively serves the purpose.
- WARNING: SPRINT-30.md creation-vs-read timing contradiction — TASK-3010 dispatch rule reads from SPRINT-30.md but TASK-3012 (which created it) was at Phase 5. **FIXED** — split into TASK-3000 (sprint-open creation, runs Phase 0 root) + TASK-3012 (sprint-close completion at Phase 5). TASK-3001 disposition row now has a place to land live.

### Cycle 3 — User-Granted Waiver Re-Review

User granted cycle-cap waiver after cycle 2. Codex cycle 3 result: **0 BLOCKERs** + 2 cascade WARNINGs (residual stale references not updated by the cycle-2 sweep) + 2 confirmatory NOTEs.
- NOTE: 3 owner relabels confirmed form-1 compliant per AGENTS.md L50.
- WARNING (cycle 3): Residual "Step 7a optional" references at L52 (Phase 4 description), L140 (Exclusions table row), and minor terminology drift "Phase 2 worktrees" at L40/L126/L145. **FIXED** — L52 reworded to point at Step 7a Strategy section; L140 reworded to "Required pre-commit by default; user may grant documented exit"; L126 risk-row reworded to preserve historical risk-name continuity while documenting the non-worktree mitigation; L145 reworded to "Phase 2 parallel agents."
- WARNING (cycle 3): SPRINT-30.md "created at sprint open as TASK-3012's first edit" stale reference at L79. **FIXED** — now reads "created at sprint open by TASK-3000; close sections filled by TASK-3012."

**Cycle 3 status: clean** after residual-cascade fixes. **Plan READY for Step 1 (sprint open).** Lens convergence reached (Codex went BLOCKER → BLOCKER (mechanical) → 0 BLOCKER across 3 cycles; Reviewer was 0 BLOCKER throughout). Per `feedback_thrash_defer_real_world.md`, this is a clean cycle-3-converging-finite case with one cycle-cap waiver — comparable to S21's cycle-cap waiver precedent.

---

### Critical Files for Implementation

- `C:\StatsTid\docker\postgres\init.sql` (schema migration block + audit table CREATE + seed-INSERT rewrite — Phase 1 root)
- `C:\StatsTid\src\Infrastructure\StatsTid.Infrastructure\EntitlementConfigRepository.cs` (repository extension surface — dated reads + supersede + soft-delete)
- `C:\StatsTid\src\Backend\StatsTid.Backend.Api\Endpoints\EntitlementConfigEndpoints.cs` (new file — admin CRUD + D2 routing + validators)
- `C:\StatsTid\src\Backend\StatsTid.Backend.Api\Endpoints\SkemaEndpoints.cs` (consumption-site migration at L313 — two-step pattern)
- `C:\StatsTid\src\Backend\StatsTid.Backend.Api\Endpoints\BalanceEndpoints.cs` (consumption-site migration at L120 — two-step pattern)
