# S148 close, plan step C-1: the knowledge-base batch (ADR, DEP, RES), exact text

| | |
|---|---|
| **Sprint** | S148 (close, plan step C-1) |
| **Drafted by** | Planning Drafter (Opus 5.5). The Orchestrator applies by exact replacement; it is the only role that edits `docs/**` and `CLAUDE.md` |
| **Base commit** | master `bda18b03` (the second comment sweep merged as `9122a6c0`), re-checked at `ec789343`, which changed only the two registers and the sprint log. Every code and doc citation below was opened at `bda18b03`. `SkemaEndpoints.cs` moved by one line in that sweep (a `shortcut(QUAL-BF-011)` line at `:343`); every cite below into it is the post-sweep number |
| **Sources** | `.claude/reviews/S148-C1-audit-report.md` § 1 and § 4; `.claude/plans/PLAN-s148.md` rows O-8, O-22 (`:789`, `:804`), "Records owed" (`:838-860`), close sequence (`:740-761`); `.claude/plans/s148/ADDENDUM-A1-cell-clearing.md:440-452` (O-23 … O-31); `docs/sprints/SPRINT-148.md`; the three rest-research files `.claude/reviews/S148-RESEARCH-rest-24h-{pass1,verification,adjudication}.md`; the S148 brief adjudications named in each block |

## What this is, in plain words

S148 changed how the product works in several places that older decision records (ADRs) still describe the old way. This batch writes the dated notes that make those records true again, adds one new decision record, ADR-044, for the 11-hour rest rule as S148 built it, and adds the research dossier that rule rests on. Nothing here decides anything new: every note records a ruling already made (by the owner, an adjudicator or the Orchestrator) or code already merged, with its file and line.

Three things a reader should know before applying:
- **One reader of the dated organisation was missing from the plan.** The plan's O-22 row lists four code paths that read the new `user_organisations` table. At HEAD there are five: S148's TASK-14828 added a fifth, the Backend's dated-record timeline behind Teamoversigt, the balance page and the compliance check. The ADR-040 note lists all five (Brief contradictions, item 1).
- **The rest-research dossier is assembled, not retyped.** Its two big parts are byte-for-byte copies of two gitignored review files (about 108 KB). K-38 gives the new head in full and a node command that appends the two parts, so no one has to retype 700 lines of legal quotations. If you want the full text inline instead, say so (choice 1).
- **ADR-034's stale citations were the S147 section's, not the S148 note's.** The eleven citations into the payroll host written in S147 had all moved (TASK-14801 and TASK-14831 shifted both files); the 2026-11-23 D4 note's citations are current. The same staleness sits in `docs/SECURITY.md:194` (K-55, optional) and in one code comment (choice 6).

## How to apply

- **Exact replacement.** Each `old_str` occurs exactly once in its file at `bda18b03` (checked by a node script over this file, with each target's CRLF folded to LF before counting; the result is in the revision history). Each `new_str` repeats its `old_str` where the edit is an insertion.
- **Line endings.** The working tree has CRLF in nine targets (git's `core.autocrlf=true`; the index holds LF for all). The blocks below are written with LF. The Edit tool keeps a file's own line endings; a script must write CRLF back into the CRLF files.

| Line endings in the working tree | Files |
|---|---|
| **CRLF** | ADR-014, ADR-017, ADR-026, ADR-028, ADR-032, ADR-034, ADR-038, DEP-004, RES-002 |
| **LF** | ADR-016, ADR-039, ADR-040, ADR-043, `docs/SECURITY.md`, `docs/knowledge-base/INDEX.md`, `CLAUDE.md`, `docs/references/sick-day-credit-and-agreement-texts-research.md`; the two new files (write them LF) |

## The edits (58: 56 replacements and 2 new files)

| Brief item | Blocks | File |
|---|---|---|
| 1 (O-22) ADR-014 | K-1 … K-4 | `docs/knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md` |
| 1 (O-22) ADR-017 | K-5 … K-7 | `…/ADR-017-local-agreement-configuration-as-a-profile.md` |
| 1 (O-22) ADR-038 | K-8 | `…/ADR-038-enhedsspor-unit-hierarchy-model.md` |
| 1 (O-22) ADR-040 | K-9, K-10 | `…/ADR-040-employee-timeline-and-as-of-resolution.md` |
| 1 (O-22) and 6 ADR-043 | K-11 … K-19 | `…/ADR-043-overtime-flex-monthly-norm.md` |
| 2 (O-8) ADR-016 | K-20, K-21 | `…/ADR-016-temporal-period-handling.md` |
| 3 ADR-028 | K-22 … K-27 | `…/ADR-028-work-time-persistence-allocation-gate-timer-retirement.md` |
| 4 ADR-026 | K-28, K-29 | `…/ADR-026-audit-visibility-surface.md` |
| 5 ADR-032 | K-30 | `…/ADR-032-vacation-consumption-correctness.md` |
| 7 ADR-039, SECURITY, RES-002 | K-31 … K-36 | ADR-039; `docs/SECURITY.md`; `docs/knowledge-base/resolutions/RES-002-manager-draft-visibility-enforced-at-surface-only.md` |
| 8 ADR-044 (new) | K-37 | `docs/knowledge-base/decisions/ADR-044-rest-period-model.md` |
| 9 the dossier (new) | K-38 | `docs/references/rest-period-24h-research.md` |
| 10 CLAUDE.md and INDEX | K-39, K-40 | `CLAUDE.md`; `docs/knowledge-base/INDEX.md` |
| 11 DEP-004 and ADR-034 | K-41, K-42; K-43 … K-54 | `docs/knowledge-base/dependencies/DEP-004-endpoint-registry-ui-api-data-alignment.md`; `…/ADR-034-payroll-export-lock.md` |
| Optional, beyond the brief's list (each says why) | K-55 … K-58 | `docs/SECURITY.md`; INDEX (ADR-039, ADR-040 rows); `docs/references/sick-day-credit-and-agreement-texts-research.md` |
| 12 QUAL-255 | a one-line suggestion, not a block | the registers batch's |

---

## Item 1 (O-22) — ADR-014

### K-1 · `docs/knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md` · the implementation note's heading
- CRLF. At HEAD `:80`. The note was written in S147 as a plan; S148 built it.

old_str:
````text
**Implementation notes** (decided; built in S148's config stage):
````

new_str:
````text
**Implementation notes** (decided in S147; built in S148's config stage; the S148 note below records what was built and narrows the fourth bullet):
````

### K-2 · ADR-014 · the fourth bullet narrowed (R-14)
- CRLF. At HEAD `:84`. R-14 (`.claude/reviews/REFINEMENT-s148-adjudication-c1.md:53`) named this narrowing an Orchestrator doc edit at close.

old_str:
````text
- `CentralAgreementConfigs` stays a seed and a defence-in-depth fallback at most.
````

new_str:
````text
- ~~`CentralAgreementConfigs` stays a seed and a defence-in-depth fallback at most.~~ *Narrowed 2026-11-25 (S148, R-14): it is neither a seed nor a fallback on the pay path; see the S148 note below.*
````

### K-3 · ADR-014 · the fifth bullet (the editor label) closed
- CRLF. At HEAD `:85`. Verified: TASK-14829 removed the page-level label (`git log -S "men endnu ikke lønberegningen" -- frontend/src` → `043c5f35`); the per-field note is `frontend/src/components/config/ProfileEditor.tsx:57-60`, used at `:72` and `:76`.

old_str:
````text
- Until the stage lands, the agreement editor and the local-profile editor say "affects screens and booking; does not yet affect pay" (S148 stage 0).
````

new_str:
````text
- Until the stage lands, the agreement editor and the local-profile editor say "affects screens and booking; does not yet affect pay" (S148 stage 0). *Done, then retired: TASK-14807 added the label in stage 0, and TASK-14829 (`043c5f35`, in the T14b stack merged as `b5f167aa`) removed it once the editors reached pay. Two local-profile fields keep a per-field note, because their only reader, the overtime-governance warning, still uses the compiled config (`frontend/src/components/config/ProfileEditor.tsx:57-60`; QUAL-262).*
````

### K-4 · ADR-014 · the S148 note (what was built; R-14; plan O-12, O-22)
- CRLF. Appended after the last line of the file (`:86`). Every cite opened at `bda18b03`: `ConfigResolutionService.cs:72-77` (the fail-closed paragraph), `RuleRegistry.cs:543-551` (`ProfileConfig`), payroll `Program.cs:926-945` (503 and 422), `init.sql:1624-1683` and `:1772-1786` (the seeds), `CentralAgreementConfigs.cs:5-12` (class remark), the three `shortcut(QUAL-262)` sites (`git grep -n "shortcut(QUAL-262)" -- src`, three hits), `git grep -n "CentralAgreementConfigs" -- src` (outside the class: exactly the three call sites), `AgreementConfigSeeder` retired in `d2f08749` (`git log --diff-filter=D`).

old_str:
````text
- The Agent Guidance line "Payroll Integration Agent: No changes if configs are resolved before reaching payroll services" did not hold: payroll resolves the configs itself, dated per day.
````

new_str:
````text
- The Agent Guidance line "Payroll Integration Agent: No changes if configs are resolved before reaching payroll services" did not hold: payroll resolves the configs itself, dated per day.

## S148 note (2026-11-25) — what the config stage built, and the fourth bullet narrowed (R-14; plan O-12, O-22)

**In plain words.** The editors now decide pay. Payroll, the compliance check, both month figures and Skema's rest warning hand the Rule Engine the agreement configuration in force on each day, resolved from the database (central → position → local) and frozen by value into the month's proof (ADR-016 D10, amendment 2026-11-23). The rules compiled into the code are no longer an answer anyone on the pay path can get, not even when the database cannot be read: a silent fallback would pay on values nobody chose, which is the very defect this ADR exists to prevent (R-14).

- **No compiled fallback on the pay path.** The one dated lookup is fail-closed: a database error on any layer is `ConfigLookupUnavailableException`, a day with no published central version is `NoConfigInForceException`, and no compiled value is ever substituted (`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:72-77`; P-13, QUAL-184, QUAL-042). Payroll answers them with 503 `payroll-config-unavailable` and 422 `payroll-config-not-in-force` (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:926-945`). The Rule Engine computes only with the config a request carries, and refuses a request with none, or with another agreement's or OK version's, with a 400 (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RuleRegistry.cs:543-551`; R-8).
- **init.sql's rows are the one seed.** The ten agreement configs (AC, HK, PROSA, AC_RESEARCH, AC_TEACHING × OK24, OK26) are seeded published and in force from `0001-01-01` (`docker/postgres/init.sql:1624-1683`), the four position overrides with their version rows (`:1772-1786`). The startup `AgreementConfigSeeder`, whose rows could never be in force because its insert wrote no `published_at`, was retired by TASK-14818 (`d2f08749`; C-6 (c4)). PIN-2 keeps the seeded rows equal to the compiled tables, member by member (`tests/StatsTid.Tests.Regression/PayPin/ConfigSeedParityTests.cs`).
- **What the compiled table is still for.** `CentralAgreementConfigs` is the reference PIN-2 compares the seed against, and the source of the config three legacy callers still send (`src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:5-12`): the Backend's overtime-governance warning (`src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs:122-123`), and the Orchestrator's rule-evaluation dispatcher (`src/Orchestrator/StatsTid.Orchestrator/Services/TaskDispatcher.cs:85-86`) and weekly pipeline (`src/Orchestrator/StatsTid.Orchestrator/Services/WeeklyCalculationPipeline.cs:93-94`). None of the three is of record for pay. Each carries a `shortcut(QUAL-262)` comment, and QUAL-262 holds the follow-up (stage 2, S1-4). R-14 spoke of "the two legacy callers"; the T14b stack's review found the third, the dispatcher, and the register counts three.
- **The census at `bda18b03`:** outside the class itself, `git grep -n "CentralAgreementConfigs" -- src` returns exactly those three call sites.
````

---

## Item 1 (O-22) — ADR-017

### K-5 · `docs/knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md` · D1: the table as built (the eighteen settings; C-4, the 14832 stack)
- CRLF. Inserted after D1's "Schema choice (Q5)" paragraph (`:88`). Verified at `bda18b03`: `init.sql:906-951` (the table; the eighteen columns and five office-hours CHECKs), `:918-922` (the remark on the `OFFICE_HOURS` basis), the `version` column added at `:2371-2372`, `customary_days` at `:984`; `LocalAgreementProfile.cs:30-50` (the eighteen members, "no calculation reads them yet (S149)"); `ConfigResolutionService.cs:202` (`ApplyProfileOverlay`, the four legacy members only); `flex_carryover_max` removed by `7b694f69`; the 14832 stack merged as `1bba25a0` (2026-11-24), TASK-14815 as `26cab7d8`.

old_str:
````text
**Schema choice (Q5):** wide-nullable columns. JSONB is over-engineering for 5 fields; normalized child tables are over-engineering for non-list values; wide-nullable is admin-readable and queryable.
````

new_str:
````text
**Schema choice (Q5):** wide-nullable columns. JSONB is over-engineering for 5 fields; normalized child tables are over-engineering for non-list values; wide-nullable is admin-readable and queryable.

**D1 note 2026-11-25 (S148 C-4, C-7, C-8, C-9; TASK-14815, merged `26cab7d8`; the 14832 stack, merged `1bba25a0`): the table as built.** In plain words: the profile now holds each institution's own flex, office-hours and paid-absence settings beside the overrides it always had. The SQL above is the S21 design. At S148's close the table is (`docker/postgres/init.sql:906-951`, with the `version` column the S22 migration adds at `:2371-2372`):
- **Four overrides, NULL = inherit central:** `weekly_norm_hours`, `max_flex_balance`, `max_overtime_hours_per_period`, `overtime_requires_pre_approval`. The fifth, `flex_carryover_max`, which no rule read, retired in S148 (C-7, TASK-14816, `7b694f69`; ADR-043 D6). Of the four, the weekly norm and the flex cap reach pay since S148, through the dated lookup and for the organisation the employee belonged to on the day (ADR-043 D12; C-13); the other two are read only by the overtime-governance warning, which still uses the compiled config (QUAL-262), and the editor says so beside them. D9a's Monday rule still governs `weekly_norm_hours`; S148 applies the same rule to the central config and the position override (QUAL-BF-007; ADR-043 provisional item 18).
- **Eighteen settings, NULL = not set (ADR-043 D6):** `flex_settlement_period` and `flex_cap_test_period` (each only `MONTH` so far), `flex_plus_cap_hours`, `flex_minus_cap_hours`, `flex_daily_credit_cap_hours` (each zero or more, with no upper ceiling, SEC-033), `flex_above_cap_action` (only `FORFEIT`), `flex_leaving_action` (only `FORFEIT_POSITIVE_RECORD_NEGATIVE`, provisional item 8), office hours as a start and an end for each weekday Monday to Friday (ten `TIME` columns; both or neither, start before end, five CHECKs), and `paid_absence_credit_basis` (`PARAGRAPH_7_NR_2` or `OFFICE_HOURS`). Values whose machinery comes later are not in a CHECK yet.
- **Saved, validated and audited, read by no calculation.** The save validates every value with a 400 and a per-field code (TASK-14832; that the `OFFICE_HOURS` basis needs at least one weekday window is the write's rule, not a CHECK, `init.sql:918-922`), the repository reads and writes all eighteen, and the editor shows and saves them (TASK-14833). No calculation reads them in S148, and they are not frozen into a month's proof: they are frozen with their first reader in S149 (the S148 wave-6 batch-A brief adjudication, ruling 9; `src/SharedKernel/StatsTid.SharedKernel/Models/LocalAgreementProfile.cs:30-50`).
- **Beside the table:** an institution's customary days, a dated list of days rather than a value (`customary_days`, `init.sql:984`; ADR-043 D9; TASK-14834).
- The counts "5 overridable fields" and "JSONB is over-engineering for 5 fields" above are S21 history.
````

### K-6 · ADR-017 · D5: the dated implementation note (ruling 8)
- CRLF. At HEAD `:163`. Verified: the PUT route `ConfigEndpoints.cs:125`; the request's own remark "The save replaces the whole profile, so an omitted member is stored as NULL" at `:773-781`; the editor's remark "the editor PUTs its full state (23 keys); the server clears anything it omits" at `frontend/src/components/config/ProfileEditor.tsx:20-22`; the clearing fact `LocalProfileNewColumnsTests.SameDaySave_ClearingASetNewSetting_IsNotANoOp` (`tests/StatsTid.Tests.Regression/Config/LocalProfileNewColumnsTests.cs:327-341`).

old_str:
````text
The PUT endpoint accepts a request body shaped as the profile's overridable subset; fields omitted from the request preserve their current value, fields explicitly set to `null` revert to inherit-central. Save-time validation runs (D9a alignment policy), the `If-Match` / `If-None-Match` precondition is checked inside the transaction, and either a new profile is created via close-then-insert or 400 / 412 is returned.
````

new_str:
````text
The PUT endpoint accepts a request body shaped as the profile's overridable subset; fields omitted from the request preserve their current value, fields explicitly set to `null` revert to inherit-central. Save-time validation runs (D9a alignment policy), the `If-Match` / `If-None-Match` precondition is checked inside the transaction, and either a new profile is created via close-then-insert or 400 / 412 is returned.

**D5 implementation note 2026-11-25 (the S148 wave-6 batch-A brief adjudication, ruling 8; plan O-22):** the sentence "fields omitted from the request preserve their current value" was never built. The PUT replaces the whole profile, which has been its contract since S21: an omitted member is stored as NULL, meaning "inherit central" for the four overrides and "not set" for the eighteen flex, office-hours and credit-basis settings (`src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs:125`, the route; the request's remark `:773-781`). The editor always sends its full state (`frontend/src/components/config/ProfileEditor.tsx:20-22`). A save that clears a value that was set is a real change, with its own audit row and event, never a silent no-op (`tests/StatsTid.Tests.Regression/Config/LocalProfileNewColumnsTests.cs`, `SameDaySave_ClearingASetNewSetting_IsNotANoOp`, ruling 15's clearing fact). *Why the whole-replace contract stays:* it is what every client already does, and a partial update would need a way to say "leave this alone" for each of 22 nullable members.
````

### K-7 · ADR-017 · the S147 note: "frozen with the first reader" (ruling 9)
- CRLF. At HEAD `:414` (the S147 note's first bullet). An inline correction keeps the S147 words as history.

old_str:
````text
the leaving action, office hours (a clock window per weekday) and the paid-absence credit basis, resolved for each day and frozen into the calculation's manifest.
````

new_str:
````text
the leaving action, office hours (a clock window per weekday) and the paid-absence credit basis, resolved for each day and frozen into the calculation's manifest *(corrected 2026-11-25: frozen with their first reader, in S149. S148 stores, validates and audits them and freezes none, because nothing reads them yet; the S148 wave-6 batch-A brief adjudication, ruling 9; see D1's note of this date)*.
````

---

## Item 1 (O-22) — ADR-038

### K-8 · `docs/knowledge-base/decisions/ADR-038-enhedsspor-unit-hierarchy-model.md` · D1: which organisation anchors what (C-13)
- CRLF. Inserted after D1's last bullet (`:24`). Verified: `init.sql:721-726` (the remark "users.primary_org_id stays the access and JWT anchor (ADR-038 D1) and the cache of the row covering today", `:723`); `ConfigResolutionService.cs:21-28` (the lookup takes "the organisation the employee belonged to on that day (their dated record, C-13), never the live primary organisation", `:26`); `EmploymentProfileResolver.cs:174-196`; the transfer's future-date refusal `AdminEndpoints.cs:2004-2008`; TASK-14820 merged as `d7458466` (2026-11-17).

old_str:
````text
The strict `CHILD` ordering (`organisation→direktion→omrade→kontor→team→enhed`) is app-enforced on create/move.
````

new_str:
````text
The strict `CHILD` ordering (`organisation→direktion→omrade→kontor→team→enhed`) is app-enforced on create/move.

**D1 note 2026-11-25 (S148 C-13; owner Q-1 = (a), "Date it in S148", 2026-11-09; TASK-14820, merged `d7458466`; QUAL-206 FIXED): the Organisation is now two anchors, one live and one dated.** In plain words: StatsTid now remembers which organisation each employee belonged to on each past day, so pay and configuration use the right institution's rules for a month before a transfer, while access rights still follow today's organisation.
- **Authority, scope, access and the login token keep `users.primary_org_id`,** today's organisation, exactly as D1 and D2 say. The column is also the cache of the dated row that covers today (`docker/postgres/init.sql:721-726`).
- **Pay and configuration read the dated organisation,** `user_organisations` (ADR-040 D4 and its note of this date): the dated config lookup takes "the organisation the employee belonged to on that day … never the live primary organisation" (`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:21-28`), the profile resolver reads the row covering the day (`src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs:174-196`), and payroll splits a month at a transfer date and freezes the organisation of each piece (`FrozenPiece.OrgId`; ADR-016 D10, amendment 2026-11-23).
- So D2's sentence that the Organisation-keyed consumers "keep reading it unchanged" no longer holds for payroll attribution (`EmploymentProfileResolver`) or for the configuration a calculation uses. A transfer now carries an "effective from" date: today by default, never in the future (`src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs:2004-2008`), and not before the start of the current row (an interim limit, QUAL-BF-008).
- **The unit (`users.unit_id`) stays undated.**
````

---

## Item 1 (O-22) — ADR-040

### K-9 · `docs/knowledge-base/decisions/ADR-040-employee-timeline-and-as-of-resolution.md` · the org-history row (`:101`) and its note
- LF. The row is the D4 table's last line; the note follows it. **The five readers were verified at `bda18b03`** with `git grep -n -E "(FROM|JOIN|UPDATE|INTO) user_organisations\b" -- src tools` (ten hits: the five reads below, the seeder's check, and the writer's four write statements at `UserOrganisationRepository.cs:69`, `:163`, `:193`, `:207`). The plan's O-22 names four readers; the fifth, `DailyNormCalculator.cs:730-736`, came with TASK-14828 (`f68ed12a`, the T14b stack, merged `b5f167aa`) and is called from `ApprovalEndpoints.cs:1378`, `BalanceEndpoints.cs:178` and `ComplianceEndpoints.cs:411` (Brief contradictions, item 1). Other lines: `init.sql:721-740` (table), `:742-762` (audit); `UserOrganisationRepository.cs:43-102` (`InsertFirstRowAsync`, the literal at `:74`), `:111-121` (`LockTimelineAsync`); `AdminEndpoints.cs:1282` and `UserOrganisationBackfillSeeder.cs:79` (the two callers of the first-row write); `EmploymentProfileResolver.cs:190-196`; `PostgresOrganisationHistoryReader.cs:17-25`; `HrBackdateWorklistRepository.cs:1022-1030`; `UserOrganisationBackfillSeeder.cs:44-54`.

old_str:
````text
| org/unit membership (`primary_org_id`, `unit_id`) | effective-dated history per the ADR-027 reporting-lines precedent | **model decided here; implementation is a NAMED FOLLOW-UP program** (OQ-3 ruling ii). Recorded consequence: until it ships, "which org in March?" stays unanswerable |
````

new_str:
````text
| org/unit membership (`primary_org_id`, `unit_id`) | effective-dated history per the ADR-027 reporting-lines precedent | **the organisation: built in S148 (C-13), `user_organisations`; the unit: still undated, a named follow-up** (OQ-3 ruling ii). "Which organisation in March?" is answerable since S148; "which unit in March?" is not. See the note below |

**Note 2026-11-25 — the organisation is dated (S148 C-13; owner Q-1 = (a), "Date it in S148", 2026-11-09; TASK-14820, merged `d7458466`; QUAL-206 FIXED).** In plain words: the system now knows which organisation each person belonged to on each day, so a month calculated after a transfer uses each day's own institution settings. The unit below the organisation is still not dated.
- **The record.** `user_organisations` and its audit table (`docker/postgres/init.sql:721-740`, `:742-762`), in the `user_agreement_codes` shape: one row per period, `effective_to` end-exclusive (ADR-018 D8), no default on `effective_from` (a writer that forgets the date fails at insert), a transfer closing one row and opening the next, a same-day change updated in place. `users.primary_org_id` stays the access and JWT anchor and the cache of today's row (ADR-038 D1 and its note of this date).
- **The first row starts at the literal `0001-01-01`** (NR-P7), so every day a profile row can ever cover has an organisation, and HR may backdate a new person's profile to their real first day. There is one first-row write path, `UserOrganisationRepository.InsertFirstRowAsync` (`src/Infrastructure/StatsTid.Infrastructure/UserOrganisationRepository.cs:43-102`). It writes the date as the SQL literal `DATE '0001-01-01'` (`:74`), never as a `DateOnly` parameter, which Npgsql 8 would send as `-infinity`. Its two callers are the admin create (`src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs:1282`) and the startup backfill seeder (`src/Infrastructure/StatsTid.Infrastructure/UserOrganisationBackfillSeeder.cs:79`).
- **One definition, five readers, no shared code.** A row covers day d when `effective_from` ≤ d and `effective_to` is null or after d (half-open). Each read applies that definition in its own SQL, because each asks a different question of the same rows:
  1. the profile resolver, "which organisation on day d" (`src/Infrastructure/StatsTid.Infrastructure/EmploymentProfileResolver.cs:190-196`; TASK-14820);
  2. payroll's organisation-history read, "on which dates did it change in a window": every row's start in the window, closed rows included, since a closed row's start is still a change date (`src/Integrations/StatsTid.Integrations.Payroll/Services/PostgresOrganisationHistoryReader.cs:17-25`, behind `IOrganisationHistoryReader`; TASK-14822);
  3. the worklist's match, "who belonged over an interval", inside a writer's transaction (`src/Infrastructure/StatsTid.Infrastructure/HrBackdateWorklistRepository.cs:1022-1030`; TASK-14823);
  4. the writer's own lock read, every row of the person's timeline `FOR UPDATE` (`src/Infrastructure/StatsTid.Infrastructure/UserOrganisationRepository.cs:111-121`, `LockTimelineAsync`; TASK-14820);
  5. the Backend's dated-record timeline, many people over a window, behind Teamoversigt's and the balance page's month figures and the compliance check's configuration runs (`src/Backend/StatsTid.Backend.Api/Services/DailyNormCalculator.cs:730-736`, `DatedRecordTimeline`; TASK-14828, in the T14b stack merged as `b5f167aa`).

  The startup backfill seeder's existence check, "does the person have an open row?" (`src/Infrastructure/StatsTid.Infrastructure/UserOrganisationBackfillSeeder.cs:44-54`), is a write-side sixth site, not a day read. Census: `git grep -n -E "(FROM|JOIN) user_organisations\b" -- src` at `bda18b03`.
- **The transfer.** The user edit's organisation change takes an "effective from" date: today by default, never in the future, and never before the start of the person's current row. The last is an interim limit: a wrongly dated earlier transfer cannot be corrected in the product yet (QUAL-BF-008). A backdated transfer that reaches a paid month or a settled holiday year puts it on HR's correction worklist, trigger `ORGANISATION_TRANSFER` (`src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs:2607-2620`; NR-11, owner Q-2 = (a)).
- **Still open:** the unit (`users.unit_id`) stays undated, the named follow-up this row first recorded for both.
````

### K-10 · ADR-040 · the D8 note (P-27; ruling 2.1)
- LF. Inserted after D8's clarification paragraph (`:161-165`). Verified at `bda18b03`: the start-date hand-off `EmploymentDateEndpoints.cs:405-407` (call) and `:960-1007` (`WriteStartDateHandOffAsync`); the end-date hand-off `:755-770`; the reversal's `SettlementReversalService.cs:414-432`, placed before the re-settlement; the trigger kind `HrBackdateWorklistRepository.cs:145`. Built by TASK-14821 (`3513d2b1`, merged `e58df211`).

old_str:
````text
  write cannot strand anything and runs no check, so legitimate reversals are never blocked.
````

new_str:
````text
  write cannot strand anything and runs no check, so legitimate reversals are never blocked.

  **D8 note 2026-11-25 (S148 P-27; the S148 wave-4 batch-C brief adjudication, ruling 2.1; TASK-14821, merged `e58df211`):** the sentence "End-date corrections that cross a settlement are excluded" is overtaken by P-27 **for the worklist policy only**. Since S148 a change of an employment start or end date, including the end-date correction a settlement reversal carries, lists on HR's worklist every exported month and settled holiday year whose pay the change can move (trigger `EMPLOYMENT_WINDOW_CHANGE`). The start date's interval is [min, max) of the old and new dates; the end date's is [min, max + 1 day), because the end date is also the settlement cutoff; a missing side is unbounded, and an unchanged date lists nothing. The sites: the start-date PUT (`src/Backend/StatsTid.Backend.Api/Endpoints/EmploymentDateEndpoints.cs:405-407`, the helper `:960-1007`), the end-date PUT (`:755-770`) and the reversal (`src/Infrastructure/StatsTid.Infrastructure/SettlementReversalService.cs:414-432`), which lists before it re-settles, so it never lists the settlement it is about to write (C-5 (c4)). **Unchanged:** ADR-033's reverse-then-re-settle rails, the existing 409 span guard, and D3's universal strand guard (the clarification above). A worklist row is a list for HR, not a recalculation (ADR-013). Not an invariant conflict (ruling 2.1).
````

---

## Items 1 (O-22) and 6 — ADR-043

### K-11 · `docs/knowledge-base/decisions/ADR-043-overtime-flex-monthly-norm.md` · the Status field's "none of it is built yet"
- LF. At HEAD `:5`.

old_str:
````text
It binds the build from S148 on; none of it is built yet, and each stage's sprint log says what has landed (see "How it is built").
````

new_str:
````text
It binds the build from S148 on; ~~none of it is built yet~~ stage 0 and the config stage were built in S148 (2026-11-25; see the S148 close note under "How it is built"), and each stage's sprint log says what has landed.
````

### K-12 · ADR-043 · D2's notes: S73 D-A superseded (owner Q1, 2026-11-10); the bound as built; the bound on a clear (addendum O-29)
- LF. Inserted after D2's last bullet (`:180`). Verified: `docs/sprints/SPRINT-73.md:24` (D-A); `docs/sprints/SPRINT-148.md:379-385` (owner Q1, 2026-11-10); `SkemaEndpoints.cs:2067` (`FindPaidAbsenceDayBoundBreachAsync`), `:2412` (advisory), `:2859` (in-lock), `:2130` (the body's `kind`), `:2204` (worked = `Math.Max(projectHours, intervalHours)`), the dates judged are the posted dates only (`:2074-2079`), cleared cells excluded (`:2101-2105`); QUAL-BF-001 (`quality-finding-register.md:408`); addendum D-8 (`ADDENDUM-A1-cell-clearing.md:201-213`).

old_str:
````text
- **Closes** QUAL-189 with D3; QUAL-231's residual and QUAL-237.
````

new_str:
````text
- **Closes** QUAL-189 with D3; QUAL-231's residual and QUAL-237.
- **S148 notes (2026-11-25).**
  - *The bound supersedes S73 D-A's same-day work clause (owner Q1, 2026-11-10, "D2 governs (Recommended)").* S73's ruling D-A made care days (`CARE_DAY`) and senior days (`SENIOR_DAY`) full-day only, and ratified that "same-day work hours remain legal" (`docs/sprints/SPRINT-73.md:24`). This decision's bound (a day's paid absence ≤ planned minus worked) makes a full care or senior day impossible on any worked day; nobody had shown the owner that interaction. Under the ruling a care or senior day goes only on a day with no work; someone who leaves mid-day records their hours and covers the rest with flex or holiday hours; a day is never worth more than planned. S73 D-A's work clause is retired, and `SkemaFullDayOnlyGuardTests.FullDayPlusSameDayWorkHours_Allowed200` was re-pinned to the 422 (TASK-14804-T). *Not chosen:* a care day covering "the rest of the day" (`docs/sprints/SPRINT-148.md:379-385`).
  - *The bound as built (TASK-14804, merged `72f286fe`).* It is checked at the Skema save twice: an advisory check before the transaction, and the authoritative one inside the per-employee lock, before any write. A breach answers 422 with `kind: "paid-absence-day-bound"`, `plannedHours` and `workedHours` (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:2067`, called at `:2412` and `:2859`; the body `:2130`). Worked is the larger of the day's project hours and its interval hours (`:2204`). It reads the § 7 nr. 2 per-day norm, not the configured credit basis, until stage 1 serves the one planned-hours figure (QUAL-BF-001). A day with no dated profile is not bounded (the wave-1 brief adjudication, ruling B3). The refusal shows a precise Danish alert (TASK-14810, AC-S0-7g).
  - *The bound judges the resulting day (addendum A1, D-8; NR-A6 confirmed (a); TASK-14840, merged `a331e452`).* Since S148 a Skema cell can be cleared (ADR-028 D6, note of this date). A date that carries a posted cell is judged on the day as it will stand after the save, cleared cells removed. A date touched only by clears is not judged by the bound: a clear can only remove paid absence or worked time, so it can never create a breach, and judging it could only refuse the repair of a day that is already over the bound (for example after a backdated profile change lowers the planned hours). This resolves the lock-in cases TASK-14804 found: a sick day recorded by mistake on a worked day, and a day stored over the bound before the bound existed, are repaired by clearing the cell and then saving the work (AC-CL-17, AC-CL-18). The children's-sick-days chain still judges a clear (addendum D-9).
````

### K-13 · ADR-043 · D6: "frozen with the first reader" (ruling 9)
- LF. At HEAD `:322`. The S147 `init.sql` lines have also moved: the profile table is `:906-951` and its audit table `:964-976` at `bda18b03`.

old_str:
````text
audit `:885-895`), resolved for each day and frozen into the manifest (D12).
````

new_str:
````text
audit `:885-895`; at S148's close `:906-951` and `:964-976`), resolved for each day and frozen into the manifest (D12) *(corrected 2026-11-25: frozen with their first reader, in S149. S148 stores, validates and audits them and freezes none, because nothing reads them yet; the S148 wave-6 batch-A brief adjudication, ruling 9; see the S148 close note under "How it is built")*.
````

### K-14 · ADR-043 · D10's note, with the pointer to ADR-044
- LF. Inserted after D10's last bullet (`:514`). Verified: `ComplianceEndpoints.cs:188` (`readStart = monthStart.AddDays(-2)`), `:249` (the interval read); `RestPeriodRule.cs:693-740` (the stints, joined at midnight), `:284` (`FindRestNotCheckedDays`); `ApprovalEndpoints.cs:2603-2618` (the send gate on interval hours); the parity table (33 rows, schema 2) and its two runners. The wave-3 brief adjudication's owed note (`.claude/reviews/S148-wave3-briefs-adjudication.md:166-167`) and the TASK-14813 Step 5a adjudication's ruling 7 ask for it.

old_str:
````text
- **Closes** QUAL-234.
````

new_str:
````text
- **Closes** QUAL-234.
- **D10 note 2026-11-25 (S148 stage 0, the rest-check half: waves 3 and 3b; TASK-14813 + 14814, merged `40d3e694`; TASK-14843 + 14844, merged `2810ebeb`).** In plain words: the server's 11-hour rest check and Skema's own warning now read the clock times employees type in Skema, and they judge rest the way the law does. **For the daily rest check this decision is superseded by [ADR-044](ADR-044-rest-period-model.md).**
  - *Built:* the compliance read sends Skema's intervals (`work_time_projection`) to the engine's rest rule, from two days before the month (`src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs:188`, `:249`); the rule joins a night shift typed as 22:00–23:59 plus 00:00–06:00 into one shift (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs:693-740`). The hours checks still sum the project entries. Payroll never runs the rest rule, so no payroll marker moved for it.
  - *Not retired: S147's "not checked" notice (T7) stays, narrowed (R-22, told to the owner).* It lists only the days whose project hours are not all on the clock, in an informational style on Skema and Teamoversigt, because retiring it outright would make those days look checked (`RestPeriodRule.cs:284`). This section's "S147's interim 'not checked' notice (T7) retires" did not happen as written. The list covers the period's own days only (QUAL-277).
  - *The rule.* Wave 3 first paired shifts by the day they started; owner Q2 (2026-11-13) had wave 3b replace that with the law's 24-hour look-back (ADR-044). The wave-3 interim "a rest belongs to the month in which it ends" is superseded too: a finding is dated the day its work ends (ADR-044 D3).
  - *Manual hours are retired* (D-2.4; TASK-14809, merged `540ac9a9`), and the send check's worked side is interval hours alone (`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:2603-2618`; ADR-028 D4, amended 2026-11-25). The first bullet's "today interval hours plus manual hours" is history.
  - *The legacy route's own clock times* went with `POST /api/time-entries` (TASK-14802). Its voluntary flag still has no writer (ADR-044 D6; QUAL-255).
  - *"The 11-hour rule then lives in two places":* the build proves they agree with one shared table of 33 scenarios, read by both test runners (`tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json`; `tests/StatsTid.Tests.Unit/Rules/RestParityTableTests.cs`; `frontend/src/hooks/__tests__/restParity.test.ts`; ADR-044 D8).
````

### K-15 · ADR-043 · D11: RR-4's sentence (one rest flag per type is not enough)
- LF. At HEAD `:532`. Source: research adjudication RR-4 and RR-8 ("ADR-043 D11 gets RR-4's sentence"); the dossier's Part 1, correction 13.

old_str:
````text
    held for a domain expert and never hard-coded as "always rest" (provisional item 13).
````

new_str:
````text
    held for a domain expert and never hard-coded as "always rest" (provisional item 13).
    *S148 note (2026-11-25; the S148 rest-research adjudication, RR-4):* one rest flag per type is not enough. On-call
    at home counts as daily rest (BEK 606 § 16 stk. 1), but on-call never counts toward the weekly rest day, at home or
    at the workplace (VEJ 9638 § 4; PAV 19.7.2; `docs/references/rest-period-24h-research.md`, Part 1, correction 13),
    so the build needs a daily and a weekly flag. No interval carries on-call today, so nothing changes yet (ADR-044 D6).
````

### K-16 · ADR-043 · provisional item 18's note (ruling 6)
- LF. At HEAD `:671`. Verified: `AgreementConfigRepository.cs:710-731` (`ChangedMondayOnlyFields`, the closed list with its reasons), `:1018-1038` (`RequireMondayForChangedFields`, the `shortcut(QUAL-BF-007)` at `:1027`); `PositionOverrideRepository.cs:698-702` (the override's one field); `LocalAgreementProfile.cs:130-148` (the profile's own rule).

old_str:
````text
18. *Expert question.* Retiring the local profile's `weekly_norm_hours` override and its Monday alignment rule (ADR-017 D9a), whose premise D1 removes. — D6; D-8 "Premises made moot".
````

new_str:
````text
18. *Expert question.* Retiring the local profile's `weekly_norm_hours` override and its Monday alignment rule (ADR-017 D9a), whose premise D1 removes. — D6; D-8 "Premises made moot". *S148 note (2026-11-25; the S148 wave-4 batch-C brief adjudication, ruling 6):* S148 extends the profile's Monday rule for `WeeklyNormHours` to the central agreement config and the position override, for the same field and on the same unretired premise (`NORM_CHECK_WEEKLY` is still an aligned window; `src/Infrastructure/StatsTid.Infrastructure/AgreementConfigRepository.cs:710-731`, `:1018-1038`; `src/Infrastructure/StatsTid.Infrastructure/PositionOverrideRepository.cs:698-702`; QUAL-BF-007). When this item is ruled, it lifts the rule for all three layers. The central config's `HasOvertime`, `HasMerarbejde` and `NormModel` are Monday-only for a different reason, which stage 1 removes: the weekly core judges a whole week by its latest evaluated day.
````

### K-17 · ADR-043 · the S148 close note: the stage boundary, the items passed on (`REF:630-642`), ruling 6's governing-day evidence
- LF. Inserted after the stage map's last paragraph (`:644-645`). **The governing-day evidence was gathered at `bda18b03`:** each OK24/OK26 pair of seed rows in `docker/postgres/init.sql` differs in two values only, `ok_version` and the description (a node diff of the comma-split rows `:1628`/`:1634`, `:1640`/`:1646`, `:1652`/`:1658`, `:1664`/`:1670`, `:1676`/`:1682`). Other facts: the refinement's passed-on table `.claude/refinements/REFINEMENT-s148-stage0-and-config.md:630-642` (gitignored; cited as ADR-043 already cites `.claude/` paths) and its interim limits `:31-45`; the merges named; R-24 (`.claude/reviews/REFINEMENT-s148-adjudication-c1.md:65`).

old_str:
````text
marker, scope and named limits into the next sprint, and ROADMAP § 1 points there (the deferred-branch pattern).
````

new_str:
````text
marker, scope and named limits into the next sprint, and ROADMAP § 1 points there (the deferred-branch pattern).

**S148 close note (2026-11-25): where the stage boundary fell, and what passes to S149.** In plain words: S148 built
stage 0 and the config stage as the owner scoped them (2026-11-09), plus three things the owner added on the way: the
dated organisation (owner Q-1), Skema cell clearing (owner Q2, 2026-11-10) and the corrected rest check (owner Q2,
2026-11-13). No demo payslip changed (PIN-1, the sprint's pay pin). Stage 1, where every HK and PROSA payslip
moves, is S149.
- **Stage 0, as built.** D11's refusal: `POST /api/time-entries` retired and payroll refuses a month holding a typed
  entry (TASK-14802; ADR-028 D5, note of this date). QUAL-236 in full (TASK-14811, 14812). D10's rest-check half, with
  two departures: T7's notice was narrowed, not retired (R-22), and the rest rule itself was corrected to the law's test
  (D10's note; ADR-044). Manual hours retired (TASK-14809). D2's bound on the § 7 nr. 2 per-day norm (TASK-14804;
  QUAL-BF-001), with S73 D-A's work clause retired (D2's notes). D12's editor label (TASK-14807), removed again by
  TASK-14829 once the editors reached pay. QUAL-239's interim (TASK-14805).
- **The marker moved in stage 0, not in the config stage.** `weekly-timeline-v3` is the call-home day's credit
  (TASK-14811; ADR-016 D10, marker footnote 2026-11-25; R-24). The table's proposal "Config: `weekly-timeline-v3`" is
  overtaken: the config stage changed no arithmetic, and a manifest written before the configs were frozen is refused
  by `replay-manifest-without-frozen-inputs` (ADR-016 D10, amendment 2026-11-23). Stage 1 opens on v3.
- **The config stage, as built.** D12 in full: configs and overrides dated (TASK-14818, QUAL-182), resolved per day
  through one fail-closed lookup, handed to the engine in each request, frozen by value (TASK-14830); the local
  profile's eighteen settings (TASK-14815, 14832, 14833; ADR-017 D1, note of this date); customary days and their
  editor (TASK-14834, 14835); the two HR facts and their editor (TASK-14836, 14837); the dated organisation (C-13;
  ADR-040, note of this date); the pay-inputs lock and the correction worklist's new triggers (TASK-14821, 14823,
  14831); demo profiles with the 37 h plus cap, demo customs and demo HR facts (TASK-14839). **Not as planned above:**
  the new local settings and the HR facts are stored, validated and audited, but not frozen into a month's proof; they
  are frozen with their first reader in S149 (D6's correction; R-21; the wave-6 batch-A brief adjudication, ruling 9).
- **Named interim limits at the close** are the refinement's list
  (`.claude/refinements/REFINEMENT-s148-stage0-and-config.md:31-45`); the sprint log records the ones that ended
  inside the sprint (plan O-33).
- **Passed on (R-11; `.claude/refinements/REFINEMENT-s148-stage0-and-config.md:630-642`).** None is read by stage 0 or
  the config stage, so each goes to the refinement of the stage that first needs it:
  - QUAL-225's HR-entered opening flex balance → S149's refinement (it receives stage 1's monthly flex);
  - how AC's annual-norm variants settle flex → S149's refinement;
  - whether HRP-024 (approve the previous month first) retires or is re-stated → S149's refinement;
  - the customary-days and HR-facts writers (C-10, C-11) joining the pay-inputs lock and the worklist → S149's
    refinement (S1-6, S1-9), when they become pay inputs;
  - where the overtime movements are stored (D8) → stage 2's refinement;
  - R-C, a leaver's owed time off and saved days → stage 2's refinement (provisional item 9 holds until then);
  - saved days → stage 2's refinement, after research cell R-A.
  - From S148 itself: the eighteen settings and the HR facts are frozen with their first reader, and a manifest
    without them reads as "not set" (ROADMAP's "For S149 (stage 1)" backlog line).
- **Ruling 6's governing-day evidence, for stage 1 (the wave-4 batch-C brief adjudication, ruling 6).** The weekly core
  judges a whole week by the regime of its latest evaluated day, so a week that holds a mid-week OK transition
  (1 April 2026 is a Wednesday) takes the new version's `HasOvertime`, `HasMerarbejde` and `NormModel` for all its
  days. That is wrong only if a seeded agreement's three values differ between OK24 and OK26. **They do not.** In
  `docker/postgres/init.sql` at `bda18b03` each OK24/OK26 pair differs only in `ok_version` and its description: AC
  (`:1628`, `:1634`) `WEEKLY_HOURS`, no overtime, merarbejde; HK (`:1640`, `:1646`) and PROSA (`:1652`, `:1658`)
  `WEEKLY_HOURS`, overtime, no merarbejde; AC_RESEARCH (`:1664`, `:1670`) and AC_TEACHING (`:1676`, `:1682`)
  `ANNUAL_ACTIVITY`, no overtime, merarbejde. So on the seeded configs the governing-day choice changes no result. It
  stays an `(unproven)` NOTE for one case only: a GlobalAdmin publishing an OK26 config whose regime differs from OK24's,
  whose transition week would then be judged on the OK26 regime. Stage 1's monthly core retires the weekly judgement.
````

### K-18 · ADR-043 · References: the S148 sources
- LF. Inserted after the References section's last bullet (`:857`).

old_str:
````text
  `docs/operations/hr-follow-up-process-register.md` HRP-001b (`:98`), HRP-021 (`:131`).
````

new_str:
````text
  `docs/operations/hr-follow-up-process-register.md` HRP-001b (`:98`), HRP-021 (`:131`).
- S148 (the notes of 2026-11-25): [ADR-044](ADR-044-rest-period-model.md), the rest model, with its dossier
  `docs/references/rest-period-24h-research.md`; the refinement `.claude/refinements/REFINEMENT-s148-stage0-and-config.md`
  rev 5.1; the brief adjudications `.claude/reviews/S148-wave4C-briefs-adjudication.md` (ruling 6) and
  `.claude/reviews/S148-wave6A-briefs-adjudication.md` (ruling 9); addendum A1
  `.claude/plans/s148/ADDENDUM-A1-cell-clearing.md` (D-8); `docs/sprints/SPRINT-148.md`.
````

### K-19 · ADR-043 · Revision history: the S148 C-1 entry
- LF. Appended after the file's last line (`:928`).

old_str:
````text
  `weekly-timeline-v2` once TASK-14708 merges" now reads in the past tense, because TASK-14708 merged (`7cc6e65e`).
````

new_str:
````text
  `weekly-timeline-v2` once TASK-14708 merges" now reads in the past tense, because TASK-14708 merged (`7cc6e65e`).
- **S148 C-1 notes (2026-11-25).** Drafted by the Planning Drafter (Opus 5.5), applied by the Orchestrator; no decision
  changed. The Status field's "none of it is built yet" struck; D2's notes (owner Q1 of 2026-11-10 superseding S73 D-A's
  work clause; the bound as built; the bound on a clear, addendum A1 D-8); D6's correction (frozen with the first
  reader, ruling 9); D10's note and its pointer to ADR-044; D11's sentence (RR-4); provisional item 18's note
  (ruling 6); the S148 close note under "How it is built" (the stage boundary, the items passed on, ruling 6's
  governing-day evidence); References. Citations in the added text opened at `bda18b03`.
````

---

## Item 2 (O-8) — ADR-016 D10

### K-20 · `docs/knowledge-base/decisions/ADR-016-temporal-period-handling.md` · the marker convention's version list
- LF. At HEAD `:180`. The S147 move updated this parenthesis; the S148 move does the same. Verified `WeeklyTimelineCore.cs:154` (`EvaluationModel = "weekly-timeline-v3"`).

old_str:
````text
(`weekly-timeline-v1` at S145; `weekly-timeline-v2` since S147, see the 2026-10-30 footnote; `WeeklyTimelineCore.cs:144`)
````

new_str:
````text
(`weekly-timeline-v1` at S145; `weekly-timeline-v2` since S147, see the 2026-10-30 footnote; `weekly-timeline-v3` since S148, see the 2026-11-25 footnote; `WeeklyTimelineCore.cs:154`)
````

### K-21 · ADR-016 · the D10 marker footnote, v2 → v3 (O-8)
- LF. Inserted after the v1 → v2 footnote (`:188`), in its style. Verified: commit `575315e9` (TASK-14811, "the evaluation-model marker moves to weekly-timeline-v3 in the same commit"), merged in `26065e1a` (2026-11-12); `WeeklyTimelineCore.cs:145-152` (the v3 paragraph), `:154`; `AbsenceClassification.cs:121` (`ChildSickCallHome` → `Credit`); R-24 (`.claude/reviews/REFINEMENT-s148-adjudication-c1.md:65`: "one marker move, in S0-2; the config stage is refused by `replay-manifest-without-frozen-inputs`").

old_str:
````text
S147 moves it once only. ADR-043's S148 stage map opens on v2, and its next move is S148's to name.
````

new_str:
````text
S147 moves it once only. ADR-043's S148 stage map opens on v2, and its next move is S148's to name.

**D10 marker footnote 2026-11-25 (S148 TASK-14811, commit `575315e9`, merged `26065e1a` on 2026-11-12; QUAL-236; refinement ruling R-24): the marker's one S148 move, `weekly-timeline-v2` → `weekly-timeline-v3`** (`WeeklyTimelineCore.cs:154`). In plain terms: payroll's absence arithmetic changed for one new code. Under OK26 a parent may take the rest of the working day on which a child falls ill, the call-home day (`CHILD_SICK_CALL_HOME`), and it is credited toward the norm like the child's sick days (`AbsenceClassification.cs:121`), so a month holding call-home hours closes differently than v2 would close it. Under the convention above, that bumps the marker, in the same commit that adds the code to the credit class. A manifest frozen under v2 is therefore refused at replay with `replay-evaluation-model-changed`. S148 moves it once only (R-24). The 2026-10-29 note's proposal "`weekly-timeline-v3` (the config stage)" is overtaken: the config stage changed no arithmetic, and a manifest written before the configs were frozen is refused instead by `replay-manifest-without-frozen-inputs` (the 2026-11-23 amendment below). ADR-043's stage 1 opens on v3; its next move is stage 1's (S149; ADR-043's stage map proposes `monthly-norm-v1`).
````

---

## Item 3 — ADR-028

### K-22 · `docs/knowledge-base/decisions/ADR-028-work-time-persistence-allocation-gate-timer-retirement.md` · D1's note: manual hours gone; a cleared day is a kept, marked row (addendum O-29)
- CRLF. Inserted after D1 (`:32`). Verified: `git grep -n -i -e manual_hours -e ManualHours -- src docker` returns nothing; `init.sql:2181-2207` (the remark and `work_time_projection`, `cleared_by_outbox_id` at `:2205`); `WorkTimeProjectionRepository.cs:79-86` (`ClearDaySql`, shared by the live clear and the rebuild); `ProjectionBackfillService.cs` replays the three clear events (its type list and branches).

old_str:
````text
replays `WorkTimeRegistered` so a deploy against an existing event log rebuilds the projection.
````

new_str:
````text
replays `WorkTimeRegistered` so a deploy against an existing event log rebuilds the projection.

**D1 note 2026-11-25 (S148).** Two changes to the work-time record:
- **Manual hours are gone** (TASK-14809, merged `540ac9a9`; ADR-043 D10, D-2.4). `WorkTimeRegistered` and `work_time_projection` carry the day's intervals only: the `ManualHours` member and the `manual_hours` column were removed, with a reseed (reseed, don't migrate). A stray `manualHours` member from a legacy client is ignored. D2's S72 note that `manual_hours` stays is history.
- **A cleared day is a kept row with its marker set** (owner Q2, 2026-11-10; addendum A1, D-4; TASK-14840, merged `a331e452`). When every work period of a day is removed, the row is not deleted. The clear sets `cleared_by_outbox_id` to the `WorkTimeCleared` event's outbox id and advances the row's `outbox_id` to the same value, so an older registration replayed later cannot pass the latest-wins guard and bring the day back (`docker/postgres/init.sql:2181-2207`; `src/Infrastructure/StatsTid.Infrastructure/WorkTimeProjectionRepository.cs:79-86`, one statement shared by the live clear and the rebuild). A later registration of the same day clears the marker. **Every reader counts only rows whose marker is null**, and the rebuild replays clears with the same update. "Latest-wins" therefore now means: the latest event for the day, a registration or a clear, decides.
````

### K-23 · ADR-028 · the D4 amendment that `SendCheckIntervalOnlyTests.cs:17` cites
- CRLF. Inserted after the S88 amendment (`:86`), so the test comment's "ADR-028 D4 as amended by S148" becomes true. Verified: `tests/StatsTid.Tests.Regression/Outbox/SendCheckIntervalOnlyTests.cs:15-17`; `ApprovalEndpoints.cs:2603-2618` (the send gate: "interval hours alone, ADR-043 D10, D-2.4"), `:1228-1251` (team overview), `:1631-1653` (allocation breakdown), and the clear-marker filters at `:1237` and `:1640`.

old_str:
````text
  opened detail never contradicts the row chip.
````

new_str:
````text
  opened detail never contradicts the row chip.

**D4 amendment 2026-11-25 (S148 TASK-14809 + -T, merged `540ac9a9`; ADR-043 D10, D-2.4): the worked side is interval hours alone.** Clock periods are now a day's only work-time record (D1's note), so the gate's `worked` is the sum of the day's interval hours from `work_time_projection`, current days only; "+ manual hours" above is history. The gate is the send command's check (`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:2603-2618`), and the S88 read sides use the same sum (the team overview `:1228-1251`, the allocation breakdown `:1631-1653`), both skipping a cleared day. Pinned by `tests/StatsTid.Tests.Regression/Outbox/SendCheckIntervalOnlyTests.cs` (AC-S0-5b): a stray `manualHours` member that a legacy client posts is ignored, so hours sent as manual are not counted, and the equality catches them as lost hours.
````

### K-24 · ADR-028 · D5's note: payroll refuses stored `timer` rows (A6)
- CRLF. Inserted after D5 (`:95`). Verified: payroll `Program.cs:1009-1054` (`RefuseTypedEntriesAsync`; its remark at `:1012` names ADR-028 D5), `shortcut(QUAL-BF-003)` at `:1021`; TASK-14802 (`e2864079`, merged `e4e6b0ea`); the A6 ruling (`.claude/reviews/S148-wave1-briefs-adjudication.md:17`); told to the owner (`docs/sprints/SPRINT-148.md:388`); the runbook line owed (`:395`).

old_str:
````text
that no payroll/wage-type or rule-engine logic branches on `ActivityType="timer"` (P6).
````

new_str:
````text
that no payroll/wage-type or rule-engine logic branches on `ActivityType="timer"` (P6).

**D5 note 2026-11-25 (S148 TASK-14802, merged `e4e6b0ea`; the wave-1 brief adjudication, ruling A6; told to the owner 2026-11-10):** the last sentence no longer holds. Payroll now refuses a month whose calculation window holds a stored time entry typed as anything but ordinary work (an `ActivityType` other than null or `NORMAL`), and a historical `"timer"` row is refused like any other: 422 `payroll-activity-type-refused`, with a fixed, date-free body, and nothing written (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:1009-1054`). After a reseed no such row exists, so nothing observable changes. A database carried forward instead would meet the refusal with no in-product remedy (QUAL-BF-003); a line in the legacy-database upgrade runbook is owed at S148's close (`docs/sprints/SPRINT-148.md:395`). The send check already left `timer` rows out of the allocation (D4). The retained timer event classes are unchanged.
````

### K-25 · ADR-028 · D6's "`POST /api/time-entries` rows stay additive" marked
- CRLF. At HEAD `:105`.

old_str:
````text
- **`POST /api/time-entries` rows stay additive.** Each such registration is a separate piece of work:
````

new_str:
````text
- **`POST /api/time-entries` rows stay additive.** *(The route retired in S148; see the D6 note below.)* Each such registration is a separate piece of work:
````

### K-26 · ADR-028 · D6's "A cell cannot be cleared" marked (addendum O-29)
- CRLF. At HEAD `:108` (the addendum's `ADR-028:108`).

old_str:
````text
- **A cell cannot be cleared.** The save refuses hours ≤ 0,
````

new_str:
````text
- **A cell cannot be cleared.** *(Superseded 2026-11-25: S148 built clearing; see the D6 note below.)* The save refuses hours ≤ 0,
````

### K-27 · ADR-028 · D6's note: clearing as built; the retired route (addendum O-29; log "Docs owed")
- CRLF. Inserted after D6's "What was given up" paragraph (`:113`). Verified: `TimeEntryProjectionRepository.cs:144` and `AbsenceProjectionRepository.cs` (`RetireSkemaCellAsync`, using this decision's mark SQL); `WorkTimeProjectionRepository.cs:206` (`ClearDayAsync`); `SkemaEndpoints.cs:3697-3704` (the request's `cleared` block); the merge `a331e452` (2026-11-12); QUAL-274 (the from-empty rebuild gap, registered at C-1).

old_str:
````text
(`SPRINT-145.md` § "Task ledger", TASK-14507, and § "CA-5").
````

new_str:
````text
(`SPRINT-145.md` § "Task ledger", TASK-14507, and § "CA-5").

**D6 note 2026-11-25 (S148; owner Q2, 2026-11-10, "build clearing in S148"; addendum A1, D-1 … D-14; TASK-14840 + -T and TASK-14841, merged `a331e452`; SPRINT-72 R17 closed).** In plain words: a Skema cell can now be emptied, and the removal is recorded with who made it, never as a silent delete and never as a 0-hour row.
- **A clear retires the cell's current row and inserts nothing.** For a project entry or an absence, the save enqueues `TimeEntryCleared` or `AbsenceCleared` and marks the cell's current Skema row `superseded_by_outbox_id` = the clear's outbox id, with this decision's mark SQL (`RetireSkemaCellAsync` in `src/Infrastructure/StatsTid.Infrastructure/TimeEntryProjectionRepository.cs` and `AbsenceProjectionRepository.cs`). No row is inserted, so the cell has no current row. A clear of a cell with no current row records nothing.
- **A cleared work-time day's row is kept and marked,** never deleted: `cleared_by_outbox_id` set and its `outbox_id` advanced to the clear's (D1's note; `WorkTimeProjectionRepository.cs:206`, `ClearDayAsync`).
- **One decision, in the lock.** What a clear removes is read once, inside the save's per-employee lock, before the first write, so two concurrent clears of one holiday cell give the day back once. A clear passes the save's gates unchanged: the approval lock, the month bound, the employment window, and who may write (the employee, or HR and above in scope).
- **The rebuild replays clears** with the same mark and the same guarded update, so a rebuild from the event log reproduces the live current set. One limit is registered: from empty tables, a fact not yet copied to `events` is missing until the next start after it is (QUAL-274).
- **A holiday-type clear gives the stored feriedage back,** through the ungated reduction, with the balance event (ADR-032, note of this date). A care day or senior day is cleared whole.
- **`POST /api/time-entries` retired in S148** (TASK-14802, merged `e4e6b0ea`). The source class `TIME_POST` stays for rows written before; such rows are never marked or cleared, and after a reseed none exist. The other bullets above stand, with "current" now meaning not superseded and, for work time, not cleared.
````

---

## Item 4 — ADR-026

### K-28 · `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` · D2's amendment note: the mapper's real shape
- CRLF. Inserted after D2's "No-mapping events" paragraph (`:136`); "**No Phase B dependency**." follows twice in the file, so it cannot be the anchor. Verified: `IAuditProjectionMapper.cs:35-42` (and its own remark `:8-15` on the S43 change); `AuditProjectionContext.cs:24-29`; `AuditProjectionRowData.cs:20-24`; `IAuditProjectionMapperRegistry.cs:18`; `SkemaEndpoints.cs:2887-2899` (`WriteClearAuditRowAsync`); the wave-2 brief adjudication, 14840 BC-6 (`.claude/reviews/S148-wave2-briefs-adjudication.md:58`).

old_str:
````text
**No-mapping events**: events without an `IAuditProjectionMapper<T>` AND without endpoint-direct projection-row construction are NOT audit-relevant. This is the opt-in mechanism — adding a new event type to the audit surface = either writing a mapper (for payload-only mapping) OR constructing the row inline at the endpoint (for events needing context).
````

new_str:
````text
**No-mapping events**: events without an `IAuditProjectionMapper<T>` AND without endpoint-direct projection-row construction are NOT audit-relevant. This is the opt-in mechanism — adding a new event type to the audit surface = either writing a mapper (for payload-only mapping) OR constructing the row inline at the endpoint (for events needing context).

**D2 amendment note 2026-11-25 (S148; the wave-2 brief adjudication, 14840 BC-6; the interface's own remark since S43 Step 7a):** the interface and records as built differ from the draft signatures above, which stay as the historical authoring intent. The live shape (`src/SharedKernel/StatsTid.SharedKernel/Audit/`):
- `IAuditProjectionMapper<TEvent>` has one method, `AuditProjectionRowData Map(TEvent @event, AuditProjectionContext context)` (`IAuditProjectionMapper.cs:35-42`), not `MapToRow`.
- `AuditProjectionContext(string? ActorId, string? ActorPrimaryOrgId, Guid? CorrelationId, DateTimeOffset OccurredAt, string? ResolvedTargetOrgId = null)` (`AuditProjectionContext.cs:24-29`). The occurrence time carries its offset, and `ResolvedTargetOrgId` (added in S44, TASK-4407) carries the target organisation the endpoint resolved before calling the mapper: this section's "passes the resolved value through the Context parameter", made concrete.
- `AuditProjectionRowData(AuditVisibilityScope VisibilityScope, string? TargetOrgId, string? TargetResourceId, string DetailsJson)` (`AuditProjectionRowData.cs:20-24`): pre-serialised JSON, not a `JsonDocument`, which spares the caller its disposal.
- The endpoint-direct pattern holds as described: the endpoint enqueues the event, then writes the row in the same transaction through `auditRepo.InsertAsync(conn, tx, eventId, outboxId, eventType, rowData, context, ct)`. S148's example is the Skema save's cell clears (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:2887-2899`). The runtime-keyed lookup the backfill uses also exists (`IAuditProjectionMapperRegistry.cs:18`).
````

### K-29 · ADR-026 · D3's inventory: the three removal events (addendum O-29; D-5, NR-A5)
- CRLF. Inserted after the 2026-05-23 clarification block (`:206`), before "**S40 mapping authorship**". Verified: the catalog rows `docs/operations/audit-projection-catalog.md:146-148`; the three mappers in `src/Backend/StatsTid.Backend.Api/AuditMappers/`; `EventSerializer.cs:151-153`; addendum D-5 (`ADDENDUM-A1-cell-clearing.md:163-177`).

old_str:
````text
> This clarification preserves ADR-026's `ACCEPTED` status. Follows the post-ACCEPTED clarification-block pattern established by the pre-rule projection disclaimer (commit `a0e30ed` 2026-05-23). Bound to architectural finding at S44 Step 4 cycle 1 dual-lens consensus (Codex + Reviewer Agent both flagged L182/L194 contradiction); resolution committed at S44 / TASK-4402 / commit [this commit].
````

new_str:
````text
> This clarification preserves ADR-026's `ACCEPTED` status. Follows the post-ACCEPTED clarification-block pattern established by the pre-rule projection disclaimer (commit `a0e30ed` 2026-05-23). Bound to architectural finding at S44 Step 4 cycle 1 dual-lens consensus (Codex + Reviewer Agent both flagged L182/L194 contradiction); resolution committed at S44 / TASK-4402 / commit [this commit].

**D3 note 2026-11-25 (S148 addendum A1, D-5; NR-A5 confirmed; TASK-14840, merged `a331e452`; catalog rows `docs/operations/audit-projection-catalog.md:146-148`): three removal events are audit-relevant, although their registration siblings are not.** Since S148 an employee, or HR for them, can empty a Skema cell (ADR-028 D6, note of this date). Each removal is its own event, `TimeEntryCleared`, `AbsenceCleared` or `WorkTimeCleared`, and writes one TENANT_TARGETED `audit_projection` row in the save's transaction: the target organisation is the employee's primary organisation, resolved by the save; the resource is the employee id; the details name the date, the cell's key, the removed registration's event id and hours, and for an absence the holiday days given back. *Why, against the exemption of `TimeEntryRegistered` and `AbsenceRegistered` in the list above:* that exemption rests on a registration's row naming its own actor. A removal leaves no row naming who removed it, since the retired row still names whoever registered it, so without an audit row "who removed this sick day?" would be answerable only by joining the retired row's `superseded_by_outbox_id` to the outbox. Removals are also far rarer than registrations, so the volume argument is weak. *Declined:* exempting them beside their siblings; the event store would still hold the actor, but the removal would never show on the audit surface. The living list of audit-relevant events is the catalog, which `AuditProjectionParityTests` checks against the serializer and the mappers.
````

---

## Item 5 — ADR-032

### K-30 · `docs/knowledge-base/decisions/ADR-032-vacation-consumption-correctness.md` · the S148 note (addendum O-29; the balance seed)
- CRLF. Inserted after D7's last bullet (`:52`), before "## Consequences". Verified: `EntitlementBalanceRepository.cs:595` (`SumCurrentFeriedageAsync`; called at `:151`, `:266`, `:353`, `:517`); QUAL-196 FIXED (`quality-finding-register.md:453`, "owner Q3 and Q4"); the sprint log calls the 2026-11-11 questions owner Q3 (`docs/sprints/SPRINT-148.md:673`) and owner Q4 (`:699`), and the addendum numbers the first one its Q1 (Brief contradictions, item 3); merges `a331e452` (TASK-14840) and `6bedcf2c` (TASK-14842), both 2026-11-12.

old_str:
````text
- Precision: per-absence feriedage rounded at 4dp (deterministic); display 2dp.
````

new_str:
````text
- Precision: per-absence feriedage rounded at 4dp (deterministic); display 2dp.

**S148 note (2026-11-25) — clearing gives holiday days back, and a missing balance row is created right (addendum A1, D-6; owner Q3 and Q4, 2026-11-11, which the addendum numbers Q1 and Q4; TASK-14840, merged `a331e452`; TASK-14842, merged `6bedcf2c`; QUAL-196 FIXED).** In plain words: when someone empties a holiday cell in Skema, the day goes back to their balance; and when the system has to create a missing balance record, it starts from what the employee has actually booked and from the year's real entitlement, never from zero.
- **A clear gives back what is stored.** Clearing a quota-consuming absence lowers `used` by the cleared row's stored `feriedage` (D2's single valuation, so after a D4 revaluation the revalued figure), through the existing ungated reduction, with one `EntitlementBalanceAdjusted` per entitlement type. A clear never asks the quota rule and is never refused for quota: per type, a request whose net change (Σ posted feriedage − Σ stored feriedage of the rows it replaces or clears) is zero or negative skips both the rule-engine check and the guarded update. A mixed request whose net change is positive is checked like any increase, and the save is all or nothing.
- **A missing balance row starts from the bookings.** Every writer that creates one (the Skema save's two, the D4 revaluation, and the year-end carryover at both its call sites) seeds `used` with the sum of the employee's current stored feriedage for that type and holiday year (`src/Infrastructure/StatsTid.Infrastructure/EntitlementBalanceRepository.cs:595`, `SumCurrentFeriedageAsync`).
- **…and its total from the year's own quota.** `total_quota` is the applicable annual quota, pro-rated where the entitlement config says so, read from the config in force on the first day of the record's own holiday year: never the live row, never the closing year's. So the revaluation no longer creates a row at 0, and the carryover seeds next year's quota (the addendum's cycle-3 rulings R1, R2).
- **Residuals** (QUAL-196's row): until a write creates the record, three reads still show 0 used for a booked day, while the in-lock guard stays authoritative, so nothing is over-booked; and when no config row is in force at the next year's start, the carryover seeds the closing year's quota and logs it, unreachable from the seeds.
````

---

## Item 7 — ADR-039, `docs/SECURITY.md`, RES-002 (N-5; RR-8 pointers)

### K-31 · `docs/knowledge-base/decisions/ADR-039-midnight-crossing-projection-normalization.md` · Status: the supersession for the daily rest check
- LF. At HEAD `:5`.

old_str:
````text
| **Status** | accepted — owner-ratified 2026-08-20 |
````

new_str:
````text
| **Status** | accepted — owner-ratified 2026-08-20. **For the daily rest check, D5 and D5b are superseded by [ADR-044](ADR-044-rest-period-model.md) (S148, 2026-11-25); see their notes** |
````

### K-32 · ADR-039 · D4's note: the continuity link is now read by nothing (QUAL-275)
- LF. At HEAD `:61`. Verified: `ComplianceEndpoints.cs:211` and `PeriodCalculationService.cs:3781` (`SourceStintId = r.EventId`); QUAL-275 (`quality-finding-register.md:532`).

old_str:
````text
field today (`TimeEntry.cs`) — this ADR adds one to the projection row / consumed model.
````

new_str:
````text
field today (`TimeEntry.cs`) — this ADR adds one to the projection row / consumed model.

> **Note 2026-11-25 (S148):** the link is still set (`ComplianceEndpoints.cs:211`, `PeriodCalculationService.cs:3781`)
> and copied to both halves by the normalizer, but since S148 wave 3 nothing reads it: the rest checks build their
> stints from Skema's clock intervals, which carry no source id (ADR-043 D10; ADR-044). Registered as QUAL-275.
````

### K-33 · ADR-039 · D5's note (the rest checks' input changed; superseded for the daily rest check)
- LF. At HEAD `:78`, D5's last line. Verified: `RestPeriodRule.cs:693-740` (stints from intervals), `:36-48` (the class's ADR-039 segmentation note), `:560-616` (`CheckWeeklyRest`, start-day counting); QUAL-256. The wave-3 brief adjudication's owed note (`:166`) said "a rest belongs to the month in which it ends"; the plan records that wave 3b supersedes it (`PLAN-s148.md:856`), so the note gives ADR-044's dating.

old_str:
````text
  `SourceStintId` is null (a deterministic shared id), else a caller-supplied crossing cannot be rejoined.
````

new_str:
````text
  `SourceStintId` is null (a deterministic shared id), else a caller-supplied crossing cannot be rejoined.

> **S148 note (2026-11-25): the rest checks' input and the daily rule have changed; superseded for the daily rest
> check by [ADR-044](ADR-044-rest-period-model.md).** Since S148 wave 3 the two rest checks no longer read time entries:
> they measure the clock intervals employees type in Skema (`work_time_projection`), joined across midnight inside the
> rule (`RestPeriodRule.cs:693-740`; ADR-043 D10). The hours-summing checks keep the per-day entries as above. Since
> wave 3b (owner Q2, 2026-11-13) the daily check judges every work end on the 24 elapsed hours before it and dates a
> finding the day the work ends, instead of measuring the gap between start-day stints (ADR-044 D1, D3). **What still
> holds:** the rationale-of-record correction (payroll never runs the rest checks; they run only on the unsegmented
> monthly compliance path, `RestPeriodRule.cs:36-48`), and the weekly check still counts a stint under its start day
> (`:560-616`), which is registered as wrong in both directions (QUAL-256).
````

### K-34 · ADR-039 · D5b's pointer (the boundary fetch is two days for the rest check)
- LF. At HEAD `:119`, the S145 amendment's last line. Verified: `ComplianceEndpoints.cs:188`, `:243-249`.

old_str:
````text
> of no longer exists. The two projection reads share one read-only REPEATABLE READ snapshot (QUAL-200).
````

new_str:
````text
> of no longer exists. The two projection reads share one read-only REPEATABLE READ snapshot (QUAL-200).

> **Pointer (S148, 2026-11-25):** for the rest checks the boundary fetch is now two days, not one. The compliance read
> starts at `monthStart − 2`, because a work end on the 1st looks back 24 elapsed hours and the day before the 1st can
> be 23 hours long (`ComplianceEndpoints.cs:188`; ADR-044 D3). A manager's read takes those days only when the previous
> month is submitted (SEC-059; ADR-044 D7). Superseded for the daily rest check by ADR-044.
````

### K-35 · `docs/SECURITY.md` · a new subsection: a manager sees nothing of an unsent month, at the month edge too (N-5; the TASK-14813 Step 5a adjudication, ruling 1)
- LF. Inserted after the "Segregation of duties" section's last bullet (`:196`), before "### Flat approval authority". `docs/SECURITY.md` has no section on the manager visibility rule today (`grep -n "ApprovalReadTier\|S124" docs/SECURITY.md` finds none). Verified: `ApprovalVisibility.cs:59-60` (`SUBMITTED`, `EMPLOYEE_APPROVED`, `APPROVED`; REJECTED removed in S127, `:26-36`); `ApprovalReadTier.cs:44`, `:116`; `ComplianceEndpoints.cs:188`, `:243-249`, `:280-282`; `SkemaEndpoints.cs:571-586`; SEC-059 (`security-finding-register.md:226`).

old_str:
````text
- Acting on someone else's record is governed by the scope rules above.
````

new_str:
````text
- Acting on someone else's record is governed by the scope rules above.

### A manager sees nothing of an unsent month (S124, S128; the month edge, S148)

**Why.** The owner ruled on 2026-07-30 that a manager cannot see anything about an employee's month before that month is submitted. A visibility rule is only real at every read that can serve the month's content, so the reads that serve it are gated one by one. [RES-002](knowledge-base/resolutions/RES-002-manager-draft-visibility-enforced-at-surface-only.md) is the census and the record of what is still open.

**As built:**
1. **The tier model (S128, owner ruling R1).** The employee and HR-or-above read everything in their scope. A leader-tier read is gated by `ApprovalVisibility.IsSubmittedToManager`, which admits `SUBMITTED`, `EMPLOYEE_APPROVED` and `APPROVED` only (`ApprovalVisibility.cs:59-60`; `REJECTED` left the set in S127, so a rejected, editable month is withheld too). The month-keyed reads answer a withheld month with 403 (`ApprovalReadTier.cs:116`).
2. **The month edge (S148, SEC-059 FIXED).** The 11-hour rest check judges a work end on the 1st on the 24 hours before it, so it reads the previous month's last two days ([ADR-044](knowledge-base/decisions/ADR-044-rest-period-model.md) D3). A leader-tier read uses those days only when that month is submitted (`ComplianceEndpoints.cs:243-249`), and the Skema month view serves its neighbouring boundary days to a leader only when their own month is submitted (`SkemaEndpoints.cs:571-586`). While the previous month is withheld, the leader's answer lists the 1st as "not checked" if the day before was an employed day and the 1st has a clock interval (`ComplianceEndpoints.cs:280-282`), so it never looks falsely compliant, and the marker reveals nothing of the unsent month. Nothing is stored, so the full finding appears at the first read after both months are sent.

**Still open:** RES-002's remainder (month-less reads that serve the same data), and SEC-059's two residuals: a leader's time-entry read still starts two days before the month, and on a withheld read a night shift begun before the month is read from 00:00 on the 1st.
````

### K-36 · `docs/knowledge-base/resolutions/RES-002-manager-draft-visibility-enforced-at-surface-only.md` · the S148 note
- CRLF. Inserted before "## Rationale" (`:90`), which occurs once. Verified: `SkemaEndpoints.cs:558-591` (the boundary block), `ComplianceEndpoints.cs:188`, `:243-249`; `ApprovalVisibility.cs:26-36`, `:59-60` (the allowlist at RES-002's `:101-103` still names REJECTED, which S127 removed); `POST /api/time-entries` retired by TASK-14802 (RES-002's `:116`).

old_str:
````text
## Rationale
````

new_str:
````text
> **S148 note (2026-11-25; SEC-059 FIXED; the TASK-14813 Step 5a adjudication, ruling 1, widened to two days by wave 3b; recorded neutrally).** The rule now reaches the month edge. The Skema month view (`SkemaEndpoints.cs:558-591`) and the compliance read (`ComplianceEndpoints.cs:188`, `:243-249`) give a leader-tier reader the last two days of the previous month, and on the Skema view the first day of the next, only when that month is submitted, decided by `ApprovalVisibility.IsSubmittedToManager`, the month gate's own predicate. The rest check needs those days because a work end on the 1st is judged on the 24 hours before it (ADR-044). Before: the Skema view served neighbouring days to a leader whatever their status (since S72), and from S148 wave 3 a leader's rest finding dated 31 March told them when the employee stopped work on a day of an unsent March. While the neighbour is withheld, the leader's compliance answer lists the 1st as "not checked" instead of looking compliant. Neither read is new to the census above: both were already gated by their own month, and the gate now also covers the days they borrow from the neighbouring months. The residuals are SEC-059's. Two side facts: the Consequences section's allowlist (SUBMITTED/EMPLOYEE_APPROVED/APPROVED/REJECTED) is out of date since S127, which removed REJECTED (`ApprovalVisibility.cs:26-36`, `:59-60`); and the write-class section's `POST /api/time-entries` was retired in S148 (TASK-14802), so the Skema save is the one write path.

## Rationale
````

---

## Item 8 — ADR-044 (new)

### K-37 · NEW FILE `docs/knowledge-base/decisions/ADR-044-rest-period-model.md`
- Write LF. The name is a proposal (choice 3). Every code cite opened at `bda18b03`; the legal facts are the dossier's Part 1 (verification) and the adjudication's rulings, cited by their ids. Decided and open are marked apart ("Decisions" vs "Open — not decided here").

````text
# ADR-044 — The rest model: every work end is judged on the 24 hours before it

| Field | Value |
|-------|-------|
| **Status** | **accepted** — owner-ruled 2026-11-13 (S148 owner Q2, "S148, after wave 3 (Recommended)", which also approved the spec wording at `SYSTEM_TARGET.md:272-284`); built in S148 wave 3b (merged `2810ebeb`, 2026-11-16; CI-verified at CI-W3b, run `43851473962`). Written down at S148's close (2026-11-25); it records rulings already made and decides nothing new |
| **Sprint** | S148 |
| **Supersedes / amends** | **Supersedes, for the daily rest check,** ADR-039 D5 (rest measured between start-day stints) and D5b's one-day boundary fetch, and the wave-3 interim "a rest is judged in the month in which it ends". **Amends** ADR-043 D10 (its rest-check half, as built) and D11 (one rest flag per time type is not enough). **Closes** QUAL-253. **Leaves open, registered:** QUAL-254, QUAL-255, QUAL-256, QUAL-277 |
| **Domains** | Rule Engine, Backend, Frontend, Security, Test |
| **Tags** | rest-period, daily-rest, 11-hour-rule, 24-hour-look-back, 13-hour-span, derogation, weekly-rest, elapsed-time, time-zone, dst, compliance, manager-visibility, arbejdsmiljøloven |
| **Evidence** | `docs/references/rest-period-24h-research.md` (Part 1, the verification, governs: R1–R5, V-1 … V-78, corrections 1–19); the adjudication `.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md` (RR-1 … RR-8, Fable 5.2); the wave-3b brief adjudication `.claude/reviews/S148-wave3b-briefs-adjudication.md` (items 1, 5–9, 16, 17); the wave-3b Step 5a adjudication `.claude/reviews/S148-WAVE3B-5a-adjudication-c1.md` (ruling 1); the TASK-14813 Step 5a adjudication `.claude/reviews/S148-TASK-14813-5a-adjudication-c1.md` (rulings 1–3). Code cited at `bda18b03` |

*How to read the labels.* **R1–R5** are the research questions, **V-n** the verification's claim rows and **Corr. n** its numbered corrections, all in the dossier. **RR-n** are the research adjudicator's rulings. **(a)–(f)** are the questions the sources leave silent (RR-6). **QUAL-n / SEC-n** are rows of the quality and security registers.

## The decision, in one sentence

**At the end of every piece of work, StatsTid looks back over the 24 elapsed hours before it, in Danish time; if the longest unbroken rest in that window is shorter than the agreement's minimum (11 hours), the day the work ends gets a finding.**

## What was wrong, in plain words

Until S148 the check grouped work by the day it started and measured only the gap between one day's work and the next. So it never noticed three real breaches:
- one shift over 13 hours (a 24-hour shift showed nothing at all);
- a day shift followed by a night shift on the same day;
- a split day longer than 13 hours from first start to last end, such as 08–12 plus 17–22.

When it did report, the number could be wrong. Tuesday 22:00–23:59, then Wednesday 06:00–14:00, showed "6.0 hours, Tuesday". The law's test gives "8.0 hours, Wednesday": the longest rest in the 24 hours before Wednesday 14:00 is 14:00–22:00 on Tuesday.

The model dates from S132. It became visible on real data in S148 wave 3, when the check started reading the clock times typed in Skema (TASK-14813). Both review lenses found the gap (Codex W-2, Fable N-1). The owner chose to register it and have the rule researched first (owner Q1, 2026-11-13, "No, register only"), then to build the fix in S148 (owner Q2).

## Why this rule: the law, verified

- Arbejdsmiljøloven § 50 and BEK nr. 606 af 29/06/2026 § 3 require "at least 11 consecutive hours of rest within each period of 24 hours", and neither says where the period starts.
- It is not the calendar day (the European Commission's 2023 Interpretative Communication), and since 29 December 1996 it is not "24 hours from the start of normal working hours" either: Parliament removed that anchor on purpose (LOV 1196/1996; the L 59 remarks).
- Arbejdstilsynet, which enforces the rule, gives one test, in the same words in 1997, 2016 and 2024 (MED 3503, VEJ 9576, VEJ 9666 § 2): from a point inside working time, count 24 hours back; that stretch must hold 11 unbroken hours of rest.
- Two elements are inferences, not quotations: the test applies at every point of working time, and only the part of a rest inside the window counts. Every reading without them passes a 24-hour shift, which the appeal board called a significant breach (KEN 9938/2006) and the Commission forbids ("no more than 13 consecutive hours"). So the sources support them by elimination (V-14, V-15).
- In practice: **no work may end more than 13 hours after the last full 11-hour rest ended** (V-75). "13 hours on, 11 off" is lawful; 13 hours and one minute is not.

## Decisions

### D1 — The rule: every work end, judged on the 24 hours before it (RR-1)
- **The ends judged.** A stint is a run of intervals joined across midnight; each stint's latest end is judged. Judging at the ends is enough, because the longest rest in the window only shrinks while work goes on (V-74).
- **The value.** The longest single unbroken rest inside the 24 elapsed hours before the end. A rest that began before the window counts from the window's start. Short rests never add up (the order allows summing only under home-work derogations, BEK 606 § 33 stk. 4). Exactly the minimum passes ("mindst 11").
- **The 13 hours is derived,** 24 minus `MinimumRestHours`, not a setting of its own. The check does not read `MaxDailyHours`; that separate hours check stays as a backstop for hours that are not on the clock.
- **One finding per day.** Among a day's failing ends, the lowest value; equal values name the earliest end.
- **The minimum of the end's own date.** When the minimum changes on a Monday, a Sunday end is judged by Sunday's value and a Monday end by Monday's, although their windows overlap (ADR-043 D12; RR-7; TASK-14829).
- Code: `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs:354-458` (`CheckDailyRest` and its remark), `:460-484` (`LongestRestHours`), `:76` (the 24-hour window).

### D2 — The true value decides; the shown value is rounded down (wave-3b brief adjudication, item 9)
- Both thresholds (the minimum, and the 8-hour floor of D5) compare the true value. The shown value is rounded down to one decimal: `Math.Floor(v × 10) / 10` on the server (`RestPeriodRule.cs:434`), and on the screen `restDisplayHours` (`frontend/src/hooks/useSkema.ts:1051-1053`).
- *Why:* rounding half up would show "11.0, minimum 11.0" as a breach, and "8.0" as both a warning and a violation. Rounding down makes the figure agree with the verdict at every threshold that is a multiple of 0.1.
- *Given up:* the figure can understate a rest by up to six minutes; 8 h 27 min shows 8,4. This supersedes the half-up direction of TASK-14814's Step 5a ruling 1.
- The finding keeps its shape (`DAILY_REST`). Its message names the judged end, in the final Danish copy (item 17; `RestPeriodRule.cs:435-441`).

### D3 — Dating, the month and the read window (RR-1; wave-3b Step 5a ruling 1)
- **A finding is dated the calendar day its work ends,** and belongs to that day's month. This replaces wave 3's interim, "a rest is judged in the month in which it ends", with its finding dated the earlier day.
- **The read starts two days before the month** (`monthStart − 2`; `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs:188`). A work end on the 1st looks back 24 elapsed hours, and the day before the 1st can be 23 hours long (the March clock change; 1 April 2030 is the next case). No window reaches further, so nothing else is needed. Skema serves the same two previous days to the screen (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:558-591`).
- **The month's last midnight.** The engine owns the cut: it builds the stints without the intervals dated after the period (`RestPeriodRule.cs:171`), so a night shift over the month's last midnight is judged as ending at 24:00 on the last day, and again in full by the next month. A chain already over the limit before a month edge can therefore show in both months, each with its own true value.

### D4 — Elapsed hours in Europe/Copenhagen, with the zone handed in (RR-1; wave-3b brief adjudication, items 5, 6, 8)
- All arithmetic is in **elapsed time** in Europe/Copenhagen, never a wall-clock difference and never the host's or the browser's own zone. The clock-change døgn are 23 and 25 hours long (PAV 19.9).
- **The zone is a parameter.** The engine host checks at boot that it can resolve the zone and passes `CopenhagenBusinessDate.Zone` into the rule (`src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs:11`, `:174-176`). `Evaluate` refuses a null zone once an interval is kept (`RestPeriodRule.cs:130-155`), and nothing under `Rules/` reads a zone (`git grep -n -e CopenhagenBusinessDate -e FindSystemTimeZoneById -- src/RuleEngine/StatsTid.RuleEngine.Api/Rules` is empty). This keeps the engine pure: external data arrives as a parameter, never fetched (ADR-002).
- **Where the wall clock is not one-to-one, the defaults err toward reporting** (the sources say only that the rule is in hours): a time in October's repeated hour is the earlier instant for a start and the later one for an end; a time in March's missing hour is the changeover instant, so an interval wholly inside that hour has no length and breaks no rest, but its end is still judged; an end typed 23:59 is the end of the day, because Skema cannot hold 24:00 and the midnight join already counts that minute as work (`RestPeriodRule.cs:486-538`, `ToInstant` and `ChangeoverInstant`).
- **These instants are never stored.** They are built from the typed clock times inside one calculation and discarded. What is stored and shown is a business date, the Copenhagen day (ADR-041). ADR-041's "instants stay UTC" governs stored instants and is untouched.
- **The browser** computes the same way, in Europe/Copenhagen. A browser without Danish time-zone data shows "Hviletiden kan ikke kontrolleres i denne browser, fordi tidszonedata for Danmark mangler." instead of warnings, rather than looking compliant (`frontend/src/components/SkemaDayPanel.tsx:304`).

### D5 — The derogation covers no rest below 8 hours (RR-2)
- Where the agreement allows a derogation (`RestPeriodDerogationAllowed`; HK and PROSA), a short rest of **8.0 hours or more is a WARNING, and one below 8 hours a VIOLATION** (`RestPeriodRule.cs:73`, `DerogationFloorHours`; `:428-430`). The 8 is the order's number (BEK 606 § 19; Statens arbejdstidsaftale § 12, identical in OK21 and OK26), not an agreement setting. The AC agreement has no rest clause and no flag.
- The warning says what StatsTid does not check: a cut is allowed at most once per 7 days (twice by local agreement), and only with equal compensatory rest straight after (`RestPeriodRule.cs:82-83`).

### D6 — What counts as work, and as rest
- **Work** is every interval the employee typed in Skema (`work_time_projection`), the only source of time of day since S148 (ADR-043 D10). Overlapping or touching intervals are one block. The hours checks (maximum daily hours, the 48-hour average) still sum the project entries.
- **Unregistered time is rest,** the directive's own definition (Art. 2(2)); this defaults the silent question (f), what came before the first record (RR-6). A day whose project hours are not all on the clock is listed as "not checked" (R-22; `RestPeriodRule.cs:284`, `FindRestNotCheckedDays`), so it never looks falsely compliant. The list covers the period's own days only (QUAL-277).
- **Voluntary unsocial hours.** A wholly voluntary stint is neither judged nor counted as work in another end's window (`SYSTEM_TARGET.md:300`). **A stint cut at the read edge,** whose first interval starts at exactly 00:00 on a day before the period, is never voluntary, because its beginning is unknown; the rule errs toward reporting (`RestPeriodRule.cs:733`; the TASK-14813 Step 5a ruling 3; cross-referenced from QUAL-255). Nothing writes the voluntary flag today, so neither rule is observable yet.
- **On-call** cannot be registered today (ADR-043 D11), so no interval carries it. When it can, one rest flag per time type is not enough: on-call at home is daily rest but never weekly rest (ADR-043 D11's note; Open, item 4).

### D7 — A manager sees nothing of an unsent month, at the month edge too (TASK-14813 Step 5a ruling 1; wave-3b item 1; SEC-059)
- A leader-tier read uses the two days before the month only when the previous month is submitted, decided by the month gate's own predicate (`ApprovalVisibility.IsSubmittedToManager`; `ComplianceEndpoints.cs:243-249`); Skema serves boundary rows to a leader on the same condition (`SkemaEndpoints.cs:571-586`). The employee and HR-or-above get them always.
- Until then the month's first day is listed as "not checked", when the day before was an employed day and the 1st has a clock interval (`ComplianceEndpoints.cs:280-282`). A finding still shown for a work end on the 1st is built from the sent month alone, so it is true, possibly understated. Once both months are sent, the full finding appears: the answer is computed at each read and never stored.
- Residuals (SEC-059): a leader's time-entry read still starts two days back; on a withheld read a night shift begun before the month is read from 00:00 on the 1st. See `docs/SECURITY.md` and RES-002.

### D8 — One rule in two places, proved equal (ADR-043 D10)
- The server's check and Skema's instant warning implement the same rule (`analyzeRestPeriods`, `frontend/src/hooks/useSkema.ts:979`).
- One shared table of 33 scenarios, schema 2 (`tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json`), is read by both test runners (`tests/StatsTid.Tests.Unit/Rules/RestParityTableTests.cs`; `frontend/src/hooks/__tests__/restParity.test.ts`). Each side checks every finding's date and value, so a row cannot pass on one side only.
- The screen marks a finding on the days whose typing opened it (wave-3b item 7, rule (b′)), so one finding can show on two or three days.

## Open — not decided here

Each item is registered or recorded; none blocks the rule above.
1. **How a permitted cut is used (QUAL-254, Medium).** The derogation is one yes/no flag. StatsTid counts no uses per 7 days, does not apply "not two døgn in a row", checks no compensatory rest (equal to the cut and taken before the next work, Jaeger para. 97), records no decision, and models no postponement (BEK 606 § 16 stk. 3, § 19; CIR 9807/1983). Blocked on expert questions (a), (b), (d); needs an HRP row when designed.
2. **The per-person exemption (QUAL-255, Medium).** BEK 606 § 6 exempts staff who set their own hours or manage, assessed for each person, never for a whole group (PAV 19.8). StatsTid checks everyone, so exempt AC staff and managers can get findings, which errs toward reporting. The shape is a dated HR fact with the assessment recorded, never an agreement-level switch.
3. **The weekly rest day (QUAL-256, High).** `CheckWeeklyRest` still counts start days in fixed 7-day blocks from the month's first day (`RestPeriodRule.cs:560-616`). It misses multi-day chains and weeks across a month edge, and flags a week that holds a 40-hour rest. The law's test is 35 unbroken hours, at most 6 døgn apart by statute and 10 working days under the state agreement § 12 stk. 3, and on-call never counts toward it. Blocked on expert questions (a), (e); the read would need about two weeks back.
4. **On-call's two rest flags** (ADR-043 D11, note of 2026-11-25; provisional item 13).
5. **Postponement.** CIR 9807/1983 (central administration, signed by AC) allows the rest to be postponed, up to 22 hours of work in a row; whether it still works under BEK 606 is expert question (c).
6. **The boundary day's clocking (QUAL-277).** A work end on the 1st looks back into the previous month's last day, but the "not checked" list covers the period's own days only.
7. **Several employments.** Daily rest applies to a worker's several contracts with one employer taken together (CJEU C-585/19); StatsTid models one employment per person.
8. **The 48-hour average is outside this decision.** It ignores its configured reference period (QUAL-123), and its shown value still rounds half up against a maximum, so 48.04 reads "48.0" as a breach (recorded below the quality register's floor; `RestPeriodRule.cs:623-654`).

**The silent questions** (RR-6), for the expert hand-off package (`docs/references/phase-b-handoff-package.md`): (a) the 7-døgn anchor, and whether it rolls; (b) what "døgn" means in "not 2 døgn in a row"; (c) whether CIR 9807 still works under BEK 606, and how its "arbejdsdøgn" maps onto the look-back; (d) whether compensatory hours come on top of the next rest or inside it ("on top", the stricter reading, if built before an answer); (e) how a night shift counts toward § 12 stk. 3's "arbejdsdage". Question (f) is defaulted in D6.

## Consequences

- **Numbers and dates on screen moved** (told to the owner): the shown rest is the longest rest in the window, not always the gap between two shifts; it is dated the day the work ends; it is rounded down.
- **A fully clocked day over 13 hours gets two findings,** the daily-rest one and the maximum-daily-hours one; merging them was declined.
- **Pay does not change.** Payroll never runs the rest rule (ADR-039 D5's rationale of record); PIN-1 stayed green at CI-W3b.
- **Exempt staff can get findings** until QUAL-255 lands, deliberately: the check errs toward reporting.
- **The weekly check's errors stay** until QUAL-256.

## Alternatives not chosen

- **The chain span as the value** ("first start to last end ≤ 13 h"): the same verdicts (V-75), but it needs unbounded history, where the look-back needs two days.
- **An exemption switch on the AC agreement:** the sources forbid exempting whole groups (PAV 19.8).
- **A structured derogation model now:** it rests on three unanswered questions.
- **Rebuilding the weekly check now:** it needs two expert answers and a longer read.
- **An interim "not checked" marker for days over 13 hours:** declined by the owner (Q1, 2026-11-13, "No, register only").
- **Merging the maximum-daily-hours and daily-rest findings.**
- **Owner Q2's other options:** (b) building it by reopening wave 3, which would have redone a reviewed stack; (c) scheduling it for the next sprint, which would have left QUAL-253 open at High and had wave 5 build the served minimum rest on a calculation it then replaced.
- **The zone read inside the rule, or the EU clock-change rule hard-coded:** the first is ambient state (ADR-002), the second a second copy of clock-change knowledge (`CopenhagenBusinessDate.cs` keeps one).
- **Half-up rounding of the shown value** (D2).

## References

- The dossier `docs/references/rest-period-24h-research.md`, and its sources `.claude/reviews/S148-RESEARCH-rest-24h-pass1.md`, `…-verification.md` and `…-adjudication.md`.
- The owner's rulings: `docs/sprints/SPRINT-148.md`, "The 11-hour rest rule: verified research and owner Q2" (`:1588-1625`), and the TASK-14813 Step 5a section (`:1511-1543`).
- Adjudications: `.claude/reviews/S148-TASK-14813-5a-adjudication-c1.md`, `S148-wave3b-briefs-adjudication.md`, `S148-WAVE3B-5a-adjudication-c1.md`, `S148-TASK-14814-5a-adjudication-c1.md` (the rounding it set, superseded by D2).
- Code: `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs`; `src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs`; `src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs`; `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`; `frontend/src/hooks/useSkema.ts`; `frontend/src/components/SkemaDayPanel.tsx`.
- Spec: `SYSTEM_TARGET.md:272-284`, `:300`, `:309`.
- ADRs: ADR-002 (pure engine), ADR-039 (midnight crossings), ADR-041 (business dates), ADR-043 D10, D11, D12.
- Registers: QUAL-253 (closed), QUAL-254, QUAL-255, QUAL-256, QUAL-275, QUAL-277; SEC-059.

## Revision history

- **2026-11-25 (S148 C-1).** Drafted by the Planning Drafter (Opus 5.5) from the three research files, the adjudications named in the header and the code at `bda18b03`; applied by the Orchestrator. It records the owner's rulings of 2026-11-13, the adjudications of 2026-11-12 … 2026-11-16 and the code merged as `40d3e694` and `2810ebeb`; it decides nothing new.
````

---

## Item 9 — the dossier (new)

### K-38 · NEW FILE `docs/references/rest-period-24h-research.md` (head in full, then two verbatim parts appended)
- Write LF. **The shape follows the ruling and the S147 precedent:** RR-8 orders "Part 1 is the verification; Part 2 is pass 1 verbatim with the 19 corrections marked. No anchor marker" (`.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md:113`; plan `:849`), and `docs/references/sick-day-credit-and-agreement-texts-research.md` is built the same way (head, "# Part 1 — The independent verification (governs)" at its `:34`, "# Part 2 — The first pass (verbatim; superseded where Part 1 corrects it)" at `:428`). The head below gives the plain-language summary, the verified verdicts with their sources, the corrections and the open questions, as the brief asks. **The corrections are marked by an index in the head, not inside Part 2,** so that Part 2 stays byte-for-byte pass 1 (the S147 dossier also marks none inside its Part 2; choice 2).
- **Inputs, checked before assembly** (`sha256sum` at drafting): verification `6916db7ff14b87e7b27b4cf3c9354fe911ff2aab150e0b3d22bc1c1233a29ae3` (287 lines, LF); pass 1 `e9fc02daee13a40611654c478db61767b7d21bd7c1a88062e816f2e82d86dad6` (422 lines, LF). Both are gitignored (`.gitignore:40`), which is why the dossier must carry them.

**Step 1.** Write the file with exactly this head (it ends with a newline after its last line):

````text
# The 11-hour daily rest and the weekly rest day — verified research (S148 research pass, R1–R5)

**Date:** 2026-11-13. **Method:** a research pass (`general-purpose`, Opus 5.5, web search and direct downloads of the primary texts, read on 2026-11-12), then an independent adversarial verification that re-downloaded and re-read every decisive source (Opus 5.5, 2026-11-13), then a Fable 5.2 adjudication of what it means for StatsTid (`.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md`, RR-1 … RR-8). **Status: verified claim by claim.** Where the verification corrects the first pass, **the verification governs** (Part 1); the first pass is kept verbatim in Part 2 as the record of what was claimed. What StatsTid decided on it is [ADR-044](../knowledge-base/decisions/ADR-044-rest-period-model.md).

## In plain words

**The question.** Danish law gives every employee "at least 11 consecutive hours of rest within each period of 24 hours" (arbejdsmiljøloven § 50; BEK nr. 606 af 29/06/2026 § 3). StatsTid's rest check had to know where that 24-hour period starts. Its old answer, to group work by the day it starts and measure only between days, missed real breaches, which both review lenses found in S148.

**The answer.**
- It is not the calendar day, and since 29 December 1996 it is not "24 hours from the start of the normal working day": Parliament removed that anchor on purpose.
- Arbejdstilsynet, which enforces the rule, gives one test, in the same words in 1997, 2016 and 2024: from a point inside working time, look back 24 hours; that stretch must hold 11 unbroken hours of rest.
- In practice, **no work may end more than 13 hours after the last full 11-hour rest ended.** So these are breaches: one shift over 13 hours; a day shift followed by a night shift; a split day over 13 hours from first start to last end (08–12 plus 17–22 breaches at 22:00 on that day). "13 hours on, 11 off" is lawful.
- The state agreement's § 12 lets the employer cut the rest to 8 hours once per 7 days (twice by local agreement, never two days in a row), with equal compensatory rest straight after. Going below 8 hours needs Arbejdstilsynet's dispensation or a wider agreement it has approved.
- Staff who set their own working time, and managers, can be exempt, assessed person by person, never for a whole group (BEK 606 § 6; PAV 19.8).
- The weekly rest day is 35 unbroken hours (24 plus the 11-hour daily rest), with no more than 6 døgn between two (10 working days under the state agreement), and on-call time never counts toward it, although on-call at home counts as daily rest.
- Five questions the sources leave silent go to a qualified expert; a sixth has a safe default.

**What StatsTid did with it (S148).** The owner chose to build the corrected daily check in S148 (owner Q2, 2026-11-13): wave 3b, merged on 2026-11-16 as `2810ebeb` and CI-verified (run `43851473962`). The rule is ADR-044. How often a cut is used and the compensatory rest (QUAL-254), the per-person exemption (QUAL-255) and the weekly rest day (QUAL-256) are registered, not built.

## The questions and the verified verdicts

| # | Question | Verified verdict | Decisive sources | What StatsTid did |
|---|---|---|---|---|
| **R1** | What is "each period of 24 hours" measured from? | **CONFIRMED.** Not the calendar day; not a start-of-day anchor since 29 Dec 1996; Arbejdstilsynet's look-back test. "Every point" and "clip to the window" are inferences, supported by elimination | Commission, OJ C 109/29 (2023); LOV 1196/1996 and the L 59 remarks; VEJ 9666/2024 § 2, MED 3503/1997, VEJ 9576/2016; KEN 9938/2006 | ADR-044 D1: every work end judged on the 24 elapsed hours before it; QUAL-253 fixed |
| **R2** | Split working days | **PARTLY**, sharper than the first pass: lawful if and only if the chain spans 13 hours or less; gaps under 11 hours never reset the chain and never add up (except under home-work derogations); the example breaches on its own day, at 22:00 | Directive Art. 2(2), Art. 17(4)(b); BEK 606 § 33 stk. 4, Bilag 1 nr. 6 | ADR-044 D1: a split day is judged like any other day |
| **R3** | A continuous over-long shift | **CONFIRMED.** A shift over 13 hours is a breach in itself | Commission, OJ C 109/29; KEN 9938/2006; KEN 9298/2025 (a standing 24-hour arrangement needs the organisations' agreement) | ADR-044 D1; the maximum-daily-hours check stays as a backstop |
| **R4** | The weekly rest day | **PARTLY.** 35 continuous hours; at most 6 døgn between two (Arbejdstilsynet), 10 working days under the state agreement § 12 stk. 3; the 7-døgn anchor is defined nowhere; on-call never counts toward it | LBK 442 § 51; BEK 606 § 4; VEJ 9667/2024 § 2; VEJ 9638 § 4; CJEU C-477/21, C-306/16 | Registered, not built: QUAL-256 |
| **R5** | Derogations for the state | **CONFIRMED with corrections.** § 12 allows a cut only, to 8 hours, once per 7 døgn (twice by local agreement, not two in a row), with compensatory rest equal to the cut before the next work; CIR 9807/1983 (central administration, signed by AC) cuts or postpones and is listed as valid, but whether it still works under BEK 606 is unverified; the AC agreement has no rest clause; some staff are exempt under BEK 606 § 6 | Statens arbejdstidsaftale § 12 (CIR1H 9594 = Medst. 022-26; identical in CIR1H 9551); BEK 606 §§ 5–34; CJEU C-151/02 (Jaeger) para. 97; PAV 19.7–19.8 | ADR-044 D5: the 8-hour floor; QUAL-254 (frequency, compensatory rest, postponement); QUAL-255 (§ 6) |

## Corrections the verification made to the first pass

Numbered as in Part 1, § 3, where each is given in full. Part 2 is kept byte for byte; this index marks where each correction applies in it.

| Corr. | In short | Where it applies in Part 2 |
|---|---|---|
| 1 | Only KEN 9938/2006 calls a 24-hour shift a "significant exceedance"; KEN 9298/2025 refused a general dispensation and grades nothing | the summary's bullet "The two missed cases are breaches"; the verdicts table, row R3 (§ R3-2's own text is accurate) |
| 2 | The 1996 change took effect on 29 Dec 1996; November 1997 is the date of Arbejdstilsynet's notice | the summary ("What applies since November 1997"); the heading of § R1-4 ("1997 to now") |
| 3 | The Arbejdstilsynet notices do not cite BEK 324/2002 or LBK 2062/2021; PAV does | "Premise corrections and currency notes", item 3 |
| 4 | The directive-implementation agreement is Bilag 1 of 022-26, not bilag 2 | § R2-1; "What the algorithm must do", item 2; the sources table, row 11 |
| 5 | A source does give the amount of compensatory rest: Jaeger para. 97 (equal to the cut, before the next work) | § R5-4, "How much compensatory rest"; "Searched and NOT FOUND" |
| 6 | CIR 9807's `EndDate 2026-06-24` is a record date, not an expiry; whether the circular still works under BEK 606 is an expert question | § R5-3, "Status" |
| 7 | "8 h instead of 11, 16 h instead of 13" models a cut, not a postponement; CIR 9807 also allows postponement (22 hours of work in a row) | "What the algorithm must do", item 6 |
| 8 | "Rolling" per 7 døgn is an inference; "not 2 døgn in a row" is undefined | "What the algorithm must do", item 6; the verdicts table, row R5 |
| 9 | The split-day example breaches on day 1 at 22:00, without the next morning | the verdicts table, row R2; § R2-4 |
| 10 | "Below 8 hours always needs a dispensation" is the board's shorthand; the routes are BEK 606 §§ 34, 21, 5 and 7 | the summary's last bullet; the verdicts table, row R5; "What the algorithm must do", item 6 |
| 11 | Agreements under BEK 606 §§ 19–22 are not limited to Bilag 1 activities | § R2-2 |
| 12 | Missed: the BEK 606 § 6 exemption, assessed person by person | the verdicts table, row R5; § R5-2, "Who is covered" |
| 13 | Missed: on-call never counts toward the weekly rest day | § R4; "Notes for the design", the weekly check |
| 14 | Missed: AC staff in central administration reach CIR 9807's derogation route | § R5-2, "Who is covered"; § R5-3 |
| 15 | Pinpoints and quotations: page numbers; an omitted "(see Chapter IX)"; the KEN 9938 quote joins two sentences | § R2-1, § R2-2, § R3-2, § R4-5; the sources table |
| 16 | PAV shows "Sidst redigeret 03.12.24" and lags OK26 and BEK 606; PAV kap. 19 does not list CIS 6240/1984 | § R5-3, the last paragraph; the sources table, row 14 |
| 17 | The 1996 bill's "successive 7-day periods" sentence comes from the remark on § 51 stk. 3 (postponement), not a general anchor | § R4-3 |
| 18 | Further corroboration: VEJ 9576/2016 (offshore) states the same test; CJEU C-585/19 (several contracts with one employer) | an addition; no Part 2 passage |
| 19 | 022-26's repeal clause misdates the 2021 circular (5 June, not 5 July); immaterial | context for § R5-2, "Version stability" (not a first-pass error) |

## Open questions (the sources are silent)

| # | Question | Needed for S148's build? | Disposition (RR-6) |
|---|---|---|---|
| (f) | What to assume before the first record in the data | Yes | Defaulted: unregistered time is rest (ADR-044 D6) |
| (a) | The 7-døgn anchor, and whether it rolls | No | Expert; blocks QUAL-254 and QUAL-256 |
| (b) | What "døgn" means in "not 2 døgn in a row" (and in VEJ 9638's "det normale arbejdsdøgn") | No | Expert; blocks QUAL-254 |
| (c) | Whether CIR 9807/1983 still works under BEK 606, and how its "arbejdsdøgn" maps onto the look-back | No | Expert; StatsTid models no postponement |
| (d) | Whether compensatory hours come on top of the next 11-hour rest or inside it | No | Expert; if built before an answer, "on top", the stricter reading |
| (e) | How a night shift counts toward § 12 stk. 3's "arbejdsdage" | No | Expert; blocks QUAL-256 |

Questions (a)–(e) belong in the expert hand-off package (`docs/references/phase-b-handoff-package.md`).

## Sources and how this file is built

Every decisive source was re-fetched on 2026-11-13; Part 1, § 5 lists each with its route and status, and Part 2 ends with the first pass's own list, with URLs. Part 1 is `.claude/reviews/S148-RESEARCH-rest-24h-verification.md` from its line 3, and Part 2 is `.claude/reviews/S148-RESEARCH-rest-24h-pass1.md` from its line 3, both verbatim; their own title lines are replaced by the part headings. No anchor marker (RR-8).
````

**Step 2.** Append the two parts verbatim (Git Bash, from the repo root; one command, no heredoc, FAIL-008):

````text
cd /c/Users/b200895/source/repos/StatsTid && node -e "const fs=require('fs');const L=(f,a,b)=>fs.readFileSync(f,'utf8').split('\n').slice(a-1,b).join('\n')+'\n';const v='.claude/reviews/S148-RESEARCH-rest-24h-verification.md',p='.claude/reviews/S148-RESEARCH-rest-24h-pass1.md';fs.appendFileSync('docs/references/rest-period-24h-research.md','\n---\n\n# Part 1 — The independent verification (governs)\n\n'+L(v,3,287)+'\n---\n\n# Part 2 — The first pass (verbatim; superseded where Part 1 corrects it)\n\n*The first pass keeps its own headings, among them its heading Part 2 (Claims, quotes and citations), an internal division of the first pass and not Part 2 of this file. Its corrections are indexed in the head of this file; the text below is the first pass byte for byte.*\n\n'+L(p,3,422));"
````

**Step 3, the checks.** `wc -l` of the result = (the head's line count) + 717 (5 separator lines, 285 lines of Part 1, 7 separator lines, 420 lines of Part 2). `grep -c "^# Part 1 — The independent verification (governs)$"` and `grep -c "^# Part 2 — The first pass"` each print 1. `grep -c "anchor-sprint"` prints 0.

---

## Item 10 — CLAUDE.md and the KB INDEX

### K-39 · `CLAUDE.md` · the "Historical & research dossiers" row (RR-8's text, verbatim)
- LF. Inserted after the tender-analysis row (`:69`), so the verified-research rows stay in date order. The description is RR-8's text word for word (`.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md:114`; plan `:850`).

old_str:
````text
| [docs/references/tender-requirements-analysis.md](docs/references/tender-requirements-analysis.md) | 2026-10-26 verified overview (not a plan): two historical state tenders for time-and-absence systems (Erhvervsministeriet 2023, Vejdirektoratet 2025) measured requirement by requirement against StatsTid, 215 rows rated Met / Partly / Not met / Outside software; the eight product defects it surfaced are in the QUAL and SEC registers |
````

new_str:
````text
| [docs/references/tender-requirements-analysis.md](docs/references/tender-requirements-analysis.md) | 2026-10-26 verified overview (not a plan): two historical state tenders for time-and-absence systems (Erhvervsministeriet 2023, Vejdirektoratet 2025) measured requirement by requirement against StatsTid, 215 rows rated Met / Partly / Not met / Outside software; the eight product defects it surfaced are in the QUAL and SEC registers |
| [docs/references/rest-period-24h-research.md](docs/references/rest-period-24h-research.md) | S148 verified research: the 11-hour daily rest is judged on the 24 hours before each work end (not the calendar day, not the start of the working day since 29 Dec 1996), so no work may end more than 13 hours after the last full rest; the state agreement's § 12 cut to 8 hours (once per 7 days, compensatory rest owed); the per-person § 6 exemption; the weekly rest day is 35 unbroken hours and on-call never counts toward it; five questions the sources leave silent. |
````

### K-40 · `docs/knowledge-base/INDEX.md` · the ADR-044 row
- LF. Inserted after the ADR-043 row (`:51`), anchored on that row's unique tail. **How the CI check reads INDEX** (`tools/check_docs.py:63-80`): it collects every `](decisions/…md)` link with the regex `\]\((decisions|patterns|dependencies|resolutions|failures)/([^)\s]+\.md)\)` and compares the set with the files on disk; an ADR-044 file without this link fails as an orphan, and this link without the file fails as dangling, so K-37 and K-40 land in the same commit. The Tag Index and Domain Index sections list no ADR after ADR-017 (ADR-039 … ADR-043 are absent), and the check does not read them, so no row is added there (choice 4).

old_str:
````text
| S146 (draft) → S147 (rulings, acceptance) → S148+ (build) | Rule Engine, Payroll Integration, Backend, Data Model, SharedKernel, Frontend, Security, Test | overtime, merarbejde, flex, norm-period, monthly-norm, public-holidays, supplements, compensation, ordered-work, forfeiture, office-hours, customary-days, clock-times, worked-time-classes, local-configuration, staged-build |
````

new_str:
````text
| S146 (draft) → S147 (rulings, acceptance) → S148+ (build) | Rule Engine, Payroll Integration, Backend, Data Model, SharedKernel, Frontend, Security, Test | overtime, merarbejde, flex, norm-period, monthly-norm, public-holidays, supplements, compensation, ordered-work, forfeiture, office-hours, customary-days, clock-times, worked-time-classes, local-configuration, staged-build |
| [ADR-044](decisions/ADR-044-rest-period-model.md) | **The rest model: every work end is judged on the 24 hours before it.** The 11-hour daily rest (arbejdsmiljøloven § 50; BEK 606 § 3) is measured with Arbejdstilsynet's test: at the end of each piece of work, look back 24 elapsed hours in Danish time; the longest unbroken rest there must reach the minimum, so no work may end more than 13 hours after the last full rest. Replaces the S132 model, which measured only between start-day groups and missed a shift over 13 hours, a day shift plus a night shift, and a long split day (QUAL-253). D1 the rule (stint ends judged; the longest single rest, clipped to the window, never summed; the 13 hours derived; one finding per day; the minimum of the end's own date); D2 the true value decides, the shown value is rounded down; D3 dated the day the work ends, read from `monthStart − 2`, the engine cuts at the month's last midnight; D4 elapsed hours in Europe/Copenhagen with the zone passed in by the host (ADR-002), clock-change defaults that err toward reporting, 23:59 read as midnight, instants never stored (ADR-041); D5 the derogation flag covers only rests of 8 h or more (BEK 606 § 19; Statens arbejdstidsaftale § 12); D6 unregistered time is rest, the not-checked list, voluntary stints and the read-edge rule; D7 a manager sees nothing of an unsent neighbouring month (SEC-059, RES-002); D8 one shared 33-row table proves server and screen agree. Open: QUAL-254 (how often a cut is used, compensatory rest, postponement), QUAL-255 (the per-person § 6 exemption), QUAL-256 (the weekly rest day), QUAL-277, five silent questions for an expert. Supersedes ADR-039 D5/D5b for the daily rest check; amends ADR-043 D10, D11. Dossier: `docs/references/rest-period-24h-research.md` | accepted — owner-ruled 2026-11-13 (S148 Q2); built S148 wave 3b | S148 | Rule Engine, Backend, Frontend, Security, Test | rest-period, daily-rest, 11-hour-rule, 24-hour-look-back, derogation, weekly-rest, elapsed-time, time-zone, dst, compliance, manager-visibility |
````

---

## Item 11 — DEP-004 and ADR-034

### K-41 · `docs/knowledge-base/dependencies/DEP-004-endpoint-registry-ui-api-data-alignment.md` · the retired `POST /api/time-entries` row (the log's "Docs owed", `SPRINT-148.md:393`)
- CRLF. At HEAD `:107`. Verified: no `MapPost` for the route in `src` (`TimeEndpoints.cs` maps only the two GETs, `:15`, `:73`); `frontend/src/hooks/useTimeEntries.ts:5-7` ("There is no register call: POST /api/time-entries is retired"). Style copied from the struck `flex-balance` row (`:111`).

old_str:
````text
| `/api/time-entries` | POST | — | useTimeEntries (legacy) | TimeEntry | TimeEntryRegistered | Employee+ | Superseded by Skema |
````

new_str:
````text
| ~~`/api/time-entries`~~ | POST | — | — (`useTimeEntries` keeps only its GET) | — | — | — | **Retired in S148 TASK-14802** (merged `e4e6b0ea`): it stored any activity type as free text and let paid hours bypass the send check; the Skema save is the one way to register time (ADR-028 D6, ADR-043 D11) |
````

### K-42 · DEP-004 · the Skema note's "intervals + manualHours"
- CRLF. At HEAD `:35`. Beyond the log's literal "DEP-004's row", but S148 made it false (manual hours retired, TASK-14809; the `cleared` block added, TASK-14840; `SkemaEndpoints.cs:3697-3704`). Drop it if you keep this batch to the owed row only (choice 5).

old_str:
````text
GET now returns `workTime` (intervals + manualHours)
````

new_str:
````text
GET now returns `workTime` (intervals only; manual hours retired in S148, TASK-14809; since S148 POST `/save` also takes a `cleared` block, TASK-14840)
````

### K-43 · `docs/knowledge-base/decisions/ADR-034-payroll-export-lock.md` · a re-verification line at the head of the 2026-11-04 amendment
- CRLF. At HEAD `:151`. Every citation into the payroll host in that amendment (`:151-251`) had moved; K-44 … K-54 fix each. The 2026-11-23 D4 note's citations (`:43-137`) were checked and are current (spot-checked `PayrollExportService.cs:300`, `:289-347`, `:297-299`; `Program.cs:1179-1191`, `:544`, `:794`, `:913-919`, `:553`, `:803`, `:452`, `:832`).

old_str:
````text
## Amendment 2026-11-04 — S147: every new export comes from its own approved month (SEC-054; owner rulings OQ-3 = (a) + (i) and R-G9)
````

new_str:
````text
## Amendment 2026-11-04 — S147: every new export comes from its own approved month (SEC-054; owner rulings OQ-3 = (a) + (i) and R-G9)

*Citations into the payroll host in this section re-verified 2026-11-25 at `bda18b03`: S148's TASK-14801 and TASK-14831 had moved them.*
````

### K-44 · ADR-034 · `:165` the retired raw routes
- CRLF. `Program.cs:221-226` is now middleware; the retirement remark is `:236-241`.

old_str:
````text
(`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:221-226`; `RawExportRoutesRetiredTests`)
````

new_str:
````text
(`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:236-241`; `RawExportRoutesRetiredTests`)
````

### K-45 · ADR-034 · `:172` the no-period refusal
- CRLF. The refusal is `PayrollExportService.cs:236-239` (`if (context.PeriodId is not { } periodId …) throw … NoPeriodMessage`); the message `:31-35` is unchanged.

old_str:
````text
opens (`…/Services/PayrollExportService.cs:228-231`; the message `:31-35`).
````

new_str:
````text
opens (`…/Services/PayrollExportService.cs:236-239`; the message `:31-35`).
````

### K-46 · ADR-034 · `:174` the B2 re-assert
- CRLF. The re-assert's remark and call are `PayrollExportService.cs:343-347`; the call itself `:347`.

old_str:
````text
the export↔reopen race named under Consequences; `:300-304`) was skipped for them. It now runs on every export made since TASK-14716 (`:304`),
````

new_str:
````text
the export↔reopen race named under Consequences; `:343-347`) was skipped for them. It now runs on every export made since TASK-14716 (`:347`),
````

### K-47 · ADR-034 · `:178` the own-month check
- CRLF. The check inside `AssertPeriodApprovedForUpdateAsync` is `PayrollExportService.cs:572-586`.

old_str:
````text
`:524-538`). A period of another employee, of another month, or of only part of the month (a legacy `WEEKLY`
````

new_str:
````text
`:572-586`). A period of another employee, of another month, or of only part of the month (a legacy `WEEKLY`
````

### K-48 · ADR-034 · `:182` the whole-month check and the approval read
- CRLF. `Program.cs:313-314` (`RefusePeriodNotAMonth`) and `:327-328` (`GetByEmployeeAndPeriodAsync` with the request's employee and dates).

old_str:
````text
approval by the request's own employee and dates (`Program.cs:296`, `:310-311`); the check guards any other caller.
````

new_str:
````text
approval by the request's own employee and dates (`Program.cs:313-314`, `:327-328`); the check guards any other caller.
````

### K-49 · ADR-034 · `:189` one month per export
- CRLF. `PayrollExportService.cs:212-227`.

old_str:
````text
is refused before any connection opens (`PayrollExportService.cs:204-219`;
````

new_str:
````text
is refused before any connection opens (`PayrollExportService.cs:212-227`;
````

### K-50 · ADR-034 · `:190` the month-spanning line
- CRLF. `PayrollExportService.cs:199-210`.

old_str:
````text
A line that spans two calendar months is still refused (`:193-202`).
````

new_str:
````text
A line that spans two calendar months is still refused (`:199-210`).
````

### K-51 · ADR-034 · `:197` only the write is skipped
- CRLF. The record-less branch is `PayrollExportService.cs:349-361`.

old_str:
````text
(`PayrollExportService.cs:316`; R-F1,
````

new_str:
````text
(`PayrollExportService.cs:349-361`; R-F1,
````

### K-52 · ADR-034 · `:203` the "flex not tracked" note
- CRLF. `Program.cs:618-619` (`OkWithFlexNote`).

old_str:
````text
and the response's note "flex not tracked" (`Program.cs:528`).
````

new_str:
````text
and the response's note "flex not tracked" (`Program.cs:618-619`).
````

### K-53 · ADR-034 · `:231` null balances without a flex-ledger write
- CRLF. `PayrollExportService.cs:366-369` (the `balances` remark and expression).

old_str:
````text
test calls reach (`PayrollExportService.cs:325-330`).
````

new_str:
````text
test calls reach (`PayrollExportService.cs:366-369`).
````

### K-54 · ADR-034 · `:233` the conflict message copied into the 409
- CRLF. `Program.cs:577-595` (`catch (PayrollExportConflictException ex)` … `error = ex.Message`).

old_str:
````text
(`Program.cs:491-503`). The period-identity message names the approval's employee and dates.
````

new_str:
````text
(`Program.cs:577-595`). The period-identity message names the approval's employee and dates.
````

---

## Optional blocks, beyond the brief's list

### K-55 · `docs/SECURITY.md` · `:194`, the same stale payroll citations as ADR-034 (optional)
- LF. The same two citations ADR-034 had (K-45, K-47 area). The cites at `:190` are pinned "at `94bd32be`" and stay.

old_str:
````text
`ExportAsync` now refuses an export that names no approval period (`PayrollExportService.cs:228-231`), so rule 2's payroll checkpoint runs on every export made since TASK-14716, on the exported employee's own whole-month approval (`:524-553`)
````

new_str:
````text
`ExportAsync` now refuses an export that names no approval period (`PayrollExportService.cs:236-239`), so rule 2's payroll checkpoint runs on every export made since TASK-14716, on the exported employee's own whole-month approval (`:572-595`)
````

### K-56 · `docs/knowledge-base/INDEX.md` · the ADR-039 row's status (optional; follows K-31)
- LF. The cell occurs once (`grep -c -F` = 1).

old_str:
````text
| accepted — owner-ratified 2026-08-20 | S132 |
````

new_str:
````text
| accepted — owner-ratified 2026-08-20; D5/D5b superseded for the daily rest check by ADR-044 (S148) | S132 |
````

### K-57 · `docs/knowledge-base/INDEX.md` · the ADR-040 row's org-history phrase (optional; follows K-9)
- LF.

old_str:
````text
org/unit history model decided per ADR-027 precedent, implementation a named follow-up
````

new_str:
````text
org/unit history model decided per ADR-027 precedent; the organisation's history built in S148 (C-13, `user_organisations`), the unit's still a named follow-up
````

### K-58 · `docs/references/sick-day-credit-and-agreement-texts-research.md` · `:646`, RR-8's pointer to the new dossier (optional)
- LF. RR-8 asks for it (`.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md:117`); the brief's list does not. The paragraph sits in that dossier's Part 2 (its verbatim first pass), so the pointer is a separate, dated line after it rather than an edit inside it.

old_str:
````text
**A separate question.** For the EU working-time limits (the 48-hour average and the 11-hour rest rule) a different definition applies, and PAV treats on-call at home as rest for the 11-hour rule unless the on-call is so demanding that the rest is lost. StatsTid's rest check therefore needs its own classification, not § 7's fractions.
````

new_str:
````text
**A separate question.** For the EU working-time limits (the 48-hour average and the 11-hour rest rule) a different definition applies, and PAV treats on-call at home as rest for the 11-hour rule unless the on-call is so demanding that the rest is lost. StatsTid's rest check therefore needs its own classification, not § 7's fractions.

*S148 pointer (2026-11-25):* the rest rule's own definition is now verified in `docs/references/rest-period-24h-research.md` (the 24 hours before each work end) and decided in ADR-044; on-call never counts toward the weekly rest day (that dossier's correction 13).
````

---

## Item 12 — QUAL-255's cross-reference (a suggestion for the registers batch, not a block)

In `docs/operations/quality-finding-register.md:512` (QUAL-255), replace "is recorded in ADR-044, owed at S148 close (wave-3b brief adjudication, item 16)" with "is recorded in ADR-044 D6 (`docs/knowledge-base/decisions/ADR-044-rest-period-model.md`; wave-3b brief adjudication, item 16)". The same row family has one more stale phrase once the dossier exists: QUAL-253 (`:510`) says "dossier owed at close as `docs/references/rest-period-24h-research.md`", which becomes "dossier `docs/references/rest-period-24h-research.md`".

---

## Brief contradictions

1. **"The four readers of `user_organisations`"** (plan O-22, `PLAN-s148.md:804`, from "final + 25"; the brief repeats it). · At `bda18b03` there are five read sites: the four named, plus `DailyNormCalculator.cs:730-736` (`DatedRecordTimeline`, added by TASK-14828 in the T14b stack, `f68ed12a`, merged `b5f167aa`), called from `ApprovalEndpoints.cs:1378`, `BalanceEndpoints.cs:178` and `ComplianceEndpoints.cs:411`. The seeder's existence check is then a write-side sixth, not fifth. · K-9 lists five and the seeder as the sixth. The plan's list was written before TASK-14828 (wave 5), so this is drift, not an error at the time.
2. **ADR-014's "two legacy callers"** (plan O-22, R-14). · Three: `OvertimeEndpoints.cs:122-123`, `TaskDispatcher.cs:85-86`, `WeeklyCalculationPipeline.cs:93-94` (QUAL-262). · The brief already says "now QUAL-262's three"; K-4 names three and says why R-14 said two.
3. **"owner Q1, 2026-11-11"** for the balance seed (addendum O-29; brief item 5 by reference). · The sprint log records it as "Owner Q3 (2026-11-11)" (`SPRINT-148.md:673`), and QUAL-196's row says "owner Q3 and Q4"; the addendum numbers its own questions Q1 … Q4. · K-30 says "owner Q3 and Q4, 2026-11-11, which the addendum numbers Q1 and Q4". Same ruling, two numbering schemes.
4. **The dossier's shape** (brief item 9: "plain-language summary first, then the verified findings with their sources, then the open questions"). · RR-8 and the plan (`PLAN-s148.md:849`) order "Part 1 the verification; Part 2 pass 1 verbatim with its 19 corrections marked", and the S147 dossier the brief names as the model is built that way. · K-38 does both: the head carries the summary, the verdicts with sources, the corrections index and the open questions, then Part 1 and Part 2 follow verbatim. Not a conflict in substance; reported so the shape is a known choice.
5. **ADR-034's stale citations "into the payroll host" (log `:358`, `:404`).** · Those log lines are now `SPRINT-148.md:420` and `:462`, and they name the citations without listing them. All eleven stale ones are in the S147 amendment; the 2026-11-23 D4 note's are current. · K-43 … K-54.
6. **Audit report § 4: "ADR-039 and ADR-043 D10 notes … a rest belongs to the month in which it ends"** (from the wave-3 brief adjudication, `:166`). · Wave 3b superseded that dating (`PLAN-s148.md:856`; ADR-044 D3): a finding is dated the day the work ends. · K-14 and K-33 record the superseded wording as superseded rather than as the rule.

## Needs the Orchestrator's choice

1. **The dossier, assembled or inline (K-38).** Recommended: write the head with the Write tool and run the one node command, which copies the 707 verbatim lines exactly. The alternative, the full 110 KB text inline in a Write call, invites transcription drift in legal quotations. If you want it inline, I can produce a second draft file with the complete text.
2. **"Corrections marked" in Part 2.** RR-8 asks for the 19 corrections "marked". K-38 marks them by an index in the head and keeps Part 2 byte-for-byte, as the S147 dossier does. If you want inline markers in Part 2 instead, that is 19 small insertions into quoted text, and Part 2 is then no longer verbatim.
3. **ADR-044's file name and status label.** Proposed `ADR-044-rest-period-model.md` and "accepted — owner-ruled 2026-11-13 (S148 Q2)". The rule was owner-ruled and is built and CI-verified; if you prefer "accepted — implemented S148" or another slug, change K-37's name and the links in K-14, K-31, K-33, K-35, K-40 and K-58 with it.
4. **INDEX's Tag and Domain indexes.** No ADR-044 row is added there, because they stop at ADR-017 and the CI check does not read them. Adding one would be the first since S21.
5. **The beyond-the-list blocks.** K-42 (DEP-004's manual-hours note), K-55 (SECURITY.md's two stale payroll cites), K-56 and K-57 (INDEX status cells for ADR-039 and ADR-040) and K-58 (RR-8's pointer). Each is S148-made drift; each can be dropped without touching the others.
6. **A stale citation in code, not mine to draft:** `PayrollExportService.cs:576-577` still says "`/calculate-and-export` refuses any other period before reading an approval, Program.cs:296"; the check is at `Program.cs:313`. A one-line comment fix for a third sweep, or leave it.
7. **Owed at close, outside this brief and not drafted here** (the sprint log's "Docs owed" lists and RR-8): `docs/references/danish-agreements.md`'s rest section; the five silent questions in `docs/references/phase-b-handoff-package.md`; the `docs/FRONTEND.md` lines (`:115`, `:185`, `:217`, `:292`, and the live-region line); the legacy-database runbook line for stored `timer` rows. Say if any of these should come to this drafter.
8. **RES-002's stale allowlist** (`:101-103` still lists REJECTED among the statuses a leader may see). K-36 records the fact inside the S148 note; correcting the Consequences bullet itself is a separate edit if you want it.

---

## Revision history

- **Draft 1 (2026-11-25, S148 C-1).** Drafted by the Planning Drafter (Opus 5.5) at master `bda18b03`. HEAD moved three times while drafting (`0d120ee9` → `bfbf22a7` → `bda18b03` → `ec789343`: the register batch, the second comment sweep, the C2-R4 census). Every `src/` cite was re-opened at `bda18b03`, where the sweep's one added line moved `SkemaEndpoints.cs` from `:343` on; `ec789343` changed only the two registers and the sprint log, so the cites stand there too. **Checks run:** a node script parsed this file's 56 replacement blocks and counted each `old_str` in its target (CRLF folded to LF): 56 found exactly once, at `ec789343`. K-38's assembly was dry-run into the scratchpad: the two appended parts compared byte-identical (`diff`) with the verification's lines 3–287 and pass 1's lines 3–422, the line arithmetic held (head + 717), and each part heading occurs once. Four ranges were corrected after re-opening the code (`UserOrganisationRepository.cs:43-102`, `RestPeriodRule.cs:486-538`, `:623-654`, and the transfer's hand-off cite `AdminEndpoints.cs:2607-2620` added to K-9).
