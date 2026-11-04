# ADR-034 — Payroll-export lock + monthly-export idempotency

| Field | Value |
|-------|-------|
| **Status** | accepted |
| **Sprint** | S90 |
| **Domains** | Payroll Integration, Backend, Data Model, Infrastructure, Frontend |
| **Tags** | payroll-export, export-lock, idempotency, reopen, corrections-manifest, cross-context-read, event-sourcing, monthly-time |
| **Supersedes / amends** | Phase 2 of the "reopen until sent to payroll" decision (S89 = Phase 1, the leader-reopen FE gate). Builds on ADR-013 (retroactive corrections, single-period no-cascade), ADR-018 (transactional outbox), ADR-026 (audit projection). DISJOINT from ADR-033 (vacation/termination settlement — different tables, different keys). |

## Context

A leader can reopen an APPROVED monthly time registration (S89). The owner's rule: a leader may reopen **until the month is sent to payroll**, after which it locks (corrections only). But the system had **no per-(employee, month) "sent to payroll" signal**: `approval_periods` carried no export state; `POST /api/payroll/calculate-and-export` wrote a delivery envelope to `outbox_messages`, emitted no domain event (`PayrollExportGenerated` was a registered-but-never-emitted placeholder), and wrote nothing back; and it had **no idempotency** — every call minted a fresh export, so an APPROVED month could be exported (and duplicated to payroll) repeatedly, reopen or no reopen. The export lives in the **Payroll** service; the reopen in the **Backend**; both share one Postgres DB.

Owner rulings (S90): OQ-1 lock at **export committed** (not delivered); OQ-2 **corrections only** post-lock (no reopen for any role, no admin recall); OQ-5 **one-transaction** atomic refactor; OQ-6 the raw `/export`+`/export-period` under the lock+idempotency. *(OQ-6 superseded S147: both raw routes are retired; see "Amendment 2026-11-04" below.)*

## Decision

### D1 — One Payroll-owned record = lock + idempotency + corrections-manifest
A new `payroll_export_records` table (Payroll context; regular monthly time, NOT settlement): `export_id` PK, nullable `period_id`, `employee_id`, `year`, `month`, `exported_at`, `original_lines JSONB` (immutable manifest as first exported), `current_effective_lines JSONB` (the evolving correction baseline — D5), `content_hash`, `source`; **UNIQUE `(employee_id, year, month)`**. The row's existence = "this month is sent to payroll / locked." The same row serves three jobs: the lock (existence), idempotency (the unique key + content_hash), and the corrections manifest (the lines).

### D2 — Atomic export; the lock is a "durable handoff record", not "delivered" (OQ-1/OQ-5)
`PayrollExportService.ExportAsync` is refactored to inject `IOutboxEnqueue` + the audit deps and run ONE `(conn, tx)`: `GuardSettlementLineDelivery` FIRST (fail-closed ordering preserved) → group lines by `(employee_id, year, month)` (D3) → per group, `FOR UPDATE`-re-lock the approval row + re-assert `status='APPROVED'` (D4 race) → INSERT `payroll_export_records` → emit `PayrollExportGenerated` to **`outbox_events`** (the ADR-018 canonical event outbox, NOT the `outbox_messages` delivery envelope) → write the ADR-026 audit row → COMMIT. The lock is set at COMMIT (OQ-1 "export committed"). **There is NO background dispatcher for `outbox_messages WHERE destination='payroll'`** (the only poller is `destination='external'`), so the actual mock-SLS send stays the EXISTING synchronous best-effort HTTP POST + status update, POST-commit; the lock does NOT depend on delivery. The event is REUSED from the vestigial `PayrollExportGenerated` (reshaped payload — replay-safe, it was never emitted; a 2nd live payroll-export event would be rot), now a real audited event (Infrastructure `IAuditProjectionMapper` + Payroll DI + catalog row; the catalog "defined-but-unemitted TBD" resolved).

*(Amended S147, see "Amendment 2026-11-04" below: an export is one (employee, year, month), so there is no grouping step; the approval period is required, so the `FOR UPDATE` re-assert of APPROVED, with the four-eyes checkpoint, runs on every export; and it first checks that the period is the exported employee's whole calendar month.)*

### D3 — Idempotency by content (OQ-6); multi-period exports
On the UNIQUE `(employee_id, year, month)` conflict: **same content_hash → no-op** (return the existing export, no duplicate record/event/envelope); **different content_hash → a discriminated 422/409** "already exported for {month}; use a correction." The raw `/export`+`/export-period` (which bypass approval and flatten multiple periods/months into one call) GROUP lines by `(employee_id, year, month)` and write one record per tuple, all-or-nothing; a line spanning calendar months is rejected. A ZERO-line month writes NO record (nothing handed off → stays reopenable). *(Amended S147, see "Amendment 2026-11-04": the raw routes are retired and an export is one (employee, year, month), so nothing is grouped; a month before the ledger start with no lines and no record writes no record, by this rule.)*

**D3 amendment 2026-10-24 (S146 TASK-14605, merged `8f366802`; rulings R-3, R-16, R-47; QUAL-188; the owner informed, OQ-4):** on the two *calculating* routes (`/calculate-and-export`, `/recalculate`) the month's record also carries the flex balance the month opened and closed at, and where the opening came from (`opening_balance`, `closing_balance`, `balance_provenance` ∈ `LEDGER_START | CHAINED | REHIRE`). The next month opens from that closing; nobody sends an opening any more. Three consequences, each following D3's own reason ("nothing handed off → stays reopenable"):
1. **An employed month with zero lines writes its record** (lines empty, balances set), because its closing balance *is* handed off to the next month. It is therefore locked like any exported month (D5). A month with no EMPLOYED day settles no flex and still writes no record. The raw routes carry no balance and keep D3 unchanged. *(S147: the raw routes are retired. A record with no balance now also comes from a month before the ledger start exported with lines through `/calculate-and-export`; see "Amendment 2026-11-04".)* Its `PayrollExportGenerated` names the calculation's manifest (S146 Step 7a, R-54), since no line carries it.
2. **The calculating routes accept only one whole calendar month** (400 `payroll-period-not-a-month`, before any read), because the record's closing is the *month's* closing (R-47). A legacy `WEEKLY` approval can no longer be sent through `/calculate-and-export`; it was already unusable there (the second week answered 409 and locked the month).
3. **The same-hash no-op on the calculating routes also compares the triple** (opening, provenance, closing), so a re-run whose lines match but whose balance moved answers 409 instead of silently keeping a stale balance (R-16).
   *Note (S146 Step 7a cycle 2, N-6): each export line carries its calculation's manifest id, and the content hash covers it. So a re-run of a month WITH lines always has a different hash and answers the "use a correction" 409; the same-hash no-op fires only for an employed zero-line month. This is safe (corrections-only) and predates S146; recorded, not changed.*

### D4 — Cross-context READ contract; the export↔reopen race
The Backend reopen **READS** `payroll_export_records` (never writes it — the Payroll context owns it; this honors the ADR-033 precedent of Backend-owns-`approval_periods` / Payroll-owns-the-export-artifact, with a read-only cross-context contract rather than a cross-context write or an eventual-consistency projection). The export↔reopen race is closed by BOTH sides taking the approval row's `FOR UPDATE` lock: the export re-asserts APPROVED under it before inserting the record; the reopen, INSIDE its advisory-locked transaction (after `AcquireTreeLockForEmployeeAsync`, before the conditional status UPDATE), takes the same row lock then reads the lock — so an export-commit and a reopen cannot interleave on the same period.

**D4 note 2026-10-24 (S146 TASK-14605, merged `8f366802`; rulings R-2, R-15, R-5):** the export transaction (all three export routes [one since S147; see "Amendment 2026-11-04"]) and the correction transaction now take a per-employee advisory lock, `pg_advisory_xact_lock(hashtext('payroll-ledger-' || employee))`, as their **first** statement (all keys in one statement, ordered), then the approval rows, then the record. The Backend reopen's leader arm takes its org advisory locks, then the approval row; its employee arm takes only the approval row. The keys are distinct by name (`payroll-ledger-` vs the `employee-` and org keys). `hashtext` is 32-bit, so a collision is possible. A single collision is harmless: it only serializes two unrelated transactions, and no wait cycle can form, because every writer takes its advisory keys before any row lock (the reopen's leader arm first does a plain read, then takes its `reporting-org-` and `unit-org-` advisory keys before its approval-row lock; its employee arm takes no advisory key and starts with the approval-row lock). A cycle among advisory locks alone would need two simultaneous collisions in opposite orders; Postgres's deadlock detector would then cancel one transaction, which is safe because both writers are transactional (S146 Step 7a, R-61(a), corrected wording). The ledger lock serializes the export, the correction and the re-derivation of the opening under the lock; a correction of M therefore derives its opening from M−1's recorded closing, and M−1 is exported and cannot be reopened (D5). This closes QUAL-201 by construction. Residual (S146 Step 7a, R-57): the race still exists for the ledger-start month and for any month before it, because a raw-recorded month corrected with its flex untracked (R-14 case 3b) derives nothing from M−1. It is bounded as before: M is exported, so the reopen writes M's `PRIOR_MONTH_REOPENED` worklist row (HRP-001b).

**D4 note 2026-10-29 (S147 TASK-14700 + 14700-T, merged `f78ddba8`, CI-verified at CI-W1, run `40839583193`; QUAL-229, R-2 with R-11..R-14).** The opening's derivation no longer trusts the month before alone. Under the same ledger lock, both at admission and in the recheck, payroll walks the employee's recorded chain from the ledger start S to P = M−1. A pure link function (`FlexLedgerOpening.DeriveLink`) evaluates each link once. If any recorded month's (opening, provenance) no longer follows from its predecessor's recorded closing, the month is refused with **409** `{kind:"payroll-prior-month-stale", month}`, naming the **earliest** such month and writing nothing. Unrecorded and raw months are not compared; a month with no employed day is skipped and its successor compared; REHIRE boundaries are respected; provenance is part of the compare, so a balance that matches with a different provenance is stale. The windows read now spans `[min(S, P).start, M.end]`. *The guarantee, as the code states it:* recalculating the named month never refuses itself, because the walk stops before it. But a month named because its own employed predecessor has no ledger record answers the case-4b refusal naming that predecessor, which is the operator's next step. A stale chain first seen in the under-lock recheck answers `payroll-ledger-changed`, and the calculation's manifest stays, as in every S146 export-time recheck. This closes the last silent way a wrong figure entered the chain (QUAL-229; the operator's sequence is HRP-003 point 4).

### D5 — Reopen lock + corrections-only (OQ-2); the manifest evolves
When `payroll_export_records` has a row for the period's `(employee, year, month)`, the reopen endpoint returns a discriminated **409** `{kind:"payroll-locked", reason:"Måneden er sendt til lønkørsel — brug en korrektion."}` for **ALL roles** (leader, HR, GlobalAdmin — no recall). The only post-lock path is the ADR-013 retroactive correction (`/recalculate`, GlobalAdmin), which reads its diff baseline from `current_effective_lines` (NOT caller-supplied) and **updates** `current_effective_lines` to the corrected state in the same tx — so a 2nd correction diffs against the 1st correction's result, not the original (avoiding double-counting); `original_lines` stays immutable. A `/recalculate` of a never-exported month is cleanly rejected ("ikke sendt til lønkørsel — genåbn i stedet"). The leader Teamoversigt surfaces `payrollExported` per row (a batched read) and hides the Genåbn control for locked rows, showing a "Sendt til lønkørsel" indicator instead.

## Consequences

- A month sent to payroll is immutable-by-reopen; fixes go through corrections (a delta to payroll), never a duplicate full export. The pre-existing duplicate-export gap is closed by D3.
- Post-lock correction authority is **GlobalAdmin** (the `/recalculate` auth), NOT the Leader who could reopen pre-lock — an accepted consequence of OQ-2 (corrections-only, no recall).
- The lock is "durable handoff record written," not "payroll confirmed receipt" — anticipatory, correct for the current no-real-SLS-delivery posture; a future "delivered"-refinement is additive on the event.
- New table (66→ tables); `payroll_export_records` is regular-monthly and disjoint from the ADR-033 settlement tables (`settlement_export_lines`/`settlement_payroll_inbox`, keyed per entitlement-year).
- Step-0b dual-lens caught 4 BLOCKERs pre-code (the outbox conflation, the export↔reopen TOCTOU, the manifest-after-correction double-count, the multi-period `/export-period`), all resolved here.
- **Cross-reference (S145, 2026-10-20; not an amendment).** Two S145 rulings build on D4 and D5. **R5(c):** reopening month M while M+1 is already exported (and M+1 opens mid-week) writes a non-blocking `PRIOR_MONTH_REOPENED` worklist row on M+1, in the reopen's own transaction after the D4 lock read (HRP-001b); and an export of M+1 now takes M's approval row `FOR SHARE` and rechecks APPROVED, so the two cannot interleave (TASK-14512 Do 7). **Q7 (b):** a week crossing a month boundary is paid by the month holding its Sunday, so a `/recalculate` of M (D5's only post-lock path, which diffs M alone) names M+1 in its response, event and audit row when M+1 is exported (HRP-003; option (a), an automatic worklist row on M+1, is QUAL-213). Detail in `docs/sprints/SPRINT-145.md`.

## Amendment 2026-11-04 — S147: every new export comes from its own approved month (SEC-054; owner rulings OQ-3 = (a) + (i) and R-G9)

**Why this exists, in plain words.** In S90 the owner brought the two "raw" routes, `POST /api/payroll/export` and
`/export-period`, under the lock and the idempotency (OQ-6), without asking whether they should exist. They took pay
lines the caller had written and sent them to payroll with no approved month behind them and no second person, so one
GlobalAdmin could pay any employee any month alone (SEC-054, found by the tender gap analysis of 2026-10-26). Nothing in
the product called them, but they were the only way to pay one kind of month: a month before the employee's first
recorded flex month (S146 R-14's "case 3a"), which the calculating route refused. The owner ruled (S147 OQ-3,
2026-10-27) to retire both, and first to pay that month through the calculating route with its flex "not tracked"
(remedy (i)). It was built in two steps, both merged: TASK-14715 (`9cbf164b`; Docker facts CI-verified at CI-W4, run
`41708962373`) and TASK-14716 (`169e9980`; CI-W4b, run `41825042200`). D1, D4's race argument and D5 stand. What
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
