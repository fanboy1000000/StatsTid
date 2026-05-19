# PLAN — Sprint 35: S34 Hardening Sweep + AC Family Compensation Seed Bug Fix

| Field | Value |
|-------|-------|
| **Sprint** | 35 |
| **Phase** | 4e (general hardening — second targeted close + pre-launch bug catch) |
| **Sprint type** | Implementation (against ROADMAP rule correction policy committed 2026-05-18) |
| **Base commit** | `328a027` (S34 close, 2026-05-17) |
| **Refinement** | `.claude/refinements/REFINEMENT-s35-s34-hardening.md` (READY post-cycle-2 dual-lens absorption — 4 BLOCKERs + 1 WARNING + 1 NOTE absorbed across 2 cycles) |
| **Sprint open date** | 2026-05-18 |
| **Task count** | 11 (TASK-3500..3510) |

## Sprint Goal

Close 6 deferred items from S34 (admin surface ETag, Case A 23505, Overtime D-test, outer-users-UPDATE stale-snapshot, concurrent admin PUT D-test, ADR-016 D5b reframe) PLUS fix the AC family compensation seed bug discovered during S35 cycle-1 absorption (classified as **bug-with-no-past-impact** under ROADMAP rule correction policy committed 2026-05-18 — pre-launch posture, no past periods exist, no retroactive recompute needed).

**S34-deferred closure status**:
- Items #1 (admin ETag), #2 (Case A 23505 → 409), #3 (Overtime D-test discriminator), #4 (outer-users-UPDATE stale-snapshot), #5 (concurrent admin PUT D-test) → **CLOSED** in S35
- Item #6 (ADR-016 D5b reframe) → **DROPPED** per Reviewer cycle-1 BLOCKER (semantic-shift-not-aggregation; D5b's current 5-pattern enumeration is correct; reframe introduced new errors)
- 5/6 CLOSED + 1/6 DROPPED with documented rationale + 1 net-new pre-launch bug-with-no-past-impact correction (AC family DefaultCompensationModel)

**Strategic context**: S35 operates under ROADMAP "Deployment Model" section + Phase 4e bullets committed 2026-05-18 — single logical deployment (150 institutions), glocal rule encoding (global interpretation, local-only on rule-delegated parameters), supersession-by-default + bug-correction-when-classified rule correction policy with NO per-institution opt-in/out. AC seed correction is the first concrete application of the bug-correction-when-classified policy under pre-launch posture (free correction window).

**Out-of-scope for S35** (deferred to S36+ multi-sprint program): full source register + role-within-agreement modeling + ADR-024 + ADR-025 + ADR-027 (post-launch).

## Phase Decomposition

Follows S34 sprint shape (1+5+3+1+1). NO worktrees — Phase 2 cutovers are file-disjoint except TASK-3506 which bundles AdminEndpoints {GET, PUT, POST} in single file (intentional per S34 TASK-3407 PUT+POST precedent, extended to GET).

| Phase | Tasks | Dispatch model |
|-------|-------|---------------|
| 0 | TASK-3500 | Orchestrator-direct (this file + SPRINT-35.md + INDEX.md provisional + commit) |
| 1 | TASK-3501..3505 | **Sequential** — schema (3501) → repository Case A 23505 catch (3502) → AC family seed bug fix (3503) → danish-agreements.md Compensation Model section (3504) → UserRepository new methods (3505) |
| 2 | TASK-3506..3508 | **Parallel non-worktree** — 3 dispatch slots: TASK-3506 single-agent on AdminEndpoints.cs (GET+PUT+POST bundle); TASK-3507 frontend; TASK-3508 Overtime D-test rewrite |
| 3 | TASK-3509 | Sequential — D-test suite (~8 tests) |
| 4 | TASK-3510 | Orchestrator-direct (sprint close + INDEX + ROADMAP RESOLVED status + QUALITY re-grade + MEMORY entry + S34 INDEX miscount correction) |

## Step 0a — Entropy Scan Findings

Run 2026-05-18 at sprint open:

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-018 + ADR-019 + ADR-020 (versioning patterns) + ADR-017 (local profile) + ADR-013 (no-cascade) + ROADMAP Deployment Model section + Phase 4e bullets all resolve cleanly post-S34 |
| Pattern compliance | CLEAN | S35 is established admin-strict If-Match pattern application (4th surface after S25 agreement_configs / position_override_configs / wage_type_mappings; users is the last unprotected admin write); AC seed correction is first concrete application of ROADMAP rule correction policy |
| Orphan detection | DEBT (carry-forward from S34) | 80+ stale locked agent worktrees under `.claude/worktrees/`; S35 uses non-worktree dispatch so non-blocking. Operational housekeeping deferred to Phase 4e backlog. |
| Documentation drift | DRIFT-IDENTIFIED | SPRINT-34.md INDEX miscount: claimed 209 Docker-gated passing; true count was 208 (Overtime D-test was failing at S34 close). TASK-3510 close corrects INDEX + SPRINT-34.md. `danish-agreements.md` missing Compensation Model section (added by TASK-3504). |
| Quality grade review | SCHEDULED | Re-grade at TASK-3510 close. Backend API **A- → A** candidate (last unprotected admin write closed); Infrastructure stays A; Rule Engine stays A++ (unchanged); Security **B → B+** candidate (audit-trail audit-cliff under concurrent admin closed + concurrent-PUT D-test landed); Domain Correctness new category — partial credit for AC seed fix; full grading deferred to S41 |
| Refinement disposition | READY | 2-cycle Step 4 dual-lens absorbed: cycle 1 = 4 BLOCKERs + 5 WARNINGs + 4 NOTEs (all absorbed); cycle 2 = 2 BLOCKERs + 1 WARNING + 1 NOTE (all absorbed mechanically, no architectural forks); cycle-cap respected (2/2 per lens) |

## Step 0b — Plan Review Trigger

**MANDATORY** per trigger criteria — sprint touches:

- **P1** (Architectural integrity) — new `users.version` column + new `users_audit` table; new admin-strict If-Match contract on users PUT extends ADR-019 D2 pattern to last unprotected admin write
- **P3** (Event sourcing / auditability) — new `users_audit` table with version-transition columns per ADR-019 D8; admin PUT audit trail under concurrent admin now correctness-guaranteed via FOR-UPDATE + If-Match
- **P7** (Security / access control) — admin-strict If-Match on `/api/admin/users` PUT (was missing); concurrent PUT race correctness; cross-org binding preserved
- **NEW: ROADMAP rule correction policy first application** — AC family seed bug correction is the first concrete classification + execution under the policy committed 2026-05-18

Dispatch dual-lens (Codex external + Reviewer Agent internal) on this PLAN file before Phase 1 dispatches. Cycle-cap = 2 per lens.

---

## Task Log

### Phase 0 — Sprint Open

#### TASK-3500 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-3500 |
| **Status** | in-progress |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s35.md` (this file), `docs/sprints/SPRINT-35.md`, `docs/sprints/INDEX.md`, `.claude/refinements/REFINEMENT-s35-s34-hardening.md` (sync-debt housekeeping per Step 0b cycle-3 Reviewer advisory — 3 vestigial cycle-1 references at L56/L163/L214 that say "3 call sites" or "AuthEndpoints JWT mint per S34 TASK-3408") |
| **Dependencies** | none |
| **KB Refs** | ROADMAP Deployment Model + Phase 4e bullets (committed 2026-05-18) |

**Validation Criteria**:
- [ ] PLAN-s35.md filed with full task log + Step 0a + Step 0b sections
- [ ] SPRINT-35.md provisional entry created
- [ ] INDEX.md gains S35 row (status: in-progress; dates: 2026-05-18 → ?; tests: pending)
- [ ] REFINEMENT-s35-s34-hardening.md cycle-1 vestigial framing reconciled (3 sites: L56, L163, L214 updated to "2 endpoint sites + 1 seeder" matching PLAN-s35.md L143-144 + L637 absorption — Step 0b cycle-3 Reviewer advisory housekeeping)
- [ ] Sprint-open commit lands atop `328a027` with message "S35 TASK-3500: sprint open — S34 hardening sweep + AC family compensation seed bug fix"

---

### Phase 1 — Sequential Foundation (5 tasks)

#### TASK-3501 — Schema migration `s35-d1-users-version` + `users_audit` table

| Field | Value |
|-------|-------|
| **ID** | TASK-3501 |
| **Status** | pending |
| **Agent** | **Data Model (extended into Infrastructure schema, cross-domain authorized)** — schema lives in `docker/postgres/init.sql`; greenfield-baked into base CREATE TABLE block per S25/S29/S30/S31/S34 precedent |
| **Components** | `docker/postgres/init.sql` (ALTER users + new CREATE TABLE block + ALTER ledger entry `s35-d1-users-version-and-audit`); `tests/StatsTid.Tests.Regression/TestFixtures/DockerHarness.cs` test schema includes both |
| **Dependencies** | TASK-3500 |
| **KB Refs** | ADR-018 D7 (row-version + If-Match), ADR-019 D8 (audit version-transition), ADR-019 D2 (admin-strict If-Match) |

**Schema contract** (mirrors `employee_profile_audit` shape post-S31):

```sql
ALTER TABLE users ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

CREATE TABLE IF NOT EXISTS users_audit (
    audit_id          BIGSERIAL    PRIMARY KEY,
    user_id           TEXT         NOT NULL,
    action            TEXT         NOT NULL CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED')),
    previous_data     JSONB        NULL,
    new_data          JSONB        NULL,
    version_before    BIGINT       NULL,
    version_after     BIGINT       NULL,
    actor_id          TEXT         NOT NULL,
    actor_role        TEXT         NOT NULL,
    audit_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_users_audit_user_id ON users_audit(user_id);
CREATE INDEX IF NOT EXISTS idx_users_audit_at ON users_audit(audit_at);
```

**Note on SUPERSEDED in CHECK enum** (cycle-2 Reviewer NOTE absorption): users has no supersession lifecycle today (PUT updates in place; agreement-code supersession lives on separate `user_agreement_codes_audit` stream). SUPERSEDED included for forward-compat — cheap, doesn't constrain anything, matches precedent enum at `init.sql:514` (employee_profile_audit).

**Note on column-name precedent** (Step 0b Reviewer WARNING 2 absorption): `audit_at TIMESTAMPTZ` column name follows the **S34 era convention** matching `user_agreement_codes_audit.audit_at` (init.sql:584) — the closer architectural sibling for this task (same user_id natural key, same S34 cohort). The earlier `employee_profile_audit` precedent uses `timestamp` (init.sql:521) — that's the S31 era. PLAN-s35.md prior framing cited employee_profile_audit shape but used the newer column name; precedent cite swapped to `user_agreement_codes_audit` for internal consistency.

**Validation Criteria**:
- [ ] `users.version BIGINT NOT NULL DEFAULT 1` column added (idempotent ALTER)
- [ ] `users_audit` table created with shape above (idempotent CREATE)
- [ ] Both indexes created (idempotent)
- [ ] CHECK constraint on action enum includes all 4 values
- [ ] ALTER ledger entry `s35-d1-users-version-and-audit` registered
- [ ] Test harness `DockerHarness.cs` schema includes both
- [ ] `dotnet build` clean; Docker harness starts cleanly with new schema

---

#### TASK-3502 — `UserAgreementCodeRepository.SupersedeAndCreateAsync` Case A 23505 catch + 409 mapping

| Field | Value |
|-------|-------|
| **ID** | TASK-3502 |
| **Status** | pending |
| **Agent** | **Infrastructure** (cross-domain authorized into Backend.Api `AdminEndpoints` endpoint mapping + UserAgreementCodeBackfillSeeder) |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/UserAgreementCodeRepository.cs` (new try/catch on Case A InsertLiveRowAsync); `src/SharedKernel/StatsTid.SharedKernel/Exceptions/ConcurrentSeedConflictException.cs` (new); `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (**2 endpoint sites**: POST L430-440 + PUT L780); `src/Infrastructure/StatsTid.Infrastructure/UserAgreementCodeBackfillSeeder.cs` (silent swallow). **AuthEndpoints.cs explicitly excluded** per Step 0b cycle-1 Reviewer BLOCKER 1: AuthEndpoints only calls `userAgreementCodeRepo.GetCurrentAsync` (read at L47); never invokes `SupersedeAndCreateAsync`, so Case A 23505 cannot fire there. |
| **Dependencies** | TASK-3501 |
| **KB Refs** | ADR-020 D2 Case A routing, ADR-019 D2 (412 vs 409 distinction for optimistic concurrency vs unique-key conflict), S31 cycle-2 deferred concurrent-startup race (same class) |

**Mechanism**:
```csharp
// In UserAgreementCodeRepository.SupersedeAndCreateAsync Case A branch:
try {
    var (newAssignmentId, newVersion) = await InsertLiveRowAsync(conn, tx, req, nextVersion: 1L, ct);
    return new SaveUserAgreementCodeResult(newAssignmentId, newVersion, SaveUserAgreementCodeOutcome.Created);
} catch (PostgresException ex) when (ex.SqlState == "23505") {
    throw new ConcurrentSeedConflictException(req.UserId);
}
```

**Endpoint mapping** (2 call sites + 1 seeder — corrected per Step 0b Reviewer BLOCKER 1):
- `AdminEndpoints.cs:430-440` (POST 5-way atomic): `catch (ConcurrentSeedConflictException ex) => Results.Conflict(new { error = "...", userId = ex.UserId, hint = "retry — concurrent seed/create raced" })`
- `AdminEndpoints.cs:780` (PUT mutation branch): same mapping
- **AuthEndpoints.cs JWT mint path EXCLUDED** — verified at Step 0b that AuthEndpoints only calls `userAgreementCodeRepo.GetCurrentAsync(...)` (read, L47) per S34 TASK-3408; it never invokes `SupersedeAndCreateAsync`, so Case A 23505 cannot fire on the JWT mint path. Original PLAN cycle 1 named 3 sites; Reviewer Agent caught the phantom site via grep. Corrected to 2 sites.

**Backfill seeder** (`UserAgreementCodeBackfillSeeder.cs`): wraps INSERT in same try/catch but **swallows silently** (`catch (PostgresException ex) when (ex.SqlState == "23505") { /* idempotent skip - another instance won the race */ }`). Matches AgreementConfigSeeder + EntitlementConfigSeeder + EmployeeProfileSeeder idempotency pattern from S30/S31 deferred fixes.

**Lock-order audit** (cycle-1 Reviewer WARNING absorption): verify `UserAgreementCodeBackfillSeeder` does NOT acquire `user_agreement_codes` lock without first acquiring `users`-row lock. Seeder operates pre-login on user-list snapshot; no concurrent users-row mutation during seed. Document in commit body.

**Validation Criteria**:
- [ ] `ConcurrentSeedConflictException` new type with `UserId` field
- [ ] `UserAgreementCodeRepository.SupersedeAndCreateAsync` Case A branch catches 23505 + re-throws as `ConcurrentSeedConflictException`
- [ ] **2 endpoint sites** map to 409 Conflict with structured body (AdminEndpoints POST + PUT; AuthEndpoints excluded — verified read-only at Step 0b)
- [ ] Backfill seeder swallows 23505 silently (idempotent)
- [ ] Lock-order audit completed; finding documented in commit body
- [ ] No regression on existing OptimisticConcurrencyException → 412 path (symmetric handling)

---

#### TASK-3503 — AC family compensation seed bug fix (pre-launch bug-with-no-past-impact)

| Field | Value |
|-------|-------|
| **ID** | TASK-3503 |
| **Status** | pending |
| **Agent** | **Data Model** (read-only file-inspection across `tests/` for cross-impact audit — universally permitted; no test code changes in this task) |
| **Components** | `docker/postgres/init.sql` (6 seed row edits at L1099/1105/1135/1141/1147/1153); `src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs` (6 entries get explicit `DefaultCompensationModel = "AFSPADSERING"`); cross-impact audit READ-ONLY on 12+ `.cs` files referencing `UDBETALING`. **Test edits, if any are needed per the audit, are folded into TASK-3508 or TASK-3509 dispatch — Data Model agent does NOT write test files** (Step 0b Codex BLOCKER 2 absorption — AGENTS.md L35-36 + cross-domain authorization sub-section L44-57: explicit-not-wildcard). Validation gate at L268 (`Unit + regression suites pass post-change`) enforces this implicitly — if audit surfaces tests that need editing, orchestrator folds them into TASK-3508/3509 before declaring TASK-3503 done. |
| **Dependencies** | TASK-3501 (sequenced for clean commit history; logically independent — could parallelize but kept sequential per S34 R7 commit-before-dispatch and clean-commit-history discipline) |
| **KB Refs** | ROADMAP rule correction policy (committed 2026-05-18: supersession-by-default + bug-correction-when-classified + no per-institution opt-in/out); ADR-014 (DB-backed agreement configs); danish-agreements.md (will be updated by TASK-3504) |

**Bug classification** (per ROADMAP rule correction policy — clarified per Step 0b Codex BLOCKER 1):
- **Was-agreed?**: **NO** — the parties never agreed UDBETALING was the AC default. Per AC overenskomst (cited below), default is afspadsering; the system's encoding inverts this. The bug originated in S17 (compensation fields added; AC entries silently inherited the model default `"UDBETALING"` from `AgreementRuleConfig.cs:67` without explicit override; init.sql seed rows perpetuated).
- **Materially-wrong (past impact)?**: **NO** — pre-launch posture; no past periods exist; no payroll lines have been computed with the wrong rule.
- **Resulting action**: **bug-fix-without-recompute** (row 4 of the 4-cell classification matrix from the ROADMAP rule correction policy + supplementary refinement framework). Forward-only seed correction. Audit narrative records this as a bug correction (not a supersession), even though no `bug-correction event` infrastructure exists yet — this is documentary only because pre-launch posture means no in-band retroactive recompute is needed; ADR-027 (post-launch) will introduce the formal event type if/when a post-launch bug-with-past-impact is discovered.
- **Sources** (exact URLs per Step 0b Codex WARNING 2 cycle-2 absorption — first application of ROADMAP rule correction policy raises evidence bar):
  - AC overenskomst cirkulære (Personalestyrelsen/Medst, employer authority): `https://oes.dk/media/ik0hm2lr/043-19.pdf` — §4 "afspadsering as far as possible; payment as fallback when afspadsering infeasible"
  - Akademikerne union OK24 guidance: `https://www.akademikerne.dk/ok24-forlig-for-90-000-akademikere-i-staten/`
  - Djøf "Afspadsering og overarbejde": `https://www.djoef.dk/vilkaar/arbejdstid/overarbejde-og-afspadsering`
  - Folketinget retningslinjer for merarbejde (2019, authoritative state guidance): `https://www.ft.dk/samling/20191/aktstykke/aktstk.1/spm/1/svar/1596781/2087480.pdf`
  - DM "Særligt for offentligt ansatte": `https://dm.dk/raad-og-svar/arbejdstid/offentligt-ansatte/`
  - All web-verified 2026-05-18.
- **Classifier**: Orchestrator (Claude), confirmed by user 2026-05-18 in pre-PLAN refinement discussion. Domain-expert validation deferred to S36+ Phase B per ROADMAP commitment.

**6 init.sql edits** (UDBETALING → AFSPADSERING):
- L1099: AC OK24
- L1105: AC OK26
- L1135: AC_RESEARCH OK24
- L1141: AC_RESEARCH OK26
- L1147: AC_TEACHING OK24
- L1153: AC_TEACHING OK26

**6 CentralAgreementConfigs.cs explicit overrides** (defeat the inheritance trap from `AgreementRuleConfig.cs:67` default):
- AC OK24 (L15-35) — add `DefaultCompensationModel = "AFSPADSERING"` + `EmployeeCompensationChoice = false` (the default, made explicit for clarity)
- AC OK26 (L98-118) — same
- AC_RESEARCH OK24 (L181-203) — same
- AC_RESEARCH OK26 (L204-226) — same
- AC_TEACHING OK24 (L227-249) — same
- AC_TEACHING OK26 (L250+) — same

**Cross-impact audit table** (cycle-2 Reviewer WARNING absorption — produce in commit body):

| File | Line | Classification | Action |
|------|------|----------------|--------|
| OvertimeAtomicTests.cs | 104 | (a) fixture-only / (b) AC-pinned / (c) DDL-DEFAULT parity | per audit |
| TxContractTests.cs | 153 | (c) DDL-DEFAULT parity | verify vs init.sql:1042/1422 |
| TxContractTests.cs | 230 | (c) DDL-DEFAULT parity | verify vs init.sql:1042/1422 |
| TxContractTests.cs | 1026 | per audit | per audit |
| TxContractTests.cs | 1347 | per audit | per audit |
| AgreementConfigAtomicTests.cs | 413 | per audit | per audit |
| AgreementConfigConcurrencyTests.cs | 411 | per audit | per audit |
| AuditVersionTransitionTests.cs | 357 | per audit | per audit |
| ForcedRollbackHarness.cs | 204 | (c) DDL-DEFAULT parity | verify |
| ForcedRollbackHarness.cs | 289 | (c) DDL-DEFAULT parity | verify |
| (plus ~2-3 more surfaced by grep) | | | |

Full enumeration at TASK-3503 dispatch (not before — keeps PLAN file stable while still capturing audit shape).

**Commit body** (template):
```
S35 TASK-3503: AC family compensation seed bug fix (bug-with-no-past-impact)

Per ROADMAP rule correction policy (2026-05-18): classified as
bug-with-no-past-impact under pre-launch posture. AC family seeds
(AC + AC_RESEARCH + AC_TEACHING) corrected from UDBETALING → AFSPADSERING.

Per AC overenskomst (sources cited): default compensation is afspadsering
(time-off-in-lieu); payment as fallback only when afspadsering infeasible.
Current seed (UDBETALING as default) inverts this. Bug originated in S17
when DefaultCompensationModel field was added; AC entries in
CentralAgreementConfigs.cs inherited the model default UDBETALING without
explicit override; matching init.sql seed rows perpetuated.

Scope:
- 6 init.sql rows (AC + AC_RESEARCH + AC_TEACHING × OK24 + OK26)
- 6 CentralAgreementConfigs.cs entries (explicit override defeats inheritance trap)
- Cross-impact audit: [table inline]

Sources (web-verified 2026-05-18):
- AC overenskomst cirkulære (Personalestyrelsen/Medst): https://oes.dk/media/ik0hm2lr/043-19.pdf §4
- Akademikerne union OK24 guidance: https://www.akademikerne.dk/ok24-forlig-for-90-000-akademikere-i-staten/
- Djøf "Afspadsering og overarbejde": https://www.djoef.dk/vilkaar/arbejdstid/overarbejde-og-afspadsering
- Folketinget retningslinjer for merarbejde (2019): https://www.ft.dk/samling/20191/aktstykke/aktstk.1/spm/1/svar/1596781/2087480.pdf
- DM "Særligt for offentligt ansatte": https://dm.dk/raad-og-svar/arbejdstid/offentligt-ansatte/

Classifier: Orchestrator (Claude), confirmed by user 2026-05-18.

Pre-launch: no past periods exist; no retroactive recompute needed.
First concrete application of ROADMAP rule correction policy's
bug-correction-when-classified path.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
```

**Validation Criteria**:
- [ ] 6 init.sql rows updated (UDBETALING → AFSPADSERING)
- [ ] 6 CentralAgreementConfigs.cs entries gain explicit DefaultCompensationModel = "AFSPADSERING" + EmployeeCompensationChoice = false
- [ ] Cross-impact audit table produced in commit body
- [ ] AC-pinned tests (if any surface) → produce list ONLY; test edits folded into TASK-3508 or TASK-3509 dispatch (per Step 0b Codex cycle-1 BLOCKER 2 + Reviewer cycle-2 NOTE 2 absorption — no separate task spawned; validation gate at next line enforces)
- [ ] DDL-DEFAULT parity (TxContractTests.cs + ForcedRollbackHarness.cs) verified — column DEFAULT decision is **explicit**: keep init.sql:1042/1422 column DEFAULT as `'UDBETALING'` per Step 0b Reviewer NOTE 3 absorption. Rationale: all post-S35 agreement rows supply value explicitly; column DEFAULT is documentary-only legacy fallback that never fires; flipping it adds churn without functional change. Decision documented inline in TASK-3503 commit body.
- [ ] `dotnet build` clean
- [ ] Unit + regression suites pass post-change
- [ ] Bug classification documented in commit body per ROADMAP policy (Step 0b Codex W2 absorbed: classifier name + verification date + exact source URLs included in commit body)

---

#### TASK-3504 — `docs/references/danish-agreements.md` Compensation Model section

| Field | Value |
|-------|-------|
| **ID** | TASK-3504 |
| **Status** | pending |
| **Agent** | Orchestrator-direct (docs/ is Orchestrator-only per CLAUDE.md governance) |
| **Components** | `docs/references/danish-agreements.md` (NEW "Compensation Model" section between current "Overtime Thresholds" L53-58 and "Entitlement Quotas" L60) |
| **Dependencies** | TASK-3503 (corrected AC values must land before doc cites them as correct) |
| **KB Refs** | S17 (Overtime Governance & Compensation Model) — added fields; never back-filled doc; ROADMAP rule correction policy 2026-05-18; forward-references S36 source register + ADR-024 / S38 |

**New section content**:

```markdown
### Compensation Model (added 2026-05-18)

The `DefaultCompensationModel` + `EmployeeCompensationChoice` fields govern how overtime/merarbejde compensation is delivered per agreement. **Source of truth: the overenskomst cirkulærer cited per agreement below; backfilled into this reference doc 2026-05-18 (S17 gap closed by S35 / TASK-3504).**

| Agreement | DefaultCompensationModel | EmployeeCompensationChoice | Source citation |
|-----------|--------------------------|----------------------------|-----------------|
| AC | AFSPADSERING | false | AC overenskomst cirkulære (oes.dk 043-19) §4 — default afspadsering; payment as fallback when afspadsering infeasible; employer determines feasibility |
| HK | AFSPADSERING | true | HK Stat overenskomst — default afspadsering within 3 months; employee has right to payment if not arranged in time |
| PROSA | AFSPADSERING | true | PROSA stat overenskomst — default afspadsering or payment 1:1; employee right per agreement |
| AC_RESEARCH | AFSPADSERING | false | Inherits from AC base agreement |
| AC_TEACHING | AFSPADSERING | false | Inherits from AC base agreement |

**Forward reference (S36+ / ADR-024)**: within-OK role distinction (fuldmægtig vs specialkonsulent vs chefkonsulent) is NOT currently modeled. Specialkonsulent + chefkonsulent under AC LOSE the contractual right to compensation per the overenskomst, but the system today treats all AC employees identically. Documentation gap acknowledged; modeling gap scheduled for S36 inventory sprint + S38 ADR-024 design + S39-S41 implementation. See ROADMAP Phase 4e "S35 domain-correctness discovery (LAUNCH-BLOCKING)".

**Forward reference (S36 source register)**: each cell above will gain full citation + confidence level + decider/authority + verification date in the upcoming `docs/references/agreement-source-register.md`.
```

**Validation Criteria**:
- [ ] New "Compensation Model" section added in correct file location (between Overtime Thresholds and Entitlement Quotas)
- [ ] All 5 agreements documented with corrected values (post-TASK-3503)
- [ ] Source citations included per agreement
- [ ] Forward references to S36 / ADR-024 / source register correctly cite ROADMAP
- [ ] `danish-agreements.md` table-of-contents updated if present (current file has no TOC; skip)

---

#### TASK-3505 — `UserRepository.GetByIdWithVersionAsync` methods (in-tx FOR-UPDATE + non-tx variants)

| Field | Value |
|-------|-------|
| **ID** | TASK-3505 |
| **Status** | pending |
| **Agent** | **Infrastructure** |
| **Components** | `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs` (2 new methods) |
| **Dependencies** | TASK-3501 (needs `users.version` column) |
| **KB Refs** | S31 `EmployeeProfileRepository.GetByEmployeeIdWithVersionAsync` Step 7a fix precedent (atomic row+version read prevents GET race) |

**API contract**:

```csharp
/// <summary>
/// In-tx FOR-UPDATE single-SELECT variant — used by AdminEndpoints PUT to atomically
/// read user fields + version under row lock. Returns null if user_id not found OR is_active=false.
/// </summary>
public async Task<(User User, long Version)?> GetByIdWithVersionAsync(
    NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct);

/// <summary>
/// Non-tx variant for GET endpoint — no FOR UPDATE, no transaction. Stamps ETag from
/// atomic read of row + version (prevents the S31 GET-race class: two reads could
/// return stale fields with newer ETag, letting next If-Match overwrite the race).
/// </summary>
public async Task<(User User, long Version)?> GetByIdWithVersionAsync(
    string userId, CancellationToken ct);
```

**Validation Criteria**:
- [ ] Both methods compile + return correct shape
- [ ] In-tx variant uses `SELECT ... FOR UPDATE`
- [ ] Non-tx variant uses single SELECT (no FOR UPDATE)
- [ ] Both filter `is_active = TRUE` (matches existing GetByIdAsync semantic)
- [ ] Returns null when user_id not found
- [ ] Unit test: round-trip + version column populated

---

### Phase 2 — Parallel Cutovers (3 tasks, file-disjoint, non-worktree dispatch)

All 3 dispatch AFTER Phase 1 commit lands per S24/S26 R7 commit-before-dispatch discipline.

#### TASK-3506 — AdminEndpoints {GET, PUT, POST} `/api/admin/users` — admin-strict If-Match + version bumping + users_audit

| Field | Value |
|-------|-------|
| **ID** | TASK-3506 |
| **Status** | pending |
| **Agent** | **Backend API** (single-agent serial work on `AdminEndpoints.cs` — GET + PUT + POST all in same file per S34 TASK-3407 precedent for PUT+POST bundling, extended to GET) |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (3 endpoint changes — GET added, PUT extended, POST extended) |
| **Dependencies** | TASK-3501 (schema), TASK-3502 (Case A 23505 mapping), TASK-3505 (UserRepository.GetByIdWithVersionAsync) |
| **KB Refs** | ADR-019 D1-D8 (admin-strict If-Match contract); S25 `EmployeeProfileEndpoints` precedent (Step 7a P2 fix shape); S31 + S33 `EmployeeProfileEndpoints` PUT pattern; S34 TASK-3407 AdminEndpoints PUT FOR-UPDATE + agreement-code branch |

**GET `/api/admin/users/{userId}`** (NEW — verified absent per cycle-1 Codex NOTE):
- `HROrAbove` + `OrgScopeValidator.ValidateOrgAccessAsync` on target user's org
- Read via `UserRepository.GetByIdWithVersionAsync(userId, ct)` non-tx variant
- Stamp `ETag: "<version>"` on 200 OK response
- 404 when user not found
- Note: HROrAbove matches existing PUT/POST RBAC on same file (cycle-1 Reviewer WARNING absorption: not LocalAdminOrAbove)

**PUT `/api/admin/users/{userId}`** (extended):
1. Parse `If-Match` via `EtagHeaderHelper.TryParseIfMatch` admin-strict mode → 428 missing/malformed
2. Inside tx, replace pre-tx `userRepo.GetByIdAsync` with in-tx FOR-UPDATE re-read via `userRepo.GetByIdWithVersionAsync(conn, tx, userId, ct)`
3. Compare locked-row version against If-Match → throw `OptimisticConcurrencyException` on mismatch
4. **Explicit catch + endpoint mapping with in-tx rollback** (cycle-1 Codex W2 absorption + Step 0b Reviewer W1 absorption — cite corrected from `WageTypeMappingEndpoints.cs:497` to `EmployeeProfileEndpoints.cs:282-291` per Reviewer W1: the EP shape has FOR-UPDATE re-read + audit emission + OCE catch matching this task's needs and explicitly calls `await tx.RollbackAsync(ct)` before returning 412 JSON — vs WTM endpoints which rely on disposal-time rollback). Catch `OptimisticConcurrencyException` BEFORE the generic catch block; explicit `await tx.RollbackAsync(ct)` then `return Results.Json(new { error = "...", expectedVersion = ex.Expected, actualVersion = ex.Actual }, statusCode: 412)`.
5. Request null-fallback runs against LOCKED row's fields (closes item #4 stale-snapshot pattern — null-fallback can no longer pick up pre-tx stale data)
6. UPDATE `users SET ..., version = version + 1` stamps new version
7. INSERT `users_audit` row in same tx: `action='UPDATED', version_before=predecessor.Version, version_after=predecessor.Version+1, previous_data=<json>, new_data=<json>`
8. Existing S34 agreement_code FOR-UPDATE branch coexists (defense-in-depth; documented lock order)
9. Lock-order discipline (cycle-1 Reviewer WARNING absorption): commit body documents new lock order explicitly (users → user_agreement_codes); TASK-3502 audit already verified backfill seeder doesn't violate

**POST `/api/admin/users`** (extended):
- No If-Match (create-not-update)
- Stamp `ETag: "1"` on 201 response (S25 AgreementConfigEndpoints precedent at L78/104/143; PositionOverrideEndpoints precedent at L64)
- users INSERT writes `version = 1` (DEFAULT from TASK-3501)
- INSERT `users_audit` CREATED row in same tx: `action='CREATED', version_before=NULL, version_after=1, previous_data=NULL, new_data=<json>`

**Validation Criteria**:
- [ ] GET `/api/admin/users/{userId}` endpoint added with ETag header
- [ ] PUT enforces admin-strict If-Match (428 missing, 412 stale via explicit catch + endpoint mapping + explicit `await tx.RollbackAsync(ct)` before 412 return per EmployeeProfileEndpoints.cs:282-291 precedent)
- [ ] PUT in-tx FOR-UPDATE re-read via TASK-3505's new method
- [ ] PUT null-fallback runs against LOCKED row (verified by D-test in TASK-3509)
- [ ] PUT version bumps + users_audit row written with version-transition columns
- [ ] **PUT response stamps `ETag: "<newVersion>"` header on 200 OK + body carries `version` field matching `version_after`** (Step 0b Codex W1 absorption — was missing; ADR-019 D2 explicit ETag contract requires it)
- [ ] PUT existing S34 agreement_code branch coexists (no regression on existing tests)
- [ ] POST stamps ETag: "1" + users_audit CREATED row + 201 body carries `version: 1`
- [ ] Lock-order documented in commit body (users → user_agreement_codes)
- [ ] `dotnet build` clean; existing tests pass

---

#### TASK-3507 — Frontend admin UI migration to `apiFetchWithEtag<T>`

| Field | Value |
|-------|-------|
| **ID** | TASK-3507 |
| **Status** | pending |
| **Agent** | **UX** |
| **Components** | `frontend/src/hooks/useAdmin.ts` (verify hook count at dispatch; migrate user-management hooks); `frontend/src/pages/admin/UserManagement.tsx` (banner-with-retry on 412); 1-2 new vitest tests on the apiFetchWithEtag flow mirroring `ProfileEditor.test.tsx` |
| **Dependencies** | TASK-3506 (backend endpoint contract must be in place) |
| **KB Refs** | S25 admin-strict pattern; S22 ProfileEditor.tsx banner-with-retry precedent at L135/213-220/283-293; S34 TASK-3409 (added effectiveFrom field; this task migrates the wider hook to ETag) |

**Migration shape**:
- `useAdmin.ts` user-management hooks switch from `apiClient.*` to `apiFetchWithEtag<T>` per S25 pattern
- Capture ETag from GET response; pass as `If-Match: "<version>"` on PUT
- On 412 response: banner-with-retry shape — show banner explaining stale version + refetch + auto-refresh form + user resubmits
- 1-2 vitest tests asserting the 412 banner-with-retry path

**Validation Criteria**:
- [ ] useAdmin.ts hook count verified at dispatch (expected: 2-4 user-management hooks)
- [ ] All user-management hooks migrated to apiFetchWithEtag
- [ ] PUT body carries `If-Match: "<version>"` header captured from prior GET/POST ETag
- [ ] 412 banner-with-retry implemented per ProfileEditor precedent
- [ ] 1-2 vitest tests added asserting the 412 path
- [ ] `npm run build` 0 new errors
- [ ] Existing UserManagement.tsx flows still work (golden path test)

---

#### TASK-3508 — Overtime D-test rewrite to PUT compensation-choice (strong discriminator via 400/200)

| Field | Value |
|-------|-------|
| **ID** | TASK-3508 |
| **Status** | pending |
| **Agent** | **Test & QA** |
| **Components** | `tests/StatsTid.Tests.Regression/UserAgreementCode/AgreementCodeHttpDeterminismTests.cs` — rewrite `Overtime_PastPeriodBalance_UsesPeriodEffectiveAgreementCode_NotLive` (L255-320); add new `MintEmployeeToken(employeeId, orgId)` helper alongside existing `MintGlobalAdminToken` (L332-348); update class-level xmldoc L36-42 |
| **Dependencies** | TASK-3501 (build cleanliness for MintEmployeeToken helper) + **TASK-3506** (admin PUT contract change: admin-strict If-Match — test must send `If-Match` header on the admin PUT flip leg or it gets 428 not 200; Step 0b cycle-2 Reviewer BLOCKER 1 absorption). **TASK-3503 dependency REMOVED per Step 0b cycle-1 Codex BLOCKER 3**: `EmployeeCompensationChoice` discriminator (AC=false vs HK=true) holds **independently** of the `DefaultCompensationModel` seed value — verified at init.sql:1099 (AC `employee_compensation_choice=FALSE`) + L1111 (HK `employee_compensation_choice=TRUE`). TASK-3508 should dispatch AFTER TASK-3506 lands (sequencing within Phase 2 — see Phase Decomposition note below) or sequence into Phase 3 if Phase 2 parallel ordering doesn't gate. |
| **KB Refs** | OvertimeEndpoints.cs:528-547 (PUT contract); S34 TASK-3414 marquee D-test precedent; cycle-2 Codex+Reviewer convergent BLOCKER 2 absorption |

**Auth setup absorbed cycle 2**: PUT endpoint at `OvertimeEndpoints.cs:541-543` enforces `if (employeeId != actor.ActorId) return 403` with NO admin bypass. Existing `MintGlobalAdminToken` returns 403, defeating the discriminator. New helper needed.

**New helper**:
```csharp
private static string MintEmployeeToken(string employeeId, string orgId)
{
    // Mirror MintGlobalAdminToken (L332-348) shape; ClaimsSet:
    //   sub = employeeId
    //   role = "Employee"
    //   scope = $"org:{orgId}"
    //   exp = +1 hour
    // Sign with DevFallbackSigningKey
}
```

**Rewritten test shape** (Step 0b cycle-2 Reviewer BLOCKER 1 absorption: admin PUT leg must send `If-Match` per TASK-3506's new admin-strict contract):

```csharp
[Fact]
public async Task Overtime_PastPeriodCompensationChoice_RejectsForACWithStrongDiscriminator()
{
    var adminClient = AuthorizedAdminClient();  // existing MintGlobalAdminToken
    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    // (1) Admin GET to capture ETag (TASK-3506 added the GET endpoint with ETag header)
    var getRsp = await adminClient.GetAsync($"/api/admin/users/emp001");
    Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
    var etag = getRsp.Headers.ETag;
    Assert.NotNull(etag);

    // (2) Admin PUT flip to HK with If-Match (TASK-3506: admin-strict If-Match required;
    //     428 if missing — would break this test if not sent)
    var flipReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/emp001") {
        Content = JsonContent.Create(new {
            agreementCode = "HK",
            effectiveFrom = today.ToString("yyyy-MM-dd"),
        })
    };
    flipReq.Headers.IfMatch.Add(etag);
    var flipRsp = await adminClient.SendAsync(flipReq);
    Assert.Equal(HttpStatusCode.OK, flipRsp.StatusCode);

    // (3) Act as emp001 (Employee role + matching org) — PUT compensation-choice
    //     for a past year (when AC was effective)
    var emp001Client = AuthorizedClientFor("emp001", "org-001");  // new MintEmployeeToken
    var pastYear = today.Year - 1;
    var rsp = await emp001Client.PutAsJsonAsync(
        $"/api/overtime/emp001/compensation-choice",
        new { periodYear = pastYear, compensationModel = "UDBETALING" });

    // (4) Strong discriminator: AC has EmployeeCompensationChoice=false; endpoint rejects 400
    //     If cutover regressed to live HK (EmployeeCompensationChoice=true), would be 200 OK
    Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
    var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(
        "Employee compensation choice is not enabled for this agreement",
        body.GetProperty("error").GetString());
}
```

**xmldoc cleanup**:
- Drop misleading paragraph at L283-291 ("this assertion alone does not discriminate")
- Drop side-channel `user_agreement_codes` read block at L292-319
- Update class-level xmldoc L36-42 to reflect corrected AC values + PUT-based discriminator + dual-token-leg approach

**Validation Criteria**:
- [ ] `MintEmployeeToken(employeeId, orgId)` helper added
- [ ] Test rewritten to use PUT compensation-choice + emp001 Employee token
- [ ] Assert 400 BadRequest with correct error message
- [ ] Misleading xmldoc + side-channel blocks dropped
- [ ] Class-level xmldoc updated
- [ ] Test PASSES on this branch
- [ ] Test FAILS on cutover-regression (per Step 0b Codex BLOCKER 3 absorption — corrected: not "stash TASK-3503", which is irrelevant since EmployeeCompensationChoice discriminator holds independently of the DefaultCompensationModel seed). Verify by stashing the S34 cutover code at `OvertimeEndpoints.cs:511-513` (the `userAgreementCodeRepo.GetByUserIdAtAsync` past-period dated lookup) and reverting to live `user.AgreementCode` read; with live HK active, response = 200 OK instead of 400 → test FAILS, proving the cutover is what the test pins.

---

### Phase 3 — D-Tests (1 task)

#### TASK-3509 — Docker-gated D-test suite (~8 tests)

| Field | Value |
|-------|-------|
| **ID** | TASK-3509 |
| **Status** | pending |
| **Agent** | **Test & QA** |
| **Components** | new file `tests/StatsTid.Tests.Regression/Admin/AdminUserVersioningTests.cs` (concurrent PUT race + If-Match + stale-snapshot + POST ETag); new tests appended to existing `AgreementCodeHttpDeterminismTests.cs` for Case A 23505 (`UserAgreementCodeRepository_CaseA_Concurrent23505_MapsTo409_NotCrash`) + seeder concurrent-startup (`Seeder_ConcurrentStartup_DoesNotCrashOnDuplicateRow`); schema migration idempotency test in `tests/StatsTid.Tests.Regression/Schema/Migrations35Tests.cs` |
| **Dependencies** | TASK-3506 (admin endpoints), TASK-3502 (Case A 23505) |
| **KB Refs** | S22 `PublisherStallReadYourWriteTests` barrier-synchronized harness precedent (cycle-1 Reviewer NOTE — correct cite); S31 GET-race precedent; ADR-019 D2 (admin-strict semantics) |

**Test suite** (~8 tests):

1. **`AdminPutUser_StaleIfMatch_Returns412_AndDoesNotMutate`** — admin GET (etag=1) → admin elsewhere PUT (advances version to 2) → first admin PUT with If-Match: "1" → 412 + body unchanged. Closes item #1 contract.
2. **`AdminPutUser_MissingIfMatch_Returns428`** — admin-strict mode rejects missing header per `EtagHeaderHelper.TryParseIfMatch` admin-strict.
3. **`AdminPutUser_NullRequestField_DoesNotOverwrite_WithLockedRowSnapshot`** — Closes item #4. Concurrent PUTs P1 (sets display_name=Alice) and P2 (sets email=foo@bar). P2 reads pre-P1; P1 commits first; P2's request has display_name=null. Pre-S35: P2's null-fallback writes Alice's predecessor name, overwriting P1's commit. Post-S35: P2 holds 412 on If-Match (because P1 bumped version), banner-with-retry refetches Alice's display_name, PUT succeeds.
4. **`UserAgreementCodeRepository_CaseA_Concurrent23505_MapsTo409_NotCrash`** — Closes item #2. Two concurrent backfill/admin-POST calls for same user → loser sees 409, not 500.
5. **`Seeder_ConcurrentStartup_DoesNotCrashOnDuplicateRow`** — idempotent skip on seeder side.
6. **`AdminPutUser_TwoConcurrentAdmins_OneSucceedsOneGets412_AuditTrailCorrect`** (cycle-1 BLOCKER 2 absorption — respects If-Match) — Closes item #5. Two concurrent admin PUTs T1 + T2 both starting with If-Match: "v=N". T1 acquires FOR-UPDATE lock first, succeeds, bumps to v=N+1. T2 acquires lock, sees actual version=N+1 ≠ expected=N, throws OptimisticConcurrencyException → 412. T2 refetches, retries with If-Match: "v=N+1", succeeds. Audit trail shows 2 rows: T1's UPDATE then T2's UPDATE. Replay shows correct chronological order via timestamp.
7. **`AdminPostUser_NewUser_StampsVersionAndETag`** — POST response carries `ETag: "1"` + JSON body's `version: 1`; users_audit shows CREATED row with version_before=NULL, version_after=1.
8. **`Migration_S35_D1_UsersVersionAndAudit_Idempotent`** — re-run `s35-d1-users-version-and-audit` against existing schema with users.version already present; assert ALTER guards detect existing column, audit table CREATE is `IF NOT EXISTS`.

**Harness pattern for test 6** (barrier-synchronized 2-thread): per S22 `PublisherStallReadYourWriteTests` precedent; use `Task.Yield` + retry-loop wrapper; tolerate <5% retry rate; fail only if 3 consecutive runs flake.

**Validation Criteria**:
- [ ] 8 new Docker-gated D-tests added with `[Trait("Category","Docker")]`
- [ ] All 8 pass on this branch
- [ ] Tests 1, 3, 6 fail on pre-S35 baseline (regression detection)
- [ ] Test 6 doesn't flake under repeated runs (>=3 consecutive clean runs)
- [ ] In-flight defect budget acknowledged (1-3 defect-fix commits expected per S29 TASK-2912 / S33 TASK-3312 / S34 precedent)

---

### Phase 4 — Close (1 task)

#### TASK-3510 — Sprint close

| Field | Value |
|-------|-------|
| **ID** | TASK-3510 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `docs/sprints/SPRINT-35.md` (close entry); `docs/sprints/INDEX.md` (final entry + S34 baseline correction note); `ROADMAP.md` (Phase 4e bullets — RESOLVED status updates + AC seed correction citation); `docs/QUALITY.md` (re-grade); `MEMORY.md` user (S35 entry); `docs/sprints/SPRINT-34.md` (correction note for INDEX miscount) |
| **Dependencies** | TASK-3509 (D-tests must pass before close) |
| **KB Refs** | sprint-test-validation skill (per `docs/WORKFLOW.md` close protocol); ROADMAP rule correction policy (first application) |

**Close sequence**:

1. Run `sprint-test-validation` skill — produce delta arithmetic table:
   - Baseline (S34 close, **corrected from 858 → 857** per cycle-2 verification of Overtime D-test failure): 526 unit + 35 plain regression + **208** Docker-gated + 88 frontend = **857**
   - S35 delta: +0 unit + +0 plain regression + **+8 Docker-gated** (TASK-3509: 7 new + 1 schema idempotency) + **+1 Docker-gated** (TASK-3508 rewrite passes; was previously failing) + **+1-2 frontend** (TASK-3507 vitest)
   - Expected S35 total: **~867-868** (857 + 9 Docker + 1-2 frontend)
2. SPRINT-35.md close entry with deliverables + test counts + 5/6 deferred CLOSED + 1/6 DROPPED + 1 new bug-with-no-past-impact correction
3. INDEX.md row updated (status: complete; dates: 2026-05-18 → 2026-05-18; tests: ~867-868; orchestrator approved: yes — 2026-05-18)
4. **SPRINT-34.md baseline correction note** — add paragraph at top noting the Overtime D-test was failing at S34 close (Expected `AFSPADSERING` / Actual `UDBETALING` per cycle-2 verification 2026-05-18); true S34 baseline = 857; S35 deltas computed against corrected baseline.
5. ROADMAP Phase 4e entries:
   - Admin surface ETag propagation → **RESOLVED** with citation to S35 close commit
   - Outer-users-UPDATE stale-snapshot → **RESOLVED**
   - Concurrent-admin-PUT D-test coverage → **RESOLVED**
   - Case A 23505 deterministic-409 → **RESOLVED**
   - Overtime D-test weak discriminator → **RESOLVED**
   - ADR-016 D5b reframing → **DROPPED per Reviewer cycle-1 BLOCKER** (rationale documented + cross-reference to this PLAN + refinement file)
   - AC family compensation seed bug → **RESOLVED** (first concrete application of bug-with-no-past-impact policy)
6. QUALITY.md re-grade:
   - Backend API **A- → A** (admin-strict If-Match now covers all admin write surfaces)
   - Infrastructure stays A (new repo method + audit table follow established shape)
   - Rule Engine stays A++ (unchanged)
   - Security **B → B+** candidate (admin-PUT race + audit-trail audit-cliff under concurrent admin closed; concurrent-PUT D-test landed)
   - Domain Correctness (new category) — partial credit for AC family seed correction; full grading deferred to S41
7. MEMORY.md S35 entry — concise one-liner per project convention
8. Sprint close commit with template:
   ```
   S35 TASK-3510: sprint close — S35 complete + S34 baseline correction + AC family seed fix

   Closes 5/6 S34-deferred items + drops 1/6 (D5b reframe per Reviewer BLOCKER) +
   ships first bug-with-no-past-impact correction (AC family DefaultCompensationModel)
   under ROADMAP rule correction policy committed 2026-05-18.

   ROADMAP Phase 4e: 5 items RESOLVED, 1 DROPPED; ADR-024/025 candidates deferred to S38.

   Tests: 526 unit + 35 plain regression + ~217 Docker-gated passing + ~89-90 frontend = ~867-868 total
   (+10-11 vs S34's corrected 857 baseline; S34 INDEX miscount documented in this commit).

   S34 INDEX correction: Overtime D-test was failing at S34 close (Expected AFSPADSERING /
   Actual UDBETALING). True S34 baseline = 857, not 858. Documented in SPRINT-34.md.

   Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
   ```

**Validation Criteria**:
- [ ] sprint-test-validation skill run cleanly with delta arithmetic
- [ ] SPRINT-35.md close entry filed
- [ ] INDEX.md row updated + S34 baseline correction noted
- [ ] SPRINT-34.md baseline correction paragraph added
- [ ] ROADMAP Phase 4e entries updated (5 RESOLVED + 1 DROPPED + AC seed correction documented)
- [ ] QUALITY.md re-grade lands
- [ ] MEMORY.md S35 entry filed
- [ ] Sprint close commit lands cleanly
- [ ] No regression on existing tests
- [ ] Build clean (`dotnet build` 0/0; `npm run build` 0 new errors)

---

## Cross-Domain Authorization Labels (per AGENTS.md L44-51)

| Task | Primary Agent | Cross-Domain Scope | Authorization Basis |
|------|---------------|-------------------|---------------------|
| TASK-3501 | Data Model | Schema lives in init.sql + test DockerHarness.cs | S29/S30/S31/S33 precedent (schema-with-test-harness convention) |
| TASK-3502 | Infrastructure | Endpoint mapping at 2 sites (AdminEndpoints POST + PUT; AuthEndpoints excluded — see TASK-3502 spec) + seeder swallow | ADR-018 D5 (atomic outbox contract — repos own their write path; endpoints map exceptions) |
| TASK-3503 | Data Model | Cross-impact audit reads `tests/*` files | Read-only audit; no test code changes (only seeds + CentralAgreementConfigs) |
| TASK-3504 | Orchestrator | `docs/` is Orchestrator-only per CLAUDE.md | Orchestrator-only governance |
| TASK-3505 | Infrastructure | n/a (Infrastructure-local) | n/a |
| TASK-3506 | Backend API | n/a (Backend-local; calls Infrastructure) | n/a |
| TASK-3507 | UX | n/a (Frontend-local) | n/a |
| TASK-3508 | Test & QA | n/a (Test-local) | n/a |
| TASK-3509 | Test & QA | n/a (Test-local) | n/a |
| TASK-3510 | Orchestrator | All governance docs (INDEX/SPRINT/ROADMAP/QUALITY/MEMORY/SPRINT-34) | Orchestrator-only governance |

## Refinement Trail

`.claude/refinements/REFINEMENT-s35-s34-hardening.md` (2 cycles + 0 cycle-cap waivers):

- **Refinement Step 4 cycle 1**: 4 BLOCKERs (2 Codex + 2 Reviewer, with BLOCKER 1 convergent on TASK-3503 cycle-1-misframed AC discriminator + BLOCKER 2 convergent on users_audit table existence). 5 WARNINGs + 4 NOTEs. All absorbed via mechanical scope adjustment + user-strategic-decision (collapsed TASK-3503; DROPPED original TASK-3504 D5b reframe; CREATE users_audit per user BLOCKER decision; surfaced AC seed bug via web research).
- **Refinement Step 4 cycle 2**: 4 BLOCKERs (2 each lens, all convergent on TASK-3503 scope omission for AC_RESEARCH/AC_TEACHING + TASK-3508 PUT-endpoint auth mismatch). 1 WARNING + 1 NOTE. All absorbed mechanically: TASK-3503 extended to 6-line scope + cross-impact audit table; TASK-3508 extended with new MintEmployeeToken helper; TASK-3501 SUPERSEDED added to CHECK enum.

Plan Review Step 0b cycle 1 — **2026-05-18**:

- **External (Codex gpt-5.5)**: 3 BLOCKERs + 2 WARNINGs + 2 NOTEs.
  - B1: TASK-3503 bug classification internally inconsistent ("NO × NO" framing collides with "inverts the rule" wording). **→ Absorbed**: clarified to "was-agreed: NO + materially-wrong-past-impact: NO" with explicit binary framework reference per ROADMAP rule correction policy.
  - B2: TASK-3503 cross-domain authorization conflicts with validation criteria (read-only audit but "AC-pinned tests updated"). **→ Absorbed**: TASK-3503 scope tightened to read-only inspection + audit list production; test edits (if any surface) handled by follow-up TASK-3503b OR folded into TASK-3508/3509; AGENTS.md L35-36 + L44-57 governance respected explicitly.
  - B3: TASK-3508 dependency on TASK-3503 is false (`EmployeeCompensationChoice` discriminator holds independently of `DefaultCompensationModel` seed). **→ Absorbed**: TASK-3508 dependency on TASK-3503 REMOVED; baseline-failure criterion rewritten to "stash the S34 cutover code, not the seed fix"; verified via init.sql:1099 (AC=FALSE) + L1111 (HK=TRUE).
  - W1: TASK-3506 missing PUT response ETag validation. **→ Absorbed**: added validation criterion for PUT 200 response carrying `ETag: "<newVersion>"` header + body version matching version_after.
  - W2: TASK-3503 source proof too weak for first ROADMAP correction-policy application. **→ Absorbed**: commit body template requires exact URLs/titles/sections + classifier name + verification date; updated TASK-3503 bug classification block with full citations.
  - N1+N2: Confirmatory — no action needed.

- **Internal (Reviewer Agent)**: 1 BLOCKER + 2 WARNINGs + 4 NOTEs. **Divergent from Codex BLOCKERs — healthy dual-lens behavior, complementary not redundant.**
  - B1: TASK-3502 cites `AuthEndpoints.cs` as a 3rd `SupersedeAndCreateAsync` call site but AuthEndpoints only calls `GetCurrentAsync` (verified via grep at L20+L47 — Step 0b orchestrator verified). **→ Absorbed**: dropped AuthEndpoints from TASK-3502 scope; "3 endpoint sites" → "2 endpoint sites + 1 seeder"; same correction needed in REFINEMENT (TODO at next refinement-sync touch).
  - W1: TASK-3506 precedent cite improvement — should cite `EmployeeProfileEndpoints.cs:282-291` (FOR-UPDATE re-read + audit + OCE catch with explicit `await tx.RollbackAsync(ct)`) instead of `WageTypeMappingEndpoints.cs:497` (no explicit rollback; relies on disposal). **→ Absorbed**: cite swapped + explicit rollback discipline added to TASK-3506 step 4 spec.
  - W2: TASK-3501 column name `audit_at` vs `employee_profile_audit` precedent uses `timestamp` (S31 era vs S34 era convention). **→ Absorbed**: precedent cite swapped from `employee_profile_audit` to `user_agreement_codes_audit` (init.sql:584 — same S34 cohort, same column name `audit_at`); rationale noted inline at TASK-3501.
  - N1: S25 line numbers freshness — flagged for TASK-3506 dispatch agent prompt; non-blocking.
  - N2: Cross-domain authorization label precision (read-only audits universally permitted); refinement of TASK-3503 cross-domain row label — informational, kept as-is for documentary clarity.
  - N3: TASK-3503 column DEFAULT consciousness — **explicit decision recorded**: keep init.sql:1042/1422 column DEFAULT as `'UDBETALING'` (legacy fallback, never fires post-S35 because all rows supply value explicitly); flipping adds churn without functional change. Decision documented inline in TASK-3503 validation criteria + commit body.
  - N4: SUPERSEDED forward-compat doc — informational; cycle-2 refinement NOTE absorption confirmed correct.

**Absorption result**: 4 BLOCKERs (3 Codex + 1 Reviewer, all divergent — different defect classes; logical-consistency vs ground-truth-verification), 4 WARNINGs, 6 NOTEs. ALL absorbed mechanically. No architectural forks. No user decisions required. Cycle-cap = 2 per lens; cycle 1 absorption complete; cycle 2 verification dispatch deferred — per `feedback_dont_pause_for_reviews.md`, mechanical-only absorptions don't gate dispatch on a verification cycle unless the absorptions changed scope substantially. None did (all were specification clarifications + cite corrections + 1 phantom-site removal). User adjudication: proceed to TASK-3500 sprint open commit when ready, OR request cycle 2 verification.

## Status History

- **2026-05-18**: Sprint opened atop `328a027`. Plan filed (this file). Refinement READY post-cycle-2 absorption.
- **2026-05-18**: Step 0b cycle 1 dual-lens dispatched + absorbed. PLAN-s35.md updated with 4 BLOCKERs + 4 WARNINGs + key NOTEs absorbed.
- **2026-05-18**: Step 0b cycle 2 verification dispatched + absorbed. **2 NEW BLOCKERs surfaced** (both finite missed-facts in different defect areas from cycle 1 — not thrash signal per `feedback_thrash_defer_real_world.md`):
  - **Codex cycle-2 BLOCKER 1**: vestigial "AuthEndpoints / 3 sites" references at L143 (Agent row) + L144 (Components row) + L618 (Cross-Domain Authorization table) — cycle-1 absorption only fixed the prose at L159-162, missed the metadata rows. **→ Absorbed**: all three sites corrected to "2 endpoint sites + 1 seeder" + AuthEndpoints exclusion rationale.
  - **Reviewer cycle-2 BLOCKER 1**: TASK-3508 rewritten test omits `If-Match` on the admin PUT flip leg — after TASK-3506 makes admin PUT admin-strict, the test would get 428 not 200, breaking the discriminator chain. Cycle-1 absorption changed the admin PUT contract but didn't update the test's admin PUT call shape. **→ Absorbed**: test shape rewritten to GET-then-PUT-with-If-Match (4-step shape); TASK-3508 Dependencies row gains TASK-3506; sequencing within Phase 2 now requires TASK-3506 to land before TASK-3508 dispatches.
  - **Codex cycle-2 WARNING**: source URLs at TASK-3503 bug classification should be exact URLs not just domains. **→ Absorbed**: full URL list inlined.
  - **Reviewer cycle-2 NOTE 2**: TASK-3503b inline-spawned rescue task referenced but not enumerated in Phase Decomposition. **→ Absorbed**: dropped speculative "TASK-3503b" naming; test edits explicitly fold into TASK-3508/3509 dispatch; validation gate enforces.
  - Confirmatory: EmployeeProfileEndpoints.cs:282-291 rollback pattern verified; init.sql:584 audit_at column name verified; init.sql:1099/1111 AC=FALSE/HK=TRUE EmployeeCompensationChoice verified; init.sql:1042/1422 column DEFAULTs verified.
- **Cycle-cap status (post-cycle-2)**: 2 of 2 cycles used per lens. Cycle 2 absorption mechanical (no architectural choices).
- **2026-05-18**: Step 0b cycle 3 verification dispatched per user-granted cycle-cap waiver. **Convergence achieved**:
  - **Codex cycle 3**: 0 BLOCKERs + 2 WARNINGs (W1: `TASK-3503b` vestigial reference in cycle-1 history block — documentary, kept; W2: commit-body template at L256-258 used abbreviated URLs while live spec L194-198 had full URLs). **→ Absorbed**: commit-body template URL block expanded to full URLs + classifier line.
  - **Reviewer cycle 3**: CLEAN — "No findings for the reviewed scope." Verification matrix confirmed all 7 cycle-2 absorption items landed correctly across L143/L144/L637 (vestigial AuthEndpoints/3-sites refs); L194-198 (full URLs); L475-507 (TASK-3508 4-step shape); L448 (TASK-3508 Dependencies includes TASK-3506); L185 (no TASK-3503b reference in live spec). **Advisory NOTE**: REFINEMENT-s35-s34-hardening.md still carries cycle-1 framing at 3 sites (L56, L163, L214) — sync-debt PLAN had already self-flagged. **→ Absorbed**: TASK-3500 scope extended to reconcile the refinement sync-debt as housekeeping.
  - **Convergence trajectory**: 4 cycle-1 BLOCKERs → 2 cycle-2 BLOCKERs (finite missed-facts in different areas) → 0 cycle-3 BLOCKERs. Ideal shrinking pattern; no thrash signal per `feedback_thrash_defer_real_world.md`.
- **READY for TASK-3500 sprint-open commit dispatch**. Cycle-cap = 3 (user waiver) respected. No further review cycles required.

## Related ADRs

- **ADR-013** — No-cascade (preserved; bug-correction-when-classified is explicit-cascade per ROADMAP rule correction policy)
- **ADR-016** — Temporal Period Handling (D10 replay determinism inviolable; AC seed bug is pre-launch so no past manifests to consider)
- **ADR-017** — Local Agreement Profile (preserved; glocal rule encoding committed to ROADMAP confirms local_configurations operates on rule-delegated parameters only)
- **ADR-018** — Transactional Outbox + Row-Version Optimistic Concurrency (D3 atomic outbox + D5 (conn, tx) overloads inherited verbatim for users_audit)
- **ADR-019** — Optimistic Concurrency via Row-Version (D2/D5/D6/D8 admin-strict If-Match extended from agreement_configs / position_override_configs / wage_type_mappings to users — last unprotected admin write closes)
- **ADR-024** (pending S38) — Role-Within-Agreement Modeling + Correction Policy + Classification Governance — S35 ships against the policy without ADR-formalization until S38
- **ADR-025** (pending S38) — Multi-Tenant Operational Concerns — orthogonal to S35
- **ADR-027** (post-launch candidate) — Bug Correction Workflow + Event Schema + SLS Reconciliation — not needed pre-launch (no past periods to recompute)
