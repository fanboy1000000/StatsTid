# C-1d — TASK-14725 fix-ups 2–3 (Step 7a cycle 2): apply-ready records

| | |
|---|---|
| **What this is** | The exact text for the records of S147 Step 7a cycle 2 and its code fix, TASK-14725 fix-ups 2 and 3 (merged `ab77c475`). Sources: the cycle-2 reviews (`.claude/reviews/SPRINT-147-step7a-{codex,reviewer}-c2.md`); the fix-ups' reviews (`S147-TASK-14725-fu2-*`, `-fu3-*`), whose Fable fix-up 3 review lists the records owed; and the Orchestrator's brief. The Orchestrator applies each block as a string replacement, into K″, the final candidate. The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` |
| **Status** | DRAFT 1 (`planner`, Opus 5.5), 2026-11-05 |
| **Base commit** | `ab77c475` (local master). Every Old text below is copied from `ab77c475`. A script counted each one as an exact substring over the whole target file, in the commit's blob and in the working copy, and each count is 1. Every line number, docs and code, is at `ab77c475` |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **Timing** | Apply as K″, before C-2 runs on K″. The C-2 placeholders stay literal until C-2 on the final candidate is green |
| **Owner approval** | None needed. No block touches `SYSTEM_TARGET.md` |

## What this is, in plain words

Every approval and rejection records *how* the person deciding was entitled to decide: as the employee's manager, as a unit leader, or through HR's organisation scope (`approval_periods.approval_method`). TASK-14725 made a leaver's manager lose authority on day 6 by date. Step 7a's second cycle (Codex WARNING 1) found that the record had not caught up. An HR user who also happens to be the leaver's manager is admitted through HR scope from day 6, yet the record still said "the manager". The decision was right; its recorded reason was not.
- **Fix-up 2** copied the date rule into the label.
- Its review (Fable WARNING-1) found one more case: a person switched off by hand with no end date.
- **Fix-up 3** takes the label straight from the authority check's own answer. A manager label is recorded only when that check admits the person as a manager, which covers every case, now and later.

The refusal rule did not change. No one gains or loses access.

The records follow:
- **ADR-040's Step 7a note** gains:
  - the label rule;
  - why the manager's id is still recorded beside an HR label;
  - one conservative residual;
  - how to read the refusal log line;
  - the six label facts;
  - its code lines refreshed to `ab77c475` (one was off by one since C-1c).
- **`SECURITY.md`** gains the label rule and the log-reading sentence.
- **SEC-058** gains the label's pins, and **SEC-057** its leader arms' new lines.
- **The sprint log:**
  - a dated note that the validator now logs at Information;
  - the merge record of fix-ups 2 and 3;
  - the ledger row and the plain-words record;
  - Step 7a cycle 2's record with every finding's disposition;
  - Phase and Next action, with K″ as the final candidate.

Rules kept:
- Nothing is described as built unless it is merged, and everything here is.
- CI-verified only with a run id: the six new Docker facts are CI-pending (C-2).
- Earlier records get dated notes. The handoff block and `SECURITY.md` describe current state, so they are updated, each with an S147 Step 7a cycle 2 marker.

## How to apply

- Each block names its **Target**, the **Old text** (one exact match at `ab77c475`) and the **New text**. Where a block has parts, apply each part as its own replacement.
- No two replacements in one file overlap. Disjoint substrings of one line: 3a and 3b are in different rows. 1f and 1g are two adjacent lines of ADR-040's "Pinned by" sentence.
- **Line endings:** every target is LF in the blob and in the working copy (a carriage-return count of 0): ADR-040, `docs/SECURITY.md`, `docs/operations/security-finding-register.md`, `docs/sprints/SPRINT-147.md`. Multi-line New texts (1d, 1e, 4e) are LF.
- **Placeholders:** none new. `<K sha>` now means K″'s sha (C-1's rule: the final candidate's), and the existing `<C-2 run id>`, `<C-2 date>` and `<7a cycles>` stay as they are. They occur in 1f (`<C-2 run id>`), 4e (`<7a cycles>`), 4f and 4h (`<K sha>`), and 4g (`<K sha>`, `<C-2 run id>`, `<C-2 date>`).
- **Blocks 1–4 are owed.** Performance F7 is not changed: its row stays true (see "Not drafted here").

## Map: each owed item → its block

| # | Owed item (source) | Block | State at `ab77c475` |
|---|---|---|---|
| 1 | ADR-040's Step 7a note: the label rule; `designated_approver_id` deliberate (Fable fu2 NOTE-5); the step-(3) residual (Fable fu3 NOTE-1; Codex fu3 note on `:249`, `:258`, `:553`); the log names the refused path (Fable fu3 NOTE-2); the label facts in "Pinned by"; the lines refreshed | 1a (lines "at `ab77c475`"), 1b (`:151-152`), 1c (`:2513-2520`), 1d (the label bullet), 1e (two residuals), 1f, 1g ("Pinned by") | the note predates fix-ups 2–3; its lines are "at `0a64c1eb`" |
| 2 | `docs/SECURITY.md`: the log-reading sentence; any label reference | 2 | no label or log-reading text |
| 3 | SEC-058: the label pins | 3a; plus 3b (SEC-057's leader arms, now `:446-457`, `:736-746`, `:1964-1976`) | pins without the label facts |
| 4 | The sprint log: `:256` (Information, not WARNING; Fable fu3 W-1); the merge record; the ledger row; the plain-words sentence; Step 7a cycle 2; Phase and Next action | 4a (`:256`), 4b (`:180`), 4c (`:112`), 4d (`:247`), 4e (`:565`), 4f (`:578`), 4g (`:581`); plus 4h (As of, `:571`), 4i (Status, `:5`), 4j (C-2's Commit row, `:528`), 4k (C-2's first-run row, `:531`) | as at K′ |
| 5 | Performance F7: only if the row is now untrue | none | still true |

**Count: 21 replacements** (1a–1g, 7; 2, 1; 3a, 3b, 2; 4a–4k, 11).

---

## Block 1 — ADR-040's Step 7a note: the recorded approval method; lines at `ab77c475`

**Target:** `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` (LF; the note "S147 Step 7a, 2026-11-05 (TASK-14725)", lines 509–572). Seven parts. Every code line the note cites was re-checked at `ab77c475`. `DesignatedApproverAuthorizer.cs` is byte-identical to `0a64c1eb`, and `OrgScopeValidator.cs` changed only after its line 602. Two lines change: the departed-self clause was cited one line early (since C-1c), and the send's refusal moved with fix-ups 2–3.

### 1a — the note's line basis (line 519)

Old text:

````text
Code lines below are at `0a64c1eb`.
````

New text:

````text
Code lines below are at `ab77c475` (re-checked at Step 7a cycle 2).
````

### 1b — the departed-self clause's line (line 533)

Old text:

````text
The departed-self clause still runs first (`:150-151`, `:339-340`).
````

New text:

````text
The departed-self clause still runs first (`:151-152`, `:339-340`).
````

### 1c — the send's refusal of a month with no employed day (line 556)

Old text:

````text
(`ApprovalEndpoints.cs:2473-2480`)
````

New text:

````text
(`ApprovalEndpoints.cs:2513-2520`)
````

### 1d — the recorded approval method (a new bullet, before "Residuals", line 548)

Old text:

````text
- **Residuals recorded, not fixed.**
````

New text:

````text
- **The recorded approval method follows the same predicate (Step 7a cycle 2, Codex WARNING 1; TASK-14725
  fix-ups 2 and 3, merged `ab77c475`).** Every approve and reject records how the decider was entitled
  (`approval_periods.approval_method`). Until fix-up 2 the label was derived without asking about the subject. So an
  HR actor admitted through HR scope who also held the manager or unit-leader position was recorded as the manager
  from D+6.
  - **How it works now.** Each handler evaluates the authority predicate once, under the held locks, for every actor
    (`ApprovalEndpoints.cs:539-542` approve, `:785-788` reject). That one answer both refuses a manager the
    org-scope arm did not admit and names the label: a manager or unit-leader label only when the predicate admits,
    otherwise `ORG_SCOPE_FALLBACK` (`DeriveApprovalMethodAsync`, step (0), `:240-241`).
  - **The cases.** An HR actor who also holds a manager position records `ORG_SCOPE_FALLBACK` from D+6 by date,
    after the switch-off, and for a subject switched off by hand with no end date (the predicate's active-user
    re-check; Fable's fix-up 2 WARNING-1).
  - **Why two fix-ups.** Fix-up 2 copied only the date clause into the label; fix-up 3 replaced that copy with the
    predicate's own answer, which covers every clause.
  - **What did not change.** The refusal rule (`!orgScopeAllowed && !holdsManagerPath`), and
    `DesignatedApproverAuthorizer.cs`, which is as TASK-14725 left it. When the predicate admits, the labels are as
    before: `DESIGNATED_MANAGER` / `ACTING_MANAGER` on the edge, `UNIT_LEADER` / `UNIT_LEADER_VIKAR` on the
    unit-leader path.
  - **`designated_approver_id` still names the manager the reporting line resolves, by design.** It records *who*
    the line names; the method records *how* the decider was entitled; and no reader infers the method from the id
    pair (Fable's fix-up 2 NOTE-5).
  - **Cost.** For HR this is one more predicate evaluation per approve or reject (Fable's fix-up 3 NOTE-6; no F-row
    needed).
- **Residuals recorded, not fixed.**
````

### 1e — two residuals (before the F7 bullet, line 568)

Old text:

````text
- **One extra read per admitted manager–employee pair** on my-reports and Teamoversigt: performance register F7.
````

New text:

````text
- **The label's step (3).** When the predicate admits but neither the re-resolved edge nor the unit-leader
    resolution names the actor, the label is `ORG_SCOPE_FALLBACK` (`ApprovalEndpoints.cs:256-259`). Which manager
    label applies comes from two further reads after the predicate: the resolver (`:553-554`) and the unit-leader
    kind (`:249-250`). State outside the held advisories, such as a role expiry or a revocation, can move between
    them. The answer then errs toward the fallback, never toward a manager label the reads do not support (Fable's
    fix-up 3 NOTE-1; Codex's fix-up 3 notes).
  - **Reading the log.** The predicate's `manager-grace-ended` line (`DesignatedApproverAuthorizer.cs:404-407`,
    Information) names a refused manager *path*, not the request's outcome. An HR actor who also holds that path is
    admitted through HR scope, so the line can sit beside a 200 (Fable's fix-up 3 NOTE-2). The validator's
    `past-grace-as-switched-off` line is Information since fix-up 2 (`OrgScopeValidator.cs:622`; Step 7a cycle 2,
    Fable N-4).
  - **One extra read per admitted manager–employee pair** on my-reports and Teamoversigt: performance register F7.
````

### 1f — "Pinned by": the label facts (line 572)

Old text:

````text
has D+5 boundary pins. The Docker facts are CI-pending until C-2 (`<C-2 run id>`).
````

New text:

````text
has D+5 boundary pins. Six more facts in `LeaverPastGraceByDateTests` pin the recorded approval method (fix-ups
  2 and 3):
  - `Day6_LeaverStillActive_HrWhoIsDesignatedManagerApproves_MethodIsOrgScopeFallback` (`:347`);
  - `Day6_LeaverStillActive_HrWhoIsUnitLeaderRejects_MethodIsOrgScopeFallback` (`:372`);
  - `Day5_LeaverStillActive_HrWhoIsDesignatedManagerApproves_MethodStaysDesignatedManager_BoundaryPin` (`:394`);
  - `Day6_LeaverSwitchedOff_HrWhoIsDesignatedManagerApproves_MethodIsOrgScopeFallback` (`:418`);
  - `Day6_LeaverStillActive_ManagerVikarStandIn_Is403_Day5Is200` (`:450`: the stand-in refused at D+6, and at D+5
    recorded as `ACTING_MANAGER`);
  - `ManuallySwitchedOff_NoEndDate_HrWhoIsDesignatedManagerApproves_MethodIsOrgScopeFallback` (`:500`).

  The Docker facts are CI-pending until C-2 (`<C-2 run id>`).
````

### 1g — "Each Docker fact keeps the leaver active" is now true of the access facts only (line 571)

*Why:* two of the new label facts are about a leaver already switched off (`:418`, `:500`).

Old text:

````text
Each Docker fact keeps the leaver active at D+6, with the leaver job off, and
````

New text:

````text
Each access fact keeps the leaver active at D+6, with the leaver job off, and
````

## Block 2 — `docs/SECURITY.md`: the recorded method, and how to read the refusal log line

**Target:** `docs/SECURITY.md` (LF; line 146, in "HR only from day 6"). `SECURITY.md` had no reference to the recorded method or to the predicate's log line; both go here, beside the sentence on how these decisions are audited.

Old text:

````text
and they audit against the subject's own organisation.
````

New text:

````text
and they audit against the subject's own organisation. *(S147 Step 7a cycle 2, TASK-14725 fix-ups 2 and 3: the decision records its approval method from the same in-lock answer of the shared approver predicate that decides a manager's admission. A manager or unit-leader label is recorded only when that predicate admits; otherwise `ORG_SCOPE_FALLBACK`. So an HR actor who also holds a manager position records `ORG_SCOPE_FALLBACK` from day 6, after the switch-off, and for a person switched off by hand. **Reading the logs:** the predicate's `manager-grace-ended` line names a refused manager path, not the request's outcome. An HR actor who also holds that path is admitted through HR scope, so the line can sit beside a 200.)*
````

## Block 3 — The security register: SEC-058's label pins; SEC-057's leader arms

**Target:** `docs/operations/security-finding-register.md` (LF). Two parts in two rows: 3a in SEC-058 (line 218), 3b in SEC-057 (line 217).

### 3a — SEC-058: the label's pins

Old text:

````text
pins `ManagerGracePredicateTests` (Unit, mutation M-26), `LeaverPastGraceByDateTests`, `DepartedSelfValidatorTests` (the four D+5/D+6 facts), `PayrollPastGraceLeaverScopeTests`
````

New text:

````text
pins `ManagerGracePredicateTests` (Unit, mutation M-26), `LeaverPastGraceByDateTests`, `DepartedSelfValidatorTests` (the four D+5/D+6 facts), `PayrollPastGraceLeaverScopeTests`; and the recorded approval method (TASK-14725 fix-ups 2 and 3, merged `ab77c475`; Step 7a cycle 2, Codex WARNING 1): `ApprovalEndpoints.cs:240-241`, `:539-542`, `:785-788` at `ab77c475`, pinned by six facts in `LeaverPastGraceByDateTests` (`:347`, `:372`, `:394`, `:418`, `:450`, `:500`)
````

### 3b — SEC-057: the leader arms' lines after fix-ups 2 and 3

Old text:

````text
since TASK-14725, merged `2cfae13c`: `:417-428`, `:699-709`, `:1924-1936`)
````

New text:

````text
since TASK-14725, merged `2cfae13c`: `:417-428`, `:699-709`, `:1924-1936`; since its fix-ups 2 and 3, merged `ab77c475`: `:446-457`, `:736-746`, `:1964-1976`)
````

## Block 4 — The sprint log

**Target:** `docs/sprints/SPRINT-147.md` (LF). Eleven parts. 4a–4g are the brief's items; 4h–4k keep the log consistent with them.

### 4a — the 5α observation on the log level (line 256; Fable fix-up 3 WARNING-1)

*Why a note:* "at WARNING … none changed" was true at the merge of `2cfae13c`; the line is a record, so it gains a dated note rather than a rewrite.

Old text:

````text
The validator logs `past-grace-as-switched-off` at WARNING, which may be noisy. All three recorded, none changed.
````

New text:

````text
The validator logs `past-grace-as-switched-off` at WARNING, which may be noisy. All three recorded, none changed. *(Step 7a cycle 2: fix-up 2 changed the third. The validator now logs `past-grace-as-switched-off` at Information (`OrgScopeValidator.cs:622`; Fable N-4). The first two stand. The logger point keeps its reason: the Backend's authorizer comes from DI with a logger (`Program.cs:179`), and the logger-less fallback at `ApprovalPeriodRepository.cs:70` serves only the Payroll host, which calls none of the three list reads (Fable's fix-up 2 NOTE-2).)*
````

### 4b — the merge record: fix-ups 2 and 3 (after line 180)

Old text:

````text
→ c3 CLEAN` | `2cfae13c` |
````

New text:

````text
→ c3 CLEAN` | `2cfae13c` |
| 14725 fix-ups 2–3 + pins (Step 7a cycle 2, Codex WARNING 1; fix-up 2's review, Fable WARNING-1), + Orchestrator nits `530dfc47` | `530dfc47` | GATE at `ab77c475`: build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135. Fix-up 2 (`6999d5f8`, pins `d997ee12`) and fix-up 3 (`d145f85a`, pins `5d6bd1dd`) each gated the same on the branch. REGEN: not needed (no route or DTO change) | fix-up 2 PASS; fix-up 3 PASS (run after the merge on `d997ee12..530dfc47`, 0 failures; two cosmetic observations) | **Fable** fix-up 2 READY-TO-MERGE (APPROVED-WITH-WARNINGS: 1 W, a person switched off by hand with no end date kept the manager label, fixed by fix-up 3; 5 N) → fix-up 3 READY-TO-MERGE (APPROVED-WITH-WARNINGS: 1 W, records only, this log's "at WARNING" line, 4a of C-1d; 6 N) | fix-up 2 0 B / 0 W / 5 N → fix-up 3 0 B / 0 W / 6 N | `ab77c475` |
````

### 4c — the TASK-14725 ledger row (line 112)

Old text:

````text
(fix-up 1, owner "(a) Extend now"); SEC-058 | **DONE** (merged `2cfae13c`; GATE `build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135`);
````

New text:

````text
(fix-up 1, owner "(a) Extend now"); SEC-058; the recorded approval method follows the same predicate (fix-ups 2–3, Step 7a cycle 2) | **DONE** (merged `2cfae13c`, fix-ups 2–3 merged `ab77c475`; GATE after each `build 0 / 144 / CA2100 114; Unit 1722; DemoSeed 170; Regression non-Docker 135`);
````

### 4d — "In plain words (TASK-14725)": the audit label (line 247)

Old text:

````text
and on the other routes it gets at day 6 the answer it gets after the switch-off. **Dispositions:**
````

New text:

````text
and on the other routes it gets at day 6 the answer it gets after the switch-off. The record agrees too (Step 7a cycle 2, fix-ups 2 and 3). Each approval and rejection stores how the decider was entitled, and an HR user who also happens to be the leaver's manager is now recorded as HR from day 6, not as the manager, because the label comes from the same check that decides the access. **Dispositions:**
````

### 4e — Step 7a's cycle-2 record (line 565)

Old text:

````text
Cycle 2 runs on K′, which carries TASK-14725 and these records. **Cycles:** `<7a cycles>`.
````

New text:

````text
Cycle 2 runs on K′, which carries TASK-14725 and these records.

### Cycle 2 — on K′ = `076ac29e` (2026-11-05)

| Lens | Model | Verdict | Artifact |
|---|---|---|---|
| External: Codex, base `b510e45b` | Codex (`gpt-6-astra`) | FINDINGS: 0 BLOCKER, 2 WARNING, 2 NOTE | `.claude/reviews/SPRINT-147-step7a-codex-c2.md` |
| Internal: `reviewer` | `claude-fable-5-1` | APPROVED-WITH-WARNINGS: 0 BLOCKER, 1 WARNING, 7 NOTE | `.claude/reviews/SPRINT-147-step7a-reviewer-c2.md` |

*In plain words:* cycle 1's defect is fixed the right way, and no lens found a new blocker. Codex found that the record of a decision (the approval method) still named the manager in the case cycle 1 had closed for access. That is fixed in code by fix-ups 2 and 3. The rest were record wording, one log level, and the condition that the fix's Docker proof must still run.

| Finding | What | Disposition |
|---|---|---|
| Codex WARNING 1 | At D+6 an HR actor who also holds the manager or unit-leader position is admitted through HR scope, but the recorded `approval_method` still said manager | Fixed by TASK-14725 fix-ups 2 and 3 (merged `ab77c475`): the label comes from the authority predicate's own in-lock answer |
| Codex WARNING 2 | "SEC-058, CLOSED" in `SECURITY.md:146` and SEC-057's note, while SEC-058 itself read "fixed in code" | Fixed in `c1934e78`: both read "fixed in code; closes at C-2" |
| Codex NOTE 1, NOTE 2 | Cycle 1's cut-off fix confirmed on both layers; the census re-run on K′ (88 routes; 80 in the three hosts) | No action |
| Fable W-1 | Approval is conditional on the fix's Docker proof: C-2 had not run at review time | Resolves when C-2 on the final candidate is green. A run on K′ (`42013444380`) was in progress at review time; it is evidence for K′ only, so C-2 runs again on K″ |
| Fable N-1 | The employed-month bound's "not exploitable" was stronger than the code | Fixed in `c1934e78`: ADR-040's note records the real gap, with S148 stage 0's fix |
| Fable N-2 | `/remove` only to D+5 understated HR's options | Fixed in `c1934e78`: ADR-040's note and HRP-011 name HR's direct switch-offs |
| Fable N-3 | `SECURITY.md` cited stale validator lines | Fixed in `c1934e78` (`OrgScopeValidator.cs:151`, `:339`) |
| Fable N-4 | The validator logged `past-grace-as-switched-off` at Warning on every active-only route | Fixed in fix-up 2: Information |
| Fable N-5 | The Step 7a header table's placeholders name the final artifacts, which do not exist yet | At the close (C-4), with the other placeholders |
| Fable N-6 | The stand-in path had no named pin | Pinned in fix-up 2: `Day6_LeaverStillActive_ManagerVikarStandIn_Is403_Day5Is200` |
| Fable N-7 | `ApprovalPeriodRepository.cs:70` builds the authorizer without a logger | Left as is, with the reason recorded at the 5α observation above: the fallback serves only the Payroll host, which calls none of the three list reads |

Cycle 3 runs on K″, which carries fix-ups 2 and 3 and these records. **Cycles:** `<7a cycles>`.
````

### 4f — Phase, the head (line 578)

Old text:

````text
| **Phase** | **Step 7a cycle 1 done on K = `c13024cd`** (2026-11-04; see "Step 7a (C-3)"). Its record fixes are in `bf3235b1`; its W-1 fix, TASK-14725, merged `2cfae13c` (owner rulings 2026-11-05). **Next: K′ (TASK-14725 and its records), C-2 on K′, Step 7a cycle 2, then the close.**
````

New text:

````text
| **Phase** | **Step 7a cycle 2 done on K′ = `076ac29e`** (2026-11-05; see "Step 7a (C-3)"). Its record fixes are in `c1934e78`; its code fix, TASK-14725 fix-ups 2 and 3, merged `ab77c475`. **Next: K″ = `<K sha>` (these records), the final candidate; C-2 on K″, Step 7a cycle 3, then the close.** **Step 7a cycle 1 done on K = `c13024cd`** (2026-11-04). Its record fixes are in `bf3235b1`; its W-1 fix, TASK-14725, merged `2cfae13c` (owner rulings 2026-11-05).
````

### 4g — Next action (line 581, the whole cell)

Old text:

````text
| **Next action** | (1) **K′**: the TASK-14725 merge (`2cfae13c`) plus these records (`.claude/plans/s147/C-1c-DOCS-OWED.md`), committed on master; not pushed. (2) **C-2 on K′** (`PLAN-s147.md:426`): push to `s147-ci-k`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K′; all seven jobs green. Then fill `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K", and flip every "CI-pending (C-2)" to CI-verified with the run id: the tidy pass's facts, TASK-14725's facts, SEC-058's Docker pins and the index row. Close the PR unmerged and delete the branch. Red → a fix task → K″ → C-2 again. (3) **C-3, Step 7a cycle 2** on K′, both lenses: `codex review --base b510e45b`; `reviewed-against-commit: <K sha>` (K′'s sha); the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". (4) **C-4, the close commit**, a docs-only child of the final candidate: every angle-bracket placeholder filled; `git diff --stat` from the final candidate lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (5) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward, TASK-14725 and Step 7a included); the handoff "closed"; teardown, TASK-14725's worktree included. The Step-0a DEBT branches stay the owner's call |
````

New text:

````text
| **Next action** | (1) **K″**: these records (`.claude/plans/s147/C-1d-DOCS-OWED.md`) on top of `ab77c475`, committed on master; not pushed. K″ is the final candidate. (2) **C-2 on K″** (`PLAN-s147.md:426`): push to `s147-ci-kpp`; a draft PR whose first body line is "EVIDENCE ONLY — S147 checkpoint C-2; never merge"; one background watch; `headSha` equals K″; all seven jobs green. Check: Unit 1722; Regression with the Docker facts all executed, the six new label facts included; no pre-existing D+6 fact red. Then fill `<C-2 run id>`, `<C-2 date>` and the counts in "C-2 — the checkpoint on K", and flip every "CI-pending (C-2)" to CI-verified with the run id: the tidy pass's facts, TASK-14725's facts (fix-ups 2–3 included), SEC-058's Docker pins and the index row. Close the PR unmerged and delete the branch. Red → a fix task → a new candidate → C-2 again. (3) **C-3, Step 7a cycle 3** on K″, both lenses: `codex review --base b510e45b`; `reviewed-against-commit: <K sha>` (K″'s sha); the reviewer on Fable 5.1, or an Opus stand-in with `.claude/reviews/SPRINT-147-review-floor-WAIVED.md`; each artifact saved before acting; the verdicts into "Step 7a (C-3)". (4) **C-4, the close commit**, a docs-only child of K″: every angle-bracket placeholder filled, the Step 7a header table's included (Fable c2 N-5); `git diff --stat` from K″ lists only `docs/**`; the guard's gates (`docs/WORKFLOW.md:41-47`); the commit message written through a file, never inside the Bash command; push; watch CI to green. (5) **C-5**: the Test Verified line with the close run id; the model-routing register's S147 row (Opus stand-in cycles: 14719 c1–2, 14718 c2–5, 14720 c1–3, 14712 c1–2, the 14710 stack c1–2; Fable from 14713 onward, TASK-14725, its fix-ups and Step 7a included); the handoff "closed"; teardown (the agent worktrees are already removed). The Step-0a DEBT branches stay the owner's call |
````

### 4h — As of (line 571)

Old text:

````text
| **As of** | 2026-11-05, K′ = `<K sha>`: K `c13024cd`, its Step 7a record fixes `bf3235b1`, the TASK-14725 merge `2cfae13c` and these records (local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

New text:

````text
| **As of** | 2026-11-05, K″ = `<K sha>`: K `c13024cd`; its Step 7a record fixes `bf3235b1`; K′ `076ac29e` (the TASK-14725 merge `2cfae13c` and its records); cycle 2's record fixes `c1934e78`; the fix-ups 2–3 merge `ab77c475`; and these records (local master, not pushed; origin/master `7cc70be8` was merged in at `c29764ce`) |
````

### 4i — the header Status (line 5, the cell's end)

Old text:

````text
Next: K′, C-2 on K′, Step 7a cycle 2 |
````

New text:

````text
Step 7a cycle 2 found that the recorded approval method still named the manager in that case; TASK-14725 fix-ups 2 and 3 (merged `ab77c475`) take it from the same check. Next: K″, C-2 on K″, Step 7a cycle 3 |
````

### 4j — C-2's Commit row (line 528)

Old text:

````text
or the K′ that Step 7a's fixes make.
````

New text:

````text
or the candidate Step 7a's fixes make (K′ `076ac29e`, then K″, the final one).
````

### 4k — C-2's first-run row: the label facts (line 531)

Old text:

````text
and the re-dated `Remove_ActorRemovesGraceLeaver_AsToday` |
````

New text:

````text
the re-dated `Remove_ActorRemovesGraceLeaver_AsToday`, and the six approval-method facts of fix-ups 2 and 3 in `LeaverPastGraceByDateTests` |
````

---

## Not drafted here

- **Performance F7.** Its row ("one extra single-row read per admitted manager–employee pair on the my-reports list and Teamoversigt") and section stay true at `ab77c475`. Fix-up 3 adds one predicate evaluation per HR approve or reject, a single request rather than a list read. Fable's fix-up 3 NOTE-6 says no F-row is needed, and ADR-040's label bullet (1d) records the cost. Fix-up 2's moved read is gone.
- **HRP-011 and `QUALITY.md`.** The label is audit metadata: it changes no HR process and no grade. SEC-058's line in `QUALITY.md` stays true.
- **Fable's fix-up 3 NOTE-4** (an HR-also-unit-leader D+5 mirror not pinned; a formatting slip at `LeaverPastGraceByDateTests.cs:69`): test-side, below the records.
- **Codex's fix-up 2 note on the separate-read race ("unbounded")** and the manual-switch-off note: superseded by fix-up 3, which removed the separate read and covers the manual switch-off.

## Brief contradictions, deviations and open points

**Claims about the existing code (checked; the code governs):**
1. **"`:401` and `:422` are valid again at `ab77c475`":** confirmed. `DesignatedApproverAuthorizer.cs` is byte-identical to `0a64c1eb` (`git diff 0a64c1eb ab77c475` on the file is empty).
2. **"Check the rest":** every other code line in ADR-040's note holds at `ab77c475`, with two exceptions.
   - The departed-self clause on the active-only overload is `OrgScopeValidator.cs:151-152`, not `:150-151`. This was my own error in C-1c: it was already `:151` at `0a64c1eb`. Fixed in 1b.
   - The send's refusal moved from `:2473-2480` to `:2513-2520`. Fixed in 1c.
   - Unchanged: `OrgScopeValidator.cs` `:145`, `:160-161`, `:332`, `:339-340`, `:349-350`, `:356`, `:385`, `:600-602`; `ApprovalEndpoints.cs:71`; Payroll `Program.cs:876`; `ReportingLineEndpoints.cs:87`, `:108`; `AdminEndpoints.cs:1579`, `:1742`.
3. **SEC-057's leader-arm lines drifted again** with fix-ups 2–3, to `:446-457`, `:736-746` and `:1964-1976`. The brief did not list this; 3b adds them.
4. **No 5α artifact for fix-up 3 is in `.claude/reviews/`** (only `S147-TASK-14725-fu2-5alpha.md`). The merge row (4b) says so. If a 5α ran and was not saved, the Orchestrator may want to correct that cell.

**Claims about required behaviour:** none contradicted.

**Needs a ruling (the Orchestrator's):** none.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-05 | `planner` (Opus 5.5) | First draft at `ab77c475`. Blocks 1–4, 21 replacements: ADR-040's note gains the label rule, `designated_approver_id`, the step-(3) residual, the log reading and the six label facts, with its lines re-checked (two fixed); `SECURITY.md`'s label and log sentence; SEC-058's label pins and SEC-057's new leader-arm lines; the sprint log (the log-level note, the merge record, the ledger row, the plain-words sentence, Step 7a cycle 2's record, Phase, Next action, As of, Status, C-2's two rows). F7 unchanged (still true) |
