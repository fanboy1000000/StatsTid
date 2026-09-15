# Sprint 141 — Increment 4 (employment lifecycle UX) + the settlement anchor

| Field | Value |
|-------|-------|
| **Sprint** | 141 |
| **Status** | in progress — wave 1 merged and gated, Step 5a absorbed; wave 2 next |
| **Start Date** | 2026-09-11 |
| **End Date** | — |
| **Orchestrator Approved** | **plan: APPROVED — Step 0b ran THREE cycles** (Codex 1B/3W/2N → 2B/1W → clean; Reviewer 1B/7W/6N → 2B/5W/6N → APPROVED-WITH-WARNINGS). Four blockers, two of which would have shipped visibly broken features: the date picker refusing every date it offered, and the edit drawer rejecting every save. Refinement `.claude/refinements/REFINEMENT-s141-increment4-and-the-settlement-anchor.md` **rev 5**, READY. Step-4 ran **three** dual-lens cycles: Codex c1 1B/4W/8N → c2 1W/1N → c3 **1B**/1W/5N; Reviewer c1 0B/9W/7N → c2 **1B**/3W/1N → c3 APPROVED-WITH-WARNINGS 0B/5W/5N. Both lenses BLOCKER-free at c3; the skill's two-cycle cap stopped it there and the remaining WARNINGs were absorbed rather than deferred. A post-rev-4 **independent write-path enumeration** then found four more items and, more usefully, the two *mechanisms* generating them. Owner rulings 2026-09-11: **OQ-1 (a)** A+B+C, Increment 4 whole, with a pre-declared cut order · **OQ-2 (a)** anchor at `max(ferieårStart, hire)`, OK-version included, SPECIAL_HOLIDAY in the same ruling · **OQ-3 (a)** profile concurrency token moves to `users.version` · **OQ-4 DEFERRED** to `ROADMAP.md` as a decision owed, trace required first · **OQ-5 (a)** delete both, ruled *against* the Orchestrator's recommendation · **OQ-6 (a)** a today-dated edit asks which period HR meant · **B0** owner-raised requirement: a scheduled change must be VISIBLE wherever a profile is read or edited |
| **Build Verified** | — |
| **Test Verified** | — |
| **Orchestrator model** | Refinement revs 1–5, Steps 0a/0b, this log, all rulings: **Opus 5**. **Disclosed deviation:** the routing rule reserves planning and rulings for the review floor (Fable 5.1); OQ-2 was a domain-correctness ruling taken on Opus. The Step-4 and Step-0b *reviews* run on the floor (hook-enforced), so the review is unaffected; the proposal was not. Recorded rather than quietly absorbed |
| **Sprint-start commit** | `050fa00` (S140 CI-green backfill) — the `codex review --base` anchor for Step 7a |
| **CI at start** | **green** — run `34469581974`, sha `050fa00`, all jobs |

## Step 0a — entropy scan

| Check | Result |
|-------|--------|
| Working tree | clean at scan time (only this sprint's `ROADMAP.md` edit) |
| Untracked source | none under `src/`, `frontend/src/`, `tests/` |
| CI health | green on HEAD; the last red (`34366179123`) was S140's first close run, both causes fixed |
| `tools/check_docs.py` | **NOT RUN — cannot run.** Python is absent from this machine (verified, not assumed: `python`, `python3` and `py` all resolve to nothing). The docs gate, the db-schema generator and the design-sync checker are **CI-only this sprint** |
| **Stale agent worktrees** | **★ FINDING — 8 worktrees, 1.5 GB, all merged into master.** See below |

**The worktree finding, and the correction that produced it.** Rev 1 of the refinement asserted "entropy scan clean". That was
false, and I wrote it without checking the one thing S140 changed about how agents run. S140 made `isolation: "worktree"`
mandatory for concurrent agents; eight of those worktrees survive on disk, each a full repo copy including build output. **All
eight commits are merged into master, so no agent work is at risk** — this is waste and noise, not loss. The noise is not
harmless: a repo-wide filename search now returns **nine copies** of every source file, so any agent or scout searching by name
rather than by path gets eight decoys, one of them a stale `bin/Release` tree. It already misled a search in this session.
**Not cleaned during planning** — removing worktrees is a delete, one of the eight is `locked`, and it was outside what the
planning step was asked to do. **Action: teardown at Step 7**, and the "auto-cleaned if unchanged" default is not to be trusted,
since it demonstrably fired for none of the eight.

## Sprint Goal

Two deliverables, the second of which grew by a factor of nine under review and is the reason this plan looks the way it does.

**Part A — the settlement anchor.** An employee hired part-way through a holiday year cannot have that year settled. The system
asks "which agreement, which agreement-version, which position governed this person?" about the *start of the holiday year*,
which for a May hire is a date before they were employed — so the dated read misses and the capture **throws**. Owner ruling
OQ-2 (a): anchor every such read at `max(ferieårStart, employmentStart)`, **OK-version included**, so every read answers one
coherent question — *what was true on this person's first accruing day of that year*. The decisive argument came from the
internal lens: the version also **keys the entitlement-config read**, so anchoring data at the hire while leaving the version at
the year start asks for a `(version, date)` pair that never coexisted, and resolves today only because the seeded config rows are
open-ended. The same ruling governs SPECIAL_HOLIDAY, whose parallel capture has the same defect but **degrades silently** through
fallbacks instead of throwing — and whose snapshot really does carry a payout wage-type key, contrary to a code comment saying it
does not. QUAL-168 rides along: one expression gives the candidate-year lower bound the reset-month mapping the upper bound
already has, so a January–August hire's first ferieår is finally enumerated.

**Part B — the precondition for scheduling a change ahead, which is now nine items.** ADR-040's Increment 4 needs HR to be able
to date a change in the future. Today every write dated after today is refused, and that refusal is load-bearing in a way nobody
had written down: **it is the only reason "the open row" and "the row covering today" are the same row.** Removing it breaks
everything that quietly relied on that coincidence. Four review cycles and one independent enumeration found nine consequences,
and the enumeration's real contribution was showing they are not nine discoveries but **two mechanisms**:

1. **The split case hands a new row the *next* row's start, and that used to be infinity.** A value read off the future row gets
   written forward into today (B6), and a value written today gets truncated at the future row (B7).
2. **Nothing in the system is triggered by an effective date arriving.** Every cache refresh and self-heal fires on a *write*, so
   a scheduled change silently stops being reflected on the day it takes effect (B2), and the "no row covers today" state has no
   detector at all (B8).

**B0, the owner's requirement, is the through-line.** Asked *"Should it not be visible to an HR employee looking at a page, that
another has scheduled a change?"* — and that question reframed B6 and B7 from two defects with two fixes into **one problem**:
the screen shows a value without saying which period it belongs to or that another period exists. **No review lens asked it.**
Visibility is therefore a first-class requirement across the feature, not an option inside a ruling, and it demoted the
concurrency-token question (OQ-3) from a product decision to plumbing.

**Part C — the Increment-4 surfaces:** the termination screen, the effective-date picker, and the HR-gated history timeline.

## Cut order (pre-declared, per OQ-1)

**Cut in this order if the sprint slips: C2 (history timeline, TASK-14109) → C1 (termination screen, TASK-14108) → the date
picker (TASK-14111).**
**B0–B9 are NOT cuttable**, because they are correctness fixes to defects that future-dating *introduces*; shipping the picker
without them ships the bug. Increment 4 has slipped twice, so declaring the order now makes a slip a **named partial** rather
than a third silent slip.

**What each cut actually leaves, stated so a partial is legible rather than just smaller:**

| Cut to here | What HR gains | What is true underneath |
|---|---|---|
| Nothing cut | Terminate, schedule ahead, view history | Increment 4 complete |
| C2 cut | Terminate, schedule ahead | No history view; every write still correct and visible |
| C2 + C1 cut | Schedule ahead | Termination stays on today's screens |
| C2 + C1 + picker cut | **Nothing new to click** | The guard is lifted, all nine consequences are fixed, and any scheduled change created by any path **is visible**. Nothing is silently wrong |

**The bottom row has an honest name, supplied at Step 0b [W-3]: "Increment 4's precondition shipped; the feature did not."**
It is a coherent and shippable state, and it is genuinely worth shipping, because the nine defects it fixes are real whether or
not a picker exists. But the review is right that it is hollower than the first draft admitted: **the picker is the only path
through the product that creates a scheduled change**, so with it cut, the visibility work and the edit prompt have nothing to
display and cannot be exercised end-to-end. They would be correct and dormant, which is the same condition as the quality
finding this sprint is fixing precisely because dormancy hid it for four sprints. **So cutting the picker is the sprint failing
to deliver, and the plan should say so in those words rather than presenting the floor as a success.**

**A cut also trims its own verification** [Codex NOTE]. Cutting C1 or C2 removes their E2E obligations from TASK-14110 and their
route registrations from the lazy-route guard. A cut that leaves the E2E for a screen that was never built turns a deliberate
partial into a red close run, which is exactly the confusion the pre-declared order exists to prevent.

## Task decomposition

**Decomposition is by FILE OWNERSHIP, not by finding.** Several findings land in the same files — `EmployeeProfileRepository.cs`
alone carries B1, B3, B4 and B5 — and S140's lesson was that worktrees prevent *build* races but do nothing about *merge*
conflicts. Grouping by finding would have produced four agents fighting over one file.

### Wave 1 — parallel, file-disjoint

| Task | Agent | Scope | Owns |
|------|-------|-------|------|
| **TASK-14101** | backend-infrastructure | **Part A**: anchor sites 1–4 at `max(ferieårStart, hire)` + per-probe `max(probeAnchor, hire)` for site 5; SPECIAL_HOLIDAY anchored *and* its snapshot key made fail-closed; QUAL-168's lower-bound reset-month mapping. Fix the `:1488` comment that conflates payout-key correctness with replay determinism. **Also owns the single B1 site inside this file, `:1359-1368`, by line range** — the call change, the nine-line comment rewrite, **and the clock decision**: `todayAgreementCode` keys the live-config read at `:1385` and the probe skip at `:1426`, so it must use the **writers' UTC day**, agreeing with what the caches would say rather than the service's Copenhagen helper. Also update the D9-parity comment at `:1402-1412`, which stops holding verbatim after the anchor change | `VacationSettlementService.cs` (capture paths), `SettlementCloseService.cs` |
| **TASK-14102** | backend-infrastructure | **B-core, the timeline data layer**: B1 as-of-today census **excluding `VacationSettlementService.cs`**; B3 lift the future-date refusal at **all four** sites and update the router rationale; B4 delete per OQ-5 (a), repository side; B5 token move to `users.version`, repository side; B2a record the absence of an employment-end ceiling; **★ B0's READ SIDE — a "next scheduled row after today" read on BOTH repositories, returned alongside the as-of-today row** [cycle-3, section 5: this had no owner at all, and without it TASK-14104 in wave 2 must either breach scope into a sibling's file or block. Same agent, same files, same worktree — free now, a collision later]; **sweep `FutureDated` in owned files** for comments and the two constants that go dead | `EmployeeProfileRepository.cs`, `UserAgreementCodeRepository.cs`, `TemporalWriteRouter.cs`, `ApprovalPeriodRepository.cs`, `AuthEndpoints.cs` |

> **★ Wave-1 build trap — TASK-14102 must NOT delete the refusal symbols this wave** [cycle-2 W-2]. `dotnet build
> StatsTid.sln` compiles the test projects, and `TemporalWriteRouterTests.cs:43/49/52/442` references
> `TemporalWriteCase.RejectedFutureDated`. That file belongs to TASK-14112, **in a different worktree**. If TASK-14102 removes
> that enum member, `TemporalWriteRejection.FutureDated`, or `IsFutureDated`, its own build gate fails on a file it cannot see,
> and the tempting fix is an out-of-scope edit into another agent's worktree. **Instruction: leave all three symbols in place,
> unreferenced, this wave.** Retiring them is a wave-2 or close concern. This is the exact class of problem the file-ownership
> decomposition exists to prevent, arriving through the compiler rather than through git.
>
> **Ownership note — the one wave-1 collision, resolved before dispatch.** Refinement B1's census includes
> `VacationSettlementService.cs:1368`, which sits in a file TASK-14101 owns. **Verified rather than assumed:** that file contains
> exactly **one** `GetCurrentAsync` call, at `:1368`, together with the comment block at `:1359-1367` that justifies it; every
> other agreement read in the file is already dated (`:827`, `:1423`, `:1495`). The justification is also the one the refinement
> shows to be doubly dead — the service *does* have a `TimeProvider`, and the claimed equality with `GetByUserIdAt(today)`
> expires the moment future-dating exists. **So the line and its comment go to TASK-14101**, which is rewriting that method
> anyway, and **TASK-14102 does not open this file at all.** Wave 1 is therefore genuinely file-disjoint. Recording the check
> because "the tasks look disjoint" is exactly the kind of claim this sprint's reviews have repeatedly falsified.
| **TASK-14103** | test-qa | **Part A pins**: capture-derived `agreementCode`/`okVersion`/`position` for a mid-ferieår hire; `Earned` unchanged *outside* the divergence window and, *at* it, **differing OK24/OK26 configs seeded** with `okVersion` + `annualQuota` asserted directly; both stored boundaries pinned separately; **A4's two hire shapes**; **a pin on the SPECIAL_HOLIDAY fail-closed change**; rewrite the S140 `cannotCompute` fact | Part-A test files only |
| **TASK-14112** | test-qa | **★ The wave-1 RED pins** [Step-0b W-4]: the **five router matrix shapes**, pure unit tests needing **no Docker and no database**, so they verify *locally, in this wave*. **Honesty correction from cycle 2: only FOUR are RED today.** Shape 4 (today-dated write with a future row → kind `Inserted`) is **already green**, because the router treats a future row like any history row. It is still a valuable regression pin, but **a green there must not be read as "the fix landed"**, and the task says so. Also: replacement assertions for the ~10 refusal tests, the two fixed-clock probe replacements **clock-sensitive**, and the JWT fixed-clock pin — **these are Docker-gated endpoint tests and complete at the wave-2 gate, not this one**, because the endpoint refusals they target are lifted by TASK-14104 | router + refusal test files, disjoint from TASK-14102's |

### Wave 2 — after wave 1 merges

| Task | Agent | Scope | Owns |
|------|-------|-------|------|
| **TASK-14104** | backend-infrastructure | **★ FIRST: lift the THREE endpoint-side future-date refusals** — profile PUT `EmployeeProfileEndpoints.cs:285-286`, users PUT `AdminEndpoints.cs:1615-1617`, agreement-code PUT `AdminEndpoints.cs:2534-2535`. **Keep** the `EffectiveFrom == default` presence guards (`:1613`, `:2529`, `EmployeeProfileEndpoints.cs:275`) — those are malformed-request checks, not policy. Update the doc comments at `EmployeeProfileEndpoints.cs:78` and `Contracts/UserAgreementCodeContracts.cs:24`. **OQ-6's request field is needed on all three of these endpoints, not two.** Then: B0 read payload carries the scheduled change **on BOTH sides** — the profile GET *and* the users/agreement GET, since OQ-6's prompt must work on the agreement side too; B4 endpoint + **the mandatory audit event** for the retired scheduled row, which needs a new event record, a mapper alongside `EmployeeProfileCreatedAuditMapper`, and its registration; B5 endpoint side, **returning the new `users.version` on the profile PUT response** so the drawer can re-stamp (see the BLOCKER); B6 round-trip made inert; B7/OQ-6 the two write branches | `EmployeeProfileEndpoints.cs`, `AdminEndpoints.cs` (agreement side), the new audit mapper |
| **TASK-14113** | backend-infrastructure | **C2's BACKEND half, moved into wave 2** [cycle-2 W-3]: two range reads + one HR-gated endpoint, **shipped with its response type declared** or the API convention gate hard-fails it. **HR gate and org scope on the subject's CURRENT organisation is a security invariant and needs its own pin** [cycle-2 W-4]. Moved here so the wave-2 regeneration covers it | **NEW FILES ONLY** [cycle-3 §4 — every existing file this would naturally touch belongs to a wave-2 sibling]: a new endpoint class, a new read repository, plus **one line** in `ApiEndpoints.cs` `MapAll` (no other wave-2 task touches that file). **The two dependency-registration lines go to the Orchestrator at merge**, like the route files |
| **TASK-14105** | backend-infrastructure | **B2** the effective-date refresh, hosted in **`DelegationExpiryService`**. **Plan premise corrected** [cycle-2 note]: that service no longer uses `CURRENT_DATE` — since S140 it reads the injected `TimeProvider`'s UTC day once per sweep (`:98-102`), which is exactly the clock this needs. **Reuse that `today`; do not add a second clock.** Also raise a QUAL row: a vikar-named service hosting the profile/agreement boundary refresh is a cohesion lie a future reader will not find. `users.version` semantics at the boundary stated and pinned; **B8** the "no row covers today" detector, extending HRP-015 to the profile hole and removing the inner join that filters out the very employee it should surface, **plus the fail-closed reader throw sites** (`EmployeeProfileRepository.cs:1123/1170/1196/1266/1288`) turned into a caught, named condition | `DelegationExpiryService.cs`, `HrFollowUpApprovalReadRepository.cs`, the named throw sites |
| **TASK-14106** | test-qa | **The ENDPOINT-dependent pins only** — everything that needs a running endpoint, which is why it sits in wave 2: the B6 round-trip pin (GET → PUT unmodified → nothing changes, no absence revalued); GET-then-PUT **and** GET-then-DELETE token round trips; the B4 delete pins; OQ-6's two branches; the B0 payload pins; **B9** the repository-internal `expectedVersion` against a split write. **The router matrix and the refusal replacements are NOT here — they belong exclusively to TASK-14112 in wave 1** [cycle-2 WARNING: the split had left them owned twice, and a pin owned twice is a pin written twice and merged badly] | endpoint test files only, disjoint from TASK-14112's |

### Wave 3 — frontend and Part C

| Task | Agent | Scope |
|------|-------|-------|
| **TASK-14107** | ux | **★ The OQ-3 drawer fix (Step-0b BLOCKER) — see below, this is the task's first job.** Then **B0 visibility, ALL SIX surfaces**: profile page; edit drawer **covering the AGREEMENT-CODE field as well as the profile fields**, since it is a second dated field written on every save and a scheduled agreement change would otherwise be the owner's defect one field over; the **danger section** carrying "Slet" (there is no delete dialog, and profile deletion has no frontend caller at all today, so do not hunt for one); **organisation roster, people search, person-reference**. The drawer states what period a save will cover. **B7/OQ-6 prompt**, likewise covering the agreement-code field. **NOT CUTTABLE** |

> ### ★ Step-0b BLOCKER — OQ-3 (a) breaks every HR save until the drawer threads the new token
>
> **What it means for a person using this.** HR opens someone, changes a job title, saves. The profile section returns "someone
> else changed this, reload and retry". They reload and try again and get the same thing. **Every save, for every employee,
> permanently** — not one refusal at rollout.
>
> **The mechanism, verified in the code rather than reasoned.** The edit drawer saves in steps. Step 1 is the users PUT, which
> runs **unconditionally on every save** (`frontend/src/hooks/useEditPerson.ts:147-163`) and bumps `users.version` exactly once
> per request even when nothing changed (`AdminEndpoints.cs:1859-1880`, the one-bump rule). Step 2 then PUTs the profile using
> an ETag captured when the drawer opened (`:200`). Today that ETag is the **profile row's** own version, which step 1 does not
> touch, so the two never interfere. **OQ-3 (a) makes the profile's token `users.version`** — the very value step 1 just
> advanced. The drawer would send the pre-bump number and the server would compare it against the post-bump number.
>
> **The project already solved this exact problem twice, in this exact function.** The comment at `:222-237` describes it for
> the date-of-birth and employment-start writes: they mutate the same user row, so a version captured at dialog-open is stale
> the moment the users PUT commits. The fix there is read-your-write threading — carry a running version cursor and re-stamp it
> after each write. **The profile PUT simply joins that family.** The comment also records how it was missed last time: *"the
> mock that accepted every PUT masked this."*
>
> **Why no planned test would have caught it.** TASK-14106's pin is a GET then a PUT, which never interposes the users PUT, so
> it passes. No frontend test drives the save sequence against a server that rejects a stale token. The close would have been
> green with the drawer unusable.
>
> **This is a plan fix, not a re-ruling.** OQ-3 (a) stands and remains right. **But the cost I quoted the owner was wrong.** I
> said "one stale refusal per open browser tab on release day". That is only true once the drawer threads the token; without
> that work it is every save, forever. Owner informed.
>
> **★ The first version of this fix was WRONG, and Step-0b cycle 2 caught it. I flagged the doubt to both lenses rather than
> waiting to be corrected, and both confirmed it.** The fix said: thread the cursor into the profile PUT and re-stamp *the
> profile snapshot* from its response. That leaves the date-of-birth and employment-start writes — which run **after** the
> profile write at `:246` onward and share the **same** `users.version` cursor — holding a value that the profile write has
> since advanced. **So the 412 would have moved from the profile save to the date-of-birth save rather than disappearing, which
> is strictly worse than the original defect, because it would have looked fixed.**
>
> **★ And the SECOND version was still wrong. Cycle 2 corrected it precisely, and found a third consumer nobody had named.**
> My second attempt said "thread `usersRowVersion` into the profile PUT". **That is literally impossible as written**: the
> cursor is declared at `useEditPerson.ts:243-244`, *after* the profile PUT at `:200`. I also cited `:238-244` as the re-stamp
> pattern; it is the *seed* comment. The actual re-stamp pattern is at `:259-265`.
>
> **The third consumer:** `frontend/src/hooks/usePlacement.ts:164-178` issues a unit change using `live.user.version`
> **after all of the drawer's sub-writes**. So changing someone's unit and their job title in one save would 412 as well.
> Three writes downstream of the profile save, not two.
>
> **The specification, exactly.** The profile PUT sends `If-Match` = the **post-step-1 `live.user.etag`**. On success it
> re-stamps **`live.user.version`, `live.user.etag` and `live.profile`** — the user-side re-stamp is the part that matters, and
> re-stamping only `live.profile.etag` (the obvious move, and what the existing profile code does) is what leaves the next three
> writes holding a superseded number.
> - **TASK-14107** implements that, and its frontend test **dirties the profile AND the date of birth together** against a
>   double that 412s any stale token, asserting the final user version matches the double's. A test that dirties only the
>   profile field passes while the defect is fully present.
> - **TASK-14104** returns the new `users.version` on the profile PUT response, **and `EmployeeProfileResponse.version` must
>   carry the same token as the ETag header** — otherwise the frontend's fallback at `employeeProfileApi.ts:47` formats the
>   profile-row number into an If-Match that means something else entirely.
> - **TASK-14110**'s end-to-end save changes **title plus hire date, or title plus unit**, so it crosses the boundary where the
>   defect lives. "Covering both steps" was not enough; the failure is at step three.
>
> **Three attempts at one fix, each wrong in a different way, each caught by asking rather than asserting.** That is the
> strongest argument in this document for why the dual lens is not ceremony.
| **TASK-14108** | ux | **C1 termination screen** — eleven statuses, both 409 shapes typed and pinned frontend-side, ETag from the terminated-inclusive GET. **Carries the B0 obligation too**: it shows profile values, so a scheduled change must be visible on it |
| **TASK-14109** | ux | **C2 history timeline — the SCREEN only.** Its backend half moved to wave 2 as TASK-14113 [cycle-2 W-3], so the API regeneration after wave 2 covers the new endpoint and this screen can type against it. **FIRST TO CUT** — and cutting it now cuts only the screen, leaving a typed, tested endpoint nobody calls, which is a cleaner partial than half a feature |
| **TASK-14111** | ux | **The effective-date picker** — the screen that lets HR date a change ahead. **LAST TO CUT**, and split out from TASK-14107 deliberately |

> **Completeness note — B0's surface list, corrected at Step 0b [Codex BLOCKER].** The first draft of this plan scoped B0 to
> the profile page, edit drawer and delete confirmation. **The refinement says "wherever a profile value is shown", and named
> three more surfaces the B1 census already touches** — the organisation roster, people search and the person-reference
> resolver, all three of which display a **position** read from the timeline. Scoping B0 to the edit surfaces alone would have
> reproduced the exact defect the owner raised, one screen removed: HR would see a promotion's job title on the roster weeks
> before it takes effect, with nothing saying so. **The termination screen (TASK-14108) carries the same obligation**, since it
> shows profile values too. Corrected: six surfaces in TASK-14107, plus the obligation named in TASK-14108's scope.
> **Why this slipped:** I wrote B0 into the refinement as a principle and then decomposed it from memory of the principle
> rather than from its own text, which is the same class of error as paraphrasing a mechanism instead of reading it.
>
> **Split note — why the picker is its own task, found while writing this plan rather than by a lens.** The cut order says B0 is
> not cuttable and the picker is. In the first draft both lived in one ux task, which would have made that distinction
> unenforceable: cutting the picker would have cut the owner's visibility requirement with it.
> **The split also answers what a cut actually leaves.** If the picker goes, the backend still accepts future-dated writes,
> because B3 lifts the refusal at all four sites and B3 is not cuttable. Future rows remain creatable through the agreement-side
> admin writes. So a picker-less S141 must still **show** a scheduled change wherever one exists, or the sprint would ship a
> system that can hold scheduled changes it never displays — which is precisely the defect B0 was raised to kill.
> **The coherent partial is therefore: guard lifted, every consequence fixed, every scheduled change visible, no picker yet.**
> HR gains nothing new to click, and nothing is silently wrong. That is a shippable state; "picker but no visibility" is not.

### Wave 4 — close

| Task | Agent | Scope |
|------|-------|-------|
| **TASK-14110** | **ux** (not test-qa) | E2E terminate + view-history flows; **one end-to-end drawer save** covering both steps of the save sequence, which is the BLOCKER's regression guard; **both new pages registered in `frontend/e2e/lazy-routes.spec.ts`** (S140's first CI red); no new `frontend/src/components/ui/` component (S140's second CI red; the design-sync gate **cannot be run locally** — Python is absent, so this is avoidance by design, not preference). **Agent reassigned at Step 0b** [W-7]: the E2E spec lives under `frontend/**`, which is ux scope, not `tests/**`, so the original test-qa assignment asked an agent to edit outside its declared scope |

## Orchestrator steps between waves — the close-time bites, declared

Three things belong to the Orchestrator and to no agent. Each was found at Step 0b, and each would otherwise have surfaced as a
red close run.

**1. Regenerate the API contract after EVERY wave that changes the API surface — which is waves 2 AND 3** [W-5; the "waves 2 and
3" placement was itself a **BLOCKER at cycle 2**]. B0's payload and OQ-6's request and response change the API shape, and **three
CI gates fire on that**: the OpenAPI sync check, the generated-types freshness check, and a convention check that **hard-fails
any new untyped operation**. `docs/api/openapi.json` sits under `docs/`, which is Orchestrator-only, so no agent can do this.

| Regeneration | Why |
|---|---|
| **After wave 2 merges** | B0's payload and OQ-6's shapes exist; **TASK-14107 cannot type against the new payload until this runs**, and would otherwise hand-edit the generated types and turn the freshness gate red — exactly S140's pattern |
| **After wave 3 merges** | **TASK-14109 adds a new endpoint *during* wave 3.** The first draft regenerated only before wave 3, so that endpoint could never ship freshly generated, and the convention gate hard-fails a new untyped operation. Missing this would have turned the close red with no local way to detect it, since Python is absent and these gates are CI-only |

TASK-14109's endpoint must also ship with its response type declared, or the convention gate fails it regardless of
regeneration; that instruction is in its task text.

**2. Own the shared frontend registration files** [W-7]. C1 and C2 are both new pages, so TASK-14108 and TASK-14109 would each
add a lazy import and a route to `frontend/src/App.tsx` and an entry to the sidebar. That is the merge collision the whole
file-ownership principle exists to prevent. **The Orchestrator registers routes and navigation after the ux tasks land**; the
agents build their pages and declare what needs registering rather than editing those two files.

**3. Register rows owed at close** [N-3]: the quality row for the interval constraint deferred out of B4, the HRP-015 row
re-read for the profile-hole extension, and the model-routing register row for this sprint.

## Step 5a — where the per-task reviews sit

Mandatory per-task review is not optional for any task here, since every one touches an invariant, and the refinement makes the
**dual lens** mandatory on A1 and on B3/B4/B5. The plan previously said where waves merge but not where reviews sit [W-6].

| Review | When |
|---|---|
| TASK-14101 (A1) — **dual lens** | at the wave-1 gate |
| TASK-14102 (B3 repository side) | at the wave-1 gate — **repository half only** |
| TASK-14104 (B4 + B5 endpoint side) — **dual lens** | at the wave-2 gate |
| **B3, B4 and B5 as wholes** — **dual lens** | **only after wave 2**, because each spans both waves. **B3 joined this list at cycle 2**: once the three endpoint refusals moved into TASK-14104, lifting the guard became a two-wave change like the others, and a wave-1-only review would have reviewed half of it — precisely what scheduling the reviews was meant to prevent |
| **B0 and OQ-6 as wholes** | at the **wave-3** gate — backend payload in wave 2, frontend in wave 3; not dual-lens-mandated, but they deserve one whole-feature look |
| All other tasks | at their own wave's gate |

## Pin register — every acceptance criterion has a named owner

Added at Step 0b [Codex WARNING: several pins had no explicit owner]. A pin nobody owns is a pin nobody writes, and this sprint
verifies almost everything for the first time in CI at close, so an unowned pin is discovered at the worst possible moment.

| Pin | Owner |
|---|---|
| Capture-derived `agreementCode` / `okVersion` / `position` for a mid-ferieår hire | TASK-14103 |
| `Earned` unchanged **outside** the divergence window; **at** it, differing OK24/OK26 configs seeded, `okVersion` + `annualQuota` asserted | TASK-14103 |
| Both stored boundaries pinned **separately** (YEAR_END `boundaryDate`, TERMINATION `terminationCutoff`) | TASK-14103 |
| **A4's two hire shapes** — a Jan–Aug hire's first ferieår IS enumerated; SPECIAL_HOLIDAY enumeration unchanged | TASK-14103 |
| SPECIAL_HOLIDAY snapshot key fail-closed (no `?? user.AgreementCode`, no live-config chain) | TASK-14103 |
| S140's `cannotCompute` fact rewritten, list membership determined not assumed | TASK-14103 |
| The **five** router matrix shapes, incl. today-dated-write-with-future-row asserting kind `Inserted` | **TASK-14112** (wave 1, local, no Docker) |
| **B3 revaluation**: absences ≥ `from` revalued, none before, none past the next scheduled row's start | TASK-14106 |
| **B3 worklist**: no rows for a future date in a **later month**; exported-month rule **unchanged** for a future date inside the current month; no settlement row **via the DATE rule** for any future date — the settlement's own recorded valuation boundary (the accrual end for a year-end settlement, the employment end date for a termination one) is always already in the past. **NOT covered by that guarantee, and this qualifier was missing until a trace restored it:** the independent SKIP-path report takes **no date test at all** and CAN still raise a settled-year row for a future-dated profile write whose revaluation interval reaches an absence grouped under an actively-settled year. **That row is CORRECT and must never be suppressed.** | TASK-14106 |
| **B6** round-trip inert: GET → PUT unmodified → timeline unchanged, no absence revalued | TASK-14106 |
| **B5** GET-then-PUT **and** GET-then-DELETE token round trips, both succeeding with a future row present | TASK-14106 |
| **B4** audit: `previous_data` shows **today's** values; the retired scheduled row's retirement is **itself audited** | TASK-14106 |
| Replacement assertions for all ~10 refusal tests; the two fixed-clock probes' replacements **clock-sensitive** | **TASK-14112** (wave 1) |
| The JWT carries the code effective **today**, pinned under a fixed clock, on the `useDbAuth` branch only | **TASK-14112** (wave 1) |
| **The full drawer save sequence** — users → profile → date of birth → employment start — succeeds against a server that 412s a stale token. The unit test **dirties profile AND date of birth**; the E2E changes **title plus hire date, or title plus unit** | TASK-14107 (unit) + TASK-14110 (E2E) |
| **B1 backend**: with a future row present, the roster, people search and person-reference reads return **today's** position | TASK-14106 |
| **B0 backend**: both GETs carry the scheduled change in the payload | TASK-14106 |
| **OQ-6 backend**: the two write branches pinned on **all three** endpoints — profile PUT, users PUT, agreement-code PUT | TASK-14106 |
| **A future write leaves both caches untouched** | TASK-14106 |
| **C2 security**: the timeline read is HR-gated and org-scoped on the subject's **current** organisation | TASK-14113 |
| **The three ENDPOINT refusals are lifted** — a future-dated save succeeds through the profile PUT, the users PUT and the agreement-code PUT | TASK-14112 (completes at the wave-2 gate) |
| **B9** repository-internal `expectedVersion` against a split write | TASK-14106 |
| **B2** refresh fires on the writers' UTC day; `users.version` semantics pinned either way | TASK-14105 |
| **B8** the fail-closed readers' 500 path pinned as a caught, named condition; HRP-015 surfaces a profile hole | TASK-14105 |
| **B0** the six surfaces show a scheduled change; the drawer states the period a save covers | TASK-14107 |
| **B7/OQ-6** both branches — until the scheduled change, and carry-forward touching **only the edited field** | TASK-14107 |
| E2E terminate + view-history; both new pages in `lazy-routes.spec.ts` | TASK-14110 |

## Verification gates between waves

Added at Step 0b [Codex WARNING: verification deferred too far]. **The risk this closes:** production code and its tests are
written in separate worktrees, Docker-gated tests run only at close, and without an explicit gate a merge or fixture mismatch
would surface at the close run rather than before the next wave builds on it.

**After every wave merges, the Orchestrator runs, on the merged tree, reading each exit status from the UNPIPED command:**
1. `dotnet build StatsTid.sln -c Release --no-incremental` — 0 errors, warning count compared to the 145 baseline.
2. The **non-Docker** test set, which is the part that can run here at all.
3. `npx tsc --noEmit` and the frontend unit suite, from wave 2 onward.

**A wave does not close until those pass.** Docker-gated facts still verify only at close; that is a standing constraint, not a
choice, and this gate exists to shrink what reaches that point unverified rather than to pretend it does not.

**TASK-14106's dependency, stated rather than implied** [Codex WARNING: wave 2 is not parallel-executable as written]. The pins
are authored **RED-first from the spec**, concurrently with their siblings, which is the project's convention and is why the task
sits in wave 2. But they cannot be **verified** until TASK-14104 and TASK-14105 merge. So TASK-14106 completes at the wave-2
gate, not when its agent returns, and its agent is told that explicitly so it does not report green against code that does not
exist yet.

## Standing constraints for every agent

- **Docker is unavailable locally.** Most Part-A and Part-B pins verify only in the watched CI run at close. Do not block on it.
  **Corrected at Step 0b [N-4]: "every Part-B pin" was over-broad.** The router matrix is pure unit testing and runs here, now,
  which is why TASK-14112 moved into wave 1. Do not assume a pin cannot run locally without checking.
- **Read exit status from the unpiped command.** S140 lost a cycle to `npx tsc --noEmit | tail` reporting the *pipe's* status as 0 while ten real type errors printed.
- **Report anything this plan does not name.** Five reviews each found one more write-side consequence than the last. The list is
  believed closed only because an independent enumeration read every SQL statement naming either table and concluded so. If you
  find a tenth, say so rather than assuming it was considered and omitted.
- `isolation: "worktree"` for every concurrent agent, and **teardown at Step 7**.

## Step 0b — plan review

**External (Codex): 1 BLOCKER / 3 WARNING / 2 NOTE — all absorbed above.**

- **BLOCKER — B0 was scoped to three surfaces when the refinement names six.** The roster, people search and person-reference
  screens all display a position read from the timeline, and none had an owner. Scoping visibility to the edit surfaces alone
  would have reproduced the owner's own defect one screen removed: a promotion's title visible on the roster weeks early with
  nothing saying so. The termination screen carries the obligation too. **Fixed:** six surfaces in TASK-14107, the obligation
  named in TASK-14108.
- **WARNING — several acceptance criteria had no named owner**, including B9, A4's two hire shapes, B2's refresh pins, B3's
  revaluation/worklist/settlement pins, B4's audit pins and B8's fail-closed-reader pin. **Fixed:** a pin register assigning
  every criterion to a task. An unowned pin in a sprint that verifies at close is discovered at the worst moment.
- **WARNING — wave 2 was not parallel-executable as written.** TASK-14106's pins test behaviour its two siblings implement.
  **Fixed:** stated explicitly — pins are authored RED-first from the spec concurrently, but the task completes at the wave gate
  rather than when its agent returns, and the agent is told so.
- **WARNING — verification was deferred too far.** No post-wave gate was declared, so a merge or fixture mismatch would surface
  at the close run. **Fixed:** an explicit build + non-Docker test + type-check gate after every wave merge.
- **NOTE — the wave-1 ownership collision is correctly resolved**, and the refinement's "one line in one method" was imprecise:
  it is one call site plus a nine-line comment rewrite. Corrected in the refinement.
- **NOTE — a cut must trim its own E2E obligations**, or a deliberate partial becomes a red close run. Added to the cut table.

**Internal (Reviewer, `claude-fable-5-1`): verdict BLOCKED — 1 BLOCKER / 7 WARNING / 6 NOTE. All absorbed; re-review owed.**

- **BLOCKER — owner ruling OQ-3 (a) makes every HR save fail permanently until the edit drawer threads the new token, and the
  cost quoted to the owner was wrong.** Detailed in the boxed note under TASK-14107. The ruling stands; the plan gains three
  pieces of work and the owner has been told the real cost. **This is the single most valuable finding of the whole sprint's
  review history**, because it would have shipped green with the drawer unusable.
- **W-1 — the settlement seam needed a clock decision**, not just an owner. Assigned to TASK-14101 by line range, with the
  writers' UTC day specified and the reason stated.
- **W-2 — more unowned criteria than the external lens found**, including the D9-parity comment, a pin on the special-holiday
  change, the fail-closed reader throw sites as *code* rather than just a pin, and visibility on the agreement side. All now
  owned; the pin register is the enforcement.
- **W-3 — the cut floor was named too kindly.** Without the picker, the visibility work is correct and dormant. The cut table
  now names that row "Increment 4's precondition shipped; the feature did not."
- **W-4 — the pins were placed GREEN-first**, and the router matrix does not need Docker at all. Split out as TASK-14112 into
  wave 1 so RED is actually demonstrated before the fix lands.
- **W-5 — no API regeneration step**, with three CI gates waiting on it. Now an explicit Orchestrator step between waves 2 and 3.
- **W-6 — Step 5a was not scheduled**, and B4/B5 span two waves so a wave-1 review would have reviewed half a change. Scheduled.
- **W-7 — wave-3 frontend ownership was undeclared** and one task was assigned outside its agent's scope. Shared registration
  files move to the Orchestrator; the E2E task moves from test-qa to ux.
- **Verified sound:** wave-1 backend disjointness, the wave 2 → 3 payload gating direction, the cut-order principle, the
  termination screen's token source, TASK-14103's match to the refinement, and the refusal inventory of roughly ten.

### Step 0b cycle 2 — re-review of the revision

**External (Codex): 2 BLOCKER / 1 WARNING — all absorbed.**

- **BLOCKER — the cycle-1 blocker's FIX was wrong, and would have moved the failure rather than removed it.** Re-stamping only
  the profile snapshot leaves the date-of-birth and employment-start writes, which run afterwards and share the same cursor,
  holding a superseded value. **The 412 would have migrated to the next write and the plan would have looked fixed.** Corrected
  to a single running cursor across all four writes, with the test driving the whole sequence.
  *Worth recording how this was caught:* the Orchestrator **doubted its own fix and said so in both cycle-2 prompts**, asking
  specifically whether the ordering still worked. Both lenses were pointed at the right place because the doubt was declared
  rather than suppressed. A fix that is uncertain should be reviewed as a question, not asserted as an answer.
- **BLOCKER — the API regeneration was placed only before wave 3, but wave 3 ADDS an endpoint.** That endpoint could never ship
  freshly generated, and the convention gate hard-fails a new untyped operation. Since Python is absent, none of these gates can
  be run locally, so this would have surfaced as a red close with no local reproduction. **Corrected: regenerate after wave 2
  AND after wave 3.**
- **WARNING — the router matrix pins had two owners** after the wave-1 split, in both the task table and the pin register. A pin
  owned twice is written twice and merged badly. **Corrected: exclusively TASK-14112.**
- **Verified sound:** the router tests really are pure and local, so the failing-first demonstration is real; the UTC day is the
  right clock for the settlement seam; `DelegationExpiryService` is a registered five-minute UTC poller and a suitable host; the
  shared route and sidebar files are Orchestrator-only with no agent needing them; and the end-to-end spec is indeed frontend
  scope, so the agent reassignment was correct.

**Internal (Reviewer, `claude-fable-5-1`): verdict BLOCKED — 2 BLOCKER / 5 WARNING / 6 NOTE. All absorbed.**

- **BLOCKER — the future-date refusal has SEVEN sites, not four, and the three that face the user were unowned.** The
  refinement counted only the repository guards. The endpoints validate independently *before* the repository is reached.
  **Consequence had it stood: the date picker ships dead.** HR picks 1 November, the endpoint returns "cannot be dated in the
  future", and every layer beneath it would have accepted the write happily. The sprint's headline feature would have looked
  finished and been unreachable. Two knock-ons absorbed: the refusal replacement tests are Docker-gated endpoint tests and so
  complete at the wave-2 gate, not wave 1; and B3 becomes a two-wave change, so its review moves after wave 2.
- **BLOCKER — the drawer fix was wrong for a THIRD time, and has a third consumer.** "Thread the cursor into the profile PUT"
  is impossible as written, because the cursor is declared *after* the profile write. The correct specification is now exact:
  send the post-step-1 user ETag, and re-stamp the **user** version and ETag, not just the profile's. The unnamed third
  consumer is the unit-change hook, which writes after all the drawer's sub-writes — so changing someone's unit and job title
  together would have failed too. Both tests widened, because the originals would have passed with the defect fully present.
- **W-2 — a wave-1 build trap.** The solution build compiles test projects, so if TASK-14102 deletes the refusal symbols its
  own build fails on a file owned by another agent in another worktree. Instruction added: leave them in place this wave.
- **W-3 — C2's backend half moved into wave 2** as TASK-14113, so the wave-2 regeneration covers its new endpoint.
- **W-4 — six more unowned pins**, including C2's HR gate and org scope, which is a **security invariant that had no test
  owner at all**. All added to the register.
- **W-5 — the agreement code is a second dated field in the same drawer**, written on every save, so OQ-6's request field is
  needed on three endpoints and the visibility indicator must cover that field too. Otherwise the owner's defect reappears one
  field over.
- **Notes absorbed:** the UTC day is exact parity with the year-overview reader, which is a better justification than the one
  I gave; the poller premise was stale, since that service already reads the right clock, and a QUAL row is owed for the
  cohesion problem of hosting this there; only four of the five router shapes are red today, and the plan now says so rather
  than implying a green means the fix landed; the delete confirmation is a danger section rather than a dialog, and profile
  deletion has no frontend caller at all today; the changed delete response must ship typed.
- **Verified sound:** the agent reassignment, the Orchestrator's ownership of the shared registration files, the regeneration
  actually unblocking the frontend, wave-1 file disjointness, and the after-wave-2 review placement.

**Verdict on dispatch: both blockers were plan-text fixes of a few lines, now applied.**

### Step 0b cycle 3 — confirmation pass

**Internal (Reviewer, `claude-fable-5-1`): APPROVED-WITH-WARNINGS. Safe to dispatch wave 1.** Every cycle-2 line citation
re-verified against the code. Both blockers correctly absorbed, and the drawer's consumer census closed at **three, not four**.

Two gaps found in the absorption itself, both fixed before dispatch:

- **★ B0's READ side had no owner at all** [§5]. The payload must carry the scheduled change, and the edit prompt reuses the
  same lookup — but neither repository exposes a "next scheduled row" read, and no task owned adding one. TASK-14104 in wave 2
  would have had to reach into a sibling's file or stop. **Added to TASK-14102**: same agent, same files, same worktree, free
  now and a collision later. *This is the third time a requirement of the owner's has needed a home the plan had not built.*
- **Wave 2 had wave 1's collision** [§4]. TASK-14113's file ownership said "the new endpoint + its reads", and every existing
  file it would naturally touch belongs to a wave-2 sibling. **Constrained to new files only**, plus one line in a routing file
  nothing else touches, with dependency registration handed to the Orchestrator.

Smaller corrections absorbed: a filename I got wrong; "update the two doc comments" is a floor, so each agent sweeps its own
files for the now-dead future-dating comments and two constants; the agreement-code field named explicitly in the frontend
task; and the edit prompt's carry-forward is a **second routed write through the existing writer**, not a new repository
method — worth saying, because an implementer could easily build a method that need not exist.

**Verified sound:** the post-step-1 ETag really is a fresh value; the profile response's version already promises to match its
ETag header, so that instruction preserves an existing contract rather than inventing one; the poller already reads the right
clock; four of five router shapes are genuinely red; and a test harness that records the header per call already exists, so
the widened drawer test has somewhere to live.

**STATUS: WAVE 1 DISPATCHED.**

## Wave 1 — agent findings the plan did not name

The dispatch prompts told every agent that finding a false claim in its own instructions is a success rather than an
embarrassment, and that anything the plan failed to name should be reported rather than assumed considered. That is producing
results, so the findings are recorded here as they arrive rather than at close.

### TASK-14101 (settlement anchor) — complete, build green, 5 findings

Build `0 errors / 145 warnings`, matching the declared baseline. Unit suite `1238 passed / 0 failed`. No Docker-gated test
attempted or claimed. **Every falsifiable claim in the dispatch prompt was checked against the code and all held** — the five
site line numbers, the two dates that must not move, the clock seam's four members, the parity citation, the emitter citation,
the accrual helper's internal maximum, and that the file contains exactly one call of the read being replaced.

1. **★ RULED — the anchor was unclamped at the top, turning a loud failure into quiet junk data.** If the hire date falls after
   the END of the settled year, `max()` yields an anchor outside that year, the reads succeed, and the result is a **zero-earned
   settlement snapshot keyed at the hire for a year the employee never worked here** — recorded, audited, and capable of
   emitting a zero payout event. Previously this threw. The agent verified it is unreachable from either poller branch and is
   reachable only by a direct call with an arbitrary year, **and declined to clamp it unilaterally because the ruling said
   `max()`** — correct judgment, and exactly the behaviour the prompts asked for.
   **Orchestrator ruling: fail closed, restoring the throw.** Junk data that looks deliberate is worse than a loud failure, and
   this is not a deviation from OQ-2 (a): that ruling was made about a year the employee was employed for *part* of, and never
   contemplated one they were not employed for at all. Restoring the throw keeps the ruling inside the case it was made about.
2. **A documented parity claim is now deliberately false.** The probe anchors are no longer a strict subset of the year-overview
   reader's. The divergence is one-directional — more closed years become settleable, never fewer — and cannot change a settled
   quantity. Documented in code at both the probe and the fallback throw, whose message named anchors it may no longer use.
3. **The capture's clock dependency is now explicit where it was hidden.** Reading "the open row" was always a disguised
   dependency on *now*; it is now an explicit clock read. Strictly better and testable, but a change of kind — relevant to both
   test tasks, which are told.
4. **The special-holiday position read is still null-tolerant while its three siblings are now fail-closed.** The emitter
   coalesces a null position to empty and the seeded mappings carry a default row, so an absent profile resolves a mapping
   instead of failing. **Registered as a quality finding rather than fixed**, because widening the fail-closed change to the
   position is outside what the owner ruled. The code comment must say it is a *registered* asymmetry, so a later reader does
   not tidy it into consistency without a ruling.
5. **A4 has a one-time backlog effect nobody had costed.** Every January-to-August-hired employee gains a previously
   unenumerated first holiday year, so the first poll after this ships settles a backlog, each row emitting an outbox event and
   an audit row. Bounded by the go-live gate and the candidate-year floor, but **there is no per-run batch cap** — the agent
   checked. No action: the service is dormant today, so there is no backlog to process yet. Recorded so it is not discovered
   during a close run.

**TASK-14101 ruling applied — the guard is in, and the agent avoided a fresh defect while adding it.** Build `0 errors / 145
warnings` (baseline held), unit suite `1238 passed / 0 failed`, no Docker-gated claim. Two comparand choices it made
deliberately and justified in code, both of which a careless implementation would have got wrong:

- **The vacation guard compares against the holiday-year end, NOT the valuation boundary.** The valuation boundary is pulled
  earlier by a termination cutoff, so comparing against it would have **rejected a legitimate leaver whose hire preceded their
  leave date** — turning a correctness guard into a new defect on the leaver path. This is the kind of second-order effect the
  sprint's reviews kept finding in the plan; here the implementer found it unprompted.
- **The special-holiday guard compares against the ACCRUAL end (31 December of the accrual year), not the much later
  settlement boundary**, because the accrual window is the period the employee must have overlapped for anything to have
  accrued at all.

It also verified the guard cannot disturb existing fixtures before adding it, and correctly observed that the two new throws
are behaviour changes owed a failing-first pin that no task owned — **relayed to TASK-14103 while it was still running**,
including the warning that a pin asserting against the valuation boundary would encode the leaver bug.

**QUAL-171 registered** for finding 4 (the special-holiday position read still tolerant while its three siblings are now
fail-closed, with a seeded blank-position mapping row that makes the degradation resolve a real payroll code rather than fail).
The code comment at the site is headed as a known, registered asymmetry so it is not tidied into consistency without a ruling.

### TASK-14112 (router + refusal pins) — complete, 4 of 5 RED as predicted, 2 more prompt claims falsified

**The failing-first discipline is now real rather than nominal**, which was the whole reason this task was split into wave 1.
Unit suite: **1244 total, 1239 passed, 5 failed — exactly the five intended**, nothing else regressed. Non-Docker regression
104/104, demo-seed 165/165.

| Shape | State | Why |
|---|---|---|
| 1, 2, 3, 5 | **RED now** | They hit the guard; they turn green when TASK-14102 lifts it |
| 4 — today-dated write while a future row exists | **GREEN now** | Today is not *after* today, so the guard never sees it; the router already handled a history row covering today |

Shape 4's green was predicted in the dispatch prompt and the test's own comment says why, so **a green there cannot be misread
as evidence the fix landed**. That instruction earned its place.

**Two more claims of mine falsified** (running total across this sprint's planning: eleven, six of them mine):
- **"The agreement-code backdating tests (four)"** — there are **three**. The agent listed every fact in the file and checked
  each future-dated one rather than trusting the number.
- **The blanket "these complete at the wave-2 gate" framing was wrong for three pins.** Two repository-direct tests and the
  login-token pin never reach an endpoint validator at all — they call the repositories directly, or seed by direct SQL. Their
  real dependency is **TASK-14102, a wave-1 sibling**, so they could go green as early as the wave-1 gate. Each pin's comment
  now carries its true dependency instead of the blanket claim. This matters for when to re-verify, which is exactly the kind
  of thing a blanket statement hides.

**Inventory: nine refusal tests, not "roughly ten."** All nine got real replacement assertions; none was deleted. Correctly
excluded: two tests pinning an unrelated validator for local agreement profiles, which this sprint does not touch.

**The two fixed-clock probe replacements are genuinely clock-sensitive**, and the construction is better than the instruction
asked for. Once future-dating is universally accepted, "a future date returns success" is true whether or not the fixed clock
reaches the endpoint — so a bare success assertion would have silently destroyed what those probes exist to prove. Instead each
asserts the response still carries **today's** value rather than the scheduled one, which only holds under a correctly-seamed
clock: an unconverted real-clock path would treat the fixture's date as over a year in the past and flip the assertion.

**One addition beyond the named five, flagged as such.** An existing test asserted refusal on both an empty and a populated
timeline. Its populated half became shape 1; its empty half became a new pin for a new hire's very first row being scheduled
ahead. Leaving it unreplaced would have left a permanently-false pin in a file no other task owns.

**Found, not fixed, out of scope:** a stale prose comment in a config test file naming a renamed method. Doc hygiene; fix at
merge.

### TASK-14103 (Part A pins) — complete, and it avoided the trap the prompt warned about

Build `0 errors / 145 warnings` (baseline held). Non-Docker suites all green: unit 1238/1238, demo-seed 165/165, regression
104/104. **Every new pin is Docker-gated and therefore CI-verified at close, not green locally** — reported as such, not as
passing.

**The trap avoided.** The obvious pin at the divergence window would assert `Earned` is unchanged. It cannot fail there,
because the two seeded agreement-version configs are identical, so it passes under either anchor and proves nothing. The agent
instead seeded **two differing test-owned configs** (25 days against 30) and asserted the version and the quota **directly**.
That is the only form of this pin that can fail, and it is the difference between a guard and a decoration.

**Two boundaries pinned separately, as required** — the year-end path stores the period end, the termination path stores the
employment end date. A single pin over "the boundary" would have covered one path and silently missed the other.

**The added guard pins were designed to discriminate.** For the special-holiday path the agent chose a hire date sitting
*between* the two candidate comparands, so a guard wrongly compared against the later payout deadline would let it through and
the pin would catch it. That is a pin built to fail for the right reason rather than merely to pass.

**What it declined to write, correctly.** A discriminating pin for the vacation guard's comparand choice. It could not
construct a scenario it was confident was both realistic and correct without the merged implementation, and said so rather than
guessing. **Orchestrator follow-up: the scenario appears to be unconstructible, and that is the real answer.** For the wrong
comparand to fire, the anchor would have to fall after the employee's leaving date, which means being hired after leaving. So
the guard is safe by construction rather than by test. **Recorded as a reachability argument in a comment at the guard**,
because an untestable safety property should be written down as an argument rather than left as an absence — if someone later
changes how the leaving date is derived, that comment is what tells them the argument needs re-checking.

**A gap it spotted and I took up:** the special-holiday path had a fail-closed pin but no positive capture-correctness pin, so
we would have proved it refuses bad input without ever proving it captures the right values from good input. Mirror pin
commissioned.

**And one more caveat of mine resolved rather than assumed.** I had said a partial-year hire "may legitimately land in neither
list". The agent computed it: this one lands in `items`, with the numbers worked rather than guessed. The caveat was an
instruction not to assume, not a prediction.

### TASK-14102 (timeline data layer) — complete. 14 findings, 4 declared deviations, 5 more falsified claims.

The sprint's largest task, and the richest report. The headline is that **the removed `if` was load-bearing in a way nobody had
written down**: it was the only reason "the row with no end date" and "the row describing today" were the same row, so every
read asking "what is this person's current X" was relying on a coincidence without knowing it.

**Findings that change other tasks — carried forward as handoffs:**

- **★ H — the delete's return shape made the owner's mandatory audit structurally impossible.** OQ-5 (a) *requires* the
  retirement of a scheduled row to be audited, and the existing two-value return cannot carry it. A new method was added that
  returns the closed row, its pre-image, the token and **the retired scheduled rows**. **TASK-14104 must adopt it or the owner's
  condition on OQ-5 (a) goes unmet.** This is the single most important handoff out of wave 1.
- **★ D — OQ-5 (a) was written for one scheduled row; two are representable.** Retiring only the first would leave the second
  standing with nothing covering today — the exact hole the delete fix exists to close. Implemented as **every row starting
  after today**, earliest first. A faithful extension of the ruling rather than a departure from it.
- **★ K — wave 1 alone opens a NEW cache-divergence window.** Before, the canonical read was wrong between the write and the
  effective date. Now it is right throughout, but the cache is wrong from the effective date until the next write, and many
  consumers read the cache. The window shrinks and moves, but a disagreement exists that did not before. **B2 in wave 2 closes
  it. Wave 1 must not ship alone.**
- **A — the boot seeders are in nobody's scope, and more usefully, a seeder CANNOT be the fix.** The refinement's phrasing
  implied a fixable guard. The live partial-unique index means an employee whose only row is future *already has* an open row,
  so a seeder converted to "has no row covering today" would try to insert a second and collide. The hole is unfillable that
  way and needs the detector. **Correction carried to TASK-14105.**
- **L — the update's existence pre-check now yields the wrong refusal code** for an employee whose only row is future.
  TASK-14104 may want it to ask "covers today".
- **M — two stale comments in files this task does not own**, one of them TASK-14104's.
- **N — a same-values write is a no-op**, so scheduling a change to a value someone already has correctly reports nothing
  scheduled. **Relevant to TASK-14107**: a drawer that optimistically renders "scheduled" after such a save would be lying.
- **I — a second zero-caller trap** of the same shape as the one this task deleted. Left in place because deleting an unnamed
  public surface is an Orchestrator call. Recommend removal in wave 2.

**Findings resolved inside the task, well:**

- **G — the new predicate loses a database-enforced uniqueness guarantee**, precisely in the three queries whose own comments
  warn about fan-out, and one of them joins *after* its count, so a fan-out would corrupt the page and disagree with the total.
  Solved with lateral single-row joins rather than a plain conversion. **This is the kind of second-order effect that no lens
  found in four review cycles.**
- **B — the token bump had to become unconditional**, because a token that does not move on every edit does not detect
  anything. Consequence, named rather than discovered later: an audit row is now written on **every** profile edit, with an
  unchanged pre-image where the category did not move. Correct, and a volume increase nobody had costed.
- **C — one token now covers three things.** A profile edit and an admin user edit now conflict where they were independent.
  That is what "one token per record" means once the record is the employee, and it is a real consequence of OQ-3 (a) that the
  plan never stated.
- **E — the retirement idiom and the scheduled-change read collide.** A retired row would have shown to HR as a pending
  scheduled change. Both reads now exclude it.
- **F — a clock divergence, pre-existing but newly visible.** The predicate cited as the in-tree reference uses the Copenhagen
  business day; this task used the writers' UTC day per the sprint's own clock rule. For an hour or two each night the two
  disagree about what day it is. **Needs a ruling; not created by this task.**

**Five more falsified claims** (running total: sixteen, seven of them mine), including that the cited reference predicate was
right in *shape* but carried a different clock, and that one instruction was "not literally implementable" — the agent
preserved its intent and flagged the departure rather than silently approximating.

## Wave-1 gate — PASSED

Merged all four branches into master, **no conflicts**. Then, each exit status read from the unpiped command:

| Check | Result |
|---|---|
| `dotnet build StatsTid.sln -c Release --no-incremental` | **0 errors, 145 warnings** — baseline held exactly |
| Unit | **1244 passed, 0 failed** |
| Demo-seed | **165 passed, 0 failed** |
| Regression, non-Docker | **104 passed, 0 failed** |

**The failing-first discipline worked end to end.** TASK-14112's four router pins were written before the fix and failed;
after merging TASK-14102 they pass. That is a real RED-to-GREEN transition rather than a test written against finished code,
and it is the thing the wave-1 split was created to make possible.

Docker-gated facts remain unverified and are **not** claimed green.

## Step 5a — wave-1 per-task review

**External (Codex): 2 BLOCKERS, both real, both fixed.**

1. **★ The converted agreement-code read had no deterministic single-row clause, and its most important consumer is the login
   token.** The retired predicate could not match twice, because a partial unique index forbade it at the database level. The
   new predicate has no such backing — non-overlap of dated rows is only a writer-side invariant, and the history index permits
   overlap. So an overlapping pair would have made the returned row **whatever the query planner emitted first**, and that value
   goes into a JWT. The implementer had identified exactly this hazard (its own finding G) and fixed it in the profile read and
   the three roster joins, but **applied the fix inconsistently and missed the agreement-code read and the cache refresh**.
   Fixed at both sites with the same tie-break the sibling read uses.
   *The lesson worth keeping: an implementer who finds a hazard is not thereby guaranteed to have found every instance of it.*
2. **★ The special-holiday capture was not fail-closed after all — and the Orchestrator's earlier ruling was the reason.** A
   missing profile row at the anchor degraded to null, the emitter coalesced null to empty, and the seeded default mapping row
   **resolved a real wage type**, so an absent profile staged a payout line under a code nobody chose.
   **This is the Orchestrator's error, not the implementer's.** TASK-14101 reported the asymmetry and I registered it as
   QUAL-171 rather than fixing it, reasoning that widening "fail-closed" to the position exceeded owner ruling OQ-2 (a). That
   reasoning was wrong. The ruling made the special-holiday **snapshot key** fail-closed like the vacation path's, and the
   position **is** one of the four components of that key — the emitter resolves the lønart from
   `(time_type, ok_version, agreement_code, position)`. Closing the hole **applies** the ruling; it does not widen it.
   The original reading conflated two different things, and only one was ever in question:
   - a **missing profile row** at the anchor → silent wrong data → **now throws**;
   - a **resolved profile whose position is null** → legitimate, passed through, exactly as the vacation path does, because the
     default mapping is the deliberate product answer for "no position recorded".
   **QUAL-171 is therefore closed as FIXED, and its register row records that the registration itself was the error** — it
   delayed a payroll-key correctness fix behind a ruling that had already been made.

**Re-verified after both fixes:** build `0 errors / 145 warnings`, unit `1244 passed`, regression non-Docker `104 passed`.

**Internal lens: running.**

**Internal (Reviewer, `claude-fable-5-1`): APPROVED-WITH-WARNINGS — 0 BLOCKER / 4 WARNING / 5 NOTE.**

It read every production diff line by line, ran its own census of every remaining open-row read on both timeline tables, traced
every token producer and consumer, and **ran the router unit tests on HEAD (48/48 green)** to confirm the four failing-first
pins genuinely flipped. Reviewed at `94560a4`, i.e. before the two external blockers were fixed, so its note that the
special-holiday position read is still tolerant is superseded.

**Verified sound, and worth recording because these are the sprint's highest-risk changes:** all three roster joins are lateral
single-row with outer-join semantics preserved, so the paged search's count and page can no longer disagree; **no current-state
read is left on the old predicate anywhere, in SQL or in C#**; the read's token, the write's validation and the delete's
predicate all use the same value and nothing compares a row version against a record version; the delete evaluates 404 before
412 so the retry contract survives, cannot write an inverted interval, and cannot manufacture a phantom retirement on a repeat;
both scheduled-change reads exclude cancelled rows; the router gained no new case and the three retained symbols have zero
producers in production code; the anchor reaches the four intended sites and **not** the probe loop; and both stored boundaries
are unchanged.

**Warnings, all carried to wave 2 as handoffs:**
- **W1 — the profile audit column now holds two different kinds of version.** The update records the record token; the delete
  still records the closed row's own version, so a reconstruction cannot chain them. One line, and the right value is already
  returned by the new delete result. **TASK-14104.**
- **W2 — the visibility requirement cannot hold when nothing covers today.** Both reads anchor on the covering row, so an
  employee whose only row is scheduled gets a 404 and the scheduled change is invisible with it. **Not reachable in wave 1** —
  the create path always writes at today and the update path's existence probe blocks re-creation. **Orchestrator decision: B0
  explicitly EXCLUDES the no-covering-row state and B8's detector owns it**, since that state is precisely what B8 exists to
  surface. Recorded rather than left implicit, and if it ever becomes reachable through the product the read should return the
  employee with a null profile rather than 404.
- **W3 — the clock divergence: register and rule, do not fix here.** Registered as **QUAL-172** with the reasoning. **The
  ruling is owed at TASK-14105 dispatch**, because that is the one place the two conventions collide in a single file.
- **W4 — a rewritten pin hard-coded the token and so passed under BOTH token definitions**, unable to tell the new source from
  the old, and would have failed for an unrelated reason as soon as any earlier record write appeared in a test. **Fixed**: a
  helper now reads the live token from the read, mirroring the sibling file that already did it correctly.

**Notes absorbed:** the audit-volume increase is confirmed correct and in fact *more* auditable, since it preserves the
invariant that every token transition has an audit row — with two refinements for wave 2, a discriminating action on the row
and a comment that is now false; three stale comments in wave-2 files, with line numbers; a test comment that gives the right
assertion the wrong reason; a vacuous assertion to retire with its symbol; and the sprint header's stale status, **now fixed**.

## OWNER RULING 2026-09-14 — the clock, and a defect the ruling exposed

**Immediate ruling (asked at wave-2 dispatch, per QUAL-172): the "does any record cover today" detector uses the WRITERS'
clock — the UTC day — not the Copenhagen day of the file family it lives in.** A deliberate, documented exception at both
sites. Reasoning: it asks a *data-integrity* question about records written and dated on the UTC day, so asking it on another
clock would report gaps that do not exist for an hour or two every night.

**Then the owner questioned the premise of the fork itself** — *"Why not update the system's clock to the Danish clock? We will
only have Danish users."* — and that turned out to expose a **real user-facing defect** nobody had found in six review passes.

**The defect.** The frontend sends "today" as the UTC calendar day of the current instant
(`frontend/src/hooks/useEditPerson.ts:44-46`). In Copenhagen at 00:30 on 1 November that returns **31 October**. So a Danish HR
user working after midnight who records a change "from today" has it stored as effective **yesterday**. That is not an internal
inconsistency between components — it is a wrong date, written to the record, originating on the user's own screen.

**Why the whole split exists.** The backend's same-day validator was made UTC *deliberately, to agree with that frontend call*
(`EmployeeProfileEndpoints.cs:84-90` says so in as many words). Every writer, both caches, the login mint and this sprint's new
reads followed. So the two conventions are **not** a considered balance between two valid readings: the Copenhagen day was
chosen once, for Danish employment law, and the UTC day propagated outward from one convenience call in the browser.

**Decision: move business dates to the Danish day — as its own work, not in S141.** Added to `ROADMAP.md` § Correctness /
domain with the full analysis. Deferred because it touches precisely the surface wave 1 changed and both lenses reviewed;
folding it in would invalidate that review and mix two unrelated risks. *Instants* (created/updated stamps, audit timestamps,
outbox ordering) stay UTC — that is correct practice and ordering depends on it. Only **business dates** move.

**The immediate ruling is forward-compatible and needs no revisiting:** the rule is "match the writers", so when the writers
move to the Danish day, the detector moves with them and no exception is left behind.

**Process note worth keeping.** This is the **second** time in this sprint that the owner answered a fork by questioning
whether the fork needed to exist — the first produced requirement B0 (visibility), this one produced the roadmap item above.
Both times the constraint turned out to be inherited rather than chosen, and neither review lens had asked. The lenses are
strong on "is this mechanism correct" and blind to "should this mechanism exist".

## Wave 2 — DISPATCHED

| Task | Agent | Carrying |
|---|---|---|
| **TASK-14104** | backend-infrastructure | The three endpoint refusals first (without them the picker ships dead); the delete adopting wave 1's new method so the owner's audit condition is met; W1's audit-column fix; the token response; B0's payload both sides; OQ-6's two branches on three endpoints; three stale comments |
| **TASK-14105** | backend-infrastructure | The refresh job reusing the poller's existing clock (its stale premise corrected); the detector, **carrying the owner's clock ruling and the instruction to document the exception at both sites**; the correction that the seeders cannot be the fix |
| **TASK-14113** | backend-infrastructure | The history endpoint, **new files only**, typed response, and its own security pin — the access gate had no test owner until Step 0b found it |
| **TASK-14106** | test-qa | The ten endpoint-dependent pins, with both "cannot fail" warnings from earlier in this sprint restated |

### TASK-14113 (employment history endpoint) — complete, and it corrected the plan on a blocking precondition

Build `0 errors`. All 10 of its facts are Docker-gated and **CI-verified only**, not claimed green.

**★ The plan was wrong about the dependency registration, and the correction is load-bearing.** The plan framed it as
merge-time bookkeeping. It is a **hard startup precondition**: minimal APIs infer an unregistered complex parameter as a *body*
parameter, and an inferred body on a GET throws at **endpoint-mapping time**, before any request. So without it the contract
generator fails, its sync gate fails, and **every Docker-gated test in the suite fails at host boot** — not merely this
endpoint's. The agent verified this by applying the line, confirming, then reverting it, leaving `Program.cs` untouched as its
scope required. **Also: one service, not the two the plan anticipated** — a single read repository serves both range reads
precisely so they can never disagree about what falls inside the window.
**Applied by the Orchestrator at merge, and verified rather than assumed:** the contract generator now runs to completion
(`exit 0`) and the new operation appears in the spec. **`docs/api/openapi.json` must be regenerated AGAIN at the wave-2 gate**,
because TASK-14104 is still adding API surface.

**★ A planning gap it found and filled: the history read's functional correctness had NO test owner anywhere.** The pin
register assigns this task only the *security* pin. The agent added four behavioural facts in its own file — ordering by
effective date, the scheduled/end-exclusive boundary, the agreement track, and the malformed-window refusal — and flagged the
gap explicitly, on the grounds that *a gate around an unverified answer is half a deliverable*. That is the Orchestrator's
planning error, not the agent's, and it is the second time this sprint that a security pin was assigned without the
correctness pin beside it.

**A security property that is structural rather than engineered, and a policy consequence worth the owner's attention.**
Neither timeline table carries an organisation column at all, so the only organisation in play is the employee's current one,
which the existing validator resolves. The stamped-organisation drift that S140's reads had to guard against **cannot arise
here**. But the consequence is a real policy decision that nobody has explicitly made: **a transferred employee's ENTIRE
history — including the years they worked in a previous organisation — is readable by their CURRENT organisation's HR, and by
nobody else.** Changing that would need an organisation dimension on the timeline tables, i.e. a schema change; it is not a
tuning knob. Recorded so it is a known property rather than an accident.

**Its highest-value pin** is the mixed-role shape: a token whose *primary* role clears the HR floor but whose HR scope is in a
different organisation, combined with a leader scope that does cover the subject. It goes red the moment someone reaches for
the no-floor overload — the single most likely future weakening, because the call still looks correct.

**Decisions it took, each with what was given up:** scheduled intervals are **included and marked as such** (hiding a booked
change in a history view would be the most surprising possible place to hide it); "today" is the writers' UTC day, consistent
with this sprint's ruling, so a change saved late in the evening does not read back as not-yet-in-force; no per-interval token
on a read-only screen, which would invite a write against the wrong row; no stamped agreement version, since an interval can
span a version transition; two parallel tracks rather than one merged timeline, because merging would invent composite
intervals no stored record corresponds to; an unknown employee returns access-denied rather than not-found, so the endpoint is
not an identifier oracle; and an employee with no records returns an empty result rather than an error, because a data hole is
the new detector's job to surface rather than this read's to dress up.

**The route for wave 3 (TASK-14109 needs it):** `GET /api/hr/employees/{employeeId}/history`, with optional `from` and `to`.

### TASK-14106 (endpoint pins) — complete, 26 facts, and the counter-test I most wanted

Build `0 errors / 145 warnings` (baseline held). Non-Docker suites identical to the wave-1 gate — unit 1244, seed 165,
regression 104 — so the additions introduced no regression and, correctly, **none of the 26 new facts executed**. Not claimed
green.

**It merged master into its own worktree before starting**, having noticed the worktree was pinned at the sprint-start commit,
so the pins were written against the real wave-1 code rather than against guessed line numbers. Unprompted and correct.

**★ The counter-test is the most valuable thing here.** The worklist rule is conditional and an earlier draft of this sprint's
plan got it wrong in a way that would have *suppressed a true finding*. The agent pinned **both** halves: a future date in a
later month raises nothing **even with an export seeded for that month** — proving the absence is structural rather than
coincidental — **and** a future date inside the current month **still raises** the exported-month row. The second is the
counter-test. Without it, an implementer could have satisfied the first by suppressing the rule outright and the suite would
have agreed with them.

**★ One thing it could NOT settle, and correctly refused to guess at.** `WriteForSkippedSettledYearsAsync` appears
**unconditional with respect to dates** — it fires whenever a revaluation declines to re-record an already-settled group. So a
future-dated write whose revaluation interval touches a pre-existing far-future absence belonging to an already-settled year
could, in principle, raise a settled-year worklist row through a path the date rule never sees. The agent pinned only that the
**date rule** is structurally impossible for a future write, declined to construct the other scenario because it was not
confident the result would be correct, and said it deserves attention before close. **A read-only trace is now running to
settle it.** The wrong outcome here would be to silence a true finding, which is exactly the mistake review caught earlier in
this sprint.

**Two more imprecise claims corrected** (running total: eighteen). The surfaces carrying a job position are **two** HTTP
endpoints, not three — one response carries it on both its employee rows and its name-resolution sub-object. And a test helper
the docs referred to was retired two sprints ago; the agent followed the current convention rather than the documented one.

**Open reconciliation for the wave-2 gate:** the pins guess two wire names for payload members TASK-14104 is defining right
now. If that task names them differently, only the property-name assertions need reconciling — the behaviour each pins is
spec-derived and not in doubt.

### TASK-14105 (refresh job + gap detector) — complete, and it falsified five line numbers in its own instructions

Build `0 errors / 145 warnings` (baseline held exactly). Unit 1244, regression non-Docker 104, demo-seed 165 — identical to
the wave-1 gate. Its eight new pins are Docker-gated and **never executed**; each carries its failing condition in its doc
comment and none is claimed green.

**★ A claim in the task prompt was FALSE, and the agent proved it at two commits.** The prompt named five line numbers as
"fail-closed dated readers" to convert. They are nothing of the kind — all five are defence-in-depth throws inside **write**
helpers ("insert produced no row", "token bump found no row", two lock-invariant guards). None is a reader and none can fire
from "no record covers today". Converting them would have been meaningless work on the wrong code. The agent found the **real**
sites — the employment-profile resolver, the compliance read and the payroll calculation — and implemented against those.
*This is the nineteenth claim in this sprint's planning contradicted by the code, and it is the most consequential kind: not an
imprecise count, but an instruction pointing at entirely the wrong code.*

**★ The clock pin is the best-constructed test of the sprint, and the reason is worth teaching.** The ordinary test fixture
pins the host at **UTC midnight** — which is precisely the instant at which the UTC day and the Copenhagen day **agree**. A
clock pin built on the standard fixture would therefore pass under either clock and prove nothing at all. The agent pinned the
host at **23:30 UTC**, already the next day in Copenhagen, which is the only construction that can fail if someone later
unifies the clocks. The owner's ruling is also stated at both sites and both file headers, each explaining *why* rather than
asserting, and each warning against "correcting" it back.

**What it built, in plain terms.** Before this, a change entered in October dated 1 November was correct when entered and
silently stopped being reflected on the day it took effect, until some unrelated write happened to refresh things. A second
sweep in the existing poller now finds every employee whose cached value disagrees with the record covering today and
re-derives it. **Written as a divergence repair rather than as "apply today's scheduled changes"** — so it is idempotent and
also self-heals drift from any other cause, such as a restored backup or a legacy row.

**And the list that was supposed to find these employees was filtering them out.** The data-gap register inner-joined a profile
record covering today and then looked only for the *agreement* hole — so an employee with a *profile* hole was excluded from
the one list meant to surface them, while payroll and compliance failed closed for them every day. The join is gone, and the
response now says **which** record is missing and **when** a scheduled one will cover again, because those are two different
conversations with HR.

**Handoffs created:**
- **★ The frontend copy is now a lie.** The follow-up list is captioned "employees without an agreement code covering today"
  and tells HR to fix the agreement code. For a profile-hole row both halves are wrong, and **a row that will heal itself on 1
  November must not be presented as broken data**. Carried to wave 3's ux task.
- **The payroll calculation still throws uncaught for the same state**, because it lives outside the agent's declared scope.
  Correctly declared rather than reached into. **Dispatched as TASK-14114** to the payroll domain.
- **New exposure to watch at close:** the poller now writes `users.version` on every host's five-minute timer, where the
  previous sweep touched an unrelated table. A Docker test class holding one host beyond five minutes *with a deliberately
  divergent cache* could see a mid-test bump. Judged low risk and reasoned, not assumed — but it is new.

**Two findings registered:** **QUAL-173**, the naming lie now that a vikar-named service hosts this refresh (the honest name is
already recorded in the class doc; the rename was declined only because it would have collided with a wave-2 sibling in
`Program.cs`), and **QUAL-174**, a response field that can echo a record boundary which, for a new hire, *equals the hire date*
— the exact shape ADR-040 D7 exists to prevent. Pre-existing, not created here.

**Raised for a later ruling, not decided:** whether the named data-gap condition deserves a client-error status now that the
product can create the state deliberately, rather than the server-error status it inherited.

### TASK-14104 (endpoint layer) — complete. It caught the sprint's headline defect being reintroduced by a different fix.

**★ C1 — the blocker came back through a different door, and only the implementer saw it.** Owner ruling OQ-6's carry-forward is
a *second* routed write. Since wave 1 every such write bumps the concurrency token. The natural implementation returns the
**first** write's token — and then the drawer's very next save fails against a bump *this same request* performed. That is
exactly the Step-0b blocker that would have made every HR save fail forever, **reintroduced as a side effect of implementing an
unrelated ruling**. All three endpoints now stamp the token after the *last* write, and each write gets its own audit row so the
transition chain has no gap. On the users endpoint this also required restating the one-bump rule: it forbids *two parties*
bumping for *one* write, not two writes each bumping once.
*The lesson: a defect class does not stay fixed just because the fix was reviewed. A later change can re-open it from a
direction nobody was watching.*

**C2 — a silent wrong-data path in the same feature.** The revaluation helper took the request object. Reused unchanged,
carry-forward would have revalued the *scheduled* interval's absences using the values HR typed for *today* rather than the
merged values — wrong consumption data inside a write that looks entirely correct. The signature now takes the two written
values explicitly.

**C3 — a bug it wrote and then found itself**, reported rather than quietly fixed. Its first carry-forward addressed "the next
row after today", which is the truncating row only when the primary write is dated today. Schedule something for 1 October
while one exists for 1 November, and the nearest future row is the one the request just created — the carry-forward would have
done nothing while reporting success. Now addressed by start date, the same way the write selects its row, so read and write
agree by construction.

**Four more claims of mine falsified (running total: twenty-three):**
1. "Return the token on the PUT response" — **already done in wave 1**; verified at every site rather than assumed, nothing
   needed changing.
2. "Both reads anchor on the covering row, so such an employee gets a 404" — **false for the users read**, which reads a
   NOT-NULL column on a different table and has always returned success. Making it 404 would have been a **regression dressed
   as a correctness fix**. The scheduled block is null there instead.
3. "The existence probe returns a confusing refusal code" — **worse than I said: it silently succeeds**, letting the one
   edit-only verb create a net-new row for an employee whose only record is in the future.
4. "Give the audit row a discriminating action" — **the action column is CHECK-constrained to four values** and this sprint is
   schema-free, so the discriminator went into the payload as a source instead.

**Orchestrator ruling on the item it raised.** The existence probe now asks "covers today", so an employee whose only record is
scheduled gets a clean refusal instead of a silent net-new row. **Keep it.** The silent insert was the worse outcome: it let an
edit verb create data it is documented as incapable of creating. The lost repair path costs nothing today because the state is
unreachable through the product, and the new detector surfaces it. **If such data ever appears from an import, the answer is a
deliberate repair path, not an accidental one.** Recorded in the code so a future reader can re-open it.

**C5, carried to the test task:** two Docker-gated assertions change meaning and survive only on the current fixture — a count
that becomes two or more once a retired row is audited, and an equality that holds only because two different kinds of number
both happen to be 1 in the seed.

## Wave-2 gate — PASSED

| Check | Result |
|---|---|
| Build, non-incremental | **0 errors, 145 warnings** — baseline held across all four tasks |
| Contract regeneration | **succeeded**; all three new wire shapes present |
| Generated frontend types | **regenerated**; the guessed names matched the implementation, so no reconciliation was needed |
| Frontend type-check | **clean, 0 errors** (status read from the unpiped command) |
| Unit | **1244 passed** |
| Regression, non-Docker | **104 passed** |
| Demo-seed | **165 passed** |

The audit-projection catalog gained its row for the new event. Docker-gated facts remain unverified and are **not** claimed.

## Wave 3 — a planning correction before dispatch

**The picker and the visibility work share files, so they cannot run in parallel.** Step-0b split them into separate tasks so
the cut order would be *enforceable* — B0 not cuttable, the picker cuttable — and that reasoning was right. But the split was
never checked for **file disjointness**, and an effective-date input lives in exactly the drawer sections TASK-14107 is editing
for visibility and for the token fix. Two agents, two worktrees, one set of files.

**Correction: run them SEQUENTIALLY, not in parallel.** TASK-14111 (the picker) goes after TASK-14107 merges. The cut order
survives intact — cutting the picker now simply means not dispatching it — and the collision disappears. *This is the third
time this sprint that "these tasks look independent" has needed checking rather than assuming, and the second time the check
found a collision.*

**Wave 3a, dispatched in parallel (file-disjoint, verified):**

| Task | Scope |
|---|---|
| **TASK-14107** | The drawer token fix (the Step-0b blocker, third specification), B0 on all six surfaces plus the agreement-code field and the danger section, the OQ-6 prompt. **NOT CUTTABLE** |
| **TASK-14108** | The termination screen — new files, plus its own B0 obligation |
| **TASK-14109** | The history screen — new files, against the route TASK-14113 defined. **FIRST TO CUT** |
| **TASK-14115** | The follow-up list wording, which the detector work made wrong — **new, not in the original plan** |

**Wave 3b:** TASK-14111, the picker, after TASK-14107 merges. **LAST TO CUT.**

Route and navigation registration stays with the Orchestrator; the screen tasks declare what needs registering rather than
editing the two shared files.

### TASK-14114 (payroll gap condition) — complete, and it found a LIVE data leak, then found the same leak next door

Build `0 errors / 145 warnings`. Unit **1247** (1244 baseline + 3 new, not Docker-gated so genuinely green here), non-Docker
regression 104.

**★ A real ADR-040 D7 violation, proved rather than argued.** The resolver's exception embeds its as-of date **in its message**,
and that date is the segment start — which for a starter's first employed segment **is the hire date**. So attaching the
exception to a logger writes an employment date into the very logs the rule exists to keep them out of. The agent did not
assert this; it **reverted its own fix, re-ran the pin, and observed the hire date appear** where the caller's period start was
a different day. It then designed around it: the caught exception is deliberately neither logged nor wrapped, and diagnostics
carry the employee, the manifest and the caller's own period — nothing date-shaped about the employment.

**★ Then it found the same leak at the sibling site it had been told to copy**, and reported rather than reached across the
domain boundary: the compliance read attached the same exception to its logger, **contradicting its own comment eleven lines
above**. That is a live leak in shipped code, reachable whenever a mid-month starter's compliance read finds a gap.
**Fixed by the Orchestrator** (single site, precise diagnosis): the exception is no longer attached, and the one thing it told
us that the null path did not — *which* record is missing — is recovered as a discriminator carrying no date. Losing the stack
trace costs nothing, since the throw site is a single known call.

**Its test discipline is the best of the sprint.** Three unit tests, no database needed, and it verified them **in both
directions** — RED against the reverted production file (2 failed, 1 passed), then GREEN. The one that passes in *both* states
is the counter-test proving the other two are not vacuous. It also chose geometry deliberately: the plan covers all of March
while employment starts on the tenth, so the segment start and the period start are **different dates** — with a
window-aligned plan the leak assertions would have passed for the wrong reason.

**Three more claims of mine falsified (running total: twenty-six, nine of them mine):**
1. **"Two of the three sites were fixed" — only one was.** The others that catch this exception are older fail-*soft* handlers
   that return null and render gracefully, the opposite posture, and were never part of this condition.
2. **★ Its worktree was stale by the ENTIRE SPRINT**, sitting at the S140 close while master carried all of S141 — including
   the sibling fix it was told to match. Had it worked as handed over, it would have built against the wrong baseline and
   reported numbers that meant nothing. It noticed, confirmed the merge was a fast-forward, took it, and verified the target
   site was byte-identical before and after so nothing in its change depended on the merge. **This is the THIRD agent this
   sprint to report a stale worktree** — see the process note below.
3. **A change I framed as pure legibility is partly behavioural**: unifying the thrown path re-anchors the exception's date
   from the segment start to the period start. It checked first that nothing anywhere asserts on that value, found it entirely
   unpinned, and shipped it with a pin — and noted that leaving it alone would have meant knowingly preserving the D7 leak at
   the one site it was already editing.

**★ PROCESS FINDING — agent worktrees are created stale, and three agents caught it independently.** TASK-14105, TASK-14106 and
TASK-14114 all found their worktree pinned at the sprint-start commit rather than at current master, and all three merged
before starting. **Two of them said so explicitly as a warning.** The failure mode if an agent does not notice is severe and
quiet: it builds against a baseline missing the whole sprint, and its "baseline held exactly" report is meaningless. Until this
is understood, **every future agent prompt should tell the agent to check its worktree against master before starting.**

**Raised twice now, still undecided:** whether the named data-gap condition deserves a client-error status rather than the
server-error one it inherited, now that the product can create the state deliberately. Both sites must move together.

### TASK-14109 (history screen) — complete. FIRST TO CUT, and it did not need cutting.

`npx tsc --noEmit` exit 0, no errors. Full frontend suite **790 tests across 67 files, all passing**. Fifteen new tests.

**How a scheduled change reads differently, which is the owner's requirement applied to a screen.** Each row is tagged past,
current, or **"Planlagt – endnu ikke i kraft"** (planned, not yet in force), and a scheduled row also carries **its own tinted
row background** — so it is distinguishable in a screenshot, to a colour-blind reader, and by its wording, not by badge colour
alone. A booked change must never be mistakable for something that already happened.

**It preserved two distinctions the backend had deliberately made, either of which was easy to flatten:**
- **Two tracks, interleaved for reading only.** The rows are sorted into one table for legibility, but every row still shows
  exactly the dates the server gave it for its own track. **No combined period is invented**, which is precisely why the
  endpoint returns two lists rather than one.
- **★ A distinction it found in the endpoint's own doc comments, which the task never mentioned.** An interval that is *not* the
  first can still legitimately carry an empty changed-fields list, when a backdated edit splits a row and a later edit restores
  the values. That is **not** the same as "this is the first record ever". Both would naturally render as "nothing to report".
  It rendered them differently, rather than quietly hiding a distinction the backend went out of its way to keep.

**It refused to smooth over two refusals**, which is the right instinct for this project: a malformed date range surfaces as a
real warning rather than as "this employee has no history", and an out-of-scope employee and a non-existent one return the
**same** message, preserving the endpoint's deliberate choice not to let a caller discover which employee identifiers exist.

**★ FOURTH agent this sprint to find its worktree stale** — checked out at the S140 close, with no sprint documents and the
history endpoint absent from the generated types, despite the task briefing telling it the endpoint was "already built and
typed". It merged to current master before doing anything. That briefing line was true of the repository and false of the
environment the agent was handed, which is exactly the failure mode the process finding above describes.

Route and navigation registration declared for the Orchestrator rather than edited, per the collision constraint. **Held until
all three screens land**, so the two shared files are edited once.

### TASK-14115 (follow-up wording) and TASK-14108 (termination screen) — both complete

**TASK-14115 — the distinction that mattered is in the wording, and it got it right.** The list now names which record is
actually missing, per row, rather than always blaming the agreement code. More importantly it separates **"Skal rettes"**
(needs fixing) from **"Planlagt fra {date}"** (already scheduled to heal). The action note now explicitly warns HR *off*
correcting a row that carries a scheduled date — which is the guard against the real risk: someone "fixing" a healthy,
scheduled gap by destroying a colleague's scheduled change. It also verified against the backend's own contract that the
scheduled badge only appears once **every** missing side has a scheduled record, so a row missing two things with only one
scheduled still correctly reads as needing action. Nine tests.

**★ And it is where the stale-worktree problem finally bit.** Unable to regenerate the shared contract file (Orchestrator-only)
and facing a red type-check, it **hand-patched two fields into the generated types**, flagged it loudly as a stopgap, and asked
for a real regeneration at merge. That was the right call in a bad position — but a hand-edited generated file is exactly what
the freshness gate exists to catch. **Resolved at merge:** the hand-patch was discarded in favour of the real generation, and a
re-run of the generator produced **no diff at all** against what is committed, so the gate will pass.

**TASK-14108 — "eleven statuses" was wrong, and it checked rather than trusted.** Reading the endpoint directly it found
**8 distinct refusal shapes plus a success body encoding 4 further outcomes — twelve meaningfully distinct results**, all
handled with their own wording. It also found that **every** non-200 response is undeclared in the contract, not merely the two
the plan named, and hand-wrote and type-guarded all eight.

**It verified a claim instead of accepting it, and the verification changed the screen.** The plan said the token must come
from the terminated-inclusive read. True — and the *reason* matters: the ordinary reads filter on active employees, so they
return nothing once someone has left. The concrete consequence is that **an already-departed employee cannot be shown by
name** on the very screen most likely to be opened for them. It falls back to the identifier **with an explanation**, rather
than fabricating a name or hiding the gap.

**Two domain points it surfaced that no planning document had:** B0 needed a **second** field, the scheduled agreement change
on the users read, not only the profile one. And **termination does not cancel a scheduled change** — only deleting the profile
does, they are different code paths entirely — so someone can be scheduled to leave in November while a part-time change is
scheduled for October, and both will happen. That now gets a banner.

**It declared a reachability gap rather than leaving it silent:** nothing in the product links to the new screen, because the
person drawer belongs to a sibling task this wave. **Relayed to TASK-14107 while it is still running**, together with the two
facts above, so the link's surrounding wording is not written on false assumptions.

**Sixth and fifth stale worktrees respectively.** Both caught it before writing code.

### TASK-14107 (drawer fix + visibility) — complete. The blocker is fixed and PROVED.

`npx tsc --noEmit` clean. Frontend suite green. **It verified the fix discriminates**: reverted it, confirmed the new test went
red, restored it, confirmed green. That is the difference between a test that guards the fix and one that merely accompanies it.

**It got right the part both earlier specifications got wrong.** The profile write now sends the token step one just produced
**and writes that token back onto the shared user record**, not only onto the profile snapshot. Its own summary states the
consequence of the half-fix better than the plan did: *"Without that second half, the failure doesn't disappear, it just moves
to the next write in the sequence, which would have looked like a different bug."* Three specifications of one fix, two wrong,
each caught by asking rather than asserting.

**★ It refused to fabricate a signal that has nowhere to live.** The task told it all six surfaces' payloads already carry the
scheduled-change indicator. **True for the profile and users reads, false for the roster, people search and person-reference** —
those response shapes carry a job title and no such field at all, and adding one needs either a forbidden second call per row
or a backend contract change. It said so rather than inventing an indicator, and noted the dangerous half was already gone
there: since wave 1 those screens show **today's** value, never a pulled-forward future one.

**OWNER RULING 2026-09-14 — add the signal to all three.** The requirement stands as stated. **TASK-14116 dispatched** for the
backend marker, then a regeneration, then the frontend pass. The owner chose the complete fix over both the "accept the gap"
and the "only where HR can act" readings, consistent with every other ruling this sprint.

**It also closed the reachability gap** the termination screen had declared, adding the link from the drawer — worded, per the
relay, so it neither claims nor implies that ending employment cancels a scheduled change.

**Seventh stale worktree, and this one had to merge TWICE** during the task to pick up siblings landing while it worked.

## Wave-3 gate — PASSED

| Check | Result |
|---|---|
| Build | **0 errors, 145 warnings** — baseline held |
| `npx tsc --noEmit` | **clean** |
| Frontend suite | **847 passing, 72 files** |

**Route and navigation registration done by the Orchestrator**, as planned, so the two shared files were edited once rather
than by three agents in three worktrees. The history screen is a browsable destination and gets a sidebar entry; the
termination screen is a per-employee **action** page, reached from the drawer, and deliberately gets none.

**Wave 3b dispatched:** TASK-14116 (the roster/search/person-reference marker) and **TASK-14111, the date picker — the feature
every other task in this sprint exists to support, and the one item marked LAST TO CUT. It was not cut.**

### TASK-14116 (the scheduled marker) — complete, and its design choices are better than the brief's

Build `0 errors / 145 warnings`, unit **1255 passed** (+8), non-Docker regression 104. Contract and frontend types regenerated
by the Orchestrator; three new nullable date members present.

**★ It aggregated rather than ordered, and the reason is a correctness one.** The obvious lateral is "order by start, take
one". It used `MIN()` instead, because **an aggregate over an empty set returns exactly one row containing null**, so the join
cannot fan out *even on malformed data*. That matters in one specific place: the search read counts its total in a separate
step *before* the join, so a fan-out there would ship more rows than the count travelling with them. Its own summary is the
clearest statement of the principle anyone has written this sprint: *"`LIMIT 1` gives the same answer on good data and a
corrupted page on bad data."*

**★ It proved its most important test by breaking the thing the test guards.** It removed the zero-width exclusion from the
canonical rule — the exact defect that would announce a change HR had already **cancelled** — re-ran, and **exactly one test
failed, the right one**, with the other seven holding. Restored and re-verified. That is a pin demonstrated to discriminate,
not merely to pass.

**A repo rule reshaped the design, and the trade-off is recorded.** Non-constant SQL is a build *error* here, so a clean helper
method failed at three sites. The fragment became a spliced constant, at the cost of a fixed correlation alias. **Given up:** a
call site can no longer choose its alias. **Bought:** one definition of "scheduled" rather than three that drift apart.

**RATIFIED — the marker spans BOTH timelines, not just the profile.** The agent extended it to cover a scheduled *agreement*
change as well and asked for a ruling. **Correct, and it should stay.** The marker answers "is it safe to act on this row",
and the drawer writes the agreement code on every save. Profile-only would have left the owner's original defect intact one
field over — which is exactly the argument the codebase already makes for the drawer.

**Not extended to a fourth surface, and I accept its reasoning:** the team overview shows an agreement code, but prefers the
*period's stamped* value when a period exists, which is a historical fact about that month rather than a live value someone is
about to edit. The "acting on a stale assumption" hazard is genuinely weaker there. Recorded as a considered exclusion.

**It also fixed a stale comment wave 1 left behind**, which described a read as "the live row" — under future-dating the live
row and today's row are different rows, and "live" is precisely the wrong one.

**Eighth stale worktree, and the worst: 41 commits behind.** Its own note names the consequence exactly — the three reads it
was asked to extend would not yet have had wave 1's changes, so it *"would have 'fixed' code that no longer exists."*

**Owed, and carried to the close:** the two contract tests need the new field in their field lists, plus two behaviour cases
that need a database — a future row surfaces its date, and a **cancelled (zero-width) row surfaces null**. That second is the
highest-value untested behaviour in this task.

**The regeneration broke 42 type errors across 7 test files** — every fixture constructing those three shapes without the new
required member. Deliberately required-and-nullable, matching the neighbouring fields, so an always-present key distinguishes
"nothing scheduled" from "this response predates the feature". **TASK-14117 dispatched** to render the marker and repair the
fixtures together, since they are the same files.

### TASK-14111 (the date picker) and TASK-14117 (the marker) — both complete. **Nothing was cut.**

**TASK-14111 — the feature every other task existed to make safe.** Default stays **today**, so the common case, correcting
something now, is still one decision. Choosing a later date raises a notice *before* any click, saying plainly that nothing
changes today. Past dates still work; backdating was never this sprint's business and was not narrowed.

**★ It found that the sibling's wording becomes FALSE once a picker exists, and fixed it rather than shipping a contradiction.**
The scheduled-change notice said "if you save **now**…", which was safe only while no save could be anything but now. It added
an optional date so the sentence names the real one, and **proved the default path unchanged by keeping the sibling's own test
suite green without editing it.** That is composition rather than collision.

**And it verified a claim instead of assuming a convenient one.** It read the handler to confirm that name, email and
organisation apply **immediately** regardless of the effective date, because those are not dated facts — then shaped both the
label and the notice around that, so the control never implies a broader freeze than the backend actually gives.

**It declined a post-save confirmation, with the better reasoning.** Such a toast would have to distinguish four independent
outcomes with no field to key off, which is precisely the optimistic claim the task warned against. It left the notice, which
reads real server state, as the single honest source of "did this actually get scheduled".

**★ It found a THIRD stale doc comment. My plan named two.** The field a frontend developer reads to learn what they may send
still said the validator refuses anything later than today — the exact reader a stale contract comment misleads, and the one
least able to tell that the handler above now says the opposite. **Fixed by the Orchestrator**, with that reason recorded in
the comment itself.

**TASK-14117 — the marker renders, and it corrected my arithmetic.** A quiet informational badge, never a warning, on the
roster, people search and the person-reference chips. Wording says only *a change is scheduled from this date* — never what
changes — for two reasons it stated itself: the marker carries only a date, and **the date can come from either timeline**, so
claiming the title changes would sometimes be wrong.

**My per-file breakdown of the 42 errors was wrong and it checked rather than trusted.** The builder-shaped errors were spread
across **three** files, not one, and **an eighth file was missing from my list entirely**. The totals were right; the
attribution was not. It ran the type-checker itself before touching anything and counted.

**It added three tests nobody asked for**, on the grounds that shipping rendering with zero coverage was the wrong trade-off —
which is this sprint's own discipline applied without being told.

**Two considered exclusions, both accepted and recorded as decisions rather than oversights:** the missing-approver card, which
is an action view for assigning an approver rather than a routine roster read; and the history page's own person-picker, where
the marker would be redundant because the history itself shows every scheduled interval in full.

### ★ THE STALE WORKTREE ROOT CAUSE — diagnosed, after nine occurrences

Nine agents this sprint were handed a worktree missing the entire sprint. TASK-14117 found why:

> its worktree branch matched **GitHub's `origin/master` exactly** — but local `master`, the branch this project actually
> integrates on, was **45 commits ahead**.

**Worktrees are created from the remote-tracking branch, not from the local integration branch.** Since this project commits
locally and pushes at close, `origin/master` lags by an entire sprint by design. Every "stale worktree" was this.

**Consequences seen this sprint:** one agent nearly rebuilt code that no longer exists; one hand-edited a generated file to
compile, which is exactly what a freshness gate exists to catch. **Every agent caught it** — several only because the prompt
told them to look.

**Action owed at close:** write this into the agent-dispatch documentation, so the instruction is standing rather than
per-prompt, and state that "compare against master" means the **local** branch.

## Wave 3b gate — PASSED. All implementation complete.

| Check | Result |
|---|---|
| Build | **0 errors, 145 warnings** — baseline held every wave |
| `npx tsc --noEmit` | **clean** |
| Frontend | **864 passing, 73 files** (from 775 at sprint start) |
| Unit | **1255 passing** |
| Regression, non-Docker | **104 passing** |
| Demo-seed | **165 passing** |

**The pre-declared cut order was never used. C2, C1 and the picker all shipped.**

## Step 7a — the sprint-end review. BOTH lenses found real defects; both verdicts were BLOCKED-class.

**External (Codex): 1 BLOCKER / 1 WARNING — both in the history endpoint, both fixed.**

- **★ BLOCKER — a CANCELLED change displayed as still forthcoming.** The owner's delete ruling retires a scheduled row by
  **zero-width close**, leaving it in place covering no days. The history endpoint classified by "starts after today" and so
  reported a change somebody had deliberately called off as coming. **This is the cross-task shape the review exists to catch:**
  the retirement mechanism and the history screen were built in the *same wave, by different tasks*, and neither task's own
  review could see the other.
- **WARNING — a date-filtered history claimed a false beginning.** The first row inside the window was treated as the
  employee's first ever record, so the screen said "Første registrering" when it was not and reported no changes for a row that
  certainly changed something. **Confirmed to be reaching a screen**, not theoretical — the page renders that flag directly.

**The fix went further than the instruction, in two ways that matter.**
- It **reused the canonical rule rather than copying it**, extracting the "covers at least one day" half so the history read
  could take that half alone (a history reports past and current intervals too, so it cannot use the whole scheduled
  predicate). It then **verified the recomposed constant is byte-identical to the original** by running the composition and
  printing both statements — so the marker's behaviour provably did not move. Its stated reason for refusing an inline copy:
  *"that is exactly how two surfaces end up giving different answers about the same cancelled change, and nobody notices until
  HR sees one."*
- It applied the exclusion to the **agreement** table too, which has **no retirement path today**, on the grounds that a rule
  applied only where a writer currently produces the shape *"would quietly expire the moment the agreement side grows a
  delete."*

**And its refinement to the second fix is subtler than my instruction.** I said fetch the row before the window. It anchored on
the row before **`rows[0].EffectiveFrom`**, not before `from` — because an interval that *straddles* `from` is itself inside the
window, so anchoring on `from` would have compared that row **against itself**, producing an empty changed-field list. The same
defect, surviving the fix, in the one case a reviewer would be least likely to construct.

**Its pins are driven through the real endpoints, never a hand-written imitation of the retirement idiom**, with the reasoning
stated: the defect *was* a disagreement between the writer's idiom and the reader's assumption, so a pin that fabricates the
idiom could drift alongside the writer and stop catching it. Plus a counter-test, so the fix cannot be satisfied by hard-wiring
the flag to false.

**Contract unchanged** — verified by regenerating and diffing, so nothing to regenerate and the shipped screen needs no change.
**RATIFIED deviation:** it edited a file belonging to another task, outside its original fence. Correct — the fence was a
wave-2 collision measure, that wave is closed, the edit is additive and provably text-preserving, and the alternative was the
duplicated rule I had explicitly told it not to write.

Build `0 errors / 145 warnings`, unit **1255 passing** after the merge.

**Internal (Reviewer, `claude-fable-5-1`): `verdict: BLOCKED` — 1 BLOCKER / 3 WARNING / 5 NOTE. All absorbed.**

It read every production diff line by line across both repositories, the router, three write endpoints, the poller, the gap
detector, the history endpoint, the settlement anchor and every new screen; traced the concurrency token through **every** write
in the drawer's save sequence including the new carry-forward second writes; and read every Docker-gated pin **by construction**,
since none has ever executed.

### ★ B1 — the sprint's own defect class, reintroduced by its LAST task, with the screen actively misdescribing it

**What it does to a real person.** A colleague schedules "0.6, Department Head, from 1 November". HR opens the same employee,
picks **1 December** in the new picker, changes only the display name, and saves. From 1 December the employee **silently
reverts** to today's fraction, today's title and today's agreement code. The colleague's decision, meant to run indefinitely,
lasts thirty days. The payroll wage-type key reverts with it. Nobody chose it, and it is recorded as an ordinary edit — so the
destruction is not even distinguishable in the audit trail, which is the condition the owner attached to OQ-5.

**The backend is correct throughout.** The same-values no-op compares against the row covering the *requested* date, which for
a date at or after the scheduled change is the scheduled row — so the router correctly splits it. **The defect is that the
drawer pre-fills a form with the wrong period's values for the date being written**, and sends every field on every save.
Carry-forward cannot rescue it: the resulting row is open-ended and the carry target requires a bounded one, so the checkbox is
ignored.

**And the copy is wrong in exactly this case**, which is worse than silence: the picker promises the values "forbliver som nu,
indtil {picked}", untrue when a scheduled change intervenes; and the notice can render a **chronologically impossible**
sentence. The dirty-check also diffs against today's values, so for the common "edit an unrelated field" case no prompt appears
at all.

**Why no test caught it, which is as instructive as the defect.** The picker's own suite schedules its fixture change for
1 December and only ever picks 1 October — with a comment saying so. **The one relationship that matters, picked ≥ scheduled,
is excluded by the fixture's construction.** Every backend pin is today-dated or single-row.

**This is the SECOND time this sprint that a fix for one ruling re-broke something another task had just fixed.** It is the
strongest possible argument for why a whole-sprint review exists separately from per-task review: neither task's own reviewer
could see the other's mechanism.

### The three warnings — two of them FALSE REDS that would have cost a debugging session each

- **W1 — two agreement-side pins asserted a field name that does not exist** (`scheduled` where the member is
  `scheduledAgreementCode`). They would have failed on the first database run **while the payload was entirely correct**.
  **And my sprint-log claim that "the guessed names matched the implementation, so no reconciliation was needed" was false** —
  true of the profile side, false of the agreement side. **Thirty-first falsified claim; eleventh of mine.**
- **W2 — the delete-audit pin read the wrong audit row, and would have failed in a way that MIMICS the defect it guards.** It
  read the *latest* deleted row, which is the retirement row, not the main one. A reader seeing it fail would have concluded
  "the audit recorded the future row's values" — precisely the pre-sprint defect — **while the production code was right**. That
  is the most expensive kind of bad test: it could have got a correct fix reverted.
- **W3 — the owner's ruled marker had no executable behaviour test at all.** The only pins were string-contains checks against
  the SQL text, which pass whether or not the query returns the right date. **The sprint log itself had recorded the two
  database cases as owed and they were never written.** Now six facts, including the case the owner's requirement hinges on: a
  **cancelled** change surfaces null, seeded as a genuine zero-width row on all three reads.
- **Two notes absorbed:** a pin whose equality held only because two different kinds of version number both happened to be 1 in
  the seed (flagged once already and carried); and a pin stating a failing condition that never existed in any commit.

**RATIFIED deviation:** the test agent edited a file belonging to another task. Its reasoning is right — the fence existed to
prevent concurrent double-ownership during authoring, not as a permanent bar, and every wave is merged.

**Verified sound, and this list is the reason the blocker is survivable:** the concurrency-token chain holds on every path a
whole-sprint view exposes, including the carry-forward second writes, each with its own chained audit row; the poller's refresh
bumps the token **and** writes a system audit row, so "every token transition has an audit row" holds; the delete now records
the aggregate token in both version columns while the event keeps the row version, so the two number-spaces are no longer mixed;
the history endpoint 403s unknown identifiers, serves no token and carries only record dates; **both data-protection log leaks
are closed**; and the clock pins and the settlement divergence-window pin are the only constructions of their assertions that
can fail.

Build `0 errors / 145 warnings`, non-Docker regression **104 passing** after the pin fixes merged.

### The B1 blocker — fixed, and the fix is a three-way classification rather than a patch

The agent's own diagnosis is the clearest statement of it: *"The picker itself was never the thing lying to HR; it was quietly
handing the save a form full of the wrong period's numbers."*

It classifies the picked date against each scheduled change's **own window**, not against "is there one":
- **before** — unchanged behaviour; the write may truncate a later scheduled change and the existing choice still applies.
- **covers** — the picked date falls *inside* the scheduled change's period, so the drawer **re-baselines the fields from the
  scheduled row's values**, per field, and **only where HR has not deliberately diverged that field** — so changing the date
  never silently discards something just typed. An untouched field then saves as a genuine no-op, preserving the colleague's
  schedule; an edited field overrides only from that date forward, which is the intended behaviour.
- **beyond** — the scheduled change has its own end and the picked date is past it, so there may be a further row the payload
  never carries. **It refuses rather than guesses**: save disabled, with an explanation.

Both wrong sentences are corrected, and the now-inert carry-forward checkbox is no longer shown in the shape where the backend
ignores it. **Eight new tests, proved RED-first** by stashing the fix and confirming all seven discriminating ones failed.

**Gates after the merge:** `npx tsc --noEmit` clean; frontend **872 passing across 73 files**.

**And the worktree problem bit one last time, twice in one task** — 42 commits behind on resumption, then a second smaller
fast-forward for a Step-7a fix landing mid-work. It verified by diff that neither touched its files before merging.

---

# SPRINT CLOSE

## Step 7a — final outcome

**Three cycles per lens. Both clear at the close commit.** Codex `APPROVED` (clean at cycles 2 and 3); Reviewer
`APPROVED-WITH-WARNINGS` with two non-close notes. Artifacts in `.claude/reviews/`, anchored at `dccf87e` with the docs-only
verification recorded in each, because the reviewed code and the closing code are byte-identical.

**Cycle 2 produced the sprint's most pointed finding: fixing a pin that could never FAIL produced one that could never PASS** —
the third instance of the false-red shape in one sprint. And it caught that the log's claim "both wrong sentences are
corrected" was untrue; one still rendered directly above the notice contradicting it.

## Final verification

| Check | Result |
|---|---|
| `dotnet build StatsTid.sln -c Release --no-incremental` | **0 errors, 145 warnings** — the baseline held in every wave |
| Unit | **1255 passed** (+11 vs S140's 1244) |
| Demo-seed | **165 passed** (unchanged) |
| Regression, non-Docker | **104 passed** (unchanged) |
| Frontend | **873 passed across 73 files** (+98 vs S140's 775) |
| `npx tsc --noEmit` | **clean** |
| Contract + generated types | regenerated twice; the regenerator produces **no diff** against what is committed |

**Every exit status was read from the unpiped command.** Docker is unavailable on this machine (standing), so **every
Docker-gated fact written this sprint executes for the first time in the watched CI run** and none is claimed green here.

## What shipped

**Increment 4, whole — the pre-declared cut order was never used.** Termination screen, effective-date picker, HR-gated
history. Plus the precondition that turned out to be nine deliverables, the settlement anchor, and **B0**, the owner's
visibility requirement.

## The three numbers worth carrying forward

1. **Thirty-two claims in this sprint's planning were contradicted by the code; twelve were the Orchestrator's own.** Including
   an instruction pointing five line numbers at entirely the wrong kind of code, and an acceptance criterion that *mandated* a
   domain-wrong result.
2. **Twice, a fix for one ruling re-broke something another task had just fixed** — and a third time, a fix for a test defect
   introduced the same defect inverted. This is the argument for whole-sprint review as its own step: no per-task reviewer can
   see another task's mechanism.
3. **The two most valuable findings came from the owner, not from either lens.** *"Should a scheduled change be visible to an
   HR employee looking at a page?"* collapsed two separately-written defects into one requirement. *"Why not update the
   system's clock to the Danish clock?"* exposed a live user-facing bug — a Danish user working after midnight records changes
   as effective yesterday — and traced it to a single frontend call. **The lenses are strong on whether a mechanism is correct
   and blind to whether it should exist.**

## Owed after close

- **Push, then the watched CI run.** Every Docker-gated fact runs for the first time.
- **Worktree teardown** — the Step-0a finding, plus this sprint's own.
- **Registers:** QUAL-171 (fixed), 172, 173, 174, 175 recorded; the business-date move and the login-token staleness decision
  are roadmap items with the analysis attached.

## Post-close CI — run `34942169407`, two jobs red, BOTH of them the Orchestrator's process failures

**Neither is a defect in the sprint's code.** Both are the same omission repeating from S140, which is the part worth
recording.

**1. The lazy-route coverage guard — and I never dispatched the wave that was meant to satisfy it.** The two new pages were
not registered in `frontend/e2e/lazy-routes.spec.ts`, so the guard that fails when `App.tsx` gains a lazy page nothing
exercises did exactly that. **The plan carried TASK-14110 with this precise job, annotated "(S140's first CI red)".** Waves 1,
2, 3 and 3b were dispatched; **wave 4 was forgotten**, and the sprint closed without it. So the guard has now caught the same
omission in two consecutive sprints — which is the strongest possible evidence it earns its place, and a reminder that **a task
written down is not a task done.**

**2. Four documents I materially changed still declared the previous sprint's freshness anchor** — the HR process register, the
model-routing register, the quality register and the quality matrix. **`SPRINT-140.md:1039` records the identical failure last
sprint**, naming the same four files.

**Both fixed.** The two pages registered with the reason written at the site; the four anchors bumped.

**The honest reading:** this sprint's review layer was the most effective it has ever been on *code*, and both close failures
are things no code review looks at — a dispatch I never made, and a marker I never bumped. The lenses reviewed what I gave
them. Nothing reviewed whether I gave them everything.

## Post-close CI remediation — run `34942169407`: 9 of 1928 red, and **NONE was a product defect**

**The result that matters: 1919 of 1928 Docker-gated facts passed on their first real execution**, including everything this
sprint wrote — the settlement anchor, the five router shapes, the delete with its retirement audit, the token round-trips, the
clock pins at 23:30 UTC, and the scheduled-change marker's cancelled case. **Every one of the nine failures was test-side.**

### The one that looked worst, and was not

`Poller_SpecialHoliday_CandidateYear_IsHiresOwnCalendarYear` failed with a message about candidate-year arithmetic — which read
exactly like *"the QUAL-168 fix leaked from the vacation series into special holiday and changed a legal geometry."* **It had
not.** A read-only trace established that the entire special-holiday settlement pass sits behind a **fail-closed feature gate**
that this file's host helper never switched on, so the fact waited thirty seconds for a row that could never appear. The trace
also confirmed structurally that the leak is impossible: **two independent methods with independent generation**, and the
untouched lower bound carries its own comment saying it is deliberately not mapped.

**This is why it went to a trace instead of a guess.** The failure message pointed squarely at the domain; the cause was one
missing line of test configuration. Fixed, with that explanation written at the site so the next reader does not start by
suspecting the legal geometry.

### The other eight — seven token consequences, and one claim of mine that was wrong

Seven are pre-existing tests still speaking the **old** concurrency dialect: hard-coded tokens, or counts of versions and audit
rows that now advance further because owner ruling OQ-3 moved the token to the employee record and made it bump on every write.
All were repaired by reading the live token rather than hard-coding one, and by deriving the correct new counts **from the
production code rather than from what the run printed** — a test updated to match observed output is not a test.

**★ The eighth was not a token consequence at all, and my dispatch said it was.** `PUT_TodayDatedEdit_ApplyUntilScheduledChange`
already read its token live. Its miscount is independent and **pre-existing in a test this sprint wrote**: the fixture's today
row starts 400 days back, so an edit dated today cannot be an in-place update — that case requires the row to *start* on the
requested date — it is a genuine split, which closes one row and inserts another, giving three rows rather than two. Routing
behaviour that predates this sprint by many sprints. **Thirty-third falsified claim; thirteenth of mine.**

### And two process failures, both mine, both repeats of S140

- **The two new pages were never registered in the route-coverage guard** — because **wave 4 was never dispatched**. The plan
  carried that task, annotated *"(S140's first CI red)"*. I dispatched waves 1, 2, 3 and 3b and closed the sprint without it.
- **Four documents I materially changed still declared the previous sprint's freshness anchor.** `SPRINT-140.md:1039` records
  the identical failure, naming the same four files.

**The honest reading of this close:** the review layer was the most effective it has ever been on code — it caught a defect that
would have broken every save, a picker that would have refused every date it offered, a payroll figure built on a superseded
fraction, a live data-protection leak, and three tests that could not do their job. **And nothing red in this run was code.**
Both process failures are things no code review looks at: a dispatch I never made and a marker I never bumped. The lenses
reviewed what I handed them; nothing checked whether I handed them everything.

**Verification before the remediation push:** build `0 errors / 145 warnings` (baseline held), unit **1255**, non-Docker
regression **104**, demo-seed **165**, frontend **873**, type-check clean. Every exit status read from the unpiped command.
