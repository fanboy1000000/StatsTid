# PLAN — Sprint 33: Phase 4d-3 Part 2 Implementation (ADR-023 cutover)

| Field | Value |
|-------|-------|
| **Sprint** | 33 |
| **Sprint type** | Implementation (against binding ADR-023) |
| **Base commit** | `55b082b` (S32 close, 2026-05-16) |
| **Binding contract** | [ADR-023](../../docs/knowledge-base/decisions/ADR-023-employee-profile-versioning-emission-and-rule-engine-cutover.md) (ACCEPTED 2026-05-16) D1-D8 |
| **Refinement** | `.claude/refinements/REFINEMENT-s33-phase-4d3-part2-impl.md` (READY after 3 cycles dual-lens) |
| **Sprint open date** | 2026-05-17 |
| **Task count** | 13 (TASK-3300..3313) |

## Sprint Goal

Implement ADR-023 D1-D8 in code. Specifically:

1. **Make rule-engine replays deterministic on 3 dated employee-profile fields** (`weekly_norm_hours`, `part_time_fraction`, `position`). Load-bearing proof = marquee D-test on **2 of the 3 fields** (`weekly_norm_hours` + `part_time_fraction`) — byte-identical `CalculationResult` under mid-period supersession. The third field (`Position`) is covered by a separate non-marquee D-test because its caller-supplied-fallback semantic per ADR-023 D1 makes a byte-identical-replay assertion conditional on caller behavior (refinement cycle 1 Reviewer BLOCKER-2 absorption).
2. **Lay a Phase 4e replay-data trail for `agreement_code`** via new `UserAgreementCodeChanged` event (emitted, never consumed in S33).
3. **Remove dead `/calculate*` endpoints** per ADR-023 D6.

Net new surfaces: 1 interface (`IEmploymentProfileResolver` in SharedKernel) + 1 implementation (`EmploymentProfileResolver` in Infrastructure), 1 event type (`UserAgreementCodeChanged`), 1 exception (`EmployeeProfileNotFoundException` in SharedKernel), 1 HTTP endpoint (DELETE `/api/admin/employee-profiles/{employeeId}`), 2 repository methods (`SupersedeAndCreateAsync` + `SoftDeleteAsync`), 1 PUT DTO extension (`EffectiveFrom: DateOnly`), 1 frontend toggle + PUT-body sync, 6 cutover sites. **Marquee proves replay-stability on 2 dated fields** (`weekly_norm_hours` + `part_time_fraction`); the third dated field (`Position`) is covered by a separate non-marquee D-test due to caller-fallback semantic per ADR-023 D1.

## Phase Decomposition

Follows S29/S30/S31 sprint shape. NO worktrees — Phase 2 cutovers are file-disjoint and don't need isolation.

| Phase | Tasks | Dispatch model |
|-------|-------|---------------|
| 0 | TASK-3300 | Orchestrator-direct (this file + SPRINT-33.md + INDEX.md + commit) |
| 1 | TASK-3301..3304 | **Sequential** — TASK-3301 first (resolver, blocks 3305-3307), TASK-3302+3303 (repository methods, block 3308), TASK-3304 (event type, blocks 3309) |
| 2 | TASK-3305..3311 | **Parallel non-worktree** — 7 file-disjoint dispatches AFTER Phase 1 commit lands (R7 commit-before-dispatch) |
| 3 | TASK-3312 | Sequential — D-test suite |
| 4 | TASK-3313 | Orchestrator-direct (sprint close) |

## Step 0a — Entropy Scan Findings

Run 2026-05-17 at sprint open:

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-023 registered in `docs/knowledge-base/INDEX.md` at S32 close; no stale paths |
| Pattern compliance | CLEAN | No new anti-patterns observed pre-S33 |
| Orphan detection | DEBT (deferred) | 80+ stale locked agent worktrees under `.claude/worktrees/` (operational noise, not load-bearing for S33 — non-worktree dispatch model). User-deferred per cleanup decision |
| Documentation drift | CLEAN | MEMORY.md synced through S32 close per session context |
| Quality grade review | N/A | Re-grade scheduled at TASK-3313 (Rule Engine + Payroll Integration domains affected by D1 cutover) |
| Refinement disposition | RESOLVED | 3-cycle dual-lens reviewed clean; cycle-cap respected (3 cycles by user-granted scope — cycles 1+2 absorbed BLOCKERs, cycle 3 verification-only) |

## Step 0b — Plan Review Trigger

**MANDATORY** per trigger criteria — sprint touches:
- **P1** (Architectural integrity) — extends ADR-020 D2 + ADR-019 D8 onto employee_profiles
- **P3** (Event sourcing / auditability) — net-new `UserAgreementCodeChanged` event + emission sites
- **P4** (Version correctness) — marquee replay-stability is the load-bearing acceptance gate
- **P7** (Security and access control) — new DELETE endpoint, AdminEndpoints PUT extension

Dispatch dual-lens (external Codex + internal Reviewer Agent) on THIS PLAN file before Phase 1 dispatches. Cycle-cap = 2 per lens.

---

## Task Log

### Phase 0 — Sprint Open

#### TASK-3300 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3300 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s33.md` (this file), `docs/sprints/SPRINT-33.md`, `docs/sprints/INDEX.md` |
| **Dependencies** | none |
| **KB Refs** | ADR-023 (binding) |

**Description**: Create PLAN-s33.md + SPRINT-33.md (from TEMPLATE.md) + INDEX.md provisional row. Commit as sprint-open.

**Validation Criteria**:
- [ ] `.claude/plans/PLAN-s33.md` exists with 13-task decomposition
- [ ] `docs/sprints/SPRINT-33.md` exists with goal + Step 0a + Step 0b placeholder + task skeleton
- [ ] `docs/sprints/INDEX.md` has Sprint 33 row (status=in-progress)
- [ ] Sprint-open commit lands on master

---

### Phase 1 — Sequential Foundation

#### TASK-3301 — `IEmploymentProfileResolver` + `EmploymentProfileResolver` + `EmploymentProfile` record refactor + `EmployeeProfileNotFoundException` + DI wiring

| Field | Value |
|-------|-------|
| **ID** | TASK-3301 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure + Backend.Api/Program.cs + Payroll.Integrations/Program.cs, cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L50 (Data Model has dominant claim via SharedKernel/Interfaces + Models + Events; resolver implementation lives in Infrastructure root which no domain agent declares as scope per AGENTS.md L46; DI wiring touches both Program.cs files) |
| **Components** | `src/SharedKernel/StatsTid.SharedKernel/Interfaces/IEmploymentProfileResolver.cs` (new), `src/SharedKernel/StatsTid.SharedKernel/Models/EmploymentProfile.cs` (refactor `sealed class` → `sealed record class`), `src/SharedKernel/StatsTid.SharedKernel/Exceptions/EmployeeProfileNotFoundException.cs` (new — sibling to existing `OptimisticConcurrencyException` shape), `src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs` (new — implements interface), `src/Backend/StatsTid.Backend.Api/Program.cs` (DI), `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` (DI) |
| **Dependencies** | TASK-3300 (sprint open) |
| **KB Refs** | ADR-023 D1, ADR-023 D2, ADR-023 D3 (fail-closed semantic for the exception), ADR-016 D5b (pattern #5 inherited), ADR-018 D5 |

**Description**: Plumbs everything S33 needs to make PCS cutover compile cleanly. Five sub-components:

1. **Interface** `IEmploymentProfileResolver` in SharedKernel/Interfaces — single method `Task<EmploymentProfile?> GetByEmployeeIdAtAsync(string employeeId, DateOnly asOfDate, CancellationToken ct)`. Placement in SharedKernel keeps PCS (Payroll.Integrations) referencing only SharedKernel + RuleEngine per ADR-002.

2. **Implementation** `EmploymentProfileResolver` in Infrastructure — single SQL JOIN against `employee_profiles` (dated) + `users` (live for `agreement_code`/`employment_category`/`primary_org_id` per ADR-023 D2). DbConnectionFactory-injected. Single method body; no caching (per-segment hot path is fine — same connection pool as repository).

3. **Refactor** `EmploymentProfile` `sealed class` → `sealed record class` so PCS site can use `with` syntax. Today the type at `src/SharedKernel/.../Models/EmploymentProfile.cs` is `sealed class` with `init`-only properties — Step 0b Reviewer cycle 3 verified clean: 7 callsites use object-initializer syntax (backwards-compatible); no Dictionary/HashSet/reference-equality usage that would shift under value-equality semantics.

4. **Exception** `EmployeeProfileNotFoundException(employeeId, asOfDate)` in SharedKernel/Exceptions — thrown by consumers in fail-closed mode (TASK-3305 PCS + TASK-3306 Compliance per ADR-023 D3); maps to 500 via existing middleware (the resolver itself returns null, never throws).

5. **DI wiring** in BOTH `Backend.Api/Program.cs` (used by ComplianceEndpoints + BalanceEndpoints + EmployeeProfileEndpoints) AND `Payroll.Integrations/Program.cs` (used by PCS). Both registrations are `AddSingleton<IEmploymentProfileResolver, EmploymentProfileResolver>()` — matches the project-wide pattern at `Backend.Api/Program.cs:54` (`AddSingleton<EmployeeProfileRepository>()`) and the stateless nature of the resolver (it only holds a `DbConnectionFactory` reference; no per-request state).

**Internal sub-step order** (Step 0b cycle 2 Reviewer WARNING absorption — pinned for agent dispatch clarity, non-load-bearing since `dotnet build` enforces dependency order anyway):
1. Interface (`SharedKernel/Interfaces/IEmploymentProfileResolver.cs`) — references the `EmploymentProfile` record (next step)
2. Record refactor (`SharedKernel/Models/EmploymentProfile.cs` class → `sealed record class`) — must compile before resolver impl
3. Exception (`SharedKernel/Exceptions/EmployeeProfileNotFoundException.cs`) — standalone
4. Resolver impl (`Infrastructure/EmploymentProfileResolver.cs`) — depends on 1+2+3
5. DI wiring (both Program.cs files) — depends on 4

**SQL contract** (binding):
```sql
SELECT
    ep.weekly_norm_hours, ep.part_time_fraction, ep.position,
    u.agreement_code, u.ok_version, u.employment_category, u.primary_org_id
FROM employee_profiles ep
INNER JOIN users u ON u.user_id = ep.employee_id
WHERE ep.employee_id = @employeeId
  AND ep.effective_from <= @asOfDate
  AND (ep.effective_to IS NULL OR ep.effective_to > @asOfDate)
```

**Returns**: `EmploymentProfile?` — null when no dated row covers `@asOfDate`. **Never throws** (caller decides fail-closed vs fallback semantic).

**Validation Criteria** (Step 0b Codex W3 absorption — full hydration shape pinned):
- [ ] `IEmploymentProfileResolver.cs` exists in `SharedKernel/Interfaces/` with single method signature
- [ ] `EmploymentProfileResolver.cs` exists in Infrastructure root and implements the interface
- [ ] `EmploymentProfile` is now `sealed record class` (was `sealed class` pre-S33); all existing init-property callsites still compile
- [ ] `EmployeeProfileNotFoundException` exists in `SharedKernel/Exceptions/` with `(employeeId, asOfDate)` constructor
- [ ] SQL JOIN includes the end-exclusive `effective_to > @asOfDate` predicate
- [ ] Returns null on no match (does NOT throw)
- [ ] **Returned `EmploymentProfile` has all 8 fields populated**: `EmployeeId`, `WeeklyNormHours`, `PartTimeFraction`, `Position` (3 dated from `employee_profiles`); `AgreementCode`, `OkVersion`, `EmploymentCategory`, `OrgId` (4 live from `users`); `IsPartTime` computed (`PartTimeFraction < 1.0m`, S31 pattern preserved)
- [ ] DI registered as `AddSingleton<IEmploymentProfileResolver, EmploymentProfileResolver>()` in `Backend.Api/Program.cs` (matches project pattern at L54)
- [ ] DI registered as `AddSingleton<IEmploymentProfileResolver, EmploymentProfileResolver>()` in `Integrations.Payroll/Program.cs`
- [ ] `dotnet build` clean

---

#### TASK-3302 — `EmployeeProfileRepository.SupersedeAndCreateAsync` (ADR-020 D2 3-case routing)

| Field | Value |
|-------|-------|
| **ID** | TASK-3302 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure, cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L50; S31 TASK-3102 precedent |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-020 D2 (3-case routing), ADR-018 D5 (`(conn, tx)` overloads), ADR-019 D8 (audit version columns) |

**Description**: New `(conn, tx)` method implementing 3-case routing under `SELECT ... FOR UPDATE`:
- **Case A** — no live row → INSERT new (`effective_from=today`, `version=1`)
- **Case B** — `existingRow.effective_from = today` → UPDATE-in-place, `version = version + 1`
- **Case C** — `existingRow.effective_from < today` → UPDATE predecessor (`effective_to=today`, version unchanged) + INSERT successor (`effective_from=today`, `version=1`)

`UpsertAsync` (S31) refactored as thin shim that delegates to `SupersedeAndCreateAsync`.

**Validation Criteria**:
- [ ] `SupersedeAndCreateAsync(conn, tx, req, expectedVersion, ct)` exists with `EmployeeProfileSupersedeAndCreateRequest` record
- [ ] 3 cases routed via `SELECT ... FOR UPDATE` against `existingRow.effective_from`
- [ ] Returns `SaveEmployeeProfileResult` discriminating Created/Updated/Superseded so endpoint can emit correct event type
- [ ] `UpsertAsync` delegates (no behavior change for existing S31 callers)
- [ ] `dotnet build` clean

---

#### TASK-3303 — `EmployeeProfileRepository.SoftDeleteAsync`

| Field | Value |
|-------|-------|
| **ID** | TASK-3303 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure, cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L50; S31 TASK-3102 precedent |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-023 D8 (predecessor version unchanged), ADR-018 D5 |

**Description**: New `(conn, tx)` method closing the live row's `effective_to` per end-exclusive `[from, to)` semantic. **No `version + 1` clause** — predecessor version unchanged per ADR-023 D8 cycle-1 absorption (refinement TASK-3303 SQL pinned).

**SQL contract** (binding — refinement cycle 1 Reviewer W2 absorption):
```sql
UPDATE employee_profiles
   SET effective_to = NOW()::date, updated_at = NOW()
 WHERE employee_id = @employeeId
   AND effective_to IS NULL
   AND version = @expectedVersion
RETURNING profile_id, version
```

Returns `(profileId, version)` where `version` is unchanged from predecessor. Audit row uses action **`DELETED`** (refinement cycle 2 Codex BLOCKER absorbed — matches `init.sql:514` CHECK constraint; event-vs-audit-action asymmetry intentional). `version_before = version_after = predecessor.version`.

**Side-effect documented**: post-soft-delete, stale `If-Match: "@expectedVersion"` retry hits **404** (partial-unique-index `WHERE effective_to IS NULL` fails on soft-deleted row), NOT 412. D-test in TASK-3312 locks this.

**Validation Criteria**:
- [ ] `SoftDeleteAsync(conn, tx, employeeId, expectedVersion, ct)` exists
- [ ] SQL shape verbatim per binding above (no version bump)
- [ ] Throws `OptimisticConcurrencyException` on version mismatch (412)
- [ ] Throws `KeyNotFoundException` when no live row (404)
- [ ] `dotnet build` clean

---

#### TASK-3304 — `UserAgreementCodeChanged` event type + EventSerializer registration

| Field | Value |
|-------|-------|
| **ID** | TASK-3304 |
| **Status** | pending |
| **Agent** | Data Model Agent |
| **Components** | `src/SharedKernel/StatsTid.SharedKernel/Events/UserAgreementCodeChanged.cs` (new), `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` |
| **Dependencies** | TASK-3300 |
| **KB Refs** | ADR-023 D2 (Phase 4e replay-data trail), PAT-004 (event-sourcing pattern) |

**Description**: New `DomainEventBase` subtype + EventSerializer dictionary entry (55 → 56 typeof). Payload pinned per refinement cycle 1 Reviewer W1 absorption.

**Type definition** (binding):
```csharp
public sealed record UserAgreementCodeChanged : DomainEventBase
{
    public string UserId { get; init; } = default!;
    public string OldAgreementCode { get; init; } = default!;
    public string NewAgreementCode { get; init; } = default!;
    public DateOnly EffectiveFrom { get; init; }
    public string? ActorId { get; init; }
    public string? ActorRole { get; init; }
    public Guid? CorrelationId { get; init; }
}
```

**Validation Criteria**:
- [ ] `UserAgreementCodeChanged.cs` exists in `StatsTid.SharedKernel/Events/`
- [ ] `EventSerializer.cs` registers the type (`["UserAgreementCodeChanged"] = typeof(UserAgreementCodeChanged)`)
- [ ] DEP-003 `EventSerializerReflectionCoverageTests` passes (auto-validates registration)
- [ ] `dotnet build` clean

---

### Phase 2 — Parallel Cutovers (7 file-disjoint tasks)

**Dispatch model**: After TASK-3304 commits land on master (R7 commit-before-dispatch), dispatch all 7 in parallel. **Files disjoint** per audit below; no worktree needed.

**Phase 2 Disjointness Audit** (Step 0b Reviewer NOTE absorption — verify zero file-overlap pre-dispatch per S24 worktree-base-mismatch cautionary precedent):

| Task | Files touched (no overlap with siblings) |
|------|------------------------------------------|
| TASK-3305 | `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` (constructor + L326-339 only) |
| TASK-3306 | `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs` (L72-79 only) |
| TASK-3307 | `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` (L64-72 fallback chain only) |
| TASK-3308 | `src/Backend/StatsTid.Backend.Api/Endpoints/EmployeeProfileEndpoints.cs` (extend PUT + add DELETE + DTO update) |
| TASK-3309 | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (PUT block L466-548 only — emit inside existing tx) |
| TASK-3310 | `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs` (L331-440 delete) + `src/Backend/StatsTid.Backend.Api/Contracts/CalculateRequest.cs` + `src/Backend/StatsTid.Backend.Api/Contracts/WeeklyCalculateRequest.cs` (deletes) + `tests/.../Orchestrator/OrchestratorScopeEnforcementTests.cs` + `tests/.../OkVersionRuntimeRegressionTests.cs` (test references) |
| TASK-3311 | `frontend/src/pages/admin/EmployeeProfileEditor.tsx` + `frontend/src/hooks/useEmployeeProfile.ts` |

Zero overlap. Test-file edits in TASK-3310 vs new test files created in TASK-3312 sit in different files in same directory — no contention via .NET SDK auto-include.

**TASK-3311 dependency on TASK-3308 — Step 0b Codex WARNING absorption**: TASK-3311 depends on TASK-3308 for backend DTO contract awareness, but the dependency is **contract-level, not file-level**. PLAN-s33.md TASK-3308 declares the binding DTO shape (`EffectiveFrom: DateOnly` required); UX Agent implements TASK-3311's frontend wire shape (`effectiveFrom: today.toISOString().slice(0,10)`) against that declared contract. Both tasks can dispatch in parallel because they touch disjoint files; the agents synchronize on the PLAN contract, not on each other's commits. Backend DTO change + frontend PUT-body change land in the same sprint commit-group so master is never in a state where one side has the field and the other doesn't.

#### TASK-3305 — PCS segmentProfile cutover

| Field | Value |
|-------|-------|
| **ID** | TASK-3305 |
| **Status** | pending |
| **Agent** | Payroll Integration Agent |
| **Components** | `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` (constructor + lines 326-339 only) |
| **Dependencies** | TASK-3301 (resolver interface + implementation + DI + `EmploymentProfile` record refactor + `EmployeeProfileNotFoundException` all delivered upstream) |
| **KB Refs** | ADR-023 D1 (PCS consumption-site), ADR-023 D3 (PCS-routed fail-closed), ADR-003 (OkVersion overlay) |

**Description** (Step 0b convergent BLOCKER-2 absorption — model + exception + interface refactor moved UPSTREAM to TASK-3301; TASK-3305 is now a pure Payroll Integration cutover): replace current segmentProfile construction at PCS.cs:326-339 with resolver-based path. Constructor gains **trailing optional parameter** `IEmploymentProfileResolver? profileResolver = null` (refinement cycle 2 Codex W absorption — existing direct-constructor test fixtures keep compiling).

**Site shape** (binding — references types created upstream in TASK-3301):
```csharp
var segmentProfile = _profileResolver is not null
    ? (await _profileResolver.GetByEmployeeIdAtAsync(profile.EmployeeId, segment.StartDate, ct))
        ?? throw new EmployeeProfileNotFoundException(profile.EmployeeId, segment.StartDate)
    : profile;  // legacy test-fixture path (resolver-null = preserves S29 fallback semantic)

if (!string.Equals(segmentProfile.OkVersion, segmentOkVersion, StringComparison.Ordinal))
    segmentProfile = segmentProfile with { OkVersion = segmentOkVersion };

if (segmentProfile.Position is null && profile.Position is not null)
    segmentProfile = segmentProfile with { Position = profile.Position };  // TASK-1802 caller-fallback preserved
```

**TASK-3305 itself touches ONLY `PeriodCalculationService.cs`** — `IEmploymentProfileResolver`, `EmployeeProfileNotFoundException`, and `EmploymentProfile` record refactor all delivered by upstream TASK-3301 in SharedKernel + Infrastructure. This keeps the task within Payroll Integration's declared scope per AGENTS.md L19.

**Validation Criteria**:
- [ ] Constructor signature `PeriodCalculationService(..., IEmploymentProfileResolver? profileResolver = null)` — trailing optional preserves S29 test fixtures
- [ ] segmentProfile constructed via resolver when non-null; falls back to caller-supplied when null
- [ ] `EmployeeProfileNotFoundException` thrown on resolver-null in production path
- [ ] All existing PCS test fixtures still compile (legacy constructor calls preserved)
- [ ] No file outside `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` modified
- [ ] `dotnet build` clean

---

#### TASK-3306 — ComplianceEndpoints cutover (fail-closed)

| Field | Value |
|-------|-------|
| **ID** | TASK-3306 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L51; `src/Backend/**/Endpoints/*.cs` is not declared in any single agent's scope; S22 TASK-2205 / S24 precedent |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs` (L72-79) |
| **Dependencies** | TASK-3301 (resolver) |
| **KB Refs** | ADR-023 D3 (rule-engine-route fail-closed), ADR-023 D8 |

**Description**: Replace inline `EmploymentProfile` constructor (hardcoded `WeeklyNormHours = 37.0m`, `EmploymentCategory = "STANDARD"`) with `EmploymentProfileResolver.GetByEmployeeIdAtAsync(employeeId, monthStart, ct)` call. Fail-closed on null per ADR-023 D3 (rule-engine-route consumer): throw `EmployeeProfileNotFoundException` → middleware maps to 500.

**Validation Criteria**:
- [ ] No hardcoded `WeeklyNormHours = 37.0m` in `ComplianceEndpoints.cs`
- [ ] Resolver call at L72-79 site
- [ ] Null resolver result throws `EmployeeProfileNotFoundException`
- [ ] D-test (TASK-3312) `Compliance_SoftDeletedProfile_Returns500FromComplianceEndpoint` passes

---

#### TASK-3307 — BalanceEndpoints cutover (graceful fallback)

| Field | Value |
|-------|-------|
| **ID** | TASK-3307 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L51; `src/Backend/**/Endpoints/*.cs` is not declared in any single agent's scope; S22 TASK-2205 / S24 precedent |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` (L66-68) |
| **Dependencies** | TASK-3301 (resolver) |
| **KB Refs** | ADR-023 D3 (pure-HTTP non-rule-engine consumer fallback) |

**Description**: Insert resolver at top of fallback chain. When resolver returns non-null → use its `WeeklyNormHours`. When null → fall through to existing chain (`AgreementConfig.GetActiveAsync` → `CentralAgreementConfigs.TryGetConfig` → `37.0m`). Graceful degradation per ADR-023 D3.

**Validation Criteria**:
- [ ] Fallback chain has resolver as first attempt
- [ ] Existing `AgreementConfig` / `CentralAgreementConfigs` / `37.0m` chain preserved as fallback
- [ ] D-test (TASK-3312) `Balance_SoftDeletedProfile_FallsThroughAgreementConfigChain_Returns200WithDefaultNorm` passes

---

#### TASK-3308 — `EmployeeProfileEndpoints` PUT extension + new DELETE

| Field | Value |
|-------|-------|
| **ID** | TASK-3308 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L51; `src/Backend/**/Endpoints/*.cs` is not declared in any single agent's scope; S22 TASK-2205 / S24 precedent |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/EmployeeProfileEndpoints.cs` (extends PUT, adds DELETE; updates `UpdateEmployeeProfileRequest` DTO) |
| **Dependencies** | TASK-3302 (SupersedeAndCreateAsync), TASK-3303 (SoftDeleteAsync) |
| **KB Refs** | ADR-023 D8, ADR-019 D2/D8 (admin-strict If-Match + audit version-transition), ADR-018 D3 (atomic outbox) |

**Description**:

**PUT extension**:
- `UpdateEmployeeProfileRequest` DTO gains required `EffectiveFrom: DateOnly` field (refinement cycle 1 dual-lens BLOCKER absorption). DTO converted to named-record syntax (not positional) so adding the 4th field doesn't break any positional-pattern matches (Step 0b Reviewer W absorption).
- **Case A 404 pre-check** (Step 0b Reviewer BLOCKER-3 absorption): BEFORE routing through `SupersedeAndCreateAsync`, the endpoint reads the live row via `repository.GetByEmployeeIdAsync(conn, tx, employeeId, ct)`. If `null` (no live row), returns 404 immediately — preserves S31 contract at `EmployeeProfileEndpoints.cs:150-154`. The repository's `SupersedeAndCreateAsync` Case A (no-live-row INSERT) is reachable only from AdminEndpoints POST `/api/admin/users` (S31 TASK-3108 4-way atomicity), NEVER from PUT. PUT-on-missing-employee semantic is 404 (not silent CREATE).
- Validator: `if (body.EffectiveFrom != DateOnly.FromDateTime(DateTime.UtcNow)) return Results.UnprocessableEntity(...)` — rejects both backdated AND future-dated per ADR-023 D8 binding "rejects `effective_from != today`". Uses `DateTime.UtcNow` to align with frontend `new Date().toISOString().slice(0,10)` UTC extraction (Step 0b Reviewer W absorption — timezone alignment documented).
- Routes through `SupersedeAndCreateAsync` (TASK-3302) — only Case B or Case C are reachable after the Case A pre-check.
- Emits `EmployeeProfileUpdated` on Case B (same-day-edit) or `EmployeeProfileSuperseded` on Case C (cross-day-edit) — distinct event types per ADR-020 D2 routing-distinct cases; both already registered in EventSerializer.cs:71-72

**New DELETE endpoint** at `/api/admin/employee-profiles/{employeeId}`:
- RBAC: `HROrAbove` + `OrgScopeValidator.ValidateEmployeeAccessAsync` (mirrors PUT)
- Admin-strict If-Match: 412 stale / 428 missing per ADR-019 D2 + `EtagHeaderHelper.TryParseIfMatch`
- Routes through `SoftDeleteAsync` (TASK-3303)
- Audit row action: **`DELETED`** (refinement cycle 2 Codex BLOCKER absorbed — matches `init.sql:514` CHECK constraint enum)
- Emits `EmployeeProfileSoftDeleted` via atomic outbox per ADR-018 D3
- Returns 204 No Content
- Error mapping: 412 / 428 / 404 / 403

**GET endpoint UNCHANGED** (refinement cycle 1 Reviewer BLOCKER-1 absorbed — no `?asOf=` extension).

**Validation Criteria**:
- [ ] PUT DTO has required `EffectiveFrom: DateOnly` (named-record syntax, not positional)
- [ ] PUT pre-checks live-row existence via `repository.GetByEmployeeIdAsync(conn, tx, employeeId, ct)`; returns 404 immediately if null (BEFORE routing through SupersedeAndCreateAsync — preserves S31 contract per Step 0b Reviewer BLOCKER absorption)
- [ ] Validator returns 422 for both backdated AND future-dated `EffectiveFrom` (using `DateTime.UtcNow` to match frontend `toISOString()` UTC extraction)
- [ ] PUT routes through `SupersedeAndCreateAsync` and emits Updated vs Superseded based on routing case (Case A unreachable from PUT after pre-check)
- [ ] DTO ISO `YYYY-MM-DD` JSON round-trip works (verify Program.cs has no custom DateOnly converter that overrides .NET 8 default)
- [ ] DELETE endpoint exists with HROrAbove + OrgScopeValidator + If-Match (412/428)
- [ ] DELETE emits `EmployeeProfileSoftDeleted` event with audit row action `DELETED`
- [ ] DELETE returns 204
- [ ] GET endpoint signature unchanged

---

#### TASK-3309 — AdminEndpoints PUT emits `UserAgreementCodeChanged`

| Field | Value |
|-------|-------|
| **ID** | TASK-3309 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L51; `src/Backend/**/Endpoints/*.cs` is not declared in any single agent's scope; S22 TASK-2205 / S24 precedent |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (PUT `/api/admin/users/{userId}` at L466-548, inside existing atomic tx at L502-539) |
| **Dependencies** | TASK-3304 (event type) |
| **KB Refs** | ADR-023 D2 (Phase 4e replay-data trail), ADR-018 D3 (atomic outbox) |

**Description**: When the narrow predicate holds, emit `UserAgreementCodeChanged` alongside existing `UserUpdated` in the same atomic tx (already at L502-539). The new event is additive — `UserUpdated` keeps its current shape.

**Emission predicate** (refinement cycle 1 Reviewer W1 absorption — null-safe + Ordinal):
```csharp
if (request.AgreementCode is not null &&
    !string.Equals(request.AgreementCode, existingUser.AgreementCode, StringComparison.Ordinal))
{
    var agreementEvent = new UserAgreementCodeChanged
    {
        UserId = userId,
        OldAgreementCode = existingUser.AgreementCode,
        NewAgreementCode = request.AgreementCode,
        EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
        ActorId = actor.ActorId,
        ActorRole = actor.ActorRole,
        CorrelationId = actor.CorrelationId
    };
    await outbox.EnqueueAsync(conn, tx, $"user-{userId}", agreementEvent, ct);
}
```

No S33 consumer — Phase 4e replay-data trail.

**Validation Criteria**:
- [ ] `UserAgreementCodeChanged` emitted inside existing atomic tx (BEFORE `CommitAsync` at L539)
- [ ] Predicate is null-safe + Ordinal compare
- [ ] No emission when `request.AgreementCode is null` (PUT body field omitted)
- [ ] No emission when `request.AgreementCode == existingUser.AgreementCode`
- [ ] D-tests (TASK-3312) `AdminPutUserChangesAgreementCode_EmitsUserAgreementCodeChangedAndUserUpdated_BothInSameTx` + `AdminPutUserOmitsAgreementCode_DoesNotEmitUserAgreementCodeChanged` pass

---

#### TASK-3310 — DELETE dead `/calculate*` endpoints

| Field | Value |
|-------|-------|
| **ID** | TASK-3310 |
| **Status** | pending |
| **Agent** | **Backend API (cross-domain authorized)** — Step 0b convergent BLOCKER absorption per AGENTS.md L51; `src/Backend/**/Endpoints/*.cs` is not declared in any single agent's scope; S22 TASK-2205 / S24 precedent |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs` (L331+L418), `src/Backend/StatsTid.Backend.Api/Contracts/CalculateRequest.cs`, `src/Backend/StatsTid.Backend.Api/Contracts/WeeklyCalculateRequest.cs`, `tests/StatsTid.Tests.Regression/Orchestrator/OrchestratorScopeEnforcementTests.cs`, `tests/StatsTid.Tests.Regression/OkVersionRuntimeRegressionTests.cs` |
| **Dependencies** | TASK-3300 (no other Phase-1 dependencies — pure deletion) |
| **KB Refs** | ADR-023 D6 (dead-code disposition) |

**Description**: Delete the 2 endpoint registrations + their request DTOs + the 2 tests that exercise them per ADR-023 D6.

**Test-coverage migration (Step 0b Reviewer NOTE absorption — explicit pre-deletion verification)**: BEFORE deleting test references, the agent verifies whether existing coverage (S25 admin endpoints D-tests + TASK-3312 new EmployeeProfileEndpoints D-tests) already exercises the same orchestrator-routing patterns (ExtractEmployeeId, S19 TASK-1903 absorbed) that `OrchestratorScopeEnforcementTests` exercises on `/calculate*`. If yes → straight deletion is safe (covered elsewhere). If a unique invariant is exercised ONLY by the doomed `/calculate*` tests, the agent migrates the test to `/api/admin/employee-profiles/{employeeId}` PUT/DELETE rather than deleting it. Cross-reference S19 TASK-1903 absorption notes for the orchestrator-scope-extraction patterns.

**Validation Criteria**:
- [ ] `MapPost("/api/time-entries/calculate", ...)` block removed from TimeEndpoints.cs
- [ ] `MapPost("/api/time-entries/calculate-week", ...)` block removed
- [ ] `CalculateRequest.cs` deleted
- [ ] `WeeklyCalculateRequest.cs` deleted
- [ ] Pre-deletion coverage audit complete (agent's task report names which S25/TASK-3312 D-tests cover each `/calculate*` test's invariant, OR enumerates migration commits)
- [ ] Test references in `OrchestratorScopeEnforcementTests` + `OkVersionRuntimeRegressionTests` either removed (with coverage-audit citation) or migrated to live endpoint
- [ ] `dotnet build` clean; no broken references

---

#### TASK-3311 — Frontend EmployeeProfileEditor as-of-date toggle + PUT-body sync

| Field | Value |
|-------|-------|
| **ID** | TASK-3311 |
| **Status** | pending |
| **Agent** | UX Agent |
| **Components** | `frontend/src/pages/admin/EmployeeProfileEditor.tsx`, `frontend/src/hooks/useEmployeeProfile.ts` |
| **Dependencies** | TASK-3308 (contract-level dependency on DTO shape declared in this plan, NOT file-level — both tasks can dispatch in parallel and synchronize on the PLAN's binding DTO contract. Backend DTO change + frontend wire-shape change land in the same sprint commit-group; master is never in a state where one side has the field and the other doesn't.) |
| **KB Refs** | ADR-023 D8 (frontend toggle) |

**Description** (refinement cycle 1 Reviewer BLOCKER-1 + cycle 2 convergent BLOCKER absorptions):

**As-of-date toggle (pure UX)**:
- New `<input type="date">` above the form; default `today`
- When `asOfDate !== today` → form fields read-only (disabled inputs + disabled submit) + banner "Showing today's profile — historical view available in a future release."
- NO `?asOf=` query param on GET; NO new HTTP call; backend GET unchanged

**PUT-body shape sync** (mandatory — backend DTO change requires frontend wire-shape update):
- `EmployeeProfileUpdateRequest` interface in `useEmployeeProfile.ts:37-41` adds required `effectiveFrom: string` (ISO `YYYY-MM-DD`)
- `formToUpdateRequest()` in `EmployeeProfileEditor.tsx:61-70` injects `effectiveFrom: new Date().toISOString().slice(0,10)` (today) on every save
- vitest verifies wire shape carries `effectiveFrom`

**Validation Criteria**:
- [ ] As-of-date Date input present above form
- [ ] Form read-only + banner shown when `asOfDate !== today`
- [ ] `EmployeeProfileUpdateRequest` interface has `effectiveFrom: string`
- [ ] `formToUpdateRequest()` injects today's date as ISO string
- [ ] vitest: existing tests still pass + new test asserts `effectiveFrom` in PUT body
- [ ] `npm run build` clean

---

### Phase 3 — D-Tests

#### TASK-3312 — Docker-gated D-test suite (~14 tests)

| Field | Value |
|-------|-------|
| **ID** | TASK-3312 |
| **Status** | pending |
| **Agent** | Test & QA Agent |
| **Components** | `tests/StatsTid.Tests.Regression/EmployeeProfile/*.cs` (new file(s)), `tests/StatsTid.Tests.Regression/Payroll/EmployeeProfileMarqueeTests.cs` (new), test fixtures as needed |
| **Dependencies** | All Phase 2 tasks (TASK-3305..3311) |
| **KB Refs** | ADR-023 D8 (marquee D-test load-bearing), S29 TASK-2909 precedent (marquee shape), S31 TASK-3110 precedent (admin-CRUD HTTP D-tests) |

**Description**: All `[Trait("Category","Docker")]` per repo convention. Test enumeration:

**Marquee (2 variants — refinement cycle 1 Reviewer BLOCKER-2 absorption)**:
1. `ReplayAsync_StableUnderEmployeeProfileMutation_WeeklyNormHours_ResultByteIdentical`
2. `ReplayAsync_StableUnderEmployeeProfileMutation_PartTimeFraction_ResultByteIdentical`

**Marquee assertion target** (Step 0b Reviewer/Codex W absorption — assertion shape pinned): both variants assert byte-identical equality of `JsonSerializer.Serialize(segmentRuleResults, jsonOpts)` between baseline (last-month PCS calc) and replay-after-mutation (admin updates `weekly_norm_hours` 37 → 32 today; replay of last month's calc still uses 37 via dated `employee_profiles` row). `segmentRuleResults` is `IReadOnlyList<RuleResult>` from `PeriodCalculationService.EvaluateSegmentAsync` — the rule-engine output BEFORE wage-type mapping (the dated profile fields feed `NormCheckRule.cs:174` `profile.WeeklyNormHours * profile.PartTimeFraction` inside rule evaluation; the byte-stability proof must therefore assert rule-engine output, not the post-mapping export shape that S29 marquee asserted on). Distinct from S29 TASK-2909 precedent which asserted export-line byte-stability via `MapSegmentToExportLinesAsync` (different consumption-site phase).

**Position (non-marquee, separate semantic)**:
3. `PCS_PositionResolution_PrefersDatedOverCallerSuppliedWhenBothPresent` (with header comment noting NON-marquee + caller-fallback semantic)

**SupersedeAndCreate 3-case routing**:
4. `SupersedeAndCreate_CaseA_NoLiveRow_Inserts`
5. `SupersedeAndCreate_CaseB_SameDayEdit_UpdatesInPlace_BumpsVersion`
6. `SupersedeAndCreate_CaseC_CrossDayEdit_ClosesPredecessorInsertsSuccessor`

**SoftDelete**:
7. `SoftDelete_PredecessorVersionUnchanged_AuditRowHasVersionBeforeEqualsVersionAfter`
8. `SoftDelete_StaleIfMatchAfterSoftDelete_Returns404NotConflict412` (locks divergence)
9. `SoftDelete_EmitsEmployeeProfileSoftDeletedOnUser_OutboxStreamReadsBack`

**PUT cross-day + validator**:
10. `PUT_CrossDayEdit_EmitsEmployeeProfileSuperseded_NotUpdated`
11. `PUT_BackdatedEffectiveFrom_Returns422`
12. `PUT_FutureDatedEffectiveFrom_Returns422`

**DELETE endpoint If-Match**:
13. `DELETE_StaleIfMatch_Returns412`
14. `DELETE_MissingIfMatch_Returns428`

**AdminEndpoints UserAgreementCodeChanged**:
15. `AdminPutUserChangesAgreementCode_EmitsUserAgreementCodeChangedAndUserUpdated_BothInSameTx`
16. `AdminPutUserOmitsAgreementCode_DoesNotEmitUserAgreementCodeChanged`

**Consumption fail-modes**:
17. `Compliance_SoftDeletedProfile_Returns500FromComplianceEndpoint`
18. `Balance_SoftDeletedProfile_FallsThroughAgreementConfigChain_Returns200WithDefaultNorm`

**Audit-action enum**:
19. `SoftDelete_AuditAction_IsDELETED_NotSoftDeleted` (locks the schema-CHECK-constraint coherence)

**Validation Criteria**:
- [ ] All ~19 tests defined (count drift acceptable up or down by ~3 based on consolidation)
- [ ] Marquee variants both PASS — byte-identical `CalculationResult` under mid-period supersession
- [ ] All tests have `[Trait("Category","Docker")]`
- [ ] `dotnet test --filter "Category=Docker"` all green

---

### Phase 4 — Sprint Close

#### TASK-3313 — Sprint close (validation + INDEX + ROADMAP + QUALITY + KB-INDEX + MEMORY)

| Field | Value |
|-------|-------|
| **ID** | TASK-3313 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/sprints/SPRINT-33.md` (close sections), `docs/sprints/INDEX.md` (final row), `ROADMAP.md`, `docs/QUALITY.md`, `~/.claude/projects/C--StatsTid/memory/MEMORY.md` |
| **Dependencies** | TASK-3312 (D-tests pass) + Step 7a clean |
| **KB Refs** | — |

**Description**: Run `sprint-test-validation` skill for verified test delta. Close SPRINT-33.md sections. Update INDEX.md final row. ROADMAP updates:
- Phase 4d-3 Part 2 IMPLEMENTATION marked COMPLETE
- **Phase 4e `agreement_code` determinism gap upgraded from "candidate" to LAUNCH-BLOCKING** per ADR-023 D2 (refinement cycle 1 Reviewer W5 absorption — explicit ROADMAP edit)

QUALITY.md re-grade: Rule Engine + Payroll Integration domains (D1 cutover affects both).

MEMORY.md S33 entry per memory-management discipline.

**Validation Criteria**:
- [ ] SPRINT-33.md fully closed
- [ ] INDEX.md final row reflects test totals + Orchestrator approval
- [ ] ROADMAP Phase 4d-3 Part 2 = COMPLETE
- [ ] ROADMAP Phase 4e `agreement_code` row says LAUNCH-BLOCKING (not "candidate")
- [ ] QUALITY.md re-grade applied
- [ ] MEMORY.md S33 line added
- [ ] Sprint-close commit lands on master

---

## Architectural Constraints Verified

- [ ] **P1 — Architectural integrity**: ADR-016 D5b stays at 5 patterns; ADR-020 D2 + ADR-019 D8 inherited verbatim; ADR-023 D1-D8 implemented faithfully
- [ ] **P2 — Rule engine determinism**: PCS replays byte-identical on 2 dated fields under mid-period supersession (marquee proof on `weekly_norm_hours` + `part_time_fraction`); `Position` byte-stability covered by separate non-marquee D-test due to caller-fallback semantic; fail-closed on resolver-null in production path
- [ ] **P3 — Event sourcing**: `UserAgreementCodeChanged` registered + emitted; `EmployeeProfileSuperseded` + `EmployeeProfileSoftDeleted` (S31 registered) now actually emitted; atomic outbox per ADR-018 D3 preserved on all new emissions
- [ ] **P4 — Version correctness**: SoftDelete predecessor version unchanged + audit `version_before = version_after`; ADR-019 admin-strict If-Match on new DELETE; cycle-3 same-day-only-edit validator on PUT
- [ ] **P6 — Payroll integration**: PCS cutover preserves OkVersion server-resolution overlay + Position caller-fallback; marquee verifies byte-identical replay
- [ ] **P7 — Security**: new DELETE endpoint HROrAbove + OrgScopeValidator; AdminEndpoints PUT new emission inside existing atomic tx; cross-org leak prevention preserved

## Legal & Payroll Verification (TASK-3313)

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | No rule logic changes in S33; PCS cutover is data-source change |
| Wage type mappings produce correct SLS codes | N/A | No mapping changes |
| Overtime/supplement calculations are deterministic | pending | Marquee verifies replay-stability on profile-driven inputs |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule changes |
| Retroactive recalculation produces stable results | pending | Marquee is precisely the retroactive-replay-stability proof |

## Step 7a External Review (Plan)

Sprint-start commit: `<TASK-3300 commit SHA>` (to be filled at sprint close).

Codex review command: `codex review "..."` (prompt-alone form, auto-targets uncommitted diff per WORKFLOW.md). Cycle-cap = 2.

Internal Reviewer Agent: dispatched in parallel with Codex Step 7a.

## Refinement Trail

`.claude/refinements/REFINEMENT-s33-phase-4d3-part2-impl.md` — READY after 3-cycle dual-lens review:
- Cycle 1: 3 BLOCKERs absorbed (effective_from contract pinned; GET endpoint unchanged; marquee variants reduced) + 6 mechanical WARNINGs
- Cycle 2: 2 BLOCKERs absorbed (frontend PUT-body sync; DELETE audit-action enum) + 2 mechanical WARNINGs
- Cycle 3: BOTH lenses clean; cycle-cap-respected verification-only pass
