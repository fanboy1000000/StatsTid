# Sprint 140 — QUAL-154 second-tranche fixed clock + the HR follow-up surface (shape 3)

| Field | Value |
|-------|-------|
| **Sprint** | 140 |
| **Status** | in-progress |
| **Start Date** | 2026-09-08 |
| **End Date** | — |
| **Orchestrator Approved** | **plan: yes — 2026-09-08** (Step 0b terminal: Codex READY at cycle 3; Reviewer APPROVED-WITH-WARNINGS at cycle 3, all absorbed; one owner ruling raised by the plan review, OQ-4) · refinement `.claude/refinements/REFINEMENT-s140-qual154-increment4-hr-landing.md` **rev 4** READY (Reviewer APPROVED-WITH-WARNINGS at cycle 2, all absorbed; Codex READY at cycle 4 after the cycle cap — owner chose "apply both fixes; run cycle 4"); owner rulings 2026-09-08 **OQ-1 (a)** S140 = QUAL-154 + the HR follow-up surface, Increment 4 whole in S141 · **OQ-2 (a)** keep the `GlobalAdminOnly` recalculate gate, action shown only to Global Admins · **OQ-3 (a)** rolling 12-month floor on HRP-011/012/022 · **OQ-4 (a)** (raised by the Step-0b Reviewer, ruled 2026-09-08) read-only HR lists in S140; the four API-only process actions become S141 items · **OQ-5 (a)** (raised by the Step-5a Reviewer, ruled 2026-09-08) convert the cross-org transfer's business date now, paired with a probe leg → TASK-14009 · **OQ-6 (a)** (raised by the Orchestrator's blind-spot sweep, adjudicated by Step-5a cycle 2, ruled 2026-09-09) the vikar start date: register now (**QUAL-164**), the `effective_from` schema fix is its own task, NOT folded into S140 |
| **Build Verified** | **wave 1: yes** — `dotnet build StatsTid.sln -c Release --no-incremental` **0 errors, 145 warnings** (baseline), Orchestrator's own run, twice. The 175-warning figure one agent saw is an incremental-build artefact. CA2100 distinct sites to be re-measured on the close's clean full build |
| **Test Verified** | **wave 1: partial** — Unit **1235** · DemoSeed **165** · non-Docker regression **102** (Orchestrator's own runs; the 144/145 in two agent reports are wrong, a shared-`bin/Release` race between concurrent agents). Docker unavailable locally (standing): the six converted suites, the four probe legs and the new 1-September fact are **CI-verified at close, not claimed green** |
| **Wave-1 commit** | `bcca33d` (2026-09-09, local only — not pushed; push happens at Step 7 after Step 7a). Committed before wave-2 worktrees per the S24 lesson. **Consequence for Step 7a:** an intermediate commit now exists on master, so the sprint-end review must use the base-anchored form `codex review --base a87f6c1`, which loses the project-specific steering prompt (AGENTS.md § Invocation Modes). The internal Reviewer keeps its full prompt, and the per-task Step-5a passes already ran prompt-steered on the uncommitted diffs |
| **Orchestrator model** | Open — refinement (4 Codex cycles, 2 Reviewer cycles), Steps 0a / 0b / 1, this log: **Fable 5.1** ✓ · Dispatch, monitoring, acceptance bookkeeping, CI watch (Steps 2–4, 6): **Opus 5 — switch TAKEN at "dispatch wave 1" (2026-09-08)**, the routing rule's first honoured Orchestrator switch (S139 offered and declined it) · Step-5a / 7a absorption and every ruling on a declared deviation: **Fable** · Close bookkeeping + CI backfill: **Opus**. Agents spawned by role name, no `model` override (`docs/WORKFLOW.md` § Model Routing, second live run) |
| **Sprint-start commit** | `a87f6c1` (S139 CI-green backfill) — the `codex review --base` fallback anchor for Step 7a |

## Sprint Goal

Two deliverables the owner ruled, in a fixed order.

**Part A — QUAL-154 first.** Six date-sensitive regression suites still read the wall clock because their product paths read it
in places the S139 fixed test clock could not reach. S140 widens the clock seam (three product sites: the delegation-expiry sweep's
SQL `CURRENT_DATE`, the designated-approver authorizer's fallbacks, the approval / skema / reporting-line "as-of today" reads),
converts the six suites to constant anchors with RED conditions, adds a falsifiability-probe leg per new seam, and closes the
secondary `.Month/.Year/.DayOfWeek` scan by evidence (three calendar-dependent suites found, all already among the six). Why first:
Part B writes NEW date-sensitive suites (deadline aging, "from 1 November") on these very paths — they must be born on a fixed
clock. It also defuses a real hard failure: `EntitlementQuotaCheckUsesYearStartTests` fails outright every 1 September.

**Part B — the HR follow-up surface.** Today HR has no follow-up surface: the S139 register found fifteen hand-offs, nine with no
screen at all, and four with a ruled deadline nobody computes. S140 builds the owner's shape 3: one HR landing page
(`/admin/opfoelgning`), a tile per process (open count + age of the oldest item), each process's own list behind the tile — nine
year-round tiles plus the §21 tile in its window state. **The lists are read-only in S140 (owner ruling OQ-4):** the process actions
(reconcile payout; resolve a flagged settlement; record a §21 agreement; settlement reversal) have no frontend today and become named
S141 items; the one write built is the backdate worklist's Resolve against the existing S138 endpoint. The three deadline rules unlocked by the owner's cutoff
ruling (HRP-011 leaver's final month · HRP-012 employee late / approver late · HRP-022 approved-not-exported) were lens-reviewed in
the refinement and are ratified with it. The backdate worklist gets its screen. Two Increment-4 owed items that need no design
ride along (admin-create hire date; routing the finished overtime page). **Increment 4 itself stays whole and follows in S141**
(terminate / schedule a future change / view history, behind the "current ≠ live" read-model precondition).

**Inputs:** the refinement rev 4 (dual-lens converged: Codex c1 3B/5W/3N → c2 2B/2W → c3 1B/1W → c4 READY; Reviewer c1 2B/6W/7N → c2
0B/5W/7N — every item absorbed; the lenses converged independently on the same three rev-2 defects: the §21 reuse seam, the
HRP-007 reversal pin, the lookback floor) · the S139 post-close owner rulings (shape 3; §21 reminder from 1 Nov; +2/+5 ratified with
export cutoff = manager deadline; QUAL-154 first) · `docs/operations/hr-follow-up-process-register.md` § Increment-4 inputs /
§ Unlocked by ruling 3 · QUAL-154…163 · ADR-034 D4 · ADR-033 D5/D8/R7b · ADR-040 D8 · PAT-008 / PAT-026 / PAT-028. S139 CI green
(`34148997832`, backfilled `a87f6c1`); the CI-health gate is satisfied at open (latest completed master run `34196927619` success;
`34202802722` in progress on the docs-only backfill push at open time).

## Entropy Scan Findings (Step 0a)

Run 2026-09-08 by the Orchestrator (greps, no agent).

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | **CLEAN** | 40 unique `src/ tests/ frontend/src/ tools/ docker/` paths cited in KB entries checked on disk; 2 heuristic hits, both annotated non-files: PAT-010's elided `tests/.../Contracts/ContractAssert.cs` (the real file is `tests/StatsTid.Tests.Regression/Contracts/ContractAssert.cs`) and ADR-018:449's `EventStoreInterfaceTests.cs`, which the ADR itself marks "never created; the Regression test carries the coverage" |
| Pattern compliance spot-check | **CLEAN** | `FindFirst("scopes")` 0 hits (FAIL-001); hardcoded `http://localhost` in non-test `src/` 0; every `Map*` without `RequireAuthorization` is a `/health` probe or `/api/auth/login` (anonymous by design — 6 files checked); direct Rule Engine references from non-RuleEngine `src/` 0 (PAT-005) |
| Orphan detection | **CLEAN** | 10 non-test files added since S137's close; 2 flagged by the filename heuristic (`Contracts/BackdateWorklistResponses.cs`, `Contracts/UserAgreementCodeContracts.cs`) — false positives: every type they declare is used by its endpoint file |
| Documentation drift | **CLEAN** | ROADMAP's HR item and the QUAL-154 row reflect S139's rulings; QUALITY.md was re-graded at S139 close |
| Quality grade review | **CLEAN** | stable — Backend API A− (held), Frontend B; S140's fixes to QUAL-162/163 are candidates for the close re-grade |
| **QUAL-154 pinned scans (re-run, the evidence the refinement relies on)** | recorded | `\.(Month\|Year\|DayOfWeek)\b` over `tests/**/*.cs`: **50 files** (top: `SkemaFullDayOnlyGuardTests` 19, `Adr032RevaluationTests` 15, `HrBackdateWorklistRepositoryTests` 9 — all constant-anchored or data-only per the S139 census) · raw clock `DateTime\.(Today\|UtcNow\|Now)\b\|DateOnly\.FromDateTime\(DateTime\.`: **82 files** · the six deferred suites: `ManagerVikarEngineTests` 17 raw / 0 month, `DesignatedApproverAuthorityTests` 23 / 0, `AdminVikarOnBehalfTests` 3 / 0, `EmploymentDateGuardTests` 1 / 1, `EmploymentEndDateLifecycleTests` 1 / 1, `EntitlementQuotaCheckUsesYearStartTests` 1 / 2 — matching the S139 census. Product raw business-date sites on their paths (the TASK-14001 list): `ApprovalEndpoints.cs` :264 :319 :475 :505 :823 :1216 :1561 :1592 · `ReportingLineEndpoints.cs` :1272 :1679 :1758 :2092 :2232 :2523 (+ six `CreatedAt` audit stamps that stay) · `ApprovalPeriodRepository.cs` :238 :488 · `SkemaEndpoints.cs` :218 · `DesignatedApproverAuthorizer.cs` :147 :322 · `ReportingLineRepository.cs` :1043, SQL :1287 (+ `:276` audit stamp stays) · `DelegationExpiryService.cs` SQL :86 |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | **MANDATORY** — Part A touches security-adjacent authorization code (the designated-approver authorizer, approval as-of reads) and domain-date semantics; Part B adds HR reads across employees (security & access control) and a read-only cross-context lookup of the payroll export ledger (ADR-034 D4; payroll boundary) |
| **External Codex** | invoked 2026-09-08 — cycle 1: **0 B / 3 W / 0 N** (`codex exec` plan mode, `< /dev/null`; artifacts `.claude/reviews/SPRINT-140-step0b-codex-c{1,2,3}.md`) · cycle 2: all 3 RESOLVED, **0 B / 1 W / 2 N** (the Sprint Goal still said "with its actions" — fixed) · cycle 3: **READY — clean, no new findings** (terminal) |
| **Internal Reviewer** | invoked 2026-09-08 — cycle 1: **BLOCKED, 1 B / 4 W / 7 N** (`reviewed-by-model: claude-fable-5-1`) · cycle 2 (resumed after a session rate limit): cycle-1 items RESOLVED (W-2 PARTIAL), **BLOCKED, 1 B / 1 W / 4 N** on the edited text — edited · cycle 3: **APPROVED-WITH-WARNINGS, 0 B / 1 W / 1 N** — every cycle-2 item RESOLVED; W-1 (refinement B5 still said "showing the exact payload" — spec drift against the plan's cell) and N-1 (the QUAL-163 vitest must mock divergent values) absorbed as the lens prescribed, before any wave-2/3 dispatch; wave 1 cleared for dispatch (terminal) |
| **BLOCKERs resolved before Step 1** | **yes** — cycle-1 B-1 ruled by the owner as **OQ-4: read-only lists in S140, the forms in S141**; cycle-2 B (the Recalculate card) edited and verified RESOLVED at cycle 3 by both lenses |

### Findings (cycle 1) and resolution

*External (Codex):*
- WARNING — waves 2/3 — 14003/14004 could be accepted before their security and aging predicates were tested (14005 runs after the merge). **Edit:** acceptance = Step 5a terminal AND the 14005 pins green in the watched CI run; merge for integration may precede acceptance, the acceptance record may not.
- WARNING — wave 3 — 14006 imports `WorklistList.tsx` that only exists in 14007's parallel worktree; 14006 could not pass `tsc` on its own. **Edit:** 14007 has no backend dependency (its APIs exist) and moves to wave 2; 14006 starts with the component merged.
- WARNING — TASK-14006 validation — "nine vs ten tiles' states" reintroduced the ambiguity the refinement had settled. **Edit:** TEN tiles in both October and November; the §21 tile closed in October, open in November.

*Internal (Reviewer):*
- **BLOCKER** — TASK-14006/14007 — "the process's existing action" (reconcile payout; resolve settlement; record §21; reversal path) does not exist in the frontend (zero callers of those routes outside generated types and tests; the register records HRP-010 as API write-only). The task silently contained four new write forms, two domain-heavy, for a Sonnet UX agent with no criteria and no domain review. **Owner ruling OQ-4 (asked one question at a time): read-only lists in S140 with "handled via API today; UI follows in S141"; the four forms become named S141 items.** Plan and refinement B4/B5 edited.
- WARNING — Part-B shared rules — the org-scope predicate named the helper but not the COLUMN; `approval_periods.org_id` / `manager_vikar.organisation_id` are stamped at send / delegation and drift on transfer. **Edit:** the subject employee's current `users.primary_org_id` (the approver's for 013/014), exactly the worklist shape (`HrBackdateWorklistRepository.cs:623`); the cross-org pin seeds the divergence so a wrong column goes RED.
- WARNING — TASK-14007 / OQ-2 (a) — the Recalculate button cannot reach the Payroll host: the frontend proxies only to Backend.Api, the Payroll host has no CORS or frontend caller, and the typed client is generated from Backend.Api's spec. **Edit (option a, keeps the payroll boundary as ruled):** a Global-Admin-only, labelled, NON-CALLING instruction showing the exact payload; the wiring is a named residual.
- WARNING — TASK-14002 — a converted fact that mixes the fixed host and the real-clock base host flakes (two `DelegationExpiryService` sweeps on one container) or passes for the wrong reason. **Edit:** one host per fact; Step 5a greps for `_factory.CreateClient()` residue.
- WARNING — wave 2/3 merge hazards — the `WorklistList` dependency (above) and a second merge point: repository DI registrations in `Program.cs`. **Edit:** both named in the wave text.
- NOTES absorbed: `RequireRole` renders a 403 card, no redirect, and nav entries live in `Sidebar.tsx` (14006/14007 wording); event predicates use camelCase JSON keys and pins seed through the real emit path (14003/14004/14005); the `InstitutionalDeadlines` grep is `AddDays\((2|5)\)` with a RED-first Unit fact and the file in the Constraint Validator scope (14004); `ComplianceEndpoints.cs:79` → QUAL-155 (14008); `EmploymentEndDateCorrectionGuardTests` named in the census evidence (14008); "no END-reason column" wording (14008). N-7 verified clean: model routing, KB refs, role gating, all 22 Part-A sites.

### Findings (cycle 2) and resolution

*External (Codex):* all three cycle-1 WARNINGs RESOLVED. WARNING — the Sprint Goal still promised "its actions behind the tile",
contradicting OQ-4 — **fixed**. NOTES: the non-calling Recalculate instruction is an acceptable transitional shape under OQ-2 (a);
scope column, divergence pin, one-host rule, merge points and regeneration commands verified consistent.

*Internal (Reviewer):* B-1, W-1, W-3, W-4, N-1…N-6 RESOLVED (re-verified against the code); W-2 PARTIAL → the new BLOCKER.
- **BLOCKER** — TASK-14007 — the cycle-1 fix said the Global-Admin card "shows the exact `POST /api/payroll/recalculate` payload for
  the row". Impossible and misleading: the Payroll `RecalculateRequest` (`Program.cs:548-575`) needs profile, entries, absences,
  period and flex balance the row does not carry, so a UX agent could only invent a body or build a browser-side payroll client; and
  rows the system already flags `recalcBlockedBy` would have shown a run-instruction for a recalculation that yields wrong wage-type
  codes. **Edit:** the card names process, row identity and the Payroll-host endpoint + gate, points to the contract the operator
  assembles, renders NO payload; a blocked row shows "Genberegning blokeret — ⟨ids⟩" with Dismiss only; vitest pins both; the Legal &
  Payroll row now reads "no recalculation call from the UI". (The Reviewer's alternative — no card at all, hint only — also honours
  OQ-2 (a); the Orchestrator kept the instruction card because the owner ruled the action visible to Global Admins.)
- WARNING — TASK-14007 was "no backend dependency" but its QUAL-163 relabel reads `pendingPastDeadlineCountByManager`, which 14004
  creates in the same wave (0 hits today); typed against the current `api-types.ts` it could only pass `tsc` by a cast. **Edit:** the
  relabel and its vitest move to TASK-14006 (wave 3, after regeneration).
- NOTES absorbed: refinement B4 still said TopNav/redirect (edited to Sidebar / 403 card); the `_factory` citation is `:317-1058`
  (19 uses); OQ-4 added to the header's rulings; "one write" glossed as "one NEW write action" (the hire-date field extends an
  existing create POST, `AdminEndpoints.cs:941-942`).

### Findings (cycle 3) — terminal

*External (Codex):* the cycle-2 WARNING RESOLVED; the Recalculate card's contract fields and `recalcBlockedBy` semantics verified
("no payload is implied, blocked rows suppress the instruction, the card should not mislead a Global Admin"); the QUAL-163 field
confirmed absent today (0 hits in `src/` and `frontend/src/`), so the move to wave 3 is right; Legal & Payroll and the header
verified. **READY — clean.**

*Internal (Reviewer):* every cycle-2 item RESOLVED against the code. WARNING — refinement B5 still described the card as "showing the
exact payload" while the plan says "renders NO payload"; a literal UX agent graded against the spec would build the removed thing.
**Absorbed:** B5 now mirrors the plan's cell (no payload; contract pointer; the `recalcBlockedBy` replacement; the two vitest pins).
NOTE — the QUAL-163 vitest could pass by coincidence if both mocked counts were equal. **Absorbed:** divergent mock values
(N = 4, M = 1). Both edits are wording in wave-2/3 material, made before any wave-2/3 dispatch as the lens required; Step 5a on
TASK-14007 verifies the built card against B5. **APPROVED-WITH-WARNINGS; wave 1 cleared for dispatch.**

**Step 0b terminal 2026-09-08 — three cycles per lens; the plan is approved for Step 1 dispatch.**

## Scope & Task Decomposition (Step 1)

Task ids `TASK-140NN`. Agents are spawned by ROLE NAME (`subagent_type`) with no `model` parameter — the definition fixes the model
and `model-routing-guard.ps1` blocks a wrong one. Every agent prompt carries `docs/CONVENTIONS.md` verbatim. Acceptance criteria are
the refinement rev 4's, quoted per task at dispatch; the refinement file path is given to every agent as the spec.

| Task | Wave | Agent (model) | Deliverable |
|------|------|---------------|-------------|
| **TASK-14001** | 1 | `backend-infrastructure` (Opus) — **Backend API + Infrastructure (cross-domain authorized)**; **Step-5a dual-lens MANDATORY** (authorizer + approval as-of reads are security-adjacent; S138/S139 found their Step-5a defects on exactly this kind of path) | **The product seams (refinement A1).** Route every raw business-date read on the six suites' paths through the injected `TimeProvider`, today computed ONCE per handler (PAT-028); SQL business dates become bound `@today`. Sites: `DelegationExpiryService` (optional ctor `TimeProvider`; `:86` `until_date < @today`, UTC day — production unchanged under the documented DB-session-UTC precondition, runbook § S139); `DesignatedApproverAuthorizer` (optional ctor provider; `:147/:322` fallbacks — production-dead, every caller passes `asOf`); `ReportingLineRepository` (`:1043` fallback via optional ctor provider; `:1287` `SET effective_to = @today` passed by the caller — same request, one clock); `ApprovalEndpoints.cs` `:264+:319` (one read; rewrite the `:321` comment), `:475+:505`, `:1561+:1592`, `:823`, `:1216`; `SkemaEndpoints.cs:218`; `ApprovalPeriodRepository.cs:238/:488` (field exists); `ReportingLineEndpoints.cs` `:1272 :1679 :1758-1760 :2092 :2232-2234 :2523`. STAY on the real clock with a BY-DESIGN comment: the six `CreatedAt` stamps, `ReportingLineRepository.cs:276`, every `expires_at > NOW()` compare, token minting. `TimeProvider.System` stays the registered default. **Validation:** the Part-A AC greps (raw-read grep returns only the pre-declared stamps; SQL statement text has no `CURRENT_DATE`/`NOW()::date`); build 0 errors; every existing suite still compiles (optional parameters); Unit + non-Docker Regression green locally |
| **TASK-14002** | 1 (sequenced after 14001 builds) | `test-qa` (Sonnet) | **Six suites onto constant anchors + probe legs (A2 + A3).** `ManagerVikarEngineTests` (constructs `DelegationExpiryService` with the fixed provider), `DesignatedApproverAuthorityTests` (`WithFixedToday(F)` AND the authorizer constructed with the provider), `AdminVikarOnBehalfTests` (`:236` becomes `effectiveFrom == F` — the assertion that proves the seam reached `ReportingLineEndpoints.cs:2232`), `EmploymentDateGuardTests`, `EmploymentEndDateLifecycleTests` (already on the seam; anchors only; `FerieaarOf` / `MonthsBack` become constants), `EntitlementQuotaCheckUsesYearStartTests` (determinism, not a seam pin — labelled so; the existing fact keeps a non-boundary anchor; a SEPARATE new fact at exactly 1 September pins the year-start branch). Anchor `F = 2025-03-12` (Wednesday, OK24) where the arithmetic allows; `Anchor_IsWednesday_OnOk24Side` self-check in each; RED conditions per PAT-008; poll deadlines may keep the wall clock with a comment. **One host per fact:** in a converted fact every client comes from the ONE fixed host — the base `_factory` (real clock) is never booted alongside it, because a second host runs its own `DelegationExpiryService` boot sweep on the wall clock against the same container (`DelegationExpiryService.cs:50-64`) and would close an `until_date = F` vikar mid-fact, and a base-host client fails the `effectiveFrom == F` pin for the wrong reason (`DesignatedApproverAuthorityTests.cs:317-1058`, 19 uses, and `AdminVikarOnBehalfTests.cs:953-990`, 3 uses, take clients from `_factory` today); Step 5a greps for `_factory.CreateClient()` residue [Reviewer 0b W-3]. `ManagerVikarEngineTests` has no live backend in CI (`ci.yml:72-79`); a locally running backend would compete — comment it. **Probe legs** in `FixedClockProbeTests`: `until_date = F` NOT closed under `F` while `F − 1` IS (the DB clock would close both); vikar `[F − 1, F + 1]` effective through the authorizer's no-`asOf` fallback (seam pin on a production-dead fallback — labelled); admin-vikar POST echoes `effectiveFrom = F`. **Validation:** each new leg shown RED with its seam reverted (report the RED run); no `DateTime.UtcNow/Today` in the six except commented poll deadlines; all `.Month/.Year` from the anchor; Docker-gated facts verify in CI |
| **TASK-14003** | 2 | `backend-infrastructure` (Opus) — **Backend API + Infrastructure (cross-domain authorized)**; **Step-5a dual-lens MANDATORY** (the §21 valuation is domain correctness on the ADR-033 settlement family) | **Settlement-family HR reads (refinement B1).** New `HrFollowUpSettlementEndpoints.cs` + `HrFollowUpSettlementReadRepository.cs` + contracts, all `HROrAbove`, org-scoped via `GetAccessibleOrgsAsync(actor, LocalHR)` (empty ⇒ 403) applied to **the subject employee's `users.primary_org_id`** — exactly the worklist shape, `HrBackdateWorklistRepository.cs:623` `(@allOrgs OR u.primary_org_id = ANY(@orgIds))`, never a row's own stamped org column [Reviewer 0b W-1]; oldest-first, each item with its age anchor; "today" = `CopenhagenBusinessDate.Today(timeProvider)` once per request. **HRP-005** `PENDING_REVIEW` rows (source `row`) + **HRP-005b** conflict-shaped `SettlementManualReviewFlagged` events from the canonical `events` table (camelCase JSON keys: `data->'snapshot' IS NULL` / `data->>'flaggedDays' = '0'` — the `SegmentManifestProjectionRebuilder.cs:99-109` precedent [N-2]; referenced row still active, DISTINCT per tuple, age from earliest `occurred_at`; eventual consistency stated in the response). **HRP-007** TERMINATION SETTLED with crystallized days > 0 (the §26 snapshot accessor) AND `settlement_state = 'SETTLED'` AND no live `termination_payout_requests` row (`state <> 'VOIDED_BY_REVERSAL'`), WAIVED excluded. **HRP-010** `GET /api/vacation-transfer-agreements/{employeeId}` + the list for (VACATION, E) with `Boundary.Year == today.Year` (`ResolveForYear`, `EntitlementPeriodResolver.cs:134`): extract `VacationSettlementService.CaptureSnapshotAsync` (`:1260`, private) into an internal read-only entry point (no `asOf` — D9 operands are defined by the ferieår; `terminationCutoff: null, deferredDisposition: false`; terminated-inclusive user read; the settlement writers keep calling the same code), per-employee failures isolated into a `cannotCompute` count, `Partition(snapshot).UnderCap > 0`; population excludes employees with an active (E, VACATION) settlement row and leavers; response carries `windowOpen` / `windowOpensOn` (1 Nov). Registers its endpoint group (one line in the mapping file; the Orchestrator merges with 14004). **Validation:** build 0 errors; existing settlement suites unchanged and green (the extraction is behaviour-preserving); the B3 pins for 005/007/010 (TASK-14005) go green in CI; no write to any settlement table from the new code (grep). **Acceptance = Step 5a terminal AND the 14005 pins green in the watched CI run** — the merge for integration (OpenAPI regeneration) may precede acceptance, the acceptance record may not [Codex 0b W1] |
| **TASK-14004** | 2 (‖ 14003, worktree, disjoint files) | `backend-infrastructure` (Opus) — **Backend API + Infrastructure (extended into `src/SharedKernel/**/Calendar/**` for `InstitutionalDeadlines`, cross-domain authorized)**; **Step-5a dual-lens MANDATORY** (HRP-022 is a cross-context read of the payroll ledger, ADR-034 D4; the reads span employees) | **Approval / lifecycle / organisation HR reads (refinement B2).** New `HrFollowUpApprovalEndpoints.cs` + repository + contracts, same scope rules — the subject employee's current `users.primary_org_id` (the approver's for HRP-013/014); NEVER `approval_periods.org_id` or `manager_vikar.organisation_id`, which are stamped at send / delegation and drift when an employee transfers, showing the old org's HR a transferred employee's months and hiding them from the new [Reviewer 0b W-1]. **`InstitutionalDeadlines`** (`SubmitDays = 2`, `ApproveDays = 5`) in SharedKernel/Calendar, consumed by BOTH hard-coded sites (`SkemaEndpoints.cs:524-525`, `ApprovalEndpoints.cs:2225-2226`) and by the computed fallback; a comment names them provisional institutional defaults (SYSTEM_TARGET §G). **The (employee × month) enumeration** (one method; `?summary=true` on both consumers): for each employee in scope, the months their employment window covers from the **rolling 12-month floor (OQ-3 (a))** to the current month, joined to existing rows; NULL stored deadlines computed from the defaults with `deadlineSource = computed`. **HRP-012** employee late = covered month with month-end + 2 < today and (no row OR DRAFT OR REJECTED — REJECTED aged from that deadline, never "instantly"); approver late = SUBMITTED / EMPLOYEE_APPROVED with `manager_deadline` < today; counted separately; lists only overdue; the 011/012 overlap stated. **QUAL-163** — the roster read gains `pendingPastDeadlineCountByManager`; `pendingCountByManager` unchanged. **HRP-011** leaver's FINAL month (window end ≤ today; the end-date month missing or not APPROVED), age anchor its manager deadline, "open = all", floor applies. **HRP-022** APPROVED with no `payroll_export_records` row (read-only, ADR-034 D4), anchor = manager deadline, floor applies; 011/022 partition exact. **HRP-013/014** orphan roll-up (`isOrphan`) + expired delegations from `ManagerVikarEnded` events with `data->>'endReason' = 'EXPIRED'` (camelCase key [N-2]; only the sweep writes it; manual closes write `REVOKED` / `APPROVER_REMOVED` incl. `AdminEndpoints.cs:2292`), `occurred_at ≥ today − 30`, joined to `manager_vikar`. **HRP-015** profiles whose window covers today with no `user_agreement_codes` row covering today, one row per employee, leavers excluded. **Validation:** build 0 errors; `AddDays\((2\|5)\)` over `src/` matches only `InstitutionalDeadlines` (today exactly `SkemaEndpoints.cs:524-525`, `ApprovalEndpoints.cs:2226`) and a RED-first Unit fact pins the two derivations — the Calendar scope's norm applies under the cross-domain label; the file is listed in the Constraint Validator scope [N-3]; the Backend performs no write to `payroll_export_records` (grep); the B3 pins for 011/012/013-014/015/022 + QUAL-163 (TASK-14005) go green in CI. **Acceptance = Step 5a terminal AND the 14005 pins green in the watched CI run** [Codex 0b W1] |
| **TASK-14005** | 3 (after wave-2 merge + build) | `test-qa` (Sonnet) | **Docker-gated pins for every new read (refinement B3), born on `WithFixedToday`.** Per endpoint: org scope (403 on empty scope; a cross-org item never appears — seeded as a DIVERGENCE: a period whose `org_id` ≠ the employee's current `primary_org_id`, so a read filtering on the wrong column goes RED [Reviewer 0b W-1]); event-backed pins (005b, 014) seed through the REAL emit path with the publisher running, never by direct INSERT into `events` [N-2]. Named pins as listed in rev 4 § B3 — HRP-012 (5: stored-deadline DRAFT rows at `F − 1` / `F`; the no-row branch as previous-month-late / current-month-not; SUBMITTED `manager_deadline = F − 1`; NULL-deadline computed; the per-manager count), HRP-011 (3), HRP-022 (2 + the no-write grep), HRP-010 (8, incl. the October `windowOpen = false` anchor and the November anchor `2025-11-12`, the `UnderCap = 0` / settled / leaver / cannot-compute exclusions), HRP-005 (4, publisher left RUNNING), HRP-007 (5, incl. absence after a bare reversal and the successor under reverse-and-supersede), HRP-013/014 (4, incl. the 30 / 31-day boundary and a manual REVOKE), HRP-015 (4), payout-pending (1, seeded included + excluded). **Validation:** each pin shown able to fail (one predicate inverted ⇒ RED, reported); anchors self-checked; Docker-gated — verifies in CI |
| **TASK-14006** | 3 (after `docs/api/openapi.json` + `npm run gen:api` regenerated from the wave-2 build) | `ux` (Sonnet) | **The HR landing page (refinement B4).** Route `/admin/opfoelgning` ("Opfølgning") inside the `RequireRole minRole="LocalHR"` group (`App.tsx:94`), a `Sidebar.tsx` entry in the Administration group (`:34-46`) — a LocalLeader sees no entry and gets the guard's `ForbiddenPage` 403 card (`RequireRole.tsx:13-14`; there is no redirect) [Reviewer 0b N-1]. Shared `ProcessTile` (`components/ui/`): title, open count, "oldest: N days", optional past-deadline badge; colour ONLY on HRP-010/011/012/022. Nine year-round tiles + the §21 tile always rendered in its window state; the final-month and past-deadline tiles state the leaver overlap; the floor stated on the three history tiles; `cannotCompute` shown on the §21 tile. **QUAL-163 relabel (here, not in 14007 — it needs 14004's field, available after the wave-2 regeneration):** the org-page tile reads `pendingPastDeadlineCountByManager` and reads "Ikke godkendt N — heraf M efter frist"; the vitest mocks DIVERGENT values (e.g. N = 4, M = 1) so a component still reading the old field goes RED [Reviewer 0b c3 N-1]. Each tile opens its list on the same page (route param), oldest-first. **The lists are READ-ONLY in S140 (owner ruling OQ-4, 2026-09-08):** none of the process actions (reconcile payout; resolve a flagged settlement; record a §21 agreement; settlement reversal) has a frontend today — they are API-only — so each such list states "håndteres via API i dag; skærm følger i S141" and the four forms are named S141 items. No new write form is built by this task [Reviewer 0b B-1]. Imports `WorklistList` from `pages/admin/opfoelgning/WorklistList.tsx`, which 14007 delivers in wave 2 and is merged before 14006 starts [Codex 0b W2, Reviewer 0b W-4]. **Validation:** vitest per page/component; mocked-response pins at an October and a November anchor — TEN tiles in both, the §21 tile closed in October and open in November [Codex 0b W3]; the 403 card for a LocalLeader; `tsc` clean; typed against the regenerated `api-types.ts` |
| **TASK-14007** | 2 (‖ 14003/14004 — its APIs already exist, no backend dependency; delivers `WorklistList.tsx` before 14006 starts) | `ux` (Sonnet) | **The worklist list + the small items (refinement B5 + B6).** `WorklistList.tsx`: rows from `GET /api/hr/backdate-worklist` (kind, month/year, triggers, `recalculatedSince` / `reversedSince` with PAT-026 wording — data MOVED, not waited), Resolve (Recalculated / Dismissed with reason; If-Match from `version`; 412/428 handled — the ONE write this task builds, against the existing S138 endpoint), the SETTLED_YEAR row shows the reversal endpoint it names (`reversalEndpoint`) as text — no reversal form in S140 (OQ-4); **OQ-2 (a):** the Recalculate action renders only for Global Admins, HR sees a "requires Global Admin" hint and may Dismiss. **The button cannot call the Payroll host from the browser:** every frontend `/api/*` call is proxied to Backend.Api (`vite.config.ts:10,21` → `:5100`; Payroll is `:5200`), the Payroll host has no CORS and no frontend caller, and `api-types.ts` is generated from Backend.Api's spec only — and a Backend→Payroll route would be a cross-context change with no ruling. So in S140 the Global-Admin action is a labelled, NON-CALLING instruction card ("Kør genberegning via Payroll-API"): it names the process (HRP-003), the row's identity (employee, period, `exportId`) and the endpoint ON THE PAYROLL HOST with its `GlobalAdminOnly` gate, and points to the `RecalculateRequest` contract (Payroll `Program.cs:548-575`: `Profile`, `Entries`, `Absences`, `PeriodStart/End`, `PreviousFlexBalance`, `Reason`) which the operator assembles — the card renders NO payload, because a worklist row cannot know one and a browser-side payroll client has no ruling. When the row's `recalcBlockedBy` is non-empty (`HrBackdateWorklistRepository.cs:208-237` — a trigger strictly inside the exported month, QUAL-149/150) the card is replaced by "Genberegning blokeret — ⟨ids⟩" and only Dismiss remains. Wiring the call is a named residual for the sprint that adds the route [Reviewer 0b W-2 (a), cycle-2 B]. **B6:** `PersonDrawer.tsx` create mode gains "Ansættelsesdato" pre-filled with today, editable, sent as `EmploymentStartDate` (HRP-016); `OvertimePreApprovalManagement` routed under the leader tier with a `Sidebar.tsx` entry in "Godkend tid" (`:26-32`) (QUAL-162) [N-1]. (The QUAL-163 relabel moved to 14006: it reads a field 14004 creates in this same wave, so it cannot be typed until the wave-2 regeneration [Reviewer 0b c2 W-1].) The hire-date field extends an existing create POST (`AdminEndpoints.cs:941-942` already accepts `EmploymentStartDate`) — so the worklist Resolve is the one NEW write action here. **Validation:** vitest — the role-gated instruction card (GlobalAdmin sees it, LocalHR does not, and it performs no network call — assert fetch not called), a row with non-empty `recalcBlockedBy` renders the blocked notice and no run-instruction, the 412 path, the create POST body carries the hire date, the overtime route resolves for a leader; `tsc` clean |
| **TASK-14009** | 1 (added mid-wave by owner ruling OQ-5, 2026-09-08) | `backend-infrastructure` (Opus) + `test-qa` (Sonnet) for the leg | **The cross-org transfer's business date onto the seam, WITH its pin.** `AdminEndpoints.cs:2223`'s `today` (→ `reporting_lines.effective_to` `:2264`, `ReportingLineSuperseded.EffectiveTo` `:2277`, the closed vikars `:2287`) moves off the raw `DateTime.UtcNow` at `:1631` onto the `TimeProvider` the handler already receives at `:1522` and already uses at `:1612` — completing a conversion S139 left half-done. `now` stays on the real clock for the `updated_at` audit stamp, and the comments must say which value is which so the exemption cannot be misread as blanket permission again. **Plus a probe leg:** a fixed-clock admin PUT moving an employee's organisation, asserting the closed line's `effective_to` AND the emitted event's `EffectiveTo` both equal `F` — because the sprint's own rule is that a seam without a pin proves nothing. Found by BOTH the implementer (while converting) and the Constraint Validator (while auditing); the Step-5a Reviewer recommended exactly this pairing |
| **TASK-14008** | close | Orchestrator (docs are Orchestrator-only) | **Governance.** SYSTEM_TARGET §H: the provisional institutional deadlines + export cutoff as ruled. HR register: rows 005/007/010/011/012/022 get their built surface + the reviewed rule (the 011/012 overlap; the 12-month floor); HRP-014 the discriminator. `SPRINT-135.md` § Program Plan + ROADMAP: Increment 4 unchanged, follows in S141; HRP-016 and QUAL-162 pulled forward; the four HR write forms (reconcile payout; resolve a flagged settlement; record a §21 agreement; settlement reversal) registered as named S141 items beside the termination screen (OQ-4). FRONTEND.md: route, `ProcessTile`, the un-orphaned page. Quality register: QUAL-154 FIXED with the scan evidence; QUAL-162/163 FIXED; QUAL-155/156 updated for the converted sites, and QUAL-155 gains `ComplianceEndpoints.cs:79` (a raw business date passed as `asOf`, off the six suites' paths — the refinement had listed it as a compliant caller) [N-4]; DEBT: the Payroll host registers no `TimeProvider`; the Recalculate wiring residual (a Backend→Payroll route, or a Payroll-host contract source for the typed client) registered [Reviewer 0b W-2]. The QUAL-154 evidence names `EmploymentEndDateCorrectionGuardTests.cs:49-53,140` (`FerieaarOf(TodayUtc)` seeds a wall-clock year, calendar-invariant in outcome by the ±2y geometry) so the "exactly three" claim can be re-checked [N-5]. Register wording: `manager_vikar` has no END-reason column (its `reason` is the absence reason) [N-6]. AGENTS.md § Invocation Modes: background `codex exec` calls pass `< /dev/null` (the 68-minute stdin hang). KB: PAT-008 gains the "optional ctor provider for direct construction" sample if the Reviewer asks; a PAT for "read-only extraction of a settlement valuation" only if the implementation yields one. Model-routing register row; QUALITY.md re-grade; INDEX row |

### Waves and dependencies

- **Wave 1 — Part A, sequenced:** 14001 (`src/**`) → Orchestrator build (`dotnet build StatsTid.sln -c Release --no-incremental` 0
  errors; Unit + non-Docker Regression green) → 14002 (`tests/**`). Constraint Validator on both; **Step 5a dual-lens on 14001**
  (Reviewer: one read per handler, no seam over-reach into audit stamps / token minting, the "tests can fail" check across 14002;
  Codex `codex review` prompt-alone: same UTC day, one read, no SQL clock in statement text). Nothing in Part B dispatches before
  wave 1 is accepted — the owner's ordering.
- **Wave 2 — Part B backend + the independent frontend task, parallel in worktrees:** 14003 ‖ 14004 ‖ 14007 (disjoint files). Two
  expected merge points between 14003 and 14004, both one-liners: the endpoint-group registration in `ApiEndpoints.cs` and the
  repository DI registrations in `Backend.Api/Program.cs` (precedent `:147`, `:365`) [Reviewer 0b W-4]. Commit wave 1 BEFORE
  dispatching wave-2 worktrees (the S24 lesson). Orchestrator merges, builds, regenerates the spec and the types:
  `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi` (the no-DB entrypoint; `tools/check_openapi_sync.py`) then
  `npm run gen:api` in `frontend/`. Constraint Validator per task; **Step 5a dual-lens on 14003 and 14004** (Reviewer: architecture /
  ADR-034 D4 read-only / ADR-033 R7b honoured / the §21 valuation reuse is behaviour-preserving / the org-scope column; Codex:
  domain correctness of the three aging rules and the §21 target year). 14003 and 14004 are **merged, not accepted** until their
  14005 pins are green in the watched CI run [Codex 0b W1].
- **Wave 3 — pins + the landing page, parallel:** 14005 (`tests/**`) ‖ 14006 (`frontend/**`; `WorklistList.tsx` already merged from
  14007). Constraint Validator; Step 5a Reviewer on 14005's "tests can fail" evidence and the frontend role gating.
- **Close:** 14008; full local suites (Unit, DemoSeed, non-Docker Regression, frontend + `tsc`); CA2100 at baseline 115; **Step 7a**
  dual-lens on the whole uncommitted sprint diff (prompt-alone form if no intermediate commits, else `--base a87f6c1`); commit + push;
  ONE background `gh run watch` and its notification — never polled; CI-green backfill on the `**Test Verified**` line.

### Risks carried from the refinement (short form — the full list is rev 4 § Risks & Conflicts)

The §21 list is the one domain-heavy read (target year, VACATION type, population and the reuse target are named; a second
"remaining days" implementation is a Step-5a BLOCKER). The (employee × month) enumeration is bounded by the ruled 12-month floor
and stated on each tile. HRP-005b and HRP-014 are eventually consistent by design (one publisher cycle; stated; pins leave the
publisher running). NULL deadlines are computed from the ratified defaults and flagged, never silently "on time". Part A widens
security-adjacent code with unchanged production behaviour (same UTC day; the DB-session-UTC precondition documented). Review
surface ~8 agent tasks in three waves; wave gates are hard. Docker unavailable locally — Docker-gated pins verify only in CI.

## Architectural Constraints Verified

_Checked at close._

- [ ] Architectural integrity — bounded contexts and dependency rules hold (Backend reads the payroll ledger read-only, ADR-034 D4; `InstitutionalDeadlines` in SharedKernel, no Infrastructure→Backend.Api reference)
- [ ] Domain correctness — rule engine untouched; the §21 valuation reuses the D9 operands; the three aging rules as ratified; OK-version anchors self-checked
- [ ] Auditability — no audit stamp routed through the fixed clock; no new write paths; the worklist Resolve keeps its event + audit row
- [ ] Integration isolation & delivery — no new event types; the outbox untouched; event-store reads are read-only projections
- [ ] Security & access control — every new read `HROrAbove` + org-scoped (empty ⇒ 403); the recalculate gate unchanged (OQ-2 (a)); token minting and `expires_at` compares stay on the real clock
- [ ] Enforcement layer — CI green; sprint-close gates; the Step-7a artifacts with `reviewed-by-model: claude-fable-5-1`
- [ ] Usability — shape 3 as ruled; tiles state their floor, overlap and consistency caveats

## Task Log

### TASK-14001 — the product clock seams (refinement A1)

| Field | Value |
|-------|-------|
| **ID** | TASK-14001 |
| **Status** | complete — **Step 5a in flight**; four declared deviations awaiting the Orchestrator ruling (Fable) |
| **Agent** | `backend-infrastructure` (Opus) — Backend API + Infrastructure, **cross-domain authorized** |
| **Components** | Backend.Api endpoints (Approval, Skema, ReportingLine, Admin); Infrastructure (DelegationExpiryService, DesignatedApproverAuthorizer, ReportingLineRepository, ApprovalPeriodRepository) |
| **KB Refs** | PAT-028 (compute today once), PAT-008 (fixed-clock test pattern), ADR-040 (as-of resolution), QUAL-154/155/156/157 |
| **Constraint Validator** | in flight (Step 5α) |
| **Reviewer Audit** | in flight — dual-lens Step 5a MANDATORY (security-adjacent authorizer + approval as-of reads) |
| **External Review (Codex)** | in flight — high-risk category: domain-date semantics on authorization paths |
| **Orchestrator Approved** | pending Step 5a terminal |

**Description**: Widened the clock seam so the frozen test clock reaches the six deferred suites' product paths. Twelve endpoint
handlers and four Infrastructure classes now take "today" from the injected `TimeProvider`; the two SQL clock reads became bound
parameters. Production behaviour is unchanged: `TimeProvider.System` stays registered, every converted site still derives the **UTC
day**, and the two SQL sites previously read Postgres `CURRENT_DATE`, which equals the UTC day under the documented UTC-session
precondition. The Copenhagen-day question was deliberately NOT touched (QUAL-157 stays open).

**Validation (Orchestrator-verified, not agent-reported)**:
- [x] `git status --porcelain` — exactly **8 modified files, all under `src/`**; nothing under `tests/`, `docs/`, `docker/`, or any host `Program.cs`
- [x] **Acceptance grep 1** (raw business-date reads over the seven files) returns only the pre-declared residue: the six `ReportingLineEndpoints` `CreatedAt` audit stamps (`:130, 624, 1078, 1410, 1971, 2414`) and `ReportingLineRepository.cs:296` — plus `SkemaEndpoints.cs:79`, a pre-existing XML doc comment whose *text* reads "No `DateTime.Now`" (declared deviation D3)
- [x] **Acceptance grep 2** (SQL clock in statement text, comment lines excluded) returns **nothing**; unfiltered it returns 11 comment lines that describe what was replaced
- [x] **BY-DESIGN comments** present: 7 in `ReportingLineEndpoints.cs` (six stamps + the `expires_at > NOW()` compare), 1 in `ReportingLineRepository.cs`, 1 in `DesignatedApproverAuthorizer.cs`
- [x] `dotnet build StatsTid.sln -c Release --no-incremental` → **0 errors, 145 warnings** = baseline (Orchestrator's own run). CA2100 distinct sites to be re-measured on the close's full build (an incremental re-run emits no warning lines, so the number is not comparable mid-sprint)
- [x] `TimeProvider` absent from `docs/api/openapi.json` (0 hits) — DI-resolved handler parameters do not enter the API contract, matching the S139 precedent
- [x] No test file modified, so every one of the ~15 direct constructions still compiles (optional parameters)
- [x] Unit **1235** green / non-Docker Regression **102** green — Orchestrator's own runs. (This line first carried the agent's erroneous 144; the Step-5a cycle-2 review caught that the log contradicted itself, since the note below already called 144 wrong. Corrected here rather than in one place only.)
- [ ] Docker-gated regression (1670 facts incl. all six target suites) — **CI-verified at sprint close**; locally every one fails with `Docker is either not running or misconfigured`, zero assertion failures. Not claimed green

**Declared deviations (all four surfaced by the agent, none silent — ruling pending on Fable)**:
1. **D1 — `RemoveAsync` gained an optional `closeDate` and four call sites changed.** The spec said the `CURRENT_DATE` write should take its date "from the caller"; that required a parameter with five callers, not one. Two callers that own a date pass it (`ReportingLineEndpoints.cs:1437/1463`, `AdminEndpoints.cs:2259`); the two plain DELETEs pass only `ct: ct` with a comment saying the repository fallback is the honest seam there. The rejected alternative — putting the parameter after `ct` — would have touched no caller but hidden which callers supply the date.
2. **D2 — `ApprovalPeriodRepository`'s constructor now passes its clock to the collaborators it derives.** Otherwise a repository handed a fixed provider would silently derive real-clock collaborators: one object, two clocks. Zero production effect (all three share the one registered singleton in the Backend host; the Payroll host has no `TimeProvider` and every branch stays `TimeProvider.System`).
3. **D3 — `SkemaEndpoints.cs:79` still matches acceptance grep 1.** It is a pre-existing doc comment asserting the *absence* of a clock read. The agent declined to reword another author's accurate comment to satisfy a regex and offered the one-word fix if the Orchestrator prefers a literally clean grep.
4. **D4 — one PAT-028 hardening on a production-dead path** (`DesignatedApproverAuthorizer.cs:300`): the combined predicate forwarded a null `asOf` to two legs that each ran their own clock fallback — one authority question, two clock reads. Behaviour-inert; converted to avoid handing the reviewer a fresh double-read.

**Defects found in passing — four "one operation, two clocks", the shape S139 found three times.** None is a wrong clock; each is
the right clock read more than once, so at a UTC-midnight straddle one request could write two different days:
- **(a) `POST /api/admin/reporting-lines/{employeeId}/remove`** closed the predecessor reporting line from the DATABASE clock while opening its successor from the APP clock. Across midnight the successor could start the day before the predecessor ended — an overlap or gap in the `effective_to` lifecycle that the partial-unique index cannot catch and an as-of read would answer wrongly. **This is why D1's parameter was necessary rather than tidy.**
- **(b) The cross-organisation transfer re-sync (`AdminEndpoints.cs:2259`)** wrote the row's `effective_to` from the database clock and the `ReportingLineSuperseded` event's `EffectiveTo` from the app clock. The event stream is meant to reconstruct the row, so this was an **auditability** defect, not a cosmetic one. Both now trace to one value. (`AdminEndpoints`' own "today" source is still raw `DateTime.UtcNow` — outside the site list, → QUAL row at close.)
- **(c) `POST /approve` resolved the business date THREE times** (admission gate, in-lock re-evaluation, and again inside `ResolveDesignatedApproverAsync` called without an `asOf`); `/reject` likewise, `/reopen` twice. At a straddle a manager could be admitted as authorized against one day while the persisted `designated_approver_id` and `approval_method` were resolved against the next — two answers to "who may act now" in one approval. The comment at old `:321` asserted "Compute asOf at action-time", which read as deliberate and hid it; it now states a checkable invariant.
- **(d) Latent, now closed:** `IsEffectiveApproverOrUnitLeaderAsync` forwarded a null `asOf` to both legs, each with its own clock fallback. Production never reaches it (D4).

**Proposed knowledge entry (agent, pending Orchestrator approval at Step 5b):** *"Widening a clock seam is a defect sweep, not a
find-and-replace"* — count clock reads per OPERATION, not per file, because a callee's `?? DateTime.UtcNow` fallback and a SQL
`CURRENT_DATE` are reads the caller's own grep cannot see; "no raw read remains in this file" is strictly weaker than "this
operation reads the clock once"; and three sprints running, the false "computed once" comment sat adjacent to the bug. Candidate as
PAT-029 or an amendment to PAT-028.

**Files Changed**: `DelegationExpiryService.cs` · `DesignatedApproverAuthorizer.cs` · `ReportingLineRepository.cs` ·
`ApprovalPeriodRepository.cs` · `ApprovalEndpoints.cs` · `SkemaEndpoints.cs` · `ReportingLineEndpoints.cs` · `AdminEndpoints.cs`

**One agent-reported number was wrong** — this task claimed the non-Docker regression subset at **144**; the Orchestrator's own run
gives **102**, which is also the S139 baseline and what TASK-14002 independently reported. Not a code problem (a filter or
transcription slip in the report), but recorded because test counts are load-bearing at close: per the `sprint-test-validation`
discipline, counts are always re-run, never carried forward from an agent's summary.

---

### TASK-14009 — the cross-org transfer's business date onto the seam (owner ruling OQ-5)

| Field | Value |
|-------|-------|
| **ID** | TASK-14009 |
| **Status** | seam **complete**; its probe leg in flight (the leg is the condition the owner attached to the conversion) |
| **Agent** | `backend-infrastructure` (Opus, resumed with its TASK-14001 context) + `test-qa` (Sonnet) for the leg |
| **Components** | `AdminEndpoints.cs` — the `PUT /api/admin/users/{userId}` cross-organisation transfer fan-out |
| **KB Refs** | PAT-028; QUAL-155/156; the proposed PAT-029 amendment below |
| **Orchestrator Approved** | pending the leg + Step-5a cycle 2 |

**Description**: one line of code and, more importantly, three comments. `AdminEndpoints.cs:2249`'s `today` now reads the injected
`TimeProvider` instead of deriving from `now`, the audit timestamp. That single value reaches four writes — the reporting line's
`effective_to` (`:2290`), the `ReportingLineSuperseded` event's `EffectiveTo` (`:2299`), the `manager_vikar` close date (`:2311`)
and each `ManagerVikarEnded.EffectiveTo` (`:2322`) — so row, event and delegation dates now agree **by source**, not by luck.
`now` is untouched and still feeds `updated_at` alone. Production is unchanged: both were the UTC day.

**Why this site hid from S139 and from TASK-14001's own site list.** The date was written
`DateOnly.FromDateTime(now)` — the clock read is one assignment away, so the standard raw-clock regex cannot see it — and `now` sat
under a comment that had already blessed it as an exempt audit stamp. The exemption was correct for the timestamp and was silently
inherited by a business date derived from it. The comment fix is therefore the durable half: the `now` declaration now carries its
own `BY DESIGN:` block stating its exact scope (`@now` → `updated_at`, "used for NOTHING else"), that it is not a business date,
and that **nothing may derive one from it**, naming this bug as the reason; the parameter comment now says the seam serves *both*
of the handler's business dates and points to that block. An exemption phrased as a property of the value reads as blanket
permission; phrased as a property of the use, it does not.

**A stronger property, now verifiable:** all four `DateOnly` derivations in the 3,700-line file read the seam. The agent stated the
limit of its own claim honestly — that check proves no `DateOnly` derives from a raw clock, not that none of the other seven `now`
values is used as a business date while staying typed `DateTime`.

**Declared deviations**: (D1) it also corrected its own TASK-14001 comment at `:2255-2260`, which had asserted that `today` was
"derived from this handler's single `now`" — true when written, false the moment this change landed, and a comment asserting wrong
provenance is the exact failure mode this task exists to fix. (D2) it replaced the line-number cross-references it had first
written with construct names, because its own comment insertions had already rotted them — a rotted citation is worse than none in
comments meant to be trusted by a future converter. (D3) it split the rule across the two comments (scope-and-ban at `now`,
what-the-seam-serves at the parameter) rather than repeating it in both. All three accepted.

**Operational finding worth the retrospective:** two agents building the same test project concurrently produced two spurious red
runs (one aborted test host on a missing `testhost.runtimeconfig.json`, one with 151 `xunit.assert` load failures) from a shared
`bin/Release` write race. Test counts taken while another agent is mid-edit are not evidence. This is also the source of the
regression-total drift the two reports disagreed on.

**Files Changed**: `AdminEndpoints.cs`

---

### Orchestrator sweep — closing the blind spot TASK-14009 exposed (2026-09-08)

TASK-14009's central lesson is that `DateOnly.FromDateTime(<local>)` is invisible to the project's standard raw-clock regex,
because the clock read is one assignment away. QUAL-154's closure evidence rests on that regex, so the Orchestrator swept the
pattern across `src/` and `tests/` before letting the claim stand.

**Result: the sweep is clean, with exactly one new finding.** Every non-`TimeProvider` hit in `src/` is one of: database hydration
(`DateOnly.FromDateTime(reader.GetDateTime(…))` — a stored date, not a clock), the Copenhagen helper itself (which takes the
provider as a parameter), or domain data (`RestPeriodRule`'s stint start). All four business-date derivations in `AdminEndpoints.cs`
and every one in the seven wave-1 files read the seam.

**The one finding — `ReportingLineEndpoints.cs:1750`, and it is the same anti-pattern one level down.** The delegation GET reports
`EffectiveFrom: DateOnly.FromDateTime(vikar.CreatedAt)` — it derives a **business date** from an **audit timestamp**, the very
inheritance TASK-14009 just banned in words. Because the vikar POST writes `CreatedAt` from the real clock by design, the
delegation GET's reported `effectiveFrom` is **not reachable by the fixed test clock even after this sprint**: a fixed-clock suite
asserting that field would see the real day. No wave-1 pin asserts it (the admin-vikar echo assertion reads the POST's own
computed value at `:2287`, which is on the seam), so nothing in this wave is wrong — but the delegation GET cannot be pinned, and
a future converter would hit it. **Handed to the Step-5a cycle-2 review to adjudicate** (does any converted pin touch it? is the
right answer a `manager_vikar.effective_from` column, or is `CreatedAt`-as-start-date the actual intended model?) rather than ruled
here, and registered for the close either way.

---

### TASK-14002 — six suites onto constant anchors + the probe legs (refinement A2 + A3)

| Field | Value |
|-------|-------|
| **ID** | TASK-14002 |
| **Status** | complete — **Step 5a in flight**; three declared deviations, one of them an unsatisfied hard rule (see E1) |
| **Agent** | `test-qa` (Sonnet) |
| **Components** | `tests/StatsTid.Tests.Regression` — ReportingLine, Approval, Settlement, Config, Hosting |
| **KB Refs** | PAT-008 (fixed-clock WAF pattern, incl. its boot-order and "verify the path reads the seam" rules), PAT-028, QUAL-153 (fixed in S139), QUAL-154 (this) |
| **Constraint Validator** | pending (wave-1 second pass) |
| **Reviewer Audit** | in flight — the internal lens's "can each pin fail?" pass is the **primary** verification here, because the RED runs could not be executed |
| **External Review (Codex)** | covered by the wave-1 Step-5a pass on the product diff (TASK-14001, CLEAN); the test diff goes to the Step-7a review |
| **Orchestrator Approved** | pending Step 5a terminal |

**Description**: The six suites now pin "today" to the constant **`F = 2025-03-12`** (a Wednesday on the OK24 side of the
2026-04-01 OK-version cutover), with every date derived from that anchor. Three of the six needed TASK-14001's seam and now boot a
`WithFixedToday(F)` host and construct their services with a matching `FixedTimeProvider(F)`; the two employment-date suites were
already on the seam and needed anchors only; the entitlement suite's product path reads no clock at all and is labelled
**a determinism conversion, not a seam pin** — an honest distinction, since no revert could turn it red.

**The 1-September fact.** `EntitlementQuotaCheckUsesYearStartTests` asserted that today is strictly after the entitlement year's
start, which is false on the reset day itself — so it hard-failed once a year for a reason unrelated to any product bug. The
existing fact keeps a non-boundary anchor, and a **separate new fact** anchored at exactly `2025-09-01` addresses the reset day.
**Corrected after the Step-5a review (W-2):** as first written that fact asserted the result of arithmetic it recomputed *inside the
test*, never calling the product resolver, so flipping the product's `>=` to `>` would have left it green — it pinned nothing. This
sentence previously claimed it "can fail for a real reason" and that the agent had "cross-checked its inline reproduction against
the product resolver"; the review showed the cross-check was a reading, not a call. The fix in flight calls
`EntitlementPeriodResolver.Resolve` and asserts the returned period start, which is a real product pin with a true red condition.
Recorded rather than quietly amended, because the overclaim was the Orchestrator's own text.

**Validation (Orchestrator-verified)**:
- [x] Scope clean — exactly 7 files, all under `tests/**`; nothing under `src/**`, `docs/**` or `docker/**` (the two wave-1 tasks stayed strictly in their lanes)
- [x] `dotnet build StatsTid.sln -c Release --no-incremental` → **0 errors, 145 warnings** = baseline
- [x] **Non-Docker regression: 102 passed / 0 failed** (Orchestrator's own run — the authoritative number; unchanged, as expected, since all seven touched files are `[Trait("Category","Docker")]`)
- [x] Unit **1235** / DemoSeed **165** — agent-reported, consistent with S139's baseline
- [ ] The six suites, the three probe legs and the new 1-September fact — **CI-verified at sprint close**; not claimed green

**Declared deviations**:
1. **E1 — the RED-run demonstration could not be executed.** The project rule is that "a pin nobody has seen fail is not yet a pin": each new seam should be reverted, the leg watched failing, then restored. Docker is unreachable on this machine (the standing constraint — the client responds, the daemon does not) and all the affected facts are Docker-gated, so the agent wrote each RED condition into the code as a reasoned trace citing the product source it depends on, rather than as an observed failure. It declared this as the one hard rule it could not satisfy instead of quietly claiming it. **Orchestrator disposition:** accepted as environment-bound, with two compensations — the internal Reviewer's adversarial "can each pin fail?" pass substitutes now, and the watched CI run at close is where these legs execute for the first time.
2. **E2 — a second assertion got the same treatment as the one the spec named.** `SelfDelegate_ContractStable_ToNonDescendantLeader_StillSucceeds` asserts the echoed date the same way the admin-vikar POST fact does, so the agent pinned both literally against `F` rather than leaving one indirected through the helper. In scope by structural identity.
3. **E3 — three suites' hard-coded `EffectiveFrom = new DateOnly(2026, 1, 1)` reporting-line fixtures moved to `F.AddYears(-1)`.** Not named in the task, but required: those fixtures only resolved because the real clock had already passed 2026-01-01, and a "today" pinned to 2025-03-12 puts that literal in the future. **This is the same latent coupling the Step-0b plan review had spotted elsewhere** (a seeded line dated 2026-01-01 resolved against the product's real "today" — flagged as latent and non-cycling). Whether other suites still carry a future-dated literal of this class is an explicit question to the Reviewer.

**Product defects found: none.** The agent reports that everything it read in `src/**` matched TASK-14001's spec, including the
entitlement resolver's boundary operator matching the test's own reproduction of it.

**Files Changed**: `ManagerVikarEngineTests.cs` · `DesignatedApproverAuthorityTests.cs` · `AdminVikarOnBehalfTests.cs` ·
`EmploymentDateGuardTests.cs` · `EmploymentEndDateLifecycleTests.cs` · `EntitlementQuotaCheckUsesYearStartTests.cs` ·
`FixedClockProbeTests.cs`

---

## Review (Step 5a / 7a — both lenses)

### TASK-14001 — Step 5a, external lens (2026-09-08): Codex **CLEAN** (0 B / 0 W / 0 N)

`codex review` prompt-alone on the uncommitted diff (`< /dev/null` — the invocation rule this sprint added after a 68-minute stdin
hang). Artifact: `.claude/reviews/SPRINT-140-task14001-codex-5a.md` (trimmed verdict + the checks it was given; the 405 KB
transcript with the reviewed diff stays in the sibling `.log`). Scope was narrow by design — per-task domain correctness, with
architecture and invariants left to the internal lens.

Verbatim: *"Clean external domain review. Converted reads retain the UTC day, operation-level dates are consistently reused, SQL
business clocks were replaced with bound parameters, expiry remains strictly less than today, real-clock timestamps remain
untouched, and all three claimed cross-clock defects are correctly resolved."*

Independently confirmed, in the lens's own words: (i) **same date** — every converted site still yields the UTC day, and neither SQL
site's value changed under the documented UTC-session precondition; (ii) **strictly-less-than expiry** preserved, so a delegation
whose `until_date` is today stays active; (iii) **one read per operation**, including the reads that had been hiding inside a
callee's `?? DateTime.UtcNow` fallback; (iv) **no seam over-reach** — audit stamps, token minting and the `expires_at`
authorization compares still read the real clock; (v) all **three defect fixes** the agent claimed in passing are real and correct.

### TASK-14001 — Constraint Validation (Step 5α, 2026-09-08): **PASS, no violations**

`constraint-validator` (Sonnet, read-only) against all six checklist items over the eight declared files.

| Check | Result | Evidence |
|-------|--------|----------|
| File-scope compliance | PASS | exactly the 8 declared `src/**` files; `docker/`, `init.sql`, every host `Program.cs`, the `.sln` and `docker-compose.yml` untouched (confirmed against `git diff --stat a87f6c1`) |
| Additive signatures | PASS | the three ctor parameters are trailing and optional (`DelegationExpiryService.cs:64`, `DesignatedApproverAuthorizer.cs:70-72`, `ReportingLineRepository.cs:49-53`); `RemoveAsync`'s `DateOnly? closeDate = null` sits before `ct` and **all five `src/` call sites plus both test call sites already use named arguments**, so insertion order is safe. The endpoint-lambda `TimeProvider` parameters are non-optional but DI-resolved, the established shape four other endpoint files already used before this sprint |
| No forbidden clock routing | PASS | all six `CreatedAt` stamps and both `expires_at > NOW()` compares carry the new `// BY DESIGN:` comment; no token-minting code touched |
| SQL parameter binding | PASS | `until_date < @today`, `effective_to = @effectiveTo`, and the CTE's `@today` are all bound via `AddWithValue`; **no date is string-interpolated into any statement** |
| No architectural drift | PASS | zero new `using` statements across the eight files, no new project reference, no Rule Engine reference, no event type or schema change, and **every `RequireAuthorization` chain unmodified** (spot-verified across 12 route definitions) |
| Standard checklist | PASS | no `FindFirst("scopes")` (FAIL-001), no hardcoded `localhost`, no PAT-005 violation |

**On the "9th file" the validator flagged:** it saw `tests/…/ManagerVikarEngineTests.cs` appear in the working tree mid-run,
outside TASK-14001's declared eight, and correctly declined to score it against this task. That file is **TASK-14002**, which the
Orchestrator dispatched in parallel by plan (wave 1 is sequenced 14001 → build → 14002, and the two own disjoint scopes:
`src/**` versus `tests/**`). Confirmed intentional and tracked, not scope leakage. The instinct was right, and the lesson for
future dispatch prompts is to tell a validator when a sibling task is live in another scope.

**Residual it found, corroborating the implementer** — `AdminEndpoints.cs:2223`'s `today` derives from a raw `DateTime.UtcNow` at
`:1631`, and that value backs a **business date** (the cross-org transfer's `reporting_lines.effective_to` and the
`ReportingLineSuperseded.EffectiveTo` event field). TASK-14001 did the right thing per PAT-028 — it reused the operation's existing
date rather than adding a second read, which is what fixed defect (b) — but the **source** is still the wall clock, so this
transfer-close date stays unreachable by a fixed test clock. Sharpening the validator adds: the pre-existing S139 `BY DESIGN:
DateTime.UtcNow` comment there scopes its exemption to the **audit stamp** (`updated_at`), *not* to this business-date derivation,
so the comment currently reads as blanket permission it was never given. Two independent lenses reached this from opposite
directions (the implementer while converting, the validator while auditing). **Disposition: raised to the owner at the Step-5a
absorption** — it is outside the refinement's reviewed site list, so converting it now would be unreviewed scope growth, but it is
a small fix of exactly the shape just done twelve times.

### Wave 1 second half — Constraint Validation (Step 5α, 2026-09-08): **PASS, no violations**

`constraint-validator` (Sonnet) over TASK-14002, its Step-5a correction pass, and TASK-14009. TASK-14001's files were not
re-audited except where TASK-14009 touched one.

**The check that mattered most here — assertion integrity — is clean.** Two agents had been working toward green under a hard rule
(the RED demonstration) they could not fully satisfy, which is exactly the pressure that produces a quietly loosened test. The
validator found **only two** `Assert.Equal` removals across all seven files, both in `AdminVikarOnBehalfTests.cs`, and both
replaced one-for-one with a **stricter** pin: an equality against the literal anchor instead of against a `Today()` helper that
could move. Zero `try/catch` additions, zero commented-out facts, zero emptied `[Theory]`, zero `Skip=`. Nothing was weakened to
pass.

Also PASS: file scope (only the 8 `src/` and 7 `tests/` files; no `.csproj`/`.sln` diff, no `init.sql`, no host `Program.cs`, no
`frontend/**`); test-only discipline (TASK-14002 added no product hook — it reused seams that already existed across 33 test
files); the `AdminEndpoints.cs` clock split (`now` unchanged at `:1643` with its exemption comment, `today` on the seam at `:2249`,
no token-minting or `expires_at` code touched); Docker traits (all seven files keep exactly one class-level
`[Trait("Category","Docker")]`, so no method silently left the filter it needs); no architectural drift and no authorization
change; and the standard FAIL-001 / localhost / PAT-005 checks.

**An error in the Orchestrator's own briefing, recorded rather than quietly fixed.** I told the validator to expect "9 files under
`src/` (the 8 from TASK-14001 plus `AdminEndpoints.cs`)". `AdminEndpoints.cs` was already one of those eight, so the correct count
is **8 `src/` files total**. The validator caught the arithmetic and said so. Recorded because this sprint has held agents to
verifying every claim they make, and the same standard applies to the coordinator's numbers — this is the second Orchestrator
overclaim the review layer has caught in this wave (the first was the sprint log's description of the 1-September fact).

**Two advisory observations carried to the close:** the new `Anchor_IsWednesday_OnOk24Side` self-check facts are pure logic but
inherit their class's Docker trait, so they run under the Docker CI filter without needing Postgres — harmless, a small runtime
inefficiency, and a candidate for the Unit project later (it pairs with the Unit-duplicate follow-up the entitlement fact already
named). And several new facts disclose in their own comments that they have never executed locally, which is the honest posture
the standing Docker constraint requires.

### Wave 1 — Step 5a, internal lens, cycle 1 (2026-09-08): Reviewer **APPROVED-WITH-WARNINGS** (0 B / 3 W / 5 N, `reviewed-by-model: claude-fable-5-1`)

Reviewed TASK-14001 + TASK-14002 as one tree. Because the RED runs could not be executed (deviation E1), this lens's adversarial
"can each pin fail?" pass **was** the primary verification. Its table: **seven of nine pins would genuinely go red** if their seam
were reverted — both sides of the delegation-expiry pair (revert the provider and the two seeded rows are eighteen months stale, so
both close and the survivor assertion fails), the authority suite's HTTP facts (every vikar window would read expired on the real
clock), the admin-vikar echoed-date assertions, and all three probe legs. It also confirmed one pin **cannot** pass for the wrong
reason: the cross-styrelse 400 asserts the guard's own word in the body, which distinguishes it from the real clock's
"effectiveTo must be after today". And it named the two conversions that are **determinism only, with no seam to break** (the two
employment-date suites and the entitlement marquee), with the instruction not to cite them as seam evidence at close.

**W-1 — two comments claimed a RED run that never happened** (`ManagerVikarEngineTests.cs:415-416`, `FixedClockProbeTests.cs:367-368`
said "the RED run demonstrated and restored for this sprint"), contradicting the sprint's own declared deviation. A maintainer would
have believed the failure was observed. **Absorbed and verified:** both now read "REASONED from the service's SQL and constructor …
NOT executed on the authoring machine — Docker is unavailable there … first run in the S140 close's watched CI job." The agent then
swept its own new comments for the same shape and found nothing else.

**W-2 — the new 1-September fact could not fail, for either reason it claimed.** The important finding of the wave. The fact
recomputed the year-selection rule inside the test and asserted its own expression, never calling
`EntitlementPeriodResolver.Resolve` — so flipping the product's `>=` to `>` left it green; it pinned only that a seed row exists,
duplicating the marquee's first step. Its second claim was also false: the seed row's `effective_from` is `0001-01-01`, so an
exclusive lower bound would still have matched. **Absorbed and verified:** both facts now call the real resolver
(`:164-166`, `:369-371`) — the marquee pins `Resolve(…, F).AccrualStart == 2024-09-01` and cross-checks its independently computed
expectation against it; the reset-day fact pins `Resolve(…, SeptFirst).AccrualStart == SeptFirst` and its year. The red condition is
now true: flip the operator and the accrual start lands a full year early. The agent **declined** to manufacture a fixture for the
dropped exclusive-bound claim, explaining that seeding a row dated exactly `SeptFirst` would reintroduce the same-day-supersession
degenerate case the fact exists to avoid — a judgement call, declared rather than taken silently. It also answered the gating
question honestly: the resolver call is pure and could be a Unit test, but the fact stays Docker-gated because it shares the class's
Postgres-backed seed read; a Unit duplicate is named as a worthwhile follow-up, not smuggled in. The provenance line now says
QUAL-154 **predicted** the 1-September failure rather than claiming it was observed.

**W-3 — the cross-org transfer's business date.** Routed to the owner with the lens's own recommendation (convert, but only paired
with a pin, since an unpinned seam is what the next review would flag). **Owner ruled OQ-5 (a): convert now with a probe leg** →
TASK-14009.

**NOTES recorded for the close (TASK-14008):** N-1 — seventeen other regression suites carry the same `EffectiveFrom =
new DateOnly(2026, 1, 1)` fixture literal that broke three suites here; harmless today because none of them pins a clock and the
real calendar never returns before 2026, so the class is *absent from fixed-clock suites*, not eliminated → register the rule
"fixture literals are relative to the suite's anchor" in PAT-008. N-2 — the shared-database hazard comment names the wrong
competitor: locally the real one is the docker-compose `backend-api` container's own hosted sweep, not another `dotnet run`. N-3 —
a fixture approves a May-2026 period under a March-2025 "today", which passes only because the approve path has no future-month
guard; the suite now encodes that absence → QUAL row. N-4 — the lens agrees with deviation D3: make acceptance grep 1 exclude
comment lines rather than reword an accurate comment. N-5 — the product diff's per-site PAT-028 prose is correct but heavy;
consider consolidating at Step 7a.

**Deviations adjudicated by the lens against the code:** D1 correct and complete (all five `src/` callers and both test callers
compile; the two DELETEs that omit the date take the event's value from the returned row, so row and event cannot disagree there
either), D2 correct with zero production effect, D4 correct and inert for every non-null input, E2 correct, E3 → N-1. All four
defect fixes verified complete, not partial.

**One measurement to settle at close:** the second agent reported the build's warning count fluctuating between 145 and 175 across
runs. The Orchestrator's own `--no-incremental` Release build gave exactly **145**, twice. Analyzer output on an incremental build
is not comparable, so the close takes one clean full build for both the warning total and the CA2100 distinct-site count.

The plan's wave-1 text puts the internal Reviewer's "check that tests can fail" pass across 14001 **and** 14002, so the expensive
lens runs once over both rather than twice. Its brief: one read per handler, no seam over-reach, the four declared deviations, and
whether each converted pin can actually fail.

### Wave 1 — Step 5a, internal lens, cycle 2 (2026-09-08/09): Reviewer **APPROVED-WITH-WARNINGS** (0 B / 1 W / 4 N) — **"Safe to commit"**

`reviewed-by-model: claude-fable-5-1`. **Cycle-1 W-1, W-2 and W-3 all RESOLVED**, each verified against the code rather than the
report:

- **W-1** — the lens swept every added line of the diff for "RED / observed / demonstrated / executed" and found only honest,
  conditional phrasing plus the two reworded sites and the new Leg 8 comment. Complete.
- **W-2** — the reset-day fact now calls `EntitlementPeriodResolver.Resolve` (`:369`) and asserts `AccrualStart == 2025-09-01` and
  `EntitlementYear == 2025`. The lens verified the red condition is **true** by reading the product: the branch is
  `asOf.Month >= resetMonth ? asOf.Year : asOf.Year - 1` (`EntitlementPeriodResolver.cs:120`) and `AccrualStart` is built at
  `new DateOnly(year, resetMonth, 1)` (`:176`), so flipping to `>` yields 2024-09-01 and both assertions fail. It also confirmed
  the marquee's cross-check is **not circular** — it asserts the resolver's result against a hard literal first, and only then
  against the test's own arithmetic. And it endorsed the agent's refusal to manufacture the dropped fixture: the equality case is
  **already pinned elsewhere** (`EntitlementConfigSupersessionTests.cs:158-160`), so nothing was lost.
- **W-3** → TASK-14009, verified line by line: the single value reaches all four writes; the "`now` is used for NOTHING else"
  claim holds (its only uses in the 800-line handler are `updated_at = @now` and its binding); and the "all four `DateOnly`
  derivations read the seam" claim holds.

**Leg 8 adjudicated: it can fail, and only for the right reason.** The lens checked the wrong-reason paths itself — the fixed
host's own delegation-expiry sweep sees the anchor and so cannot pre-close the seeded row; the row read keys on a fresh
(employee, manager) pair with no ambiguity; and the `outbox_events` read is sound because the publisher only ever `UPDATE`s and
**no `DELETE FROM outbox_events` exists anywhere** in `src/` or `docker/`, so drain timing cannot remove the row.

**WARNING NEW-1 — the delegation start date, adjudicated → owner ruling OQ-6.** The lens confirmed the Orchestrator's sweep
analysis and added the part the sweep had missed: this is not only a test-reachability problem. The create endpoint echoes the
start date from the seam and stamps `CreatedAt` from the raw clock a few lines later, so **a midnight straddle makes the create
response say day D and the read-back say D+1** — the same "computed twice" shape as S139's defects, at small scale. It verified
that no S140 pin asserts the field (the three fixed-clock assertions all read POST bodies; the only GET-body assertions check JSON
*kind*, not value), so nothing in this wave passes for the wrong reason. It established that "effective from the creation instant"
**is** the intended model today, and offered two honest options. **Owner ruled 2026-09-09: register now, the schema fix is its own
task** — the review's option (ii), a real `effective_from` column written from the seam and carried on the creation event, so row,
event, echo and read agree by source. Recorded as **QUAL-164**; explicitly NOT folded into S140.

**NOTES, all absorbed before the commit:** NEW-2 — Leg 8's red-condition comment overstated, claiming that reverting *either* fix
independently would fail the row half; in fact dropping only the `closeDate:` argument still passes, because the repository's
fallback reads the *same* fixed provider — only a full revert to the database clock fails. Reworded, with the reason stated.
NEW-3 — the probe class's header still declared every leg "RED-FIRST against the unconverted product", false for the four legs
added this sprint, and an S139 paragraph about a *deliberately omitted* Leg 5 now collided with the real new Leg 5. Header rewritten
to say which legs came from which sprint and which are reasoned-not-executed; the omitted-leg note renamed off "Leg 5". Both
verified comment-only (assertion count 32 and trait count 1 unchanged). NEW-4 — the quality register's own pinned census command
was itself the blind spot; **widened in `QUAL-155`** so the reproduce step can see `FromDateTime(<local>)`. NEW-5 — the stale
"144" in this log, corrected.

**The two disputed numbers, corroborated by the lens from the tree** — and both agent-reported variants were wrong:

| Measure | Agent reports | Authoritative | Note |
|---------|---------------|---------------|------|
| Non-Docker regression | 144 (TASK-14001), 145 (TASK-14009), 102 (TASK-14002) | **102** | 144/145 are consistent with the shared-`bin/Release` race; the lens re-ran and got 102 |
| Release build warnings | 145–175 (fluctuating) | **145** | 175 is an incremental-build artefact and "would mislead at close" |

**Retrospective item agreed by the lens:** two agents building the same test project concurrently is not evidence of anything —
serialise the builds or give each agent its own output directory.

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | §21 (31 Dec transfer deadline, Ferieloven) drives the HRP-010 window and target year; no rule-engine change |
| Wage type mappings produce correct SLS codes | N/A | no payroll export change |
| Overtime/supplement calculations are deterministic | N/A | untouched |
| Absence effects on norm/flex/pension are correct | N/A | untouched |
| Retroactive recalculation produces stable results | N/A | no recalculation call from the UI in S140 — the Global-Admin card is an instruction only (Reviewer 0b W-2 (a)); the ADR-013 manual path is untouched; no cascade |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | pending |
| **Sprint-start commit** | `a87f6c1` |
| **Command** | `codex review "<prompt>"` prompt-alone (uncommitted) — `< /dev/null` — or `codex review --base a87f6c1` if intermediate commits exist |
| **Review Cycles** | — |
| **Findings** | — |
| **Resolution** | — |
