# PLAN — Sprint 145: payroll works week by week (QUAL-186), then months with a mid-month change become payable (QUAL-149/150)

*Draft 5, 2026-10-02, for Step 0b cycle 4 on both lenses (cap 5), scoped by the cycle-3 ruling's § 4. Draft 5 applies the Fable seat's rulings R60–R66 on Step 0b cycle 3 (internal lens READY-WITH-WARNINGS; external NOT-READY, 2 B, both verified by the seat) and the owner's ruling OQ1 ("Keep for S145"). Under R66b it is also slimmed, by move only. Each task brief now lives in `.claude/plans/s145/TASK-<id>.md`, linked from the ledger. The ruling texts, the brief contradictions, the draft-to-draft absorption tables and the revision history move to the sprint log's § "Planning record (Step 0b)". Draft 4 applied R52–R59, the mechanical partition table (R59); draft 3 applied R41–R51. Before that, Drafted by the `planner` (Opus) under "Fable judges — Opus drafts". draft 2 applied the Fable seat's rulings on draft 1 (`adjudicator`, `claude-fable-5-1`, verdict RULED): all fifteen brief contradictions confirmed (two entries corrected), N-1 ruled (B) as **R26**, the fifteen drafting proposals accepted as **R25 and R27–R40** (eight with amendments), the evidence partition accepted with three mutations relocated, and five plan defects fixed. Source of truth: `.claude/refinements/REFINEMENT-s145-qual149-150-genuine-splits.md` rev 3.4, READY (882 lines); every requirement below comes from it and from the rulings it records. The refinement is gitignored (`.gitignore:39`). This directory is not (`PLAN-s144.md` is tracked), but every brief (now `.claude/plans/s145/TASK-*.md`, R66b) is still written to be pasted whole, with the refinement lines it names pasted verbatim beside it, because a prompt is the only channel guaranteed to reach a worktree (`docs/AGENTS.md:212-240`).*

| | |
|---|---|
| **Status** | PLANNED — draft 5. Step 0b cycles 1–3 are absorbed (rulings R41–R66; owner OQ1). Next: Step 0b cycle 4 of 5 on both lenses, **scoped** by the cycle-3 ruling's § 4, plus the move map and a content diff of the R66b split (`s145-plan-move-map.md`). D1 and E1 (draft 4) stand; R61 and R63 build on them. **Stage B, B.0 revision 1 (2026-10-16):** the Fable seat's rulings R-53..R-79 on the scoped Step-0b cycle 1 are absorbed here (close sequence, label list, Stage B mutation outline, records owed) and in the nine Stage B briefs. **B.0 revision 2 (2026-10-19):** the cycle-2 rulings R-80..R-92 (adjudicated on Opus by the owner's direction of 2026-10-19) are absorbed here (EB-1 = 14 facts, the close sequence's failure path and patch validity, the Step 7a per-pass label table, the Stage-A-only reconciliation of QUAL-209/210/211) and in the briefs. **B.0 revision 3 (2026-10-19):** the cycle-3 rulings R-93..R-105 (adjudicated on Opus by the owner's direction of 2026-10-19) are absorbed here (full object ids in the saved patches, one code-set path list, C-4's push order and commit-message rule under the close guard, re-cuts that never overwrite a frozen patch and a SHA-256 record of each, the fallback after the green push, the relocated-assertion and label-qualifier rule) and in the briefs; the scoped Step-0b cycle 4 returned READY on both lenses, and the seat's rulings R-106..R-112 are applied verbatim. **The Stage B briefs and the plan's Stage B and close sections are signed at B.0 (R-113, 2026-10-19; the seat on Opus by the owner's direction).** Next: B.1 |
| **Theme** | Payroll stops judging a whole month against one week's norm, which today makes every ordinary month's overtime, merarbejde and flex wrong (QUAL-186). Then a month with a mid-month change to hours, position or agreement becomes payable, each part under its own wage-type codes (QUAL-149/150). First, a corrected Skema cell stops being counted twice (QUAL-192..194) |
| **Predecessor** | S144: close `cbaa5e0` (CI red: nine test-setup failures) · governance `6b0d982` (review cap 3 → 5; the close guard's CI-health query, `.claude/hooks/sprint-close-guard.ps1:322`) · post-close fix `8a87d16` (CI green `36610948637`) · docs-only `018ef2a` (QUAL-186), `6849b84` (QUAL-187..191 + two ROADMAP lines), `de6c228` (S144 C-9), `62fcdc3` (QUAL-192..194) |
| **Base commit (Step 7a)** | **`cbaa5e0`**, the S144 close commit, not HEAD (`docs/WORKFLOW.md:38`, "the next sprint's base is the previous sprint's close commit"). So `6b0d982` (the hook change, never reviewed on its own), `8a87d16` (the post-close fix) and the five docs commits (`018ef2a`, `6849b84`, `de6c228`, `62fcdc3`, `299c3dd`) all fall inside S145's Step-7a diff |
| **Master at draft 2** | **`299c3dd`** (S145 opened at planning). It is docs-only since `018ef2a`: `git diff 018ef2a 299c3dd -- src tests frontend docker tools` is empty, so the code S145 starts from is `8a87d16`'s. Every file:line in this plan was verified at `62fcdc3`, which is code-identical to `299c3dd` |
| **Orchestrator model** | Opus 5.5 seat throughout (client 2.1.285), never switched (owner ruling 2026-09-29, `docs/WORKFLOW.md:468-479`). Drafting: `planner` (Opus 5.5). Judgment: `adjudicator` and `reviewer` (Fable 5.1). Implementation by role name, never with a `model` override: `rule-engine`, `payroll-integration`, `backend-infrastructure` (Opus); `data-model`, `test-qa`, `ux`, `constraint-validator` (Sonnet); `sweep` (Haiku). External lens: Codex (`gpt-6`) |
| **Refinement** | rev 3.4 — READY after five Step-4 cycles; cycle 5 READY-WITH-WARNINGS on both lenses, no blocker. The carried warnings are absorbed below (§ "Carried warnings") |
| **Owner rulings in force** | **OQ1 = Keep for S145** (2026-10-02, in session: the evidence design stays, lightened by R66a) · **Q1** both stages, hard gate between (2026-10-01) · **Q2 = A** per working day · **Q3 = F1** settle when the week closes · **Q7 = (b)** name the next month · leans in force, no objection: **Q5 (c)** refuse a mid-week agreement change, **Q6 (a)** raw routes refuse split months from Stage B · standing: reseed, don't migrate; review cap 5 per lens |
| **Owner informed, no objection** | Q4 (Fable seat R4: freeze what is data, fingerprint what is code) · R14 (payroll reads its own hours) · a Skema save replaces the cell, fixed in Stage A · R24 (clearing a cell stays out of S145) |
| **Entropy scan (Step 0a)** | Done 2026-10-01 at `62fcdc3` (`docs/sprints/SPRINT-145.md` header): tree clean except an uncommitted ROADMAP line; 0 worktrees; no non-master branches beyond pre-existing dependabot remotes; 0 untracked source |
| **Baseline** | S144 final (`SPRINT-144.md:6`): build 0 errors / **145** warnings · CA2100 ratchet 115 · Unit **1309** (CI) · DemoSeed **170** · Regression non-Docker **128** (2016 in CI) · Smoke 7 · frontend vitest **988**. Re-measured at the first gate by `sprint-test-validation`, never carried forward |
| **Pre-merge gate G-0** | **SATISFIED.** The rule was "no S145 code merges until the CI run on master `62fcdc3` (S144's record push) is green". That run is green: `36842927335`, all 7 jobs. The run on `299c3dd` (docs-only) was in progress at draft 2, with its docs job already green; the Orchestrator records its result in the handoff block before the first merge |

---

## Plain-language summary (read this first)

**What is wrong today, in three parts.**
1. **Every ordinary month is paid wrong (QUAL-186).** The overtime, merarbejde (extra work paid at the normal rate) and flex rules are written for one week. Payroll hands them a whole month as if it were one week. A full-time HK employee who works exactly 37 hours every week of September 2026 is exported with 3.0 h of overtime at 50%, 122.8 h at 100% and a 25.8 h flex payout. An AC employee would get 125.8 h of merarbejde.
2. **A month with a mid-month change is refused (QUAL-149).** When an employee's hours, position or agreement changes partway through a month, payroll refuses the month and HR handles it by hand. When those months become payable, each part must be paid under its own wage-type codes (QUAL-150).
3. **A corrected Skema cell counts twice (QUAL-192..194).** Changing 7.4 h to 8.0 h stores a second row. The screen shows 8.0, but every server total adds both rows (15.4 h). The send check then refuses the month with no way for the employee to repair it, and a re-saved ferie day uses up quota twice.

**What S145 does, as you ruled (Q1): two stages with a hard gate.**
- **Stage A** fixes part 3 first, then part 1, then the wage-type-code half of QUAL-150. Payroll works week by week. It reads the recorded hours itself, takes each month's codes from the employee's dated record, records exactly what each calculation used, and checks that neighbouring months fit together. Split months stay refused exactly as today.
- **The gate.** Stage A must be merged, reviewed task by task, green in CI, and its deliberate-breakage evidence must match before any Stage B task is even briefed. "Stage A closed" is a named checklist (§ "Gate A-CLOSE").
- **Stage B** then switches the refusal off: split months become payable, each part under its own codes. If Stage B cannot pass review within the five-cycle cap, Stage A ships alone; that is your ruled fallback, not a new decision.

**What you will see change.**
1. **Every month's overtime, merarbejde and flex figures change, correctly.** The September example pays no overtime and no flex payout. A month at or under norm is unchanged. Overtime earned in a month's last days now appears in the next month's payroll, because the week is settled when it closes (Q3). An unsplit month takes its wage-type codes from the employee's dated record instead of from the request.
2. **Two new refusals.** (a) An agreement change in the middle of a week — on a non-Monday 1st, or in the previous month's last days that the month's first week includes — is refused with a 422 on every payroll route. (b) On the two calculating routes, a month whose first week reaches the previous month is refused with a 409 until that previous month is APPROVED. A previous month exported through the raw routes has no approval row, so it must be approved after the fact.
3. **Payroll stops accepting hours in the request.** Nobody sends them in practice. A request that carries hours now gets a clear 400 (`payroll-entries-not-accepted`), and payroll reads the recorded hours itself, which is what makes a payslip provable later.
4. **The Skema re-save fix.** A corrected cell counts once everywhere: the send check, the team overview, balances, compliance, the vacation quota and the year-end and leaver settlements. A retried save adds nothing. A save of 0 or negative hours is refused with a 400. An employee still cannot empty a cell; that is on the backlog (R24).
5. **Figures that move in the demo seed.** Any demo month that someone calculates or exports now shows the corrected overtime and flex. A demo month whose previous month is not APPROVED is refused with the new 409 until it is. The demo loader itself is unaffected: it already avoids re-saving a cell (`tools/StatsTid.DemoSeed/Loading/ApiClient.cs:67-77`).
6. **Stage B only:** split months become payable, and the worklist badge shows only the one case that stays manual (a mid-week agreement change).

**How the work is organised.** Stage A runs in six steps ("waves"). The payroll core file is changed by four tasks in a row, because two agents editing it at once would collide. The database and test work runs in parallel around them. Every task passes three checks before it is merged: the build, the mechanical rule check (Step 5α) and the review (Step 5a). The sprint ends with the usual whole-sprint review (Step 7a) and the close.

**What this plan does differently from S144, and why.**
- **The per-task checks are gates, not afterthoughts.** In S144 seven tasks were merged on a green build alone, and Steps 5α/5a ran only at the end (`SPRINT-144.md:290-292`). Here every task row carries its 5α and 5a cells, and a task with an empty cell cannot be merged (§ "The merge checklist").
- **"Paired test passes."** Several changes intentionally break existing tests: the old single-week rule methods disappear, payroll stops taking hours as arguments, every test month now needs its previous month approved. A test agent is sent onto the implementer's own branch to update exactly those tests before anything is merged, so master always builds and passes. The test agent may change an expected value only where the refinement labels the change as intended, and it never touches production code. Anything else it must report, not edit.
- **Evidence happens at the gate, not only at the close.** Stage A's deliberate-breakage runs (each one breaks a single behaviour on purpose, on a throwaway branch, to prove a test notices) happen before Stage B starts, because your ruling makes "Stage A closed" a precondition.

*Glossary, one line each.* **PCS** — `PeriodCalculationService`, payroll's calculation core. **Projection** — a table kept up to date from the event log so it can be read quickly; here the two hours tables. **Manifest** — the stored record of how a calculation was split and what it used. **Context days** — the previous month's days that fall in a month's first Monday–Sunday week. **Mutation** — a deliberate one-behaviour breakage applied only in a throwaway copy to prove a test can fail. **Evidence run** — a CI run on a throwaway branch carrying mutations, which must turn exactly a pre-written list of tests red.

---

## Carried warnings from refinement cycle 5 — where each landed

| Item | Disposition | Where |
|---|---|---|
| **W1 / Codex W4 — R23 pre-check edge** | **Ruled** in the Fable seat's draft-1 rulings (the R25–R40 series, § 1 item 1): the skip form is the ruling, **"no quota pre-check for an entitlement type whose signed delta is ≤ 0"**, and the cycle-5 "equivalently" clause is withdrawn. The reason: the rule rejects whenever `remainingAfter < 0`, and with `Used` above the cap and `RequestedDays = 0` that is already negative (`EntitlementValidationRule.cs:69-74`). For delta > 0 the mirror stands: `used = balance.Used − Σ superseded`, `requestedDays = Σ new` (identical to `requestedDays = delta` with `used = balance.Used` when delta > 0). Nothing else is lost by the skip: the quota call at this site carries no age gate or per-episode limit (`SkemaEndpoints.cs:1392-1408`; `perEpisodeLimit` null at `:1402`). The SENIOR_DAY age gate is a separate call (`:1055-1071`) and is untouched. The authoritative in-lock check is unchanged (R23) | TASK-14507 Do 5; pins `UsedAboveCapAfterRevaluation_Reduction_Succeeds` and `NearCap_ResaveIncreaseWithinCap_Returns200` (TASK-14502); mutations MD-06..MD-09, MD-18 |
| **Codex wording — "less than the full day" (`:574`), "anything but the full day" (`:214`)** | Applied: "a full-day-only absence cell re-saved at **any positive number of hours other than the full day** keeps today's 422". Zero and negative hours get the 400 first | TASK-14507 Do 6; pin `Resave_FullDayOnlyAbsence_PositivePartialHours_Keeps422` |
| **W2 — stale "because October is already exported" (`:670`)** | Applied: "the suite is PCS-level; the export route is not involved" | TASK-14515 (`Reconstruction_*` facts) |
| **W3 — host preconditions for the months-in-order facts** | Applied. `/calculate-and-export` for October needs October APPROVED (`Payroll/Program.cs:304-317`, else 403). `/recalculate` for October needs October exported (`:493-505`, else 422 `payroll-not-exported`); the 409 fact exports October first through the raw `/api/payroll/export` (it bypasses approval, `:185-215`) and leaves September unapproved. The EMPLOYEE_APPROVED leg sets September to EMPLOYEE_APPROVED | TASK-14503a/b Facts + fixture rules |
| **N1 — refusal order is a requirement** | Applied as a contract of TASK-14512, with one pin per adjacent pair: planner 422 → week-check 422 → prior-month 409 → rules | TASK-14512 Do 3; pins in TASK-14503b; mutations MD-23, MD-24 |
| **N2 — enumerate the suites R21 touches** | Applied under **R26**. TASK-14512-T lists every suite and classifies each by the R26 fixture rule. A fact about the check, a host fact, `WeeklyPayrollTests` and the smoke test use the real adapter with M−1 seeded APPROVED. Every other PCS-level fact injects a fake that answers APPROVED, declared once in its fixture | TASK-14512-T |
| **N3 — settlement fixture dates; extend the termination fact** | Applied: the SPECIAL_HOLIDAY_ALLOWANCE re-save is dated inside `[1 May Y+1, 30 Apr Y+2]` (`VacationSettlementService.cs:917-924`). `TerminationSettlementTests.Termination_PostEndDateBooking_DoesNotConsume` (`:233`) gains the re-save leg | TASK-14501 Do 4 |
| **N4 — `source TEXT NOT NULL` default** | **Decided: `DEFAULT 'TIME_POST'`, plus a CHECK on the two values** (R28, accepted). Reasons: (1) TIME_POST is the truthful class of every pre-S145 row, since the table was "append-per-event" (`init.sql:1933`). (2) The house idiom for a new column on an existing database is `ADD COLUMN IF NOT EXISTS` (S66 block, `init.sql:3004-3032`), and a NOT NULL column without a default cannot be added to a populated table. (3) 21 test files insert raw rows without the column, and they keep their additive meaning unchanged. (4) The partial unique index then selects no legacy row, so creating it can never fail. The cost: a future Skema writer that forgets to stamp `SKEMA` would silently be additive. That is pinned: `Resave_Entry_*` go red under MD-01 | TASK-14500 Do 1 |

---

## Rulings in force, and where each lands

*Owner (2026-10-02, in session):* **OQ1 = "Keep for S145"** → the verified 51-mutation evidence design stays, lightened by R66a (CA-1 re-derives by effect only rows touched by a reported deviation or whose cited code changed, otherwise two spot checks per run; the run itself is the check of record) → TASK-14518 CA-1, Gate A-CLOSE CA-1. The "Switch now" instructions are not applied.

*Owner (2026-10-01):* **Q1** → wave structure + Gate A-CLOSE · **Q2 (A)** → TASK-14505 (`DayNorm`) · **Q3 (F1)** → TASK-14505 (settlement day) · **Q7 (b)** → TASK-14512 Do 6 · leans **Q5 (c)** → TASK-14512 Do 1 (all four routes) · **Q6 (a)** → TASK-14522 (Stage B).

*Fable seat (refinement rulings R1–R24), one line each:*

| Ruling | In one line | Lands in |
|---|---|---|
| R1 | Stage A leaves the planner's split refusal, the registered classifications and the handed-in rule set behaving as today | TASK-14511 Do 1; O-8 stop-point check |
| R2 | The mid-week agreement refusal and the dated wage-type key land in Stage A | TASK-14512 Do 1, Do 5 |
| R3 | Freeze what is data, fingerprint what is code; plan-level slot on manifest, event and column | TASK-14500 Do 5; TASK-14513 |
| R4 | Q4 ruled; marquee replay pins become pins of the freeze | TASK-14513-T; TASK-14515 |
| R5 | (a) backdates reach the settling month; (b) months in order; (c) reopen flags M+1; (d) Q7 rebuilt; (e)(f) registered | TASK-14509; TASK-14512; QUAL-187/188 |
| R6 | The window contract (which days each figure covers) | TASK-14505 Do 2 |
| R7 | The OK-boundary week is dated 5 April, keyed OK26; no refusal | TASK-14505; TASK-14514 |
| R8 | One arithmetic path; single-week entry points retired; labelled intentional changes | TASK-14505 Do 4, 14505-T |
| R9 | Flex restaged: unit pins in Stage A, PCS two-month fact; credits through the helper | TASK-14505 Do 5; TASK-14506; TASK-14514 |
| R10 | Raw-route refusal pins unchanged in Stage A, changed in Stage B | TASK-14522-T |
| R11 | Owners: Calendar → rule-engine, Events → data-model, `Segmentation/**` per task; ADR-002 note | Ledger labels; O-4 |
| R12 | "Every ordinary month" qualifier | Summary |
| R13 | F2 cost recorded (Q3 provenance) | — |
| R14 | Payroll reads its own hours (current rows); body hours refused 400; event-id binding; `ReplayAsync(manifestId)` | TASK-14508; TASK-14513 |
| R15 | Lossless digest of the rows as read, before normalization | TASK-14513 Do 3 |
| R16 | A raw-exported month has no approval row and does not satisfy months-in-order | TASK-14512 Do 2; HRP row |
| R17 | The Stage A gate is behaviour, not files | O-8 |
| R18 | F1 fixtures with per-day hours | TASK-14514 |
| R19 | Correctness claims qualified by QUAL-187..191 and QUAL-188 | Summary; O-4 |
| R20 | Every reader of the two tables counts current rows only; the census is the file list | TASK-14507 Do 3 |
| R21 | The prior-month check is optional but never silent; fixtures seed the earlier month APPROVED — **amended by R26** (an interface instead of the concrete repository; the fixture rule replaces refinement `:552-556`) | TASK-14512 Do 2; 14512-T |
| R22 | Backfill pinned in `ProjectionBackfillTests`; parity in `ProjectionParityTests` | TASK-14501 |
| R23 | Signed per-type quota delta: pre-check mirror; in-lock guarded for > 0, ungated for ≤ 0 | TASK-14507 Do 5 (as amended by W1) |
| R24 | Clearing a cell out of S145; hours ≤ 0 refused 400; retraction not precluded | TASK-14507 Do 6 |

*Fable seat (plan rulings on draft 1, R25–R40; full text in the sprint log's § "Planning record (Step 0b)", R66b):*

| Ruling | In one line | Lands in |
|---|---|---|
| R25 (P1) | A payroll-local hours seam; every public PCS calculate entry point drops its hours parameters; the never-silent throw comes before any read; `ProjectionHoursReader` is the only Payroll type touching the two repositories | TASK-14508; 14508-T; Step-7a item 8 |
| R26 (N-1) | The prior-month check reads through a payroll-local `IPriorPeriodApprovalReader?` with a production adapter; optional but never silent, exactly as R21; the fixture rule | TASK-14512 Do 2; 14512-T; MD-21 |
| R27 (P2) | Paired test passes. 5α on a paired branch takes the union of both file lists; the paired `test-qa` never edits `src/**`; R35's rounding is labelled `// S145 R9:` | merge checklist; standing constraints; every `-T` row |
| R28 (P3) | `source` gets `DEFAULT 'TIME_POST'` and a CHECK | TASK-14500 Do 1 |
| R29 (P4) | `plan_inputs_jsonb JSONB NULL` | TASK-14500 Do 1 |
| R30 (P5) | Reader-matrix pins; cut first | TASK-14502; cut order |
| R31 (P6) | The four extra legs (a)–(d) | TASK-14503b, 14504, 14514 |
| R32 (P7) | Stage A verified in CI on a throwaway branch before Stage B; `origin/master` never touched | Gate A-CLOSE CA-4 |
| R33 (P8) | Abandoning Stage B is a revert, never a reset; `-m 1` only for an actual merge commit, otherwise plain `git revert <sha>` newest first; the contract is the empty diff | Stage B "if it does not converge"; CA-7 |
| R34 (P9) | Every norm entry point runs the core. The legacy dispatch carries a **forced norm mode** for the three decomposed ids and tags the result with the requested id | TASK-14505 Do 4; the R40 contract; 14505-T |
| R35 (P10) | `AbsenceRule`'s 0-hour default credit goes through `DayNorm` over 37.0; the constant stays; `AbsenceRule.Evaluate` is "not retired" | TASK-14505 Do 4–5 |
| R36 (P11) | The reopen hook does not inherit R5(a)'s widening | TASK-14509 Do 2 |
| R37 (P12) | Q7 (b): optional `DateOnly?` fields on the existing event, no new event type, the mapper by cross-domain label; over-naming M+1 for a leaver is advisory and harmless | TASK-14500 Do 3; TASK-14512 Do 6 |
| R38 (P13) | Fix S144's N-c4-1/2/4 in passing | TASK-14503b Do 3 |
| R39 (P14) | Stage A evidence re-observed at the close where the mutated method's hunk changed after `K_A`; the hunks are frozen at C-1 | C-1, C-5 (renumbered by R-53) |
| R40 (P15) | The contract names for the new Rule Engine surface, plus R34's forced-mode member and the payroll exception names | TASK-14505 contract; TASK-14512 Do 1–2 |

*Fable seat (Step 0b cycle-1 rulings, R41–R51; full text and the mapping in the planning record, R66b):*

| Ruling | In one line | Lands in |
|---|---|---|
| R41 | Freeze `PlanInputs.SettlementCutoff` (the window's inclusive `End` when ≤ `periodEnd`, else null); it is not recoverable from the plan | TASK-14500 Do 5; 14511 Do 2; 14513 Do 1, 3; 14514, 14515 pins; MD-43, MD-44 (+ MD-52) |
| R42 | Admission split from evaluation; replay enters evaluation only and never consults the approval reader, the agreement-code repository, the window resolver or `ReadCurrentAsync` | TASK-14512 Do 2; 14513 Do 3; sentinel pin (14515); `NoApprovalReader_Replay_Succeeds` (14514); MU-38; MD-45 |
| R43 | PCS drops hire-dated agreement dates before the week predicate | TASK-14512 Do 1; 14505 contract; 14503b starter fact; MD-46 |
| R44 | MD-18 → EA-3; mutations exclusive per run when they edit the same code path | RED evidence; TASK-14518 CA-1 |
| R45 | Three missing list entries; resolved `FullyQualifiedName`s published before `K_A` | MD-21, MD-26; CA-1 |
| R46 | "(guard)" tags; MD-47..MD-50; MU-32 and MU-39 by injection; "every non-guard Docker pin" | 14503b, 14504; RED evidence; fact 2 |
| R47 | A rebuild keeps `PlanInputs`: new rebuild file + MD-51 | TASK-14515; 14513 Do 4 |
| R48 | Backfill marks, then inserts — never insert first | TASK-14507 Do 4 |
| R49 | The RED Unit characterization file merges with 14506; 14503 pre-split | TASK-14503a/b; 14506 |
| R50 | No task authors into a file another open-wave task edits; 14521 label; 14524 file names | fact 1; 14514, 14515 files; Stage B table |
| R51 | Harness-DDL-match guard fact | TASK-14501 Do 6 |

*Fable seat (Step 0b cycle-2 rulings, R52–R59; full text and the mapping in the planning record, R66b):*

| Ruling | In one line | Lands in |
|---|---|---|
| R52 | MD-22 leaves EA-3 for EA-1; its list gains the sentinel pin's fresh-calculation half | RED evidence (table and runs) |
| R53 | MD-45 is null-tolerant: replay consults the approval reader **when one is registered** | MD-45 row; TASK-14518 CA-1 step 4 |
| R54 | MD-40 becomes MU-40 (Unit, fact 3); `EmploymentEdges_Starter3Sep_…` is covered by MU-40; R46a reworded | MU table; TASK-14514; fact 2; CA-1 step 2 |
| R55 | MD-42's list grows (the September HK and October real-engine facts, each confirmed by assertion); MD-21 enumerated by effect; MD-21 → EA-4 | MD-21 and MD-42 rows; **placement deviates (D1)**: MD-21 stays EA-2, MD-42 → EA-4 |
| R56 | The sentinel pin's fourth source is a real `UserAgreementCodeRepository` over an unreachable connection; the fresh calculation runs on the non-sentinel PCS | TASK-14515; "Needs a ruling" item 3; contradiction 16 |
| R57 | The termination leg is a Skema save and re-save before the single settle: `used` 3, crystallized 9.5 | TASK-14501 Facts and Do 4 |
| R58 | `AddProjectionHoursReader` lives in `ProjectionHoursReader.cs`; the guard's allowlist stays one file | TASK-14508 Do 1; TASK-14514 guard |
| R59 | A mechanical partition table, one row per MD, with derived check lines per run | RED evidence; TASK-14518 CA-1 step 3; Gate A-CLOSE CA-1 |

*Fable seat (Step 0b cycle-3 rulings, R60–R66; full text and the mapping in the planning record, R66b):*

| Ruling | In one line | Lands in |
|---|---|---|
| R60 | `ReplayFrozenInputsTests` builds both PCS instances with a test-local echo handler that encodes every entry (context days included), each piece's fraction and the cutoff; comparisons are over the full `PeriodCalculationResult`; nine rows re-derived against it | TASK-14515 header; C-R4; rows MD-03, 15, 20, 31, 36, 38, 43, 44, 52 |
| R61 | MD-24 sits in the per-plan lane's admission step, before its call to `BuildPlanForLegacyCallersAsync`; H11 and H5 join its Not list | MD-24 row; CA-1 step 4; "Needs a ruling" item 9; contradiction 18 |
| R62 | Every fixture that Skema-saves an entitlement-typed absence installs the entitlement-validation stub and asserts each save's 200 | TASK-14501 Do 4; TASK-14502 fixture rules |
| R63 | MD-27's five calculating-route reds surface as 500 in EA-2; "relies on" is derived hard (route guards, check (c)) and soft (Not-guards, check (c′)) | MD-27 and MD-21 rows; path-key legend; Not-guards table; check lines; CA-1 step 3; Gate CA-1 |
| R64 | The forskud branch is cited for TRM's headroom; the SHO employee has no end date before the cell | TASK-14501 Do 4; C-A2 |
| R65 | MD-39 and MD-42 patch one method (CA-3; R39 re-observes both); three citation nits | TASK-14518 CA-3; TASK-14503a/b Facts; TASK-14507 Facts; TASK-14511-T; MD-17 row |
| R66 | (a) CA-1 re-derives by effect only rows a reported deviation or changed code can have moved, otherwise two spot checks per run; the run is the check of record. (b) The plan is slimmed by move: briefs to `s145/TASK-*.md`, the record to `SPRINT-145.md` | CA-1 step 3; Gate CA-1; RED evidence; this file's layout |

*Standing:* reseed, don't migrate (no backfill, no pre-S145 manifest) · cap 5 per lens · the seat never switches · no `git stash` · worktree freshness (`docs/AGENTS.md:135-160`).

---

## How the waves are built — read before the ledger

1. **Pins define done, but master must compile** (`SPRINT-141.md:325-329`, the S141 convention).
   - Pins that speak only HTTP, SQL or existing members compile today. They land first, in wave A.1a: TASK-14501, 14502, 14503a, 14503b and 14504.
   - **Exception (R49):** a compile-today *Unit* pin that is RED against master (`MonthAsWeekCharacterizationTests`) cannot merge under O-1. It is authored and observed RED on 14503a's branch, then merged green with TASK-14506 once the core exists.
   - **No task authors into a file another open-wave task edits (R50).** New pins go into new files, or into an existing file only after its other editors have merged.
   - Pins that name a type or member S145 creates are authored from the spec in their own worktree and merged at a later gate, once the implementation is on master: TASK-14506, 14514, 14515.
   - Existing tests that an implementation breaks on purpose are migrated by paired passes (R27).
2. **RED evidence has two mechanics, and "revert after merge" is neither** (`docs/AGENTS.md:162-180`).
   - A compile-today pin proves RED by running before the implementation exists. A Unit one is observed locally. A Docker one is argued in its docstring under "Red conditions", and its named mutation is observed in an evidence run.
   - A new-API pin proves RED by a named mutation at its gate. A Unit mutation is run locally by a re-spawned `test-qa` in a detached worktree. A Docker mutation is observed in an evidence run.
   - **Docker does not run locally.** Every **non-guard** Docker pin has a mutation (R46a, as worded by R54). That is either a Docker one on a frozen expected-red list for some evidence run, or a Unit one under fact 3 with a recorded ruling (MU-40, for `EmploymentEdges_Starter3Sep_…`). A pin tagged "(guard)" passes today and is exempt.
3. **CI stops a job at its first red step.** `build-and-test` runs Unit (`ci.yml:165-166`), then DemoSeed (`:176`), then Regression (`:187-188`). So a mutation that turns a Unit test red means the Regression step never runs.
   - Every evidence commit keeps Unit and DemoSeed green.
   - A Docker mutation that cannot avoid a Unit red is observed at Unit level instead, and reported at CA-1 for a Fable ruling.
   - Regression non-Docker reds run in the same step, so they are allowed only if listed.
4. **The payroll core is changed by four tasks in a row**, each branched after the previous one merged: TASK-14508 → 14511 → 14512 → 14513. All four edit `PeriodCalculationService.cs`; two worktrees on that 2097-line file would be the S142 merge hazard. Everything else is arranged around that chain.
5. **The Skema fix precedes the payroll read.** The ruling says payroll must never read rows the approval check counted differently (refinement `:222`). So TASK-14507 (A0) merges at the A.1 gate, before TASK-14508 is dispatched.

| Wave | Tasks (parallel within a wave, `isolation: "worktree"`, each from **local** master) | Gate before the next wave |
|---|---|---|
| **G-0** | — | CI on `62fcdc3` green; recorded |
| **A.1a** | 14500 schema + shapes (data-model) · 14501 harness DDL + A0 infra pins (test-qa) · 14502 Skema A0 pins (test-qa) · 14503a real-engine payroll pins + the RED characterization file (test-qa) · 14503b stub-engine payroll pins (test-qa) · 14504 A6 backend pins (test-qa) · 14505 week-by-week Rule Engine (rule-engine) → 14505-T · 14506 new-API pins, authored only (test-qa) | each merges through the merge checklist as it lands; **14507 is dispatched the moment 14500 merges** |
| **A.1b** | 14507 Skema supersession (backend-infrastructure) → 14507-T if needed | **A.1 gate:** 14500, 14501–14504, 14505+T, 14506 (rebased, merged), 14507+T all merged · O-1 · O-2 check · O-3 · 14506 merged **with** 14503a's characterization file (R49) · **14506 re-spawn 2: Unit mutation run** (MU-01..MU-24, MU-37, MU-41, MU-42) · handoff |
| **A.2** | 14508 hours read + host contract (payroll-integration) → 14508-T · 14509 A6 + rebuilder + `DailyNormCalculator` (backend-infrastructure) → 14509-T (added at 14504's return) · 14510 trigger label (ux) · 14516 negative-hours guard in the grid (ux; added at 14507's return) · 14514 and 14515 authored from the spec (test-qa) | **A.2 gate:** 14508+T, 14509+T, 14510, 14516 merged · O-1 incl. `tsc --noEmit` + vitest · O-2 check |
| **A.3** | 14511 per-plan lane (payroll-integration) → 14511-T | **A.3 gate:** merged · O-1 |
| **A.4** | 14512 calculability checks + dated key + Q7 (payroll-integration) → 14512-T | **A.4 gate:** 14512+T merged · 14514 rebased, merged · **14514 re-spawn 2: Unit mutations** (MU-25..MU-32, MU-36, MU-38, MU-39, MU-40, MU-43, MU-44, MU-45) · O-1 |
| **A.5** | 14513 replay contract (payroll-integration) → 14513-T; then 14519 the local-profile activation key (payroll-integration, added 2026-10-15, R-20) → 14519-T | **A.5 gate:** 14513+T merged · 14515 rebased, merged · **14515 re-spawn 2** (MU-33..MU-35) · 14519+T merged · O-1 · O-8 stop-point check |
| **A-CLOSE** | 14518 Stage A evidence (test-qa) | **Gate A-CLOSE** (CA-1..CA-7). No Stage B brief before every box is ticked |
| **B.0** | `planner` revises the Stage B briefs against `K_A`; Fable seat reviews; scoped Step-0b cycle if any criterion or file list changes beyond line numbers | Fable seat signs the B briefs |
| **B.1** | 14520 reclassify (rule-engine) · 14521 worklist derivation (backend-infrastructure) · 14524 Stage B pins, authored (test-qa) | merged · O-1 · O-2 check |
| **B.2** | 14522 lift the refusal + Q6 (a) (payroll-integration) → 14522-T | merged · O-1 |
| **B.3** | 14523 residual badge (ux) · 14524 rebased, merged · 14524 re-spawn 2 (Unit mutations) | **C-1**: lists, **patches and `B`** frozen (R-53, R-83) |
| **close** | Close sequence **C-2..C-9** | sprint-close guard at C-4 (the close commit, R-53); C-9 seals |

**The build-safe Stage B order (accepted with two amendments, R-78; 8 GB machine, at most one building agent at a time):**
- **B.0:** the seat signs (after the scoped Step-0b cycles); the Orchestrator registers **QUAL-209, QUAL-210 and QUAL-211** in one docs-only commit before TASK-14521 or TASK-14524 is dispatched.
- **B.1:** (1) 14524 authoring: one Unit build to observe the compile-today reds, **the QUAL-210 drift pair included** (R-78), then the new-API files without building; (2) 14520, then 14520-T, then the Orchestrator's branch build; (3) 14521, then 14521-T, then the branch build; (4) the merges, then O-1 and O-2.
- **B.2:** 14522, then 14522-T, then the build, then the merge and O-1.
- **B.3:** (1) 14523, frontend only, never alongside a `dotnet` build; (2) 14524 re-spawn 1, then the merge; (3) re-spawn 2, the Unit mutations, one at a time; (4) C-1, with sequential dry runs, **which produce the frozen patches of R-53** (`cb1/mut/*.diff`).

---

## The merge checklist — every task, every fix-up, every paired pass (designs out the S144 miss)

*Plain language:* S144 merged seven tasks on a green build alone, and ran the mechanical check and the review only when preparing the close (`SPRINT-144.md:290-292`). Here those two checks sit **before** each merge, and the record of them is a column that the gates read.

For each branch, in this order:
1. **Report read.** Brief contradictions and declared deviations are dispositioned. Factual errors are settled by the Orchestrator. Requirement, invariant or ruling questions go to the `adjudicator`.
2. **Build.** `dotnet build` 0 errors; warnings ≤ 145; CA2100 ≤ 115.
3. **Step 5α — `constraint-validator`** on the branch diff (`git diff <base>...<branch>`), with the task's authorized file list as its scope. **On a paired branch the scope is the union of the implementer's and the paired pass's file lists** (R27), and any `src/**` change in the paired pass's own commits is a violation. Result: PASS, or violations fixed and re-run.
4. **Step 5a — `reviewer` (Fable)** on the branch diff with the pasted brief. Each task gets its own verdict line, even when one spawn reviews several tasks of a gate. Every BLOCKER and WARNING is dispositioned: a fix-up is dispatched (and goes through this checklist itself), or an adjudicator ruling is recorded.
5. **Step 5a — Codex**, for the rows the 5α/5a table marks high-risk (schema, payroll export, legal rule logic, entitlement quotas, retroactive corrections; `docs/AGENTS.md:585-595`). It runs on the branch diff, inlined if the sandbox blocks; if so, the verdict says "performed on inlined source" (`docs/AGENTS.md:558-566`). Findings are dispositioned.
6. **Merge**, then O-1 on master. The sprint-log task row and the handoff block row are written: `task · branch · build · 5α · 5a-R · 5a-C · merged sha`.

**A row with an empty 5α or 5a cell cannot be merged.** Every wave gate checks the column, Gate A-CLOSE checks it again (CA-2), and so does C-2. The cycle cap is 5 per lens per task (`docs/WORKFLOW.md:34`). For a paired pass, steps 2–5 run once, on the combined branch, after both spawns return.

---

## Standing constraints — pasted verbatim into every agent prompt

```
★ FIRST, BEFORE ANY WORK: check your worktree against master.
Run `git log --oneline -1` and compare with the main checkout's LOCAL master — not `origin/master`,
which lags by design because this project pushes only at sprint close. Fast-forward if you are
behind, and say in your report what you found.
```
```
Do NOT use `git stash` — it is repository-global, not per-worktree, and will collide with
other agents running concurrently.

RED EVIDENCE (docs/AGENTS.md § "RED evidence"):
 (a) A pin that compiles against TODAY's code proves RED by running before the implementation
     exists. If it is Docker-gated, argue the RED in its docstring under "Red conditions": name
     the production mutation (its MD-id) and the assertion it trips.
 (b) A pin that names a type or member S145 CREATES proves RED by MUTATION at its gate, never by
     reverting: in an isolated `git worktree add --detach <path outside the repo> master`, apply
     ONE named mutation that keeps the API and breaks one behaviour, run the pin, capture the
     assertion failure, `git -C <path> checkout -- .`, repeat, then
     `git worktree remove --force <path>` and `git worktree prune`. A compile error never counts.
     The mutated production file is edited only in that throwaway checkout and never committed.
 (c) Docker mutations are observed only in the evidence runs (Stage A at Gate A-CLOSE, Stage B at
     the close), prepared only by TASK-14518 / TASK-14528 on throwaway branches.
TEST NAMES ARE CONTRACT: the evidence lists name facts in advance. Use the names given; if you
must rename, report old → new.
```
- `docs/CONVENTIONS.md` verbatim (the Orchestrator pastes it).
- **Docker is unavailable locally.** Report Docker facts as "CI-verified, not locally green". Never block on them.
- **Read exit status from the unpiped command** (S140).
- **Never edit a generated file by hand:** `docs/api/openapi.json`, `frontend/src/lib/api-types.ts`, `docs/generated/db-schema.md`. Ask the Orchestrator to regenerate.
- **Report anything this brief does not name**: an enumeration site, a caller, a test, a harness copy.
- **Contract vs. hint.** A "*Hint (HOW)*" is one way; the contract is the WHAT. Deviate from a hint freely and say so. Deviate from a contract only by reporting.
- **Brief challenge:** the BRIEF CHALLENGE block of your definition applies. A contradiction is a report, never a permission.
- **Paired passes only (R27):** you are on another agent's branch.
  - Edit only the files listed, and **never any file under `src/**`**. A production defect you find is reported, not fixed.
  - Change an expected value only where the brief lists the change as intentional, and label each such change with its ruling: `// S145 R8:` (the four labelled R8 changes), `// S145 R9:` (the R35 default-credit rounding), `// S145 R14:` (hours no longer passed as arguments, call counts), `// S145 R26:` (the prior-month fixture rule), or, in Stage B, `// S145 B1:` (TASK-14520-T), `// S145 B2:` (TASK-14522-T) or `// S145 B3:` (TASK-14521-T). B1, B2 and B3 name the refinement's Stage B sections, not the waves B.1, B.2 and B.3 (R-65).
  - Any other value that changes is a **STOP and report**: a requirement question, never an edit to match the code.

---

## Stage A — task ledger

*Each task's brief is its own file under `.claude/plans/s145/` (R66b), linked from the Task column. Paste it whole, with the standing constraints above and the refinement lines it names. TASK-14507-T, which is conditional, has no brief of its own: the Orchestrator lists its files in the spawn.*

| Task | Wave | Role (model by definition) | What | Authorized files (exactly these) | Depends on | Size |
|---|---|---|---|---|---|---|
| **[TASK-14500](s145/TASK-14500.md)** | A.1a | `data-model` (Sonnet) — **`Data Model (extended into SharedKernel/Segmentation, cross-domain authorized)`**; `init.sql` edit **Orchestrator-approved here** (S144 TASK-14400 precedent) | Two hours tables gain `source` + `superseded_by_outbox_id` + CHECK + two partial unique indexes; `segment_manifests.plan_inputs_jsonb`; S145 segment; `init.sql:1933` comment; event `Source` ×2; `SegmentManifestCreated.PlanInputs`; `RetroactiveCorrectionRequested` next-period fields (R37); row fields; `PlanInputs` record; optional `SegmentManifest.PlanInputs` | `docker/postgres/init.sql`; `src/SharedKernel/StatsTid.SharedKernel/Events/TimeEntryRegistered.cs`, `…/Events/AbsenceRegistered.cs`, `…/Events/SegmentManifestCreated.cs`, `…/Events/RetroactiveCorrectionRequested.cs`; `…/Models/TimeEntryProjectionRow.cs`, `…/Models/AbsenceProjectionRow.cs`, new `…/Models/HoursSource.cs`; `…/Segmentation/SegmentManifest.cs`, new `…/Segmentation/PlanInputs.cs` | G-0 (merge only) | M |
| **[TASK-14501](s145/TASK-14501.md)** | A.1a | `test-qa` (Sonnet) | Harness DDL copies follow the schema; the false "byte-for-byte" comment fixed; A0 backfill, parity and settlement pins (compile today; Docker) | `tests/StatsTid.Tests.Regression/Outbox/ProjectionSchemaTestFixture.cs`, `…/Infrastructure/TxContractTests.cs` (DDL block `:283-320` only), `…/Segmentation/TestFixtures.cs` (`SchemaDdl` + `:399-409` comment only), `…/Outbox/ProjectionBackfillTests.cs`, `…/Outbox/ProjectionParityTests.cs`, `…/Settlement/TerminationSettlementTests.cs`, `…/Settlement/SpecialHolidaySettlementTests.cs`, new `…/Migrations/HarnessDdlMatchesInitSqlTests.cs` (R51) | — | M |
| **[TASK-14502](s145/TASK-14502.md)** | A.1a | `test-qa` | Skema A0 endpoint pins: re-save surfaces, retry, quota ×7, ≤ 0, full-day-only, additive guard, reader matrix (R30) — compile today; Docker | new `tests/StatsTid.Tests.Regression/Skema/SkemaCellSupersessionTests.cs`, new `…/Skema/SkemaQuotaResaveTests.cs` | — | M–L |
| **[TASK-14503a](s145/TASK-14503a.md)** | A.1a | `test-qa` | Real-engine half (R49): `PayrollHostFactory` real-engine option; the three `_RealEngine` host facts (A1 defect ×2, R14 HTTP). Also **authors and observes RED** the Unit characterization file, which is **excluded from this merge** and moves to TASK-14506 | `tests/StatsTid.Tests.Regression/Payroll/PayrollHostFactory.cs`; new `…/Payroll/PayrollHostRealEngineTests.cs`; authored here, merged by 14506: new `tests/StatsTid.Tests.Unit/Rules/MonthAsWeekCharacterizationTests.cs`. **The branch is kept until TASK-14506 re-spawn 1 has taken that file** (N5): the house merge is squash or file-copy (`docs/AGENTS.md:583`), after which branches are commonly pruned | — | M |
| **[TASK-14503b](s145/TASK-14503b.md)** | A.1a | `test-qa` | Stub-engine half (R49): 400s, 409s, no-prior-approval guards, refusal order, raw-route guard, Q7 (b) + negatives; agreement residual on four routes + guards; R38 wording | new `tests/StatsTid.Tests.Regression/Payroll/PayrollHostWeeklyTests.cs`; `…/Payroll/PayrollHostRecalcBlockedTests.cs` (new facts at the end + R38 comment/assertion wording only) | — | M |
| **[TASK-14504](s145/TASK-14504.md)** | A.1a | `test-qa` | A6 Backend pins: backdate reaches the settling month; reopen flags the next month (+R31c legs) — compile today; Docker | `tests/StatsTid.Tests.Regression/Worklist/HrBackdateWorklistRepositoryTests.cs` (new facts at the end), new `…/Approval/ReopenFlagsNextMonthTests.cs` | — | S |
| **[TASK-14505](s145/TASK-14505.md)** | A.1a | `rule-engine` (Opus) — **`Rule Engine (extended into SharedKernel/Models, cross-domain authorized)`**, one file | Week-by-week core; `POST /api/rules/evaluate-timeline` with config fingerprint + model marker; legacy dispatch on the core (R8, R34); single-week entry points retired; `AbsenceRule` default credit on the helper (R35); `DayNorm` + `WeeklyContext` in Calendar | `src/SharedKernel/StatsTid.SharedKernel/Calendar/DayNorm.cs` (new), `…/Calendar/WeeklyContext.cs` (new); `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs` (new), `…/Rules/ConfigFingerprint.cs` (new), `…/Rules/OvertimeRule.cs`, `…/Rules/NormCheckRule.cs`, `…/Rules/FlexBalanceRule.cs`, `…/Rules/AbsenceRule.cs`, `…/Rules/RuleRegistry.cs`, `…/Program.cs`, `…/Contracts/EvaluateTimelineRequest.cs` (new); **cross-domain:** new `src/SharedKernel/StatsTid.SharedKernel/Models/TimelineEvaluationResponse.cs` | — | **L** (largest Rule Engine pass; the A.1 gate requires all of it merged, incl. 14505b if the cut line is used) |
| **[TASK-14505-T](s145/TASK-14505-T.md)** | A.1a (paired) | `test-qa` on 14505's branch | Migrate the call sites of the retired members (listed in § 14505-T) to the core with one-piece timelines (forced mode per R34); `AbsenceRuleTests` for R35 rounding only; labelled intentional changes only | `tests/StatsTid.Tests.Unit/OvertimeRuleTests.cs`, `…/NormCheckRuleTests.cs`, `…/FlexBalanceRuleTests.cs`, `…/AbsenceRuleTests.cs` (R35 only), `…/Sprint11Tests.cs`, `…/Validation/NormPeriodWeeksConstantTests.cs`, `…/Rules/RuleRegistryDispatchTests.cs` (check only); `tests/StatsTid.Tests.Regression/RegressionTests.cs`, `…/OkVersionRuntimeRegressionTests.cs` | 14505 returned | L |
| **[TASK-14506](s145/TASK-14506.md)** | authored A.1a; re-spawn 1 (rebase, reconcile) and re-spawn 2 (Unit mutations) at the A.1 gate | `test-qa` | New-API Unit pins for the core, the helpers, the fingerprint, the legacy route and the shapes' round-trips; the Orchestrator-pipeline fact | new `tests/StatsTid.Tests.Unit/Rules/WeeklyTimelineCoreTests.cs`, new `…/Rules/ConfigFingerprintTests.cs`, new `…/Calendar/DayNormTests.cs`, new `…/Calendar/WeeklyContextTests.cs`, new `…/Rules/LegacyDispatchOnCoreTests.cs`, new `…/Orchestrator/WeeklyPipelineOnCoreTests.cs`, new `…/Serialization/S145ShapeRoundTripTests.cs`; `…/Rules/MonthAsWeekCharacterizationTests.cs` (taken unchanged from 14503a's branch at re-spawn 1, R49); re-spawn 2 only: a detached throwaway worktree (production files mutated there, never committed) | spec; merges after 14500 + 14505 | L |
| **[TASK-14507](s145/TASK-14507.md)** | A.1b | `backend-infrastructure` (Opus) — **`Backend + Infrastructure (cross-domain authorized)`** | A0: a Skema save replaces the cell; every census reader counts current rows; signed quota delta (W1 form); ≤ 0 refused; backfill supersession; `GetByEventIdsAsync` | `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`, `…/Endpoints/ApprovalEndpoints.cs` (the five raw-SQL reads only); `src/Infrastructure/StatsTid.Infrastructure/TimeEntryProjectionRepository.cs`, `…/AbsenceProjectionRepository.cs`, `…/VacationSettlementService.cs` (`:1914-1922` only), `…/EmploymentWindowStrandCheck.cs` (`:64-70` only), `…/ProjectionBackfillService.cs`, `…/EntitlementBalanceRepository.cs` (a `(conn, tx)` ungated adjust, if needed) | 14500 merged | L |
| **TASK-14507-T** | A.1b (paired, conditional) | `test-qa` on 14507's branch | Only if an existing fact pinned the QUAL-192 behaviour (a re-saved cell counted twice); re-expected under the ruling | the files 14507 reports (the Orchestrator lists them in the spawn) | 14507 returned | S |
| **[TASK-14508](s145/TASK-14508.md)** | A.2 | `payroll-integration` (Opus) | R14: `IRecordedHoursReader` + `ProjectionHoursReader`; host DI for the two repositories + reader; PCS calculate entry points drop hours; read `[opening Monday − 1, periodEnd]`; body hours → 400 on both routes; `RetroactiveCorrectionService` signature | `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs`, `…/Services/RetroactiveCorrectionService.cs`, `…/Program.cs`, new `…/Services/IRecordedHoursReader.cs`, new `…/Services/ProjectionHoursReader.cs` | A.1 gate | L |
| **[TASK-14508-T](s145/TASK-14508-T.md)** | A.2 (paired) | `test-qa` on 14508's branch | The PCS and correction-service call sites listed in § 14508-T migrated; host-test bodies carrying hours; the smoke test's body | the files listed in § 14508-T; `tests/StatsTid.Tests.Unit/Payroll/EmploymentWindowPcsFixture.cs`; `tests/StatsTid.Tests.Regression/Segmentation/TestFixtures.cs` (`BuildPcs` only); `tests/StatsTid.Tests.Regression/Payroll/PayrollHostRecalcBlockedTests.cs` (bodies only); `tests/StatsTid.Tests.Smoke/PayrollCalcAuditSmokeTests.cs` (body only) | 14508 returned | L |
| **[TASK-14509](s145/TASK-14509.md)** | A.2 | `backend-infrastructure` (Opus) — **`Backend + Infrastructure (cross-domain authorized)`** | R5(a) settling-month reach; R5(c) reopen hook + `PRIOR_MONTH_REOPENED` (R36); rebuilder carries `plan_inputs_jsonb`; `DailyNormCalculator` on `DayNorm` | `src/Infrastructure/StatsTid.Infrastructure/HrBackdateWorklistRepository.cs`, `…/SegmentManifestProjectionRebuilder.cs`; `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` (reopen handler only), `…/Services/DailyNormCalculator.cs`; `docker/postgres/scripts/rebuild_segment_manifests.sql` (added at Step 5a of 14500: the ops twin of the rebuilder); `…/Endpoints/BalanceEndpoints.cs` (the year-overview header's weekly norm `:795-796` only; added at Step 5a of 14505, N4); `src/SharedKernel/StatsTid.SharedKernel/Events/BackdateWorklistRowCreated.cs` (the `TriggerEffectiveFrom` docstring only; added at the batched 5a of 14501–14504) | A.1 gate | M |
| **[TASK-14509-T](s145/TASK-14509-T.md)** | A.2 (paired; added at 14504's return) | `test-qa` on 14509's branch | Re-expect W1-EX under R5(a) (labelled `// S145 R5(a):`); add the guard W1-G (off-by-one: widening from `hi` instead of `hi − 1`) | `tests/StatsTid.Tests.Regression/Worklist/HrBackdateWorklistRepositoryTests.cs` | 14509 returned | S |
| **[TASK-14510](s145/TASK-14510.md)** | A.2 | `ux` (Sonnet) | Label for `PRIOR_MONTH_REOPENED` | `frontend/src/pages/admin/opfoelgning/WorklistList.tsx` (`triggerKindLabel` only), `…/__tests__/WorklistList.test.tsx` | spec | S |
| **[TASK-14516](s145/TASK-14516.md)** | A.2 (added at 14507's return) | `ux` (Sonnet) | The Skema grid refuses a negative hours value at entry (no propagation, inline flag); R24's 400 is then reachable only by API misuse | `frontend/src/components/SkemaGrid.tsx` (cell input and blur paths only), its test under `frontend/src/components/__tests__/`; `frontend/src/components/SkemaDayPanel.tsx` only if its census finds a Skema-cell hours input | 14507 merged | S |
| **[TASK-14511](s145/TASK-14511.md)** | A.3 | `payroll-integration` | The per-plan lane: histories over the evaluated days (agreement read widened); timeline pieces; one timeline call; results joined; per-segment flex carry retired; attempt budget adjusted | `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs` | 14508 merged | **L** (the QUAL-186 fix itself) |
| **[TASK-14511-T](s145/TASK-14511-T.md)** | A.3 (paired) | `test-qa` on 14511's branch | Stubs and fakes learn the timeline endpoint; request-shape assertions rewritten; labelled figure changes | `tests/StatsTid.Tests.Regression/Segmentation/TestFixtures.cs` (`DefaultRuleEngineHandler` only), `tests/StatsTid.Tests.Unit/Payroll/EmploymentWindowPcsFixture.cs`, `…/Unit/Payroll/EmploymentWindowSegmentSkipTests.cs`, `…/Unit/Payroll/EmploymentWindowAlignedWindowMergeTests.cs`, `…/Unit/Payroll/EmploymentRecordGapDiagnosticsTests.cs`, `tests/StatsTid.Tests.Regression/Payroll/EmploymentWindowLiveRulesetTests.cs`, `…/Payroll/EmploymentWindowPayrollTests.cs`, `…/Payroll/EmployeeProfileMarqueeTests.cs` (stub only), `…/Payroll/AgreementCodeMarqueeTests.cs` (stub only) | 14511 returned | L |
| **[TASK-14512](s145/TASK-14512.md)** | A.4 | `payroll-integration` — **`Payroll Integration (extended into SharedKernel/Segmentation and Infrastructure/AuditMappers, cross-domain authorized)`** | Week check on all four routes + 422 variant; prior-month 409 through `IPriorPeriodApprovalReader` (R26), never silent; refusal order (N1); dated key (A4); Q7 (b) | `…/Payroll/Services/PeriodCalculationService.cs`, `…/Services/PayrollPlanRefusalProblem.cs`, `…/Services/RetroactiveCorrectionService.cs`, `…/Program.cs`, `…/Services/PayrollExportService.cs` (Do 7 only; added at 14509's Step 5a, the race closure), new `…/Services/PeriodNotCalculableException.cs`, new `…/Services/PriorMonthNotApprovedException.cs`, new `…/Services/IPriorPeriodApprovalReader.cs`, new `…/Services/ApprovalPeriodPriorPeriodReader.cs`; **cross-domain:** `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PeriodPlanner.cs`, `…/Segmentation/IPlannerEnrollment.cs`, `src/Infrastructure/StatsTid.Infrastructure/AuditMappers/RetroactiveCorrectionRequestedAuditMapper.cs` | 14511 merged | L |
| **[TASK-14512-T](s145/TASK-14512-T.md)** | A.4 (paired) | `test-qa` on 14512's branch | The R26 fixture rule (N2): fake injection (answers APPROVED) in every PCS construction helper; real adapter + M−1 seeded APPROVED in the named suites | the files of § 14512-T | 14512 returned | M |
| **[TASK-14513](s145/TASK-14513.md)** | A.5 | `payroll-integration` | Replay contract: `PlanInputs` populated and persisted (event, projection, load); lossless digests; fingerprint + marker recorded; `ReplayAsync(manifestId)` merged; verify-and-refuse; frozen pieces in both lanes | `…/Payroll/Services/PeriodCalculationService.cs`, new `…/Services/HoursDigest.cs`; `…/Services/RetroactiveCorrectionService.cs` and new `…/Services/ReplayRefusedException.cs` (added at the return, R-11 and R-14(ii), 2026-10-15) | 14512 merged; 14509 merged (rebuilder) | L |
| **[TASK-14513-T](s145/TASK-14513-T.md)** | A.5 (paired) | `test-qa` on 14513's branch | 10 `.ReplayAsync(` call sites + the cref; the marquee stub reads the fraction from the request's pieces | `tests/StatsTid.Tests.Regression/Segmentation/ReplayDeterminismTests.cs`, `…/Segmentation/ManifestReplayTests.cs`, `…/Segmentation/BoundaryCauseEncodingTests.cs`, `…/Payroll/EmployeeProfileMarqueeTests.cs`, `…/Payroll/AgreementCodeMarqueeTests.cs`; added at 14513's return (2026-10-15): `tests/StatsTid.Tests.Unit/Payroll/PcsNeverSilentTests.cs` (the call site only), new `tests/StatsTid.Tests.Regression/Payroll/RetroactiveCorrectionTrailTests.cs` (R-11), `tests/StatsTid.Tests.Unit/Payroll/EmploymentWindowSegmentSkipTests.cs` (R-12 facts only), new `tests/StatsTid.Tests.Unit/Payroll/HoursDigestTests.cs` (R-13) | 14513 returned | M |
| **[TASK-14519](s145/TASK-14519.md)** | A.5 (added 2026-10-15, R-20) | `payroll-integration` | The local-profile activation lookup keyed per (agreement, OK-version) interval on the dated record, never the request body (QUAL-203) | `…/Payroll/Services/PeriodCalculationService.cs` | 14513 merged | S–M |
| **[TASK-14519-T](s145/TASK-14519-T.md)** | A.5 (paired) | `test-qa` on 14519's branch | Three Docker pins: body vs record, the mirror, the straddle (R-20) | new `tests/StatsTid.Tests.Regression/Payroll/LocalProfileActivationKeyTests.cs` | 14519 returned | S |
| **[TASK-14514](s145/TASK-14514.md)** | authored A.2; merged at the A.4 gate | `test-qa` | New-API calculation pins: F1, no gap, the leaver cutoff (R41), edges, OK boundary, opening week, window, flex, dated key, refusal variant, never-silent incl. replay (R42), screen parity, R31d and N3 guards | new `tests/StatsTid.Tests.Regression/Payroll/WeeklyPayrollTests.cs`; new `…/Payroll/EmploymentEdgesTimelineTests.cs` (R50; `EmploymentWindowLiveRulesetTests.cs` only at re-spawn 1, after 14511-T merged, and only if its fixture is file-private); new `tests/StatsTid.Tests.Unit/Segmentation/DatedWtmKeyPlannerTests.cs`; `…/Unit/Payroll/PayrollPlanRefusalProblemTests.cs` (new facts); new `…/Unit/Payroll/PcsNeverSilentTests.cs`; new `…/Unit/ScreenParityTests.cs`; `…/Unit/ArchitectureConstraints/PlannerBypassGuardTests.cs` (R31d); new `…/Unit/ArchitectureConstraints/HoursRepositoryAccessGuardTests.cs` (N3) | spec; 14512 merged | L |
| **[TASK-14515](s145/TASK-14515.md)** | authored A.2; merged at the A.5 gate | `test-qa` | New-API replay pins: byte-identical replays, every named refusal, reconstruction (two facts), the leaver-cutoff replay (R41), the sentinel replay (R42), the rebuild round trip (R47), lossless digest, forward-calc as-of pin | new `tests/StatsTid.Tests.Regression/Segmentation/ReplayFrozenInputsTests.cs` (R50), new `…/Segmentation/ManifestProjectionRebuildPlanInputsTests.cs` (R47), new `…/Payroll/EmployeeProfileForwardCalcAsOfTests.cs` (R50), new `tests/StatsTid.Tests.Unit/Payroll/HoursDigestTests.cs` | spec; 14513 merged | M |
| **[TASK-14518](s145/TASK-14518.md)** | A-CLOSE | `test-qa` (re-spawnable) | CA-1: resolve the four Stage A expected lists to `FullyQualifiedName`s and verify disjointness; CA-3: prepare EA-1..EA-4 from `K_A` | no repository file; throwaway branch `s145-evidence-a` only (production files mutated there, committed to that branch only) | A.5 gate | M |

*Orchestrator rows (no agent):* **O-1** gate check (build, warnings ≤ 145, CA2100 ≤ 115, Unit, DemoSeed, Regression `Category!=Docker`; from A.2, `npx tsc --noEmit` + vitest). **O-2** contract regeneration check after 14507, 14509, 14521 (`dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`; `npm run gen:api`; expect no drift; commit if any). **O-3** `docs/generated/db-schema.md` from `tools/generate_db_schema.py` after 14500 (CI `--check` arbitrates). **O-4** records (§ "Records owed"). **O-5** Step 7a. **O-6** CI watch and evidence runs. **O-7** routing-register row. **O-8** the stop-point check at the A.5 gate. **O-10** post-A.1 test tidy (one `test-qa` spawn, beside 14514 authoring; tests only, no value changes): consolidate the four copies of the entitlement-validation stub into `TestSupport`; pass the OK version into `PayrollHostRecalcBlockedTests.SeedMonthApprovalAsync` (approval rows labelled OK24 under an OK26 profile); one weekday-premise assert for the new AR facts; the two stale "compiles against today's code" sentences (`ProjectionBackfillTests.cs:282`, `HarnessDdlMatchesInitSqlTests.cs:46`); RE3's docstring on the 26 Oct–1 Nov week (it belongs to November under F1). From the batched Fable 5a of 14501–14504. Added at Step 5a cycle 3 of 14506: `Assert.Empty(result.Overtime.LineItems)` in `Core_Flex_MidMonthFractionChange_ZeroHourCredits_UseEachDaysFraction_1340`, so its docstring's "no overtime arises" is executable (no value changes). Added at 14508's 5α: `MonthEndCrossingTests.cs:39` (and its class docstring) still says the pin is "RED by design until the guard is retired". Since `37ab341` it is green; reword to "observed RED against the guard (`0c36ed6`), green since its retirement" (comment only). **O-9** the stale demo-loader comment `tools/StatsTid.DemoSeed/Loading/ApiClient.cs:70-76` (a Small Tasks Exception, after 14507). Widened at 14507's return to the other comments its change made stale, all comments only: `tools/StatsTid.DemoSeed/Loading/DemoLoader.cs:558-565`; `tests/StatsTid.Tests.Smoke/SmokeTests.cs:221-235`; `src/Infrastructure/StatsTid.Infrastructure/ProjectionBackfillService.cs:163-165` ("absences ride time/skema streams"; both writers use `employee-{id}`). The 14507 fix-up corrects the same claim at `:188-194`; O-9 covers what remains. Also, per the adjudicator at Step 5a of 14507: `tools/ProjectionBackfill/Program.cs:82-83` prints the new `SupersededTime` / `SupersededAbsences` counters (two lines).

---

## RED evidence — the catalogue, the partition table (R59) and the runs

> **Frozen at Gate A-CLOSE (R-33..R-46, 2026-10-15): the lists of record are in `SPRINT-145.md` § "Gate A-CLOSE" → "The frozen lists"; the table below is the planning record.** Substantive changes made here at the freeze: MD-29 withdrawn (R-34, now MU-46); MD-24's wording (R-35); C-R3 (R-33); W1-G in the guard sentence (R-42); the runs table gains EA-5 and MD-56 in EA-3 (R-38, R-40). The new mutations MD-53, MD-55..MD-61 and the by-effect list additions (R-36..R-39) are recorded only in the sprint log. Line numbers in this section are stale since Stage A's merges; CA-1's re-found sites (`.claude/plans/s145/ca1/`) govern (R-43).

*Plain language:* each Docker mutation below is a deliberate breakage of one behaviour, made only on a throwaway branch. One CI run carries several of them at once, so a run proves something only if every red test traces back to exactly one mutation, and no mutation hides another one's reds. Two review cycles failed on the same pattern: the lists said which tests a mutation was *meant* to break, while the conflicts came from what it *actually* breaks. So the check is now a table (R59), with one row per mutation, built from what its code site reaches. Each run then gets five mechanical check lines.

**Unit mutations** (run locally by the re-spawn named, one at a time; the named pin must trip):

| Id | Pin | Mutation (throwaway checkout only) |
|---|---|---|
| MU-01 | `Core_September2026_…_NoOvertime_NormTotal16280_FlexZero` | core sums the whole settlement window as one week against one week's norm |
| MU-02 | `Core_StatedTail_…_DatedSunday20Sep_Only` (+ the R8-relabelled dates in 14505-T) | week lines dated on the week's Monday |
| MU-03 | `Core_ClosingWeek_…`, `Core_TailCrossing_…` | week settled by the month containing its Monday |
| MU-04 | `Core_Leaver_…_Dated29Sep` | `SettlementCutoff` ignored by the core |
| MU-05 | `Core_Starter_…_1480` | norm counted for uncovered weekdays |
| MU-06 | `Core_OkBoundaryWeek_…` | one config per week, taken from the Sunday |
| MU-07 | `Core_OpeningWeekFractionChange_…` (both) | one fraction per week, the Sunday's |
| MU-08 | `Core_WindowContract_NoNormalHoursForContextDays` | `NORMAL_HOURS` emitted for context entries |
| MU-09 | `Core_WeekWithNormPeriodWeeksChange_SundayModeGoverns` | mode taken from the week's first covered day |
| MU-10 | `Core_AnnualActivity_DaysOver365PerPiece` | annual mode via weekday per-day norms |
| MU-11 | `Core_Flex_95Plus10Minus8_…` | flex clamped per week |
| MU-12 | `Core_Flex_CapInForceOnSettlementDay` | cap from the first covered day |
| MU-13 | `Core_Flex_Fraction0811_…` | default credit `7.4 × fraction`, unrounded |
| MU-14 | `DayNorm_HalfToEven_AtMidpoint` | `MidpointRounding.AwayFromZero` |
| MU-15 | `DayNorm_Weekend_Zero` | weekends get the weekday norm |
| MU-16 | `WeeklyContext_…_Matrix` (context row) | context days ignored |
| MU-17 | `WeeklyContext_…_Matrix` (Monday row) | Monday exemption dropped |
| MU-18 | `…ChangesWhenAnyConfigPropertyChanges` | one property omitted from the fingerprint |
| MU-19 | `…StableAcrossPropertyOrder` | properties in declaration order, not ordinal |
| MU-20 | `Legacy_Evaluate_…_2960` | legacy opening partial week judged against 37 |
| MU-21 | `Orchestrator_WeeklyPipeline_…_DatedSunday` | legacy dispatch dates lines on the period's first day |
| MU-22..24 | the four round-trip facts | `[JsonIgnore]` on `Source`, on `PlanInputs` (which also drops `SettlementCutoff`), on the next-period fields |
| MU-25 | `Plan_TwoSegments_EachCarriesOwnDatedWtmKey` | key hydrated once per plan, **in the planner** |
| MU-26 | `WeekVariant_HasReasonAgreementChangeMidWeek_NoRuleId` | `reason` omitted |
| MU-27 | `WeekVariant_SerializedJson_HasNoDateNoEmployeeId` | the change date included in `error` |
| MU-28 | `NoApprovalReader_OctoberWith…_Throws_…` | null reader skips the check |
| MU-29 | `NoApprovalReader_June2026_Succeeds` | throw whenever the reader is null |
| MU-30 | `NoHoursReader_Calculation_Throws_…` | null reader → empty hours |
| MU-31 | `ScreenParity_…` | week norm = `weekly × fraction` instead of the per-day sum |
| MU-32 | `NoIntegrationFile_…EvaluateTimeline` | **by injection (R46b):** add a `PostAsJsonAsync("/api/rules/evaluate-timeline", …)` literal to a non-allowlisted `src/Integrations/**` file. The guard flags a file only with both the path and a POST (`PlannerBypassGuardTests.cs:110-117`), so a valid tree gives it nothing to find |
| MU-33 | `HoursDifferingInFifthDecimal_…` | `Hours` written to four decimals |
| MU-34 | `TimesDifferingBy100ns_…` | times as `HH:mm:ss` |
| MU-35 | `HashesRowsAsRead_BeforeNormalization` | digest over normalized entries |
| MU-36 | 14511-T's timeline-pieces assertions (unit) | timeline starts at `periodStart` |
| MU-37 | `Legacy_NormCheckWeekly_OnFourWeekConfig_ForcesWeeklyMode_TagsOwnId`, `…NormCheckAnnual_OnWeeklyConfig_…` (+ 14505-T's forced-id `NormCheckRuleTests`) | legacy dispatch passes `ForcedNormMode = null` for every norm id (R34) |
| MU-38 | `NoApprovalReader_Replay_Succeeds` (R42) | replay throws on a null approval reader (it runs the calculation rule) |
| MU-39 | `OnlyProjectionHoursReader_NamesTheProjectionRepositories_UnderIntegrations` (N3) | **by injection:** add a `TimeEntryProjectionRepository` reference to a non-allowlisted `src/Integrations/**` file |
| **MU-41** (added at Step 5a of 14505, W1) | `Core_PiecesBeyondPeriodEnd_DoNotGovern` | the `day > _periodEnd → -1` bound in `PieceIndexOn` removed (`WeeklyTimelineCore.cs`, fix-up `2259cc1`). Run by 14506 re-spawn 2, beside MU-01..MU-24, MU-37 and MU-42 |
| **MU-42** (added at Step 5a of 14506, WARNING; 2026-10-14) | `Core_Flex_MidMonthFractionChange_ZeroHourCredits_UseEachDaysFraction_1340` | the default credit takes its fraction from the piece in force on the settlement day (the period end) instead of the absence day's piece (`WeeklyTimelineCore.cs`, `PieceOn(absence.Date).PartTimeFraction`). The pin gives 12.00 instead of 13.40. A first-piece regression (14.80) and a week-level one (12.00 or 14.80) also trip it, because both absences sit in the straddle week Mon 14 – Sun 20 Sep. Run by 14506 re-spawn 2 |
| **MU-43** (added at 14508's return, adjudicator Q2; 2026-10-14) | `MonthEndNightShift_Wed30Sep2300To0200_SeptemberNotRefused_PostMidnightHoursReachOctober` (14508-T) | the retired `AssertNoDroppedBoundaryCrossing` throw restored in PCS. September then refuses the month-end night shift. Run by 14514 re-spawn 2 at the A.4 gate, beside MU-25..MU-32, MU-36, MU-38..MU-40 |
| **MU-44** (added at 14511's return, adjudicator R-6; 2026-10-14) | `TimelineCallFails_PerSegmentCallsSucceed_CalculationRefused_NothingWritten`, `OnePerSegmentCallFails_TimelineSucceeds_CalculationRefused_NothingWritten` (14511-T) | partial failure tolerated: `callsFailed >= callsAttempted` restored in PCS. Run by 14514 re-spawn 2 at the A.4 gate, beside MU-36 and MU-40 |
| **MU-45** (added at 14511's Step 5a, adjudicator R-10; 2026-10-14) | `ContextPiece_PromotedToDepartmentHead1Sep2026_31AugPieceCarriesNoPosition_CallerPositionNotInherited`, `InMonthPieces_DatedRowHasNoPosition_CallerPositionNotInherited` (14511-T) | the caller-position fallback restored in `ResolveProfileAtAsync` (a null dated position replaced by the request's). Run by 14514 re-spawn 2 at the A.4 gate, with MU-44 |
| **MU-40** (R54) | 14511-T's piece assertions on the hire-dated and end-dated unit fixtures: `EmploymentWindowSegmentSkipTests.cs:51, :68` (hire), `:79, :102, :124` (last day), `:236-237` (two spells). It stands in for the Docker pin `EmploymentEdges_Starter3Sep_…` under fact 3 | PCS's piece builder includes NOT_EMPLOYED days. Run by 14514 re-spawn 2, beside MU-36, at the A.4 gate |

*Why MU-40 is a Unit mutation (R54):* "pieces include NOT_EMPLOYED days" can only sit in PCS's piece builder. Every PCS-level form of it reddens 14511-T's Unit piece assertions, which would stop an evidence run at its Unit step (fact 3). An endpoint-level form never reaches `EmploymentEdges_Starter3Sep_…`, which inspects the request PCS sends to a fake engine. So the Docker pin rests on this Unit observation, and its leaver twin keeps MD-52.

### The partition table (R59)

Every **non-guard** Docker pin of Stage A has a mutation (R46a, as worded by R54): a Docker one on an evidence list below, or a Unit one under fact 3 with a recorded ruling (today: `EmploymentEdges_Starter3Sep_…` → MU-40). A pin tagged "(guard)" passes today and is exempt, though several guards still carry an MD.

**Fact key.** The table cites facts by key; CA-1 resolves each to its `FullyQualifiedName`.

*Not pinned (adjudicated at Step 5a of 14507):*
- `ApprovalEndpoints.cs:1003` **awayToday** is filtered (R20). It cannot be observed until a retraction exists, because a replaced row always has a same-day successor. No fact and no MD; the pin is owed by the retraction task (ROADMAP).
- **The pre-check snapshot** (14507 fix-up) is a race with no deterministic observation point. It was reviewed, not pinned.

| Key | Fact | Task | |
|---|---|---|---|
| B1 · B2 | `Backfill_AfterSkemaResave_RebuildsCurrentSet_SupersessionInOutboxOrder` · `Backfill_AfterSkemaResave_ReRun_LeavesSupersessionMarksUnchanged` | 14501 | |
| B3 | `Backfill_AfterSkemaAbsenceResave_RebuildsCurrentSet_SupersessionInOutboxOrder` | 14501 (follow-up) | added at 14501's return (its pin-gap finding): the absence side of the rebuild. Conditions: a no-quota absence type (MD-06 cannot reach it), raw-SQL asserts (MD-02 cannot), absence events only (MD-01 cannot) |
| PAR | `Parity_AfterSkemaResaveAndTimePost_LiveCurrentSetEqualsFoldOfEvents` | 14501 | guard |
| TRM | `Termination_PostEndDateBooking_DoesNotConsume` (with R57's re-save leg) | 14501 | |
| SHO | `SpecialHoliday_ResavedAllowanceDayInTakingWindow_UsedCountedOnce` | 14501 | |
| HDL | `HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema` | 14501 | guard, no MD |
| S1 · S2 · S3 · S4 | `Resave_Entry_SkemaGetReturnsOneEntry_8` · `…_TimeEntriesGetReturnsOneRow` · `…_SendGateBalancedAt8` · `…_TeamOverviewShows8` | 14502 | |
| S5 | `Resave_Entry_EventsTableHoldsTwoEvents` | 14502 | guard |
| S6 · S7 · S8 | `Resave_Absence_SkemaGetAndAbsencesGetReturnOneRow` · `Retry_IdenticalPayload_LeavesOneCurrentRow` · `Resave_ZeroOrNegativeHours_Returns400_WritesNothing` (4 rows) | 14502 | |
| S9 · S10 | `Resave_FullDayOnlyAbsence_PositivePartialHours_Keeps422` · `TimePost_TwoRegistrationsSameDayAndTask_BothCounted` | 14502 | guards |
| S11 · S12 · S13 | `Resave_Entry_AllocationBreakdownCountsOnce` · `Resave_Entry_BalanceSummaryCountsOnce` · `Resave_StrandCheckCountsCellOnce` | 14502 | |
| Q1 · Q2 · Q3 · Q4 | `Resave_Vacation_SameHours_UsedUnchanged` · `…_FewerHours_UsedFallsBySignedDelta` · `ExhaustedQuota_UnchangedReplacement_Returns200` · `ExhaustedQuota_ReducedReplacement_Returns200` | 14502 | |
| Q5 | `ExhaustedQuota_IncreaseBeyondCap_Returns422` | 14502 | guard |
| Q6 · Q7 | `UsedAboveCapAfterRevaluation_Reduction_Succeeds` · `NearCap_ResaveIncreaseWithinCap_Returns200` | 14502 | |
| RE1 · RE2 · RE3 | `CalculateAndExport_September_FullTimeHK_AtNorm_NoOvertimeNoFlexPayout_RealEngine` · `…_September_FullTimeAC_AtNorm_NoMerarbejde_RealEngine` · `…_October_HoursFromProjections_ClosingWeekPaidDated4Oct_RealEngine` | 14503a | |
| H1 · H2 | `CalculateAndExport_BodyCarriesEntry_Returns400_WritesNothing` · `Recalculate_BodyCarriesEntry_Returns400_WritesNothing` | 14503b | |
| H3 · H4 · H5 | `CalculateAndExport_October_SeptemberNotApproved_Returns409_NamesSeptember_WritesNothing` · `…_October_SeptemberEmployeeApproved_Returns409` · `Recalculate_October_SeptemberNotApproved_Returns409` | 14503b | |
| H6 · H7 | `CalculateAndExport_June2026_StartsMonday_NoPriorApprovalNeeded_200` · `…_StarterOnFirst_NoPriorApprovalNeeded_200` | 14503b | guards, no MD |
| H8 | `CalculateAndExport_StarterThursday3Sep_NoAugustApprovalNeeded_200` | 14503b | guard |
| H9 · H10 | `CalculateAndExport_SplitMonth_PriorNotApproved_AnswersPlanner422First` · `CalculateAndExport_AgreementChangeTue1Sep_AugustNotApproved_AnswersWeekCheck422First` | 14503b | H9 passes today (no prior-month check exists, so the planner 422 already answers). Its only RED is MD-24 in EA-4, which satisfies R46a. CA-1 should expect no pre-implementation RED for H9 (14503b report) |
| H11 | `Export_October_SeptemberNotApproved_RawRouteUnaffected_200` | 14503b | guard, no MD |
| H12 | `Recalculate_ExportedSeptember_OctoberExported_ResponseAndAuditNameOctober` | 14503b | |
| H13 · H14 | `Recalculate_ExportedSeptember_OctoberNotExported_NamesNothing` · `Recalculate_ExportedMay2026_MonthEndsSunday_NamesNothing` | 14503b | guards |
| AR1–AR4 | `Recalculate_` · `CalculateAndExport_` · `Export_` · `ExportPeriod_` + `AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing` | 14503b | |
| AR5–AR8 | the same four routes + `AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing` | 14503b | |
| AR9 · AR10 | `CalculateAndExport_AgreementChangeMonday1June_Pays_200` · `CalculateAndExport_AgreementChangeWed18Mar_StillPlanner422_WithRuleId` | 14503b | guards, no MD |
| X1 · X2 | existing S144 `Recalculate_MidMonthAgreementCodeChange_Returns422_RedactedProblem_LinesUnchanged` (`PayrollHostRecalcBlockedTests.cs:151`) · `CalculateAndExport_MidMonthAgreementCodeChange_Returns422_NoExportRecord_NoManifest` (`:203`) | existing | |
| X3 · X4 | existing S144 `CalculateAndExport_RulesUnavailable_Returns503_NoExportRecord_NoManifest` (`:454`) · `Recalculate_RulesUnavailable_Returns503_LinesUnchanged` (`:493`) | existing | |
| SMK | `PayrollCalculateAndExport_WritesExactlyOneManifestLinkedAuditRow_ComposedStack` (smoke job, March 2026) | existing | |
| W1 | `WriteForExportedMonths_BackdateTue29Sep_BoundedAt1Oct_FlagsSeptemberAndOctober` | 14504 | |
| W1-EX | the existing `WriteForExportedMonths_SelectsMonthsIntersectingInterval_OneRowPerMonth_BaselineHashPerRow` (`HrBackdateWorklistRepositoryTests.cs:101-149`), re-expected under R5(a) to February, March **and** April | 14509-T | added at 14504's return (its finding 1): R5(a) moves its `hi` from Wed 1 Apr to Mon 6 Apr, so April is now flagged |
| W1-G | `WriteForExportedMonths_BackdateBoundedAtMonday28Sep_DoesNotFlagOctober` | 14509-T | (guard). It catches the off-by-one that widens from `hi` instead of `hi − 1` (14504's finding 5) |
| RO1 · RO3 | `Reopen_ApprovedNotExportedSeptember_OctoberExported_WritesOctoberRow_PriorMonthReopened_Unblocked` · `Reopen_ApprovedNotExportedSeptember_OctoberAndNovemberExported_WritesOctoberOnly` | 14504 | |
| RO2 · RO4 | `Reopen_ApprovedNotExportedSeptember_OctoberNotExported_WritesNothing` · `Reopen_ApprovedNotExportedMay2026_JuneExported_WritesNothing` | 14504 | guards, no MD |
| WP1 · WP2 · WP3 | `TwoMonths_ClosingWeek_SeptemberPaysNothing_OctoberPays300And180_Dated4Oct` · `TwoMonths_TailCrossing_OctoberPays300_Dated4Oct` · `AugustToOctober_NoDuplicateNoGap_OvertimeEqualsWeeklyExcessOfSettledWeeks` | 14514 | |
| WP4 · WP5 | `Leaver_LastDayWed30Sep_SeptemberPaysClosingWeek_Dated30Sep` · `OkBoundary_April2026_OvertimeDated5April_OK26Key_ContextCarries30And31March` | 14514 | |
| WP6 · WP7 | `OpeningWeekFractionChange_Tue1Sep_OneSegmentMonth_Pays256And604_Dated6Sep` · `OpeningWeekFractionChange_Tue1Sep_AC_Merarbejde860` | 14514 | |
| WP8 · WP9 · WP10 | `WindowContract_September_NormTotal16280_NoNormalHoursFor31Aug` · `WindowContract_PositionChangeTue1Sep_SundayModeReported_NoExportLineChange` · `WindowContract_AcResearch_AnnualTotalKept` | 14514 | |
| WP11 · WP12 | `Flex_TwoMonths_SeptemberPaysOut500Dated30Sep_OctoberFed100_NoPayout` · `DatedKey_StaleBodyPosition_MapsWithDatedRecordPosition` | 14514 | |
| EE1 | `EmploymentEdges_Starter3Sep_TimelineCoversEmployedDaysOnly` | 14514 | covered by MU-40 (fact 3, R54) |
| EE2 | `EmploymentEdges_LeaverTue29Sep_SeptemberSettlesWeek_OctoberEvaluatesNothing` | 14514 | |
| RP1 · RP2 · RP3 | `Replay_AfterBackdateAtFrozenStart1Sep_ByteIdentical` · `Replay_After31AugContextFractionChange_ByteIdentical` · `Replay_RecordedEntryIdAbsent_RefusesReplayEntriesMissing` | 14515 | |
| RP4 · RP5 · RP6 | `Replay_SupersededBoundRow_IsNotAbsent_ReplaysByteIdentical` · `Replay_TamperedEntryRow_RefusesEntriesDigestMismatch` · `Replay_TamperedAbsenceRow_RefusesAbsencesDigestMismatch` | 14515 | |
| RP7 · RP8 · RP9 | `Replay_ConfigFingerprintChanged_RefusesReplayConfigChanged` · `Replay_EvaluationModelChanged_RefusesReplayEvaluationModelChanged` · `Replay_ManifestWithoutPlanInputs_RefusesManifestWithoutFrozenInputs` | 14515 | |
| RC1 · RC2 | `Reconstruction_NewEntryAfterCalc_ReplayByManifestIdOnly_ByteIdentical_FreshCalcDiffers` · `Reconstruction_BoundCellResaved_ReplayByteIdentical_FreshCalcDiffers` | 14515 | |
| RL · RS · RB | `Replay_LeaverEndDateRemovedAfterCalc_ByteIdentical_FreshCalcDiffers` · `Replay_AfterPriorMonthReopened_ByteIdentical_FreshCalcRefused409` · `Rebuild_PreservesPlanInputs_ReplaySucceeds` | 14515 | |
| FA | `ForwardCalc_ResolverAsOf_UsesRowInForceAtSegmentStart` | 14515 | guard, no MD (N1) |

**Path key.** A path id names one guard, branch or value-producing statement sequence (R44). "Edits" is the path a mutation changes; "relies on" lists the paths its reds need left intact.

**How "relies on" is derived (R63).**
- *Hard (the "Relies on" column; check (c)):* every guard on the route between the request and the asserted value of each red.
- *Soft (the "Not-guards" table below; check (c′)):* the guard that keeps each Not fact green.
- If one MD edits another MD's Not-guard in the same run, that is not a partition failure while check (a) holds. But the row must state the status the affected facts then show.

| Path | What it is (task) | Path | What it is (task) |
|---|---|---|---|
| STAMP-E · STAMP-A | the Skema save stamps `Source` on `TimeEntryRegistered` · on `AbsenceRegistered` (14507 Do 1) | EMIT-E | the Skema endpoint emits one `TimeEntryRegistered` per posted entry cell (`SkemaEndpoints.cs:1790-1816`) |
| MARK | `InsertAsync` marks the cell's current SKEMA row, then inserts (14507 Do 2) | MARK-SRC | the mark's source restriction: SKEMA inserts mark SKEMA rows only |
| RD-E · RD-A | the two default reads' current-row predicate, entries · absences (14507 Do 3) | BYID | `GetByEventIdsAsync` reads unfiltered |
| SQL-TO · SQL-AL · SQL-SET · SQL-ST | the current-row predicate in team overview · allocation · settlement · strand check | BF-MARK | the backfill's mark step (14507 Do 4) |
| ZERO · FDO | the ≤ 0 400 · the full-day-only 422 for replacement cells (14507 Do 6) | Q-DELTA | the signed in-lock delta (14507 Do 5) |
| Q-POS · Q-NEG | delta > 0 → guarded `CheckAndAdjustAsync` · delta ≤ 0 → ungated adjust | Q-GATE · Q-MIR | the pre-check runs only for delta > 0 · its `used = balance.Used − Σ superseded` |
| H-400 · H-DI-APR | host: non-empty body hours → 400 (14508) · registration of the approval adapter (14512) | HRD-FROM | `ProjectionHoursReader.ReadCurrentAsync` honours `fromInclusive` (14508) |
| AGR-WIDE · HIRE | the agreement read starts at the opening Monday (14511) · the hire-dated-row filter (14512 Do 1) | WK-CALC · WK-RAW | the per-plan lane calls the week check · the raw-route guard calls it (14512 Do 1) |
| PM | the prior-month check: its APPROVED-only predicate and its place after planning and the week check (14512 Do 2–3) | CUT | PCS's cutoff derivation, `End` ≤ `periodEnd` else null (14511 Do 2) |
| FLEX-OPEN · KEY | the per-plan lane's opening flex · the dated `WtmNaturalKey` hydrator (14512 Do 5) | EP-CFG · EP-FRAC · EP-START | the timeline endpoint handler's per-day config · per-piece fraction · core `PeriodStart` = the request's (14505 Do 3) |
| RP-ENTRY · RP-READ | replay enters evaluation only · reads by recorded id (14512 Do 2; 14513 Do 3) | RP-MISS · RP-DIG · RP-FP · RP-MOD · RP-NULL | replay's refusals: absent id · digest · fingerprint · marker · null slot (14513 Do 3) |
| RP-SEG · RP-CTX · RP-CUT | replay's per-segment lane uses frozen pieces (no `:570` read) · the per-plan lane uses frozen context fractions · the frozen cutoff | RB-SLOT | the rebuilder carries `plan_inputs_jsonb` (14509 Do 3) |
| PLAN · FETCH | the planner's split refusal · the classification fetch, both inside the builder's `PeriodPlanner.Plan(` call (`PeriodCalculationService.cs:1100-1105`) | | |
| Q7 | the Q7 (b) rule with its two conditions (14512 Do 6) | WL-WIDE · WL-HOOK · WL-ROUTE | R5(a) widening (14509 Do 1) · the reopen hook · the hook writes exactly M+1 (14509 Do 2) |

**Unit/DemoSeed reach — the four reasons for N.** CA-1 step 4's dry run confirms every N.
- *(u1)* The site runs only against Postgres or the HTTP hosts. No Unit test executes it: the Unit suite uses `VacationSettlementService` only through pure statics (for example `TerminationCrystallizationTests.cs:57`), and names `SkemaEndpoints.cs` only as a path in a source-text guard against accrual formula bodies (`AccrualMathSingleSourceTests.cs:110-114`), which no quota or supersession mutation writes. No Unit file references the two projection repositories, `ApprovalEndpoints`, `ProjectionBackfillService`, `EmploymentWindowStrandCheck` or `SegmentManifestProjectionRebuilder` (grep at `299c3dd`).
- *(u2)* A PCS site. The Unit fixtures use the APPROVED fake (R26), in-memory hours readers (14508-T), no agreement-code repository, and assert no cutoff (C-T1).
- *(u3)* The timeline endpoint handler. No Unit test calls `/api/rules/evaluate-timeline`: the core's pins call `WeeklyTimelineCore.Evaluate`, and the legacy route builds one piece starting at `periodStart` (TASK-14505 Do 4).
- *(u4)* A replay site. The one Unit replay, `NoApprovalReader_Replay_Succeeds`, asserts success with a null approval reader and normal in-memory readers.
- *DemoSeed:* no mutation site is in `tools/StatsTid.DemoSeed/**` (Fable cycle-2 review, § 6).

**The table.** "Not:" names the near facts a row was checked against and does not reach. "Existing:" says whether a fact outside the Stage A pins can turn red. "Restores today" means the mutation puts back today's behaviour at its site, and every existing fact passes on today's code.

| Id | Site (file · method · guard/branch) | Expected red, by effect | Edits | Relies on | Unit / DemoSeed | Run |
|---|---|---|---|---|---|---|
| MD-01 | `SkemaEndpoints.cs` save handler · the `TimeEntryRegistered` emission (`:1794-1810`) · `Source` left null | S1, S2, S3, S4, S7, S11, S12, S13: both rows are TIME_POST, so both stay current. Not: S6 (absence), S5 (two events either way), PAR (fold and live both additive). Existing: none (restores today) | STAMP-E | MARK-SRC, EMIT-E | N (u1) | EA-1 |
| MD-02 | `AbsenceProjectionRepository.cs` · `GetByEmployeeAndDateRangeAsync` (`:76`), `GetByEmployeeAsync` (`:101`) · predicate removed | S6. Not: the quota facts (one re-save each, so no superseded row exists when predecessors are read), TRM/SHO (raw-SQL read). Existing: none (restores today) | RD-A | STAMP-A, MARK | N (u1) | EA-2 |
| MD-03 | `TimeEntryProjectionRepository.cs` · `:76`, `:102` · predicate removed | S1, S2 (`TimeEndpoints.cs:366`), S3, S12. Not: S7 (asserts by SQL, C-S2), S4/S11/S13 (raw SQL), RC2 (with the echo handler its fresh half encodes both 28 September rows, 9 h and 7.4 h, and still differs from the frozen 9 h, C-R4). Existing: none (restores today) | RD-E | STAMP-E, MARK, EMIT-E | N (u1) | EA-2 |
| MD-04 | `ApprovalEndpoints.cs` team-overview SQL (`:888`, `:908`) · predicate removed | S4. Existing: none (restores today) | SQL-TO | STAMP-E, MARK, EMIT-E | N (u1) | EA-2 |
| MD-05 | `VacationSettlementService.cs` · `ReadRecordedFeriedageAsync` (`:1914-1922`) · predicate removed | TRM (`used` 4, crystallized 8.5), SHO. Existing: none (restores today) | SQL-SET | STAMP-A, MARK | N (u1) | EA-1 |
| MD-06 | `SkemaEndpoints.cs` in-lock quota step · delta := Σ new, unsigned | Q1, Q2, Q3, Q4, Q6, Q7. Not: Q5 (refused either way); TRM/SHO (the guarded `+Σ new` stays inside the cap, C-A2, and the TERMINATION snapshot uses recorded rows, `VacationSettlementService.cs:1826-1828`). Existing: CA-1 census | Q-DELTA | Q-POS | N (u1) | EA-1 |
| MD-07 | `SkemaEndpoints.cs` pre-check · `used = balance.Used` (no `− Σ superseded`) | Q7 (`cap − 0.5 + 1 > cap`). Only delta > 0 reaches the pre-check. Not: TRM/SHO (the first save has no superseded row; the re-save has delta 0). Existing: none (first saves are unchanged) | Q-MIR | Q-GATE | N (u1) | EA-2 |
| MD-08 | `SkemaEndpoints.cs` in-lock · the delta ≤ 0 branch calls `CheckAndAdjustAsync` | Q6: `used + delta` stays above the cap (C-Q2). Not: Q1–Q4 (at or under the cap, so the guard passes). Existing: CA-1 census | Q-NEG | Q-DELTA | N (u1) | EA-3 |
| MD-09 | `SkemaEndpoints.cs` pre-check gate · runs for delta ≤ 0 too | Q6 (the mirror gives `cap + 0.5`). Not: Q3/Q4 (remaining 0 and 0.5 with no planned days, C-Q1). Existing: CA-1 census | Q-GATE | Q-DELTA, Q-MIR | N (u1) | EA-4 |
| MD-10 | `SkemaEndpoints.cs` pre-lock validation · the ≤ 0 400 removed | S8 (4 rows). Existing: none (restores today) | ZERO | — | N (u1) | EA-2 |
| MD-11 | `ProjectionBackfillService.cs` · the per-event mark step removed, **in both blocks** (the time-entry block and the absence block; removing only one leaves B1/B2 or B3 green, so EA-1 would not match; observed at 14501's follow-up, a note for TASK-14518's CA-3 builder) | B1, B2, B3 (B3 added at 14501's return). Not: PAR (live path, never the rebuild). Existing: none (restores today) | BF-MARK | — | N (u1) | EA-1 |
| MD-12 (N6) | `SkemaEndpoints.cs` save handler · **the entry replacement path**: a cell with a current SKEMA row is updated in place and no `TimeEntryRegistered` is emitted | S5 (one event), PAR (fold 7.4 ≠ live 8.0). Not: S1–S4, S7, S11–S13 (one row of 8.0); RC2/RP4, whose re-saves go through `InsertAsync` at repository level, not the endpoint. Existing: CA-1 census | EMIT-E | — | N (u1) | EA-4 |
| MD-13 | both repositories' `InsertAsync` · the mark runs for every insert and drops the `source = 'SKEMA'` restriction | S10, PAR. Not: B1/B2 (the backfill has its own mark step); TRM/SHO (their Skema dates carry no raw row, C-A1); S11/S13 (Skema saves only, C-S3). Existing: CA-1 census | MARK-SRC | — | N (u1) | EA-3 |
| MD-14 | `SkemaEndpoints.cs` · both full-day-only checks (`:982-990`, `:1776-1784`) skipped when the cell has a current row | S9 (C-S4). Existing: CA-1 census | FDO | — | N (u1) | EA-1 |
| MD-15 | both repositories' `GetByEventIdsAsync` · adds `superseded_by_outbox_id IS NULL` | RP4, RC2 (replay half): the replay refuses `replay-entries-missing`, which no handler output can hide (C-R4). Not: RC1, RS (no superseded row). Existing: none (new member) | BYID | MARK, RP-READ | N (u1) | EA-2 |
| MD-16 | `EmploymentWindowStrandCheck.cs:64-70` · predicate removed | S13. Not: TRM (its superseded row is before the end date). Existing: none (restores today) | SQL-ST | STAMP-E, MARK, EMIT-E | N (u1) | EA-3 |
| MD-17 | `ApprovalEndpoints.cs` allocation breakdown (`:1293-1294`, `:1339`) · predicate removed | S11. Existing: none (restores today) | SQL-AL | STAMP-E, MARK, EMIT-E | N (u1) | EA-3 |
| MD-18 | `SkemaEndpoints.cs` · *(corrected at CA-5, R-47)* the delta > 0 branch **of a re-save (Σ replaced > 0)**: no pre-check and the ungated adjust (the delta ≤ 0 branch and first bookings untouched). *(Draft 5's "the delta > 0 branch" also ungated first bookings: EA-3 mismatch, CI run `38793589235`.)* | Q5. Not: Q7 (allowed either way); FB-1..FB-6 (first bookings: Σ replaced = 0 keeps them on master's path, R-47). Existing: CA-1 census | Q-POS, Q-GATE | Q-DELTA | N (u1) | EA-3 |
| MD-19 | `Payroll/Program.cs` both calculating handlers · the non-empty-list 400 removed | H1, H2. Existing: none (14508-T removes every body list) | H-400 | — | N (u1) | EA-1 |
| MD-20 | `ProjectionHoursReader.cs` · `ReadCurrentAsync` · `fromInclusive` replaced by the first day of `toInclusive`'s month | RE3, WP1, WP2, WP3, WP5, WP6, WP7; RC1, RC2: the echo handler encodes context entries (C-R4), so on correct code their fresh half differs. Under MD-20 the forward read and the fresh read both start on 1 October and miss 29 and 28 September, so it does not (refinement `:668, :671`; C-R1). Not: RE1, RE2, WP8, WP9, WP11 (context days at exactly the day norm, so dropping them only lowers an at-norm week, C-RE3/C-W3); WP4 (closing week only); WP10, WP12; the replay byte-identity halves (forward and replay read the same clamped set); RL (its difference is the encoded cutoff, which MD-20 does not touch); H*, AR*, SMK. Existing: none reached (X1–X4 refuse before any rule; SMK writes its audit row) | HRD-FROM | — | N (u2) | EA-1 |
| MD-21 | `Payroll/Program.cs` · the `IPriorPeriodApprovalReader` registration removed (PCS gets null; R21's throw → 500) | Every host calculating fact whose month's opening week reaches an EMPLOYED earlier month and that gets past the week check: RE1, RE2, RE3, H3, H4, H5, H12, H13, H14 (31 August; 27–30 April), SMK. Not: H1/H2 (400 first); H6/H7/H8 (no EMPLOYED M−1 day); H9, AR10, X1, X2 (planner first); H10, AR1, AR2, AR5, AR6 (week check first, outside EA-2); X3, X4 (503 first); the raw routes. Existing: SMK, listed (the other existing callers of the two routes are X1–X4; `CalculateAndExportScopeTests` never calculates) | H-DI-APR | PM | N (u1) | **EA-2** (D1) |
| MD-22 | PCS prior-month check · the predicate accepts EMPLOYEE_APPROVED | H4, RS (fresh half). Not: H3/H5 (C-H2). Existing: none (new check) | PM | — | N (u2) | EA-1 (R52) |
| MD-23 | PCS admission · the prior-month check runs before the week check (still after planning) | H10. Not: AR1, AR2, AR5, AR6 (M−1 APPROVED, C-H1). Existing: none | PM | — | N (u2) | EA-3 |
| MD-24 (R61) | PCS calculating entry points · **the per-plan lane's admission step** — *frozen wording (R-35, reader-first):* in `CalculatePlanlessAsync` (`:1305`), before `BuildPlanWithHistoriesAsync`, the prior-month check runs first and consults the approval reader; only when M−1 is not APPROVED (or no reader is registered) does it read the employment windows, then throws `PriorMonthNotApprovedException` (a null reader throws `InvalidOperationException`). `BuildPlanWithHistoriesAsync` and the raw guard `EnsurePeriodCalculableAsync` (`:1433-1442`) are untouched, so the raw routes never meet it. *(Draft-5 wording, superseded: "before the lane's call to `BuildPlanForLegacyCallersAsync` … the mutation reads the employment windows itself"; that second window read turns `Shim_PreHireSegment_NeverResolvesProfile_EmployedSegmentDoes` red, CA-1 F3.)* | H9, H10; X1, X2 (planner 422 → 409); X3, X4 (503 → 409). The planner's refusal and the classification fetch are both raised inside the builder call (`:1100-1105`; `HttpRuleClassificationProvider.cs:126`), which the mutated check now precedes. Not: AR10 (February APPROVED, C-H1); H6–H8 (no M−1 day); **H11 and H5**: the raw routes never enter the lane, so H11 (the raw-route guard) stays 200 and H5's raw-route export of October still succeeds, and H5's recalculation answers 409 either way. Existing: X1–X4, listed (14512-T leaves them unseeded) | PM | — | N (u2: `PcsNeverSilentTests` still throws for a null reader) | EA-4 |
| MD-25 | `Payroll/Program.cs` raw-route guard (`RefuseUnplannableMonthsAsync`, `:577-610`) · skips the week predicate | AR3, AR4, AR7, AR8. Not: H8 (a calculating route). Existing: none (restores today) | WK-RAW | — | N (u1) | EA-1 |
| MD-26 | PCS per-plan lane · the week-check call removed | AR1, AR2, AR5, AR6 (200), H10 (409, not 422). Existing: none (restores today) | WK-CALC | — | N (u2) | EA-1 |
| MD-27 | PCS · the agreement read keeps `afterExclusive: periodStart` (`PeriodCalculationService.cs:1058-1059`) | AR1–AR8 (1 September is `periodStart`; 29 September is before October), H10. **In EA-2, AR1, AR2, AR5, AR6 and H10 surface as the never-silent 500 (MD-21's null reader), not 200/409** (R63). The red still proves MD-27: with the week check intact, the 422 fires whatever MD-21 does. Existing: none (restores today's read) | AGR-WIDE | — | N (u2) | EA-2 |
| MD-28 | `RetroactiveCorrectionService.cs` · the Q7 (b) block removed | H12. Existing: none (restores today) | Q7 | — | N (u1: the service has no Unit caller) | EA-1 |
| MD-29 — **withdrawn (R-34); its behaviour is MU-46** (it turns `FlexCarry_PassesThroughNotEmployedMiddleSegment_Unchanged` red, so it cannot be a Docker mutation; WP11 and `TwoSpells_…` rest on MU-46) | PCS per-plan lane · `PreviousFlexBalance = 0` | WP11. Not: the replay facts (forward and replay both send 0). Existing: CA-1 census | FLEX-OPEN | — | N (u2) | EA-2 |
| MD-30 | PCS · the `WtmNaturalKey` hydrator returns the request profile's key for every segment. **In PCS, not the planner**, so MU-25's planner pin stays green | WP12. Not: AR9 (C-H3), RE*, the other WP* (C-W4). Existing: none (restores today's once-per-plan key, `:1092-1096`) | KEY | — | N (u2) | EA-1 |
| MD-31 | PCS replay, per-segment lane · re-resolves the profile at `:570` (`segment.StartDate`) | RP1: the replay's per-segment `/api/rules/evaluate` call carries the profile re-resolved at 1 September, and the echo handler echoes its fraction, agreement code and position, so the replay differs (C-R4). Not: RS (no profile resolver, C-R3); RP2 (31 August is not a segment start); the marquee replays (their supersessions are effective today, after April 2026, `EmployeeProfileMarqueeTests.cs:173-183`). Existing: none | RP-SEG | — | N (u4) | EA-1 |
| MD-32 | PCS replay · both digest comparisons skipped | RP5, RP6 | RP-DIG | — | N (u4) | EA-1 |
| MD-33 | PCS replay · the fingerprint comparison skipped | RP7 | RP-FP | — | N (u4) | EA-3 |
| MD-34 | PCS replay · the marker comparison skipped | RP8 | RP-MOD | — | N (u4) | EA-2 |
| MD-35 | PCS replay · a null `PlanInputs` falls back to live inputs | RP9. Not: RB (its slot is present unless MD-51) | RP-NULL | — | N (u4) | EA-1 |
| MD-36 | PCS replay, per-plan lane · re-resolves the context pieces' fractions from the dated profile resolver | RP2: the replay sends the re-resolved 31 August fraction, which the echo handler encodes per piece (C-R4). Not: RS (C-R3); RP1 (1 September is in the month); the marquee replays | RP-CTX | — | N (u4) | EA-2 |
| MD-37 | PCS replay · proceeds past an absent recorded id | RP3 | RP-MISS | RP-READ | N (u4) | EA-3 |
| MD-38 | PCS replay · reads `ReadCurrentAsync` over the window instead of `ReadByEventIdsAsync` (the id check kept) | RC1 (the new 29 September row → digest mismatch), RC2 and RP4 (the superseded row is absent), RS (its `ReadCurrentAsync` sentinel throws). Each is a refusal or a throw, so it shows whatever the handler encodes (C-R4). Not: RP3, RP5, RP6 (refused either way) | RP-READ | MARK | N (u4) | EA-4 |
| MD-39 | the timeline endpoint handler (TASK-14505 Do 3) · one config for the whole timeline, from the first piece (**the named substitute**, ruling on draft 1, § 3) | WP5 (fingerprint over OK24 only), WP9 (`NormPeriodWeeks` 1). Not: WP6/WP7, RE* (one config in force). Week grouping lives only in the core and is observed by MU-01 | EP-CFG | — | N (u3) | EA-4 |
| MD-41 | the timeline endpoint handler · the first piece's fraction on every piece. **Not in PCS's piece builder**, so 14511-T's piece assertions stay green | WP6, WP7 | EP-FRAC | — | N (u3) | EA-3 |
| MD-42 | the timeline endpoint handler · hands the core `PeriodStart = Pieces[0].From` instead of the request's `PeriodStart`. **The request is unchanged**, so 14511-T's request-shape assertions (made on what PCS sends) cannot see it; the core is unchanged, so `WeeklyTimelineCoreTests` stay green | RE1 (a NORMAL_HOURS line for 31 August; sum 170.20), RE3 (C-RE2), WP8, WP10 (31 August counts in days ÷ 365). Not: RE2 (C-RE1); WP1–WP7, WP9, WP11 (C-W1, C-W2, C-W6); WP12 (no context day); H*, AR*, the replay facts (stub engine, C-R4); SMK (audit row only); EE* (fake engine) | EP-START | — | N (u3) | **EA-4** (D1) |
| MD-43 | PCS cutoff derivation (TASK-14511 Do 2) · returns the last piece day (`periodEnd` for a stayer) | WP1, WP2, WP3, RL: for the 30 September leaver the last piece day is 30 September before and after the end date is removed, so the echo handler encodes the same cutoff and "differs" fails (C-R4). Not: the stayer replay facts (their frozen and fresh cutoffs are both `periodEnd` under MD-43, and RC1/RC2 still differ by their hours); WP4, EE2 (for a leaver the last piece day is the window end); WP6/WP7 (C-W5); RE1, RE2, WP8 (at norm); EE1 and 14511-T's assertions (no cutoff asserted, C-E1, C-T1) | CUT | — | N (u2) | EA-3 |
| MD-44 | PCS replay · re-derives the cutoff from the live window (consults `IEmploymentWindowResolver`) | RL (the replay encodes the live null cutoff instead of the frozen 30 September, C-R4), RS (its window sentinel throws) | RP-CUT | — | N (u4) | EA-2 |
| MD-45 (R53) | PCS replay · consults `IPriorPeriodApprovalReader` **when one is registered**, and applies the prior-month predicate | RS (its approval sentinel throws). Not: the `BuildPcs` replays (the APPROVED fake answers); the Unit `NoApprovalReader_Replay_Succeeds` (a null reader is not consulted, so the Unit step stays green; the null case is MU-38's) | RP-ENTRY | — | N (u4) | EA-3 |
| MD-46 | PCS admission · the hire-date filter (TASK-14512 Do 1) dropped | H8 (422). Existing: CA-1 census | HIRE | WK-CALC | N (u2) | EA-4 |
| MD-47 | `HrBackdateWorklistRepository.cs` · `WriteForExportedMonthsAsync` · the R5(a) widening removed | W1, W1-EX (re-expected by 14509-T, so restoring today's clip turns it red; added at 14504's return). Not: W1-G (a guard; no October either way). Existing: W1-EX only | WL-WIDE | — | N (u1) | EA-1 |
| MD-48 | `ApprovalEndpoints.cs` reopen handler · the R5(c) hook removed | RO1, RO3. Existing: none (restores today) | WL-HOOK | — | N (u1) | EA-1 |
| MD-49 | `ApprovalEndpoints.cs` reopen handler · the hook writes through the widened writer | RO3 (November is flagged too) | WL-ROUTE | WL-HOOK, WL-WIDE | N (u1) | EA-2 |
| MD-50 | `RetroactiveCorrectionService.cs` · Q7 (b) names M+1 without its two conditions | H13, H14. Existing: CA-1 census (a grep at `299c3dd` finds no whole-payload comparison of the correction event in `RetroactiveCorrection*Tests.cs` or `RecalcBlockedLiveRulesetTests.cs`) | Q7 | — | N (u1) | EA-3 |
| MD-51 | `SegmentManifestProjectionRebuilder.cs` · `plan_inputs_jsonb` omitted | RB (replay refuses `…without-frozen-inputs`) | RB-SLOT | RP-NULL | N (u1) | EA-2 |
| MD-52 | PCS cutoff derivation · always null, even for a leaver | WP4, RL (forward and fresh both encode a null cutoff, so the fresh half no longer differs, C-R4), EE2 (the request's cutoff) | CUT | — | N (u2) | EA-1 |

MD-40 is withdrawn (R54); its behaviour is MU-40. MD-29 is withdrawn (R-34); its behaviour is MU-46.

**The derived check lines, one per run.** Five checks per run:
- (a) every fact on at most one list in the run;
- (b) every edited path id at most once in the run;
- (c) no MD edits a path that another MD in the run relies on (hard, R63);
- (c′) where an MD edits another MD's Not-guard, every affected Not fact is on a list in the run, and its row states the status the fact then shows (soft, R63);
- (d) every Unit/DemoSeed reach is N.

Checks (a), (b) and (d) are R59's. Check (c) is an extension (E1, § "Needs a ruling" item 7). It is R44's "removes, moves or bypasses code the other edits" made mechanical, and it is the only check that sees MD-21 + MD-24 and MD-12 + MD-03. Check (c′) is R63's: it catches the case where one MD's change alters the status a listed fact shows, as MD-27 does in EA-2.

**Not-guards (R63).** These are the rows whose Not facts are kept green by a code guard. In every other row, a Not fact stays green because of a fixture condition, or because it does not lie on the mutated path.

| MD | Not-guards (Not facts they keep green) |
|---|---|
| MD-02 | SQL-SET (TRM, SHO are read through the settlement's raw SQL; its editor MD-05 is in EA-1, not MD-02's run) |
| MD-03 | SQL-TO, SQL-AL, SQL-ST (S4, S11, S13 read through raw SQL) |
| MD-06 | Q-POS (Q5 is refused either way) |
| MD-07 | Q-GATE (the TRM/SHO re-saves have delta 0, so they get no pre-check) |
| MD-20 | H-400, WK-CALC, WK-RAW, PM, PLAN, FETCH (the host facts' statuses are set before any rule runs); CUT (RL's fresh half, WP4); RP-SEG (RP1's replay half) — Step-0b cycle 4, internal W1 |
| MD-35 | RB-SLOT (RB; its editor MD-51 is in EA-2, not MD-35's run) |
| MD-21 | H-400 (H1, H2); PLAN (H9, AR10, X1, X2); WK-CALC and AGR-WIDE (H10, AR1, AR2, AR5, AR6); FETCH (X3, X4) |
| MD-38 | RP-MISS, RP-DIG (RP3, RP5, RP6 are refused either way) |

| Run | Mutations (count) | (a) facts | (b) edited paths | (c) relies-on | (c′) Not-guards | (d) reach |
|---|---|---|---|---|---|---|
| **EA-1** | MD-01, 05, 06, 11, 14, 19, 20, 22, 25, 26, 28, 30, 31, 32, 35, 47, 48, 52 (**18**) | 55 facts, each once: S1–S4, S7, S11–S13 · TRM, SHO · Q1–Q4, Q6, Q7 · B1, B2, B3 · S9 · H1, H2 · RE3, WP1–WP3, WP5–WP7, RC1, RC2 · H4, RS · AR3, AR4, AR7, AR8 · AR1, AR2, AR5, AR6, H10 · H12 · WP12 · RP1 · RP5, RP6 · RP9 · W1, W1-EX · RO1, RO3 · WP4, RL, EE2. **PASS** (W1-EX added at 14504's return; MD-47 is the only EA-1 site in `WriteForExportedMonthsAsync`. B3 added at 14501's return; MD-11 is the only EA-1 site in the backfill, and B3's conditions keep MD-01 and MD-06 off it) | STAMP-E, SQL-SET, Q-DELTA, BF-MARK, FDO, H-400, HRD-FROM, PM, WK-RAW, WK-CALC, Q7, KEY, RP-SEG, RP-DIG, RP-NULL, WL-WIDE, WL-HOOK, CUT: 18 distinct. **PASS** | relied on: MARK-SRC, EMIT-E (MD-01); STAMP-A, MARK (MD-05); Q-POS (MD-06). None is edited in EA-1. **PASS** | MD-19, MD-22, MD-25 and MD-26 edit Not-guards of MD-20 (H-400, PM, WK-RAW, WK-CALC). Every affected fact (H1, H2; H4; AR3, AR4, AR7, AR8; AR1, AR2, AR5, AR6, H10) is on that editor's own list, and MD-20 changes none of their statuses. MD-52 edits CUT and MD-31 edits RP-SEG, also Not-guards of MD-20: RL, WP4 and RP1 are on those editors' lists and show the status their rows state. **PASS** | N ×18. **PASS** |
| **EA-2** | MD-02, 03, 04, 07, 10, 15, 21, 27, 29, 34, 36, 44, 49, 51 (**14**) | 36 facts, each once: S6 · S1, S2, S3, S12 · S4 · Q7 · S8 · RP4, RC2 · RE1–RE3, H3–H5, H12–H14, SMK · AR1–AR8, H10 · WP11 · RP8 · RP2 · RL, RS · RO3 · RB. **PASS** | RD-A, RD-E, SQL-TO, Q-MIR, ZERO, BYID, H-DI-APR, AGR-WIDE, FLEX-OPEN, RP-MOD, RP-CTX, RP-CUT, WL-ROUTE, RB-SLOT: 14 distinct. **PASS** | relied on: STAMP-A, STAMP-E, MARK, EMIT-E (MD-02/03/04); Q-GATE (MD-07); MARK, RP-READ (MD-15); PM (MD-21); WL-HOOK, WL-WIDE (MD-49); RP-NULL (MD-51). None is edited in EA-2. **PASS** | MD-27 edits AGR-WIDE, a Not-guard of MD-21: AR1, AR2, AR5, AR6 and H10 are on MD-27's list and **surface as the never-silent 500, not 200/409** (stated in MD-27's row). MD-04 edits SQL-TO, a Not-guard of MD-03: S4 is on MD-04's list, with the same status. **PASS** | N ×14. **PASS** |
| **EA-3** | MD-08, 13, 16, 17, 18, 23, 33, 37, 41, 43, 45, 50 (**12**) | 18 facts, each once: Q6 · S10, PAR · S13 · S11 · Q5 · H10 · RP7 · RP3 · WP6, WP7 · WP1–WP3, RL · RS · H13, H14. **PASS** | Q-NEG, MARK-SRC, SQL-ST, SQL-AL, Q-POS + Q-GATE, PM, RP-FP, RP-MISS, EP-FRAC, CUT, RP-ENTRY, Q7: 13 ids, distinct. **PASS** | relied on: Q-DELTA (MD-08, MD-18); STAMP-E, MARK, EMIT-E (MD-16/17; MD-13 edits MARK-SRC, not MARK, and Skema-only flows mark exactly as before); RP-READ (MD-37). None is edited in EA-3. **PASS** | No Not-guard is edited in EA-3. **PASS** | N ×12. **PASS** |
| **EA-4** | MD-09, 12, 24, 38, 39, 42, 46 (**7**) | 20 facts, each once: Q6 · S5, PAR · H9, H10, X1–X4 · RC1, RC2, RP4, RS · WP5, WP9 · RE1, RE3, WP8, WP10 · H8. **PASS** | Q-GATE, EMIT-E, PM, RP-READ, EP-CFG, EP-START, HIRE: 7 distinct. **PASS** | relied on: Q-DELTA, Q-MIR (MD-09); MARK (MD-38); WK-CALC (MD-46). None is edited in EA-4. **PASS** | No Not-guard is edited in EA-4 (RP-MISS and RP-DIG sit in EA-3 and EA-1). **PASS** | N ×7. **PASS** |

*Existing facts.* Rows marked "CA-1 census" can reach an existing Docker fact that the plan cannot name today. Each such row names the kind of fact CA-1 searches for:
- EA-1: MD-06 (quota re-saves, for example a fact 14507-T re-expected) and MD-14 (partial re-saves of a full-day-only type);
- EA-2: MD-29 (PCS-level facts that assert flex with a non-zero opening balance);
- EA-3: MD-08 (quota re-saves with delta ≤ 0), MD-18 (quota re-saves with delta > 0) and MD-13 (two TIME_POST rows for one employee, date and task);
- EA-4: MD-09 (quota re-saves with delta ≤ 0), MD-12 (Skema entry re-saves through the endpoint) and MD-46 (agreement rows dated on an employment start inside a settled week, for example through the gap-insert path, `UserAgreementCodeRepository.cs:669-681`).

Within one run the kinds do not overlap: MD-08 and MD-18 act on opposite branches of one `if`, so a given re-save reaches at most one of them (R-47: in-lock the `if` is three-way; the first-booking case belongs to neither). If CA-1 finds one existing fact that two mutations of a run both reach, that is a partition finding. Every other row either restores today's behaviour, or names its existing reds (SMK; X1–X4).

**Coverage.** Every non-guard Docker pin is on at least one list above, except EE1, which MU-40 covers (R54). Guards with no MD: HDL, H6, H7, H11, AR9, AR10, RO2, RO4, FA, W1-G (W1-G added at the freeze, R-42).

**The four runs** (five since the freeze; the lists of record are in `SPRINT-145.md`).

| Run | Mutations |
|---|---|
| **EA-1** | MD-01, 05, 06, 11, 14, 19, 20, 22, 25, 26, 28, 30, 31, 32, 35, 47, 48, 52 |
| **EA-2** | MD-02, 03, 04, 07, 10, 15, 21, 27, ~~29~~ (withdrawn, R-34), 34, 36, 44, 49, 51 |
| **EA-3** | MD-08, 13, 16, 17, 18, 23, 33, 37, 41, 43, 45, 50, **56** (R-38) |
| **EA-4** | MD-09, 12, 24 (R-35 wording), 38, 39, 42, 46 |
| **EA-5** (R-40) | MD-53, 55 (R-38 body-key form), 57, 58, 59, 60, 61 |

51 Docker mutations (18 / 14 / 12 / 7, as R54–R55 target) plus MU-40 at Unit level. Against R55, MD-21 and MD-42 trade places: MD-21 stays in EA-2, and MD-42 moves from EA-2 to EA-4 (deviation D1, § "Needs a ruling" item 6).

### Fixture conditions the table relies on

A row can only be "by effect" if the facts it is checked against are written the way the row assumes. Where a brief left the deciding assertion open, it is fixed here, and each test brief pastes the conditions for its own facts.

| Id | Facts | Condition | Protects |
|---|---|---|---|
| C-A1 | TRM, SHO | Each Skema-saved date carries no raw-seeded row of the same absence type | MD-13 |
| C-A2 | TRM, SHO | The type's balance leaves at least two days of headroom. TRM: `used` 5 is seeded at `TerminationSettlementTests.cs:243`, and the VACATION cap is the full 25 through the forskud branch (`SkemaEndpoints.cs:1353-1364`). SHO: the SPECIAL_HOLIDAY quota is 5 (`SpecialHolidaySettlementTests.cs:30`), and the employee has no end date before the cell, which would clamp the cap (`SkemaEndpoints.cs:1373-1375`) (R64) | MD-06 |
| C-S1 | S1–S4, S7, S11–S13; S6 | Each saves and re-saves one entry cell and no absence cell; S6 saves and re-saves one absence cell and no entry cell | MD-02, MD-03 |
| C-S2 | S7 | Asserts the cell's current-row count by SQL on `time_entries_projection` (`superseded_by_outbox_id IS NULL`), not through a GET | MD-03 |
| C-S3 | S11, S13 | Skema saves only; no `POST /api/time-entries` | MD-13 |
| C-S4 | S9 | Asserts `error == "absence_full_day_only"` (`SkemaEndpoints.cs:985`), so a quota 422 (`"Entitlement quota exceeded"`, `:1424`) cannot stand in for it (N3) | MD-06, MD-14 |
| C-Q1 | Q1–Q7 | No planned days | MD-09 |
| C-Q2 | Q6 | The reduction leaves `used` above the cap: for example `used` = cap + 1 after the revaluation, and a full day re-saved as a half day | MD-08, MD-09 |
| C-RE1 | RE2 | Asserts no merarbejde line and no flex payout; not NORMAL_HOURS | MD-42 |
| C-RE2 | RE3 | Also asserts that every NORMAL_HOURS line is dated in October (the window contract at host level, R6) | MD-42 |
| C-RE3 | RE1, RE2 | Every weekday carries exactly the day norm, 31 August included | MD-20 |
| C-H1 | AR1, AR2, AR5, AR6, AR10; H9, H10; X1–X4 | The AR facts seed M−1 APPROVED (R26's host-fact rule). H9 and H10 leave M−1 unapproved by design. X1–X4 stay unseeded (14512-T) | MD-21, MD-23, MD-24 |
| C-H2 | H3, H5 | September has no approval row (or a status other than APPROVED and EMPLOYEE_APPROVED) | MD-22 |
| C-H3 | AR9 | The request body carries AC, the agreement in force on 1 June | MD-30 |
| C-W1 | WP1–WP7, WP9; WP11 | WP1–WP7 and WP9 assert overtime and merarbejde lines (amount, date, key), `NormPeriodWeeks`, fingerprints and refusals. WP11 asserts flex payouts and balances. None asserts NORMAL_HOURS lines or the norm total | MD-42 |
| C-W2 | WP11 | Every context day (31 August; 28–30 September) carries exactly the day norm. *Not "or no hours" (N2's alternative):* under MD-42 a covered context day becomes an in-period day, and with no hours it would subtract its norm from the flex delta (contradiction 19) | MD-42 |
| C-W3 | WP8, WP9, WP11 | Context days carry exactly the day norm | MD-20 |
| C-W4 | WP1–WP11 | The request profile equals the dated record; only WP12's is stale | MD-30 |
| C-W5 | WP6, WP7 | The closing week (28 September – 4 October) carries exactly the day norm | MD-43 |
| C-W6 | WP9 | "No export line change" compares with its own no-position-change twin, calculated in the same fact | MD-42 |
| C-W7 | WP1–WP12 | Hours are read through the real `ProjectionHoursReader` over seeded rows, and the engine is the in-process real one | MD-20, MD-39, MD-41, MD-42 |
| C-E1 | EE1, EE2 | EE1 asserts the pieces only; EE2 the pieces and the cutoff; neither asserts the entries | MD-20, MD-43 |
| C-T1 | 14511-T's rewritten request-shape assertions | Neither the unit nor the Regression ones assert `SettlementCutoff`; EE2 is its request-level pin | MD-43, MD-52 |
| C-R1 | RC1, RC2 | RC1 registers the new entry on 29 September; RC2 re-saves 28 September from 9 h to 7.4 h (both context days of October; refinement `:668, :671`) | MD-20 |
| C-R2 | RC2, RP4 | Their rows are SKEMA rows written through `InsertAsync`, so the re-save supersedes | MD-15, MD-38 |
| C-R3 | RS; RS-P; RP1, RP2 | *(Restated at the freeze, R-33.)* RS's replay PCS has no profile resolver. RS-P's replay PCS has the throwing profile resolver and no other sentinel. RP1 and RP2 build theirs with a dated profile resolver | MD-31, MD-36 |
| C-R4 | every 14515 Docker fact | `ReplayFrozenInputsTests` builds both PCS instances with TASK-14515's **test-local echo handler** (R60), passed through `TestFixtures.BuildPcs`'s optional handler (`:246-272`). On `/api/rules/evaluate-timeline` it encodes every entry (context days included), each piece's fraction and the cutoff (null distinctly). On `/api/rules/evaluate` it echoes the profile's fraction, agreement code and position. Comparisons are over the full `PeriodCalculationResult`. The other 14515 Docker facts keep a stub engine. None uses the real engine | MD-39, MD-41, MD-42 (kept out of reach); MD-03, 15, 20, 31, 36, 38, 43, 44, 52 (made observable) |

**Why these four runs — the rationale, not the check.** The table above is the check. These are the pairs that shape the partition, in plain words:
- MD-01/MD-03 share the re-save facts. MD-13 would undo MD-01: supersession regardless of source restores replacement (MD-01 relies on MARK-SRC).
- MD-12 (no second event at all) would hide every reader mutation: MD-01, MD-03, MD-04, MD-16 and MD-17 rely on EMIT-E.
- Of the five quota mutations, only MD-08 and MD-18 may share a run. They act on opposite branches of one `if`. Every other pair shares Q6 or Q7, or one edits a path the other relies on (Q-DELTA, Q-POS, Q-GATE, Q-MIR).
- **The prior-month family needs four runs.** MD-22, MD-23 and MD-24 all edit PM, and MD-21's reds rely on PM staying where it is. H10 is reached by MD-23, MD-24, MD-26 and MD-27. With R52's MD-22 in EA-1 beside MD-26, the only free slot for MD-21 is EA-2, beside MD-27 (D1).
- MD-20 shares facts with MD-21 and MD-42 (RE3), MD-39 (WP5), MD-41 (WP6/WP7), MD-43 (WP1–WP3), MD-15 and MD-38 (RC2, RC1).
- MD-21 shares facts with MD-42 (RE1, RE3), MD-22 (H4), MD-28 (H12) and MD-50 (H13, H14).
- The sentinel pin RS is reached by MD-22, MD-38, MD-44 and MD-45, one per run. The leaver replay RL is reached by MD-43, MD-44 and MD-52.
- MD-25/MD-26 vs MD-27 share the residual facts. MD-46 relies on the week-check call that MD-26 removes; MD-25 edits only the raw-route call, which H8 does not use.
- MD-28/MD-50 both edit Q7. MD-43/MD-52 both edit CUT. MD-49 relies on what MD-47 and MD-48 remove. MD-51 relies on the null-slot refusal that MD-35 removes.

Every evidence commit keeps Unit and DemoSeed green (fact 3; column (d)). At CA-1, TASK-14518 re-derives by effect the rows R66a names (those touched by a reported deviation, or whose cited code changed) and spot-checks two rows per run. It reports the diff, publishes the resolved lists before `K_A`, and the Fable seat freezes them. The run itself is the by-effect check of record (R66a). TASK-14518 also names, at CA-1, the expected **green** spot checks per run: the nearest facts that must stay green, each a separate fact that executes in that build.

---

## Step 5α / 5a — who reviews what (every row is a merge precondition)

| Task(s) | 5α | 5a Reviewer (Fable) | 5a Codex (high-risk category) |
|---|---|---|---|
| 14500 | ✓ | ✓ | ✓ schema migration |
| 14501, 14502, 14503a, 14503b, 14504 | ✓ | ✓ (one spawn, five verdict lines) | — |
| 14505 + 14505-T | ✓ | ✓ | ✓ legal rule logic |
| 14506 | ✓ | ✓ | — |
| 14507 (+T) | ✓ | ✓ | ✓ entitlement quotas + schema use |
| 14508 + T, 14511 + T, 14512 + T, 14513 + T | ✓ each | ✓ each | ✓ each — payroll export / retroactive corrections |
| 14509 | ✓ | ✓ | ✓ retroactive-correction paths (worklist, reopen) |
| 14510 | ✓ | **SKIP** — pure UI label, no backend change (`docs/AGENTS.md:353`); rationale recorded | — |
| 14514, 14515 | ✓ | ✓ | — |
| 14518 (evidence) | — (no master change) | the Fable seat compares the runs (CA-5) | — |
| Stage B: 14520 + T · 14521 + T · 14522 + T · 14523 · 14524 (the paired passes on their parents' combined branches, R-65) | ✓ each | ✓ (14523: SKIP, rationale) | 14520 ✓ rule logic · 14521 ✓ corrections · 14522 ✓ payroll export |
| Every fix-up | ✓ | ✓ | as its parent |

---

## Gate A-CLOSE — "Stage A closed" (CA-1..CA-7)

*Plain language:* Stage A is closed when it is all merged, every task has passed its two checks, the whole Stage A suite is green in a real CI run, and every deliberate breakage turned exactly its pre-written list of tests red. Only then is Stage B briefed.

1. **CA-1 — before `K_A`.** TASK-14518 does four things:
   - resolves the EA-1..EA-4 lists to `FullyQualifiedName`s;
   - runs the coverage matrix: every non-guard Docker pin has a mutation, Docker or (with a recorded ruling) Unit; every mutation can reach its assertion;
   - **re-derives by effect the R59 rows that can have moved** (R66a: those touched by a reported deviation, or whose cited code changed), spot-checks two rows per run, reports the diff, and recomputes each run's five check lines. These are shared facts; edited paths, which is **the same code path**, R44; hard relied-on paths, which cover removed, moved or bypassed code and masking; soft Not-guards with their surfaced status (R63); and Unit/DemoSeed reach. The runs (CA-5) are the by-effect check of record;
   - dry-runs each set on the non-Docker suites. The resolved lists are published in `SPRINT-145.md` before `K_A` is committed (R45). **Fable seat:** freezes them on the resolved names. Any later change is a Fable ruling.
2. **CA-2.** The Orchestrator checks:
   - the 5α/5a column of every Stage A row (14500–14515, every paired pass, every fix-up) is filled;
   - O-8's stop-point check is recorded (R17): `RuleRegistryClassificationTests` and every A8 refusal pin unchanged in the diff and green, the planner's split refusal and the handed-in set unchanged in behaviour.

   Then it commits the Stage A record (`SPRINT-145.md` § "Stage A gate", results as marked placeholders) as **`K_A`**, with message `S145 Stage A: …` and without the phrase "sprint close" (`sprint-close-guard.ps1:80-92`). Not pushed to master.
3. **CA-3.** TASK-14518 re-spawn builds EA-1..EA-4 from `K_A` and reports shas and diffs.
4. **CA-4 — V-A.** `git push origin <K_A>:refs/heads/s145-evidence-a`. Open a **draft** PR to master whose first body line is "EVIDENCE ONLY — S145 Stage A verification and deliberate mutations; never merge". Watch one background command to completion and confirm `headSha == K_A`. **All seven jobs must be green.** That run is "the Stage-A criteria green in CI".
   - If red: a Stage A fix task (through the merge checklist), giving a new `K_A′` that descends from `K_A`. Push it as a fast-forward, repeat V-A, and re-make the EA chain from `K_A′` (CA-3).
5. **CA-5.** Fast-forward the branch to EA-1, watch, confirm `headSha` and that the PR base is an ancestor of the head (a `pull_request` run tests the head merged into the base, R-52). **Fable seat:** compare with the frozen list: exactly that list red, nothing else, and the green spot checks green. Then EA-2, EA-3, EA-4 (and EA-5, R-40) in turn. *(R-49:)* a job the run's diff cannot reach is outside the run's evidence; a second failure on the same tree stops the count.
   - A mismatch is never interpreted: TASK-14518 adds a corrected commit on the tip (tree = `K_A` + corrected set) and the run is repeated (S144 R9).
6. **CA-6.** Close the PR unmerged. `git push origin --delete s145-evidence-a`; `git branch -D s145-evidence-a`; confirm the branch is gone on both sides. If `claude-code-review.yml` fired, its output is ignored and said to be ignored.
7. **CA-7.** A docs-only commit on master records V-A's run id, each EA run's id, head sha and red set against its list ("matched"), the PR number ("closed unmerged, branch deleted"), and the handoff block. **Stage A is closed.** `K_A` is the Stage B base and the revert target (R33).

---

## Stage B — genuine splits (QUAL-149), briefed only after Gate A-CLOSE

*Plain language:* with week-by-week evaluation in place, a month split by a change can be evaluated as one plan, and each line is mapped under the codes of the part of the month it falls in. The full briefs are written at B.0 against `K_A`, because Stage A moves most of the line numbers. What follows is their outline, files and criteria, which Step 0b reviews now.

| Task | Wave | Role | What (refinement) | Files (at `62fcdc3`; re-verified at B.0) | Size |
|---|---|---|---|---|---|
| **[TASK-14520](s145/TASK-14520.md)** | B.1 | `rule-engine` — **`Rule Engine (extended into SharedKernel/Segmentation, cross-domain authorized)`** | B1: `TimelineAware` → `SinglePlanEvaluation` (the merger passes one result through and throws on two); `NORM_CHECK_37H` registered; rest rules keep `AlignedWindow` | `src/SharedKernel/StatsTid.SharedKernel/Segmentation/SplitBehavior.cs`, `…/MergeStrategy.cs` (`MergeStrategyKind` `:66-72`), `…/CalculationResultMerger.cs`; `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RuleRegistry.cs` (`:53-68`, `ResolveDefault` `:148-157`) | M |
| **[TASK-14521](s145/TASK-14521.md)** | B.1 | `backend-infrastructure` (Opus) — **`Backend + Infrastructure (cross-domain authorized)`** (R50) | B3: worklist derivation on `WeeklyContext.MidWeekAgreementChange` under the new residual register id; the server 409 and the Global-Admin gate stay (QUAL-165) | `src/Infrastructure/StatsTid.Infrastructure/HrBackdateWorklistRepository.cs` (`:241-262`, the lock snapshot `:1087`), `src/Backend/StatsTid.Backend.Api/Endpoints/BackdateWorklistEndpoints.cs` (`:247-260, :343-358`) | M |
| **[TASK-14522](s145/TASK-14522.md)** | B.2 | `payroll-integration` — **`(extended into SharedKernel/Segmentation, cross-domain authorized)`** for comments only | B2: payroll hands the planner only the classifications of the rules it runs; the per-plan lane spans every EMPLOYED segment; lines are mapped by line date → the containing segment's dated key; the Q5 predicate also catches interior changes. B3 raw routes: Q6 (a) refusal of ≥ 2 EMPLOYED segments by a separate reason | `…/Payroll/Services/PeriodCalculationService.cs`, `…/Payroll/Program.cs` (`:199-203, :248-253, :577-610`), `…/Services/PayrollPlanRefusalProblem.cs`; comments `PeriodPlanner.cs:312-318`, `BoundaryCause.cs:98-105` | L |
| **[TASK-14522-T](s145/TASK-14522-T.md)** | B.2 (paired) | `test-qa` | Flip the refusal pins named by the Stage B criteria: `EmploymentWindowLiveRulesetTests.cs:207`; `RecalcBlockedLiveRulesetTests.cs:108, :159, :206`; `PayrollHostRecalcBlockedTests.cs:151, :203` (they now seed February APPROVED), `:263, :300` (the single-profile body, R10); `:339` and `RecalcBlockedLiveRulesetTests.cs:246` unchanged | those files | M |
| **[TASK-14523](s145/TASK-14523.md)** | B.3 | `ux` | B4: the badge renders only for the residual | `frontend/src/pages/admin/opfoelgning/WorklistList.tsx` (`:47-48, :321, :391`), `frontend/src/hooks/useHrBackdateWorklist.ts`, `…/__tests__/WorklistList.test.tsx` | S |
| **[TASK-14524](s145/TASK-14524.md)** | authored B.1, merged B.3 | `test-qa` | The Stage B criteria (refinement `:683-712`): classification; the worked split week (2.67/4.53, AC 7.20); the positive real-engine split month; QUAL-150 end to end (SLS_0210 → SLS_0310, `danish-agreements.md:145-147`); the interior residual; one predicate, three consumers; raw routes Q6 (a) incl. a fraction-only fact; worklist; flex discriminating facts; planner contract kept | new `tests/StatsTid.Tests.Regression/Payroll/SplitMonthPayrollTests.cs`; new `tests/StatsTid.Tests.Unit/Segmentation/SinglePlanEvaluationMergerTests.cs`; new `tests/StatsTid.Tests.Unit/Rules/RuleRegistryStageBClassificationTests.cs` (R50; rename only by reporting at B.0); `BackdateWorklistDerivationTests`, `HrBackdateWorklistRepositoryTests`, `BackdateWorklistEndpointTests` (new facts at the end) | L |
| **[TASK-14528](s145/TASK-14528.md)** | close | `test-qa` | C-1 (resolve the lists, dry-run and freeze the patches) and C-5 (build the runs on the pushed close sha) for Stage B, plus the R39 re-observation set (R-53) | throwaway branch `s145-evidence-b` | M |
| **[TASK-14520-T](s145/TASK-14520-T.md)** (accepted at B.0, R-65) | B.1 (paired) | `test-qa` on 14520's branch | Re-expect the shipped classification pins and the live-set planner refusals that now name `NORM_CHECK_WEEKLY`, labelled `// S145 B1:`; `EmploymentWindowLiveRulesetTests.cs:253` included (R-61) | the files in its brief | S |
| **[TASK-14521-T](s145/TASK-14521-T.md)** (accepted at B.0, R-65) | B.1 (paired) | `test-qa` on 14521's branch | Re-seed and re-expect the worklist block pins on the residual (QUAL-209), labelled `// S145 B3:` | the files in its brief | M |

*The nine briefs govern; the rows above are the outline (the B.0 file lists, as ruled at R-53..R-79, are in each brief).*

**Stage B mutations (fixed at B.0, ruled at R-54/R-56/R-62/R-72; frozen with their patches at C-1). Full table and census: TASK-14528.**
- *Docker (ride the EB runs):*
  - **MB-2:** each later part's wage-type key dated at the first EMPLOYED part's start.
  - **MB-4:** the week check ignores agreement-change dates inside the month.
  - **MB-5:** the raw routes skip the Q6 (a) reason.
  - **MB-7:** the evaluated-set filter removed (the planner sees the full rule list again).
  - **MB-13 (R-54):** on replay, the timeline is handed only the frozen pieces that start on or before the first EMPLOYED part's start. Reds: RP-B1 and the two existing OK-straddle replays SR1, SR2 (R-80).
- *Unit (fact 3; TASK-14524 re-spawn 2; R-72 rules the ones with Docker pins resting on them):* **MB-1** (the merger passes two results through), **MB-3** (the per-plan lane covers only the first EMPLOYED part; FX3 rests on it, R-62), **MB-6 / MB-6r / MB-6p** (the worklist derivation), **MB-8** (the merge handed the per-segment rows again), **MB-9** (the converter arm), **MB-10a/b/c** (the registry), **MB-11** (the Q6 (a) body), **MB-12** (R-56: a line dated in no EMPLOYED part falls back silently instead of refusing).

**Runs (partitioned by the same rule as the EA runs):** EB-1 = {MB-2, MB-4, MB-5, MB-13}, **14 facts** (R-80: MB-13 also reaches the existing straddle replays SR1, SR2) · EB-2 = {MB-7}, 22 facts · EA-R1 = {MD-25, MD-30} · EA-R2 = {MD-24} (R-70; MD-55/MD-56 accepted by inspection). Four CI runs at the close.

**If Stage B does not converge** — a Stage B task reaches 5a cycle 5 with an open BLOCKER, or an EB run cannot match after repeat — it is a halt-and-prompt to the owner. The ruled default (Q1) is "Stage A ships alone":
- the Stage B landings are reverted newest first, by plain `git revert <sha>`, or `git revert -m 1 <sha>` for an actual merge commit. The contract (R33, as read after CA-7): `git diff K_A HEAD -- src tests frontend docker tools` lists only QUAL-207's two files (`frontend/src/pages/admin/enhedsspor/PersonDrawer.tsx` and its `__tests__/PersonDrawer.scheduledChange.test.tsx`; `SPRINT-145.md` § "Stage A gate — `K_A`", "After CA-7"), and a revert must not take QUAL-207 with it;
- the close proceeds on Stage A;
- the B.0 register rows are reconciled in the same docs commit (R-84): **QUAL-209** and **QUAL-211** are closed as WITHDRAWN ('S145 Stage B reverted; never shipped'). **QUAL-210** is re-worded to the Stage A worklist drift and is A8's row: the derivation's month-interior test disagrees with payroll's week check on a weekday-1st or context-day agreement change (refinement A8), and it has no employment window (the hire and leaver cases).
- **If the halt comes after C-4's green push** (an EB run that cannot match after repeat), the owner's fallback ruling replaces the close of record (R-100). The evidence PR is closed unmerged and the branch deleted, as at C-8. The reverts then go through the close sequence again in its Stage-A-only form (§ "If Stage A ships alone"): C-1 freezes only the R39 set; C-2 makes a new K on top of the reverts; C-3 reviews the reverts and the reconciliation above; C-4 makes a new close commit and its green push, which is the new close of record. C-4's no-push rule runs from that push. C-5..C-8 run only if the R39 set is non-empty.

---

## Close sequence — checkpoints C-1..C-9 (Orchestrator on Opus; Fable-seat judgments marked)

*Plain language (R-53):* the deliberate breakages are decided and saved as patch files before the whole-sprint review, the review reads those patches, the close commit is pushed and watched green, and only then are the throwaway evidence commits built on top of that pushed commit. Building them last is what makes master their ancestor, which is how CI tests a pull request (R-52). *(R-83:)* a saved patch is valid only for the exact code it was cut from. So every new K is checked against that code, and the patches are re-cut if anything changed. If the close run comes back red, the fix goes back through K and the review; the "never push master" rule starts at the push that comes back green. *(R-97:)* if the fix is in the code, it is pushed and must come back green before a new close commit is made, because the close guard refuses a close commit while master's newest finished run is red.

**The code set (R-94).** Every tracked path except `docs/`, `.claude/` and the three root-level Markdown files (`CLAUDE.md`, `ROADMAP.md`, `SYSTEM_TARGET.md`), as a pathspec run at the repository root (or with `git -C <root>`): `-- . ':(exclude)docs' ':(exclude).claude' ':(exclude,glob)*.md'`. It includes everything that can change a patch, what a mutation reaches, or what CI runs and in what order: `src`, `tests`, `frontend`, `docker`, `tools`, `.github`, `StatsTid.sln`, `Directory.Build.props`, `global.json`, `.editorconfig`, `coverlet.runsettings`. It is the one path set of C-2's validity check, C-3's fix routing, C-4's (b)/(c) split and C-5's confirmations. *(The fallback's revert contract, R33, keeps its own list: it checks whole-commit reverts against `K_A`, a different question.)*

1. **C-1 (B.3 gate): resolve and freeze.** TASK-14518's successor, TASK-14528, resolves the EB lists and the R39 set.
   - It dry-runs each run's set in a detached worktree at master (build plus non-Docker suites green), and **records that master sha as the patch base `B`**.
   - It saves each run's applied set **as raw bytes**: `git -C <wt> diff --no-color --no-ext-diff --full-index --default-prefix --output=<abs path>`, never a PowerShell redirect. The files are `.claude/plans/s145/cb1/mut/EB-1.diff`, `EB-2.diff`, `EA-R1.diff`, `EA-R2.diff`. `--full-index` writes whole object ids, so a patch's bytes do not change when Git lengthens its short ids (7 to 8 characters at 16,384 packed objects); `--default-prefix` keeps `a/` and `b/` whatever the local diff settings (R-93).
   - It round-trips each patch once, as TASK-14528 C-1 step 4 says.
   - **Seat:** freezes the lists, the patches and `B`; the Orchestrator records each patch's SHA-256 (`sha256sum`, Git Bash) in `SPRINT-145.md` beside `B` (R-98). Any later change to the lists or to a patch's bytes is a seat ruling; `B` moves only as C-2 says (R-98).
2. **C-2: K, then patch validity.**
   - The O-4 docs are written, with the Step-7a outcome and the evidence results as marked placeholders.
   - The 5α/5a column is re-checked for every task.
   - Commit **K**; it is not pushed (the one exception: C-4 (c) step 2).
   - **Then the patch-validity check (R-83, R-94):** `git diff --name-only <B> <K> -- . ':(exclude)docs' ':(exclude).claude' ':(exclude,glob)*.md'`.
     - Empty: the frozen patches stand.
     - Not empty: TASK-14528 redoes C-1 step 4 on K for all four runs: the dry run; a raw-byte re-cut saved to `.claude/plans/s145/cb1/mut/<K12>/<run>.diff` (`<K12>` is K's first 12 hex digits), never over the frozen file; the round trip (R-98). C-1 steps 2–3 are redone first:
       - for the affected rows, when a changed file holds a mutation's site method, an edited or relied-on path of any row, or a fact on any list or expected-green list;
       - for every Docker row (EB and EA-R), when the code-set diff adds or changes a Docker-gated fact: each such fact enters step 2's census (R-96).
     - **The outcome (R-98):**
       - every dry run green, every round trip exiting 0, every re-cut byte-identical to its frozen patch (`cmp`), and no list, expected-green list or check line changed: the freeze stands and `B` becomes K. The Orchestrator records both in the sprint log; no seat ruling is needed.
       - anything else: the seat rules on the difference (`git diff --no-index <frozen> <re-cut>` per changed patch, and each list change). On a re-freeze the Orchestrator keeps the superseded file as `cb1/mut/<old B12>/<run>.diff` (`mkdir -p` first), copies the re-cut over `cb1/mut/<run>.diff` (Git Bash `cp`, never a PowerShell redirect), records the SHA-256 of every patch in `cb1/mut/` in the sprint log beside the ruling (`sha256sum <abs>/.claude/plans/s145/cb1/mut/*.diff`, Git Bash; the latest freeze then lists every frozen patch, not only the re-frozen ones), and `B` becomes K.
       - Either way, these records are made in the working tree after K and are never committed on their own. They ride in the close commit (C-4) or, when a C-3 fix comes first, in the new K that fix leads to. So K stays the close commit's parent, as the guard requires (`sprint-close-guard.ps1:180-184`, `:273`).
3. **C-3: O-5, Step 7a.**
   - Both lenses, base `cbaa5e0`, `reviewed-against-commit: <K>`.
   - Carried item 6 reviews the frozen patches, valid for this K (C-2), not evidence commits. Before the lenses are dispatched, the Orchestrator checks each patch's SHA-256 against the sprint log's latest freeze; a mismatch is a halt for the seat (R-98).
   - A fix that changes any path in the code set goes through the merge checklist (5α; 5a as its parent task). Any other fix (under `docs/` or `.claude/`, or a root-level `*.md`) needs no merge checklist. Either way the fix makes a new K and the sequence repeats from C-2, because the close guard requires the Step 7a artifacts to name the close commit's parent. The repeated C-3 is scoped to the fix and to every patch and list re-frozen at that C-2 (R-94, R-98).
4. **C-4: the close commit and O-6.**
   - **Commit messages (R-97).** The close guard (`.claude/hooks/sprint-close-guard.ps1:75-111`) checks every `git commit` command whose text holds the words "sprint close" (any case) and an `S<N>` token.
     - The close commit's command carries "S145 … sprint close" inline (`-m` or a heredoc, not `-F <file>`), so the guard runs on it.
     - No other commit from C-2 to C-9 (a K, a fix, a (b) commit, an evidence commit, C-9) has the words "sprint close" anywhere in its command, subject or body; the guard would treat it as a close commit: it refuses it, or passes it as the close when its parent is the reviewed K.
   - The close commit is a docs-only child of K: `git diff --name-only K HEAD` lists only paths under `docs/`. The guard then requires both Step 7a artifacts to name K (`reviewed-against-commit:`) and master's newest completed push run not to be red. It also refuses while any worktree beyond the main checkout stands, while an untracked file sits under `src`, `tests`, `tools` or `frontend`, and while a TASK id that `SPRINT-145.md` names anywhere lacks DONE, CUT, DEFERRED or DROPPED on a line of its Task ledger (`sprint-close-guard.ps1:438-662`). The ledger check reads `TASK-14503a` and `TASK-14503b` as `TASK-14503`, so the ledger also needs a line naming `TASK-14503` with no letter after it, carrying a disposition. Before every close commit, including (c) step 3's and R-100's, the Orchestrator removes every worktree beyond the main checkout (`git worktree list`, then the guard's own remediation, `sprint-close-guard.ps1:543-549`), confirms that nothing is untracked under those four roots, and checks the ledger.
   - Push master and watch the push run to completion (one background command; confirm `headSha` is the pushed sha).
   - **If red:** diagnose before changing anything, and record the failing job, step and first failing line in the sprint log. Then take exactly one branch:
     - (a) *The failing step is not a build or test step* (checkout, toolchain setup, service start, upload), with the log line quoted. Re-run the failed jobs once on the same sha (`gh run rerun <id> --failed`). Green continues below. A second red is diagnosed again and goes to (b), (c) or (d); there is no second re-run.
     - (b) *A docs-only fix:* one commit on top that changes only paths under `docs/`. Push it and watch; a red is handled by "If red" again. K and `B` are unchanged.
     - (c) *A fix in the code set.* The fix and its green run come first and the new close commit last, because the guard refuses a close commit while master's newest completed push run is red (`sprint-close-guard.ps1:322-341`):
       1. Make the fix through C-3's fix path (the merge checklist) on top of master's tip; then C-2 (a new K on top of the fix, with its patch-validity check) and C-3, scoped as C-3 says (the fix, and every patch and list re-frozen at that C-2).
       2. Push master up to the new K and watch that run to completion. It is not the close of record. If it is red, diagnose it as above: (a) re-runs it once; a fix makes a newer K through C-2 and C-3 (the merge checklist only for a code-set fix) and this step repeats; anything else is (d).
       3. Once it is green, make the new close commit, a docs-only child of the new K; push it and watch; a red is handled by "If red" again.
     - (d) *Anything else:* a build or test step that fails for an infrastructure reason (for example Testcontainers failing to start inside "Run regression tests"), a red whose cause is not found (such as a flaky test), or a fix that is neither docs-only nor in the code set. Halt for a seat ruling; nothing is pushed until it.
   - **The green push is the close of record (R-97):** the first green run whose head is the close commit, or a (b) commit above it; a re-run under (a) counts.
     - Its run is "the Stage-B criteria green in CI".
     - Its head sha is the **close sha**.
     - The last K committed at C-2 is the **close's K**: `git diff --name-only <close's K> <close>` lists only paths under `docs/`.
     - The green run of (c) step 2, whose head is a K, is never the close of record.
   - **From the green push until C-9, never push `origin/master`**, except as § "If Stage B does not converge" says for an owner's fallback ruling (R-100). Before the green push, master is pushed only by C-4's close push and the pushes of (b) and (c).
5. **C-5: build the chain.** The TASK-14528 re-spawn builds EB-1 → EB-2 → EA-R1 → EA-R2 on the **close sha** (= `origin/master`).
   - **First (R-98):** `sha256sum <abs>/.claude/plans/s145/cb1/mut/*.diff` (Git Bash) matches the latest freeze's values in `SPRINT-145.md`. A mismatch is a halt for the seat; a patch is never re-saved to match.
   - Each commit is its parent's tree, with the previous patch reverted and its own frozen patch applied.
   - **The check of record:** `git diff --no-color --no-ext-diff --full-index --default-prefix <close> <EB-k> --output=<tmp>`, then `cmp <tmp> <abs>/.claude/plans/s145/cb1/mut/<run>.diff` exits 0 (Git Bash; `<abs>` is the main checkout's root; R-93).
   - Build and the non-Docker suites are green at each commit. Shas and diffs are reported verbatim.
   - Confirm that `git diff --name-only <close's K> <close> -- . ':(exclude)docs' ':(exclude).claude' ':(exclude,glob)*.md'` is empty, that `git diff --name-only <B> <close> -- . ':(exclude)docs' ':(exclude).claude' ':(exclude,glob)*.md'` is empty (the code set, R-94), and that `git merge-base --is-ancestor origin/master <EB-1>` holds (R-52).
   - On a repeat, remove the unpublished branch first.
6. **C-6: EB-1.** Push to `s145-evidence-b` with a draft PR ("EVIDENCE ONLY …"). Watch, confirm `headSha`, and confirm the PR base is an ancestor. **Fable seat:** compare.
7. **C-7: EB-2.., EA-R.** Fast-forwards, each compared.
8. **C-8.** Close unmerged; delete the branch on both sides.
9. **C-9 — docs-only follow-up commit (seals the close).** The CI-green line; per run its id, head sha, red set and "matched"; the PR number ("closed unmerged, branch deleted"); the O-7 routing row. Its message does not contain the words "sprint close" (R-97). Push; watch green.

*(EA-R2 is ruled in, R-70: the chain is EB-1 → EB-2 → EA-R1 → EA-R2, four CI runs.)*

**If Stage A ships alone:** C-1 freezes only the R39 set (normally empty, because the reverts restore `K_A`'s code); C-5..C-8 run only if the R39 set is non-empty. Before that C-1, the Orchestrator moves every patch already in `cb1/mut/` (the Stage B freeze; none if C-1 never ran) to `cb1/mut/stage-b/` (Git Bash `mkdir -p`, then `mv`). So no frozen file is overwritten, and `sha256sum <abs>/.claude/plans/s145/cb1/mut/*.diff` at C-3 and C-5 lists only the new freeze's files (R-98).

---

## Step 7a — carried checklist

1. `6b0d982` changes `.claude/hooks/sprint-close-guard.ps1` (the CI-health query now lists ten push runs and takes the newest completed one client-side, `:322`) and has never been reviewed on its own (`SPRINT-145.md` header). Step 7a reviews it and its harness.
2. `8a87d16` (tests only), already reviewed post-close. Its W, the false `TestFixtures.cs:406-407` comment, is fixed in TASK-14501: verify.
3. S144's N-c4-1, N-c4-2 and N-c4-4: fixed in TASK-14503b under R38 (verify), or re-carried if R38 is struck.
4. The docs commits `018ef2a`, `6849b84`, `de6c228`, `62fcdc3`, `299c3dd`: the S145 record does not contradict them.
5. Every merged task has its 5α and 5a record in `SPRINT-145.md` (the S144 miss).
6. The frozen EA/EB lists, the EA diffs against `K_A`, the **frozen patches of all four close runs, `cb1/mut/EB-1.diff`, `EB-2.diff`, `EA-R1.diff` and `EA-R2.diff`, valid for K (C-2) and applied unchanged at C-5**, the three Unit mutation tables (14506, 14514, 14515) and 14524's re-spawn-2 table.
7. **The labelled value changes (R27; R-81 replaces R-65's list).** In each paired pass's diff, every changed **existing** expected value carries a label from **that pass's row**, at a site its ruling names. A changed value with no label, or with a label outside its pass's row, is a **finding**: never an approval, and never closed by relabelling. `S145` comments that mark no changed value are outside this check: notes on migrated helpers (`TASK-14505-T`), notes on new facts, added assertions (`Step 5a`) and reworded comments. So is a **relocated assertion**: its expected value is unchanged, and its actual value is read from where the code now carries it. The one in the record is 14513-T's `R14` move at `BoundaryCauseEncodingTests.cs:231-232` (`replay.ManifestId` → each rule result's `ManifestId`, expected `plan.ManifestId` both times). Whether a move is as strong as the line it replaced (an `Assert.All` passes on an empty list) is ordinary Step 7a review, not item 7. **The label is the ids it names** (`R14`, `R-6`, `F1`, `B2`). Other text in it, such as a parenthetical (`(adjudicator)`, `(guard)`) or an owner-question option (`Q7 (b)`), qualifies the label and adds none. In a composite such as `R14 / F1`, each id must be in the pass's row (R-101). The standing-constraints block (`:264`) instructs the Stage B paired agents and is unchanged; this table is the Step 7a check.

| Paired pass | Labels authorized on a changed expected value | Recorded at |
|---|---|---|
| 14505-T | `R8` (TASK-14505 Do 6; one change materialized: change 4, `OkVersionRuntimeRegressionTests.cs:318`); `R9` (R35 rounding; none expected, none present) | TASK-14505-T Do; sprint log `:92`, `:594` |
| 14508-T | `R14` (the record shows no expected value changed) | sprint log `:856` |
| 14509-T | `R5(a)` (W1-EX) | TASK-14509-T Do; sprint log `:773`, `:779` |
| 14511-T | `R14` (call counts); `F1` (the rewritten request shapes; `R14 / F1` where both apply); `R-6` (the two re-messaged asserts); `R-10` (the position fixture and the marquee comments) | TASK-14511-T; sprint log `:1027`, `:1039-1050`, `:1078` |
| 14512-T | `R26` (construction; no assertion value changed); `R37` (H12–H14 tightened to the as-built Q7 names) | TASK-14512-T; sprint log `:1136`, `:1148-1158` |
| 14513-T | `R14` (the three `BoundaryCauseEncodingTests` refusal names); `R-11`, `R-12`, `R-13`, `R-17`, `R-18`, `R-19`, `R-22`, `R-23`, `R-24`, each only at the site its ruling names | TASK-14513-T; sprint log `:1298-1300`, `:1334-1378` |
| 14519-T | `R-20` (including the `ProfileBoundaryHydrationTests` variant-A fixture); `R-26`, `R-28`, `R-29`, `R-30`, each only at the site its ruling names | TASK-14519-T; sprint log `:1442-1461` |
| 14507-T | not run: no existing test pinned the double count | sprint log `:550` |
| 14520-T · 14521-T · 14522-T | `B1` · `B3` · `B2` | R-65; standing constraints |

*Outside item 7:* value changes that non-paired test tasks made under their own briefs were authorized there and reviewed at their 5a, and item 7 does not re-judge them. Examples: 14501's R57 termination leg (`TerminationSettlementTests.cs:301-303`) and 14503b's R38 wording.

8. The A0 census re-run at K (`git grep -n -E "time_entries_projection|absences_projection|TimeEntryProjectionRepository|AbsenceProjectionRepository" -- src`): every reader filters current rows, and `GetByEventIdsAsync` is the only unfiltered read. On the payroll side, **`ProjectionHoursReader` is the only type that touches the two repositories** (R25). CI now holds this through the Unit guard `OnlyProjectionHoursReader_NamesTheProjectionRepositories_UnderIntegrations` (N3). Step 7a confirms the guard is green and that its allowlist names only `ProjectionHoursReader.cs`.
9. The ADR amendments match the code. The stop-point evidence (O-8). If Stage A ships alone, the QUAL-209/210/211 reconciliation (R-84).
10. **Added at Step 5a of 14507 (Fable cycle-3 NOTE 3).** The three-phase quota pre-check is meant to preserve the multi-type refusal order, but no test pins that order. Decide whether a pin is owed. The case: one save over two entitlement types, where an earlier type's rule-engine refusal must answer before a later type's profile-missing 422.
    - Accepted: on a database fault, a 500 can now answer first (Codex cycle-3 W). The fault can come from a later type's Phase 1 read or from any Phase 2 read.
    - Not taken: Fable's optional filter that would skip reading a refused type's balance in Phase 2.

---

## Cut order (if the sprint must shrink)

1. **R30** reader-matrix pins. If cut, the allocation breakdown, the balance summary and the strand check are recorded as known unpinned readers.
2. **R31** extra legs, each recorded as a known unpinned boundary if cut.
3. **Stage B as a whole.** This is the owner's ruled fallback (Q1), triggered by the review cap, not by the Orchestrator's preference.

**Never:** any A0–A8 item, any of R1–R24, the 5α/5a gates, V-A, or the EA/EB runs. Anything else goes to the owner through the known-accepted-hole mechanism.

---

## Acceptance criteria → verifying test → task

| Refinement criterion (rev 3.4) | Verified by | Task |
|---|---|---|
| A0 re-save replaces the cell `:557-564` | `Resave_Entry_*` (5), `Resave_Absence_*` | 14502 |
| A0 retry `:565` | `Retry_IdenticalPayload_LeavesOneCurrentRow` | 14502 |
| A0 quota `:566-572` (+ W1) | `SkemaQuotaResaveTests` (7) | 14502 |
| A0 settlement `:573` (+ N3) | `Termination_PostEndDateBooking_DoesNotConsume` (extended), `SpecialHoliday_Resaved…` | 14501 |
| A0 ≤ 0 refused; full-day-only `:574` (+ wording) | `Resave_ZeroOrNegativeHours_…`, `Resave_FullDayOnlyAbsence_…` | 14502 |
| A0 backfill + parity `:575-579` | `Backfill_…` (2), `Parity_…` (guard) | 14501 |
| Harness copies match `init.sql` (R51; S144 C-5 class) | `HarnessDdl_HoursTablesAndManifests_MatchInitSqlSchema` (guard) | 14501 |
| A0 additive `:580` | `TimePost_TwoRegistrations…` | 14502 |
| Defect pinned, then fixed `:581` | `MonthAsWeekCharacterizationTests` (RED observed on 14503a's branch; merged green by 14506, R49), `Core_September2026_…`, `CalculateAndExport_September_*_RealEngine` | 14503a, 14506 |
| Weekly judging `:582` | `Core_StatedTail_…` | 14506 |
| Closing week / tail `:583-587` | `Core_ClosingWeek_…`, `Core_TailCrossing_…`, `TwoMonths_*` | 14506, 14514 |
| Payroll reads its own hours `:588-595` | `CalculateAndExport_October_HoursFromProjections…`, `…BodyCarriesEntry_Returns400…` (2) | 14503a, 14503b |
| No duplicate, no gap `:596` | `AugustToOctober_NoDuplicateNoGap…` | 14514 |
| Employment edges `:597-601` | `Core_Starter_…`, `Core_Leaver_…`, `EmploymentEdges_*` (new file, R50) | 14506, 14514 |
| The leaver's frozen cutoff (R41; Domain correctness + Auditability) | `Leaver_LastDayWed30Sep_SeptemberPaysClosingWeek_Dated30Sep`, `Replay_LeaverEndDateRemovedAfterCalc_ByteIdentical_FreshCalcDiffers`; the round trip of `PlanInputs.SettlementCutoff` | 14514, 14515, 14506 |
| OK-boundary week `:602-606` | `Core_OkBoundaryWeek_…`, `OkBoundary_April2026…` | 14506, 14514 |
| Opening-week fraction change `:607` | `Core_OpeningWeekFractionChange_*`, `OpeningWeekFractionChange_*` | 14506, 14514 |
| Window contract `:608-614` | `Core_WindowContract_…`, `Core_WeekWithNormPeriodWeeksChange_…`, `Core_AnnualActivity_…`, `WindowContract_*` | 14506, 14514 |
| Flex `:615-620` | `Core_Flex_*` (3), `Flex_TwoMonths_…` | 14506, 14514 |
| One arithmetic path `:621-628` | 14505-T's migrated suites; `Legacy_Evaluate_…_2960`; `Orchestrator_WeeklyPipeline_…` | 14505-T, 14506 |
| Screen parity `:629` | `ScreenParityTests.Fraction0811_…` | 14514 |
| Dated key `:630-634` | `DatedKey_StaleBodyPosition_…`, `Plan_TwoSegments_EachCarriesOwnDatedWtmKey` | 14514 |
| Agreement residual `:635-641` | the 8 `…AgreementChange…MidWeek…` facts + 2 guards; `PayrollPlanRefusalProblemTests` variant (2) | 14503b, 14514 |
| Months in order `:642-652` (+ W3, N1) | the host 409 facts, the no-prior-approval guards (the 3 September starter seeded with a hire-dated row, R43), the order facts; `PcsNeverSilentTests` (3, plus the replay fact, R42) | 14503b, 14514 |
| Backdate reaches settling month `:653` | `WriteForExportedMonths_BackdateTue29Sep_…` | 14504 |
| Reopen flags next month `:654-658` | `Reopen_…` (2 + 2 R31c); `WorklistList.test.tsx` label | 14504, 14510 |
| Q7 (b) `:659` | `Recalculate_ExportedSeptember_OctoberExported_…` (+ 2 R31b guards) | 14503b |
| Replay `:660-665` | the 9 `Replay_*` facts (in `ReplayFrozenInputsTests`, R50); marquee re-description | 14515, 14513-T |
| Replay never runs admission (R42) | `Replay_AfterPriorMonthReopened_ByteIdentical_FreshCalcRefused409` (sentinel fakes); `PcsNeverSilentTests.NoApprovalReader_Replay_Succeeds` | 14515, 14514 |
| A rebuild keeps the frozen inputs (R47) | `Rebuild_PreservesPlanInputs_ReplaySucceeds` | 14515 |
| Only `ProjectionHoursReader` touches the hours repositories (R25, N3) | `OnlyProjectionHoursReader_NamesTheProjectionRepositories_UnderIntegrations` | 14514 |
| Reconstruction `:666-673` (+ W2) | `Reconstruction_*` (2) | 14515 |
| Lossless digest `:674` | `HoursDigestTests` (3) | 14515 |
| Stop-point gate `:675-680` | O-8 (existing suites unchanged in behaviour) | O-8 |
| Records; build and CI `:715-716` | `tools/check_docs.py`; V-A; the close CI run | O-4, O-6 |
| Stage B `:683-712` | TASK-14524 + 14522-T (+ 14520-T, 14521-T) | Stage B |
| *Criterion wording amended at B.0 (recorded in the sprint log):* `:699` narrowed to "an employee employed throughout the month's settled weeks", with the hire-dated and leaver-cutoff cases registered as QUAL-210 and pinned by two counterexample facts (R-55); `:713` "all unchanged" reads "unchanged except the refusing rule's name, which becomes `NORM_CHECK_WEEKLY` (the first `AlignedWindow` rule after B1)" (R-73); `:690`'s flex figure is observed by WS1, not SP1 (R-59) | the briefs' acceptance tables | Stage B |

---

## Records owed (O-4; Orchestrator, at A-CLOSE where marked, otherwise at the close)

- **ADRs** (`docs/knowledge-base/decisions/`):
  - **ADR-028** gains D6: Skema cells are latest-wins per cell; Time-POST rows stay additive; superseded rows are retained.
  - **ADR-016** is amended:
    - D10: the plan-level slot, verify-and-refuse, and the read-only cross-context contract on the two hours tables;
    - D10 also records the fingerprint's canonical text as a contract (Ruling 5). Transcribe it from the code's docstring `ConfigFingerprint.cs:21-38`, not from refinement `:390`, which leaves several parts out (Step 5a of 14505, N1). The parts are: the per-block header `config|{agreement}|{okVersion}|{position}`; `Name=Value` lines in ordinal order with invariant decimals; dedupe; sort; join with `\n`; SHA-256 over UTF-8; lowercase hex. An unsupported property type throws.
    - an exception recorded against `:242` (pre-S145 manifests are refused, and none exist);
    - D2/D3/D4 after Stage B.
  - **ADR-020** D1 component 5: the per-segment dated key.
  - **ADR-040**: the "Replay vs re-plan" paragraph (`:270-273`) is rewritten.
  - **ADR-002**: a note on the shared Calendar helpers (R11).
  - **ADR-034** is cited by the refinement as precedent (D4) and premise (D5) but not amended. A one-line cross-reference note in ADR-034 to S145's R5(c) and Q7 (b) is the Orchestrator's discretion, and the Fable seat **recommends** it (brief contradiction 9, as confirmed).
- **Quality register:**
  - QUAL-186 and QUAL-150 → BUILT at A-CLOSE; QUAL-149 → BUILT after Stage B;
  - QUAL-192, -193 and -194 → FIXED;
  - QUAL-187..191 stay open, unchanged;
  - new rows: the Q7 option (a) follow-up. *(QUAL-209/210/211 were registered at B.0, below; if Stage A ships alone they are reconciled as § 'If Stage B does not converge' says, R-84.)*
  - **At B.0, in one docs-only commit before TASK-14521 or TASK-14524 is dispatched (R-55, R-57, R-78):** **QUAL-209** (the residual: an agreement change inside a settled week stays manual; the id the worklist and the frontend show); **QUAL-210** (Medium, R-55: "The worklist block derivation has no employment window. It over-blocks a hire-dated non-Monday agreement row and under-blocks a leaver's last partial week. Revisit: feed it the window (cross-context), or move the derivation behind a payroll-owned query."); **QUAL-211** (Medium, R-57: "Split-month summary labels name the first part only; the full detail is reachable only through the manifest link.").
- **ADR-016 D10, the Stage B amendment (R-57):** "For a month with more than one EMPLOYED part, the result's `AgreementCode` and the correction event's `AgreementCode`/`Position` name the FIRST part (R-11/R-12, S145). The manifest's frozen pieces carry every part; readers follow `ManifestId`. Residual: QUAL-211."
- **ADR-040 `:202-207`** ("four AlignedWindow rules") is rewritten for the Stage B classification (R-63).
- **HR follow-up register:**
  - HRP-001 updated;
  - HRP-003 gains the three sentences of refinement `:449`, and its "S144" sentence (`hr-follow-up-process-register.md:100`) is rewritten (R-64): since S145 Stage B the calculating routes pay a month with a mid-month profile, category or Monday agreement change, part by part; the one manual case is an agreement change inside a settled week (the week-check 422, `reason: agreement-change-mid-week`, on all four routes; worklist id QUAL-209); the raw routes refuse every split month with `reason: split-month-single-profile-route` and point to the calculating routes; `HANDLED_MANUALLY` stays the resolution for the residual;
  - a new row, **HRP-024** (the next free id at `62fcdc3`), "approve the previous month first", with the R16 raw-export case; the accountable role is M−1's approver.
- **ROADMAP:**
  - retire the QUAL-149 entry (`ROADMAP.md:254-267`);
  - the R24 retraction line was committed at `299c3dd` (`ROADMAP.md:344-352`): confirm only;
  - the four-question line already landed (`:268-276`).
- **`docs/references/danish-agreements.md`:** the Q2 and Q3 rules, source "owner ruling, 2026-10-01".
- **`docs/generated/db-schema.md`:** regenerated (O-3).
- **`docs/operations/legacy-db-upgrade-runbook.md`:** an S145 row (the segment; "pre-S145 databases are reseeded").
- **`docs/operations/model-routing-register.md`:** the S145 row (O-7, in C-9).
- **`SPRINT-145.md`:** the plain-language operational changes (the summary's six items), the 5α/5a table, the Unit mutation tables, the frozen lists, any cut recorded as a known unpinned change, and the note that criterion `:618` (the 6.00 credit) is met at unit level because its branch needs a 0-hour absence, which R24 makes unreachable (contradiction 12).
- **KB proposals** from agents, reviewed at 5b.

---

## Size check

| Task | Role | One line | Wave | Size | Flag |
|---|---|---|---|---|---|
| 14500 | data-model | schema + shapes | A.1a | M | — |
| 14501 | test-qa | harness DDL + A0 infra pins | A.1a | M | — |
| 14502 | test-qa | Skema A0 endpoint pins | A.1a | M–L | — |
| 14503a | test-qa | factory + real-engine host facts + the RED characterization file | A.1a | M | pre-split per R49 |
| 14503b | test-qa | stub-engine host facts | A.1a | M | pre-split per R49 |
| 14504 | test-qa | A6 Backend pins | A.1a | S | — |
| 14505 | rule-engine | week-by-week core + endpoint + R8 | A.1a | **L** | **largest single pass.** Cut line if it cannot finish: the Calendar helpers + the core land first; the endpoint + fingerprint + R8/R34 follow as 14505b. **The A.1 gate requires 14505b merged**; two arithmetic paths may coexist only intra-wave on local master, never at a gate |
| 14505-T | test-qa | the listed call sites of the retired members (7 test files) + `AbsenceRuleTests` for R35 + a dispatch check | A.1a | L | mechanical but wide |
| 14506 | test-qa | core + shape pins, MU-01..24 | A.1a → A.1 gate | L | — |
| 14507 | backend-infrastructure | Skema supersession | A.1b | L | — |
| 14508 | payroll-integration | hours read + host contract | A.2 | L | — |
| 14508-T | test-qa | the listed PCS and `RecalculateAsync` call sites | A.2 | L | may hand its mechanical remainder to `sweep` |
| 14509 | backend-infrastructure | A6 + rebuilder + calculator | A.2 | M | — |
| 14510 | ux | one label | A.2 | S | — |
| 14511 | payroll-integration | the per-plan lane | A.3 | **L** | **the QUAL-186 fix; no safe cut inside it** |
| 14511-T | test-qa | stubs + request shapes | A.3 | L | — |
| 14512 | payroll-integration | checks + dated key + Q7 | A.4 | L | four concerns. If long: Q7 (b) splits off as 14512b (it touches only `RetroactiveCorrectionService` and the mapper) |
| 14512-T | test-qa | R26 fixture rule: fake injection + seeding in the named suites | A.4 | M | shrank under R26 |
| 14513 | payroll-integration | replay contract | A.5 | L | — |
| 14513-T | test-qa | replay call sites | A.5 | M | — |
| 14514 | test-qa | calculation pins | A.2 → A.4 | L | — |
| 14515 | test-qa | replay pins | A.2 → A.5 | M | — |
| 14518 | test-qa | Stage A evidence | A-CLOSE | M | five CI runs (V-A + 4) |
| 14520–14524, 14528 | per row | Stage B | B | S–L | briefs written at B.0 |

**No task is drafted as too large for one pass.** Two carry a pre-named split line (14505, 14512), and one is the irreducible core of the sprint (14511). Stage A totals: 20 dispatched units (14503 pre-split, R49), 6 of them paired passes, plus 5 CI runs at the gate.

---

## Handoff block (the Orchestrator writes it at every gate into `SPRINT-145.md`)

| | |
|---|---|
| **As of** | <date, master sha> |
| **Phase** | <wave / gate> |
| **G-0** | CI on `62fcdc3`: <run id, result> |
| **Tasks** | `task · worktree/branch · build · 5α · 5a-R · 5a-C · merged sha` — one row per task, paired pass and fix-up |
| **Pending gates** | <next gate and its open boxes> |
| **Evidence** | `K_A` <sha> · V-A <run> · EA-1..4 <runs, matched?> · K <sha> · EB <runs> |
| **Open rulings** | <id · question · who> |
| **Next action** | <one line> |

---

## Needs a ruling

**Nothing for the owner.** R52–R59 rule every Step-0b cycle-2 finding, and R60–R66 every cycle-3 finding. The owner ruled OQ1 "Keep for S145" (2026-10-02).

**One declared deviation (D1, item 6) and one declared extension (E1, item 7).** Both were judged sound in cycle 3 by the internal lens, and R61 and R63 build on them. Items 2 and 3 keep their draft-3 numbers, so the cycle-3 scope still points at them.

1. **MD-52 (from draft 3, unchanged).** It is the only mutation that trips R41's forward pin `Leaver_LastDayWed30Sep_…`, because for a leaver the last piece day *is* the window end, so MD-43 cannot. It stays in EA-1.
2. **MD-45 (R53, rewritten).** "Replay consults `IPriorPeriodApprovalReader` **when one is registered**, and applies the prior-month predicate."
   - It still trips the sentinel pin, because its approval reader throws.
   - With a null reader nothing is consulted. So the Unit fact `NoApprovalReader_Replay_Succeeds` stays green, and EA-3 reaches its Regression step (`ci.yml:165` Unit → `:176` DemoSeed → `:187` Regression). The null case stays MU-38's.
   - MD-45 is on TASK-14518 CA-1 step 4's placement list. Its table row says "Unit N (u4)".
3. **The sentinel pin covers all four of R42's sources (R56, rewritten).**
   - The approval reader, the window resolver and `ReadCurrentAsync` are throwing fakes.
   - The agreement-code repository is a real `UserAgreementCodeRepository` over `new DbConnectionFactory("Host=127.0.0.1;Port=1;…;Timeout=1")`. Its factory takes a plain string (`DbConnectionFactory.cs:9`), and its read opens the connection (`UserAgreementCodeRepository.cs:306-307`), so any consultation fails at once.
   - The replay PCS has no profile resolver (C-R3), so MD-31 and MD-36 cannot reach the pin.
   - The fresh calculation runs on the first, non-sentinel PCS, with its fake swapped to EMPLOYEE_APPROVED.
   - As a result, MD-22, MD-38, MD-44 and MD-45 all reach this pin, and they sit in four different runs. Codex's "keep the null build too" is declined by R56, as redundant.
4. **R50 applied to `ReplayDeterminismTests.cs` (from draft 3, unchanged).** 14515's replay facts go in the new `ReplayFrozenInputsTests.cs`.
5. **No cutoff assertion in 14511-T (from draft 3; extended).** Draft 3 covered the unit assertions. C-T1 now covers the Regression assertions too, so that MD-43 and MD-52 do not gain unlisted reds there. `EmploymentEdges_LeaverTue29Sep_…` remains the request-level pin.
6. **Deviation D1 (from R55's placement): MD-21 stays in EA-2, and MD-42 moves from EA-2 to EA-4.** The counts are R55's own: 18 / 14 / 12 / 7.
   - *Why the table forces it:*
     - MD-22, MD-23 and MD-24 all edit PM, the prior-month check. MD-21's list is right only while PM stays where it is, after planning and the week check (contradiction 18). So the four need four runs.
     - H10 is reached by MD-23, MD-24, MD-26 and MD-27, so those four need four runs too.
     - R52 puts MD-22 in EA-1 beside MD-26. MD-23 sits in EA-3 and MD-24 in EA-4. That leaves MD-21 only one run, EA-2, beside MD-27 (no shared fact, no relied-on path).
     - MD-42 must then leave EA-2, because it shares RE1 and RE3 with MD-21 (Fable W3). EA-4 is clean for it: MD-24's facts use the stub engine; MD-39 edits a different statement of the same handler (EP-CFG, not EP-START) and shares no fact.
   - *Alternative considered:* follow R55 and move MD-24 out of EA-4. That fails, because H10 then has no free run.
7. **Extension E1 (to R59): a "relies on" column and a fourth check (c).** R59's three checks are kept as ruled: (a), (b) and (d).
   - *Why a fourth is needed:* those three cannot see a conflict in which one mutation's reds depend on code that another mutation in the same run changes.
   - *Worked example 1:* MD-21 + MD-24 have no shared fact and distinct path ids, yet they produce five unlisted reds.
   - *Worked example 2:* MD-12 shares no fact with MD-03, yet it hides every one of MD-03's reds.
   - Check (c) is R44's existing rule ("removes, moves or bypasses code the other edits") made mechanical. It adds no new constraint; it only makes an existing one checkable row by row.
8. **MD-42's exact site (R55/R59).** The timeline endpoint handler hands the core `PeriodStart = Pieces[0].From`.
   - *Does 14511-T see it?* No. The request's `PeriodStart` is unchanged, so 14511-T's request-shape assertions (on what PCS sends) cannot see it. The core is unchanged too, so the Unit step stays green.
   - *Which real-engine facts it reaches, checked against their assertions:*
     - RE1 is reached (its "none dated 31 August" assertion);
     - RE2 is not: it asserts merarbejde and payout only, and 31 August at the day norm moves neither (C-RE1);
     - RE3 is reached only through C-RE2's added assertion (NORMAL_HOURS dated in October only).
   - *Alternative considered:* PCS sends `PeriodStart` = the opening Monday. Rejected: that changes the very request 14511-T's unit assertions inspect, so it would risk a Unit red that stops the run.
9. **MD-24's exact site (R61).** It sits in the per-plan lane's admission step, before the lane's call to `BuildPlanForLegacyCallersAsync` (`PeriodCalculationService.cs:797`, `:861`). The mutation reads the employment windows itself; the builder and `EnsurePeriodCalculableAsync` are untouched.
   - *Why not draft 4's site:* draft 4 put it before `PeriodPlanner.Plan(` inside the shared builder (`:1100`). The raw-route guard also calls that builder (`EnsurePeriodPlannableAsync`, `:903-911`; `Payroll/Program.cs:593`), and it catches only the planner and classification exceptions (`:595`, `:601`). So H11 and H5's raw-route setup would have answered 500.
   - *What it reaches:* the planner refusal and the classification fetch are still raised inside the builder call (`:1100-1105`), so its list keeps H9, H10 and the four existing S144 host facts X1–X4, which 14512-T leaves unseeded. They are on no other list. H11 and H5 are on its Not list.
10. **N2 applied as "exactly the day norm"** (contradiction 19; C-W2).
11. **Fixture conditions outside the cycle-3 scope.** A by-effect row is only determinate if the facts it is checked against assert what the row assumes. Where a brief left that open, § "RED evidence" → "Fixture conditions" fixes it (27 conditions), and each test brief now pastes its own. The brief lines touched outside the named cycle-3 scope are:
    - TASK-14501's Facts and Do 5–6;
    - TASK-14502's paste line and the full-day-only bullet;
    - TASK-14503a/b's paste line and the two real-engine fact lines;
    - TASK-14511-T's cutoff bullet;
    - TASK-14514's paste line, `Flex_TwoMonths` and the two edges facts;
    - TASK-14515's header line, the two reconstruction lines and the as-of guard;
    - TASK-14514's guard sub-bullet on the one-file allowlist;
    - fact 2 of "How the waves are built", the A.4 row of the wave table (MU-40), the ledger row of 14503a (N5), and Gate A-CLOSE CA-1 (R54, R59 wording);
    - the R52–R59 rulings table, contradictions 16–19, the header and the status row.

    Final line numbers are in the report. Cycle 3 may want to read them.

*Nothing here needs the owner.*

---

## Planning record

*The full ruling texts (R25–R66), the brief contradictions (1–19), the draft-to-draft absorption tables and the revision history moved to the sprint log's § "Planning record (Step 0b)" under R66b. The Orchestrator places that section in `docs/sprints/SPRINT-145.md`. The one-line rulings-in-force tables above remain the plan's index.*

**Revision history (Stage B, kept here because the rest moved to the sprint log):**
| Rev | Date | By | What |
|---|---|---|---|
| B.0 | 2026-10-16 | `planner` (Opus 5.5) | The nine Stage B briefs written against `K_A` (`923f7fb`); the plan itself unchanged except the brief links |
| B.0 rev 1 | 2026-10-16 | `planner` (Opus 5.5) | Absorbs the Fable seat's R-53..R-79 (scoped Step-0b cycle 1, `.claude/plans/s145/b0/adjudication-cycle1.md`): the close sequence renumbered into one linear order with frozen patches (R-53); the R27 label list gains B1/B2/B3 here and in every brief's pasted block, and Step 7a item 7 lists every label (R-65); the Stage B mutation outline replaced by the fixed set, MB-12 and MB-13 included (R-54, R-56, R-62, R-72); the two paired passes added to the Stage B table (R-65); the build-safe order with R-78's amendments; the records owed gain QUAL-209/210/211, the ADR-016 D10 amendment, ADR-040 `:202-207` and the HRP-003 rewrite (R-55, R-57, R-63, R-64); the amended criterion wording `:699`, `:713`, `:690` (R-55, R-73, R-59) |
| B.0 rev 2 | 2026-10-19 | `planner` (Opus 5.5) | R-80..R-92 (scoped Step-0b cycle 2, `.claude/plans/s145/b0/adjudication-cycle2.md`; adjudicated on Opus by the owner's direction of 2026-10-19, Fable on its usage limit). R-80: the MB-13 outline names its reds (RP-B1, SR1, SR2) and EB-1 is 14 facts; the F-1 parenthetical is deleted. R-83: the close sequence's plain-language paragraph gains the patch-validity sentence, and C-1..C-5 are replaced (the patch base `B`, raw-byte patches and their round trip, the patch-validity check at C-2, the three red branches at C-4, the no-push interval from the green push, the close's K). R-90: Step 7a item 6 names all four patches. R-81: Step 7a item 7 becomes the per-pass label table; the pasted standing-constraints block is unchanged. R-84: the Stage-A-only fallback reconciles QUAL-209/210/211 (§ "If Stage B does not converge", Step 7a item 9, the records owed). The status row says rev 2. Next: the scoped Step-0b cycle 3 (R-92) |
| B.0 rev 3 | 2026-10-19 | `planner` (Opus 5.5) | R-93..R-105 (scoped Step-0b cycle 3, `.claude/plans/s145/b0/adjudication-cycle3.md`; adjudicated on Opus by the owner's direction of 2026-10-19, Fable on its usage limit). The close sequence from its plain-language paragraph through C-5 is replaced by the ruled consolidated edit A: R-93 (`--full-index --default-prefix` at the C-1 save and at C-5's check of record), R-94 (the code set, defined once and used by C-2, C-3, C-4 and C-5), R-96 (what re-opens the census at C-2), R-97 (the commit-message rule, C-4 (c)'s push order, branch (d), the green-push definition), R-98 (re-cuts never overwrite a frozen patch, each patch's SHA-256 recorded and re-checked at C-3 and C-5, how `B` moves, C-3's rescope, the absolute `cmp` path). R-100: § "If Stage B does not converge" gains the fallback after the green push. R-97: C-9's message. R-101: Step 7a item 7 gains the relocated assertion and the label-qualifier rule. R-103: the B.3 row names `B`. R-95, R-99 and R-102 change briefs only; R-104 (the N7 owner question, for C-1) and R-105 (cycle 4) need no plan text. `:11`, `:686` (R33) and the standing-constraints block are unchanged. The status row says rev 3. Next: the scoped Step-0b cycle 4 (R-105) |
| B.0 rev 4 | 2026-10-19 | Orchestrator (Opus 5.5), seat rulings applied verbatim | R-106..R-113 (scoped Step-0b cycle 4, `.claude/plans/s145/b0/adjudication-cycle4.md`; adjudicated on Opus by the owner's direction of 2026-10-19). R-106: C-2's after-K records ride in the close commit or the next K. R-107: `mkdir -p` before the re-freeze's archive copy. R-108: a re-freeze records every patch's SHA-256. R-109: the Stage-A-only form moves the Stage B freeze to `cb1/mut/stage-b/` before its C-1. R-110: C-4 names the guard's worktree, untracked-source and task-ledger gates. R-111: five wording fixes in C-4. R-112: R33's contract at `:686` reads as after CA-7. R-113: signed; no cycle 5 |
