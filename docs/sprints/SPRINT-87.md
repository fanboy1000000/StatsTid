# Sprint 87 — Leder-oversigt P1: Teamoversigt table + approvals + the team-overview aggregate

| Field | Value |
|-------|-------|
| **Sprint** | 87 |
| **Status** | complete |
| **Start Date** | 2026-06-20 |
| **End Date** | 2026-06-20 |
| **Orchestrator Approved** | yes — 2026-06-20 |
| **Build Verified** | yes — `dotnet build` 0/0; FE `tsc`/`build` clean |
| **Test Verified** | yes — TeamOverviewAggregateTests 15/15; FE vitest 494; approval suite 80/80; full pyramid + e2e on CI |

## Sprint Goal
P1 of the `design_handoff_leder_oversigt` redesign (the 2-sprint split, OQ-4): the leader **Teamoversigt** at `godkend/oversigt` — the per-employee table (status / overenskomst / norm+bar / flex / ferie / warnings / handling) + KPI band + filters/sort + month stepper + bulk-select, the approve/reject/reopen + **bulk-approve**, backed by a NEW **`team-overview` aggregate endpoint**, and the **nav switch** off `godkend/godkendelser`. **P2 (S88) = the expandable detail** (allocation breakdown + full per-employee compliance). This sprint is backend + frontend.

Refinement: `.claude/refinements/REFINEMENT-leder-oversigt.md` (dual-lens reviewed; 2 BLOCKERs resolved). Owner rulings 2026-06-20: OQ-1 FE-loop bulk; OQ-2 cheap allocation-warning + lazy compliance (P2); OQ-3 redirect+remove old route; OQ-4 two sprints (this = P1).

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| check_docs hard checks | CLEAN | through S86; db-schema in sync |
| Working tree | CLEAN | at S86 tip `8d783d6` |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (the designated-candidate-roster correctness [ADR-027 D13 see==act]; the aggregate auth-scope; new endpoint + a new page + a nav switch touching the e2e) |
| **External Codex** | invoked 2026-06-20 — cycle 1: 1 BLOCKER, 2 WARNING, 4 NOTE; cycle 2: "Clean — resolved" |
| **Internal Reviewer** | invoked 2026-06-20 — cycle 1: 0 BLOCKER, 2 WARNING, 5 NOTE |
| **BLOCKERs resolved before Step 1** | yes — the `periodId`-in-row BLOCKER (added, nullable) |

### Findings (cycle 1)
- **BLOCKER (Codex)** — row omitted `periodId` (the handling/bulk key on it). **RESOLVED** (nullable `periodId` added; no-period rows null → no actions).
- **WARNING (both) — "reuse the balance summary" under-scoped + N+1.** The `/summary` computation is an inline `MapGet` lambda (`BalanceEndpoints.cs:38-424`), ~30+ round-trips/employee incl. a full per-employee event-stream flex replay; 40× = a heavy N+1 + copy-paste would re-open the S81 dated-config split-brain. **RESOLVED** — the aggregate computes the cheap table fields via batched/set-based projection reads (ferie from `entitlement_balances` VACATION, norm from work_time/time-entries, etc.); flex extract-a-shared-batch OR documented tolerance; NOT 40× `/summary`; the authoritative full Saldi = P2. `BalanceEndpoints.cs` added to Files Changed (conditional).
- **WARNING (Codex) — bulk per-row 428.** `useApprovals.approvePeriod` discards the HTTP status (`useApprovals.ts:32`); the bulk must use the direct `apiClient` status pattern (`ApprovalDashboard.tsx:230`) to distinguish 428→confirm. **RESOLVED** (8702).
- **NOTE (both) — roster derivation confirmed correct:** derive from the candidate-employees CTE (`:272`) → R5 filter → LEFT JOIN the period (period-independent; supports zero-period DRAFT rows). The existing methods are period-first → a new repo method is needed. `displayName`/`agreement` for no-period rows from `users`. **Folded into the criteria.**
- **NOTE — `hasWarning` mirrors only the allocation arm (not coverage/uncovered-days)** — a named P1 narrowing; `false ≠ submittable`. **Folded in.**
- **NOTE — nav-switch refs complete** (`App.tsx:65`, `Sidebar.tsx:28`, `TopNav.tsx:15`, `e2e:137`); ALSO update the stale endpoint-registry docs (`docs/FRONTEND.md:142/149/165`, `DEP-004:63`); keep ApprovalDashboard's vitest green as dead-but-tested until S88. P1 (no expand) coherent; no P2-only data leaks into the table.

### Resolution
1 BLOCKER + the WARNINGs folded into 8701/8702/8703. Cycle-2 (verification) runs before Step 1.

## Architectural Constraints
- [ ] P7 — the aggregate is **designated-approver-scoped** (ADR-027 D13 see==act; the `my-reports` resolution, NOT org-scope, NOT `/reports`)
- [ ] P5/P2 — the cheap `hasWarning` mirrors the approval-gate predicate (work_time_projection + NORMAL/TaskId); NO rule-engine call in the aggregate (the S73 503-coupling avoided)
- [ ] No new concurrency surface — bulk-approve reuses the S78/S83-hardened single-approve (FE loop)
- [ ] P9 — hifi fidelity (tokens/copy); a11y (Radix Dialog, focus-visible, Escape)
- [ ] No regression to approve/reject/reopen (S78/S83) or the balance/skema endpoints

## Task Log

### TASK-8701 — `team-overview` aggregate endpoint (backend)

| Field | Value |
|-------|-------|
| **ID** | TASK-8701 |
| **Status** | planned |
| **Agent** | Backend + API Integration |
| **Components** | `ApprovalEndpoints.cs` (new GET), `ApprovalPeriodRepository.cs` (the roster query), tests |
| **KB Refs** | ADR-027 D13 (designated authority), ADR-028 D4 (allocation gate), ADR-026 |
| **Orchestrator Approved** | no |

**Description**: `GET /api/approval/team-overview?year=&month=` (LeaderOrAbove). One row per employee in the leader's **designated-act-authority set**:
- **Roster (the BLOCKER):** derive the employee set from the SAME designated-candidate resolution the approval queries use (`GetByMonthForDesignatedReportsAsync`/`GetPendingForDesignatedReportsAsync` `:171/:222` + `IsEffectiveDesignatedApproverAsync` — descend from the actor + every approver they vikar for, advance through INACTIVE sub-managers, same-tree; ADR-027 D13 see==act), **extended to ALSO emit zero-period reports** (a report with no period this month still appears as DRAFT). NOT `/reports`.
- Per row: **`periodId` (nullable — Codex BLOCKER; the handling/bulk approve/reject/reopen key on it; a zero-period DRAFT row has `periodId=null` → no actions)**, `employeeId, displayName` (join `users`), `agreement` (from the period, else `users.agreement_code` for no-period rows), `status, submittedAt, decisionAt` (neutral — there's no `rejectedAt`; rejects write `approved_at` too, `:969/974`), `rejectionReason, normExpected, normRegistered, flexBalance, overtime, ferieUsed/ferieTotal` (from VACATION entitlement — ferieår-correct, NOT top-level `vacationDaysUsed`), `awayToday` (today's absence, server-side, **per-employee fault-isolated**), `hasWarning` (the **cheap allocation/"Ikke fordelt" warning** mirroring the ALLOCATION arm of the gate at `ApprovalEndpoints.cs:947-973` — worked from `work_time_projection`, allocated = NORMAL + non-null `TaskId`; **NO rule-engine call**; this mirrors only the allocation arm, NOT the coverage/uncovered-days arm — a named P1 narrowing, so `hasWarning=false` ≠ "submittable").
- **Balance fields — batch/set-based, NOT the per-employee full `/summary` (Step-0b WARNINGs).** The `/summary` computation is an inline `MapGet` lambda (`BalanceEndpoints.cs:38-424`) doing ~30+ round-trips/employee incl. a FULL per-employee event-stream replay for flex — running it 40× is a ~1,200-round-trip N+1, and copy-pasting it would re-introduce the dated-OK/entitlement split-brain the S81 sweep just closed. Instead the aggregate computes the **cheap table fields via batched/set-based reads over the team's employee-ids**: ferie from `entitlement_balances` VACATION (used/totalQuota), norm-registered from summed `work_time_projection`/time-entries, norm-expected from the per-employee profile norm, overtime + flex from their balance/projection source. **If flex genuinely requires the summary's event-replay, extract a minimal shared/batch flex read (add `BalanceEndpoints.cs` to Files Changed) OR document a bounded table-vs-`/summary` tolerance — do NOT re-implement the dated-config/entitlement resolution.** The authoritative full Saldi (replayed) is P2 (lazy `/summary` on expand).

**Validation Criteria**:
- [ ] Roster = the designated-act-authority set, derived from the **candidate-employees CTE → R5 (`IsEffectiveDesignatedApprover`) filter → LEFT JOIN the month period** (NOT a period-first join — so zero-period reports emit a DRAFT row; `periodId=null`). RED-on-naive (`/reports`): a **vikar-coverage** report appears (approvable), an **inactive-escalation** report appears, an **acting-reassigned-away** direct report does NOT; tested via fixtures. (Codex/Reviewer NOTE — confirmed the CTE supports period-independent derivation.)
- [ ] `periodId` nullable on the row; no-period rows carry it null + source `displayName`/`agreement` from `users`.
- [ ] Per-row fields correct: ferie from VACATION entitlement; `decisionAt` neutral + status disambiguates; `awayToday` fault-isolated; `hasWarning` = the gate-mirrored ALLOCATION warning (assert the rule-engine is NOT hit).
- [ ] **Balance fields are batch/set-based** (NOT 40× the full `/summary`; NOT a re-implementation of the dated-config/entitlement resolution) — a perf-shape check (bounded queries over the team, not a per-employee replay loop); values consistent with `/summary` (or a documented tolerance if flex is replay-only).
- [ ] LeaderOrAbove + designated-approver-scoped (a non-approver gets their own set / 403; no org-scope leak).
- [ ] `dotnet build` 0/0; the new tests green; no regression to the by-month/balance endpoints.

**Files Changed**: `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs`, `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs`, `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs` (IF a shared/batch flex read is extracted), `tests/**`

---

### TASK-8702 — TeamOversigt page + approvals + bulk + nav switch (frontend)

| Field | Value |
|-------|-------|
| **ID** | TASK-8702 |
| **Status** | planned |
| **Agent** | UX/Frontend |
| **Components** | new `frontend/src/pages/approval/TeamOversigt.tsx` (+ css) + `useTeamOverview` hook; `App.tsx`/`TopNav.tsx`/`Sidebar.tsx`; `frontend/e2e/approval.spec.ts`; tests |
| **KB Refs** | ADR-011 (FE design system), S82 (a11y kit), ADR-027:170 (reopen policy) |
| **Orchestrator Approved** | no |

**Description**: Build the page from `design_handoff_leder_oversigt/` (hifi, tokens/copy as-is). **P1 = the table, NOT the expandable detail** (P2/S88).
- The table (kit table styles): columns status badge / overenskomst / norm/registered + 5px bar / flex (signed, colored) / ferie / advarsler chip / handling; the status-badge mapping; the row is NOT expandable in P1 (the chevron + accordion = P2).
- KPI band (5 cards: Afventer din godkendelse [green top border] / Advarsler / Norm-opfyldelse / Fravær i dag / Godkendt), search (name+empId), filter chips (alle/afventer/godkendt/advarsel, full-team counts), column sort (navn/status/norm/flex), month stepper (drives the year/month query).
- **Handling + bulk:** per-row Godkend/Afvis(dialog)/Genåbn; **bulk-approve = FE loop** of single-approve (OQ-1) — "Godkend N valgte" fires N sequential approves, surfacing per-row {approved / 409 / 428→confirm-and-retry}; sequential by design. **The per-row status-aware handling must use the direct `apiClient` status pattern** (`ApprovalDashboard.tsx:230`), NOT `useApprovals.approvePeriod` which DISCARDS the HTTP status (`useApprovals.ts:32`) and so can't distinguish 428→confirm (Codex WARNING) — refactor/share that status-aware approve. **Reopen control LocalHR+** (preserve the dashboard policy, `ApprovalDashboard.tsx:134`). Reject = the kit Radix `Dialog`, optional reason.
- **Nav switch (OQ-3):** add the `godkend/oversigt` route + the "Oversigt" sidebar item under Godkend tid; **redirect `godkend/godkendelser` → `godkend/oversigt`**, remove the "Godkendelser" sidebar item, repoint `TopNav` firstRoute, and update `frontend/e2e/approval.spec.ts` (the approval journey now lands on the new page). Keep `ApprovalDashboard` code for one release (delete in S88 after parity).

**Validation Criteria**:
- [ ] The page matches the hifi (tokens/copy); table + KPI + filters/sort + month stepper + bulk-select work; consumes `team-overview`.
- [ ] Approve/reject(dialog)/reopen + bulk (per-row {approved/409/428→confirm-retry}); reopen LocalHR+.
- [ ] Nav switch: `godkend/oversigt` live, `godkend/godkendelser` redirects, sidebar updated, the e2e approval journey green against the new page.
- [ ] FE vitest (table/filters/sort/bulk/reject-dialog/nav) + a11y; `tsc` + build clean; no expand (P2).

**Files Changed**: `frontend/src/pages/approval/TeamOversigt.tsx` (+ css), `frontend/src/hooks/useTeamOverview.ts`, `frontend/src/App.tsx`, `frontend/src/components/layout/{TopNav,Sidebar}.tsx`, `frontend/e2e/approval.spec.ts`, `frontend/src/pages/approval/__tests__/**`

---

### TASK-8703 — Validate + Step-7a + docs + close (Orchestrator)

| Field | Value |
|-------|-------|
| **ID** | TASK-8703 |
| **Status** | planned |
| **Agent** | Orchestrator |
| **Components** | validation, `docs/QUALITY.md`, `ROADMAP.md`, `docs/sprints/SPRINT-87.md` |
| **Orchestrator Approved** | no |

**Description**: Build + FE vitest + the Admin/approval backend regression + a fresh e2e (the moved approval journey). Step-7a dual-lens (the roster=act-authority correctness; the batch/set-based balance reads [no N+1, no S81 split-brain]; the bulk per-row UX; the nav switch; a11y; no rule-engine in the aggregate). Docs: QUALITY/ROADMAP/SPRINT-87 + **update the stale endpoint-registry refs for the moved route** (`docs/FRONTEND.md:142/149/165`, `docs/knowledge-base/dependencies/DEP-004-endpoint-registry-ui-api-data-alignment.md:63`). Commit + push + CI-verify; MEMORY.

**Validation Criteria**:
- [ ] Full pyramid + e2e green; CI green on push. Docs updated; `check_docs` green.

**Files Changed**: `docs/**`, `ROADMAP.md`

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Backend regression — `TeamOverviewAggregateTests` | 15 (+1 Step-7a) | green (Testcontainers; roster=act-authority RED-on-naive-`/reports`; ferie/decisionAt/awayToday/flex/hasWarning/auth; the over-allocation symmetric-warning) |
| Approval suite (incl. new) | 80/80 | green |
| Frontend vitest | 494 (+18) | green (`TeamOversigt` table/status-mapping/KPI/filters/sort/bulk[428→confirm/409]/reject-dialog/reopen-gating/nav-redirect; ApprovalDashboard 18/18 dead-but-tested) |
| `tsc` / `npm run build` | — | clean |
| e2e (`approval.spec.ts`) | rewritten | the mgr03 approve/reject journey now drives the new TeamOversigt page (two distinct nonce months); exercised on CI |

**Step-7a artifacts:** `.claude/reviews/SPRINT-87-step7a-{codex,reviewer}.md` — both PASS; the one finding (`hasWarning` symmetric-gate-mirror) fixed + RED-on-old-tested.

## Sprint Retrospective

**What went well**: The refinement's central thesis held end-to-end — this was the one handoff that genuinely needed backend work, and naming that up front (vs the FE-only skema/oversigt/medarbejder-admin) set the right scope. **[[review-lens-complementarity]] was decisive twice before code:** the Step-4 refinement review caught the **roster-source BLOCKER** (the page must key on the designated-act-authority set, not `/reports` — the S75/S77 see==act invariant), and Step-0b caught the **balance-reuse N+1 / S81-split-brain trap** + the `periodId`-in-row BLOCKER + the bulk-status-discarding-hook WARNING. Because those were fixed in the plan, the implementation came back clean: the backend reused the *identical* candidate-CTE + R5 predicate the action endpoints authorize through (provably no see/act drift), and computed every balance field set-based (flex provably equal to `/summary`, no replay loop, no dated-config duplication). Step-7a then caught one real residual — the `hasWarning` asymmetry vs the gate — fixed to a true symmetric mirror.

**What to improve**: The full-Saldi/allocation-breakdown + per-employee compliance are deferred to **S88 (P2)**; the P1 `hasWarning` is the cheap allocation arm only (a named narrowing — `false ≠ submittable`). A manual visual spot-check of the new page vs the prototype is a recommended follow-up (the CI e2e drives it, but pixel-fidelity is best eyeballed).

**Knowledge produced**: No new KB entry (a feature over the established approval/reporting-line domains). The reusable pattern (a leader-team read aggregate must derive its roster from the SAME designated-candidate resolution the action endpoints authorize through — never a raw `/reports` join) is recorded in ADR-027's orbit via this sprint log + reinforces the S75/S77 see==act invariant.
