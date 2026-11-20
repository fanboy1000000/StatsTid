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

**Wave 2 — TASK-14842** (`backend-infrastructure`; branch `worktree-agent-ab112bd82ed22a09a`, tip `bbbd5eea` on `87b26453`; report `.claude/reviews/S148-TASK-14842-report.md`). This fixes QUAL-196 (owner Q3) with owner Q4's quota seed.

**What was wrong, in plain words.** Each employee has one holiday balance record per type and year. When that record was missing, the code that created it got it wrong in three ways:
- It started the days used at 0, ignoring holidays already booked. Two booked days plus a third gave "1 used" instead of 3.
- When HR backdated a change of working hours, the recalculation set the year's entitlement to 0. The team overview then showed "of 0 days" for good.
- The year-end carryover gave next year the closing year's entitlement, even when the entitlement changed on the new year's first day.

**What it does now.** Every created record gets two things:
- the sum of the employee's current bookings for that type and holiday year (each type with its own year window);
- the entitlement in force on the first day of its own holiday year, read the way the Skema save reads it, never the live row.

**The change, for the record.**
- Five source-compatible writer signatures. All five production call sites pass the seed inputs explicitly.
- The carryover is fixed at both of its call sites.
- If no configuration exists for the new year, the carryover falls back to the closing year's total and logs it (OA-5).

**Local gate:** green, with no existing fact expected red.

**Orchestrator rulings on the report:**
- **Accepted:**
  - the rescue now fails closed (500, rolled back) on a snapshot with no reset month, instead of writing a zero-quota row; with the reseed, that state cannot occur;
  - the settlement service is injected into the resolve head, so both carryovers share one chain;
  - one extra helper parameter;
  - comment edits in authorized files.
- **Put to both 5a lenses:**
  - **the security question:** the rescue's new terminated-inclusive user read, which the R9c allowlist names as "settlement resolve";
  - **the cost question:** one extra sum query per type on every save, even when the record exists, which the reviewers judge as "seed only on insert" versus an F-row.
- **Accepted as a shortcut, registered at merge:** the year-end close names the VACATION absence code itself, because Infrastructure cannot reference the Backend mapping.

**Found, not fixed:** the dead writer `AdjustUsedAsync` (creates `total_quota` 0, no production caller), and three stale test or resolver remarks.

TASK-14842-T dispatched.
- **TASK-14842-T** (`test-qa`; tip `2dcac3e3` on `bbbd5eea`; report `.claude/reviews/S148-TASK-14842-T-report.md`): three new Docker test files, all F except one R. No existing fact moves; the census of 31 files classifies them all.
  - **The booking path:** the missing row is seeded from current bookings; the over-long-window guards on 1 Oct 2026 and 30 Apr 2026 are in the fixture.
  - **The reduction path.**
  - **An existing row adds only the change:** the R pin.
  - **The revaluation:** the seed is read after the overwrite; the quota comes from the year-start row, not the live one (25, not 26); the new fraction gives 12,5.
  - **The carryover:** both call sites seed 26, not the settled year's 25 and not the live 27.
  - **Local gate:** green.
  - **Unproven until CI:** four fixture risks. HK was used for AC-BS-4 rather than the settlement harness's AC precedent; if CI shows an HK-specific failure, switching is a one-line change.
- **Dispatched in parallel:** 5a Fable, 5a Codex (high-risk: balance correctness, concurrency, the security question) and 5α, all over `87b26453..2dcac3e3`.
- **5a on `87b26453..2dcac3e3` (14842 + 14842-T), cycle 1:**
  - **Codex: CHANGES-REQUIRED**, 1 B / 0 W / 4 N (`S148-TASK-14842-5a-codex.md`).
  - **Fable 5.2: APPROVED-WITH-WARNINGS**, 0 B / 2 W / 6 N (`S148-TASK-14842-5a-reviewer.md`).
  - **5α: PASS** (`S148-TASK-14842-5alpha.md`). The terminated-inclusive read is behind the allowlisted LocalHR gate. Infrastructure takes no Backend dependency. No contract changed, so no REGEN is needed.
  - **Both lenses independently, the blocker (Codex B-1 = Fable W-2).** The carryover falls back to the closing year's entitlement whenever next year's settings key has no *open* row. That covers a key whose open row an admin later deleted while the row in force on the year's first day remains, so a rescue would seed 25 where the year-start row says 26, for good. The ruling allows the fallback only when the key has no row at all.
  - **Fable W-1.** The R4 pin "settlement does not use the graceful resolver" is green while the carryover seed now uses it. Its name, its docstring and the resolver's remarks are untrue.
  - **Both lenses agree on the questions put to them:**
    - The security question is safe: the read sits after the LocalHR scope gate for the same employee, on the R9c allowlist.
    - The deviations are acceptable.
    - The per-save sum is not a merge blocker. Codex recommends an insert-only sum; Fable says leave it unless measured.
  - **Disposition.** Fix-up 1 is dispatched to the implementer:
    - check the dated row before OA-5;
    - use the service's own type mapping instead of a fixed VACATION list, which retires that shortcut;
    - pin ReadCommitted explicitly on the profile PUT and the resolve route.
    
    Then 14842-T adds the dated-row-without-open-row regression case. W-1 (the R4 pin and the resolver remarks) is the Orchestrator's at merge. The per-save sum becomes an F-series row at merge.
- **TASK-14842 fix-up 1** (tip `73a395dc`; `S148-TASK-14842-c2-report.md`):
  - **The OA-5 fallback is narrowed.** With no open row, the carryover reads the row in force at year N+1's start under the same key, and falls back only when there is none.
  - **The settled type's absence code** comes from the service's own `SettledAbsenceType` switch. The VACATION constant and its `shortcut(pending)` are gone, so this stack has no shortcut left to register.
  - **The isolation level is explicit.** The profile PUT and the resolve route now open their transactions as ReadCommitted (PAT-015).
  - **Gate:** green.
- **TASK-14842-T fix-up 1** (tip `cbababe8`; `S148-TASK-14842-T-c2-report.md`): three AC-BS-4 rows.
  - (d) The year-start row is in force but has no open row: the close seeds 26 (F).
  - (f) The same through the feriehindring rescue (F).
  - (e) No row is in force at all: OA-5 gives 25 (R).
  - **A fixture risk the pass marks unproven:** with no open row, the settled year's fail-closed snapshot capture may refuse. Both 5a lenses are asked to rule on it.
- **Dispatched:** 5a cycle 2, on both lenses, over `2dcac3e3..cbababe8`.
- **5a cycle 2 on `2dcac3e3..cbababe8`:**
  - **Codex: CHANGES-REQUIRED**, 1 B (`S148-TASK-14842-5a-codex-c2.md`).
  - **Fable 5.2: CHANGES-REQUIRED**, 1 B / 2 N (`S148-TASK-14842-5a-reviewer-c2.md`).
  - **What is closed.** Both lenses close the production blocker: the dated row in force at the year start now seeds the quota, and OA-5 fires only when no row is in force. Both carryover call sites use it. N-1 and N-5 are closed too.
  - **The same new blocker, from both lenses: the test fixture.**
    - Cases (d) and (e) leave the employee's own current config key (HK, OK24) with no open row.
    - The year-end close's snapshot capture needs that row to learn when the holiday year starts, and it refuses (throws) before the seed runs. That happens on the base and on the fix alike.
    - So (d) is red for the wrong reason, and (e) is red when it should be green.
    - **Fix:** seed the employee on OK26, today's version on the pinned clock, so capture uses an untouched key, and keep the OK24 history as is. Move (e)'s close date to 1 Sep 2025 to pin the same-day boundary (Fable N-8). Dispatched as 14842-T fix-up 2.
  - **Fable N-7, recorded neutrally.** At year-end the new dated read matters only when the employee's current key differs from next year's. A deleted open row for the employee's current key already makes the close refuse, which is the existing fail-closed choice (ADR-033 D10). Whether capture should try a dated row before refusing is a separate question for the register, if the owner wants it.
- **TASK-14842-T fix-up 2** (tip `6946caa0`; `S148-TASK-14842-T-c3-report.md`). In (d) and (e) the employee is seeded on OK26, so the capture uses an untouched key, and (e)'s close date moves to 1 Sep 2025.
- **5a cycle 3 on `cbababe8..6946caa0`:**
  - **Codex: APPROVED** (`S148-TASK-14842-5a-codex-c3.md`). Both repaired cases reach the seed. (d) is red on the base at exactly 25 versus 26; (e) is green on both sides.
  - **Fable 5.2: APPROVED**, 0 B / 0 W / 1 N (`S148-TASK-14842-5a-reviewer-c3.md`). B-1 and N-8 are closed.
  - **N-9 (open):** (e) exercises the same-day boundary but cannot detect a change to it. The closed row and the fallback both give 25.
  - **The fix:** a closed row worth 26, superseded inside the settled year, so that an inclusive boundary would turn (e) red. It is small, and is dispatched as 14842-T fix-up 3 under the complete-fix preference.
- **TASK-14842-T fix-up 3** (tip `82b3f9bf`; `S148-TASK-14842-T-c4-report.md`). Case (e) now pins the same-day boundary. A 26 row is superseded inside the settled year and closed on 1 Sep 2025, so an inclusive boundary gives 26 and turns (e) red, while the correct read falls back to 25.
- **5a cycle 4 on `6946caa0..82b3f9bf`: Codex APPROVED** (`S148-TASK-14842-5a-codex-c4.md`) and **Fable 5.2 APPROVED** (`S148-TASK-14842-5a-reviewer-c4.md`). N-9 is closed. Two hygiene notes, a stale comment line and a lost space, were fixed by the Orchestrator at merge.
- **Merged** (TASK-14842 stack, `--no-ff` from `82b3f9bf`). The merge commit also carries:
  - **Fable c1 W-1.** The R4 pin is renamed `R4_SettlementService_ConstructorTakesNoGracefulResolver`, and its docstring and section comment now name the carryover seed as the one sanctioned use of the graceful resolver. The resolver's two remarks say the same, and that two callers now use it inside a locked transaction.
  - **Fable c2 N-7.** One clause in the carryover seed's remark: at YEAR_END the new dated read reaches only a next-year key that differs from the employee's current one.
  - **QUAL-196 closed FIXED in the register**, with its two residuals: readers show 0 used until a write creates the row, and OA-5, now pinned by AC-BS-4 (e).
  - **The dead balance writer** `AdjustUsedAsync` (no transaction) is recorded below the floor.
  - **F8 registered:** the seed's sum on every save, accepted, revisit, with Codex's insert-only option.
  - No `shortcut(pending)` remains, and `check_docs` passes (13 shortcuts, all registered).
- **GATE after the 14842 stack, on master `6bedcf2c`: green.**
  - Build: 0 errors, 143 warnings, CA2100 113.
  - Tests: Unit 1720, DemoSeed 170, Regression non-Docker 140.
  - No frontend change in this stack.
  - The AC-BS Docker facts are CI-W2's.

**Wave 2 — TASK-14840** (`backend-infrastructure`; branch `worktree-agent-a9b87ad039bca59b7`, tip `b1839b86` on `728f4702`; report `.claude/reviews/S148-TASK-14840-report.md`). Cell clearing, owner Q2, closing QUAL-248.

**What an employee can now do.** Empty a project cell, an absence cell or a work-time day. A day a typing mistake had locked can now be fixed. For example, a mistaken full sick day used to block the real work that day; now it can be emptied first, or in the same save as the work.
- An emptied holiday, care or senior day gives exactly its stored days back to the balance.
- A sent or approved month still refuses changes.

**What HR sees.** Each removal writes one audit row, in the same transaction: who removed what, on which date, and which registration it replaced. The removed row is never deleted. It is kept and marked as retired, so the record shows both the registration and its removal, and earlier pay calculations still replay exactly.

**One deliberate tightening (W-a).** A posted work-time day with no real work period is now refused. The explicit removal is the only way to empty a day. That is why 14840 and 14841 merge together: until the screen changes, it could still post such a day.

**The build, for the record:**
- the `cleared` request arrays;
- three typed events with their audit mappers;
- `cleared_by_outbox_id` on the work-time table, with readers filtered and the rebuild replaying clears;
- the give-back, with 14842's seed inputs;
- the child's-sick judge extended, so a removal is judged on the day as it will stand. Clearing day 1 while day 2 stands is refused, and so is clearing the work on a call-home day (owner Q5's Rule B).

**Gate.** Green: Unit 1720, DemoSeed 170, non-Docker 140. `AuditProjectionParityTests` and the event-serializer completeness fact are green.

**Orchestrator rulings.** Accepted:
- the four declared deviations: the clear's idempotent guard; retire methods that fail closed unless exactly one row changes; the split read; nullable seed inputs for a clear-only type;
- the brief correction: `SkemaProjectionAtomicTests` also needs the fixture column.

**Owed by 14840-T beyond its brief:**
- re-point 14811-T's empty-intervals Rule B fact to a `cleared.workTime` request, because W-a now answers first;
- filter the new marker in a child's-sick test helper.

**Owed at merge:**
- close QUAL-248;
- the three audit-catalog rows;
- regenerate `db-schema.md`.

**Owed at close:**
- the ADR-026 D2 note;
- a stale comment in `ProjectionBackfillService.cs:133`;
- the backfill tool not printing the new counters.

**Next.** TASK-14840-T dispatched.
- **TASK-14840-T** (`test-qa`; tip `c4e76ada` on `b1839b86`; report `.claude/reviews/S148-TASK-14840-T-report.md`).
  - **New facts:** 28 Docker facts across seven new files, plus one Unit mapper fact. They cover:
    - the clear itself;
    - authority;
    - the quota give-back, including AC-CL-36 (a clear-only request against a missing row) and AC-CL-37 (the net change per type);
    - the bound;
    - the races, forced with a lock-holding connection and no sleeps;
    - the rebuild's replay of clears;
    - payroll.
  - **M-17 proved locally.** Dropping one field from the absence-clear audit mapper fails the byte-stable golden.
  - **M-18 … M-21:** each run locally (nothing reddens outside Docker); their Docker reds are argued.
  - **Re-points:**
    - 14811-T's empty-intervals Rule B fact is now `ClearingWorkOnStoredCallHomeDay_Refused`, a `cleared.workTime` request;
    - the child's-sick helper filters the new marker;
    - the schema copy has the new column;
    - the S8 docstring.
  - **Kind corrections:**
    - AC-CL-20 (2) is F in its form.
    - AC-CL-38 errors on the base (no such column), so its red evidence is M-21.
  - **Local gate:** Unit 1721.
- **REGEN** (`41c66b19`): `openapi.json` adds `cleared` and four schemas, 77 lines, additions only, as predicted.
- **Dispatched in parallel:**
  - TASK-14841 (`ux`);
  - 5a Fable and 5a Codex on `728f4702..41c66b19` (high-risk: legal rule logic, auditability, concurrency).
- **5a on `728f4702..41c66b19` (14840 + 14840-T + REGEN), cycle 1:**
  - **Codex: APPROVED-WITH-WARNINGS**, 0 B / 1 W / 1 N (`S148-TASK-14840-5a-codex.md`).
  - **Fable 5.2: APPROVED-WITH-WARNINGS**, 0 B / 1 W / 8 N (`S148-TASK-14840-5a-reviewer.md`).
  - **Both found no production defect**, and both accept all four declared deviations.
  - **Codex confirmed:**
    - one transaction per clear;
    - no double give-back under concurrent saves;
    - replay never resurrects a cleared day or wipes a re-registered one;
    - every reader of the work-time table filters the marker.
  - **Fable's W: the "all or nothing" promise is untested.** Every fact that claims it is refused before the transaction opens. Fable's fix is a race fact in which the in-lock quota guard refuses *after* the clears were written, and nothing may survive.
  - **Codex's W: two R claims are overstated.** AC-CL-33 (2) reads the new column, so it errors on the base. AC-CL-38 fails earlier than stated.
  - Both W's, Fable's docstring note, and (if cheap) a fact for the untested "seed quota unresolved" branch go to 14840-T fix-up 1.
  - **Owed in the merge commit:**
    - `db-schema.md` regenerated;
    - three audit-catalog rows;
    - QUAL-248 closed;
    - the rebuild's stale class summary;
    - the backfill tool printing the new counters.
  - **Recorded neutrally for close:**
    - a clear of a work day needs its outbox row to stay, beside O-31; nothing prunes the outbox today;
    - whether a give-back can reach an already-settled holiday year (pre-existing; check reachability).
- **TASK-14840-T fix-ups 1 and 2** (tips `bfc324c6`, `1fa8b152`; `S148-TASK-14840-T-c2-report.md`):
  - **The new rollback fact `ClearThenInLockQuotaRefusal_RollsBackTheClear`** forces the in-lock quota guard to refuse after the clears were written. Nothing may survive: no event, no audit row, the row still current.
    - Both 5a cycle-2 lenses found it would time out on the base as first seeded. Fix-up 2 seeds 23 instead of 24, so it is genuinely green on the base.
    - It also ties the 422 to the quota guard by its body.
  - **AC-CL-33 (2)** reads base-compatible columns only.
  - **Docstrings corrected:** AC-CL-38, -3 and -4.
  - **The "seed quota unresolved" branch fact is not built.** It would need its own container, since it changes global config. It is registered as a test gap at merge.
  - **Fix-up 2's full gate was reaped** for low memory, with two heavy runs at once. Only the Regression project was built (0 errors). The edits touch only Docker facts and docstrings; the full gate runs on master after the merge.
- **5a cycle 2 on `41c66b19..bfc324c6`:** Codex and Fable both APPROVED-WITH-WARNINGS (`…-5a-codex-c2.md`, `…-5a-reviewer-c2.md`).
  - Both closed their cycle-1 W's.
  - Both found the same new W: the rollback fact was not green on the base. Fixed in fix-up 2.
  - Cycle 3 is dispatched on `bfc324c6..1fa8b152`, static reading only, to spare memory.
- **TASK-14841** (`ux`; tip `c777b757` on `41c66b19`; `S148-TASK-14841-report.md`):
  - **What changed for the employee:**
    - Emptying a cell, or typing 0, now posts an explicit removal.
    - Blanking every period in the day panel removes the day.
    - Half-typed periods send nothing, so the screen never posts an empty day (W-a).
    - A removal of something actually saved is announced once ("Registreringen er fjernet") in its own polite live region, without `role="status"`.
    - A 422 restores the value. A 409, 5xx or network failure keeps the cell empty and re-queues the removal.
  - **Gate:**
    - tsc, lint, the e2e typecheck and the build are green.
    - The agent's full vitest was reaped. The Orchestrator ran it in the foreground: 93 files, 1117 tests, **1 failed**.
    - The failure is `employeeTypedWire.test.ts`, the save-body key-set pin, which still pinned the old "0 and null are dropped" behaviour. It is a missed re-point, not a product defect.
  - **Fix-up 1 dispatched:**
    - re-point that test to the new `cleared` contract;
    - fix the agent's own found defect (a 0-edit removal refused with 422 did not revert);
    - the full vitest run;
    - RED runs on the base.
- **5a cycle 3 on 14840-T (`bfc324c6..1fa8b152`): Codex APPROVED and Fable 5.2 APPROVED** (`S148-TASK-14840-5a-codex-c3.md`, `…-reviewer-c3.md`). The rollback fact is green on the base and pins the rollback with the feature; its body is tied to the quota guard. Both lenses have now approved the 14840 backend and its tests.
- **5α over Branch A `728f4702..1fa8b152` and Branch B `41c66b19..c9b03f4d`: PASS** (`S148-TASK-14840-14841-5alpha.md`).
  - Every commit is in scope; the payroll and SharedKernel model edits are comment-only.
  - The three events are registered with the serializer, the rebuild and the audit mappers.
  - Every reader of the work-time table filters the marker.
  - The `shortcut(QUAL-248)` line is deleted.
- **TASK-14841 fix-up 1** (tip `c9b03f4d`; `S148-TASK-14841-c2-report.md`):
  - The save-body key-set pin is re-pointed to the new `cleared` contract.
  - The agent's found defect is fixed at the root: a 0 edit is treated as empty, so a refused removal restores the stored value. It is pinned by a new F test.
  - **Gate:** vitest 94 files / 1118 tests green, with RED shown on the base for every new file.
- **5a Fable on TASK-14841 (`41c66b19..c9b03f4d`): APPROVED-WITH-WARNINGS**, 0 B / 4 W / 9 N (`S148-TASK-14841-5a-reviewer.md`):
  - **W-1:** the screen-reader announcement was visible text that never cleared and overlapped "Indlæser…".
  - **W-2:** blanking a day and then retyping still sent a removal mid-edit.
  - **W-3:** the "announced once" test could not fail.
  - **W-4:** a refused work-time removal restored the grid but not the open day panel.
  
  Fable accepted the dropped `role="status"` (aria-live polite + atomic is equivalent) and the three edits outside the range. All four W's, the fixture-shape note, a re-queue test and three PAT-032 comments go to 14841 fix-up 2. A **5a Codex pass on 14841** follows, because the stack's checklist row carries the high-risk Codex lens.
- **TASK-14841 fix-up 2** (tip `0b163e16`; `S148-TASK-14841-c3-report.md`): Fable's four cycle-1 warnings were answered, and vitest was 94 files / 1121 tests, green.
- **5a cycle 2 (Fable) and cycle 1 (Codex) on 14841: both CHANGES-REQUIRED** (`S148-TASK-14841-5a-reviewer-c2.md`, `S148-TASK-14841-5a-codex.md`).
  - **Fable's BLOCKER:** the "screen-reader only" fix used `visually-hidden`, a class defined in a stylesheet the app never loads. The announcement therefore still printed in the month header, and the test passed only because it checked the class name. Fable notes this was partly its own cycle-1 error.
  - **Fable's W-A:** half-typing after a removal was already sent put the old times back in the grid, after the server had removed them.
  - **Codex's two W's:**
    - a half-typed day could still post a pending valid edit;
    - a refused cell removal stayed visibly blank while the input kept focus.
  - **Codex also noted an inherited limit:** the save queue orders saves only within 15 seconds, so a stalled older save could still resurrect a value. This is to be registered at close; it needs server-side ordering.
  - **Disposition: 14841 fix-up 3.**
    - A module-scoped screen-reader-only class, pinned by its styling and not its name.
    - One half-typed rule from both lenses: withdraw an unsent change for that day and restore the grid from server truth; leave an in-flight change to its result.
    - Reset the input overlay after a refusal. The Orchestrator authorizes code changes in `SkemaGrid.tsx` and `SkemaDayPanel.tsx` for this.
    - Keep keyboard focus on re-seed, by reusing row ids.
    - Read the open day from a ref.
    - Record the accepted half-typed-second-row loss in a comment.
- **TASK-14841 fix-up 3** (tip `42216942`; `S148-TASK-14841-c4-report.md`).
  - **Screen-reader class:** the screen-reader-only class now lives in the page's own CSS module, and its test pins the styling rule.
  - **One half-typed rule:** an unsent change for the day is withdrawn; an in-flight change is left to its result.
  - **Focused input:** the typing overlay of a focused input resets after a refusal.
  - **Re-seed:** keeps keyboard focus, and reads the open day from a ref.
  - **Gate:** the agent's full vitest run was reaped for memory; the Orchestrator ran it in the foreground, green at 94 files / 1126 tests.
- **5a, 14841 fix-up 3:**
  - **Fable 5.2 cycle 3:** APPROVED-WITH-WARNINGS (`…-5a-reviewer-c3.md`). It closes all seven prior findings, including Codex's W1 and W2.
  - **Codex cycle 2:** CHANGES-REQUIRED (`…-5a-codex-c2.md`). Its cycle-1 cases are closed.
  - **The same two narrower timing defects, found by both lenses:**
    - with a second edit on top of a save still in flight, followed by half-typing, the grid ends showing times the server does not hold;
    - a refused removal can erase text the user has started but the page has not taken in yet (a leading "," or "-").
  - **Disposition: 14841 fix-up 4.** Each defect is answered as one general rule rather than another special case:
    - on withdraw, restore to the newest in-flight value; on a 200, set the grid to what was confirmed unless a newer change is pending; never re-queue a half-typed day after a 5xx;
    - restore an input after a refusal only if the user has not typed since.
  - The CSS pin, the ref and the comment notes are folded in.
  - **Review budget:** 14841 is at Fable cycle 3 and Codex cycle 2, against a cap of 5 per lens.
- **TASK-14841 fix-up 4** (tip `a2483a53`; `S148-TASK-14841-c5-report.md`): both timing defects answered as general rules (the newest in-flight value on withdraw and on a 200; an overlay reset only when untouched since propagation). Vitest 94 files / 1132 tests, green.
- **5a on fix-up 4:**
  - **Codex cycle 3: CHANGES-REQUIRED** (`…-5a-codex-c3.md`). Its earlier cases are closed. New: overlapping saves past the 15-second queue bound can settle newest-first, leaving the grid stale. The fix is to track every outstanding save.
  - **Fable 5.2 cycle 4: APPROVED-WITH-WARNINGS**, "correct enough to merge" (`…-5a-reviewer-c4.md`). Three new warnings:
    - **W-1:** a failed removal is not retried after the user half-typed and closed the panel.
    - **W-2:** a refused removal stays blank in a cell the user returned to after typing elsewhere.
    - **W-3:** test (iv) does not pin its rule.
    
    Fable would register Codex's out-of-order case rather than fix it.
- **Disposition: 14841 fix-up 5, the final round** under the review cap (Codex cycle 4, Fable cycle 5 next). It takes the frontend half of Codex's case (per-save tracking, which closes Codex's only open warning) and all three of Fable's warnings.
- **Registered at merge:**
  - the server-side half: a stalled older save can still commit after a newer one, and the client cannot prevent that write. This needs server ordering or version checks, and is the inherited limit Codex named in cycle 1;
  - Fable's accepted contract: a failed *edit* on a day left half-typed is dropped when the panel is closed, and the failure alert is the only signal.
- **TASK-14841 fix-up 5** (tip `7caaab7c`; `S148-TASK-14841-c6-report.md`).
  - **What it fixed:**
    - per-save in-flight tracking: the newest outstanding save, else server truth, everywhere;
    - a failed removal re-marked and retried;
    - a refused cell restored after the user returns to it;
    - the 5xx test now pins its rule.
  - **Vitest:** 94 files / 1137 tests, green.
- **Final 5a on 14841:**
  - **Fable 5.2 cycle 5 (the cap): APPROVED, merge-ready** (`…-5a-reviewer-c5.md`). All findings are closed. Fable would register the cases beyond the 15-second bound.
  - **Codex cycle 4: CHANGES-REQUIRED** (`…-5a-codex-c4.md`).
    - The case: three saves of one day (09:00, 10:00, 11:00), all stuck past 15 s. The server confirms 11:00, then 09:00, and the screen falls back to the still-outstanding 10:00.
    - If 10:00 then fails and is retried, it overwrites the confirmed 11:00. It needs only delayed answers, not out-of-order server commits.
- **Owner Q6 (2026-11-12): "Fix it, allow 1 extra round (Recommended)".**
  - **The question.** The lenses had reached the five-cycle cap and disagreed: Fable said merge, Codex said fix. A fix needed a sixth Fable cycle, past the cap. The alternative was to register the case beside the server-ordering limit and merge.
  - **The ruling.** Fix it, as a one-time exception to the cap recorded here: one more small round, then Codex cycle 5 and Fable cycle 6.
  - **The fix (14841 fix-up 6).** Track per day the newest confirmed save. Ignore, and never retry, a save older than it. Fable's two notes are folded in: the re-mark reads the computed target, not a lagging mirror, and the "server truth" comments are made true.
- **TASK-14841 fix-up 6** (tip `239e2b0d`; `S148-TASK-14841-c7-report.md`). Per day, the newest confirmed save wins: an older outstanding save is ignored and never retried. The grid mirror is patched in step with the restore, and the comments are made true. Vitest 94 files / 1138 tests, green; the new F test is red on the base (10:00 where 11:00 is expected).
- **Final 5a on 14841 fix-up 6:** **Codex cycle 5 APPROVED** and **Fable 5.2 cycle 6 APPROVED** (owner Q6's one extra round; `…-5a-codex-c5.md`, `…-5a-reviewer-c6.md`). Both called it merge-ready; neither raised a blocker or a warning.
- **Merged** (octopus, `--no-ff`): the TASK-14840 stack (`1fa8b152`: 14840, 14840-T fix-ups 1–2, REGEN) together with TASK-14841 (`239e2b0d`). The server refuses an empty work-time day from this merge on, and the screen never sends one.
  - **In the merge commit:**
    - `db-schema.md` regenerated (the new `cleared_by_outbox_id` column);
    - three audit-catalog rows (`TimeEntryCleared`, `AbsenceCleared`, `WorkTimeCleared`);
    - the rebuild's class summary made true, plus the backfill tool's missing cleared counters;
    - QUAL-248 **closed FIXED**.
  - **Registered:**
    - **QUAL-252 (M):** a stalled Skema save can still be written after a newer one. The client orders saves only within 15 s, and the server not at all. Its residuals: the untested mirror patch, and the accepted half-typed-then-closed contract.
    - **Three below-floor notes:** the untested "seed quota unresolved" branch; a clear's dependence on its outbox row (beside O-31); the settled-year give-back reachability (pre-existing).
  - `check_docs`: 12 shortcuts, all registered. No `shortcut(pending)`, and the `shortcut(QUAL-248)` line is gone.
- **GATE after the last wave-2 stack, on master `a331e452`: green.**
  - Build: 0 errors, 143 warnings, CA2100 113.
  - Backend tests: Unit 1721, DemoSeed 170, Regression non-Docker 140.
  - Frontend: tsc, lint, e2e typecheck and build all green; vitest 94 files / 1138 tests.
  - **Wave 2 is fully merged:** 14809→14810, 14811→14812 (with owner Q5's rules), 14842, and 14840→14841 (with owner Q6's extra round).
  - **Next:** CI-W2, CI-M2 and CI-M2b.

**CI-W2 started** (draft PR #57, run 43233683529, `ci/s148-w2` at `03bde472`, headSha confirmed). It is the first run of wave 2's Docker facts.

**CI-M2 and CI-M2b: the mutation branches are built.** The builder was a re-spawned `test-qa`; its report is `.claude/reviews/S148-CI-M2-M2b-builder-report.md`.
- **What these runs are for.**
  - A mutation is a deliberate break in production code. Each test written to guard a behaviour should go red when that behaviour is broken.
  - Docker cannot run here, so for the Docker tests that red has only been argued. These two runs are where it is observed.
  - The *frozen list* is written before CI runs. It names every test that must go red, and the check it must fail at first. That way a red cannot be explained away afterwards.
- **CI-M2** (`ci/s148-m2`): the mutation commit `0f1e9b85`, carrying M-8, M-18, M-19, M-20 and M-21, then the evidence-only `ci.yml` commit `d12c7a94`.
  - The `ci.yml` commit adds `if: ${{ !cancelled() }}` to three steps, three lines in all (rule 3).
  - Locally at the mutation commit: build 0 errors, 143 warnings; Unit 1719 passed, 2 failed (both M-8, observed); DemoSeed 170; Regression non-Docker 140.
- **CI-M2b** (`ci/s148-m2b`): `78f6a719`, carrying M-22 and M-23, then `06ff9fee`, the same three-line `ci.yml` commit.
  - Locally: Unit 1720 passed, 1 failed (M-22, observed); DemoSeed 170; Regression non-Docker 140.
- **Census corrections against the paired passes.** None changes a design; all are recorded here before CI.
  1. **M-8 trips nine more facts than 14811-T counted.** Its census predates the Rule A and Rule B facts and 14840-T's re-pointed call-home fact. Every one of the nine goes red at the vocabulary gate, which is a save step (NR-P29). So they show only that M-8 makes the save refuse the code.
  2. **M-18 also turns AC-CL-35 red**, not only AC-CL-34 as 14840-T's census said. A re-save queued behind a clear takes its give-back from the stale pre-lock sum, so the balance ends at −0,5.
  3. **M-18 is a reconstruction.** The only pre-transaction read of absence rows is the phase-2 snapshot, which the code reduces straight to a sum. The builder kept its rows, matching 14840-T's three edits. When the snapshot does not run, the in-lock row is used, so those clears are unchanged.
  4. **Two cites have drifted:**
     - the vocabulary gate is now `SkemaEndpoints.cs:1062` (the plan says `:970`);
     - the unmapped-line skip is in `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:3958-3963`.
- **The CI-M2 frozen list** (verbatim in `0f1e9b85`'s message). O means observed locally; A means argued, and CI is where it is observed.
  1. Unit `AbsenceClassificationTests.EveryVocabularyCode_HasExactlyOneClassification`: M-8; the entry count, expected 12, actual 11, `:81`. O
  2. Unit `AbsenceClassificationTests.EveryCode_HasTheSpecifiedEffect_AndTheEngineReadsTheSameTable(CHILD_SICK_CALL_HOME, Credit)`: M-8; "CHILD_SICK_CALL_HOME has no classification", `:132`. O
  3. `ChildSickCallHomePayrollTests.CallHome3Point5Hours_Worked3Point9_Credited`: M-8; the save's 200 assertion in `SaveCallHomeAsync` (`:116`), which gets 422 `absence_type_unknown`. NR-P29: a save step, not the credit half. A
  4. `…_ExportedUnderSls0530`: M-8; the same save assertion. A
  5. `ChildSickIllnessStartTests.CallHomeThenDayOne_Accepted`: M-8; the first `AssertOkAsync`, a setup save. A
  6. `ChildSickIllnessStartTests.CallHomeAfterCallHome_Refused`: M-8; the first `AssertOkAsync`, a setup save. A
  7. `ChildSickIllnessStartTests.CallHomeWithIntervals_Accepted`: M-8; `AssertOkAsync`. A
  8. `ChildSickIllnessStartTests.CallHomeWithOnlyProjectHours_Accepted`: M-8; `AssertOkAsync`. A
  9. `ChildSickIllnessStartTests.ClearingWorkOnStoredCallHomeDay_Refused`: M-8; the setup call-home save. A
  10. `ChildSickIllnessStartTests.DayOneThenCallHome_CallHomeRefused`: M-8; the refusal's `error` is `absence_type_unknown` where `absence_type_not_eligible` is expected. The vocabulary gate answers before Rule A. A
  11. `ChildSickIllnessStartTests.CallHomeWithoutWork_Refused`: M-8; `error` is `absence_type_unknown`. The vocabulary gate answers before Rule B. A
  12. `ChildSickSaveTests.HkOk24_CallHome_NotOffered_Returns422`: M-8; `error` is `absence_type_unknown`. A
  13. `ChildSickSaveTests.NoOptIn_CallHome_Returns422_OptInBody`: M-8; `error` is `absence_type_unknown`. A
  14. `SkemaCellClearRaceTests.ClearQueuedBehindAResave_GivesBackTheInLockRow` (AC-CL-34): M-18; `clearedFeriedage` expected 0,5, actual 1,0 (then `used` −0,5). A
  15. `SkemaCellClearRaceTests.ResaveQueuedBehindAClear_SaveRowIsCurrent` (AC-CL-35): M-18; the last assertion, `used` expected 0,5, actual −0,5. Census correction 2. A
  16. `TeamOverviewAggregateTests.AwayToday_False_AfterTodaysAbsenceCleared` (AC-CL-24): M-19; the final `Assert.False(await AwayAsync())`. A
  17. `ProjectionBackfillClearTests.UnpublishedClear_SurvivesTheRebuild` (AC-CL-33 (1)): M-20; `(marker, outbox_id)` reads `(−1, registration id)`. It is red under M-21 too, because the unpublished clear is never replayed. A
  18. `ProjectionBackfillClearTests.PublishedClear_UnpublishedReRegistration_StaysCurrent` (AC-CL-38): M-21; `(intervals, outbox_id, marker)`, where the marker is the clear's id. It is red under M-20 too. A
  19. `SkemaCellClearTests.ClearWorkTimeDay_RowKeptAndMarked_EveryReaderOmitsTheDay` (AC-CL-3): M-20; `Assert.Equal(clear.OutboxId, kept.OutboxId)`. A
  20. `ProjectionBackfillClearTests.FromEmpty_ClearsReplayed_EqualsLive` (AC-CL-22 (1)): M-20; the final work-time tuple. A
  21. `ProjectionBackfillClearTests.RerunOverPopulated_ResurrectsNoClearedCell` (AC-CL-22 (2)): M-20; the per-day `AssertAllClearedAsync` tuple. A
  22. `ProjectionBackfillClearTests.SecondRerun_ChangesNothing` (AC-CL-22 (3)): M-20; the first `AssertAllClearedAsync`. A
  23. `ProjectionBackfillClearTests.DrainedThenRerun_ClearedStayCleared_ReRegisteredStaysCurrent` (AC-CL-33 (4)): M-20; the final work-time tuple. A
- **Argued green under CI-M2:**
  - **Under M-8:**
    - the reflection fact and the call-home golden-line row (they read constants and the engine line);
    - `ChildSickCatalogueTests`, the 12-row S119 fact, the allowance tile, the mapping fact and the chain facts;
    - no host start-up check of the classification exists, so every Docker host starts;
    - the demo seed, smoke and e2e never use the code.
  - **Under M-20 and M-21:** AC-CL-33 (2) and (3).
  - **Under M-18:** `TwoConcurrentClears_SameVacationCell_GiveTheDayBackOnce`, `ClearQueuedBehindASend_409_RowStaysCurrent`, and every clear fact without a race.
  - **Under M-19:** the other `awayToday` facts.
- **Independence:** M-20 and M-21 edit one statement, and under both, pins 17 and 18 still reach their "must trip" assertion. M-8 touches only the call-home code, while M-18 uses VACATION and M-19 uses SICK_DAY.
- **The CI-M2b frozen list** (verbatim in `78f6a719`'s message):
  1. Unit `EveryCode_HasTheSpecifiedEffect…(CHILD_SICK_CALL_HOME, Credit)`: M-22; expected Credit, actual Neutral. O
  2. `ChildSickCallHomePayrollTests.CallHome3Point5Hours_Worked3Point9_Credited`: M-22; the closing-flex member of the `Assert.Multiple`, expected 1,00, actual −2,50. The save answers 200 first. A
  3. `…_ExportedUnderSls0530`: M-23; `Assert.Single` finds no call-home export line, because payroll skips an unmapped line. A
  4. `ChildSickCallHomeMappingTests.EveryAgreement_HasAnOk26Sls0530Row`: M-23; "AC/OK26: … found []". A
- **Argued green under CI-M2b:**
  - **Under M-22:** the export fact, the mapping fact, the 12-entry count, and every Rule A, Rule B and save fact.
  - **Under M-23:** the credit fact, the `wage_type_mappings` count, idempotency and schema facts, the DemoSeed, smoke and e2e.
  - **Independence:** neither mutation turns red a fact on the other's list.
- **Next:** push both branches as draft PRs and watch them. Each is judged against its list by the plan's rules (`PLAN-s148.md:576-585`): a pin counts only when its trx message is the listed assertion.

**Wave-3 dispatch briefs: adjudicated.**
- **Who did what:**
  - the `planner` (Opus 5.5) drafted TASK-14813, 14813-T and 14814; its report is `.claude/reviews/S148-wave3-briefs-planner-report.md`;
  - the `adjudicator` (Fable 5.2) ruled on them, verdict RULED (`.claude/reviews/S148-wave3-briefs-adjudication.md`);
  - the planner then applied the rulings: plan "final + 7" and all three briefs at draft 2.
- **What wave 3 changes, in plain terms:**
  - **Today:** the server's 11-hour rest check (the one a manager sees) only reads clock times on project hours. Skema stores none there, so every Skema day shows "Hviletid ikke kontrolleret" and no real short rest is ever reported. Meanwhile the screen's instant warning flags a false 0,02-hour rest when someone types a night shift as 22:00–23:59 plus 00:00–06:00.
  - **After wave 3:** the server reads the clock periods typed in Skema and joins the two night halves into one shift. A night until 06:00, then work from 16:00, is reported as a 10-hour rest.
  - **Shared rule:** the screen runs the same algorithm, and one shared table of twelve scenarios, read by both test runners, proves the two always agree.
  - **The "not checked" notice** stays, but only for days whose hours are not all on the clock. It turns blue (informational) instead of amber, on Skema and Teamoversigt alike.
  - **Screen reader:** Skema's warnings panel is announced once when it changes.
  - **Pay** does not change.
- **The rulings that change what gets built:**
  - **A short rest across a month boundary is now reported**, once, in the month where the rest ends, dated the day the earlier shift began.
    - Today a 9-hour rest from 31 March to 1 April appears in neither month: March's read stops at month end, and April's check drops a shift that lies wholly in March.
    - New: the Docker fact AC-S0-3f and the mutation M-26.
  - **The voluntary unsocial-hours exemption is kept, decided per day.** A day counts as voluntary only when every time entry on it carries the flag. The product spec still requires the exemption. Nothing can set the flag until stage 2 (a registered shortcut), so nothing observable changes now.
  - **The screen follows the engine's model of shifts**, rather than its own "first start, last end per day" rule. Two table rows pin the cases where the two used to disagree: a continuation day with no other work, and a chain over two midnights.
  - **Two pins become kind N:** AC-S0-3b and the C# half of AC-S0-4a. Both name an input the task creates, so they cannot compile on today's code. They are proved red by three local mutations at 14813-T's own gate: M-24 (no midnight join), M-25 (any clock period counts as checked) and M-26 (the old month-edge filter). Wave 3 has no CI mutation run.
- **Four things the owner should know** (no decision needed):
  1. A short rest across a month boundary is now reported, in the month where it ends, dated the earlier day.
  2. The voluntary exemption is kept at day level, and nothing can set the flag until stage 2.
  3. The "not checked" notice is blue on Teamoversigt too.
  4. The narrowed notice stays (R-22, as O-9 already says).
- **Settled by the Orchestrator at application:**
  - The `:315` ledger row says the re-pins span four existing test files, not one (14813-T BC-1).
  - TASK-14813's cross-domain label is widened to name `ComplianceCheckResult.cs :11-37` (comment only) and `RestPeriodNotCheckedTests.cs :122-138` (deletion only), on 14809's precedent.
- **Counts:** 117 AC ids (50 stage 0, 67 config stage, AC-S0-3f added), 85 owned by a paired pass; checked by a count script calibrated on "final + 6".
- **Owed at close:**
  - a live-region line in `docs/FRONTEND.md`;
  - ADR-039 and ADR-043 D10 notes;
  - register rows:
    - the now-unread `SourceStintId` link;
    - the third private parse copy;
    - Teamoversigt's per-line live regions;
    - the two month-edge limits: a chain starting before the read window is dated at the first day read; April's notice does not name an unclocked 31 March.
- **Dispatch** waits for CI-W2 green (TASK-14813's prerequisite) and for CI-M2 and CI-M2b to match their frozen lists.

**CI-W2** (run 43233683529, draft PR #57, at `03bde472`, headSha confirmed): **green, all seven jobs.**
- **What it shows:** every Docker fact that wave 2 wrote or re-pinned ran for the first time, and all of them passed. Until now they had only been argued. Read from the run's trx files, by name, not from the job colour.
- **Tests:**
  - Unit 1721, DemoSeed 170, and Regression 2555 of 2555, Docker included (CI-W1b had 2458).
  - Nothing failed and nothing was skipped.
- **Facts seen passing, by name:**
  - the child's-sick facts: 42 Regression and 10 Unit, including Rules A and B (`DayOneThenCallHome_CallHomeRefused`, `CallHomeWithoutWork_Refused`);
  - the call-home credit and export facts (`…_Credited`, `…_ExportedUnderSls0530`);
  - the five-row mapping fact `EveryAgreement_HasAnOk26Sls0530Row`;
  - the cell-clearing facts: 46 `SkemaCellClear*` and 9 `ProjectionBackfillClearTests`, including the race facts AC-CL-34/35 and the rebuild facts AC-CL-33 (1) and AC-CL-38;
  - `AwayToday_False_AfterTodaysAbsenceCleared` (AC-CL-24);
  - the balance-seed facts;
  - the paid-absence bound facts;
  - both PIN-1 worlds (`World20260115_December2025_EqualsGolden`, `World20260715_June2026_EqualsGolden`), so pay is unchanged by wave 2;
  - `ConfigSeedParityTests` and `DemoPinSeedSelectionTests`.
- **The other jobs:** smoke, E2E, the frontend build, docs, lizard and gitleaks are all green.
- **Run time:** about 100 minutes in all (14:05 to 15:45 UTC).
- **Status change:** wave 2's Docker facts move from "CI-pending" to "CI-verified" (run 43233683529). PR #57 is closed unmerged and `ci/s148-w2` deleted.
- **Wave 3 dispatch is unblocked** (TASK-14813's prerequisite). No wave-3 merge happens before CI-M2 and CI-M2b are judged against their frozen lists (`PLAN:119`).

**CI-M2 and CI-M2b: judged, both as frozen.** Each run deliberately broke production code, and was expected to end red on exactly the tests named in its frozen list, at the named assertion. Both did. They were read from the trx files of runs whose headSha matched the pushed tips. Both had a Regression trx.
- **What this proves:** every wave-2 Docker pin that rested on a mutation can fail for the defect it exists for. Until now that was argued; now it is observed.

**CI-M2** (run 43238030414, draft PR #58, at `d12c7a94` = mutations `0f1e9b85` + the evidence-only `ci.yml` commit).
- **Jobs:** `build-and-test` red, as designed. Smoke, E2E, the frontend build, docs, lizard and gitleaks are green. Smoke and E2E staying green confirms the census: neither uses the call-home code.
- **Unit:** 1719 passed, 2 failed, frozen rows 1 and 2:
  - the entry count, `Expected: 12 Actual: 11` at `:81`;
  - "CHILD_SICK_CALL_HOME has no classification" at `:132`.
- **DemoSeed:** 170/170.
- **Regression:** 2534 passed, 21 failed. These are exactly frozen rows 3 … 23, with no unlisted red and no listed fact green. Each pin failed at its "must trip" assertion:
  - **M-8** (rows 3–13): every call-home fact was refused at the vocabulary gate. The response was 422 `absence_type_unknown` at the save, or `absence_type_unknown` in place of `absence_type_not_eligible`. This is the save step, as NR-P29 narrowed it: M-8's evidence is "the save refuses an unclassified code", not the credit or export halves. Those halves are CI-M2b's.
  - **M-18:**
    - AC-CL-34: `clearedFeriedage` expected 0.5, actual 1.0 (`SkemaCellClearRaceTests.cs:150`).
    - AC-CL-35, the census correction: `used` expected 0.5, actual −0.5 (`:240`).
  - **M-19:** AC-CL-24 `Assert.False(away)`: actual True (`TeamOverviewAggregateTests.cs:748`).
  - **M-20:** AC-CL-33 (1) `(marker, outbox_id)` expected (40, 40), actual (−1, 39) (`ProjectionBackfillClearTests.cs:252`). The census facts AC-CL-3 (157 vs 156), AC-CL-22 (1)–(3) and AC-CL-33 (4) failed at their work-time tuples.
  - **M-21:** AC-CL-38 `(intervals, outbox_id, marker)` expected ("09:00-13:00", 41, null), actual (…, 41, 40) (`:408`). The combined M-20 + M-21 reading held exactly: the marker is set, and `outbox_id` stays the re-registration's.

**CI-M2b** (run 43238051149, draft PR #59, at `06ff9fee` = mutations `78f6a719` + the same `ci.yml` commit).
- **Jobs:** `build-and-test` red, as designed; the other six jobs green.
- **Unit:** 1720 passed, 1 failed, row 1: `Expected: Credit Actual: Neutral`.
- **DemoSeed:** 170/170.
- **Regression:** 2552 passed, 3 failed. These are exactly rows 2–4:
  - **M-22, the credit fact:** the closing flex expected 1.0, actual −2.50 (`ChildSickCallHomePayrollTests.cs:78`), exactly the census value. The save answered 200 first.
  - **M-23, the export fact:** `Assert.Single` found no call-home export line (`:99`).
  - **M-23, the mapping fact:** "AC/OK26: expected exactly one open CHILD_SICK_CALL_HOME mapping, to SLS_0530; found []" (`ChildSickCallHomeMappingTests.cs:64`).
- **Independence observed:**
  - the export and mapping facts did not fail on M-22's credit change;
  - the credit fact failed with M-22's arithmetic, not on the mapping;
  - neither mutation changed what the save accepts.

**What this settles:**
- **Fable c5 N-1** (Step 0b, carried in the plan's M4 list): with M-8 narrowed to the save gate, the "exported under `SLS_0530`" half had no red evidence. It is **closed with evidence**: M-23 turned the export fact red, and M-22 the credit fact.
- **The wave-2 mutation evidence is complete:**
  - M-8 and M-18 … M-23 all observed red at their pins;
  - M-17 was proved locally at 14840-T's gate;
  - the Rule A and Rule B pins (M-A, M-B) stay argued, since their F facts' base red is their evidence.

**Clean-up:** PRs #58 and #59 closed unmerged; `ci/s148-m2` and `ci/s148-m2b` deleted, remotely and locally; the builder's worktree removed.

**The wave-2 gate (`PLAN:119`) is passed:** CI-W2, CI-M2 and CI-M2b. Wave-3 merges are unblocked once the stack clears its merge checklist.

**Wave 3: the stack built.** Each task was dispatched in its own worktree; the reports are saved in `.claude/reviews/`.
- **TASK-14813** (`rule-engine`; `ea77c138` on `ff529b10`): the server's 11-hour rest check reads the clock intervals typed in Skema.
  - **The request:** `CheckComplianceRequest.WorkIntervals`, a list of `ComplianceWorkInterval(Date, Start, End)`. The Backend fills it from current (not cleared) work-time days over `[monthStart−1, monthEnd]`, after the access, month and employment-window gates.
  - **The engine:**
    - joins a 00:00 start to the previous day's 23:59-or-later end, repeating over several midnights;
    - groups stints by start day and pairs consecutive groups;
    - judges a rest in the month where it ends;
    - lists a day as not checked when its clock hours are below its project hours.
  - `EvaluateDailyRest` and `EvaluateWeeklyRest` are retired.
  - **Scope and local gate:** exactly the six authorized files. Build 0 errors / 143 warnings / CA2100 113. Ten Unit facts went red as predicted (the -T re-pins them). DemoSeed 170 and Regression non-Docker 140 stay green.
- **TASK-14813-T** (`test-qa`; `f86aacaf`): nine test files.
  - **Re-pins:** the ten red facts, plus four that had gone vacuous.
  - **New Unit facts:** AC-S0-3b (kind N), the parity C# fact over a twelve-row shared table (`tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json`, N), and the voluntary-day R fact.
  - **New Docker facts:** AC-S0-3a, -3c and -3f, F (CI), on a real-engine harness.
  - **Mutations observed locally:** M-24 (no midnight join), M-25 (any interval counts as checked) and M-26 (the old month-edge filter) each turned exactly their named facts red.
  - **Local gate:** Unit 1735, DemoSeed 170, Regression non-Docker 140.
- **TASK-14814** (`ux`; `846789a1`): eleven frontend files.
  - **The screen's rest warning** runs the engine's algorithm, so the false "0,0 timers hvile" at midnight is gone.
  - **The parity vitest** reads the same table (`import.meta.glob` reached it; no `node:fs` exception).
  - **The warnings panel** is one always-mounted live region.
  - **The "not checked" notice** is blue on Skema and Teamoversigt.
  - **Local gate:** tsc, lint, e2e typecheck and build green; vitest 98 files / 1161 tests.
- **Step 5a cycle 1 on 14813 + 14813-T** (`ff529b10..f86aacaf`): both lenses APPROVED-WITH-WARNINGS.
  - **Codex** (`S148-TASK-14813-5a-codex.raw.md`): 2 W / 1 N.
  - **Fable** (`S148-TASK-14813-5a-reviewer.md`): 2 W / 6 N.
  - **Both recomputed all twelve parity rows by hand:** every row is correct as ruled.
- **Adjudication** (`S148-TASK-14813-5a-adjudication-c1.md`, Fable 5.2, RULED-WITH-QUESTIONS):
  1. **A manager must not see a rest finding built from an unsent month (Fable W-1). Fixed now; not an owner question.**
     - **The problem:** a manager reading a sent April could get a finding dated 31 March, built from March's clock times while March was unsent. That breaks RES-002, the rule that a manager sees nothing of an unsent month.
     - **A sibling hole, open since S72:** the Skema month view served the neighbouring months' edge days to a manager, whatever their status.
     - **The design keeps both rules:**
       - A manager's read uses a neighbouring day only once that day's month is submitted.
       - Until then the month's first day is listed as "not checked", so nothing looks falsely fine. It is listed only when the day before is an employed day and the first day has clock times.
       - Once both months are sent, the finding appears.
     - Registered as SEC-059 (fixed, with a residual) at merge.
  2. **The rest model misses real breaches (Codex W-2, Fable N-1, N-2).** It measures the rest between working days, never inside one: a day shift plus a same-day night shift, or one shift over 13 hours, gives no finding.
     - **Research ordered:** what "11 consecutive hours within each 24-hour period" means. The repo's sources did not settle it.
     - **Owner Q1 (2026-11-13): "No, register only".** No interim "not checked" marker for days over 13 hours. QUAL-253 is registered at merge and carries the gap until the full fix.
     - **Q2** (build the full check in S148 or schedule it) is asked when the research is verified.
  3. **A chain cut at the read edge could wrongly count as voluntary (Codex W-1). Fixed now:** a stint starting at 00:00 before the period is never voluntary. Latent today: the voluntary flag has no writer until stage 2.
  4. **The engine's period-edge guard had no test that could fail (Fable W-2, Codex N-1).** Two Unit pins with mutations M-27 and M-28.
  5. **Smaller items:**
     - three kind labels corrected (N-4);
     - ADR-039, ADR-043 D10, `docs/SECURITY.md` and RES-002 owed at close (N-5);
     - N-3 and N-6 declined with reasons.
- **Fix-ups dispatched on the stack tip `846789a1`:**
  - 14813 fix-up 1 (`rule-engine`): the two manager-visibility reads, the read-edge voluntary rule, and comments. Newly authorized: `SkemaEndpoints.cs:491-506`, the boundary read only.
  - 14813-T fix-up 1 follows on its tip: M-27, M-28, the read-edge fact, the manager-read Docker facts on compliance and on Skema, and the labels.
  - TASK-14814 gets no fix-up.
  - A security consult on the fix-up diff joins the merge checklist.
- **Step 5a cycle 1 on TASK-14814** (`f86aacaf..846789a1`): dispatched on both lenses.
- **The rest-definition research**, step 1 of 3 (`general-purpose`, Opus 5.5, web).
  - **Pass 1 (unverified):** the 24-hour period is a rolling window looking back from working time, not the calendar day and not 24 hours from the start of the working day. A continuous shift over 13 hours is a breach in itself. The state agreement § 12 allows a cut to 8 hours under limits.
  - **Step 2, an independent verification** that re-reads every source, is running. Step 3 is a Fable adjudication of the consequences, and then owner Q2.

**Plan "final + 8"** (`61fd7991`) carries the TASK-14813 Step 5a adjudication and owner Q1:
- the manager-visibility rule on 14813's row, with `SkemaEndpoints.cs:491-506` authorized;
- the read-edge voluntary rule;
- M-27 and M-28;
- a security consult on the fix-up diff.

The consult's output is input to the cycle-2 review, so it runs first. AC ids stay at 117.

**Step 5a cycle 1 on TASK-14814** (`f86aacaf..846789a1`): both lenses APPROVED-WITH-WARNINGS.
- **Codex** (`S148-TASK-14814-5a-codex.raw.md`): 2 W / 5 N.
- **Fable** (`S148-TASK-14814-5a-reviewer.md`): 5 W / 4 N.
- **Both found** that on saveable input the screen and the server agree on which rests are short and how they are dated.

**Adjudication** (`S148-TASK-14814-5a-adjudication-c1.md`, Fable 5.2, RULED, no owner question): every finding fixed now.
- **Rounding:** a rest is shown to one decimal with halves rounded up, everywhere.
  - The server's value used banker's rounding while its own message rounded half up.
  - The screen's `toFixed` rounded some halves down, because it works on the binary value.
  - So an 8 h 27 min rest would have read 8.5 on the server and 8,4 on the screen.
  - No pinned value moves: every pinned rest value is whole.
- **The midnight-join boundary** (23:59 or later, then exactly 00:00) is pinned on both sides by two near-miss rows, with mutations M-29 and M-30.
- **The screen never shows a negative rest:** "kun -1 timers hvile" was possible while overlapping, unsaved periods were being typed.
- **A clean month** no longer gets an extra 16 px gap. The empty live region is taken out of the layout, with `position: absolute` while empty, as the page's two other live regions do.
- **The vitest** fails if the table and its own id list differ, so a row cannot run on one side only. It also names the pairs it expects to drop.
- **The remark** says what the screen does not apply: the voluntary exemption and the derogation severity.
- **A failed compliance call** clears last month's warnings instead of showing them under the new month.
  - Side effect: a failed call now shows nothing at all.
  - Saying "could not be checked" needs copy, so that part is registered as a QUAL row at merge.
- **The shared table grows from twelve to sixteen rows.**

**Plan "final + 9"** carries this ruling. The fix-up order is:
1. 14813 fix-up 1 (in flight: manager visibility, read-edge voluntary rule);
2. 14813 fix-up 2 (server rounding);
3. 14813-T fix-up 1 (both adjudications' facts, M-27 … M-30, four table rows);
4. 14814 fix-up 1 (clamp, rounding helper, region class, the stale clear, tests).

**For the owner** (no decision):
- rest hours now round halves up on both screens and the server;
- a failed compliance check now shows nothing rather than last month's warnings.

**Notes below the QUAL floor:**
- `formatDanishNumber` rounds some halves down wherever hours are shown; no disagreement with a server figure was found outside the rest warning.
- The engine formats its finding messages in the host's culture (unproven whether that is ever Danish).
- A clean month is still to be looked at in a browser.

**The 11-hour rest rule: verified research and owner Q2.**
- **The research** (`.claude/reviews/S148-RESEARCH-rest-24h-*.md`, three steps):
  - pass 1, by `general-purpose` (Opus 5.5, web);
  - an independent verification (Opus 5.5) that re-downloaded every decisive source. All five verdicts stand, with 19 corrections;
  - a Fable 5.2 adjudication of what it means for StatsTid (RR-1 … RR-8).
- **What the law means.**
  - The daily rest is judged on the 24 hours before each work end. It is not the calendar day, and since 29 December 1996 it is not 24 hours from the start of the working day.
  - In practice no work may end more than 13 hours after the last full 11-hour rest.
  - Under that test the following are breaches:
    - one shift over 13 hours;
    - a day shift followed by a same-day night shift;
    - a split day over 13 hours from first start to last end (for example 08–12 plus 17–22).
  - "13 hours on, 11 hours off" is lawful.
  - The state agreement's § 12 allows a cut to 8 hours, once per 7 days and with compensatory rest.
  - Staff who set their own hours, and managers, can be exempt person by person (BEK 606 § 6).
  - The weekly rest day is 35 unbroken hours, and on-call never counts toward it.
  - The sources are silent on five questions; they go to the expert hand-off package.
- **Owner Q2 (2026-11-13): "S148, after wave 3 (Recommended)."**
  - Wave 3 merges as reviewed. Its plumbing all survives: the clock-time read, the midnight join, the "not checked" list, the manager-visibility rule and the screen's live region.
  - The corrected check follows as **wave 3b**, three tasks:
    - TASK-14843 (`rule-engine`): the 24-hour look-back, the 8-hour floor under the derogation flag, and the read from `monthStart − 2`;
    - TASK-14843-T (`test-qa`): the table recomputed, the new rows, and every moved value re-pinned by name;
    - TASK-14844 (`ux`): the screen on the same rule.
  - Wave 3b merges before the T14b stack (TASK-14826).
  - The answer also approves the spec wording update for the rest rule (`SYSTEM_TARGET.md:272-284`).
- **Registered later, pending expert answers:**
  - QUAL-254: how often a cut is allowed, compensatory rest, postponement;
  - QUAL-255: the per-person exemption;
  - QUAL-256: the weekly rest-day check.

  QUAL-253 is corrected to the verified rule.
- **Docs owed at close:**
  - the dossier `docs/references/rest-period-24h-research.md`, with a CLAUDE.md row;
  - ADR-044 for the rest model, plus pointers from ADR-039 and ADR-043;
  - `danish-agreements.md`;
  - the phase-B hand-off questions.
- **Under way:** the wave-3b briefs and plan "final + 10" are being drafted (`planner`), and go to brief adjudication like every wave.

**Wave-3 fix-ups so far:**
- TASK-14813 fix-up 1 (`6d2b43b0`): the manager-visibility rule at both surfaces, and the read-edge voluntary rule.
- Fix-up 2 (`3f1e5242`): half-up rounding.
- Fix-up 2b (`b5e87247`): the Skema boundary read moved after the month gate. This was the security consult's cheap fix, so no wasted reads for a refused manager.
- **The security consult** (`S148-TASK-14813-security-consult.md`) is CLEAN-WITH-RESIDUALS.
  - No remaining leader-tier path, and the marker leaks nothing about the unsent month.
  - Two SEC-059 notes at merge: the employed-day bit, and the weekly and voluntary direction on a withheld read.
  - A one-line note for future screens, because no leader surface reads `boundaryWorkTime` (`SkemaPage` is routed only to the signed-in user, `App.tsx:91`, `SkemaPage.tsx:289`).
- **TASK-14813-T fix-up 1** (`2fcb5486`): the facts from both adjudications and the consult's negative tests 3–12.
  - M-27 … M-30 each observed red at its named fact.
  - The table is at sixteen rows.
  - Gate: Unit 1746, DemoSeed 170, Regression non-Docker 140.
- **TASK-14814 fix-up 1** (`ux`) is in flight. Step 5a cycle 2 follows on the whole stack.

**The tender review.** The owner finished reviewing the 215 tender requirements on the review page. The decisions are recorded in `docs/references/tender-requirements-analysis.md` § "Owner review (2026-11-13)" (`837ecb29`):
- 128 should be met;
- 85 are not relevant;
- 2 are open.

The comments set a direction:
- SLP replaces SLS / HR-Løn and FRAV;
- Statens SSO;
- identity and roles come from outside StatsTid;
- shift planning is in scope;
- general reporting is wanted.

A planner is drafting the roadmap translation as a proposal for the owner (`.claude/plans/tender-review-roadmap-draft.md`). Nothing enters `ROADMAP.md` before the owner agrees.

**Wave 3 merged** (`40d3e694`, `--no-ff` of the stack tip `31a956b4`; paperwork in the merge: SEC-059 and QUAL-253 registered, two `shortcut(SEC-059)` tags, three Docker facts relabelled R, comments true-now, plan re-pointed). Step 5a cycle 2 on the fix-ups: Codex APPROVED-WITH-WARNINGS (`S148-WAVE3-5a-codex-c2.raw.md`), Fable APPROVED-WITH-WARNINGS (`S148-WAVE3-5a-reviewer-c2.md`); 5alpha PASS (`S148-TASK-14813-14814-5alpha.md`). Two small items go to wave 3b, which rewrites the same screen code: a rest typed with seconds can show 0.1 higher on screen than on the server (the screen rounds twice); the parity test keys the expected side through the display helper.
- **GATE on `40d3e694`: green.** Build 0 errors / 143 warnings / CA2100 113; Unit 1746; DemoSeed 170; Regression non-Docker 140; frontend tsc, lint, e2e typecheck, build, vitest 99 files / 1169 tests. **Next:** CI-W3.

**Tender review: owner answers to the roadmap questions (2026-11-13).** The planner's proposal (`.claude/plans/tender-review-roadmap-draft.md`) grouped the 128 "should meet" requirements into twelve epics; the owner answered its five questions, each with the recommended option:
1. **Shift planning: built into StatsTid, in stages, on ADR-043 stage 2's roster record** (one record for pay and planning; automatic roster proposals last). Decided before stage 2's refinement, as it must be.
2. **"Extract" means on-screen reports and statistics plus documented dataset endpoints** that tools such as SAS pull with a service login.
3. **First unblocked lane: the HR back office** (HR acting on behalf on screen, employee requests with manager approval, notifications), alongside or after the overtime programme, which stays the priority.
4. **The HR-system and user-administration feeds are the source for what they carry:** fed facts are read-only in StatsTid and corrected at the source; StatsTid edits only what no feed carries.
5. **Both deliberate design choices stay:** a manager sees a month only after it is sent (S124), and a month sent to payroll is never reopened (ADR-034); the tender rows are met through sent-month overviews, HR's view and the corrections path.

**Next:** the proposal goes to the `adjudicator` (Fable 5.2) with these answers; the Orchestrator then applies the ruled `ROADMAP.md` edits.

**Tender review: roadmap applied (2026-11-13).** The adjudicator (Fable 5.2, `.claude/reviews/TENDER-roadmap-adjudication.md`, RULED-WITH-QUESTIONS) confirmed the proposal carries the owner's review and five answers, with four corrections (a not-relevant capability struck from E1; VD 29 read as statistics; advance vacation left open as a third deliberate choice; VD 23 not built), and found that 53 of the 128 ids wait on the owner's specifications and master-data list (the draft said 76). **Owner ruling (2026-11-13): SEC-060** (one administrator alone can publish configuration that changes pay; no second signer) is registered now and **built as the first item of the compliance epic (E11), after the HR back office (E3)**; not S148 scope. Applied: `ROADMAP.md` (§1 item 7, the tender scope with the owner rulings; twelve epic rows across §2; a new theme "Integrations with the state's shared systems"; the SEC-060 row; the stale "(S128)" and the promoted cell-clearing row marked), SEC-060 in the security register, the owner answers in `tender-requirements-analysis.md`, and the draft committed with its status line. **Told to the owner, no decision:** the §21 transfer gets a manager approval at the vacation epic; advance vacation returns at that refinement; fed facts make three HR screens read-only views once a feed exists; keeping S124 and ADR-034 may still be scored as gaps; `SYSTEM_TARGET.md` needs owner approval per epic.

**Wave-3b briefs adjudicated** (`.claude/reviews/S148-wave3b-briefs-adjudication.md`, Fable 5.2, RULED, no owner question; planner report `S148-wave3b-briefs-planner-report.md`). Plan "final + 11" and TASK-14843, 14843-T, 14844 at draft 2: 129 AC ids (12 new: AC-S0-3g … -3o, AC-S0-4d … -4f); M-24 … M-33 at 14843-T's gate; the shared table at 33 rows. **What changes for a user (told, no decision):** the rest is judged on the 24 hours before each work end, dated the day the work ends, in real Danish hours; for HK and PROSA a rest of 8 hours or more is a warning, below 8 a violation; **the shown rest is rounded down to one decimal** (8 h 27 min shows 8,4; a breach never shows the minimum itself), which supersedes the half-up direction wave 3 merged; the engine is handed the Copenhagen time zone by its host and never reads one itself (ADR-002); a browser without Danish time-zone data shows a notice rather than looking compliant; the Danish copy is final. **Orchestrator at application:** § 5 item 12 applied at its correct line (the truncated-chain limit retired, the exemption arm to ADR-044 and QUAL-255); `:744` settled "not registered"; TASK-14844's Files cell widened to the panel's warning block and three test arms (the brief's contract already authorized them); the planner's three TASK-14844 implementation choices confirmed (the named zone error caught alone; the wrapping mock; "1 times" when the shown value is 1).

**CI-W3** (run 43412751883, draft PR #60, at `de3da64b`, headSha confirmed): **green, all seven jobs.**
- **What it shows:** wave 3's Docker facts ran for the first time, and all of them passed. Until now they had only been argued. Read from the run's trx files, by name, not from the job colour.
- **Tests:**
  - Unit 1746, DemoSeed 170, and Regression 2574 of 2574, Docker included (CI-W2 had 2555).
  - Nothing failed and nothing was skipped.
- **Facts seen passing, by name:**
  - the three `RestFromSkemaIntervalsTests` facts: `Interval2200To2359_Then0600To1400_DailyRestFinding` (AC-S0-3a), `ProjectHoursAboveIntervalHours_Listed_EqualHours_Analysed` (AC-S0-3c), `ShortRestAcrossMonthEdge_ReportedInTheMonthItEnds` (AC-S0-3f);
  - the re-pinned `RestCheckNotCheckedDaysTests` (`SkemaMonth_HoursOnlyDays_AreServed_AndTheEightHourGapIsStillFound`, `MonthWithNoEmployedDay_ReturnsAnEmptyNotCheckedList`);
  - every `RestLeaderVisibilityTests` method, 12 methods and 16 cases with the theories' rows (SEC-059's manager-visibility facts), including `LeaderRead_PreviousMonthUnsent_WithholdsTheBoundaryRest` and `LeaderRead_January_PreviousDecemberUnsent_Withholds_ThenReportsOnceSent`;
  - `SkemaRowPreferencesMonthGetTests` (the boundary work-time facts);
  - both PIN-1 worlds (`World20260115_December2025_EqualsGolden`, `World20260715_June2026_EqualsGolden`), so pay is unchanged by wave 3;
  - `ConfigSeedParityTests` and `DemoPinSeedSelectionTests`;
  - in Unit, the 16 `RestParityTableTests` rows and the table-shape fact.
- **The other jobs:** smoke, E2E, the frontend build, docs, lizard and gitleaks are all green.
- **Run time:** about 125 minutes in all (10:54 to 12:59 UTC on 2026-11-13; the regression step is most of it). Read by hand on 2026-11-16: the watcher was reaped for low memory and not restarted.
- **Status change:** wave 3's Docker facts move from "CI-pending" to "CI-verified" (run 43412751883). PR #60 is closed unmerged and `ci/s148-w3` deleted.
- **Wave 3b dispatch is unblocked** (TASK-14843's First action: local master after CI-W3 is green).

**Wave 3b built and reviewed (cycle 1), 2026-11-16.** The corrected rest check (owner Q2): the server and Skema judge every work end on the 24 elapsed Danish hours before it; the finding is the longest unbroken rest there, dated the day the work ends, shown rounded down; HK/PROSA derogation softens only rests of 8 hours or more.
- **TASK-14843** (`rule-engine`, `761a6acb`, report `.claude/reviews/S148-TASK-14843-report.md`): the engine rule, the zone handed in by the engine host (`Evaluate(..., TimeZoneInfo? copenhagenZone)`, null refused when an interval is kept), the read window at `monthStart − 2` with the approval lookup and the employed-day check kept on `− 1`, Skema's two-day previous boundary. Build 0 / 143 / CA2100 113; Unit 1729 + exactly the 17 predicted reds; DemoSeed 170; Regression non-Docker 140. Worked examples: Tuesday 22:00–23:59 then Wednesday 06:00–14:00 was "6.0, Tuesday", now "8.0, Wednesday"; a 24-hour shift was nothing, now "0.0" on the day it ends.
- **TASK-14843-T** (`test-qa`, `290b6203`, report `…-T-report.md`): the shared table in schema 2 (33 rows; at the base exactly the 26 F rows red), the 17 re-pins labelled `// S148 3b (RR-1):`, new facts AC-S0-3h, -3i (two), -3j, -3o, arm (iii) of AC-S0-3b, and three Docker facts (AC-S0-3k, -3l, -3m, CI-pending). Unit 1768/1768; M-24 … M-33 each turned named facts red on assertions.
- **TASK-14844** (`ux`, `916d9e85`, report `S148-TASK-14844-report.md`): Skema's instant warning on the same rule in Europe/Copenhagen (never the browser's zone), the (b′) day marking, the final copy, rounding down once, the zone notice. Frontend gate green: vitest 99 files / 1199 tests.
- **Security consult** (`S148-TASK-14843-security-consult.md`): **CLEAN-WITH-RESIDUALS**. A leader of an unsent previous month gets neither of its last two days at either surface; the approval lookup is exact; the marker leaks nothing new. Owed: SEC-059's wording from one day to two (at merge).
- **5α** (`S148-TASK-14843-14844-5alpha.md`): **PASS**.
- **Step 5a cycle 1, dual lens:** Fable `S148-WAVE3B-5a-reviewer.md` and Codex `S148-WAVE3B-5a-codex.raw.md`, both **APPROVED-WITH-WARNINGS**, both recomputing all 33 rows correct. **Adjudication** (`S148-WAVE3B-5a-adjudication-c1.md`, Fable 5.2, RULED, no owner question):
  - **The month cut moves into the engine** (ruling 1, option 2; amends TASK-14843 brief item 4.6). A night shift over the month's last midnight was judged "as the month sees it" only because the Backend stopped reading at the month's end, and the one test row trimmed its own input. *Given up:* one more review cycle. *Gained:* the pure rule owns its ruled behaviour, and the shared row tests the engine and the screen alike.
  - **The screen's parity test had a hole** (Codex): a finding without a date, or dated before the month, was never checked. So `fall-back-night-is-an-hour-longer` was green on the old screen by accident: it is **F, not R** (correcting the TASK-14844 report § 4).
  - **M-26 rerun** with the engine's own typed-date filter (the lenses disagreed; Codex was right that the run was wider than specified).
  - **New:** M-34 and M-35 (behavioural red for the rounding and the zone notice), M-36 and one Docker fact for the marker's employed-day check staying on `− 1`; comment corrections.
  - **Declined, noted here:** an unreachable tie where engine and screen name a different end time (needs overlapping intervals, which Skema refuses); the zone-name strictness (unproven; fails toward a notice); the shared `Alert`'s `role="alert"` re-announcing (predates the wave); the 48-hour average still rounds half up (`RestPeriodRule.cs:594`, brief adjudication item 9).
  - **Fix-ups dispatched:** A (`rule-engine`, the engine cut) and C (`ux`, the parity test, M-34, M-35) in parallel on `916d9e85`; B (`test-qa`) follows on A. Then cycle 2, both lenses, scoped to the fix-up diff.

**Wave-3b fix-ups and cycle 2, 2026-11-16.**
- **Fix-up A** (`rule-engine`, `62df2585`, report `.claude/reviews/S148-WAVE3B-fixA-report.md`): the daily check ignores intervals dated after `periodEnd`, so a night shift over the month's last midnight is judged as ending at 24:00 on the last day whatever the caller sends. Nothing a user sees changes: the Backend already reads only to the month's last day. Unit 1768 with tests untouched; DemoSeed 170; Regression non-Docker 140; build 0 / 143 / CA2100 113. Declared: `CheckDailyRest` lost its unused `periodEnd` parameter; the contract holds "for every call that returns" (a null zone with only after-period intervals throws, as the guard requires).
- **Fix-up B** (`test-qa`, `105ecd53`, `…-fixB-report.md`): the C# parity fact hands the engine every interval of each row. Base red at `916d9e85`: `night-shift-over-the-upper-month-edge`, expected 31 March 8.0, actual none. **M-26 rerun** with the typed-date filter: both named rows red (plus two interval facts). **M-27** (the trim removed): three red, exactly as fix-up A predicted. **M-28**: three red. New Docker fact `Marker_HiredOnThePreviousMonthsLastDay_ListsTheFirst` (M-36 argued). Unit 1768, DemoSeed 170; its non-Docker Regression run did not finish: **the C: disk filled to 0 bytes** (see below).
- **Fix-up C** (`ux`, `f989ff8c`, `…-fixC-report.md`): the parity vitest checks every finding's shape, equal duplicates, and three partitions that must sum to the total. Against the old screen the fall-back row now fails on the undated 10.5 finding. **M-34** (rounding half up): 9 red across the frontend suite. **M-35** (the zone notice switched off): its arm red on `getByText`. Frontend gate green: vitest 99 files, 1199 tests.
- **Cycle 2, dual lens:** Fable `S148-WAVE3B-5a-reviewer-c2.md` **APPROVED** (six NOTEs) and Codex `S148-WAVE3B-5a-codex-c2.raw.md` **APPROVED** (two NOTEs). Applied at merge: the parity fact's comment (the compliance read sends `[periodStart − 2, periodEnd]`, Fable NOTE 1); the kind-R count in `RestLeaderVisibilityTests` (NOTE 6); brief 4.6 worded "for every call that returns" (NOTE 5). **Recorded, no action:** the Regression build of the new Docker fact is owed to the merge gate (NOTE 2, Codex NOTE 2); with after-period work sent, the daily and weekly checks could disagree on a cut stint's voluntary status, unreachable today (the Backend stops at the month's end and nothing writes the flag), to be settled when the voluntary toggle is built (NOTE 3); "the weekly check still reads it" has no pin, for wave 5 to know (NOTE 4); M-35 was run against its own test file only (NOTE 6).
- **The full disk.** During fix-up B the C: volume reached 0 bytes free: 36 agent worktrees had piled up, each with its own build output and some with a full `node_modules`. The Orchestrator removed the 29 whose commits were already on master and whose trees were clean, after removing the one `node_modules` junction into the main checkout by itself (the main `node_modules` checked intact). 5.4 GB free afterwards. Fix-up B's report, truncated by the full disk when first saved, was re-saved from the task notification.

**Wave 3b merged** (`--no-ff`, an octopus merge of fix-up B's tip `105ecd53` and fix-up C's `f989ff8c`; 14843 `761a6acb`, 14843-T `290b6203`, 14844 `916d9e85`, fix-up A `62df2585`). Paperwork in the merge:
- `ComplianceCheckResult.cs` remarks ("measures rest between"; the manager's check does not read the two days before the month, and a work end on the 1st is then judged from the submitted month alone), as the Orchestrator's comment fix (Small Tasks Exception), with the two test-comment fixes above.
- **SEC-059** amended: the residuals say two days; the dated note of the 3b consult, with `Success` able to differ between the manager's and the employee's read.
- **QUAL-254, -255, -256** registered (the research adjudication's § 4 texts; QUAL-255 cross-referenced to ADR-044, owed at close). QUAL-253 is marked FIXED after CI-W3b.
- **`SYSTEM_TARGET.md`** (owner Q2 approved the wording): the daily rest stated as the 24-hour look-back in elapsed Danish time; the derogation's 8-hour floor and what is not checked (QUAL-254); the Skema analysis and its example in the ruled copy; the 13-hour implication.
- **Plan "final + 12"** and TASK-14843 brief item 4.6 (the engine owns the month cut). M-34, M-35, M-36 added; 129 AC ids unchanged.

**GATE after wave 3b** (`2810ebeb`): **green.** Build `-m:2 --no-incremental` 0 errors / 143 warnings / CA2100 113 distinct (both unchanged); Unit 1768 (1746 + 22: 17 new table rows and 5 new facts); DemoSeed 170; Regression non-Docker 140 (the Regression project builds with the new marker fact, settling the cycle-2 NOTE); frontend tsc, lint, `typecheck:e2e`, build green; vitest 99 files, 1199 tests (1169 + 30); `tools/check_docs.py` all hard checks pass. **CI-W3b next**, on a throwaway branch and draft PR.

**CI-W3b** (run 43851473962, draft PR #61, at `ea684941`, headSha confirmed): **green, all seven jobs.**
- **What it shows:** wave 3b's Docker facts ran for the first time, and all of them passed. Read from the run's trx files, by name, not from the job colour.
- **Tests:**
  - Unit 1768, DemoSeed 170, and Regression 2578 of 2578, Docker included (CI-W3 had 2574).
  - Nothing failed and nothing was skipped.
  - The Unit run on Linux time-zone data passed all 33 table rows, so the daylight-saving answers match the Windows host (an `(unproven)` item of TASK-14843 and -T, now settled).
- **Facts seen passing, by name:**
  - `RestFromSkemaIntervalsTests`: AC-S0-3a (`Interval2200To2359_Then0600To1400_DailyRestFinding`, now 8.0 dated 11 March), AC-S0-3c, AC-S0-3f (dated 1 October), and **AC-S0-3k** (`WorkEndOnTheFirst_AfterA23HourDay_IsJudgedFromTwoDaysBack`: the harness accepted the 2024 dates);
  - `RestLeaderVisibilityTests`, all 15 methods (19 cases), including the four re-pinned facts, **AC-S0-3l** (`LeaderRead_PreviousMonthUnsent_WithholdsBothDaysBefore`), **AC-S0-3m** (`Skema_TwoPreviousBoundaryDays_ServedToTheEmployee_ToALeaderOnlyWhenSent`) and the new marker fact `Marker_HiredOnThePreviousMonthsLastDay_ListsTheFirst` (M-36's guard);
  - `SkemaRowPreferencesMonthGetTests.BoundaryWorkTime_ExactlyThreeDays_NoiseExcluded` and the re-pinned `RestCheckNotCheckedDaysTests`;
  - both PIN-1 worlds (`World20260115_December2025_EqualsGolden`, `World20260715_June2026_EqualsGolden`), so pay is unchanged by wave 3b (AC-S0-3n); `ConfigSeedParityTests` and `DemoPinSeedSelectionTests`.
- **The other jobs:** smoke (the composed compliance probe non-503, so the engine container resolves the Copenhagen zone at boot, the other `(unproven)` item, settled), E2E, the frontend build, docs, lizard and gitleaks are all green.
- **Run time:** about two hours (10:44 to 12:45 UTC on 2026-11-16).
- **Status change:** wave 3b's Docker facts move from "CI-pending" to "CI-verified". **QUAL-253 marked FIXED** in the register. PR #61 is closed unmerged and `ci/s148-w3b` deleted. The six wave-3b worktrees were removed after the merge (their two `node_modules` junctions unlinked first; the main `node_modules` intact).
- **Wave 3b is complete.** Next: wave 4 per the plan.

**Wave 4 briefs, 2026-11-16.** Wave 4 is the config stage's foundation: the schema (dated configs, the local profile's new settings, customary days, the HR facts, the dated organisation), two retirements, dated config reads, the dated organisation and the "pay inputs" lock. Twelve dispatch briefs, drafted in three batches by the Planning Drafter at master `e9d09a66`:
- **Batch A** (TASK-14815 schema, -T, 14816 the C-6/C-7 retirements, -T, 14817 the editors; report `.claude/reviews/S148-wave4A-briefs-planner-report.md`); **batch B** (TASK-14818 dated config reads, -T, 14819 the regression seed; `…-wave4B-…`); **batch C** (TASK-14820 the dated organisation, -T, 14821 the pay-inputs lock, -T; `…-wave4C-…`).
- **Batches A and B adjudicated** (`S148-wave4AB-briefs-adjudication.md`, Fable 5.2, RULED, no owner question) and applied (`S148-wave4AB-briefs-applied-report.md`): the eight briefs at draft 2, the plan at **"final + 13"** (130 AC ids: AC-C2q added; M-37 … M-40 added). **Told to the owner, no decision:** wave 4 accepts dated publishes, but payroll neither splits nor prices on them until wave 5 (TASK-14825 switches the planner's two sources on); a brand-new agreement's first version may start on any weekday; the Monday-only field list comes from a read-only trace (ruling B3); a version that starts the day someone changes agreement or position keeps that change's name in the calculation record. *Given up:* AC-C2a's split fact is proven by a CI mutation (M-39) rather than an F red in wave 4; no option of the drafted three for the same-day tie was needed (the planner's run shape removes the premise).
- **Found by the adjudicator, not the planners:** 26 test files delete users in cleanup and the new organisation table's foreign key would refuse them (TASK-14819 census C5 deletes the organisation rows first; `ON DELETE CASCADE` declined, since it would let history be erased to suit tests); 54 more files insert users by SQL (census C3); no fact saw the "not before the predecessor" refusal (AC-C2q added).
- **Orchestrator, accepting the apply report's ten interpretation calls** (its "Needs a ruling" list): all held as the planner did them.
- **Pending:** the B3 trace (running); batch C's adjudication (after the trace). **TASK-14815 dispatched** (the schema; its brief needs nothing from batch C).

**Wave-4 batch C and the B3 trace adjudicated** (`S148-wave4C-briefs-adjudication.md`, Fable 5.2, RULED, no owner question; the B3 trace `S148-wave4-B3-week-read-trace.md`; applied `S148-wave4C-briefs-applied-report.md`). Plan **"final + 14"** (131 AC ids: AC-C13l and AC-PIL-g (3) added; M-41 redefined, M-42 new; fifteen Docker mutations in five runs). **Told to the owner, no decision:**
- **The Monday-only list** for a dated agreement config or override (TASK-14818 item 4.5): central configs `WeeklyNormHours`, `HasOvertime`, `HasMerarbejde`, `NormModel`; overrides `WeeklyNormHours` only. The three regime fields protect the arithmetic: the engine judges a whole ISO week by its latest evaluated day, so a Wednesday change would silently re-judge Monday and Tuesday. **The weekly norm stays Monday-only on an unretired premise:** the engine now sums it per day, but ADR-017 D9a and the profile's own Monday rule still stand, and lifting it is ADR-043 provisional item 18 (an expert question), which this sprint does not answer by the back door. *Given up:* a norm change cannot be dated mid-week although the engine could price it day by day.
- **An HR user can no longer close their own employee profile** (TASK-14821, P-26's fourth site, under the A-18 rule because the task already edits that handler). The owner may strike it.
- **A read-only trace is owed before TASK-14831's brief** (O-23): whether a today-dated wage-type-mapping edit committed between calculate and record changes a recorded line.
- The planner found that wiring the dated organisation into the history reader through a new constructor dependency would have broken the payroll host at runtime (the S134 failure); the reader reads it inside its own query instead.

**TASK-14815 (the config stage's schema) and TASK-14815-T, 2026-11-16.**
- **TASK-14815** (`data-model`, `00f84e26`, report `.claude/reviews/S148-TASK-14815-report.md`): `applies_from` (no DEFAULT) on agreement configs and position overrides; `position_override_config_versions` with four seed rows; 18 local-profile settings; `customary_days`, `employee_hr_facts`, `user_organisations` with audit tables; five new events with TenantTargeted audit mappers registered in Backend `Program.cs`. Storage only: nothing writes the new tables or emits the events yet; no screen, route or pay figure moves.
- **TASK-14815-T** (`test-qa`, `a56e5e9a`): five round-trip facts and five mapper facts (all N; M-37 and M-38 each turned exactly five facts red); the schema copies and two SQL fixtures carry `applies_from`. Build 0 / 143 / CA2100 113; Unit 1778; DemoSeed 170; Regression non-Docker 140.
- **Step 5a cycle 1, dual lens:** Fable `S148-TASK-14815-5a-reviewer.md` and Codex `S148-TASK-14815-5a-codex.raw.md`, both APPROVED-WITH-WARNINGS on one shared WARNING: two mapper facts gave the request context and the event the same institution, so a mapper reading the wrong source still passed. **Adjudicated** (`S148-TASK-14815-5a-adjudication-c1.md`, RULED): fix-up A (`data-model`, `ca7e4fa1`: the HR-facts audit table's `employee_id` index; five event remarks now say nothing emits them yet and name the emitter) and fix-up B (`test-qa`, `af9be289`: context organisation `STY09`; **M-38b** points each mapper at the wrong source and turned exactly five facts red). **Cycle 2:** both lenses APPROVED (`…-reviewer-c2.md`, `…-codex-c2.raw.md`). **5α PASS** (`S148-TASK-14815-5alpha.md`).
- **Recorded, no action:** the unused `@appliesFrom` bound on an UPDATE (`AgreementConfigRepository.cs:334`) is harmless: Npgsql 8.0.3 sends only the placeholders a query names, and the same UPDATE already binds unused `agreementCode` and `okVersion` (the 14815 report misnamed it `status`). For the model-routing register at close: TASK-14815's own report read suites through `| tail`, did not dedupe CA2100 and skipped a gate grep; the paired pass re-ran everything properly. Codex cycle 2 noted fix-up B's mutation evidence was a table rather than raw output; both lenses confirmed it by reading.
- **Carried by the Orchestrator as "final + 15"** (the adjudication's § 1, § 3): M-38b in the plan; TASK-14820 states and TASK-14820-T pins that a transfer's audit row targets the NEW organisation; TASK-14836's brief carries the HR-facts delete census; each emitter's merge removes the whole "Nothing emits it yet" sentence (O-14, O-20, new O-25); at TASK-14818's review both lenses confirm no write path still saves an unset `AppliesFrom` (O-25).

**TASK-14815 merged** (`--no-ff` of fix-up B's tip `af9be289`: TASK-14815 `00f84e26`, -T `a56e5e9a`, fix-up A `ca7e4fa1`, fix-up B `af9be289`). Paperwork in the merge (O-10): five catalog rows in `docs/operations/audit-projection-catalog.md`, each marked define-only until its emitter (14820 twice, 14821, 14834, 14836); `docs/generated/db-schema.md` regenerated with `tools/generate_db_schema.py` (67 → 74 tables: the override version table, customary days, HR facts, user organisations and their audit tables); `tools/check_docs.py` hard checks pass. GATE next; the Docker facts are CI-pending at CI-W4a (no existing fact expected red).

**GATE after TASK-14815** (`26cab7d8`): **green.** Build `-m:2 --no-incremental` 0 errors / 143 warnings / CA2100 113 distinct (unchanged); Unit 1778 (1768 + 10); DemoSeed 170; Regression non-Docker 140; `tools/check_docs.py` hard checks pass. The frontend is unchanged since `ea684941` (`git diff --stat ea684941 HEAD -- frontend` empty), so its green gate there stands. Next: TASK-14816 (the C-6 / C-7 retirements).

**TASK-14816 stack (C-6 and C-7 retired), 2026-11-16.**
- **TASK-14816** (`backend-infrastructure`, `7b694f69`, report `S148-TASK-14816-report.md`): the unused local-override checker `ValidateLocalOverride` and its two private helpers deleted with their tests (C-6); the flex carryover maximum, which no calculation ever read, leaves SharedKernel config and models, the three role-override events, the Backend and Infrastructure, `init.sql` (four columns; the ten agreement seeds lose that value, `max_flex_balance` kept: AC 150, HK 100, PROSA 120) and the legacy migrator's known keys (a legacy row now takes the unknown-key drop with its old value recorded, ruling A8). 27 test files lost only the lines naming the retired members (A1). Build warnings 143 → **125** (the deleted checker's obsolete-call warnings). Config fingerprints change value (one property fewer); a replay of an older calculation would be refused by name (`replay-config-changed`), and nothing stored survives a reseed.
- **TASK-14816-T** (`test-qa`, `5e1f6f9e`): the census re-pins (schema copies, SQL fixtures, the migration fact, the S118/S119 key sets, posted bodies) and one R theory: an old stored role-override payload still carrying the key deserialises with every other member intact (DEP-003). Unit 1757.
- **REGEN** (Orchestrator, `0c796bf8`): `openapi.json` drops the field from exactly the ten predicted schemas (52 deleted lines, nothing added).
- **TASK-14817** (`ux`, `aac81276`): `gen:api`; the field leaves the agreement editor, the position-override page, the local profile editor and the configuration overview, and seven tests. Frontend gate green (vitest 99 files, 1199 tests).
- **Reviews:** 5α PASS (`S148-TASK-14816-14817-5alpha.md`). Step 5a cycle 1: Fable `S148-TASK-14816-5a-reviewer.md` and Codex `S148-TASK-14816-5a-codex.raw.md`, both APPROVED-WITH-WARNINGS, no code defect. **Adjudicated** (`S148-TASK-14816-5a-adjudication-c1.md`, RULED, no owner question): **no agent fix-up and no cycle 2** (both lenses agreed; what remained was ten comment lines and two test names, proven by the compiler, the greps and unchanged suite counts). The Orchestrator's commit `c2a31a6a` made the stale counts true-now and renamed `S118AgreementConfigSpecRuntimeTests.Create_Post201Exact_RulingOnePin_Always48MemberFullEntity` → `…_AlwaysFullEntity` and `Clone_Post201Exact_RulingOnePin_Always48MemberFullEntity` → `…_AlwaysFullEntity` (a count in a name goes stale again when TASK-14818 adds a member). Declined: asserting the recorded old value in the migration re-pin (a path with no caller and no data; QUAL-039).
- **New SEC-061 (Medium), found by the internal lens:** the live "save local profile" route checks no value, so a LocalAdmin could store a negative or zero weekly norm. It predates this stack; the deleted checker was only the last place those limits were written down. **Fixed in TASK-14832** (wave 6, which already rebuilds that route's validation): `weeklyNormHours` > 0, `maxFlexBalance` ≥ 0, `maxOvertimeHoursPerPeriod` ≥ 0, no upper ceiling (owner ruling OQ-2 allows no invented ceilings). S148 does not close with the engine switch merged and SEC-061 open. **Told to the owner, no decision:** the profile save's value check arrives in wave 6; "a local flex maximum may not exceed the central one" has been unenforced since S21 and is not rebuilt, since that field's meaning retires at stage 1.
- **Carried:** TASK-14832 compares all twenty-two settable columns in the profile save's "no change" check, so a save changing only a new setting is never silently dropped (AC-C4e); TASK-14838 ties the agreement editor's labels to their inputs (accessibility, pre-existing); the typedWire re-pin is kind R (fixture-pinned). Plan amendments "final + 16" follow.

**TASK-14816 stack merged** (`--no-ff` of `c2a31a6a`). Paperwork in the merge: `docs/generated/db-schema.md` regenerated (O-11; the four columns gone); `SYSTEM_TARGET.md` marks the field retired (the section heading's "31 fields" was not recounted); SEC-061 registered and the SEC-033 backstop ledger row names it; QUAL-039 gains the migrator's all-NULL-assertion note; no catalog change (A10). GATE next.

**GATE after the TASK-14816 stack** (`7b5317d4`): **green.** Build `-m:2 --no-incremental` 0 errors / **125 warnings** (143 → 125: the deleted checker took its obsolete-call warnings) / CA2100 113 distinct; Unit 1757; DemoSeed 170; Regression non-Docker 140; frontend tsc, lint, `typecheck:e2e`, build green; vitest 99 files, 1199 tests; `tools/check_docs.py` hard checks pass. Next: TASK-14818 (dated config reads).

**TASK-14818 (dated agreement configs and position overrides) and TASK-14818-T, built and reviewed (cycle 1), 2026-11-16.**
- **TASK-14818** (`backend-infrastructure`, `d2f08749`, report `S148-TASK-14818-report.md`): every config publish and override write carries an optional `appliesFrom` (omitted = Copenhagen today; future refused; before the predecessor refused; a change to the closed Monday-only list mid-week refused); every version kept, one in-force read per family; each write locks every row of its key in id order (B4); archiving the ACTIVE config refused (409 "publish a successor"); the obsolete `AgreementConfigSeeder` deleted; payroll's planner gains two optional repositories and hydrates its boundary sources (dormant until TASK-14825 registers them, so PIN-1 holds). Gate as ruled (B8): only the listed Regression sites fail to compile; Unit 1757, DemoSeed 170.
- **TASK-14818-T** (`test-qa`, `0630b31b`, `585c6983`): the new facts (AC-C2b (1) in four arms, AC-C2d, -2e, -2l, -2q, the archive facts, the in-force and tie-break facts, the split and shared-date facts), a `PositionOverrideTestWriter` helper re-pointing every broken call site, census C5's four real-clock weekly-norm facts given explicit Mondays, the S118 ACTIVE-archive re-pin to 409; mutations M-9, M-10, M-14, M-15, M-16, M-39, M-40 (no local red, Docker reds argued). Unit 1757, DemoSeed 170, Regression non-Docker 140.
- **REGEN** (Orchestrator, `af4325ba`): `openapi.json` +75 lines, additions only, as predicted. Running `gen:api` then `tsc` on that tip failed at 8 sites, not the 3 fixtures the report predicted: four test fixtures and the production `AgreementConfigEditor.tsx` (its form type inherits the new required member; the single-config read lacks it). The regenerated `api-types.ts` was not committed.
- **Reviews:** 5α PASS (`S148-TASK-14818-5alpha.md`). Step 5a cycle 1: Codex `S148-TASK-14818-5a-codex.raw.md` **CHANGES-REQUIRED** and Fable `S148-TASK-14818-5a-reviewer.md` **CHANGES-REQUIRED**. **The lenses disagreed on concurrency:** Codex found that the key lock locks only existing rows, so on a position that never had an override three well-timed writes can store an override dated before committed versions (head ACTIVE, dated read INACTIVE); the internal lens judged the design sound without considering the absent key. Fable alone found that the REGEN made three lines of `tools/openapi-bodyless-declared.txt` stale (a CI failure by design). Codex's warning on the Monday errors' field name was a **false positive caused by the Orchestrator's review prompt**, which misstated the contract (the brief names the changed field, e.g. `WeeklyNormHours`; `AppliesFrom` only for the future and before-predecessor refusals).
- **Adjudicated** (`S148-TASK-14818-5a-adjudication-c1.md`, Fable 5.2, RULED, no owner question): Codex's empty-key case confirmed by reading; **fix:** a transaction-scoped advisory lock per override key (`position-override-` + agreement|OK|position), the first statement of every override writer, ahead of the row lock; lock order pay-inputs (TASK-14823) → this key lock → the key's rows → the version row. The single-config read gains `appliesFrom` (a gap in the brief, not a requirement change). A second create on an occupied key answers 409, not 500. Test gaps closed (AC-C2r replays the three-writer case deterministically; AC-C2s the 409; the create and activate date rules and the across-ids predecessor rule; three audit rows' dates; a stale-If-Match archive arm). Orchestrator at merge: the second REGEN, the body-less list, `gen:api`, the editor's form type and five fixtures (Small Tasks Exception, with a stop rule), `ARCHITECTURE.md:115`, plan "final + 17". CA2100: two pragma pairs, one covering two helpers; distinct count unchanged at 113.
- **Told to the owner, no decision (O-12 (7)):** until TASK-14838 gives the editors a date field (wave 6), four actions are refused on any day but a Monday: publishing a change to the weekly norm, overtime, merarbejde or norm model, and creating, switching off or switching on a position override that sets a weekly norm. Every other change works every day. The refinement's "the editors keep working" (REF:438) holds for everything else. Defaulting an omitted date to the previous Monday was declined: it would silently backdate a pay input by up to six days with no one having typed a date. The owner may strike this and have the date field pulled forward.
- **For the model-routing register at close:** Codex caught a concurrency hole the internal lens passed; the internal lens caught a CI-list consequence Codex missed. **Evidence ordered:** CI-W4a proves the real-clock census only if its Copenhagen date is not a Monday (today is); record the run's weekday.
- **Fix-up 1 dispatched** (`backend-infrastructure`); fix-up 2 (`test-qa`) follows; then the Orchestrator's REGEN and frontend lines; then cycle 2, both lenses.

**TASK-14818 fix-ups and cycle 2, 2026-11-16.**
- **Fix-up 1** (`backend-infrastructure`, `668adeec`, report `S148-TASK-14818-fix1-report.md`): every position-override writer's `LockKeyAsync` first takes a transaction-scoped advisory lock on the key (`position-override-` + agreement|OK|position), as its own statement, ahead of the row lock; the single-config read (`AgreementConfigWithEntitlementsResponse`) gains `appliesFrom` (null until published); the create maps a second ACTIVE override to 409 with the activate route's text. Hang census: none. Build 0 / 125 / CA2100 113; Unit 1757, DemoSeed 170, Regression non-Docker 140.
- **Fix-up 2** (`test-qa`, `0fb40202`, `…-fix2-report.md`): AC-C2r replays the three-writer empty-key case at repository level. **Orchestrator ruling during the fix-up** (raised by the plan planner): its poll waits for the late writer to hold *any* ungranted lock, not specifically the advisory one, so that with the lock deleted (M-43) the late writer waits on the index instead, then commits a 5 January version, and the red lands at the outcome assertions rather than at a timed-out wait. AC-C2s (the 409, nothing written); the W1 arms (create and switch-on Monday rule; the predecessor across override ids; a first config version on a Wednesday is 200); W2 (three audit rows' dates); the stale-If-Match archive arm; the by-id key set. Unit 1757; all new facts Docker, argued.
- **Orchestrator:** second REGEN (`aa946da5`: `openapi.json` +6, `appliesFrom` on the single-config read only; `tools/openapi-bodyless-declared.txt` drops publish, deactivate and activate, the convention gate passes with 5 declared body-less); frontend (`d7ec55b7`, Small Tasks Exception: `gen:api`; `ConfigForm` omits `appliesFrom`, since the date is supplied at publish; four typed fixtures; tsc, lint, `typecheck:e2e`, build green, vitest 99 files / 1199 tests).
- **Cycle 2, dual lens:** Codex `S148-TASK-14818-5a-codex-c2.raw.md` **APPROVED** and Fable `S148-TASK-14818-5a-reviewer-c2.md` **APPROVED** (notes only). Recorded: AC-C2r prints the lock type the late writer waits on (expected `advisory` green, `transactionid` under M-43; any other value would show the any-lock poll accepting an unrelated wait); the create's and activate's 409 catch every unique violation (all other keys on the path are fresh GUIDs); arm 7 is a guard, not a fix-proving arm. Nothing ordered.

**TASK-14818 merged** (`--no-ff` of `d7ec55b7`: TASK-14818 `d2f08749`, -T `0630b31b` and `585c6983`, REGEN `af4325ba`, fix-up 1 `668adeec`, fix-up 2 `0fb40202`, REGEN `aa946da5`, frontend `d7ec55b7`). Paperwork in the merge: `docs/ARCHITECTURE.md:115` no longer names the retired seeder. `init.sql` unchanged, so `db-schema.md` needs no regeneration. GATE next, then **CI-W4a** (the first CI run of wave 4's Docker facts; ruling 1.9: it proves the real-clock census only on a non-Monday Copenhagen date).

**Correction after the TASK-14818 merge (Orchestrator):** the merge commit `f56dc64e` left TASK-14818's `shortcut(pending)` for the Monday rule unregistered (the standing rule is to register it at merge), and `tools/check_docs.py` failed its PAT-032 check; the failure was hidden because the check ran through a pipe in the merge command. Registered now as **QUAL-BF-007** (below the Medium+ floor; the Monday rule for week-read fields, owner-accepted under A-16; upgrade when stage 1 removes the weekly basis) and the comment points at it (`AgreementConfigRepository.cs:1024`). `check_docs.py` passes. The owed QUAL rows that were to take "QUAL-257 onward" are unaffected (QUAL-BF is the appendix series).

**GATE after TASK-14818** (`ad0f3013`): **green.** Build `-m:2 --no-incremental` 0 errors / 125 warnings / CA2100 113 distinct; Unit 1757; DemoSeed 170; Regression non-Docker 140; frontend `gen:api` shows no drift (a line-ending difference only, no content), tsc, lint, `typecheck:e2e`, build green; vitest 99 files, 1199 tests; `tools/check_docs.py` and `tools/check_openapi_convention.py` pass. **CI-W4a next.** Its Copenhagen date is Monday 16 November 2026, so by ruling 1.9 it cannot prove the real-clock census; a non-Monday Regression run is owed before CI-W4 is declared.

**CI-W4a, first attempt** (run 43943858508, draft PR #62, at `e866632a`, headSha confirmed): **red in two minutes, before any test ran.** `init.sql` failed at line 1008: `check constraint "customary_days_part_hours_check" already exists`. **Cause (TASK-14815):** in the new `customary_days` table the `part_hours` and `paid_mode` columns carry an inline CHECK, which PostgreSQL names automatically `customary_days_<column>_check`; the table then declares its two cross-column rules under exactly those names. Smoke and E2E failed for the same reason (their stacks apply the same script). Both Step 5a lenses had judged "init.sql executes as written" by reading; the clash shows only when PostgreSQL runs the script, and Docker does not run on this machine. Master has carried the defect since the TASK-14815 merge (`26cab7d8`); this was the first CI run since.
- **How it was found and proven locally (new):** this machine has PostgreSQL 16 installed. The Orchestrator started a throwaway private cluster in the scratchpad (`initdb` with trust auth, its own port, nothing outside the scratchpad touched; the machine's own PostgreSQL service and its credentials were not used), reproduced the exact CI error, scanned every CREATE TABLE for explicit constraint names that collide with PostgreSQL's automatic ones (only these two), and applied the fixed script end to end: exit 0, 74 tables, 10 agreement configs, 4 override version rows, every constraint present; a second application also succeeds. `f74c232a` (before TASK-14818) fails the same way, confirming the origin.
- **Fix (Orchestrator, Small Tasks Exception: two lines, one file):** the two inline column checks get their own names (`customary_days_part_hours_positive_check`, `customary_days_paid_mode_vocabulary_check`); the two cross-column rules keep the names the briefs and tests cite (`customary_days_part_hours_check`, `customary_days_paid_mode_check`). `db-schema.md` unaffected. Proven by execution, not by reading; CI-W4a re-runs on the same PR.
- **Lesson, adopted:** from now on the Orchestrator applies `init.sql` to a throwaway local PostgreSQL cluster as part of every GATE that changes it, so a script that cannot run is caught before CI.

**CI-W4a** (run 43946317628, draft PR #62, at `27860214`, headSha confirmed): **green, all seven jobs** (finished 2026-11-16 21:28 UTC).
- **What it shows:** wave 4's first half (TASK-14815, the TASK-14816 stack, TASK-14818) ran its Docker facts for the first time, and all passed. Read from the trx files by name, not from the job colour.
- **Tests:** Unit 1757, DemoSeed 170, Regression **2597 of 2597**, Docker included (CI-W3b had 2578). Nothing failed, nothing skipped.
- **Facts seen passing, by name:** the dated-config facts (`ConfigDateValidationTests` 4, `ConfigPublishAuditTests`, `ConfigArchiveTests` 2, `PublishedOnlyInForceTests`, `SeededConfigsInForceTests`, `PositionOverrideVersionReadTests` 3, `SameDayTieBreakTests` 3, `DatedConfigBoundaryTests` 2, `PositionOverrideOccupiedKeyTests`); **AC-C2r** `EmptyKey_ThreeWriters_LateCreateRefusedBeforePredecessor`, whose output reads "B waited on an ungranted 'advisory' lock" (the new key lock is what queued the late writer, as designed); the schema-copy and fixture classes after `applies_from` and the retirement (`TxContractTests` 28, `AgreementConfigConcurrencyTests` 5, `PositionOverrideConcurrencyTests` 5, `AuditVersionTransitionTests` 3, `S122CompensationVocabularyTests` 4, `LocalProfileActivationKeyTests` 6, `ProfileMigrationTests` 7); the S118/S119 contract key sets (32); SEC032/033/034 (24); in Unit, the five round-trip and five mapper facts and the DEP-003 theory; **both PIN-1 worlds** (`World20260115_December2025_EqualsGolden`, `World20260715_June2026_EqualsGolden`), so pay is unchanged by wave 4's first half.
- **The other jobs:** smoke, E2E, the frontend build, docs, lizard and gitleaks green; smoke and E2E bring the whole stack up from `init.sql`, the end-to-end proof of the fix.
- **Status change:** wave 4's first-half Docker facts move from "CI-pending" to "CI-verified". PR #62 closed unmerged, `ci/s148-w4a` deleted. **Still owed (ruling 1.9):** this run's Copenhagen date was Monday 16 November, so a non-Monday Regression run is owed before CI-W4 is declared; CI-W4 itself, on a later weekday, will serve.
- **TASK-14819 (the regression seed) and the batch-C tasks are unblocked.**

**TASK-14819 (T25, the regression seed): built, reviewed, merged, 2026-11-17.**
- **What it does, in plain words:** TASK-14820 will make the employee lookup read each day's organisation from the new dated table and refuse a day that has a profile but no organisation. Tests that create employees directly in the database would then fail for the wrong reason. So every test that creates an employee by SQL now also writes that employee's one open organisation row from `0001-01-01`, and every test clean-up that deletes users first deletes their organisation rows (the new table's foreign key would otherwise refuse the delete). No assertion or expected value moved; the tests mean what they meant.
- **Built** (`test-qa`, `15eef9e8`, report `S148-TASK-14819-report.md`): the one-row helper `RegressionSeed.SeedOrganisationRowAsync(conn, userId, orgId)` (a SQL literal `0001-01-01`, `ON CONFLICT DO NOTHING`), also called by `SeedEmployeeAsync`; 57 test files, +203 / -3 (the three removed lines are "three rows" comments now saying four). Censuses: C1 50, C2 106, C1 ∩ C2 18, C3 56, C4 3, C5 30.
- **Reviews, dual lens, cycle 1:** 5α PASS (`S148-TASK-14819-5alpha.md`); Codex `S148-TASK-14819-5a-codex.raw.md` **APPROVED** (two unproven notes; an independent census matching the report's totals); Fable `S148-TASK-14819-5a-reviewer.md` **APPROVED-WITH-WARNINGS**: one warning, a `YearOverviewTests` user given two agreement codes by SQL and no organisation row, which the report had described as "no profile or code".
- **Adjudicated** (`S148-TASK-14819-5a-adjudication-c1.md`, Fable 5.2, RULED, no owner question): **R1** add the row (the brief's rule covers anyone given a profile *or* an agreement code), made by the Orchestrator under the Small Tasks Exception (`040a260a`: the helper call inside `InsertBareUserAsync`, one file, tests only; 5α re-run PASS, `S148-TASK-14819-5alpha-fixup.md`); **R6** no cycle 2 by either lens, since the fix-up is exactly the line the internal lens prescribed (1 of 5 per lens used).
- **The record, corrected (R2):** C1's 50 files are 28 Regression files plus the Smoke file in class (a) and 21 in class (b) (20 test files plus `RegressionSeed.cs`); C3's 56 are 7 (a) and 49 (b) (47 Regression, 2 DemoSeed goldens); 35 test files call the helper; **CI-W4's "T25's re-seeded files" figure is 36** (the 35 callers plus the Smoke file). **The rule that makes a test user safe** once TASK-14820 lands: the fixture writes the row; or the user is active and exists before a Backend host boots (the start-up seeders then give the profile and the organisation row together); or the user never has a profile and is never moved through the user edit; or the file runs on a hand-made schema with no host and no lookup. The report's backfill reason for `DatedEntitlementConfigResolverTests` is withdrawn (it never builds the lookup).
- **Accepted deviation (R3):** the Smoke file's own insert uses `WHERE NOT EXISTS` rather than the brief's `ON CONFLICT DO NOTHING`, matching its two sibling inserts; same effect. Recorded, not changed: the helper's `ON CONFLICT DO NOTHING` would silently keep a row for a different organisation (no fixture does that).
- **Carried to TASK-14820 and TASK-14820-T (R4, R5):** three test files move a user's live organisation by SQL (`ReportingLineWriteLifecycleTests` R9, `S95FlatOrgLockTests`, `AdminVikarOnBehalfTests`), leaving the live column and the open organisation row disagreeing; the transfer must answer 200 in place on an equal date even when the new organisation equals the open row's (no extra throw). And fixture rows store the finite date `0001-01-01` while rows the product writes through a `DateOnly` parameter store `-infinity`: compare start dates in C#, never by SQL equality.
- **Merged** (`--no-ff` of `040a260a`). Gate 5 and 6 on the tip: only `tests/` in the diff, no assertion line, every one of the 30 user deletes with its organisation delete before it. No REGEN, no register row, no PIN-1 change. The Docker effects of all 57 files are CI-pending until **CI-W4** (the first run in which the lookup reads the rows). GATE next, then TASK-14820 + -T.

**GATE after TASK-14819** (`7703f940`): **green.** Build `-m:2 --no-incremental` 0 errors / 125 warnings / CA2100 113 distinct; Unit 1757; DemoSeed 170; Regression non-Docker 140; `tools/check_docs.py` and `tools/check_openapi_convention.py` pass. Tests-only change: no frontend, no `init.sql`, no REGEN. **TASK-14820 + -T next**, from this commit.

**TASK-14820 + TASK-14820-T (T21a, the dated organisation; QUAL-206): built, reviewed in two cycles, merged, 2026-11-17.**
- **What it does, in plain words:** until now StatsTid knew which institution a person belongs to today but not on a past day, so every dated lookup pasted today's institution onto past days. From wave 5 each institution's own settings (weekly norm, flex cap) reach pay, so a recalculation of a month before a move would have used the wrong institution's rules. The organisation now has a dated history (`user_organisations`), every existing person gets a first row "from the start of history" (`0001-01-01`) at host start, and a person made by the user create gets the same first row. A move between institutions on the user edit now has a date (omitted = today; a future date refused; a date before the current row's start refused, naming it); a later date closes the current row and opens a new one, an equal date corrects it in place. Nobody may move themselves (403, any role, before the date checks). A move dated back into a paid month or a settled holiday year puts it on HR's correction worklist under the new reason `ORGANISATION_TRANSFER`; nothing is recalculated. The employee lookup now returns each day's organisation and fails loudly on a day with a profile but no organisation. **Access does not change:** who may see or edit a person still follows today's organisation (`users.primary_org_id`).
- **Built:** TASK-14820 (`backend-infrastructure`, `a2f65f02`, report `S148-TASK-14820-report.md`; the usage limit interrupted it once and it was resumed); TASK-14820-T (`test-qa`, `f02bd8f9`, eleven Docker facts; M-11 and M-41 run locally, no local red). Declared deviations, all accepted by both lenses and the adjudicator: no constructor dependency on the repository; **the repository writes its own audit rows** (so no organisation write can exist without one); the seeder writes through the create's first-row method; the first row's date is the SQL literal `DATE '0001-01-01'`, so product and fixture organisation rows both hold the finite date (this settles the TASK-14819 carry R5); M-41 is therefore applied in `UserOrganisationRepository.InsertFirstRowAsync`, skipping the audit insert when the actor is `SYSTEM_SEED`.
- **Security consult** (`S148-TASK-14820-security-consult.md`, RESIDUALS): the access anchor untouched; scope over both organisations; the self-transfer refusal correctly placed. Three residuals: SEC-R1 (a request that becomes a transfer only under the row lock leaves one organisation un-scope-checked, inherited from S104), SEC-R2 (the old institution loses sight of the transfer), SEC-R3 (a single HR user can backdate a first transfer arbitrarily).
- **Cycle 1, dual lens:** 5α PASS. Codex **CHANGES-REQUIRED** (BLOCKER: SEC-R1 confirmed, fix now; WARNING: the transfer event's members unpinned). Fable **APPROVED-WITH-WARNINGS** (three pins that could not fail: the no-block pin's date, the event members, the self-transfer for every role and before the date rules; SEC-R1 real, proposed carrying it to TASK-14821). **The lenses disagreed on where SEC-R1 is fixed.**
- **Adjudicated** (`S148-TASK-14820-5a-adjudication-c1.md`, Fable 5.2, RULED, no owner question): **SEC-R1 fixed now, and wider than either lens framed it.** On the same path an HR user scoped only to one institution could also rename a person who had just moved, or change their agreement code (a pay input). So the user edit now refuses with 409 whenever the locked row's organisation differs from the one the checks were made on, whatever the request carries, directly after the If-Match check and before every other refusal. **Fable's carry declined**, because TASK-14821 does not in fact rewrite that part of the handler (it adds one lock statement before the tree locks), so carrying would save nothing while a second review cycle was owed anyway. The three test warnings and Codex's warning fixed as proposed; two race facts (AC-C13m, plan-added) and mutation M-44 added.
- **Fix-ups:** backend (`ff082ceb`, `AdminEndpoints.cs` only: the guard and two corrected comments; gate green); tests (`24f27085`: the mid-week no-block pin with an `AGREEMENT_CODE_CHANGE` control, the event members in both shapes, a GlobalAdmin self-transfer and a future-dated self-transfer (403, not 400), the relabel, `TransferRaceTests` with a held users-row lock and a `pg_stat_activity` barrier; M-44 run locally, no local red). 5α re-run PASS.
- **Cycle 2, dual lens:** Codex `S148-TASK-14820-5a-codex-c2.raw.md` **APPROVED** and Fable `S148-TASK-14820-5a-reviewer-c2.md` **APPROVED**. Both confirm the guard closes both halves of SEC-R1 (the unchecked organisation and the missing serialising locks), the race facts cannot pass by timing luck, no existing fact reaches the 409, and the named mutations now turn facts red. Carried to TASK-14821-T: Fable NOTE 1, assert the 409 body has exactly one member (no organisation id can leak).
- **The heads trace and TASK-14821's scope** (`S148-SEC062-heads-trace.md`; `S148-TASK-14821-SEC062-scope-adjudication.md`, Fable 5.2, RULED, no owner question): the same weakness exists in five more handlers (the profile PUT and close, the two employment-date PUTs, the settlement reversal, which needs no guessed version and did not lock the person's row), besides the agreement-code route. All six are fixed in S148 by TASK-14821, after a new security pre-pass, **TASK-14821-S**, gives the scope validator two additive methods that report the organisation they authorised, so one read serves both the check and the compare (this also closes the rarer A→B→A double move). About a dozen more routes with the same outline are registered as untraced; a second read-only trace is ordered now and adjudicated before wave 5. Plan "final + 18" carries these.
- **Told to the owner, no decision needed (either may be struck):** (1) the drift refusal covers every edit of a person whose organisation changed mid-request, not only transfers; (2) the guard goes on all six heads in S148, with two files added beyond TASK-14821's list; (3) the scope validator gains two additive methods, written by the `security` agent; (4) an honest settlement reversal caught in a true transfer race now answers 409 and must be retried.
- **Interim limits (O-13):** a wrongly dated earlier transfer cannot be corrected in the product in S148 (registered as QUAL-BF-008). Until TASK-14838 (wave 6) gives the edit drawer its date handling, an organisation change saved with a future date in the drawer shows an English 400 (`EFFECTIVE_FROM_NOT_TODAY_OR_PAST`), since the drawer always sends its picker's date.
- **Corrected counts:** the resolver has 19 construction sites, not 18 (21 with this pass's two); the `primary_org_id = @` census has 9 hits, not 3 (six are read predicates). A housekeeping slip: TASK-14820's agent deleted `*.log` files by glob in the Orchestrator's scratchpad (the GATE logs; their results were already in this log); later dispatches say "delete by exact name".
- **Merged** (`--no-ff` of `24f27085`: `a2f65f02`, `f02bd8f9`, `ff082ceb`, `24f27085`). Paperwork in the merge: QUAL-206 **FIXED**; QUAL-BF-008 registered and the comment pointed at it; SEC-048's note (its fix source is now dated; the row stays open); **SEC-062** (fixed for the user edit; six heads confirmed open, fix ruled into TASK-14821; the untraced routes named), **SEC-063** and **SEC-064** (Low, registered); the guard's comment names SEC-062; both organisation events lose "Nothing emits it yet" (O-25); the audit-projection catalog's two rows name their emitters; `BackdateWorklistRowCreated`'s remarks list the new trigger kind. No REGEN (no served type changed), `init.sql` unchanged. Every Docker effect is CI-pending until **CI-W4** (M-11, M-41, M-44 at CI-M4a). GATE next.

**GATE after TASK-14820** (`d7458466`): **green.** Build `-m:2 --no-incremental` 0 errors / 125 warnings / CA2100 113 distinct; Unit 1757; DemoSeed 170; Regression non-Docker 140; `tools/check_docs.py` and `tools/check_openapi_convention.py` pass. No frontend file, no `init.sql`, no REGEN. **Next:** TASK-14821-S (the scope validator's two additive methods, `security`) and the second SEC-062 trace, in parallel; then TASK-14821 + -T on TASK-14821-S's branch; then CI-W4, CI-M4a, CI-M4b.

**SEC-062, the second trace and the owner's ruling, 2026-11-17.**
- **Plan "final + 18"** (Planning Drafter, report `S148-plan-f18-report.md`): the TASK-14820 adjudication's plan parts (AC-C13m, M-44) and the TASK-14821 scope adjudication (`S148-TASK-14821-SEC062-scope-adjudication.md`): the new pre-pass **TASK-14821-S** (`security`, the scope validator's two additive methods, brief written), the guard on six heads in TASK-14821 (AC-C13n, M-45), the reversal's users-row lock in Block C, O-26 and O-27. The carries for TASK-14822 and TASK-14838 sit in their ledger rows (their briefs are not written yet). The drafter's four questions are ruled below.
- **The second trace** (`S148-SEC062-second-trace.md`, a census of all 92 scope calls): 22 more person-scoped write sites share the weakness, eleven of them with no token at all because a transfer does not touch the row they use as their version (approval decisions, the HR send, overtime decisions, the Skema save, first-time creates). The worst is the reporting-line "remove person" route, which deactivates the person with no version check. Three entries on the register's untraced list were reads, not writes, and are corrected.
- **Adjudicated** (`S148-SEC062-second-trace-adjudication.md`, Fable 5.2): the trace confirmed by spot-checks; the fix stays the per-site guard in three tiers (compare the tree-locked organisation already in hand; compare the already-locked users row; or add a locked users read); the alternative of having the transfer take the `employee-` lock is declined (it covers 6 of 22 sites and changes TASK-14821's lock order at dispatch). A manager admitted only by the reporting edge never meets the guard; an actor with both arms is refused once and succeeds on retry. **TASK-14821 does not change.** The nine sites in files S148 already edits go into a new task, **TASK-14845** (+ -T), last in wave 4 (the adjudicator amended its own earlier placement in TASK-14825/14828: those tasks edit other ranges and carry no security consult).
- **Owner ruling (asked 2026-11-17, one question, prose first):** the thirteen sites in files S148 does not otherwise touch (the reporting-line remove-person, assign, delete and the two acting routes; settlement resolve, reconcile-payout and the transfer agreement; the termination payout request; the eligibility and birth-date PUTs; the worklist resolve; the user-unit change) were the owner's call under the A-18 rule. **The owner chose to fix all thirteen in S148, in TASK-14845** (the recommended option): one task, one pattern and one review close the whole class, at the cost of a larger task (22 sites in nine files), thirteen more timing facts and a later start to wave 5. Pins: AC-C13o (the nine) and AC-C13p (the thirteen), mutations M-46 and M-47 at CI-M4a.
- **Orchestrator rulings on the drafter's questions (brief interpretation, no requirement change):** (1) TASK-14821-S's "one core": one core per check (the active-only and the terminated-inclusive check each keep their own core, shared by their existing methods and their new sibling), since the two checks differ in more than their read; (2) the guard sites use TASK-14821-S's constants, and TASK-14821's Gate 4 grep is restated to count the constant's uses (the implementer reports each site's form); (3) Block C's reversal remainder after the settlement rows applies only when the reversal carries an end-date correction; (4) the drafter's additions beyond the listed parts are accepted.
- **Next:** TASK-14821-S dispatched; plan "final + 19" (TASK-14845 + -T, AC-C13o, AC-C13p, M-46, M-47, the carries into TASK-14825/14828, the corrected SEC-062 row) drafted in parallel, then the TASK-14845 briefs adjudicated before its dispatch. SEC-062's register row is updated with the TASK-14821 merge.

**TASK-14821-S returned; plan "final + 19", 2026-11-17.**
- **TASK-14821-S** (`security`, `51c61ac7`, report `S148-TASK-14821-S-report.md`): `OrgScopeValidator.cs` only. Two sibling methods (`ValidateEmployeeAccessWithOrgAsync`, `ValidateEmployeeAccessIncludingTerminatedWithOrgAsync`, each with a nullable role floor) return the organisation they authorised from the check's own single read; one core per check, so every existing caller is unchanged; the clause name and the 409 sentence become constants. Build 0 / 125 / CA2100 113; Unit 1757, DemoSeed 170, Regression non-Docker 140. Reviewed on the union with TASK-14821 and -T, no review cycle of its own (ruling 3).
- **Plan "final + 19"** (Planning Drafter, report `S148-plan-f19-report.md`): TASK-14845 (`backend-infrastructure`) and TASK-14845-T (`test-qa`), drafts 1, last in wave 4 before CI-W4: the guard on all 22 remaining sites (the owner's ruling), AC-C13o (nine parts) and AC-C13p (thirteen parts), M-46 and M-47 at CI-M4a; the shared-files rows; the carries into the TASK-14825 and 14828 rows; 75 agent tasks; 139 AC ids. TASK-14821 and -T at draft 3.1 (Gate 4's constant count; the 409 body's exactly-one-member assertion), TASK-14821-S at draft 1.1 (rulings recorded; contract unchanged).
- **Orchestrator rulings on the drafter's questions:** (1) Block C's reversal line stays as written from the code (the re-settlement runs in every reverse-and-supersede reversal; only the span and strand guards, the lifecycle writer and the worklist rows depend on an end-date correction), which corrects my earlier ruling (3), made without reading `SettlementReversalService.cs:252`; (2) the locked read at each tier-3 site is the same kind as that site's existing validator; (3) "the route's existing not-found" means the person's not-found, else the new 404; (4) on the reversal the 409 body has exactly its two contract members, `error` and `failure`; (5) "no read between the validator call and the guard" means no read added and none feeding the compare; (6) the drafter's additions are kept, and TASK-14845-T's fact names are proposals for the brief adjudication.
- **Next:** TASK-14821 dispatched on `51c61ac7`; the TASK-14845 and 14845-T briefs adjudicated in parallel.

**TASK-14845 and -T briefs adjudicated; plan "final + 20", 2026-11-17.**
- **Brief adjudication** (`S148-TASK-14845-briefs-adjudication.md`, Fable 5.2, RULED, no owner question): all 22 site contracts confirmed against the code and the validator as built; the eight tree-locked sites' premise holds (the tree root is the person's organisation, and a transfer writes it only under that lock). Changes: the send command (W4) and the Skema save (W20) make their existing in-lock read of the person the locking read and compare directly after its 404, so a stale request learns nothing about a person outside its scope (this amends the second-trace adjudication's W20 placement); W5, W7 and W8 state READ COMMITTED (PAT-015, behaviour-neutral); the timing facts' red assertion, their before/after counts (W21's own fixture is a worklist row) and the success status each mutation produces are fixed in the test brief. All six Orchestrator rulings on the "final + 19" drafter's questions stand. A new 5a reading item: seventeen tables reference `users`, so a same-request insert on another connection could wait on the request's own row lock (unproven; the HR-on-behalf success facts at CI-W4 would hang).
- **Applied** as TASK-14845 and -T draft 2 and plan "final + 20" (report `S148-plan-f20-report.md`; counts unchanged). Orchestrator rulings on the drafter's three questions: the drafted reading (a) in each case, and the sentence added to TASK-14845's contract item 7 (on W4 the admit's two refusals precede the compare, because the compare needs the admit's value) is confirmed.

**TASK-14821-S + TASK-14821 + TASK-14821-T: built and reviewed (cycle 1), 2026-11-17/18.**
- **Built:** TASK-14821-S (`security`, `51c61ac7`); TASK-14821 (`backend-infrastructure`, `3513d2b1`, report `S148-TASK-14821-report.md`): the system-wide pay-inputs lock `(148, 1)` as the first statement of seven correction transactions under READ COMMITTED; the four self-change refusals (P-26); the employment-date and profile-close hand-offs to HR's worklist (`EMPLOYMENT_WINDOW_CHANGE`) and the start-date event; the organisation-drift guard on six heads (SEC-062). TASK-14821-T (`test-qa`, `fd13174c`): the lock census, self-change, hand-off and drift facts; the 16 `SettlementReversalTests` facts given `ExpectedSubjectOrgId`; M-42 and M-45 run locally, no local red.
- **Security consult** (`S148-TASK-14821-security-consult.md`): RESIDUALS, all Low (the profile heads' own 412/404/422 answer before the guard; the system-wide lock couples organisations; the eligibility and birth-date self-edit doors).
- **Cycle 1, dual lens:** 5α PASS. Codex **CHANGES-REQUIRED** and Fable **CHANGES-REQUIRED**, agreeing that the production code is sound and that the lock-census test helper reads three columns PostgreSQL leaves empty on every waiter's `virtualxid` lock, so the eight lock facts would crash on correct code (BLOCKER). Fable also found three pins that could not fail (GlobalAdmin on the self-change refusals; the opening-Monday rule; the profile PUT's drift fact masked by its second clause). The lenses differed on pool starvation (Codex: a WARNING with a production fix; Fable: a bounded stall).
- **Adjudicated** (`S148-TASK-14821-5a-adjudication-c1.md`, Fable 5.2): the BLOCKER and the three pins fixed in a `test-qa` fix-up, plus two optional pins (the self close with no If-Match; the start cleared when already none); three stale comments in a comment-only backend fix-up; pool starvation real but bounded, carried to TASK-14831's measurement and registered (SEC-066), because removing the second connection would change what a gap-fill correction writes (ADR-032 D4, the S138 deferral). **Told to the owner, who may strike it.**
- **Owner ruling (2026-11-18, one question, prose first):** the "nobody changes their own inputs" rule is extended to one's own **entitlement eligibility** and own **birth date**, added in **TASK-14845** (the recommended option); the bare reversal of one's own settlement stays registered (SEC-065). The TASK-14845 briefs and the plan are amended before its dispatch.
- **Fix-up 1** (`backend-infrastructure`, `d5466748`, comments only, build green). **Next:** the `test-qa` fix-up (adjudication § 3b) on `d5466748`, 5α, cycle 2 by both lenses, merge with § 5's paperwork. The work paused here for a machine restart; the handoff is `.claude/plans/s148/RESUME-POINT.md`.

**TASK-14821-S + TASK-14821 + TASK-14821-T: fix-up 2, cycle 2, merged, 2026-11-18.**
- *In plain words:* the test fix-up repaired the lock-table helper that would have crashed on correct code, and turned four weak pins into real ones. Both reviewers then approved without a blocking finding, and the three tasks merged together. Nobody can now change their own pay inputs. Every Backend correction to a pay input takes one system-wide lock first, so payroll and a correction can no longer miss each other. Employment-date changes that reach paid months land on HR's worklist. Six more write routes refuse a request caught by a transfer mid-flight.
- **Resumed after the machine restart.** The stale worktree `agent-a94cd019d00c5c17c` (locked by a dead agent) was unlocked and removed. Free disk was 15 GB.
- **Fix-up 2: `test-qa`, adjudication § 3b, commit `24e6e004`.** Report: `S148-TASK-14821-T-fix1-report.md`.
  - **The BLOCKER (R1) is fixed.** `HoldsSql` now COALESCEs the three key columns to -1. This is proven on a throwaway local PostgreSQL 16 cluster: the waiter's granted `virtualxid` row read empty with the old query and `-1/-1/-1` with the new one.
  - **New facts, all F (CI):**
    - AC-C13h (5): four GlobalAdmin self-change facts;
    - AC-C13h (6): the self close with no If-Match answers 403, not 428;
    - AC-PIL-h (12): a start on Tue 30 Jun 2026 with July exported lists July, effective from Mon 29 June;
    - AC-PIL-h (13): a start cleared when already none gives 200, one event and no row;
    - AC-C13n (7): the profile PUT behind `(148, 1)`, where only the authorised compare can refuse.
  - **M-45:** as run on the profile PUT, it removed both organisation clauses (`EmployeeProfileEndpoints.cs:559-560`) and kept the null checks. The local census found no red (every guard fact is Docker). The frozen list is seven.
  - **Gate:** build 0 / 125, CA2100 113; Unit 1757; DemoSeed 170; Regression non-Docker 140.
- **5α** (`S148-TASK-14821-5alpha-c2.md`): PASS.
  - Its one FAIL, item 9, was the Orchestrator's checklist error, not the commit's: `test-qa` runs on Sonnet 5.5 by definition, so its `Co-Authored-By: Claude Sonnet 5.5` trailer is truthful. Accepted, no amendment.
- **Cycle 2, dual lens:**
  - **Codex APPROVED** (`S148-TASK-14821-5a-codex-c2.raw.md`), one NOTE: part (7)'s summary named the wrong clause as unpinned, and the class choreography said "every fact".
  - **Fable 5.2 APPROVED** (`S148-TASK-14821-5a-reviewer-c2.md`), five NOTEs. NOTEs 1–3 are stale test-class headers; NOTE 4 is `StrandedRegistrations` missing from the reversal's 409 list; NOTE 5 is `(unproven)`.
  - **NOTE 5:** part (7)'s "exactly one waiter" poll would see a background taker of `(148, 1)` if one ever ran in the Backend test host. It could only flake red, never pass falsely.
  - **Orchestrator rulings, no adjudication needed (every NOTE is comment drift or a carried re-read):** Codex's NOTE and Fable NOTEs 1–4 are applied as an Orchestrator comment-only commit, `2ab51e6a`. NOTE 5 is **carried: re-read part (7)'s poll when TASK-14831 lands** a shared taker of the lock.
- **Merge-time comments in `2ab51e6a`** (adjudication c1 § 5 items 2 and 4, R6):
  - The `EmployeeEmploymentStartDateSet` remark loses "Nothing emits it yet…" (O-14).
  - `BackdateWorklistRowCreated` gains `EMPLOYMENT_WINDOW_CHANGE` (its kind, its trigger events, its effective-from) and the profile close's `EmployeeProfileSoftDeleted`.
  - The reversal's 409 list gains `StrandedRegistrations`.
  - Comments only; worktree build 0 / 125.
- **Paperwork in the merge:**
  - **SEC-062:** the row now carries the second-trace adjudication's § 5 text, with these status groups:
    - FIXED: the user edit;
    - FIXED: six more heads, with the accepted residual (the 412, 404 and date-free 422 answer before the guard);
    - OPEN, fix ruled into S148 (TASK-14845): the nine (AC-C13o, M-46) and, by the owner's "Fix all 13 now", the thirteen (AC-C13p, M-47);
    - NO CASE.
  - **SEC-065 registered:** self-edit. Pay inputs are FIXED. The eligibility writes and the birth-date PUT are OPEN, fix ruled into TASK-14845 by the owner on 2026-11-18. The bare reversal of one's own settlement is OPEN and registered.
  - **SEC-066 registered:** the system-wide lock couples organisations, and its holder needs a second pooled connection. It is carried to TASK-14831-T's measurement.
  - **SEC-060** gains "The per-person self doors are SEC-065."
  - **Catalog:** the `EmployeeEmploymentStartDateSet` row is now "emitted by the employment start-date PUT, inline row, target organisation the locked users row's (TASK-14821)".
  - **ROADMAP backlog:** one "plausible date" bound at the request edge for dated admin writes (R5).
- **O-14 notes:**
  - The pay-inputs lock (PIL) closes S138's timing gap. A correction and a payroll recording of the same month now serialize on `(148, 1)`, so neither can miss the other. Payroll's shared half lands in TASK-14831.
  - The profile close's self refusal follows A-18's rule: nobody closes their own profile.
- **Recorded per the adjudication's § 5 item 8:**
  - **Rulings:** R1–R10 of `S148-TASK-14821-5a-adjudication-c1.md` stand as written. The file is the verbatim record.
  - **Told to the owner, who may strike it:** the pool carry (SEC-066). If struck, the question is whether S148 gains a task that moves the profile correction's revaluation reads into its own transaction, which would also make a gap-fill correction re-record absences (S138's deferral).
  - **The § 6 question is answered:** option (a), 2026-11-18.
  - **Declined** (adjudication R4, § 7): consult items 4, 6, 7, 9, 10 and 12–17; replacing part (2)'s choreography; a LocalAdmin leg; a separate mutation for part (7); the clamp for NOTE 5 (cycle 1); Codex's unproven lock-cycle NOTE; NOTE 6's wait-poll remark.
  - **Real lock lines:** `EmploymentDateEndpoints.cs:214` and `:581`.
  - **The overwritten scratchpad `build.log`:** in cycle 1, a `test-qa` pass overwrote the shared scratchpad `build.log`. Agents now work in their own named subfolder.
  - **CI-W4 watch items:**
    - the LocalHR agreement-code fixture on the user edit;
    - the reversal fixtures at the 10 November clock (part (6)'s TERMINATION settle is argued, not run);
    - `EmploymentEndDateCorrectionGuardTests.SameFerieaarCorrection_SettlementOutsideSpan_StillAllowed`;
    - the eight `PayInputsLockCensusTests` lock facts, expected green now.
- **Plan:** the planner is applying the owner's ruling of 2026-11-18 as plan **"final + 21"** with TASK-14845 and -T draft 3. This merge's plan items (adjudication c1 § 5 item 5) follow as **"final + 22"**:
  - AC-C13h (5) and (6); AC-PIL-h (12) and (13); AC-C13n (7);
  - M-45 with a frozen list of seven;
  - CI-W4's new parts;
  - TASK-14831-T's third measured figure (SEC-066);
  - O-19's note.
- **Next:** GATE on the merged master; then plan "final + 22"; then TASK-14845 + -T (draft 3) with a security consult and both lenses; then CI-W4, CI-M4a, CI-M4b.

**GATE after TASK-14821** (`e58df211`): **green.** Build `-m:2 --no-incremental` 0 errors / 125 warnings / CA2100 113 distinct (all unchanged); Unit 1757; DemoSeed 170; Regression non-Docker 140 (the ten new facts and the eight lock facts are Docker, CI-pending until CI-W4). `AuditProjectionParityTests` (which parses the catalog row edited at this merge) 3/3. `tools/check_docs.py` and `tools/check_openapi_convention.py` **not run locally**: no Python on this machine today. CI runs both, and this merge changes no schema, KB entry or OpenAPI surface. No frontend file, no `init.sql`, no REGEN.

**TASK-14845 and -T draft 3 (the owner's ruling of 2026-11-18) and the draft-3 brief adjudication; plans "final + 21", "final + 22", 2026-11-18.**
- *In plain words:* the briefs now extend "nobody changes their own inputs" to one's own entitlement eligibility and birth date. Each of the two requests answers 403 with one fixed sentence when the actor is the person, for every role, and writes nothing. A review of the amended briefs accepted the design. It added two small tests so the refusal's place before any other check is pinned, and ruled that only the owner can drop it. The owner then chose to fix how the edit drawer shows such a refusal, in a new small UX task.
- **Plan "final + 21"** (Planning Drafter, `S148-plan-f21-report.md`, `64e0e524`): TASK-14845 contract item 11, the self refusal on the eligibility PUT and the birth-date PUT; TASK-14845-T's four facts (AC-C13q, a new file `Security/SelfEntitlementInputRefusalTests.cs`); O-28 (SEC-065's two doors move to fixed at TASK-14845's merge). **Census:** no existing test, e2e spec or demo path edits its own eligibility or birth date. The one self request in the suites (`SkemaEntitlementEligibilityGuardTests.EligibilityWrite_NonHrCaller_IsForbidden`) is an Employee stopped by the route's `HROrAbove` policy. Demo birth dates are written by SQL. Counts: 140 AC ids, 44 mutation ids, 75 agent tasks.
- **Plan "final + 22"** (`S148-plan-f22-report.md`, `b96ee8ea`): TASK-14821's merge items: AC-C13h (5), (6); AC-PIL-h (12), (13); AC-C13n (7); M-45's frozen seven; CI-W4's new parts; TASK-14831-T's third measured figure (SEC-066); O-19's note; cycle-2 NOTE 5 carried to TASK-14831-T's row as a re-read. Orchestrator rulings on the drafter's two questions: M-45's "with" list and the CI-W4 M-45 line stay as they are (cosmetic; still true). O-14 is not marked done in the plan, following the plan's own precedent; its merge record is the TASK-14821 entry above.
- **Brief adjudication** (`S148-TASK-14845-draft3-adjudication.md`, Fable 5.2, RULED-WITH-QUESTIONS): every code claim and the census re-verified at master `5e3b71fd`. The drafter's readings were accepted on (i) the new reason sentence, (ii) the unconditional refusal (neither writer has a no-op outcome, and a pass-through would still write an audit row signed by the subject), (iii) the place, directly after the scope 403 and before the transaction, (iv) the `self-change` log clause, and (vi) no departed-self order fact. **(v) is ruled the other way: the two refusals are not cuttable; only the owner strikes them** (R6). **Added: AC-C13q (5) and (6)**, a LocalHR self request with no precondition header answers 403, not 428 (R4; the pin that the refusal's place is kept). Stale validator and test-helper cites corrected to master. The edits are applied as draft 3.1 and plan "final + 23".
- **Owner ruling (2026-11-18, one question, prose first; the adjudication's § 4):** when an HR user tries to edit their own record in the edit drawer, the server's refusal currently shows as raw English JSON on a Danish screen. The owner chose **(a), the recommended option: a small `ux` task in S148 after TASK-14845 merges (TASK-14846)**. When the drawer's subject is the logged-in user, the own-record fields the server refuses are disabled with one Danish note, following the termination page's existing "this is you" pattern. It is added to the plan in "final + 23"; it is owner-ruled, so the Orchestrator cannot cut it.
- **Plan "final + 23"** (`S148-plan-f23-report.md`): the adjudication's § 3 is applied as TASK-14845 and -T draft 3.1 (AC-C13q (1)–(6), six F (CI) facts; the self refusal is in the "Not cuttable" list). **TASK-14846** (`ux`, size S, wave 5, last, before CI-W5) and **AC-C13r** are added.
  - **Why wave 5:** it needs only TASK-14845. Putting it at wave 4's tail would hold up CI-W4, the backend checkpoint wave 5 waits for. It lands before wave 6's TASK-14837 and TASK-14838, which also edit `PersonDrawer.tsx`.
  - **Orchestrator rulings on the drafter's questions:**
    1. The draft-3 history lines stay as annotated.
    2. The Danish note is contract: "Du kan ikke selv ændre de låste felter, fordi de har betydning for din egen løn eller dine rettigheder. Bed en anden HR-administrator om at gøre dette." Its second sentence is the termination page's own (`TerminationPage.tsx:266`).
    3. The effective-date picker is disabled, not hidden, in the self case, since every dated field it governs is locked there. TASK-14838's row carries "keep the disable".
- **The drafter's finding 4, put to the owner (2026-11-18, one question, prose first): an HR user or administrator can deactivate their own account.** Two routes allow it: "remove person" on the reporting-line tree (`ReportingLineEndpoints.cs:1290`; the scope check at `:1320` has no self check; the deactivation is at `:1677`), and the user edit with `isActive: false` (`AdminEndpoints.cs:2184-2185`; P-26's self check at `:2141` covers only the agreement code). Both are confirmed in the code by the Orchestrator. The recommendation was to refuse both in S148. **The owner chose (b), register only.** Recorded as **SEC-067** (Low; audited and reversible; self-lockout), revisited with SEC-060's second-signer work (E11) and SEC-065; SEC-065 points to it. "Remove person" and the user edit's `isActive` stay enabled for a self in TASK-14846.
- **TASK-14845 returned** (`3cfc9693`, `backend-infrastructure`, Opus 5.5; report `S148-TASK-14845-report.md`). It adds the organisation-drift guard to W1–W22 through one helper (`Helpers/OrganisationDriftGuard.RefuseAsync`), and the self refusal on one's own eligibility PUT and birth-date PUT (contract item 11). GATE matches the baseline: 0 errors, 113 distinct CA2100, Unit 1757, DemoSeed 170, Regression non-Docker 140.
  - **TASK-14845-T** (`252b8a9a`, `test-qa`, Sonnet 5.5) adds 22 race facts and six self facts. All 28 were exercised locally on a throwaway PostgreSQL 16 cluster, with the Docker harness patched only in scratch worktrees; they remain CI-pending. All 28 are red on `ba0a054e`. The M-46 and M-47 local censuses trip exactly their nine and thirteen facts.
  - **One existing fact turned red:** `OvertimeCompensateRaceTests`. W19 now locks the employee's users row first, so the second compensation queues there instead of at the balance update.
    - The Orchestrator ruled this a correct consequence: the outcome the fact asserts is unchanged, and only the observation point moved.
    - Fix-up 1 (`148ac311`, `0251f918`) re-pointed the second observation and passed locally three runs out of three.
    - A static census of the lock-barrier facts found no other fact to re-point; the ten classes most at risk were run green.
- **Security consult** (`S148-TASK-14845-security-consult.md`, Sonnet 5.5): RESIDUALS, all Low, and nothing to change before merge.
- **5α** (`S148-TASK-14845-5alpha.md`): one FAIL, accepted. The WIP commit `148ac311` has no trailer; it is one unit with `0251f918`, which carries the trailer, and rewriting would have changed the SHAs both lenses were reviewing.
- **Step 5a, cycle 1:**
  - Codex: APPROVED, two NOTEs.
  - Fable 5.2: APPROVED-WITH-WARNINGS. The two WARNINGs are about text, not behaviour:
    - three race facts state the wrong red reason;
    - comments that the change made untrue, one of them the terminated-inclusive read's allowlist.
  - Neither lens found a wrong outcome. The Orchestrator ruled directly (`S148-TASK-14845-5a-rulings-c1.md`), since no finding is contested: both warnings are fixed in a fix-up round, and the unproven lock interactions and the 2-of-7 local run become CI-W4 watch items.
- **Correction, the Orchestrator's own earlier ruling.** The R7 overtime note was first ruled below the QUAL floor because "no screen reaches the pre-approvals". That premise was wrong: the page has been routed for leaders since S140 (QUAL-162 FIXED). Weighed again by analogy with QUAL-239 (M, D-audit), it clears the Medium floor, and Fable independently says to register it. It becomes **QUAL-257**.
- **Owner ruling (2026-11-18, one question, prose first): QUAL-257, the overtime pre-approval double decision, is fixed now, inside TASK-14845's fix-up round.** This was option (a), the recommended one.
  - **The problem.** Two leaders can approve and reject the same request at once, and both decisions land. The first leader is told their decision stood, and the trail records two decisions for one request. Nothing in pay reads it.
  - **The fix.** The status update requires PENDING, and the second decision answers 409 with nothing written. TASK-14845's new users-row lock already queues the two decisions, so the guarded update is enough.
  - **The plan.** It is planned as "final + 24": AC-C13s and M-48. TASK-14845 fix-up 1 (`backend-infrastructure`) also carries the comment corrections and W22's READ COMMITTED pin, and TASK-14845-T fix-up 2 (`test-qa`) follows it. Cycle 2 runs on both lenses.
- **Plan "final + 24"** (`S148-plan-f24-report.md`):
  - **Fix-up rounds.** TASK-14845 fix-up 1 and TASK-14845-T fix-up 2.
  - **New ids.** AC-C13s and M-48 (CI-M4a; 11 mutations there now). The plan now has 142 AC ids and 45 mutation ids.
  - **O-29.** QUAL-257 is registered as FIXED at the merge, and SEC-062 gains its dated notes.
  - **CI-W4 watch items.** R6, R7 and R8 of the rulings.
  - **The Orchestrator's rulings on the drafter's questions:**
    - fix-up 1 gets its own security consult, "as its parent" (`PLAN:176`);
    - W17 and W18 also state READ COMMITTED, because QUAL-257's clean 409 depends on it;
    - the older CI-M4a rows stay as dated records.
- **TASK-14845's fix-up round and merge (`53c2793d`).**
  - **TASK-14845 fix-up 1** (`9d9a027b`, `44931e61`; `backend-infrastructure`, Opus 5.5; report `S148-TASK-14845-fix1-report.md`):
    - QUAL-257's guarded status update on overtime approve and reject; both routes now state READ COMMITTED, on which the clean 409 depends.
    - The comments the task had made untrue are corrected, and the user-unit PUT states READ COMMITTED.
    - Two Orchestrator comment commits complete the terminated-inclusive locked read's allowlist with its four older callers (`63096080`) and correct one of them (`2e1cd20e`; the fix-up's own security consult, R1).
    - Its security consult (`S148-TASK-14845-fix1-security-consult.md`, as its parent's, `PLAN:176`) answers RESIDUALS, with nothing to change before merge.
  - **TASK-14845-T fix-up 2** (`7116a0da`; `test-qa`, Sonnet 5.5; report `S148-TASK-14845-T-fix2-report.md`):
    - AC-C13s, the overtime decision race: green three times out of three locally, and red on `0251f918` with 200 and 200.
    - M-48's local frozen list is that one fact only.
    - The W5, W8 and W9 red reasons are corrected, and the self facts now assert the body's members.
  - **5α cycle 2** (`S148-TASK-14845-5alpha-c2.md`): one count in the brief ("six assertions") is ruled PASS on substance, since one shared helper covers all six facts.
  - **Cycle 2** (Codex and Fable both APPROVED-WITH-WARNINGS; `S148-TASK-14845-5a-rulings-c2.md`):
    - Both lenses find the QUAL-257 fix sound.
    - Their one shared WARNING: the reworked failure helper changed how W5, W8 and W9 fail on the base, and the docstrings had not followed it. It is fixed in an Orchestrator comment commit (`07286b1f`), together with Codex's "not cancelled" note and Fable's stale "no audit row" remarks.
    - **Cycle 3** (a confirmation pass over that commit): both lenses APPROVED.
  - **Registers:**
    - **SEC-062.** The 22 remaining sites are FIXED, with dated notes (a)–(f).
    - **SEC-065.** One's own eligibility and birth date are FIXED; only the bare reversal of one's own settlement stays open.
    - **QUAL-257.** Registered as FIXED (owner ruling of 2026-11-18, option (a)).
- **Carried from TASK-14845:**
  - **CI-W4 watch items:**
    - The on-behalf success facts must not hang or deadlock (rulings R6; the unproven lock interactions).
    - `OrganisationDriftGuardTests` must run all seven facts; the local run showed 2 (R7).
    - Choreography B and AC-C13s must stay green on repeat (R8, C2-R5).
    - M-46, M-47 and M-48 run at CI-M4a.
  - **Owed at the sprint close:** a census of the other decision routes for QUAL-257's check-then-act shape, with a concrete two-request ordering for each hit (C2-R4, unproven).
  - **Found, not fixed, below the floor:**
    - six more `UnitEndpoints.cs` transactions open on the default isolation with the `unit-org-` advisory first, the same PAT-015 class as W22, so the behaviour is neutral;
    - stale `OvertimeEndpoints.cs` line citations in `AgreementCodeHttpDeterminismTests.cs`.
- **GATE green after the TASK-14845 merge** (`53c2793d`, paperwork `30d382d4`): `dotnet build StatsTid.sln -m:2` has 0 errors and 125 warnings, with 113 distinct CA2100 (a full rebuild; unchanged). Unit 1757, DemoSeed 170, Regression `Category!=Docker` 140. These suites ran one at a time, in the foreground. The Docker facts (AC-C13o, AC-C13p, AC-C13q, AC-C13s and the re-pointed compensate race) are CI-pending until CI-W4, and M-46, M-47 and M-48 until CI-M4a. `check_docs.py` and `check_openapi_convention.py` were not run locally (no Python on this machine); CI runs them.

**CI-W4** (run 44352425811, draft PR #63, at `c17db07c`, headSha confirmed): **green, all seven jobs** (started 2026-11-18 16:53 UTC, a Wednesday; Regression finished 19:01 UTC).
- **What it shows:** wave 4's second half (TASK-14819, TASK-14820, the TASK-14821 stack and TASK-14845, with QUAL-257) ran its Docker facts for the first time, and all passed. Read from the trx files by name, not from the job colour.
- **Tests:** Unit 1757, DemoSeed 170, Regression **2691 of 2691**, Docker included (CI-W4a had 2597). Nothing failed, nothing skipped.
- **Facts seen passing, by name:**
  - AC-C13o and AC-C13p: `OrganisationDriftGuardRoutesTests`, 22 of 22.
  - AC-C13q: `SelfEntitlementInputRefusalTests`, 6 of 6.
  - AC-C13s: `OvertimePreApprovalDecisionRaceTests.TwoConcurrentDecisions_OneLands_TheOtherIs409_OneEvent`, and the re-pointed `OvertimeCompensateRaceTests` fact.
  - AC-C13n (1)–(7): `OrganisationDriftGuardTests`, 7 of 7. AC-C13m (the user edit): `TransferRaceTests`, 2 of 2.
  - The earlier wave-4 lock and transfer classes: `PayInputsLockCensusTests` 10, `SelfChangeRefusalTests` 14, `EmploymentWindowHandOffTests` 12, `TransferWriteTests` 9, `SelfTransferTests` 4, `TransferHandOffTests` 2, `UserOrganisationBackfillSeederTests` 2, `ProfileCloseHandOffTests`, `EmploymentProfileResolverOrganisationTests`, `CreatedPersonBackdatedOrganisationTests`.
  - **Both PIN-1 worlds** (`DemoPayLinesPinTests`, 2), so pay is unchanged by wave 4.
- **Watch items (the TASK-14845 Step 5a rulings):**
  - **R6, no hang:** the slowest regression fact took 7.6 s (a PIN-1 world), and no fact ran over 20 s. Every drift and self-refusal fact took under 0.6 s. So the on-behalf success facts neither hung nor deadlocked. This is one run's evidence; it lowers the unproven lock interaction's likelihood but does not prove its absence (SEC-062 note (e) stays).
  - **R7, 7 of 7:** all seven `OrganisationDriftGuardTests` facts executed and passed. The local count of 2 was a local-run artefact, not missing facts.
  - **R8 and C2-R5, green on repeat:** Choreography B's eight tier-1 facts ("…BeforeTheTreeLock…") and AC-C13s are green on this run. The repeat is CI-M4b, whose mutations do not touch them.
- **Ruling 1.9 settled:** this run's Copenhagen date was Wednesday 18 November, so it is the owed non-Monday Regression run. Every real-clock fact passed.
- **The other jobs:** smoke, E2E, the frontend build, docs, lizard and gitleaks are green.
- **Status change:** wave 4's second-half Docker facts move from "CI-pending" to "CI-verified". That covers AC-C13o, -p, -q and -s on SEC-062, SEC-065 and QUAL-257; their mutations M-46, M-47 and M-48 stay CI-pending until CI-M4a. PR #63 is closed unmerged, and `ci/s148-w4` is deleted.

**CI-M4a and CI-M4b built** (`test-qa`, Sonnet 5.5; report `.claude/reviews/S148-CI-M4-builder-report.md`), on `c17db07c`, as two local branches:
- **CI-M4a** (`ci/s148-m4a`): the mutation commit `3d506e58` holds M-9 (read half), M-11, M-15 (read half), M-39, M-41, M-43, M-44, M-45, M-46, M-47 and M-48, and touches `src/**` only. The evidence commit (rule 3) is `5f3f9a72`.
- **CI-M4b** (`ci/s148-m4b`): the mutation commit `1834c305` holds M-10, M-14, M-16, M-40 and M-42. The evidence commit is `6fc866ca`.
- **Local run:** on each branch Unit 1757, DemoSeed 170 and Regression non-Docker 140 stay green, as expected, because every pin is Docker-gated.
- **The Docker pins, exercised locally:** the builder also ran the pins on a throwaway PostgreSQL 16 cluster on port 55434, with `DockerHarness` patched in scratch worktrees only. The cluster and the worktrees are deleted, and the 5432 service was never touched.
  - Every frozen fact that ran went red at the assertion its row names, marked [O] below.
  - The facts behind an earlier red assertion are argued, marked [A].
  - This is not CI evidence. It ran on PostgreSQL 16.14 on Windows, not on `postgres:16-alpine`.
- **Orchestrator rulings on the report:**
  - **M-10 touches `docker/postgres/init.sql`** (two lines that unset the seeds' `published_at`), so the brief's "`src/**` only" does not hold for CI-M4b. Ruled acceptable: the compiled seeds are written only by `init.sql`, 14818-T's census used the same form, CI-M2b's M-23 set the precedent, and the branch is never merged.
    - `(unproven)`: an `init.sql`-level mutation reaches every class that applies the schema. The builder ran only the Config namespace and the five pinned classes under it (all frozen facts red at their assertions, the rest green).
    - Rule 5 governs any wider red. A red explained by M-10 is a census miss, recorded by name with no re-run.
  - **M-45, the profile PUT answers 403, not 200.** Under M-45, `ProfilePut_OrganisationChangedUnderTheLock_409_NothingWritten` answers 403 "Actor scope does not cover target organization". 14821-T had argued 200.
    - The fact is still red at its 409 assertion, so the pin is proved under rule 5.
    - The 403 shows that the compare is not the only guard on that path: a later scope check also refuses. Recorded for the mutation table; no change ordered.
  - **Plan drift, noted only:** the census for M-41 is in TASK-14820-T's report, not TASK-14819's. M-44's lines are `AdminEndpoints.cs:1935-1945` at `c17db07c`.
- **Frozen list, CI-M4a** (verbatim from `3d506e58`):

```text
FROZEN LIST (rule 2). Class.Fact : assertion expected to fail first. [O] = seen red on a
throwaway local PostgreSQL 16 by the builder, [A] = argued from the code.

Unit 0 reds; DemoSeed 0 reds; Regression Category!=Docker 0 reds.

M-9  PublishedOnlyInForceTests.ArchivedDraft_NeverInForce_NeverPredecessor [O]
       first May read: Assert.Equal(v1, inForce.ConfigId) (PublishedOnlyInForceTests.cs:51); the
       v2-publish BEFORE_PREDECESSOR half is behind it [A]
M-11 TransferWriteTests.Transfer_DrawerCorrectionBeforeLaterTransfer_200 [O]
       Assert.Equal(OK, status): expected OK, actual BadRequest (:380)
M-15 SameDayTieBreakTests.TwoPublishesSameDayDateOmitted_ReadReturnsTheLaterPublish [O]  (1) Assert.Equal(v3) :40
     SameDayTieBreakTests.OverrideEditedTwiceOnOneDate_ResolvesTheLaterWrite [O]          (2) expected 130 actual 110 :101
     SameDayTieBreakTests.EarlierStartedPublishCommittingLater_IsTheOneInForce [O]        (4) second assertion, in-force is not X :156
       (its first assertion, X's published_at later than Y's, stays green under M-15)
M-39 DatedConfigBoundaryTests.VersionFromMondayInsideMonth_SplitsTheMonth [O]
       arm 1: Assert.Equal(2, plan.Segments.Count): expected 2, actual 1 (:109); arm 2 [A]
M-41 UserOrganisationBackfillSeederTests.Backfill_ActiveUsersGetOneRowFrom00010101_WithAuditRowAndEvent_InactiveSkipped [O]
       Assert.Equal(1L, auditCount of CREATED by SYSTEM_SEED): expected 1, actual 0 (:83)
     UserOrganisationBackfillSeederTests.Backfill_SecondRun_AddsNothing [O]
       Assert.Equal(rows, audit): expected 21, actual 0 (:120)
M-43 PositionOverrideConcurrencyTests.EmptyKey_ThreeWriters_LateCreateRefusedBeforePredecessor [O]
       Assert.Throws<AppliesFromRefusedException>: no exception was thrown (:329)
M-44 TransferRaceTests.OrganisationChangedUnderTheLock_RequestCarriesOldOrganisation_409_NothingWritten [O]
     TransferRaceTests.OrganisationChangedUnderTheLock_NameOnlyEdit_409_NothingWritten [O]
       both: Assert.Equal(Conflict, status): expected Conflict, actual OK (RunRaceAsync :149)
M-45 OrganisationDriftGuardTests, seven facts, each at AssertDriftRefusedAsync "expected 409, got <status>" [O]:
       AgreementCodeRoute_OrganisationChangedUnderTheLock_409_NothingWritten            got 200
       ProfilePut_OrganisationChangedUnderTheLock_409_NothingWritten                    got 403 (scope check, not 200)
       ProfileClose_OrganisationChangedUnderTheLock_409_NothingWritten                  got 204
       StartDatePut_OrganisationChangedUnderTheLock_409_NothingWritten                  got 200
       EndDatePut_OrganisationChangedUnderTheLock_409_NothingWritten                    got 200
       BareSettlementReversal_OrganisationChangedWhileWaiting_409_NothingWritten_NoGuessedToken  got 200
       ProfilePut_OrganisationChangedWhileWaitingForThePayInputsLock_409_NothingWritten got 200
M-46 OrganisationDriftGuardRoutesTests, nine facts, each Assert.True(status == Conflict) [O]:
       Approve_, Reject_, ReopenLeaderArm_, Send_OnAnothersBehalf_, OvertimePreApprovalCreate_OnAnothersBehalf_,
       OvertimePreApprovalApprove_, OvertimePreApprovalReject_, OvertimeCompensate_, SkemaSave_OnAnothersBehalf_
       (statuses 200; 201 for the pre-approval create)
M-47 OrganisationDriftGuardRoutesTests, thirteen facts, each Assert.True(status == Conflict) [O]:
       TransferAgreementPost_, SettlementResolve_, ReconcilePayout_, TerminationPayoutRequest_, EligibilityPut_,
       BirthDatePut_, ReportingLineAssign_, ReportingLineDelete_ (204), ActingAssign_, ActingDelete_ (204),
       RemovePerson_ (200), WorklistResolve_, UserUnitPut_  (statuses 200 / 201 / 204)
M-48 OvertimePreApprovalDecisionRaceTests.TwoConcurrentDecisions_OneLands_TheOtherIs409_OneEvent [O]
       status set: "expected one 200 and one 409, got 200 / 200"

TOTAL frozen: 41 Docker facts (1 + 1 + 3 + 1 + 2 + 1 + 2 + 7 + 9 + 13 + 1), 0 Unit, 0 DemoSeed, 0 non-Docker Regression.
No smoke or e2e fact is expected red [A].

ARGUED GREEN (a reader might expect red): M-39's shared-date fact VersionDatedOnAKeyChange_KeepsTheKeyChangesCause [O];
M-15's first assertion in EarlierStartedPublishCommittingLater [O]; Transfer_DrawerFutureAgreementCodeDate_200_NoOrganisationRow
under M-11 [O]; OvertimeCompensateRaceTests and the six SelfEntitlementInputRefusalTests facts under M-46/M-47 [O];
SameDayTieBreakTests under M-9 (no archived draft) [A]; the create-route CREATED audit rows under M-41 (actor is the admin) [A].
```

- **Frozen list, CI-M4b** (verbatim from `1834c305`):

```text
FROZEN LIST (rule 2). [O] = seen red on a throwaway local PostgreSQL 16 by the builder, [A] = argued.

Unit 0 reds; DemoSeed 0 reds; Regression Category!=Docker 0 reds.

M-10 SeededConfigsInForceTests.EveryCompiledAgreementOk_HasPublishedVersionInForceFrom00010101 [O]
       "10 of 10 compiled keys lack a published version in force from 0001-01-01" (SeededConfigsInForceTests.cs:52)
M-14 PositionOverrideVersionReadTests.CapEditedFromMonday2November2026_Friday30OctoberReads100_2NovemberReads120 [O]
       first read: Assert.Equal(100, MaxFlexBalance) expected 100 actual 120 (:77)
     PositionOverrideVersionReadTests.DeactivatedFromMonday9November_6NovemberReads120_9thNone [O]
       first read: Assert.NotNull(Friday 6 November) value is null (:108)
     PositionOverrideVersionReadTests.ReactivatedFromMonday16November_15thNone_16thReads120 [O]
       Assert.Null(Sunday 15 November) value is not null (:142)
M-16 SameDayTieBreakTests.EarlierStartedPublishCommittingLater_IsTheOneInForce [O]
       first assertion Assert.True(xStamp > yStamp): "X's published_at ... must be later than Y's" (:151)
M-40 DatedConfigBoundaryTests.VersionDatedOnAKeyChange_KeepsTheKeyChangesCause [O]
       arm 1: Assert.Equal(AgreementCodeChange, cause): actual AgreementConfigPromotion (:166); arm 2 (EmployeeProfileChange) [A]
M-42 EmploymentWindowHandOffTests.EndResavedUnchanged_Tue30Jun2026_JuneExported_200_NoRow [O]
       zero-rows assertion Assert.Empty(rows): Collection was not empty (:254); its 200 and event assertions stay green

TOTAL frozen: 7 Docker facts (1 + 3 + 1 + 1 + 1), 0 Unit, 0 DemoSeed, 0 non-Docker Regression.
No smoke or e2e fact is expected red [A].

ARGUED GREEN (a reader might expect red): under M-16 TwoPublishesSameDayDateOmitted_ReadReturnsTheLaterPublish [O]
(separate transactions, NOW() still orders them); under M-14 OverrideEditedTwiceOnOneDate_ResolvesTheLaterWrite [O]
(the head holds the later write); under M-40 VersionFromMondayInsideMonth_SplitsTheMonth [O] (its versions sit strictly
inside the period); under M-10 every fact that publishes its own versions under unique codes, the shared-date fact of
M-40 included [O for the 14 facts of the five classes run].
```

**Wave 5 briefs, 2026-11-19** (drafted while CI-M4a and CI-M4b run; implementation waits for both). Planning Drafters, Opus 5.5, cite the code at `bb529e62`, whose `src/` equals the CI-W4 head.
- **Batch A:** TASK-14822 (payroll's organisation boundary) and -T, TASK-14823 (the configuration writers' lock and HR hand-off) and -T, and TASK-14824 (the worklist's three Danish labels). Report: `.claude/reviews/S148-wave5A-briefs-planner-report.md`.
- **Batch B:** TASK-14825 (C-1's dated, fail-closed configuration lookup) and -T. Report: `…-wave5B-…`.
- **Main forks for the adjudicator:**
  - one dated-organisation reader, or two (14822 BC-1 together with 14825's organisation lookup);
  - what a screen shows when the configuration cannot be read (14825 BC-4: today all four callers fall back silently to the built-in values);
  - the three Danish labels (14824 BC-1: neither the plan nor the refinement gives them);
  - a pre-existing check-then-act on the local profile's version check (14823 BC-8; the QUAL-257 shape, `(unproven)`).
- **Adjudication of A and B** (Fable `adjudicator`) and the **batch C draft** (the T14b stack: TASK-14826, -T, 14827, 14828, -T, 14829) are running in parallel. Batch C is drafted against batch B's proposed contract and reconciled when the rulings are applied, as wave 4 did with TASK-14815's names. Batch D (14830, -T, 14831, -T, 14846) follows.

**O-23, the wage-type-mapping trace (done, 2026-11-19; `.claude/reviews/S148-O23-wage-type-mapping-trace.md`, `trace`, read-only).**
- **The answer is no.** A today-dated mapping edit committed between payroll's calculate step and its record step cannot change a recorded line.
- **Why:** calculate and record run in one request (`/api/payroll/calculate-and-export`). Each line's wage-type code is fixed at calculate time, from a read dated at the segment's start, and travels to the export in memory. The export never reads the mapping table. Because an edit only closes the old row at today and opens the new one from today, every segment that starts before today keeps its code on recalculation or replay.
- **The one open case, `(unproven)`:** a segment starting today or later. If such a month can be approved and exported, a later recalculation could pick up the new code. The trace did not establish that it can. If it can, the smallest closure is the writer taking the pay-inputs lock together with a mapping check inside TASK-14831's re-derivation.
- **Two corrections to the plan's premise:** the mapping routes are `GlobalAdminOnly`, not HR's; and nothing in `ExportAsync` re-reads the mapping today, so ruling 2.5's "the mapping inside payroll's recheck" would be a new check, not an extension.
- The trace is input to TASK-14831's brief adjudication (batch D).

**Wave-5 batches A and B adjudicated** (`.claude/reviews/S148-wave5AB-briefs-adjudication.md`, Fable 5.2, RULED, no owner question). A planner is applying the rulings to the seven briefs and to the plan ("final + 25"). Most planner leans stand; six things change:
- **One Danish label is corrected:** `EMPLOYMENT_WINDOW_CHANGE` is "Ansættelsesperiode-ændring", not "Ansættelsesdato-ændring". The screens use "ansættelsesdato" for the start date only, and the trigger also covers the end date ("fratrædelsesdato"). The other two labels stand: "Konfigurationsændring" and "Organisationsskift".
- **A race in the local-profile save is fixed inside TASK-14823** (ruling A9; new fact AC-C2u, mutation M-49 at CI-M5).
  - The save reads the profile it replaces before it waits for the lock. If another administrator supersedes that profile during the wait, the audit row and event describe a replacement of the wrong row.
  - TASK-14823 lengthens that wait, so it now refuses that case with its existing 412.
  - A related weakness is registered at O-15, not fixed: the version counter restarts at 1 after a supersession, so a stale version can pass sequentially. The sprint-close C2-R4 census gains this second shape.
- **"Refused by name" holds on every configuration-reading screen** (ruling B4). One Backend mapping answers 503 when the configuration cannot be read and a named 500 (`config_not_in_force`) when none is in force. Without it, Skema and the year overview would answer an anonymous error.
- **The overtime compensation-choice routes ask for the OK version of the day they read** (B12). Paired with the current OK version, a configuration dated at its OK period's start would have refused a whole year.
- **The norm calculator's per-day reads are a registered performance limit** (B6). The year overview goes from a handful of configuration reads to about 780 per request. 14825-T records a timing line, and TASK-14828 owns restoring reuse.
- **PIN-2 keeps both comparisons** (B14), so its independence from QUAL-190 holds.
- **Seams fixed for later batches:** one dated-organisation definition, but three readers, since three different questions are asked (A1: payroll's `IOrganisationHistoryReader` is new). The lookup is `IAgreementConfigLookup`, with `ResolveAsync`, `ResolveCentralAsync` and two named exceptions (B8). Batch C is reconciled with these names.
- **Told to the owner, no decision:**
  - Between TASK-14825 and the T14b stack, payroll splits a month at a dated change but still prices it on the built-in rules. No invariant is traded: the seeded versions all apply from `0001-01-01`, so PIN-1 is unaffected, and nothing exported in that window survives a reseed.
  - The fail-closed error now reaches every configuration-reading screen, with an English sentence; Danish copy for it is not built.
  - The year overview's registered read cost.
  - The registered version-restart weakness.
  - The corrected label.

**CI-M4a and CI-M4b judged, 2026-11-19 (rules 4 and 5): both prove every pin they carry.** Results were read from the trx files by name.
- **CI-M4a** (run 44481246221, draft PR #64, at `5f3f9a72`, headSha confirmed):
  - Regression 2650 passed and **41 failed: exactly the 41 frozen facts**, each at its frozen assertion and line. The other jobs are green: Unit 1757, DemoSeed 170, smoke, E2E, the frontend build, docs, lizard and gitleaks.
  - By mutation: M-9 1 (`PublishedOnlyInForceTests.cs:51`); M-11 1 (`TransferWriteTests.cs:380`, OK expected, BadRequest got); M-15 3 (`SameDayTieBreakTests.cs:40`, `:101`, `:156`); M-39 1 (`DatedConfigBoundaryTests.cs:109`); M-41 2 (`UserOrganisationBackfillSeederTests.cs:83`, `:120`); M-43 1 (`PositionOverrideConcurrencyTests.cs:329`); M-44 2 (`TransferRaceTests.cs:149`); M-45 7 (`OrganisationDriftGuardTests.cs:349`, `AssertDriftRefusedAsync`'s 409 assertion); M-46 9 and M-47 13 (`OrganisationDriftGuardRoutesTests.cs:840`, the same assertion); M-48 1 (`OvertimePreApprovalDecisionRaceTests.cs:147`, "got A(approve)=200 / B(reject)=200").
  - No drift fact failed on the observation helper's "not seen waiting" remark, so each reached its locked read and failed only on the 409, as rulings C2-R1 and C2-R7 require.
- **CI-M4b** (run 44481256823, draft PR #65, at `6fc866ca`, headSha confirmed):
  - Regression 2684 passed and **7 failed: exactly the 7 frozen facts**, each at its frozen assertion and line. All other jobs are green.
  - By mutation: M-10 1 (`SeededConfigsInForceTests.cs:52`, "10 of 10 compiled keys lack a published version"); M-14 3 (`PositionOverrideVersionReadTests.cs:77`, `:108`, `:142`); M-16 1 (`SameDayTieBreakTests.cs:151`); M-40 1 (`DatedConfigBoundaryTests.cs:166`); M-42 1 (`EmploymentWindowHandOffTests.cs:254`).
  - M-10 changed `init.sql` and turned nothing else red, in Regression, smoke or E2E. That settles the builder's `(unproven)` that it might reach other classes.
- **No unlisted red, and no frozen fact green.** So: no census miss, no defect, no re-run. The [A]-argued halves behind a first red assertion stay argued: M-9's predecessor half, M-39's arm 2 and M-40's arm 2.
- **R8 and C2-R5 repeat (CI-M4b):** the eight tier-1 Choreography B facts and AC-C13s are green again, so they are green on two runs.
- **Correction to the CI-M4 build entry above (M-45, the profile PUT).** In CI, `ProfilePut_OrganisationChangedUnderTheLock_409_NothingWritten` answered **200** under M-45, as 14821-T had argued, not the 403 the builder saw on its local cluster.
  - So the logged sentence "the compare is not the only guard on that path: a later scope check also refuses" is **wrong for CI and is withdrawn**. In CI the compare is the guard, and the pin is proved either way.
  - The cause of the local 403 is `(unproven)`; the builder's locally patched `DockerHarness` (one shared cluster) is the likely difference.
- **Status change:** M-9, M-11, M-15 (read half), M-39, M-41, M-43 … M-48 and M-10, M-14, M-16, M-40, M-42 are CI-verified. The SEC-062, SEC-065, QUAL-206 and QUAL-257 cells now read "CI-verified", with the run ids.
- PRs #64 and #65 are closed unmerged. `ci/s148-m4a` and `ci/s148-m4b` are deleted on the remote and locally.
- **Wave 4 is fully closed out:** CI-W4, CI-M4a and CI-M4b are all judged. **Wave 5 is unblocked.**

**Wave-5 batches A and B, rulings applied** (planner, Opus 5.5; `.claude/reviews/S148-wave5AB-briefs-applied-report.md`). The seven briefs are at draft 2, adjudicated, and the plan is at **"final + 25"**: 146 AC ids (+4: AC-C13t, AC-C2t, AC-C2u, AC-C1o) and 46 mutation ids (+M-49, at CI-M5). The counts were made by a script over the saved plan, which first reproduced "final + 24".
- **Orchestrator ruling on T-BC-10** (raised while applying B12): option (b). AC-C1o (2)'s fixture gains an HK / OK24 publish dated Monday 2 February 2026, so its 2026 arm also catches a central-only lookup that ignores its day. No expected value moves.
- **Interpretation calls accepted as the planner held them:** the existing `LocalAgreementProfileRepository.cs` shared-files row was edited in place; "four readers" was kept, with the backfill seeder named as a write-side fifth; the year-overview timing fact's name is the drafter's; and the four additions beyond § 3 that the revision row names are kept.
- **The A9 QUAL ids** (Case A FIXED, Case B open) are assigned at O-15, after the 14823 stack.

**TASK-14822 dispatched** (`payroll-integration`, worktree, base `86e33f5b`; brief at draft 2). This is wave 5's first implementation task.

**Wave-5 batch C drafted** (the T14b stack: TASK-14826, -T, 14827, 14828, -T and 14829; planner, Opus 5.5; report `.claude/reviews/S148-wave5C-briefs-planner-report.md`). The planner rebuilt it against "final + 25" after the A/B rulings landed, so it already carries them.
- **Main item for the adjudicator (14826 BC-11), found, not fixed.** `PositionOverrideConfigs.ApplyOverride` (`PositionOverrideConfigs.cs:63-105`) builds the merged configuration without seven members, so they take their defaults: minimum rest, rest derogation, norm model, annual norm hours, max daily hours, voluntary unsocial hours, and the weekly-max reference period.
  - The seeds are unaffected, because the seeded overrides are AC's and AC uses every default.
  - From the engine switch on, an override on an annual-activity agreement would pay it as a weekly-norm agreement, and an override would reset a configured minimum rest to 11 h.
  - This is concrete evidence for the refinement's open A-10 `(unproven)` NOTE.
- **Other forks:**
  - the engine's fixed 400 sentences (14826 BC-6);
  - replay between the stack and TASK-14830, when frozen pieces carry no configuration (14827 BC-2);
  - payroll's second refusal status (14827 BC-3);
  - the two per-field notes' Danish text (14829 BC-1, which blocks that task).
- **Adjudication of batch C** (Fable `adjudicator`) is running.

**TASK-14822 returned** (`payroll-integration`, Opus 5.5; `ee98e546` and `075bd098` on `86e33f5b`; report `.claude/reviews/S148-TASK-14822-report.md`).
- **What it does:** payroll reads each employee's transfer dates once per calculation through `IOrganisationHistoryReader` (a payroll-local interface with a Postgres reader; ruling A1).
  - A transfer date joins the existing employee-profile boundary source, so a Monday 15 June transfer splits June into 1–14 and 15–30 June, with no new boundary kind.
  - The local-profile activation read and the context pieces cut there too.
  - A service built without the reader refuses every planning call with a fixed message naming the missing registration (ruling A2), before anything is read. The payroll host registers the reader.
  - No payslip changes: pay still uses the built-in values until the T14b stack.
- **Gate:** the build has 0 errors and 125 warnings, with 113 distinct CA2100 (unchanged). DemoSeed 170 and Regression non-Docker 140 are green.
- **Unit has 49 expected reds in 9 files**, all on the fixed message. These are the hand-built services that TASK-14822-T re-points. The implementer also argues 84 Docker facts red for the same reason, CI-pending.
- **Found, not fixed, handed to -T:**
  - `RetroactiveCorrectionManifestTests.TransactionalBlockFailure_…` passes for the wrong reason until the re-point, because it uses `ThrowsAnyAsync<Exception>`.
  - Never-silent facts that match a message substring can be satisfied by another reader's refusal. The implementer's first draft did exactly that, and `075bd098` fixed it.
- **Found, not fixed, below the floor:** stale line cites in two `PeriodCalculationService.cs` remarks (`:394-395` "L72-73"/"L79"; `:150-156` "line 124"/"line 86"). These are for the 5a reviewers to weigh.
- **TASK-14822-T dispatched** (`test-qa`, worktree, base `075bd098`).

**Wave-5 batch C adjudicated** (`.claude/reviews/S148-wave5C-briefs-adjudication.md`, Fable 5.2, RULED, no owner question). A planner is applying it ("final + 26"). Four material changes:
- **The override-merge defect is QUAL-190, fixed inside the stack** (ruling 1).
  - `ApplyOverride` sets 34 of the configuration's 41 members.
  - Beyond the annual-norm case it also drops HK's and PROSA's rest derogation, so an override on an HK position would have turned rest warnings into violations.
  - The fix goes into TASK-14826, the first commit in which the engine trusts what it is handed. A reflection-based fact (new AC-C1p) stops the next omission, and an end-to-end arm (AC-C1f (2)) proves a dated 12 h minimum rest survives an override.
  - No seeded value moves. QUAL-190 is closed at O-17.
- **Replay between the stack and TASK-14830** (ruling 11): the planner's lean is overruled.
  - Reading the organisation from today's employment record would have suspended R42 ("a replay never consults today's profile history") and turned its pin red.
  - Instead TASK-14827 freezes the organisation on each piece now (`FrozenPiece.OrgId`, brought forward from 14830), so replay re-resolves from frozen keys only. R42 holds unchanged.
  - A named limit is recorded, not built around: the interim fingerprint is a set hash, so in that window it cannot see two pieces swapping configurations. No route replays, and TASK-14830 retires the fingerprint.
- **A wire defect nobody had flagged** (ruling 15). Payroll serialises engine requests with a string-enum converter, while the engine host binds enums as numbers. The configuration carries the first enum on that wire, so every payroll calculation would have been refused after the stack.
  - Contract: engine requests carry enums as numbers.
  - Proof before CI: a real-engine control fact (ruling 19, new local mutation M-50). TASK-14828-T's local-cluster run of PIN-1 is now expected, not optional (ruling 22).
- **Exact strings fixed as contract:** ten engine refusal sentences, two payroll bodies (503 `payroll-config-unavailable`; 422 `payroll-config-not-in-force`), the Danish per-field note ("Påvirker endnu ikke lønberegningen. Advarslerne om overarbejde bruger stadig aftalens indbyggede værdi."), and the two Skema warning sentences, which now name the served minimum rest instead of a fixed "11".
- **Told to the owner at O-17, no decision needed:** the interim replay's set-fingerprint limit; one misconfigured agreement blanks a whole Teamoversigt response; Teamoversigt's agreement and OK-version source changes.

**TASK-14822-T returned** (`test-qa`; `a154a19f` on `075bd098`; report `.claude/reviews/S148-TASK-14822-T-report.md`).
- **Facts:**
  - AC-C13b: `TransferPayrollBoundaryTests.TransferMonday15June2026_SplitsJune`, F (CI). It expects two EMPLOYED segments, [1–14] and [15–30 June], each with the cause `EmployeeProfileChange`.
  - An exported June is unchanged by a back-dated transfer: R (CI). Its content hash, both line sets and `exported_at` are compared, after a non-empty guard.
  - AC-C13t: the new institution's profile from 22 June makes a third segment, and a 30 June transfer cuts July's opening-week context piece. Both are F (CI).
  - AC-C13g: `OrganisationHistoryWiringTests.Unwired_RefusesWithFixedMessage`, N (Unit). It asserts the message names `IOrganisationHistoryReader` and the host file, and names no other reader.
- **M-6, local:** with "skip the read when unwired" mutated in, exactly that one Unit fact goes red ("No exception was thrown"). The frozen list is that fact only.
- **Re-points:** 22 hand-built services now pass a fake answering "no transfer dates" (one fake per test project). The census found 3 target-typed `new(...)` sites the brief's grep missed. The never-silent substring facts now also assert the absence of the other readers' names.
- **Gate:** the build is unchanged (125 warnings, 113 CA2100). Unit is 1758 of 1758, so all 49 of 14822's reds are green. DemoSeed 170 and Regression non-Docker 140 are green.
  - The 84 Docker facts are argued green again, and `TransactionalBlockFailure_…` is argued to fail only for its own reason. All of this is CI-pending.
  - The optional local-cluster pre-check was not run.
- **Merge checklist started:** 5α (`constraint-validator`) and 5a Codex (cycle 1, `86e33f5b..a154a19f`) are running. 5a with the `reviewer` (Fable) follows when an agent slot frees. There is no security consult (`PLAN:164`).

**TASK-14822 5α and Codex cycle 1** (`.claude/reviews/S148-TASK-14822-5alpha.md`; `S148-TASK-14822-5a-codex.md`):
- **5α: FAIL, on one confirmed defect.** Two SQL literals in `TransferPayrollBoundaryTests.cs` (`:547`, `:556`) end at `employee_id = ` without `@e`, so the exported-month fact would fail in CI with a syntax error.
  - The Orchestrator verified it byte for byte.
  - The -T agent had split a shared helper into two literal-SQL methods to keep the CA2100 count unchanged, and lost the placeholder.
  - Every other check passes.
  - **Orchestrator ruling on 5α's authorization note:** the three target-typed `new(...)` sites outside the brief's literal census are covered by ruling A2 ("every site that plans passes a reader") and were reported, not hidden. PASS on substance.
- **Codex: CHANGES-REQUIRED, two BLOCKERs and one NOTE.**
  - BLOCKER 1 is the same SQL defect.
  - **BLOCKER 2:** `DatedConfigBoundaryTests.cs:198` builds its own payroll service without the reader. The -T report had said it builds through `TestFixtures.BuildPcs`; it does not. Its two Docker facts, the M-39 and M-40 pins, would throw the wiring refusal at CI-W5a. The Orchestrator verified it.
  - **NOTE:** new test remarks say "CI-verified" for CI-pending facts, and some PCS remarks carry stale numeric line cites (`:153`, `:161`, `:394-395`).
  - Codex found the production change consistent with the contract.
- **5a Fable (cycle 1) dispatched** on the same SHA, told the known items so it can look beyond them. A fix-up follows both lenses.
- **Lesson, carried to the remaining -T briefs:** a census that relies on a report's claim ("builds through the shared fixture") rather than on a grep of every construction site missed a real site. The stack's -T passes must grep every `new PeriodCalculationService(`, every `new(` typed as the service, and every reflection invocation.

**TASK-14822 5a cycle 1 — Fable and the rulings** (`.claude/reviews/S148-TASK-14822-5a-reviewer.md`, Fable 5.2, APPROVED-WITH-WARNINGS; rulings `S148-TASK-14822-5a-rulings-c1.md`, R1–R9).
- **Production change:** both lenses find it correct, with nothing to change in behaviour.
- **New from Fable (WARNING): the exported-month fact (AC-C13b) cannot fail.** It writes the transfer by SQL and re-reads the export record with no product code in between.
  - Ruled R3: record the transfer through the Backend user-edit route on a Backend host, call payroll's `calculate-and-export` for June again, and then assert the record unchanged.
  - Fallback, if one class cannot compose two hosts: the SQL transfer, the payroll call, and a narrowed docstring.
- **Remarks not true now** (R4, R5). In PCS and `IOrganisationHistoryReader`: present-tense "is priced under its own institution's profile" (no paid figure depends on the organisation until the T14b stack), "a split month is refused" (false), "two planning-shape suites" (three), and numeric line cites. Also `EmployeeProfileChange`'s SharedKernel definition, which now also marks a transfer date. All are comment only.
- **Carried:**
  - R8: every later brief that changes PCS's constructor uses a census of every construction form.
  - R9 (unproven): TASK-14828-T pins that a pure-transfer split leaves June's line totals unchanged, on the real engine.
- **Fix-up order:** TASK-14822 fix-up 1 (`payroll-integration`, comment only, R4 and R5) is dispatched on `a154a19f`. TASK-14822-T fix-up 1 (`test-qa`: R1, R2, R3, the test remarks, R6, R7) follows on its tip. Then cycle 2 on both lenses.

**TASK-14822 fix-up 1 returned** (`payroll-integration`; `7f0ef37a`, comment only; report `.claude/reviews/S148-TASK-14822-fix1-report.md`). R4's production remarks and R5's three SharedKernel definitions are corrected. The comment-only check is clean, the build is unchanged and Unit is 1758 green.
- **The agent correctly stopped on one item.** The stale "L72-73"/"L79" cites are inside the window-resolver coupling exception's string, not a comment, so ruling R4 had misdescribed them.
  - Orchestrator commit `a91615d8` (Small Tasks Exception: two string lines): the message now names its registrations by symbol. No test asserts that text.
- **Two low items it flagged are left as true:** `:2264` is explicitly about the code before the task, and `IOrganisationHistoryReader.cs:7` ("each part resolves its own institution's record") holds today.
- **TASK-14822-T fix-up 1 dispatched** (`test-qa`, base `a91615d8`): R1, R2, R3 (the exported-month fact through the route), the test remarks, R6, R7, and the R8 census re-run. **A local-cluster pre-check of the touched Docker facts is expected this time**: the two defects were the kind a single local run would have caught.

**Wave-5 batch C, rulings applied** (planner, Opus 5.5; `.claude/reviews/S148-wave5C-briefs-applied-report.md`).
- **The six briefs are at draft 2, adjudicated, and the plan is at "final + 26"** (`d473fa6d`): 147 AC ids (+AC-C1p) and 47 mutation ids (+M-50, local).
- The 23 ruled strings were checked byte for byte by script.
- **Interpretation calls accepted as held:**
  - S7 and S10's place in the compliance route order is reported by the implementer, since no fact depends on it.
  - Ruling 1's "serializer branch is ruling 22" is transcribed with ruling 19's control arm beside it.
  - Ruling 15's cite of `HttpRuleClassificationProvider.cs:276` is recorded as a brief challenge. That reader accepts both forms, and the ruling stands on the Backend's other engine reads.
  - 14828's "merge equal configs" hint is kept with ruling 13's caution.
  - M-49 is added to 14828-T's CI-M5 list.
- **Orchestrator, in the same commit:** TASK-14828-T's plan row carries the TASK-14822 5a rulings R8 (the census of every construction form) and R9 (a real-engine fact that a pure-transfer split moves no amount).

**Wave-5 batch D drafting** (TASK-14830, -T, 14831, -T, 14846; planner, Opus 5.5), cited at `d473fa6d`.
- The O-23 trace is input to TASK-14831's mapping fork, which goes to the adjudicator. The planner presents it and does not decide it.
- The wave-5 lessons are carried into its prompt: the census of every construction form, R facts that run product code between their readings, and the expected local-cluster pre-check.

**TASK-14822-T fix-up 1 returned** (`test-qa`; `b9f9b0a0`, `0b166b8b` on `a91615d8`; report `.claude/reviews/S148-TASK-14822-T-fix1-report.md`).
- **R1, R2, R4's second bullet, R6 and R7 are done.** R6 went further: the SQL helper now mirrors the route's versions, and says what it does not write.
- **R3 was built as option (a).** The exported-month fact boots a Backend host over the same container, records the transfer through `PUT /api/admin/users/{id}`, and has payroll calculate June again. It asserts the newest manifest has two segments and June's export record is unchanged.
- **The R8 census** confirms every site that plans or calculates passes a reader.
- **Gate:** build 0 errors, 125 warnings, 113 CA2100. Unit 1758, DemoSeed 170 and Regression non-Docker 140 are green.
- **Local pre-check** (a throwaway cluster on port 55432; not CI evidence): the six touched Docker facts are 6 of 6 green.
  - Its first run caught a wrong expectation: the repeat June export answers **409 `payroll-export-conflict`**, not 200. The fact now accepts either.
- **Found by that run, not yet ruled:** `PayrollExportManifest.ComputeContentHash` hashes lines that carry each calculation's own `manifestId`, so on `/calculate-and-export` the "same hash is an idempotent no-op" branch may never fire, and every repeat export of an exported month answers 409. Both cycle-2 lenses are asked to verify it.
- **Cycle 2 dispatched** over `a154a19f..0b166b8b`: Codex and Fable.

**TASK-14822 5a cycle 2** (`.claude/reviews/S148-TASK-14822-5a-codex-c2.md`, APPROVED-WITH-WARNINGS; `S148-TASK-14822-5a-reviewer-c2.md`, Fable 5.2, APPROVED-WITH-WARNINGS; rulings `S148-TASK-14822-5a-rulings-c2.md`, C2-R1 … C2-R5).
- **Every cycle-1 finding is closed as ruled.**
  - The exported-month fact can now fail: both lenses name product changes that turn it red.
  - The two-host fixture is safe.
  - "Manifest +1 with two segments" is the correct reading. The cycle-1 ruling's "manifest counts unchanged" was wrong, and the fix-up rightly departed from it.
- **C2-R1 (Fable WARNING):** only a 409 is correct after the transfer, because the split changes the lines' periods. The fact therefore asserts 409 `payroll-export-conflict` only (TASK-14822-T fix-up 2).
- **C2-R2:** three remarks made true; the one in PCS is an Orchestrator comment commit, `b212a8bf`.
- **C2-R3, census correction:** the fix-up report's "no target-typed `new(` construction exists" is false. Three exist (`EmploymentRecordGapDiagnosticsTests`, `FlexLedgerOpeningTests`, `PcsNeverSilentTests`) and all pass the reader; `PcsConstructionCouplingTests` only constructs. The conclusion, that every planning site has a reader, is confirmed by both lenses' own censuses.
- **C2-R4, the repeat-export limitation, is verified by both lenses.**
  - Each export line carries its calculation's manifest id, and the content hash covers it. So on `/calculate-and-export` a repeat of a month with lines always answers 409 "different content; use a correction", even when nothing changed. The idempotent no-op fires only for an employed zero-line month.
  - It was already recorded in ADR-034 D3 (S146 N-6, "recorded, not changed"), but it has no register row.
  - **Registered at the merge** as an open QUAL row, Medium: no wrong figure and no duplicate, but the idempotency promise is void on the only live export route, and a retried export is told something false.
- **C2-R5 (unproven), carried to TASK-14828-T's plan row:** what a correction of June emits after a pure transfer, i.e. whether the correction diff key includes a line's period.
- **TASK-14822-T fix-up 2 dispatched;** cycle 3 (confirmation on both lenses) follows.

**TASK-14822 + -T merged** (`6fadd1fe`, `--no-ff` of `9143857a`).
- **Cycle 3** (a confirmation pass over `0b166b8b..9143857a`): **Codex APPROVED** (`S148-TASK-14822-5a-codex-c3.md`) and **Fable APPROVED** (`S148-TASK-14822-5a-reviewer-c3.md`).
  - Fable has two optional NOTEs for the next touch of `TransferPayrollBoundaryTests.cs`: parse the 409 body's `kind` with `JsonDocument`, as sibling facts do, and also assert the error text "different content".
- **What it delivers:** payroll knows which institution each day belongs to.
  - A transfer splits the month through the existing employee-profile boundary, so a Monday 15 June transfer gives 1–14 and 15–30 June.
  - The new institution's local profile is asked for its own days, and the opening week's context piece is cut too.
  - A payroll service built without `IOrganisationHistoryReader` refuses by name.
  - No payslip changes until the T14b stack.
- **Review trail:** 5α FAIL on one confirmed test defect, ruled. Codex and Fable ran three cycles, ending APPROVED. Every finding was in the tests or in remarks, and none was in production behaviour. A security consult is not required (`PLAN:164`).
- **Registers:** **QUAL-258** registered, OPEN, Medium (ruling C2-R4). Retrying an unchanged export is told "different content; use a correction", because each line's manifest id enters the content hash. ADR-034's S146 note had recorded this without a register row.
- **CI-pending until CI-W5a:**
  - AC-C13b (now driven through the Backend route and payroll's recalculation);
  - AC-C13t (both cuts);
  - AC-C13a's split half;
  - the 84 re-pointed Docker facts;
  - `DatedConfigBoundaryTests` (the M-39 and M-40 pins);
  - PIN-1 (AC-C13c, argued: no demo person is transferred).
  - AC-C13g is Unit and green, and M-6 was proved locally.
- **Carried:**
  - R8, the census of every construction form, goes to every later brief that changes a payroll constructor.
  - R9 and C2-R5 go to TASK-14828-T's plan row: a real-engine fact that a pure-transfer split moves no amount, and what a correction emits after it.
- **GATE next.**

**GATE green after the TASK-14822 merge** (`6fadd1fe`, paperwork `7df487f7`): `dotnet build StatsTid.sln -m:2 --no-incremental` has 0 errors and 125 warnings, with 113 distinct CA2100 (unchanged). Unit 1758 (+1, AC-C13g); DemoSeed 170; Regression `Category!=Docker` 140. These ran one at a time, in the foreground. The Docker facts are CI-pending until CI-W5a. **TASK-14823 next.**

**TASK-14823 dispatched** (`backend-infrastructure`, worktree, base `5685aa3d`; brief at draft 2). **The wave-5 batch D planner was re-dispatched**: the first attempt was stopped by the Orchestrator at a usage limit, before it had written any file.

**TASK-14823 returned** (`backend-infrastructure`, Opus 5.5; `ed94b085` on `5685aa3d`; report `.claude/reviews/S148-TASK-14823-report.md`).
- **What it does:** a configuration write dated into the past lists the paid months and settled holiday years it reaches on HR's worklist, under the new trigger `CONFIG_PUBLISHED`, once per month and per year. The six writes are the publish, the four override writes and the local-profile PUT.
  - Example: an HK OK24 publish dated Monday 5 January 2026 lists January to April, and April only because April's pay covered 30 March to 5 April.
  - All six take the pay-inputs lock first, at READ COMMITTED; the local-profile write leaves REPEATABLE READ.
  - The local-profile save refuses with its existing 412 when the profile it replaces was superseded during its wait (ruling A9, Case A).
  - Nothing recalculates automatically.
- **Gate:** the build has 0 errors and 125 warnings, with 113 CA2100. Unit 1758, DemoSeed 170 and Regression non-Docker 140 are green.
  - The worklist SELECTs were checked on a throwaway PostgreSQL 16 cluster.
  - The implementer's Docker census expects no red, and smoke, E2E and the demo loader call none of the six routes, so PIN-1 is unaffected.
  - REGEN is expected none.
- **Found, not fixed:** the local-profile PUT never validates `okVersion`. A version string the resolver never returns gives an empty reach, so the worklist lists nothing, silently. The S119 test key `OKS119` is an example. This is for the 5a lenses to weigh.
- **`(unproven)`:** whether a large central publish is fast enough, since it reads per matched employee. TASK-14823-T records the timing figure.
- **TASK-14823-T dispatched** (`test-qa`, base `ed94b085`). It carries TASK-14822's review lessons: a census of every call form, R facts that run product code, assertions that accept only the right answer, and a local-cluster pre-check of its Docker facts, which is expected.

**Wave-5 batch D drafted** (TASK-14830, -T, 14831, -T, 14846; planner, Opus 5.5; report `.claude/reviews/S148-wave5D-briefs-planner-report.md`; draft 1 committed `ced63e9f`).
- **Forks for the adjudicator, all on TASK-14831:**
  - **Reads under the shared lock** (BC-1). Every re-derivation read opens its own connection. The planner argues correctness holds, because no exclusive writer can commit while the shared lock is held, but it couples payroll's pool the way SEC-066 does. Lean: separate connections, plus a dated SEC-066 note.
  - **The wage-type mapping** (BC-2, the O-23 trace). Lean: nothing in the check. Open: whether a month recorded on its own last day can carry a segment starting that day.
  - **The exact 409 sentences** (BC-3), and a recording-time 503 sentence, since TASK-14827's "Nothing was calculated" is untrue once the manifest exists (BC-4).
- **Proposals:**
  - a new local mutation M-51 for TASK-14830-T's round-trip-every-member pin;
  - two comment-only authorizations outside TASK-14830's files;
  - refuse a calculating-route export whose context lacks the recheck (the never-silent rule).
- **TASK-14846's Danish note is already ruled** (`PLAN:371`, `:617`).
- **Adjudication dispatched** (Fable `adjudicator`).

**TASK-14823-T returned** (`test-qa`; `d8ef347b` on `ed94b085`; report `.claude/reviews/S148-TASK-14823-T-report.md`).
- **Facts:**
  - Eight hand-off facts in `ConfigPublishHandOffTests`: AC-C2g, -2f twice, -2j (1) and (2), AC-C2c's hand-off half, AC-C2i, and AC-C2t (two matching intervals give one trigger).
  - AC-C2h's Unit half, which was red on `5685aa3d` as stated.
  - The six row-(2) lock facts in `PayInputsLockCensusTests`, plus AC-C2u (the stale predecessor refused with 412, nothing written).
  - The publish timing fact.
- **M-49, local:** with the route's identity comparison deleted, AC-C2u answers 200, because the stale `If-Match: "1"` passes against R2. The frozen list for CI-M5 is AC-C2u alone.
- **Local pre-check** (a throwaway cluster on port 55432; not CI evidence): all 26 of its Docker facts are green.
- **Measured publish:** 300 people and 1,200 rows took 2,960 ms of wall clock, with the pay-inputs lock held. This is local (`fsync=off`, a Debug build), not CI evidence. The timing line reaches the trx, so CI-W5a can read the figure.
- **Gate:** build 0 errors, 125 warnings, 113 CA2100. Unit 1759, DemoSeed 170 and Regression non-Docker 140 are green.
- **Census:** none of the 14 existing classes that call the six routes can change what it asserts, and no test constructs `SaveProfileResult`.
- **TASK-14824 dispatched** (`ux`, stacked on `d8ef347b`; REGEN none). The three labels are ruled: Konfigurationsændring, Organisationsskift, Ansættelsesperiode-ændring.

**Wave-5 batch D adjudicated** (`.claude/reviews/S148-wave5D-briefs-adjudication.md`, Fable 5.2, **RULED-WITH-QUESTIONS**: one owner question, which does not block dispatch). A planner is applying it ("final + 27").
- **TASK-14831's reads under the shared lock (ruling 1): separate connections.**
  - The adjudicator grepped every writer of every re-derived table. Each takes `(148, 1)` exclusive, or cannot touch a calculated month (the user create, startup seeders, drafts, and the test-only profile overload). So under READ COMMITTED the fresh reads see a stable state, and reading inside the transaction would add nothing.
  - **The cost is availability, not correctness:** a recording holds two pooled connections. As many recordings as the pool has connections (100 by default), in their turn at once, stall about 15 s and fail, writing nothing.
  - It is **registered** (a dated SEC-066 note at the merge, naming the payroll variant and its two known closures), **marked** (one `shortcut(pending)` at the recheck) and **measured** (a fourth TASK-14831-T figure with a pool of 4). No invariant is traded.
- **The wage-type mapping stays out of the check (ruling 2).** The O-23 trace's open case is reachable, but it is not a race, so no lock closes it.
  - **Example:** March has a profile change scheduled for Wednesday 25 March 2026, March is recorded on Friday 20 March, and the mapping is edited on Monday 23 March. The recorded 25–31 March lines keep the old code, and a replay maps them with the new code without refusing.
  - ADR-016 D10 already records this exception. **It goes to the owner** (below).
- **Exact strings as contract:**
  - two 409 `payroll-inputs-changed` sentences. The correction's says "run the correction again", not "calculate the month again", which would send an admin to the route that answers "use a correction".
  - one recording-time 503 sentence, in its own exception type, so TASK-14827's calculate-time arm cannot answer the wrong sentence.
- **New local mutations:**
  - M-51, the round-trip-every-member pin, with two more arms (a default-value round trip, and `normModel` as a JSON number).
  - M-52, AC-PIL-f arm (3). M-7 trips arms (1) and (2) only, which corrects the brief's "(1)–(3)".
- **TASK-14846:** the two carry-forward checkboxes are also disabled on one's own record, and the note uses the `info` variant.
  - The adjudicator also found AC-C13r's facts would pass falsely while the drawer loads, since every input is disabled then. Each case now first waits until `ep-email` is enabled.
- **Other corrections:**
  - comments name "every exclusive taker", never a count (TASK-14832 adds two in wave 6);
  - the recheck's hint signature drops its unused `(conn, tx)`;
  - QUAL-258 interacts as known: an unchanged retry passes the recheck and answers `payroll-export-conflict`, and a dated revisit note is added at the merge.
- **Owner question (asked 2026-11-19):** should the replay promise keep its one known exception for wage-type codes? The options are (a) close it after S148, by storing each line's resolved code in the month's proof (the adjudicator's lean); (b) accept it as a known hole; or (c) let payroll record only finished months.

**Owner ruling, 2026-11-19 (the batch-D adjudication § 2): option (a), close the replay gap for wage-type codes after S148.**
- **The gap:** a month recorded before it ends, with a mapping edit before its tail days, keeps the old codes in its recorded lines. A later replay re-reads the live mapping and shows the new codes without refusing.
- **What the owner chose:** store each line's resolved wage-type code in the month's proof, so a replay reproduces what was recorded. ADR-020 D1 is amended when this is built.
- **Recorded as QUAL-259**, OPEN, Medium, "build in the sprint after S148". Plan O-19 and TASK-14831's "Not built" now cite the answer.
- **What was given up:** nothing in S148. The owner rejected (b), accepting it as a known hole, and (c), recording only finished months.

**Wave-5 batch D, rulings applied** (planner, Opus 5.5; `.claude/reviews/S148-wave5D-briefs-applied-report.md`).
- **The five briefs are at draft 2, and the plan is at "final + 27":** 147 AC ids and 49 mutation ids (+M-51, +M-52, both local).
- **Interpretation calls accepted as held:**
  - "exactly one" `shortcut(pending)` at the recheck, with the implementer reporting its site;
  - the recording-time exception type sits beside `PayrollExportConflictException`;
  - AC-PIL-g (1) moves from M-7's argued list to M-52's;
  - AC-C13r case (4)'s reachability is left to the `ux` agent to report;
  - the adjudication's own cite slips are corrected (`PLAN:100`; `PeriodCalculationService.cs:2105`).

**TASK-14824 returned** (`ux`; `bb72fea1` on `d8ef347b`; report `.claude/reviews/S148-TASK-14824-report.md`).
- The three Danish labels are in, byte for byte as ruled.
- `gen:api` shows no diff, so REGEN is none.
- Three vitest facts are red first, then green.
- `tsc`, lint, `typecheck:e2e` and the build pass, and vitest is 99 files and 1202 tests.
- **The 14823 stack (14823, -T, 14824) is complete. Its merge checklist starts:** 5α on the union, plus 5a with Codex and Fable.

**TASK-14823 stack 5a cycle 1.** 5α PASS (`.claude/reviews/S148-TASK-14823-5alpha.md`). Codex CHANGES-REQUIRED (`…-5a-codex.md`). Fable APPROVED-WITH-WARNINGS (`…-5a-reviewer.md`). Rulings in `…-5a-rulings-c1.md`, R1–R9.
- **Both lenses find the design sound:** no deadlock cycle, READ COMMITTED everywhere, a reach that never under-lists, and once per month and year.
- **R1, the shared finding: the stale-predecessor save answers 400 instead of the ruled 412 in one ordering.**
  - The case: A is dated before the R2 that superseded R1 during A's wait. The repository's backdate guard then throws before the route's identity comparison.
  - **Fix:** the identity check moves into the repository, under the lock and before any date-dependent validation. M-49 is re-pointed there.
  - TASK-14823 fix-up 1 is dispatched on `bb72fea1`.
- **R2–R4, test additions** (TASK-14823-T fix-up 1, next):
  - AC-C2u's missed ordering;
  - a seeded export, so AC-C2u's "no worklist row" line can fail;
  - Unit facts for the reach rule's two unpinned branches (an OK26 change dated before 1 April; an unknown version);
  - a fact that a second write appends to an open row.
- **R6 (Fable W1): a long publish makes other pay-input writers fail after 30 seconds instead of waiting** (Npgsql's default command timeout on the lock statement).
  - At the local rate, about 3,000 people with four paid months reach the cliff (an extrapolation).
  - It is registered at O-15 as an F-row with the CI-W5a figure.
  - TASK-14831's ruled `PayInputsLock.cs` comment edit also corrects "Blocks until granted" (carried into its plan row).
- **R7:** the profile PUT accepts any OK-version string. Both lenses rate it Low and inherited, and validating it would break the deliberate `OKS119` test key.
  - Recorded here, below the register floor. Revisit on any change to the profile PUT's validation, alongside the config and override create routes.
- **R9, `(unproven)`:** months after an OK version ends may depend on the changed months through carried flex balances. This questions refinement A-1's ruled reach, not the code.
