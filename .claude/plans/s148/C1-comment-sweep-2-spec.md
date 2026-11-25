# S148 close, plan step C-1: the second comment-only sweep, exact specification

| | |
|---|---|
| **Sprint** | S148 (close, plan step C-1, second sweep) |
| **Drafted by** | Planning Drafter (Opus); the Orchestrator rules, the `sweep` agent applies |
| **Base commit** | `989aea81` (master; the first sweep merged as `9a1b6e8c`, GATE green). Every line number below is at `989aea81`, and the files named here have no working-tree changes |
| **Sources** | `.claude/plans/s148/S148-C1-register-draft.md`, "Below the registration floor", item 24 (and item 1, which item 24 points to); the register batch ruling (2) for QUAL-BF-011 (`docs/sprints/SPRINT-148.md:4704`, `:4713`); `.claude/reviews/S148-TASK-14851-report.md:249-250` (the builder's F3, F4); `.claude/reviews/S148-TASK-14838-5a-reviewer-c5.md` NOTE 4 |

## What this is, in plain words

The first sweep made 48 comments true after S148 retired the time-entry POST and the manual hours (PAT-032: a comment states what is true now, and why). While drafting the register batch, the drafter found five more comments that were already false before S148 closed. Three name a repository method that no longer exists. One cites five line numbers that have all moved. One says a page does not re-read its list on a failed save, when sometimes it does. One says only two tables are rebuilt, when three are. One says "nothing has been written" in a case where the month's calculation proof has been written. This sweep corrects them.

It also adds one comment that is new rather than corrected. QUAL-BF-011 records an accepted limit, registered by the register batch: the Skema month view decides once, at the month's last day, whether to offer the "Særlig feriedag" row. So in the month a særlige-feriedage configuration ends, the row is not offered, even on the days the configuration still covers. PAT-032 asks that such a limit carry a `shortcut(QUAL-BF-011):` comment at the spot in the code, so the next person to change that line sees the limit and the register row it points to. `tools/check_docs.py` checks the pairing in CI.

Nothing that runs changes. Where a fix would change behaviour (a response sentence) or an identifier (a variable name), it is excluded and the reason is given.

## Counts

| Status | Count | Items |
|---|---|---|
| Still stale (apply) | **8 remarks** | item 1 (three names: S2-1, S2-2, S2-3); item 2 (S2-4); item 3 (two comments: S2-5, S2-6); item 4 (S2-7); item 5's class doc (S2-8) |
| Shortcut comment to add | **1** | item 6 (S2-9) |
| Already fixed | 0 | |
| Gone | 0 | |
| Excluded: behaviour or identifier | **2** | `PriorMonthNotApprovedException.cs:35-39` (a response string); the rename of `hasRows` |
| Excluded: needs regeneration | **1** | `SqlEmitter.cs:202/230` (carried from the first sweep) |
| Found on verification, not on the list (needs a ruling) | **3 places** | more mentions of the non-existent `InsertLiveRowAsync` (§ D) |
| New sweep items since the first spec (the re-run grep) | **0** | § E |

Total blocks to apply: **9** (S2-1 to S2-9), in **8 files**.

## How to apply (for the sweep agent)

- Apply the blocks in order, one `Edit` per block: `old_str` is the text between its fences, exclusive; `new_str` likewise. Indentation and trailing text are exact.
- Each `old_str` occurs exactly once in its file at `989aea81`, and still exactly once after the earlier blocks in the same file are applied (S2-1 and S2-4 share a file). If a count is not 1, stop and report; do not widen or guess.
- **Line endings:** all eight files are CRLF in the working tree (`git ls-files --eol`: `i/lf w/crlf`), none with a byte-order mark. The blocks are written with plain line breaks; the `Edit` tool matches across CRLF and keeps the file's endings. If you apply by script, convert `\n` to `\r\n` first.
- **S2-9 is an insertion:** its `old_str` is two existing lines and its `new_str` is the same two lines with the shortcut comment between them. The keyword must stay exactly `shortcut(QUAL-BF-011):`, lowercase, with no space before the parenthesis.
- Change nothing else: no reflow of neighbouring lines, no rename, no other edit. In particular, do not rename `hasRows` (S2-6).

**Verification method.** As for the first spec: a script (scratchpad `c1sweep/check2.js`) took each `old_str` from the cited lines at `989aea81` and confirmed its anchor text. It counted occurrences (1 for each), applied the blocks in sequence and re-counted (1 for each), and confirmed every changed line is a comment line. S2-9 is the exception, checked by hand: it inserts a comment line and leaves the statement after it byte-identical. S2-9's comment was also run through the two regexes of `tools/check_docs.py` and the register's QUAL-BF-011 location cell, with a match.

## Blocks to apply

### S2-1 · `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` · `CreateAsync` summary, "It is deliberately NOT the only INSERT"

status: still stale
lines at `989aea81`: `:591` · line endings: CRLF
source: item 1 (register draft below-floor 24; the builder's F3, `S148-TASK-14851-report.md:249`)
why: No `InsertLiveRowAsync` exists in this class (`git grep -n InsertLiveRow -- src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` finds only this comment). Cases A and T (`TemporalWriteCase.Create` / `InsertTrailing`) run `ExecuteOpenInsertAsync` (`EmployeeProfileRepository.cs:1044-1045`, `:1420-1441`), which inserts through `InsertRowAsync` (`:1432`, `:1533`).
hazards: none: an XML doc comment; no `cref` added (the name is in `<c>`); no project generates XML docs.

old_str (exact, unique in the file):
```text
    /// (and Case T) also produces a net-new live row, via <c>InsertLiveRowAsync</c>, for a
```

new_str:
```text
    /// (and Case T) also produces a net-new live row, via <c>ExecuteOpenInsertAsync</c>, for a
```

### S2-2 · `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileSeeder.cs` · "S33 Step 7a P1 absorption: backfill MUST use the '0001-01-01' anchor"

status: still stale
lines at `989aea81`: `:145-149` · line endings: CRLF
source: item 1 (`EmployeeProfileSeeder.cs:147`)
why: Names the non-existent `EmployeeProfileRepository.InsertLiveRowAsync`. A same-day PUT on a year-1 open row is the dated writer's Case C' (`TemporalWriteCase.SplitCovering` → `ExecuteSplitAsync`, `EmployeeProfileRepository.cs:1048-1049`), which stamps the successor `anchor.Version + 1` when the covering row is open (`:1483-1484`). "The ETag monotonicity contract" no longer rests on that row version: the client's token has been `users.version` since S141 (`:90`, `:770-771`), bumped on every write (`:1063`), so the sentence now says what the row version does and where the ETag comes from.
hazards: none: `//` comment inside a method body.

old_str (exact, unique in the file):
```text
                // Same-day PUT against a seeder-backfilled row routes to Case C
                // (effective_from='0001-01-01' < today), but the Step 7a P1 fix in
                // EmployeeProfileRepository.InsertLiveRowAsync now stamps
                // supersedingVersion = predecessor.Version + 1, so the ETag monotonicity
                // contract holds across the supersession.
```

new_str:
```text
                // Same-day PUT against a seeder-backfilled row routes to Case C'
                // (effective_from='0001-01-01' < today), and the Step 7a P1 rule, now in
                // EmployeeProfileRepository.ExecuteSplitAsync, stamps the successor
                // predecessor.Version + 1, so the row version keeps climbing across the
                // supersession (the client's ETag has been users.version since S141).
```

### S2-3 · `src/Infrastructure/StatsTid.Infrastructure/EmployeeEntitlementEligibilityRepository.cs` · `SupersedeAndCreateAsync` summary (the eligibility writer), "Case C — Superseded"

status: still stale
lines at `989aea81`: `:190` · line endings: CRLF
source: item 1 (`EmployeeEntitlementEligibilityRepository.cs:190`)
why: Names the non-existent `EmployeeProfileRepository.InsertLiveRowAsync`; the profile writer's "predecessor version + 1 across a supersession" rule lives in its Case C' path, `ExecuteSplitAsync` (`EmployeeProfileRepository.cs:1470-1486`). This class's own `InsertLiveRowAsync` (`:355`) is real and is not touched.
hazards: none: XML doc text; the name is in `<c>`, not a `cref`.

old_str (exact, unique in the file):
```text
    ///     mirroring EmployeeProfileRepository.InsertLiveRowAsync).</description></item>
```

new_str:
```text
    ///     mirroring the employee-profile writer's Case C', <c>EmployeeProfileRepository.ExecuteSplitAsync</c>).</description></item>
```

### S2-4 · `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` · `Today()` summary, "Five call sites depend on it"

status: still stale
lines at `989aea81`: `:562-565` · line endings: CRLF
source: item 2 (the builder's F4, `S148-TASK-14851-report.md:250`)
why: The line cites `:164`, `:194`, `:255`, `:709`, `:1155` point at nothing relevant now. Re-counted at HEAD, `Today()` has exactly **five** call sites, so "Five call sites" stays: `GetByEmployeeIdAsync` (self-managed, `:166`), `GetByEmployeeIdAsync` (conn, tx, `:196`), `GetByEmployeeIdWithScheduledAsync` (`:257`; `GetByEmployeeIdWithVersionAsync` reaches it through `:224`), `SupersedeAndCreateAsync` (`:891`) and `SoftDeleteTimelineAsync` (`:1281`, `closeDate ?? Today()`; `SoftDeleteAsync` reaches it through `:1196`). "routing/cache anchor" is also corrected: the writer's own comment says the router does not read `today`; it decides the covering-today check and the category-cache anchor (`:885-891`). Methods are named, not lines, so the cites cannot drift again.
hazards: none: XML doc text; names in `<c>`, not `cref`.

old_str (exact, unique in the file):
```text
    /// both mechanisms the sprint distinguishes: the as-of-today READS at <c>:164</c>, <c>:194</c>
    /// and <c>:255</c> (which row is in force right now), the dated writer's routing/cache anchor at
    /// <c>:709</c>, and the soft-delete's close-date FALLBACK at <c>:1155</c>, which becomes a row's
    /// <c>effective_to</c> and the emitted event's <c>EffectiveTo</c> — a STORED date, not a view.
```

new_str:
```text
    /// both mechanisms the sprint distinguishes: the as-of-today READS in both
    /// <c>GetByEmployeeIdAsync</c> overloads and in <c>GetByEmployeeIdWithScheduledAsync</c> (which row
    /// is in force right now), the dated writer's covering-today check and category-cache anchor in
    /// <c>SupersedeAndCreateAsync</c>, and the soft-delete's close-date FALLBACK in
    /// <c>SoftDeleteTimelineAsync</c>, which becomes a row's
    /// <c>effective_to</c> and the emitted event's <c>EffectiveTo</c> — a STORED date, not a view.
```

### S2-5 · `frontend/src/pages/admin/PositionOverrideManagement.tsx` · `handleMutationError` comment

status: still stale
lines at `989aea81`: `:151-152` · line endings: CRLF
source: item 3 (`S148-TASK-14838-5a-reviewer-c5.md` NOTE 4; log :4160, :4203)
why: "a failed write does not refresh the list" is untrue: after a refusal that names the predecessor's date the handler re-reads it (`PositionOverrideManagement.tsx:159-165`). What a failed write does keep is the forms as typed: the create form is reset only on success (`:232-235`, `resetCreate` `:246-252`).
hazards: none: `//` comment; no test reads it; `tsc`, lint and vitest are unaffected.

old_str (exact, unique in the file):
```text
  // lower limit. An answer for an interaction that was cancelled or replaced since, or for a date the
  // user has changed since, is ignored; a failed write does not refresh the list, so nothing is lost.
```

new_str:
```text
  // lower limit. An answer for an interaction that was cancelled or replaced since, or for a date the
  // user has changed since, is ignored. A failed write leaves the forms as typed; it re-reads the list
  // only after a refusal that names the predecessor's date (below), so the dates shown catch up.
```

### S2-6 · `frontend/src/hooks/usePositionOverrides.ts` · `hasRows` ref comment

status: still stale
lines at `989aea81`: `:80-81` · line endings: CRLF
source: item 3 (same NOTE 4: "introduced by FU-4")
why: "`loading` is the first read only" is untrue: `setLoading(true)` runs on every read while `hasRows.current` is false (`usePositionOverrides.ts:85`), and that flag turns true only after a successful read (`:87-90`), also one that returned zero rows. **The rename of `hasRows` the review suggested is EXCLUDED** (an identifier change, not comment text, per the brief); the comment instead says what the name means.
hazards: none: `//` comment; the identifier `hasRows` is unchanged at `:82`, `:85`, `:89`.

old_str (exact, unique in the file):
```text
  // `loading` is the first read only (no rows to show yet). A later read, from `refetch` or after a
  // write, is quiet: the rows already shown stay on screen, and a failed one leaves them and sets `error`.
```

new_str:
```text
  // `loading` is raised on every read until one succeeds (until then there is no list to show). Every
  // read after a success, from `refetch` or after a write, is quiet: the rows already shown stay on
  // screen, and a failed one leaves them and sets `error`. `hasRows` means "a read has succeeded",
  // even one that returned no rows.
```

### S2-7 · `src/Infrastructure/StatsTid.Infrastructure/ProjectionBackfillService.cs` · file header, "Rebuilds `time_entries_projection` + `absences_projection`"

status: still stale
lines at `989aea81`: `:17-18` · line endings: CRLF
source: item 4 (register draft below-floor 24)
why: The rebuild writes three tables: inserts into `time_entries_projection` (`ProjectionBackfillService.cs:199`) and `absences_projection` (`:214`, plus the revaluation update `:236-240`), and the latest-wins upsert into `work_time_projection` (`:248`). The class summary (`:132-139`) is already true and is not touched.
hazards: none: `//` file-header comment.

old_str (exact, unique in the file):
```text
// Rebuilds `time_entries_projection` + `absences_projection` from the canonical
// `events` log so deployments don't strand pre-S27 events outside the new sync
```

new_str:
```text
// Rebuilds `time_entries_projection`, `absences_projection` and `work_time_projection`
// from the canonical `events` log (the replayed event types are listed on the class
// summary below) so deployments don't strand pre-S27 events outside the new sync
```

### S2-8 · `src/Integrations/StatsTid.Integrations.Payroll/Services/PriorMonthNotApprovedException.cs` · class summary, "Where it is thrown"

status: still stale
lines at `989aea81`: `:20` · line endings: CRLF
source: item 5 (register draft below-floor 1)
why: "Either way nothing has been written" holds for admission only. The export recheck (thrown at `PayrollExportService.cs:669`) rolls the export transaction back, but the calculation's manifest, written a moment earlier, stays (`Payroll/Program.cs:560-569`, the catch's own comment).
hazards: none: XML doc comment. The response sentence `ErrorSentence` (`:35-39`) is not touched; see "Not applied".

old_str (exact, unique in the file):
```text
/// Either way nothing has been written. The host maps it to a 409
```

new_str:
```text
/// At admission nothing has been written. At the export's recheck the export transaction rolls back
/// (no export record, no event, no audit row), but the month's calculation manifest, written a
/// moment earlier, stays. The host maps it to a 409
```

### S2-9 · `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` · month GET, the absence catalogue's month-end anchor

status: shortcut comment to add (an insertion)
lines at `989aea81`: `:342-343` · line endings: CRLF
source: item 6 (QUAL-BF-011; register draft B-10; ruling (2) at `SPRINT-148.md:4704`)
why: PAT-032: a registered accepted limit carries a `shortcut(<tag>):` comment at its spot. The month view decides the catalogue once, at `monthEnd` (`SkemaEndpoints.cs:343-345`), and the særlige-feriedage row is served only when a `SPECIAL_HOLIDAY` config resolves at that anchor (`:3590-3592`), so a config ending mid-month offers no row that month (pinned by `SkemaSpecialHolidayRowTests.ConfigEndingMidMonth_RowNotServedInTheEndingMonth_ServedTheMonthBefore`, `:240`, docstring `:228`).
hazards: **the shortcut check.** `tools/check_docs.py` (`:190-191`, `:262-275`) finds the tag and requires QUAL-BF-011's location cell to equal this file's path. It does: `docs/operations/quality-finding-register.md:418`, cell `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`. Simulated with the checker's two regexes: tag `QUAL-BF-011`, lowercase keyword, cell match. The check's count goes from 22 to 23 found, 0 bad. The comment is a new line between an existing comment and an unchanged statement; nothing that runs changes. The file is UTF-8 without BOM and already holds Danish letters.

old_str (exact, unique in the file):
```text
            // (the established S59 display anchor).
            var absenceCatalog = await ComputeAbsenceCatalogAsync(
```

new_str:
```text
            // (the established S59 display anchor).
            // shortcut(QUAL-BF-011): the særlige-feriedage row is decided once, at month end, so a config ending mid-month offers no row that month, even on the days it still covers; upgrade when the catalogue is decided per day
            var absenceCatalog = await ComputeAbsenceCatalogAsync(
```


## Not applied

### A. Excluded: behaviour or identifier

- **`PriorMonthNotApprovedException.cs:35-39`**, "… Nothing was calculated, written or exported." This is the value of `public const string ErrorSentence`, the fixed sentence of the payroll host's 409 body (`Payroll/Program.cs:984`, `error = PriorMonthNotApprovedException.ErrorSentence`), not a comment. Changing it changes a response. **EXCLUDED: behaviour.** It stays recorded below the floor (register draft item 1), and the class doc (S2-8) now says the export-recheck case truthfully.
- **The rename of `hasRows`** in `usePositionOverrides.ts` (the reviewer suggested something like `hasLoaded`). **EXCLUDED: an identifier change.** S2-6 corrects the comment and says what the name means; the rename waits for the next change to the hook.

### B. Excluded: needs regeneration (carried)

- **`tools/StatsTid.DemoSeed/Generation/SqlEmitter.cs:202` and `:230`.** These are emitted SQL comments, so a change regenerates `tests/StatsTid.Tests.DemoSeed/Golden/golden-legacy-smoke.sql` and the committed `docker/postgres/99-demo-seed.sql`. Unchanged ruling: fix when the emitter is next touched.

### C. A doc that S2-9 makes stale (Orchestrator, `docs/`)

- **QUAL-BF-011's status cell** (`docs/operations/quality-finding-register.md:418`) ends "open (registered S148, 2026-11-25; no `shortcut(QUAL-BF-011)` comment yet)". Once S2-9 lands, "no … comment yet" is untrue. It is a `docs/` edit, outside this sweep. Suggested: "open (registered S148, 2026-11-25; `shortcut(QUAL-BF-011)` at the month view's catalogue anchor)".

### D. Found on verification: more mentions of the non-existent `InsertLiveRowAsync` (needs a ruling; not authorized)

Re-running `git grep -n InsertLiveRow -- src tests tools docker frontend` finds the same stale name in three more places. They are not in item 24. Each is more than a rename, because the sentence around the name has also drifted, so no ready block is offered. They need a sentence rewritten with care, at their next touch or in a third sweep.

| Place | What is stale | Why a rename alone is not enough |
|---|---|---|
| `docker/postgres/init.sql:5050-5052` (protected file) | "all four production INSERT paths (EmployeeProfileRepository.CreateAsync / InsertLiveRowAsync, EmployeeProfileSeeder, AdminEndpoints user-create) populate the column same-tx from users" | Since S143 the seeder and the admin create both go through `CreateAsync` (`EmployeeProfileRepository.cs:587-589`); the dated writer's `InsertRowAsync` takes the request's category, else the predecessor's, else the users value (`:1001`, `:1546-1547`), not always the users value |
| `tests/StatsTid.Tests.Regression/EmployeeProfile/ProfileCategoryDatingTests.cs:20`, `:86`, `:174`, `:214`, `:234` | "born in / inside / both serve / via `InsertLiveRowAsync`" | The paths are `ExecuteOpenInsertAsync` (Case A) and `ExecuteSplitAsync` (Case C'), both through `InsertRowAsync`; ":20" and ":86" also say the successor "copies it from `users`", but it copies the predecessor's category first (`:1001`) |
| `tests/StatsTid.Tests.Regression/UserAgreementCode/UserAgreementCodeRepositoryTests.cs:33` | "`UserAgreementCodeRepository.InsertLiveRowAsync` xmldoc" | The rule's doc is now on `UserAgreementCodeRepository.ExecuteSplitAsync` (`UserAgreementCodeRepository.cs:644-651`), whose stated reason (the audit chain's "v → v+1") differs from the test's ("ETag monotonicity alone") |

(The real `InsertLiveRowAsync` methods in `EmployeeEntitlementEligibilityRepository.cs:355` and `RoleConfigOverrideRepository.cs:524` are correctly named and untouched.)

### E. The re-run grep (after the first spec)

`grep -n -iE "comment sweep|comment-only sweep|stale comment|stale-comment|next touch"` over `docs/sprints/SPRINT-148.md` after line 4600, and over `.claude/reviews/S148-TASK-14839*` (with "stale remark|untrue|slightly broad" added):

| Hit | Disposition |
|---|---|
| Log :4650 and `S148-TASK-14839-5a-reviewer-c1.md:76`, the emitter's "nothing reads" | excluded, § B |
| `S148-TASK-14839-5a-reviewer-c1.md:68`, AC-C12c's docstring | already fixed in `c186e2b1` (first spec, § A) |
| Log :4663-4672, the first sweep's merge record | no item |
| Log :4704, :4713, :4742 (item 24 and the QUAL-BF-011 comment) | this spec |

No new item.

## Authorized file list for the sweep agent

Only these 8 files, only the blocks above:

- `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileRepository.cs` (CRLF): S2-1, S2-4
- `src/Infrastructure/StatsTid.Infrastructure/EmployeeProfileSeeder.cs` (CRLF): S2-2
- `src/Infrastructure/StatsTid.Infrastructure/EmployeeEntitlementEligibilityRepository.cs` (CRLF): S2-3
- `frontend/src/pages/admin/PositionOverrideManagement.tsx` (CRLF): S2-5
- `frontend/src/hooks/usePositionOverrides.ts` (CRLF): S2-6
- `src/Infrastructure/StatsTid.Infrastructure/ProjectionBackfillService.cs` (CRLF): S2-7
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PriorMonthNotApprovedException.cs` (CRLF): S2-8
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` (CRLF): S2-9

Not authorized: everything else, including `PriorMonthNotApprovedException.cs:35-39`, any rename of `hasRows`, `SqlEmitter.cs`, the § D places, and every file under `docs/`.

## Expected effect

- **Behaviour:** none. S2-1 to S2-8 edit only `//` or `///` comment text. S2-9 inserts one `//` line and leaves the statement after it byte-identical.
- **Build:** warnings and CA2100 unchanged. No project generates XML docs, and no block adds a `cref`: the method names are in `<c>`.
- **Tests:** counts and names unchanged. No test reads these comments. The frontend checks (`tsc`, lint, vitest, build) are unaffected by comment text.
- **Generated and pinned outputs:** none move. No DemoSeed, `init.sql` or OpenAPI text is touched.
- **CI docs job:** `tools/check_docs.py`'s shortcut check goes from 22 found to 23 found, 0 bad. This holds because QUAL-BF-011's location cell is exactly `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` and the tag's form is exact. The register's status cell for QUAL-BF-011 becomes untrue (§ C); that is a `docs/` edit, not a CI failure.

## Revision history

| Version | Date | Change |
|---|---|---|
| 1 | 2026-11-25 | First draft of the second sweep, from register-draft item 24, item 1 and ruling (2); every item verified at `989aea81`. |

---

## Orchestrator rulings, 2026-11-25 (bind the sweep agent)

1. **Apply S2-1 … S2-9, all of them,** including S2-9's `shortcut(QUAL-BF-011):` insertion.
2. **The three further `InsertLiveRowAsync` mentions** (`init.sql:5050-5052`, `ProfileCategoryDatingTests.cs` ×5, `UserAgreementCodeRepositoryTests.cs:33`) are **not applied**. The sentences around them have drifted, so they need a careful rewrite. They are recorded in the sprint log for their next touch.
3. **Exclusions confirmed:**
   - `PriorMonthNotApprovedException.cs:35-39` is a runtime message, the 409 body, so changing it is behaviour.
   - The `hasRows` rename changes an identifier.
   - `SqlEmitter.cs:202/230` is carried.
4. QUAL-BF-011's status cell is updated by the Orchestrator at this sweep's merge.
