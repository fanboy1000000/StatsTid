# PROGRAM — S36–S41 Pre-Launch Domain Correctness (Role-Within-Agreement + Multi-Tenant Operational Concerns)

| Field | Value |
|-------|-------|
| **Program** | Pre-launch domain-correctness sweep |
| **Sprint range** | S36 → S41 (7 sprints total inc. parallel Phase B workstream) |
| **Phase** | 4e (general hardening — launch-blocking program) |
| **Authored** | 2026-05-18 (during S35 refinement discussion) |
| **Trigger** | AC=UDBETALING seed bug discovered during S35 cycle-1 absorption surfaced systemic gaps |
| **Status** | IN PROGRESS — S35/S36/S37/S38/S38b COMPLETE 2026-05-21; S39+S40+S41 pending |
| **Dependencies** | S35 close (clears narrow hardening); pre-launch posture (last-free-correction window) |

---

## Cross-References (Authoritative Sources)

This document is the granular execution plan. The authoritative governance + strategic commitments live elsewhere:

- **ROADMAP.md "Deployment Model" section (L16-27)** — single logical deployment, 150 institutions, glocal rule encoding, supersession-by-default + bug-correction-when-classified rule correction policy
- **ROADMAP.md Phase 4e bullets** — S35 domain-correctness discovery (L388) + S35 SaaS multi-tenant operational concerns (L389) enumerate the gaps + name the ADR candidates
- **REFINEMENT-s35-s34-hardening.md** — the AC seed bug that triggered this program (classified as bug-with-no-past-impact pre-launch)
- **`feedback_thrash_defer_real_world.md`** — convergence trajectory discipline; cycle-cap protocol
- **`feedback_dont_pause_for_reviews.md`** — Codex/Reviewer dual-lens are the formal checkpoints; don't pause for confirmation between

---

## Program Goal

Address three systemic domain-correctness gaps discovered during S35:

1. **AC seed correction was a symptom, not the root cause.** The bug existed because (a) source-of-truth was inverted (code/DB authoritative; agreements consulted only ad-hoc), and (b) no process catches encoding drift from the cirkulærer. Without fixing the process, more AC-like bugs will surface post-launch where they're expensive to correct.
2. **Within-OK role distinction is unmodeled.** Specialkonsulent/chefkonsulent under AC lose contractual merarbejde compensation right per the overenskomst, but the system treats all AC employees identically. Production chefkonsulent users would receive contractually-wrong compensation. `PositionOverrideConfigs` schema covers only 4 quantitative fields (can't express "no entitlement"); `User.EmploymentCategory` field exists but no rule reads it (vestigial).
3. **Multi-tenant operational gaps** for 150-institution single deployment: per-tenant SLS endpoint, customer-onboarding runbook, GDPR per-tenant export/erasure, noisy-neighbor fairness, cross-tenant reporting, per-tenant feature flags, audit visibility, explicit `Institution` type vs generic top-level org.

**This program addresses all three before launch.** Pre-launch posture is load-bearing — every bug we find here ships as a free seed correction; every bug we miss becomes a post-launch supersession event with workflow overhead.

---

## Sprint-by-Sprint Plan

### S36 — Phase A Inventory Sprint 1 (DESIGN-ONLY)

**Goal**: produce comprehensive source register + agreement matrix for AC, HK, PROSA, AC_RESEARCH, AC_TEACHING. No code changes. Last-free-correction-window high-stakes.

**Deliverables** (3 new docs + 1 doc update):
- `docs/references/agreement-source-register.md` (NEW) — machine-readable traceability table
- `docs/references/role-dimension-audit.md` (NEW) — within-OK role enumeration per agreement
- `docs/references/agreement-ruleset-audit.md` (NEW) — 3-column comparison (current code | current init.sql seed | authoritative source)
- `docs/references/danish-agreements.md` (UPDATE) — cite source register row IDs in existing cells; full rewrite deferred to S41

**Source register schema** (13 columns — Codex's framing strengthened by Reviewer's confidence/decider additions):

| # | Column | Purpose |
|---|--------|---------|
| 1 | `agreement_code` | AC / HK / PROSA / AC_RESEARCH / AC_TEACHING |
| 2 | `ok_version` | OK24 / OK26 (forward-extensible to OK28) |
| 3 | `field` | The cell name in code (e.g., `DefaultCompensationModel`, `WeeklyNormHours`, `HasMerarbejde`) |
| 4 | `current_encoded_value` | What's actually in DB seed + `CentralAgreementConfigs.cs` today |
| 5 | `authoritative_source` | PDF URL + paragraph (e.g., `https://oes.dk/media/ik0hm2lr/043-19.pdf §4.2`) |
| 6 | `interpretation` | Plain-language rule statement |
| 7 | `confidence_level` | HIGH / MEDIUM / LOW (HIGH = explicit cirkulære statement; LOW = inferred or ambiguous) |
| 8 | `interpretation_authority` | Personalestyrelsen / Akademikerne / negotiated / contested |
| 9 | `last_verified_by` | Name of person who signed off |
| 10 | `decision_date` | When the cell was last verified |
| 11 | `supersession_history` | Chronological list of supersession events per cell |
| 12 | `bug_correction_history` | Chronological list of bug correction events per cell |
| 13 | `disputed?` | Boolean — does the source register record disagreement between parties? |

**Audit ordering**: AC first (highest known-incomplete; we've already started). HK + PROSA in parallel after AC. AC_RESEARCH + AC_TEACHING last (smaller employee base, less production risk).

**Tasks** (~10):
- TASK-3600 — Sprint open (PLAN-s36.md + SPRINT-36.md + INDEX.md provisional)
- TASK-3601 — Source register skeleton + first 20 cells from AC OK24 (proof-of-shape)
- TASK-3602 — Complete AC OK24 + OK26 source register entries
- TASK-3603 — Complete HK OK24 + OK26 source register entries
- TASK-3604 — Complete PROSA OK24 + OK26 source register entries
- TASK-3605 — Complete AC_RESEARCH + AC_TEACHING source register entries
- TASK-3606 — Role dimension audit doc
- TASK-3607 — Agreement ruleset audit doc (current vs source comparison)
- TASK-3608 — `danish-agreements.md` cross-reference update (cite source register row IDs)
- TASK-3609 — Phase B kickoff packaging (export source register for domain-expert review)
- TASK-3610 — Sprint close

**Expected discovery**: source register surfaces N additional bugs in HK/PROSA/AC variants beyond the AC seed bug we found in S35. All classified pre-launch as bug-with-no-past-impact; all ship as seed corrections in S37 or absorbed into S39 schema migration.

---

### Parallel: Phase B — Domain-Expert Validation

**Workstream type**: Out-of-sprint. Runs in parallel with S36 (starting the moment S36 has draft tables) through S37.

**Goal**: HR/payroll specialist sign-off per agreement; surface disputed cells; flag newly-discovered bugs.

**Process**:
1. Identify candidate expert(s) during S35 close week — options listed in earlier discussion: internal customer HR/payroll, Akademikerne / DM / HK consultant, retired-from-personalestyrelsen consultant
2. Package source register for review (export as PDF or web-viewable form)
3. Expert iterates through cells; flags disputed + corrects misinterpretations + signs off uncontested cells
4. Findings absorbed in S37 source-register finalization
5. Each cell gains `last_verified_by` name + `decision_date`

**Risk**: expert availability. Mitigation — identify candidate(s) DURING S35; engage when S36 has draft tables (week 2 of S36).

---

### S37 — Phase A Inventory Sprint 2 (DESIGN-ONLY; absorption + finalization)

**Goal**: absorb Phase B domain-expert feedback; resolve disputed cells per Personalestyrelsen-default policy; finalize source register; surface any newly-discovered bugs.

**Tasks** (~10):
- TASK-3700 — Sprint open
- TASK-3701..3705 — Absorb expert feedback per agreement (AC, HK, PROSA, AC_RESEARCH, AC_TEACHING)
- TASK-3706 — Resolve disputed cells per default-employer-side policy (ADR-024 D5 will formalize this; provisional application here)
- TASK-3707 — Document newly-discovered bugs (provisional classification per ROADMAP rule correction policy framework; ADR-024 D4 will formalize classification governance)
- TASK-3708 — Source register status → APPROVED
- TASK-3709 — Sprint close + Phase B handoff document

**Bug correction during S37**: bugs surfaced during inventory get fixed as seed edits in this sprint (still pre-launch; bug-with-no-past-impact). May result in N supplementary commits beyond the headline source-register work.

---

### S38 — Phase C Design Sprint (DESIGN-ONLY; ADR authorship)

**Goal**: produce ADR-024 + ADR-025 + ADR-013 amendment. Step 7a 2-cycle dual-lens review per Step 7a-equivalent discipline (S28/S32 design-only sprint precedent).

#### ADR-024 — Role-Within-Agreement Modeling + Correction Policy + Classification Governance

Seven decisions (D7 added 2026-05-21 per S37 TASK-3704 Bug #4 split-routing):

| D | Topic | Options to adjudicate |
|---|-------|----------------------|
| **D1** | Role dimension placement | (a) Extend `PositionOverrideConfigs` schema (lowest-disruption); (b) Activate `EmploymentCategory` as first-class rule input + introduce `RoleConfigOverride` parallel to position override (cleanest separation); (c) Promote senior roles to separate agreement codes (e.g., `AC_CHEFKONSULENT`) — matches AC_RESEARCH/AC_TEACHING precedent but conflates "agreement" with "stratum" |
| **D2** | Compensation entitlement model | Tri-state `MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE` replacing the current `DefaultCompensationModel` + `EmployeeCompensationChoice` binary which can't express chefkonsulent's "no contractual right" |
| **D3** | Correction policy formalization | Codify ROADMAP rule correction policy (supersession-by-default + bug-correction-when-classified + no per-institution opt-in/out); enumerate the binary classification framework (was-agreed × materially-wrong) |
| **D4** | Classification governance | Who classifies a discovered discrepancy? Encoding owner + product owner review BEFORE any code change; workflow for surfacing + adjudicating disputed classification |
| **D5** | Interpretation authority | Default Personalestyrelsen / Medst cirkulær (employer-side) per ROADMAP commitment; deviations documented in source register per cell |
| **D6** | Bug correction operational model | Operator-triggered (not per-institution choice); applies globally to all 150 institutions or to none; new `AgreementConfigBugCorrected` event type (distinct from existing `AgreementConfigPublished`); SLS reconciliation pattern — defer to ADR-027 post-launch |
| **D7** | **Overtime authorization model** — pre-approval + post-hoc necessity-acknowledgment (added 2026-05-21 per S37 Bug #4 split-routing decision) | The Danish state-sector "beordret merarbejde/overarbejde" concept requires employer authorization for compensable overtime — but authorization can be either (a) **prior pre-approval** via the existing `OvertimePreApproval` workflow, OR (b) **post-hoc necessity-acknowledgment** ("manager later marks entry as ordered/necessary"). The S17 OvertimeGovernanceRule only handles (a). Without (b), flipping `OvertimeRequiresPreApproval=true` for HK/PROSA (the resolution direction from S37 Bug #4) would block legitimate necessity-driven overtime that currently flows through under `false`. **Design scope**: schema (new event type or extension to `OvertimePreApprovalApproved`; new column on `overtime_pre_approvals` or new table for necessity-acknowledgments); endpoint (post-hoc acknowledgment); UI (manager flow); audit-trail discipline (post-hoc acknowledgments must be distinguishable from prior approvals — audit log records temporal direction explicitly). **Implementation lands S40 jointly with the HK/PROSA seed flip** so the flip never ships without the workflow that makes it usable. **No per-institution opt-in/out** (uniform with ADR-024 D6 operational model). |

#### ADR-025 — Multi-Tenant Operational Concerns

Eight decisions (one per concern from ROADMAP L389):

| D | Topic | Notes |
|---|-------|-------|
| **D1** | Per-tenant SLS payroll endpoint | Each institution submits to its own SLS file destination/credentials; current global SLS config doesn't scale |
| **D2** | Customer-onboarding workflow | Provisioning runbook: create top-level org + seed `local_configurations` + create first LocalAdmin + configure SLS endpoint + (if billing) subscription |
| **D3** | GDPR per-tenant export + Article 17 right-to-erasure | Non-trivial in event-sourced system; design pattern needed |
| **D4** | Noisy-neighbor / per-tenant fairness | OutboxPublisher has cross-stream parallelism but no per-tenant fairness; pre-launch acceptable; post-launch at scale becomes relevant |
| **D5** | SaaS-operator cross-tenant reporting | GlobalAdmin-only surface that deliberately breaks scope binding for usage/billing dashboards |
| **D6** | Per-tenant feature flags | E.g., institution A enables overtime governance pre-approval; institution B doesn't — where does this live? `local_configurations` extension or separate mechanism |
| **D7** | Tenant-scoped audit visibility | Verify `audit_log` queries respect scope binding for institution-internal auditors |
| **D8** | Explicit `Institution` type vs generic top-level org | Clarity for billing/contracts vs convention preservation (keep "institution = top-level org" convention) |

#### ADR-013 Amendment

Cross-reference ADR-024 D3 — bug corrections become an explicit-cascade trigger under ADR-013's no-cascade discipline (the cascade is explicit not implicit; bug correction is a new trigger source for explicit cascade).

**Tasks** (~5):
- TASK-3800 — Sprint open
- TASK-3801 — ADR-024 authorship
- TASK-3802 — ADR-025 authorship
- TASK-3803 — ADR-013 amendment
- TASK-3804 — Step 7a-equivalent dual-lens review on all three ADRs (in-sprint per S28/S32 precedent)
- TASK-3805 — Sprint close

---

### S39 — Implementation Sprint 1 (Schema + Repository + Continuous Validation)

**Goal**: schema for role-within-agreement layer; versioned-config repository following S29/S30/S31/S33 precedent; Phase E continuous-validation tests baked in from day one.

**Tasks** (~7):
- TASK-3900 — Sprint open
- TASK-3901 — Schema migration: `role_within_agreement_configs` table + `role_within_agreement_config_audit` table + indexes (per ADR-018 D14 + ADR-019 D8 versioning + audit pattern); ALTER ledger entry
- TASK-3902 — `RoleWithinAgreementConfigRepository` with `(conn, tx)` overloads + `SupersedeAndCreateAsync` 3-case routing per ADR-020 D2 (mirrors S29/S30/S31/S33/S34 versioned-config pattern)
- TASK-3903 — Seed `role_within_agreement_configs` rows from S37-finalized source register
- TASK-3904 — Admin endpoints for role-within-agreement CRUD with admin-strict If-Match per ADR-019 (mirrors S25 admin-strict pattern + S35 users surface extension)
- TASK-3905 — **Phase E continuous-validation tests** (Codex's framing):
  - **Seed-parity tests** — assert DB seed values match source register expected values; fail if drift
  - **"Unknown unknown" tests** — enumerate every active `agreement_configs` + `position_override_configs` + `role_within_agreement_configs` row; fail if any field lacks a source-register reference
  - **DRAFT-OK rule enforcement** — new OK versions require source citation before publish (test asserts no `ACTIVE` row without `authoritative_source` field populated)
- TASK-3906 — D-tests for schema + repository + admin endpoints
- TASK-3907 — Sprint close

---

### S40 — Implementation Sprint 2 (Cutover)

**Goal**: `ConfigResolutionService` resolves role layer; rule engine reads new field; payroll mapping respects entitlement model.

**Tasks** (~7):
- TASK-4000 — Sprint open
- TASK-4001 — Activate `EmploymentCategory` as first-class field per ADR-024 D1 decision (or alternative if ADR-024 chose option (a) or (c))
- TASK-4002 — `ConfigResolutionService` extended with role layer (between agreement config and local override): central agreement → role-within-agreement → position override → local override
- TASK-4003 — Rule engine cutover: `OvertimeGovernanceRule` + any other rules reading entitlement model now read tri-state `MerarbejdeCompensationRight` per ADR-024 D2
- TASK-4004 — Payroll mapping cutover: compensation entitlement model respected — `NONE` doesn't emit MERARBEJDE wage type; `DISCRETIONARY` requires explicit one-off-payment trigger (no auto-compensation); `CONTRACTUAL` follows existing AFSPADSERING/UDBETALING logic
- TASK-4005 — Frontend admin UI for role-within-agreement CRUD per S25 pattern + `apiFetchWithEtag<T>`
- TASK-4006 — Marquee D-test: chefkonsulent past-period replay determinism + correct no-entitlement behavior (per ADR-016 D10 replay determinism preserved)
- TASK-4007 — Sprint close

---

### S41 — Implementation Sprint 3 (Exhaustive D-tests + Doc rewrite + Governance bake-in)

**Goal**: agreement × role × OK-version × compensation × payroll matrix; replay determinism tests for role/config mutation; reference doc finalization; OK-version transition checklist baked into governance.

**Tasks** (~9):
- TASK-4100 — Sprint open
- TASK-4101 — D-test matrix per agreement: AC (fuldmægtig + specialkonsulent + chefkonsulent + department_head + researcher)
- TASK-4102 — D-test matrix per agreement: HK + PROSA
- TASK-4103 — D-test matrix per agreement: AC_RESEARCH + AC_TEACHING
- TASK-4104 — Cross-agreement payroll mapping D-tests (wage type matrix per agreement × role × time-type)
- TASK-4105 — Replay determinism tests for role/config mutation per ADR-024 D3 + ADR-016 D10 (chefkonsulent's no-entitlement state, role changes, supersession + bug-correction events)
- TASK-4106 — `danish-agreements.md` final rewrite as human-readable cited summary (source register = machine-readable traceability; this = human-readable narrative with citations)
- TASK-4107 — `docs/WORKFLOW.md` gains OK-version transition checklist: every new OK starts as `DRAFT` with mandatory source citation; trigger Phase A re-audit per OK transition
- TASK-4108 — `docs/QUALITY.md` re-grade: Rule Engine stays A++; new "Domain Correctness" category lands at A (or B if expert validation surfaced material disputes)
- TASK-4109 — Sprint close + program close commit

---

## Post-Launch Deferred

### ADR-027 — Bug Correction Workflow + Event Schema + SLS Reconciliation

**Status**: planned-but-not-filed.

**Trigger**: filed when the first post-launch bug-with-past-impact is discovered (per ROADMAP rule correction policy, the in-band retroactive recompute path is only needed once past periods exist that need correcting).

**Scope** (per ROADMAP L25):
- New event type `AgreementConfigBugCorrected` (distinct from existing `AgreementConfigPublished` per ROADMAP supersession-vs-bug distinction)
- Bug discovery workflow (operator-triggered classification per ADR-024 D4)
- Retroactive recompute via existing PCS replay infrastructure (new segment manifest with corrected result; original manifest preserved per ADR-016 D10)
- SLS reconciliation pattern (correction batch export when past-period exports diverge from corrected state)
- Visibility surface: "this period has been recomputed N times due to bug events; here's the history"

**Why deferred**: pre-launch posture means no past periods exist; building the in-band recompute path before it's needed is YAGNI. The S38 ADR-024 D6 decision codifies the model; ADR-027 fills in the operational detail when first needed.

---

## 7-Sprint Commitment Table

| Sprint | Type | Goal | Outputs |
|---|---|---|---|
| **S36** | Design-only | Phase A inventory pass 1 | Source register skeleton + AC/HK/PROSA/variants entries + 3 new audit docs |
| **S37** | Design-only | Phase A absorption + finalization | Source register APPROVED + Phase B feedback absorbed + bug list per agreement |
| **S38** | Design-only | Phase C ADR authorship | ADR-024 + ADR-025 + ADR-013 amendment + Step 7a-equivalent review |
| **S39** | Implementation | Schema + repository + Phase E tests | New table + repo + admin endpoints + seed-parity + unknown-unknown + DRAFT-OK tests |
| **S40** | Implementation | Cutover | ConfigResolutionService + rule engine + payroll mapping read role layer |
| **S41** | Implementation | Exhaustive D-tests + doc + governance | Full agreement×role×OK matrix + danish-agreements.md rewrite + WORKFLOW.md OK-transition checklist + QUALITY.md re-grade |
| **Parallel** | Domain-expert validation | Phase B sign-off + dispute resolution | Runs alongside S36+S37; out-of-sprint workstream |

**Phase 5 (UX polish) pushes out by 6-7 sprints** but launch-readiness becomes honest. Without this program, launching with the AC=UDBETALING bug + chefkonsulent modeling gap = production payroll incorrectness on day one.

---

## ADR Portfolio After S41

| ADR | Status post-S41 | Content |
|---|---|---|
| ADR-013 | Amended (S38) | No-cascade + bug-correction as explicit cascade trigger |
| ADR-016 | Preserved (S34 closed D10 for dated inputs) | Replay determinism inviolable; bug corrections produce new manifests, originals preserved |
| ADR-017 | Preserved | Local profile model unchanged |
| ADR-024 | ACCEPTED (S38) | Role-within-agreement modeling + correction policy + classification governance + interpretation authority (**7 decisions D1-D7**; D8 candidate from S37 merged into D3 per user adjudication; D7 added S37 TASK-3704 for overtime authorization model with post-hoc necessity-acknowledgment workflow) |
| ADR-025 | ACCEPTED-WITH-D7-DEFERRED (S38) | Multi-tenant operational concerns (7 of 8 decisions: D1-D6 + D8). D7 audit-visibility surface deferred to ADR-026 per `feedback_thrash_defer_real_world.md` halt-and-prompt at cycle 3. |
| **ADR-026** | **ACCEPTED (S38b)** — Audit Visibility Surface (path C event-projection per ADR-018 D13). Prior PROGRAM entry "REJECTED per glocal principle" superseded by S38 halt-and-prompt outcome: tenant-scoped audit visibility is launch-required, not rejected. 7 decisions D1-D7 covering new audit_projection table + per-event mappers + 3-tier visibility_scope enum + cross-tenant bypass reconciliation + new GET /api/admin/audit endpoint + admin UI + 5 Phase E tests. |
| ADR-027 | Deferred (post-launch) | Bug correction workflow + event + SLS reconciliation |

---

## Risks and Dependencies

| Risk | Mitigation |
|---|---|
| Phase B domain-expert availability | Identify candidate(s) DURING S35; engage when S36 draft tables exist (week 2 of S36) |
| S37 absorbs more bugs than expected → spillover into S38 design | Time-box S37; spillover-bugs file for S38 ADR-024 D4 classification deferral |
| ADR-024 D1 decision (role dimension placement) blocks S39+ | Cycle-cap-2 ADR review per Step 7a discipline; user adjudicates if cycles diverge |
| Schema migration in S39 conflicts with active S35 schema | S35's `users.version` migration ships clean; S39's `role_within_agreement_configs` is additive; no conflict expected |
| Phase E continuous-validation tests fail in S39 against existing seed | **Expected** — they SHOULD fail until S37 source register reconciliation completes; surface as bugs (likely will be) |
| Domain expert disagrees with Personalestyrelsen default (ADR-024 D5) | Document deviation in source register per ADR-024 D5; ship with deviation noted |
| Customer onboarding before ADR-025 ships | First customer go-live should NOT happen before S38 ADR-025 lands; defer customer-go-live commitment |
| Phase 5 (UX) pushed out 6-7 sprints | Acceptable per pre-launch posture; UX polish on stable backend > UX polish on moving target |

---

## Continuous Validation Bake-In (Phase E)

These ship in S39 TASK-3905 but are program-level commitments:

1. **OK-version transition checklist** — `docs/WORKFLOW.md` standing process item: every new OK triggers Phase A re-audit (S41 TASK-4107)
2. **Per-rule traceability** — every rule in code carries comment citing source (cirkulære PDF + section + paragraph); Reviewer Agent gets a new check item: "agreement-rule changes carry source citation?" (S41 TASK-4108)
3. **Test matrix as living spec** — D-test suite organized by (agreement × role × OK-version); test failure = code-out-of-sync-with-spec; each test names source paragraph (S41 TASK-4101-4105)
4. **Seed-parity tests** (Codex's framing) — DB seed must equal source-register expected values; fail if drift (S39 TASK-3905)
5. **"Unknown unknown" tests** (Codex's framing) — enumerate every active config row; fail if any field lacks source-register reference (S39 TASK-3905)
6. **DRAFT-until-source-cited rule** — new OK versions start as DRAFT with mandatory source-citation column populated before publish (S39 TASK-3905)

---

## When to Pick Up

After S35 close. Immediate next step: **TASK-3600 sprint-open for S36** (file PLAN-s36.md following S35 PLAN structure; provisional SPRINT-36.md; INDEX.md update).

Domain-expert candidate identification should start the week of S35 close so Phase B can engage by week 2 of S36.

The ROADMAP.md Phase 4e bullets (L388-389) carry the high-level structure. This program file captures the granular plan. Both should be consulted together.
