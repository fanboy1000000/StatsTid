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

---

## Schema Validation (TASK-3601 self-check)

After populating the 20 cells, the schema's adequacy for AC OK24's real cell shapes:

| Cell shape | Count | Schema fit |
|------------|-------|------------|
| Quantitative numeric (weekly norm, rest hours, ref period) | 4 (001–004) | ✓ — 15 columns sufficient |
| Enum / categorical | 4 (005–008) | ✓ |
| Rate / multiplier (mostly inert for AC) | 4 (009–012) | ✓ — `confidence_level = N/A-for-agreement` handles inertness cleanly; no separate inertness column needed |
| Entitlement (cross-table to `entitlement_configs`) | 4 (013–016) | ✓ — `field` accepts cross-table reference syntax |
| Compliance / governance | 4 (017–020) | ✓ — including the compound `NormModel + NormPeriodWeeks + AnnualNormHours` cell where 3 fields jointly encode one decision |

**Findings**:
- The speculative schema from PROGRAM L51–67 works on real data with **two enhancements** documented above: explicit `row_id` field + explicit `notes` field. Both were implicit in PROGRAM and made first-class here.
- **No schema BLOCKERs surfaced** — TASK-3602 can dispatch on the schema as-defined (no halt required per PLAN-s36 L171).
- **One enhancement to monitor in TASK-3602**: compound cells (like SR-AC-OK24-020 with three fields jointly encoding norm-period model) may want their own `field_group` semantic in the schema rather than relying on string-joined `field` values. Decision deferred until TASK-3602 surfaces second instance; if it doesn't, the string-join convention is fine.

**Candidate bug discoveries (from these 20 cells)**:
- **SR-AC-OK24-015 SENIOR_DAY quota = 0** is LOW-confidence and semantically unclear — `0` with `min_age=60` is either an incomplete encoding (rule should override per age band) or a vestigial field. Flag for S37 absorption + Phase B priority. **Candidate bug-with-no-past-impact correction** if Phase B confirms the encoding should be age-banded grants.

These observations feed into S36 TASK-3607 (agreement-ruleset-audit) and into S37 Phase B feedback packaging.

---

## Cell Count Tracker

| Agreement | OK24 cells | OK26 cells | Total |
|-----------|------------|------------|-------|
| AC | 20 (proof-of-shape, TASK-3601) | 0 (TASK-3602 will fill) | 20 |
| HK | 0 (TASK-3603) | 0 | 0 |
| PROSA | 0 (TASK-3604) | 0 | 0 |
| AC_RESEARCH | 0 (TASK-3605) | 0 | 0 |
| AC_TEACHING | 0 (TASK-3605) | 0 | 0 |
| **Total** | **20** | **0** | **20** |
