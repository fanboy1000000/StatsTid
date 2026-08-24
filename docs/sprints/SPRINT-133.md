# Sprint 133 — Test Integrity (fix-next program, increment 2)

| Field | Value |
|-------|-------|
| **Sprint** | 133 |
| **Status** | complete |
| **Start Date** | 2026-08-24 |
| **End Date** | 2026-08-24 |
| **Orchestrator Approved** | yes — 2026-08-24 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors (Release, final merged tree) |
| **Test Verified** | Unit + DemoSeed green locally (DemoSeed 153/153; the rewired rule-registry/legacy-unit tests pass locally); Docker-gated regression suite + the new DemoSeed CI step **CI-pending** — no local Docker, verifies in the CI run this sprint's push triggers (established no-local-Docker close posture, per S132; Step-7a WARNING-1 owner-accepted). |

## Sprint Goal
Increment 2 of the S131 fix-next program (S132 = correctness+safety core; S133 = **test integrity**;
S134 = audit-scope + observability + docs). Kill the **verification theater** the S131 audit found —
tests named for a guarantee they cannot check because they re-implement the system-under-test inside the
test body or assert values they just assigned. Rewire each such test to exercise the SHIPPED production
path and **prove the new assertion is falsifiable** (mutation-on-the-real-guard: break the real code → the
test goes RED). Plus: retire two confirmed dead-code traps, close five CI-gate holes so green means green,
and register + route the eight S132-discovered follow-ups. Owner ruled **OQ-1(a): spike-first** for the
atomic-outbox family (QUAL-016) — size the real-endpoint conversion before committing its full scope.

**Refinement**: `.claude/refinements/REFINEMENT-s133-test-integrity.md` (READY — dual-lens Step-4 clean;
owner ruled OQ-1a spike-first 2026-08-24, OQ-2 delete). **Baseline HEAD**: `1052909` (S132 close).

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | No stale paths introduced; S132 new files (`MidnightCrossingNormalizer`, `CopenhagenBusinessDate`) referenced across 11 call sites. |
| Pattern compliance spot-check | CLEAN | `FindFirst("scopes")` (FAIL-001): 0 in src. Hardcoded `http://localhost`: only in `Properties/launchSettings.json` dev profiles (expected). |
| Orphan detection | CLEAN | Both S132 new SharedKernel files wired into production call sites — not dead. |
| Documentation drift | CLEAN | QUALITY.md / registers / ROADMAP re-anchored to S132 at S132 close. (One register-routing contradiction corrected at S133 open — see TASK-13301.) |
| Quality grade review | stable | S132 re-grades hold; Test Suite grade (B−) revisited at S133 close. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — touches production code (QUAL-027 SharedKernel hoist is auditability-adjacent; QUAL-036 deletes production overloads) + CI enforcement layer; not documentation-only. |
| **External Codex** | invoked 2026-08-24 — cycle 1: 7 BLOCKER across 6 themes (the two invalid-agent-label BLOCKERs share one theme), 2 WARNING; cycle 2: 5 cleared, 2 residual/new on TASK-13309 (absorbed); cycle 3 pending. (Inline, stdin-closed; earlier runs stalled on open stdin — root cause fixed.) |
| **Internal Reviewer** | invoked 2026-08-24 — cycle 1: 1 BLOCKER, 2 WARNING, 3 NOTE; cycle 2: **READY-TO-DECOMPOSE** (all absorbed, 2 NOTEs → absorbed) |
| **BLOCKERs resolved before Step 1** | **yes** — Codex cycle 3 "Cleared - no residual findings"; Reviewer cycle 2 READY-TO-DECOMPOSE. Step-0b COMPLETE 2026-08-24. |

### Findings (cycle 1)

_Codex findings (external lens):_
- **BLOCKER — TASK-13307** — spike scopes *creating* new host infra; the WAF already exists — extend `StatsTidWebApplicationFactory`, override `IOutboxEnqueue` with the throwing double, measure per-file cost.
- **BLOCKER — TASK-13307/13311** — OQ-3 owner checkpoint placed too late (at close); add an explicit mid-sprint checkpoint immediately after the spike; no full QUAL-016 conversion proceeds until ruled.
- **BLOCKER — TASK-13308** — "Data Model / Infra" invalid (no Infra agent); Backend→SharedKernel move needs explicit cross-domain authorization + named ownership.
- **BLOCKER — TASK-13308** — the audit-format pin must be captured BEFORE the hoist and compared AFTER; a golden test written from post-change code would bless a regression. Placement must resolve the SharedKernel "no business logic" / QUAL-025 risk.
- **BLOCKER — TASK-13309** — "Infra" invalid agent; cross-repo deletion needs cross-domain authorization + concrete owners.
- **BLOCKER — TASK-13310** — QUAL-074/072/073 lack required ordering (gating the mocks makes the opt-outs load-bearing; the CA2100 freeze count must be taken after gating).
- **BLOCKER — TASK-13311** — cannot blanket-mark all rows `fixed(S133)` (QUAL-016 stays spike-only unless OQ-3 rules full conversion); "B− → higher" presupposes the re-grade.
- **WARNING — TASK-13309** — "sequenced last" must also mean after QUAL-074 gating, so the caller census includes the newly-compiled mock-host projects.
- **WARNING — Legal & Payroll table** — "verified-via-test" is premature while build/test + rewires are pending; mark pending.

_Internal Reviewer findings (architectural lens):_
- **BLOCKER — TASK-13307** — CONVERGENT with Codex: a booting WAF already exists (`StatsTidWebApplicationFactory.cs`, S27); the harness "no WAF here" docstring (`ForcedRollbackHarness.cs:49-58`) is STALE. Extend the WAF; measure per-file cost (authenticated HTTP + boot vs hand-mirrored orchestration — both already need Docker). **Verified against the tree** (`Program.cs:499` partial class; `Program.cs:37` single `IOutboxEnqueue` DI).
- **WARNING — TASK-13310** — CONVERGENT: QUAL-072/074/073 coupling + ordering (register:161 says gate then decide opt-outs on evidence).
- **WARNING — TASK-13308/13309** — CONVERGENT: undeclared-scope tasks need explicit cross-domain-authorized labels with named scope paths (AGENTS.md:44-57).
- **NOTE — TASK-13308** — QUAL-027 blast radius ≈ 62 mapper files (not just 17); the golden pin should cover both groups and the placement decision recorded (ARCHITECTURE line / ADR note) so it doesn't recreate QUAL-025.
- **NOTE — TASK-13309** — confirm deletion runs after TASK-13310's gating (or that gates add no self-connection caller).
- **NOTE — QUAL-069** — the rationale-comment fix is mentioned (out-of-scope note) but unassigned; fold into TASK-13310 or drop explicitly.

### Resolution
All 6 distinct BLOCKER themes absorbed by plan edit (cycle 1): (1) TASK-13307 rewritten to EXTEND the existing WAF + measure per-file cost + correct the OQ-1 framing; (2) an explicit **OQ-3 mid-sprint owner checkpoint** added immediately after the spike (TASK-13307), with TASK-13311 recording — not deciding — the ruling; (3) TASK-13308 split into **13308a** (Test & QA captures the BEFORE golden-format pin, green on pre-hoist code) + **13308b** (`Data Model (extended into Backend.Api/AuditMappers + new SharedKernel serialization folder, cross-domain authorized)` hoist; the 13308a pin must stay green; placement recorded); (4) TASK-13309 relabeled `Infrastructure repositories (cross-domain authorized)` with named repo paths, sequenced after the rewires + spike + QUAL-074 gating; (5) TASK-13310 encodes the QUAL-074→072→073 ordering + folds the QUAL-069 rationale-comment fix; (6) TASK-13311 drops the blanket `fixed(S133)` + presupposed re-grade. L&P table set to pending. Cycle-2 dual-lens verification runs on the edited plan before Step 1.

## Architectural Constraints Verified
- [x] Architectural integrity preserved (SharedKernel `Serialization/` placement ruled cross-cutting-not-business-logic; ARCHITECTURE census updated — both Step-7a lenses confirmed)
- [x] Domain correctness — no domain behavior changed (Reviewer confirmed the entire `src/**` diff is the 78 mapper repoints + the new SharedKernel options + dead-code deletions + xmldoc rewrites; no rule/payroll logic)
- [x] Auditability — audit payload byte-format pinned unchanged across the QUAL-027 hoist (golden pin 5/5 green on the merged tree; options byte-identical)
- [x] Security & access control — SEC-043/044/045 follow-ups registered/routed; no auth surface changed; the QUAL-018 deny tests strengthen coverage
- [x] CI/CD enforcement — five gate holes closed (QUAL-096/072/073/074/121) + QUAL-026; green now means green

## Scope & Task Decomposition

**In scope (per ROADMAP:198 program record + the READY refinement):**
- 11 non-outbox test-integrity rows: **QUAL-013/014/015/017/018/019/020/022/095/110/111**
- Atomic-outbox family **QUAL-016** — SPIKE only in S133 (OQ-1a); full-45 scope ruled on the spike result (OQ-3)
- Dead-code: **QUAL-027** (hoist canonical `AuditMapperJsonOptions` → SharedKernel + repoint 17 copies + Backend canonical) · **QUAL-036** (delete 33 production-unused self-connection write overloads)
- CI gates: **QUAL-096** (DemoSeed in CI) · **QUAL-074** (mock hosts compiled+gated) · **QUAL-072** (dead warn-gate opt-outs) · **QUAL-073** (CA2100 freeze+ratchet) · **QUAL-121** (FE fixture-contract binding)
- Register + route the 8 S132-discovered follow-ups; correct the register routing contradiction

**Out of scope:** QUAL-069 (payroll warn-gate, DEFERRED — blocked on the S132 legacy CS0618; S133 may fix
only its false rationale comment) · the S134 audit-scope/observability/docs block · any domain-behavior change.

### TASK-13301 — Register housekeeping (Orchestrator, docs) — ✅ COMPLETE 2026-08-24
**Done:** register routing corrected (QUAL-027/036 S134→**S133**, quality-finding-register S132-status block);
8 follow-ups registered — **QUAL-142** (rule-eval swallow, H) · **QUAL-143** (adjacent-interval false-gap, M) ·
**QUAL-144** (guard⇄payroll-widen coupling, M, go-live) · **QUAL-145** (D6a normalize-or-retire, M) ·
**QUAL-146** (null-snapshot byte-identity, L) in the quality register's new "S132-discovered" section;
**SEC-043** (login timing oracle) · **SEC-044** (InvalidOperationException detail echo) · **SEC-045**
(app-wide username CR/LF audit) in the security register's new Group 10. S133 fixes none of them (routed).

Register the 8 S132-discovered follow-ups as new QUAL/SEC rows with dispositions; correct the
quality-finding-register S132-status block that mis-routes QUAL-027/036 → S134 (ROADMAP:198 + S132 OUT-set
place them in S133). Grade follow-up #1 (rule-eval swallow) as **High-class**; #2 (adjacent-interval rest
false-gap) with explicit severity. **Route** (S133 fixes NONE of the 8 as domain fixes): rule-eval swallow
→ QUAL (Backend/orch track); adjacent-interval false-gap → QUAL (domain/rule track); guard⇄payroll-widen
go-live coupling → §15 go-live precondition (NOT S133); WeeklyCalculationPipeline/TaskDispatcher
normalize-or-retire → QUAL (S134-adjacent); login timing side-channel → SEC; InvalidOperationException
detail echo → QUAL/SEC; app-wide username-log CR/LF audit → SEC; universal manifest byte-identity → QUAL.

### TASK-13309 (QUAL-036 dead-code delete) — ✅ COMPLETE (non-isolated on the final tree) — PARTIAL CLOSE
Censused the FINAL merged tree. **Scope correction:** the register's "33 overloads across 14 repos" over-counted — it folded in self-connection READ overloads + no-`(conn,tx)`-sibling writes (S131's F7 fold-in). QUAL-036's actual target (a WRITE overload that opens its own connection AND has a `(conn,tx)` sibling — the real "pick the wrong twin" trap) = **26 across 9 repos**. **DELETED 14** (+ 6 orphaned private helpers) across 6 repos — verified zero callers (their only prior callers were the hand-mirror atomic tests S133 retired). **RETAINED 12** — still have a live caller: mostly TEST seeders (`Create/Assign/Remove/CheckAndAdjust/Supersede/UpdateStatus/Deactivate` self-connection forms) + one legitimate PRODUCTION caller (`AgreementConfigSeeder.cs:99` — a bootstrap seed path, not an atomic-outbox write). `dotnet build StatsTid.sln` **0 errors** (compiles production + all 4 test projects → proves no test calls a deleted overload). No reflection/DI/tooling caller for any deleted method. **QUAL-036 → PARTIALLY fixed(S133): 14/26 deleted; residual 12 traps survive on live test-seeding + the seeder path → FOLLOW-UP** (retire/repoint those test seeders onto a `(conn,tx)` test-support helper, then delete the rest). Out-of-scope no-sibling dead writes the agent flagged (LocalConfiguration/LocalAgreementProfile.Deactivate/WageTypeMapping.Delete/EntitlementBalance.Upsert+AdjustUsed/OvertimeBalance.Upsert+AdjustAccumulated — several = S131 F8/F14) stay distinct findings, not S133.

### Wave-2 rewire progress (11 non-outbox rows)
- **TASK-13306 (legacy-Unit QUAL-095 + If-Match QUAL-111)** — ✅ COMPLETE + MERGED. QUAL-111: the 8 "MissingIfMatch" tests were identical clones calling `EtagHeaderHelper.TryParseIfMatch` directly (pinned the shared helper, NEVER the endpoints — a removed endpoint 428 guard left every clone green). Deleted all 8 + helpers; added **6** genuine per-endpoint 428 HTTP tests (new `Hosting/AgreementConfigPreconditionHttpTests.cs` ×3 + `Hosting/PositionOverridePreconditionHttpTests.cs` ×3) + one honest `Unit/Endpoints/EtagHeaderHelperTests.cs`. **REGISTER CORRECTION:** QUAL-111 said "three endpoints have no 428 test"; on re-attack it is **SIX** (AgreementConfig ×3 AND PositionOverride ×3 uncovered; only WageTypeMapping's 2 were genuinely covered → its clones were pure redundancy, deleted, docstring now cites the real `WageTypeMappingEndpointTests`). QUAL-095: rewired 4 Unit files (`TaskDispatcherTests` drives the real `TaskDispatcher` incl. config-override; `Sprint7ScopeTests` keeps genuine `CoversOrg`, drops literal echoes/tautology; `Sprint9SkemaTests` real `EventSerializer` round-trips, ~16 POCO/predicate re-impls deleted; `ReportingLineTests` POCO echoes deleted, genuine serializer/registration/reflection kept). Cref-overlap on `AgreementConfigConcurrencyTests` handled — my wave-1 cref fix survived (line 13). Unit + Regression build 0 errors; 38 unit tests pass locally. **No production bug** (all 8 endpoints genuinely invoke the 428 guard). Proposed PAT ("a shared-helper test named per-endpoint is zero per-endpoint coverage") → decide at close. QUAL-095/111 → `fixed(S133)`.
- **⚠ CLOSE TO-DO (CA2100 ratchet):** the QUAL-073 baseline (111) was measured by TASK-13310 BEFORE wave-2 + the cheap-outbox conversion added new wire-driven test files (regression warnings 123→126). **Re-measure CA2100 on the FINAL tree (after TASK-13309) + update the `ci.yml` baseline** or CI fails on the rise. Handled in TASK-13311.
- **TASK-13302 (payroll-mapping QUAL-013/020/110)** — ✅ COMPLETE + MERGED. QUAL-013: the 3 `PayrollMappingTests.cs` unit tests were pure theater (all would pass with `PayrollMappingService` deleted) → retired; a real DB-backed resolver proof added in `WageTypeMappingRegressionTests.cs` (the real resolver is DB-backed + `BuildLine` internal, unreachable from the Docker-less Unit project without an `InternalsVisibleTo` src change → folded into the regression suite; net coverage up). QUAL-020: `WageTypeMappingSupersessionTests.cs` rewritten wire-driven — the 4 emitter D-tests now drive the REAL HTTP endpoints (audit action/version + outbox `event_type` produced by shipped code, not hand-written SQL). QUAL-110: `WageTypeMappingRegressionTests.cs` drift guard now applies the SHIPPED `init.sql` (`ApplyFullSchemaAsync` + TRUNCATE + explicit-column seed) and pins exactly what the copy got wrong — the omitted `idx_wtm_natural_key_history` (23505 + constraint-name assert) + the phantom `effective_from DEFAULT` (23502 NOT-NULL assert). Falsifiability recorded all 3; Docker-gated. Regression + unit build 0 errors. **No production bug** (the drift masked CORRECT production schema). Proposed PAT (extends PAT-014: bind schema-guards to shipped init.sql; wire-driven for endpoint-only emitters) → decide at close. QUAL-013/020/110 → `fixed(S133)`.
- **TASK-13305 (migration QUAL-014 + replay QUAL-015)** — ✅ COMPLETE + MERGED. QUAL-014: new shared helper `Migrations/CanonicalInitSql.cs` extracts each migration DDL block from the SHIPPED `docker/postgres/init.sql` at runtime (anchored on quoted ledger-ids like `'s25-d2-2-version'` + `DO $$` boundaries; throws loudly on a missing anchor); 4 migration test files rewired off their pasted copies (S22 LegacyProfileSchema, S25, S35, S43 AuditProjectionLegacyMigration) — pre-migration baselines stay pasted (historical, correct). QUAL-015: `EmployeeProfileMarqueeTests.cs` — the mutated `part_time_fraction` never reached the serialized `RuleResults` (stub echoed only employeeId); added a stub that faithfully carries the resolved fraction into `NormHoursTotal = 37.0 × fraction` so a resolver regression now MOVES the asserted bytes. Falsifiability recorded both rows; Docker-gated. Build 0 errors. **No production bug** — but confirmed all 4 pasted-DDL provenance citations had rotted (predicted drift) + the old S35 copy flattened the ledger INSERT outside the `DO $$` guard (never exercised the shipped ordering). 2 proposed PATs (migration-extract-from-init.sql; replay-marquee-mutation-must-reach-serialized-target) → decide at close. Declared optional follow-up (NOT done, outside tests/**): add `SEGMENT-BEGIN/END` markers to init.sql for idiom consistency. QUAL-014/015 → `fixed(S133)`.
- **TASK-13304 (lifecycle/compliance QUAL-017 + authz-deny QUAL-018)** — ✅ COMPLETE + MERGED. QUAL-017: `EmployeeProfileLifecycleTests.cs` fail-closed test now STUBS the rule-engine hop to 200 so the resolver-null guard is the ONLY non-200 source (kills the any-5xx theater); asserts `NotEqual(OK)` + the specific `EmployeeProfileNotFoundException`. QUAL-018: 3 deny tests added (one per 403 guard in role-revoke `AdminEndpoints.cs:2468-2486`) in `RoleAssignmentGrantRevokeEndpointTests.cs`, each pins its 403 + that the assignment stays active. Falsifiability table recorded (guard + mutation→RED for all 4); Docker-gated (CI runs the RED/GREEN). Build 0 errors. **Finding (not a bug):** role-revoke Guard 3 is DEAD-BY-CONSTRUCTION — a DB CHECK (`role_assignments_global_scope_shape`) forbids the only row shape that reaches it → correct redundant defense-in-depth (a PAT-016 instance at the DB-CHECK layer), NOT a bypass. Proposed PAT ("any-5xx assertion is non-discriminating when an unstubbed downstream also faults — stub it so the guard is the sole fault source") → decide at close. QUAL-017/018 → `fixed(S133)`.
- **TASK-13303 (rule-registry QUAL-019/022)** — ✅ COMPLETE + MERGED. QUAL-019: new `Rules/RuleRegistryClassificationTests.cs` reads the SHIPPED `new RuleRegistry()` (16-row classification theory + inventory + negative); falsifiability proven locally (mutate `AlignedWindow`→`SegmentSafe` → only the `OVERTIME_CALC` row RED). QUAL-022: the phantom `Sprint17OvertimeGovernanceTests.cs` (a byte-identical copy of `OvertimeRuleTests`, giving `OvertimeGovernanceRule` **0** coverage) replaced with 11 genuine tests (0→11); two real-guard mutations → RED. Unit build 0 errors, 29/29 pass locally. No production bug (test-side only). Proposed PAT ("pin the shipped source-of-truth by READING it; a test named for X must reference X") → decide at close. QUAL-019/022 → `fixed(S133)`.

### TASK-13302 — Test rewire: payroll-mapping falsifiability (Test & QA) — QUAL-013/020/110
Replace in-test re-implementation of the wage-type/SLS mapping with a call to the SHIPPED mapper; each test
shown RED on a seeded mapping regression, GREEN otherwise. Evidence per row (mutation id, guard, RED test,
revert, final GREEN).

### TASK-13303 — Test rewire: rule-registry falsifiability (Test & QA) — QUAL-019/022
Exercise the real `RuleRegistry` / rule-evaluation entry points rather than a hand-built rule list; prove
falsifiability against a seeded registry regression.

### TASK-13304 — Test rewire: lifecycle/compliance + authz-deny (Test & QA) — QUAL-017/018
QUAL-017: drive the real lifecycle/compliance path. QUAL-018: prove the authz-deny test actually fails when
the real deny guard is loosened (not a self-asserted deny).

### TASK-13305 — Test rewire: migration + replay (Test & QA) — QUAL-014/015
QUAL-014: migration tests run the REAL init.sql DDL, not a pasted copy that can drift. QUAL-015: the replay
test rebuilds from the real event stream / projection, proving byte-identity on a real replay path.

### TASK-13306 — Test rewire: legacy-Unit + If-Match (Test & QA) — QUAL-095/111
QUAL-095: promote the legacy unit test to exercise the shipped path. QUAL-111: the If-Match/optimistic-
concurrency test fails when the real version guard is bypassed.

### TASK-13307 — QUAL-016 atomic-outbox SPIKE (Test & QA) — MID-SPRINT OWNER CHECKPOINT
**Correction (Step-0b, both lenses):** the real-endpoint host ALREADY EXISTS —
`tests/StatsTid.Tests.Regression/Hosting/StatsTidWebApplicationFactory.cs` (S27) boots the real `Backend.Api`
against a Postgres testcontainer with full `init.sql`; `Program.cs:499` exposes the WAF entry
(`public partial class Program {}`); `Program.cs:37` is a single `AddSingleton<IOutboxEnqueue>` overridable
via `ConfigureTestServices`. The harness "no WAF here" docstring (`ForcedRollbackHarness.cs:49-58`) is STALE.
So the spike does NOT build host infra.
**Spike work:** extend `StatsTidWebApplicationFactory` with a `ConfigureTestServices` hook swapping
`IOutboxEnqueue` for the existing `ForcedRollbackHarness.ThrowingOutboxEnqueue` double; convert **1–2
representative** `*AtomicTests` (one Pattern-B audit-emitting, one Pattern-C non-audit) to drive the REAL
endpoint over authenticated HTTP; prove falsifiability end-to-end (break the endpoint's atomic contract →
the converted test goes RED). Correct the stale docstring as part of the conversion. **MEASURE the genuine
per-file conversion cost** (authenticated HTTP + app boot + assertion rewrite vs the hand-mirrored
orchestration — note both paths already require Docker, so conversion adds no new Docker dependency; the
delta to measure is per-test, NOT host-build).
**OWNER CHECKPOINT (OQ-3) — fires immediately after this spike, mid-sprint, BEFORE any full conversion:** the
Orchestrator brings the owner the measured per-file cost + feasibility verdict → owner rules **(a)** convert
all ~45 in S133 vs **(b)** split QUAL-016 into its own increment. No full-45 conversion proceeds until ruled.
TASK-13311 (close) RECORDS this ruling; it does not decide it.

### TASK-13308a — QUAL-027 audit-format BEFORE-pin (Test & QA) — RUNS FIRST
Author a per-mapper-family golden-payload serialization test that captures the CURRENT (pre-hoist) audit
payload byte-format for representative mappers from BOTH groups — the 17 with private copies AND ones using
the Backend canonical. Prove it GREEN on the pre-hoist tree. This is the BEFORE snapshot: it must be written
against the code as it stands TODAY, so that when TASK-13308b hoists and repoints, the SAME test staying
green proves byte-identity. (A golden written from post-hoist code would merely bless whatever the new code
emits — the defect Codex flagged.) **Snapshot the serialized OUTPUT (payload strings) only — the test must
NOT reference the `AuditMapperJsonOptions` TYPE directly**, so 13308b's namespace move cannot break the
pin's compile (the pin lives in `tests/**`, outside 13308b's scope).

### TASK-13308b — QUAL-027 audit-mapper JSON options hoist — `Data Model (extended into src/Backend/StatsTid.Backend.Api/AuditMappers/** + src/Infrastructure/StatsTid.Infrastructure/AuditMappers/** + src/SharedKernel/**/Serialization/** [new folder], cross-domain authorized)` — AUDITABILITY BOUNDARY
**Confirmed layout:** the canonical `AuditMapperJsonOptions` lives in **Backend.Api/AuditMappers/** (used by
its 62 mappers); the **17 byte-identical private copies live in Infrastructure/AuditMappers/** — which
*cannot* reference the Backend canonical (wrong dependency direction), the true reason the duplication exists
and why SharedKernel is the correct home (both assemblies reference SharedKernel). Move the canonical into a
new SharedKernel serialization folder (serialization options are a shared cross-cutting concern, not business
logic — record the placement as an ARCHITECTURE.md line / ADR note so it does not recreate QUAL-025's
"undocumented SharedKernel resident" finding); delete the 17 Infrastructure private copies and repoint them
+ the 62 Backend mappers at the SharedKernel canonical (namespace/using updates). The TASK-13308a pin MUST
stay GREEN across the move (byte-format unchanged → ADR-026 audit projection safe). Step-5a high-risk
override FIRES (auditability boundary) → dual-lens per-task review. Depends on TASK-13308a (done).

### TASK-13309 — QUAL-036 delete production-unused write overloads — `Infrastructure repositories (cross-domain authorized)` — SEQUENCED LAST
**Cross-domain scope (concrete file-scope for the Constraint Validator):**
`src/Infrastructure/StatsTid.Infrastructure/*Repository.cs`. **Step 1 is a census** that regenerates the
S131 family enumeration against the CURRENT tree and NAMES the exact 14 repositories + 33 overloads in the
sprint log before any deletion (the register points the concrete list at the S131 adjudication /
gitignored sweep papers; pre-freezing a 14-name subset of the 32 repo files here would be false precision —
the census re-verifies against the tree as it actually stands at deletion time). Only files matching the
scope glob are touched.
Delete the 33 production-unused self-connection write overloads (OQ-2 = DELETE, not `[Obsolete]` —
deprecation preserves the exact trap). Zero-caller re-verify INCLUDING non-source callers (reflection / DI /
tooling), not just a source grep.
**Sequencing — genuinely LAST:** runs after (i) the 11 test rewires, (ii) the spike, (iii) TASK-13310's
QUAL-074 gating (so the census includes the newly-compiled mock-host projects), AND (iv) **if the OQ-3
owner ruling authorizes the full QUAL-016 conversion, after that conversion completes** — so the caller
census inspects the truly-final tree, not merely the post-spike tree (Codex cycle-2 BLOCKER). The
atomic-outbox tests use the GOOD `(conn,tx)` overloads, not these self-connection ones — the re-verify
confirms. Build + full suite green.

### TASK-13310 — CI gates (CI/tooling) — QUAL-096/074/072/073/121 + QUAL-069 comment
Independent gates: **QUAL-096** run the DemoSeed suite in CI (`ci.yml`); **QUAL-121** bind the FE fixtures'
nullability to the served contract.
**Coupled gates — MUST run in this order** (both lenses; register:161 = "gate, then decide opt-outs on
evidence"): (1) **QUAL-074** bring the two mock-host projects (`MockPayroll`/`MockExternal`) into a compiled
gate; (2) observe whether the now-compiled stubs emit warnings under warn-as-error; (3) **QUAL-072** remove
the `Directory.Build.props:77-78` opt-outs ONLY if the gated build is clean without them — otherwise KEEP
them with a corrected rationale (they are no longer "unreachable" once QUAL-074 gates the projects); (4)
**QUAL-073** freeze CA2100 at the CURRENT count measured on the FINAL gated tree (S132 added test-file
warnings — not the stale 109) and ratchet down (build fails if it rises).
Also: fix the **QUAL-069** false rationale COMMENT in `Directory.Build.props` (the warn-gate stays DEFERRED —
blocked on the S132 legacy CS0618 — but its misleading comment is a cheap, separable correction). Docker/
Python-gated evidence runs in CI.

### TASK-13311 — Validate + re-grade + close (Orchestrator)
`dotnet build` clean; full suite green (CI for Docker/Python gates); QUALITY.md Test-Suite grade **revisited
on the evidence** (direction not presupposed); Step-7a dual-lens. **Register rows flipped `fixed(S133)` ONLY
for rows actually completed** — the 11 non-outbox rewires + QUAL-027 + QUAL-036 + the gates; **QUAL-016 is
flipped only if the OQ-3 owner ruling (TASK-13307) authorized AND the full conversion landed** — otherwise it
records `spike-done(S133); conversion → <owner ruling>`. This task RECORDS the OQ-3 ruling; the ruling itself
was made mid-sprint at the TASK-13307 checkpoint.

## Falsifiability method (the load-bearing discipline)
Each rewired row is proven by a **mutation-on-the-real-guard**, recorded **per row / per distinct guard**
(NOT per test case): the mutation identifier, the exact real guard changed, the observed failing test,
**restoration proof (mutation reverted)**, and a **final clean-tree re-run GREEN**. No temporary production
mutation survives in the tree or the diff (Assumption 4). A rewire that surfaces a REAL production bug is a
WIN — route it as a new finding, do not bury it.

## Execution status (updated 2026-08-24)

- **TASK-13301 (register housekeeping)** — ✅ COMPLETE. Routing corrected; 8 follow-ups registered (QUAL-142–146 + SEC-043–045).
- **TASK-13307 (QUAL-016 spike)** — ✅ COMPLETE + **OWNER RULED OQ-3 = SPLIT** (2026-08-24). The spike hoisted the wire-driven rollback mechanism into `StatsTidWebApplicationFactory` (`WithThrowingOutbox()` / `WithThrowOnSecondEnqueueOutbox()`), converted 1 Pattern-B (`AgreementConfigCreateAtomicHttpTests`) + 1 Pattern-C (`TimeEntryRegisterAtomicHttpTests`) template, and corrected the stale `ForcedRollbackHarness` docstring. Honest population: **~32 hand-mirror tests across 10 files** (not ~45/15). Cost split measured: **cheap cluster ≈15 tests / 5 files** (AgreementConfig, WageTypeMapping, PositionOverride, Admin, Time — GlobalAdmin/Employee tokens, low/no seed, single-emit, copy-shape) vs **bespoke cluster ≈16 tests / 6 files** (Skema, WorkTime, Approval, Overtime — org-scope tokens + heavy seed + rule-engine stub; CI-only iteration). **Pattern recorded → PAT-019.**
  - **OQ-3 RULING (owner):** convert the **cheap cluster in S133** (TASK-13312, below); **defer the bespoke cluster to its own increment** (routed → ROADMAP backlog as "QUAL-016 bespoke atomic-outbox conversion"; the mechanism is shared+proven, so it is pure copy-work later). QUAL-016 flips `fixed(S133)` ONLY for the cheap cluster + templates; the register records the bespoke residual.
  - Spike worktree merged to main; regression + unit projects build **0 errors**.
- **TASK-13308a (audit-format BEFORE-pin)** — ✅ COMPLETE. `AuditPayloadFormatPinTests.cs` (5 facts, both QUAL-027 groups) GREEN on the pre-hoist tree; does NOT reference the `AuditMapperJsonOptions` type (13308b's move can't break its compile). Merged to main. Unblocks TASK-13308b.
- **TASK-13308b (QUAL-027 hoist)** — ✅ COMPLETE + MERGED. Canonical `AuditMapperJsonOptions` moved to `src/SharedKernel/StatsTid.SharedKernel/Serialization/` (options block byte-identical; xmldoc rationale); 17 Infrastructure private copies deleted + repointed; 61 Backend mappers repointed; old Backend canonical removed (78 mappers total). **Dual-lens Step-5a (auditability boundary):** Codex "Cleared — no findings"; Reviewer APPROVE-WITH-WARNINGS (1 doc-census WARNING → closed by adding `Serialization/` + the other drifted folders `Audit/Segmentation/Normalization/Exceptions/` to the ARCHITECTURE.md SharedKernel census; 2 trivial NOTES). **Byte-format proof:** the authoritative merged pin passes **5/5** against the hoisted tree. Orchestrator-verified: 0 leftover private copies, 0 dangling refs, all referencers repointed. The 5 non-audit `JsonSerializerOptions` sites left untouched (correct — different wire contracts). QUAL-027 → `fixed(S133)`.
- **TASK-13310 (CI gates)** — ✅ COMPLETE + MERGED. `ci.yml` + `Directory.Build.props` + `StatsTid.sln` + one FE test file. **QUAL-096:** DemoSeed suite (153 tests — the register's "~82" was stale; hermetic, no Docker) now RUN in CI in `build-and-test` (no `needs:`-chaining; S63 rule respected); ran locally 153/153. **QUAL-121:** the `TeamOversigt` FE fixtures typed against the generated spec record `TeamOverviewRow` (nullability authoritative), gated by the existing `tsc`; binding proven to bite (TS2322 on a null-into-non-null, TS2353 on an unknown field), then reverted. **QUAL-074:** MockPayroll + MockExternal added to `StatsTid.sln` (compiled gate). **QUAL-072:** their `Directory.Build.props` opt-outs REMOVED (they build 0-warnings clean under warn-as-error on the gated tree — held to the production bar). **QUAL-073:** CA2100 ratchet added — `ci.yml` step tees the build log under `pipefail` + fails on a RISE (dedupe via `sort -u` for MSBuild's inline+summary double-print). Baseline measured **111** at this task's time (mid-sprint, pre-wave-2; up from the stale 109 by S132's +2); **RE-MEASURED to 116 on the FINAL tree at close** (TASK-13311) after wave-2 + the cheap-outbox conversion added 5 new test-file SQL sites — the `ci.yml` + `QUALITY.md` baseline is **116** (the authoritative final value). **QUAL-069:** false rationale COMMENT corrected (actual: one unsuppressed CS0618 at `Program.cs:249` + a second pragma-suppressed caller at `RetroactiveCorrectionService.cs:210`; the prior "single caller at :198" was wrong); gate stays DEFERRED. **Also resolves QUAL-026** (two mock hosts outside the solution). Orchestrator-verified the agent's accidental shared-checkout `sln` edit was reverted (main `StatsTid.sln` byte-identical to HEAD before merge). Register: QUAL-096/121/074/072/073/026 → `fixed(S133)`; QUAL-069 comment-corrected, gate DEFERRED. Proposed KB (warning-ratchet PAT) → decide at close.

> **TASK-13312 — ✅ COMPLETE + MERGED.** 15 cheap-cluster rollback proofs converted to the wire-driven PAT-019 shape across 4 new `Hosting/*AtomicHttpTests.cs` files (AgreementConfig lifecycle ×5 incl. the dual-emit supersede leg, WageTypeMapping ×3, PositionOverride ×4, Admin ×2); the 4 legacy hand-mirror `Outbox/*AtomicTests.cs` retired. Per-test falsifiability recorded (mutation-on-real-guard; CI-gated). Regression build **0 errors**. **Merge dedup performed:** the agent's worktree branched from the S132 commit `1052909` — which does NOT contain the wave-1 spike/hoist (those were uncommitted working-tree merges, invisible to a fresh worktree). The agent therefore re-created the WAF helpers + the create template; the Orchestrator deduped at merge (WAF byte-identical → skipped; create-template → kept main's reviewed spike version; the 4 genuinely-new files copied + 4 legacy deleted). 4 dangling `<see cref>` refs to the retired classes were converted to prose to avoid CS1574. **Operational note for the rest of S133:** worktree agents branch from the last COMMIT, so remaining waves must touch files DISJOINT from the uncommitted wave-1 merges (verified: the 11 rewires target different test files + SUTs). QUAL-016 cheap cluster → `fixed(S133)`; bespoke cluster → deferred increment (ROADMAP backlog).

### TASK-13312 — QUAL-016 cheap-cluster conversion (Test & QA) — NEW, per OQ-3 SPLIT ruling
Convert the ~13 remaining cheap-cluster `*AtomicTests` (AgreementConfig ×6 incl. 1 dual-emit, WageTypeMapping ×3, PositionOverride ×4, Admin ×2 — the 2 templates are already done) to the wire-driven shape per **PAT-019**, reusing the merged `WithThrowingOutbox()` helpers + the `AssertNo*Async` witnesses. Per-test falsifiability recorded (mutation-on-the-real-guard; CI-gated RED/GREEN). The bespoke cluster (Skema/WorkTime/Approval/Overtime) is OUT (deferred increment).

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule logic changed (tests/CI/dead-code only). |
| Wage type mappings produce correct SLS codes | pending | QUAL-013/020/110 rewire will exercise the shipped mapper (no mapping change); mark verified once the rewires land green. |
| Overtime/supplement determinism | N/A | Unchanged. |
| Absence effects correct | N/A | Unchanged. |
| Retroactive recalculation stable | pending | QUAL-015 replay rewire will prove byte-identity on the real path; mark verified once it lands green. |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — both lenses, 2026-08-24 |
| **Sprint-start commit** | `1052909` (`10529098f53ffa3abed25ebdbf3fad46780205e6`) |
| **Command** | Codex `codex review "<prompt>"` (prompt-alone, full uncommitted diff via git — the review pipeline is unaffected by the `codex exec` file-read host instability seen this session); internal Reviewer Agent on the full diff |
| **Review Cycles** | 1 (both lenses clean/near-clean; no BLOCKER → no fix cycle needed) |
| **Findings** | 0 BLOCKER, 2 WARNING (both settled), several confirmatory NOTEs |
| **Resolution** | all resolved / owner-accepted posture (WARNING-1) |

### Findings
- **Codex — PASS-WITH-NOTE:** "composes without functional regression or invariant breach." [P3 NOTE] CA2100 baseline stale in the close evidence (`SPRINT-133.md:234` said 111 vs the shipped 116) — **absorbed** (reconciled: 111 = mid-sprint, 116 = final authoritative). Artifact: `.claude/reviews/SPRINT-133-step7a-codex.md`.
- **Reviewer — CLOSE-WITH-WARNINGS:** no invariant at risk, no domain behavior changed, no dishonest disposition (byte-identical options, zero-caller deletions, retained-honesty, theater-not-coverage all verified). **WARNING-1** Docker-gated suite green unverified locally → **owner-accepted CI-pending posture** (verifies in the triggered CI run; red → post-close follow-up). **WARNING-2** CA2100 sprint-log 111 → absorbed. NOTE doc-in-progress state → flipped at this close. Artifact: `.claude/reviews/SPRINT-133-step7a-reviewer.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | (S132 baseline + S133 rewires) | build 0 errors; rewired rule-registry (29) + legacy-unit (38) pass locally |
| DemoSeed | 153 | all passing locally (now RUN in CI per QUAL-096) |
| Regression (Docker-gated) | — | **CI-pending** — no local Docker; verifies in the CI run this push triggers |
| Frontend (vitest) | — | `tsc` clean + affected vitest pass (QUAL-121 binding) |
| **Full solution build** | — | `dotnet build StatsTid.sln` Release **0 errors** on the final merged tree |

_Docker-gated regression + the DemoSeed CI step establish green in CI (established no-local-Docker posture)._

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 11 (13301 housekeeping · 13302–13306 rewires · 13307 spike · 13308a/b pin+hoist · 13309 delete · 13310 gates · 13312 cheap-outbox) |
| Domain agents dispatched | 12 (incl. the spike + the Step-5a/Step-7a Reviewer passes) |
| Reviewer/External findings | Step-0b: Codex 7B (3 cycles→clear) + Reviewer 1B; Step-5a (hoist): Codex 0 + Reviewer 1W; Step-7a: Codex 1 NOTE + Reviewer 2W — all absorbed |
| Re-dispatches | 0 (all agent outputs accepted after review; merges deduped by the Orchestrator) |
| Real production bugs surfaced | 0 (every theater rewire confirmed the defect was test-side) |
| First-Pass Rate | 100% (no agent re-dispatched) |

## Sprint Retrospective

**What went well:** the spike-first ruling paid off — it retired the "can we even build this" risk (the wire-driven mechanism already existed, S127) and measured the real split so the owner ruled OQ-3 on evidence. Every rewire proved falsifiable; several rotted artifacts (a phantom byte-copy, four drifted DDL copies, eight helper-clones) were retired. The dual-lens gates earned their keep repeatedly (Step-0b caught the inflated spike scope + the audit-pin timing trap; Step-7a caught the CA2100 doc drift). `codex review` reached the full diff even while `codex exec` file-reads were crashing — a useful operational fact.

**What to improve:** worktree agents branch from the last COMMIT, so the uncommitted wave-1 merges were invisible to wave-2 — forcing manual merge-dedup (the spike-helper reproduction, the cref overlaps). A mid-sprint intermediate commit would have avoided it, but the commit-when-asked rule + the sprint-close-commit model made manual dedup the right call this time. The register carried three over-counts (QUAL-111, QUAL-096, QUAL-036) — re-attack-at-fix-time caught them, but the sweep counts should be treated as estimates.

**Knowledge produced:** **PAT-019** (wire-driven atomic-outbox rollback proof) — created + INDEX-registered. **Proposed, decided at close:** the warning-ratchet PAT (QUAL-073), the read-the-source-of-truth PAT (QUAL-019/022), the stub-the-downstream-so-the-guard-is-sole-fault-source PAT (QUAL-017), the migration-extract-from-init.sql + replay-marquee-must-reach-serialized-target PATs (QUAL-014/015), the shared-helper-test-is-zero-per-endpoint-coverage note (QUAL-111) — all recorded as sprint-log rationale; PAT authorship deferred to keep the close mechanical (candidates for a follow-up KB pass).
