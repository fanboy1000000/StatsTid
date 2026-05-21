# Agreement Source Register

> **Status**: DRAFT (S36 Phase A inventory pass 1 in progress).
> **Scope**: AC / HK / PROSA / AC_RESEARCH / AC_TEACHING across OK24 + OK26.
> **Created**: 2026-05-21 (S36 TASK-3601).
> **Phase B sign-off pending**: domain-expert validation cycles per `PROGRAM-s36-s41-domain-correctness.md` L88–101; absorption in S37.

## Purpose

This register is the **machine-readable traceability table** linking every cell in the agreement / role / OK matrix to a cited paragraph in an authoritative source. It closes the systemic gap that the S35 `AC=UDBETALING` bug exposed: encoding drift from cirkulærer with no process catching it.

Three downstream uses:

1. **Phase B domain-expert review** (S37) — each cell carries a `last_verified_by` + `decision_date` column so external experts can sign off cells one-by-one.
2. **Phase E continuous-validation tests** (S39 TASK-3905) — DB seed values must match this register's expected values; the test fails on drift.
3. **Audit trail for rule corrections** — `supersession_history` + `bug_correction_history` columns record every change to a cell over time, providing the audit trail for the ROADMAP rule correction policy (supersession-by-default + bug-correction-when-classified, committed 2026-05-18).

## 13-Column Schema

Fixed by `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` L51–67.

| # | Column | Type | Purpose |
|---|--------|------|---------|
| 1 | `row_id` | `SR-{agreement}-{ok}-{NNN}` | Stable identifier — referenced from `danish-agreements.md` cross-references and from ruleset-audit doc. Never reused. |
| 2 | `agreement_code` | enum | AC / HK / PROSA / AC_RESEARCH / AC_TEACHING |
| 3 | `ok_version` | enum | OK24 / OK26 (forward-extensible to OK28) |
| 4 | `field` | string | Code-side property name on `AgreementRuleConfig` (e.g., `WeeklyNormHours`, `DefaultCompensationModel`, `HasMerarbejde`) OR the cross-table reference (e.g., `entitlement_configs.VACATION.annual_quota`) |
| 5 | `current_encoded_value` | scalar | What's actually in DB seed + `CentralAgreementConfigs.cs` today (post-S35). Format must be exact (string with quotes for enums; numeric without; boolean `true`/`false`). |
| 6 | `authoritative_source` | URL + paragraph | PDF URL + paragraph (e.g., `https://oes.dk/media/ik0hm2lr/043-19.pdf §4`). `pending` if Phase B will determine. |
| 7 | `interpretation` | plain text | One-line rule statement in plain language. Should answer "what does this cell mean?" without requiring the reader to click the source. |
| 8 | `confidence_level` | enum | HIGH / MEDIUM / LOW. HIGH = explicit cirkulær statement; MEDIUM = inferred with strong precedent or universally-accepted convention; LOW = ambiguous, contested, or inferred from secondary source only. **An `N/A-for-agreement` value MAY appear** when the field is functionally inert for this agreement (e.g., `EveningRate` for AC where supplement is disabled). |
| 9 | `interpretation_authority` | enum | Personalestyrelsen (employer-side default per ROADMAP commitment) / Akademikerne / DM / HK / PROSA / negotiated / contested / EU (for WTD-transposed cells). |
| 10 | `last_verified_by` | string | Name of person who signed off OR `pending` if Phase B has not adjudicated yet. |
| 11 | `decision_date` | YYYY-MM-DD | When the cell was last verified OR `pending`. |
| 12 | `supersession_history` | list | Chronological list of supersession events (interpretation change, encoded value updated for new OK version). Each entry `{date, from_value, to_value, source_url, commit}`. |
| 13 | `bug_correction_history` | list | Chronological list of bug correction events per cell (per ROADMAP rule correction policy). Each entry `{date, from_value, to_value, source_url, commit, classifier, was_agreed, materially_wrong}`. |
| 14 | `disputed?` | boolean | Does the source register record disagreement between parties? If `true`, `notes` SHOULD enumerate the disagreement. |
| 15 | `notes` | text | Free-form. Use for: feature-inert clarifications, dispute summaries, classifier rationale, role-specific overrides that the cell doesn't itself express (e.g., `HasMerarbejde=true` on AC but chefkonsulent loses entitlement per role distinction). |

> **Schema delta from PROGRAM L51–67**: 13 columns nominally; this file uses **15 effective columns** because (a) `row_id` is needed for cross-referencing — implicit in PROGRAM but explicit here, and (b) `notes` is needed for the "functionally inert" + role-override information that the AC proof-of-shape surfaces. **`disputed?` retained as a separate boolean** (per PROGRAM L67) rather than folded into `notes` so future bulk filters / lints can locate disputed rows mechanically. If TASK-3602 fills surface more schema gaps, an amendment lands in TASK-3601 retroactively before TASK-3602 starts.

## Audit Ordering (PROGRAM L69)

1. **AC OK24** — highest known-incomplete (we've already started; S35 found one bug here)
2. **AC OK26** — same cirkulær base, expected near-identical
3. **HK OK24 + OK26** — distinct cirkulær; potentially more bugs (HK has full supplement enablement, more cells exercised)
4. **PROSA OK24 + OK26** — distinct cirkulær (IT-faglig organisation)
5. **AC_RESEARCH OK24 + OK26** — AC-derived but annual-norm divergence
6. **AC_TEACHING OK24 + OK26** — AC-derived, reduced annual norm (1680h for research obligations)

## Source Citation Convention

- **Primary source preferred**: official cirkulær PDF URL (Personalestyrelsen / Medst / Akademikerne / HK / PROSA), with section + paragraph (e.g., `§4` or `§4.2`).
- **Secondary source acceptable for MEDIUM confidence**: Djoef / DM / union guidance pages when they cite the underlying cirkulær explicitly.
- **EU-derived cells** cite EU Working Time Directive 2003/88/EC + Danish transposition law (`Lov om arbejdstid` or equivalent).
- **Conventional / inherited values** (e.g., default supplement rates that AC doesn't actually use because the feature is disabled) get `confidence_level = N/A-for-agreement` + `notes` clarifying inertness.

## Confidence Level Definitions

| Level | Meaning |
|-------|---------|
| **HIGH** | Explicit cirkulær statement with specific paragraph citation. The cell value is unambiguous from the primary source. |
| **MEDIUM** | Inferred from the primary source via strong precedent, OR universally-accepted state-sector convention (e.g., the 37h weekly norm), OR cited only in secondary guidance with no contradicting primary source. |
| **LOW** | Ambiguous in source, contested between parties, OR inferred without firm primary-source backing. Flagged for Phase B adjudication priority. |
| **N/A-for-agreement** | Field is functionally inert in this agreement (feature flag disabled at agreement level; the value exists in seed but never reaches a rule's decision). Value still recorded for migration / cutover safety but doesn't reach production behavior. |

## Cross-References

- **ROADMAP "Deployment Model"** (L16–27) — single logical deployment, 150 institutions, glocal rule encoding (interpretation GLOBAL; only locally-delegated parameters vary per institution).
- **ROADMAP rule correction policy** (L25, committed 2026-05-18) — supersession-by-default + bug-correction-when-classified.
- **PROGRAM-s36-s41-domain-correctness.md** — granular execution plan (this file = its TASK-3601 deliverable).
- **`docs/references/danish-agreements.md`** — human-readable narrative (TASK-3608 will add row IDs as cross-references; full prose rewrite deferred to S41 TASK-4106).
- **`docs/references/role-dimension-audit.md`** (S36 TASK-3606) — within-OK role enumeration; chefkonsulent's no-merarbejde-entitlement gap.
- **`docs/references/agreement-ruleset-audit.md`** (S36 TASK-3607) — 3-column code-vs-seed-vs-source comparison.

## How to Add a New Cell

1. Assign next sequential `row_id` within the `{agreement}-{ok}` segment.
2. Populate all 15 fields. Use `pending` for `last_verified_by` + `decision_date` if Phase B has not adjudicated yet (acceptable — explicit per PLAN-s36 L168).
3. Set `confidence_level` honestly — LOW for anything inferred without firm source.
4. If introducing a new cell shape that the 15-column schema doesn't accommodate, halt + propose extension before continuing (PLAN-s36 critical-path callout 5).
5. Cross-reference the row from `agreement-ruleset-audit.md` if a code-vs-seed-vs-source mismatch surfaces.

## How to Record a Bug Correction

When the rule correction policy classifies a discrepancy as **bug** (not interpretation change):

1. Append entry to `bug_correction_history` with `{date, from_value, to_value, source_url, commit, classifier, was_agreed: NO, materially_wrong: YES/NO}`.
2. Update `current_encoded_value` to the corrected value.
3. Update `last_verified_by` + `decision_date` to reflect the correction author + date.
4. Leave `supersession_history` untouched (supersession ≠ bug correction).

## How to Record a Supersession

When the rule correction policy classifies a discrepancy as **interpretation change** (parties agreed new reading, or OK version transition introduces a new value):

1. Append entry to `supersession_history` with `{date, from_value, to_value, source_url, commit}`.
2. Update `current_encoded_value` to the new value if applicable for current effective date.
3. Leave `bug_correction_history` untouched.

---

## AC OK24 Cells (proof-of-shape — 20 cells)

The 20 cells below validate the 15-column schema works on real AC OK24 data, before per-agreement fill begins in TASK-3602.

**Cell distribution per PLAN-s36 L153–161**:
- Cells **001–004**: quantitative numeric (`WeeklyNormHours`, `MaxOvertimeHoursPerPeriod`, `MinimumRestHours`, `WeeklyMaxHoursReferencePeriod`)
- Cells **005–008**: enum / categorical (`DefaultCompensationModel`, `EmployeeCompensationChoice`, `HasMerarbejde`, `OvertimeRequiresPreApproval`)
- Cells **009–012**: rate / multiplier (overtime supplement rate 50% / 100%, flex conversion ratio, norm-deviation tolerance) — most inert for AC; the inertness IS the proof-of-shape finding
- Cells **013–016**: entitlement (vacation days quota, care days quota, senior days quota, child sick days policy)
- Cells **017–020**: compliance / governance (rest period derogation flag, daily max hours, weekly rest day requirement, norm-period model)

### SR-AC-OK24-001 — WeeklyNormHours

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-001 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `WeeklyNormHours` |
| `current_encoded_value` | `37.0` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §2 (Personalestyrelsen / Medst — Aftale om arbejdstid) |
| `interpretation` | Standard weekly norm for full-time AC employees is 37 hours, matching the universal Danish state employee norm. |
| `confidence_level` | HIGH (universally-accepted state-sector convention; AC overenskomst confirms) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same value across AC OK24 + OK26 + HK + PROSA + AC_RESEARCH + AC_TEACHING — universal state-sector norm. Reduced annual norm in AC_TEACHING (1680h) reflects research obligations but the weekly norm field itself stays 37h. |

### SR-AC-OK24-002 — MaxOvertimeHoursPerPeriod

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-002 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `MaxOvertimeHoursPerPeriod` |
| `current_encoded_value` | `0` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §4 (merarbejde regime — no fixed cap per period) |
| `interpretation` | AC has no fixed cap on merarbejde hours per period; the value `0` is sentinel for "no cap applies". S17 Overtime Governance feature uses this field only for HK / PROSA where caps exist. |
| `confidence_level` | MEDIUM (the sentinel-zero convention is project-internal; cirkulær doesn't fix a cap so the encoding is correct, but reader of the seed needs to know `0` means "no cap" not "zero hours"). |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Sentinel-zero convention worth documenting — Phase B should confirm this is the intended encoding (vs e.g., NULL or `int.MaxValue`). Cross-ref: `OvertimeGovernanceRule.cs` should treat `0` as "no cap" not "zero allowed". Candidate Phase E test: ensure rule treats `0` as no-cap correctly. |

### SR-AC-OK24-003 — MinimumRestHours

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-003 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `MinimumRestHours` |
| `current_encoded_value` | `11.0` |
| `authoritative_source` | EU WTD 2003/88/EC Article 3 (daily rest 11 consecutive hours) — Danish transposition via Lov om arbejdstid / Arbejdstidsloven |
| `interpretation` | Minimum 11 consecutive hours of rest between two working days. EU-mandated floor; applies to all state-sector agreements identically. |
| `confidence_level` | HIGH (explicit EU Directive Article 3; Danish transposition law mirrors verbatim) |
| `interpretation_authority` | EU (transposed by Personalestyrelsen) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Identical across all 5 agreement codes (EU floor). Derogation flag (`RestPeriodDerogationAllowed`) varies per agreement — see SR-AC-OK24-017. |

### SR-AC-OK24-004 — WeeklyMaxHoursReferencePeriod

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-004 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `WeeklyMaxHoursReferencePeriod` |
| `current_encoded_value` | `17` |
| `authoritative_source` | EU WTD 2003/88/EC Article 6 (max 48h average over reference period) + Article 16 (reference period up to 4 months / 17 weeks) — Danish transposition via Lov om arbejdstid |
| `interpretation` | Reference period (in weeks) over which the 48h weekly maximum averages. EU directive allows up to 4 months / 17 weeks; Danish state sector adopts the 17-week maximum. |
| `confidence_level` | HIGH (EU Article 16 explicit; 17 weeks is the standard Danish transposition value for state sector) |
| `interpretation_authority` | EU (transposed by Personalestyrelsen) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Identical across all 5 agreement codes. Used by S16 RestPeriodRule + NormCheckRule for 48h compliance check. |

### SR-AC-OK24-005 — DefaultCompensationModel

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-005 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `DefaultCompensationModel` |
| `current_encoded_value` | `"AFSPADSERING"` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §4 (AC overenskomst cirkulær 043-19, Personalestyrelsen / Medst) — afspadsering as far as possible; payment as fallback when afspadsering infeasible |
| `interpretation` | AC default compensation for merarbejde is afspadsering (time-off-in-lieu). UDBETALING is a fallback only when afspadsering is infeasible per cirkulær §4. AC employees do not have an unconditional employee-side choice between models (`EmployeeCompensationChoice = false` — see SR-AC-OK24-006). |
| `confidence_level` | HIGH (explicit cirkulær §4 + S35 web-verified across 5 sources: Personalestyrelsen + Akademikerne + Djoef + Folketinget + DM) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | Orchestrator (Claude), per S35 TASK-3503 |
| `decision_date` | 2026-05-18 (S35 TASK-3503 commit `cbaea7d`) |
| `supersession_history` | (none) |
| `bug_correction_history` | `[{date: 2026-05-18, from_value: "UDBETALING", to_value: "AFSPADSERING", source_url: "https://oes.dk/media/ik0hm2lr/043-19.pdf §4", commit: "cbaea7d", classifier: "Orchestrator (Claude)", was_agreed: NO, materially_wrong: NO_PRE_LAUNCH, action: "bug-fix-without-recompute"}]` |
| `disputed?` | false |
| `notes` | **First concrete application of the ROADMAP rule correction policy's bug-correction-when-classified path.** Bug originated in S17 when `DefaultCompensationModel` field was added; AC entries in `CentralAgreementConfigs.cs` inherited the model default `"UDBETALING"` from `AgreementRuleConfig.cs:67` without explicit override; matching `init.sql` seed rows perpetuated. Forward-only correction; no past periods exist (pre-launch posture); no retroactive recompute. Same correction propagates to AC_RESEARCH + AC_TEACHING (TASK-3605). |

### SR-AC-OK24-006 — EmployeeCompensationChoice

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-006 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `EmployeeCompensationChoice` |
| `current_encoded_value` | `false` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §4 (employer-determined feasibility of afspadsering; AC employee does not have an unconditional model choice) |
| `interpretation` | AC employee does NOT have an unconditional choice between afspadsering and udbetaling for merarbejde compensation. Employer determines whether afspadsering is feasible per §4; if not, fallback to udbetaling. Contrast HK where `EmployeeCompensationChoice = true` (employee choice within rules). |
| `confidence_level` | HIGH (paired with SR-AC-OK24-005; both verified in S35 TASK-3503 same source pass) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | Orchestrator (Claude), per S35 TASK-3503 |
| `decision_date` | 2026-05-18 |
| `supersession_history` | (none) |
| `bug_correction_history` | (none — `false` was already the model default; made explicit in S35 TASK-3503 for clarity but value unchanged) |
| `disputed?` | false |
| `notes` | Distinguishes AC from HK / PROSA where the field is `true`. Functionally relevant — Overtime D-test `Overtime_PastPeriodBalance_UsesPeriodEffectiveAgreementCode_NotLive` (per S35 TASK-3508) tests on HK/AC discriminator using this field. |

### SR-AC-OK24-007 — HasMerarbejde

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-007 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `HasMerarbejde` |
| `current_encoded_value` | `true` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §4 (AC overenskomst — merarbejde regime applies to AC) |
| `interpretation` | AC employees are subject to the merarbejde regime (additional work beyond norm compensated as afspadsering or, fallback, as udbetaling). Distinct from overtime — AC has `HasOvertime = false`. |
| `confidence_level` | HIGH |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Role-distinction caveat**: AC chefkonsulent loses contractual merarbejde compensation right per AC overenskomst (the PROGRAM L31–32 gap). Cell value `true` is correct at agreement level but the rule engine treats all AC employees identically because `User.EmploymentCategory` is vestigial. ADR-024 D2 (S38) will introduce tri-state `MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE` to express this. Cross-ref: `role-dimension-audit.md` (TASK-3606). |

### SR-AC-OK24-008 — OvertimeRequiresPreApproval

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-008 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `OvertimeRequiresPreApproval` |
| `current_encoded_value` | `false` |
| `authoritative_source` | pending (Phase B confirmation needed — S17 introduced this field as a governance gate, not a cirkulær-mandated value) |
| `interpretation` | Pre-approval workflow for overtime requests. AC has `false` because AC overtime is merarbejde-routed (employer-initiated under §4 feasibility); no pre-approval workflow at agreement level. HK / PROSA have `true` (their overtime is genuine overtime with worker-initiation possible, hence the pre-approval gate). |
| `confidence_level` | MEDIUM (the encoding aligns with merarbejde-vs-overtime distinction but the binary `false` value for AC reflects "workflow disabled" not "cirkulær explicitly says no pre-approval" — Phase B sign-off should confirm). |
| `interpretation_authority` | negotiated (this is a system-design gate, not a cirkulær-mandated boolean) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Workflow gate, not rule-engine input. Field added in S17 Overtime Governance. Phase B should confirm `false` is intended for AC at agreement level (vs e.g., wanting per-institution opt-in via local_configurations). |

### SR-AC-OK24-009 — EveningRate (inert)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-009 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `EveningRate` |
| `current_encoded_value` | `1.25` |
| `authoritative_source` | n/a-for-agreement — AC has `EveningSupplementEnabled = false`, so the field value never reaches any rule decision. The `1.25` is the C# `init` default from `AgreementRuleConfig.cs:26` + the init.sql column DEFAULT (1.25). |
| `interpretation` | **Functionally inert in AC**. The rate value exists in seed but is never applied because evening-supplement is disabled at the agreement level. AC employees do not receive evening hourly supplements. Reference for HK / PROSA: rate 1.25 = 25% supplement on the standard hourly wage. |
| `confidence_level` | N/A-for-agreement |
| `interpretation_authority` | (n/a — value inert) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Proof-of-shape finding**: inert cells exist when an agreement disables a feature but the rate / threshold value remains in seed. Schema accommodates via `confidence_level = N/A-for-agreement` + this `notes` clarification. Phase E seed-parity tests should treat inert cells as "value irrelevant, do not fail on mismatch" — encoded via per-cell `confidence_level` filter, not by separate inertness flag. Same pattern applies to SR-AC-OK24-010 (NightRate), -011 (WeekendSundayRate), and other supplement-disabled cells. |

### SR-AC-OK24-010 — NightRate (inert)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-010 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `NightRate` |
| `current_encoded_value` | `1.50` |
| `authoritative_source` | n/a-for-agreement — AC has `NightSupplementEnabled = false`. |
| `interpretation` | **Functionally inert in AC**. Rate exists in seed; never applied. Reference for HK / PROSA: 1.50 = 50% supplement for hours worked between 23:00–06:00. |
| `confidence_level` | N/A-for-agreement |
| `interpretation_authority` | (n/a) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same pattern as SR-AC-OK24-009. |

### SR-AC-OK24-011 — MaxFlexBalance

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-011 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `MaxFlexBalance` |
| `current_encoded_value` | `150.0` |
| `authoritative_source` | pending (Phase B; HK has 100h, PROSA has 120h, AC has 150h — likely local-agreement-driven per ROADMAP glocal principle, but the central seed value reflects an AC-side baseline that Phase B should source-cite) |
| `interpretation` | Maximum positive flex balance an AC employee can accrue (hours). Exceeding this triggers automatic conversion or payout per FlexBalanceRule. AC's higher ceiling (150h vs HK's 100h) reflects the AC merarbejde regime — AC employees have more flexibility to accumulate flex hours before being forced to take afspadsering. |
| `confidence_level` | MEDIUM (the value is well-established in the project but the cirkulær-paragraph source needs Phase B confirmation — this is the kind of cell where local-agreement variation is permitted per ROADMAP glocal principle, so the "central seed value" is a baseline, not a hard cirkulær number). |
| `interpretation_authority` | Personalestyrelsen (baseline) / negotiated (local-agreement variations permitted via `local_configurations` per ADR-017) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Glocal cell**: per ROADMAP glocal principle, this is a rule-delegated parameter where institutions vary via `local_configurations` (ADR-017 LocalAgreementProfile). The central seed value (150h) is the default; institutional override permitted within bounds determined by central cirkulær. Phase B should confirm the central baseline is correctly cited. |

### SR-AC-OK24-012 — FlexCarryoverMax

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-012 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `FlexCarryoverMax` |
| `current_encoded_value` | `150.0` |
| `authoritative_source` | pending (Phase B; co-located with `MaxFlexBalance` — typically equal so the entire balance can carry year-over-year) |
| `interpretation` | Maximum flex balance hours an AC employee can carry across the flex year boundary. Equal to `MaxFlexBalance` = full carryover; the year boundary doesn't truncate. |
| `confidence_level` | MEDIUM (same glocal-cell rationale as SR-AC-OK24-011) |
| `interpretation_authority` | Personalestyrelsen / negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Glocal cell. Co-located with SR-AC-OK24-011. Phase B should confirm whether full carryover is canonical or an institutional choice (could see a cirkulær that says "no carryover" with institutional opt-in to full carryover via local agreement). |

### SR-AC-OK24-013 — entitlement_configs.VACATION.annual_quota

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-013 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.VACATION.annual_quota` |
| `current_encoded_value` | `25` |
| `authoritative_source` | Ferieloven (Lov om ferie, LBK nr 230 af 12/02/2021 §8 — 25 ferie-dage per ferieår) + tjenestemandslovens supplerende bestemmelser |
| `interpretation` | 25 paid vacation days per ferieår (September 1 → August 31). Universal Danish statutory minimum applies to all state employees regardless of agreement. |
| `confidence_level` | HIGH (Ferieloven explicit; identical across AC / HK / PROSA / AC_RESEARCH / AC_TEACHING) |
| `interpretation_authority` | Folketinget (statutory law) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-table reference — value lives in `entitlement_configs`, not `agreement_configs`. Reset month = 9 (September) co-stored in same row. Pro-rated by part-time fraction (`pro_rate_by_part_time = true`). Carryover max = 5 days. Same pattern repeats for SPECIAL_HOLIDAY (5 days, no carryover), CARE_DAY (2 days), CHILD_SICK (1/2/3 days agreement-varying), SENIOR_DAY (0 days quota; min_age=60). |

### SR-AC-OK24-014 — entitlement_configs.CARE_DAY.annual_quota

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-014 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.CARE_DAY.annual_quota` |
| `current_encoded_value` | `2` |
| `authoritative_source` | pending (Phase B; omsorgsdage are typically established via overenskomst; AC overenskomst §X — exact paragraph TBD) |
| `interpretation` | 2 omsorgsdage (care days) per calendar year (reset month = 1 / January). Not pro-rated by part-time fraction (full quota regardless of working hours). |
| `confidence_level` | MEDIUM (value is standard across AC / HK / PROSA; cirkulær-paragraph source needs Phase B confirmation) |
| `interpretation_authority` | Personalestyrelsen / Akademikerne (negotiated) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-table reference. Reset month = 1, no carryover, not pro-rated. |

### SR-AC-OK24-015 — entitlement_configs.SENIOR_DAY.annual_quota

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-015 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.SENIOR_DAY.annual_quota` |
| `current_encoded_value` | `0` |
| `authoritative_source` | pending (Phase B — `0` with `min_age = 60` suggests the rule is "age-based grant, no automatic quota" rather than "0 days for everyone") |
| `interpretation` | Sentinel `0` with `min_age = 60` predicate. The intended encoding is "no automatic grant; eligibility starts at age 60+ and quota is determined per-employee (likely 1–5 days depending on age band)". As encoded today, `0` returns no days regardless of age — likely incomplete encoding. |
| `confidence_level` | LOW (encoding semantics unclear; this is exactly the kind of cell Phase B should adjudicate) |
| `interpretation_authority` | negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Candidate bug discovery**. The `0` quota means SENIOR_DAY entitlement never grants any days even for age-60+ employees. Either (a) the encoding is incomplete (rule logic should override `0` with age-banded quota lookup), or (b) the `0` is correct and senior days are not granted via this table (but then `min_age = 60` is vestigial). Flag for S37 absorption + Phase B sign-off. Priority for S37 because senior-employee compensation correctness is a domain-correctness concern. |

### SR-AC-OK24-016 — entitlement_configs.CHILD_SICK.annual_quota

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-016 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.CHILD_SICK.annual_quota` |
| `current_encoded_value` | `1` |
| `authoritative_source` | pending (Phase B; AC = 1 day per episode is the most restrictive across the three; HK = 2, PROSA = 3) |
| `interpretation` | 1 day per episode (`is_per_episode = true`); not pro-rated, no carryover. Encoding diverges across agreements: AC=1, HK=2, PROSA=3 days per episode. The per-episode semantic means each child-illness incident grants the quota independently (no annual cumulative limit). |
| `confidence_level` | MEDIUM (the AC=1 / HK=2 / PROSA=3 progression matches the project's prior encoding from S15; cirkulær-paragraph source needs Phase B confirmation) |
| `interpretation_authority` | negotiated (per-overenskomst) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-agreement variance — AC has the most restrictive quota. Phase B should verify the 1-day AC value (some sources cite "barn syg" as 2 days for state employees regardless of agreement — would need correction if so). |

### SR-AC-OK24-017 — RestPeriodDerogationAllowed

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-017 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `RestPeriodDerogationAllowed` |
| `current_encoded_value` | `false` |
| `authoritative_source` | EU WTD 2003/88/EC Article 17 (derogation permitted for specific worker categories) + AC overenskomst — derogation not extended to standard AC employees |
| `interpretation` | AC employees may NOT derogate from the 11-hour minimum daily rest. Strict EU floor applies. Contrast HK / PROSA where the field is `true` — HK and PROSA permit derogation under specific operational circumstances (e.g., on-call work) with compensatory rest. |
| `confidence_level` | HIGH (EU directive explicit; AC overenskomst silent on derogation = default-no per Article 17 framework) |
| `interpretation_authority` | EU / Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Used by S16 RestPeriodRule. The AC=false vs HK/PROSA=true split is correct per agreement-level operational reality (AC employees don't typically have on-call obligations that require derogation). |

### SR-AC-OK24-018 — MaxDailyHours

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-018 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `MaxDailyHours` |
| `current_encoded_value` | `13.0` |
| `authoritative_source` | EU WTD 2003/88/EC Article 3 (implicit cap: 24h day - 11h minimum rest = 13h max work) + Danish transposition |
| `interpretation` | Maximum 13 hours of work per day, computed as the residual after the 11-hour rest mandate. Applies to all state-sector agreements identically — EU floor. |
| `confidence_level` | HIGH (EU directive derivation explicit) |
| `interpretation_authority` | EU |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Identical across all 5 agreement codes. Used by S16 RestPeriodRule. |

### SR-AC-OK24-019 — VoluntaryUnsocialHoursAllowed

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-019 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `VoluntaryUnsocialHoursAllowed` |
| `current_encoded_value` | `true` |
| `authoritative_source` | pending (Phase B — this is a compliance / workflow gate, not a cirkulær-mandated value) |
| `interpretation` | AC employees may voluntarily work hours that would otherwise trigger unsocial-hours-supplement entitlements (e.g., evening / night / weekend) WITHOUT auto-emitting the supplement events. Because AC has all supplement flags `false` anyway, this field is `true` for completeness but has no functional effect on AC payroll mapping. |
| `confidence_level` | MEDIUM (the field semantic is project-internal; `true` for AC is consistent with "supplements disabled so voluntary-work distinction moot") |
| `interpretation_authority` | negotiated (system-design gate) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Workflow gate. For HK / PROSA where supplements are enabled, this field gates whether voluntarily-worked unsocial hours emit supplement events. Phase B should confirm the field semantic + AC=true encoding. |

### SR-AC-OK24-020 — NormModel + NormPeriodWeeks + AnnualNormHours (combined cell)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-020 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `NormModel + NormPeriodWeeks + AnnualNormHours` (compound — these 3 fields together encode the norm-period model) |
| `current_encoded_value` | `NormModel = "WEEKLY_HOURS"; NormPeriodWeeks = 1; AnnualNormHours = 1924` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §2 (37h weekly norm × 52 weeks = 1924h annual) |
| `interpretation` | AC OK24 uses the standard WEEKLY_HOURS norm model: 37h per week, 1-week norm period, no multi-week averaging. The `AnnualNormHours = 1924` field is set for forward-compat with ANNUAL_ACTIVITY consumers (AC_RESEARCH + AC_TEACHING use ANNUAL_ACTIVITY model with same 1924 / 1680 annual targets) but is not consulted by AC's weekly-norm rule path. |
| `confidence_level` | HIGH |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Schema observation**: this cell groups 3 schema fields because their values jointly encode the norm-period model — they're not independently meaningful (changing `NormModel` from `WEEKLY_HOURS` to `ANNUAL_ACTIVITY` makes `NormPeriodWeeks` irrelevant). The 15-column schema handles this via `field` accepting compound names. Phase E seed-parity tests should treat the triple atomically. Cross-ref: SR-AC_RESEARCH-OK24-XXX will document the ANNUAL_ACTIVITY variant where `NormPeriodWeeks` becomes inert. |

### SR-AC-OK24-021 — HasOvertime

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-021 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `HasOvertime` |
| `current_encoded_value` | `false` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §4 (AC overenskomst — merarbejde regime, not overtime regime) |
| `interpretation` | AC employees are NOT subject to the standard overtime regime (where hours beyond a daily/weekly threshold automatically trigger 50% / 100% supplement). Excess hours instead route through the merarbejde regime (see SR-AC-OK24-007). The `HasOvertime = false` flag disables `OvertimeRule.Evaluate` from emitting OVERTIME_50 / OVERTIME_100 events for AC employees. |
| `confidence_level` | HIGH (explicit cirkulær framework — AC has merarbejde, HK/PROSA have overtime; these are mutually exclusive regimes per project-internal convention but underlying cirkulær clearly distinguishes them) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Mutually exclusive with `HasMerarbejde` in project-internal convention. AC = (false, true); HK = (true, false); PROSA = (true, false). Renders `OvertimeThreshold50/100` cells inert for AC (see SR-AC-OK24-022 + SR-AC-OK24-023). |

### SR-AC-OK24-022 — OvertimeThreshold50 (inert)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-022 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `OvertimeThreshold50` |
| `current_encoded_value` | `37.0` |
| `authoritative_source` | n/a-for-agreement — AC has `HasOvertime = false`; threshold never reaches an evaluation. The `37.0` is the C# `init` default from `AgreementRuleConfig.cs:33` + the init.sql column DEFAULT. |
| `interpretation` | **Functionally inert in AC**. Reference for HK / PROSA: hours per week beyond 37 (the weekly norm) trigger 50% supplement until `OvertimeThreshold100` (40h) is reached. |
| `confidence_level` | N/A-for-agreement |
| `interpretation_authority` | (n/a — value inert) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same pattern as SR-AC-OK24-009 / -010 (inert supplement rates). |

### SR-AC-OK24-023 — OvertimeThreshold100 (inert)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-023 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `OvertimeThreshold100` |
| `current_encoded_value` | `40.0` |
| `authoritative_source` | n/a-for-agreement — AC has `HasOvertime = false`; threshold never reaches an evaluation. |
| `interpretation` | **Functionally inert in AC**. Reference for HK / PROSA: hours per week beyond 40 trigger 100% supplement. |
| `confidence_level` | N/A-for-agreement |
| `interpretation_authority` | (n/a) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Inert. Same pattern as SR-AC-OK24-022. |

### SR-AC-OK24-024 — Supplement-disablement bundle (compound, 11 columns inert)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-024 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `EveningSupplementEnabled + NightSupplementEnabled + WeekendSupplementEnabled + HolidaySupplementEnabled + EveningStart + EveningEnd + NightStart + NightEnd + WeekendSaturdayRate + WeekendSundayRate + HolidayRate` (compound — 11 columns; see notes) |
| `current_encoded_value` | `false / false / false / false / 17 / 23 / 23 / 6 / 1.50 / 2.0 / 2.0` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf — AC overenskomst does not provide hourly supplements for evening / night / weekend / holiday work. AC's compensation model is merarbejde, not per-hour supplements. |
| `interpretation` | AC employees receive NO hourly supplements for unsocial hours (evening / night / weekend / holiday). The 4 boolean flags disable the supplement rules at the agreement level; the time-window + rate values are functionally inert. Each flag's `false` is the source-of-truth decision; the inert-for-AC time-windows + rates are project-internal placeholders matching the HK / PROSA default time windows and HK / PROSA supplement rates so the schema column DEFAULTs apply uniformly across all rows. |
| `confidence_level` | HIGH for the 4 boolean flags (AC overenskomst clearly does not provide supplements); N/A-for-agreement for the 7 time-window + rate values (inert) |
| `interpretation_authority` | Personalestyrelsen (for the disablement); n/a (for the inert values) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Compound row covering 11 columns**: the 4 enable flags carry the load-bearing decision (AC = no supplements); the 7 follow-on cells (time windows + supplement rates not yet covered by SR-AC-OK24-009 / -010) are inert in AC and exist only because the seed table requires NOT NULL columns. **Phase B sign-off scope**: confirm AC does not have ANY hourly supplements (e.g., no Christmas Eve premium, no Easter Friday premium); historically AC's compensation is salary-only with merarbejde for surplus work. EveningRate (SR-AC-OK24-009) + NightRate (SR-AC-OK24-010) already covered as individual proof-of-shape rows. |

### SR-AC-OK24-025 — OnCallDutyEnabled

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-025 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `OnCallDutyEnabled` |
| `current_encoded_value` | `false` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf — AC overenskomst does not provide on-call duty (rådighedsvagt) compensation at the agreement level. |
| `interpretation` | AC employees are NOT subject to on-call duty compensation at the agreement level. **Caveat**: specific roles within AC (e.g., researchers on field deployment, lab-monitor obligations) may have on-call arrangements via local agreement — see `local_configurations` per ADR-017. Agreement-level default is `false`. |
| `confidence_level` | MEDIUM (AC standard is no on-call; LOW would be too pessimistic since the historical absence is well-established, but Phase B should confirm whether any AC role has on-call as a contractual baseline) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Local-agreement overrides via `local_configurations` may flip this per ADR-017; glocal interaction. Renders SR-AC-OK24-026 (OnCallDutyRate) inert at agreement level. |

### SR-AC-OK24-026 — OnCallDutyRate (inert)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-026 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `OnCallDutyRate` |
| `current_encoded_value` | `0.33` |
| `authoritative_source` | n/a-for-agreement (AC has `OnCallDutyEnabled = false` at agreement level; rate inert) |
| `interpretation` | **Functionally inert in AC at agreement level**. Reference for HK / PROSA: 33% of standard hourly wage per on-call hour. If `local_configurations` enables on-call for a specific AC institution, the rate would apply at that institution. |
| `confidence_level` | N/A-for-agreement (at agreement level); MEDIUM if a local override surfaces |
| `interpretation_authority` | (n/a at agreement level) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Inert at agreement level; activated only via `local_configurations` override (glocal cell). |

### SR-AC-OK24-027 — CallInWorkEnabled + CallInMinimumHours + CallInRate (compound, 3 columns)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-027 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `CallInWorkEnabled + CallInMinimumHours + CallInRate` (compound — 3 columns) |
| `current_encoded_value` | `false / 3.0 / 1.0` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf — AC overenskomst does not provide call-in (tilkald) compensation at the agreement level. |
| `interpretation` | AC employees are NOT subject to call-in compensation at agreement level. The 3.0-hour minimum + 1.0× rate are HK / PROSA reference values, inert for AC. Same local-agreement caveat as on-call (SR-AC-OK24-025). |
| `confidence_level` | MEDIUM for the boolean (AC no-call-in is standard); N/A-for-agreement for the inert minimum-hours + rate |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Compound row covering 3 columns. Same glocal caveat as SR-AC-OK24-025: local-agreement overrides via `local_configurations`. Note the wage_type_mappings table has CALL_IN_WORK → SLS_0810 mapped for AC (see SR-AC-OK24-037 bundle) — the mapping exists so that if local override enables call-in for an AC institution, the payroll code is ready. |

### SR-AC-OK24-028 — TravelTimeEnabled

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-028 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `TravelTimeEnabled` |
| `current_encoded_value` | `true` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf — AC employees on official travel are compensated for travel time per the working / non-working split |
| `interpretation` | AC employees are entitled to travel-time compensation for official trips. The split between "working" (full-rate) and "non-working" (half-rate) travel applies per AC overenskomst. |
| `confidence_level` | MEDIUM (the boolean is well-established as `true` for state-sector AC; cirkulær-paragraph specifically on AC travel-time may need Phase B verification) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Identical to HK / PROSA (all three have `TravelTimeEnabled = true`). Activates SR-AC-OK24-029 (WorkingTravelRate) + SR-AC-OK24-030 (NonWorkingTravelRate). |

### SR-AC-OK24-029 — WorkingTravelRate

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-029 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `WorkingTravelRate` |
| `current_encoded_value` | `1.0` |
| `authoritative_source` | pending (Phase B — typically AC overenskomst states travel time DURING ordinary working hours is counted 1:1; cirkulær-paragraph specific to AC TBD) |
| `interpretation` | Travel during an employee's ordinary working hours is compensated at 100% — counted as worked hours for norm purposes. `1.0` = 1:1 conversion ratio. |
| `confidence_level` | MEDIUM (1:1 ratio for in-hours travel is the universal state-sector convention; Phase B should confirm AC-specific cirkulær wording) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same value across AC / HK / PROSA. Used by TravelTimeRule (S10). |

### SR-AC-OK24-030 — NonWorkingTravelRate

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-030 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `NonWorkingTravelRate` |
| `current_encoded_value` | `0.5` |
| `authoritative_source` | pending (Phase B — typically AC overenskomst states travel time OUTSIDE ordinary working hours is counted at 50%) |
| `interpretation` | Travel outside an employee's ordinary working hours is compensated at 50% — half the elapsed travel time counts as worked hours for norm purposes. `0.5` = half-rate conversion. |
| `confidence_level` | MEDIUM (0.5 half-rate for out-of-hours travel is the well-established state-sector convention; cirkulær-paragraph cite needs Phase B) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same value across AC / HK / PROSA. The split between SR-AC-OK24-029 (in-hours full rate) + SR-AC-OK24-030 (out-of-hours half rate) is the universal state-sector pattern. |

### SR-AC-OK24-031 — entitlement_configs.SPECIAL_HOLIDAY.annual_quota

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-031 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.SPECIAL_HOLIDAY.annual_quota` |
| `current_encoded_value` | `5` |
| `authoritative_source` | pending (Phase B — særlige feriedage / 6. ferieuge is typically established via overenskomst for state employees) |
| `interpretation` | 5 særlige feriedage ("special holiday days" or "6th vacation week") per ferieår (reset month = 9). Pro-rated by part-time fraction (`pro_rate_by_part_time = true`). No carryover (`carryover_max = 0`). |
| `confidence_level` | MEDIUM (5 special holiday days for state employees is the established convention; cirkulær-paragraph specific to AC needs Phase B confirmation) |
| `interpretation_authority` | Personalestyrelsen / Akademikerne (negotiated) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-table reference — `entitlement_configs` row at init.sql:1352. Reset month = 9 (September), no carryover, pro-rated. Same value across AC / HK / PROSA. |

### SR-AC-OK24-032 — entitlement_configs.VACATION sub-fields (compound, 4 columns)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-032 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.VACATION.{accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` (compound — 5 sub-fields beyond `annual_quota` covered in SR-AC-OK24-013) |
| `current_encoded_value` | `IMMEDIATE / 9 / 5 / true / false` |
| `authoritative_source` | Ferieloven (LBK nr 230 af 12/02/2021) §8 + §15 (ferieår starts September 1; max 5 days carryover) |
| `interpretation` | VACATION accrues immediately on hire (`IMMEDIATE` accrual; not the alternative `MONTHLY` accrual where days accrue 1/12 per month). Ferieår resets September 1. Up to 5 days can be carried over to the next ferieår. Pro-rated by part-time fraction. Not per-episode (`is_per_episode = false` — annual cumulative quota, not per-event). |
| `confidence_level` | HIGH (Ferieloven explicit) |
| `interpretation_authority` | Folketinget (statutory law) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-table reference. Co-located with SR-AC-OK24-013 (VACATION annual_quota). Compound row for sub-field bundle. |

### SR-AC-OK24-033 — entitlement_configs.CARE_DAY sub-fields (compound)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-033 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.CARE_DAY.{accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` |
| `current_encoded_value` | `IMMEDIATE / 1 / 0 / false / false` |
| `authoritative_source` | pending (Phase B — omsorgsdage established via overenskomst; ikke pro-rate per state-sector convention) |
| `interpretation` | CARE_DAY accrues immediately on hire. Resets January 1 (`reset_month = 1` — calendar-year, distinct from VACATION's September-start ferieår). No carryover. Not pro-rated by part-time fraction (`pro_rate_by_part_time = false` — full 2 days regardless of working hours; supports work-life-balance principle that care obligations exist regardless of FTE). Not per-episode (annual cumulative quota). |
| `confidence_level` | MEDIUM (the pattern of January-reset + no-pro-rate is established state-sector convention; cirkulær-paragraph specific to AC needs Phase B) |
| `interpretation_authority` | Personalestyrelsen / Akademikerne |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Co-located with SR-AC-OK24-014 (CARE_DAY annual_quota). Cross-table reference. |

### SR-AC-OK24-034 — entitlement_configs.CHILD_SICK sub-fields (compound)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-034 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.CHILD_SICK.{accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` |
| `current_encoded_value` | `IMMEDIATE / 1 / 0 / false / true` |
| `authoritative_source` | pending (Phase B — barn-syg-dage are typically per-episode under all state agreements) |
| `interpretation` | CHILD_SICK is per-episode (`is_per_episode = true` — each child-illness episode grants the full quota independently; no annual cumulative limit). Reset month = 1 (calendar year). No carryover. Not pro-rated. The `reset_month` is irrelevant to per-episode semantics (the field is required by schema but the per-episode behavior bypasses annual reset). |
| `confidence_level` | MEDIUM (per-episode semantic is well-established for barn-syg; cirkulær-paragraph specific to AC's 1-day allowance needs Phase B confirmation) |
| `interpretation_authority` | negotiated (per-overenskomst varies AC=1 / HK=2 / PROSA=3) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Co-located with SR-AC-OK24-016 (CHILD_SICK annual_quota). **Schema observation**: `reset_month = 1` is required by NOT NULL constraint but semantically meaningless when `is_per_episode = true`. Candidate Phase E test: assert rule treats per-episode entitlements ignoring reset_month. |

### SR-AC-OK24-035 — entitlement_configs.SENIOR_DAY sub-fields (compound)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-035 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.SENIOR_DAY.{accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode, min_age}` |
| `current_encoded_value` | `IMMEDIATE / 1 / 0 / false / false / 60` |
| `authoritative_source` | pending (Phase B priority — same LOW-confidence concern as SR-AC-OK24-015; the `min_age = 60` with `annual_quota = 0` encoding is unclear) |
| `interpretation` | The `min_age = 60` field gates eligibility to employees aged 60+. **However**, paired with `annual_quota = 0` (see SR-AC-OK24-015), no days are actually granted. The intended semantic is likely "age-banded grant lookup outside this table" (e.g., 1 day at age 60–61, 2 days at age 62–63, ...) but as encoded the system never grants any senior days regardless of age. **Bug candidate**. |
| `confidence_level` | LOW (encoding semantics unclear; paired with SR-AC-OK24-015 LOW finding) |
| `interpretation_authority` | negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Candidate bug** (paired with SR-AC-OK24-015). The `min_age = 60` field is non-NULL only for SENIOR_DAY entitlements — schema design implies age-based grant. Phase B priority. |

### SR-AC-OK24-036 — entitlement_configs.SPECIAL_HOLIDAY sub-fields (compound)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-036 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.SPECIAL_HOLIDAY.{accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` |
| `current_encoded_value` | `IMMEDIATE / 9 / 0 / true / false` |
| `authoritative_source` | pending (Phase B — same as SR-AC-OK24-031 SPECIAL_HOLIDAY quota) |
| `interpretation` | SPECIAL_HOLIDAY accrues immediately on hire. Resets September 1 (`reset_month = 9` — same ferieår boundary as VACATION). No carryover (distinct from VACATION's 5-day carryover). Pro-rated by part-time fraction. Not per-episode. |
| `confidence_level` | MEDIUM (same as SR-AC-OK24-031 — pattern established; AC-specific cirkulær paragraph needed) |
| `interpretation_authority` | Personalestyrelsen / Akademikerne |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Co-located with SR-AC-OK24-031. The no-carryover semantic distinguishes special holidays from regular vacation (which has 5-day carryover). |

### SR-AC-OK24-037 — wage_type_mappings AC OK24 bundle (compound, 17 mappings)

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-037 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `wage_type_mappings (time_type, wage_type) for (AC, OK24, position='')` — bundle of 17 mappings (init.sql:217–285 + L1197 NORM_DEVIATION) |
| `current_encoded_value` | `NORMAL_HOURS → SLS_0110; MERARBEJDE → SLS_0310; VACATION → SLS_0510; CARE_DAY → SLS_0520; CHILD_SICK_DAY → SLS_0530; CHILD_SICK_DAY_2 → SLS_0531; CHILD_SICK_DAY_3 → SLS_0532; PARENTAL_LEAVE → SLS_0540; SENIOR_DAY → SLS_0550; LEAVE_WITHOUT_PAY → SLS_0560; LEAVE_WITH_PAY → SLS_0565; SPECIAL_HOLIDAY_ALLOWANCE → SLS_0570; FLEX_PAYOUT → SLS_0610; ON_CALL_DUTY → SLS_0710; CALL_IN_WORK → SLS_0810; TRAVEL_WORK → SLS_0820; TRAVEL_NON_WORK → SLS_0830; NORM_DEVIATION → SLS_0150` |
| `authoritative_source` | SLS (Statens Lønsystem) wage type codes — Personalestyrelsen / Medst publishes the canonical SLS code list; specific code-to-time-type mapping established at project setup (S5 SLS export) and confirmed during integration. Authoritative source URL: SLS technical documentation (internal to Personalestyrelsen — pending Phase B reference). |
| `interpretation` | 17 wage type mappings carry time-type events from the rule engine to SLS wage codes for AC OK24 employees. Coverage includes: normal hours (SLS_0110), AC-specific merarbejde (SLS_0310), all absence types, flex payout, on-call (mapped despite agreement-level disablement to support local-agreement enablement), call-in (same), travel time, and NORM_DEVIATION (S11). **No OVERTIME_50 / OVERTIME_100 / supplement mappings for AC** because AC has `HasOvertime = false` and supplements disabled (correctly omitted from seed). |
| `confidence_level` | HIGH for the AC-pinned mappings (validated through S5 SLS export pipeline + S11 NORM_DEVIATION + S17 compensation model); MEDIUM for the SLS code values themselves (Phase B should verify against current Personalestyrelsen SLS technical doc) |
| `interpretation_authority` | Personalestyrelsen (SLS technical authority) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none — wage type codes are stable across OK transitions; renewal events would supersede via S29 `WageTypeMappingSuperseded` event) |
| `bug_correction_history` | (none — AC mappings have not been corrected; HK/PROSA + AC_RESEARCH/AC_TEACHING may surface bugs in TASK-3603/3604/3605) |
| `disputed?` | false |
| `notes` | **Compound row covering 17 mappings**. Phase B verification priority: confirm SLS codes match current Personalestyrelsen reference (some SLS codes may have been renamed between 2020 — current effective_from — and now). **Cross-agreement note**: NORMAL_HOURS, VACATION, CARE_DAY, etc. share SLS codes across AC / HK / PROSA — correct per state-sector convention. MERARBEJDE (SLS_0310) and NORM_DEVIATION (SLS_0150) are AC-family-only mappings. Position-tier mappings (e.g., position-specific MERARBEJDE rates for chefkonsulent) live in separate rows when implemented in S40 (currently `position = ''` for all AC mappings — see SR-AC-OK24-038 for position override modeling). |

### SR-AC-OK24-038 — position_override_configs AC OK24 DEPARTMENT_HEAD

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-038 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `position_override_configs (AC, OK24, DEPARTMENT_HEAD).{max_flex_balance, norm_period_weeks, flex_carryover_max?, weekly_norm_hours?}` |
| `current_encoded_value` | `max_flex_balance = 200.0; norm_period_weeks = 4; flex_carryover_max = NULL (inherits 150 from base); weekly_norm_hours = NULL (inherits 37 from base)` |
| `authoritative_source` | pending (Phase B — kontorchef / department head working-time arrangement typically established via local agreement at the institution level; the central seed value here is a baseline / default) |
| `interpretation` | AC employees in position DEPARTMENT_HEAD (kontorchef) have a higher flex balance ceiling (200h vs base 150h) and a 4-week norm period (vs base 1-week) reflecting management-level flexibility. Other fields inherit from base AC OK24 config (weekly norm 37, flex carryover 150). Cross-ref: SR-AC-OK24-011 (base MaxFlexBalance). |
| `confidence_level` | MEDIUM (the 200h cap + 4-week norm convention is established for department-head-level state employees but may vary by institution; Phase B should confirm centralized vs local-only encoding) |
| `interpretation_authority` | Personalestyrelsen (baseline) / negotiated (local-agreement variations) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Glocal cell — central baseline at agreement level; institutional override permitted via `local_configurations`. **Role-distinction cross-ref**: DEPARTMENT_HEAD here is the position registry entry (S11 Option C); the role-within-agreement modeling for AC stratification (fuldmægtig / specialkonsulent / chefkonsulent) is a SEPARATE concern that ADR-024 D1 (S38) will adjudicate. DEPARTMENT_HEAD covers `kontorchef` specifically, not the broader specialkonsulent/chefkonsulent strata. See `role-dimension-audit.md` (TASK-3606). |

### SR-AC-OK24-039 — position_override_configs AC OK24 RESEARCHER

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK24-039 |
| `agreement_code` | AC |
| `ok_version` | OK24 |
| `field` | `position_override_configs (AC, OK24, RESEARCHER).{max_flex_balance, norm_period_weeks, flex_carryover_max?, weekly_norm_hours?}` |
| `current_encoded_value` | `max_flex_balance = NULL (inherits 150 from base); norm_period_weeks = 4; flex_carryover_max = NULL; weekly_norm_hours = NULL` |
| `authoritative_source` | pending (Phase B — research-staff multi-week norm period is established via AC overenskomst's research/teaching provisions; AC_RESEARCH agreement code provides the broader annual-activity model in S11) |
| `interpretation` | AC employees in position RESEARCHER (forsker — when carried under base AC, not AC_RESEARCH agreement) have a 4-week norm period (vs base 1-week) reflecting the project-driven nature of research work. All other fields inherit from base. **Distinct from AC_RESEARCH agreement code** which uses `NormModel = ANNUAL_ACTIVITY` with 1924h annual target — that's a fundamentally different norm model. The AC + RESEARCHER position override here applies when a researcher is contractually on AC but their work pattern needs the multi-week norm flexibility. |
| `confidence_level` | MEDIUM (research-staff multi-week norm is established AC overenskomst feature; cirkulær-paragraph cite needed; the position-vs-agreement-code distinction is project-internal and Phase B should confirm whether this distinction is meaningful) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-ref: AC_RESEARCH agreement code uses ANNUAL_ACTIVITY norm model (see SR-AC_RESEARCH-OK24-NNN in TASK-3605). The two are distinct paths to "researcher flexibility": (a) AC + RESEARCHER position-override → 4-week norm with same 37h weekly target; (b) AC_RESEARCH agreement → annual norm with 1924h target. Phase B should clarify the intended use-case split. |

---

## AC OK26 Cells (placeholder bundles — 4 rows)

AC OK26 is a placeholder in `CentralAgreementConfigs.cs:100` — the comment reads "OK26 (placeholder — identical to OK24 for now)". The OK26 cirkulær is under negotiation between Personalestyrelsen and Akademikerne / unions; final values will land when the cirkulær publishes. Until then, the seed mirrors OK24 cell-by-cell. The placeholder bundles below cover every AC OK26 cell with a single register row per data domain. Phase B priority: re-verify when OK26 cirkulær lands; supersession events will track divergence.

### SR-AC-OK26-001 — AC OK26 agreement_configs placeholder bundle

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK26-001 |
| `agreement_code` | AC |
| `ok_version` | OK26 |
| `field` | All AC OK26 cells in `agreement_configs` (37 columns; mirrors AC OK24 cell-by-cell) |
| `current_encoded_value` | Identical to AC OK24 (see SR-AC-OK24-001 through SR-AC-OK24-030 + SR-AC-OK24-021..030). `agreement_configs` row at init.sql:1140 has byte-identical column values to L1134 (AC OK24). |
| `authoritative_source` | pending — OK26 cirkulær under finalization between Personalestyrelsen + Akademikerne. Code comment at `CentralAgreementConfigs.cs:100`: "OK26 (placeholder — identical to OK24 for now)". For now, source-of-truth = AC OK24 cells (SR-AC-OK24-001..030) by inheritance. |
| `interpretation` | AC OK26 currently inherits every cell from AC OK24 as a placeholder. When the OK26 cirkulær publishes, individual cells WILL be re-verified; cells that diverge from OK24 will receive their own SR-AC-OK26-NNN rows and a `supersession_history` entry on the divergent cell; cells that confirm identical to OK24 stay under this bundle row with `last_verified_by` + `decision_date` populated. |
| `confidence_level` | LOW (placeholder; awaiting OK26 cirkulær publication) |
| `interpretation_authority` | Personalestyrelsen (anticipated) |
| `last_verified_by` | pending — OK26 cirkulær publication required |
| `decision_date` | pending |
| `supersession_history` | (none — OK24 → OK26 transition supersession not yet triggered; the seed-time copy is the placeholder, not a supersession event) |
| `bug_correction_history` | (none — placeholder inherits AC OK24's `bug_correction_history` for shared cells; SR-AC-OK24-005's AC=AFSPADSERING correction applies to OK26 by inheritance, see init.sql:1140's `default_compensation_model = 'AFSPADSERING'`) |
| `disputed?` | false |
| `notes` | **Phase B priority**: when OK26 cirkulær publishes, dispatch a Phase A re-audit (a recurring item per S41 TASK-4107 OK-version transition checklist). Each AC OK26 cell needs explicit confirmation: identical-to-OK24 stays under this bundle; divergent cells get individual SR-AC-OK26-NNN rows. **Bug-correction inheritance**: the S35 AC=AFSPADSERING correction propagated to OK26 in the same commit (`cbaea7d`) — the bug-with-no-past-impact classification applies uniformly because no past OK26 periods exist either (OK26 is forward-only). Cells with HIGH confidence on OK24→OK26 invariance (EU-derived: MinimumRestHours, MaxDailyHours, WeeklyMaxHoursReferencePeriod): even if OK26 cirkulær is silent, these EU-floor cells cannot diverge below SR-AC-OK24-003 / -018 / -004. |

### SR-AC-OK26-002 — AC OK26 entitlement_configs placeholder bundle

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK26-002 |
| `agreement_code` | AC |
| `ok_version` | OK26 |
| `field` | All AC OK26 cells in `entitlement_configs` (5 entitlement types × ~6 sub-fields each = ~30 sub-cells; mirrors AC OK24 entitlements cell-by-cell) |
| `current_encoded_value` | Identical to AC OK24 (see SR-AC-OK24-013..016 + SR-AC-OK24-031..036). Rows at init.sql:1346 (VACATION), 1353 (SPECIAL_HOLIDAY), 1360 (CARE_DAY), 1367 (CHILD_SICK), 1374 (SENIOR_DAY) all byte-identical to OK24 counterparts. |
| `authoritative_source` | pending — Ferieloven (LBK nr 230 af 12/02/2021) applies to VACATION across OK versions (statutory); overenskomst-driven entitlements (CARE_DAY, CHILD_SICK, SENIOR_DAY, SPECIAL_HOLIDAY) await OK26 cirkulær |
| `interpretation` | AC OK26 entitlements currently inherit AC OK24 row-by-row. Phase B verification per entitlement when OK26 cirkulær publishes. |
| `confidence_level` | HIGH for VACATION (Ferieloven is statutory and not OK-version-specific); MEDIUM-LOW for overenskomst-driven entitlements (CARE_DAY, CHILD_SICK, SENIOR_DAY, SPECIAL_HOLIDAY) until OK26 cirkulær confirms |
| `interpretation_authority` | Folketinget (VACATION) / Personalestyrelsen + Akademikerne (others, anticipated) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none — but SR-AC-OK24-015 SENIOR_DAY candidate-bug applies by inheritance: if Phase B confirms the `annual_quota = 0` + `min_age = 60` encoding is incomplete, the fix propagates to OK26 in the same correction event) |
| `disputed?` | false |
| `notes` | VACATION's statutory basis (Ferieloven) is OK-invariant; that cell can flip to HIGH confidence in OK26 with the same Ferieloven cite. Other entitlements need OK26-specific cirkulær cite. |

### SR-AC-OK26-003 — AC OK26 wage_type_mappings placeholder bundle

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK26-003 |
| `agreement_code` | AC |
| `ok_version` | OK26 |
| `field` | All AC OK26 cells in `wage_type_mappings` (17 mappings + 1 NORM_DEVIATION; mirrors AC OK24 mappings) |
| `current_encoded_value` | Identical to AC OK24 (see SR-AC-OK24-037 bundle). Rows at init.sql:290 (NORMAL_HOURS), 297 (MERARBEJDE), 306 (VACATION) and continuing through L1201 (NORM_DEVIATION OK26 AC). |
| `authoritative_source` | pending — SLS wage type codes are Personalestyrelsen-administered and typically stable across OK transitions; OK26 cirkulær may introduce new wage types (e.g., for new compensation categories) that would require new mappings |
| `interpretation` | AC OK26 wage type mappings mirror AC OK24. SLS codes stable across OK24 → OK26 transition. New mappings (if any) will be added as Phase B identifies them from the OK26 cirkulær. |
| `confidence_level` | HIGH for the existing mappings (SLS codes are stable infrastructure; the inheritance is correct); LOW for "completeness" (OK26 may introduce new time types requiring new mappings that don't exist in the OK24 set) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Wage type mapping completeness is a separate concern from individual mapping correctness. The OK26 placeholder inherits the OK24 set; if OK26 introduces new time types, additional rows land via S40 cutover. |

### SR-AC-OK26-004 — AC OK26 position_override_configs placeholder bundle

| Field | Value |
|-------|-------|
| `row_id` | SR-AC-OK26-004 |
| `agreement_code` | AC |
| `ok_version` | OK26 |
| `field` | All AC OK26 cells in `position_override_configs` (DEPARTMENT_HEAD + RESEARCHER; mirrors AC OK24 overrides) |
| `current_encoded_value` | Identical to AC OK24 (see SR-AC-OK24-038 + SR-AC-OK24-039). Rows at init.sql:1259 (DEPARTMENT_HEAD OK26) + L1261 (RESEARCHER OK26). |
| `authoritative_source` | pending — same as AC OK24 position overrides; Phase B should confirm DEPARTMENT_HEAD's 200h flex cap + 4-week norm carries through to OK26 |
| `interpretation` | AC OK26 position overrides mirror AC OK24. DEPARTMENT_HEAD = 200h flex + 4-week norm; RESEARCHER = 4-week norm. |
| `confidence_level` | MEDIUM (carries forward AC OK24 overrides' MEDIUM confidence; Phase B should confirm OK26 cirkulær doesn't introduce new senior-role-specific overrides) |
| `interpretation_authority` | Personalestyrelsen / negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Role-distinction cross-ref: ADR-024 D1 (S38) may extend or replace the position override mechanism for role-within-agreement modeling. Until then, OK26 mirrors OK24's two position overrides. |

---

## HK OK24 Cells

HK = Handels- og Kontorfunktionærer i Staten. Distinct cirkulær from AC; substantially different compensation model. HK uses the standard overtime regime (`HasOvertime = true`, `HasMerarbejde = false` — inverting AC), all hourly supplements enabled, on-call + call-in active. This inverts the AC pattern across ~12 cells; each load-bearing inversion gets its own row.

**HK cirkulær source**: Personalestyrelsen / Medst administers the HK overenskomst per state-sector convention; specific PDF URL pending Phase B verification. HK union counterpart published at hk.dk/raadgivning/overenskomst/stat. Cells with HK-specific values that mirror established state-sector convention carry MEDIUM confidence pending paragraph cite.

### SR-HK-OK24-001 — WeeklyNormHours + NormModel + NormPeriodWeeks + AnnualNormHours (compound)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-001 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `WeeklyNormHours + NormModel + NormPeriodWeeks + AnnualNormHours` |
| `current_encoded_value` | `37.0 / "WEEKLY_HOURS" / 1 / 1924` |
| `authoritative_source` | https://oes.dk/media/ik0hm2lr/043-19.pdf §2 (the 37h weekly norm is universal state-sector convention; the same Aftale om arbejdstid governs); HK cirkulær mirrors |
| `interpretation` | HK weekly norm = 37h, standard WEEKLY_HOURS model with 1-week norm period. Same as AC OK24 (SR-AC-OK24-001, SR-AC-OK24-020) — universal state-sector norm. |
| `confidence_level` | HIGH (universal convention) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Compound row covering 4 columns. Same values as AC; semantics identical (standard WEEKLY_HOURS norm). |

### SR-HK-OK24-002 — MaxFlexBalance (DIVERGENT from AC)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-002 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `MaxFlexBalance` |
| `current_encoded_value` | `100.0` |
| `authoritative_source` | pending (Phase B — HK overenskomst flex ceiling; PROSA = 120h, AC = 150h, HK = 100h hierarchy) |
| `interpretation` | Maximum positive flex balance for HK employees = 100 hours. Lower than AC (150h) and PROSA (120h), reflecting HK's tighter overtime-regime balance handling. Excess hours convert to OVERTIME_50 / OVERTIME_100 events directly per `HasOvertime = true` (see SR-HK-OK24-004). |
| `confidence_level` | MEDIUM (the 100h baseline is well-established in project history; glocal cell — institutional override permitted via `local_configurations` per ADR-017) |
| `interpretation_authority` | Personalestyrelsen / negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-011 = 150h). The lower ceiling reflects HK's overtime-regime: balance growth has a direct payroll-export path (OVERTIME_50/100), so a tighter cap reduces accumulation pressure. Glocal cell. |

### SR-HK-OK24-003 — FlexCarryoverMax (DIVERGENT from AC)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-003 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `FlexCarryoverMax` |
| `current_encoded_value` | `100.0` |
| `authoritative_source` | pending (Phase B — paired with MaxFlexBalance) |
| `interpretation` | HK flex carryover ceiling = 100h, equal to MaxFlexBalance (full carryover, no year-boundary truncation). |
| `confidence_level` | MEDIUM (same rationale as SR-HK-OK24-002) |
| `interpretation_authority` | Personalestyrelsen / negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-012 = 150h). Co-located with SR-HK-OK24-002. Glocal cell. |

### SR-HK-OK24-004 — HasOvertime (DIVERGENT from AC)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-004 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `HasOvertime` |
| `current_encoded_value` | `true` |
| `authoritative_source` | pending (Phase B — HK overenskomst establishes the overtime regime with 50% / 100% supplement tiers) |
| `interpretation` | HK employees ARE subject to the standard overtime regime. Hours beyond `OvertimeThreshold50` (37h/week) trigger 50% supplement; hours beyond `OvertimeThreshold100` (40h/week) trigger 100%. Distinct from AC's merarbejde regime (where excess hours route through afspadsering / udbetaling). |
| `confidence_level` | HIGH (HK's overtime-regime is well-established cirkulær framework; the boolean inversion AC↔HK is project-internal but reflects the underlying cirkulær distinction cleanly) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-021 = false). Mutually exclusive with `HasMerarbejde` in project convention. Renders SR-HK-OK24-006 (OvertimeThreshold50) and SR-HK-OK24-007 (OvertimeThreshold100) load-bearing for HK (vs inert for AC). |

### SR-HK-OK24-005 — HasMerarbejde (DIVERGENT from AC)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-005 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `HasMerarbejde` |
| `current_encoded_value` | `false` |
| `authoritative_source` | pending (Phase B — HK overenskomst silent on merarbejde; HK uses overtime instead) |
| `interpretation` | HK employees are NOT subject to merarbejde. Excess hours route through OVERTIME_50 / OVERTIME_100 events, not MERARBEJDE. |
| `confidence_level` | HIGH (HK's no-merarbejde stance is the inverse of AC's merarbejde-only stance; well-established) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-007 = true). Mutually exclusive with HasOvertime per project convention. |

### SR-HK-OK24-006 — OvertimeThreshold50 (LOAD-BEARING for HK)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-006 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `OvertimeThreshold50` |
| `current_encoded_value` | `37.0` |
| `authoritative_source` | pending (Phase B — HK cirkulær establishes the 50% supplement starts at the weekly norm boundary; matches OK-published overtime tiers) |
| `interpretation` | Hours per week beyond 37 (the weekly norm) trigger 50% supplement (OVERTIME_50 → SLS_0210). Standard state-sector overtime tier. |
| `confidence_level` | MEDIUM (37h-as-trigger is universal; specific cirkulær paragraph pending) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK** (inert in AC per SR-AC-OK24-022). Same value across HK / PROSA (both have `HasOvertime = true`). Used by OvertimeRule. |

### SR-HK-OK24-007 — OvertimeThreshold100 (LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-007 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `OvertimeThreshold100` |
| `current_encoded_value` | `40.0` |
| `authoritative_source` | pending (Phase B — HK cirkulær on 100% supplement tier) |
| `interpretation` | Hours per week beyond 40 trigger 100% supplement (OVERTIME_100 → SLS_0220). |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK**. Same value across HK / PROSA. |

### SR-HK-OK24-008 — Supplement-enablement quad (compound, 4 boolean flags ON)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-008 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `EveningSupplementEnabled + NightSupplementEnabled + WeekendSupplementEnabled + HolidaySupplementEnabled` (compound — 4 columns) |
| `current_encoded_value` | `true / true / true / true` |
| `authoritative_source` | pending (Phase B — HK cirkulær establishes the 4 supplement categories with rates) |
| `interpretation` | All 4 hourly supplements enabled for HK. Activates supplement rates (SR-HK-OK24-010..014) and time windows (SR-HK-OK24-009). Each enabled flag is a load-bearing decision: supplement events emit at payroll mapping time when the time entry falls within the configured window. |
| `confidence_level` | HIGH for the 4 flags-all-true semantic (HK gets supplements is universal state-sector knowledge; cirkulær specifies rates which are listed in SR-HK-OK24-010..014) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-024 = all-false bundle). Compound row covering 4 columns. The flags-all-true pattern repeats for PROSA. Inverts AC's all-inert supplement structure. |

### SR-HK-OK24-009 — Supplement time windows (compound, 4 columns)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-009 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `EveningStart + EveningEnd + NightStart + NightEnd` (compound — 4 columns) |
| `current_encoded_value` | `17 / 23 / 23 / 6` |
| `authoritative_source` | pending (Phase B — HK cirkulær specifies evening window 17–23 + night window 23–06) |
| `interpretation` | Evening supplement applies to hours worked between 17:00–23:00. Night supplement applies to hours between 23:00–06:00 (crosses midnight). Standard state-sector hour boundaries. |
| `confidence_level` | HIGH (17–23 + 23–06 is universal Danish convention for evening / night supplement boundaries) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK** (inert in AC per SR-AC-OK24-024). The hour-of-day boundaries are encoded as integers; the cross-midnight night window (23–06) is handled correctly by the supplement rule. Used by EveningSupplementRule + NightSupplementRule. |

### SR-HK-OK24-010 — EveningRate (LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-010 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `EveningRate` |
| `current_encoded_value` | `1.25` |
| `authoritative_source` | pending (Phase B — HK cirkulær on evening supplement rate; 25% supplement is established state-sector standard) |
| `interpretation` | Evening supplement = 25% on top of standard hourly wage for hours worked between 17:00–23:00. Encoded as `1.25` multiplier. Emits EVENING_SUPPLEMENT → SLS_0410 event at payroll mapping. |
| `confidence_level` | MEDIUM (25% evening supplement is standard convention; cirkulær paragraph cite pending) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK** (inert in AC per SR-AC-OK24-009). Same value across HK / PROSA. |

### SR-HK-OK24-011 — NightRate (LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-011 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `NightRate` |
| `current_encoded_value` | `1.50` |
| `authoritative_source` | pending (Phase B — HK cirkulær on night supplement; 50% supplement) |
| `interpretation` | Night supplement = 50% supplement for hours worked between 23:00–06:00. Emits NIGHT_SUPPLEMENT → SLS_0420. |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK**. |

### SR-HK-OK24-012 — WeekendSaturdayRate (LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-012 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `WeekendSaturdayRate` |
| `current_encoded_value` | `1.50` |
| `authoritative_source` | pending (Phase B — Saturday supplement = 50% per state-sector convention) |
| `interpretation` | Saturday supplement = 50% for hours worked on Saturday. Emits WEEKEND_SUPPLEMENT → SLS_0430. |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK**. Note: the Saturday + Sunday rates are encoded as separate columns (Saturday = 1.50, Sunday = 2.0). Both emit the same wage type (SLS_0430) but the rule applies the day-specific multiplier. |

### SR-HK-OK24-013 — WeekendSundayRate (LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-013 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `WeekendSundayRate` |
| `current_encoded_value` | `2.0` |
| `authoritative_source` | pending (Phase B — Sunday supplement = 100% per state-sector convention) |
| `interpretation` | Sunday supplement = 100% (double) for hours worked on Sunday. Emits WEEKEND_SUPPLEMENT → SLS_0430. |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK**. The Sunday-higher-than-Saturday pattern is universal state-sector convention. |

### SR-HK-OK24-014 — HolidayRate (LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-014 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `HolidayRate` |
| `current_encoded_value` | `2.0` |
| `authoritative_source` | pending (Phase B — public-holiday supplement = 100%, matching Sunday rate) |
| `interpretation` | Public-holiday supplement = 100% for hours worked on public holidays. Emits HOLIDAY_SUPPLEMENT → SLS_0440. |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK**. Public-holiday list is project-internal (Danish public holidays — påske, pinse, jul, nytår, etc.); Phase B should confirm the public-holiday set is comprehensive. |

### SR-HK-OK24-015 — OnCallDutyEnabled (DIVERGENT from AC)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-015 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `OnCallDutyEnabled` |
| `current_encoded_value` | `true` |
| `authoritative_source` | pending (Phase B — HK on-call (rådighedsvagt) is established cirkulær feature for HK roles with on-call obligations) |
| `interpretation` | HK employees can be assigned on-call duty with compensation per `OnCallDutyRate` (33%). Activates the on-call rule path. |
| `confidence_level` | MEDIUM (HK's on-call enablement at agreement level is well-established; cirkulær paragraph pending) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-025 = false). Activates SR-HK-OK24-016 (OnCallDutyRate) as load-bearing. |

### SR-HK-OK24-016 — OnCallDutyRate (LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-016 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `OnCallDutyRate` |
| `current_encoded_value` | `0.33` |
| `authoritative_source` | pending (Phase B — 33% of standard hourly wage per on-call hour is established state-sector convention) |
| `interpretation` | On-call compensation = 33% of standard hourly wage per on-call hour (not full working-time accrual; on-call is paid waiting). Emits ON_CALL_DUTY → SLS_0710. |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **LOAD-BEARING in HK** (inert in AC per SR-AC-OK24-026). |

### SR-HK-OK24-017 — CallInWorkEnabled + CallInMinimumHours + CallInRate (compound, LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-017 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `CallInWorkEnabled + CallInMinimumHours + CallInRate` (compound — 3 columns) |
| `current_encoded_value` | `true / 3.0 / 1.0` |
| `authoritative_source` | pending (Phase B — HK call-in (tilkald) cirkulær establishes the 3-hour minimum guarantee) |
| `interpretation` | HK employees called in outside ordinary hours receive guaranteed compensation for a minimum of 3 hours (even if the actual call lasts less), at standard hourly rate (1.0 multiplier — supplements apply on top via the standard supplement rules). Emits CALL_IN_WORK → SLS_0810. |
| `confidence_level` | MEDIUM (3-hour minimum guarantee is standard state-sector convention; cirkulær paragraph pending) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-027 = `false / 3.0 / 1.0` with the boolean flipped). Compound row covering 3 columns. The 3.0-hour minimum becomes load-bearing in HK because the rule fires; the rate of 1.0 means base hourly rate (any supplements stack on top). |

### SR-HK-OK24-018 — Travel cluster (compound, 3 columns matching AC)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-018 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `TravelTimeEnabled + WorkingTravelRate + NonWorkingTravelRate` (compound — 3 columns; same values as AC counterparts SR-AC-OK24-028..030) |
| `current_encoded_value` | `true / 1.0 / 0.5` |
| `authoritative_source` | pending (Phase B — universal state-sector convention for travel time) |
| `interpretation` | HK employees on official travel receive travel-time compensation: in-hours full rate (1.0×), out-of-hours half rate (0.5×). Identical to AC SR-AC-OK24-028..030. |
| `confidence_level` | MEDIUM (same as AC; universal state-sector convention) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same values + load-bearing semantics as AC. Compound row collapses the 3 cells because there's no divergence from AC. |

### SR-HK-OK24-019 — RestPeriodDerogationAllowed (DIVERGENT from AC)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-019 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `RestPeriodDerogationAllowed` |
| `current_encoded_value` | `true` |
| `authoritative_source` | EU WTD 2003/88/EC Article 17 (derogation permitted under specific worker categories) + HK cirkulær (HK roles with on-call obligations get derogation with compensatory rest) |
| `interpretation` | HK employees MAY derogate from the 11-hour minimum daily rest under specific operational circumstances (most commonly: on-call disruption). Compensatory rest must be granted per `compensatory_rest` table (S16). The derogation is the EU WTD Article 17 exception, NOT a waiver of the rest requirement. |
| `confidence_level` | HIGH (EU directive + HK overenskomst alignment well-established; HK's on-call enablement makes derogation operationally necessary) |
| `interpretation_authority` | EU + Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-017 = false). Compensatory-rest tracking (S16 `compensatory_rest` table) is mandatory when this flag is `true` + a derogation actually occurs. |

### SR-HK-OK24-020 — EmployeeCompensationChoice (DIVERGENT from AC, LOAD-BEARING)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-020 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `EmployeeCompensationChoice` |
| `current_encoded_value` | `true` |
| `authoritative_source` | pending (Phase B — HK cirkulær establishes employee right to choose between afspadsering and udbetaling for overtime compensation within agreement rules) |
| `interpretation` | HK employees CAN choose between afspadsering (time-off-in-lieu) and udbetaling (payment) for overtime compensation, subject to rules in the cirkulær (e.g., budget approval for high udbetaling rates). The choice is employee-initiated; employer may not override absent contractual basis. |
| `confidence_level` | MEDIUM (HK employee-choice semantic is well-established; cirkulær paragraph pending) |
| `interpretation_authority` | Personalestyrelsen / negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-006 = false). Used by S17 OvertimeGovernanceRule + payroll compensation-model mapping. The Overtime D-test `Overtime_PastPeriodBalance_UsesPeriodEffectiveAgreementCode_NotLive` (S35 TASK-3508) discriminates on this field's HK=true vs AC=false. |

### SR-HK-OK24-021 — DefaultCompensationModel

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-021 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `DefaultCompensationModel` |
| `current_encoded_value` | `"AFSPADSERING"` |
| `authoritative_source` | pending (Phase B — HK cirkulær on default compensation model; AFSPADSERING is the established default state-sector across all 3 base agreements) |
| `interpretation` | HK default compensation = afspadsering. Employee may elect udbetaling per `EmployeeCompensationChoice = true` (SR-HK-OK24-020). The default applies when no employee election is recorded. |
| `confidence_level` | MEDIUM (default-afspadsering is universal state-sector convention; cirkulær paragraph pending) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none — HK seed was already correctly `AFSPADSERING` pre-S35; the S35 TASK-3503 bug correction applied only to AC family) |
| `disputed?` | false |
| `notes` | Same value as AC SR-AC-OK24-005 + PROSA equivalent. The HK / PROSA / AC family difference is `EmployeeCompensationChoice` (HK / PROSA = true; AC = false), not `DefaultCompensationModel`. |

### SR-HK-OK24-022 — OvertimeRequiresPreApproval (candidate bug)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-022 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `OvertimeRequiresPreApproval` |
| `current_encoded_value` | `false` |
| `authoritative_source` | pending (Phase B PRIORITY — HK overtime is the load-bearing overtime regime; the cirkulær may require leader pre-approval for non-emergency overtime; current `false` may be incorrect) |
| `interpretation` | Pre-approval workflow gate. `false` means HK overtime can be entered without prior manager pre-approval; the S17 overtime governance rule will emit warnings but not block. Question for Phase B: does HK overenskomst require pre-approval for non-emergency overtime? If yes, current encoding inverts the rule. |
| `confidence_level` | LOW (encoding may be incorrect; Phase B priority) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none — flagged candidate, not yet corrected) |
| `disputed?` | false |
| `notes` | **CANDIDATE BUG**. Current value `false` for HK matches AC's `false` (where pre-approval is moot because AC has merarbejde-routing). For HK with real overtime, pre-approval requirement IS a governance question. The seed default at init.sql column DEFAULT = `FALSE` carried through without explicit per-agreement consideration in S17. Phase B priority for HIGH-impact resolution: if HK requires pre-approval, this is a bug-with-no-past-impact correction (per ROADMAP rule correction policy); if not, flag stays as documented MEDIUM-confidence. |

### SR-HK-OK24-023 — EU-derived compliance cluster (compound, 4 columns)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-023 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `MinimumRestHours + MaxDailyHours + WeeklyMaxHoursReferencePeriod + VoluntaryUnsocialHoursAllowed` (compound — 4 columns; EU-derived + governance) |
| `current_encoded_value` | `11.0 / 13.0 / 17 / true` |
| `authoritative_source` | EU WTD 2003/88/EC Articles 3, 6, 16 + Danish transposition (Lov om arbejdstid). Same EU floor as AC. |
| `interpretation` | EU-mandated working time compliance values. Identical across all 5 base agreements + variants (EU floor). HK's `VoluntaryUnsocialHoursAllowed = true` is consistent with HK's enabled-supplement model (voluntary unsocial hours emit supplements normally). |
| `confidence_level` | HIGH for the 3 EU-derived cells (rest, max-daily, ref-period); MEDIUM for VoluntaryUnsocialHoursAllowed (system-design gate) |
| `interpretation_authority` | EU (3 cells) + negotiated (1 cell) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same values as AC counterparts (SR-AC-OK24-003 + 004 + 018 + 019). Compound row collapses across cells matching AC. |

### SR-HK-OK24-024 — MaxOvertimeHoursPerPeriod

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-024 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `MaxOvertimeHoursPerPeriod` |
| `current_encoded_value` | `0` |
| `authoritative_source` | pending (Phase B — HK cirkulær on overtime caps; the sentinel `0` matches AC SR-AC-OK24-002 "no cap" convention) |
| `interpretation` | Sentinel `0` = no fixed cap on overtime hours per period for HK. The S17 OvertimeGovernanceRule still emits warnings but does not hard-cap. |
| `confidence_level` | MEDIUM (sentinel-zero convention same as AC; HK-specific cirkulær cite needed) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same sentinel-zero convention as AC SR-AC-OK24-002. **Phase B should confirm whether HK overenskomst specifies a hard cap** (e.g., "no more than 200 overtime hours per quarter") that should be encoded here instead of `0`. |

### SR-HK-OK24-025 — entitlement_configs.VACATION (compound, all sub-fields)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-025 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.VACATION.{annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` |
| `current_encoded_value` | `25 / IMMEDIATE / 9 / 5 / true / false` |
| `authoritative_source` | Ferieloven (LBK nr 230 af 12/02/2021) §8 + §15 |
| `interpretation` | Same as AC: 25 vacation days, IMMEDIATE accrual, ferieår resets September 1, up to 5 days carryover, pro-rated by part-time fraction. Universal Danish statutory minimum. |
| `confidence_level` | HIGH (Ferieloven explicit) |
| `interpretation_authority` | Folketinget (statutory) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-table reference. Identical to AC SR-AC-OK24-013 + 032 (Ferieloven is OK-version-invariant). |

### SR-HK-OK24-026 — entitlement_configs.SPECIAL_HOLIDAY (compound)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-026 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.SPECIAL_HOLIDAY.{annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` |
| `current_encoded_value` | `5 / IMMEDIATE / 9 / 0 / true / false` |
| `authoritative_source` | pending (Phase B — same as AC SR-AC-OK24-031 + 036) |
| `interpretation` | 5 særlige feriedage, IMMEDIATE accrual, ferieår-aligned (September), no carryover, pro-rated. Same as AC SR-AC-OK24-031 + 036. |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen / HK union |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-table. Same shape as AC. |

### SR-HK-OK24-027 — entitlement_configs.CARE_DAY (compound)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-027 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.CARE_DAY.{annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` |
| `current_encoded_value` | `2 / IMMEDIATE / 1 / 0 / false / false` |
| `authoritative_source` | pending (Phase B — same as AC SR-AC-OK24-014 + 033) |
| `interpretation` | 2 omsorgsdage per calendar year. Not pro-rated. Same as AC. |
| `confidence_level` | MEDIUM |
| `interpretation_authority` | Personalestyrelsen / HK union |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Cross-table. Same shape as AC. |

### SR-HK-OK24-028 — entitlement_configs.CHILD_SICK (compound, DIVERGENT quota)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-028 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.CHILD_SICK.{annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode}` |
| `current_encoded_value` | `2 / IMMEDIATE / 1 / 0 / false / true` |
| `authoritative_source` | pending (Phase B — HK cirkulær on barn-syg quota; 2 days per episode is HK-specific value) |
| `interpretation` | 2 days per episode (per-episode semantic). Each child-illness episode grants 2 days; no annual cumulative limit. **DIVERGENT from AC** (1 day) and PROSA (3 days). |
| `confidence_level` | MEDIUM (per-episode semantic universal; HK-specific 2-day quota is the project encoding — Phase B confirms) |
| `interpretation_authority` | Personalestyrelsen / HK union (negotiated) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **DIVERGENT from AC** (SR-AC-OK24-016 = 1 day). The AC=1 / HK=2 / PROSA=3 progression matches established convention; Phase B should confirm cirkulær-paragraph. |

### SR-HK-OK24-029 — entitlement_configs.SENIOR_DAY (compound, inherits paired bug candidate)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-029 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `entitlement_configs.SENIOR_DAY.{annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode, min_age}` |
| `current_encoded_value` | `0 / IMMEDIATE / 1 / 0 / false / false / 60` |
| `authoritative_source` | pending (Phase B PRIORITY — same paired-bug-candidate as AC SR-AC-OK24-015 + 035) |
| `interpretation` | Same encoding as AC: `annual_quota = 0` with `min_age = 60`. As encoded, no senior days grant regardless of age. **Bug candidate** — likely incomplete encoding (rule should override with age-banded lookup) or vestigial `min_age` field. |
| `confidence_level` | LOW (same as AC paired finding) |
| `interpretation_authority` | negotiated |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **CANDIDATE BUG** — inherits paired finding from SR-AC-OK24-015 + 035. Same encoding across AC / HK / PROSA / AC_RESEARCH / AC_TEACHING — bug correction (if classified) applies uniformly to all 5 agreements per ROADMAP no-per-institution-opt-in policy. |

### SR-HK-OK24-030 — wage_type_mappings HK OK24 bundle (compound, ~17 mappings including supplements + overtime)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-030 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `wage_type_mappings (time_type, wage_type) for (HK, OK24, position='')` — bundle of HK OK24 mappings (init.sql:218–285) |
| `current_encoded_value` | `NORMAL_HOURS → SLS_0110; OVERTIME_50 → SLS_0210; OVERTIME_100 → SLS_0220; EVENING_SUPPLEMENT → SLS_0410; NIGHT_SUPPLEMENT → SLS_0420; WEEKEND_SUPPLEMENT → SLS_0430; HOLIDAY_SUPPLEMENT → SLS_0440; VACATION → SLS_0510; CARE_DAY → SLS_0520; CHILD_SICK_DAY → SLS_0530; CHILD_SICK_DAY_2 → SLS_0531; CHILD_SICK_DAY_3 → SLS_0532; PARENTAL_LEAVE → SLS_0540; SENIOR_DAY → SLS_0550; LEAVE_WITHOUT_PAY → SLS_0560; LEAVE_WITH_PAY → SLS_0565; SPECIAL_HOLIDAY_ALLOWANCE → SLS_0570; FLEX_PAYOUT → SLS_0610; ON_CALL_DUTY → SLS_0710; CALL_IN_WORK → SLS_0810; TRAVEL_WORK → SLS_0820; TRAVEL_NON_WORK → SLS_0830` |
| `authoritative_source` | SLS technical documentation (Personalestyrelsen — pending Phase B reference). |
| `interpretation` | HK OK24 wage type mappings cover normal hours, BOTH overtime tiers, ALL 4 supplements, on-call, call-in, travel, all absences, flex payout. The supplement + overtime mappings distinguish HK from AC (which has no overtime + no supplements). **No MERARBEJDE mapping for HK** (correctly omitted because `HasMerarbejde = false`). |
| `confidence_level` | HIGH for AC-pinned-equivalent mappings (NORMAL_HOURS, absences, travel, flex); MEDIUM for HK-distinct mappings (OVERTIME_50/100, 4 supplements) pending SLS code verification |
| `interpretation_authority` | Personalestyrelsen (SLS technical authority) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Compound row covering ~22 mappings**. Differs from AC SR-AC-OK24-037 by adding 2 overtime + 4 supplement mappings + omitting MERARBEJDE + NORM_DEVIATION (NORM_DEVIATION is AC-family-only per init.sql:1197). |

### SR-HK-OK24-031 — position_override_configs HK OK24 — none (explicit absence)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK24-031 |
| `agreement_code` | HK |
| `ok_version` | OK24 |
| `field` | `position_override_configs (HK, OK24, *)` — explicit absence row |
| `current_encoded_value` | `(no rows in init.sql; HK has no position overrides at seed time)` |
| `authoritative_source` | n/a — explicit-absence row |
| `interpretation` | HK has NO position overrides at seed time. Contrast AC which has 2 (DEPARTMENT_HEAD + RESEARCHER per SR-AC-OK24-038 + 039). Within-HK role distinctions (if any) are not currently encoded; any such encoding would land via Phase B + S38 ADR-024 D1 (role-within-agreement modeling). |
| `confidence_level` | HIGH for "no current overrides" (init.sql:1257-1262 explicitly enumerates only AC overrides); LOW for whether this is correct (Phase B may identify HK roles needing overrides) |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | **Explicit-absence row pattern**. Used when an agreement has no rows in a data domain to document "this is intentional + verified", distinguishing from "this is missing by oversight". Phase B should confirm. Cross-ref: `role-dimension-audit.md` (TASK-3606) will enumerate within-HK strata if any exist. |

---

## HK OK26 Cells (placeholder bundles — 4 rows)

HK OK26 mirrors HK OK24 per `CentralAgreementConfigs.cs:123` ("HK OK26 (placeholder)") + init.sql:1152. Pattern matches AC OK26 (placeholder bundles per data domain).

### SR-HK-OK26-001 — HK OK26 agreement_configs placeholder bundle

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK26-001 |
| `agreement_code` | HK |
| `ok_version` | OK26 |
| `field` | All HK OK26 cells in `agreement_configs` (~37 columns; mirrors HK OK24 cell-by-cell) |
| `current_encoded_value` | Identical to HK OK24 (see SR-HK-OK24-001 through SR-HK-OK24-024). init.sql:1152 is byte-identical to L1146 (HK OK24). |
| `authoritative_source` | pending — OK26 cirkulær between Personalestyrelsen + HK union under finalization. |
| `interpretation` | HK OK26 currently inherits HK OK24 cell-by-cell as placeholder. Phase B verification when OK26 cirkulær publishes. |
| `confidence_level` | LOW (placeholder) |
| `interpretation_authority` | Personalestyrelsen (anticipated) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none — OvertimeRequiresPreApproval candidate bug from SR-HK-OK24-022 inherits to OK26; if confirmed bug, correction propagates) |
| `disputed?` | false |
| `notes` | Phase B priority on OK26 cirkulær publication (recurring item per S41 TASK-4107). HK-specific OvertimeRequiresPreApproval candidate-bug carries through. |

### SR-HK-OK26-002 — HK OK26 entitlement_configs placeholder bundle

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK26-002 |
| `agreement_code` | HK |
| `ok_version` | OK26 |
| `field` | All HK OK26 cells in `entitlement_configs` (5 entitlement types × ~6 sub-fields; mirrors HK OK24) |
| `current_encoded_value` | Identical to HK OK24 (see SR-HK-OK24-025..029). |
| `authoritative_source` | Ferieloven (VACATION); pending for overenskomst-driven entitlements |
| `interpretation` | HK OK26 entitlements inherit HK OK24 row-by-row. CHILD_SICK quota (HK = 2 days per episode) and SENIOR_DAY candidate-bug inherit to OK26. |
| `confidence_level` | HIGH for VACATION (statutory); MEDIUM-LOW for overenskomst-driven entitlements |
| `interpretation_authority` | Folketinget (VACATION) / Personalestyrelsen + HK union (others) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none — SENIOR_DAY candidate inherits) |
| `disputed?` | false |
| `notes` | Same shape as AC OK26 bundle (SR-AC-OK26-002). |

### SR-HK-OK26-003 — HK OK26 wage_type_mappings placeholder bundle

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK26-003 |
| `agreement_code` | HK |
| `ok_version` | OK26 |
| `field` | All HK OK26 cells in `wage_type_mappings` (mirrors HK OK24 mappings) |
| `current_encoded_value` | Identical to HK OK24 (see SR-HK-OK24-030 bundle). |
| `authoritative_source` | pending — SLS technical documentation; codes stable across OK24 → OK26 transition |
| `interpretation` | HK OK26 wage type mappings mirror HK OK24. SLS codes stable. |
| `confidence_level` | HIGH for existing mappings (stable infrastructure); LOW for completeness (OK26 may introduce new wage types) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Same shape as AC OK26 bundle (SR-AC-OK26-003). |

### SR-HK-OK26-004 — HK OK26 position_override_configs placeholder bundle (explicit absence)

| Field | Value |
|-------|-------|
| `row_id` | SR-HK-OK26-004 |
| `agreement_code` | HK |
| `ok_version` | OK26 |
| `field` | `position_override_configs (HK, OK26, *)` — explicit absence |
| `current_encoded_value` | `(no rows; HK OK26 has no position overrides — inherits HK OK24 absence per SR-HK-OK24-031)` |
| `authoritative_source` | n/a — explicit-absence row |
| `interpretation` | HK OK26 has no position overrides at seed time. Inherits from HK OK24 explicit-absence pattern. |
| `confidence_level` | HIGH (no rows present); LOW for whether this is correct |
| `interpretation_authority` | Personalestyrelsen |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
| `bug_correction_history` | (none) |
| `disputed?` | false |
| `notes` | Explicit-absence row, same pattern as SR-HK-OK24-031. |



### TASK-3601 — 20 cells, AC OK24 proof-of-shape

| Cell shape | Count | Schema fit |
|------------|-------|------------|
| Quantitative numeric (weekly norm, rest hours, ref period) | 4 (001–004) | ✓ — 15 columns sufficient |
| Enum / categorical | 4 (005–008) | ✓ |
| Rate / multiplier (mostly inert for AC) | 4 (009–012) | ✓ — `confidence_level = N/A-for-agreement` handles inertness cleanly; no separate inertness column needed |
| Entitlement (cross-table to `entitlement_configs`) | 4 (013–016) | ✓ — `field` accepts cross-table reference syntax |
| Compliance / governance | 4 (017–020) | ✓ — including the compound `NormModel + NormPeriodWeeks + AnnualNormHours` cell where 3 fields jointly encode one decision |

**TASK-3601 findings**:
- The speculative schema from PROGRAM L51–67 works on real data with **two enhancements** documented above: explicit `row_id` field + explicit `notes` field. Both were implicit in PROGRAM and made first-class here.
- **No schema BLOCKERs surfaced** — TASK-3602 can dispatch on the schema as-defined (no halt required per PLAN-s36 L171).

### TASK-3602 — AC OK24 completion (19 more cells: 021–039) + AC OK26 placeholder (4 bundles)

| Cell shape | Count | Schema fit |
|------------|-------|------------|
| Active boolean disabler (HasOvertime, OnCallDutyEnabled) | 2 (021, 025) | ✓ |
| Inert single fields | 2 (022, 023 — overtime thresholds) | ✓ — `confidence_level = N/A-for-agreement` pattern repeats |
| Compound inert bundle (supplements: 11 columns under one row) | 1 (024) | ✓ — schema accommodates 11-column compound via string-joined `field` + per-sub-field confidence notation in body |
| Compound mixed-inert bundle (CallInWorkEnabled + sub-fields) | 1 (027) | ✓ — same compound pattern; mixed-confidence within one row handled via explicit per-sub-field annotation |
| Active boolean enabler + dependent rates (Travel) | 3 (028, 029, 030) | ✓ — split into individual rows because each rate carries semantic weight |
| Entitlement quota (cross-table, individual) | 1 (031 — SPECIAL_HOLIDAY) | ✓ |
| Entitlement sub-field bundles (5 sub-fields each, compound) | 5 (032–036) | ✓ — compound pattern; reset_month / pro_rate_by_part_time / accrual_model / is_per_episode / carryover_max grouped per entitlement type |
| Wage type mapping bundle (cross-table, 17 + 1 mappings under one row) | 1 (037) | ✓ — largest compound row to date; "list-of-tuples" current_encoded_value format works |
| Position override (compound per position) | 2 (038, 039) | ✓ |
| OK26 placeholder bundles (cross-table, identity-with-OK24) | 4 (OK26 001–004) | ✓ — placeholder pattern with `confidence_level = LOW` + `last_verified_by = pending` + explicit reference to OK24 row IDs works cleanly |

**TASK-3602 findings**:
- **No new schema BLOCKERs**. The compound-cell pattern surfaced in TASK-3601 (SR-AC-OK24-020) generalised cleanly to 11-column bundles (SR-AC-OK24-024) and 17-row mapping bundles (SR-AC-OK24-037). The decision to defer a first-class `field_group` schema field stands — string-joined `field` + body-level enumeration is tractable.
- **OK26 placeholder pattern works**. Single bundle row per data domain (`agreement_configs`, `entitlement_configs`, `wage_type_mappings`, `position_override_configs`) cites OK24 inheritance + flags placeholder status without inflating row count. When OK26 cirkulær lands and Phase B verifies, divergent cells get their own SR-AC-OK26-NNN rows; identical cells stay under the bundle.
- **One observation to monitor in TASK-3603 (HK)**: HK has all supplements ENABLED, which inverts the AC pattern — HK's supplement-rate cells will be load-bearing individual rows, not compound inert bundles. Schema accommodates this without change (the same per-cell row pattern works); just expect more individual rows for HK / PROSA.

### TASK-3603 — HK OK24 completion (31 cells: 001–031) + HK OK26 placeholder (4 bundles)

| Cell shape | Count | Schema fit |
|------------|-------|------------|
| Universal-state-sector compound (norm) | 1 (001) | ✓ |
| HK-divergent-from-AC individual | 7 (002, 003, 004, 005, 015, 017, 019, 020) | ✓ — explicit `DIVERGENT from AC` annotation pattern works cleanly; cross-references AC row IDs |
| Load-bearing-in-HK (inert-in-AC) individual | 8 (006, 007, 010, 011, 012, 013, 014, 016) | ✓ — "LOAD-BEARING in HK" marker pattern paired with cross-ref to AC counterpart |
| Compound load-bearing bundles (HK supplements + windows + call-in) | 3 (008, 009, 017) | ✓ |
| Compound matches-AC cluster (travel, EU compliance) | 2 (018, 023) | ✓ — saves repeating identical AC content |
| Same-as-AC standalone | 2 (021, 024 — DefaultCompensationModel, MaxOvertimeHoursPerPeriod) | ✓ |
| Candidate-bug standalone | 1 (022 — OvertimeRequiresPreApproval) | ✓ — `confidence_level = LOW` + explicit candidate-bug notes |
| Entitlement compound | 5 (025–029) | ✓ — all sub-fields in one row per entitlement type |
| Wage type mappings bundle | 1 (030) | ✓ — ~22 mappings including supplements + overtime tiers |
| Explicit-absence row | 1 (031 — no HK position overrides) | ✓ — **new pattern** documented; `current_encoded_value = "(no rows...)"` cleanly carries "intentional absence vs missing by oversight" distinction |
| OK26 placeholder bundles | 4 | ✓ |

**TASK-3603 findings**:
- **No new schema BLOCKERs**. The "DIVERGENT from AC" + "LOAD-BEARING in HK" annotation patterns work cleanly as cross-references via row ID (e.g., `SR-AC-OK24-024`) rather than needing a structural cross-agreement-cell-reference field.
- **New pattern: explicit-absence row** (SR-HK-OK24-031) documents "this agreement has no rows in this data domain — verified intentional". Distinguishes from "missing by oversight". Will be re-used for PROSA / AC_RESEARCH / AC_TEACHING where applicable.
- **New candidate bug discovery**: HK `OvertimeRequiresPreApproval = false` (SR-HK-OK24-022). The seed default (column DEFAULT = `FALSE`) carried through to HK without explicit per-agreement consideration in S17. For HK with real overtime regime, pre-approval requirement IS a governance question. If Phase B confirms HK overenskomst requires pre-approval, this is a bug-with-no-past-impact correction.

### Candidate bug discoveries (cumulative through TASK-3603)

| Row(s) | Finding | Severity | Phase B priority |
|--------|---------|----------|------------------|
| **SR-AC-OK24-015 + SR-AC-OK24-035 + SR-HK-OK24-029** | SENIOR_DAY `annual_quota = 0` with `min_age = 60` — encoding semantics unclear. Either (a) rule overrides with age-banded lookup (incomplete encoding), or (b) `min_age` field is vestigial. As encoded, no senior days ever grant regardless of age. Same encoding across all 5 agreements per init.sql:1373–1378 — fix propagates uniformly per ROADMAP no-per-institution-opt-in policy. | bug-candidate (unconfirmed) | **HIGH** — senior-employee compensation correctness; cross-agreement |
| **SR-HK-OK24-022** | HK `OvertimeRequiresPreApproval = false` — for HK's real overtime regime, current `false` may invert cirkulær intent. The seed default carried through without per-agreement consideration in S17. | bug-candidate (unconfirmed) | **MEDIUM-HIGH** — workflow gate affecting all HK overtime registration; needs Phase B cirkulær cite to confirm direction |

These observations feed into:
- S36 TASK-3607 (agreement-ruleset-audit doc) — both candidates surface as DRIFT-IN-CODE / DRIFT-IN-SOURCE classification candidates
- S37 Phase B feedback packaging — both are high-priority cells for domain-expert sign-off
- Phase E continuous-validation tests in S39

---

## Cell Count Tracker

| Agreement | OK24 cells | OK26 cells | Total |
|-----------|------------|------------|-------|
| AC | **39** (20 proof-of-shape TASK-3601 + 19 completion TASK-3602) | **4** (placeholder bundles, TASK-3602) | **43** |
| HK | **31** (TASK-3603) | **4** (placeholder bundles, TASK-3603) | **35** |
| PROSA | 0 (TASK-3604) | 0 | 0 |
| AC_RESEARCH | 0 (TASK-3605) | 0 | 0 |
| AC_TEACHING | 0 (TASK-3605) | 0 | 0 |
| **Total** | **70** | **8** | **78** |

**HK OK24 cell coverage by source surface**:
- `agreement_configs` columns: all ~37 effective columns covered across SR-HK-OK24-001..024
- `entitlement_configs` rows: 5 entitlement types fully covered via SR-HK-OK24-025..029 (one row per entitlement type with all sub-fields)
- `wage_type_mappings` rows: ~22 mappings covered in SR-HK-OK24-030 bundle (HK has supplements + overtime mappings that AC doesn't)
- `position_override_configs` rows: explicit-absence pattern via SR-HK-OK24-031 (HK has no overrides seeded)

**HK OK26 cell coverage**: all data domains covered via 4 placeholder bundles (SR-HK-OK26-001..004).
