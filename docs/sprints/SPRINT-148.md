# Sprint 148 — the overtime/flex model, stage 0 and the config stage (ADR-043)

| | |
|---|---|
| **Status** | **OPEN — refinement READY (rev 5.1); plan `.claude/plans/PLAN-s148.md` FINAL (draft 1.5.1), Step 0b READY on both lenses 2026-11-10 (five cycles; see "Plan Review (Step 0b)"); wave 0 merged and confirmed (CI-W0b green); wave 1 briefed and adjudicated, dispatch next** (opened 2026-11-09; owner "Go" to the plan, 2026-11-09). **The review floor moved to Fable 5.2 on 2026-11-10** (the owner reset the Fable limit and named the new model, 2026-11-09). After the owner's restart the session runs client 2.1.322; the floor id changed in all its places (reviewer and adjudicator self-checks, the close guard's `$reviewFloor`, its harness fixtures, WORKFLOW's reference line and gate sentence; harness 37/37). The bump procedure listed four places and missed the adjudicator's self-check; it now lists five. Three probes (reviewer, adjudicator, reviewer again) ran on `claude-fable-5-2`, but all three refused: they still read the 5.1 self-check, although the file on disk was changed. Agent definitions were not re-read inside the running session, so the owner restarted a second time. After it, the reviewer and adjudicator probes both read the 5.2 floor. The bump is committed as **`636bc7da`**, with WORKFLOW's "Last verified" record and the procedure now ending in a restart and the probes. **Step 0b cycle 1:** Codex ran detached from 09:11 to 09:16 on draft 1.2 and returned **CHANGES-REQUIRED, 0 BLOCKER / 2 WARNING** (`.claude/reviews/PLAN-s148-codex-c1.raw.md`). Both warnings were confirmed against the code. **W-1:** NR-P2's "with no exported month, no call" would also skip the settled-holiday-year hand-off, which is selected independently of exports (`HrBackdateWorklistRepository.cs:1027`). **W-2:** TASK-14830 retires `ConfigFingerprint` but is not authorized for its remaining engine callers (`RuleRegistry.cs:467`, engine `Program.cs:105`). The Fable 5.2 lens gave the first Fable read to the refinement material that cycles 2–5 had reviewed only on the Opus stand-in, and found the cycle-1 BLOCKER there (REF-1) |
| **Plan** | `.claude/plans/PLAN-s148.md` **draft 1** (2026-11-09, `planner`): 63 agent tasks (40 dispatch ids TASK-14800 … 14839 + 23 paired `-T` passes), waves 0–7 with CI checkpoints CI-W0 … CI-W6 and four CI mutation runs; every one of the refinement's 111 acceptance criteria owned by exactly one task (script-checked); the five carried NOTEs placed. **Draft 1.1** (Orchestrator): NR-P1 … NR-P6 ruled — the T14b specialist stack merged once; the open-interval start; the Teamoversigt slowness threshold (300-person median doubled or > 2 s → an F-series row); **NR-P4: the edit drawer's organisation change with a future agreement date, which S148's new transfer rule would break, is fixed in S148**; CI mutation runs for the six Docker pins' RED; a day no record covers uses that day's OK version (**told to the owner:** Teamoversigt then reads each day's OK version, invisible on seeded data). **Draft 1.2** (Orchestrator, 2026-11-10): NR-P4 had been ruled but not written into the plan body, the same gap the refinement's cycles 3 and 4 caught, so it is now carried in: TASK-14838 takes the drawer's date block and runs after 14837, since both touch `PersonDrawer.tsx`; new acceptance criterion **AC-C13j** (112 ids). The plan's model rows moved to Fable 5.2 |
| **Theme** | The first two stages of the overtime/flex model program (ADR-043, accepted in S147). **Stage 0** stops paying what is wrong today: on-call, call-ins and travel are refused at registration instead of paid twice; the child's-sick-day quotas are corrected (QUAL-236); the 11-hour rest check reads the clock times employees enter in Skema, so S147's "not checked" notice can go; a day of paid absence can no longer be credited more than it was worth; the configuration editors say plainly that they do not yet affect pay. **The config stage** makes the values the editors already let people change reach payroll: dated, resolved per day (central → position → local) and frozen into each month's proof, with the new local settings and the two HR-recorded facts the later stages read. **Neither stage changes pay arithmetic**, so the sprint's acceptance pin is identical demo pay lines before and after, except for any stage-0 item the refinement names as a marker exception |
| **Owner rulings in force** | **S148 Q-1 = (a) "Date it in S148" (2026-11-09):** the employee's organisation gets a dated history in S148 (a dated `user_organisations` table with its audit table, the S138 agreement-code shape; the transfer gains an "effective from" date; access rights keep using today's organisation; QUAL-206 closes), so an institution's own profile values can reach pay through the config stage without a past month being recalculated on the new institution's values after a transfer. Asked because the review found the profile's four legacy columns already reach the weekly engine once payroll is connected (Codex BLOCKER, cycle 1). Offered and not chosen: (b) hold local values off the pay path until S149. Not offered: connecting the profile without dating the organisation and naming the transfer case a limit (a wrong pay input) · **S148 Q-2 = (a) "Correction worklist" (2026-11-09):** a configuration published with a past "applies from" date (allowed by ADR-043 D12) puts every (employee, paid month) it reaches on HR's correction worklist (HRP-001) for the GlobalAdmin who recalculates (HRP-003), each row audited when resolved. Offered and not chosen: (b) show the publisher a count only. Backdating itself was not re-asked (ADR-043 D12) · **S148 scope = "Stage 0 + config" (2026-11-09):** S148 carries stage 0 and the config stage; stage 1 (the monthly norm and flex by default, where every HK and PROSA payslip moves) gets its own sprint, S149, then stage 2 and stage 3 (`ROADMAP.md` § 1). Offered and not chosen: stage 0 only (smaller, but the corrected pay model lands a sprint later); stage 0 + config + stage 1 (the model sooner, but one sprint mixing "pay must not change" with "every payslip changes"). **Carried from S147** (every ruling in `SPRINT-147.md`'s "Owner rulings in force" binds this sprint, notably D-1 … D-12, Q-2 and OQ-4). **The governance hooks stay fail-open; the fail-closed choice is decided at S148's close** on this sprint's log-vs-transcripts reconciliation (owner 2026-11-06, `ROADMAP.md` § Governance / docs; FAIL-009). Standing: reseed, don't migrate; the complete fix without padding; owner questions one at a time, explained first; review cap 5 per lens. **CI evidence runs (owner "go ahead", 2026-11-10):** throwaway branches and draft PRs for the CI checkpoints and mutation runs, closed unmerged; nothing reaches `origin/master` before the close push |
| **Predecessor** | S147: close `e553777e` · **CI GREEN `42065541572`** on the close push, all seven jobs (Regression 2436) · C-5 `446ec114` pushed. **Post-close, local and unpushed** (pushed with S148's close): `4375bafc` (FAIL-009: the governance hooks run reliably again), `b5ac67ad` (the owner's fail-closed ruling queued), `fee2a635` (the Ponytail-ideas trial: SMALLEST COMPLETE CHANGE, PAT-032 and check 5, the concrete-case review rule; block identity `f57be923033c5ab9` × 9; its CI evidence, draft PR #52, closed unmerged: run `42469199003` red on exactly the 14 frozen rejections, run `42469369827` green on all seven jobs, Unit 1722 · DemoSeed 170 · Regression 2436) |
| **Base commit (Step 7a)** | **`e553777e`**, the S147 close commit (`docs/WORKFLOW.md` step 7a), so `446ec114`, `4375bafc`, `b5ac67ad` and `fee2a635` fall inside S148's Step-7a diff |
| **Orchestrator model** | Opus 5.5 seat, never switched (owner ruling 2026-09-29); client 2.1.301 to 2026-11-09, 2.1.322 from 2026-11-10. Drafting: `planner` (Opus 5.5). Judgment: `reviewer` and `adjudicator` on Fable 5.1 through the refinement (cycle 1 only; cycles 2–5 on the labelled Opus stand-in), **Fable 5.2 from 2026-11-10** |
| **Trials and checks this sprint runs** | **The Ponytail-ideas trial, first measured sprint** (`docs/operations/model-routing-register.md`, "The Ponytail-adoption trial"): M1–M6 go in this sprint's register row; the implementer briefs carry the SMALLEST COMPLETE CHANGE block; both lenses apply the concrete-case rule; every `(unproven)` NOTE is recorded with its disposition. **FAIL-009's 60 s hook limit, first test:** the register row reconciles "log ALLOW N / transcripts M" |
| **Refinement** | `.claude/refinements/REFINEMENT-s148-stage0-and-config.md` — **rev 1** (2026-11-09, `planner`, at `fee2a635`; source `.claude/plans/s147/S148-BUILD-INPUT.md` draft 2.6, every citation re-opened at the base; 20 brief contradictions recorded, one owner question proposed, seven points for the Orchestrator's ruling). Step 4 cycle 1 running on both lenses |
| **Entropy scan (Step 0a)** | 2026-11-09 at `fee2a635`. **Worktrees:** none beyond the main checkout; local branches: master only. **Remote:** `add-claude-github-actions-1772139476170` (2026-02-26) still lingers (**DEBT**, as at S147); the evidence branch `evidence/pre-s148-shortcut-check5` is deleted; Dependabot's update branches are open as usual. **Untracked:** `.claude/plans/s145/{b0,ca1,ca3,cb1,cb5}/`, `.claude/plans/s146/ADR-043-draft.md`, `Tidligere udbud/` — as at S147, none under `src/`, `tests/`, `tools/` or `frontend/`. **Anti-patterns:** no `FindFirst("scopes")`; no hard-coded localhost in `src/`; no project outside the Rule Engine references it (PAT-005). Endpoint authorization was not re-swept: since S147's clean sweep the only `src/` change outside S147's reviewed tasks is `fee2a635`'s comment-only edit. **KB paths:** 149 cited, 20 non-resolving, all benign and as at S147 — gitignored refinement files, an elided path in PAT-010, the two files deleted since their ADR was written (ADR-018's `EventStoreInterfaceTests.cs`, ADR-023's `UserManagement.tsx`), and the three runbooks ADR-025 designs but never built. **Shortcuts (new item 6, PAT-032):** none in the code; the SMALLEST COMPLETE CHANGE block is identical in all nine places (`f57be923033c5ab9`). **Freshness (soft):** `docs/sprints/INDEX.md` anchored at S141 and four other anchored docs trail by more than three sprints (**DEBT**, reported by CI's docs job). `QUALITY.md` is anchored at S147 (current) |
| **Baseline** | S147 final (`SPRINT-147.md`): Unit 1722 · DemoSeed 170 · Regression 2436 (CI) · Smoke 7 · vitest 84 files / 1089 · e2e 7. Re-measured at the first gate, never carried forward |
| **Test Verified** | — (pending) |

## Plain-language summary

S148 is the first building sprint of the overtime/flex model. In S147 the owner decided, item by item, how
the state's agreements should be read: the norm is a calendar month, every hour above it is flex, and only
hours a leader ordered or accepted become overtime. This sprint does not yet change how anyone is paid. It
removes the places where today's system pays or accepts something it should not, and it lays the
foundation the new pay model needs: payroll will calculate with the dated agreement and local settings
the editors already maintain, and freeze them into each month's proof, instead of ignoring them in
favour of rules compiled into the code. The test that holds the sprint honest is simple to state: every
demo payslip must come out the same before and after. The monthly norm itself, where payslips do change,
is S149.

## Refinement record (Steps 1–4)

**Rev 1** (2026-11-09, `planner` Opus 5.5, at `fee2a635`): the build input's 21 stage-0 and config-stage items
plus the six tidy inputs, every citation re-opened; 20 brief contradictions, the most consequential that the
configuration lookup falls back silently to compiled values on any read failure (`ConfigResolutionService.cs:127-145`,
`:151-165`, `:181-189`) and that the legacy time route lets paid hours bypass the send check.

**Step 4, cycle 1** — both lenses NOT-READY (the first refinement reviewed under the concrete-case rule; every
BLOCKER and WARNING below carries a concrete case):
- *Codex* (`.claude/reviews/REFINEMENT-s148-codex-c1.raw.md`): 1 B / 3 W / 1 N. **B:** an employee's organisation is
  not dated, yet the legacy local `max_flex_balance` already reaches the weekly engine (`ConfigResolutionService.cs:230-243`),
  so after C-1 recalculating a month from before an organisation move would use the new organisation's cap. **W:** the
  child's-sick-day prerequisite cannot be met when the illness starts on a weekend; PIN-1 compares fields the export
  does not have and omits `Amount` and the period end; an event-store-only old manifest fails as "cannot replay"
  instead of the named refusal. **N:** OQ-1 option (c) contradicts accepted ADR-043 D12.
- *Fable* (`.claude/reviews/REFINEMENT-s148-reviewer-c1.md`): 1 B / 5 W / 7 N + 1 `(unproven)` N. **B:** PIN-1 measures
  nothing — payroll refuses a month opening mid-week unless the prior month is approved, both demo worlds open mid-week,
  and the demo approves one month per person, so the golden would be near-empty with no minimum asserted. **W:** OQ-1
  re-opens ADR-043 D12; `evaluate` keeps a compiled fallback, so a missed payroll call site fails open unseen; three pins
  cannot fail for their stated reason; the engine request-shape test churn (23 files) is uncounted; items the owner
  assigned to "S148's refinement" (the opening flex balance's sprint, D-8a) are not passed on.
  **(unproven) N:** tidy 21a's "someone's approver" under the approval walk — evidence: the plan's shared predicate with
  a departed-manager-with-reports fact (disposition: open).
- **Adjudication c1** (Fable, `.claude/reviews/REFINEMENT-s148-adjudication-c1.md`, RULED-WITH-QUESTIONS): 26 rulings,
  no finding declined, every finding's code opened first. Both BLOCKERs upheld: the organisation-history gap (R-1,
  which leaves the owner fork Q-1) and the vacuous pin (R-2: Monday-opening demo months, 15 January 2026 → December
  2025 and 15 July 2026 → June 2026, plus a non-vacuity count). Among the rest: the illness chain allows zero-planned
  days (R-3); the pin compares every real export field (R-4); an old manifest is refused by name on both storage paths
  (R-5); every evaluation endpoint refuses a request without a config, and the two legacy callers send the compiled one
  under a `shortcut(pending)` comment (R-8); three pins get a mechanism that can fail (R-9); the items ADR-043 assigned
  to this refinement are passed on to S149's and stage 2's refinements (R-11); configs and overrides refuse a future
  date, as profiles do (R-18, told, not asked); the narrowed "not checked" notice stays for days not on the clock
  (R-22, told: retiring it outright would make those days look compliant); QUAL-202 closes at S148 (R-25). The
  `(unproven)` 21a NOTE is not promoted; its evidence is ordered inside task T5 (R-19). OQ-1 was narrowed to the
  hand-off (Q-2): backdating itself is ADR-043 D12's, not re-asked.
- **Owner answers:** Q-1 = (a), Q-2 = (a) (2026-11-09; "Owner rulings in force" above).
- **Rev 2** (`planner`, 2026-11-09): R-1 … R-26 and both answers applied; new C-13 (dated organisation) and Q-2's
  worklist hand-off in C-2; 24 tasks. **Rev 2.1** (Orchestrator): rulings on the planner's NR-8 … NR-12 — 8 (a ruled
  test date fell on Easter Monday), 9 (`evaluate-absence` reads no config) and 11 (a backdated transfer that reaches a
  paid month also lands a worklist row, Q-2's reason applied to transfers) confirmed; 10 restored S0-4's served minimum
  as the last cut; 12 confirmed the narrow transfer back-dating (only within the current organisation row) as a named
  interim limit and a likely PAT-032 shortcut.

**Step 4, cycle 2:**
- *Codex* (`.claude/reviews/REFINEMENT-s148-codex-c2.raw.md`): all five cycle-1 findings closed; 0 B / 2 W. **W:** two
  transfers on the same date would break the copied unique `(user_id, effective_from)` index — the writer needs the
  agreement writer's equal-date update-in-place case; the worklist's employee match drops the month whose opening week
  reaches back into the publish's range (an OK24 publish ending 31 March changes April's week from 30 March).
- *Internal lens*: **Fable reached its usage limit at the start of cycle 2** (2026-11-09, HTTP 429). By the owner's
  standing instruction for that case (S146, "Lets just use opus for now") the internal lens runs on **Opus as a labelled
  stand-in**, never presented as Fable. Fable resumes when the limit resets. Result (`.claude/reviews/REFINEMENT-s148-reviewer-c2.md`,
  `reviewed-by-model: claude-opus-5-5`): every cycle-1 internal finding closed; access control verified unchanged by
  C-13; 0 B / 8 W / 10 N (1 `(unproven)`), READY-WITH-WARNINGS. The weightiest: a publish or transfer that races a payroll
  export leaves a stale paid month off the worklist — the export locks per employee, the config writers take no lock,
  and the worklist read runs in the writer's own transaction; S138's corrections share the gap.
- **Adjudication c2** (the Orchestrator's Opus seat, labelled; Fable limited; `.claude/reviews/REFINEMENT-s148-adjudication-c2.md`,
  RULED, no owner question): 18 rulings, none declined. **Told to the owner, not asked:** the race is closed in S148
  (A-3: one "pay inputs" lock, exclusive in every writer that dates a pay input into the past — including S138's two
  correction writers — and shared at the start of every export, which re-checks its frozen inputs under the lock and
  refuses by name if any changed), because Q-2 = (a) asked for nothing silent and this was the remaining silent path; a
  person cannot transfer themselves (A-18, the four-eyes principle applied to a pay input S148 makes live), and the
  existing self-edit class (HR editing their own profile or agreement code) is registered as a new SEC row at close;
  settled holiday years reached by a backdated transfer or config are flagged (A-4); a live agreement config can no
  longer be archived directly (A-6). The rest: the settling-month predicate (A-1), same-day transfers (A-2), the four
  screens moved onto the dated lookup (A-5), a non-empty pay pin (A-7), the test-seed census (A-8), and smaller items.
- **Rev 3** (`planner`): A-1 … A-18 applied; 27 tasks (T14 split; T24 the export-side lock, T25 the test-seed sweep);
  found while revising, the local-profile write runs at REPEATABLE READ (`ConfigEndpoints.cs:264`), which would have
  defeated the lock — it moves to READ COMMITTED. **Rev 3.1** (Orchestrator): NR-13 the correction takes the lock too;
  NR-14 PIN-1 stays at smoke scale with a fixed seed giving ≥ 3 APPROVED months per world; NR-15 the ACTIVE "Arkiver"
  button goes in the editors' task; NR-16 HR is refused changing their own employee profile or agreement code (P-26,
  first in the cut order — **told to the owner:** S147's strict four-eyes principle applied to pay inputs); NR-17 the
  employment-date writers join the lock.

**Step 4, cycle 3:**
- *Codex* (`.claude/reviews/REFINEMENT-s148-codex-c3.raw.md`): both cycle-2 findings closed; the lock protocol judged
  coherent (no deadlock found; admission only, no arithmetic); 0 B / 1 W — the employee-profile DELETE closes a pay input
  today and escapes the lock.
- *Internal lens*: a Fable spawn failed again (HTTP 429); **Opus stand-in, labelled** (`.claude/reviews/REFINEMENT-s148-reviewer-c3.md`):
  every cycle-2 internal finding closed; the lock verified (no wait cycle, a separate key space, the isolation claim, no
  pay figure changes); 0 B / 5 W / 6 N, READY-WITH-WARNINGS — the rev 3.1 rulings had not reached the body; the recheck
  could pass a change dated inside a frozen run; the employment-date writers need a worklist hand-off, not only the lock
  (a start date moved earlier into a paid month changes it); a discarded draft could read as in force; the transfer's
  date rules and the self-refusals must fire on a real change, not on a field's presence (the edit drawer always sends
  both, so S141's scheduling and S138's backdating would break).
- **Adjudication c3** (the Orchestrator's Opus seat, labelled; `.claude/reviews/REFINEMENT-s148-adjudication-c3.md`, RULED,
  no owner question): 12 rulings, none declined — the rulings applied throughout (B-1), the recheck compares the whole
  re-derived inputs incl. the EMPLOYED days and the settlement cutoff (B-2), the employment-date writers and the profile
  DELETE take the lock and list the paid months they reach (B-3, B-4), "published" means `published_at` set (B-5),
  change-not-presence (B-6), screens sum month figures per day (B-9), T21 split in two (B-12).
- **Rev 4** (`planner`): B-1 … B-12 applied; 28 tasks; the stale-text sweep clean. **Rev 4.1** (Orchestrator): NR-18 a
  start-date event so every worklist trigger names its causing event; NR-19 the end date's interval shifted one day (it
  is inclusive); **NR-20 = (b): the screens keep the owner's "fraction at month end" answer of 2026-10-14 (QUAL-198)** and
  only sum the configured norm per day — option (a) would have varied an owner ruling, which the Orchestrator cannot do,
  so QUAL-226 stays owed to stage 1; NR-21 the worked figure is 150.80 h (the Orchestrator's 151.8 was wrong); NR-22 the
  employment-date writers are not cuttable.

**Step 4, cycle 4:**
- *Codex* (`.claude/reviews/REFINEMENT-s148-codex-c4.raw.md`): the cycle-3 finding and notes closed; the new design matches
  the code; 0 B / 2 W — both consistency: NR-20 (b) and NR-22 had not reached the Summary, the task rows and the
  checklist.
- *Internal lens* (Fable at its limit again, HTTP 429; **Opus stand-in, labelled**, `.claude/reviews/REFINEMENT-s148-reviewer-c4.md`):
  cycle-3 findings closed; **1 BLOCKER — the Orchestrator's own rev 4.1 edit had deleted most of C-1** (the engine
  contract and acceptance criteria AC-C1a … AC-C1m) by cutting between two text markers it wrongly assumed were
  adjacent; plus 3 W (the end-date interval still missed the month ending on the earlier end date — the most common
  leaver correction; the rulings not carried through; the narrowed formula would round a part-timer's figure differently
  from payroll and nothing pinned option (b)) and 4 N. **Repaired the same day:** the refinement folder is gitignored, so
  the Orchestrator rebuilt C-1 from the rev-4 planner's transcript (its earlier read of the file plus its own edits
  replayed in order); every C-1 criterion is defined exactly once again. Lesson recorded (exact replacements only; print
  a span before replacing it; check every cited id survives; back up gitignored working documents first).
- **Adjudication c4** (the Orchestrator's Opus seat, labelled; `.claude/reviews/REFINEMENT-s148-adjudication-c4.md`, RULED,
  no owner question): C-1 … C-8 — the end date's interval includes the earlier end date's month (C-2), a full sweep of
  NR-18 … NR-22 (C-3), the exact (b) formula with an 81.40 h pin for a mid-month fraction change (C-4), and four small
  fixes. **Cycle 5 is the last under the cap**: a new BLOCKER there goes to the owner.
- **Rev 5** (`planner`, rev 4.1 backed up first; every edit an exact replacement): C-1 … C-8 applied; the NR-18 … NR-22
  sweep clean; a mechanical self-check (tested first on planted errors): 111 acceptance-criterion ids cited, 111 defined,
  each exactly once; 28 tasks. **Rev 5.1** (Orchestrator): NR-23 (each lock tested in the task that writes it) and
  NR-24 (the unused startup seeder retires) confirmed.

**Step 4, cycle 5 (the last under the cap):**
- *Codex* (`.claude/reviews/REFINEMENT-s148-codex-c5.raw.md`): **READY**, 0 B / 0 W / no new note; C-1 complete; the
  figures re-checked independently (150.80, 81.40 vs 118.40, 132.00).
- *Internal lens* (Fable at its limit again, HTTP 429; **Opus stand-in, labelled**, `.claude/reviews/REFINEMENT-s148-reviewer-c5.md`):
  **READY**, 0 B / 0 W / 5 N (1 `(unproven)`); its own id check: 111 cited, 111 defined. NOTEs, at the Orchestrator's
  discretion and **carried into the sprint plan** (which Step 0b reviews on both lenses): an unchanged end date re-saved
  should list nothing; the screen formula's rule when no record covers the month end (today's sources; a profile-less
  screen keeps rendering); AC-C2n's and AC-C2m's wave timing; two stale sentences (the Summary's cut-order line, the
  Readiness line). **`(unproven)` N-3** — summing the month day by day may slow Teamoversigt; evidence: a timed read on a
  30- and a 300-person roster before and after T14b; disposition **open**, carried to T14b (resolve per stretch between
  dated changes; a slow figure becomes an F-series row).

**Refinement: READY (rev 5.1, 2026-11-09).** Five cycles per lens. Internal lens: cycle 1 on Fable, cycles 2–5 on an Opus
stand-in (Fable at its usage limit throughout, by the owner's standing S146 instruction); adjudications: cycle 1 Fable,
cycles 2–4 the Orchestrator's Opus seat. Trial counts for the model-routing register (refinement chains, descriptive,
not M1 cells): Codex 5 cycles, internal 5 cycles; cycle-1 B/W/N Codex 1/3/1, internal 1/5/8.

## Plan Review (Step 0b)

**Plan:** `.claude/plans/PLAN-s148.md`. **Lenses:** Codex (external) and `reviewer` on **Fable 5.2** (internal, the review floor since `636bc7da`). Cap 5 per lens. Adjudication on the `adjudicator` (Fable 5.2).

**Cycle 1 (2026-11-10, on draft 1.2).**
- *Codex:* CHANGES-REQUIRED, 0 BLOCKER / 2 WARNING (`.claude/reviews/PLAN-s148-codex-c1.raw.md`).
  - **W-1:** NR-P2's "with no exported month, no call" would also skip the settled-holiday-year hand-off, which is selected independently of exports.
  - **W-2:** TASK-14830 retires the config fingerprint but may not edit the engine files that still use it.
- *Fable 5.2:* CHANGES-REQUIRED, 1 BLOCKER / 4 WARNING / 7 NOTE (`.claude/reviews/PLAN-s148-reviewer-c1.md`). This was also the first Fable read of refinement rev 2.1 → 5.1, which only the Opus stand-in had reviewed.
  - **REF-1, BLOCKER (in the refinement):** a person created in the product gets an organisation row from the creation day only. When HR backdates their profile and start date (allowed since S138), the earlier days have no organisation, so payroll refuses them and no screen can repair it.
  - **A-1:** dated position overrides have no decided storage shape, and the task that must build them cannot touch the schema.
  - **A-2:** in the engine-switch stack, both test projects stop compiling for three dispatches.
  - **A-3:** this is Codex's W-2, with two more hits.
  - **A-4:** no test pins which of two same-day publishes is in force.
  - **NOTEs:** A-5 (the plan header's stale floor id, fixed at once), A-6, A-7 (unproven; Codex's W-1 as a NOTE), A-8 (unproven), A-9, A-10, REF-2.
  - **Found sound:** C-1 is whole after the restore; every figure recomputes; the end-date interval holds; the lock has no wait cycle and its writer census is complete; the fail-closed lookup removes exactly the three fallbacks; the 112 AC ids each have one owner.
- *Adjudication (`adjudicator`, Fable 5.2):* **RULED. Every finding accepted, none declined, no owner question** (`.claude/reviews/PLAN-s148-adjudication-c1.md`).
  - **NR-P7 (REF-1):** a product-created person's first organisation row runs from `0001-01-01`, like the seeded data, so a backdated profile always has an organisation. Given up: the table no longer records the first institution's join date, which the employment start date already holds. Recorded as DV-7, a deviation from the refinement's create rule.
  - **NR-P8 (A-1):** position overrides keep their editable row, and a new append-only version table holds their dated history, so recalculating an old month reads the override in force then.
  - **NR-P9 (A-2):** the engine-switch stack's test pass is split in two, so the tests compile after the engine step.
  - **NR-P10 (W-2/A-3):** TASK-14830 owns every line its own retirement grep finds.
  - **NR-P11 (A-4):** the same-day tie-break is the later publish, pinned by a test that fails if the order is inverted.
  - **NR-P12 (W-1/A-7):** the settled-holiday-year hand-off is always made when an employment date changes; NR-P2's "no export, no call" now covers only the exported-month hand-off.
  - **NR-P13 (A-9):** the two profile fields that still don't reach pay keep a per-field note.
  - **NR-P14 (REF-2):** the editor offers the nearest valid Monday when a mid-week date is refused.
  - A-6, A-8 and A-10 are recorded as notes. **5a watch item (A-10):** TASK-14821 is the densest single task (eight lock sites, P-26, three hand-offs, a new event).
  - **Told to the owner:** under NR-P6, Teamoversigt reads each day's OK version, which makes no visible difference on seeded data. Under NR-P11, when two versions are published on the same date, the later one is in force.
  - **Plan draft 1.3** (`planner`) carries every ruling into the body. A count script matched the adjudicator's expected figures: 115 AC ids, each with one owner; 40 dispatch ids plus 24 paired passes, 64 agent tasks. While folding in, the planner raised **NR-P15**: under M-14 the override read already returns the later write, so if M-14 and M-15 share a CI mutation run, M-15's override half can't be seen failing. **Draft 1.3.1:** the Orchestrator ruled (a), M-14 moves to CI-M4b beside M-10, still four runs. Condition: AC-C2o's fixtures must not depend on the seeded agreement configs that M-10 breaks; otherwise fall back to (b).

**Cycle 2 (2026-11-10, on draft 1.3.1).**
- *Codex:* CHANGES-REQUIRED, 0 BLOCKER / 3 WARNING / 1 NOTE (`.claude/reviews/PLAN-s148-codex-c2.raw.md`). Its cycle-1 W-1 and W-2 are CLOSED in the body; the counts are verified (115 ids; 64 tasks).
  - **W-1:** the tie-break's `clock_timestamp()` is only a hint, so a publish whose transaction started earlier but took the lock later can lose to the version it replaced.
  - **W-2:** NR-P14's offered Monday can precede the latest published version, which the predecessor rule refuses.
  - **W-3:** the mutations moved to TASK-14826-T's gate (M-2 … M-5) would run on a worktree made from master, which lacks the stack's code.
  - **NOTE (unproven):** NR-P15's fixture condition is not in TASK-14818-T's row; M-15 might mask M-13 if AC-C3a's fixture reuses the applies-from date.
- *Fable 5.2:* **READY-WITH-WARNINGS**, 0 BLOCKER / 2 WARNING / 6 NOTE (`.claude/reviews/PLAN-s148-reviewer-c2.md`). Every cycle-1 finding is CLOSED in the body at the place it acts; the new design holds against the code; the counts recompute exactly.
  - **W-1:** two more "this name appears nowhere" criteria (AC-C1h, AC-C7) have hits in files their tasks may not edit.
  - **W-2:** the settled-year row's "look from" date has no value when a holiday year's window cannot be placed.
  - **NOTEs:** N-1 = Codex W-3; N-3 = Codex W-2; N-5 = Codex's NOTE. Also N-2 (three test-only override writers would skip the version row), N-4 (AC-C2p (1) names no date) and N-6 (stale text).
- *Adjudication (`adjudicator`, Fable 5.2):* **RULED. All accepted, none declined, no owner question** (`.claude/reviews/PLAN-s148-adjudication-c2.md`).
  - **NR-P16 (Codex W-1):** a publish, and an override's version row, is stamped when it activates (`clock_timestamp()`), not when its transaction began (`NOW()`), so "the later publish wins" holds under concurrency. New AC-C2p (4); AC-C2p's facts run on an injected Monday.
  - **NR-P17 (Codex W-2 / Fable N-3):** the editor offers a Monday only when the server would accept it; otherwise it says next Monday is the earliest date.
  - **NR-P18 (Codex W-3 / Fable N-1):** M-2 … M-5's mutation worktree is detached at 14826-T's branch tip.
  - **NR-P19 (Fable W-1):** TASK-14826 and TASK-14817 own every line their own "appears nowhere" greps return.
  - **NR-P20 (Fable W-2):** a settled year whose window can't be placed is dated from 1 January of its entitlement year. New AC-PIL-h (11).
  - **NR-P21 (Fable N-2):** the override version row is written inside the repository, so no writer can skip it; two test-only writers retire.
  - **NR-P22 (the M-15/M-13 NOTE, `(unproven)`):** settled by fixture conditions, not promoted. NR-P15's condition goes into TASK-14818-T's row; AC-C3a's changed publish carries a later date than the version it replaces.
  - **Plan draft 1.4** (`planner`) carries NR-P16 … NR-P22 into the body; the counts are unchanged and nothing was left out. The planner raised two more points. **Draft 1.4.1:** the Orchestrator ruled them.
    - **NR-P23:** commit `636bc7da` is named in the header, the push note and Step 7a's carried items.
    - **NR-P24 (b):** AC-C2p (4) is written against code the sprint creates, so it is an N fact. It is proved by a ninth Docker mutation, **M-16** (the publish stamped at transaction start), in CI-M4b beside M-10 and M-14, with NR-P15's kind of fixture condition. Still four CI mutation runs.

**Cycle 3 (2026-11-10, on draft 1.4.1).**
- *Codex:* CHANGES-REQUIRED, 0 BLOCKER / 2 WARNING (`.claude/reviews/PLAN-s148-codex-c3.raw.md`). All four cycle-2 findings are CLOSED in the body. AC-C2p (4)'s reversed-start fixture is buildable, and M-15 and M-16 each discriminate. The counts are verified (115 ids; 64 tasks; nine Docker mutations in four runs). Both warnings concern CI-M2, unchanged since draft 1:
  - **W-1:** the frozen failure list names the vocabulary-reflection fact, which M-8 leaves green, and omits the classification-completeness fact, which M-8 turns red.
  - **W-2:** M-8's expected Unit failure stops CI before the Regression step, so `ChildSickCallHomePayrollTests`' RED is never observed.
- *Fable 5.2:* **READY-WITH-WARNINGS**, 0 BLOCKER / 2 WARNING / 4 NOTE (`.claude/reviews/PLAN-s148-reviewer-c3.md`). Every cycle-2 finding and every ruling NR-P16 … NR-P24 is carried into the body; the counts recompute exactly.
  - **W-1:** the hand-written test-database copies (`ForcedRollbackHarness.cs`, which the plan doesn't name, and `TxContractTests.cs`) lack the new override-history table that every override write now inserts into.
  - **W-2:** CI-M5's frozen list omits the wave-4 read-half facts that M-9 and M-15 turn red again. This is the same class as Codex's CI-M2 findings.
  - **NOTEs:** N-1 (unproven: no override twin of M-16), N-2 (unproven: M-10's frozen list), N-3 and N-4 (wording).
- *Adjudication (`adjudicator`, Fable 5.2):* **RULED. All accepted, none declined, no owner question** (`.claude/reviews/PLAN-s148-adjudication-c3.md`).
  - **NR-P25:** one general rule for CI mutation runs. Each evidence branch carries an "EVIDENCE ONLY" `ci.yml` patch: `if: ${{ !cancelled() }}` on the DemoSeed, init.sql and Regression steps, so the Docker tests run after an expected Unit failure. Master's CI is unchanged. A run's frozen list is every fact the mutation turns red, from any wave and suite. An unlisted red is recorded as a census miss when the mutation explains it, and is a defect otherwise.
  - **NR-P26:** M-8's frozen list is corrected: the classification-completeness fact goes red, and the vocabulary-reflection fact stays green.
  - **NR-P27:** CI-M5's list includes the wave-4 read-half facts that M-9 and M-15 turn red again.
  - **NR-P28:** `ForcedRollbackHarness.cs` and `TxContractTests.cs` gain the override-history table in their hand-written schema copies.
  - **N-1 (unproven):** settled by inspection at TASK-14818's 5a, which quotes the version INSERT's column list. **N-2 (unproven):** closed by a fixture condition. **N-3 and N-4:** wording.
  - **Plan draft 1.5** (`planner`) carries NR-P25 … NR-P28 and the four NOTE dispositions into the body. The counts are unchanged: 115 ids; 64 agent tasks; nine Docker mutations over eight AC ids in four runs. The id check and stale-text sweep came back clean, and no new ruling was raised.

**Cycle 4 (2026-11-10, on draft 1.5).**
- *Codex:* **READY**, 0 BLOCKER / 0 WARNING (`.claude/reviews/PLAN-s148-codex-c4.raw.md`). Its cycle-3 W-1 and W-2 are CLOSED in the body.
  - NR-P25 verified against `ci.yml` and GitHub's expression and pull-request semantics: `!cancelled()` lets Regression run after a failed Unit step; `test-results` uploads with `always()`; no job depends on `build-and-test`; the PR run uses the branch's workflow.
  - The harness copies match their writers, and the counts are verified.
- *Fable 5.2:* **READY-WITH-WARNINGS**, 0 BLOCKER / 1 WARNING / 3 NOTE (`.claude/reviews/PLAN-s148-reviewer-c4.md`). Every cycle-3 finding and NR-P25 … NR-P28 is carried; the workflow patch is verified against `ci.yml`.
  - **W-1:** M-8 removes the call-home code from the classification, and the Skema save refuses an unclassified code (`SkemaEndpoints.cs:970`). So the Docker pin likely fails at the save, before the "credited and exported" assertion, and rule 5 would log it as proof of the payroll credit.
  - **NOTEs:** N-1 (a green non-pin census fact), N-2 (smoke and e2e artifacts), N-3 (two local censuses).
- *Orchestrator rulings (draft 1.5.1):* small and mechanical, so ruled on the Opus seat as NR-P15 and NR-P24 were; the cycle-5 lenses verify them.
  - **NR-P29:** a pin counts as proved only when its CI failure message is the row's "must trip" assertion. The M-8 row and TASK-14811-T say so; if the save gate trips first, the claim is narrowed and the credit half rests on the Unit census.
  - **NR-P30:** a green non-pin census fact is an over-claim, with no re-run.
  - **NR-P31:** rule 4 names the smoke and e2e artifacts; rule 1 the two local censuses.

**Cycle 5 (2026-11-10, the last under the cap; a verification of the 1.5.1 diff only).**
- *Codex:* **READY**, 0 BLOCKER / 0 WARNING / 1 NOTE (the header's stale cycle number, fixed) (`.claude/reviews/PLAN-s148-codex-c5.raw.md`).
- *Fable 5.2:* **READY**, 0 BLOCKER / 0 WARNING / 2 NOTE (`.claude/reviews/PLAN-s148-reviewer-c5.md`). All four cycle-4 items are CLOSED, and the cited code and workflow lines are verified.
  - **N-1 `(unproven)`, carried to the plan's M4 list:** if M-8 is narrowed to the save gate, the "exported under `SLS_0530`" half has no red evidence from M-8. It is settled at CI-M2: either the mutation table says so in words, or 14811-T proposes a second mutation that passes the save.
  - **N-2:** the same header phrase, fixed.

**Step 0b: READY on both lenses (2026-11-10).** The plan is **final as draft 1.5.1**.
- *In plain words:* five review rounds per lens made the plan more exact without changing what S148 delivers. The one design defect, a backdated new hire who could not be paid, was found in round 1 on the first Fable read of the refinement. Rounds 3–5 tightened how we prove each new test can fail.
- *Rulings:* NR-P7 … NR-P31. Three adjudications ran on Fable 5.2; the Orchestrator ruled NR-P15, NR-P23, NR-P24 and NR-P29 … NR-P31 as small mechanical points.
- *Counts:* 115 acceptance criteria, each with one owner; 64 agent tasks (40 dispatch ids, 24 paired test passes) in waves 0–7; nine Docker mutations over eight criteria in four CI evidence runs.
- *For the model-routing register (Step 0b chain, descriptive):* Codex 5 cycles (B/W/N: 0/2/0 → 0/3/1 → 0/2/0 → 0/0/0 → 0/0/1); internal lens 5 cycles, all on Fable 5.2 (1/4/7 → 0/2/6 → 0/2/4 → 0/1/3 → 0/0/2); 3 adjudications on Fable 5.2.

## Wave 0 — the pay-unchanged pin

**TASK-14800** (`test-qa`; PIN-1, PIN-1b, PIN-2).
- **Brief:** `.claude/plans/s148/TASK-14800.md`, drafted by the `planner` from the final plan and committed at `19f642bc`. The planner listed nine brief contradictions, all ruled as drafted. The two that matter most:
  - **BC-5:** the handler seam must never dispose a handler it was given, because the loader disposes its sibling clients after every load.
  - **BC-7:** PIN-1 runs payroll with the real Rule Engine, since a stubbed engine would make the golden blind to the engine changes the sprint makes.
- **NR-P32 (at brief-writing):** PIN-1's non-vacuity also requires the loader's approved count to equal the generator manifest's, with no loader outcome failure (FAIL-006), so a month the loader silently failed to approve cannot shrink the baseline unseen.
- **Dispatched** 2026-11-10 in a worktree from local master `19f642bc`.
- **Returned** on branch `worktree-agent-a5532be7a5ccf9174`, tip `3ae12437`, one commit, 9 files: new `tests/StatsTid.Tests.Regression/PayPin/**`, the Regression csproj, and `ApiClient.cs` (one constructor, `CreateSibling`).
  - **Seeds:** 47 for the December 2025 world and 45 for the June 2026 world, each with 3 APPROVED months, scanned from 43.
  - **Agent's gate:** build 0 errors / 144 warnings / CA2100 114; Unit 1722, DemoSeed 170 and Regression non-Docker 138, all green. M-1a and M-1b each turned both PIN-1b facts red, observed at `3ae12437`.
  - **PIN-2:** reads the compiled tables by reflection (10 agreement/OK keys, 4 position keys) and compares them through the repositories.
  - **`(unproven)`, settled by CI-W0:** PIN-1 end to end, meaning the loader's two-signer path over an in-process handler and payroll accepting every APPROVED month at the base.
  - **Found, not fixed:** `DemoLoader.cs:520` reads the real clock for a profile-edit date. It is argued harmless to the text, since that date is always after both activity months.
- **CI-W0:** draft PR #53 (`ci/s148-w0` at `3ae12437`, a throwaway, to be closed unmerged), started 2026-11-10 in parallel with 5α and 5a. If a review changes the canonical text, CI-W0 is re-run.
- **5α (constraint-validator):** PASS. All nine files authorized, no `docs/**` change, no golden file (`.claude/reviews/S148-TASK-14800-5alpha.md`).
- **5a, internal lens (Fable 5.2):** **APPROVED**, 0 B / 0 W / 5 N (`.claude/reviews/S148-TASK-14800-5a-reviewer.md`). It found no way for the pin to pass while a demo payslip changes. Four of the notes are `(unproven)` with stated evidence; none is promoted:
  - the loader's real-clock edit date;
  - the pin covers the lines payroll returns, not the lines it stores;
  - the copied Backend stub;
  - one exclusion list for both worlds.
- **5a, external lens (Codex):** APPROVED-WITH-WARNINGS, 0 B / 2 W / 1 N (`.claude/reviews/S148-TASK-14800-5a-codex.md`).
  - **W-1:** the header's date uses the machine's culture. Fable's first note says the same.
  - **W-2:** lines tied on every sort key, which differ only in `PeriodEnd` or in null versus empty, keep their input order.
  - Both are accepted as a fix-up by the same agent: an invariant-culture header and appended tie-breakers, each with a non-Docker fact. Neither should move a byte of the CI-W0 capture on a Gregorian-calendar runner unless a pinned world holds a tied pair; CI-W0b would show it.
- **Fix-up** `920dcd72` (same agent): the header is extracted into `PayLineCanonicaliser.Header(...)`, formatted with the invariant culture, and tie-breakers are appended (`PeriodEnd`, then the full printed line, ordinal). Two non-Docker R facts were added: `Header_IsCultureInvariant` and `ReversedInput_GivesIdenticalText`. **Codex verification:** APPROVED (`.claude/reviews/S148-TASK-14800-5a-codex-c2.md`); W-1 and W-2 are closed, and each fact fails when its fix is removed.
- **Merged** to local master as `4e38a682`. **GATE** green: build 0 errors, CA2100 114; Unit 1722, DemoSeed 170, Regression non-Docker 140 (138 + 2).
- **CI-W0 result** (run 42805305917, PR #53, at `3ae12437`): `build-and-test` red **only** on the two PIN-1 facts, each failing with `golden capture` as designed. Every other check passed:
  - Unit 1722, DemoSeed 170, Regression 2440 of 2442 (Docker included);
  - PIN-2 green (`EveryCompiledAgreementOkPosition_DatabaseConfigEqualsCompiled`);
  - smoke, E2E, frontend build, docs, lizard and gitleaks green.
  - The `(unproven)` item is now settled: the loader approved every month the manifest planned (3 = 3 in both worlds), and payroll calculated all three with no refusal.
  - **December 2025 world** (seed 47): OK24, 84 lines, 3 records.
  - **June 2026 world** (seed 45): OK26, 86 lines including 2 `OVERTIME_CALC`, 3 records.
  - The two worlds therefore pin both OK versions.
- **Fix-up versus the capture:** CI-W0 ran before `920dcd72`. Re-sorting the captured lines with the fix-up's full key leaves both texts byte-identical, since each world has one period end and no null or empty rule id. CI-W0b is expected to compare green.
- **O-1, goldens committed:** each world's text between the markers, verbatim from the run's trx, LF-only (`PayPin/Golden/demo-2026-01-15.golden.txt` 93 lines, sha256 `8ca6ceef…`; `demo-2026-07-15.golden.txt` 95 lines, sha256 `a051b25f…`). No exclusion was needed; `ExcludedSeeds` stays empty.
- **CI-W0b** (run 42816549335, draft PR #54 on `ci/s148-w0b` at `0ed6f150`): **green**. Unit 1722, DemoSeed 170, Regression 2444 of 2444; both PIN-1 facts pass against the committed goldens. PIN-2, PIN-1b and the seed-selection pin are green. Smoke, E2E, frontend build, docs, lizard and gitleaks are green. PRs #53 and #54 are closed unmerged; nothing reached `origin/master`. **Wave-1 merges are unblocked.**

## Wave 1 — stage 0's independent fixes

**Briefs.** Three `planner` runs (Opus) drafted twelve briefs from the final plan into `.claude/plans/s148/`, verifying them at `0ed6f150`:
- TASK-14801, 14801-T, 14802, 14802-T and 14803 (the payroll admission chain);
- TASK-14804, 14804-T, 14805 and 14805-T (the paid-absence bound and compensate);
- TASK-14806, 14806-T, 14807 and 14808 (the orphan list, the stage-0 label and two pin sets).

Every census matched the plan's counts. The route-retirement census found 15 posting files in all: the plan's 13 literal-URL posts, plus the smoke suite and one inherited helper.

**Adjudication** (`adjudicator` on Fable 5.2, `.claude/reviews/S148-wave1-briefs-adjudication.md`, RULED-WITH-QUESTIONS). It ruled on 22 open items, 21 of them itself and one put to the owner. The ones that change behaviour or scope:
- **A1:** the four-eyes refusal goes on `/calculate-and-export` only. `/recalculate` has no approval read, by ADR-034's design.
- **A3:** 14801-T adds a race for the record-less zero-line month (S147's R-F1 path), which would otherwise lose its four-eyes pin.
- **A4:** `RequestValidator.cs` and its unit tests are deleted together, so the build never goes red between them.
- **A5:** the typed-entry check uses the calculation's own window, through `HoursReadStart` made `internal`, so the two windows cannot drift.
- **B2:** wave 1's bound counts intervals plus manual hours as worked; 14809 removes the manual term in wave 2.
- **B3:** a day with no dated profile is not bounded, as today's cap; recorded as found-not-fixed.
- **B4:** the 422 body keeps today's string, so the screen's alert still shows, and gains `kind`, `plannedHours` and `workedHours`. TASK-14810 writes the precise Danish wording.
- **B9:** two `-T` passes touch `SendConcurrencyTests.cs`. The 14801 → 14802 → 14803 stack merges first, and AC7d must be seen green by name at CI-W1.
- **C2:** AC-21a-d's fixture widens to the shapes where R-19's readings differ. 14806 reports who leaves and who joins the orphan list.
- **C3:** AC-5a corrected to the month-level row (the plan's AC row and fact name changed).

Kinds moved: AC-S0-7e to F; AC-21a-b and -c to R. The plan's "final + 2" row carries these.

**Owner Q1 (2026-11-10), "D2 governs (Recommended)":** may a care day (omsorgsdag) or senior day sit on a day that also holds work? No. S73's ruling D-A made both full-day only and ratified that same-day work stays legal. ADR-043 D2, which the owner accepted later, bounds a day's paid absence by planned minus worked and names care days. Together they make a full care day impossible on any worked day; nobody had shown the owner this interaction. Under the ruling:
- a care or senior day goes only on a day with no work;
- someone who leaves mid-day records their hours and covers the rest with flex or holiday hours;
- a day is never worth more than planned;
- S73 D-A's work clause is retired, and `SkemaFullDayOnlyGuardTests.FullDayPlusSameDayWorkHours_Allowed200` is re-pinned to the 422 by 14804-T.

The alternative, a care day covering "the rest of the day", was not chosen for S148.

**Told to the owner** (2026-11-10, no decision asked):
- **A6:** payroll refuses stored `timer` rows. ADR-028 D5 said they count as ordinary time, but after a reseed none exist.
- **C3:** a særlige feriedage config ending mid-month removes the row for that whole month. This is pinned and registered as a known limit, not a defect.

**Docs owed (Orchestrator, before close):**
- ADR-028 D5 and D6 amendment notes;
- DEP-004's row;
- `docs/FRONTEND.md:115`, `:185`, `:217` and `:292` (the retired route);
- a line in the legacy-database upgrade runbook that stored `timer` rows are refused;
- ADR-043 D2: a note that it supersedes S73 D-A's same-day work clause (owner Q1);
- the AC-5a month-level limit registered (QUAL);
- the 14802-T wire-level atomicity proof recorded as a test-gap row;
- **a comment-only sweep** of the eleven comments the wave-1 changes leave stale (PAT-032; adjudication A9).

**Briefs draft 1.1** (a `planner` applied the adjudication to all thirteen brief files; the Orchestrator then filled in owner Q1). The Orchestrator's rulings on the planner's four remaining questions:
1. `SkemaEndpoints.cs:804` and `init.sql:1868` join the comment-only sweep.
2. The bound's `kind` is fixed as `"paid-absence-day-bound"`, so that 14804-T and TASK-14810 can rely on it.
3. The planner's labelled hints in 14808, 14806-T and 14802-T stay.
4. B9's merge order applies to TASK-14804 itself, not only to its test pass.

One fact corrected along the way: A9's `cref` premise does not hold. No project sets `GenerateDocumentationFile`, so the compiler does not check doc-comment references, and the deleted validator leaves only a dangling comment.

**Dispatched 2026-11-10** from local master `a03d61ae`, each in its own worktree: TASK-14801, 14804, 14806 and 14807. TASK-14805 and 14808 wait for a free slot, so that too many builds do not run at once on this machine. 14802 → 14803 follow 14801's merge.

**TASK-14801** (`payroll-integration`, Opus).
- **Returned** on `worktree-agent-a926cc928b39a3fab` at tip `e7fcb023`, with one commit changing two files (`Program.cs`, `PayrollExportService.cs`). Report: `.claude/reviews/S148-TASK-14801-report.md`.
- **What it does:** payroll now checks "the approver is the sender" and "the approver is the employee" right after reading the month's approval, before calculating. It answers its own 409 `payroll-four-eyes`, and writes nothing. The in-transaction re-check gives the same answer, and both sites call one rule, `PayrollFourEyesException.FailedClause`.
- **Agent's gate:** build 0 errors / 144 warnings / CA2100 114; Unit 1722, DemoSeed 170, Regression non-Docker 140.
- **Expected Docker reds, which 14801-T re-pins:**
  - `FlexLedgerCase3aHostTests` `:291` and `:318` (the kind);
  - `PayrollSenderSeparationHostTests` `:96`, `:114` and `:132` (the exception type).
- **Declared deviation, accepted by the Orchestrator:** the in-transaction query reads `employee_approved_by` and `submitted_by` raw rather than a SQL `COALESCE`. This keeps the sender rule in one C# home, as RES-003 asks, instead of a SQL-to-C# mirror. No other task owns those lines (14831 owns `:262-286`).
- **Found, not fixed:** `PriorMonthNotApprovedException.cs:35-39` says "Nothing was calculated, written or exported". The export-time re-check uses the same body after the manifest has already been written. It is Low severity and outside 14801's files, so it goes to the register at close.
- **Docs owed:** ADR-034's line citations into the payroll host have shifted; QUAL-246's row (O-3).
- **TASK-14801-T dispatched** on `e7fcb023` (`test-qa`).
- **TASK-14805 dispatched** (`backend-infrastructure`), when a slot freed.

**TASK-14807** (`ux`).
- **Returned** on `worktree-agent-a0a2101a4c01b8608`, tip `e1673e46`, changing 4 files. The notice is identical on the agreement editor, `ProfileEditor.tsx` and the position-override page: "**Bemærk:** Indstillingerne her påvirker skærmbilleder og booking, men endnu ikke lønberegningen. Hviletidskontrollen læser stadig de indbyggede værdier." AC-S0-8a adds 5 vitest facts, all red with the pages reverted. The five frontend checks exit 0.
- **5α:** PASS (`.claude/reviews/S148-TASK-14807-5alpha.md`).
- **5a (Fable 5.2):** APPROVED-WITH-WARNINGS, 0 B / 2 W / 4 N (`.claude/reviews/S148-TASK-14807-5a-reviewer.md`). Both warnings were accepted as a fix-up by the same agent.
  - **W-1:** `Alert` is `role="alert"`, so screen readers announce the notice as an interruption, again after every save. It becomes a plain static note.
  - **W-2:** the test matched only "endnu ikke", so the pay clause could vanish with a green suite. The fix-up asserts the three clauses in order, plus a fact that the element is not a live region.
  - **NOTE (unproven), carried to wave 5:** payroll reads a local profile's activation date to split a period (`PeriodCalculationService.cs:2286`). If a split alone can change a pay line, then "not yet pay" is not strictly true for the profile editor.

**TASK-14804** (`backend-infrastructure`).
- **Returned** on `worktree-agent-a7b9324ca7deb4f36`, tip `cf0d4aef`, changing 2 files: `SkemaEndpoints.cs` +188, and `SkemaConsumptionValidationException.cs`, whose summary comment now names its four failure modes. Report: `.claude/reviews/S148-TASK-14804-report.md`.
- **What it does:** one function checks the bound at two insertion points: an advisory check before the transaction, after every existing refusal, and an authoritative check inside the per-employee lock, before any write. Today's cap answers first on a day both would refuse. The body is `kind: "paid-absence-day-bound"`, with `plannedHours` and `workedHours`.
- **Agent's gate:** build 0 errors / 144 warnings / CA2100 114; Unit 1722, DemoSeed 170, Regression non-Docker 140.
- **Expected Docker reds:** `Adr032ConsumptionPinTests.cs:240` (a weekend sick day) and `SkemaFullDayOnlyGuardTests.cs:414` (owner Q1). Both are intentional; 14804-T re-pins them. AC7d now fails by timeout, because the bound refuses its save before it queues on the lock; 14804-T repairs the fixture.
- **Shortcut (PAT-032), registered at merge:** the bound reads the § 7 nr. 2 per-day norm, not the configured credit basis (`SkemaEndpoints.cs:1868-1870`). Upgrade when stage 1 serves the one planned-hours figure.
- **Found, not fixed:**
  - A day with no dated profile has no limit at all (ruled B3).
  - **A day can be locked with no way out in Skema.** An absence cell cannot be cleared, because hours must be above 0 (SPRINT-72 R17). So an employee who recorded a sick day by mistake, and actually worked, can no longer add the work. A day stored over the bound before this change refuses every later save that touches it. This goes to the 5a lenses for verification before anything is put to the owner.
  - Two stale comments (`:2550-2553`, `:2137-2138`) join the comment sweep.
- **TASK-14804-T dispatched** on `cf0d4aef`.

**TASK-14801-T** (`test-qa`).
- **Returned** on `worktree-agent-ab79b8abde8c799bf`, tip `11172d86`, on top of `e7fcb023`. It changes three test files.
  - New `PayrollFourEyesRefusalTests`, two theories, kind F:
    - AC-12b-a: admission refusal, rows "sender" and "employee", including that the rule engine is never called and no manifest is written;
    - AC-12b-b: the in-transaction race, rows "tracked June" and "record-less zero-line April" (ruling A3). It is forced by a `FOR UPDATE` holder and a `pg_stat_activity` condition wait.
  - Five facts re-pinned to the new kind and type, each labelled `// S148 P-5:`.
- **Gate:** build 0 errors / 144 warnings; Unit 1722, DemoSeed 170, Regression non-Docker 140. Report: `.claude/reviews/S148-TASK-14801-T-report.md`.

**TASK-14801 + -T, reviewed and merged.**
- **5α:** PASS (`S148-TASK-14801-5alpha.md`).
- **5a (Fable 5.2):** APPROVED, 0 B / 0 W / 5 N (`S148-TASK-14801-5a-reviewer.md`). No path pays a self-approved month through `/calculate-and-export`. `??` and the old `COALESCE` agree for every row. The race cannot pass without racing. The notes, none promoted:
  - `FailedClause` has no Docker-free pin;
  - an empty-string `employee_approved_by` would hide a legacy sender. This is `(unproven)`, pre-existing, and the same in the Backend's guard;
  - the HTTP race covers the sender clause only;
  - the race's wait filter is not row-specific (safe: own container, sequential rows);
  - docs owed.
- **5a (Codex):** APPROVED, 0 B / 0 W / 1 N (`S148-TASK-14801-5a-codex.md`; Docker still pending).
- **Merged** to local master as `c18d525b`. **GATE** green: build 0 errors, 144 warnings, CA2100 114; Unit 1722, DemoSeed 170, Regression non-Docker 140.
- **Docs owed at close:**
  - QUAL-246 closes (O-3);
  - ADR-034's line citations into the payroll host;
  - register the `PriorMonthNotApprovedException.cs:35-39` sentence (Low).
- **TASK-14802 dispatched next**, from `c18d525b`.

**TASK-14807 fix-up** `fb90143f`.
- The notice is now a static `<p role="note">` on all three pages, styled from the info tokens. The two `Alert` imports were removed.
- The test asserts the three clauses in order, and that the element is not a live region. Each mutation turned 2 facts red.
- The five frontend checks exit 0, read unpiped (vitest 85 files / 1094 tests).
- **5α on the fix-up:** PASS (`S148-TASK-14807-5alpha-c2.md`). Spacing is raw px, matching each file's own neighbouring rules; colours are tokens.
- **Unrelated flake** on a first vitest run: `OpfoelgningPage.test.tsx`, "OPEN state … at a 'November' anchor", got '0' for '1'. It passed alone and on a full rerun; it may depend on the date. It is recorded for the Fable verification.

**TASK-14804-T** (`test-qa`).
- **Returned** on `worktree-agent-a54f2710ce52466ec`, tip `c01758bd`, on top of `cf0d4aef`.
- **AC-S0-7a … 7e:** five new Docker facts, every refusal asserting `kind`. AC-S0-7e is F, a pg_locks-ordered race on Friday 12 June 2026.
- **Re-pins:**
  - `Guard_WeekendSickDay_Allowed_LegacyBehavior` → `…_Rejected422_PaidAbsenceDayBound`;
  - `FullDayPlusSameDayWorkHours_Allowed200` → `…_Refused422` (owner Q1);
  - the AC7d fixture moved to Saturday 14 March 2026, so the work save reaches the lock.
- **Gate:** build 0 errors / 143 warnings / CA2100 114; Unit 1722, DemoSeed 170, Regression non-Docker 140.
- **Flagged for the Orchestrator:** `Adr032ConsumptionPinTests.cs:283` and `SkemaFullDayOnlyGuardTests.cs:393` assert only `error` and `maxHours`, so they would pass if the bound answered instead of the cap.
- **5α:** PASS (`S148-TASK-14804-5alpha.md`). The two 5a lenses are running; both were asked to verify the "a day can be locked" finding.

**TASK-14805** (`backend-infrastructure`).
- **Returned** on `worktree-agent-a65134109752c498f`, tip `aede6e63`, a single file: `OvertimeEndpoints.cs` +20 / −10.
- **What it does:** compensate reads the guarded update's `Success`; on false it rolls back and answers 409 before the enqueue. The 200 path is byte-identical. The stale clause at `:563` was rewritten (ADR-043 D8, stage 2).
- **Gate:** build 0 errors / 144 / 114; Unit 1722, DemoSeed 170, Regression non-Docker 140. No existing fact expected red. Report: `.claude/reviews/S148-TASK-14805-report.md`.
- **Found, not fixed, to register at close:**
  - F-1 (M): the compensation-choice PUT reads the balance and writes all three figures back non-atomically, so a compensation committed in between is overwritten. It is unreachable today and retires in stage 2.
  - F-2 (M): the same PUT records no event and runs outside the outbox, although the model matters for pay. It is reachable today.
  - F-3: QUAL-239's citations and the audit catalog's `:170` label.
- **TASK-14805-T dispatched** on `aede6e63`.

**TASK-14806** (`backend-infrastructure`).
- **Returned** on `worktree-agent-ab037c8fc8947599f`, tip `5c76266f`. It adds a new `ApproverCoverage.cs` with one predicate behind both the roster card and the HR roll-up, and passes `today` at `:207` (C1). Report: `.claude/reviews/S148-TASK-14806-report.md`.
- **R-19 reading (i):** "someone's approver" means another member reports to them on an open PRIMARY line. No stand-in drops off the list, so C2 has nothing to bring back.
- **Cost:** the card's Org1 count is now 5, exactly at the guard's ≤ 5 bound (CI-pending).
- **Requirement-level gap, to the adjudicator:** "shape F". The top manager G departs with nobody above, M reports to G, and R reports to M. M is uncovered, but under every reading M counts as "someone's approver", so M is classed Root and stays off the orphan list. Nobody is named to approve M's month. This is not a regression (today M reads as covered), but it is not surfaced either.
- **TASK-14806-T** was refused once: the harness found a half-created worktree with unresolvable metadata. That worktree was confirmed clean and removed, and 14806-T was redispatched on `5c76266f`.

**TASK-14807, merged** to local master as `ece51892`.
- **Frontend gate:**
  - tsc, lint, typecheck:e2e and build all exit 0.
  - The first vitest run was red on 1 of 1094: the known `OpfoelgningPage` flake ("OPEN state … at a 'November' anchor" read '0' for '1' under machine load).
  - **Flake fixed** under the small-task exception (one test-only line, in a file no S148 task owns). The test now waits on the count's text, not on the element's presence, because the tile renders its count as 0 before the fetch resolves. This is the cause Fable's c2 review traced.
  - The file passes 21/21, and a full vitest rerun is green: 85 files, 1094/1094.
  - The .NET gate is unchanged from `c18d525b`, because no .NET file changed.
- **Carried to wave 5's brief (TASK-14829):** its authorized files gain the two CSS modules' `.stage0Label` class. The class's px spacing equals that of its sibling rules (Fable c2 NOTE); this is recorded as a declared deviation.

**TASK-14805 + -T, reviewed and ready to merge.**
- **TASK-14805-T** returned on `worktree-agent-a5a94c394e9bd1b0c`, tip `0acde975`. It adds `OvertimeCompensateRaceTests.TwoConcurrentCompensationsOfSix_One200_One409_OneOutboxEvent`, kind F. A row lock holds both requests at the guarded UPDATE; the test observes one waiter, then two, then releases. Gate green. Report: `S148-TASK-14805-T-report.md`.
- **5α:** PASS (`S148-TASK-14805-14806-5alpha.md`).
- **5a (Fable 5.2):** APPROVED-WITH-WARNINGS, 0 B / 1 W / 4 N (`S148-TASK-14805-5a-reviewer.md`).
  - The warning is F-1: the compensation-choice PUT can erase a committed payout. It is unregistered and not reachable today; it is registered at Medium at close.
- **5a (Codex):** APPROVED-WITH-WARNINGS, 0 B / 2 W / 4 N (`S148-TASK-14805-5a-codex.md`).
  - The two warnings are F-1 and F-2, both pre-existing and outside scope; both are registered at close.
- **Not built (recorded):** a race on the AFSPADSERING branch. Both lenses rate it a NOTE.

**TASK-14806 + -T, reviewed.**
- **TASK-14806-T** returned on `worktree-agent-a7f734124e435385e`, tip `ae3968a0`. It adds four `OrphanCoverageTests` facts:
  - AC-21a-a, kind F;
  - AC-21a-b and -c, kind R, each with a discriminating assertion;
  - AC-21a-d: card = roll-up for every person in shapes A, B, D and E.

  Report: `S148-TASK-14806-T-report.md`.
- **5α:** PASS.
- **5a (Fable 5.2):** APPROVED-WITH-WARNINGS, 0 B / 2 W / 7 N (`S148-TASK-14806-5a-reviewer.md`).
  - W-1: shape F.
  - W-2: three stale comments outside the task's files.
- **5a (Codex):** APPROVED-WITH-WARNINGS, 0 B / 2 W / 3 N (`S148-TASK-14806-5a-codex.md`).
  - W-1: shape F.
  - W-2: the card read "today" more than once (projection `:661`, name lookup `:1350`), against contract 4(e).
- **Adjudication** (`S148-TASK-14804-14806-adjudication.md`, item 2a): **shape F is fixed in S148** with a fourth reading. Root = the walk names nobody AND the person has no PRIMARY line of their own (ADR-027 D9's literal root) AND someone reports to them. Every other uncovered person is an Orphan. Shapes A, B, D and E are unchanged; only shape F's middle manager moves from Root to Orphan. No new database read.
- **Fix-up `38b800f6`** (same agent, on top of `ae3968a0`):
  - the fourth reading, with the docs aligned in both callers;
  - Fable W-2's three stale comments, comment only (`HrFollowUpApprovalResponses.cs:173`, `:208-209`; `Program.cs:192-194`). No OpenAPI drift: 0 matches for the old sentences;
  - **Codex W-2 fixed:** the card now reads "today" once (`:1102`). A private overload threads it into the projection and the name lookup. The public `GetPeriodStatusProjectionForTreeAsync` keeps its signature and behaviour for `AdminEndpoints.cs:3505`.
  - Gate: build 0 / 144 / 114; Unit 1722, DemoSeed 170, Regression non-Docker 140.
- **14806-T fix-up dispatched:** the shape F fact `ManagerOfManagersLosesApprover_IsOrphan_CardEqualsRollUp` (kind F) and the `HrFollowUpApprovalEndpointTests.cs:399-402` docstring. Both lenses then re-review the fix-up delta.

**TASK-14804 + -T: review outcome and adjudication** (`S148-TASK-14804-14806-adjudication.md`, items 1a–1e).
- **5a (Fable 5.2):** APPROVED-WITH-WARNINGS, 0 B / 4 W / 3 N.
- **5a (Codex):** CHANGES-REQUIRED, 1 B / 4 W / 1 N.
- **1a, Codex BLOCKER dismissed** as an Orchestrator prompt error. The prompt wrote the rule as "max(project, intervals) + manual". The brief and ruling B2 define "worked" as the larger of project hours and interval-plus-manual hours. The prompt's version would count manual hours twice whenever they are also allocated to a project. Codex re-runs on the fix-up with the rule stated correctly.
- **1b, precedence accepted as chosen.** Two refusals that are decided only inside the lock no longer take precedence over the bound: the employment-window gate and the authoritative quota guard. Both are still 422s, nothing is written, and no employment date is exposed. The comment now says so. Moving code was rejected, because it would either double-count replaced cells or run writes for a save already known to be refused.
- **1c, to the owner.** See owner Q2 below.
- **1d, test fix-ups owed now, in 14804-T:**
  - AC-S0-7c asserts `totalHours` 4,5 and re-saves 4,4 → 200, so it proves the replaced-cell exclusion;
  - a new R fact: today's cap answers first, with no `kind`;
  - a new F race: an absence save against a work save.

  Recorded, not owed: facts for B3, B5 and `TIME_POST`. B5 is `(unproven)` for M4.
- **1e:** the legacy route bypasses the bound until 14802 merges. B9's merge order (14802's stack first) is the answer. After 14804 merges, grep that only the Skema save writes the projections.
- **14804 fix-up `8415a2a2`** (comment-only, on top of `c01758bd`). Build 0 / 144 / 114. It adds:
  - the precedence paragraph;
  - the quota-guard sentence;
  - a catch comment naming all four in-lock refusals;
  - `shortcut(pending)` at `SkemaEndpoints.cs:1790` ("an absence or project cell cannot be cleared … upgrade when cell clearing ships").
- **14804-T fix-up dispatched** (facts 5–7).

**Owner Q2 (2026-11-10), "Build it in S148 (Recommended)":** may an employee empty a Skema cell? Yes, built in S148.
- **Today's limitation:** a cell can be changed but never emptied, because hours must be above 0 (SPRINT-72 R17), and there is no delete route.
- **Why it matters now:** under the new paid-absence bound, a mistaken entry can lock a day.
  - A mistaken full sick day blocks recording the real 7,4 h.
  - A mistaken care or senior day (full-day only) blocks all work that day.
  - A mistaken project entry must keep at least 0,01 h.
  - HR has no tool to help.
- **How it is built** (the adjudicator's lean): a new task pair, a backend task with its test pass plus a stacked frontend task, in wave 2 after 14811 in the `SkemaEndpoints.cs` order. It gets its own plan addendum and Step 0b review. 14804 merges first, with the limit marked by `shortcut(pending)`.
- **What clearing must preserve:**
  - the removal is its own audited event with the actor, never a 0-hour row or a silent delete;
  - the approval lock and the month bound refuse a clear exactly as they refuse a save;
  - clearing a holiday-type cell returns the days, with the balance event;
  - the full-day-only types' clear rule is stated;
  - the roadmap's two riders (ROADMAP:490-503): the "away today" filter pin, and the QUAL-196 revisit.

**TASK-14808** (`test-qa`).
- **Returned** on `worktree-agent-acec1e191d8c165a5`, tip `cffee784`, with three R facts:
  - AC-5b `April2026_ServesTheOk26Row`;
  - AC-5a, renamed `ConfigEndingMidMonth_RowNotServedInTheEndingMonth_ServedTheMonthBefore`, whose docstring states the month-level limit;
  - AC-12a `ApprovalFourEyesPredicateTests.LegacySubmittedByActor_Refused_DifferentActor_Admitted`.

  Gate green. Report: `S148-TASK-14808-report.md`.
- **5a (Fable 5.2, with the 5α checklist):** APPROVED-WITH-WARNINGS, 0 B / 1 W / 2 N (`S148-TASK-14808-5a-reviewer.md`). All three were accepted as a fix-up:
  - W: the AC-5a docstring's red condition is wrong (an inclusive read stays green);
  - N1: a stray deleted space in an existing banner;
  - N2: a probably needless CA2100 pragma.

**TASK-14802** (`backend-infrastructure`).
- **Returned** on `worktree-agent-a19ac318296cbfca2`, tip `8949b1c8` (implementation `e2864079` plus a merge of master `ece51892`). Report: `S148-TASK-14802-report.md`.
- **What it does:**
  - Removes `POST /api/time-entries`, its request and receipt types, and `RequestValidator.cs`, together with the validator's unit tests (A4, six facts; Unit is now 1716).
  - Both calculating routes now refuse a month whose calculation window (`HoursReadStart`, A5) holds a stored entry typed other than null or `NORMAL`. The answer is 422 `payroll-activity-type-refused`. It comes after 14801's four-eyes check, and on `/recalculate` after the idempotency fast path.
- **Gate:** build 0 / 144 / 114; Unit 1716, DemoSeed 170, Regression non-Docker 140.
- **Stale-comment sweep:** the list grows to 23 — the adjudication's eleven, the two A9 items, and ten more the census pattern missed.
- **Accepted:**
  - two declared deviations: the POST-only logger locals are removed; a blank-id branch makes `/recalculate` keep its 422 rather than throw a 500;
  - one unlisted `shortcut(pending)`: a stored typed row has no in-product remedy.
- **TASK-14802-T dispatched** on `8949b1c8`.

**Merges and gates (2026-11-10).** Every gate below gave: build 0 errors, 144 warnings, CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 140.
- **TASK-14805 + -T** merged as `14521382`. GATE green.
- **TASK-14808** merged as `b42b3b23`, after fix-up `3afc8996` (the docstring's red condition corrected, the banner's space restored, the unneeded CA2100 pragma removed with the count still 114). 5a Fable cycle 2: APPROVED, 0/0/0, with the fix-up's 5α checklist (`S148-TASK-14808-5a-reviewer-c2.md`). GATE green.
- **TASK-14806 + -T** merged as `81705338`:
  - It carries the fix-up `38b800f6` and 14806-T's `fee20add`. The latter adds `ManagerOfManagersLosesApprover_IsOrphan_CardEqualsRollUp` (kind F) and the `HrFollowUpApprovalEndpointTests.cs:399-404` docstring.
  - 5a cycle 2: Fable APPROVED, 0 B / 0 W / 6 N (`S148-TASK-14806-5a-reviewer-c2.md`); Codex APPROVED, 0 B / 0 W / 4 N (`S148-TASK-14806-5a-codex-c2.md`).
  - GATE green.
  - Owed:
    - N-1: the doc's "D9's literal root" becomes "D9's root that also has a report";
    - N-3: three stale test-comment words (`OrphanCoverageTests.cs:74`, `:187`, `:190`) join the comment sweep;
    - N-2: record the fourth reading in `TASK-14806.md` contract 4(b);
    - O-6: HRP-013 wording "a designated approver is named".

**TASK-14804 + -T, cycle 2.**
- **Fable:** APPROVED-WITH-WARNINGS, 0 B / 1 W / 2 N (`S148-TASK-14804-5a-reviewer-c2.md`).
- **Codex:** CHANGES-REQUIRED, 0 B / 1 W / 4 N (`S148-TASK-14804-5a-codex-c2.md`). Codex accepts the adjudicated worked-hours formula.
- **The shared warning:** both lenses found the same gap. In the new absence-versus-work race, either save could take the lock first. Under the mutation the fact was commissioned to catch (the in-lock check moved inside the absences block), it stays green when the work save goes first.
- **Fix-up `2f1d3a57`:** an ordered start. The sick save is fired, one waiter is observed, the work save is fired, two waiters are observed, then the lock is released. The end state is now deterministic: sick 200, work 422 with `kind`, SICK_DAY 7,4 and zero work rows. Both lenses are verifying it (cycle 3).
- **Fable NOTE 2:** the register row for the cell-clearing shortcut must name both lock-in cases (absence and project cell).

**TASK-14802 + -T + REGEN + TASK-14803, reviewed and merged.**
- **TASK-14802-T** returned on `worktree-agent-ac765aa6f755d08a9`, tip `789b3ccf`. Report: `S148-TASK-14802-T-report.md`.
  - **Route-retirement census:** 15 posting files moved to the Skema save, moved to a SQL insert, or retired, wherever a Skema twin pins the same property.
  - **New facts:** AC-S0-1a (the route answers 404 or 405, and the GET stays), AC-S0-1b (the typed refusal on both routes, including the context day Sunday 27 September 2026), AC-S0-1c (replay reproduces; `/recalculate` is refused), plus an R pin for a blank employee id.
  - **Smoke:** both facts re-pointed to the Skema save; the smoke count stays at 7.
- **REGEN `2c6a7dd1`** (Orchestrator): `docs/api/openapi.json` drops exactly the POST operation and its two schemas (97 deletions).
- **5a, cycle 1:**
  - **Fable:** APPROVED-WITH-WARNINGS, 0 B / 2 W / 10 N (`S148-TASK-14802-5a-reviewer.md`). Retiring the route lost two pins:
    - **W-1:** the "same refusal body before the start and after the end" pin, part of SEC-046's date dimension;
    - **W-2:** the month-lock status matrix on the surviving writer.

    Both are test-only.
  - **Codex:** APPROVED-WITH-WARNINGS, 0 B / 1 W / 3 N (`S148-TASK-14802-5a-codex.md`).
    - **W:** the projection rebuild can restore a historical typed `TimeEntryRegistered` between payroll's admission check and the calculation. The report's "no production writer of typed rows" was too broad.
    - **Orchestrator disposition: recorded, not fixed.** Under "reseed, don't migrate" no historical typed events exist. A complete fix would need the check inside the calculation's own snapshot, which ruling A5 declined (option c). This becomes a QUAL row at close and a line in the legacy-database upgrade runbook: carry no historical typed `TimeEntryRegistered` events into a live payroll; reseed instead.
- **14802-T fix-ups:**
  - **`43ffa351`:**
    - **W-1:** `SkemaSave_BeforeStartAndAfterEnd_RefusalBodiesAreByteIdentical` (R).
    - **W-2:** new `SkemaSaveMonthLockMatrixTests` (R, Theory). EMPLOYEE_APPROVED and APPROVED answer 409 with nothing written; DRAFT, SUBMITTED, REJECTED and no-row answer 200.
    - **N-5:** `timer` and lowercase `normal` are refused, an ordinal comparison (F).
    - **N-7, N-8:** an outbox assertion and a docstring.
  - **`2d33539e`:** the N-5 rows' employee id collided with an existing fact in the same shared fixture, `EMP-S148-AT-NORMAL`. Codex c2 raised it as a BLOCKER and Fable c2 marked the fix-up BLOCKED. The rows now use `EMP-S148-AT-ORD-TIMER` and `-ORD-LOWNORMAL`.
  - **Verdicts:** Codex c3 APPROVED. Fable c2 ruled the delta approvable once the worktree's correction was committed; the committed diff is that correction.
- **TASK-14803** (`ux`, stacked on `2c6a7dd1`): returned at `b8316737`.
  - **Removed:** the page, the form, the form's test and two CSS files (A8); the register path in `useTimeEntries`. `api-types.ts` was regenerated: 67 deletions, exactly the POST.
  - **Checks:** the five frontend checks exit 0 (vitest 84 files / 1090).
  - **5a (Fable):** APPROVED, 0 B / 0 W / 4 N (`S148-TASK-14803-5a-reviewer.md`). The reviewer regenerated the client itself and got an identical file. The census count is 7, not 8.
  - **Notes carried:** `useTimeEntries` now has no production consumer (`docs/FRONTEND.md:217` to be corrected under BC-4). The phase-pin test title's counts were already stale (comment sweep).
- **Merged:** 14802 stack as `e4e6b0ea`, then 14803 as `fc91e554`.
  - **GATE green.** .NET: build 0 errors / 143 warnings / CA2100 113; Unit 1716; DemoSeed 170; Regression non-Docker 140. Frontend: tsc, lint, typecheck:e2e and build exit 0; vitest 84 files / 1090.

**TASK-14804 + -T merged** as `72f286fe`, after the 14802 stack, as ruling B9 requires.
- **Cycle 3:** both lenses APPROVED the ordered race (`S148-TASK-14804-5a-codex-c3.md`, `…-5a-reviewer-c3.md`). The sick save is queued first by observation, under a FIFO advisory lock, so the named mutation always turns the fact red.
- **Auto-merge in `SendConcurrencyTests.cs`:** both sides survive. The AC7d fixture is on Saturday 14 March, and 14802-T's deletions are present.
- **Adjudication item 1e check:** `git grep` over `src` shows the projection tables written only by the Skema save (`SkemaEndpoints.cs:2339`, `:2372`, `:2425`) and by the backfill replay. `new TimeEntryRegistered`, `AbsenceRegistered` and `WorkTimeRegistered` are constructed only in the Skema save.
- **GATE:** see below.

**Cell clearing: plan addendum A1** (`.claude/plans/s148/ADDENDUM-A1-cell-clearing.md`).
- **Draft 1** (`planner`): TASK-14840 (backend), 14840-T and 14841 (ux), 32 ACs, NR-A1 … NR-A10.
- **Step 0b, cycle 1.**
  - **Fable:** CHANGES-REQUIRED, 1 B / 3 W / 7 N (`ADDENDUM-A1-reviewer-c1.md`).
  - **Codex:** CHANGES-REQUIRED, 1 B / 4 W / 2 N (`ADDENDUM-A1-codex-c1.md`).
  - **The shared BLOCKER:** a cleared work-time day was to be deleted. The startup projection rebuild reads only published events and runs before the publisher, so a clear that was committed but not yet published came back after a restart.
- **Adjudication** (`ADDENDUM-A1-adjudication-c1.md`, RULED-WITH-QUESTIONS):
  - **The fix, option (c):** a cleared work-time day is kept and marked (`cleared_by_outbox_id`, with `outbox_id` advanced to the clear's id), as entries and absences already are. That means one nullable column, and a filter at six reader sites. `init.sql` approval is widened to cover it.
  - **The from-empty rebuild gap:** it exists today for every replayed type. It is registered as a QUAL row (O-31), not built in 14840.
  - **AC-S0-2j** moves to 14840-T *before* 14811 is dispatched.
  - **New tests owed:** two forced-order clear-versus-re-save facts, and M-18 and M-20 redefined.
  - **AC-CL-20(2):** becomes kind R.
  - **D-13:** rewritten, because an exported month cannot be reopened (409 `payroll-locked`). Two new ACs come with it.
  - **Frontend:** one 409 behaviour, the same as any failed edit; only a 422 restores the cell's value. The removal announcement is shown only when it is true.
- **Owner Q3 (2026-11-11), "Fix it in S148 (Recommended)":** QUAL-196, the missing balance record, is fixed in S148.
  - **The problem:** clearing would let an employee in that rare state reach −1 day used, so the screen would offer one more holiday day than they are entitled to.
  - **The fix:** a missing record is created from the actual current bookings, in all three places that create one. It is a new task pair, TASK-14842 + -T.
- **Told, not asked** (2026-11-11):
  - Clearing has no confirmation dialog and no undo.
  - An empty posted work-time day is refused; the screen sends a removal instead.
  - The from-empty rebuild gap is registered.
- **Draft 1.1** is being written by a `planner` run: the adjudication's 17 edits, plus 14842, plus the main-plan "final + 3". Cycle 2 of Step 0b follows.
- **GATE after 14804** (`72f286fe`): green. Build 0 errors / 143 warnings / CA2100 113; Unit 1716; DemoSeed 170; Regression non-Docker 140. **All of wave 1 is merged on local master**: 14801, 14802 (+ 14803 stack), 14804, 14805, 14806, 14807 and 14808, each with its paired pass where it has one. **CI-W1 next**, on a throwaway branch and draft PR (owner "go ahead" for evidence runs, 2026-11-10).

**Cell-clearing addendum A1: Step 0b, cycles 2–5** (`.claude/plans/s148/ADDENDUM-A1-cell-clearing.md`, now **FINAL (draft 1.3.1)**; main plan "final + 5").

- **Cycle 2, on draft 1.1** (the cycle-1 adjudication plus owner Q3):
  - Fable: READY-WITH-WARNINGS, 0 B / 2 W / 6 N (`ADDENDUM-A1-reviewer-c2.md`). The mark design closes B-1; Fable found no way left to bring a cleared day back.
  - Codex: CHANGES-REQUIRED, 0 B / 2 W / 3 N (`ADDENDUM-A1-codex-c2.md`).
  - The new warnings:
    - the seed test could not catch a year window that is too long;
    - no fact caught the replayed clear losing its guard;
    - the revaluation's seed-plus-delta was wrong for a row with NULL feriedage;
    - the revaluation seeds `total_quota = 0`.
  - **Ruled by the Orchestrator.** Both lenses agreed on every item, and the one scope question went to the owner:
    - the per-type year window, with fixture rows that catch an over-long one;
    - AC-CL-38 and M-21 for the replay guard;
    - the revaluation result is the sum read after the overwrite;
    - the quota exemption applies to the net change per type (AC-CL-37);
    - OA-1: four writers; OA-2: keep AC-CL-36; OA-4: F with explicit expectations.
- **Owner Q4 (2026-11-11), "Include it (Recommended)":** every writer that creates a missing holiday balance gives it the applicable annual entitlement, pro-rated where the agreement says so. Before this, the revaluation path created such a balance with an entitlement of 0, and the team overview then showed "X of 0 days" for good. This is folded into TASK-14842.
- **Cycle 3, on draft 1.2:**
  - Fable: READY-WITH-WARNINGS, 0 B / 2 W / 2 N. Codex: READY-WITH-WARNINGS, 0 B / 1 W / 3 N.
  - Both found that the year-end carryover seeded the closing year's entitlement.
  - Fable also found that the revaluation test could not tell the year-start configuration from the live one.
  - **Ruled the complete fix:**
    - the carryover seeds the new year's own year-start entitlement, at both call sites;
    - the revaluation's source is named as the year-start key;
    - AC-BS-3 gains cases (c) and (d), and AC-BS-4 becomes a Theory.
  - The planner's open items were ruled as leaned:
    - OA-5: with no config for the new year, fall back to the closing year's total, log it, and register a residual;
    - OA-6: no part-time fraction on VACATION, which is flat by law;
    - OA-7: keep the rescue-route case.
- **Cycle 4, on draft 1.3:**
  - Fable: READY-WITH-WARNINGS, 0 B / 2 W / 2 N. Codex: READY-WITH-WARNINGS, 0 B / 1 W / 3 N.
  - The warnings: O-25 did not carry the OA-5 residual, and the carryover cases could not catch a live-row seed.
  - The Orchestrator fixed both in draft 1.3.1:
    - O-25 records the residual;
    - AC-BS-4 gains a second supersession, so the settled-year seed (25), the live seed (27) and the right seed (26) all differ;
    - the plan row is reworded.
  - The cycle-4 internal review was saved late, after it had been acted on, and is marked so.
- **Cycle 5, on draft 1.3.1:** **READY on both lenses.** Fable: READY, 0 B / 0 W / 3 bookkeeping N. Codex: READY.
- **The result:**
  - TASK-14840 + 14840-T + 14841 build cell clearing, and TASK-14842 + 14842-T fix the missing balance record and its entitlement, all in wave 2.
  - The order in wave 2: 14809 stack → 14811 stack → 14842 → 14840 stack.
  - Size: 44 AC ids; 65 facts (F 47, R 17, N 1); 69 agent tasks; M-18 … M-21 in CI-M2.
- **Owed before wave 2 dispatch:** the 14811 and 14811-T briefs must carry the AC-S0-2j move to 14840-T, and the corrected `EntitlementMapping.cs` path.

**CI-W1** (run 43011040973, draft PR #55, at `59231338`).
- **Tests: green.** `build-and-test` passed.
  - Unit 1716, DemoSeed 170, and Regression 2458 of 2458, Docker included.
  - Every wave-1 fact passed by name:
    - AC7d (ruling B9's condition);
    - both PIN-1 worlds, so pay is unchanged;
    - PIN-2;
    - all `OrphanCoverageTests`, including the shape-F fact;
    - the paid-absence bound and race facts;
    - the four-eyes admission and race theories;
    - the compensate race;
    - the typed-entry refusal facts;
    - the restored month-lock matrix and uniform-refusal pins;
    - the special-holiday pins;
    - the route-retirement pin;
    - the S106 cost guard `Roster_IsOneScopedLoad…`, still at ≤ 5 commands.
  - Smoke, E2E, the frontend build, lizard and gitleaks are green.
- **Red: the docs job only.** Four `shortcut(pending)` comments had not been registered at their merges, so merge-checklist item 8 was missed for 14802, 14804 and 14806.
  - They are now registered in `76c6da46` as QUAL-248 (Medium) and QUAL-BF-001 … 003, with comment-only tag edits. The local `check_docs.py` passes.
- **The regression step took 117 minutes**, against the usual 75. The tests themselves add up to 554 s; the rest is container start-up from the new Docker test classes. No test hung.
  - A diagnostic run, CI-W1b (draft PR #56, `ci/s148-w1b`), was started before this was known. It is master `76c6da46` plus one evidence-only commit adding `--blame-hang-timeout 15m`, and it exists only on that throwaway branch.
  - CI-W1b is kept as the green evidence for the full tip, including the docs fix.
  - The CI-time growth is noted for the F-series register at close.

**Wave-2 dispatch briefs: adjudication** (`.claude/reviews/S148-wave2-briefs-adjudication.md`, Fable 5.2, at `76c6da46`; verdict RULED).
- **What happened:** three planners drafted the eleven wave-2 briefs; each ended with the places where the plan and today's code disagree, or where a task would need a file it is not authorized for. One adjudicator pass ruled on all of them. None needed the owner.
- **The rulings that change what gets built:**
  - **Manual hours (14809, 14810):** the old 24-hour check stays as a backstop; it cannot fire today, because the interval parser stops at 23:59, but it will guard the rule if the midnight work in 14814 changes the parser. The alert for a refused paid-absence day now has fixed Danish wording in three cases (no planned hours; the day already full; room for at most M hours), so the test's expected text comes from the brief rather than the implementer.
  - **Child's sick days (14811, 14812):** the dated CHILD_SICK config gives the number of day codes offered (day 1 up to min(quota, 3)). Whether the call-home day exists is a small per-OK-version table in SharedKernel (OK24 no, OK26 yes; an unnamed version gets none). That table is a registered shortcut, because the config cannot yet carry per-illness extras. The call-home code gets one wage-type mapping row per agreement (five), not one; a single row would have dropped the call-home hours from payroll for four of the five agreements, because an unmapped line is skipped. The yearly remainder shown to the employee is on the Årsoversigt tile, not the balance summary.
  - **Missing balance record (14842):** the balance writers keep their signatures, the five production call sites pass the seed inputs explicitly, and the carryover's "zero-state" remarks are rewritten to be true.
  - **Cell clearing (14840, 14841):** one error code for an out-of-month date on every cleared array (`skema_cell_date_out_of_range`), pinned by AC-CL-10 as a Theory. The `shortcut(QUAL-248)` line is deleted and QUAL-248 closes in 14840's merge. The removal announcement gets its own screen-reader live region, so the existing "Gemmer…" region cannot overwrite it.
  - **Test passes:** two more mutations, M-22 and M-23, one for each half of the call-home day, credit and export. Two more facts: the five mapping rows, and the served allowance. Each AC fact's kind (F = fails on today's code, R = regression pin) is set row by row, following Block A's rule.
- **Comment-only and compile-only edits outside a task's own files** are authorized on the wave-1 precedents (A4, A8, A9): 15 test lines in 5 files for 14809; three comments each for 14809 and 14840; three comment lines for 14811.
- **Four things the owner should know** (no decision needed):
  1. An admin who edits the CHILD_SICK config's day count now changes which day codes the Skema offers.
  2. The call-home day's availability per OK version is in code for now, as a registered shortcut.
  3. The grid does not warn about the paid-absence bound before saving; the employee learns from the alert, which now says exactly why.
  4. The two AC variants (research and teaching) are labelled "provisional", the same as AC, which they inherit.
- **Owed at close:** an ADR-026 D2 amendment note (the audit mapper's real shape); register rows for the `/summary` CHILD_SICK row that no screen reads, the dead writer `AdjustUsedAsync`, the grid mirror of the bound (roadmap), and the call-home shortcut.
- **Applying the rulings** (plan "final + 6"; addendum "FINAL (draft 1.3.2)", bookkeeping only, design unchanged; all eleven briefs at draft 2). The planners found four things the adjudication had not settled, which the Orchestrator ruled:
  - **CI-M2b (a new mutation evidence run).** A mutation is a deliberate break used to prove that a test notices it, and the mutations in one run must not mask each other. M-8 removes the call-home code's classification row, so the save refuses the code before either payroll check is reached, and M-22 and M-23 would hide each other while credit and export sat in one test. Ruled on the NR-P15 precedent:
    - AC-S0-2e's payroll fact splits into a credit fact and an export fact;
    - M-22 and M-23 run apart from M-8, in CI-M2b;
    - 14811-T shows locally, under each mutation alone, that the two reds are independent.
    - Mutation evidence runs go from 4 to 5.
  - **The call-home code without a CHILD_SICK row:** offered only where day 1 is offered. This follows from the adjudication's fail-closed reading: a missing config row offers nothing.
  - **Two cites corrected against the code.**
    - S119's absence-type count moves in two places (`S119ConfigSpecRuntimeTests.cs:174`, 11 → 12, and `:225`, 10 → 11), as S147 moved both.
    - `HoursDigest.cs` is a payroll file, so AC-CL-21 ("no payroll file changes but comments") now names its comment beside `ProjectionHoursReader.cs`'s.
  - **The QUAL-248 register row's closing pointer** is corrected from O-28 to addendum O-24.
  - **Counts:**
    - plan AC ids 115 → 116 (AC-S0-7g, the precise alert, owner 14810);
    - Docker mutations 9 → 11;
    - agent tasks unchanged at 69;
    - addendum unchanged (44 ids; 65 facts).

**CI-W1b** (run 43027618243, draft PR #56, closed unmerged): **green, all seven jobs.**
- The docs job confirms the four `shortcut(pending)` registrations of `76c6da46`; build-and-test, smoke, E2E, frontend-build, lizard and gitleaks are green.
- **The regression step ran ~120 minutes** (13:18 → 15:18 UTC) under `--blame-hang-timeout 15m`, and no hang dump was produced. So the slow step is steady, not a hang: it is container start-up for the new Docker test classes. This confirms the CI-time F-series row owed at close.
- Wave-1 evidence is complete; wave-2 merges are unblocked.

**Wave 2 — TASK-14809** (`backend-infrastructure`, Opus 5.5; branch `worktree-agent-a2cc6517b09822064`, tip `b1fe3f7e`, one commit on `57624e6c`; report `.claude/reviews/S148-TASK-14809-report.md`).
- **What changed, in plain words:** "manual hours" (a bare daily total with no clock times, which no screen has created since S72) are gone from the backend: the database column, the event field, and the Skema request and response fields. Every "worked hours" sum now counts clock periods only. Payroll never read manual hours, so no pay can move (PIN-1). A caller that still sends the field is not refused; the field is ignored.
- 17 files, +54 / −92, all authorized, including the three comment-only files and the 15 compile-only test-line deletions ruled in the adjudication. The source grep for the column and the field finds 0 lines (AC-S0-5a, backend half).
- **Local gate:** build 0 errors, 143 warnings, CA2100 113; Unit 1716, DemoSeed 170, Regression non-Docker 140, all green. The Docker facts it expects to go red (the retired refusal, the contract pin, SQL seeders naming the dropped column, and two allocation facts whose work existed only as manual hours) are 14809-T's census.
- **Found, not fixed:** two stale line pointers in DemoSeed comments (Low, for the comment sweep at close).
- **Harness note:** the agent's isolated worktree started at the last *pushed* commit, not at local master, because S148 is unpushed until close. The agent fast-forwarded to `57624e6c`. From now on every wave-2 dispatch names its exact base SHA and fast-forwards to it first.
- **TASK-14809-T** (`test-qa`; tip `0570aa71` on `b1fe3f7e`; report `.claude/reviews/S148-TASK-14809-T-report.md`):
  - 24 test files, +342 / −169, tests only. Every test that seeded worked time as manual hours now seeds the same hours as clock intervals, with assertions unchanged.
  - Three bounds facts are retired (the negative check is gone; 24 h cannot be exceeded by same-day intervals). `Save_Exactly24Hours` stays as R.
  - New AC-S0-5b fact `SendCheckIntervalOnlyTests.SendCheck_ComparesIntervalHoursWithProjectHours` (F, CI): a stray `manualHours` member is ignored, so the send check compares clock hours with project hours.
  - The C5 row is re-pinned at 26 695 s; its raw-sum equality holds untouched.
  - Local gate green (143 warnings, CA2100 113; Unit 1716, DemoSeed 170, non-Docker 140).
  - **Found, not fixed:** the schema-copy drift guard does not compare `work_time_projection` (`HarnessDdlMatchesInitSqlTests`), so drift in that copy is invisible to CI. Both 14809 and 14809-T flagged it; it is a register candidate at merge.
- **REGEN** (`80b23b69`, on 14809-T's branch): `openapi.json` drops `manualHours` from `SkemaWorkTimeDayRow` and `SkemaWorkTimeDay`; nothing else drifts, exactly as predicted.
- Dispatched in parallel from `80b23b69`: TASK-14810 (`ux`), the 5a Fable review and the 5a Codex review (high-risk: schema) of `57624e6c..80b23b69`.
- **5a on `57624e6c..80b23b69` (14809 + 14809-T + REGEN):**
  - **Codex: APPROVED**, 0 B / 0 W / 3 N (`S148-TASK-14809-5a-codex.md`). It confirms that old events still carrying `manualHours` load with the value ignored, that every reader sums clock time alone with unchanged rounding, that payroll is untouched, and that AC-S0-5b is red on the base.
  - **Fable 5.2: APPROVED-WITH-WARNINGS**, 0 B / 1 W / 5 N (`S148-TASK-14809-5a-reviewer.md`). No code change is asked.
  - **W-1 (the test report's census figures were wrong).** The Orchestrator re-ran the census at `80b23b69`. `git grep -n -i -e manual_hours -e manualhours -- tests` returns **13 lines in 3 files**:
    - `S120SkemaSpecRuntimeTests.cs:71,73` (2 lines);
    - `SendCheckIntervalOnlyTests.cs` (9 lines);
    - `SkemaRowPreferencesMonthGetTests.cs:337,338` (2 lines).
    
    Each is a deliberate F-fact label or the fact's stray member. The 14809-T report's "15 lines, 8 files" and "0 further hits" are superseded by this line.
  - **Notes carried:**
    - O-7: `db-schema.md` is regenerated in the stack's merge, so the docs job stays green.
    - The bounds test docstring numbers the checks (1)/(2) where production keeps (2)/(3); align it in the close-time comment sweep.
    - The one-line history in the `SkemaEndpoints.cs:896-898` comment is for TASK-14814, which owns those lines, to drop when it renumbers.
    - Three test tidiness items go to the comment sweep. One of them cites the ADR-028 D4 amendment owed at close.
    - The C5 row's exact decimal round-trip is settled by CI-W2. If it is red, STOP; do not loosen it.
- **TASK-14810** (`ux`; tip `7ea1b263` on `80b23b69`; report `.claude/reviews/S148-TASK-14810-report.md`):
  - **What the employee sees:** the "Manuelt registreret" line is gone. Every worked total on the Skema screens comes from clock periods alone: the day panel, the grid's work row, "Diff. fra normtid", the flex card's month figure, and the manager's read-only grid. Every save sends `{ date, intervals }`.
  - **The alert (AC-S0-7g):** a refused paid-absence save now gives the precise Danish alert in its three ruled cases, naming the planned and worked hours, instead of the old, wrong "overstiger normtiden (0 t)".
  - 14 files under `frontend/src/`, +268 / −287. The AC-S0-5a grep over `src tools frontend/src init.sql` is empty.
  - **Frontend gate green:** tsc, lint, the e2e typecheck, the build, and vitest at 85 files / 1088 tests (1090 → 1088: five retired facts, three added).
  - Two things beyond the brief, put to the 5a review:
    - the fixture's 1,5 t moved into the clock interval (end 16:00 → 17:30 in three assertions);
    - one extra companion fact for the planned = 0 sentence.
  - **Found, not fixed:** the grid's advisory cap counts absence only, so the grid does not warn before the server's bound refuses a save. This is the roadmap row already owed.
- Dispatched in parallel: 5a Fable on `80b23b69..7ea1b263`, and 5α over the union `57624e6c..7ea1b263`.
- **5a Fable on TASK-14810: APPROVED**, 0 B / 0 W / 6 N (`S148-TASK-14810-5a-reviewer.md`). It re-ran tsc and the touched vitest files (316 tests green).
  - Both things beyond the brief are legitimate. The 16:00 → 17:30 change is the forced consequence of moving the fixture's 1,5 t into its clock interval; the same 9,5 h is worked and every assertion still discriminates. The planned = 0 companion fact covers a case the change reaches.
  - The test delta is −6 / +4 (net −2, 1090 → 1088), not the report's "−5 / +3".
  - **The Orchestrator fixed four notes before the merge** (`8e085742`, comment and label only, under the small-tasks exception; tsc and the two touched files green, 107 tests):
    - the companion fact is labelled F;
    - a describe title no longer names the deleted `dayWorkedHours`;
    - two test comments are made true-now;
    - the alert's page comment names the paid-absence day bound.
  - The unreachable "bound body without its two figures" note is left (the only emitter always sends both).
- **5α over the union `57624e6c..7ea1b263`: PASS** (`S148-TASK-14809-14810-5alpha.md`).
  - Every commit stayed in its scope.
  - The only `docs/**` change is the Orchestrator's REGEN.
  - The column drop and the event-member removal are consistent across schema, serializer, producer and consumers.
  - No `shortcut(` or TODO was added, so merge-checklist item 8 has nothing to register.
- **Merged** (the 14809 → 14809-T → REGEN → 14810 stack, `--no-ff` from `8e085742`). The same commit carries O-7: `db-schema.md` regenerated from `init.sql` (the `manual_hours` row removed), so the docs job stays green.
- **GATE after the 14809 stack, on master `540ac9a9`: green.**
  - Build `-m:2`: 0 errors, 143 warnings (cap 144), CA2100 113 (cap 114).
  - Unit 1716, DemoSeed 170, Regression `Category!=Docker` 140.
  - Frontend: tsc, lint, e2e typecheck and build all green; vitest 85 files / 1088 tests.
  - The Docker facts, including AC-S0-5b, the key-set pins and the C5 row, are CI-W2's.

**Wave 2 — TASK-14811** (`backend-infrastructure`; branch `worktree-agent-abcbe641097cf0bb8`, tip `162da27c`, two commits on `6ec6a111`, marker commit `575315e9`; report `.claude/reviews/S148-TASK-14811-report.md`).

**What changed, in plain words.** Before, StatsTid gave AC staff 1 child's sick day, HK 2 and PROSA 3, counted against a yearly limit, and had no way to register the "call-home" hours. Now:
- **Days per illness.** The dated CHILD_SICK setting says how many days one illness gives: 2 under OK24 and 3 under OK26, for all five agreements. An administrator who edits that number changes which day codes are offered.
- **The call-home day.** A new code, `CHILD_SICK_CALL_HOME`, is offered from OK26 only. A small table in code decides that (a registered shortcut).
- **Consecutive days.** Day 2 must follow day 1, and day 3 must follow day 2, unless the missing day had no planned hours (a weekend). An illness keeps the OK version of its first day.
- **No yearly limit.** The HR opt-in that allows child's sick days at all is unchanged.

**Pay.** The call-home hours are credited like a sick day. That is the sprint's one change to the pay arithmetic, so the engine's version marker moves to `weekly-timeline-v3` in the same commit as the classification change. A pay result recorded under the old rules is then refused at replay rather than silently recalculated (ADR-016 D10). Payroll exports the hours under `SLS_0530` (provisional), with one mapping row per agreement.

**The change.**
- 12 files. The chain is the fifth in-lock refusal, judged advisory and then authoritatively right after the paid-absence bound.
- The new refusal bodies reuse `absence_type_not_eligible` with `kind` `child-sick-not-offered` or `child-sick-chain`. The opt-in refusal is byte for byte unchanged.
- The year-overview tile trades `childSickRemaining` for `childSickDaysPerIllness` and `childSickCallHomeDay`.
- Four `shortcut(pending)` comments to register at merge:
  - the other parent is not modelled (M);
  - `SLS_0530` is provisional (M);
  - AC's OK26 rule is provisional (L);
  - the call-home table lives in code (L).

**Local gate.** Build 0 errors, 143 warnings, CA2100 113. Unit 1711 of 1716: exactly the five predicted reds, which 14811-T re-pins. DemoSeed 170, non-Docker 140. PIN-1 is argued: the demo draws no child's-sick code.

**Orchestrator rulings on its § 12** (accepted; 5a judges them too):
- The chain judges posted-absence dates only. Clock time and project hours cannot break a chain, and judging them would refuse an unrelated save over stored data.
- The catalogue lists the child codes together, call-home first.
- A child's-sick row on a weekend now gets the paid-absence bound's accurate "no planned hours" refusal instead of the old "non-working day" one.
- A one-line comment fix outside the brief's lines.

**Found, not fixed** (register at merge or close):
- the `/summary` CHILD_SICK row;
- the rule engine's unreachable per-episode branch;
- three stale comments.

TASK-14811-T dispatched on `162da27c`.
- **TASK-14811-T** (`test-qa`; tip `17269f7c`, two commits on `162da27c`; report `.claude/reviews/S148-TASK-14811-T-report.md`).
  - **Scope:** 21 test files (7 new), +1017 / −45, tests only.
  - **New facts:**
    - the seeds and compiled defaults agree for all five agreements (Unit F, red on base);
    - the catalogue per OK version;
    - the per-code refusal;
    - five chain facts, including the 30 March → 1 April case, where the illness keeps OK24's two days;
    - the call-home credit and export as two N facts (CI-M2b ruling);
    - the five mapping rows (F);
    - the tile's served allowance (F);
    - the opt-in body's missing `kind` (R).
  - **Re-pins:**
    - the nine payroll rows, now each a legal OK26 illness: three F (AC `_2`, AC `_3`, HK `_3`), six R;
    - the S119 counts, 12 and 11;
    - the marker moved to v3 in every stub.
  - **Rulings:** neither the weekend-refusal ruling nor the catalogue-order ruling forced any re-pin.
  - **Local mutation censuses:**
    - M-8 reddens the two classification Unit facts. Both payroll Docker facts are argued red at the save's vocabulary gate.
    - M-22 alone reddens one Unit row. Only the credit fact is argued red in Docker.
    - M-23 alone reddens nothing locally. Only the export fact and the mapping fact are argued red in Docker.
    - **Independence holds:** neither frozen list relies on a fact the other reddens. M-8 and M-22 share one Unit row, but they run in different CI runs.
  - **Local gate:** green (143 warnings, CA2100 113; Unit 1719, DemoSeed 170, non-Docker 140).
- **REGEN** (`a3063fbc`, on 14811-T's branch): `YearOverviewTiles` drops `childSickRemaining` and gains `childSickDaysPerIllness` and `childSickCallHomeDay`. Nothing else drifts, as predicted.
- **Dispatched in parallel from `a3063fbc`:**
  - TASK-14812 (`ux`);
  - the 5a Fable review of `6ec6a111..a3063fbc`;
  - the 5a Codex review of the same range (high-risk: legal rule logic, payroll export, schema seeds).
- **5a on `6ec6a111..a3063fbc` (14811 + 14811-T + REGEN):**
  - **Codex: APPROVED**, 0 B / 0 W / 2 N (`S148-TASK-14811-5a-codex.md`). It found no save interleaving that builds an illegal chain, and no write path that admits an OK24 call-home row. It also showed that the seeded worked hours do reach the bound, settling the test report's "(unproven)". Its note on the `/summary` CHILD_SICK row is sharper than ours: no screen reads it, but other API callers get a misleading yearly figure, so its register row is not "harmless".
  - **Fable 5.2: APPROVED-WITH-WARNINGS**, 0 B / 2 W / 4 N (`S148-TASK-14811-5a-reviewer.md`). It accepts the Orchestrator's four § 12 rulings.
    - **W-1:** nothing pins that the call-home code is in the child's-sick code set, which is what puts it behind the HR opt-in and the OK24 refusal.
    - **W-2:** the per-illness limit binds only the day-2 and day-3 codes. "Barns 1. sygedag" five days running gives five paid days, and a call-home day needs no work.
- **W-1 fixed** (14811-T fix-up 1, tip `ba247730`; `S148-TASK-14811-T-c2-report.md`):
  - a Unit fact that the set is exactly the four codes;
  - two Docker facts, the call-home code refused in OK24 for an opted-in employee, and refused with the opt-in body for one without the opt-in.
  
  A mutation that drops the code from the set reddens the Unit fact locally; the Docker facts are argued red. Two stale docstrings were fixed. Gate green (Unit 1720).
- **Owner Q5 (2026-11-12), "Build both rules (Recommended)", on W-2.**
  - **The question.** The system cannot see when a child's illness starts or ends; it trusts the day code chosen. The circular's own definitions (`sick-day-credit-and-agreement-texts-research.md:196-197`: day 2 *is* the calendar day after day 1; the call-home day is the day the child fell ill *after the employee met at work*) support two rules.
  - **The rules:**
    - **(A)** A new illness (day 1, or a call-home day) cannot start on the calendar day right after a child's-sick day. Day 1 directly after a call-home day is the normal case and is allowed.
    - **(B)** A call-home day needs registered work the same day.
  - **What it gives up:** a genuinely new illness starting the very day after a covered day is refused. The text arguably counts it as the same illness.
  - The remaining limit, an illness whose middle days fall on days off, is registered as a shortcut.
  - **Being built** as 14811 fix-up 1, on `ba247730`, with new refusal kinds `child-sick-illness-start` and `child-sick-call-home-without-work`.
- **TASK-14812** (`ux`; tip `f3ce1535` on `a3063fbc`; report `.claude/reviews/S148-TASK-14812-report.md`).
  - **The change:** the Årsoversigt tile now shows the allowance per illness ("3 dage, pr. sygdomsforløb + hjemkaldelsesdag" under OK26, "2 dage" under OK24) instead of a yearly remainder. No Skema change is owed, because the screen leads with the server's message for these refusals.
  - **Gate:** green, with vitest at 86 files / 1091 tests (+1 file / +3 tests on 1088; the report's "baseline 1090" is wrong). The RED was shown on the unchanged page after `gen:api`, not in a scratch worktree.
  - **5a Fable:** dispatched.
- **5a Fable on TASK-14812: APPROVED-WITH-WARNINGS**, 0 B / 1 W / 5 N (`S148-TASK-14812-5a-reviewer.md`).
  - **W-1:** the new test checks that the tile contains "3 dage", which the old hours-first display "22,2 t (3 dage)" also contains, so a regression to it would pass.
  - **Notes:** a stale tile-kind comment; the not-opted-in case is R, not F; the eligible-but-no-config dash is unpinned; a hook test for the refusal shape is missing; and the report's baseline was wrong.
  - It confirmed the two pending refusal kinds need no frontend change, as long as they keep the `absence_type_not_eligible` + `message` shape.
  - **Fix-up 1 dispatched to the `ux` agent:**
    - pin the whole tile text, and show the hours-first mutation turns it red;
    - the comment;
    - the kind label;
    - the dash case;
    - a hook test for all four child's-sick kinds.
- **TASK-14811 fix-up 1: the owner's two rules built** (tip `53a981ac` on `ba247730`; `S148-TASK-14811-c2-report.md`).
  - **Rule A** (`child-sick-illness-start`) and **Rule B** (`child-sick-call-home-without-work`) are in the child's-sick judge after the chain, both advisory and in-lock, still the fifth refusal family.
  - **Rule B's worked figure** is the paid-absence bound's own: its reads and its formula moved into two shared helpers, so there is still one calculation.
  - **Declared deviation, accepted by the Orchestrator:** the move touches TASK-14804's bound code. The instruction "reuse, do not duplicate" could not be met otherwise. The bound's behaviour must be unchanged, and both 5a lenses check it; its CI facts pin it.
  - **A brief contradiction, settled:** a save can post a work-time day with empty intervals today, bringing worked to 0. On a call-home day, Rule B now refuses that.
  - **Shortcuts:** a fifth `shortcut(pending)` is added (an illness whose middle days fall on days off is told apart only by the code chosen), and the AC_TEACHING seed row is tagged.
  - **Gate:** green (Unit 1720). No existing fact is expected red.
  - 14811-T fix-up 2 (the eight rule facts, M-A and M-B) dispatched.
- **TASK-14812 fix-up 1** (tip `3f3625cd`; `S148-TASK-14812-c2-report.md`):
  - **The tile test** now pins the whole tile text. A mutation back to the hours-first display turns both allowance cases red.
  - **Comments and kind labels** are made true.
  - **A new F case:** opted in with no day configured today.
  - **A new R hook test:** all four child's-sick refusal kinds pass the server's message through, not the quota branch.
  - **vitest:** 87 files / 1096 tests (master 85 / 1088 → +2 files / +8 tests).
- **5a Fable cycle 2 on 14812: APPROVED**, 0 B / 0 W / 1 N (`S148-TASK-14812-5a-reviewer-c2.md`). All six cycle-1 findings are closed. The one note, a stale red-condition line in the test header, was fixed by the Orchestrator (`28ac4e89`, comment only).
- **TASK-14811-T fix-up 2** (tip `d64559ef` on `53a981ac`; `S148-TASK-14811-T-c3-report.md`): eight Docker facts for the owner's rules, in `ChildSickIllnessStartTests.cs`.
  - **Rule A:**
    - consecutive day-1 rows: the second is refused (F);
    - call-home then day 1: accepted (R);
    - day 1 then call-home: refused (F);
    - a day 1 posted before a stored day 1: the stored date is refused (F).
  - **Rule B:**
    - call-home with clock hours: accepted (R);
    - call-home with no work: refused (F);
    - call-home with project hours only: accepted (R);
    - empty intervals posted onto a stored call-home day: refused (F).
  - **The R facts** guard against a rule reaching too far.
  - **The bound-refactor census:** the seven existing bound facts reach the bound only over HTTP and compile unchanged; they pin it in CI.
  - **Local mutations:** M-A (Rule A's day-1 arm off) is argued red at facts 1 and 4; M-B (Rule B never refuses) at facts 6 and 8. Neither reddens anything locally, because all the facts are Docker.
  - **Gate:** green.
- **Dispatched:** 5a cycle 2, on both lenses, over `a3063fbc..d64559ef`.
- **5a cycle 2 on `a3063fbc..d64559ef`** (14811 fix-up 1 + 14811-T fix-ups 1 and 2):
  - **Codex: APPROVED**, 0 B / 0 W / 1 N (`S148-TASK-14811-5a-codex-c2.md`).
    - The bound extraction preserves behaviour.
    - Rule A catches batched and reverse-order adjacency across month ends.
    - Rule B judges the resulting state for every save shape, and the employee lock serializes concurrent saves.
  - **Fable 5.2: APPROVED-WITH-WARNINGS**, 0 B / 1 W / 5 N (`S148-TASK-14811-5a-reviewer-c2.md`). All cycle-1 findings closed; the bound refactor is unchanged in behaviour.
    - **W-1:** two of Rule A's refusal pairs are untested (day 1 after day 2 or 3; call-home after call-home). A narrowing mutation would let the 1-2-1-2-1 pattern through with every test green.
    - **Notes:**
      - no fact reaches the in-lock judge;
      - the W-1 docstrings became untrue after Rule B;
      - the refusal for a day 1 posted before a stored day 1 names the stored day;
      - the fifth shortcut's wording is narrower than the limit (gap days on weekdays too);
      - the grid's entry order for a call-home day is unproven.
  - **Disposition:**
    - W-1, the race note and the docstring note go to 14811-T fix-up 3: two pair facts, one race fact, and the docstrings with work posted.
    - The shortcut wording is broadened at registration, with its code comment reworded at merge.
    - Two roadmap lines owed at close: the stored-day message wording (Low, rare, safe) and the call-home entry order (an e2e check).
- **5α over Branch A `6ec6a111..d64559ef` and Branch B `a3063fbc..28ac4e89`: PASS** (`S148-TASK-14811-14812-5alpha.md`).
  - Every commit is in scope; the bound refactor is the accepted deviation.
  - The marker and the classification are in one commit.
  - Five distinct `shortcut(pending)` comments across nine spots, to register at merge.
- **TASK-14811-T fix-up 3** (tip `58c7bad8`; `S148-TASK-14811-T-c4-report.md`):
  - **Two pair facts** close Fable's W-1. Day 1 after day 2 is refused, and so is call-home after call-home. Each goes red under its narrowing mutation (X, Y).
  - **A race fact** covers the in-lock judge. Two concurrent day-1 saves on consecutive days: exactly one commits, using the bound's lock-holding technique. It goes red under M-L, when the in-lock call is skipped.
  - **The W-1 facts** now post work, so only their intended gate can refuse.
- **5a cycle 3 on `d64559ef..58c7bad8`:**
  - **Codex: APPROVED** (`S148-TASK-14811-5a-codex-c3.md`). The race forces both saves past the advisory check; either grant order gives the required refusal; and it cannot pass vacuously.
  - **Fable 5.2: APPROVED**, 0 B / 0 W / 2 N (`S148-TASK-14811-5a-reviewer-c3.md`). All three answered findings are closed.
  - **Both notes fixed by the Orchestrator** (`72fa5c23`, tests only; the Regression project builds):
    - the race fact also pins the refused date;
    - two colliding fact labels are renumbered.
- **Merged** (`26065e1a`): one octopus merge of the TASK-14811 stack (`72fa5c23`) and TASK-14812 (`28ac4e89`), so master's frontend never trails the regenerated contract.
  - **Merge-checklist item 8 is done in the same commit.** The nine `shortcut(pending)` tags are registered, and `check_docs` reports 13 shortcuts, all registered. The registrations:
    - **QUAL-249** (M): the call-home hours export under the provisional `SLS_0530`;
    - **QUAL-250** (M): the other parent's days are not modelled;
    - **QUAL-BF-004**: AC's OK26 rule is provisional;
    - **QUAL-BF-005**: the call-home table lives in code;
    - **QUAL-BF-006**: illnesses are told apart only by the code chosen and calendar adjacency. Its wording is broadened per Fable c2, and the code comment is reworded to match.
  - **QUAL-251** (M) registers the `/summary` CHILD_SICK row that still serves a yearly quota (Codex: not harmless for API callers).
- **GATE after the 14811 stack, on master `26065e1a`: green.**
  - Build: 0 errors, 143 warnings, CA2100 113.
  - Unit 1720, DemoSeed 170, Regression non-Docker 140.
  - Frontend: tsc, lint, e2e typecheck and build green; vitest 87 files / 1096 tests.
  - The Docker facts (the chain, Rules A and B, the race, payroll, the mapping, the tile) and M-8, M-22 and M-23 are CI-W2, CI-M2 and CI-M2b's.
