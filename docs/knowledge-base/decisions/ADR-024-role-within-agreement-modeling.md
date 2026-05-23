# ADR-024 — Role-Within-Agreement Modeling + Correction Policy + Classification Governance (Phase C Design)

| Field | Value |
|-------|-------|
| **Status** | ACCEPTED (S38 TASK-3801; cycles 1-3 trail closed all ADR-024 defects: cycle 1 D6 surface coverage; cycle 2 event-name propagation to ADR-013/References; cycle 3 verified clean by both lenses. D7 (overtime authorization model) ships in this ADR — distinct from ADR-025's deferred D7 audit-visibility surface. ACCEPTED per S28 / S32 design-only precedent at cycle-3 lens convergence on this ADR.) |
| **Sprint** | S38 (design-only sprint; produces this ADR + ADR-025 + ADR-013 amendment for S39 schema migration + S40 cutover + S41 D-tests). |
| **Domains** | Backend, Infrastructure, Data Model, SharedKernel, Rule Engine, Payroll Integration. |
| **Tags** | role-within-agreement, employment-category, role-config-override, merarbejde-compensation-right, bug-correction-policy, classification-governance, interpretation-authority, overtime-authorization, post-hoc-necessity-acknowledgment, design-binding, phase-4e. |
| **Supersedes** | none |
| **Amends** | none (companion ADR-013 amendment lands separately under TASK-3803 to cross-reference D3 + D6 below). |

> **Pre-rule projection disclaimer** (added 2026-05-23): This ADR was authored before `docs/WORKFLOW.md` § "Binding to Architectural Events, Not Sprint Numbers" landed. **Sprint-number references in this ADR (S39, S40, S41, TASK-39XX, etc.) are projections at time of authoring, not binding architectural commitments.** The binding architectural constraint is on Phase D implementation (schema → cutover → D-tests) shipping before customer-go-live. Current sprint plan in `ROADMAP.md` supersedes specific sprint slot mapping. Re-prioritisations re-map Phase→sprint without invalidating this ADR.

## Context

S36 Phase A inventory + S37 interim-expert absorption surfaced three systemic gaps that this ADR settles architecturally. The headline finding (PROGRAM-s36-s41-domain-correctness.md L31, reinforced by `role-dimension-audit.md`) is that **AC chefkonsulent / kontorchef / specialkonsulent are widely understood to lose contractual merarbejde compensation right per the AC overenskomst, but the system treats all AC employees identically** — an AC chefkonsulent user provisioned today receives merarbejde compensation via agreement-level fallthrough. `User.EmploymentCategory` exists but no rule in `src/RuleEngine/` reads it (verified S36 TASK-3606 via grep). `PositionOverrideConfigs` schema covers only 4 quantitative fields and cannot express "no entitlement". The role distinction is unmodeled.

The S35 → S37 sprint chain also formalised a **rule correction policy** (ROADMAP L25, committed 2026-05-18) — supersession-by-default + bug-correction-when-classified — and applied it 5 times (S35 AC=AFSPADSERING; S37 Bugs #1-#4). The policy has been operating against an unwritten convention; this ADR codifies it so S39+ implementations can build deterministic seed-parity tests + "unknown unknown" tests against an explicit contract.

S37 TASK-3704 (Bug #4 absorption) added a 7th decision: an **overtime authorization model** with post-hoc necessity-acknowledgment workflow. The Bug #4 split-routing decision (record direction, defer implementation) was made specifically because flipping HK/PROSA `OvertimeRequiresPreApproval` from `false → true` without the necessity-acknowledgment workflow would create an intermediate-state regression — legitimate necessity-driven overtime would be persistently blocked. This ADR specifies the workflow extension for S40 implementation.

**This ADR operates under interim-expert posture** — real Phase B domain-expert engagement remains pending. Architectural decisions below are made on the **system-design correctness frame** (which option is cleanest for the architecture) rather than on confirmed cirkulær-paragraph cites. Each decision notes whether it depends on Phase B confirmation or stands on architecture alone. When Phase B engages, this ADR may need amendment if expert findings diverge.

**Every decision below is binding for S39 implementation refinement** — precise enough that S39 refinement Step 4 should converge on a single mechanical path, not a per-decision architectural fork.

## Decisions

### D1 — Role dimension placement: activate `User.EmploymentCategory` as first-class rule input + introduce `RoleConfigOverride` parallel to position override

**Three options were considered**:

- (a) **Extend `PositionOverrideConfigs` schema** with 6 boolean disabler columns (HasMerarbejde / HasOvertime / 4 supplement enabled flags / OnCallDuty / CallInWork) + a tri-state column for the compensation entitlement model. Lowest-disruption — existing `ConfigResolutionService` chain (central → position-override → local) already routes through this table. Rejected because it mixes concerns: PositionOverrideConfigs becomes both quantitative-tuning AND entitlement-toggling. Schema grows ambiguously. Position registry would need to enumerate every role variant (CHEFKONSULENT not currently seeded; would need to add it as a position even though chefkonsulent is conceptually a role-stratum, not a position).
- (b) **Activate `User.EmploymentCategory` as first-class rule input + introduce `RoleConfigOverride` parallel to position override** ← **CHOSEN**
- (c) **Promote senior roles to separate agreement codes** (e.g., `AC_CHEFKONSULENT` parallel to AC). Rejected because it conflates two concepts: agreement = overenskomst (the framework), stratum = role within agreement. AC chefkonsulent IS still on the AC overenskomst — they just lose merarbejde entitlement per the same cirkulær. Promoting to separate agreement codes would also break "all AC employees" queries in admin views.

**Chosen design (option b)**:

- New `role_config_overrides` table keyed by composite `(agreement_code, ok_version, employment_category)` with 6 boolean disabler columns + a tri-state `merarbejde_compensation_right` column (per D2) + nullable quantitative override columns matching the existing `PositionOverrideConfigs` schema where they apply (`max_flex_balance`, `flex_carryover_max`, `norm_period_weeks`, `weekly_norm_hours`).
- `ConfigResolutionService` chain gains a 4th layer: **central → role-override → position-override → local**. Role override applies after central but before position so that role-level entitlement disablements can be re-enabled by position-specific overrides (e.g., a hypothetical "chefkonsulent on emergency-response project" could re-enable merarbejde via position override).
- `User.EmploymentCategory` (`User.cs:13`, currently defaults to `"Standard"`) becomes load-bearing. The `EmploymentProfile.EmploymentCategory` field (S31 introduction; consumed by `EmploymentProfileResolver` per ADR-023 D1) is already alive on the rule-engine path — S40 cutover just adds the `role_config_overrides` lookup at the `ConfigResolutionService` level.
- New event types: `RoleConfigOverrideCreated`, `RoleConfigOverrideUpdated`, `RoleConfigOverrideSuperseded`, `RoleConfigOverrideSoftDeleted` following S29/S33 versioned-config pattern (`SupersedeAndCreateAsync` 3-case routing per ADR-020 D2).
- New admin endpoints under `/api/admin/role-config-overrides/` with admin-strict If-Match per ADR-019 D2 (5th surface after the 4 closed in S25/S35).
- New frontend admin page `RoleConfigOverrideEditor.tsx` (HROrAbove + GlobalAdmin variants per ADR-008).

**Standard employment categories to seed** (initial S40 scope):
- `"Standard"` — default; no overrides; matches today's behavior
- `"Fuldmægtig"` — AC entry-level; explicit MerarbejdeCompensationRight=CONTRACTUAL (documents the encoded default; no actual override needed but makes the rule explicit)
- `"Specialkonsulent"` — AC senior IC; MerarbejdeCompensationRight=DISCRETIONARY pending Phase B paragraph cite (PROVISIONAL per `role-dimension-audit.md`)
- `"Chefkonsulent"` — AC most-senior IC; MerarbejdeCompensationRight=NONE pending Phase B
- `"Kontorchef"` — AC managerial; MerarbejdeCompensationRight=NONE pending Phase B
- HK / PROSA / AC_RESEARCH / AC_TEACHING within-stratum categories: TBD per Phase B — initial seed covers the 5 AC strata only; other agreements use `"Standard"` until Phase B identifies relevant strata.

**Why this is the cleanest architecture**: role distinction lives in a dedicated dimension separate from position; `EmploymentCategory` becomes load-bearing as designed in S31; the chefkonsulent-vs-kontorchef-position distinction is preserved (kontorchef IS a position; chefkonsulent is a role-stratum); future role additions don't require schema changes.

**Phase B dependency**: the specific 5 AC categories' `MerarbejdeCompensationRight` values are PROVISIONAL pending real Phase B paragraph cite. The architecture stands regardless.

### D2 — Tri-state `MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE`

The current binary `agreement_configs.default_compensation_model` (`UDBETALING` / `AFSPADSERING`) + `employee_compensation_choice` (boolean) cannot express the chefkonsulent's "no contractual right" case — both binary values imply the employee has a right to compensation in some form. The tri-state model captures the distinction the cirkulær makes:

| Value | Meaning |
|-------|---------|
| `CONTRACTUAL` | Employee has contractual right to compensation. Compensation form (afspadsering vs udbetaling) follows existing `default_compensation_model` + `employee_compensation_choice` logic. Default for fuldmægtig + most agreements. |
| `DISCRETIONARY` | Compensation may be granted at employer discretion but is not contractually mandated. Rule engine emits a flag event but no automatic MERARBEJDE/SLS line. Admin UI surfaces a one-off-payment trigger. |
| `NONE` | No compensation right. Rule engine does NOT emit MERARBEJDE events for this category × agreement combination. Payroll mapping skips merarbejde entirely. |

**Where the column lives**: on `role_config_overrides` (D1) keyed by `(agreement_code, ok_version, employment_category)`. NOT on `agreement_configs` — that table stays at the per-overenskomst level; the role distinction lives one dimension out.

**Rule-engine cutover**: `OvertimeGovernanceRule` + `PayrollMappingService.BuildLine` read the tri-state via `ConfigResolutionService.GetEffectiveConfig(employee_id, date)`. For `NONE` categories, MERARBEJDE emission is suppressed. For `DISCRETIONARY`, a new `MerarbejdeDiscretionary` event type is emitted (registration via PAT-004); admin UI surfaces it for manual one-off-payment trigger.

**Replay determinism**: tri-state value is read via dated lookup (S33 ADR-023 D1 pattern) — past-period replay uses the `role_config_overrides` row that was effective at segment start. Lands as load-bearing D-test in S41 TASK-4106-equivalent (chefkonsulent past-period replay determinism).

**Phase B dependency**: same as D1 — the specific tri-state value assigned to each category is PROVISIONAL. The architecture stands.

### D3 — Correction policy formalization + bug_correction_history schema definition

Codifies the ROADMAP rule correction policy (committed 2026-05-18) + the `bug_correction_history` schema row that has evolved across S35-S37 application. (Merged from D8 candidate per S38 user adjudication.)

**Policy** (codification of ROADMAP L25 + L29-32):

Discovered discrepancies are classified as either:

- **(a) Interpretation change** — the parties agree on a new reading or amend the overenskomst; encoded rule was correct at the time → **supersession from effective date forward**, no retroactive recomputation, past periods replay against prior interpretation. Original `agreement_configs` row carries `archived_at`; new row publishes with `effective_from = decision_date`. Standard ADR-014 lifecycle.

- **(b) Bug** — the encoding never matched the agreed interpretation → **retroactive correction permitted** via in-band recompute. Sub-classified by past-impact:
  - **bug-with-no-past-impact** (pre-launch posture; no past periods exist) — forward-only seed correction; no recompute needed. Action: `bug-fix-without-recompute`.
  - **bug-with-past-impact** (post-launch; past periods exist that need correcting) — requires retroactive recompute via PCS replay infrastructure. New segment manifest with corrected result; original manifest preserved per ADR-016 D10. Action: `bug-fix-with-recompute`. SLS reconciliation pattern per ADR-027 (post-launch).
  - **decision-recorded-fix-deferred** — direction adjudicated but implementation deferred due to prerequisite scope (S37 Bug #4 pattern). No seed change; SR cell records the direction; downstream sprint(s) implement.

**Binary classification framework** (was-agreed × materially-wrong):

| was-agreed | materially-wrong | Classification |
|------------|------------------|----------------|
| YES (parties agree on new reading) | — | (a) supersession |
| NO (parties never agreed this encoding) | YES (past-impact OR pre-launch but production-broken) | (b) bug-with-past-impact OR bug-with-no-past-impact |
| NO | NO | (b) bug-with-no-past-impact OR decision-recorded-fix-deferred |
| pending Phase B | pending | NOT YET CLASSIFIED — stays candidate |

**`bug_correction_history` schema** (source register column 13 formalized):

```
{
  date: YYYY-MM-DD,
  from_value: <prior encoded value or "(no rows)">,
  to_value: <new encoded value>,
  source: <cirkulær URL + paragraph OR free-form sprint/task reference>,
  commit: <git commit hash or "<this S<N> commit>">,
  classifier: <name + role>,
  was_agreed: <YES | NO | PENDING>,
  materially_wrong: <NO_PRE_LAUNCH | YES_PRE_LAUNCH_BUT_BROKEN | PENDING_S<NN> | YES_WITH_PAST_IMPACT>,
  action: <bug-fix-without-recompute | bug-fix-with-recompute | decision-recorded-fix-deferred>
}
```

**Existing applications** (5 entries cumulative through S37; all `action="bug-fix-without-recompute"` except S37 Bug #4 `action="decision-recorded-fix-deferred"`):
1. S35 AC=AFSPADSERING (cbaea7d)
2. S37 Bug #1 AC variants entitlement_configs absence (3eea4f5)
3. S37 Bug #2 AC variants wage_type_mappings SLS divergence (ce1bf68)
4. S37 Bug #3 SENIOR_DAY paired-bug (2eaa021)
5. S37 Bug #4 HK/PROSA OvertimeRequiresPreApproval (fa00d97) — decision-recorded-fix-deferred

S39 Phase E continuous-validation tests assert the schema (presence of all fields per entry; valid enum values).

### D4 — Classification governance workflow

**Who classifies a discovered discrepancy**: encoding owner (Orchestrator / Claude in the current workflow) surfaces the candidate; **product owner review BEFORE any code change** adjudicates the classification + action. Workflow:

1. Discovery — encoding owner finds discrepancy via inventory (S36 pattern), incidental investigation, or Phase B feedback.
2. Surface — encoding owner files the finding in source register with `last_verified_by: pending` + appropriate confidence level + clear evidence pointers.
3. Adjudication — product owner reviews; classifies per D3 binary framework; assigns action (bug-fix-with/without-recompute, decision-recorded-fix-deferred, or "not a bug — interpretation change requiring supersession").
4. Implementation — encoding owner ships the seed/code change per action; appends `bug_correction_history` entry with classifier + date.
5. Confirmation (for cells originally PENDING Phase B) — when real Phase B engages, expert may re-adjudicate; if classification changes, new `bug_correction_history` entry appended (not edited; history is append-only).

**Workflow exception — interim-expert posture**: when external Phase B engagement is pending, the Orchestrator may act as interim classifier with explicit user confirmation, recording `classifier: "Orchestrator (interim, user-confirmed)"`. S37 Bugs #1-#4 followed this pattern. Real Phase B re-adjudication may produce additional `bug_correction_history` entries on the same cells later.

**Disputed cells** (was-agreed = PENDING + parties disagree on interpretation) — defer to D5 interpretation authority.

### D5 — Interpretation authority

Per ROADMAP commitment (2026-05-18), **default interpretation authority is Personalestyrelsen / Medst cirkulær (employer-side)**. Deviations are documented per-cell in the source register `notes` field with `disputed?: true` and explicit rationale.

**Why employer-side default**: ROADMAP positions StatsTid as a standardization mechanism. The system's encoded interpretation becomes a forcing function on customer/employer-side negotiation. Adopting a union-side default would introduce per-tenant interpretation drift, violating the glocal principle (interpretation GLOBAL; only locally-delegated parameters vary per institution).

**Exceptions allowed**:
- Per-cell deviation when both parties have publicly agreed on a non-employer reading. Document with cirkulær URL + agreed-party signatures.
- EU-derived cells (WTD Article 3, 6, 16 etc.) — authority is EU directive + Danish transposition law, not Personalestyrelsen.
- Statutory cells (Ferieloven) — authority is Folketinget (statute), not Personalestyrelsen.

S39 Phase E tests assert per-cell `interpretation_authority` field consistency: cells with `disputed?: true` MUST have a non-default value documented.

**No Phase B dependency** — D5 is policy/architecture; cirkulær wording doesn't change the policy choice.

### D6 — Bug correction operational model

**Operator-triggered, not per-institution choice**. Per ROADMAP no-per-institution-opt-in policy: bug corrections apply globally to all 150 institutions or to none. Customer cannot opt out of a bug fix; this preserves the glocal interpretation principle.

**Surface coverage (S38 Step 7a P1.2 absorption)**: bug corrections per D3 apply across **5 config surfaces** that the 5 cumulative bug-correction applications (S35 + S37 Bugs #1-#4) have touched:

| Config surface | Example bug application |
|----------------|--------------------------|
| `agreement_configs` | S35 AC=AFSPADSERING; S37 Bug #4 (decision-recorded for HK/PROSA OvertimeRequiresPreApproval) |
| `entitlement_configs` | S37 Bug #1 (AC variants missing entitlement rows); S37 Bug #3 (SENIOR_DAY paired-bug) |
| `wage_type_mappings` | S37 Bug #2 (AC variants divergent SLS codes + CHILD_SICK_DAY chain restoration) |
| `position_override_configs` | not yet applied; available for future bug corrections |
| `role_config_overrides` (new per D1) | not yet applied; available for future bug corrections |

D6 generalizes the bug-correction model across all 5 surfaces via **a single surface-discriminated event** + **a single endpoint pattern**:

**Single event type**: `ConfigBugCorrected` (NOT `AgreementConfigBugCorrected` — generalized from S35 → S37 → S38 evolution). EventSerializer registration per PAT-004. Payload:

```typescript
type ConfigBugCorrected = {
  // Discriminator: which config surface
  configSurface: 'agreement_configs' | 'entitlement_configs' | 'wage_type_mappings'
              | 'position_override_configs' | 'role_config_overrides';
  // Natural-key of the corrected row (varies per surface — JSONB shape)
  configKey: object;
  // Classification metadata per D3
  classification: {
    wasAgreed: 'YES' | 'NO' | 'PENDING';
    materiallyWrong: 'NO_PRE_LAUNCH' | 'YES_PRE_LAUNCH_BUT_BROKEN' | 'PENDING_S<NN>' | 'YES_WITH_PAST_IMPACT';
    action: 'bug-fix-without-recompute' | 'bug-fix-with-recompute' | 'decision-recorded-fix-deferred';
    classifier: string;
    decisionDate: string;     // YYYY-MM-DD
    sourceCirkular: string;   // URL + paragraph OR free-form reference
  };
  // From/to values for audit
  fromValue: object | null;
  toValue: object;
};
```

**Single endpoint pattern**: `POST /api/admin/{surface}/{id}/correct-as-bug` where `{surface}` ∈ the 5 enumerated surfaces. GlobalAdminOnly RBAC (operator decision; not customer-side). Routes through atomic outbox per ADR-018 D3 (single tx: surface-table row update + surface-specific audit table append + `ConfigBugCorrected` event emit).

**Why one event type with discriminator vs per-surface event types**: 5 surfaces × bug-correction = 5 event types is registration overhead without consumer-side benefit (downstream replays + audit queries treat all 5 identically; the only consumer-side variance is the natural-key shape, which the `configKey` JSONB carries). One event with discriminator preserves all the auditability + replay properties + matches PAT-004 polymorphism convention without 5× registration.

**SLS reconciliation pattern** for bug-with-past-impact corrections: **defer to ADR-027 (post-launch)**. Pre-launch posture means no past periods exist that need recompute; the in-band recompute path can be designed when first needed. ADR-027 placeholder filed for the first post-launch bug-with-past-impact discovery. ADR-027 covers the recompute workflow uniformly across all 5 surfaces using the same `ConfigBugCorrected` event as the trigger.

**Replay determinism preserved**: bug corrections produce a NEW segment manifest with corrected result; the original manifest persists and remains replayable byte-identically per ADR-016 D10. "Current truth for period P" = latest manifest's result; "historical truth at time T" = the manifest that existed at T. Holds uniformly across all 5 surfaces.

**Source register `bug_correction_history` `source` field** records which surface was corrected; matches the event's `configSurface` discriminator for cross-reference auditing. S39 Phase E test asserts this 1:1 mapping.

**No Phase B dependency**: D6 is operational/architecture; stands on its own.

### D7 — Overtime authorization model (pre-approval + post-hoc necessity-acknowledgment)

**Context**: S37 Bug #4 absorption recorded the Path A direction (HK + PROSA `OvertimeRequiresPreApproval = true` per cirkulær framework) but deferred the seed flip to S40 alongside this workflow extension. Without the necessity-acknowledgment path, flipping `false → true` would create an intermediate-state regression: legitimate necessity-driven overtime (e.g., after-hours system-outage response with no prior approval) would be persistently blocked. The cirkulær framework ("beordret merarbejde/overarbejde") permits BOTH prior approval AND post-hoc necessity-acknowledgment — both encodings serve the legal concept that compensable overtime must be employer-authorized.

**Design scope**:

1. **Schema** — extend the `overtime_pre_approvals` table (S17 introduction) with new fields:
   - `authorization_mode TEXT NOT NULL CHECK (authorization_mode IN ('PRIOR_APPROVAL', 'POST_HOC_NECESSITY'))` — default `'PRIOR_APPROVAL'` for existing rows
   - `necessity_reason TEXT NULL` — required when `authorization_mode = 'POST_HOC_NECESSITY'`
   - `acknowledged_at TIMESTAMPTZ NULL` — when manager applied the necessity-ack
   - `acknowledged_by TEXT NULL` — manager's user_id
   Add a new audit table `overtime_authorization_audit` matching the post-S25 schema pattern (audit-version-transition columns per ADR-019 D8).

2. **Endpoint** — new `POST /api/overtime-pre-approvals/{id}/acknowledge-necessity` under LocalAdminOrAbove + OrgScopeValidator. Body: `{necessity_reason: string, acknowledged_for_entries: [TimeEntryId[]]}`. Atomic tx per ADR-018 D3 (overtime_pre_approvals UPDATE + overtime_authorization_audit INSERT + new event `OvertimeNecessityAcknowledged` emit).

3. **UI** — new manager workflow on `Approval` page: lists time entries that triggered overtime under `OvertimeRequiresPreApproval=true` without prior approval; manager selects entries + provides necessity reason + acknowledges. Mirror banner-with-retry pattern from S25 admin-strict If-Match (per ADR-019 D2).

4. **Audit-trail discipline** — post-hoc acknowledgments MUST be distinguishable from prior approvals in audit log queries. The `authorization_mode` column on `overtime_pre_approvals` plus the temporal direction (`acknowledged_at > affected time_entry.registered_at`) provides this distinction. S41 D-tests assert audit log can answer "was this entry approved prior or acknowledged post-hoc?" via SQL query.

5. **Payroll-mapping replay-determinism** (Reviewer N2 from S37 Step 7a) — when a post-hoc necessity-acknowledgment converts an entry from "rejected/warning" to "approved/compensable" retroactively, the payroll export side must NOT silently recompute the past period. Per ADR-016 D10, the original manifest persists; the new acknowledgment produces a NEW segment manifest. Operator-triggered explicit recompute (per D6 bug-correction operational model) can ship the corrected payroll line; otherwise the un-acknowledged state remains the historical truth. Lands as load-bearing D-test in S41.

6. **No per-institution opt-in/out** — uniform with D6 operational model. The choice between prior-approval and post-hoc-necessity-ack is per-entry, not per-institution.

**Seed flip for HK/PROSA `OvertimeRequiresPreApproval = false → true`** lands in S40 atop the workflow extension. SR-HK-OK24-022 + SR-PROSA-OK24-007 `bug_correction_history` `action` updates from `decision-recorded-fix-deferred` to `bug-fix-without-recompute` when S40 commits.

**Phase B dependency**: cirkulær paragraph cite for the pre-approval requirement is pending. Architecture stands.

## Consequences

### S39 schema migration (Phase D Implementation Sprint 1)

New tables + columns derived from D1 + D7:

- `role_config_overrides` table (D1) with full versioned-config schema (effective_from / effective_to / partial-unique-index `WHERE effective_to IS NULL` / history-unique-index / version BIGINT) per S29/S30/S31/S33/S34 pattern
- `role_config_override_audit` table with version-transition columns per ADR-019 D8
- `overtime_pre_approvals` extension (D7): `authorization_mode` + `necessity_reason` + `acknowledged_at` + `acknowledged_by` columns
- `overtime_authorization_audit` table (D7)

Migration approach: greenfield-baked + guarded ALTER block per S30 / S31 / S35 pattern. Ledger entries `s39-d1-role-config-overrides` + `s39-d7-overtime-authorization-extension`.

### S40 cutover (Phase D Implementation Sprint 2)

- `RoleConfigOverrideRepository` with full `(conn, tx)` overloads + `SupersedeAndCreateAsync` 3-case routing per ADR-020 D2 + `SoftDeleteAsync` per ADR-023 D8.
- New event types from ADR-024: `RoleConfigOverrideCreated/Updated/Superseded/SoftDeleted` (4) + `OvertimeNecessityAcknowledged` (1) + generalized `ConfigBugCorrected` (1, per D6 P1.2 absorption replacing the per-surface `AgreementConfigBugCorrected`) + `MerarbejdeDiscretionary` (1, per D2 tri-state model) = **7 new typeof registrations** (EventSerializer 58 → 65 from ADR-024 alone post-S40; ADR-025 adds 4 more = 58 → 69 net post-S40 combined).
- `ConfigResolutionService` 4-layer chain (D1 cutover): central → role-override → position-override → local. Dated lookup (S33 ADR-023 D1 pattern) for replay determinism.
- `OvertimeGovernanceRule` + `PayrollMappingService.BuildLine` read tri-state `MerarbejdeCompensationRight` (D2). `NONE` → no MERARBEJDE emission. `DISCRETIONARY` → flag event for manual trigger.
- New admin endpoint `/api/admin/role-config-overrides/{...}` with admin-strict If-Match (D1).
- Frontend admin page `RoleConfigOverrideEditor.tsx` (D1).
- New endpoint `POST /api/overtime-pre-approvals/{id}/acknowledge-necessity` + UI (D7).
- HK + PROSA seed flip `OvertimeRequiresPreApproval = false → true` atop the D7 workflow (Bug #4 final resolution).

### S41 D-test matrix + governance bake-in (Phase D Implementation Sprint 3)

- Marquee D-test: chefkonsulent past-period replay determinism — admin sets MerarbejdeCompensationRight=NONE for AC.Chefkonsulent today; replay of last month's PCS-routed calc uses prior CONTRACTUAL value; result byte-identical. Closes ADR-016 D10 for the role-stratum dimension.
- Per-agreement D-test matrix: AC (fuldmægtig + specialkonsulent + chefkonsulent + kontorchef + researcher × OK24/OK26) + HK + PROSA + AC_RESEARCH + AC_TEACHING — verifies MERARBEJDE emission semantics per category.
- Overtime authorization D-tests (D7): prior-approval path + post-hoc-necessity-ack path + denied path + audit-log discriminator queries.
- Phase E continuous-validation tests:
  - Seed-parity per source register (filtering `N/A-for-agreement` cells)
  - "Unknown unknown" — every active config cell across `agreement_configs` / `entitlement_configs` / `wage_type_mappings` / `position_override_configs` / `role_config_overrides` has a source-register row with `authoritative_source` populated
  - DRAFT-OK source-cite enforcement — new OK versions cannot publish without `authoritative_source`
  - `bug_correction_history` schema validation per D3
- `docs/WORKFLOW.md` gains OK-version transition checklist + per-rule traceability requirement
- `docs/QUALITY.md` Domain Correctness category added at A (or B if expert validation surfaces material disputes)

### Companion ADRs

- **ADR-025** (S38 TASK-3802) — multi-tenant operational concerns; orthogonal to this ADR.
- **ADR-013 amendment** (S38 TASK-3803) — cross-reference D3 + D6: bug corrections become an explicit-cascade trigger under ADR-013's no-cascade discipline. The cascade is explicit (operator-triggered per D6), not implicit (rule-engine-derived).
- **ADR-027 (post-launch)** — bug-with-past-impact workflow + SLS reconciliation pattern; filed when first post-launch bug-with-past-impact is discovered.

### Phase B engagement implications

When real Phase B engages:

- D1 + D2 architecture stands. Specific category seed values (the 5 AC strata + any HK/PROSA/variant strata Phase B identifies) may shift per expert paragraph cites — new `bug_correction_history` entries appended; no architectural amendment.
- D3 policy formalization stands; Phase B may refine wording on edge cases (disputed cells, multi-party disagreement handling).
- D7 workflow stands. Phase B may suggest additional audit-trail columns or refine the necessity-reason taxonomy.
- D4 + D5 + D6 are policy/governance; less likely to change.

Real Phase B engagement may produce findings that warrant ADR-024 amendments — those file as new ACCEPTED-via-Step-7a-equivalent revisions (not in-place rewrites).

## References

- ROADMAP "Deployment Model" L16-27 — single logical deployment, 150 institutions, glocal rule encoding
- ROADMAP rule correction policy L25 (committed 2026-05-18)
- PROGRAM-s36-s41-domain-correctness.md L29-34 (three systemic gaps) + L121-164 (Phase C design scope)
- `docs/references/agreement-source-register.md` — 111-cell register with the chefkonsulent gap evidence + bug_correction_history audit trail
- `docs/references/role-dimension-audit.md` — within-OK role enumeration + production-incorrectness call-out (PROVISIONAL pending Phase B)
- `docs/references/agreement-ruleset-audit.md` — Candidate Bug Routing Summary
- ADR-013 (no-cascade) — amended to cross-reference D3 + D6
- ADR-014 (DB-backed agreement configs) — generalized `ConfigBugCorrected` event (per D6) distinct from `AgreementConfigPublished`
- ADR-016 D10 (replay determinism) — bug corrections preserve original manifests
- ADR-018 D3 (atomic outbox) — `ConfigBugCorrected` + `OvertimeNecessityAcknowledged` + role-config-override lifecycle events ride atomic single-tx
- ADR-019 D2 + D8 (admin-strict If-Match + audit version-transition) — new admin endpoints follow this contract
- ADR-020 D2 (3-case routing) — `RoleConfigOverrideRepository.SupersedeAndCreateAsync` per pattern
- ADR-023 D1 (consumption-time lookup) — `ConfigResolutionService` dated lookup for replay determinism
- S37 absorption commits: 3eea4f5 (Bug #1) + ce1bf68 (Bug #2) + 2eaa021 (Bug #3) + fa00d97 (Bug #4) + 65f9866 (cosmetics) + e4c6517 (Step 7a) + 03f63d7 (sprint close)

---

## Amendment 2026-05-23 — Cutover Seams (S41a)

**Why this amendment exists**: S41 refinement cycle 1 surfaced 4 architectural BLOCKERs (both Codex + Reviewer Agent lenses convergent) when the original D1+D2 cutover scope was translated into implementation tasks. The BLOCKERs were not transcription nits — they were genuine architectural seams that the in-body D1+D2 text described in terms that didn't match the existing codebase. Per `feedback_thrash_defer_real_world.md` smoke-alarm discipline (this was cycle 4 of same-area ADR-024 read-through misses across the session), user adjudicated 2026-05-23 to author an amendment settling each seam explicitly rather than absorb-and-redraft another refinement cycle.

The 4 seams + decisions:

### Seam A — Rule-engine consumer of MERARBEJDE suppression

**In-body text (L69)** said: *"`OvertimeGovernanceRule` + `PayrollMappingService.BuildLine` read the tri-state via `ConfigResolutionService.GetEffectiveConfig(employee_id, date)`. For `NONE` categories, MERARBEJDE emission is suppressed."*

**The mismatch**: `OvertimeGovernanceRule.cs` is a WARNING-only ceiling checker (S17/S35 surface; emits `ComplianceViolation`s with `Severity=WARNING`); it does NOT emit MERARBEJDE wage-line items. The actual MERARBEJDE emitter is `OvertimeRule.cs` (RuleId="OVERTIME_CALC"), invoked by `RuleRegistry.cs:220` in the rule-engine pipeline.

**Amendment decision**: `OvertimeRule.cs` (RuleId="OVERTIME_CALC") consumes the tri-state **INDIRECTLY** via the existing `HasMerarbejde` boolean disabler on `AgreementRuleConfig`. The role-override merge logic in `ConfigResolutionService` (per Seam C decision below) computes: when `merarbejde_compensation_right ∈ {DISCRETIONARY, NONE}`, the merged config returned to the rule pipeline has `HasMerarbejde=false`. `OvertimeRule`'s existing `if (HasMerarbejde && HasOvertime)` short-circuit (already in the code per S17) suppresses MERARBEJDE wage-line emission without any rule-side code change. The rule-engine layer stays **tri-state-naive** — it operates on the merged boolean disabler contract that has been the API since S17.

**`OvertimeGovernanceRule`** stays unchanged per its existing S17/S35 scope (WARNING-only ceiling checker). No tri-state consumption there.

**Consequences for S42 implementation**:
- TASK-4101 (ConfigResolutionService 4-layer extension) implements the **merge logic**: NONE → `HasMerarbejde=false`; DISCRETIONARY → `HasMerarbejde=false`; CONTRACTUAL → preserve agreement_configs default.
- TASK-4102 (rule-engine cutover) is now **a non-task**: zero changes to `OvertimeRule.cs` or any other rule file. New unit tests verify the merged-config short-circuit behavior (which is testable at the ConfigResolutionService boundary, not the rule).

### Seam B — DISCRETIONARY event-emit seam

**In-body text (L64 + L69)** said: *"For `DISCRETIONARY`, a new `MerarbejdeDiscretionary` event type is emitted (registration via PAT-004); admin UI surfaces it for manual one-off-payment trigger."* And: *"`PayrollMappingService.BuildLine` reads the tri-state."*

**The mismatch**: `PayrollMappingService.BuildLine` (`src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollMappingService.cs:217`) is an `internal static` pure constructor taking a `WageTypeMapping` parameter. It has no `AgreementRuleConfig` access, no DB access, no outbox enqueue capability. Cannot emit events as scoped.

**Amendment decision**: the `MerarbejdeDiscretionary` event-emit seam is **`PeriodCalculationService` orchestration** (specifically `MapCalculationResultAsync` at PCS.cs:1288 or the equivalent post-rule-engine orchestration point), NOT `PayrollMappingService.BuildLine`. PCS has DB + outbox + audit access via injected dependencies; BuildLine remains a pure-function builder.

**Detection logic in PCS**: for each segment, PCS looks up the role-merged config via the dated `ConfigResolutionService.ResolveAsync(...)` overload (Seam C). For any segment where `merarbejde_compensation_right = DISCRETIONARY` AND the segment carries overtime hours > 0, PCS emits `MerarbejdeDiscretionary` event in the atomic tx alongside payroll-line construction. The event payload carries `(EmployeeId, Date, MerarbejdeHours, EmploymentCategory)` per the S40 TASK-4004 event-class definition.

`BuildLine` signature unchanged. PCS owns the orchestration that ties together rule output + payroll line construction + event emission.

**Consequences for S42 implementation**:
- TASK-4103 (PayrollMappingService cutover) is now actually a **PCS cutover** — modify `PeriodCalculationService.MapCalculationResultAsync` to: (a) read role-merged config via Seam C signature; (b) inspect tri-state per segment; (c) emit `MerarbejdeDiscretionary` in atomic tx for DISCRETIONARY + overtime>0 segments; (d) audit-line entry `compensation_choice: 'DISCRETIONARY_PENDING_ADMIN'` for visibility.
- For `NONE`: PCS doesn't emit `MerarbejdeDiscretionary`; the rule-engine layer already suppressed MERARBEJDE emission per Seam A. Audit-line entry `compensation_choice: 'NONE_NO_ENTITLEMENT'` for visibility.

### Seam C — ConfigResolutionService signature change

**In-body text (L69)** said: *"`ConfigResolutionService.GetEffectiveConfig(employee_id, date)`"*.

**The mismatch**: actual signature at `src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:115` is `ResolveAsync(string orgId, string agreementCode, string okVersion, string? position, CancellationToken ct)`. No `employeeId`, no `date`, no `employmentCategory`. The "GetEffectiveConfig" name doesn't exist.

**Amendment decision**: introduce a new dated-lookup overload alongside the existing live-only overload:

```csharp
// Existing — preserved as live-only form for backward-compat (admin endpoints, JWT mint, current-period reads):
public async Task<AgreementRuleConfig> ResolveAsync(
    string orgId, string agreementCode, string okVersion, string? position,
    CancellationToken ct = default);

// NEW — dated-lookup form for replay-sensitive paths (PCS planner, payroll export, retroactive correction):
public async Task<AgreementRuleConfig> ResolveAsync(
    string orgId, string agreementCode, string okVersion, string? position,
    string? employmentCategory, DateOnly asOfDate,
    CancellationToken ct = default);
```

The dated overload routes through `RoleConfigOverrideRepository.GetByEmploymentCategoryAtAsync(employmentCategory, agreementCode, okVersion, asOfDate)` for the role-override layer. When `employmentCategory` is null or no row matches, falls through to position-override / local-config layers (existing behavior preserved).

**Migration path**:
- PCS callers switch to the dated overload (replay-sensitive — must use dated lookup per ADR-016 D10)
- `BalanceEndpoints` + `ComplianceEndpoints` + Skema/Overtime endpoints stay on the live-only overload for current-period reads (matches S33 ADR-023 D3 split: dated for replay, live for current)
- Admin endpoints stay on the live-only overload (admin UI is always current-period)
- JWT mint stays on the live-only overload

**Consequences for S42 implementation**:
- TASK-4101 (ConfigResolutionService 4-layer extension) implements the new dated overload + the role-override merge logic (per Seam A). Existing overload stays.
- Pattern reference: mirror S33's `IEmploymentProfileResolver.GetByEmployeeIdAtAsync(employeeId, asOfDate, ct)` dated-lookup signature.

### Seam D — employment_category determinism gap

**In-body text** (D1 L40) acknowledged the gap: *"`User.EmploymentCategory` ... becomes load-bearing. ... future Phase 4e work will move ok_version / employment_category / primary_org_id into dated history tables too."*

**The depth of the gap** (under-specified in D2): `EmploymentProfileResolver` (`src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs:116, 153`) joins `users.employment_category` LIVE (not dated), per its docstring L26-29. The dated `RoleConfigOverrideRepository.GetByEmploymentCategoryAtAsync(employmentCategory, asOfDate)` is the dated tri-state lookup — **but the `employmentCategory` argument itself is sourced from a live read**. If an admin promotes an employee Fuldmægtig → Chefkonsulent today, past-period replay of last month's calc would look up the role-override using TODAY's category (Chefkonsulent → NONE) instead of the period-effective category (Fuldmægtig → CONTRACTUAL) → byte-identity vs original manifest BREAKS.

**Amendment decision**: the gap is **EXPLICITLY DOCUMENTED** as a Phase 4e launch-blocking candidate. Pre-launch posture means no past periods exist that could expose the gap; the role-override + dated-tri-state lookup ships in S42 with the gap documented as a known caveat.

**Caveat text added to source register**: each `role_config_overrides` seed row's `bug_correction_history` annotation carries the new action `provisional-pending-phase-4e:employment-category-dating` (extends the enum at D3 L111 — the Phase E `bug_correction_history` schema validation test at `tests/StatsTid.Tests.Regression/PhaseE/BugCorrectionHistorySchemaTests.cs` will need updating to accept this action value).

**Phase 4e candidate sprint** (post-S42 audit-visibility close; launch-blocking): add dated `employment_category` via one of two options:
- **(a)** Extend `employee_profiles` table with a versioned `employment_category` column (small schema delta; piggybacks on S31 EmployeeProfile bitemporal pattern)
- **(b)** Introduce new `user_employment_categories` versioned-config table mirroring the S34 `user_agreement_codes` shape (cleaner separation; matches the 5 existing versioned-config repo pattern; 6th versioned-config repo overall)

**Recommended option**: **(b)** for consistency with established versioned-config repo pattern. ADR amendment defers the binding choice between (a) and (b) to the Phase 4e candidate sprint refinement.

**Pre-launch posture acknowledgement**: byte-identity replay determinism for chefkonsulent role-stratum changes IS NOT GUARANTEED until Phase 4e employment_category dating lands. The risk is purely theoretical pre-launch (no past periods to replay). Once the first customer goes live AND the first role-stratum change occurs AND that customer requests a past-period recompute (e.g., SLS correction for the affected period), the gap would expose. Phase 4e fix is launch-blocking for that scenario.

### Updated bug_correction_history action enum (per Seam D)

The D3 L111 enum gains:
- `provisional-pending-phase-b` (already in use per S40 TASK-4005 seed annotations)
- `provisional-pending-phase-4e:<feature>` (new; e.g., `provisional-pending-phase-4e:employment-category-dating`)

Phase E test at `BugCorrectionHistorySchemaTests.cs` updates to accept the new action values (regex pattern `provisional-pending-(phase-b|phase-4e:[a-z-]+)`).

### Consequences for sprint sequencing

- **S42 = ADR-024 D1+D2 cutover** (re-drafted refinement against amended ADR): TASK-4101 ConfigResolutionService dated overload + role-override merge logic (per Seams A + C); TASK-4102 deleted (no rule-engine code change per Seam A); TASK-4103 PCS event-emit cutover (per Seam B); TASK-4104 admin endpoints; TASK-4105 frontend; TASK-4106 source-register annotations for S40-seeded rows; TASK-4107 sprint close. **~7 tasks** — same ballpark as the original S41 refinement target.
- **S43 = ADR-024 Sub-Sprint 2b**: D7 necessity-ack endpoint + Approval UI + HK/PROSA Bug #4 seed flip + D6 generalized correct-as-bug endpoint pattern across 5 surfaces + source-register annotations.
- **S44 = ADR-024 Sub-Sprint 3**: D-test matrix + Phase E completion + WORKFLOW.md governance bake-in.
- **Phase 4e launch-blocking candidate** (post-S44): `employment_category` dating per Seam D option (a) or (b).

### Replay determinism load-bearing D-test (Seam A/B consequence)

ADR-024 D2 L71 + D7 L222-224 + L258 said the chefkonsulent past-period replay determinism D-test lands in **S41** (now S42 post-amendment). The amendment preserves this commitment. The D-test asserts byte-identity of `JsonSerializer.Serialize(segmentRuleResults)` between baseline forward-calc and replay-after-Case-C-supersession on the role_config_overrides row — analogous to S33 EmployeeProfile + S34 UserAgreementCode marquee D-tests. Caveat per Seam D: the D-test exercises the role_config_overrides dated lookup, NOT the employment_category dating; the latter is the Phase 4e candidate. The chefkonsulent D-test will pass post-S42 as long as the employment_category itself doesn't change between baseline calc and replay (which the test fixture controls).

### Cycle-trail record

This amendment is itself the smoke-alarm response artifact — preserved here at the bottom of ADR-024 rather than in `.claude/refinements/` so future readers of ADR-024 see the architectural decisions inline with the original D-decisions. The 4 cycles of same-area thrash that produced this amendment:

1. **S38b ADR-026 authorship** (resolved via path C event-projection; same dense-ADR-text class of issue)
2. **S40 refinement cycle 1** (7 BLOCKERs on tri-ADR S40 bundling) → user adjudication: split per-ADR
3. **S40 refinement cycle 2** (4 BLOCKERs on ADR-024-full bundling) → user adjudication: honor ADR-author sub-sprint split
4. **S41 refinement cycle 1** (3-4 architectural BLOCKERs surfacing this amendment) → user adjudication: amendment authorship via S41a

Lesson recorded: **dense ADR text requires line-by-line reading against the codebase BEFORE refinement drafting; ADR-author intent statements can mis-identify codebase surfaces (Seam A) or assume signatures that don't exist (Seam C) or ascribe capabilities to pure functions (Seam B) — the refinement process catches these but only when both lenses run with codebase-grounding criteria.**
