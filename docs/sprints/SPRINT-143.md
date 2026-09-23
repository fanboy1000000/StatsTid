# SPRINT-143 — The client's own clock

| | |
|---|---|
| **Status** | **CODE COMPLETE** — 16 tasks, all DONE. Awaiting Step 7a + CI. Step 0b took 2 internal cycles and 3 external (the third owner-authorised past the cap); Step 5a ran on every substantive task, and found a defect in **every single one** |
| **Result** | **No frontend production source derives a business date from the device clock** — not the four seeding sites, and (after a Step-7a BLOCKER) not the six that supply STORED dates through the approved helper either. Enforced by an AST guard for the spelling, and by ADR-042 for the authority the guard cannot see |
| **Final counts** | build **145 warnings / 0 errors** (S142 baseline, unmoved through 15 merges) · unit **1290** (+26) · demo-seed **170** · regression non-Docker **128** · frontend **976** (+82) |
| **Opened** | 2026-09-23 |
| **Predecessor** | S142 (`5408769` close, `0a001b4` post-close) — ADR-041, business dates are the Copenhagen day |
| **Refinement** | `.claude/refinements/REFINEMENT-s143-the-clients-own-clock.md` rev 5 — READY. Dual-lens reviewed, both lenses used both cycles, internal verdict APPROVED-WITH-WARNINGS |
| **Entropy scan (Step 0a)** | Clean: working tree clean, 0 worktrees, 0 non-master branches, no untracked source |
| **Baseline** | build 0 errors / 145 warnings; 4,299 tests (unit 1264, demo-seed 170, regression 1971, frontend 894) |

## Plain-language summary

S142 fixed every place the product **writes or validates** a date, moving them from the UTC calendar
to the Danish one. It deliberately left four places in the frontend that decide **which month a
screen opens on**, on the reasoning that these are display defaults.

That reasoning was wrong, and finding out why is what this sprint is built on. The opening month is
passed to the server as the period envelope of both the save and the approval submission
(`useSkema.ts:211-217`, `:334-336`). It is the month a person's time is **filed under**. A default
computed from the wrong calendar therefore files work into the wrong month — visibly, since the
screen shows which month you are in, but wrongly.

So S143 finishes the calendar work, and adds the thing S142 could not: **a guard the frontend has
never had.** The backend's source-text guard scans C# only; the frontend's lint turned out to be a
hand-enumerated 34-file list backed by a config with no general rules at all. Four migrated sites
with nothing stopping a fifth is how this defect got here.

## Owner rulings

| # | Ruling | Date |
|---|---|---|
| **OQ-1a/1b** | **Server clock, delivered as a bootstrap read.** One typed endpoint returns the Copenhagen business day; read once at app start; shared by all four sites. Decided by evidence: the year overview *already* returns a server `Today` and `ArsoversigtPage.tsx:119` still reads the browser, because you must ask for a year before the server will tell you what year it is | 2026-09-23 |
| **OQ-1c** | *Consequence, not a separate ruling* — the shell is gated, so an authoritative day always exists when a page renders; `SkemaGrid`'s highlight reads it. No `Today` member added to `SkemaMonthResponse`; no device read survives | 2026-09-23 |
| **OQ-1d** | **Gate the app shell.** If the startup read fails, no page renders — owner ruling OQ-11's shape carried to the browser: refuse rather than guess | 2026-09-23 |
| **OQ-3** | **QUAL-165's build goes to S144, named and committed.** Its third consecutive deferral; naming the sprint is what stops a fourth. *Correction on the record:* the exposure was described as auditability when deciding; it is **domain correctness** | 2026-09-22 |
| **OQ-4** | **QUAL-177: `CreateAsync` becomes the single write path** and *takes* the effective date rather than computing it. Implements the S137 "one date for the whole create" ruling rather than sitting in tension with it | 2026-09-23 |

Resolved by measurement rather than ruling: **OQ-2** (1 executable clock read in frontend test files,
not "dozens"), **OQ-5** (no general ESLint rules exist to widen), **OQ-6** (2 e2e sites, both already
in scope — same rule, same exemption).

## Task plan

Revised at Step 0b after both lenses returned BLOCKED. Three of the corrections are structural.

### The landing mechanism (internal-lens B2) — read this before the table

The first draft sequenced the guard **before** the migration so the four existing sites would be its
natural RED proof. That ordering is right and it was **not buildable as written**. The guard covers
`frontend/src/**` and `frontend/e2e/**`, so on the day it merges it fails on **seven** known sites —
the four production seeds, `DelegationPage.test.tsx:54`, and the two e2e sites — and the last of
those is not fixed until the final task in the chain. CI would be red on master for most of the
sprint. The only ways an implementer could merge it green are `it.skip`, `test.fails`, or an
improvised allowlist, and the first two are precisely a guard that passes while doing nothing.

**The repo has a near-miss of the right pattern, and the difference matters.** PAT-012 gate 4 uses
`tools/openapi-convention-exempt.txt`. Plan rev 2 inferred that the manifest is shrink-only *because*
a stale entry fails. **The stale check is real** — `tools/check_openapi_convention.py:247` plus
`:314-315` fail the build on an entry that no longer offends, with the reasoning stated at `:292-296`
(both lenses verified this independently). **But shrink-only does not follow from it**, which is what
rev 2 got wrong: `:238-241` routes a *live* offender that appears in the manifest into
`grandfathered_hits` rather than `failures`, so adding a new offender **together with** a manifest
line stays green. The manifest cannot go stale; it can grow without limit. "Do not add" is a comment,
not an enforcement.

So the list cannot be shrink-only by that mechanism alone. The guard adopts the manifest's good half
and adds the missing half:

- A known-offenders list naming **exactly** the seven sites, each with the task that removes it.
  *(AST-enumerated and confirmed at exactly seven by the external lens.)*
- An **unlisted** offender fails — the guard's purpose.
- A **listed file that no longer offends also fails** — the stale check, adopted as-is.
- **The bound is the pinned sidecar set, not a counter.** Rev 3 used a `MAX_KNOWN_OFFENDERS` constant
  that each task would lower. **That was a fresh instance of the bug it was fixing:** the constant
  lives in the guard file, so all four migration tasks would have had to edit one shared file — the
  conflict the sidecars exist to remove — and their narrowed scopes *exclude* that file, so none of
  them could legally have done it. External-lens cycle 3.

  Replaced with something no task has to touch: the guard **pins the set of sidecar filenames** —
  exactly four, one per owning task. An **empty sidecar is legal**. So:
  - Adding an eighth offender means either appending to an existing sidecar — visible in that task's
    own diff, next to the code that caused it — or creating a fifth sidecar, which **fails the pinned
    filename assertion**. There is no third route.
  - **A missing or unreadable sidecar fails**, which closes the hole a glob leaves open: a sidecar
    holding only stale entries would otherwise vanish silently, since a stale check can only inspect
    entries it discovered.
  - Migration tasks touch **only their own sidecar**. Nothing shared, no counter, no decrement, no
    guard-file edit.
- **Every entry names the task that removes it**, and the guard asserts the id is one of
  `{14303, 14304, 14305, 14306b}`. This is a legibility check, not proof of ownership or
  completeness — the pinned filename set is what supplies those.
- **The close gate is what actually makes it shrink-only**, and it is unconditional: all four
  sidecars empty. The openapi manifest has no such gate, which is precisely why it grandfathers
  permanently and this one cannot.
- **At close, the mechanism itself is deleted** — the pinned filename set, the sidecar glob and the stale check — so no
  allowlist survives into S144. Re-introducing one would then be a visible change rather than a line
  appended to a file nobody reads.
- The natural-RED proof is one recorded run with the sidecars emptied, captured in 14302's output.

### Tasks

**How to read the Agent column.** The tree after each agent name (`frontend/**`, `src/Backend/**`) is
the agent's **home scope** — the boundary it may not cross without a cross-domain label. It is not
the task's licence. **The authorized file set for each task is the files its Scope column names**, and
the Constraint Validator checks against that, per `AGENTS.md:44-57` ("file-scope checks operate on the
explicit scope declared in the sprint plan, not on the agent label"). A task that names one fixture
is authorized for that fixture, not for the frontend.

**Acceptance row 11 is inapplicable, stated rather than left blank.** The refinement's conditional
criterion — a helper-level failure assertion must name a specific message and use `vi.resetModules()`
plus dynamic import — binds only if a task asserts a failure path inside `copenhagenDate.ts`. **No
task in this sprint modifies or failure-tests that file**: 14305 imports it, 14310 tests runtime
capability around it. If any task finds it needs such an assertion, the criterion binds and the task
declares it.

| Task | Scope | Agent + authorized files | Depends on |
|---|---|---|---|
| **14300** | **The server's day.** A **new `CalendarEndpoints` class** plus one `ApiEndpoints.MapAll` line, route `GET /api/calendar/today`. *Not* an extension of `TimeEndpoints` — plan rev 2 read the cycle-1 note as "put it there" when it only said the name was taken: `TimeEndpoints` is the **time-entries** family (`/api/time-entries`, `Endpoints/TimeEndpoints.cs:24,340`), and a calendar bootstrap does not belong in it. The response record is **fixed here, not left to 14301**: the Copenhagen business day **plus `secondsUntilNextMidnight`** — a duration, because an absolute `nextMidnightUtc` would need `Date.now()` on the client to turn into a delay, reintroducing the device read this field exists to remove. **But a duration ages in transit** (external-lens cycle 3), so it is not sufficient alone: a response computed seconds before a month-end midnight can arrive after it, and the client would seed yesterday's month *and* schedule its refresh late. Two requirements follow, both owned here: (a) the client times the refresh from **`performance.now()` monotonic elapsed since receipt** — never the wall clock, so transit and skew cannot corrupt it; (b) if `secondsUntilNextMidnight` arrives below a small threshold, the client **re-reads instead of trusting it**. **DST-safe computation, specified rather than left to the implementer:** derive **tomorrow's unspecified-kind local midnight**, convert it through `Zone` to UTC, and subtract **the same captured instant** used to derive `today`. Adding 24 hours, or reusing today's offset, is wrong on the 23- and 25-hour days — **pin both**. **Scope trap:** if this tempts a new helper on `CopenhagenBusinessDate`, that file is `src/SharedKernel/**/Calendar/**` — **rule-engine scope, outside this task**. Compute it in the handler from the public `CopenhagenBusinessDate.Zone`. `DateOnly` already serialises as `format: date`, so no DTO invention. Name the any-authenticated policy explicitly (policies are string-named, cf. `AdminEndpoints.cs:93`). **Born typed:** registration through `ApiEndpoints.MapAll`, the response record + `.Produces<T>`, `--openapi` regeneration of `docs/api/openapi.json` and `npm run gen:api` regeneration of `frontend/src/lib/api-types.ts` — **both under PAT-012 § Scope status' standing authorization for these two pipeline outputs, invoked here by name** so the agent does not stall on the `docs/` Orchestrator-only rule. Must **not** enter the grandfather manifest. Pin at `BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen` asserting the **literal** `2026-07-16` — the one anchor that kills both raw-UTC and a hardcoded +01:00. **Both proofs are Docker-gated** (`OpenApiSpecRuntimeTests` is `[Trait("Category","Docker")]`; `WithFixedInstant` rides the Postgres factory) — report **CI-verified, never locally green** | backend-infrastructure — `src/Backend/**`, `docs/api/openapi.json` + `frontend/src/lib/api-types.ts` (generated only, PAT-012 authorized), `tests/**` · **Backend API (cross-domain authorized)** | — |
| **14301** | **The client's one day.** Calendar context reading 14300 once at app start. **Gate placement specified:** inside `RequireAuth` (`components/guards/RequireAuth.tsx:4-12`, the only gate), after `isAuthenticated`, so `/login` stays reachable; auth restore is synchronous from `localStorage` (`AuthContext.tsx:49-94`) and is not disturbed. **The gate distinguishes 401 from everything else** — an expired token routes to login, not to a calendar error screen. Refresh: timer to the next Copenhagen midnight (from 14300's field) **plus** re-read on tab visibility regain. **AC-7, this task's half — four scenarios, not one.** Pin: (i) the **in-flight race** — a consumer mounting *while* the midnight refresh is still pending; (ii) **an already-open month does not move** when the refresh completes; (iii) **a mount before the refresh has started**, i.e. the response itself arrived already stale because it aged in transit past midnight — the case the duration field cannot prevent and the near-zero re-read threshold exists for; (iv) **a mount during retry backoff after a refresh failure**, where the last known day is deliberately being served. Rev 3 had only (i) and (ii); (iii) and (iv) are external-lens cycle 3. (14310 owns the fifth: a mount *after* midnight with the refresh already completed, against a real page.) A refresh failure keeps the last known day and retries; it does **not** gate a running app — the asymmetry with OQ-1d is deliberate and documented at the seam. **Ships `renderWithCalendar(today)`** so the affected test files do not each invent one. **Blast radius named before code (S121 rule):** `App.opfoelgningRoute.test.tsx`, `App.overtimeRoute.test.tsx` (both render `<App/>`), plus every test rendering `SkemaPage`, `SkemaPageParamInit`, `SkemaGrid`, `ManagerSkemaGrid`, `TeamOversigt`, `TeamRowDetail`, `ArsoversigtPage` | ux — `frontend/**` | 14300 |
| **14302** | **The guard + the offenders list.** AST scan via `ts.createSourceFile`, walking for a `NewExpression` on `Identifier('Date')` with empty/undefined arguments — **not** regex. The hazard cases are **inlined into the task prompt, not cited** (the refinement is gitignored and does not reach a worktree — `AGENTS.md:164`): template literals with nested holes, regex literals containing a double slash, JSX text versus expression containers, escaped quotes, comment markers inside strings, a comment between `new` and `Date`, and **`new Date` with no parentheses or with spaced parentheses**. Discovery via `import.meta.glob` over `/src/**` and `/e2e/**` with raw eager imports — never `node:fs` (`tsconfig.json:18` excludes Node types from `src/**`); **if it does not typecheck, add `vite/client` to `types`, never `node`**. Lives under `src/**/*.test.ts` (`vite.config.ts:32`). Asserts its own scanned set, a floor on its size, **a violation in a newly created nested previously-unknown file**, and **failure on a missing or mistyped root**. On that last point the causal link must be explicit in the code, because the obvious reading is wrong: `import.meta.glob` **returns `{}` silently** for an absent or mistyped root — it throws nothing. **The floor assertion is the only thing that turns that silence into a failure**, so an implementer must not go looking for an exception that will never be raised | ux — `frontend/src/**/*.test.ts` (the guard), the sidecar directory | — |
| **14303** | **Skema.** `SkemaPage.tsx:218` seed and `SkemaGrid.tsx:125` highlight onto the shared day **via a hook, not a prop** — no signature ripple. **Owns `ManagerSkemaGrid.tsx:106-120` and its tests by name**: the Teamoversigt detail panel renders the same grid, so 14303's change reaches 14304's area either way, and assigning it here is what stops the "ordered is not atomic" split. Owns the query-parameter precedence proof (`SkemaPage.tsx:212-230`, existing `SkemaPageParamInit.test.tsx`). Delivers AC-5 as **one** combined assertion — opened month = month sent to `/save` = month sent to `/approval/send` = highlighted cell. Month-boundary filing pin. `forceTestTimeZone` with the forcing itself asserted (ADR-041:74-75). **Also owns `SkemaGrid.test.tsx:446-447`**, carved out of 14306a because two worktrees editing one file is the S142 merge hazard (`SPRINT-142.md:298`). **Re-proves the guard** by injecting a violation into `SkemaPage.tsx` and `SkemaGrid.tsx` individually under `npm run test` — that proof is only meaningful after migration, which is why it lives here and not in 14302. Empties its own sidecar (the file stays; an empty sidecar is legal, and nothing else is touched). **Must not touch `SkemaGrid.tsx:108-122`** | ux — `frontend/src/pages/SkemaPage.tsx`, `frontend/src/components/SkemaGrid.tsx`, `frontend/src/pages/approval/ManagerSkemaGrid.tsx`, their `__tests__` (incl. `SkemaPageParamInit.test.tsx`, `SkemaGrid.test.tsx`), its own sidecar. **Narrowed by file because 14303 and 14304 run concurrently** — the plan's own reason for moving `ManagerSkemaGrid` here was that two worktrees must not edit one file, and `AGENTS.md:53` says scope checks run on what the plan declares | 14301, 14302 |
| **14304** | **Approval + year.** `TeamOversigt.tsx:384` and `ArsoversigtPage.tsx:119` onto the shared day. `forceTestTimeZone` with the forcing asserted — these seeds need it as much as Skema's. Re-proves the guard by injecting a violation into `ArsoversigtPage.tsx`. Empties its own sidecar | ux — `frontend/src/pages/approval/TeamOversigt.tsx`, `frontend/src/pages/ArsoversigtPage.tsx`, their `__tests__`, its own sidecar | 14301, 14302 |
| **14305** | **The e2e cluster — three limbs, one change.** `helpers/dates.ts:60-80` (UTC base, slots 1–18); `approval.spec.ts:329-334` (own UTC base, slots 19–30, **deliberately disjoint — preserve that**); and `skema-registration.spec.ts:52-58`'s **relative click-count navigation**, which silently encodes "the app opened where I computed" and is an integer no date sweep would find. **The authority is named, not left open:** e2e imports `copenhagenToday()` from the `src/lib` helper — TypeScript follows imports past `tsconfig.e2e.json`'s include list, and the helper has no Node dependency — so no second helper and no new exemption. Correct `dates.ts:5-6`. Empties its own sidecar | ux — `frontend/**` | 14303, 14304 |
| **14306a** | **Hygiene, C# tier.** The ~32 census Category-3 sites. **Not dispatched as a sweep** — they are heterogeneous judgment items (2-year margins, self-threaded assertions, 18 PhaseE placeholder dates, a harness-hygiene item), and the sweep agent is defined as ONE pattern-shaped change with no improvisation. The census rows are **appended to this sprint log as a ~32-row table before dispatch** — file:line plus the intended disposition per row — and the prompt quotes that table. Rev 2 said "inlined into the prompt", which is ephemeral; the plan's own method rule makes the sprint log the record, and putting the rows here also lets Step 0b check Assumption 4 ("non-gating") rather than take it on trust. **Scheduling exception, recorded explicitly:** `AGENTS.md:37` requires Test & QA to run after all implementation agents; this task has no implementation predecessors because none of the ~32 sites is touched by any other task in this sprint — they are pre-existing tests being made deterministic, not pins derived from new behaviour. The rule's purpose (do not pin behaviour that is still moving) is satisfied, so the exception is safe and is named rather than silently taken | test-qa — `tests/**` (the ~32 named files only) | — |
| **14306b** | **Hygiene, frontend tier.** `DelegationPage.test.tsx:54` **and `MondayDatePicker.test.tsx`** — the latter is a census Category-3 row (`SWEEP-s142-test-clock-census.md:143`, "coverage gap, not a live defect") that the 14306a/14306b split orphaned: it is a frontend test, so `tests/**` cannot own it, and rev 2 named only DelegationPage. Empties its own sidecar (the file stays; an empty sidecar is legal, and nothing else is touched). *Dependency on 14303 dropped* — with per-task sidecars there is no shared file to conflict on, and this is a three-line change that should not queue behind the sprint's largest task | ux — `frontend/src/pages/delegation/__tests__/DelegationPage.test.tsx`, `frontend/src/components/config/__tests__/MondayDatePicker.test.tsx`, its own sidecar | 14302 |
| **14307** | **QUAL-176 — four named stamps, not "both sites".** `ReportingLineEndpoints.cs` carries `DateTime.UtcNow` at `:131, :625, :1079, :1418, :2008, :2458`, **each commented "BY DESIGN"**. Only the two vikar creates (`:2008`, `:2458`) are in scope, because the delegation GET derives a **business date** from `CreatedAt` (`:1771-1773`) — plus `ManagerVikarRepository.cs:207`'s fallback and `LocalAgreementProfileMigrator.cs:544`. The task must **say why the other four stay** (no reader derives a date from them) and **reconcile all six S140 comments**, or the file ends with two stamps on the provider and six comments claiming the real clock. The row-25 pin is `WithFixedInstant`-based → Docker-gated, report CI-verified | backend-infrastructure — `src/Backend/**`, `src/Infrastructure/**`, `tests/**` · **cross-domain authorized** | — |
| **14308** | **QUAL-177 (OQ-4).** `CreateAsync` takes the effective date; both inline INSERTs route through it, preserving each caller's transaction, audit and outbox semantics. Seeder passes the `0001-01-01` anchor (`EmployeeProfileSeeder.cs:85-109`); admin passes its single pre-transaction `effectiveFrom` (`AdminEndpoints.cs:924-1090` — the S137 one-date ruling, with its four-way atomic INSERT and two outbox enqueues). Four test callers move with it. Fix the `AdminEndpoints.cs:931-933` comment that contradicts the code | backend-infrastructure — `src/Infrastructure/**`, `src/Backend/**`, `tests/**` · **cross-domain authorized.** *Was data-model; that agent's scope excludes every file here, and this is the sprint's one atomic-write/outbox path — the stronger model by that agent's own definition* | — |
| **14309** | **The worktree gate.** `sprint-close-guard.ps1` blocks a close while that sprint's worktrees remain — enumerate with `git worktree list --porcelain` minus the main worktree. Test seam follows the existing **S99-only** env pattern (`sprint-close-guard.ps1:302-310`), so a leaked variable cannot disable the gate. Owed since S141; proven to fire | *Orchestrator* | — |
| **14310** | **The two unowned acceptance proofs.** (a) Copenhagen timezone capability test — offsets read with an explicit zone and a **locales argument first**; RED proved by mutating the expected literal, never by changing the environment. (b) **The actual-page** post-midnight mount pin: the refresh **already completed**, then **`SkemaPage` mounted after midnight**, asserting it does not seed yesterday's month. This is the AC-7 half 14301 cannot deliver — until 14303 lands, the page still reads the device, so a provider-level pin would prove the context and not the product. *Division of AC-7:* 14301 pins the in-flight race and the already-open month not moving; 14310 pins the after-midnight mount against a real page | ux — `frontend/src/lib/__tests__/**`, `frontend/src/pages/__tests__/**` | 14301, **14303** |

**Guard GREEN is an Orchestrator gate, not a task.** 14302 delivers the guard plus four sidecars
holding seven entries between them; they empty as 14303/14304/14305/14306b land. The Orchestrator verifies every sidecar
is empty and the guard green at integration, before Step 7a. The first draft left this undefined,
which is how a sprint ends with a red guard and everyone assuming someone else owned it.

**Close gates, Orchestrator-owned:** full CI green on the close sha; warnings not above the S142
baseline of 145; all four offender sidecars empty -- then the mechanism itself deleted (TASK-14312), so none survives into S144.

### Cut order (internal-lens N5)

If **14301** is cut, **14300 leaves a dead route** — the QUAL-162 class; the pair is cut together.

**On cutting a migration after 14302 has landed:** plan rev 2 said the ledger would record DEFERRED
with the residual window named. The external lens is right that this **contradicts** the
unconditional empty-list close gate above — a ledger entry is not a gate, and two rules that
disagree mean the weaker one wins in practice. Corrected: **the close gate stands unconditionally.**
The only thing that can waive it is an explicit owner ruling, taken through the known-accepted-hole
mechanism and recorded as such — the same route every other invariant waiver takes in this project.
An Orchestrator cannot waive it by writing DEFERRED in a ledger.

## Governance contradiction found at Step 0b — surfaced, not unilaterally resolved

`docs/AGENTS.md` defines seven domain agents. `.claude/agents/` contains **twelve**, and two this
sprint dispatches — `backend-infrastructure` and `sweep` — appear in neither the document nor its
Cross-Domain Authorization table.

But it is not simply drift. `docs/AGENTS.md` carries a section headed **"Why this exists rather than
a 'Backend Agent' or 'Infrastructure Agent'"**, arguing that such a generalist "would absorb work
that legitimately splits across specialists, hide cross-domain coupling that should be surfaced for
review, and grow unboundedly to swallow whatever doesn't fit elsewhere." An agent named exactly that
now exists and is the project's most-dispatched implementer. Its own definition is mature — it cites
ADR-019, ADR-018 D3, ADR-026 and ADR-040 D7 — so practice moved and the document did not follow.

**Rewriting a documented architectural decision is an owner call, not an Orchestrator one**, so this
sprint does not do it. Instead: dispatch proceeds to the agents that exist, and every task above
carries an **explicit authorized file scope** with the documented cross-domain label — which is what
`docs/AGENTS.md` actually requires, and closes the external lens's blocker without pre-empting the
decision. The standing question for the owner: adopt the generalist in the document, or retire it in
favour of the convention it was built to replace.

## Orchestrator-owned (docs, no agent)

The `docs/AGENTS.md` roster question above; `ADR-041:79-80` and `ROADMAP.md:166-168`, which both
explicitly park display/navigation dates and are amended at close; `SkemaEndpoints.cs:182-184`'s
comment **reviewed against** what 14300/14301 change (under the bootstrap ruling it stays true — the
handler still derives its dates from the requested month); QUAL-177's register row corrected (its
"either answer is cheap" is false); a new register row for **"the frontend has no baseline lint
ruleset"**; QUAL-165 recorded against S144 by name.

## Method rules carried into this sprint

- **The sprint log supersedes the census.** Reading `.claude/sweeps/SWEEP-s142-test-clock-census.md`
  as a live backlog put two already-completed items into the refinement's first draft.
- **Measure before asking.** Three of eight open questions dissolved when a command was run instead
  of reasoned about — twice contradicting a cost estimate from a review lens.
- **Parse, don't pattern-match.** The measurement that sized the guard's reach reported five
  test-file violations; four were comments. A text-scanning guard would have failed on four comments.
- **A guard's reach must be asserted, not implied.** A scanner hard-coded to the four known files
  satisfies "enumerate what you scanned" and every injected-violation proof while missing every file
  created afterwards.
- **A guard needs a landing mechanism, not just a RED proof.** See B2 above: "write it first, prove
  it red" is only buildable with a shrink-only offenders list, or the sprint runs on a red master.
- **Never cite a gitignored artifact in a task prompt** — inline it. Violated again in the first
  draft (the guard's hazard list) after being made a standing rule in S142.
- **"Commit and report the sha" belongs in every task's acceptance criteria, not in a follow-up
  message.** *(New, S143.)* Three of this sprint's tasks finished with their work uncommitted — and in
  two cases with a **new test file untracked**, which is FAIL-003 exactly: local build and test glob
  everything on disk, so local green does not prove the file is in the commit, and CI builds only what
  was committed. The Orchestrator had to ask afterwards each time. The briefs that *did* carry the
  instruction produced clean commits first time. It costs one line in the prompt and saves a round
  trip per task; more importantly, an agent that reports "done" with an untracked file has told the
  truth as it understands it, so the gap is in the brief, not the work.

  **Recorded twice, because writing the rule down did not prevent the next instance.** This entry was
  added to the log and then the *very next dispatch* omitted the instruction again — a fourth task
  finished uncommitted. Which is the actual lesson: a method rule in a sprint log is a note to a
  future reader, not a control on present behaviour. The durable fix is a prompt template that carries
  it, or a dispatch checklist — something that has to be passed through rather than remembered.
  Compare TASK-14309: the worktree teardown was *named* as owed in S141, cost two sprints, and only
  stopped costing when it became a gate.
- **An agent must not end a turn with a verification still pending — backgrounded work is reaped at
  the turn boundary.** *(New, S143; hit three times, by two different agents.)* An agent that launches
  a test suite in the background and stops to wait will never receive it: the completion notification
  fires precisely *because* it stopped with no live children. Both agents reported the run as "alive,
  verified not hung" — true when they looked, false the moment their turn ended. This is not a
  judgement error, it is how backgrounded work interacts with turn boundaries, and it is invisible
  from inside.

  Two failure modes compound it. A size or liveness check on the output file can read **before the
  tool flushes**, producing a confident "it died" about a run that completed fine. And piping through
  `tail` or any pager buffers the whole stream, so a finished run looks unfinished.

  The instruction that works, and which belongs in the brief rather than in a rescue message: run it
  in the foreground, or hold the turn open with **one** blocking command — `until grep -q "Test Files"
  <log>; do sleep 5; done` — writing straight to a file, never through a pager.
- **An agent's worktree base is not reliably current master — make every dependent task verify it and
  say what to do about it.** *(New, S143.)* Worktrees spawned before a merge sit on the pre-merge
  commit, and the base is not guaranteed to be current HEAD even for a later spawn: this sprint had
  TASK-14311 correctly land on `41c43bb` while TASK-14301, dispatched afterwards, landed on `0a001b4`.
  That matters whenever a task consumes something a predecessor produced — 14301 needs the generated
  `api-types.ts` that 14300's merge created, and without it nothing it writes would compile.

  The dispatch instruction that caught it: state the required base sha, tell the agent to verify with
  `git log --oneline -1`, **and give it a second check on the artifact itself** (here,
  `grep -c "calendar/today" frontend/src/lib/api-types.ts`) — a sha comparison alone is easy to get
  right and still miss the point. Tell it to fast-forward (`git merge --ff-only master`, safe while the
  branch has no commits of its own) and only stop if that fails. The first dispatch stopped and asked,
  which was correct but cost a round trip; the instruction now resolves it without one.

## Task ledger

Dispositions: DONE | CUT | DEFERRED | DROPPED. **All sixteen DONE; none cut.** Two were added
mid-sprint by review findings (14311, 14313) and one at close (14312) — see each row.

| Task | Disposition | Note |
|---|---|---|
| TASK-14300 | DONE | `GET /api/calendar/today`, born typed. RED demonstrated by substituting each wrong implementation |
| TASK-14301 | DONE | The calendar context and the `RequireAuth` shell gate. Step 5a found a measured boot loop (26 reads / 25 reloads) and a background reload destroying unsaved work; both fixed and pinned |
| TASK-14302 | DONE | The frontend's first clock guard, plus the sidecar mechanism that let it land green ahead of the migrations. Three review rounds, each finding a spelling the last had not imagined |
| TASK-14303 | DONE | Skema: the seed, the highlight, and the AC-5 combined assertion |
| TASK-14304 | DONE | The approver's team view and the year overview |
| TASK-14305 | DONE | The e2e cluster — and the click count that was a date bug in disguise |
| TASK-14306a | DONE | 29 C# hygiene sites (not the census's 30 — one cited location was a *use* of an already-computed field) |
| TASK-14306b | DONE | The frontend hygiene pair, and a coverage gap left honest rather than papered over |
| TASK-14307 | DONE | QUAL-176, in two halves: the clock source, then the read *count* that Step 5a found still open |
| TASK-14308 | DONE | QUAL-177 / OQ-4 — `CreateAsync` the single create path. Step 5a caught an `-infinity` sentinel a `DateOnly` round trip was hiding |
| TASK-14309 | DONE | The worktree-teardown gate, owed since S141, proven four ways |
| TASK-14310 | DONE | The two acceptance criteria Step 0b found with no owner; both proofs verified by mutation |
| TASK-14311 | DONE | **Added mid-sprint by a Step 5a finding.** `CopenhagenBusinessDate.FromInstant(DateTimeOffset)` — named against the Orchestrator's lean, on the implementer's argument that ADR-041's vocabulary belongs in the signature. `Today(TimeProvider)` delegates to it |
| TASK-14312 | DONE | **Close task.** Retired the guard's allowlist once all four sidecars were empty, keeping every reach assertion and all 21 spelling tests. Proven by mutation, both observed |
| TASK-14313 | DONE | **Added at close by a Step 7a BLOCKER.** Six sites still took a *stored* business date from the device clock through the approved helper. See "the claim that outran the code" below |
| *(Orchestrator)* | DONE | Two stale comments citing a column dropped in S53; ADR-042; ADR-041 and ROADMAP corrections; QUAL-178/179/180 |

### The claim that outran the code — S143's own signature defect, committed by S143

This sprint spent itself finding comments that asserted more than the code supported, and corrected five
in one task alone. **Then it shipped three governance documents claiming no frontend production source
read the browser clock for a business date, and that was false.**

Six sites called bare `copenhagenToday()`, whose default argument is an executable `new Date()`. Right
calendar, wrong authority — the device in front of the user rather than the day the product agreed on.
Every one of them supplied a date that is **stored**: a profile edit's `effective_from` (S142's own
headline defect site), an approver assignment, an org reassignment, a delegation floor, a validation
boundary. All six rendered inside the gate, so the seam had been available to them throughout.

**Why no guard caught it, which is worth more than the fix.** They go through the approved helper, and
the helper *is* the approved answer — **for zone**. The guard enforces a *spelling*; ADR-042 claims a
*property*. A correctly-spelled call through a sanctioned path still reads the device. **A guard can
police syntax; it cannot police which clock a call ultimately reaches.**

**And the count was wrong twice on the way to being right.** The external lens said three sites. The
Orchestrator repeated that three without re-deriving it — the exact "enumerated beats matching" lesson
recorded in this same log — and the internal lens found five, citing that line back. The implementer
then found a sixth while migrating. **3 → 5 → 6.** A count arrived at by inspection is a hypothesis.

| Task | Disposition | Note |
|---|---|---|
| TASK-14300 | DONE | `GET /api/calendar/today`. **RED demonstrated empirically, not asserted**: each of the two wrong implementations was substituted and run — "add 24 hours" failed 5/14, "reuse today's offset" 3/14, shipped 14/14. Unit 1264 → 1278. Spec + FE types regenerated, idempotent; grandfather manifests untouched. 4 deviations, all accepted |
| TASK-14302 | DONE | Guard + four sidecars, 30 guard tests, frontend 894 → 924. Step 5a took **two cycles, each finding a spelling the previous had not imagined**. RED proof re-recorded each round and produced exactly the same seven sites every time |

### TASK-14302 — what three rounds of a syntactic guard actually taught

| Round | Spellings it could not see |
|---|---|
| 1 (as written) | `new Date` with no parentheses; `new Date ()` spaced |
| 2 (review) | `new (Date)()` — a parenthesised callee is a different node kind, so the identifier check silently failed |
| 3 (review) | `new (0, Date)()`, `new (Date as any)()`, `new (<any>Date)()`, `new (Date satisfies any)()`, `new (Date!)()` |

Every round was found by **executing** the parser against candidate spellings, never by reading it.
The fix that ended the sequence was not a sixth patch: it is a loop over every *transparent wrapper*
node kind, iterating until a pass changes nothing, so combinations compose
(`new ((Date as any)!)()` unwraps correctly). One committed test per spelling — the test list is as
much the deliverable as the loop.

**This is the same trajectory the C# guard took in S142 (five Step-7a passes), which is the point.**
A syntactic matcher faces an adversary holding the whole grammar, and enumeration always trails it.
The defence is not cleverness but structure: match on what the code *is* (a `NewExpression` on the
`Date` identifier with no arguments) after removing everything that wraps it without changing what it
means.

**Its honest limits, stated because a guard nobody bounds gets over-trusted.** It cannot see aliasing
(`const D = Date; new D()`) or member access (`new (globalThis.Date)()`) — the first needs type
analysis, and the second is a genuinely different expression that could not be flagged without false
positives on any `x.Date`. **This guard is for accidents, not evasion**, and that is the right scope:
nobody writes `const D = Date` by mistake, while everybody writes `new Date()` by habit.

**The fixture bug worth remembering.** One of its two new hazard tests did not test what it claimed:
the explanatory prose *inside* the template literal — "no `${}` hole at all" — was itself a parse
error, creating the very hole it described the absence of. The words broke the example.
| TASK-14307 | DONE | QUAL-176 closed in both halves. See "the flip nobody witnessed" below |
| *(14307 history)* | Step 5a absorbed, then held for 14311 | Four stamps moved; the other four demonstrably untouched; six "BY DESIGN" comments reconciled into two named rules (A mandatory, B permitted). Window reduced from "spans a blocking lock" to "two adjacent statements" — **explicitly not closed**, carried as a `⚠ KNOWN RESIDUAL` block until the overload lands. 5 Docker-gated facts, **one deliberately RED**: the single-read pin was written now rather than after the fix, so it is honestly RED-first. Build 145 warnings, unmoved |
| TASK-14308 | DONE | Post-merge verification on master: **145 warnings / 0 errors / 1264 unit tests** — the S142 baseline exactly, unmoved. Worktree torn down after proving the branch merged. |
| *(14308 detail)* | Step 5a **PASSED**, 2 cycles | Single write path. Cycle 1: 1 BLOCKER (external), APPROVED-WITH-WARNINGS (internal). Cycle 2 on the fix: **no blockers, no warnings**. Build 145 warnings, baseline unmoved; Unit 1264; regression non-Docker 128; net test delta **+3**. Docker-gated CI-verified throughout |
| TASK-14309 | DONE | Orchestrator; gate built and proven — see below |
| TASK-14306a | DONE | **29** sites across 13 files — not the census's 30; see the recount below. Step 5a found one BLOCKER. Unit 1264, DemoSeed 170, regression non-Docker 128 |

### TASK-14306a — the blocker nobody could have run, and a recount worth reading

**The blocker.** The sweep pinned a PUT's `effectiveFrom` to a literal. Correct at the site: the value
genuinely is not asserted, exactly as the census said. But it still *participates* — the three
fresh-user tests in that suite create their user via a POST that stamps the profile from the **real**
clock. A literal that predates that stamp turns a same-day edit into a **backdate correction**, which
422s on a user with no employment-category history. Three tests the sweep never touched would have
broken.

**And every file in that sweep is Docker-gated**, so it would have gone red in CI and nowhere else —
after merge, on a machine that cannot reproduce it. That is the gap between "I ran everything I could"
and "this is correct", and it is the entire argument for a review pass between the work and the merge.

The general hazard, worth carrying forward: **converting one side of a comparison to a literal changes
which branch the code takes.** The site looks inert; the comparison is not. The implementer then
re-checked all twelve remaining rows against that question by tracing each seed helper to its source,
and found no other instance.

**The recount.** Reported first as "30 sites, matching the census exactly"; the diff contains 29. The
census cited `EmploymentEndDateCorrectionGuardTests.cs:49-51,140-142` as two locations, but only `:51`
is a clock read — `:142` is `FerieaarOf(TodayUtc)`, a *use* of the already-computed field. The
implementer's own account: *"I'd copied the census's 2 without re-deriving it from the code, which is
the exact mistake the review was warning against."*

The overclaim mattered more than the site did. "Matching the census exactly" is far stronger evidence
than a plausible-sounding total, so it is the part a reader leans on — and this project has been bitten
by a summed-rather-than-enumerated count before, including by the Orchestrator during this sprint's own
planning. **An enumerated count is worth more than a matching one.**
| TASK-14311 | DONE | **Added mid-sprint by a Step-5a finding** — `CopenhagenBusinessDate.FromInstant(DateTimeOffset)`, so a caller holding an instant can derive its Copenhagen day without a second clock read. See the QUAL-176 note below |
| TASK-14301 | DONE | Unblocked by 14300's merge — the calendar context, the `RequireAuth` shell gate, the refresh, and `renderWithCalendar` for the sibling tasks |
| TASK-14303, TASK-14304, TASK-14305, TASK-14306b, TASK-14310 | DONE | all merged; see the ledger above |

### TASK-14307 — the flip nobody witnessed, and a regression caught inside a simplification

**The Orchestrator asked for a claim the agent could not support, and the agent declined it.** The
single-read pin had been written *before* the fix, deliberately, so it was RED by construction. When
the overload landed, the instruction was: "confirm it flips, and say so." The reply:

> *"I have observed neither the RED nor the GREEN — Docker is unavailable here, so the flip is
> reasoned from the handler source and CI is where it is actually witnessed. I will not report a flip
> I did not see."*

That is the correct answer and the instruction was the flawed part. The strongest honest statement is
the one it gave instead: the fact was authored against the two-read code, its RED condition is stated
in the test, and the code that condition discriminates against no longer exists. **A red-then-green
sequence is powerful evidence only when someone watched both halves.** On this machine, where every
Docker-gated pin is written blind, the temptation to narrate a flip from reasoning is constant — and
it would make the sprint's own test reports the kind of artifact that says more than it knows.

**And a silent regression caught inside a tidy-up.** Collapsing the GET's hand-inlined conversion onto
`FromInstant`, the obvious simplification would have dropped a `SpecifyKind` that looks redundant. It
is not: Npgsql returns `TIMESTAMPTZ` as a UTC-kind `DateTime`, and stating the kind explicitly stops
the `DateTimeOffset` constructor applying the **host's local offset**. Dropping it while simplifying
would have silently reintroduced the QUAL-005 class — a wrong offset, no error. The kind moved into
the argument rather than disappearing, and the reason is recorded at the site.

### A frozen clock cannot prove a single-read property — three tasks learned this independently

Worth recording as the sprint's second transferable finding, because it arrived three separate times
from three different directions and nobody was looking for it:

- **TASK-14300** claimed its captured-instant wrapper guaranteed one clock read. Every provider in its
  suite was frozen, so an implementation reading twice passed all 14 tests. Measured: the two-read
  implementation failed **0 of the original 14**.
- **TASK-14308** claimed a fact guarded the S137 "one date for the whole create" ruling. Under a pinned
  clock, two reads return the same day, so three-way equality held either way. The pre-existing S137
  pin has the same limitation.
- **TASK-14307** hit it twice — its three frozen-clock facts structurally could not see that QUAL-176's
  window survived, and the Orchestrator then proposed a migrator test with the identical blind spot
  one message after describing it.

The instrument in each case is a clock that *moves*: a counting provider, a stepping provider, a
day-per-call provider. Which one depends on what else shares it — 14307's advances a full day per call
precisely because background services consume reads first, so a smaller step would make the outcome
depend on call index; and it needed `Interlocked` because that provider is host-wide and read from
other threads, where a lost increment would hand two callers the same instant and quietly restore the
blind spot.

**The general form:** a test fixture that holds a value still cannot distinguish "read once" from
"read many". If the property under test is *how often* something is consulted, the fixture must change
between consultations.

### TASK-14311 — a task the plan did not contain, and why that is correct

Step 5a found that TASK-14307's fix was **incomplete in a way its own tests could not show**. It moved
`created_at` onto the injected clock — the source — but left the operation reading that clock **twice**
for one logical date, with a blocking advisory lock and half a dozen queries between the reads. So the
23:59:59 window QUAL-176 describes survived the fix. **PAT-028 names this exact case in its Agent
Guidance:** *"Before converting a clock read to the seam, count the reads in the operation. If there
are two or more for the same business date, the fix is 'compute once and pass', not 'convert each'."*

No frozen-clock test can see it, which is why it reached review rather than CI: a `FixedTimeProvider`
returns the same value on both reads.

The implementer identified an in-scope workaround — wrap the captured instant in a throwaway
`TimeProvider` — and **declined to ship it**, on the grounds that a production `TimeProvider` subclass
existing only to compensate for a missing overload is a pattern others copy instead of asking for the
overload. That judgement was upheld: this sprint already contains one such wrapper (TASK-14300's
`CapturedInstantTimeProvider`, accepted because no alternative existed at the time), and a second would
have made it a convention. The overload also collapses a **third** site —
`ReportingLineEndpoints.cs:1816-1820` hand-inlines the same conversion today.

**Adding a task mid-sprint is scope discipline, not scope creep, when the review shows a committed fix
is unfinished.** The alternative was closing QUAL-176 as done while the defect it names remained
reachable.

### TASK-14306a — the sweep found the sprint's theme again, unprompted

Two of the thirty rows needed more than a literal, and both for the same reason: the code they test had
moved and the test's own documentation had not.

- `TerminationSettlementTests.cs` — a bare literal would have been overtaken by the real clock, because
  the resolved `VacationSettlementService` reads "today" from its own injected provider. Needed a
  derived fixed host, not a constant.
- `VacationSettlementEndpointTests.cs` — **the file's doc comment claimed the §21 deadline check reads
  a "private, non-injectable" clock.** It has read `CopenhagenBusinessDate.Today(timeProvider)` since
  S141. The suite was rewired onto a fixed host so the dates the test sends and the deadline the server
  checks are provably the same instant rather than coincidentally in agreement.

### TASK-14308 — declared deviations, Orchestrator ruling

The agent declared five. Rulings on four; the fifth waits for Step 5a because it touches an audit column
and auditability is an inviolable invariant — an Orchestrator does not wave that through on a report.

| # | Deviation | Ruling |
|---|---|---|
| 1 | `SeedAsync` gained an `EmployeeProfileRepository` parameter; `Program.cs` edited to supply it from DI | **ACCEPTED.** In scope, and the reasoning is right: constructing the repository locally would re-create the hidden second clock seam this task exists to remove |
| 2 | `version_after` in two audit rows changed from a literal `1` to `CreateAsync`'s returned version | **HELD pending Step 5a.** The value is unchanged today and recording what was actually written is better practice than asserting a constant — but this is an audit column, and the invariant is not tradeable against tidiness |
| 3 | The S142 pin rewritten in place and renamed rather than moved; its fixture narrowed | **ACCEPTED.** The task asked for the pin to move onto a path production executes; this is that, and renaming it to name the path it now drives is an improvement |
| 4 | A new test class means one extra Docker container start in CI | **ACCEPTED.** A class named for the thing under test is the honest home; folding a seeder assertion into a class about the admin create would save a container and cost legibility |
| 5 | **Not fixed, flagged:** `EmployeeProfileSeeder`'s class doc and `Program.cs:498` both still cite a `weekly_norm_hours = 37.0` default for a column **dropped in S53** | **ACCEPTED as a finding, and it is the same species as the comment the task was told to fix.** Registered rather than swept into this task's diff. Orchestrator fixes it at integration |

### TASK-14300 — declared deviations, all ACCEPTED

| # | Deviation | Ruling |
|---|---|---|
| 1 | The two DST pins live in the **Unit** suite, which required making `ComputeServerDay` public | **ACCEPTED, and it is the right call.** Docker is unavailable on this machine, so a Docker-only pin would have shipped as an assertion nobody had ever run. Putting the discriminating arithmetic in the always-runnable tier is what made the RED demonstration possible at all. Both DST days are *also* pinned end-to-end in the Docker class, so nothing is lost |
| 2 | New `S143CalendarSpecRuntimeTests` class rather than extending `OpenApiSpecRuntimeTests` | **ACCEPTED.** Twenty-plus per-family `S1xx…SpecRuntimeTests` classes are the established pattern, and the existing class carries a heavy seed fixture this endpoint has no use for |
| 3 | Two extra facts pinning the access decision (401 without a token, 200 with an Employee token) | **ACCEPTED.** The access posture was *ruled*, not derived, and an unpinned ruling is one careless copy-paste from a neighbouring admin file away from reversal |
| 4 | `npm ci` run inside the worktree's `frontend/` to produce the generated types | **ACCEPTED.** `node_modules` is gitignored and the tree shows only the seven intended paths |

**Two design choices the brief did not ask for and should have.** The agent froze the clock read: one
`GetUtcNow()` wrapped in a private captured-instant provider, so the day and the duration cannot come
from two reads straddling midnight and produce a zero-or-negative delay on the wire. And it rounds the
duration **up**, so a client timer cannot fire a fraction of a second early, re-read the day it already
had, and reschedule against a near-zero delay. Both are defects the specification would have allowed.

### TASK-14307 — declared deviations, all ACCEPTED

`ReportingLineRepository` now passes its own clock down to the derived vikar repository (constructing
it with the factory alone would have handed it a fresh `TimeProvider.System` — a caller pinning one
clock would silently have been running two, which is the finding the task removes); the six "BY DESIGN"
comments became one class-level rule plus six site-specific lines rather than six copies; the delegation
GET is annotated as *the* date-deriving reader that makes the rule's YES-branch true; three facts
instead of one, because the winter instant kills a hardcoded `+02:00` the marquee passes and the third
covers the second writer. **Registered, not fixed:** `ManagerVikar.cs:28` defaults `CreatedAt` to
`DateTime.UtcNow` — a model default that is itself a real-clock read. SharedKernel is data-model scope
and making the property `required` would break every object initializer that omits it, so this is a
follow-up rather than a two-line fix.

### Environment fact recorded for later sprints

**Python is not installed on this machine** — `python`/`python3` resolve to the Microsoft Store shim
and fail. So `tools/check_docs.py`, `check_openapi_convention.py` and `check_openapi_sync.py` cannot be
run locally at all; they are CI-only, exactly like the Docker-gated suites. Worth knowing before a
future sprint plans a local doc-gate check it cannot perform.

### TASK-14308 — the Step-5a blocker, and two places the Orchestrator was wrong

**The blocker.** Replacing the seeder's reliance on the SQL schema default with a `DateOnly` parameter
looked value-identical and was not: Npgsql 8 maps `DateOnly.MinValue` to Postgres `DATE '-infinity'`,
so every seeded row would silently have stopped holding the finite date it held before. The pin could
not see it, because the read-back converts `-infinity` **back** to `DateOnly.MinValue` — a lossless-
looking round trip over changed data, which is the exact shape this sprint exists to remove. The
project had already paid for this once: `UserAgreementCodeBackfillSeederTests.cs:74-79` carries the
finding from S64 on the sibling seeder.

Fixed by binding the anchor as text with an explicit `::date` cast, so no client-side sentinel is in
the path. The implementer rejected two tempting alternatives with reasons worth keeping: the global
`DisableDateTimeInfinityConversions` switch (repo-wide semantics change, and a sibling's tests now
*assert* the `-infinity` behaviour) and a conditional bind-as-text-only-when-`MinValue` branch
("a branch that exists for one caller is what rots").

**Two corrections the implementer made to the Orchestrator, both upheld:**

1. The Orchestrator's supporting argument — "this codebase keys real routing on the `0001-01-01`
   anchor", citing `AdminEndpoints.cs:1075-1077, :1150, :1271, :2091` — **was wrong**. Those are
   comments and doc; there is no executable SQL in `src/` comparing that column to the finite literal.
   The blocker stood on two better grounds the implementer supplied instead: row/event parity (the row
   would say `-infinity` while the event said `0001-01-01`, since `System.Text.Json` has no such case)
   and the test corpus, which seeds the finite value (`YearOverviewTests.cs:2321`,
   `PayrollCalcAuditSmokeTests.cs:181`).
2. The Orchestrator proposed counting profiles as "in a fresh DB every profile row is a seeder row".
   The implementer used the seeder's **own selection predicate** (`users WHERE is_active = TRUE`)
   instead, because the suggested denominator cannot detect a *missing* profile and is order-coupled
   to the other fact not yet having created its admin user. Stronger; adopted.

**And a pin the Orchestrator said could not exist.** Told that a frozen clock cannot detect a second
clock read and to narrow the claim, the implementer narrowed it *and* built
`AdminUserCreate_ReadsTheClockOnce_AllDatedCellsAgree` on a provider returning one day later per read,
asserting only that the four dated cells **agree** — never which day they name. One read and they
agree by construction; two and they cannot. The day-per-read step is what makes it robust to startup
seeders consuming an unpredictable number of reads. Its residual risk (an unrelated CI flake) was
retired by verifying that `Microsoft.IdentityModel` validates token lifetime against `DateTime.UtcNow`
rather than the injected provider.

**Worth recording, because it is the sprint's theme arriving unprompted:** the agent found that
`CreateAsync`'s own comment claimed the row used the `0001-01-01` schema default while the code had
stamped *today* since S33 — and that the same false sentence had been copy-pasted into
`AdminEndpoints.cs`, twenty lines above a ruling that contradicted it. Nobody asked it to look.

### TASK-14309 — the worktree-teardown gate (DONE, 2026-09-23)

Owed since S141 and now mechanical. `.claude/hooks/sprint-close-guard.ps1` blocks a sprint-close
commit while any worktree beyond the main checkout survives, enumerating them via
`git worktree list --porcelain` and dropping exactly the first entry (the main checkout is always
first). Fail-open on git errors, per the hook's convention; fail-closed only on a real enumerated
leftover. Waiver: `.claude/reviews/SPRINT-{N}-worktree-WAIVED.md`.

**Proven to fire — four cases, all run against the real hook:**

| Case | Result |
|---|---|
| Two leftover worktrees | **BLOCKS**, exit 2, lists both with remediation |
| Clean | **Passes this gate** and falls through to the next (task-ledger) — so it is not blocking unconditionally |
| Waiver present, tree dirty | Gate **skipped**, reason printed |
| **Non-S99 sprint with `STATSTID_WORKTREE_MOCK` set** | Mock **ignored**, real `git` used — it enumerated the four live agent worktrees running at that moment. A leaked env var cannot blind a real close |

The fourth case is the one that matters: every other seam in this hook carries the same S99-only
hardening, and this gate now does too. Written because the hook's own history records why —
S142 closed with 24 worktrees standing, found only because `git status` had slowed to 0.47s, and
S141 had already named the teardown as owed without gating it. Work that is named but not gated does
not get done; this one cost two sprints before becoming a gate.

## Appendix — the Category-3 hygiene rows (14306a / 14306b), enumerated

Tracked here rather than only in an agent prompt: the census is gitignored and a prompt is ephemeral,
so the sprint log is the record. Enumerated row by row and counted per row — **not** by summing the
census's headline.

**The census says "~32". It overcounts: three of its S143 rows were closed by S142 itself.** This is
the method rule biting a second time, and it is why the table below carries a status column instead
of being copied across.

| Already closed — do **not** dispatch | Evidence |
|---|---|
| `HrFollowUp/EffectiveDateBoundaryTests.cs:476` (harness hygiene, census Q5) | `HostAtInstant` is now a one-line delegation to the **public** `StatsTidWebApplicationFactory.WithFixedInstant` (`EffectiveDateBoundaryTests.cs:581-582`, factory at `:283`). S142's harness task (14200) promoted it; the census predates that |
| `Config/AdminEndpointsAgreementCodeTests.cs:300-339` (census UNRESOLVED-1) | Traced NON-GATING at S142 Step 0b (`SPRINT-142.md:318-320`) — the OQ-6 comparison uses the truncating row's boundary, never the client date |
| `ReportingLine/ReportingLineWriteLifecycleTests.cs:1499` + `Security/S98OrgStructureTests.cs:560` (census UNRESOLVED-2) | Traced NON-GATING **and misassigned** at S142 Step 0b (`SPRINT-142.md:321-323`) — the org-transfer fan-out only stamps, never compares |

**14306a — C# tier (30 sites across 11 files).** Every row: replace the real-clock read with a fixed
anchor or a literal; none is wrong today, all are undisciplined.

| File:line | Shape | Sites |
|---|---|---|
| `Settlement/EmploymentEndDateCorrectionGuardTests.cs:49-51,140-142` | `DateOnly.FromDateTime(DateTime.UtcNow)` ±2-year band. Its siblings (`EmploymentDateGuardTests`, `EmploymentEndDateLifecycleTests`) were converted to a fixed `F` anchor in S140; this file's own comment names it as the one left | 2 |
| `PhaseE/AuditProjectionCatalogCloseTests.cs:119,152,183,253,280,310` | `EffectiveFrom` placeholder on synthetic DTOs, never asserted | 6 |
| `PhaseE/AuditProjectionCutoverTests.cs:362,397` | same | 2 |
| `PhaseE/AuditProjectionFamilyCutoverTests.cs:111,112,140,141,169,219,220,245,246,271` | same | 10 |
| `Approval/SelfApprovalGuardTests.cs:365` | real-clock `asOf:` against a non-expiring relationship | 1 |
| `Security/UnitFoundationTests.cs:353` | same shape, topological fact | 1 |
| `Config/EmployeeProfileEndpointTests.cs:518` | real-clock `effectiveFrom`, 7 callers, none asserting it | 1 |
| `Settlement/TerminationSettlementTests.cs:517` | 2-year margin | 1 |
| `Settlement/VacationSettlementEndpointTests.cs:64` | structurally immune (Copenhagen is never behind UTC) — fixed anyway, for discipline | 1 |
| Payroll Marquee tests ×2 | 4.5-month margin | 2 |
| `TxContractTests.cs:762`, `TemporalWriteZeroWidthReopenTests.cs:69,126` | self-threaded — no independent recompute, so no oracle | 3 |

**14306b — frontend tier (2 sites).** `DelegationPage.test.tsx:50-54,144,165` (+7-day buffer) and
`MondayDatePicker.test.tsx:43-58` (a coverage gap rather than a defect — the test never dates
anything near today, so it could not catch a regression either way; note what it cannot see rather
than inventing an assertion).

**Assigned to 14303, not 14306:** `SkemaGrid.test.tsx:446-447` —
`vi.setSystemTime(new Date('2026-03-11T09:00:00'))` with **no `Z`**, so it parses in local time
unlike its three sibling pins. Verified still open at HEAD. It lives in a file 14303 rewrites, and
two worktrees in one file is the S142 merge hazard.

**Assumption 4 check, now possible because the rows are here rather than in a prompt:** every row
above is a test that reads a real clock where a fixed one would be better. None asserts a date that
could flip. So the assumption holds — CI stays green if this task were cut — and it is verifiable
against this table rather than taken on trust.
