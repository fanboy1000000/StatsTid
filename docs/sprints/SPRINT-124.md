# Sprint 124 — UI/testing (rolling), second of the kind

| Field | Value |
|-------|-------|
| **Sprint** | 124 |
| **Status** | CLOSED (2026-07-30) |
| **Start Date** | 2026-07-29 |
| **End Date** | 2026-07-30 |
| **Type** | Rolling UI/testing sprint (the second, after S123) — the owner drives the demo system by hand and names fixes one at a time; each clears the Pre-Implementation Gate (`refine-requirements`) with a proportionate dual-lens review, is implemented, and is verified against the RUNNING stack, not only by tests |
| **Orchestrator Approved** | per-task |
| **Build Verified** | `dotnet build` green (API + Regression); FE tsc 0 / lint 0 |
| **Test Verified** | 868u + 1390r + 6s + 55demoseed + 707fe = **3026** — full regression MEASURED locally (1387 at the pre-Step-7a run: 1384 pass + 3 FAIL-002 environmental sheds, isolation-cleared 13/13; +3 TASK-12405 gate tests after). tsc 0 / lint 0. Smoke + DemoSeed carried, CI-verified at close |

## Shape
Same rolling shape as S123, but this sprint turned out **not** to be UI-only. Driving the live demo
surfaced three authority defects behind the cosmetics, and the owner ruled on each as it appeared:
a picker that offered choices the server would always reject, a manager reading employees'
un-submitted timesheets, and a manager able to *write* them. So the sprint spans P7 (access control)
and the PAT-012 typed contract alongside the P9 work.

Two process notes worth carrying forward:
- **Every task was live-verified against the running stack**, not just unit-tested. That is what
  caught the last task's real problem (see TASK-12403): the demo world has **zero** time
  registrations, so a working review surface was indistinguishable from a broken one.
- **The dual lenses earned their keep repeatedly** — 4 BLOCKERs across the sprint, 3 of them
  convergent between Codex and the Reviewer Agent, and one of them (TASK-12403's authority gap) was
  a hole the codebase had already found and fixed twice on sibling endpoints.

---

### TASK-12400 — Row primary action moves onto the NAME (Organisation & medarbejdere)
| Field | Value |
|-------|-------|
| **ID** | TASK-12400 |
| **Status** | complete (2026-07-29) — FE-only. Clicking a person's name opens their edit drawer; clicking a unit's name opens the unit. The right-edge `Rediger ›` / `Åbn ›` links are gone. Hit area is the NAME only (owner ruling); verified live at 84px of an 806px row (10%). Testids MOVED onto the name buttons rather than re-minted, so the CapabilityMatrix gating contract keeps expressing the same intent. Gate preserved: below LocalHR a name renders as inert text via ONE `PersonNameCell` (the gate had been duplicated across two rows). Affordance CSS on NEW `.nameAction`/`.unitNameAction` classes — never on the shared `.personName`, which also renders the INERT orphan-card name (that would have been an S91 dead-button regression introduced by a polish task). `aria-label` carries the verb. |
| **Agent** | Orchestrator (small-task exception; FE-only) |
| **Components** | `enhedsspor/StrukturPanel.tsx` + `.module.css`; 4 test consumers incl. the CI-gating `e2e/organisation.spec.ts` |
| **Also staged (carried in from the prior session, recorded here per Step-7a)** | The left-tree **expansion-state lift**: per-node expand state moved up to `OrganisationOgMedarbejdere.tsx` so the Struktur panel's "Vis/Skjul org." drives the tree too (`onToggleNode` / `onExpandSync`, keyed by RAW node id); the deliberately **asymmetric** collapse (expand = path + descendants so the subtree is visible; collapse = descendants only, written as explicit `false`, so the selected node and its ancestors stay open and the org tiers do not revert to default-OPEN); and both people toggles **omitted at MAO tier** (a MAO loads no roster, so neither could reveal a row — the S91 dead-button discipline). Independently tested behaviour, not incidental refactoring |
| **Refinement** | `.claude/refinements/REFINEMENT-name-click-row-actions.md` — READY; Reviewer 1B/4W/6N, Codex 3B/2W/2N |

**The BLOCKER (both lenses, convergent)**: two test sites encoded the *inverse* of the request as a
deliberate invariant (`expect(getByText('Jens Kofoed').closest('button')).toBeNull()`), one inside a
role-parameterised loop so it failed for every permitting role. They were **inverted, not deleted** —
the surviving half (below-floor names must be inert) is the load-bearing P7 guard.

**Lesson**: jsdom has no layout engine, so a hit-area claim cannot be asserted geometrically. The
test pins the `nameAction` class (assertable via `classNameStrategy: 'non-scoped'`); the geometry is
a live check.

---

### TASK-12401 — Godkender/vikar picker scoped to the subject's Organisation
| Field | Value |
|-------|-------|
| **ID** | TASK-12401 |
| **Status** | complete (2026-07-29) — backend + contract + FE. `GET /api/admin/users/search` gained an optional `organisationId`. Live: unscoped 3251 across orgs → scoped-to-STY02 **7** in one org; unknown org 200-empty-total-0; empty param unscoped. |
| **Agent** | Orchestrator (owner-ruled direct implementation — see the governance note) |
| **Components** | `AdminEndpoints.cs`, `ApprovalPeriodRepository.SearchPeopleAsync`, regenerated `openapi.json` + `api-types.ts`, `useReportingLines`, `PersonPickerDialog` + 4 call sites + 3 threading hosts |
| **Refinement** | `.claude/refinements/REFINEMENT-approver-picker-org-scope.md` — READY; Reviewer 1B/4W/2N, Codex 3B/2W/2N |

**Why**: `ValidateSameOrganisationAsync` (ADR-027 D2) hard-rejects a cross-Organisation approver with
a 400, so every cross-org name in that picker was a guaranteed dead end — a UX-honesty fix aligning
the UI with an invariant the domain already enforced.

**THE SECURITY-CRITICAL PROPERTY**: the new predicate is a SEPARATE conjunct AND-ed with the RBAC
`accessibleOrgs` clause — the two INTERSECT. Applied *instead* it would have been privilege
escalation (a scoped LocalHR passing a foreign org id to enumerate that roster). A foreign id returns
an empty set and deliberately **not** a 403, which would make the parameter an org-existence oracle.
Pinned by a dedicated escalation test asserting `items` empty **and `total == 0`** (placing the
conjunct in the `page` CTE instead of `matched` would have passed an items-only assertion).

**The BLOCKER (both lenses, convergent)**: edit-mode scoping. The approver assign is an IMMEDIATE
POST validated against the PERSISTED `primary_org_id`, while a cross-Organisation transfer is a
first-class drawer flow — so scoping to the unsaved draft org would have listed the new org's people
and then 400'd on pick, reinstating the exact bug. Rule adopted: **scope to the Organisation the
SERVER will validate against** — draft in CREATE, persisted in EDIT — with an explanatory notice in
both modes. Three drawer-level tests pin it.

**Governance note**: Codex raised the CLAUDE.md delegation rule as a BLOCKER (cross-domain work
should go to domain agents). The trade-off was stated to the owner in the question they answered;
they chose direct implementation. Recorded as an informed deviation, not a silent override.

---

### TASK-12402 — A manager sees nothing before a month is submitted (Teamoversigt surface)
| Field | Value |
|-------|-------|
| **ID** | TASK-12402 |
| **Status** | complete (2026-07-30) — backend + contract + FE. Five fields WITHHELD (null) on a non-submitted row: `normRegistered`, `overtime`, `hasWarning`, `flexBalance`, `ferieUsed`. Badge relabelled **"Kladde" → "Ikke indsendt"** (owner ruling). Live-verified: DRAFT rows null, SUBMITTED/APPROVED/REJECTED released. |
| **Agent** | Orchestrator |
| **Components** | `ApprovalEndpoints.cs` (team-overview), `ApprovalResponses.cs`, regenerated spec + types, `TeamOversigt.tsx` + `.module.css` |
| **Refinement** | `.claude/refinements/REFINEMENT-manager-no-draft-visibility.md` — READY; Reviewer 3B/2W/6N, Codex 4B/2W/2N |

**The leak was real, not cosmetic**: the numeric fields were computed over the WHOLE roster
regardless of period status, so a leader could read an employee's un-submitted registered hours.

**Owner ruling** (asked as an operational question, per `feedback-design-forks-plain-language`):
keep the ROW (the `lederfrist` duty and the "Fravær i dag" tile both need the full team) but blank
the content. Then, strictly: *"A manager cannot see anything before a month is submitted."*

**The finding the strict ruling forced out**: `ferieUsed` had been classified as a standing balance.
It is not — `entitlement_balances.used` is incremented **inside the employee's own Skema save
transaction** (`SkemaEndpoints.cs` → `EntitlementBalanceRepository.CheckAndAdjustAsync`) with no
approval gate, so a *drafted* day off moved the manager's Ferie column. Now withheld. The backend
test seeds 7 used days on a draft month specifically so a regression surfaces as `7`, not as a
vacuous null.

**Nullable, not zero** (PAT-012): "registered 0,0 t" would be a lie about someone who may have
registered 140. `normExpected` + `ferieTotal` survive as the honest denominators — standing
contract/quota facts no registration can move. The predicate is a fail-CLOSED allowlist
(`submittedToManager`), so a future sixth status defaults to withheld.

**Also fixed from review**: the aggregate leak (`kpiNorm` averaged draft hours across the team) — now
covers only sent rows and says so ("Norm-opfyldelse · 3 af 11 indsendt"), rendering `—` rather than a
false `0%` on an all-draft team; and un-submitted rows had kept a dead row-click that silently
collapsed whichever row was open.

**DECLARED RESIDUE → RES-002** (owner-deferred to the next sprint): the rule is enforced on the
Teamoversigt surface **and, since TASK-12405, on the month GET's LEADER tier**. **~6** sibling reads
still serve the same content to the same actor with no status filter — `/balance/{id}/summary`
(`normHoursActual`, the unwithheld twin of the field withheld here), `/balance/{id}/year-overview`,
`/time-entries/{id}`, `/absences/{id}`, the allocation-breakdown and the compliance read. Full census
in `docs/knowledge-base/resolutions/RES-002-…md`, which also now records an OPEN ACTOR-MODEL
question: the withholding is actor-blind while the month GET is HR-exempt, so HR sees `—` on the row
yet can open the grid. The follow-up must pick one model.

---

### TASK-12403 — The employee's full skema, inline in the leader's review panel
| Field | Value |
|-------|-------|
| **ID** | TASK-12403 |
| **Status** | complete (2026-07-30) — backend authority fix + FE. Expanding a submitted row now renders **summary → SKEMA (the full day-by-day grid) → decision buttons**. Read-only; live-verified with 19 working days of real data. |
| **Agent** | Orchestrator |
| **Components** | NEW `approval/ManagerSkemaGrid.tsx` + `.module.css`; `TeamOversigt.tsx`; `SkemaGrid.tsx` (`showWorkedHours`); `SkemaEndpoints.cs` (month-GET authority) |
| **Refinement** | `.claude/refinements/REFINEMENT-manager-skema-grid-view.md` — READY; Reviewer 1B/3W/6N |

**Why**: approving a month was a decision made without the evidence — the row showed a month TOTAL,
never which DAYS carried it, and `SkemaPage` is structurally self-only (no manager route existed).

**The BLOCKER**: the month GET was org-scope gated only, while BOTH sibling reads on the same
expander pair org-scope with `IsEffectiveApproverOrUnitLeaderAsync` — the compliance read carries an
S88-8801 B2 comment explaining exactly why (the roster is the DESIGNATED-approver set, which admits
cross-afdeling vikar/escalation approvers and peer unit-leaders whose ORG_ONLY scope does not name
the employee's org). The grid would have 403'd for precisely that population, surfacing as "Kunne
ikke hente skemaet" — a systematic hole masked as a transient fault. Fixed with the same additive
OR-branch; the refinement's "no backend change" exclusion was withdrawn.

**Test honesty**: the first three authority tests were **vacuous** — every actor in that fixture
holds covering org-scope, so they passed pre-fix. A discriminating case was built (an approver
holding the edge but a token scope naming a different org) and **proven RED-on-old** by a probe that
neutralised the branch. Two containment guards (403 for a non-designated out-of-scope leader, and
cross-styrelse) confirm the branch adds access without becoming a bypass. The test file states which
cases are proof and which are only regression guards.

**Owner ruling mid-task**: *"Skema needs to be the default view … the skema should always be shown."*
It had briefly sat behind a "Vis skema" button; that put the evidence one click further from the
decision. Now inline and always rendered.

**`showWorkedHours`**: under `readOnly` the Arbejdstid row renders the ALLOCATION classification
(`✓` when balanced), so on a correctly-allocated month the hours worked appeared NOWHERE — on the one
surface whose purpose is to show them. An additive prop, opted into by the manager view only, so the
employee's own locked month keeps its semantics. Pinned both directions.

**THE DEMO-DATA FINDING (the sprint's most transferable lesson)**: the owner reported "I do not see
the changes". The feature was correct; the **demo world has ZERO time registrations** — 0 rows in
`time_entries_projection` and 0 in `work_time_projection` for the month, only absences. Every row
read `0,0 / 155,4 t` truthfully, and an empty grid is indistinguishable from a broken one. Also,
projects existed **only** for the baseline STY02 org, so demo employees could not carry project
hours at all. Seeded one employee (19 working days, work-time + matching allocation across two new
STYX1 projects) through the real endpoint as `demo_admin`. **Candidate for the next sprint: fold
registrations into the DemoSeed generator so the demo world exercises the time spine it is meant to
demonstrate.**

**Also**: the inline grid is fault-isolated (a partial month payload degrades the block to empty
rather than taking down the leader's whole review panel) — the posture the sibling compliance fetch
already documents.

---

### TASK-12405 — The leader month-read gate (Step-7a BLOCKER absorption)
| Field | Value |
|-------|-------|
| **ID** | TASK-12405 |
| **Status** | complete (2026-07-30) — absorbs the Step-7a external BLOCKER. `GET /api/skema/{employeeId}/month` is now admitted in TIERS: self → unrestricted; HR-or-above covering scope → unrestricted (the CORRECTIVE tier — TASK-12404 lets HR edit, and you cannot correct what you cannot read); LEADER (covering below-HR scope OR the designated edge) → **only for a month the employee has SENT**. Fail-CLOSED on an unknown status. |
| **Agent** | Orchestrator |
| **Components** | `SkemaEndpoints.cs` (month GET) + the authority tests |

**Why this was a BLOCKER, not a nicety**: TASK-12403's additive edge branch was a P7 **WIDENING of an
ungated read**. It handed an edge-only leader the FULL grid of a DRAFT — or entirely nonexistent —
month, flatly contradicting TASK-12402's ruling in the same sprint. Worse, the tests written for
TASK-12403 *demonstrated* the leak: they asserted 200 for a month with no approval period at all. A
named deferral (RES-002) does not make a widening acceptable under the priority order.

The status allowlist is the SAME one the team-overview withholding uses, so the two surfaces cannot
drift — a row whose figures are withheld must not have its full grid readable through the back door.
New: `SkemaMonth_DesignatedApprover_UnsubmittedMonth_Is403` (RED-on-old — the branch returned the
whole grid), `…_DraftMonth_Is403` (which is also the reopen case, since every persistent DRAFT is a
reopened month), and `SkemaMonth_Hr_UnsubmittedMonth_IsAllowed` pinning the corrective tier.

---

### TASK-12404 — A manager can never edit an employee's registrations
| Field | Value |
|-------|-------|
| **ID** | TASK-12404 |
| **Status** | complete (2026-07-30) — P7 narrowing. Both write endpoints now require HR-or-above to touch another employee's registrations. Live: leader→other **403**, leader→own 200, admin→other 200, leader read 200. |
| **Agent** | Orchestrator |
| **Components** | `SkemaEndpoints.cs` (save), `TimeEndpoints.cs` (`POST /api/time-entries`) |
| **Refinement** | folded into TASK-12403's (the finding was surfaced while scoping it) |

**Owner ruling**: *"A manager can never edit an employee's registrations. Only HR and admins can."*

**What was open**: both endpoints admitted ANY non-Employee actor whose org-scope covered the target
— LocalLeader included. `POST /api/time-entries` was the worse member: **no approval-period check at
all**, so a leader could write in any period state. Both now pass
`roleFloor: StatsTidRoles.LocalHR` to `ValidateEmployeeAccessAsync` (the existing per-scope floor
mechanism, so a mixed-role actor's LEADER scope cannot carry the write while an HR scope still can).

**SELF IS EXEMPT and that is load-bearing**: a LocalLeader is also an employee registering their own
time, and is not Employee-role, so they fall through the same scope branch — an unconditional floor
would have locked every leader out of their own timesheet. Guarded by a named test.

**Both denials proven RED-on-old.** A first probe attempt did not compile, so those tests silently
ran against a stale DLL and "passed"; caught and redone rather than reported as verification.

**Caller census before changing authority** (per `feedback_cross_process_caller_census_required`):
both FE writers are self-only; the Orchestrator only READS; DemoSeed authenticates as `demo_admin`
(GlobalAdmin), so seeding survives. Then **175 existing regression tests** across every area
touching these endpoints: all green, unchanged — nothing relied on the old permissiveness.

**HR's read deliberately NOT narrowed** — pinned by a test so the write narrowing cannot bleed
across. The LEADER read *was* narrowed, in TASK-12405.

---

## Validation

| Suite | Count | Note |
|-------|-------|------|
| Unit | 868 | unchanged |
| Regression | see close commit | every TOUCHED area run green locally (S91 authority 20, AllocationBreakdown 22, TeamOverview 22, Skema/TimeProjection/OkVersion 139, remaining census areas 36); full suite + smoke CI-verified at close |
| Smoke | 6 | carried; CI-verified |
| DemoSeed | 55 | carried |
| Frontend | 707 | `npx vitest run` green; tsc 0 / lint 0 |

Contract gates: `check_openapi_sync` ✓ · `check_openapi_convention` ✓ (134 typed / 3 grandfathered /
9 declared) · `check_endpoint_contracts` ✓ · `check_docs` ✓ (KB INDEX 59 entries, 0 orphans).

## Carried to the next sprint
1. **RES-002** — the deferred endpoint-level read gate (**~6** reads after TASK-12405). Must be
   **period-status-based**, not a blanket denial, or it breaks TASK-12403; must settle the recorded
   ACTOR-MODEL question (actor-blind withholding vs the HR-exempt month read); and should reuse
   `ApprovalVisibility.IsSubmittedToManager` rather than re-deriving the status set per endpoint.
2. **The reopen fork** — a leader-reopened month reverts to un-submitted and therefore hides figures
   the leader already approved. Applied literally per the ruling; the alternative distinguishes
   leader-reopen (visible) from an employee self-reopen of `EMPLOYEE_APPROVED` (withheld) via
   `PeriodReopened.PreviousStatus`. Owner has not ruled.
3. **DemoSeed time registrations** — see the TASK-12403 demo-data finding.
4. **P4 arm on the write class**: a save against a SUBMITTED period does not transition status, so
   content can change after submission and the approval binds to content the employee never sent.
