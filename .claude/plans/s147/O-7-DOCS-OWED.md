# O-7 — The documents owed once TASK-14716 has merged (SEC-054 step 2): apply-ready text

| | |
|---|---|
| **What this is** | The exact text for every line of the plan's O-7 row (`.claude/plans/PLAN-s147.md:315`), the refinement's T6 "Docs" line (`.claude/refinements/REFINEMENT-s147-defects-and-model-rulings.md:431`), the sprint log's O-7 items (`docs/sprints/SPRINT-147.md:166`, `:213-245`, `:364`, `:425`) and O-6's hand-over (`.claude/plans/s147/O-6-DOCS-OWED.md:597-601`), so the Orchestrator can apply each as a string replacement. The `planner` drafts; the Orchestrator applies. Every target is under `docs/**` or is `ROADMAP.md` (X8 only) |
| **Status** | DRAFT 2 (`planner`, Opus 5.5), 2026-11-04: the cycle-1 findings of both lenses folded (`.claude/reviews/S147-O7-codex.md`, `S147-O7-reviewer.md`; see "Revision history") |
| **Base commit** | `94bd32be` (local master, the main checkout). Every "Old text" below is copied from `94bd32be` and was checked unique in its target by an exact-substring count over the whole file, both in the commit's blob and in the working copy (each count is 1; no text occurs twice). Every line number is at that commit |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 |
| **What merged (the premise)** | **Step 1:** TASK-14715 + 14715-T, merged `9cbf164b` (a month before the ledger start is paid with flex "not tracked"); Docker facts CI-verified at CI-W4, run `41708962373` on `eaf90ed4` (`SPRINT-147.md:246`, `:422`). **Step 2:** TASK-14716 + 14716-T, merged `169e9980` (the raw routes retired; the period required; the whole-month identity check, R-G2/R-G8; one month per call, owner R-G9); merge record `94bd32be`. CI-W4b is running on `94bd32be` (`SPRINT-147.md:422`) |
| **Timing** | **Apply after CI-W4b is green** (it CI-verifies AC-054-3 and the TASK-14716 Docker facts). Two placeholders are the Orchestrator's to fill: `<CI-W4b run id>` and `<CI-W4b date>` (where, see "How to apply"). No block needs the owner's approval |

## What this is, in plain words

SEC-054 was the finding that one global administrator could send pay lines they had written themselves to payroll, for any employee and any month, with no approved month behind them and no second person checking (the tender gap analysis found it on 2026-10-26). Two "raw" routes allowed it, and nothing in the product called them. The owner ruled (OQ-3, 2026-10-27) to retire both, but first to give a normal path to the one kind of month only they could pay: a month before the employee's first tracked flex month, now paid with its flex marked "not tracked". Both steps have merged:
- **TASK-14715** pays that month through the normal, approval-checked route.
- **TASK-14716** deletes the two routes. The export service itself now refuses any export that does not name the employee's own approved month, and it exports exactly one employee's one month per call. The owner chose (R-G9) to remove the old multi-month machinery rather than leave it dead.

What is left is paperwork. It matters because several documents still describe the raw routes as live, or record a decision whose premise has changed:
- **ADR-034** (the payroll-export lock) brought the raw routes under the lock in S90 (its OQ-6), and its D3 describes splitting one call into several months. One dated amendment records what changed, with short pointers from OQ-6, D2, D3 and D4's S146 note. The S90 and S146 text stays as written. **ADR-016** (period handling) gets one pointer of the same kind, because its S145 amendment still lists the raw routes among the routes that plan a month.
- **A correction is not an export.** Every first export made since TASK-14716 comes from an approved month, but a correction needs only an existing export record. So a record the raw routes wrote before S147 can still be corrected, with no approved month behind it, until the database is reseeded. Every block that states what a correction can reach says so.
- **The registers.** SEC-054 closes. QUAL-183's open question ("do these routes need to exist at all?") is answered: no. QUAL-225, HRP-003, SEC-048 and SEC-023 each get a dated note where they described the raw routes or now carry a consequence.
- **`SECURITY.md`.** The departed-self census said two of its 19 guarded routes stayed open "until it merges". It has merged, so all 19 are closed. The segregation-of-duties section listed the raw routes as outside the four-eyes rule; they no longer exist.
- **DEP-004** (the endpoint registry) still lists the two routes, and the **audit catalog** names them as the only source of an export event with no flex balance. **`FRONTEND.md`** has a small fix owed since TASK-14707.
- **Two new register rows.** QUAL-243 records what the raw routes left behind (a database column that still allows a record with no approved month, a refusal written for their records, and a test-only way into the export service). QUAL-244 records a wage-type lookup that only tests still use.

Four rules shaped every block:
- **Nothing is described as built unless it is merged.** Both tasks are. The TASK-14716 Docker facts are called CI-verified only through the CI-W4b placeholders.
- **Earlier decisions are not rewritten.** ADR-034 keeps its S90 and S146 text and gains pointers plus one dated section. Register rows keep their earlier status as *Was:* or gain a dated note.
- **Plain words first, then the mechanism**, with a `file:line` opened at `94bd32be`.
- **The register rows propose; they do not decide.** QUAL-243's and QUAL-244's "→" cells are proposed fixes for a later sprint.

## How to apply

- Each block names its **Target**, the **Old text** (a unique match at `94bd32be`) and the **New text**. Where a block has parts (a, b, …), apply each part as its own replacement.
- No two replacements in one file overlap, so the order inside a file does not matter. Several land in the same line (1a and 1b in SEC-054's row; 7a, 7b and 7c in HRP-003's row); they are disjoint substrings of it.
- **Line endings (the working copy; checked with `file` and a carriage-return count).** The repository's blobs are LF everywhere; `core.autocrlf = true` gives some working-copy files CRLF on checkout.
  - **LF:** `docs/operations/security-finding-register.md`, `docs/operations/quality-finding-register.md`, `docs/operations/hr-follow-up-process-register.md`, `docs/knowledge-base/INDEX.md`, `docs/SECURITY.md`, ADR-016 (Block 13), `docs/operations/audit-projection-catalog.md` (Block 14), ADR-040 (X5), `ROADMAP.md` (X8).
  - **CRLF throughout:** ADR-034 (Block 3), `docs/FRONTEND.md` (Block 11, X6), DEP-004 (Block 10).
  - **Mixed:** ADR-020 (X7) is CRLF except lines 103-104; its target, line 116, is CRLF.
  - The multi-line Old texts (Block 10, X6b) match once CRLF is read as LF, as the Edit tool does. Multi-line New texts in a CRLF file (3b, 3e, 10) must be written with the file's CRLF.
- **Placeholders.** Replace the literal tokens before applying. `<CI-W4b run id>` sits inside backticks, as run ids do in these docs. `<CI-W4b date>` is written bare, as dates are. They occur in Blocks 1a, 1b, 2, 3a, 3b, 3c, 3d, 3e, 3f, 4, 5a, 8, 12, 13 and X5.
- **Blocks 1a–14 are owed by O-7.** Blocks **X1–X8** are beyond the owed list; each says what it fixes and its lean. None needs the owner. All targets are the Orchestrator's (`docs/**`; `ROADMAP.md` for X8).

## Map: each owed item → its block

| # | Owed item (source) | Block | State before this draft |
|---|---|---|---|
| 1 | SEC-054 CLOSED, with what was done (step 1 TASK-14715 `9cbf164b`, CI-W4 `41708962373`; step 2 TASK-14716 `169e9980`, CI-W4b), what was given up and the residuals (plan `:315`; refinement `:431`; `SPRINT-147.md:425`) | 1a | REGISTERED, "not yet adjudicated" (`security-finding-register.md:214`) |
| 2 | SEC-054's last sentence, past tense ("Note QUAL-230: today the raw route is probably the only way …"; O-6 `:599`) | 1b | untrue since TASK-14713 |
| 3 | QUAL-183's follow-up: its open question "do these routes need to exist at all?" (`quality-finding-register.md:406`, the "→" cell) | 2 | open |
| 4 | ADR-034 OQ-6 superseded (`ADR-034:15`), and the S146 D4 note's "all three export routes" (`:37`; Fable cycle-1 NOTE-2) | 3a, 3f, 3e | not written |
| 5 | ADR-034 D3 (`:26`, `:29`) with an S146 R-14 trail note (refinement `:431`) | 3c, 3d, 3e | not written |
| 6 | ADR-034 D3: a null-balance record now also comes from a case-3a calculate-and-export (Fable c1 NOTE 4; `SPRINT-147.md:242`) | 3d, 3e | not written |
| 7 | One `(employee, year, month)` per export, owner R-G9 2026-11-04: the grouping gone, a mixed call refused before any connection (`SPRINT-147.md:215`, `:222`) | 3b, 3c, 3e | not written |
| 8 | B2's identity check, R-G2/R-G8: the period is the exported employee's whole calendar month (`SPRINT-147.md:214`, `:221`) | 3b, 3e | not written |
| 9 | `ExportAsync` requires a period (refinement `:420`; `SPRINT-147.md:214`) | 3b, 3e | not written |
| 10 | The KB INDEX row for ADR-034, if stale | 4 | stale: "multi-period `/export-period` → one record per (employee,month); zero-line month doesn't lock" (`INDEX.md:42`) |
| 11 | The census sentence "17 → 19 guarded" and O-6 Block 6's "until it merges …" (`SPRINT-147.md:425`; O-6 `:599`) | 5a (and X5) | `SECURITY.md:128` still says the two Payroll routes are open "until it merges" |
| 12 | *(not on the list; untrue now, and the home of R-G6's rule)* `SECURITY.md:194` "That is SEC-054, handled by TASK-14716" | 5b | untrue |
| 13 | QUAL-225 (refinement `:416`, `:431`) | 6 | no S147 note |
| 14 | HRP-003 (refinement `:431`) | 7a, 7b, 7c | two sentences describe the raw routes as live; no S147 note |
| 15 | SEC-048 (refinement `:431`) | 8 | names the raw routes and `BuildExportContext` as live |
| 16 | SEC-023 (refinement `:431`) | 9 | justifies its floor by a sibling that is now retired |
| 17 | DEP-004, its two raw-route rows (refinement `:431`; Fable c1 N-5) | 10 | live rows at `:145-146` |
| 18 | `docs/FRONTEND.md:113`, `:291` (`SPRINT-147.md:166`, `:364`, "at O-4/O-7") | 11a, 11b | **not done**: no S147 commit touched the file (its last change is `dc9ad254`, S146; O-4's `20a78197` changed four other files) |
| 19 | Register candidate (a), the O-7 bundle: `period_id` NOT NULL with the `init.sql` comments and the db-schema regen; case 4b's raw arm; the no-flex-write seam (Fable c1 N-4, c2 N-4; R-G3; `SPRINT-147.md:225`) | 12 (QUAL-243) | not registered |
| 20 | Register candidate (b), R-G7: `GetMappingAsync`'s date-less lookup (`SPRINT-147.md:226`) | 12 (QUAL-244) | not registered |
| 21 | Register candidate (c), R-G6's residual: the 409 body copies `ex.Message` (`SPRINT-147.md:224`) | 1a (a residual on SEC-054's closing entry) + 5b (the rule, in `SECURITY.md`) | recorded in the sprint log only |
| 22 | *(added in draft 2, Codex cycle-1 W2)* ADR-016's operative S145 D4 amendment still lists four routes that plan a month and the raw routes' split-month 422 (`ADR-016:89`) | 13 | untrue |
| 23 | *(added in draft 2, Fable cycle-1 NOTE-3; moved from "Already done")* the audit catalog's `PayrollExportGenerated` row: null flex keys "as on a raw export" (`audit-projection-catalog.md:119`) | 14 | names only the retired source |

**Count: 24 owed replacements (1a, 1b, 2, 3a, 3b, 3c, 3d, 3e, 3f, 4, 5a, 5b, 6, 7a, 7b, 7c, 8, 9, 10, 11a, 11b, 12, 13, 14) and 8 optional blocks (X1–X8: 9 replacements, because X6 has two parts). 33 replacements in all.**

**Ids used: QUAL-243 and QUAL-244. No new SEC id.** How checked: the QUAL register's last row is QUAL-242, on the file's last line (`quality-finding-register.md:466`); `git grep "QUAL-24[3-9]"` over the whole tree at `94bd32be`, and a grep over `.claude/plans`, `.claude/refinements` and `.claude/reviews`, find no QUAL-243 or later. The SEC register's last row is SEC-057 (`security-finding-register.md:217`), and SEC-058 occurs nowhere; it is not used, because R-G6 goes on SEC-054's closing entry (see "Brief contradictions …", point R-1).

## Already done (no block; checked at `94bd32be`)

| Owed item | Where it is done | Evidence |
|---|---|---|
| The leaver half of SEC-054's last sentence: a switched-off leaver's month has a payment path | `docs/SECURITY.md:141-145` (HR only from day 6; payroll `/calculate-and-export` at the LocalAdmin floor); QUAL-230's outcome note (`quality-finding-register.md:454`: "the raw route is no longer needed to pay a leaver's month") | written at the TASK-14713 merge and at O-6 |
| TASK-14715's manifest-link gap (a record-less untracked month's calculation proof is linked only by the HTTP audit detail) | Needs no doc change. ADR-016's D10 footnote of 2026-10-26 already scopes the event's `manifestId` to "every `/calculate-and-export` that writes the month's record" (`ADR-016-temporal-period-handling.md:167`), and the sprint log rules "No fix" (`SPRINT-147.md:243`) | read at `94bd32be` |
| `src` comments that still describe the raw routes as live | Tidy item 25 holds them (`SPRINT-147.md:403-410`), except one (see "Not drafted here") | read at `94bd32be` |
| Historical mentions of the raw routes in ADR-040's S144 note (`:237-240`) and ADR-016 D5's original in-scope list (`:97`, S20: "named entry points (`/calculate-and-export`, `/export-period`, …)") | Records of what was decided and built then, not statements of how the system works now; nothing to change. (ADR-016's operative S145 D4 amendment, `:89`, is different: it states the current refusals, so it gets Block 13) | read at `94bd32be` |

---

## Block 1 — SEC-054 closed

**Target:** `docs/operations/security-finding-register.md` (line 214, the SEC-054 row). Two parts. 1a: the status and source cells become CLOSED, keeping the original status as *Was:* (as QUAL-230 did at O-6) and the original citations as AS-REGISTERED (as SEC-049 did). 1b: the adjudication cell's last sentence goes into the past tense and gains the outcome.

### 1a — the status cell and the source-of-truth cell

*Why this shape:* the brief asks for what was done (both steps, each with its CI evidence), what was given up, and the residuals. R-G6's residual is recorded here, not in a new row (point R-1 below).

Old text:

````text
REGISTERED (tender gap analysis, 2026-10-26); not yet adjudicated | `Payroll/Program.cs:209-318`; `PayrollExportService.cs:126, :361-366`; `OrchestratorScopeHelpers.cs:41-53`; ADR-034 D3; QUAL-183; SEC-048 |
````

New text:

````text
**CLOSED (S147, <CI-W4b date>): both raw routes are retired, and every export made since TASK-14716 comes from the employee's own approved month, approved by someone other than the employee and, where one is recorded, the sender.** Owner OQ-3 = (a) + (i) (2026-10-27): retire both, and first give the one month only they could pay a normal path. **Step 1, TASK-14715** (merged `9cbf164b`; Docker facts CI-verified at CI-W4, run `41708962373`): a month before the employee's ledger start (case 3a) is paid through `/calculate-and-export` with its flex "not tracked". With pay lines it gets a locked record with no balance and no flex payout; with none, nothing is recorded. **Step 2, TASK-14716** (merged `169e9980`; Docker facts CI-verified at CI-W4b, run `<CI-W4b run id>`): `POST /api/payroll/export` and `/export-period` answer 404, gone with every helper only they used and the Orchestrator's dead `payroll-export` task type. `ExportAsync` refuses an export that names no approval period, before any connection opens, so the APPROVED re-assert and the four-eyes checkpoint run on every export. That re-assert also requires the period to be the exported employee's whole calendar month (R-G2, R-G8), and an export is exactly one (employee, year, month) (owner ruling R-G9, 2026-11-04). **Given up:** an administrator's way to send hand-written lines (a paid month now changes only by a correction, `/recalculate`); the flex of a month before the ledger start, which is not carried forward (S146's refusal of that month reversed by the owner's choice; QUAL-225 is the opening-balance route); the export service's multi-month call; the facts that guarded only the raw routes (the files are named in the TASK-14716-T commit `75b463b7`). **Residuals:** corrections stay single-signer (`/recalculate` is GlobalAdmin-only; `docs/SECURITY.md` § "Segregation of duties"), and a correction needs only an existing export record, not an approved month (`RetroactiveCorrectionService.cs:211-223`). So a record the raw routes wrote before S147, which has no approved month behind it, can still be corrected (`FlexLedgerHostTests.cs:749`) in a database that is not reseeded. What the raw routes left behind, a nullable `period_id`, case 4b's raw arm and the no-flex-write seam, is QUAL-243. `/calculate-and-export` copies the export's conflict message into its 409 body (`Payroll/Program.cs:501`); the period-identity message, which names the approval's employee and dates, cannot occur there, so a second caller of `ExportAsync` must redact it at its own route (R-G6). The rule is recorded in ADR-034's amendment of <CI-W4b date>. *Was:* REGISTERED (tender gap analysis, 2026-10-26); not yet adjudicated | AS-REGISTERED (at `67809348`, the tender analysis's evidence commit): `Payroll/Program.cs:209-318`; `PayrollExportService.cs:126, :361-366`; `OrchestratorScopeHelpers.cs:41-53`; ADR-034 D3; QUAL-183; SEC-048. AS-CLOSED (at `94bd32be`): `Payroll/Program.cs:221-226` (the retirement note); `PayrollExportService.cs:204-219` (one month per call), `:228-231` (the period required), `:487-557` (B2, the in-transaction re-assert: the identity check, APPROVED, four-eyes); `RawExportRoutesRetiredTests`, `PayrollExportNoPeriodTests`, `PayrollExportPeriodIdentityTests`, `FlexLedgerCase3aHostTests`; ADR-034 amendment <CI-W4b date> |
````

### 1b — the adjudication cell: the last sentence in the past tense, and the outcome

Old text:

````text
Note QUAL-230: today the raw route is probably the only way to pay a deactivated leaver's month. |
````

New text:

````text
Note QUAL-230: when this row was registered, the raw route was probably the only way to pay a deactivated leaver's month. Since TASK-14713 (S147), `/calculate-and-export` pays a switched-off leaver's month at the LocalAdmin floor, and since TASK-14715 a month before the ledger start, so the retirement left no month without a way to be paid. **Outcome (S147): retired, as preferred above** (owner OQ-3 = (a) + (i)) → SPRINT-147 (the OQ-3 ruling; the TASK-14715 and TASK-14716 merge records, rulings R-G1..R-G10); ADR-034 amendment <CI-W4b date>; `docs/SECURITY.md` § "Segregation of duties"; QUAL-183's follow-up answered. |
````

## Block 2 — QUAL-183's follow-up answered

**Target:** `docs/operations/quality-finding-register.md` (line 406, the QUAL-183 row; the end of its "→" cell).

*What the follow-up is:* the row closed its S144 defect with a guard and left one question open, "do these routes need to exist at all? … Retiring them is its own refinement." S147's OQ-3 answered it.

Old text:

````text
The outage case (guard blind while the Rule Engine is unreachable) is QUAL-185, also fixed in S144. |
````

New text:

````text
The outage case (guard blind while the Rule Engine is unreachable) is QUAL-185, also fixed in S144. **The follow-up, answered (S147, O-7, <CI-W4b date>): no, they do not need to exist.** The owner ruled OQ-3 = (a) + (i) (2026-10-27): retire both, after first giving a normal path to the one month only they could pay, a month before the employee's ledger start. TASK-14715 (merged `9cbf164b`; CI-W4, run `41708962373`) did that, and TASK-14716 (merged `169e9980`; CI-W4b, run `<CI-W4b run id>`) deleted the routes, which now answer 404. This row's guard went with them: its raw-route facts in `PayrollHostRecalcBlockedTests` were deleted by TASK-14716-T (`75b463b7`), and the calculating routes keep the same planner refusal. SEC-054 CLOSED; ADR-034 amendment <CI-W4b date>. |
````

## Block 3 — ADR-034: pointers, and one dated amendment

**Target:** `docs/knowledge-base/decisions/ADR-034-payroll-export-lock.md` (**CRLF**). Six parts. 3a–3d and 3f add one-line pointers where the S90 and S146 text is superseded (OQ-6, D2, D3, the S146 D3 item, the S146 D4 note); 3e appends the amendment after the last line (`:51`), as O-6 did for ADR-040.

*Why one appended section and not notes inside D2–D4:* the brief asks for O-6's shape. The change also cuts across OQ-6, D2, D3 and the S146 R-14 ruling, so one section tells it once and the pointers send each reader there.

### 3a — OQ-6 (line 15)

Old text:

````text
OQ-6 the raw `/export`+`/export-period` under the lock+idempotency.
````

New text:

````text
OQ-6 the raw `/export`+`/export-period` under the lock+idempotency. *(OQ-6 superseded S147: both raw routes are retired; see "Amendment <CI-W4b date>" below.)*
````

### 3b — D2 (line 23, the paragraph's end; a new paragraph follows it)

Old text:

````text
now a real audited event (Infrastructure `IAuditProjectionMapper` + Payroll DI + catalog row; the catalog "defined-but-unemitted TBD" resolved).
````

New text:

````text
now a real audited event (Infrastructure `IAuditProjectionMapper` + Payroll DI + catalog row; the catalog "defined-but-unemitted TBD" resolved).

*(Amended S147, see "Amendment <CI-W4b date>" below: an export is one (employee, year, month), so there is no grouping step; the approval period is required, so the `FOR UPDATE` re-assert of APPROVED, with the four-eyes checkpoint, runs on every export; and it first checks that the period is the exported employee's whole calendar month.)*
````

### 3c — D3 (line 26, the paragraph's end)

Old text:

````text
A ZERO-line month writes NO record (nothing handed off → stays reopenable).
````

New text:

````text
A ZERO-line month writes NO record (nothing handed off → stays reopenable). *(Amended S147, see "Amendment <CI-W4b date>": the raw routes are retired and an export is one (employee, year, month), so nothing is grouped; a month before the ledger start with no lines and no record writes no record, by this rule.)*
````

### 3d — the S146 D3 amendment, item 1 (line 29)

Old text:

````text
The raw routes carry no balance and keep D3 unchanged.
````

New text:

````text
The raw routes carry no balance and keep D3 unchanged. *(S147: the raw routes are retired. A record with no balance now also comes from a month before the ledger start exported with lines through `/calculate-and-export`; see "Amendment <CI-W4b date>".)*
````

### 3e — the S147 amendment (appended after line 51)

Old text:

````text
Detail in `docs/sprints/SPRINT-145.md`.
````

New text:

````text
Detail in `docs/sprints/SPRINT-145.md`.

## Amendment <CI-W4b date> — S147: every new export comes from its own approved month (SEC-054; owner rulings OQ-3 = (a) + (i) and R-G9)

**Why this exists, in plain words.** In S90 the owner brought the two "raw" routes, `POST /api/payroll/export` and
`/export-period`, under the lock and the idempotency (OQ-6), without asking whether they should exist. They took pay
lines the caller had written and sent them to payroll with no approved month behind them and no second person, so one
GlobalAdmin could pay any employee any month alone (SEC-054, found by the tender gap analysis of 2026-10-26). Nothing in
the product called them, but they were the only way to pay one kind of month: a month before the employee's first
recorded flex month (S146 R-14's "case 3a"), which the calculating route refused. The owner ruled (S147 OQ-3,
2026-10-27) to retire both, and first to pay that month through the calculating route with its flex "not tracked"
(remedy (i)). It was built in two steps, both merged: TASK-14715 (`9cbf164b`; Docker facts CI-verified at CI-W4, run
`41708962373`) and TASK-14716 (`169e9980`; CI-W4b, run `<CI-W4b run id>`). D1, D4's race argument and D5 stand. What
changes is below.

**OQ-6 is superseded.** Both raw routes are deleted and answer 404
(`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:221-226`; `RawExportRoutesRetiredTests`), with every helper
only they used and the Orchestrator's dead `payroll-export` task type. `/calculate-and-export` is the one route that
exports; `/recalculate` stays the one way a paid month changes (D5). The D4 note of 2026-10-24 says the ledger lock is
taken by "all three export routes"; there is now one.

**The approval period is required, and it must be the month's own (D2, D4).**
- `ExportAsync` refuses a context with no approval period (null or the empty id) by throwing, before any connection
  opens (`…/Services/PayrollExportService.cs:228-231`; the message `:31-35`). Until S147 the raw routes passed no
  period, and D2's in-transaction `FOR UPDATE` re-assert of APPROVED (called "B2" in the code, after S90's Step-0b
  blocker B2, the export↔reopen race named under Consequences; `:300-304`) was skipped for them. It now runs on every export made since TASK-14716 (`:304`),
  with S147's four-eyes checkpoint (SEC-053).
- The re-assert first checks that the locked period is this month's own approval: the exported employee's (compared
  Ordinal), starting on the 1st and ending on the last day of the exported month (rulings R-G2, made exact by R-G8;
  `:524-538`). A period of another employee, of another month, or of only part of the month (a legacy `WEEKLY`
  approval) aborts the export with the existing 409 `payroll-export-conflict`, before its status or approver is checked
  (all three are read in the one `SELECT … FOR UPDATE`).
  On `/calculate-and-export` this holds by construction, because the route accepts only a whole month and reads the
  approval by the request's own employee and dates (`Program.cs:296`, `:310-311`); the check guards any other caller.

**D3, as amended.**
1. **One `(employee, year, month)` per export (owner ruling R-G9, 2026-11-04).** D3's grouping of one call's lines into
   a record per `(employee, year, month)` existed for the raw `/export-period`. With the period required and checked
   as the month's own, a second group could never pass. Both review lenses recommended recording the dead grouping for
   later (R-G5); the owner chose to remove it now. A call whose lines, or whose lines and flex-ledger write, belong to
   more than one `(employee, year, month)` is refused before any connection opens (`PayrollExportService.cs:204-219`;
   the message `:44-47`). A line that spans two calendar months is still refused (`:193-202`). D2's step "group lines
   by `(employee_id, year, month)`" no longer exists.
2. **D3's zero-line rule, applied to a month before the ledger start (TASK-14715; R-17, the S147 refinement's ruling
   that such a month writes no record).** A case-3a month with no lines
   and no record hands off nothing: no lines, and no closing, because its flex is not tracked. So, by D3's own rule, it
   writes no record. It still runs every check a calculating export runs (the ledger lock, the under-lock recheck, the
   previous month's `FOR SHARE`, this month's APPROVED re-assert with four-eyes), and only the write is skipped
   (`PayrollExportService.cs:316`; R-F1, the Orchestrator's ruling on TASK-14715's first review that only the write may
   be skipped). The month stays reopenable, a re-export writes nothing, and `/recalculate`
   refuses it as never exported (422 `payroll-not-exported`).
3. **A record with no balance also comes from a calculate-and-export now (TASK-14715).** Item 1 of the S146 amendment
   says the raw routes carry no balance. Since S147 such a record, with all three balance columns null, also comes from
   a case-3a `/calculate-and-export` with lines: source `CALCULATE_AND_EXPORT`, its period set, no `FLEX_PAYOUT` line,
   and the response's note "flex not tracked" (`Program.cs:528`). It is locked and corrections-only, like case 3b. A
   re-run answers the "use a correction" 409, because each calculation's manifest id enters the line hash (the S146
   Step 7a note under item 3). Records the raw routes wrote before S147 are the other source, in a database that has
   not been reseeded.

**S146 R-14 trail note.** S146's ruling R-14 refused case 3a with `payroll-export-out-of-order` ("can only enter as a
raw export"): a month before the ledger start could not be exported anew through the calculating route
(`.claude/refinements/REFINEMENT-s146-payroll-correctness.md:239`, `:370`, `:405`). S147's OQ-3 remedy (i) reverses that
refusal, by the owner's choice. Case 3a now derives `(0, UNTRACKED)`, as case 3b does
(`…/Services/FlexLedgerOpening.cs:335-342`), and the kind `payroll-export-out-of-order` is deleted, since case 3a was
its only source. The ledger start still never moves (`:340`). The cost, which the owner accepted: such a month's flex is
not carried into the ledger, whose first tracked month still opens at 0. The remedy not chosen for S147, an audited
opening balance that HR enters (QUAL-225), is designed in ADR-043 D6 and stays on the backlog. R-14's cases 3b, 4a and
4c stand; case 4b keeps both arms (see the residuals). The S146 race in which the later month is exported first now
ends with the earlier month paid untracked on its retry, not refused
(`FlexLedgerRaceTests.TwoFirstExports_LaterMonthFirst_EarlierGetsLedgerChanged_ThenUntrackedOnRetry`).

**What was given up, and why.** An administrator's way to send hand-written lines: a paid month now changes only by a
correction, which recalculates from the employee's recorded hours. A month before the ledger start keeps its pay but
not its flex (above). The export service's multi-month call (R-G9). The facts that guarded only the raw routes, deleted
with them (the files are named in the TASK-14716-T commit `75b463b7`).

**Residuals recorded, not fixed.**
- **What the raw routes left behind (QUAL-243).** `payroll_export_records.period_id` stays nullable, and `init.sql`'s
  comments still explain it by the raw routes (`docker/postgres/init.sql:4597-4600`, `:4623`, `:4662`). Case 4b's raw
  arm, which refuses a month whose employed previous month has a record with no balance and asks for that month to be
  corrected first, can now meet only records written before S147 (`FlexLedgerOpening.cs:359-366`, `:387-390`).
  `ExportAsync` still accepts a context with no flex-ledger write and then records null balances, a path only direct
  test calls reach (`PayrollExportService.cs:325-330`). One change closes all three, under "reseed, don't migrate".
- **The conflict message (R-G6).** `/calculate-and-export` copies the export's conflict message into its 409 body
  (`Program.cs:491-503`). The period-identity message names the approval's employee and dates. It cannot occur on that
  route, so a second caller of `ExportAsync` must redact it at its own route (`docs/SECURITY.md` § "Segregation of
  duties").
- **Corrections stay single-signer, and need no approved month.** `/recalculate` is GlobalAdmin-only with no second
  person (Consequences, second bullet). A correction needs only an existing export record, not an approval
  (`…/Services/RetroactiveCorrectionService.cs:211-223`). Every first export made since TASK-14716 comes from an
  approved month, but a record the raw routes wrote before S147 has none behind it, and it can still be corrected, as
  S146 built deliberately (`FlexLedgerHostTests.Recalculate_RawMonthAfterLedgerStart_FillsItsNulls`,
  `tests/StatsTid.Tests.Regression/Payroll/FlexLedgerHostTests.cs:749`), until the database is reseeded.

**Enforced by** `PayrollExportService.ExportAsync` (the checks before the transaction, and the B2 re-assert) and
`FlexLedgerOpening.DeriveLink` (case 3a). **Pinned by** the Unit facts `PayrollExportNoPeriodTests` (a null and an empty
period; mutation M-13), `PayrollExportMoreThanOneMonthTests` (three), `PayrollExportEmptyCallTests` and
`TaskDispatcherTests.GetServiceUrl_PayrollExport_IsNoLongerARoute`; the Docker facts `RawExportRoutesRetiredTests` (two
404s), `PayrollExportPeriodIdentityTests` (five: three pin one clause each, the employee, the start and the end; two are
refused by both date clauses; the test file's summary, `PayrollExportPeriodIdentityTests.cs:34-41`),
`FlexLedgerCase3aHostTests` (seven), and the re-pinned
`FlexLedgerHostTests.Export_UnrecordedMonthBeforeLedgerStart_Returns200Untracked_NamingTheStart_WritesNothing` and the
`FlexLedgerRaceTests` fact above.
````

### 3f — the S146 D4 note (line 37)

*Why:* the note says the ledger lock is taken by "all three export routes". Since S147 there is one; the amendment says so, and this pointer sends the reader there, as 3a–3d do.

Old text:

````text
the export transaction (all three export routes) and the correction transaction
````

New text:

````text
the export transaction (all three export routes [one since S147; see "Amendment <CI-W4b date>"]) and the correction transaction
````

## Block 4 — The KB index row for ADR-034

**Target:** `docs/knowledge-base/INDEX.md` (line 42, inside the ADR-034 row).

*Why:* the row summarises D3 with the multi-period raw route and "zero-line month doesn't lock". The first is gone (S147), and the second has not been true since S146 (an employed zero-line month writes its record). The original words stay; the amendment follows them, as O-6 did for ADR-040.

Old text:

````text
multi-period `/export-period` → one record per (employee,month); zero-line month doesn't lock);
````

New text:

````text
multi-period `/export-period` → one record per (employee,month); zero-line month doesn't lock; **amended S146** [an employed zero-line month writes its record, with the month's flex balance] **and S147, <CI-W4b date>** [SEC-054: the raw `/export` + `/export-period` are retired, OQ-6 superseded; an export is ONE (employee, month), from that month's own APPROVED whole-month period, re-asserted FOR UPDATE with four-eyes on every export; a month before the ledger start is paid with flex untracked, reversing S146 R-14's case-3a refusal]);
````

## Block 5 — `SECURITY.md`: the census, and the segregation-of-duties exclusion

**Target:** `docs/SECURITY.md`. Two parts.

### 5a — the census (line 128; O-6 Block 6's sentence)

*What changes, and the arithmetic.* The census counts do not change: they are the census as taken, 84 write routes in the three hosts = 32 (the shared check) + 19 (guarded another way: 13 inline, 2 by the self-service floor, 4 by retirement) + 33 (classified out) (`SPRINT-147.md:10`; `PLAN-s147.md:240`). What changes is how many of the 19 are in force. From TASK-14710's merge until TASK-14716's, the two raw Payroll routes were still live, so 13 + 2 + 2 = **17** were closed and 2 were open; now all 4 retirements have merged, so **19** are. The live inventory is 4 routes smaller: 84 − 4 = **80** = 32 + (19 − 4 = 15) + 33. The census command (`git grep -n -E "\.Map(Post|Put|Delete|Patch)\(" -- src`, `PLAN-s147.md:232`) at `94bd32be` gives 88 routes in `src`: Backend 76 (78 − 2), Payroll 3 (5 − 2), Orchestrator 1, plus External 1 and Rule Engine 7 outside the three hosts; 76 + 3 + 1 = 80.

Old text:

````text
The other two are the raw Payroll `/export` and `/export-period`, which TASK-14716 retires (SEC-054); until it merges they are `GlobalAdminOnly` routes with no departed-self check.
````

New text:

````text
The other two are the raw Payroll `/export` and `/export-period`, which TASK-14716 retired (SEC-054; merged `169e9980`, its Docker facts CI-verified at CI-W4b, run `<CI-W4b run id>`). Until that merge they were `GlobalAdminOnly` routes with no departed-self check, so 17 of the 19 were closed; now all 19 are. The census's counts stay as taken; the live inventory is four routes smaller, 80 write routes in the three hosts (84 − 4 = 32 + 15 + 33; by the census command at `94bd32be`: Backend 76, Payroll 3, Orchestrator 1).
````

### 5b — "Outside these rules": the raw exports (line 194)

*Why:* the bullet says the raw exports bypass the approval check and are "handled by TASK-14716". They are retired, so the rule now covers every export. This is also where the next author of an export route looks, so R-G6's rule goes here.

Old text:

````text
- The raw GlobalAdmin exports (`/api/payroll/export`, `/export-period`) do not pass through the approval check at all. That is SEC-054, handled by TASK-14716.
````

New text:

````text
- **No longer outside (S147):** the raw GlobalAdmin exports (`/api/payroll/export`, `/export-period`), which skipped the approval check, were retired by TASK-14716 (SEC-054, CLOSED). `ExportAsync` now refuses an export that names no approval period (`PayrollExportService.cs:228-231`), so rule 2's payroll checkpoint runs on every export made since TASK-14716, on the exported employee's own whole-month approval (`:524-553`). A correction is not an export: it takes its own path (the next bullet) and needs only an existing export record (`RetroactiveCorrectionService.cs:211-223`), so a record the raw routes wrote before S147, with no approved month behind it, can still be corrected until the database is reseeded. A second caller of `ExportAsync` must not copy its conflict exception's message into a response: the period-identity message names the approval's employee and dates (R-G6). `/calculate-and-export` copies it (`Payroll/Program.cs:501`), but that message cannot occur there, because the route reads the approval by the request's own employee and whole month (`:296`, `:310-311`).
````

## Block 6 — QUAL-225: the S147 consequence

**Target:** `docs/operations/quality-finding-register.md` (line 449, the QUAL-225 row; the end of its "→" cell).

*Why here:* OQ-3 offered two remedies for a month before the ledger start: (i) pay it untracked, or (ii) this row's audited opening balance. The owner chose (i) for S147 and kept (ii) on the backlog (refinement `:33`, `:416`, `:617`). So this row is now the only way a flex balance from such a month could ever enter the ledger.

Old text:

````text
S148's refinement schedules it (stage 1 or later). |
````

New text:

````text
S148's refinement schedules it (stage 1 or later). **S147 (OQ-3 = (a) + (i); TASK-14715, merged `9cbf164b`, CI-verified at CI-W4, run `41708962373`):** a month before the employee's ledger start, which only the retired raw routes could pay, is now paid through `/calculate-and-export` with its flex "not tracked": no flex payout and no balance recorded. Whatever flex it held is therefore not carried into the ledger, whose first tracked month still opens at 0. This row's opening balance was the alternative remedy (ii) for that month; the owner chose (i) for S147, reversing S146's refusal, and (ii) stays here (D-8; ADR-043 D6). Until it is built, a balance that should carry from such a month has no way in. |
````

## Block 7 — HRP-003: the raw routes retired; what the admin must know now

**Target:** `docs/operations/hr-follow-up-process-register.md` (line 100, the HRP-003 row; its "Resolution + audit" cell). Three parts, all in that cell. The cell describes the process "as it stands now", so the two untrue sentences are corrected, with the earlier state kept in a dated parenthesis; part c adds the fifth thing the admin must know.

### 7a — the routes that plan a month

Old text:

````text
the four payroll routes that plan a month answer the week-check **422**
````

New text:

````text
the payroll routes that plan a month (the two calculating routes since S147; four until then) answer the week-check **422**
````

### 7b — the raw routes' split-month refusal

Old text:

````text
The raw `/api/payroll/export` and `/export-period` routes, which map every line under one profile, refuse every split month with **422** `reason: "split-month-single-profile-route"` and point to the calculating routes.
````

New text:

````text
*(Until S147 the raw `/api/payroll/export` and `/export-period` routes, which mapped every line under one profile, also refused every split month, with **422** `reason: "split-month-single-profile-route"`, and pointed to the calculating routes. TASK-14716 retired them (SEC-054, merged `169e9980`), and that reason went with them.)*
````

### 7c — a fifth thing to know

Old text:

````text
naming that predecessor, so that one is exported or corrected first.
````

New text:

````text
naming that predecessor, so that one is exported or corrected first. **A fifth since S147 (SEC-054; owner OQ-3 = (a) + (i); TASK-14715 merged `9cbf164b`, TASK-14716 merged `169e9980`):** (5) **a month first reaches payroll only from its own approved month, through `/calculate-and-export`.** The raw routes, which let one administrator send hand-written lines, are retired, so a paid month changes only through `/recalculate`, this row's path. A month before the employee's ledger start is paid like any other, with its flex "not tracked": no flex payout and no balance recorded. With pay lines it is locked, and a correction keeps its flex untracked. With none, nothing was recorded, so `/recalculate` refuses it as never exported (**422** `payroll-not-exported`), and the month is reopened and edited instead. A correction needs only the month's export record, so a month the raw routes recorded before S147 can still be corrected, although no approved month stands behind it; and a case-4b refusal naming a month "exported without a flex balance" can now only concern such a month.
````

## Block 8 — SEC-048: the raw routes' half is gone

**Target:** `docs/operations/security-finding-register.md` (line 201, the SEC-048 row; its status cell).

*Why:* the row's finding and citations name the raw routes and `BuildExportContext` (deleted). The weakness itself, the audit row's organisation taken from the request body, stands for the two routes that remain. The finding text and the citations stay as registered; the note says what holds now.

Old text:

````text
REGISTERED (S145, 2026-10-15); not fixed |
````

New text:

````text
REGISTERED (S145, 2026-10-15); not fixed. **S147 note (O-7, <CI-W4b date>):** the raw `/export` and `/export-period` are retired (SEC-054; TASK-14716, merged `169e9980`), so the weakness now concerns the two routes that remain, and is otherwise unchanged: `/calculate-and-export` puts the body's `OrgId` on the export context (`Payroll/Program.cs:464`), from which `ExportAsync` files the audit row (`PayrollExportService.cs:365-370`); `/recalculate` takes it from the request profile (`RetroactiveCorrectionService.cs:325-327`, used at `:489-492`). Lines at `94bd32be`; the next cell's citations are as registered (S145) |
````

## Block 9 — SEC-023: its floor's sibling is retired

**Target:** `docs/operations/security-finding-register.md` (line 114, the SEC-023 row; its source-of-truth cell).

*Why:* S130 raised `/api/external/send` to `GlobalAdminOnly` "matching sibling `/api/payroll/export`". That sibling is gone. The floor does not depend on it, so it stands; the note says so, so a reader does not go looking for the sibling.

Old text:

````text
sibling `Payroll/Program.cs:157`; deferred → SendAsync |
````

New text:

````text
sibling `Payroll/Program.cs:157` *(S147: that sibling, `POST /api/payroll/export`, was retired by TASK-14716, merged `169e9980` (SEC-054). This route's `GlobalAdminOnly` floor does not depend on it and is unchanged: forwarding a caller-written envelope to an outside system is a privileged integration action in its own right. The code comment that still names the sibling, `Integrations.External/Program.cs:47-48`, is a `src` comment for tidy item 25)*; deferred → SendAsync |
````

## Block 10 — DEP-004: the two raw-route rows retired

**Target:** `docs/knowledge-base/dependencies/DEP-004-endpoint-registry-ui-api-data-alignment.md` (lines 145-148, **CRLF**; one replacement over the four Payroll rows).

*Why this shape:* the registry's precedent for a removed endpoint is the Timer section (`:47-52`): the rows go and a dated "RETIRED" note says what they did and what replaced them. Its rule 3 (`:208`) asks for a status note. The two surviving rows are carried over unchanged, except that `/calculate-and-export`'s purpose says it is now the only exporting route.

Old text:

````text
| `/api/payroll/export` | POST | — (S2S) | PayrollExportLine, WageTypeMapping | PayrollExportGenerated | Map calculation result to wage lines |
| `/api/payroll/export-period` | POST | — (S2S) | PeriodCalculationResult, PayrollExportLine | PayrollExportGenerated | Batch export multiple results |
| `/api/payroll/calculate-and-export` | POST | **None yet** | PeriodCalculationResult, PayrollExportLine | PeriodCalculationCompleted, PayrollExportGenerated | Full period calculate + export (approval-guarded) |
| `/api/payroll/recalculate` | POST | **None yet** | CorrectionExportLine | RetroactiveCorrectionRequested | Retroactive correction (idempotent) |
````

New text:

````text
| `/api/payroll/calculate-and-export` | POST | **None yet** | PeriodCalculationResult, PayrollExportLine | PeriodCalculationCompleted, PayrollExportGenerated | Full period calculate + export (approval-guarded); since S147 the only route that exports |
| `/api/payroll/recalculate` | POST | **None yet** | CorrectionExportLine | RetroactiveCorrectionRequested | Retroactive correction (idempotent) |

**RETIRED (S147, TASK-14716; SEC-054).** `POST /api/payroll/export` (mapped a caller-built calculation result to wage lines) and `POST /api/payroll/export-period` (exported several results in one call) were removed, with their request types and the Orchestrator's `payroll-export` task type. They took pay lines the caller had written, with no approved month and no second person; every export now goes through `/calculate-and-export`. Both paths answer 404 (`RawExportRoutesRetiredTests`).
````

## Block 11 — `FRONTEND.md`: the compliance component also shows the "not checked" notice

**Target:** `docs/FRONTEND.md` (**CRLF**). Two parts, lines 113 and 291.

*Why:* TASK-14707 (QUAL-234 interim) made `ComplianceWarnings` show one month-level notice, "Hviletid ikke kontrolleret for N dage", and export the function that builds it, which Teamoversigt imports so both screens use the same words (`frontend/src/components/ComplianceWarnings.tsx:45-56`; `TeamOversigt.tsx:11`, `:205`). Both lines still say "warnings" only (Fable 14707 NOTE 4, cycle 1, `.claude/reviews/S147-TASK-14707-5a-reviewer.md:10`: "note the shared string lives in the component module by design").

### 11a — the component table (line 113)

Old text:

````text
| ComplianceWarnings | `ComplianceWarnings.tsx` | EU-WTD compliance warnings |
````

New text:

````text
| ComplianceWarnings | `ComplianceWarnings.tsx` | EU-WTD compliance warnings, and (S147, QUAL-234 interim) one month-level notice, "Hviletid ikke kontrolleret for N dage", for days the 11-hour rest check could not measure. The sentence is built by `restNotCheckedNotice`, exported from this module by design so Teamoversigt shows the same words; it retires when the rest check reads Skema's intervals (S148 stage 0) |
````

### 11b — the directory tree (line 291)

Old text:

````text
      ComplianceWarnings.tsx    # Domain: EU-WTD warnings
````

New text:

````text
      ComplianceWarnings.tsx    # Domain: EU-WTD warnings + the rest-not-checked notice (S147)
````

## Block 12 — Two new QUAL rows: QUAL-243 (the raw routes' leftovers) and QUAL-244 (the undated wage-type lookup)

**Target:** `docs/operations/quality-finding-register.md` (line 466, the end of the QUAL-242 row and of the file; the two rows follow it in the same continuation table, as QUAL-168 … QUAL-242 do). Columns: QUAL · what it means · title · Dim · Sev · status · source of truth · adjudication.

**Floor (Medium+: "would plausibly change behavior, block or mislead a future change, or misinform a reader", `quality-finding-register.md:6-8`).**
- **QUAL-243 clears it (M).** The `init.sql` comments misinform a reader (they explain a column by routes that no longer exist). The nullable column and the no-flex-write seam together would let a future caller write exactly the record that case 4b's raw arm exists to refuse, so they would mislead a future change.
- **QUAL-244 clears it (M), narrowly.** The closest precedent, QUAL-177 (a method only tests call), was registered at L. This one is M because the undated branch is what any new caller gets by default, and its query ignores which mapping row is in force, so it can return a closed row once a mapping has been edited.

Old text:

````text
A design task, not a fix; plan it with the Skema save model as a whole. |
````

New text:

````text
A design task, not a fix; plan it with the Skema save model as a whole. |
| QUAL-243 | **The raw payroll routes are gone, but three things that existed only for them remain: a database column that still lets an export record have no approved month, a refusal written for the records those routes left, and a way into the export service that writes a record with no flex balance.** (1) `payroll_export_records.period_id` is nullable, and `init.sql`'s comments still explain it by the raw routes ("the raw /export + /export-period endpoints bypass approval"), with the `source` values `EXPORT` / `EXPORT_PERIOD` and "a raw export" as the source of an all-null balance (`init.sql:4590`, `:4597-4600`, `:4611-4612`, `:4623`, `:4662`). Since S147 `ExportAsync` requires the period (`PayrollExportService.cs:228-231`), so the rule that every record written since TASK-14716 comes from an approved month is held by the service only, not by the table (records the raw routes wrote before S147 have no period, and `/recalculate` can still correct them). (2) Case 4b's raw arm in the flex-opening walk refuses a month whose employed previous month has a record with no balance, and tells the operator to correct that month first (`FlexLedgerOpening.cs:359-366`; the sentence `:387-390`). At or after the ledger start such a record came only from the raw routes: a case-3a record always lies before the ledger start, which never moves (`:340`). So the arm can meet only records written before S147. (3) `ExportAsync` still accepts a context with no flex-ledger write: the record gets null balances, the balance-triple compare and the manifest check are skipped, and a call with no lines returns success without connecting (`PayrollExportService.cs:237`, `:246-249`, `:325-330`, `:394`). The one route always sends a flex-ledger write (`Program.cs:441`, `:470`), so only direct test calls reach it. Together, (1) and (3) would let a future caller write the record that (2) exists to refuse. | Raw-route leftovers: nullable period_id, case 4b's raw arm, the no-flex seam | D-domain | M | **REGISTERED (S147 O-7, <CI-W4b date>; TASK-14716 reviews, Fable c1 N-4 and c2 N-4; Orchestrator ruling R-G3 and its cycle-2 fold).** Not fixed in S147: the column change is a schema change outside S147's ruled scope (R-G3) | `docker/postgres/init.sql:4590, :4597-4600, :4611-4612, :4623, :4662`; `docs/generated/db-schema.md:1378`; `FlexLedgerOpening.cs:340, :359-366, :387-390`; `PayrollExportService.cs:228-231, :237, :246-249, :325-330, :394, :648, :902`; `Payroll/Program.cs:441, :470` (all at `94bd32be`); SEC-054; ADR-034 amendment <CI-W4b date> | → one change, under the standing "reseed, don't migrate": `period_id` NOT NULL with the comments rewritten and `db-schema.md` regenerated; case 4b's raw arm and its sentence deleted (after a reseed no record without a balance can exist at or after the ledger start); `ExportAsync` requires the flex-ledger write, or its null branch is deleted, with the direct-call test seeds re-pointed. Each deletion keeps or replaces its pins. |
| QUAL-244 | **The wage-type lookup's undated path has no production caller, yet it is what a caller gets by default, and it ignores which mapping row is in force.** `PayrollMappingService.GetMappingAsync` takes an optional `asOfDate`. With a date it reads the mapping in force on that date (`PayrollMappingService.cs:51-62`; ADR-020 D1); payroll always passes one, per segment (`PeriodCalculationService.cs:3951-3953`). Without a date it queries `wage_type_mappings` with no effective-date condition (`PayrollMappingService.cs:64-102`), so once a mapping has been edited and has a closed row beside its open one, it can return either. Its last production caller, `MapCalculationResultAsync`, served only the raw export routes and was deleted with them in S147 (TASK-14716 fix-up 1, ruling R-G1). Only `WageTypeMappingRegressionTests` still calls it (ten calls, `:156-293`), so those tests pin a path nothing in production runs, and a new caller who leaves out the optional argument gets an undated lookup. | Wage-type lookup's undated default path is test-only | D-domain | M | **REGISTERED (S147 O-7, <CI-W4b date>; TASK-14716 fix-up 1 report; Orchestrator ruling R-G7, confirmed by Codex c1).** Not fixed in S147: beyond SEC-054's scope. M rather than QUAL-177's L (a method only tests call), because this path is the default for any new caller and can return a mapping row that is not in force | `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollMappingService.cs:42-125`; `PeriodCalculationService.cs:3951-3953` (the one production call, dated); `tests/StatsTid.Tests.Regression/WageTypeMappingRegressionTests.cs:156-293` (all at `94bd32be`); `docs/generated/db-schema.md` § `wage_type_mappings` (`effective_from`, `effective_to`); ADR-020 Implications §7 | → make `asOfDate` required and delete the undated branch, re-pointing `WageTypeMappingRegressionTests` at a date (the dated read keeps the same position fallback, `PayrollMappingService.cs:46-50`); or keep the branch and restrict it to the open row (`effective_to IS NULL`), with a fact for an edited mapping. The first is smaller. |
````

## Block 13 — ADR-016: the S145 D4 amendment still lists four planning routes and the raw routes' split-month refusal

**Target:** `docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md` (LF; line 89, the operative paragraph of the D4 amendment of 2026-10-19, S145 Stage B).

*Why:* this paragraph is the decision's current statement of what still stops a payroll month. It gives the week check's 422 "on each of the four routes that plan a month, `/calculate-and-export`, `/recalculate`, `/export` and `/export-period`", and the raw routes' own refusal of a split month (422 `split-month-single-profile-route`). Since TASK-14716 two routes plan a month, and that refusal was deleted with the raw routes (fix-up 1, R-G1; `git grep split-month-single-profile-route` over `src` at `94bd32be` is empty). A dated pointer at the end of the sentence keeps the S145 text. **Line 97 needs no pointer:** it is D5's original in-scope list (S20: "Planner introduced; OK-boundary calculations route through it for named entry points (`/calculate-and-export`, `/export-period`, `RetroactiveCorrectionService`)"), a record of what that decision set out to build, not a statement of how the system works now.

Old text:

````text
and the months-in-order 409 on the calculating routes.
````

New text:

````text
and the months-in-order 409 on the calculating routes. *(S147, <CI-W4b date>: TASK-14716 retired `/export` and `/export-period` (SEC-054), so two routes now plan a month, `/calculate-and-export` and `/recalculate`, and the raw routes' `split-month-single-profile-route` refusal was deleted with them; see ADR-034's amendment of that date.)*
````

## Block 14 — The audit catalog's `PayrollExportGenerated` row: null flex keys also come from a case-3a calculate-and-export

**Target:** `docs/operations/audit-projection-catalog.md` (LF; line 119, the `PayrollExportGenerated` row's history cell).

*Why:* the row says the three flex keys are "left out when null, as on a raw export". The raw routes are retired, and since TASK-14715 a month before the ledger start exported with lines through `/calculate-and-export` also writes its record and event with null balances, so the keys are left out there too. The mapper writes no nulls (`src/Infrastructure/StatsTid.Infrastructure/AuditMappers/PayrollExportGeneratedAuditMapper.cs:32-33`), which also means `periodId` is absent only on events the raw routes wrote before S147. The same phrase in the mapper's comment is on tidy item 25 (`SPRINT-147.md:405`, `:409`).

Old text:

````text
each left out when null, as on a raw export.
````

New text:

````text
each left out when null, as on a raw export. *(S147: the raw routes are retired (TASK-14716, SEC-054). Since TASK-14715 the keys are also left out for a month before the employee's ledger start exported with lines through `/calculate-and-export`, whose flex is not tracked; and `periodId`, set on every event written since TASK-14716, is absent only on events the raw routes wrote before.)*
````

---

## Beyond the owed list (optional; each with a lean)

### X1 — `SECURITY.md`: refresh the payroll checkpoint's pointer · lean: apply (pointer only)

**Why:** rule 2's payroll leg cites `PayrollExportService.cs:404-444`. At `94bd32be` the two four-eyes checks are at `:544-553`, inside the B2 method `:487-557`.

**Target:** `docs/SECURITY.md` (line 188).

Old text:

````text
(`PayrollExportService.cs:404-444`; a null sender passes)
````

New text:

````text
(`PayrollExportService.cs:544-553`, inside the B2 re-assert `AssertPeriodApprovedForUpdateAsync`, `:487-557`, at `94bd32be`; a null sender passes)
````

### X2 — SEC-053: its "raw exports" exclusion is gone · lean: apply

**Why:** SEC-053's closing note records the raw exports as an exclusion from four-eyes "(SEC-054, TASK-14716)". They no longer exist, so the only exclusion left is corrections.

**Target:** `docs/operations/security-finding-register.md` (line 213, the SEC-053 row; its status cell).

Old text:

````text
Recorded exclusions: the raw exports (SEC-054, TASK-14716) and retroactive corrections (own path)
````

New text:

````text
Recorded exclusions: the raw exports (SEC-054, TASK-14716) and retroactive corrections (own path). *(S147, O-7: the raw exports were retired by TASK-14716, merged `169e9980`; every export made since TASK-14716 passes the payroll checkpoint, because `ExportAsync` requires the period, `PayrollExportService.cs:228-231`, `:544-553` at `94bd32be`. Retroactive corrections remain the one exclusion: they need only an existing export record, so they can still reach a record the raw routes wrote before S147, which has no approved month behind it.)*
````

### X3 — QUAL-200: "the raw routes stay parked at QUAL-183" · lean: apply

**Why:** the row's residual chain A (a raw-exported month that stays DRAFT) rested on the raw routes, which "stay parked at QUAL-183". They are retired and QUAL-183's question is answered.

**Target:** `docs/operations/quality-finding-register.md` (line 424, the QUAL-200 row; the end of its "→" cell).

Old text:

````text
The raw routes themselves stay parked at QUAL-183. |
````

New text:

````text
The raw routes themselves stay parked at QUAL-183. *(S147: retired by TASK-14716, merged `169e9980` (SEC-054; QUAL-183's follow-up answered). Chain A now arises only for a month the raw routes recorded before S147: every new export record comes from an APPROVED month, which locks both write paths.)* |
````

### X4 — HRP-024: the R16 case's second way out is gone · lean: apply

**Why:** the row says the next month waits "until M−1 is approved after the fact, or until the next month also goes through the raw route". The second way no longer exists, and the case itself can arise only from a month the raw routes recorded before S147.

**Target:** `docs/operations/hr-follow-up-process-register.md` (line 123, the HRP-024 row; its "Trigger" cell).

Old text:

````text
the next month answers the 409 until M−1 is approved after the fact, or until the next month also goes through the raw route.
````

New text:

````text
the next month answers the 409 until M−1 is approved after the fact, or until the next month also goes through the raw route. *(S147: the raw routes are retired (SEC-054; TASK-14716, merged `169e9980`), so only the first way remains, and the case arises only from a month the raw routes recorded before S147.)*
````

### X5 — ADR-040's amendment: the census's pending retirement has merged · lean: apply (a pointer inside the dated text)

**Why:** ADR-040's amendment of 2026-11-03 gives the census with the raw Payroll routes as something "TASK-14716 retires". The same fact as Block 5a; a bracketed pointer keeps the dated text and says it has happened.

**Target:** `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` (line 407).

Old text:

````text
and Payroll `/export` and `/export-period`, which TASK-14716 retires);
````

New text:

````text
and Payroll `/export` and `/export-period`, which TASK-14716 retires [merged `169e9980`, <CI-W4b date>: all 19 are now closed; see ADR-034's amendment of that date]);
````

### X6 — `FRONTEND.md`: ProjectPicker was deleted in S72 · lean: apply (tidy item 22's docs half, adjacent to Block 11)

**Why:** tidy item 22 (`SPRINT-147.md:393`) records that `FRONTEND.md:114` and `:292` still list `ProjectPicker`. The file was deleted in S72 (`c91f63d2`; absent from `frontend/src/components` at `94bd32be`). The table's precedent for a deleted component is the struck-through FlexBalanceCard row (`:110`); the directory tree lists only files that exist.

**Target:** `docs/FRONTEND.md` (**CRLF**; lines 114 and 292-293). Two parts.

**X6a** Old text:

````text
| ProjectPicker | `ProjectPicker.tsx` | Project selector for work-time rows |
````

New text:

````text
| ~~ProjectPicker~~ | — | Deleted in S72 (the Skema redesign, `c91f63d2`) |
````

**X6b** Old text:

````text
      ProjectPicker.tsx         # Domain: project selector
      TimeEntryForm.tsx         # Domain: legacy (unrouted)
````

New text:

````text
      TimeEntryForm.tsx         # Domain: legacy (unrouted)
````

### X7 — ADR-020 Implications §7: the method it names was deleted · lean: apply (pointer)

**Why:** §7 says `MapCalculationResultAsync` "stays current-row". The method was deleted with the raw routes (R-G1), and the current-row branch it used is now QUAL-244.

**Target:** `docs/knowledge-base/decisions/ADR-020-versioned-config-design-foundations.md` (line 116; this line is CRLF).

Old text:

````text
7. **MapCalculationResultAsync stays current-row** (carried forward from deferred refinement Open Q5): only `MapSegmentToExportLinesAsync` (the export-time path) gets `asOfDate=segmentStart`; `MapCalculationResultAsync` (per-line-date path) stays current-row per ROADMAP L350 "no retroactive recomputation."
````

New text:

````text
7. **MapCalculationResultAsync stays current-row** (carried forward from deferred refinement Open Q5): only `MapSegmentToExportLinesAsync` (the export-time path) gets `asOfDate=segmentStart`; `MapCalculationResultAsync` (per-line-date path) stays current-row per ROADMAP L350 "no retroactive recomputation." *(S147: `MapCalculationResultAsync` was deleted with the raw export routes, its only callers (TASK-14716 fix-up 1, ruling R-G1). The current-row branch of `PayrollMappingService.GetMappingAsync` that it used now has no production caller (QUAL-244).)*
````

### X8 — ROADMAP: the raw routes' split-month refusal · lean: apply

**Why:** the S145 Stage B entry says "the raw export routes refuse split months by their own reason". The routes and the reason are gone.

**Target:** `ROADMAP.md` (line 341).

Old text:

````text
and the raw export routes refuse split months by their own reason.
````

New text:

````text
and the raw export routes refuse split months by their own reason *(retired in S147 with that reason: TASK-14716, SEC-054)*.
````

---

## Not drafted here (for other O-items or the tidy pass)

- **Tidy item 25 misses one `src` comment.** `src/Integrations/StatsTid.Integrations.External/Program.cs:47-48` still calls `/api/payroll/export` "its sibling". It is not in item 25's list (`SPRINT-147.md:409`). The register side is Block 9.
- **The tender analysis (O-12)** describes the raw routes as live in rows the build changed: the defect table's D2 (`docs/references/tender-requirements-analysis.md:98`), the payroll baseline paragraph (`:280`), EM A38 (`:376`), EM §8.3 (`:403`), VD 35 (`:412`) and the summary (`:454`). O-12 re-rates only where the build changed a rating.
- **DEP-004's External row** (`:154`) gives `/api/external/send`'s caller as "Payroll service". In `src` its only caller is the Orchestrator's `TaskDispatcher` (`src/Orchestrator/StatsTid.Orchestrator/Services/TaskDispatcher.cs:19`, `:45`). This drift predates S147 (the registry records its own drift at `:172-174`), so it is not drafted.
- **The sprint log** (the O-7 "done" record, the Next-action row) and the close-time items are the Orchestrator's.

## Brief contradictions, deviations and open points

**Claims about the existing code (checked; the code governs):**
1. **"The census sentence '17 → 19 guarded'"** is not a sentence in any document at `94bd32be`; it is the Next-action row's shorthand (`SPRINT-147.md:425`). Two texts still describe TASK-14716's retirements as pending: `docs/SECURITY.md:128` (O-6 Block 6, "until it merges …") and ADR-040's amendment `:405-407` ("which TASK-14716 retires"). The census counts do not change (84 = 32 + 19 + 33); what changes is that all 19 are closed (17 were, from TASK-14710's merge to TASK-14716's), and the live inventory is 80 write routes in the three hosts. Verified by the census command at `94bd32be`: 88 routes in `src`, Backend 76, Payroll 3, Orchestrator 1, External 1, Rule Engine 7 (the plan's 92 was 78 / 5 / 1 / 1 / 7, `PLAN-s147.md:232`). Block 5a, and X5 for ADR-040.
2. **The `init.sql` comment sites.** The brief names `:4597`, `:4623` and `:4662`. The same comment block also explains things by the raw routes at `:4590` ("… AND raw /export + /export-period"), `:4598-4600` (the dead `source` values `EXPORT` / `EXPORT_PERIOD`) and `:4611-4612` (all-null balances = "a raw export, or a record written before the ledger"). QUAL-243 cites all of them.
3. **`docs/FRONTEND.md` "amended at O-4/O-7"** (`SPRINT-147.md:166`, `:364`): O-4 did not do it. `20a78197` changed four other files, and no S147 commit touched `FRONTEND.md` (its last change is `dc9ad254`, S146). Drafted as Block 11.
4. **`docs/SECURITY.md:194`** ("That is SEC-054, handled by TASK-14716") is untrue now and is not in the brief's list. Drafted as Block 5b, which is also where R-G6's rule lands.
5. **The deleted-fact count.** The sprint log and the merge commit say 15 facts guarded only the raw routes (`SPRINT-147.md:217`; `169e9980`'s message). The diff `9cbf164b..94bd32be` removes 18 test methods: 15 Regression and 3 Unit, one of the Unit facts replaced by `GetServiceUrl_PayrollExport_IsNoLongerARoute`. The TASK-14716-T report's header says 15 but lists 18 names. The drafts avoid the number and point to `75b463b7`, which names the files.
6. **Confirmed as the brief states:** `Payroll/Program.cs:501` (`error = ex.Message`, in the catch at `:491-503`; the route is `LocalAdminOrAbove`, `:536`); `FlexLedgerOpening.cs:359-366`, `:387-390`; `WageTypeMappingRegressionTests.cs:156-293` (ten calls); DEP-004 `:145-146`; ADR-034 `:15`, `:26`, `:29`; `PayrollExportService.cs` has one production caller (`Program.cs:470`), guarded by `flexWrite is not null` (`:441`).
7. **More stale "raw route" text than the brief lists:** SEC-053's exclusion (`security-finding-register.md:213`), QUAL-200 (`:424`), HRP-024 (`hr-follow-up-process-register.md:123`), ADR-020 §7 (`:116`) and `ROADMAP.md:341`. These are X2, X3, X4, X7 and X8. The External comment and DEP-004's External row are under "Not drafted here". *(Draft 2:)* Codex cycle 1 found one more that draft 1 missed, ADR-016's operative S145 D4 amendment (`:89`); it is now Block 13, owed rather than optional because it states the current refusals. Fable cycle 1 moved the audit catalog row out of "Already done" (Block 14).

**Claims about required behaviour:** none contradicted. Every statement in Blocks 1–12 follows the owner's rulings (OQ-3 = (a) + (i), R-G9) and the Orchestrator's R-G1..R-G10 as the sprint log and the TASK-14716 report record them.

**Needs a ruling (the Orchestrator's; none for the owner):**
- **R-1, where R-G6's residual lives. Lean: a residual on SEC-054's closing entry (Block 1a) plus a rule in `SECURITY.md` (Block 5b), and no new row.**
  - *For the lean:* nothing reaches the leak today. `/calculate-and-export` reads the approval by the request's own employee and whole month (`Program.cs:296`, `:310-311`), so the period-identity message cannot occur there. The hazard needs a future change (a second caller of `ExportAsync`). The register's rows are weaknesses that exist and are re-attacked; this is a rule for the next route's author, and that author reads `SECURITY.md`.
  - *The alternative:* a new row, SEC-058, Low ("the export conflict 409 copies the exception text"). It is more visible in a register sweep, at the cost of a row for something unreachable. SEC has no stated registration floor (unlike QUAL's Medium+), so either is allowed. QUAL is the wrong register, because this is a disclosure concern.
- **R-2, QUAL-244's severity. Lean: M.** If the Orchestrator reads it as L, like QUAL-177, it falls below the QUAL floor and belongs only in the inventory appendix (`quality-finding-register.md:367`); in that case drop its row from Block 12. R-G7 itself ruled it a register candidate.
- **R-3, the amendment's date.** Lean: the CI-W4b date, as O-6 dated ADR-040's amendment by CI-W3b's day. If the docs are applied on a later day, the Orchestrator may prefer the apply date; the token is the same everywhere, so one substitution covers it.

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-04 | `planner` (Opus 5.5) | First draft at `94bd32be`: Blocks 1a–12 for every O-7 item (SEC-054 closed; QUAL-183's follow-up; the ADR-034 amendment with pointers and its INDEX row; the census and the segregation-of-duties exclusion in `SECURITY.md`; QUAL-225, HRP-003, SEC-048, SEC-023, DEP-004; `FRONTEND.md`; QUAL-243 and QUAL-244); X1–X8 beyond the list; "Already done" with evidence; seven code-claim reports; three Orchestrator rulings proposed (R-1..R-3) |
| 2 | 2026-11-04 | `planner` (Opus 5.5) | Cycle-1 findings folded, still at `94bd32be` (Codex 0 B / 2 W / 1 N; Fable NOT READY, 0 B / 1 W / 6 N). **Codex W1:** a correction needs only an existing export record (`RetroactiveCorrectionService.cs:211-223`), so a pre-S147 raw record can still be corrected (`FlexLedgerHostTests.cs:749`); the guarantee is now "every export made since TASK-14716" in 1a, 3e (the heading, the period bullet and the corrections residual), 5b, 7c, 12 (QUAL-243) and X2, with the historical exception stated in each. **Codex W2:** new Block 13, a dated pointer on ADR-016's operative S145 D4 amendment (`:89`); `:97` is D5's original S20 scope list and gets none. **Fable W-1:** the identity facts are "five: three pin one clause each; two are refused by both date clauses" (`PayrollExportPeriodIdentityTests.cs:34-41`). **Codex NOTE and Fable N-1:** "before its status or approver is checked (all three are read in the one `SELECT … FOR UPDATE`)". **Fable N-2:** new part 3f, a pointer on ADR-034's D4 note (`:37`). **Fable N-3:** the audit catalog row moved from "Already done" to new Block 14, naming the case-3a source. **Fable N-5:** B2 glossed at first use in the amendment (and in 1a's citation), R-17 and R-F1 glossed. **Fable N-6:** Block 11's provenance is cycle 1's `S147-TASK-14707-5a-reviewer.md:10`. Fable N-4 (the sprint log's "15") left to the Orchestrator, as ruled. Count: 24 owed replacements and 9 optional (33). Fable confirmed R-1 (R-G6 as a residual on SEC-054 plus a `SECURITY.md` rule) and QUAL-243/244 at M; R-1..R-3 remain the Orchestrator's to rule |
| 2.1 | 2026-11-04 | Orchestrator | Fable c2 NOTEs folded: 7c's lead says a month *first* reaches payroll only from its own approved month; the plain-words promise narrowed to blocks that state what a correction can reach; FlexLedgerHostTests.cs:748 → :749 (the method). Codex c2 CLEAN, Fable c2 READY (APPROVED). |
