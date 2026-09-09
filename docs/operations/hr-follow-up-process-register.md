# HR follow-up process register — every hand-off the system leaves for a human

<!-- anchor-sprint: 139 -->

**Status:** DRAFT rev 3.1 (S139 TASK-13904) — **cycle 3: both lenses APPROVED** (Codex APPROVED; Reviewer APPROVED with one phrasing NOTE, applied). Consolidated from three read-only sweeps; **two dual-lens cycles
absorbed** (cycle 1: Codex BLOCKED 2B/4W/2N, Reviewer APPROVED-WITH-WARNINGS 0B/5W/5N; cycle 2: Codex BLOCKED on
two residuals — the NOT-READY column still described aging behaviour, and HRP-005b was a row in the table but not
in the counts — Reviewer APPROVED-WITH-WARNINGS 0B/1W/4N; details in `SPRINT-139.md` § Review). **Owner rulings recorded 2026-09-08** (§ Owner rulings, below): shape 3; the HRP-010 reminder; the payroll cutoff ratified provisionally (HRP-011/012/022 → READY); QUAL-154 first in S140. Row ids
are `HRP-NNN` and are **stable** — rows relocated by review keep their id and say where they went. Line numbers in
`AdminEndpoints.cs`, `EmployeeProfileEndpoints.cs` and `ApprovalPeriodRepository.cs` were re-cited against the S139 CLOSE
tree after the last product edit (they shifted three times during the sprint as the clock-seam work ran alongside the
reviews). **"Revisit, not shield"** semantics as in the SEC and QUAL registers.

## What this register is, in plain language

StatsTid increasingly hands HR a list and walks away. A backdated correction lands a row on a worklist; a
holiday-year close that cannot lawfully forfeit days parks the settlement for a human decision; a leaver's
last month must still be approved and sent by someone; an approved month reaches payroll only when an admin
calls the export by hand; a manager's stand-in expires and the reports fall back to an absent approver. Each
was designed on its own, usually well, and each stops at the same point: the system records the debt
truthfully and nothing escalates it. Nobody had looked at those hand-offs **together**, so the Increment-4
question — one HR "to-do" surface, or a list per process? — had no evidence to stand on.

This register puts every hand-off in one table with the same columns: what creates it, who owns it, by when
and on whose authority, how (or whether) it surfaces today, what happens when it ages, and how its resolution
is audited. Four headline findings, recomputed from the rows after two review cycles:

1. **Fifteen hand-offs are HR's or an admin's to resolve. Almost none are visible.** Two appear as count
   tiles embedded in the organisation page; three have an API list nobody's screen calls; one is write-only;
   the other nine have no surface in any form — the person must already know which employee and year to look
   at. The refinement's claim "HR has no follow-up surface" is confirmed for the worklist and the whole
   settlement family, nuanced by the organisation page's two tiles, and worse in one place: a finished
   overtime pre-approval worklist page was never given a route (a leader tool, so outside this register's
   rows, but a real defect).
2. **Nothing escalates.** Not one of the fifteen computes "this has been open too long". One process computes
   an expiry and acts on it (the stand-in poller closes a lapsed delegation) and tells nobody. The single UI
   label that claims a deadline — "godkendere efter frist", approvers past deadline — counts every manager
   with a pending month and never reads the deadline stored on the period.
3. **Only one hand-off is decision-ready.** Accountable roles are known for every row except one half of one
   (who acts when an HR or Admin role assignment itself lapses). But a **deadline with a stated source** exists
   for exactly one: the §21 vacation-transfer agreement (31 December, Ferieloven). The other fourteen have
   never had "by when" ruled — not by law, not by the agreements, not by the institution. Two values in code
   look like deadlines (month-end + 2 and + 5 days on approval periods) but are developer defaults no one has
   ratified. Under the owner's ruling, an aging rule is therefore proposed for one row; for the rest the
   register names the missing fact and what settling it would make possible — no rule is proposed for them.
   **Post-close ruling (2026-09-08):** the owner ratified the month-end + 2 / + 5 defaults as provisional institutional
   deadlines and set the export cutoff at the manager deadline, making HRP-011/012/022 decision-ready — four of fifteen.
4. **The biggest gap was outside every sweep's universe.** Every approved month reaches payroll only when a
   Local Admin calls `calculate-and-export` per employee and month. No job does it, no screen calls it, and
   no list shows approved months that have not gone. SYSTEM_TARGET names "cutoff dates" as configuration;
   none exists. The internal review lens found it; the register carries it as HRP-022.

## Method (vetted by the S139 refinement's dual-lens review; reproducible)

**Definition.** A hand-off is a system state that requires a LATER human action by an accountable person to
resolve. **Inclusion rule:** IN if the resolver is Local HR, Local Admin or Global Admin (roles per
`SYSTEM_TARGET.md` §F), or if the item BECOMES theirs by aging (an unapproved month at the payroll cutoff, an
expired delegation with no cover, a leaver whose last month is never sent). OUT: employee and leader steps
inside the designed §H approval flow — listed once as **B-rows**, not analysed; same-actor work in progress is
not a hand-off. Ops-facing dead-ends with no HR role are listed once as **O-rows**. Specification-stated
duties with no code, and computed-but-never-persisted facts, are kept as **gap rows** outside the strict
definition so they are not lost.

**Universes and coverage (three read-only `trace` agents, Sonnet tier; reports under
`.claude/sweeps/S139/TASK-1390{1,2,3}-*.md`, gitignored — the evidence is carried in the rows):**

| Universe | Coverage | What was swept |
|----------|----------|----------------|
| (i) Backend endpoint layer — `src/Backend/StatsTid.Backend.Api/Endpoints/*.cs` | **EXHAUSTIVE**: all 25 endpoint files (26 347 lines) + `ApiEndpoints.cs` + the 4 `Endpoints/Helpers/` files (plumbing only) | three seed families: 169 "HR must / manual / by hand / worklist…" comment hits in 15 files; ~180 409/422 sites in 19 files, each classified CORRECTED-REQUEST (caller retries → OUT) or LATER-HUMAN-ACTION (IN); 259 `HROrAbove` / `LocalAdminOrAbove` / `GlobalAdminOnly` mutations in 24 files asked "is this the resolution step of a hand-off?" Caveat: ~15–20 org-hierarchy 409s in `AdminEndpoints.cs` (the org/unit block) classified by pattern against read siblings |
| (ii) Infrastructure + hosted services + event vocabulary + DB lifecycle columns | **EXHAUSTIVE**: 87 source files under `src/Infrastructure/StatsTid.Infrastructure/` (9 IN, 78 OUT with reasons); all 94 event types in `EventSerializer.EventTypeMap` (`EventSerializer.cs:16-219`); all 68 tables in `docs/generated/db-schema.md` (6 IN, 62 OUT) | the three hosted services (`Program.cs:44-46`): `OutboxPublisher`, `DelegationExpiryService`, `SettlementCloseService`; status / `*_at` columns with no writer for the resolved side |
| (iii) Rule engine compliance, Orchestrator control loop, frontend surface, spec sources | **SAMPLED** (declared): every route in `frontend/src/App.tsx` with its `minRole` gate (exhaustive over routes; 6 admin config pages grep-checked only); `RestPeriodRule.cs`, `ComplianceEndpoints.cs`, `OrchestratorControlLoop.cs`, Orchestrator `Program.cs` in full; `SYSTEM_TARGET.md` §F/§H/§K/§N; ADR-013, ADR-033, ADR-040; `danish-agreements.md` grepped for deadline vocabulary | 8 spec-stated HR obligations extracted |
| **Review probes (cycles 1–2)** | the Payroll host's endpoints and export service, the External host, `EmploymentDateEndpoints.cs` in full, SYSTEM_TARGET §H/§K/§N and the payroll section, GDPR mentions; every citation re-opened and every negative claim re-grepped by both lenses | three missed hand-offs (HRP-022, HRP-005b, HRP-023) and two O-rows added; eleven cells corrected |

**Evidence bar.** Every cell cites `file:line` or `doc §:line`. `VERIFIED` = read in code by a sweep and re-opened
by the Reviewer; `UNVERIFIED` = stated with what would settle it. Orchestrator spot-checks (2026-09-07) are cited
"[Orch]". All greps excluded `.claude/worktrees/`.

**Columns.** Trigger · Accountable role (source: code authorization / spec) · Deadline + SOURCE (law / agreement /
institutional / `NONE` → Phase B) · Surfacing today (`UI` / `embedded tile` / `API list` / `API write-only` /
`log only` / `nothing`) · Aging (none / expiry / escalation) · Resolution + audit trail · Classification (`designed
workflow` / `API-only list` / `silent state` / `fail-loud dead-end`) · **Decision-readiness** (deadline source AND
accountable role both known → READY; else NOT READY, missing fact named) · Increment-4 relevance.

**The nine ROADMAP-named members, mapped:** (1) backdate worklist → HRP-001/002 · (2) ADR-033 settlement review →
HRP-005/005b/006/007/009/010 · (3) leaver lifecycle → HRP-011 (+ HRP-007 §26; deactivation itself is automatic,
`SettlementCloseService.cs:454-641`) · (4) window-edit strand refusals → HRP-004, ruled not a hand-off · (5)
ADR-013 recalculation → HRP-003 · (6) config DRAFT→ACTIVE → HRP-017, ruled same-actor WIP · (7) compliance
warnings / compensatory rest → gap rows HRP-019/020 · (8) "hired today" default → HRP-016, ruled not a hand-off ·
(9) undated-employee / data-integrity → HRP-015.

## The rows — HR / admin hand-offs (15)

### Time-control corrections (ADR-013 / ADR-040)

| Id | Process | Trigger | Accountable role | Deadline + source | Surfacing today | Aging | Resolution + audit | Class | Ready? | Inc-4 |
|----|---------|---------|------------------|-------------------|-----------------|-------|--------------------|-------|--------|-------|
| **HRP-001** | **Backdate worklist — EXPORTED_MONTH row** | a dated profile / agreement-code / category correction touches an already-exported payroll month — `EmployeeProfileEndpoints.cs:752-765`, `AdminEndpoints.cs:2188-2190` (users PUT) and `:2783-2785` (the dedicated agreement-code PUT); writer `HrBackdateWorklistRepository.cs:671-733` | Local HR floor, `HROrAbove` — `BackdateWorklistEndpoints.cs:100, 212`; spec ADR-040 D8 "readable by HROrAbove only" | **NONE** — ADR-013 forbids cascade and names no time bound | **API list**: `GET /api/hr/backdate-worklist` (`:60-101`); no frontend route, page or hook — only the generated type (`frontend/src/lib/api-types.ts:3448, 3481`) and a type-contract test | none — `recalculatedSince` / `reversedSince` say whether data MOVED, not how long the row waited (PAT-026) | `POST …/{id}/resolve` Recalculated / Dismissed with If-Match; `BackdateWorklistRowResolved` event + ADR-026 audit row, one tx (`BackdateWorklistEndpoints.cs:171-204`; repo `:993-1047`); the fix itself is HRP-003 | API-only list | **NOT READY** — missing: by when a correction reaching an exported month must be recalculated (institutional: before the next payroll export of a later month?) | the worklist UI |
| **HRP-002** | **Backdate worklist — SETTLED_YEAR row** (reverse-then-re-settle) | a correction reaches a settled holiday year; the revaluation SKIPS the year and flags it — `HrBackdateWorklistRepository.cs:745-892` | as HRP-001 | **NONE** | as HRP-001 | none | same resolve verb; the fix is the reversal (HRP-009) then re-settle on ADR-033 rails | API-only list | **NOT READY** — same missing fact, plus whether a settled year may stay stale across a ferieår boundary | worklist UI; the reversal must be reachable FROM the row |
| **HRP-003** | **Manual recalculation after a correction (ADR-013)** | any correction to an exported month (HRP-001 is its diagnostic list). ADR-013:25 says an administrator can manually trigger corrections for downstream periods and :37 that administrators must manually identify and correct them; a "cascade assistant" is named there as a future enhancement | **Global Admin** — `POST /api/payroll/recalculate` is `GlobalAdminOnly` (Payroll `Program.cs:393`, `:475`) | **NONE** | **nothing** beyond HRP-001's list; no "which periods are affected" view exists | none | the recalculation evolves the corrections manifest (ADR-034 D5) and is audited in the Payroll context; confirmation against the payroll system is manual | fail-loud dead-end (the need lives only in the worklist row it came from) | **NOT READY** — missing: the institutional "before the next payroll run" rule | the worklist's "recalculate" action calls this path — note the role mismatch (checklist) |

### Vacation settlement (ADR-033)

| Id | Process | Trigger | Accountable role | Deadline + source | Surfacing today | Aging | Resolution + audit | Class | Ready? | Inc-4 |
|----|---------|---------|------------------|-------------------|-----------------|-------|--------------------|-------|--------|-------|
| **HRP-005** | **Settlement flagged PENDING_REVIEW** (§34 forfeiture / §22 feriehindring — fail-closed, never auto-forfeit, ADR-033 D10) | the period-close poller finds an over-cap remainder it may not forfeit → row written `PENDING_REVIEW` + `SettlementManualReviewFlagged` — `SettlementCloseService.cs:354-429` → `VacationSettlementService.cs:364, 496` (year-end), `:676` (TERMINATION negative pre-clamp), `:770` (leaver-deferred) | `HROrAbove` — `VacationSettlementEndpoints.cs:1052`; spec ADR-033 D9 amendment 7(e) | **NONE** — ADR-033 D5/D10 name no SLA; the statutory §34 boundary is when the settlement is DUE, not when the review must finish | **nothing**: no GET lists PENDING_REVIEW rows org-wide — not even API-only; the only visibility is the per-employee `/api/balance/{employeeId}/summary` "still pending" flag (`BalanceEndpoints.cs:373-380`) | **none — and by design the poller never revisits it**: ADR-033 D5 makes the due-check skip `PENDING_REVIEW` (ADR-033:38), so the row is raised once | `POST …/{employeeId}/{type}/{year}/resolve` FORFEIT / DEFER / WAIVED / FERIEHINDRING — atomic CAS + outbox event + ADR-026 audit (`VacationSettlementEndpoints.cs:357-1052`) | **silent state** (undiscoverable without the key) | **NOT READY** — missing: how long a flagged year may sit unreviewed (the §34 lapse date is the natural outer bound — Phase B to confirm) | a "pending settlement reviews" list — a NEW read |
| **HRP-005b** | **Refused TERMINATION settlement (R7b) — flagged, no row** | a leaver's target ferieår already holds an active YEAR_END row → the termination settle is REFUSED via `RefuseTerminationConflictAsync` with `SettlementManualReviewFlagged` + ADR-026 audit + log and **writes NO settlement row** (`VacationSettlementService.cs:180-196, 1000-1032`); the R3 anti-join keeps the tuple out of later polls (`SettlementCloseService.cs:392-400`) | `HROrAbove` (the reversal + re-settle path) | **NONE** | **log / event only** — even HRP-005's per-employee balance flag cannot show it, because there is no `PENDING_REVIEW` row to read | none; never re-fires | fix = reverse the YEAR_END row (HRP-009) then re-settle as TERMINATION (ADR-033 D9) | **silent state** | **NOT READY** — as HRP-005 | the same list must include event-only flags |
| **HRP-006** | **§24 payout-pending reconciliation** | settlement SETTLED with `payout_days > 0` and `payout_reconciled_at IS NULL` — `VacationSettlementEndpoints.cs:1103-1105` | `HROrAbove`, org-scoped — `:1134` | **NONE** — waits on the external payroll system (SLS) | **API list**: `GET /api/vacation-settlements/payout-pending` (`:1060-1136`), oldest-first; no frontend caller | none (ordering only) | `POST …/reconcile-payout` CAS write of `payout_reconciled_at/by` + `AppendAuditAsync` (`:1337`) — **no outbox event**: `IOutboxEnqueue` is injected only at `:365` (resolve), the reconcile handler (`:1143-1361`) enqueues nothing → QUAL candidate | API-only list | **NOT READY** — missing: the SLS reconciliation window (payroll calendar) | payout-pending tile + list |
| **HRP-007** | **§26 termination payout REQUEST** | TERMINATION settlement SETTLED with `CrystallizedDays > 0` after the end date — `TerminationPayoutRequestEndpoints.cs:41-60, 216-254`; the settlement itself is automatic (`SettlementCloseService.cs:678-809` → `VacationSettlementService.cs:657`) | `HROrAbove` — `:372`; spec ADR-033 D9 "HR-recorded" | **explicitly none, by design**: `:70-72` "NO statutory-deadline validation is applied to requestDate … the recorded follow-up owns deadline semantics" | **nothing**: the file has exactly one route (`:92`, the POST); no GET lists settled terminations without a request; no frontend | none | `TerminationPayoutRequested` event + ADR-026 audit, one tx (`:334-344`); consumed by the Payroll host (O-rows) | **silent state** — if HR forgets, nothing surfaces it | **NOT READY** — missing: the §26 payment deadline as a date rule (Ferieloven §26 — Phase B) | "settled terminations awaiting request" — a NEW read |
| **HRP-009** | **Bare settlement reversal → "needs re-settling"** | discovered REACTIVELY: the end-date PUT returns 409 naming `reversalEndpoint` and every blocking settlement row (`EmploymentDateEndpoints.cs:559-591` — the ONE 409/422 site in the endpoint layer classified LATER-HUMAN-ACTION) | `HROrAbove` — `SettlementReversalEndpoints.cs:243` | **NONE** | **reactive only**; after a bare reversal the state "this ferieår must be re-settled" is recorded NOWHERE: `SettlementReversalEndpoints.cs:19` calls it "TERMINAL in 3b"; `SettlementReversalService.cs` has no worklist write [Orch, Reviewer] | none | two-aggregate CAS, no ETag by declaration (`:78-83`); `SettlementReversed` event | **fail-loud dead-end** | **NOT READY** — missing: when a reversed year must be re-settled | the reversal must leave a row HRP-002 can show |
| **HRP-010** | **§21 vacation-transfer agreement recording** | the employee wants to carry the 5th week forward; HR records the written agreement — `POST` / `PUT /api/vacation-transfer-agreements/{employeeId}` (`VacationSettlementEndpoints.cs:344, 347`; handler `:110-348` calls only `transferRepo.InsertAsync/UpdateAsync`, `:283, :301`) | `HROrAbove`; spec ADR-033 D8 "HR-recorded" | **31 December — Ferieloven §21 (law)**; after it the untaken week falls to §24 auto-payout | **API write-only**: POST + PUT exist, **no GET or list anywhere** (the file's only `MapGet` is payout-pending, `:1060`) [Orch, Codex, Reviewer] | none | the repository writes its own `vacation_transfer_agreement_audit` (`VacationTransferAgreementRepository.cs:213-222`); **no outbox event exists or is defined** for a transfer agreement (`EventTypeMap` has only the unrelated `FeriehindringTransferred`; ADR-033:42) → QUAL candidate | **silent / write-only state** | **READY** — role known, deadline statutory | a "5th week not yet agreed" list from 1 Nov; the record-agreement action; a GET for what was recorded |

### Employment lifecycle, approval flow and organisation

| Id | Process | Trigger | Accountable role | Deadline + source | Surfacing today | Aging | Resolution + audit | Class | Ready? | Inc-4 |
|----|---------|---------|------------------|-------------------|-----------------|-------|--------------------|-------|--------|-------|
| **HRP-011** | **Leaver's final month — approval + send** | employee deactivated while a month sits DRAFT / SUBMITTED / REJECTED — `ApprovalEndpoints.cs:1868-1930` (the S136 dead-end, opened for HR in S138) | Local HR floor forced for a terminated subject — `:1857-1864, 1908-1929` | **NONE** for a month never created; deadlines exist only on period rows that exist (HRP-012) | **nothing proactive**: `GET /api/approval/pending` (`:597-679`) lists SUBMITTED and EMPLOYEE_APPROVED rows (`ApprovalPeriodRepository.cs:141, 154, 293`) — a never-drafted final month has no row | none | standard send-command outbox + audit | **silent state** | **READY** (owner ruling 2026-09-08: the cutoff for a leaver's last month = the manager deadline, month-end + 5, provisional) | the termination screen's checklist ("last month sent?") |
| **HRP-012** | **Unsubmitted / unapproved month past deadline** (aging INTO HR) | period stays OPEN past `employee_deadline` or SUBMITTED past `manager_deadline`; both **hard-coded at period creation, month-end + 2 and + 5 days** (`SkemaEndpoints.cs:524-525`), stored per period (`ApprovalPeriodRepository.cs:1804`) | Leader (B-row flow); becomes HR's by aging — the organisation page is HR's (`App.tsx:101`, LocalHR) | **NONE stated** — the +2 / +5 are developer defaults nobody has ratified; SYSTEM_TARGET §G:167 names "cutoff dates" as configuration that does not exist | **embedded tiles** on `admin/organisation-medarbejdere`: "Ikke indsendt" = non-orphan employees with `periodStatus === 'OPEN'` (`StrukturPanel.tsx:942`), "Ikke godkendt … godkendere efter frist" = managers with any pending SUBMITTED month (`:941-943, 1304-1328`) | **none computed — and the label overclaims**: nothing reads the stored deadlines except the Skema DTO (`SkemaEndpoints.cs:517-518`); no hosted service reads `approval_periods`; "efter frist" is a static caption [Orch, Reviewer] | approve / reject / reopen in `TeamOversigt` (cross-page) | designed workflow with a **mislabelled tile** | **READY** (owner ruling 2026-09-08: +2 / +5 ratified as provisional institutional deadlines) | fix the tile to compute from the stored deadlines before any aging colour is trusted |
| **HRP-013** | **Employee with no approver (orphan)** | no structural approver resolves for the employee (`isOrphan` on the roster row) | Local HR / Local Admin (`canEditUnits` gate) | **NONE stated**; consequence: nobody can approve the month | **embedded card** "mangler godkender" with inline assign (`StrukturPanel.tsx:940, 1331-1367`) — per-organisation scope, no cross-org roll-up | none | assign approver; audited via the reporting-line write | designed workflow | **NOT READY** — missing: whether an orphan is tolerable at all, or must be fixed before the month's manager deadline (itself HRP-012's unratified value) | keep; add a cross-org roll-up |
| **HRP-014** | **Expired stand-in (vikar) / expired role assignment — no cover** | (a) `manager_vikar.until_date` passes → `DelegationExpiryService` closes it every 5 min, emits `ManagerVikarEnded` (SYSTEM actor) + audit (`DelegationExpiryService.cs:72-168`) — **nobody is told**; (b) `role_assignments.expires_at` is **written at grant** (`POST /api/admin/roles/grant`, `AdminEndpoints.cs:2866`; INSERT `:2960-2970`) and manually revocable (`POST /api/admin/roles/revoke`, `:3049`); when the date passes the row silently stops granting authority at the next check (`RoleAssignmentRepository.cs:24`, `DesignatedApproverAuthorizer.cs:557`, `IAuthorityFactsSource.cs:130`, `ReportingLineEndpoints.cs:2682`) — **no automatic closer, event, audit row or list for the expiry transition** [Codex, Reviewer] | (a) self-service: the delegating leader (`LeaderOrAbove`, `ReportingLineEndpoints.cs:1721, 2006`); admin-on-behalf `HROrAbove` (`:2191, 2424, 2573`) · (b) **nobody named** | the expiry date IS a deadline, but nothing acts when it passes; **NONE** for "someone must re-cover" | **reactive only**: `GET /api/reporting-lines/delegate` self-only (`:1647-1721`); admin GET per manager (`:2155`); `DelegationPage` shows the leader's OWN delegation (`App.tsx:89`); no org-wide "who has no acting cover"; the orphan card does not fire (the structural approver still exists, they are just away) | (a) **expiry computed and acted on** (the close), no notice; (b) none | revoke / re-delegate write audit rows | **silent state** | **NOT READY** — missing: is an uncovered approver a condition HR must fix, and by when; and who acts when an HR/Admin ROLE assignment lapses (an org losing its only HR) | an "uncovered approvers" tile; (b) needs its own row once a role is named |
| **HRP-015** | **Undated employee / profile without a covering agreement row** | an `employee_profiles` row exists with no `user_agreement_codes` row covering the date → the EMPLOYEE gets 422 `employment_profile_missing` when registering a pro-rated absence (`SkemaEndpoints.cs:1288-1292`); other consumers fail closed or soft (`BalanceEndpoints.cs:139, 754`, `DailyNormCalculator.cs:210`, `ComplianceEndpoints.cs:114`, `PeriodCalculationService.cs:458`) [Orch] | HR / Admin fix it through the profile / agreement-code surfaces — but the 422 goes to the employee and **no census or report lists the gap** [Orch] | **NONE stated** — the consequence (the employee is blocked) makes urgency obvious, but no rule states it | **nothing** | none | n/a (fail-closed; nothing written) | **fail-loud dead-end** (blocks the employee, HR is never told) | **NOT READY** — missing: whether, and by when, a registration-blocking data gap must be fixed (no rule states it) | a data-integrity tile ("employees who cannot register") |
| **HRP-022** | **Approved month awaiting payroll export** *(added by the cycle-1 Reviewer; outside all three sweep universes)* | a period reaches APPROVED; it reaches payroll only when a person calls `POST /api/payroll/calculate-and-export` per employee and month (Payroll `Program.cs:248`, `LocalAdminOrAbove` `:388`); no hosted service exports (`Program.cs:29, :51` — `OutboxPublisher` + the staging-only `SettlementExportEmitter`) | **Local Admin** (code); SYSTEM_TARGET §H:178 "approved by a leader before payroll export" states the sequence, not the exporter | **NONE stated** — SYSTEM_TARGET §G:167 lists "cutoff dates" as operational configuration; none exists in code | **nothing for HR/Admin**: no frontend caller of any `api/payroll` route (grep excluding generated types: 0) [Orch]; no read anywhere lists APPROVED periods lacking a `payroll_export_records` row — the Backend's reads of that table are the leader's team-overview per-employee `payrollExported` flag (`ApprovalEndpoints.cs:986-1000`, used to hide the reopen button), the reopen guard (`:1628`), and the worklist repository's baseline lookups (`HrBackdateWorklistRepository.cs:514, 611`) [Orch, Reviewer, Codex] | none | the export writes the lock record + `PayrollExportGenerated` + audit (ADR-034 D2); a failed downstream delivery leaves a `failed` outbox envelope "for ops" (O-rows) | **silent state** | **READY** (owner ruling 2026-09-08: the export cutoff = the manager deadline, month-end + 5, provisional) | an "approved, not exported" tile — a NEW read (ADR-034 D4 permits the read-only cross-context lookup) |

## Gap rows — outside the strict definition, kept so they are not lost (4)

| Id | What | Evidence | Why outside | Disposition |
|----|------|----------|-------------|-------------|
| **HRP-019** | **Compliance violations (DAILY_REST, WEEKLY_REST, MAX_DAILY_HOURS, WEEKLY_MAX_HOURS)** are pure results recomputed on every read and never persisted; a VIOLATION has no owner and no escalation path if the leader never expands the row | `RestPeriodRule.cs:52-88`; `ComplianceEndpoints.cs:41-247`; shown on `SkemaPage.tsx:238, 993-994` (employee) and `TeamOversigt.tsx:155-179` (leader); no compliance table in `init.sql` | no persisted state, no accountable HR/admin role | **NOT READY** — Phase B: who is accountable for an open working-time violation (the employer's statutory duty needs an institutional owner); a persisted "open violation" fact is the prerequisite for any tile |
| **HRP-020** | **Compensatory rest (EU-WTD) is a dead feature**: table (`init.sql:1452-1466`), repository (`CompensatoryRestRepository.cs:16-77`), GET (`ComplianceEndpoints.cs:269`) and FE hook exist, but `CreateAsync` / `GrantAsync` have **zero callers** (only the DI registration at `ComplianceEndpoints.cs:252`); the GET always returns empty; the hook is referenced only from a test | [Orch, Reviewer] | not a process today | → **QUAL row at S139 close** (misleading surface); Increment 4 decides: wire it to `RestPeriodRule` or remove it |
| **HRP-021** | **HK Stat afspadsering 3-month conversion to payment** — comp-time-off not arranged within 3 months → right to cash payment; **no code counterpart**: no expiry field in `OvertimeBalanceRepository.cs`, `CompensationModel.cs`, `OvertimeBalance.cs` | `docs/references/danish-agreements.md:79` | deadline stated (agreement) but the accountable party is not named in the agreement text | **NOT READY** — Phase B: who "arranges" afspadsering under HK Stat |
| **HRP-023** | **Pre-go-live manual settlement** *(added by the cycle-1 Reviewer)* — by spec, every ferieår whose boundary closed before the settlement go-live date is settled by a "manual operator-recorded process"; the close service is DORMANT when `Settlement:GoLiveDate` is unconfigured; **no endpoint records a manual settlement** (the only settlement writes are resolve / reverse / reconcile / transfer-agreement) | SYSTEM_TARGET:365; ADR-033 D3 clarification (ADR-033:30); `SettlementCloseService.cs:49-58`; `VacationSettlementEndpoints.cs:344, 347, 357, 1060, 1143`; `SettlementReversalEndpoints.cs:91` | spec-only | **NOT READY** — owner: is this owed at all before go-live, and by whom |

## Ruled OUT or relocated by review (with the alternative reading recorded)

- **HRP-004 — window-edit strand refusal (ADR-040 D3)**: HR narrows an employment window while registrations sit
  outside it → synchronous 409 naming the affected months (`EmploymentDateEndpoints.cs:594-609, 798-827`; the query
  is `EmploymentWindowStrandCheck.QueryStrandedMonthsAsync`). Nothing persists, nothing ages → **not a hand-off**.
  Alternative reading: if HR cannot correct the stranded registrations itself, it becomes one. Increment-4 input:
  the termination screen shows the affected months, not a raw 409.
- **HRP-016 — admin-create "hired today" default (S137)**: an omitted hire date defaults to today, audited with
  `employmentStartDateDefaulted` (`POST /api/admin/users`, `AdminEndpoints.cs:833`; flag `:941`, recorded `:991-1002`)
  → **not a hand-off** (immediate, audited default; a later correction routes through HRP-001). Increment-4 input:
  the create form pre-fills an editable hire date (the S137 owed item).
- **HRP-017 — config DRAFT → ACTIVE promotion**: a DRAFT is its author's own work in progress — `GlobalAdminOnly`
  at every step (`AgreementConfigEndpoints.cs:43, 73, 89, 172, 306, 425, 596, 707`; POST `:98`, clone `:179`,
  publish `:435`), listed by `GET /api/agreement-configs?status=DRAFT` and shown in `AgreementConfigList.tsx`
  (`App.tsx:122-124`), publish atomically archives the prior ACTIVE with outbox + audit for both legs
  (`:513-596`). **Ruled OUT as same-actor WIP** (Codex BLOCKER 1) — and kept here as **the pattern to copy**:
  one role, a list, a UI, an audited state flip. The sibling config surfaces share the shape (schema walk: OUT).
- **HRP-018 — overtime pre-approval**: the resolver is the **leader** — request `OvertimeEndpoints.cs:147-240`,
  list `:300-355`, approve `:359-435`, reject `:439-508`, all `LeaderOrAbove` → **B-row** (Codex BLOCKER 2). The
  finding stands as a UX/QUAL defect: a finished, unit-tested worklist page (`OvertimePreApprovalManagement.tsx:1-167`,
  PENDING / APPROVED / REJECTED tabs, Godkend / Afvis actions) has **no route in `App.tsx`, no sidebar link, and never
  had one** (`git log -S OvertimePreApprovalManagement -- frontend/src/App.tsx`: empty) → QUAL row at close;
  Increment-4 input: route it under its intended tier.
- **HRP-008 — termination payout request stuck OPEN** → **O-row** (Codex): the Payroll host promotes
  `OPEN → LINE_STAGED` when it consumes the settlement event (`SettlementExportEmitter.cs:597-598`,
  `SettlementInboxLineRepository.cs:287-290`) [Orch]; a failed consumption lands in the settlement inbox
  `RETRY_PENDING` / `DEAD_LETTER`, which no endpoint exposes — the Payroll host serves only `/health` + five POSTs
  (`Program.cs:166-480`) [Reviewer]. Ops, not HR.
- `ReportingLineEndpoints.cs:1360-1368` "transferred to a different styrelse … resolve it manually first, then
  retry" — the batch rolls back atomically and the same operator retries; nothing persists. OUT despite the word
  "manually" (both lenses concur).
- The ~180 other 409/422 sites — CORRECTED-REQUEST shapes (version conflicts, body-shape 422s, wrong-state 409s,
  "already exists / resolved / reconciled", the settlement "stand down" 409 at `VacationSettlementEndpoints.cs:1269-1277`).
- `ConfigEndpoints.cs:191-195` — local-agreement-profile activations are "today onwards only" (ADR-017 D2); admins
  are told to use external calendar reminders. A design choice to remember when the aging model is designed.

### B-rows — the designed §H flow, listed once, not analysed

Employee registers (`TimeEndpoints.cs:24`, Skema saves) → submits (`ApprovalEndpoints.cs:159`, `employee-approve
:1411`) → leader approves / rejects / reopens (`:199, :421, :1478`; `TeamOversigt.tsx`); **overtime pre-approval**
request / list / approve / reject / compensate (`OvertimeEndpoints.cs:147, 300, 359, 439, 512` — `LeaderOrAbove`;
see HRP-018 above for the unrouted page); the leader's own vikariering self-service (`DelegationPage.tsx`);
`PeriodSubmitted / Approved / Rejected / EmployeeApproved / Reopened` events; `approval_periods.status` DRAFT →
SUBMITTED → APPROVED / REJECTED (`ApprovalPeriodRepository.cs`, the status writers); `compensatory_rest.status`
PENDING → GRANTED (`CompensatoryRestRepository.cs:71` — dead, HRP-020).

### O-rows — ops-facing dead-ends with no HR role (listed once)

- **Outbox quarantine** — after 10 failed publish attempts or a correlation mismatch a row is excluded from
  `ReadBatchAsync` forever and the log says "manual reconcile required" (`OutboxPublisher.cs:64, 131, 209-227,
  290-317, 445-484`); the "ops-dashboard query" its own comment promises (`:59-61`) does not exist.
- **Failed ordinary payroll delivery** — the export RECORD (the lock) is committed regardless; a failed downstream
  POST leaves a `failed` `outbox_messages` envelope "for ops", not an unlocked month (`PayrollExportService.cs:232-243`).
  No surface. [Codex]
- **Settlement export inbox dead-letter** (Payroll host) — `RETRY_PENDING` / `DEAD_LETTER` in
  `SettlementExportEmitter.cs` / `SettlementInboxLineRepository.cs`; no endpoint exposes it. Includes HRP-008.
- **External host delivery dead-letter** — `DeliveryTracker.cs:52` `MarkDeadLetterAsync`, driven by
  `EventConsumerService.cs`; the host exposes only `/health` + `POST /api/external/send` (`Program.cs:45, 54`). [Reviewer]
- **Orchestrator failed task** — `Status = "failed"` + `ErrorMessage` persisted (`OrchestratorControlLoop.cs:85-98,
  241-267`) with a status index built for a list query (`init.sql:121`), but the service exposes only `POST /execute`
  and `GET /tasks/{id}` (`Program.cs:48, 79`) — discoverable only by GUID. The service is the repo's own build tooling
  as much as product.

## Cross-cutting analysis

### Counts (recomputed from the rows after cycle 2; HRP-005b counted as the row it is)

| | Rows |
|--|------|
| **HR / admin hand-offs (IN)** | **15**: HRP-001, 002, 003, 005, 005b, 006, 007, 009, 010, 011, 012, 013, 014, 015, 022 |
| designed workflow | HRP-012 (mislabelled tile), HRP-013 — **2** |
| API-only list | HRP-001, 002, 006 — **3** |
| silent state | HRP-005, 005b, 007, 010 (write-only), 011, 014, 022 — **7** |
| fail-loud dead-end | HRP-003, 009, 015 — **3** |
| **Surfacing** | embedded tile 2 (012, 013) · API list 3 (001, 002, 006) · API write-only 1 (010) · nothing / reactive / log-only 9 (003, 005, 005b, 007, 009, 011, 014, 015, 022) |
| **Aging** | 0 compute escalation · 1 computes expiry and acts on it, telling nobody (HRP-014a) |
| **Decision-ready** | at review time **1** (HRP-010); **after the 2026-09-08 ruling 4** (HRP-010, 011, 012, 022) · **NOT READY 11** — all eleven lack a stated deadline source; HRP-014(b) additionally lacks a named role |
| Gap rows (outside the strict rule) | 4: HRP-019, 020, 021, 023 |
| Ruled not a hand-off / relocated | 5: HRP-004, 016 (not a hand-off) · HRP-017 (same-actor WIP) · HRP-018 (B-row) · HRP-008 (O-row) |

### The refinement claim, adjudicated

"HR has admin pages but no follow-up or worklist surface at all; the S138 worklist exists only as an API and
a TypeScript type." **Confirmed** for the worklist (`api-types.ts` + one type-contract test are the only frontend
hits) and **extended** to the entire ADR-033 settlement family (payout-pending, reconcile, transfer agreements,
PENDING_REVIEW resolve, termination payout request: zero frontend callers — ADR-033:148 says so itself) and to the
payroll export trigger (HRP-022). **Nuanced**: the organisation page embeds two live open-item surfaces (the period
tiles and the missing-approver card) — a different mechanism (count + filter inside an org browser) that covers
none of the settlement, correction or export items. **Worse than claimed** in one place: the overtime pre-approval
worklist page exists, is tested, and is unreachable (a leader tool — HRP-018).

### Three shapes for Increment 4, with what each gives up

1. **One HR to-do surface** — a single table of open items across processes. Gains: one place to look, one
   answer to "is anything open?". Gives up: the processes have DIFFERENT resolution verbs (recalculate / dismiss ·
   forfeit / defer / waive · reconcile · send · export · assign · record agreement), so one table either flattens
   them into "go there" links or grows a verb per row type. (Context ownership is NOT the obstacle: ADR-034 D4
   permits the Backend a read-only cross-context read of `payroll_export_records`, which it already performs at
   `ApprovalEndpoints.cs:986-1000`, `:1628` and in the worklist repository — corrected after the cycle-1 Reviewer.)
2. **Per-process lists** — each process gets its own page. Gains: no new concept; each page speaks its process's
   language; the settlement family already has most APIs. Gives up: seven or more pages HR must open daily, and
   no view answers "is anything open at all?" — the failure mode this register documents (recorded, never
   escalated) is reproduced one page at a time.
3. **Hybrid — one landing tile per process, per-process detail behind it.** An HR "Opfølgning" landing page with
   one tile per process showing the open count and the age of the oldest item — **an age FACT (days since
   `created_at`), not an aging RULE; no colour or threshold is implied for rows without a ruled deadline** — each
   tile opening that process's list with its own verbs. Gains: the one-glance answer AND the right verbs; each tile
   needs only a count + oldest-age read per process. Gives up: a new page and a small read model per process;
   aging rules are ruled per tile, only where a deadline exists (OQ-2 (b)).

**Recommendation: shape 3.** The organisation page's tiles are already a working proto-version of it; the
processes differ in VERB too much for shape 1; shape 2 reproduces the failure mode. No proposal introduces an
automatic downstream action: no auto-recalculation (ADR-013), no auto-forfeit (ADR-033 D10) — both lenses
verified this.

### Aging / escalation — proposed for the DECISION-READY row only (OQ-2 (b))

| Row | Proposal | Owner ruling needed |
|-----|----------|---------------------|
| **HRP-010 §21 transfer agreement** | from 1 November list employees with an untaken 5th week and no recorded agreement; the tile shows the count and days to 31 December; after 31 December the item lapses by law into §24 payout (HRP-006) — the existing automated close, no new action; a GET/list for recorded agreements is added so HR can see what was recorded. **Caveat (cycle-2 Reviewer):** before the 31 December close the system has no exact §21 residual, only an accrual-side projection (the S65 research records this owner-ratified caveat, `docs/references/ferie-transfer-timing-research.md:9`; the exact partition is computed only at settlement, `VacationCarryoverExecuted`, ADR-033:42). The candidate set would therefore come from the beyond-cap projection at `BalanceEndpoints.cs:1162-1167` (the "Til udløb" figure) and can list an employee the close later clears | confirm 1 November as the reminder start; **accept a projection-based candidate list** (approximate before the close, authoritative after) |

### The missing fact per NOT-READY row — and what settling it would make possible

_Information for the owner, not proposals: no aging rule, threshold or escalation step is proposed for these rows
(OQ-2 (b)). Missing-FACT rulings are welcome at close; a row whose fact is ruled becomes READY and gets its proposal
in a later cycle._

| Row | Missing fact | What a ruling would make possible |
|-----|--------------|-----------------------------------|
| HRP-001 / 002 / 003 | by when a correction reaching an exported month must be recalculated (institutional); whether a settled year may stay stale across a ferieår boundary | a computable age and a truthful "overdue" count for worklist rows |
| HRP-005 / 005b | how long a PENDING_REVIEW settlement or a refused termination may sit (the §34 lapse date as the outer bound — confirm) | a "pending settlement reviews" list that can show days remaining |
| HRP-006 | the SLS reconciliation window (payroll calendar) | an "overdue" count on payout-pending rows |
| HRP-007 | the §26 payment deadline as a date rule (Ferieloven §26) | a "settled terminations awaiting request" list that can show days remaining |
| HRP-009 | when a reversed year must be re-settled | a row HRP-002 can show, with an age |
| HRP-011 / 012 / 022 — **RULED 2026-09-08 → READY** | **the institutional payroll cutoff** — one ruling answers three rows: ratify or replace the month-end + 2 / + 5 defaults (`SkemaEndpoints.cs:524-525`) and state the export cutoff SYSTEM_TARGET §G:167 promises | the "efter frist" tile computing truthfully from the stored deadlines; "approved, not exported" and "leaver month not sent" counts that can be called overdue |
| HRP-013 | whether an orphan employee is tolerable at all | the orphan card inheriting HRP-012's deadline |
| HRP-014 | is an uncovered approver a condition HR must fix, and by when; who acts when an HR/Admin role assignment itself lapses | an "uncovered approvers" tile; a role-assignment expiry row with an owner |
| HRP-015 | whether, and by when, a registration-blocking data gap must be fixed | a data-integrity census tile |
| HRP-019 / 021 / 023 | see the gap rows — Phase B / owner |  |

## What S140 BUILT — read this before treating any "Surfacing today" cell above as current

The rows above describe the system **as analysed in S139**, when almost nothing was visible. **S140 built the surface**, so those
"Surfacing today" cells are now historical for the rows listed here. The analysis is kept verbatim rather than rewritten in place,
because the point of the register is to make the *gap* comparable across processes; this section records what closed.

**The surface:** nine new read operations on eight new paths, behind `/admin/opfoelgning` ("Opfølgning"), Local-HR tier, one tile per process (open count + age of the oldest item),
each process's list behind its tile — the owner's **shape 3**, ruled 2026-09-08.

| Row | Was | Now (S140) |
|-----|-----|------------|
| **HRP-001 / 002** backdate worklist | API only — no route, page or hook; just a generated type | **A screen.** Row kind, period, triggers, provenance; Resolve with reason under an admin-strict `If-Match`. The two "since" fields are worded as *has this moved*, never as an age (PAT-026), because the row genuinely carries none. The SETTLED_YEAR row shows the reversal route as text |
| **HRP-003** manual recalculation | nothing beyond the worklist row | **A Global-Admin-only instruction card** naming the process, the row identity and the Payroll-host endpoint with its gate, pointing at the request contract the operator assembles. It renders **no payload** and makes **no network call** — the browser cannot reach the Payroll host, and a fabricated request would mislead the only role permitted to act. Replaced by a blocked notice when the row carries `recalcBlockedBy` |
| **HRP-005** flagged settlement reviews | **nothing** — no org-wide list existed at all | **A list**, `PENDING_REVIEW` rows plus (HRP-005b) the refused-termination flags that write **no row at all** and were previously unfindable, read from the event stream with the publisher lag disclosed in the response |
| **HRP-007** §26 requests outstanding | **nothing** — the file had exactly one route, the POST | **A list**, excluding what HR cannot act on: reversed settlements, waived claims, zero-day terminations |
| **HRP-010** §21 fifth week | **write-only** — POST and PUT with no reader anywhere | **A read of what was recorded**, plus the "still needs an agreement" list for the ferieår whose deadline is *this* 31 December, with days remaining, gated to open 1 November, and a `cannotCompute` count so one employee's missing dated history cannot empty the tile |
| **HRP-011** leaver's final month | nothing proactive | **A list** — the month containing a passed end date, missing or not approved, aged from that month's manager deadline |
| **HRP-012** past deadline | an **embedded tile whose label overclaimed**: "efter frist" counted every pending month | **Employee-late and approver-late counted separately** from the stored deadlines, with NULL deadlines computed from the ratified defaults and flagged `computed` rather than read as on-time; the roster read gains the past-deadline subset so the tile's label becomes true (QUAL-163 FIXED) |
| **HRP-013 / 014a** uncovered approvers | per-organisation card only; expired stand-ins told nobody | **A cross-organisation roll-up** plus delegations the expiry sweep closed in the last 30 days, distinguished from manual closes by the event's `endReason` (the table has no end-reason column), with `approverHasActiveCover` so a re-covered approver is not miscounted |
| **HRP-015** cannot register | **nothing** — the employee got a 422 and HR was never told | **A list** of employees employed today whose profile has no covering agreement-code row |
| **HRP-016** hire date | server-side silent default | **Pre-filled and editable at create**, so a backdated hire is a seen choice |
| **HRP-018** overtime page | finished, tested, **routed nowhere** | **Routed** under the leader tier (QUAL-162 FIXED) |
| **HRP-022** approved, not exported | **nothing** — no read listed it | **A list**, via the read-only cross-context lookup ADR-034 D4 permits, aged from the ratified export cutoff |

**Deliberately NOT built (owner ruling OQ-4, 2026-09-08 — the date the ruling was given; corrected from an earlier 09-09 typo): the four write forms.** Reconcile payout, resolve a flagged settlement,
record a §21 agreement, and settlement reversal all remain API-only; their lists say so instead of offering a form. Two of the four
are domain-heavy (the settlement resolve carries the §34-versus-§22 residual partition; the §21 record feeds a carryover write), and
they were never in the reviewed refinement — the plan review caught them being smuggled in as "the process's existing action" when
no such frontend existed. They are named S141 items.

**Still NOT READY, unchanged:** the eleven rows whose missing fact is a *deadline nobody has ruled* are still missing it. Building a
list does not make a process decision-ready — HRP-005, 007, 013, 014, 015 now have a surface and still have no stated "by when".
Four rows are decision-ready (010, 011, 012, 022), exactly as after the owner's 2026-09-08 cutoff ruling.

**New findings from building it:** QUAL-164 (a stand-in's start date reported from two clocks), QUAL-165 (the blocked-row rule
exists only in the screen — owner ruled to S141), QUAL-166 (generated contracts under-describe two required inputs), QUAL-167 (an
employee deactivated without an end date accrues overdue months for ever — a policy gap the implementer declined to paper over).

### Increment-4 design inputs (citable checklist)

- [ ] The HR landing page (shape 3): one tile per process — worklist (HRP-001/002), pending settlement reviews
      incl. event-only flags (HRP-005/005b — NEW read), payout-pending (HRP-006), settled terminations awaiting a §26
      request (HRP-007 — NEW read), leaver months not sent (HRP-011 — NEW read), **approved months not exported
      (HRP-022 — NEW read, ADR-034 D4 read-only)**, uncovered approvers (HRP-014 — NEW read), profile-integrity gap
      (HRP-015 — NEW census read), 5th-week agreements from 1 Nov (HRP-010 — NEW read, projection-based).
- [ ] The worklist UI itself (HRP-001/002) with the recalculate action calling the manual ADR-013 path (HRP-003) and
      the SETTLED_YEAR row opening the reversal path (HRP-009 must then leave a row behind). **Role mismatch to
      resolve first:** the worklist is `HROrAbove` but `POST /api/payroll/recalculate` is `GlobalAdminOnly` (Payroll
      `Program.cs:475`; ADR-034 D5) — an HR user would see the row and be unable to press the button; either the UI
      hides the action below Global Admin or the role floor gets its own ruling (the register does not propose
      lowering the gate).
- [ ] The termination screen: end date + the strand months as a list, not a 409 (HRP-004); the "last month sent?"
      checklist item (HRP-011); the §26 request (HRP-007); the refused-termination flag (HRP-005b).
- [ ] The admin-create form: hire date pre-filled and editable (HRP-016) — the S137 owed item.
- [ ] Fix the "efter frist" tile to compute from the stored deadlines (HRP-012) before any aging colour is trusted;
      the deadline values themselves await the owner's ruling.
- [ ] Route `OvertimePreApprovalManagement` under its intended tier (HRP-018 — a leader tool).
- [ ] QUAL rows at S139 close: HRP-020 dead compensatory-rest feature; HRP-006's reconcile emits no outbox event;
      HRP-010's transfer agreement has no outbox event defined; HRP-014(b) role-assignment expiry has no closer,
      event, audit or list; HRP-018 the unrouted page.

## Owner rulings (2026-09-08, post-close; asked one question at a time, recorded in `SPRINT-139.md`)

1. **Shape → 3.** Increment 4 builds one HR landing page with a tile per process (open count + age of the oldest
   item) and each process's own list with its own actions behind the tile. Shapes 1 and 2 declined for the
   trade-offs stated above.
2. **HRP-010 aging → accepted as proposed.** From 1 November, a list of employees with an untaken 5th week and no
   recorded agreement, showing days to 31 December; the owner accepts that the list is projection-based until the
   year-end close makes it exact; after 31 December the item lapses by law into the existing §24 payout. A GET for
   recorded agreements is added.
3. **The institutional payroll cutoff → ruled.** The month-end + 2 (submit) and + 5 (approve) developer defaults are
   **ratified as provisional institutional deadlines**, and the **export cutoff is the manager deadline** (month-end
   + 5). Provisional: to be made configurable per institution later. Consequence: **HRP-011, HRP-012 and HRP-022 are
   now DECISION-READY** — four of fifteen — and the "efter frist" tile can be computed honestly (QUAL-163). Their aging
   proposals are drafted below for S140's refinement to review; they have not yet been through the review lenses.
4. **QUAL-154 (the second-tranche clock conversion) → the first task of S140**, before Increment 4's own work.

### Unlocked by ruling 3 — proposals for S140's refinement (drafted post-close; NOT yet lens-reviewed)

| Row | Proposal (to be reviewed in S140) |
|-----|-----------------------------------|
| HRP-012 unsubmitted / unapproved month | compute "past deadline" from the STORED `employee_deadline` / `manager_deadline`; the tile shows the past-deadline counts truthfully (today it counts every pending month); a period past the manager deadline surfaces on the HR landing page |
| HRP-011 leaver's final month | a "deactivated employees whose final period is missing or not approved" read; ages against the manager deadline of that month |
| HRP-022 approved month awaiting export | an "approved, not exported" read (ADR-034 D4 read-only cross-context lookup); ages against the export cutoff = the manager deadline; the export call itself stays a manual admin action (ADR-013 / ADR-034 unchanged) |
