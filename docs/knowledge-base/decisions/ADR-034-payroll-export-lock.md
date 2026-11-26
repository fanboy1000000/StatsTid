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

**D4 note 2026-10-24 (S146 TASK-14605, merged `8f366802`; rulings R-2, R-15, R-5):** the export transaction (all three export routes [one since S147; see "Amendment 2026-11-04"]) and the correction transaction now take a per-employee advisory lock, `pg_advisory_xact_lock(hashtext('payroll-ledger-' || employee))`, as their **first** statement [the **second** since S148, after the shared pay-inputs lock; see the D4 note of 2026-11-23] (all keys in one statement, ordered), then the approval rows, then the record. The Backend reopen's leader arm takes its org advisory locks, then the approval row; its employee arm takes only the approval row. The keys are distinct by name (`payroll-ledger-` vs the `employee-` and org keys). `hashtext` is 32-bit, so a collision is possible. A single collision is harmless: it only serializes two unrelated transactions, and no wait cycle can form, because every writer takes its advisory keys before any row lock (the reopen's leader arm first does a plain read, then takes its `reporting-org-` and `unit-org-` advisory keys before its approval-row lock; its employee arm takes no advisory key and starts with the approval-row lock). A cycle among advisory locks alone would need two simultaneous collisions in opposite orders; Postgres's deadlock detector would then cancel one transaction, which is safe because both writers are transactional (S146 Step 7a, R-61(a), corrected wording). The ledger lock serializes the export, the correction and the re-derivation of the opening under the lock; a correction of M therefore derives its opening from M−1's recorded closing, and M−1 is exported and cannot be reopened (D5). This closes QUAL-201 by construction. Residual (S146 Step 7a, R-57): the race still exists for the ledger-start month and for any month before it, because a raw-recorded month corrected with its flex untracked (R-14 case 3b) derives nothing from M−1. It is bounded as before: M is exported, so the reopen writes M's `PRIOR_MONTH_REOPENED` worklist row (HRP-001b).

**D4 note 2026-10-29 (S147 TASK-14700 + 14700-T, merged `f78ddba8`, CI-verified at CI-W1, run `40839583193`; QUAL-229, R-2 with R-11..R-14).** The opening's derivation no longer trusts the month before alone. Under the same ledger lock, both at admission and in the recheck, payroll walks the employee's recorded chain from the ledger start S to P = M−1. A pure link function (`FlexLedgerOpening.DeriveLink`) evaluates each link once. If any recorded month's (opening, provenance) no longer follows from its predecessor's recorded closing, the month is refused with **409** `{kind:"payroll-prior-month-stale", month}`, naming the **earliest** such month and writing nothing. Unrecorded and raw months are not compared; a month with no employed day is skipped and its successor compared; REHIRE boundaries are respected; provenance is part of the compare, so a balance that matches with a different provenance is stale. The windows read now spans `[min(S, P).start, M.end]`. *The guarantee, as the code states it:* recalculating the named month never refuses itself, because the walk stops before it. But a month named because its own employed predecessor has no ledger record answers the case-4b refusal naming that predecessor, which is the operator's next step. A stale chain first seen in the under-lock recheck answers `payroll-ledger-changed`, and the calculation's manifest stays, as in every S146 export-time recheck. This closes the last silent way a wrong figure entered the chain (QUAL-229; the operator's sequence is HRP-003 point 4).

**D4 note 2026-11-23 (S148 TASK-14831 + -T and TASK-14847 + -T, merged 2026-11-23, the TASK-14831 stack's merge commit; plan O-19; refinement A-3, B-2, B-7, B-8 and the plan's Block C; the TASK-14831 pair's Step 5a cycle-1 adjudication W1, W2, W6, W9, W10; wave-4 batch-C rulings 2.4, 2.5; wave-5 batch-D rulings 1, 2, 14 and amendment 9; TASK-14818 Step 5a ruling 1.1; the SEC-062 scope adjudication R4, R8; TASK-14821 Step 5a R2; batch-A/B ruling A6; the owner's ruling of 2026-11-19 (QUAL-259); QUAL-265; SEC-066): the pay-inputs lock.**
*In plain words.* Payroll records a month in two steps: it calculates the month, then a moment later records it. The recording is the export, or `/recalculate` for a correction, which has the same two steps. Until S148, an HR change dated back into that month could commit between the two steps, and the month was then recorded on the old values with nobody told. Such changes are a new agreement-config version, an override, a local profile, a transfer, a profile or employment-date correction, and a profile close. S148 closes that race with one system-wide *pay-inputs lock*, a named database lock that guards a decision rather than a row. Every Backend writer of a pay input takes it exclusively. Payroll's recording takes it shared, so recordings run side by side and take turns only with a writer. Whichever commits first, the other sees it. A recording that committed first is found by the change, which lists the month on HR's worklist. A change that committed first is found by payroll's *recheck*: inside its turn, payroll works the month's inputs out again and compares them with what its calculation used. Those inputs are each stretch's agreement, part-time fraction, position, institution and full rule set, the employed days, and a leaver's last day. If anything differs, or a day can no longer be covered, the recording is refused.
- *What a caller sees.* A refused recording gets **409 `payroll-inputs-changed`** with the body `{success, error, kind, month:"yyyy-MM"}`. The export's sentence ends "calculate the month again" and the correction's "run the correction again" (`…/Services/PayrollExportService.cs:1065-1078`; the helper and arms in `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:1179-1191`, `:544`, `:794`). Nothing is recorded, only the calculation's manifest (its stored proof) stays, and a retry calculates on the new values. If the recheck cannot read the agreement configuration, the recording-time **503 `payroll-config-unavailable`** says that nothing was exported or corrected (`Program.cs:913-919`, `:553`, `:803`). A failed read is never a pass (`…/Services/PeriodCalculationService.cs:1903-1906`).
- *Why no payslip moves.* The recheck can only refuse, never change a figure. The demo pay pin (PIN-1) was unchanged on the local runs.
- *The cost.* Changes to pay inputs take turns across the whole system. A recording also borrows extra database connections during its turn (the SEC-066 point below).

*The key and its two modes.* `PayInputsLock` (`src/Infrastructure/StatsTid.Infrastructure/PayInputsLock.cs:34-61`) takes `pg_advisory_xact_lock(148, 1)` exclusive (`:44`) or `pg_advisory_xact_lock_shared(148, 1)` shared (`:58`), released at commit or rollback.
- The key is in PostgreSQL's two-integer key space, which PostgreSQL keeps apart from the single-key `hashtext` space of every other advisory lock (`employee-`, `payroll-ledger-`, `reporting-org-`, `unit-org-`, `position-override-`). So, unlike those, it cannot collide with another key. In `pg_locks` it shows as `advisory`, classid 148, objid 1, objsubid 2 (`:12-16`).
- It is always the first statement of a transaction stated READ COMMITTED (PAT-015: under REPEATABLE READ the snapshot would predate the wait).
- It is taken only at a route's or an owning service's transaction head, never in a repository helper or a shared writer.
- A read on a separate connection never takes it, because a request that holds it and reads on a second connection would otherwise wait for itself (`:18-25`).
- At this merge it is taken shared by the export (`PayrollExportService.cs:300`) and the correction (`…/Services/RetroactiveCorrectionService.cs:378`).
- It is taken exclusive by 13 calls:
  - the user edit, when it carries an organisation or an agreement code (`src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs:1857-1858`);
  - the dedicated agreement-code route (`:2975`);
  - the profile PUT and the profile close (`EmployeeProfileEndpoints.cs:405`, `:1305`);
  - the employment start-date and end-date PUTs (`EmploymentDateEndpoints.cs:214`, `:581`);
  - the agreement-config publish (`AgreementConfigEndpoints.cs:517`);
  - the local-profile write (`ConfigEndpoints.cs:286`);
  - the four override writes (`PositionOverrideEndpoints.cs:156`, `:349`, `:492`, `:640`);
  - the settlement reversal, when it carries an end-date correction (`src/Infrastructure/StatsTid.Infrastructure/SettlementReversalService.cs:175-176`).
- The Backend reopen takes it in neither mode.

*The lock order.* Each site's own comment states its order.
- **Export:** pay inputs (shared) → `payroll-ledger-` (now the second statement) → the flex-opening recheck → the pay-inputs recheck (reads only) → the previous month's approval `FOR SHARE` → this month's approval `FOR UPDATE` with the four-eyes checkpoint → the record (`PayrollExportService.cs:289-347`). A calculating export without the recheck is refused before any connection opens (`:252-262`).
- **Correction:** pay inputs (shared) → `payroll-ledger-` → the flex-opening recheck → the pay-inputs recheck → the record `FOR UPDATE` (`RetroactiveCorrectionService.cs:369-412`).
- **The exclusive takers:** pay inputs first, then each writer's existing order, unchanged:
  - the user edit: `reporting-org-` → `unit-org-` → the users row → the agreement-code and organisation rows → the worklist rows (`AdminEndpoints.cs:1848-1858`);
  - the agreement-code route: the users row → the agreement-code rows → the worklist rows (`:2971-2975`);
  - the profile PUT: `employee-` → the profile rows → the users row → the revaluation → the worklist rows (`EmployeeProfileEndpoints.cs:398-405`);
  - the profile close: the profile timeline rows → the users row → the close → the audit rows and events → the worklist rows (`:1299-1305`);
  - the start-date PUT: `employee-` → the users row → the worklist rows (`EmploymentDateEndpoints.cs:211-214`);
  - the end-date PUT: `employee-` → the users row → the lifecycle writer → the worklist rows (`:577-581`);
  - the publish: the key's rows `FOR UPDATE` → the worklist rows (`AgreementConfigEndpoints.cs:514-517`);
  - the local-profile write: the open profile row `FOR UPDATE` → the worklist rows (`ConfigEndpoints.cs:281-286`).
- **The override writers' `position-override-` key lock (ruling 1.1):** pay inputs → `position-override-` → the key's override rows `FOR UPDATE`, in id order → the head write, then the version row → the audit, outbox and projection rows → the worklist rows (`…/PositionOverrideRepository.cs:612-620`; `PositionOverrideEndpoints.cs:82-88`).
  - *What it is:* a transaction-scoped advisory lock in the single-key `hashtext` space, one per (agreement, OK version, position). The four override writers take it in the repository's `LockKeyAsync`, as a statement of its own before the row lock (`PositionOverrideRepository.cs:623-635`).
  - *Why:* it exists even when the key has no row yet, so the first writers of a key also take turns. A row lock alone let a create that had passed the date rules commit after a later-dated create and switch-off, which left a version dated before committed ones.
  - *Why it cannot deadlock:* only override writers take it, and a transaction holding it takes no other advisory lock afterwards. So a 32-bit hash collision with another lock family costs a wait, never a cycle (`:618-620`).
- **The settlement reversal's users-row lock (rulings R4, R8):** pay inputs, only when the reversal carries an end-date correction → `employee-` → the person's users row `FOR UPDATE`, in every mode → the settlement rows → … (`SettlementReversalService.cs:171-212`).
  - *Why:* the reversal's scope check runs before its transaction. A transfer committed in between would leave the reversal acting on a person the administrator was never checked for. The row lock makes a bare reversal and a transfer take turns. The organisation-drift guard then compares the locked organisation with the one the scope check authorised, and a difference answers 409.
  - A bare reversal changes no employment date and takes no pay-inputs lock.
- **The Backend reopen (unchanged):** it takes neither the pay-inputs lock nor `payroll-ledger-`. Its leader arm takes `reporting-org-` → `unit-org-` → the approval row `FOR UPDATE` → reads the record. Its employee arm takes only the approval row (`ApprovalEndpoints.cs:2016-2065`).

*Why no wait cycle can form.*
1. The pay-inputs lock is the first statement of every transaction that takes it. When a request asks for it, the request holds no lock on any connection: the calculation and its manifest committed earlier, and the route's own reads finished on their own connections (`PayrollExportService.cs:297-299`; `RetroactiveCorrectionService.cs:375-377`). So a transaction waiting for it holds nothing.
2. Only payroll takes `payroll-ledger-`, always after the shared pay-inputs lock, and no exclusive taker takes `payroll-ledger-`. So no transaction waits for the pay-inputs lock while holding the ledger lock, and none waits for the ledger lock while holding the pay-inputs lock exclusive (`PayInputsLock.cs:27-32`). Two shared holders never wait for each other on the pay-inputs lock; they take turns per employee on `payroll-ledger-`, as before.
3. Below the advisory locks the row-lock orders are unchanged. D4's export↔reopen argument stands, because the reopen takes neither of payroll's advisory keys.
4. The recheck's and the revaluation's reads on separate connections take no lock, so they cannot join a lock cycle. What they can wait for is a pooled connection, which is the availability limit below, not a deadlock.
5. `position-override-` follows the rule in the override bullet above.
6. The reversal's users-row lock comes after `employee-`, the same order as the end-date PUT. Every settlement-row writer takes `employee-` first, and the user edit takes no `employee-` lock, so no transaction locks a settlement row and then the users row outside `employee-`. This is the internal lens's reading in TASK-14821's Step 5a cycle 1, NOTE 2. Child inserts whose foreign key takes a key-share lock on the users row were not traced; they are the same exposure the end-date PUT already had, `(unproven)`.

*The writers outside the lock, each with its reason.* (TASK-14821's census of pay-input writers; batch-C rulings 2.4 and 2.5; batch-D amendment 9.)
- **The user create** (`AdminEndpoints.cs:1125`, `:1194`): a new person has no recorded month.
- **The startup profile seeder and the two back-fill seeders**, `UserAgreementCodeBackfillSeeder` and `UserOrganisationBackfillSeeder` (Backend `Program.cs:545`, `:568`, `:583`): each runs before the host serves a request (`app.Run()`, `:642`).
- **The customary-days and HR-facts writers**: not built at this merge (TASK-14834 and TASK-14836, wave 6). No calculation reads them in S148, so no recorded month can go stale on them. They join the lock with their first reader and their hand-off (S1-6, S1-9; Block C).
- **The entitlement-config writers** (`AgreementEntitlementEndpoints.cs:185`, `:434`; `EntitlementConfigEndpoints.cs:264`, `:527`) **and the child's-sick eligibility writer** (`EntitlementEligibilityEndpoints.cs:236`): payroll reads neither; nothing in the payroll project references either table.
- **The wage-type-mapping writer** (`WageTypeMappingEndpoints.cs:481`): an edit is dated today (`:394-397`) and cannot change a recorded line.
  - The unfinished-month case is not a calculate-to-record race, so no lock can close it (ADR-016 D10's exception). In that case a month is recorded before it ends and the mapping is edited before its tail days.
  - The owner ruled on 2026-11-19, option (a), to close it after S148: each line's resolved wage-type code is stored in the month's proof, so a replay reproduces what was recorded (QUAL-259, OPEN; ADR-020 D1 is amended when this is built).
- **The test-only local-profile save overload**, `LocalAgreementProfileRepository.SupersedeAndCreateAsync(expectedCurrentVersion, newProfile, ct)` (`LocalAgreementProfileRepository.cs:240-257`; its remark at `:223-227`): it opens its own REPEATABLE READ transaction and takes no pay-inputs lock. It has no production caller, because the route calls the `(conn, tx)` overload inside its locked READ COMMITTED transaction (`ConfigEndpoints.cs:278-303`). So it stays outside the lock as a test-only writer.
- **The dormant profile migrator** (`LocalAgreementProfileMigrator.RebuildAsync`, its insert at `LocalAgreementProfileMigrator.cs:564`): it writes `local_agreement_profiles` with no lock and no worklist hand-off, but it has no production caller (QUAL-039, SEC-037); only tests call it. Whoever wires it for a legacy upgrade must give it the lock first. *(Added 2026-11-26, S148 Step 7a, Fable NOTE.)*

*The lock holder's extra connections (SEC-066).* A pooled connection is one of the fixed number of database connections a service keeps and lends to requests.
- **The profile PUT's revaluation.** The PUT holds the lock exclusive while its revaluation reads committed history on further pooled connections: the absences, then the dated profile through the resolver once per absence (`EmployeeProfileEndpoints.cs:1837`, `:1879`, `:1906`).
  - None of these reads takes the lock. One that took it shared would wait for its own request forever.
  - They read committed history by design (ADR-032 D4). Moving them into the transaction would change what a gap-fill correction writes, which needs an owner ruling (TASK-14821 Step 5a R2).
- **The recording's recheck, SEC-066's payroll variant.** A recording holds one pooled connection for its transaction, and its recheck's reads borrow up to two more, so up to three in all.
  - The reason is that the profile resolver keeps its connection open while it reads the agreement code (`…/EmploymentProfileResolver.cs:197`, `:247`; `UserAgreementCodeRepository.cs:108`).
  - Its calculation, before the lock, already needs two connections at once.
  - The limit is marked in the code as `shortcut(SEC-066)` (`PeriodCalculationService.cs:1893-1895`).
- **What it costs: availability only.**
  - A full stall becomes possible with recordings near half the payroll pool in their turn at once. That is about 50 of the driver's default 100; no pool size or timeout is set in `src`.
  - Stalled recordings fail after the 15 s connection timeout, and each failure rolls back and writes nothing. Under that exhaustion, the first read to fail decides the answer: usually an unnamed server error, and the recording-time 503 only when the failed read is the configuration lookup.
  - Stalled recordings keep the shared lock while they wait, so every Backend pay-input writer waits too.
- Registered as SEC-066 (Low), with its payroll variant and the known closures.

*The four measured figures* (`PayInputsLockTimingTests`). They are recorded here, not judged. All four are local runs on a throwaway PostgreSQL 16 cluster, not CI evidence; their CI run is CI-W5. A figure judged slow becomes a performance-register F-row.
1. **A large agreement-config publish with five recordings waiting.** The publish covered 300 persons × 4 exported months = 1,200 worklist rows. It answered 3,285 ms after the lock's release, and the five exports answered 4,137–4,627 ms after it (TASK-14831-T's local run).
2. **A profile PUT queued behind a held `employee-` lock, with an unrelated export waiting behind the PUT.** The PUT held the pay-inputs lock exclusive, and the test held the `employee-` lock for a fixed 2,000 ms. The export answered 2,061 ms after it was seen waiting, at most 52 ms after the release; the PUT answered 200 (same run).
3. **Five profile PUTs, each revaluing an absence, against a Backend pool of 4 (SEC-066).** One completed (200 at 30,815 ms). Four failed with 500 at 15,283–30,693 ms and wrote nothing. One revaluation event landed (same run).
4. **Payroll's own pool, re-staged (W6).** Pool of 4 and three exports. Each export was asserted waiting, 3 of 3 at the release, and timed both from submission and from the release.
   - One export completed: 200 at 15,223 ms from the release, 17,242 ms from submission.
   - Two failed with a database connection error (`NpgsqlException`) at 15,033 ms and 17,053 ms, and wrote nothing (TASK-14847-T's local run, at `8572b0a0`).
   - The first staging used four exports. That cannot work on a pool of 4: a calculation needs two connections at once and three waiters already hold three, so the fourth failed at 0 ms before reaching the lock. Pool size minus one is the largest crowd that can wait.

*The last-day rule (TASK-14847, QUAL-265).* Payroll now checks each piece's last day, as well as its first, for a covering employment record (profile, agreement code and organisation) (`PeriodCalculationService.cs:1266`, `:4049`; the helper `:4180-4189`). So a record that ends inside a month while the person stays employed is refused. That answers 422 `payroll-employment-record-gap` at calculation, before any manifest (`Program.cs:452`, `:832`), and 409 `payroll-inputs-changed` inside the recheck (`PeriodCalculationService.cs:1907-1919`).

*Pinned by.*
- The Unit facts `PayInputsRecheckTests`, `PieceCoverageTests` and `PayrollExportNoRecheckTests`.
- The Docker facts:
  - `PayInputsLockCensusTests`: one fact per taker, plus two negatives;
  - `PayInputsLockRaceTests`: both orders, both routes and the 503;
  - `PayrollRecordCoverageTests`;
  - `PayInputsLockTimingTests`.
- The Docker facts were green on the local runs and were CI-pending until CI-W5. *(CI-verified at CI-W5, run 45280970629, 2026-11-24; this note added 2026-11-25.)*

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

*Citations into the payroll host in this section re-verified 2026-11-25 at `bda18b03`: S148's TASK-14801 and TASK-14831 had moved them.*

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
(`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:236-241`; `RawExportRoutesRetiredTests`), with every helper
only they used and the Orchestrator's dead `payroll-export` task type. `/calculate-and-export` is the one route that
exports; `/recalculate` stays the one way a paid month changes (D5). The D4 note of 2026-10-24 says the ledger lock is
taken by "all three export routes"; there is now one.

**The approval period is required, and it must be the month's own (D2, D4).**
- `ExportAsync` refuses a context with no approval period (null or the empty id) by throwing, before any connection
  opens (`…/Services/PayrollExportService.cs:236-239`; the message `:31-35`). Until S147 the raw routes passed no
  period, and D2's in-transaction `FOR UPDATE` re-assert of APPROVED (called "B2" in the code, after S90's Step-0b
  blocker B2, the export↔reopen race named under Consequences; `:343-347`) was skipped for them. It now runs on every export made since TASK-14716 (`:347`),
  with S147's four-eyes checkpoint (SEC-053).
- The re-assert first checks that the locked period is this month's own approval: the exported employee's (compared
  Ordinal), starting on the 1st and ending on the last day of the exported month (rulings R-G2, made exact by R-G8;
  `:572-586`). A period of another employee, of another month, or of only part of the month (a legacy `WEEKLY`
  approval) aborts the export with the existing 409 `payroll-export-conflict`, before its status or approver is checked
  (all three are read in the one `SELECT … FOR UPDATE`).
  On `/calculate-and-export` this holds by construction, because the route accepts only a whole month and reads the
  approval by the request's own employee and dates (`Program.cs:313-314`, `:327-328`); the check guards any other caller.

**D3, as amended.**
1. **One `(employee, year, month)` per export (owner ruling R-G9, 2026-11-04).** D3's grouping of one call's lines into
   a record per `(employee, year, month)` existed for the raw `/export-period`. With the period required and checked
   as the month's own, a second group could never pass. Both review lenses recommended recording the dead grouping for
   later (R-G5); the owner chose to remove it now. A call whose lines, or whose lines and flex-ledger write, belong to
   more than one `(employee, year, month)` is refused before any connection opens (`PayrollExportService.cs:212-227`;
   the message `:44-47`). A line that spans two calendar months is still refused (`:199-210`). D2's step "group lines
   by `(employee_id, year, month)`" no longer exists.
2. **D3's zero-line rule, applied to a month before the ledger start (TASK-14715; R-17, the S147 refinement's ruling
   that such a month writes no record).** A case-3a month with no lines
   and no record hands off nothing: no lines, and no closing, because its flex is not tracked. So, by D3's own rule, it
   writes no record. It still runs every check a calculating export runs (the ledger lock, the under-lock recheck, the
   previous month's `FOR SHARE`, this month's APPROVED re-assert with four-eyes), and only the write is skipped
   (`PayrollExportService.cs:349-361`; R-F1, the Orchestrator's ruling on TASK-14715's first review that only the write may
   be skipped). The month stays reopenable, a re-export writes nothing, and `/recalculate`
   refuses it as never exported (422 `payroll-not-exported`).
3. **A record with no balance also comes from a calculate-and-export now (TASK-14715).** Item 1 of the S146 amendment
   says the raw routes carry no balance. Since S147 such a record, with all three balance columns null, also comes from
   a case-3a `/calculate-and-export` with lines: source `CALCULATE_AND_EXPORT`, its period set, no `FLEX_PAYOUT` line,
   and the response's note "flex not tracked" (`Program.cs:618-619`). It is locked and corrections-only, like case 3b. A
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
  test calls reach (`PayrollExportService.cs:366-369`). One change closes all three, under "reseed, don't migrate".
- **The conflict message (R-G6).** `/calculate-and-export` copies the export's conflict message into its 409 body
  (`Program.cs:577-595`). The period-identity message names the approval's employee and dates. It cannot occur on that
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
