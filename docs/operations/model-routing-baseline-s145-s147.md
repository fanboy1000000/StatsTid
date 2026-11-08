<!-- Appendix of docs/operations/model-routing-register.md ("Ponytail-adoption trial"). Reconstructed 2026-11-08, before S148, by a read-only Sonnet counting agent from .claude/reviews/ (gitignored, on the owner's disk), the S145-S147 sprint logs and the register; reviewed by the Orchestrator. It is the S145-S147 baseline for the S148-S150 trial; do not edit the counts, add dated notes instead. The section "Brief contradictions" is the counting agent's own term for where the records disagreed with its brief or with the register. -->

# Review-cost baseline, S145-S147 (counted from .claude/reviews/, the sprint logs and the routing register)

Read this first. "Cycles" = how many reviews one lens ran on one scope (a chain). "Cycle 1" = the first review of that chain.
B/W/N = BLOCKER / WARNING / NOTE as the lens labelled them. Every number below was counted from a saved artifact or a
sprint-log line; where it could not be, the cell says "not reconstructable" and why. Nothing is estimated.
(F) = Fable internal lens, (O) = Opus stand-in for the internal lens (the file says so), (X) = Codex, the external lens.

Counting conventions actually used (details in "Method notes"):
- Chain = one lens on one scope. A task's Step 5a chain includes its fix-up re-reviews (S147 TASK-14725 is one chain of 6 internal / 8 Codex reviews).
- All-cycle B/W/N counts a finding once, in the cycle it first appeared; I removed only repeats the artifact itself marks ("carried", "still open from cycle N", "remains").
- Verification notes that a lens itself labelled NOTE ("cycle-1 item resolved") are counted as NOTE, because the rule says count by the lens's own label.

---------------------------------------------------------------------------------------------------------------------

## 1. M1 cells: Step 5a and Step 7a x internal/external

### 1a. Step 5a (per task / task stack)

| Sprint | Lens | Chains | Cycles per chain (list) | Total cycles | Mean |
|---|---|---|---|---|---|
| S145 | internal (F 16 chains, O 5 chains) | 21 | 14500:2, 14505:2, 14501-04 batched:1, 14507:3, 14506:3, 14516:2, 14509:3, 14508:3, 14511:2, 14512:2, 14514:1, 14513:2, 14515:1, 14519:2, 14518:1, QUAL-207:2, **(O)** 14520:1, 14522:1, 14524:2, step7a-fix batch:2, 14521:1 | 39 | 1.86 |
| S145 | external (X) | 14 | 14500:2, 14505:2, 14507:3, 14509:3, 14508:3, 14511:2, 14512:2, 14513:2, 14519:3, 14520:1, 14522:1, 14524:2, step7a-fix batch:2, 14521:1 | 29 | 2.07 |
| S146 | internal (F; O on the last cycle of 14610) | 12 | 14600:1, 14601:2, 14602:1, 14603:2, 14604:1, 14605:3, 14606:3, 14607:2, 14608:2, 14609:2, 14610:2 (c2 = O), CI-W2 fix:1 | 22 | 1.83 |
| S146 | external (X) | 8 | 14600:1, 14601:2, 14604:1, 14605:3, 14606:3, 14607:2, 14610:2, CI-W2 fix:1 | 15 | 1.88 |
| S147 | internal (F, except O on 14710, 14712, 14718 c2-5, 14719, 14720) | 23 | 14700:1, 14701:1, 14702:2, 14703:2, 14704:1, 14705:2, 14706:1, 14707:2, 14708:1, 14709:2, 14710:2(O), 14712:2(O), 14713:3, 14715:2, 14716:3, 14718:5 (c1 F, c2-5 O), 14719:2(O), 14720:3(O), 14721:2, 14722:1, 14723:2, 14724:4, 14725:6 | 52 | 2.26 |
| S147 | external (X) | 21 | 14700:1, 14701:1, 14703:2, 14704:1, 14705:2, 14706:1, 14707:2, 14708:1, 14710:2, 14712:2, 14713:3, 14715:2, 14716:3, 14718:5, 14719:2, 14720:3, 14721:2, 14722:1, 14723:2, 14724:4, 14725:8 | 50 | 2.38 |
| **S145-S147 pooled** | internal | 56 | | 113 | **2.02** |
| **S145-S147 pooled** | external | 43 | | 94 | **2.19** |

S145 internal split: Fable-labelled 16 chains / 32 cycles (mean 2.00); Opus-lens 5 chains / 7 cycles (mean 1.40).
S147 internal: Fable 39 reviews, Opus stand-in 13 (matches the register's "52 internal (39 Fable, 13 Opus stand-in)"; 50 Codex also matches).
Tasks with no review chain for a lens (not counted as chains): S145 14510, 14523 (UI, SKIP by plan); S145 Codex "not required" on 9 tasks; S146 14602, 14603, 14608, 14609 have no Codex chain; S147 14702, 14709 have no Codex chain.

Sensitivities (so the owner can see how much a choice moves the mean):
- S145 "batched 5a of 14501-14504" counted as 1 chain (one review covered four tasks). Counted as 4 chains: internal 24 chains / 42 cycles (pooled mean falls to ~1.98).
- S146 internal: the log gives cycle counts for 14605 (3), 14606 (3), 14607 (2), 14608 (2), 14609 (2) and the CI-W2 fix, but several of those cycles have no saved artifact (see 2c). The table uses the log (22). Artifact-only: 16 cycles over 11 chains (mean 1.45).
- S146 "CI-W2 fix" (a post-merge fix to TASK-14606's test, reviewed by both lenses once) counted as its own chain. Folded into 14606 instead (as a 4th cycle of that chain): internal 11 chains / 22 cycles (2.00), Codex 7 chains / 15 cycles (2.14). Dropped altogether: internal 11 / 21 (1.91), Codex 7 / 14 (2.00).
- S147 TASK-14725 counted as one chain per lens. Split into base (c1-c3) and fix-ups (fu2-fu4): internal 24 chains / 52 cycles (2.17); Codex 22 chains / 50 cycles (2.27).

### 1b. Step 7a (sprint-end, whole-sprint)

| Sprint | Lens | Chains | Cycles | Mean |
|---|---|---|---|---|
| S145 | internal (O, all 8 cycles; owner ruling R-104, Fable at limit) | 1 | 8 | 8.0 |
| S145 | external (X) | 1 | 8 | 8.0 |
| S146 | internal (c1 F; c2-c4 O) | 1 | 4 | 4.0 |
| S146 | external (X) | 1 | 4 | 4.0 |
| S147 | internal (F, all 4) | 1 | 4 | 4.0 |
| S147 | external (X) | 1 | 4 | 4.0 |
| **Pooled, 3 chains per lens** | each lens | 3 | 16 | **5.33** |

Extra scoped 7a-type chain: S146 "T7" (post-Step-7a coverage for a hook-file comment change, file names `S146-T7-step7a-*`, log SPRINT-146.md:106): internal F 1 cycle, Codex 1 cycle. I kept it out of the main row (it is a separate scope that the log calls "its own Step 7a cycle"); including it: 4 chains per lens, 17 cycles, mean 4.25.
S145 note: cycles 6-8 ran past the five-cycle cap by owner rulings R-138..R-140 and reviewed the close guard's own test harness (register line 96).

### 1c. Stages that are not M1 cells (reported separately)

| Sprint | Stage | Codex cycles | Internal cycles | Source of the cycle count |
|---|---|---|---|---|
| S145 | refinement (Step 4) | 5 | 5 | SPRINT-145.md:47-53 (artifacts not on disk) |
| S145 | plan (Step 0b) | 5 | 5 | SPRINT-145.md:63-72 (artifacts not on disk) |
| S146 | refinement | 3 | 3 | REFINEMENT-s146-{codex,reviewer}-c1..c3 |
| S146 | plan | 4 | 2 | PLAN-s146-codex-c1..c4, PLAN-s146-reviewer-c1..c2 |
| S147 | refinement | 5 | 4 (c1 = O stand-in) | REFINEMENT-s147-codex-c1..c5, -reviewer-c1..c4 |
| S147 | plan | 3 | 3 | PLAN-s147-codex-c1..c3, -reviewer-c1..c3 |
| S147 (other chains, not stages) | ADR-043 review (decide track) | 3 | 3 | S147-ADR-043-{codex,reviewer}[-c2,-c3] |
| S147 (other chains, not stages) | O-7 docs-owed review | 2 | 2 | S147-O7-{codex,reviewer}[-c2] |

---------------------------------------------------------------------------------------------------------------------

## 2. M2 cycle-1 B/W/N and all-cycle B/W/N

### 2a. Step 5a

Cycle-1 totals are the sum over the chains' first reviews.

| Sprint | Lens | Cycle-1 B / W / N | All-cycle B / W / N | Notes |
|---|---|---|---|---|
| S145 | internal | **B = 2** (14519 pin 6 (F); 14524 QA1/QP1 seed (O)); W/N **not reconstructable** | B = 2; W/N not reconstructable | No saved 5a artifacts exist for S145. The log gives verdict words, not counts. Cycle-1 verdicts: 17 APPROVED-WITH-WARNINGS (so at least 17 W, exact W not recoverable), 2 REQUEST-CHANGES, 2 APPROVED. After cycle 1 every internal verdict is APPROVED or APPROVED-WITH-WARNINGS, so no later blocker. |
| S145 | external | **B = 6** (14507:1, 14513:3, 14519:1, 14524:1); W/N not reconstructable | B = 6; W/N not reconstructable | Cycle-1 verdicts: 9 APPROVE-WITH-WARNINGS, 4 REQUEST-CHANGES, 1 APPROVE. No REQUEST-CHANGES after cycle 1. |
| S146 | internal | 0 / 17 / 61 | 0 / 18 / 81 (artifact cycles only) | 6 later internal reviews have no artifact (14605 c3, 14606 c3, 14607 c2, 14608 c2, 14609 fix-up, CI-W2 fix) so their W/N are **not reconstructable**; cycle-1 for the CI-W2-fix chain also not reconstructable (that chain has no internal cycle-1 artifact; it is excluded from the cycle-1 sum). |
| S146 | external | 2 / 12 / 32 | 3 / 15 / 60 | Cycle-1 blockers: 14605, 14606. Later new blocker: 14605 c2 (H14 census miss). |
| S147 | internal | 4 / 29 / 128 | 5 / 46 / 245 | Cycle-1 blockers: 14705 (F), 14710 (O), 14712 (O), 14718 (F). Later blocker: 14713 c2 (F). |
| S147 | external | 3 / 15 / 26 | 6 / 34 / 62 | Cycle-1 blockers: 14710 (1), 14718 (2). Later blockers: 14713 c2, 14718 c2, 14718 c5. |

Per-chain cycle-1 B/W/N (so the sums can be audited):

S146 internal c1: 14600 0/2/4; 14601 0/2/7; 14602 0/0/6; 14603 0/0/5; 14604 0/2/6; 14605 0/2/6; 14606 0/2/6; 14607 0/1/5; 14608 0/4/8; 14609 0/1/3; 14610 0/1/5.
S146 external c1: 14600 0/0/4; 14601 0/1/6; 14604 0/2/5; 14605 1/2/1; 14606 1/4/3; 14607 0/2/5; 14610 0/0/6; CI-W2 fix 0/1/2.
S147 internal c1: 14700 0/1/6; 14701 0/1/3; 14702 0/2/4; 14703 0/0/6; 14704 0/0/7; 14705 1/2/2; 14706 0/1/7; 14707 0/0/5; 14708 0/1/2; 14709 0/1/6; 14710 1/1/9; 14712 1/3/5; 14713 0/2/5; 14715 0/1/4; 14716 0/1/6; 14718 1/3/3; 14719 0/3/7; 14720 0/1/9; 14721 0/1/7; 14722 0/0/2; 14723 0/1/7; 14724 0/2/7; 14725 0/1/9.
S147 external c1: 14700 0/1/1; 14701 0/0/1; 14703 0/2/1; 14704 0/0/3; 14705 0/0/1; 14706 0/0/3; 14707 0/0/2; 14708 0/0/2; 14710 1/1/0; 14712 0/1/4; 14713 0/1/0; 14715 0/0/1; 14716 0/1/3; 14718 2/3/0; 14719 0/1/0; 14720 0/2/1; 14721 0/0/1; 14722 0/0/0; 14723 0/1/0; 14724 0/0/2; 14725 0/1/0.

### 2b. Step 7a

| Sprint | Lens | Cycle 1 B/W/N | Cycle-by-cycle B/W/N (new findings) | All-cycle B/W/N |
|---|---|---|---|---|
| S145 | internal (O) | 1 / 2 / 10 | c1 1/2/10, c2 0/0/6, c3 0/1/6, c4 0/0/5, c5 0/0/4, c6 0/0/4, c7 0/0/3, c8 0/0/4 | 1 / 3 / 42 |
| S145 | external (X) | 0 / 2 / 2 | c1 0/2/2, c2 1/0/1, c3 0/1/2, c4 1/1/2, c5 0/2/1, c6 0/2/1, c7 0/2/1, c8 0/1/1 | 2 / 11 / 11 |
| S146 | internal (c1 F, c2-4 O) | 0 / 4 / 7 | c1 0/4/7, c2 0/1/6, c3 0/1/8 (W-1 excluded as a repeat), c4 0/0/4 | 0 / 6 / 25 |
| S146 | external (X) | 1 / 1 / 2 | c1 1/1/2, c2 0/0/5, c3 0/2/3, c4 0/0/6 | 1 / 3 / 16 |
| S147 | internal (F) | 0 / 4 / 8 | c1 0/4/8, c2 0/1/7, c3 1/1/4, c4 0/0/5 (its 1 W is "carried from cycle 2 W-1", excluded; the file's own summary says 1 W) | 1 / 6 / 24 |
| S147 | external (X) | 0 / 2 / 2 | c1 0/2/2, c2 0/2/2, c3 0/0/2, c4 0/0/1 | 0 / 4 / 7 |

S146 T7 scoped chain: internal 0/0/3; Codex 0/0/3 (one cycle each; APPROVED / APPROVE).
Every S147 Step-7a external count is the file's own summary line ("0 BLOCKER, 2 WARNING, 2 NOTE").

### 2c. Cycle-1 and all-cycle counts for the non-M1 stages

Refinement (cycle-1 B/W/N; per-cycle new findings by heading count, repeats not removed except where noted):

| Sprint | Lens | Cycle-1 | Per cycle B/W/N | All-cycle |
|---|---|---|---|---|
| S145 | external | "BLOCKER" (count not stated; **not reconstructable**) | log only: c1 BLOCKER (count n/a), c2 NOT-READY, c3 2 B, c4 1 B, c5 0 B | not reconstructable (no artifacts; log gives B only for c3-c5) |
| S145 | internal | NOT-READY (count n/a) | log only: c1 NOT-READY, c2 1 B, c3 1 B, c4 0 B, c5 0 B | not reconstructable |
| S146 | external | 2 / 3 / 0 | c1 2/3/0, c2 2/3/2, c3 0/1/4 | 4 / 7 / 6 |
| S146 | internal | 1 / 7 / 9 | c1 1/7/9, c2 1/3/8, c3 0/5/8 | 2 / 15 / 25 |
| S147 | external | 2 / 2 / 0 | c1 2/2/0, c2 1/3/1, c3 1/2/2, c4 1/0/1, c5 1/2/1 | 6 / 9 / 5 |
| S147 | internal (c1 = O) | 1 / 5 / 9 | c1 1/5/9, c2 1/3/7, c3 0/4/9, c4 0/2/5 | 2 / 14 / 30 |

Plan review (Step 0b):

| Sprint | Lens | Cycle-1 | Per cycle B/W/N | All-cycle |
|---|---|---|---|---|
| S145 | external | 2 B / 6 W / N n/a | log only: c1 2B/6W, c2 2B, c3 2B, c4 READY (0 B), c5 READY | not reconstructable |
| S145 | internal | 0 B / 4 W / N n/a | log only: c1 0B/4W, c2 0B/4W, c3 0B/1W, c4 0B/2W, c5 READY (3 notes) | not reconstructable (N never stated per cycle) |
| S146 | external | 2 / 4 / 3 | c1 2/4/3, c2 1/1/0, c3 0/0/0 (R-35 "partial" is the same blocker, a repeat), c4 0/0/0 | 3 / 5 / 3 |
| S146 | internal | 1 / 9 / 9 | c1 1/9/9, c2 0/2/7 | 1 / 11 / 16 |
| S147 | external | 2 / 2 / 2 | c1 2/2/2, c2 1/1/2, c3 0/1/4 | 3 / 4 / 8 |
| S147 | internal | 0 / 5 / 8 | c1 0/5/8, c2 0/2/4, c3 0/1/5 | 0 / 8 / 17 |

Other S147 chains: ADR-043 review: Codex c1 3/4/1, c2 0/0/0, c3 0/0/0; internal c1 0/5/11, c2 0/1/3, c3 0/0/1. O-7 docs-owed: Codex c1 0/2/1, c2 clean; internal c1 0/1/6, c2 0/0/3.

---------------------------------------------------------------------------------------------------------------------

## 3. M3: CI runs to green and post-close defects (the register's cells, quoted)

Source: docs/operations/model-routing-register.md, rows at lines 96 (S145), 97 (S146), 98 (S147).

- **S145 CI runs to green:** "**1.** `39571040922` on the close `13a40e68`, green on all 7 jobs (Regression 2150). The four Stage B evidence runs on PR #38 are excluded; all four matched on the first attempt"
  **Post-close defects:** "**0** at C-9"
- **S146 CI runs to green:** "**5.** CI-W1 `39992507686` green · CI-W2 `40049157171` red 1/2216 (a test-expectation defect), re-run `40055382782` green · CI-W3 `40294555527` red (frontend lint list; Docker green) · `40300521014` on `c532f004` green on all 7 jobs. C-2 follows the push"
  **Post-close defects:** "**0**"
- **S147 CI runs to green:** "**13 runs, 3 red.** … CI-W1 `40839583193` green. CI-W2 `41060488976` red 8/2353 (test-side faults, TASK-14719) → CI-W2b `41310143330` green. CI-W3 `41335270197`, CI-W3b `41641201066`, CI-W4 `41708962373`, CI-W4b `41825042200`: each green on its first run. **C-2**, the checkpoint on the close candidate: on K `41878946063`, green, Regression 2417/2417 … on K′ `42013444380`, red 1/2430 … on K″ `42024818530` … red 2435/2436 … on K‴ `42036154930`, green 2436/2436. The close push `42065541572` on `e553777e`: green, all seven jobs (Regression 2436/2436). Not counted: `40783740597` … **One 'CI to green' sequence** is the runs one checkpoint needs, on the candidates it gates, until it is green. Read that way, C-2 needed three runs after Step 7a re-opened it (K′, K″, K‴), and every other checkpoint one or two" (cell abridged here; the quoted parts are verbatim)
  **Post-close defects:** "**0** so far (C-5, 2026-11-05)"

M3 reading for the trial: the stated figures are 1, 5 and 13 (mean 6.33), total post-close defects 0. **These three CI figures are not on one definition, so a pooled mean is not safe (see method notes 6 and Brief contradictions 5 and 6).** The post-close total (0) is as the register states it; S147's "so far" means it was only open 0 days past its C-5 note when written.

---------------------------------------------------------------------------------------------------------------------

## 4. Method notes

1. **Files classified and excluded (not reviews).**
   - Adjudications (rulings): PLAN-s146-adjudication-c1/c2, REFINEMENT-s146-adjudication-c1/c2/c3-outbox, S146-D5-ADR043-adjudication, S146-step7a-adjudication-c1, S146-TASK-14605-5a-adjudication, PLAN-s147-adjudication-c1/c2, REFINEMENT-s147-adjudication-c1..c4, RESEARCH-s147-adjudication, S147-ADR-043-adjudication-c1, S147-D-10 / D-batch1..3 adjudications.
   - Constraint Validator "5alpha" files (42 on S147: `S147-TASK-*-5alpha*.md`): a mechanical checklist (PASS/FAIL), not a review lens.
   - Security consults: S147-TASK-14703-security-consult, S147-TASK-14712-security-consult.
   - `.log` files (Codex run transcripts) and `.raw.md` files (the Codex output before summarising; duplicates of the `.md`): S147-TASK-14725-fu4-5a-codex[-c2,-c3].raw.md, SPRINT-147-step7a-codex-c3/c4.raw.md.
   - Final consolidated copies, which duplicate the last cycle: SPRINT-146-step7a-{codex,reviewer}.md (one-line summary of c1-c4) and SPRINT-147-step7a-{codex,reviewer}.md (copy of c4).
   - Waivers: SPRINT-146-review-floor-WAIVED.md. Older sprints' files (S139-S144, REFINEMENT-s140-codex-c3.log): out of scope.
   - **S145:** the only review artifacts on disk are the two Step 7a files `SPRINT-145-step7a-{codex,reviewer}.md`, each holding all eight cycles. No refinement, plan or 5a artifacts for S145 exist in `.claude/reviews/`. S145 refinement, plan and 5a cycle counts therefore come from the sprint log (merge-checklist table, SPRINT-145.md:47-72 and :524-586), and B/W/N for those stages is mostly **not reconstructable**.
2. **Included as reviews:** `S14N-TASK-*-5a-{reviewer,codex}[-cK].md`, `…-fu2/fu3/fu4-5a-…`, `S146-TASK-14601-fixup-*`, `S146-TASK-14603-followup-reviewer`, `S146-TASK-14606-ciw2-fixup-codex`, `S146-T7-step7a-*`, `S146/S147 SPRINT-…-step7a-{reviewer,codex}-cK`, `PLAN-s14N-*`, `REFINEMENT-s14N-*`, `S147-ADR-043-*`, `S147-O7-*`.
3. **Label mappings made.**
   - Codex used BLOCKER / WARNING / NOTE (or its own "verdict: FINDINGS (x BLOCKER, y WARNING, z NOTE)" summary line, used directly for all S147 Codex 5a and 7a files). **No P1/P2/P3 labels exist in any in-scope Codex artifact: 0 mapped.**
   - Codex S146 files carry no summary line; I counted headings (the bullet lines starting BLOCKER / WARNING / NOTE). S146 refinement Codex c2 and c3 also contain "B1 / W1 / W3 …" in "Part 1" lines: those restate cycle-1/2 findings with a RESOLVED/PARTIAL status, so they are repeats and were not counted.
   - Internal (Fable/Opus) files use `B-1/W-1/N-1`, `W1/N1`, `WARNING 1/NOTE 1`, `W3-1`, `N-A` forms: mapped directly (B→BLOCKER, W→WARNING, N→NOTE); about 25 files use shorthand. A few condensed artifacts hide a blocker inside a paragraph rather than a heading (S147 7a c3: "BLOCKER-1"); I read those paragraphs.
   - S145 5a (log only): verdict words mapped: REQUEST-CHANGES → at least 1 BLOCKER; APPROVED/APPROVE → 0 B, 0 W; APPROVED-WITH-WARNINGS/APPROVE-WITH-WARNINGS → 0 B, at least 1 W. The blocker counts (14507: 1; 14513: 3; 14519: 1 each lens; 14524: 1 each lens) are the log's own words ("one BLOCKER", "three blockers", "B-1 (BLOCKER, both lenses)").
   - S145 7a internal "NOTE N-A..N-F", S146 7a "N-A..N-D": counted as NOTE.
4. **Repeats removed (rule 3), the only ones applied:** S147 internal 7a c4 W (carried from c2 W-1); S147 14710 c2 N-5 and 14713 c2 N-8 (marked open/handed on from cycle 1); S147 14725 fu3 W-1 (the SPRINT-147.md:256 records drift, first raised as fu2 NOTE-3: counted once, as the fu2 NOTE; the fu3 file labels it WARNING); S146 7a internal c3 W-1 (partly closed from c2); S147 Codex 14718 c3 test-timing W (cycle-2's warning, "rewording does not resolve"), 14716 c2 W, 14725 c2 W, 14725 fu4 c2 W (each "remains/still"); S146 plan Codex c3 (R-35 partial). Repeats that are less explicit (for example, 14718 Codex cycles 3-5 raise related save/lock themes under new labels) were counted as new. This only affects all-cycle totals, never cycle-1 totals.
5. **Judgement calls on scope.**
   - S146 TASK-14606 internal c1 is CHANGES-REQUESTED because it ratified Codex's blocker ("Agree, BLOCKER"). I counted the lens's own findings (0 B / 2 W / 6 N); the log says "Internal blockers: none" (register line 97). If ratified findings are counted as the internal lens's own, cycle-1 internal would be 1 B.
   - S147 14725 and S145 batched 5a: see sensitivities in 1a.
   - S146 5a: where the log gives more cycles than artifacts exist, the cycle counts follow the log (verdict sequence recorded in the merge tables, SPRINT-146.md:131-290, :344, :405), B/W/N follow artifacts only.
6. **Counts that could not be made.**
   - S145: 5a W/N (cycle 1 and all), refinement and plan B/W/N beyond the log's cells, 7a is complete. Reason: no artifacts, log gives verdicts.
   - S146 internal 5a: findings of 14605 c3, 14606 c3, 14607 c2, 14608 c2, 14609 fix-up, the CI-W2-fix review (no artifact; verdicts only: APPROVED, APPROVED, APPROVED-WITH-WARNINGS (c2 of 14607), APPROVED, APPROVED, APPROVED). 14609's cycle-1 artifact is a transcript recovery with no model line.
   - M3: CI runs to green on a common definition for S145 (the register counts only the close push; the log also records a green Stage A run `38747749039` (SPRINT-145.md:6) and others, and the records do not list every S145 push run). Not reconstructable without the GitHub run list.
   - Token cost: not counted (the brief asks for cycles and findings only).
7. **Arithmetic check:** internal 5a artifacts on S147 = 52 files (matches the register's 52); Codex 5a artifacts on S147 = 50 (matches); the per-chain cycle lists above sum to those totals.

---------------------------------------------------------------------------------------------------------------------

## 5. Brief contradictions (claim | what the record shows | file:line)

1. **Claim:** sprint-log Step 5a task entries carry "Reviewer Audit" / "External Review (Codex)" fields with cycles and B/W/N counts. **Record:** neither phrase occurs in SPRINT-145/146/147.md (0 matches each). The logs record 5a as merge-checklist table columns "5a Fable" / "5a Codex" (verdict words and fix-up commits only, e.g. SPRINT-145.md:524-586, SPRINT-146.md:131, :167, :254, :285) plus narrative. B/W/N counts exist only in artifacts, and only for S146-S147.
2. **Claim:** review artifacts named `S14N-TASK-<id>-5a-…` exist for S145-S147. **Record:** none exist for S145; only `SPRINT-145-step7a-{codex,reviewer}.md` (.claude/reviews directory listing). S145 refinement, plan and 5a artifacts are absent.
3. **Claim:** Codex "sometimes writes P1/P2 or W1/NOTE-3". **Record:** no P1/P2 in any in-scope Codex file; "B1/W1/W3" appear only in the "Part 1" status lines of REFINEMENT-s146-codex-c2.md and -c3.md (repeats, not findings).
4. **Register S145 under-states Codex blockers.** Claim: "Codex blockers on 3 (14507; 14513 ×3; 14519)" (model-routing-register.md:96). Record: Codex also returned REQUEST-CHANGES with the same blocker on TASK-14524 (SPRINT-145.md:549, :2398-2403 "Both lenses returned REQUEST-CHANGES on the same blocker"). Four Codex chains had a cycle-1 blocker (6 blockers), not three.
5. **Register S146: "Codex blockers on … the CI-W2 fix"** (model-routing-register.md:97). Record: `S146-TASK-14606-ciw2-fixup-codex.md` is APPROVE-WITH-WARNINGS, 0 BLOCKER / 1 WARNING / 2 NOTE, and the log agrees (SPRINT-146.md:405 "Codex APPROVE-WITH-WARNINGS (folded in), Fable APPROVED"). The blocker was a CI failure, not a Codex finding.
6. **Register S146: "1 of 11 tasks' reviews not persisted (14609's fix-up)"** (model-routing-register.md:97). Record: besides 14609's fix-up, no artifact exists for 14605's scoped cycle 3, 14606's cycle 3, 14607's cycle 2, 14608's cycle 2 or the CI-W2 fix's internal review, although the log records a verdict for each (SPRINT-146.md:131-290, :344, :405). They were saved only as verdict lines.
7. **Register S147: "13 runs, 3 red"** (model-routing-register.md:98). Record: the cell lists 12 run ids (CI-W1; CI-W2, CI-W2b; CI-W3, W3b, W4, W4b; C-2 on K, K′, K″, K‴; the close push). The three reds (CI-W2, K′, K″) check out; the total of 13 does not match the 12 ids. (The cell's own exclusion, `40783740597`, is a 13th id it says it does not count.)
8. **S145's "1" CI run is not comparable.** The register counts only the close push `39571040922` for S145 while S146 and S147 count every run to green. The S145 log records another green run at the Stage A gate (`38747749039`, SPRINT-145.md:6) that the cell does not count.
9. **S147 7a cycle 4, internal warning.** The file's summary says "0 BLOCKER, 1 WARNING, 5 NOTE" (SPRINT-147-step7a-reviewer-c4.md:7) and the register copies that; the warning is "carried from cycle 2 W-1" (:8), so under the "count a repeat once" rule it is not a new finding (I counted 0 new W in cycle 4). Not an error in the record, but the register's per-cycle W series (4,1,1,1) and a rule-3 series (4,1,1,0) differ.

---------------------------------------------------------------------------------------------------------------------

## 6. Plain-language summary

Across the three sprints, each review pass on one piece of work (a "chain") took on average about two rounds per task for both the in-house reviewer and the external reviewer (Codex): 2.0 rounds for the internal lens and 2.2 for Codex at the per-task stage, and about 5.3 rounds for the end-of-sprint review, though that last figure is dominated by S145's eight rounds, which mostly reviewed the close guard's own test harness, not the product. The per-task rounds rose from S145 (1.9 / 2.1) to S147 (2.3 / 2.4) as S147 had longer chains (TASK-14718: five rounds each lens; TASK-14725: six and eight). The first round of a task usually finds a lot of small notes and a few warnings but rarely a blocker: across S146-S147, 4 of 34 internal first rounds and 5 of 29 Codex first rounds found a blocker, and S145's records show 2 of 21 and 4 of 14. The CI and defect picture is clean on post-close defects (0 in all three sprints) but the CI-runs-to-green numbers (1, 5, 13) were counted by different rules and should not be averaged without first restating S145 and S147 on the same footing. Three things the PM should know: S145's per-task and planning review detail was never saved to disk (only the sprint log's verdict words survive), so some baseline cells are marked "not reconstructable"; several S146 re-reviews exist only as verdict lines; and the register contains a few small inconsistencies (Codex blockers on S145's TASK-14524, the S147 run count of 13 versus 12 listed) that the trial should correct before it compares against these numbers.

**Dated correction (2026-11-08, Orchestrator, after review):** the summary above says "5 of 29 Codex first rounds found a blocker" for S146–S147. Counted from §2a it is **4 of 29 chains** (14605, 14606, 14710, 14718) holding **5 blockers**; with S145's 4 of 14, the Codex total is **8 of 43 chains (11 blockers)**. The internal figures (4 of 34; 2 of 21) stand. The counts in §1–§2 are unchanged.
