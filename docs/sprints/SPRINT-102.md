# Sprint 102 — Enhedsspor Org Model: Phase-0 ADR Design Sprint (ADR-038)

| Field | Value |
|-------|-------|
| **Sprint** | 102 |
| **Status** | complete |
| **Start Date** | 2026-06-28 |
| **End Date** | 2026-06-28 |
| **Orchestrator Approved** | yes — 2026-06-28 |
| **Build Verified** | n/a — design-only sprint (no code) |
| **Test Verified** | n/a — design-only sprint (test count unchanged from S101's 2584; no code/test change) |

## Sprint Goal
Produce **ADR-038 "Unit-Hierarchy Organisational Model (Enhedsspor)"** (ACCEPTED), settling the org + reporting domain re-architecture the owner has approved (Strategy A) — a 7-level typed unit hierarchy, single-unit membership, explicitly-designated unit leaders, and reporting presented as derived — **before any implementation**. This is a **design-only sprint** (no code, no schema, no test change), mirroring the S28 / S32 / S38b ADR-design-sprint precedent. It is Phase 0 of the multi-phase Enhedsspor program; implementation sprints follow only after ADR-038 is ACCEPTED. Refinement: `.claude/refinements/REFINEMENT-merged-admin-page.md` (Strategy A locked; Step-4 dual-lens reviewed, 1 BLOCKER resolved).

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Spot-check of the ADRs in scope (035/036/037/008/027/026) + refinement paths resolve. |
| Pattern compliance spot-check | CLEAN | `grep FindFirst("scopes")` / `FindFirst(StatsTidClaims.Scopes)` in `src/` → 0 hits (FAIL-001 clean). Design-only sprint adds no endpoints/events to spot-check. |
| Orphan detection | CLEAN | No new files created this sprint (design-only). |
| Documentation drift | DEBT | The transitional `enhed_label` column (S97-deferred drop) still has consumers in `AdminEndpoints.cs` / `EmployeeProfileEndpoints.cs` / `ApprovalPeriodRepository.cs` / `EmployeeProfileRepository.cs` / `Program.cs`. Tracked debt; **subsumed by this program** (the Enhedsspor migration supersedes the enhed_label/enhed model wholesale) — do not spot-fix. |
| Quality grade review | CLEAN | No domain quality change this sprint (design-only). |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 architectural integrity + P3 event-sourcing + P4 version + P7 security + schema). Even though design-only, the ADR it produces governs all four — review the *plan's framing of the design agenda*. |
| **External Codex** | invoked 2026-06-28 — 1 cycle, 0B/2W/2N |
| **Internal Reviewer** | invoked 2026-06-28 — 1 cycle, 0B/2W/3N |
| **BLOCKERs resolved before Step 1** | n/a — 0 BLOCKERs either lens. All WARNINGs/NOTEs absorbed as plan edits (mechanical sharpening; no architecture change → no cycle-2 verification required, per `feedback_thrash_defer_real_world.md` no-thrash). |

### Findings (cycle 1)

_Codex (external) — 0B / 2W / 2N:_
- WARNING — TASK-10204 — "may spawn sibling ADRs" is a loophole to defer a Phase-0 deliverable out of scope. → tightened: siblings may only split presentation/ownership; ADR-038 cannot be ACCEPTED until all deliverables (units + #1–#8) are decided in ADR-038 or same-sprint ACCEPTED cross-linked siblings.
- WARNING — TASK-10203 — audit-projection parity underspecified (named EventSerializer but not the DEP-003 + audit-projection-catalog / mapper parity surface). → added DEP-003 + `docs/operations/audit-projection-catalog.md` refs + a full event-registration/audit-parity criterion.
- NOTE — TASK-10204 — add an explicit Phase-0 coverage checklist criterion (each deliverable settled with decision + rejected options + rationale + test hooks). → added.
- NOTE — deliverable assignment confirmed complete (10201 = units+#6; 10202 = #1/#2/#3/#5 + the leaderIds fork; 10203 = #4/#7/#8); ordering + design-only framing sound.

_Internal Reviewer — 0B / 2W / 3N:_
- WARNING — TASK-10204/10205 — the mechanical close-guard requires `.claude/reviews/SPRINT-102-step7a-{codex,reviewer}.md` with `verdict:` + a HEAD-prefixing `reviewed-against-commit:`; the S38b precedent predates the staleness line. → added the artifacts to TASK-10204 Components + a TASK-10205 close-gate criterion.
- WARNING — TASK-10202(a) — the keystone fork must separate SCOPE authority (stays Organisation-LOCKED, unconditional, RED-pinned) from the reporting EDGE (direct-member approval only); a `leaderIds` designation may at most materialize the direct-member edge, NEVER deep-tree scope. → criterion sharpened.
- NOTE — TASK-10202(c) — pin computed-on-read vs denormalized-stored `organisation_id` (lean: denormalized-stored, recomputed on move — also answers the no-unbounded-scan + replay criteria). → pinned.
- NOTE — disposition completeness: name ADR-010/014 + `role_assignments` + `projects` (preserve via the derived anchor) + an ADR-026 forward-ref. → added to TASK-10204.
- NOTE — cross-reference the migration splits (semantics in 10201 vs mechanics in 10203; unit-move locking in 10202(d) vs path recompute in 10203) so neither half is settled in isolation. → added.

### Resolution
All four WARNINGs and the substantive NOTEs were absorbed as plan edits to TASK-10202/10203/10204/10205 below (recorded inline). 0 BLOCKERs → cleared for Step 1. No cycle-2 dual-lens re-run: the edits are mechanical sharpening of criteria/refs/close-gate wiring with no architectural change (the architecture itself was pressure-tested at the refinement Step-4).

## Architectural Constraints Verified
- [x] P1 — Architectural integrity: the program opens with an ADR design gate; no code until ADR-038 ACCEPTED (protects #1).
- [ ] P2 — Rule engine determinism: untouched (no rule logic in scope; agreement *rules* unchanged).
- [x] P3 — Event sourcing: the ADR must settle the new event model + replay determinism + audit-projection parity before code.
- [x] P4 — Version correctness: the ADR retains the stored-edge `version` substrate (deliverable 1) so OK/If-Match version correctness is preserved by design.
- [ ] P5 — Integration isolation: untouched this sprint.
- [x] P6 — Payroll correctness: the ADR must preserve attribution via a derived `organisation_id` home (deliverable 3) — verified at design time, implemented later.
- [x] P7 — Security: the LOCKED Organisation authority boundary (deliverable 2) is the keystone against re-opening the S76/S85/S91 subtree-inheritance bug class.
- [x] P8 — CI/CD: design-only; the implementation phases carry their own CI gates.
- [x] P9 — Usability: the Enhedsspor design is the UX driver, subordinate to #1–#7 per the priority order.

## Task Log

### TASK-10200 — Sprint open: refinement, entropy scan, Phase-0 plan, Tier-2 impact assessment
| Field | Value |
|-------|-------|
| **ID** | TASK-10200 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | docs/sprints, ROADMAP.md, .claude/refinements |
| **KB Refs** | ADR-035, ADR-036, ADR-037, ADR-008, ADR-027, ADR-026 |
| **Orchestrator Approved** | yes — 2026-06-28 |

**Description**: Refined the owner's "merge org + medarbejdere" request to the real intent — adopt the Enhedsspor design's domain model fully (Strategy A). Ran Step-4 dual-lens on the refinement (1 BLOCKER resolved: retain the stored reporting edge as the materialized projection). Opened S102 as a design-only Phase-0 ADR sprint. Authored the ROADMAP Tier-2 impact assessment.

**Validation Criteria**:
- [x] Refinement READY, Strategy A locked, Step-4 BLOCKER resolved + re-verified.
- [x] Entropy scan recorded.
- [x] ROADMAP Tier-2 impact assessment written (program shape + affected phases).

---

### TASK-10201 — ADR-038 DRAFT: the unit model + membership
| Field | Value |
|-------|-------|
| **ID** | TASK-10201 |
| **Status** | complete (settled in ADR-038 D1/D2/D3) |
| **Agent** | Orchestrator (KB authorship; may spawn research agents for option exploration) |
| **Components** | docs/knowledge-base/decisions/ADR-038 |
| **KB Refs** | ADR-008 (materialized path), ADR-036 (Enhed), ADR-037 (org lifecycle) |

**Description**: Draft ADR-038's unit-model decisions: the `units` table (7 ordered types ministeromrade→…→enhed via the `CHILD` map; `parent_id`; lifecycle/move/delete); single-unit membership (`person.unitId` at any depth) replacing Organisation-home + multi-enhed-tags; explicit `leaderIds` designation + promote semantics; and the **membership-migration semantics** (which of a user's current enhed tags becomes the structural unit; fate of secondary tags). Resolves Phase-0 deliverables: units table + #6 (membership migration).

**Validation Criteria**:
- [ ] `units` schema shape + the 7-type ordering + lifecycle decided, with options + rationale.
- [ ] Single-unit membership + the `leaderIds`-vs-reporting distinction recorded.
- [ ] Migration semantics for multi→single membership recorded (no silent data loss).
- [ ] ADR-036 (zero-authority Enhed) disposition stated (supersede/amend).

---

### TASK-10202 — ADR-038: authority, reporting & concurrency (the security-critical core)
| Field | Value |
|-------|-------|
| **ID** | TASK-10202 |
| **Status** | complete (settled in ADR-038 D4/D5/D6/D8) |
| **Agent** | Orchestrator (KB authorship) |
| **Components** | docs/knowledge-base/decisions/ADR-038 |
| **KB Refs** | ADR-035 (flat authority), ADR-027 (reporting/approval/vikar/locking), docs/SECURITY.md (S76/S78/S83 invariants) |

**Description**: The keystone task. Settle: **(a)** the LOCKED Organisation authority boundary (deeper units grant NO scope; pinned by a RED test in implementation) — resolving the keystone fork **"does designating someone in `leaderIds` grant approval authority, or is it presentational over the edges?"**; **(b)** the stored-edge retention (keep `reporting_lines` + `manager_vikar` as the materialized projection of `unitId`+`leaderId`, preserving the version/`organisation_id` lock/revoke substrate — deliverable 1); **(c)** the derived `organisation_id` home for scope/config/attribution under deep membership (deliverable 3); **(d)** two-regime concurrency (within-Organisation S100 advisory vs cross-Organisation FOR-UPDATE — deliverable 5). Resolves the ADR-035 + ADR-027 disposition and protects priorities #1/#4/#7.

**Validation Criteria**:
- [x] The keystone `leaderIds`-authority fork **DECIDED with the owner 2026-06-28**: `CanApprove(actor, E)` = actor is E's **primary leader** (E's `leaderId` → the existing `reporting_lines` PRIMARY edge — the DEFAULT) **OR** a **secondary/peer leader of E's own unit** (`E.unit.leaderIds`, the EXCEPTION path — so `leaderIds` IS authority-bearing, BOUNDED to the unit's own direct members) **OR** an active **vikar** of such a leader (`manager_vikar`) **OR** HR/Admin in scope. **SCOPE authority stays Organisation-LOCKED** (unconditional, RED-test-pinned — a parent unit's leader grants NO subtree `ValidateEmployeeAccess`; no deep-tree inheritance — the S76/S85/S91 guard). ADR-038 extends `DesignatedApproverAuthorizer` with the same-unit-secondary-leader path. Vikar scope CONFIRMED 2026-06-28: **same Organisation** (matches the existing `manager_vikar` constraint; S76/S83 guarantees preserved — no loosening).
- [ ] Stored-edge retention vs edgeless-rewrite decided (lean: retain), with the substrate (version/org anchor/advisory/revoke) traced.
- [ ] Derived `organisation_id` home decided — **pin computed-on-read vs denormalized-stored** (lean: denormalized-stored, recomputed on move; satisfies the no-unbounded-scan + replay-determinism criteria); every `primary_org_id` consumer class named (payroll/settlement/audit + config: `local_configurations`/agreement-config ADR-010/014, `role_assignments`, `projects`).
- [ ] Two-regime concurrency boundary recorded (cross-reference the unit-move locking here with the `materialized_path` recompute in TASK-10203 — settle the move operation as one decision, not two halves).

---

### TASK-10203 — ADR-038: migration, events, audit-projection & vikar reconciliation
| Field | Value |
|-------|-------|
| **ID** | TASK-10203 |
| **Status** | complete (settled in ADR-038 D9/D10/D11/D12) |
| **Agent** | Orchestrator (KB authorship) |
| **Components** | docs/knowledge-base/decisions/ADR-038 |
| **KB Refs** | ADR-026 (audit projection), ADR-018 (outbox), DEP-003 (EventSerializer registration parity), `docs/operations/audit-projection-catalog.md`, legacy-db-upgrade-runbook |

**Description**: Settle the migration + event design: new `Unit*`/membership/leader events + `EventSerializer` name-keying + replay determinism; the `materialized_path` roster-consumer re-anchoring (`GetMedarbejderRosterForTreeAsync` / `GetPeriodStatusProjectionForTreeAsync` + ADR-037 D2 path recompute — deliverable 4; cross-reference the unit-move locking regime in TASK-10202(d)); audit-projection migration (backfill idempotency, `target_org_id` FK compatibility, tenant-visibility parity after replay — deliverable 7); and vikar reconciliation (keep `manager_vikar` authority-bearing, render the design's display-only way; parity tests named — deliverable 8). Resolves the ADR-008 + ADR-026 disposition. (Migration MECHANICS here cross-reference the membership-migration SEMANTICS in TASK-10201 — settle the two halves together.)

**Validation Criteria**:
- [ ] Event model + FORWARD replay determinism decided; the **full event-registration/audit-parity contract** recorded — not just event names: each new event has an `EventSerializer` type-map entry (DEP-003), an `IAuditProjectionMapper` + registration + an audit-projection-catalog row (ADR-026), per PAT-004.
- [ ] **Data strategy = greenfield reseed (no migration)** recorded (owner-decided 2026-06-28; pre-launch test-only data → drop-and-recreate via init.sql + demo seed; no backfill/replay-across-migration/audit-history-preservation).
- [ ] `materialized_path` consumers enumerated + re-anchoring approach recorded (Organisation-level consumers unchanged; D11).
- [ ] Vikar reconciliation + parity tests recorded.

---

### TASK-10204 — ADR-038 dual-lens design review → ACCEPTED
| Field | Value |
|-------|-------|
| **ID** | TASK-10204 |
| **Status** | complete |
| **Agent** | Orchestrator (spawns Codex + Reviewer) |
| **Dual-lens review** | 2 cycles. Cycle 1: Codex 2B/1W/1N + Reviewer 2B/3W/3N (4 distinct BLOCKERs: D2 NULL-`unit_id` home; D4 see==act read-side; D3/D4 `unit_leaders` not revalidated on a leader's move; D6/D8 cross-Org transfer lock-regime composition). All absorbed. Cycle 2: Codex all RESOLVED + no new BLOCKER; Reviewer all RESOLVED + 1 WARNING (designate-vs-move race → `unit-org-` advisory on leader designation) + 1 citation nit, both absorbed. **0 residual BLOCKERs; cycle-cap (2/lens) respected.** ADR-038 DRAFT → ACCEPTED 2026-06-28. |
| **Components** | docs/knowledge-base/decisions/ADR-038, docs/knowledge-base/INDEX.md, `.claude/reviews/SPRINT-102-step7a-{codex,reviewer}.md` |
| **KB Refs** | all in-scope ADRs |

**Description**: Run the in-sprint dual-lens review on the ADR-038 DRAFT (the S28/S32/S38b design-sprint Step-7a-equivalent): Codex (external) + Reviewer Agent (internal), cycle-capped. Absorb BLOCKERs. Flip ADR-038 DRAFT → ACCEPTED. Add ADR-038 to KB INDEX. Apply supersession disclaimers/disposition notes to ADR-035/036/037/008/027 + a forward-reference to ADR-026 (audit projection — preserve+migrate). **Sibling-ADR constraint:** ADR-038 may spawn sibling ADRs (ADR-039/040) ONLY to split presentation/ownership — it cannot be ACCEPTED until all Phase-0 deliverables (units table + #1–#8) are decided, either inside ADR-038 or in same-sprint ACCEPTED siblings cross-linked from ADR-038 (no deliverable may be deferred out of the sprint). S38 → ADR-024/025/026 precedent.

**Validation Criteria**:
- [ ] Dual-lens review run; BLOCKERs absorbed; cycle-cap respected. The Step-7a artifacts `.claude/reviews/SPRINT-102-step7a-{codex,reviewer}.md` exist, each with a `verdict:` line + a `reviewed-against-commit: <SHA>` that prefixes the close-commit HEAD (post-S38 staleness contract; the S38b precedent files predate this line — do not copy their format).
- [ ] **Phase-0 coverage checklist:** the ADR review verifies the units table + deliverables #1–#8 are EACH settled with a decision, the rejected options, the rationale, and the implementation test hooks where applicable.
- [ ] ADR-038 ACCEPTED; KB INDEX updated; supersession recorded on ADR-035/036/037/008/027 + ADR-026 forward-ref + the ADR-010/014 / `role_assignments` / `projects` preserve disposition recorded in ADR-038.

---

### TASK-10205 — Sprint close (design-only): ROADMAP promotion + retrospective
| Field | Value |
|-------|-------|
| **ID** | TASK-10205 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | docs/sprints/INDEX.md, ROADMAP.md |
| **KB Refs** | — |

**Description**: Close the design-only sprint: INDEX row + anchor bump; ROADMAP promotion of the first implementation sprint (S103) to detailed planning; Step-7a artifacts (design-sprint variant). Test count unchanged (design-only contract).

**Validation Criteria**:
- [ ] INDEX updated (S102 row + `anchor-sprint` bump); ROADMAP next-sprint (implementation Phase 1) promoted.
- [ ] Close-guard (`sprint-close-guard.ps1`) satisfied: `.claude/reviews/SPRINT-102-step7a-{codex,reviewer}.md` each carry a `verdict:` line + a `reviewed-against-commit: <SHA>` prefixing HEAD; CI-health not red; the `**Test Verified**` line records the design-only (unchanged-count) status.

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | Design-only; agreement *rules* unchanged. Attribution preservation is a Phase-0 design criterion (deliverable 3), implemented later. |
| Wage type mappings produce correct SLS codes | N/A | Unchanged. |
| Overtime/supplement calculations are deterministic | N/A | Unchanged. |
| Absence effects on norm/flex/pension are correct | N/A | Unchanged. |
| Retroactive recalculation produces stable results | N/A | Replay determinism under the new model is a Phase-0 design criterion (TASK-10203). |

## External Review (Step 7a)
_Design-sprint variant: the dual-lens ADR review (TASK-10204) is the Step-7a-equivalent (S28/S32/S38b precedent). Recorded at close._

## Test Summary
| Suite | Count | Status |
|-------|-------|--------|
| (design-only) | 2584 | unchanged from S101 — no code/test change |

## Sprint Retrospective

**What went well**: The refinement gate caught that the "merge two pages" request was actually a domain-model re-architecture before any planning; the owner chose Strategy A eyes-open. The greenfield-reseed decision (no migration) removed the single biggest risk surface mid-design. The dual-lens ADR review earned its keep again — 4 BLOCKERs on the markdown ADR (NULL-`unit_id` home, see==act reads, `unit_leaders`-on-move, cross-Org transfer lock composition) that would each have been a schema/auth bug if found in code. The 2+5-split (D1) reconciled "adopt fully" with the locked Organisation boundary + the ~100-consumer blast radius.

**What to improve**: The keystone domain fork (leaderIds-authority) was first framed as abstract A/B/C and the owner declined it to answer in operational terms — recorded as `feedback-design-forks-plain-language.md` for next time.

**Knowledge produced**: ADR-038 (ACCEPTED) — Enhedsspor unit-hierarchy org+reporting model; supersedes ADR-035/036, amends ADR-037/008/027, preserve+migrate ADR-026. Disposition notes stamped on all five affected ADRs.
