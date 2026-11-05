# O-6 — The documents owed once all of QUAL-230's T5 has merged: apply-ready text

| | |
|---|---|
| **What this is** | The exact text for every line of the plan's O-6 row (`.claude/plans/PLAN-s147.md:314`) and for every "owed at O-6" item the sprint log gathered (`docs/sprints/SPRINT-147.md:167`, `:173`, `:219`, `:232`, `:238`, `:247`, `:344-346`, `:376`), so the Orchestrator can apply each as a string replacement. The `planner` drafts; the Orchestrator applies. The targets (`docs/**`, `SYSTEM_TARGET.md`, `ROADMAP.md`, and one optional `src/**` comment) are the Orchestrator's |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-03 |
| **Base commit** | `808d4297` (local master, the main checkout; the session started there and nothing landed during the work). Every "Old text" below is copied from `808d4297` and was checked unique in its target by an exact-substring count over the whole file (X5's text occurs twice, both in SEC-057's row, by design). Every line number is at that commit |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 (available again from 2026-11-03) |
| **What merged (the premise)** | All of T5: TASK-14703 `8d2b5716` (the departed-self rule); the TASK-14710 stack `9c7866f0` (the 5-day grace, login refused from day 1, the self-service write floor, one message per clause); TASK-14712 `52ab15c8` (a departed approver loses authority on day 1); the TASK-14713 stack `652f26aa` (HR only from day 6; the HRP-011 row actions). Docker evidence: CI-W2b run `41310143330` (14703), CI-W3 run `41335270197` (the 14710 stack, 14712), CI-W3b (the 14713 stack; running at `808d4297`) |
| **Timing** | **Apply after CI-W3b is green.** Two placeholders are the Orchestrator's to fill: `<CI-W3b run id>` and `<CI-W3b date>` (Blocks 1b and 2a). Block X2 (`SYSTEM_TARGET.md`) also needs the owner's approval first |

## What this is, in plain words

QUAL-230 was the defect where a person who had left could not have their last month approved or paid: the system switched them off the day after their last working day, and from then on nobody could act on the month. The owner ruled (OQ-2) on a timeline: the leaver's **own** access ends the day after the last day, whatever their role; their **manager** keeps 5 calendar days to finish the month as usual; from **day 6 only HR** may act, and payroll pays it. All four pieces of that work have merged. What remains is paperwork, and it matters because three documents still state the old rule:

- **ADR-040 D3** (the decision record for employment dates) still says "`is_active` governs login/session only". That is no longer true: login and the write rules now look at the last working day, not only at the on/off flag. The decision is amended in one dated section, with a pointer from D3, rather than rewritten, so a reader can see what was decided in S135 and what S147 changed.
- **The registers** (QUAL-230, SEC-003, SEC-046, HRP-011, HRP-014) each get a dated note saying what changed for that row.
- **`docs/SECURITY.md`** already says most of it (the Orchestrator wrote it at each merge). One line is wrong and incomplete: the route count. It is corrected here.

Four rules shaped every block:
- **Nothing is described as built unless it is merged.** All four T5 tasks are. The two raw payroll routes (TASK-14716) are *not* merged, so the census line says they are still open.
- **Earlier decisions are not rewritten.** D3 keeps its text and gains a one-line pointer to the amendment.
- **Plain words first, then the mechanism**, with a `file:line` opened at `808d4297`.
- **`SECURITY.md` is not redone.** The section "Already done" lists what it already says, with line numbers, and only the missing pieces are drafted.

## How to apply

- Each block names its **Target**, the **Old text** (a unique match at `808d4297`) and the **New text**. Where a block has parts (a, b), apply each part as its own replacement.
- No two replacements in one file overlap, so the order inside a file does not matter.
- **Line endings.** Every target is LF except `SYSTEM_TARGET.md` (X2) and `VacationSettlementService.cs` (X3), which are CRLF throughout. Their multi-line Old texts match once CRLF is read as LF, as the Edit tool does (checked that way); keep the file's CRLF endings in the New text.
- **Blocks 1a–7 are owed by O-6.** Blocks **X1–X6** are beyond the owed list; each says what it fixes, its lean, and whether it needs anything first. **X2 needs the owner's approval** (CLAUDE.md: `SYSTEM_TARGET.md` changes need approval; plan line 300). **X3 is a `src/**` comment** (the Orchestrator's small-task exception, or the tidy pass).

## Map: each owed item → its block

| # | Owed item (source) | Block | State before this draft |
|---|---|---|---|
| 1 | QUAL-230 closed, "CLOSED when CI-W3b is green" (plan `:314`; `SPRINT-147.md:219`) | 2a, 2b | open; the row's progress note says "closes at O-6" |
| 2 | The ADR-040 amendment: `REFINEMENT…:392-399` (the grace constant, the three dates, login from D+1, the write rule with the identity clause, the validator clause "reads included" with the PAT-028 residual, HR only from D+6, T5c's acting-user rule reconstructable from `EmployeeEmploymentEndDateSet`) plus the sweep (the inline guards, `/recalculate`) | 1a, 1b, 1c | not written |
| 3 | P-2 / owner Q-B = (c): "a leaver's active stand-in survives the leaver's deactivation until its end date; HR revokes or reassigns (`/remove`)" — in the amendment | 1b | not written |
| 4 | The same sentence on HRP-014 | 4 | not written |
| 5 | P-12: the superseded S136 Step-5a (a) residual, named in the amendment | 1b (and 5) | not written |
| 6 | SEC-046's S147 note: closed for every role at the self-service sites and inside the validator (R-21, R-30) | 5 | partly: the row's 2026-11-01 note covers the login and floor change only |
| 7 | Q-C = (b): door by door, no front-door write gate — in the amendment | 1b | not written |
| 8 | HRP-011: the grace, the HR actions, the hand-over date, aging unchanged | 3 | not written |
| 9 | `SECURITY.md`: the leaver's access end; the allowlist surfaces; the org-scope departed-self paragraph; the census summary (the subject-key rule; 32 / 19 / 33) | 6 | three of four done (see "Already done"); the census line is incomplete and mislabelled |
| 10 | SEC-003's row: own data closed centrally; the residual is other people's data; the grace does not widen it; the revisit's shape (R-25, R-30) | 7 | partly: the 2026-11-01 note covers the login refusal and the floor |
| 11 | The settlement comments (K-2) | — / X3 | half done: `SettlementCloseService.cs:765-776` carries them; `VacationSettlementService.cs:1158` does not |
| 12 | The R9c banner checked (`UserRepository.cs:254-256` in the refinement) | — | **done** (checked; see "Already done") |
| 13 | SEC-055 stays registered | — / X4 | nothing to change; the premise still holds; its line pointers are stale (X4, optional) |
| 14 | R4: the clear routes reuse one date across a retry (`SPRINT-147.md:167`) | 1b | not written |
| 15 | Consult R6: the validator's per-call date read plus the handler's re-read (`:173`) | 1b | not written |
| 16 | K-14: a reversal corrected into the grace rolls back until day 6 (`:232`, `:344-345`) | 1b | not written |
| 17 | The import residual (`:346`) | 1b | not written |
| 18 | The holder rule (14712, P-1) (`:247`) | 1b | not written |
| 19 | The two refusal messages (14710 fix-up 1) | 1b | not written |
| 20 | Step A and its events fire on day 6 (K-4); `ManagerGraceCalendarDays` (`:376`) | 1b | not written |
| 21 | D3's "`is_active` governs login/session only" no longer holds (K-10; SEC-046's note) | 1a, 1b, 1c (and X2) | not written |
| 22 | 14713's surfaces; the employed-month bound and its date-free 422; the tree-lock overload | 1b | not written in the ADR (in `SECURITY.md:135-140`) |
| 23 | The reason-text change for below-HR actors (`SPRINT-147.md:212`) | 1b | not written in the ADR (in `SECURITY.md:140`) |

**Count: 10 owed replacements (1a, 1b, 1c, 2a, 2b, 3, 4, 5, 6, 7) and 6 optional blocks (X1–X6: 8 Old texts, 9 replacements, because X5's text is replaced at both of its places).**

## Already done (no block; checked at `808d4297`)

| Owed item | Where it is done | Evidence |
|---|---|---|
| `SECURITY.md` — the org-scope departed-self paragraph | `docs/SECURITY.md:114-131` ("The departed-self clause", where the predicate runs, the self-service write floor, one message per clause) | written at the 14703 and 14710 merges |
| `SECURITY.md` — the allowlist surfaces (HR only from day 6) | `docs/SECURITY.md:135-140` (the five terminated-inclusive surfaces, the employed-month bound after the gate, the tree-lock overload, the subject-organisation audit, the reason-text change) | written at the 14713 merge (`808d4297`) |
| `SECURITY.md` — the leaver's access end | `docs/SECURITY.md:147-155` (the active-but-departed window: the switch-off on day 6, login refused from day 1, the remaining 480-minute token, what that token can do) and `:157` (the rule for a new route) | written at the 14719 and 14710 merges |
| The R9c banner checked | `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs:258-268` rewrites "a closed spell implies the poller flipped" to hold only from D+6 (TASK-14710); `:270-292` adds the TASK-14713 extension with every surface and the identity clause | read at `808d4297`; accurate |
| The validator's XML doc and R9c inventory name the clause (refinement `:401`, code) | `src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs:13-34`, `:161-178`; the deny-only reads are inventoried on `DepartedSubject.cs` (its type doc) | read at `808d4297` |
| K-2, the first site | `src/Infrastructure/StatsTid.Infrastructure/SettlementCloseService.cs:765-769` (the active branch excludes a grace leaver; settlement due from D+6) and `:775-776` (the leaver branch from D+6; K-6) | TASK-14710 point 5 named only this site (`.claude/plans/s147/TASK-14710.md:26`) |
| SEC-057 registered (the 14712 consult residuals 1–2) | `docs/operations/security-finding-register.md:217` | registered at the 14712 merge |
| SEC-055 stays registered | `docs/operations/security-finding-register.md:215`; its premise holds at `808d4297`: approve checks status at `ApprovalEndpoints.cs:386` before its gate at `:409-420`, reject at `:677` before `:689-699`, and reopen's leader arm authorises first (`:1913-1925` before `:1934`) | nothing to change (X4 refreshes its pointers, optional) |
| The ROADMAP notification item the refinement says the docs reference | `ROADMAP.md:465-475` | exists; Block 1b references it |

---

## Block 1a — ADR-040 D3: a pointer to the amendment

**Target:** `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` (lines 70-73).

*Why:* the bullet's first sentence is no longer true. The decision text stays as the S135 record; one indented line tells the reader it was amended and how.

Old text:

````text
- **`is_active`** governs **login/session only** for the ACTOR. The SUBJECT's deactivation state
  is what selects the role floor above (writes for a deactivated leaver require HROrAbove) — and
  the existing self-write exemption (`writeFloor = null` when subject == actor) **yields to it**:
  a deactivated self is not exempt. This is the precise closure of SEC-046.
````

New text:

````text
- **`is_active`** governs **login/session only** for the ACTOR. The SUBJECT's deactivation state
  is what selects the role floor above (writes for a deactivated leaver require HROrAbove) — and
  the existing self-write exemption (`writeFloor = null` when subject == actor) **yields to it**:
  a deactivated self is not exempt. This is the precise closure of SEC-046.
  *(Amended S147, 2026-11-03 — see "Amendment 2026-11-03" below: login also ends the day after the
  last day employed; the floors read "departed" (deactivated, or past the last day), not the flag
  alone; and a departed self is refused whatever their role.)*
````

## Block 1b — ADR-040: the S147 amendment (one dated section, appended)

**Target:** `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` (the file's last four lines, `:336-339`; the new section follows them).

*Where it goes, and why one section:* the amendment changes D3 and adds a precision to D7; it also records how D6 holds for a switched-off leaver. One dated section after the two S138 amendments keeps the record in time order, as those did. D3 gets the pointer in Block 1a.

Old text:

````text
Enforced by `BackdateWorklistDerivation.SettledYearThreatened` (the composition),
`CorrectionReachesSettlementBoundary` (half a) and `SettledYearIntersects` (half b);
pinned in `tests/StatsTid.Tests.Unit/Worklist/BackdateWorklistDerivationTests.cs`, including the
boundary-day case.
````

New text:

````text
Enforced by `BackdateWorklistDerivation.SettledYearThreatened` (the composition),
`CorrectionReachesSettlementBoundary` (half a) and `SettledYearIntersects` (half b);
pinned in `tests/StatsTid.Tests.Unit/Worklist/BackdateWorklistDerivationTests.cs`, including the
boundary-day case.

## Amendment 2026-11-03 — S147: a leaver's last days (QUAL-230; owner rulings OQ-2, Q-B = (c), Q-C = (b))

**Why this exists, in plain words.** Until S147 the system switched a leaver off the day after their last working
day, and from that moment nobody could approve, reject, reopen or pay their final month: not the manager, not HR,
not payroll (QUAL-230). The owner ruled (S147 OQ-2, 2026-10-27) that the leaver's own manager keeps **5 calendar
days** after the last day to finish the month as usual, that from **day 6 only HR** may act, and that the leaver's
**own** access still ends the day after the last day, whatever their role. Building that changed three statements
of D3, so this amendment records the rule as built: TASK-14703, the TASK-14710 stack, TASK-14712 and the
TASK-14713 stack, all merged, their Docker facts green in CI-W2b (run `41310143330`), CI-W3 (run `41335270197`)
and CI-W3b (run `<CI-W3b run id>`). D1, D2, D6 and D7 stand; D7 gains one precision below.

**The three dates of a leaver's final month.** D is the last day employed (`employment_end_date`, inclusive, D1).

| | The leaver, acting | The leaver's record | Their manager | HR and payroll |
|---|---|---|---|---|
| **D** | as any employee | active | as before | as before |
| **D+1 … D+5** | cannot log in; a session opened earlier cannot change anything of their own, whatever their role; their authority as an approver over others has ended | still `is_active`, by design | approves, rejects or reopens from Teamoversigt, as before | as before |
| **D+6 on** | no access | switched off by Step A (the leaver job's switch-off step) | refused | HR approves, rejects, reopens and views the month; payroll's LocalAdmin pays it |

Example: last day Friday 13 March 2026 → login refused from 14 March; the manager acts until 18 March; HR only
from 19 March. **The month's own deadline and payroll export cutoff do not move:** month-end + 5 (here 5 April),
owner-ratified and the same for every employee (`InstitutionalDeadlines`). For a month-end leaver the manager's
last day coincides with that deadline; for a mid-month leaver it falls earlier, and the month's deadline stays.
Nothing new is stored: the hand-over dates are derived from the end date and one constant (K-11).

**What changed in D3.**
1. **"`is_active` governs login/session only for the ACTOR" no longer holds.** Login also refuses a person whose
   last day has passed (`AuthEndpoints.cs:93-98`: the same generic 401 as a wrong password, logged
   `employment_ended`). The floors read **"departed"**: deactivated, OR the last day lies before today's
   Copenhagen date (`DepartedSubject.IsDeparted`, `src/SharedKernel/StatsTid.SharedKernel/Security/DepartedSubject.cs:101-116`).
   The last day itself still counts as employed. `is_active` still decides which reads see a person (active-only,
   or the R9c terminated-inclusive reads: the allowlist of surfaces that may address a switched-off person) and,
   from D+6, who may act for them.
2. **A departed self has no role to fall back on.** D3 said a deactivated self "is not exempt" from the role
   floor. Under that letter, S136's Step 5a accepted that a deactivated HR-tier actor could still register and
   send their own facts (adjudicated behaviour (a), `SPRINT-136.md:89-90`). **That residual is superseded**
   (S147 Step 0b P-12, under OQ-2's "whatever the role"): when the actor is the subject there is no role fallback;
   the HR floor exists for HR acting on others. The two S136 pins now expect 403 and are renamed
   `…_Is403_ReDecidedS147` (`SendCommandTerminatedSubjectTests.cs:309`; `TerminatedEmployeeAccessTests.cs:653`).
3. **D3's role floor covers the grace.** At the self-service sites the floor refuses when the subject has departed
   AND (the actor is the subject OR the actor is below LocalHR) (`Endpoints/Helpers/DepartedSubjectWriteFloor.cs:94-110`).
   So during D+1 … D+5 the manager decides the month (approve, reject, reopen) but cannot write the leaver's hours,
   exactly as from D+1 before S147. HR may still correct them.

**Where the departed-self rule runs (owner Q-C = (b), 2026-10-28: door by door).**
- **Inside the org-scope validator,** in both employee overloads, right after the target read and before the
  terminated-inclusive overload's HR gate (`OrgScopeValidator.cs:115`, `:286`; the test at `:522-524`). It is the
  identity arm only, so a manager still reaches a leaver during the grace and HR still acts on leavers. **It
  refuses reads too:** a departed self is refused on every org-scoped route, reads included (R-30; ruled intended
  at the TASK-14703 security consult, R1). Self-reads that do not pass through the validator stay as before (the
  Employee own-data branch; K-13).
- **Inline at 13 write routes the validator does not reach:** the users PUT, roles grant and revoke, the unit PUT,
  unit delete, unit leaders add and remove, the reporting-line import, the delegate set and clear, the admin
  stand-in set and clear when the manager is the actor, and Payroll `/recalculate` (`Payroll/Program.cs:670-690`).
  Plus a derived guard on `/remove` for an actor among the removed person's reports
  (`ReportingLineEndpoints.cs:1434-1437`). Each answers 403 before its first write.
- **At the seven self-service sites** an Employee token reaches through the validator's own-data branch (the floor
  in point 3 above; `docs/SECURITY.md` lists the sites).
- **The census (P-1, the subject-key rule).** A route is guarded when the departed person's own id is the subject
  key of a row it writes (`users.user_id`, `role_assignments.user_id`, `unit_leaders.user_id`,
  `reporting_lines.employee_id`, `manager_vikar.absent_approver_id`). An id that only holds authority over someone
  else is left to the acting-user rule below. Of the 84 write routes in the three hosts, 32 close through the
  validator; 19 are guarded another way (13 inline, 2 by the self-service floor, 4 by retirement: the two project
  selection routes, retired by TASK-14710, and Payroll `/export` and `/export-period`, which TASK-14716 retires);
  33 are classified out.
- **Alternative declined (Q-C = (b)).** One check at each service's front door, refusing every write by a departed
  actor. It would have covered other people's data as well, but it is a new cross-cutting gate the owner chose not
  to build. So a departed HR, admin or leader's last token can still act on *other people* until it expires:
  SEC-003's residual, and SEC-057 for the grace.

**One message per clause.** The departed-self refusal carries `DepartedSubject.DenyReason`, "A departed employee
cannot act on their own record" (`DepartedSubject.cs:76`), from the validator, the inline guards and the floor. A
below-LocalHR actor on *another* departed subject at a self-service site gets D3's sentence, "Writes for a
deactivated employee require LocalHR or above" (`DepartedSubjectWriteFloor.cs:66`). Where both apply, the self
message wins (`:103-104`). From D+6 a below-HR actor on a switched-off subject at the five surfaces below reads the
validator's "Terminated-employee access requires LocalHR or above" (`OrgScopeValidator.cs:293-297`) where it used
to read "Target employee not found". That reveals the person has left, never when, as S138's send already did.
Every body stays date-free (D3, D7); the server log names the clause.

**HR only from D+6: an R9c allowlist extension (TASK-14713).** From D+6 five surfaces admit a switched-off subject
through `ValidateEmployeeAccessIncludingTerminatedAsync`:
- approve, reject and reopen's leader arm, at the LocalHR floor (`ApprovalEndpoints.cs:409-410`, `:689-690`,
  `:1913-1914`);
- the Skema month view, HR tier only (`SkemaEndpoints.cs:224`, `:262`);
- Payroll `/calculate-and-export`, at the LocalAdmin floor (`Payroll/Program.cs:369-370`).

The manager's edge fails closed for a switched-off subject, so the manager is refused from D+6. **An
employed-month bound runs after the gate** (R-15): a switched-off subject is admitted only for a month that holds
an employed day, otherwise D3's date-free 422 `month_outside_employment_period` (`ApprovalEndpoints.cs:61-85`,
`:117-123`). Because it runs after the gate, an actor the gate refuses sees the same 403 for every month, so the
bound cannot be used to probe employment dates. This is D6 unchanged: a month with any employed day is approved
and paid as a whole. The three decision endpoints lock through a terminated-inclusive tree-lock overload with the
same key (`ReportingLineRepository.cs:787`; the shared helper at `:741` and its six reporting-line callers stay
active-only) and audit against the subject's own organisation. Four-eyes holds across the grace (OQ-1): whoever
sent the month cannot also approve it (SEC-053), so if HR sends after D+5, a second HR-tier person approves. The R9c
inventory is `UserRepository.cs:270-292`.

**D7 precision.** The HRP-011 leaver row is the one HR-scoped read that now carries derived dates, "manager until
D+5, HR only from D+6" (`HrFollowUpApprovalReadRepository.cs:733-737`; refinement R-27). The end date itself still
crosses no boundary, and no employee-facing body carries either date.

**A departed approver (TASK-14712).** The grace would otherwise have kept a departed *manager* in charge of their
reports for 5 days. From D+1 an approver counts only while "acting": `is_active` AND the last day has not passed.
That is checked at five reads: the role floor and its prefetch (`DesignatedApproverAuthorizer.cs:633-668`;
`IAuthorityFactsSource.cs:256-259`), the resolver's activity check and its prefetch
(`ReportingLineRepository.cs:1345-1357`; `PrefetchedReportingLineDataSource.cs:211-218`), and the pending-list
walk (`ApprovalPeriodRepository.cs:388-403`). A departed manager is skipped and the month goes up the chain; whoever
may act on a month also sees it (see = act). The rule only removes authority.
- **Stand-ins (owner Q-B = (c), 2026-10-28; Step 0b P-2).** A leaver's active stand-in survives the leaver's
  deactivation until its end date; HR revokes it (`DELETE /api/admin/reporting-lines/{managerId}/vikar`) or
  reassigns the team with `/remove`, which closes it (`ReportingLineEndpoints.cs:1640-1647`). The resolver still
  consults the primary manager's stand-in whatever that manager's own state (`ReportingLineRepository.cs:1255-1260`,
  `:1290`). That is S74 R3a's behaviour; nothing was built for it. A stand-in a departed manager tries to appoint
  after leaving is refused by the departed-self rule.
- **The holder rule (P-1).** A stand-in who has herself left grants nothing; the month goes back to the manager she
  covered, or further up if that manager has left too (`ReportingLineRepository.cs:1293-1297`, `:1304-1306`).
- **Audit (K-4).** `ReportingLineManagerDeactivated` and the switch-off event `EmployeeEndDateDeactivationApplied`
  still fire when the row is switched off, on D+6 (`SettlementCloseService.cs:565`, `:581`): emitting
  "deactivated" on D+1 for a row that is still active would be false. The D+1 routing is reconstructable from
  `EmployeeEmploymentEndDateSet` plus this rule.
- **Not checked for departure (SEC-057).** The HR/Admin org-scope fallback on approve, reject and reopen, and the
  overtime decisions, trust the token's scopes. A departed HR or admin user's last token, minted on D at the
  latest, can still act on others until it expires (480 minutes).

**The switch-off (TASK-14710).** One constant, `ManagerGraceCalendarDays = 5`
(`EmploymentEndDateLifecycleWriter.cs:84`), read through one helper (`IsPastGrace` and
`GraceDeactivationCutoff`, `:99-108`) by both deactivation paths: Step A's `IsDeactivationDue`
(`SettlementCloseService.cs:1052-1054`), whose two SQL copies bind the same cutoff (`:464`, `:476`, `:534`), and
the writer's same-transaction `ComputeEndDateLifecycle` (`EmploymentEndDateLifecycleWriter.cs:303`). Step A and its
events fire on D+6 (K-4). Termination settlement runs from D+6 (`SettlementCloseService.cs:765-776`; K-2), so the
§26 payout request, which needs a SETTLED termination settlement, also opens from D+6
(`TerminationPayoutRequestEndpoints.cs:194-210`, `:245`; K-12). The special-holiday active pass excludes a passed
end date (`SettlementCloseService.cs:912-938`; K-3). `/remove` still deactivates at once, which ends the grace early
(`ReportingLineEndpoints.cs:1677-1686`; K-6, accepted).

**Residuals recorded, not fixed.**
- **Two reads of "today" (PAT-028, compute today once per operation; TASK-14703 consult R6).** The validator reads
  the Copenhagen date from its own `TimeProvider` once per call (`OrgScopeValidator.cs:524`); the handler reads it
  again for its own dates. The two can differ only for a request that straddles Copenhagen midnight, and the
  self-service sites re-check in-lock. **And (consult R4):** the clear routes reuse one date across a retry, so a
  lock wait that crosses midnight acts on the day the request arrived (the delegate clear reads it at
  `ReportingLineEndpoints.cs:2329`, before its retry at `:2346`; the admin stand-in clear at `:2828`, before
  `:2851`). The create routes re-read the clock per attempt.
- **A reversal corrected into the grace (K-14).** A settlement reversal whose corrected end date lands inside the
  grace reactivates the leaver (the writer's rule: a correction into the grace reactivates), so its re-settle finds
  nothing due, refuses `SupersedeNotEligible`, and the whole reversal rolls back
  (`SettlementReversalService.cs:381-391`). The operator bare-reverses, or retries from day 6.
- **The import.** During the grace a departed manager can still be named as a manager in the bulk reporting-line
  import, which checks only `is_active` (`ReportingLineEndpoints.cs:1042-1046`). The acting-user rule gives that
  manager no authority from D+1.

**What was given up, and why.** A front-door gate (declined, Q-C = (b)). Moving the month's deadline with the
grace (rejected: the deadline and cutoff are owner-ratified and the same for every employee; K-11). Storing a
hand-over date (not needed: it is derived). Emitting "manager deactivated" on D+1 (it would be false; the D+1
routing is reconstructable instead).

**Not built here:** telling the manager that the window is open or closing — the ROADMAP item "Notify the manager
to give a leaver's final month its last approval".

**Enforced by** `DepartedSubject`, `DepartedSubjectWriteFloor`, the validator clause and
`EmploymentEndDateLifecycleWriter.IsPastGrace`. **Pinned by** the Unit facts `DepartedSubjectTests`,
`DepartedSubjectWriteFloorTests`, `EndDateLifecycleWriterParityTests` and `SettlementCloseLeaverLogicTests`; the
Docker facts in `tests/StatsTid.Tests.Regression/Leaver/`, `Security/DepartedSelf*Tests.cs`, `LeaverLoginTests`,
`DepartedApproverTests` and `RecalculateDepartedSelfTests`; and the screen's `FollowUpLists.leaverActions.test.tsx`.
````

## Block 1c — The KB index row for ADR-040

**Target:** `docs/knowledge-base/INDEX.md` (line 48, inside the ADR-040 row).

*Why:* the index summarises D3 with the sentence that no longer holds; a reader who stops at the index would get the old rule.

Old text:

````text
`is_active` = login/session only — plus DATE-FREE rejection bodies
````

New text:

````text
`is_active` = login/session only (**amended S147, 2026-11-03:** login and the write floors key on "departed" = deactivated OR past the last day; a departed self is refused whatever the role; the manager keeps a 5-calendar-day grace, then HR only from day 6) — plus DATE-FREE rejection bodies
````

## Block 2 — QUAL-230 closed

**Target:** `docs/operations/quality-finding-register.md` (line 454, the QUAL-230 row). Two parts, as for QUAL-229 and QUAL-235: the status cell becomes CLOSED with a summary and the original state kept as *Was:*; the progress note in the "→" cell is replaced by its outcome.

### 2a — the status cell

Old text:

````text
**REGISTERED (tender gap analysis, 2026-10-26; an independent static trace, not yet adjudicated).** Found by the tender analysis (`docs/references/tender-requirements-analysis.md`, defect D3)
````

New text:

````text
**CLOSED (S147; closed when CI-W3b ran green: run `<CI-W3b run id>`, `<CI-W3b date>`).** A leaver's final month can now be finished and paid. The leaver's own access ends the day after the last day: login is refused and nothing of their own can be changed, whatever their role (TASK-14703 `8d2b5716`; the TASK-14710 stack `9c7866f0`). Their manager keeps 5 calendar days to approve, reject or reopen, because the automatic switch-off moved to day 6 (one constant, `ManagerGraceCalendarDays`); a departed manager's own authority over others ends on day 1 (TASK-14712 `52ab15c8`). From day 6 HR approves, rejects, reopens and views the month and payroll's LocalAdmin pays it, for a month with an employed day; HR's leaver list shows whose turn it is and offers the actions (the TASK-14713 stack `652f26aa`). The RED-first test the "→" cell asked for exists: `LeaverHrOnlyTests` (day 6: the manager refused, HR approves, LocalAdmin exports), `LeaverGraceExportTests`, `LeaverOrphanSendApproveTests` (send, four-eyes, approve, export). Docker facts green in CI-W2b (run `41310143330`), CI-W3 (run `41335270197`) and CI-W3b. The rule is recorded in ADR-040's amendment of 2026-11-03. **Residuals:** SEC-057 (a departed HR or admin user's org-scope authority over others during the grace); the amendment's recorded residuals (two reads of "today", a reversal corrected into the grace, the import); the manager notification is a ROADMAP item. *Was:* REGISTERED (tender gap analysis, 2026-10-26; an independent static trace, not yet adjudicated). Found by the tender analysis (`docs/references/tender-requirements-analysis.md`, defect D3)
````

### 2b — the "→" cell's progress note

Old text:

````text
**S147 progress (2026-11-01):** T5a merged: the departed-self rule (TASK-14703 `8d2b5716`); the 5-day manager grace, login refused from day 1, and the self-service write floor (TASK-14710 stack `9c7866f0`); a departed approver loses authority over others on day 1 (TASK-14712 `52ab15c8`). Docker evidence at CI-W3. T5b (TASK-14713/14714, HR only from day 6) remains; the row closes at O-6 with the ADR-040 amendment.
````

New text:

````text
**S147 outcome:** built as the owner ruled on OQ-2 (5 calendar days for the manager, then HR) rather than as the R9c-only ruling sketched above; the raw route is no longer needed to pay a leaver's month (`/calculate-and-export` admits a switched-off leaver at the LocalAdmin floor). T5a: TASK-14703 `8d2b5716`, the TASK-14710 stack `9c7866f0`, TASK-14712 `52ab15c8` (Docker evidence CI-W2b and CI-W3). T5b: the TASK-14713 stack `652f26aa` (CI-W3b). Closed at O-6 with ADR-040's amendment of 2026-11-03.
````

## Block 3 — HRP-011: the grace, the HR actions, the hand-over date, aging unchanged

**Target:** `docs/operations/hr-follow-up-process-register.md` (line 117, the HRP-011 row; the "Resolution + audit" cell, followed by the "Class" cell). The note goes in the resolution cell, as HRP-003's S145 and S147 notes did (`:100`). The row's S139 analysis stays as written, as the register's "What S140 BUILT" section explains (`:299-303`).

Old text:

````text
| standard send-command outbox + audit | **silent state** |
````

New text:

````text
| standard send-command outbox + audit. **S147 (QUAL-230 closed; owner OQ-2; TASK-14710 … 14714, merged by `652f26aa`): whose turn it is now depends on the day.** D is the last day employed. **D+1 to D+5** (the manager's grace): the leaver is still active, cannot log in, and their manager approves, rejects or reopens the month from Teamoversigt as before; HR can act too, as for any employee. **From D+6** the leaver job switches the person off and only HR may act: approve, reject and reopen at the LocalHR floor, and payroll's `/calculate-and-export` at LocalAdmin, for a month that holds an employed day (a month with none gets a date-free 422). The list shows the final month from D itself, because it keys on the end date, never on `is_active` (`HrFollowUpApprovalReadRepository.cs:373-378`). **The hand-over date:** each leaver row says whose turn it is, for example "Leder til og med 18. marts 2026, kun HR fra 19. marts 2026" (`FollowUpLists.tsx:217-222`). It is derived from the end date plus `ManagerGraceCalendarDays` (`HrFollowUpApprovalReadRepository.cs:733-737`); nothing new is stored, and this HR-scoped row is the one place the dates may appear (ADR-040 amendment 2026-11-03, D7 precision). **The HR actions on the row:** "Åbn måned" opens the month in the same read-only grid the manager sees; "Send måned" appears where no period exists or it is DRAFT or REJECTED; "Godkend" and "Afvis" appear where the month is SUBMITTED or EMPLOYEE_APPROVED (`FollowUpLists.tsx:281-282`). Each is offered only where the server accepts it, and refusals are shown in Danish. If HR sends the month, a second HR-tier person approves it (SEC-053). **Aging is unchanged:** the row is aged from the month's manager deadline, month-end + 5 (`HrFollowUpApprovalReadRepository.cs:331`, `:349`), not from the hand-over date | **silent state** |
````

## Block 4 — HRP-014: a departed manager's stand-in

**Target:** `docs/operations/hr-follow-up-process-register.md` (line 120, the HRP-014 row; the "Resolution + audit" cell).

*Why here:* HRP-014 is the register's row for stand-in (vikar) cover. The owner's Q-B = (c) means a departed manager's stand-in does *not* end with the manager, so HR is the one who ends it.

Old text:

````text
| revoke / re-delegate write audit rows |
````

New text:

````text
| revoke / re-delegate write audit rows. **S147 (owner Q-B = (c), 2026-10-28; TASK-14712):** a departed manager's stand-in keeps signing until its end date, and HR revokes it (`DELETE /api/admin/reporting-lines/{managerId}/vikar`) or reassigns the team with `/remove`, which closes it. Nothing ends it when the manager leaves: neither the manager's last day nor the day-6 switch-off. A stand-in who has herself left grants nothing; the month goes back to the manager she covered, or further up if that manager has left too (ADR-040 amendment 2026-11-03) |
````

## Block 5 — SEC-046: the S147 note completed

**Target:** `docs/operations/security-finding-register.md` (line 192, the SEC-046 row; the end of its last cell, after the existing 2026-11-01 note).

Old text:

````text
The ADR-040 amendment (O-6) records it. |
````

New text:

````text
ADR-040's amendment of 2026-11-03 records it. **S147, the class closed for every role (O-6, 2026-11-03):** a departed person cannot change their own record with a still-valid token, whatever their role: at the seven self-service sites (`DepartedSubjectWriteFloor`, R-21) and inside the org-scope validator's two employee overloads (R-30; `OrgScopeValidator.cs:115`, `:286`), plus 13 inline guards and `/remove`'s derived guard. This supersedes S136's Step-5a adjudication (a) (`SPRINT-136.md:89-90`), which accepted a deactivated HR-tier actor registering their own facts under D3's letter: the two S136 pins now expect 403 and are renamed `…_Is403_ReDecidedS147` (`SendCommandTerminatedSubjectTests.cs:309`, `TerminatedEmployeeAccessTests.cs:653`; Step 0b P-12). |
````

## Block 6 — `SECURITY.md`: the census summary, corrected and completed

**Target:** `docs/SECURITY.md` (line 122).

*What is wrong today:* the line says "19 inline". The 19 are 13 inline guards, 2 routes covered by the self-service floor, and 4 routes guarded by retirement (`.claude/plans/PLAN-s147.md:237`, `:240`). Two of those four retirements are the raw Payroll `/export` and `/export-period`, which TASK-14716 retires and which have **not** merged: at `808d4297` both are live, `GlobalAdminOnly`, with no scope check and no departed-self check (`Payroll/Program.cs:223-273`, `:275-328`). The line also lacks the sorting rule the owed list names (the subject key).

Old text:

````text
- Thirteen inline guards cover the write routes the validator does not reach, plus a derived guard on `/remove`. The S147 census counted 84 write routes: 32 shared-check, 19 inline, 33 not keyed on the actor. TASK-14710 later retired the two project selection routes (`POST`/`DELETE /api/projects/{orgId}/select/{projectId}`), so the inventory is two smaller.
````

New text:

````text
- Thirteen inline guards cover the write routes the validator does not reach, plus a derived guard on `/remove`.
- **The S147 census (TASK-14703; Step 0b P-1, the subject-key rule).** A write route is guarded when the departed person's own id is the *subject key* of a row it inserts, updates or closes: `users.user_id`, `role_assignments.user_id`, `unit_leaders.user_id`, `reporting_lines.employee_id` or `manager_vikar.absent_approver_id`. An id that only *holds* authority over someone else (a manager, an acting manager, a stand-in, `/remove`'s replacement) is left to TASK-14712's acting-user rule, which makes a departed holder inert from day 1. Of the 84 write routes in the three hosts:
  - **32** reach the employee through the shared check;
  - **19** are guarded another way: 13 inline guards, 2 by the self-service floor below (row-preferences, compensation-choice), and 4 by retirement;
  - **33** are classified out: 31 with no employee subject, 1 that cannot address an existing person, and the anonymous login.

  Of the four retirements, TASK-14710 retired the two project selection routes (`POST`/`DELETE /api/projects/{orgId}/select/{projectId}`). The other two are the raw Payroll `/export` and `/export-period`, which TASK-14716 retires (SEC-054); until it merges they are `GlobalAdminOnly` routes with no departed-self check. The owner kept this door-by-door design rather than one check at each service's front door (Q-C = (b)).
````

## Block 7 — SEC-003: own data closed centrally; the residual is other people's data

**Target:** `docs/operations/security-finding-register.md` (line 74, the SEC-003 row; the end of its last cell, after the existing 2026-11-01 note).

Old text:

````text
org-scope authority over others in that window is SEC-057. |
````

New text:

````text
org-scope authority over others in that window is SEC-057. **Own data is closed centrally (S147, O-6, 2026-11-03):** the departed-self identity clause runs inside the org-scope validator, so a departed person's own record is refused on every org-scoped route, reads included (R-30), and the inline guards and the self-service floor cover the rest. This row's residual is therefore **other people's data** only, and the S147 grace does not widen it: login is refused from day 1, as the D+1 switch-off refused it before, so the exposure is still one token minted on the last working day. **The revisit's shape (R-25):** a per-request check of the *actor's* state (`DepartedSubject.IsDeparted` on the actor, in an endpoint filter) would close every token-scope route at once; SEC-057 is this residual's instance during the grace. |
````

---

## Beyond the owed list (optional; each with a lean)

### X1 — `SECURITY.md`: the stand-in and holder rules beside the approver rule · lean: apply

**Why:** `SECURITY.md:134` names TASK-14712's day-1 rule and SEC-057 but not the owner's stand-in ruling, which is the one place where a departed manager's choice keeps granting authority. A security reader looks here, not in HRP-014.

**Target:** `docs/SECURITY.md` (line 134).

Old text:

````text
- Acting on *other* people stays governed by the existing rules, SEC-003's accepted residual. A departed person's edge-based approver authority ends on day 1 (TASK-14712). Their org-scope authority during the grace is SEC-057.
````

New text:

````text
- Acting on *other* people stays governed by the existing rules, SEC-003's accepted residual. A departed person's edge-based approver authority ends on day 1 (TASK-14712). Their org-scope authority during the grace is SEC-057. A stand-in appointed before the manager left keeps approving until its end date, or until HR revokes it or removes the manager (owner Q-B = (c)); a stand-in who has herself left grants nothing, and the month goes back to the manager she covered (`ReportingLineRepository.cs:1290-1306`).
````

### X2 — `SYSTEM_TARGET.md` §N: the product definition still says "`is_active` governing login/session only" · lean: apply, **after the owner approves**

**Why:** the product definition states the S135 rule that S147 changed, and it says nothing about the owner's leaver timeline (OQ-2) or the stand-in ruling (Q-B = (c)). This is an approval of wording for rulings the owner already gave, not a new ruling. The note follows O-10's format.

**Target:** `SYSTEM_TARGET.md` (lines 453-457, § N "Employment Lifecycle & Time-Control").

Old text:

````text
- **Enforcement**: registration (time/absence/skema) outside the window is refused with date-free
  errors; window edits that would strand existing registered data are refused; approval keeps
  whole-month geometry with content window-clean by construction; access for departed employees is
  role-gated (HR corrections via the terminated-inclusive paths), with `is_active` governing
  login/session only.
````

New text:

````text
- **Enforcement**: registration (time/absence/skema) outside the window is refused with date-free
  errors; window edits that would strand existing registered data are refused; approval keeps
  whole-month geometry with content window-clean by construction; access for departed employees is
  role-gated (HR corrections via the terminated-inclusive paths).
- **A leaver's last days**: the leaver's own access ends the day after their last working day,
  whatever their role: login is refused and nothing of their own can be changed. Their manager keeps
  5 calendar days after that day to approve, reject or reopen the final month; from day 6 only HR may
  act, and payroll pays the month. The month's deadline and payroll cutoff do not move. A departed
  manager's authority over others ends the day after their own last day; a stand-in appointed before
  they left keeps signing until its end date or until HR revokes it. *(S147, `<approval date>`: ADR-040
  amendment 2026-11-03; owner rulings OQ-2 and Q-B = (c); owner-approved spec update)*
````

### X3 — K-2's second site: the termination-due predicate's comment · lean: apply (small-task exception, or the tidy pass) · **`src/**`, comment only**

**Why:** the refinement's K-2 named two sites (`REFINEMENT…:358`: `SettlementCloseService.cs:750-754` and `VacationSettlementService.cs:1158`). TASK-14710's brief named only the first (`TASK-14710.md:26`), so the in-lock predicate's clause (1) still explains `isActive` only as "reactivated". Under the grace an active subject may also be a leaver in D+1 … D+5, and the reversal residual K-14 runs through this clause. `VacationSettlementService.cs` is unchanged since `6b987c5b` (empty `git diff --stat`), so the line is `:1158`'s docstring, `:1138-1140`.

**Target:** `src/Infrastructure/StatsTid.Infrastructure/VacationSettlementService.cs` (lines 1138-1140).

Old text:

````text
    ///   <item><description><paramref name="isActive"/> is FALSE — a reactivated user is NOT a
    ///   leaver (Step B only ever enumerates flipped leavers; the R1 correct-to-future
    ///   re-evaluation REACTIVATES);</description></item>
````

New text:

````text
    ///   <item><description><paramref name="isActive"/> is FALSE — a reactivated user is NOT a
    ///   leaver (Step B only ever enumerates flipped leavers; the R1 correct-to-future
    ///   re-evaluation REACTIVATES). S147 / TASK-14710 (K-2, OQ-2): a leaver inside the manager's
    ///   5-day grace (D+1 .. D+5) is still active by design, so termination settlement is due only
    ///   from D+6, once Step A has switched them off; a reversal whose corrected end date lands in
    ///   the grace reactivates the leaver, so its re-settle is not due here (K-14);</description></item>
````

### X4 — SEC-055: refresh its line pointers · lean: apply (pointers only; the row stays REGISTERED)

**Why:** the row's premise still holds at `808d4297` (see "Already done"), but every line it cites moved by about 120 lines after TASK-14704 and TASK-14713. A "revisit, not shield" row is only useful if its pointers lead to the code.

**Target:** `docs/operations/security-finding-register.md` (line 215, the SEC-055 row). Two parts.

**X4a** Old text:

````text
In both handlers the status check runs before the authorisation gate (`ApprovalEndpoints.cs:261` then `:274-285`; `:491` then `:499-509`)
````

New text:

````text
In both handlers the status check runs before the authorisation gate (`ApprovalEndpoints.cs:386` then `:409-420`; `:677` then `:689-699`, at `808d4297`)
````

**X4b** Old text:

````text
The reopen handler's leader arm already authorises first (`:1657-1669` before `:1671`).
````

New text:

````text
The reopen handler's leader arm already authorises first (`:1913-1925` before `:1934`).
````

**X4c** Old text:

````text
`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:261, :274-285, :491, :499-509, :1657-1671`
````

New text:

````text
`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:386, :409-420, :677, :689-699, :1913-1934` (at `808d4297`)
````

### X5 — SEC-057: refresh the approval fallback pointer · lean: apply

**Why:** the row cites the HR/Admin fallback at `ApprovalEndpoints.cs:293-304`, which at `808d4297` is the send adapter. The fallback is at approve `:409-420`, reject `:689-699` and reopen's leader arm `:1913-1925`. The overtime pointers (`OvertimeEndpoints.cs:360`, `:458`) are the route declarations and still correct.

**Target:** `docs/operations/security-finding-register.md` (line 217, the SEC-057 row). The text `ApprovalEndpoints.cs:293-304` occurs **twice** in this row (the finding and the source cell); replace both (replace-all within the file: the string occurs nowhere else; checked).

Old text (both occurrences):

````text
ApprovalEndpoints.cs:293-304
````

New text:

````text
ApprovalEndpoints.cs:409-420, :689-699, :1913-1925
````

### X6 — ROADMAP: the notification item can use the date the screen already shows · lean: optional

**Why:** K-11 recorded that the reminder's date is the end date plus the grace, with no storage needed. From TASK-14713 that date is already derived and shown on HRP-011's rows, so whoever builds the notification has its date.

**Target:** `ROADMAP.md` (line 472).

Old text:

````text
  - after it, the month is on HR's leaver list (HRP-011, `docs/operations/hr-follow-up-process-register.md`).
````

New text:

````text
  - after it, the month is on HR's leaver list (HRP-011, `docs/operations/hr-follow-up-process-register.md`).

  The dates need no storage: the manager's last day is the end date plus `ManagerGraceCalendarDays` (5), which
  HR's leaver row already shows ("Leder til og med …, kun HR fra …", S147 TASK-14713).
````

---

## Not drafted here (for other O-items)

- **SEC-054 (O-7).** Its last sentence, "Note QUAL-230: today the raw route is probably the only way to pay a deactivated leaver's month" (`security-finding-register.md:214`), is no longer true: `/calculate-and-export` admits a switched-off leaver from TASK-14713. O-7 closes the row and should drop or past-tense that sentence. O-7 should also update Block 6's census sentence ("until it merges they are `GlobalAdminOnly` routes with no departed-self check") when TASK-14716 retires the two routes.
- **The tender analysis rows EM A33 and VD 33 (O-12)** re-rate QUAL-230 at C-1.
- **Stale "closed spell means already flipped" comments** in `src` (`EmploymentDateEndpoints.cs:107-109`, `UserRepository.cs` ~`:490`, `User.cs:50-53`) and the K-14 wording in the reversal's failure text ("or retry from day 6", `SettlementReversalService.cs:387-391`) are tidy items 20 and 22, not O-6.

## Brief contradictions, deviations and open points

**Claims about the existing code (checked; the code governs):**
1. **The import residual's lines.** Tidy item 20 cites `ReportingLineEndpoints.cs:1030`, `:1042` for "a departed manager can still be imported as a manager". At `808d4297` (and at `9c7866f0`) `:1030` is the *employee's* `is_active` check; the manager's check is `:1036-1046`, the test itself at `:1042`. Block 1b cites `:1042-1046`.
2. **The holder rule's line.** The 14712 merge record cites the stand-in holder check at `ReportingLineRepository.cs:1220`; later merges moved it to `:1293-1294`. Block 1b cites the current line.
3. **`SECURITY.md:122`'s "19 inline".** The 19 are 13 inline, 2 self-service floor and 4 retirement, and 2 of the 4 retirements (Payroll `/export`, `/export-period`) are TASK-14716, not merged. So of the 19, 17 are guarded today and 2 are open until TASK-14716. Block 6 states it that way. The plan already records this route ("if the owner cuts 14716, the inline predicate goes into both handlers in the cut's fix", `PLAN-s147.md:237`), so it is a wording fix, not a new finding.
4. **K-2.** The O-6 row lists "the settlement comments (K-2)" as owed; the refinement named two sites, and TASK-14710's brief named only one. The first is done; the second is X3 (`src`, comment only).
5. **SEC-055 and SEC-057 pointers** are stale (X4, X5); their substance holds.

**Claims about required behaviour:** none contradicted. Every rule in Blocks 1b–7 matches the owner's rulings (OQ-1, OQ-2, Q-B = (c), Q-C = (b)) and the Step 0b rulings P-1, P-2, P-10, P-12 as the plan records them.

**Needs a ruling:** none. **Needs an approval:** X2 changes `SYSTEM_TARGET.md`, which CLAUDE.md reserves for the owner's approval even when the substance is an existing ruling (plan line 300). If the Orchestrator does not ask now, the stale § N sentence stays until it does; Blocks 1a–7 do not depend on X2.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-03 | `planner` (Opus 5.5) | First draft at `808d4297`: Blocks 1a–7 for every owed O-6 item and every "owed at O-6" record in `SPRINT-147.md`; X1–X6 beyond the list; "Already done" with evidence; five code-claim corrections |
