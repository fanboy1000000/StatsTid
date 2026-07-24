# Sprint 122 — The compensation-vocabulary authority gap + the S17 default-trap eradication (P6)

| Field | Value |
|-------|-------|
| **Sprint** | 122 |
| **Status** | complete |
| **Start Date** | 2026-07-24 |
| **End Date** | 2026-07-24 |
| **Orchestrator Approved** | yes (2026-07-24) |
| **Build Verified** | yes — `dotnet build` 0 errors |
| **Test Verified** | yes — 2956 green (see Test Validation Report; 44 environmental sheds isolation-cleared per FAIL-002) |

## Sprint Goal
Close the P6 compensation-vocabulary authority gap flagged at S120 (raw strings, no DB CHECK) AND eradicate the S17 inheritance-trap remnants the grounding sweep exposed — **as one cluster (owner-ruled 2026-07-24), because all of it shares the one S17 root cause and one test surface**: the `default_compensation_model` / `compensation_model` vocabularies get DB CHECKs (the authority the S120 PAT-012 refusal was waiting for) + spec-enum declarations; the `"UDBETALING"` defaults that INVERT the documented per-agreement authority (every agreement defaults AFSPADSERING — `docs/references/danish-agreements.md:71-83`) flip at every head; and the two GENUINELY-LIVE field-loss bugs the census exposed get repaired.

**THREE OWNER RULINGS (2026-07-24):**
1. **Stamp-now (OQ-1):** flip the defaults so auto-created/choice-set rows carry the corrected AFSPADSERING; the nullable/fall-through column redesign is DEFERRED until an employee-choice UI ships (the `useCompensationChoice` hook is orphaned — zero consumers).
2. **Declare the `compensationType` enum (OQ-2):** citing the VALIDATOR (`OvertimeEndpoints.cs:531-532`) as a named new **"handler-enforced" authority class** in PAT-012 — the first spec-enum whose authority is not a DB CHECK or a total projection. The `OvertimeCompensationApplied.cs:7` doc-comment (which names the WRONG vocabulary) is corrected as a prerequisite.
3. **Whole cluster in S122 (scope):** the authority gap + the default flip + the two field-loss fixes + the spec enums + the event-comment fix + the FE test fix + the runbook entry.

Refinement: `.claude/refinements/REFINEMENT-s122-compensation-vocabulary.md` — READY; Step-4 dual-lens 2 cycles (Codex 1B/3W/2N → cycle-2 verify; Reviewer 1B + WARN/NOTE, all absorbed). Both BLOCKERs resolved in scope (the field-loss fixes; the four fixture-DDL heads).

**Explicit exclusions:** NO employee-choice UI (the hook stays orphaned); NO nullable/fall-through redesign of `overtime_balances.compensation_model` (the deferred OQ-1 deep fix); NO create-DTO model field (`AgreementConfigRequest` has no compensation field — a functional gap NOTED as orthogonal; adding it is an unruled wire change); NO rule-engine/payroll/orchestrator change (the consumer sweep confirmed nothing reads either vocabulary for a decision — display/echo only); NO wire-JSON-byte change (the enum declarations are spec/generated-TS metadata per the PAT-012 precedent; the FE union narrowing is the only typed-client delta); the `source` vocabulary stays refused (no authority).

## Ground Facts (Step 0a digest — refinement-grounded, both-lens-verified)
| Check | Result | Detail |
|-------|--------|--------|
| The two vocabularies | distinct, no mixing | `compensationModel` (AFSPADSERING\|UDBETALING; the standing preference; 2 DB columns) vs `compensationType` (PAYOUT\|AFSPADSERING; per-event; never a column; drives `paid_out`/`afspadsering_used` `OvertimeEndpoints.cs:567-576`). No cross-vocab comparison in prod code (both-lens sweep) |
| The LIVE inversions | 2 field-loss sites | `PositionOverrideConfigs.ApplyOverride` (`:64-100`) drops all 4 overtime-governance fields → AC position keys resolve CLR-default UDBETALING (in-memory, via `ConfigResolutionService`); the CLONE endpoint (`AgreementConfigEndpoints.cs:200-247`) drops them → a cloned config PERSISTS UDBETALING. **Fix = ADD the missing assignments** (the CLR flip alone masks only the AFSPADSERING-base case) |
| The auto-create path | LATENT, not live | `AdjustAccumulatedAsync` (`OvertimeBalanceRepository.cs:50-68`, the `:56` omitting INSERT) has ZERO callers; `OvertimeBalanceAdjusted` never emitted; balance rows are created only via the choice-PUT `UpsertAsync` (sets the model explicitly). The `overtime_balances` DB DEFAULT is unreachable today — the flip there is defence-in-depth; its pin is REPO-LEVEL |
| The default heads | 2 DB + 4 fixture + 3 CLR | DB: `agreement_configs.default_compensation_model` (`init.sql`), `overtime_balances.compensation_model`. Fixture: `TxContractTests.cs:153`+`:239`, `ForcedRollbackHarness.cs:261`+`:346` (hand-rolled DDL, NOT sourcing init.sql — the cross-schema BLOCKER). CLR: `AgreementRuleConfig.cs:67`, `AgreementConfigEntity.cs:67`, `OvertimeBalance.cs:13` |
| The CHECK shape | guarded named ALTER | NOT inline (misses legacy/rerun DBs); the init.sql house form `ALTER TABLE … DROP CONSTRAINT IF EXISTS <n>; ADD CONSTRAINT <n> CHECK (…)` — precedent `entitlement_configs_full_day_only_types` (`init.sql:1636-1639`, captured by the generator → `db-schema.md:800`). Canonical names pinned below (the Step-0b BLOCKER resolution). The DEFAULT flips target `init.sql:1293` (agreement_configs) + `:1842` (overtime_balances) — today those DEFAULTs are `'UDBETALING'` while every seed writes `'AFSPADSERING'`, an internal mismatch the flip fixes |
| The spec-enum sites | 4 DISTINCT records | `CompensationChoiceResponse.CompensationModel` (`OvertimeBalanceResponses.cs:44`), `CompensationChoiceUpdateResponse.CompensationModel` (`:52`), `OvertimeBalanceResponse.CompensationModel` (`:34`), `BalanceSummaryOvertimeInfo.CompensationModel` (`BalanceResponses.cs:68`) — no shared record; 4 `[property: AllowedValues]`. The type enum: `OvertimeCompensateResponse.CompensationType` (`OvertimeBalanceResponses.cs:61`). S113 machinery covers all with NO new code (Reviewer-confirmed) |
| Consumer sweep | display/echo only | zero rule-engine/payroll/orchestrator readers of either vocabulary; `useCompensationChoice` orphaned (referenced only by `eslint.config.mjs:115` + its own test) |
| Blast radius | additive | zero out-of-set DB writers (CHECKs additive); the ONLY test edit from the union narrowing = the FE cross-vocab mock `employeeTypedWire.test.ts:328-347` (`'PAYOUT'` used as a MODEL value → correct to a valid model value). **Step-0b MUST also sweep tests asserting the CURRENT (buggy) clone/override compensation output** — the field-loss fix changes those outputs |
| Stale citations | 2 comments | the refused-enum comments `OvertimeBalanceResponses.cs:17` + `BalanceResponses.cs:60-62` cite `local_configurations` — the wrong table (the column is on `agreement_configs`); fix as they graduate |

## Canonical CHECK DDL (Step-0b BLOCKER resolution — the SINGLE source; TASK-12200 init.sql and TASK-12201 fixtures use these EXACT names + predicates)
The two constraints are pinned to ONE canonical name each (both Step-0b lenses: two agents pulling the name from different places would drift, and the `DROP CONSTRAINT IF EXISTS <name>` rerun-guard silently no-ops on a drifted name). Chosen the `_check` suffix to match the same-shape precedents `approval_periods_status_check` / `reporting_lines_source_check`:
- **`agreement_configs_default_compensation_model_check`** — `CHECK (default_compensation_model IN ('AFSPADSERING', 'UDBETALING'))`
- **`overtime_balances_compensation_model_check`** — `CHECK (compensation_model IN ('AFSPADSERING', 'UDBETALING'))`

**"byte-identical" is the WRONG requirement (Step-0b Reviewer):** init.sql uses the guarded `ALTER TABLE … DROP CONSTRAINT IF EXISTS <name>; ADD CONSTRAINT <name> CHECK (…)` form (legacy/rerun-safe); the four fixture heads build fresh tables via `CREATE TABLE IF NOT EXISTS`, whose natural form is an inline `CONSTRAINT <name> CHECK (…)` (or the same guarded ALTER if the fixture container is reused). Postgres normalizes the stored predicate across forms, so literal source-byte identity is neither achievable nor needed. **What must match: the constraint NAME + column + value set** — pinned above.

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — init.sql schema change (2 DB CHECKs + 2 DEFAULT flips) + P6 payroll-destined data + 2 behavior-correcting field-loss fixes + a new spec-enum authority class |
| **External Codex** | cycle 1 (2026-07-24): **1 BLOCKER / 1 WARNING / 4 NOTE** — B the constraint-name inconsistency (`_check` vs no-suffix) → the Canonical CHECK DDL block; W "5 enum members" imprecise (5 properties / 10 values) → reworded; N (confirming) blast radius clean, ApplyOverride has base, the nullable-wrapper reachability OK (`BalanceSummaryOvertimeInfo` already response-reachable), the DB DEFAULT production-dead. |
| **Internal Reviewer** | cycle 1 (2026-07-24): **1 BLOCKER / WARN+NOTE** — B CONVERGENT (same constraint-name inconsistency; recommends single-sourcing) + reword "byte-identical"→name+column+value-set + name the `ApplyFullSchemaAsync` pin harness + pin a non-compensation governance field in the field-loss regression + the orphaned-enum note + the init.sql DEFAULT/seed alignment. Checks 1/2/4/5 empirically CLEAN: zero assertions flip under the field-loss fixes (full enumeration); the nested `[AllowedValues]` reaches through the wrapper (closure computed pre-wrap + AllOf-walked; live-spec corroborated); the seeder writes AFSPADSERING explicitly (flip touches no seed). ALL absorbed. |
| **BLOCKERs resolved before Step 1** | **yes — the convergent constraint-name BLOCKER resolved by the Canonical CHECK DDL block (single source, both tasks reference it); Codex cycle-2 (2026-07-24): "Clean — resolution verified. Constraint names single-sourced with `_check` throughout, no conflicting no-suffix DDL remains." CONVERGED.** |

---

### TASK-12200 — Backend: CHECKs + default flips + field-loss fixes + spec enums + regen
| Field | Value |
|-------|-------|
| **ID** | TASK-12200 |
| **Status** | complete (2026-07-24) — build 0 err; `check_docs.py` green (db-schema captured both CHECKs + flipped DEFAULTs); convention UNCHANGED 134/3/9; openapi in sync; regen sha-idempotent ×2 (`91b32a04…`). **The openapi delta is EXACTLY 5 enum arrays (4 model `["AFSPADSERING","UDBETALING"]` + 1 type `["PAYOUT","AFSPADSERING"]`; 10 literals across 5 properties), ZERO other hunks — wire-JSON-byte-identical; api-types.ts narrowed exactly those 5 members.** Field-loss fixes grep-proven (ApplyOverride `:102-105` + clone `:246-249` each carry the 4 governance assignments). No `"UDBETALING"` DEFAULT literal remains outside in-set values/validator legal-sets. **KEY FINDING (feeds TASK-12201): `generate_db_schema.py` captures ONLY `CREATE TABLE`-inline constraints, NOT `ALTER TABLE ADD CONSTRAINT` — so the CHECKs were added in BOTH forms (inline in the CREATE body for generator capture + the guarded ALTER for legacy/rerun), exactly as the precedent `entitlement_configs_full_day_only_types` already exists in both (`init.sql:1543` inline + `:1647` ALTER). The fixture DDL heads (CREATE TABLE IF NOT EXISTS) therefore take the INLINE `CONSTRAINT <name> CHECK (…)` form.** Captured at db-schema.md `:683`/`:926` (CHECKs), `:669`/`:918` (DEFAULTs). |
| **Agent** | Backend/API + Data Model (cross-domain AUTHORIZED into the regen outputs — the standing PAT-012 pipeline pattern: openapi.json + api-types.ts; and db-schema.md via `generate_db_schema.py`) |
| **Components** | docker/postgres/init.sql (2 named ALTER CHECKs + 2 DEFAULT flips), AgreementRuleConfig.cs, AgreementConfigEntity.cs, OvertimeBalance.cs (CLR default flips), PositionOverrideConfigs.cs + AgreementConfigEndpoints.cs (the 2 field-loss assignments), OvertimeBalanceResponses.cs + BalanceResponses.cs (4 [AllowedValues] + type enum + the stale-comment fixes), OvertimeCompensationApplied.cs (comment), regen |
| **KB Refs** | PAT-012 (the enum-authority rule + the new handler-enforced class), ADR-018, the ROADMAP rule-correction policy (the S35 lineage) |

**Description**: (1) init.sql: the two guarded named CHECKs (house form) + both DEFAULTs `'UDBETALING'`→`'AFSPADSERING'`. (2) The 3 CLR defaults flip. (3) The 2 LIVE field-loss fixes — `ApplyOverride` copies all FOUR overtime-governance fields (`DefaultCompensationModel`, `EmployeeCompensationChoice`, `MaxOvertimeHoursPerPeriod`, `OvertimeRequiresPreApproval`) from the base (`PositionOverrideConfigs.cs:64` has `baseConfig` in scope); the CLONE endpoint copies the same four from the fetched `source` (`AgreementConfigEndpoints.cs:193` `GetByIdAsync` → the full entity; the repo `InsertSql` writes `default_compensation_model`, so the copied value lands). NOTE: an orphaned `enum CompensationModel` exists (`CompensationModel.cs:6`) — the columns/DTOs use raw `string`, NOT that enum; do NOT conflate it with the `[AllowedValues]` spec-enum work. (4) The 4 model `[property: AllowedValues("AFSPADSERING","UDBETALING")]` citing the new CHECK; the type `[AllowedValues("PAYOUT","AFSPADSERING")]` (OQ-2) citing the validator; the 2 stale `local_configurations` comments corrected to `agreement_configs`. (5) The `OvertimeCompensationApplied.cs:7` comment corrected to PAYOUT/AFSPADSERING. (6) Regen: `generate_db_schema.py` (db-schema.md picks up both CHECKs + the flipped DEFAULTs at `:669`/`:918`); `--openapi` ×2 sha-idempotent + `npm run gen:api` (the 5 enum-bearing members — 10 literal values across the 5 — narrow `string`→literal unions). **DECLARED CONSTRAINT: zero wire-JSON change; the spec delta is enum metadata + the FE union narrowing only.**

**Validation Criteria**:
- [ ] Build 0 err; `check_docs.py` green (db-schema.md in sync — both named CHECKs + flipped DEFAULTs captured); convention gate unchanged 134/3/9; drift + freshness green; regen sha-idempotent ×2
- [ ] The spec delta = 5 enum-BEARING PROPERTIES (4 model + 1 type; 10 literal values total across the 5) gain `enum:[…]` + the FE union narrowing; ZERO response-shape/wire-byte hunks; no other schema change
- [ ] `ApplyOverride` + clone copy ALL FOUR overtime-governance fields (grep-proven assignments present); no other handler-logic change
- [ ] A grep proves no `"UDBETALING"` DEFAULT literal remains outside in-set seed/test values and the validator legal-set literals

---

### TASK-12201 — Test & QA: the fixture-DDL alignment + CHECK/field-loss/repo pins (depends: 12200)
| Field | Value |
|-------|-------|
| **ID** | TASK-12201 |
| **Status** | complete (2026-07-24) — **scoped runs all green: primary 38/38 (TxContract 27 + AtomicTests 6 + S122 Config 4 + S122 Clone 1), S120 enum-fidelity 9/9 (stays green — all seeds in-set, the matcher now enforces the declared model+type membership), Sprint14PositionOverride 19/19; build 0 err.** The 4 fixture DDL heads flipped + INLINE canonical CHECK (the agent verified every consumer spins a FRESH container per test, so inline suffices — no guarded ALTER needed; name+column+value-set match init.sql). 5 new tests: the 2 CHECK-violations (`23514` + the named constraint, driven against an `ApplyFullSchemaAsync` real-init.sql DB), the repo-level `AdjustAccumulatedAsync` auto-create AFSPADSERING pin, and both field-loss pins (`ApplyOverride` service-level + clone HTTP-level, each also asserting `MaxOvertimeHoursPerPeriod`/`OvertimeRequiresPreApproval` survive — the whole 4-field repair). Seeds S122-prefixed, disjoint (verified vs boot + S118/S120/Atomic/Concurrency). Named tripwires byte-unchanged (git-diff-confirmed: only the 2 fixture files + 2 new S122 files). **Flagged for TASK-12203: the S120 overtime suite's header comment calls the model/type "REFUSED, no enum" — now stale post-12200; a comment-only correction.** |
| **Agent** | Test & QA |
| **Components** | tests/.../Infrastructure/TxContractTests.cs (`:153`/`:239` DDL heads), tests/.../Outbox/ForcedRollbackHarness.cs (`:261`/`:346` DDL heads), new S122 Config/Contracts assertions |
| **KB Refs** | PAT-012 (gate #1), FAIL-002, the cross-process-schema-contract lesson |

**Description**: (1) The four fixture DDL heads: flip `DEFAULT 'UDBETALING'`→`'AFSPADSERING'` AND add the CANONICAL named CHECK (the exact name + predicate from the Canonical CHECK DDL block — NOT literal byte-identity of two DDL shapes; the cross-schema contract is name+column+value-set). (2) CHECK-violation RED pins on BOTH columns, driven against an `ApplyFullSchemaAsync`-provisioned DB (`tests/…/Hosting/StatsTidWebApplicationFactory.cs:166-174` reads the real init.sql = the source of truth, immune to a fixture-name-drift regression — Step-0b Reviewer), NOT a fixture head. (3) The field-loss regression pins: a clone of a config whose model differs from the CLR default PRESERVES it (HTTP-level); an override PRESERVES the base's model (service-level). **Each also asserts at least ONE non-compensation governance field (`MaxOvertimeHoursPerPeriod` or `OvertimeRequiresPreApproval`) — the field loss is all FOUR overtime-governance fields, so pin the whole repair, not just the model (Step-0b Reviewer).** (4) The auto-create REPO-LEVEL pin: `AdjustAccumulatedAsync` called directly stamps AFSPADSERING (no endpoint drives it). (5) The S120 overtime spec-runtime suites' enum-fidelity now covers the declared model + type sets. Seeds S122-prefixed, disjoint. Named tripwire suites otherwise UNMODIFIED.

**Validation Criteria**:
- [ ] The four fixture heads flipped + carrying the canonical named CHECK (name+column+value-set, not byte-identity); CHECK-violation REDs proven both columns against an `ApplyFullSchemaAsync` DB; the field-loss preserve pins (clone + override, each also pinning a non-compensation governance field) + the repo-level auto-create pin green; new assertions green (exact counts); seed census disjoint

---

### TASK-12202 — FE: the cross-vocab test correction (depends: 12200)
| Field | Value |
|-------|-------|
| **ID** | TASK-12202 |
| **Status** | complete (2026-07-24) — tsc 0 / lint 0 / vitest **663/663** (56 files). The 4 cross-vocab literals corrected `'PAYOUT'`→`'UDBETALING'` in the `useCompensationChoice` update test (mock echo `:331`, arg `:337`, put-body assert `:342`, state assert `:346`); grep-confirmed `'PAYOUT'` occurred ONLY in that block. **Vindication of the enum work: the load-bearing tsc error was at `.toBe('PAYOUT')` — the narrowed `'AFSPADSERING'|'UDBETALING'` union caught the wrong-vocabulary value the FE had been asserting.** `'UDBETALING'` chosen so the round-trip stays a meaningful change vs the `:316` `'AFSPADSERING'` GET seed; no assertion weakened; no hook edit needed (the `updateChoice` param stays `string`). |
| **Agent** | UX/Frontend |
| **Components** | frontend/src/hooks/__tests__/employeeTypedWire.test.ts |
| **KB Refs** | PAT-012 |

**Description**: The union narrowing (`compensationModel: string`→`'AFSPADSERING'|'UDBETALING'`) from the regen lands in api-types.ts. Correct the cross-vocab mock at `:328-347` (`'PAYOUT'` used as a MODEL value → a valid model value, e.g. `'UDBETALING'`); verify tsc/lint/vitest green under the narrowed types. Small-task-sized — a single test fix + a full FE validation pass.

**Validation Criteria**:
- [ ] tsc 0 / lint 0 / vitest green; the `'PAYOUT'`-as-model mock corrected; no other FE change

---

### TASK-12203 — Orchestrator: docs + runbook + validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-12203 |
| **Status** | complete (2026-07-24) — docs done (PAT-012: the model vocabulary graduated refused→declared + the NEW handler-enforced authority class recorded; danish-agreements: the S122 completion of the S35 lineage + the DB-CHECK authority; the runbook S122 entry with the census SELECT + guarded ALTER incl. the SET DEFAULT; the stale S120 header comment corrected). **Step 7a CONVERGED both lenses: External Codex 1 BLOCKER (the legacy/rerun DEFAULT gap — init.sql's guarded path added the CHECK but not `ALTER COLUMN SET DEFAULT`, so a legacy DB rerun kept `DEFAULT 'UDBETALING'`; the inline flip only lands greenfield) → FIXED (guarded `SET DEFAULT 'AFSPADSERING'` added to both blocks; db-schema no-drift confirmed; the CHECK-violation tests exercise the updated init.sql via ApplyFullSchemaAsync) → cycle-2 "Clean — resolution verified"; Internal Reviewer 0B/0W/2N (both cosmetic doc-accuracy fixes — the runbook line-number hint + the danish-agreements historical present-tense — absorbed) with a full empirical trace confirming the field-loss fixes (all 4 fields, correct source, no crossed assignments; census re-verified complete), the exactly-5-enum/zero-wire delta, and tripwire integrity.** Validation + close pending. |
| **Agent** | Orchestrator |
| **Components** | docs/knowledge-base/patterns/PAT-012 (the model-set graduation + the new handler-enforced authority class), docs/references/danish-agreements.md (the DB-CHECK authority cross-ref + the S35 lineage note extended), docs/operations/legacy-db-upgrade-runbook.md (the census SELECT + guarded ADD CONSTRAINT entry), sprint log, INDEX, close gates |
| **KB Refs** | PAT-012, FAIL-002/003 |

**Description**: PAT-012: the model vocabulary graduates from refused to declared; record the new handler-enforced authority class (the type enum) explicitly as a precedent expansion. danish-agreements: cross-reference the new CHECK as the vocabulary authority; extend the S35 correction's lineage note (the trap's remaining heads closed at S122). The runbook entry (the census SELECT targeting admin-cloned/created rows + the guarded ADD CONSTRAINT, mirroring the init.sql house form). Validation (`sprint-test-validation`, FAIL-002 sequencing). Step 7a dual-lens. Close per the 5 gates; explicit file set.

**Validation Criteria**:
- [ ] Docs updated; delta table; Step 7a converged; close + push + CI green all 7 jobs

---

## Test Validation Report (Step 4/5, sprint-test-validation — run 2026-07-24)
| Suite | Previous (S121) | Current | Delta |
|-------|----------|---------|-------|
| Unit | 868 | 868 | 0 |
| Regression | 1359 | 1364 | +5 (the 5 new S122 tests: 2 CHECK-violation + 2 field-loss preserve + 1 repo-level auto-create) |
| Smoke | 6 | 6 | 0 (green against the freshly built composed stack) |
| DemoSeed | 55 | 55 | 0 |
| Frontend | 663 | 663 | 0 (the S122 FE change was a 4-literal cross-vocab correction in an existing test — no count change) |
| **Total** | **2951** | **2956** | **+5** |

**Run honesty:** the central chain (unit 868/868 green; regression 1h20m quiet run 1320/1364) shed 44 — ALL isolation-adjudicated environmental, none S122-caused: 42 = the ReportingLine fixed-port class (stack down per the FAIL-002 teardown rule), 2 = long-run contention flakes (`OpenApiSpecRuntimeTests.Search_TwoSectionEnvelope`, `S115EmployeeFieldSpecRuntimeTests.EmploymentStartDate` — both testcontainer, neither an S122 test), cleared **106/106** on a fresh compose Postgres. Smoke 6/6 against the healthy composed stack. FE 663/663 on the final tree (TASK-12202). The 5 new S122 tests ran green INSIDE the central run (none among the 44 sheds).

## Phase Plan
- **Phase 1:** TASK-12200 (one Backend+DataModel agent — schema + src + regen coupled).
- **Phase 2 (parallel):** TASK-12201 (test) ∥ TASK-12202 (FE) — file-disjoint; both depend on 12200 (the fixtures need the constraint name; the FE test needs the narrowed types).
- **Phase 3:** TASK-12203 (docs + validation + close).

**Atomicity pin:** ONE close commit; explicit file set; gates at close.

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance (P4) | PASSIVE — the SETS are closed system vocabularies, not agreement-defined (declaring enums does not fence agreement variation); the per-agreement DEFAULT is config data; the flip ALIGNS the code with the documented cirkulære authority (a correction, per the ROADMAP rule-correction policy / S35 lineage) |
| Wage type mapping (P6) | N/A — untouched |
| Overtime/payroll (P6) | ACTIVE-WATCH — the model is payroll-destined; the flip + field-loss fixes make the stored/resolved model MATCH the agreement authority for the first time; no payroll export path changes (nothing reads it yet) |
| Event sourcing / audit (P3) | PASSIVE — the `OvertimeCompensationApplied` payload is untouched (comment-only); no event/outbox construction change |
| Security (P7) | PASSIVE — no endpoint/policy change |
