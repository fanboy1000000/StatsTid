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
