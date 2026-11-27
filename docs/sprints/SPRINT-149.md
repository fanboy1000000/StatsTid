# Sprint 149 — the dependency refresh: every dependency current, on .NET 10, and nothing changed

| | |
|---|---|
| **Status** | **OPEN** (opened 2026-11-27). The plan is **FINAL** (draft 3.2, plus "final + 1") after Step 0b READY on both lenses at cycle 5. **Wave 0 is merged, and CI-W0 is green (run `45997941910`), so A0 = `719e7996`.** Wave 1 (TASK-14903, the login-token library) and wave 4 (TASK-14912, the frontend toolchain, on `s149/w4`) are dispatched in parallel. |
| **Plan** | `.claude/plans/PLAN-s149.md` **FINAL (draft 3.2)**: Step 0b READY on both lenses on 2026-11-27 after five cycles (see "Plan Review (Step 0b)"). Drafts 1–3 by the `planner` (Opus 5.5), 3.1 and 3.2 by the Orchestrator. From here it changes only by ruling, each change a "final + n" row of its Revision history. **20 dispatch ids** (TASK-14900 … 14919), six of them reserved for the runtime wave's fallout (14909–14911, 14917–14919) and DROPPED if unused, plus 3 paired test passes (14903-T, 14904-T, 14905-T); eight CI runs |
| **Theme** | Bring every dependency to current in one deliberate refresh: the NuGet and npm packages, the GitHub Actions, CI's Node, and the .NET runtime and its base images (.NET 8 → .NET 10). **Nothing may change in behaviour**: no pay figure, seeded row, API contract, persisted hash or access decision. That is proven by pins, some of them new and taken on .NET 8 before anything moves. Dependabot is then set up so this backlog does not quietly rebuild. |
| **Owner rulings in force** | **"Lets do all the updates now"** (2026-11-26), after the Orchestrator's recommendation to split the 34 stale Dependabot pull requests into a small batch now and the .NET 10 move later. **Q1 = (a) "Updates are S149"** (2026-11-27, AskUserQuestion): S149 is the refresh alone; S150 takes over the old S149, QUAL-284 first and then stage 1. This **supersedes** the ruling of 2026-11-26, "Register High; S149 first" (recorded in ROADMAP § 1, in its Tier-2 Impact Assessment, and on the QUAL-284 register row). **Q2 = (a) ".NET 10"** (2026-11-27): .NET 11 and its framework-tied packages are recorded hold-backs. **The merged `add-claude-github-actions-1772139476170` branch was deleted** (owner-approved 2026-11-26; it was fully contained in master, 2,052 commits behind). |
| **Predecessor** | S148: close `df582c20`, **CI GREEN `45781374620`** (all seven jobs). C-5 `d092595f` pushed, **CI GREEN `45815604149`** (all seven jobs; Unit 1848, DemoSeed 180, Regression 2978, read from the job log by suite name). |
| **Base commit (Step 7a)** | **`df582c20`**, the S148 close commit (`docs/WORKFLOW.md` step 7a), so the post-close commit `d092595f` falls inside S149's Step-7a diff |
| **Orchestrator model** | Opus 5.5 seat, never switched (owner ruling 2026-09-29). Drafting: `planner` (Opus 5.5). Judgment: `reviewer` on Fable 5.2 (the refinement's cycle 1 artifact carries `reviewed-by-model: claude-fable-5-2`). Implementation by role name, on Opus and cheaper. |
| **Trials and checks this sprint runs** | **The Ponytail-ideas trial, second measured sprint** (decided at S150's close): M1–M6 in this sprint's model-routing register row. **FAIL-009's ruling is in force:** the routing guard fails closed (`"onFailure": "block"`, applied 2026-11-26). The telemetry log's synthetic `ALLOW` line at `2026-11-26T11:57:03Z` (S148 C-5's pipe test) is not a spawn and is not counted. |
| **Refinement** | `.claude/refinements/REFINEMENT-s149-dependency-refresh.md`, **r2, READY** (2026-11-27). See "Refinement record" below. |
| **Entropy scan (Step 0a)** | 2026-11-27 at `d092595f`.<br>**Worktrees:** none beyond the main checkout; local branches: master only.<br>**Remote:** the stale `add-claude-github-actions-…` branch is deleted (closing S148's DEBT); Dependabot's 34 update branches remain, and close with their pull requests in W5.<br>**Untracked:** `.claude/plans/s145/{b0,ca1,ca3,cb1,cb5}/`, `.claude/plans/s146/ADR-043-draft.md` and `Tidligere udbud/`, as at S148, none under `src/`, `tests/`, `tools/` or `frontend/`. **DRIFT, fixed in this sprint's opening commit:** `.claude/plans/s148/C5-routing-row-draft.md` is cited by FAIL-009 and `SPRINT-148.md` but was never committed.<br>**Anti-patterns:** no `FindFirst("scopes")` (FAIL-001); no project outside the Rule Engine references it (PAT-005). Every endpoint without a visible authorization call is `/health` or `/api/auth/login`, both anonymous by design. Hard-coded localhost appears only in the frontend's health page, which is QUAL-023, registered.<br>**KB paths:** 258 cited, 6 non-resolving, all benign and as at S148: an elided path in PAT-010, ADR-018's `EventStoreInterfaceTests.cs` (the ADR says "never created"), ADR-023's `UserManagement.tsx` (deleted since), and the three runbooks ADR-025 designs but never built.<br>**Shortcuts (PAT-032):** 24 comments under 16 tags. Each tag's register row is open, except SEC-059, whose FIXED row names its two `ComplianceEndpoints.cs` comments as known residuals to revisit. CLEAN. The SMALLEST COMPLETE CHANGE block is identical in all nine places (`f57be923033c5ab9`).<br>**Freshness (soft, CI's docs job on `d092595f`):** five anchored docs trail by more than three sprints: the security, quality and HR follow-up registers, `SPRINT-140.md`, and `docs/sprints/INDEX.md` (**DEBT**, as at S148). The design-sync advisory ("the UI kit changed since the last Claude Design sync", PAT-011) is **DEBT**. |
| **Baseline** | S148 final (`SPRINT-148.md`): Unit 1848 · DemoSeed 180 · Regression 2978 (CI) · Smoke 10 · vitest 122 files / 1405 · e2e green. Build 0 errors / 126 warnings; CA2100 distinct 114 locally, 115 in CI's ratchet. Re-measured at the first gate, never carried forward |
| **Test Verified** | — (pending) |

## Plain-language summary

S149 is a maintenance sprint, and it holds itself to the same evidence standard as a feature sprint. It changes no product behaviour, which is exactly what it must prove.

The system has been built on .NET 8 since the start. Microsoft stopped supporting .NET 8 on 10 November 2026. Since May, Dependabot (GitHub's update robot) had opened 34 update proposals that nobody triaged. They were also only part of the picture: Dependabot opens at most a handful per kind of package, so updates such as Vite 5 → 8 and Swashbuckle 6 → 11 never appeared.

The owner asked for all of it now. So S149 moves everything to current:
- the backend to .NET 10, the long-term-support release;
- the database driver, the login-token library, the API-description generator and the test tools to their current versions;
- the frontend to React 19, React Router 7 and Vite 8;
- CI to current GitHub Actions and Node 24.

The difficulty is proving that nothing changed. Some changes would be invisible to today's tests. One example: payroll stores a fingerprint (a hash) of each export, so it can tell a repeated export from a new one. Today's tests compute the expected fingerprint on whatever runtime they run on, so if .NET 10 produced different bytes, both sides would move together and every test would stay green. So the sprint's first step, while still on .NET 8, records the actual values as fixed expected answers.

Then the pieces move one at a time:
1. the login-token library, on its own and checked by the security agent;
2. the other libraries;
3. the .NET 10 runtime itself, last.

If anything breaks, there is only one possible cause. The database stays on PostgreSQL 16. Moving it is a behaviour change of its own and gets its own sprint.

## Task ledger

*Filled as tasks are dispatched. The close guard requires every `TASK-<n>` this log names to appear here with DONE, CUT, DEFERRED or DROPPED (`docs/WORKFLOW.md` step 7, gate 6).*

| Task | Disposition | Merge on master | Note |
|---|---|---|---|
| TASK-14900 | DONE | `93da6dc7` | wave 0, the persisted-bytes goldens G-1 … G-5 |
| TASK-14901 | DONE | `b6efa4a2` | wave 0, the login pins L-1 … L-13 (with L-5b, L-9a, L-9b) and L-11 |
| TASK-14902 | DONE | `90abdfa7` | wave 0, CI hygiene: the Actions' newest majors, Node 24 |

## Refinement record (Step 4)

**r1** (2026-11-26, Orchestrator) was written after the owner's "Lets do all the updates now". It used the facts gathered that day: the installed SDKs, all 16 csproj on `net8.0`, the 7 Dockerfiles, the CI actions and Node 20, `dotnet list package --outdated` and `npm outdated`, the Swashbuckle → `openapi.json` → `api-types.ts` chain, and the Testcontainers fixtures.

**Cycle 1, both lenses**
- **Codex:** `.claude/reviews/S149-refinement-codex.raw.md`. 0 BLOCKER, 3 WARNING, 2 NOTE.
- **Fable `reviewer`:** `.claude/reviews/S149-refinement-reviewer-c1.md`, `reviewed-by-model: claude-fable-5-2`. 0 BLOCKER, 7 WARNING, 9 NOTE; **APPROVED-WITH-WARNINGS**.

The two lenses agreed on the two largest points:
- .NET 10's login library (JwtBearer 10) requires IdentityModel 8, so the "runtime-only" wave was not runtime-only.
- No existing test can detect a change in persisted serialized bytes: `PayrollExportManifest.ComputeContentHash` → `payroll_export_records.content_hash`, `HoursDigest` and `EventSerializer`. Every test computes both sides in the same process.

**r2** (2026-11-27) absorbs every finding. The refinement's closing section lists each change.
- **The order.** The literal goldens go first, on .NET 8. Then IdentityModel 8 on .NET 8, owned by the security agent. Then the other libraries on .NET 8. The runtime moves last.
- **New acceptance criteria.**
  - The OpenAPI criterion is an empty `api-types.ts` diff against `df582c20`, plus an allow-listed comparison of the parsed spec.
  - A correct Npgsql inventory: the `-infinity` default, and untyped `date` reads that return `DateOnly`. The "legacy timestamp switch" r1 named does not exist.
- **New rules and limits.**
  - An explicit out-of-scope section: Postgres 16, xunit v3, and parts 1, 2 and 4 of the ROADMAP dependency item.
  - An `ignore` rule for every hold-back.
  - The CA2100 ratchet may not rise without a ruling.
- **New records.** Re-pointing every S149 reference after Q1, and a completed docs list.

**Owner answers (2026-11-27):** Q1 = (a) "Updates are S149"; Q2 = (a) ".NET 10". r2 is **READY**.

## Plan Review (Step 0b)

**Draft 1** (2026-11-27, `planner` on Opus 5.5) came from refinement r2. **Every target version was re-verified** against nuget.org, npm, GitHub releases, Node's schedule and .NET's release metadata. That re-check changed four of the refinement's targets:
- **React Router 8**, not 7, because v8 removed `react-router-dom`;
- **Swashbuckle 10.3.1**, not 11, because 11 runs only on .NET 10/11 and would force a second filter rewrite;
- **jsdom 30, jest-dom 7 and TypeScript 5.9**, held back under the young-major rule or for lack of tool support.

It also found a breakage the refinement had missed. Npgsql 10 renamed its tracing tags, so the performance test's query counter would count zero, and two of its four checks would pass without measuring anything. W2 repairs the counter and adds a "count is above zero" check.

The draft has 13 brief contradictions, and six questions (NR-1 … NR-6).
- **The Orchestrator ruled NR-1, NR-2, NR-4, NR-5 and NR-6 (a), as drafted**, before Step 0b:
  - NR-1: the event goldens cover all 113 registered types, with a fact that fails when a new type has no golden;
  - NR-2: the filter test is translated one-for-one, and a comparison of the whole `openapi.json` carries "unchanged";
  - NR-4: xunit 2.9.3's new analyzer warnings are reported and registered, not fixed;
  - NR-5: every young major is held back, with a revisit date;
  - NR-6: the Tier-2 Impact Assessment gets its three missing parts.
- **NR-3 went to the owner** (AskUserQuestion, 2026-11-27): **(b) "Install it"**, the .NET 10.0.4xx SDK on the owner's machine before W3, so local and CI build with the same SDK band.
  - The first attempt (`winget`, 2026-11-27 11:48) failed at Windows' elevation prompt (installer exit 1602, "Failed to elevate"). The owner runs it, or approves the prompt, before W3. That is plan step O-0.
  - **O-0 done (2026-11-27):** the owner ran `winget install --id Microsoft.DotNet.SDK.10 --exact --version 10.0.404`, which reported "Successfully installed". `dotnet --list-sdks` now shows **8.0.423, 10.0.302 and 10.0.404**, installed side by side. The repo root still resolves 8.0.423 (`global.json` `8.0.0` with `latestFeature`) until W3's F-1. After that it resolves the 4xx band, the same band as CI's `10.0.x`.

**Brief contradiction landed in the docs:** the ROADMAP's dependency item said "CI runs no vulnerability scan". In fact `ci.yml:107-118` runs `dotnet list package --vulnerable --include-transitive` and fails on High or Critical; it is npm that has no audit. Corrected in the ROADMAP working tree.

**Cycle 1, both lenses, on draft 1**
- **Codex:** `.claude/reviews/S149-0b-codex-c1.raw.md`. 0 BLOCKER, 3 WARNING; READY-WITH-WARNINGS.
  - W1: the frontend wave's isolation contradicted the general merge instructions.
  - W2: the key-length pin tested token issuance, not the validator.
  - W3: the mutation worktrees started from master, before the new tests existed.
- **Fable `reviewer`:** `.claude/reviews/S149-0b-reviewer-c1.md`, `reviewed-by-model: claude-fable-5-2`. 0 BLOCKER, 3 WARNING, 8 NOTE; APPROVED-WITH-WARNINGS (READY-WITH-WARNINGS).
  - W1: two tasks would hand back a solution that does not build (an NU1605 downgrade; the Unit filter test).
  - W2: the runtime wave had no fallout owner for SharedKernel, the Rule Engine and Auth.
  - W3: the reason for leaving the two SHA-256 id derivations unpinned was false; the existing fact compares two calls in the same process.
  - N1–N8: the OpenAPI comparison repeated at W3; one "unedited" command over every frozen path; Npgsql 9.0's breaking changes classified too; the four `vi.mock` strings; the restore audit; a shape column for the filter-test translation; `required` arrays compared as sets; a CI budget of eight runs, not seven.

**Orchestrator rulings on cycle 1: every finding absorbed.** NR-1's second half was re-ruled to pin the two derivations (new golden G-5). Fable N5 was pre-ruled: if the .NET 10 SDK's restore audit turns a Low or Moderate advisory (NU1901/NU1902) into an error, `WarningsNotAsErrors` for those two codes is added, matching CI's existing policy. It is applied only if it fires (F-10).

**Draft 2** (2026-11-27, `planner`) absorbs all 14 findings, mapped in its section "Step 0b cycle 1 → draft 2". The plan grew from 957 to 1,078 lines.

**Cycle 2, both lenses, on draft 2 (verification)**
- **Codex:** `.claude/reviews/S149-0b-codex-c2.raw.md`. Its three cycle-1 warnings are **CLOSED**. One new WARNING and one NOTE (unproven); READY-WITH-WARNINGS.
  - **The WARNING.** The new "`required` as a set" comparison turned members into text before comparing them. Codex changed `"label"` to `["label"]` in memory, and the helper reported zero differences: a malformed schema would pass.
  - **The NOTE.** The freeze check on the golden-data `ItemGroup` reads a textual diff, so an edit far enough from the matched line could escape it.
- **Fable `reviewer`:** `.claude/reviews/S149-0b-reviewer-c2.md`, `reviewed-by-model: claude-fable-5-2`. All 11 of its cycle-1 findings are **CLOSED**. One new WARNING and three NOTEs; READY-WITH-WARNINGS.
  - **The WARNING.** The plan freezes the wave-0 test files, and it also requires that no test file mention ".NET 8" after W3. Yet its own wording ("captured on .NET 8") would put that phrase into the frozen files.
  - **The NOTEs.** The conditional F-10 edit should not sit inside a Haiku sweep. M-9 should not need a second `node_modules` install. The plan's growth is proportionate.

**Orchestrator rulings on cycle 2: every finding absorbed.**
- **The comparison.** Set comparison applies only to a schema's `required` keyword, and only to string members, with no coercion. Example payloads are compared in order.
- **The golden data.** It moves to its own imported file, and FROZEN-CHECK covers that whole file.
- **The frozen files.** They name no runtime version ("the pre-refresh runtime", citing ADR-045).
- **F-10.** The sweep reports the restore line, and the Orchestrator applies F-10.
- **M-9.** It runs in place inside the W4 worktree.

**Draft 3** (2026-11-27, `planner`) absorbs cycle 2.
- **The comparison helper is rewritten:** set semantics apply only to the schema `required` keyword, with string members only. All seven read-only checks behave as required.
- **The golden data:** `Goldens.props`, with M-12 to test its freeze check.
- **The frozen files** name "the pre-refresh runtime".
- **F-10** belongs to the Orchestrator, and **M-9** runs in place.

The plan is now 1,124 lines.

**Cycle 3, both lenses, on draft 3 (verification)**
- **Codex** (`.claude/reviews/S149-0b-codex-c3.raw.md`). Its cycle-2 WARNING is **CLOSED**: it re-ran `"label"` → `["label"]`, → `42`, and `required` → a string. 1 new WARNING and 1 NOTE (unproven); READY-WITH-WARNINGS.
- **Fable** (`.claude/reviews/S149-0b-reviewer-c3.md`, `reviewed-by-model: claude-fable-5-2`). Its four cycle-2 items are **CLOSED**. It ran the helper against twelve synthetic variants, and each one gave the expected exit code. 1 new WARNING; READY-WITH-WARNINGS.
- **Both lenses found the same defect:** M-12 cannot go red. Its mutation is uncommitted, while FROZEN-CHECK compares two commits, and before A0 exists that check does not apply at all.
- **Codex's NOTE:** the `Import` check matched a substring, so `<Import Project="Goldens.props" Condition="false" />` would pass.

**Orchestrator rulings on cycle 3:**
- **M-12 is dropped.** A whole-file diff of a committed file is complete by construction.
- **FROZEN-CHECK line 3 becomes an exact-line match.** The pattern was tested: it accepts the plain import and rejects one with `Condition="false"`. A disabled import would also fail loudly, because the golden facts would lose their data files.

The edits were small and mechanical, so the **Orchestrator made draft 3.1** itself (its section "Step 0b cycle 3 → draft 3.1").

**Cycle 4, both lenses, on draft 3.1 (verification)**
- **Codex** (`.claude/reviews/S149-0b-codex-c4.raw.md`): both of its cycle-3 findings are **CLOSED**, and it found nothing new; **READY**.
- **Fable** (`.claude/reviews/S149-0b-reviewer-c4.md`, `reviewed-by-model: claude-fable-5-2`): its cycle-3 WARNING is **CLOSED**. It raised one new WARNING; READY-WITH-WARNINGS.
  - **The Orchestrator's own mistake:** FROZEN-CHECK line 3 used `git grep -x`, which does not exist (`error: unknown switch 'x'`, exit 129). The Orchestrator had tested the pattern with plain `grep`, which does have `-x`.

**Ruling:** anchor the pattern instead. The Orchestrator re-tested it on this machine's git 2.55 in a throwaway repository. It prints only the plain import; it matches neither a commented-out copy nor `Condition="false"`; it still matches a CRLF file. **Draft 3.2** (Orchestrator) has the anchored pattern.

**Cycle 5, both lenses, on draft 3.2 (verification), the last the cap allows**
- **Codex** (`.claude/reviews/S149-0b-codex-c5.raw.md`): its cycle-4 verdict stands. It re-tested the command in a throwaway repository on git 2.55: the plain import matched (exit 0); the commented-out and `Condition="false"` variants did not (exit 1); mixed LF and CRLF files gave only the plain import. 0 B / 0 W / 0 N; **READY**.
- **Fable** (`.claude/reviews/S149-0b-reviewer-c5.md`, `reviewed-by-model: claude-fable-5-2`): its cycle-4 WARNING is **CLOSED**, re-tested the same way. One cosmetic NOTE: the plan's header still said "draft 3". **APPROVED (READY)**.

**Step 0b is READY on both lenses.** The plan is **FINAL at draft 3.2** (2026-11-27), with its header updated per Fable's NOTE. The cycle cap was never reached: no cycle raised a BLOCKER. Across the five cycles:
- **Codex:** 3 W, then 1 W / 1 N, then 1 W / 1 N, then 0, then 0.
- **Fable:** 3 W / 8 N, then 1 W / 3 N, then 1 W, then 1 W, then 1 N.

**`(unproven)` NOTEs carried from refinement Step 4** (`docs/WORKFLOW.md` 5c; `.claude/refinements/` is gitignored):
- **Fable N2: Node 26 is probably the active LTS, not 24.** Evidence that settles it: the Node release schedule. The planner checks it, and if confirmed, Node 24 is a recorded hold-back (for parity with the local machine). Disposition: **open**, settled at plan time.
- **Fable N4: Swashbuckle 6.6.2 may not run on .NET 10.** Disposition: **moot**. Under r2's order, Swashbuckle moves on .NET 8 before the runtime does.
- **Fable N7: time-zone data on the Ubuntu-based .NET 10 images.** `CopenhagenBusinessDate.cs:212-240` throws if the zone is missing. Evidence that settles it: Smoke and e2e green on the .NET 10 images. Disposition: **open**, settled at W3's CI evidence run.

**`(unproven)` NOTEs from Step 0b cycle 1**
- **Fable N1: the API contract was compared only once, on .NET 8.** Evidence that settles it: any difference from `--openapi` on .NET 10. Disposition: **absorbed**; the comparison now also runs at W3's GATE and at C-2. **Open** until W3.
- **Fable N3: Npgsql 9.0's breaking changes were not checked.** Evidence that settles it: each 9.0 item classified in TASK-14904's brief. Disposition: **absorbed**; **open** until TASK-14904 returns.
- **Fable N5: the .NET 10 SDK's restore audit could add a gate.** Evidence that settles it: any NU1901/NU1902 line in the flip's restore output. Disposition: **pre-ruled** as F-10; **open** until W3.

## Wave 0: the "nothing changed" pins, on the pre-refresh runtime

*Plain language.* Before any dependency moves, wave 0 writes down how the system behaves today, as tests with fixed expected answers. After CI-W0 these tests are **frozen**: no task may ever edit them. If an upgrade turns one red, the upgrade is wrong. The test is never "updated to match".
- **TASK-14900** pins the bytes the system *stores* and later hashes or replays: every event's JSON, the payroll export's manifest and its content hash (how payroll recognises a repeated export), the proof digests, the frozen plan inputs, and two derived ids.
- **TASK-14901** pins how a login token is *validated*: which tokens are accepted or refused, and what the application sees from an accepted one.

Each was reviewed until it could be frozen safely. That is why the review cycles below are longer than usual: a frozen file gets no second chance.

### TASK-14900: the persisted-bytes goldens (test-qa)

**Merge row**

| Item | Value |
|---|---|
| Branch | `worktree-agent-aabf294ee7d7412ff` (pruned) |
| Build | 0 errors / 126 warnings |
| 5α | PASS (c1 `5deef874`, c2 `0dfd1432`) |
| Security consult | — |
| 5a reviewer | APPROVED (c2) |
| 5a Codex | APPROVED (c2) |
| Mutation sha | `0dfd1432` |
| Merged | `93da6dc7` |

**What it built.** 14 facts and 5 data files under `tests/StatsTid.Tests.Unit/Serialization/Goldens/`, `Goldens.props`, and one `Import` line.
- **G-1:** event JSON for all **113** registered event types, with **two lines per type**: one fully populated, one with alternate optional members null (226 lines). It is checked by serialize, by replay-read, and by coverage.
- **G-2:** the payroll manifest's order, its JSON, and its **content hash**, which has its own fact.
- **G-3:** the proof digests.
- **G-4:** `PlanInputs`.
- **G-5:** a config id and a stint id.

Every literal is unedited capture output. Both lenses independently recomputed the G-2 hashes and the G-3 digests with `sha256sum`, and they match.

**Mutations.** On the reviewed tip `0dfd1432`, each of these turned its golden red: M-1, M-2, M-2b (added by ruling), M-3, M-4, M-10 and M-11.

**Measured**
- G-1: 113 keys, 226 lines.
- Data files: G1 119,160 bytes, G2 1,322, G3 903, G4 6,866, G5 168.
- Unit: 1848 → 1862 on the branch.
- Four facts read only the data file. They are labelled "capture sanity" and are not counted as pins.

**M1 (cycles per chain) and M2 (cycle-1 findings: blockers / warnings / notes)**

| Chain | Cycles | Cycle-1 findings |
|---|---|---|
| Codex | 2 | 0 / 2 / 0 |
| Fable 5.2 | 2 | 1 / 1 / 4 |
| 5α | 2 | PASS |

- **Codex's two warnings:** the populated `PlanInputs` on the event path; M-2 never reached the hash.
- **Fable's blocker:** half the optional members were never written, and the docstring was false.
- **Fable's warning:** the segment snapshot was pinned as `{}`.
- **Fable's notes:** file-only facts; reflection order; no JSON `false`; a pre-existing culture-sensitive sort key.

The rulings are in `.claude/reviews/S149-TASK-14900-5a-rulings-c1.md` (R-1 … R-8). One fix-up absorbed all of them.

**M4: the `(unproven)` NOTEs**
- **Fable c1 N2: the `#nulls` lines depend on `NullabilityInfoContext`.**
  - Constructor and member order were made explicit (R-5). The populated lines do not depend on it.
  - Evidence that would prove it: a W1 or W3 red where a serialize fact fails while its replay fact stays green. That would mean the factory drifted, not the stored bytes.
  - Disposition: **open**, until W3.
- **Fable c2: a frozen file could fail to *compile* when the compiler or xunit moves.**
  - Evidence that would prove it: a compile error or new analyzer warning under `Serialization/Goldens/` in the first W1 or W2 build.
  - Disposition: **open**. If it happens, it is a tooling event for an owner ruling, not "the upgrade is wrong".

**M5 and M6.** No "Not built" item was overruled. Two review findings of missing machinery trace to the never-extras column: every map key with a *populated* instance, and the hash reached by its own fact. No shortcuts.

**Found, not fixed (R-7).** `PayrollExportManifest.SortKey` (`:33`) formats `Hours` and `Amount` with the host culture.
- **Orchestrator assessment: Low**, below the QUAL register's floor.
- The JSON bytes, and so the hash, are culture-invariant. Only the ordering key varies.
- Ordinal comparison sorts both `,` and `.` below the digits and below `|`. So the order is the same in practice: the golden's `3,1,0,2` holds under `da-DK` and under invariant culture.

### TASK-14901: the login pins (test-qa)

**Merge row**

| Item | Value |
|---|---|
| Branch | `worktree-agent-af39b926de57ee5cd` (pruned) |
| Build | 0 / 126 |
| 5α | PASS (c1 `d51c2d0b`, c2 `f4c8cfae`) |
| Security consult | GAPS → CLEAN-WITH-RESIDUALS (c2) |
| 5a reviewer | APPROVED (c3) |
| 5a Codex | APPROVED (c3) |
| Mutation sha | `f4c8cfae` |
| Merged | `b6efa4a2` |

**What it built.** `TokenPathPinTests.cs` (Regression, non-Docker) runs an in-memory host with the real `AddStatsTidJwtAuth`, and a probe that returns `GetActorContext()`.

| Fact | What it pins |
|---|---|
| L-1 | the legacy issuer |
| L-10 | `JsonWebTokenHandler` |
| L-2 | unmapped claim types; `Identity.Name` = `sub`; `IsInRole("local_hr")` = false |
| L-3 | wrong issuer |
| L-4 | wrong audience |
| L-7 | wrong key |
| L-8 | `alg none` |
| L-5 | expired 5 min ago → 401 |
| L-5b | expired 100 s ago → 401; detects a skew of two minutes or more |
| L-6 | expired 20 s ago → 200 |
| L-9a | the key-length minimum at issuance |
| L-9b | the key-length minimum at the validator, with hand-built tokens |
| L-12 | no `exp` → 401 |
| L-13 | `nbf` 10 min ahead → 401 |

`PasswordHashPinTests.cs` (Unit) is L-11, BCrypt: the seeded hash verifies, and new hashes use cost 11.

**Outcomes captured on the pre-refresh runtime, now frozen**
- **L-9a:** a 31-byte key throws; a 32-byte key mints.
- **L-9b:** a 31-byte key → 401, probe not reached; a 32-byte key → 200, probe reached.
- **L-12:** 401, probe not reached.
- **L-13:** 401, probe not reached.

**Mutations on `f4c8cfae`**
- M-5 → L-4 red. It was applied as `ValidateAudience = settings.Issuer == null`, because analyzer CA5404 blocks the literal `false` at compile time; Codex confirmed the expression is equivalent.
- M-6 → L-2 red, and L-1 and L-10 with it.
- M-7 → L-5, L-5b and L-13 red.
- M-7b (added by ruling) → only L-5b red.

Regression non-Docker went from 148 to 162.

**M1 and M2**

| Chain | Cycles | Cycle-1 findings |
|---|---|---|
| Codex | 3 | 1 / 4 / 0 |
| Fable 5.2 | 3 | 0 / 4 / 4 |
| 5α | 2 | PASS |
| Security consult | 2 | GAPS |

- **Codex's blocker:** a future `nbf` and a missing `exp` were unpinned.
- **Codex's four warnings:**
  - the skew bracket was loose;
  - `NameClaimType` was unpinned;
  - issuer and audience came from the environment;
  - L-6 had only a 30 s margin.
- **Fable's four warnings:**
  - L-5 sat on the library's 5-minute default;
  - the consult's case-sensitivity pin would freeze an outcome W1 is known to change;
  - the fix shapes;
  - the L-11 kind statements.

The rulings are in `.claude/reviews/S149-TASK-14901-5a-rulings-c1.md` (R-1 … R-9) and `-c2.md` (R2-1 … R2-4). Two fix-ups absorbed them.

**The case-sensitivity ruling (R-5), recorded here for W1**
- **The change.** IdentityModel 8 makes claim-*name* lookups case-sensitive (`CaseSensitiveClaimsIdentity`). The plan accepts this as a known change (P-4 point 7). It is safe because a census shows every lookup in `src` uses exact casing.
- **What the consult proposed.** A frozen pin, "a token with `Role` and `Sub` capitalised still resolves". That pin would have gone red at W1 with no legal way forward.
- **What is frozen instead.** Only the half that holds on both versions: claim *values* compare exactly.
- **Where the rest goes.** The miscased-name token becomes a **mandatory, non-frozen W1 fact in TASK-14903-T**, asserting the post-upgrade behaviour (plan "final + 1"; the consult asked for it to be mandatory).
- **Pre-upgrade behaviour,** from the consult and Fable's reading: such a token resolves today.

**M4: the `(unproven)` NOTEs**
- **Fable c1 N7, L-9a caught only `ArgumentException`.** **Closed by R-9**, which uses `ThrowsAny` with a control.
- **Fable c2 N2, L-11's cost pin assumes BCrypt 4.2.0 keeps cost 11.** The evidence is TASK-14907 running L-11 against 4.2.0. **Open** until then. If the default moved, that is a true detection and it goes to the owner.
- **Fable c2 N3, three facts minted already-expired tokens through the library.** **Closed by R2-3**: the tokens are hand-built now.

**The consult's residuals.** These are recorded as known limits of a test host, not as defects:
- the pin host has no real middleware stack and no policies;
- the `Testing` environment's in-memory key means the dev-fallback and fail-fast branches are not run;
- the production issuance path (`AuthEndpoints.cs:69`) is not run;
- the explicit issuer and audience mean the `?? "statstid"` defaults are not pinned;
- the L-9 literals detect movement but do not say which direction is safe, so the W1 and W3 consults judge.

**M5 and M6.** No "Not built" item was overruled. Several review findings of missing machinery trace to the never-extras column: the negative cases, and both minting handlers. No shortcuts.

### TASK-14902: CI hygiene (sweep, `CI workflows (cross-domain authorized)`)

**Merge row**

| Item | Value |
|---|---|
| Branch | `worktree-agent-a249d051642f33665` (pruned) |
| 5α | PASS |
| 5a reviewer | APPROVED (c1) |
| 5a Codex | — (not marked) |
| Merged | `90abdfa7` |

**What changed:** 24 lines in the three workflow files.
- `checkout` v4 → v7 (9 sites)
- `setup-dotnet` v4 → v6 (2)
- `setup-node` v4 → v7 (2)
- `setup-python` v5 → v7 (2)
- `upload-artifact` v4 → v7 (7)
- `node-version` '20' → '24' (2)

`dotnet-version: '8.0.x'` is untouched until W3. The Orchestrator confirmed every target tag upstream with `git ls-remote`.

**The review.** Fable read the upstream release notes for every major crossed and found no changed default or removed input that alters what a step does here:
- fetch depth and credentials;
- artifact names, hidden files and overwrite;
- no npm auto-cache, since `frontend/package.json` has no `packageManager`;
- the `engines` ranges admit Node 24.

**M1 and M2.** One cycle per chain.
- 5α: PASS.
- Fable: 0 B, 0 W, 4 N.

**M4: the `(unproven)` NOTEs**
- **N2: the gates check as much as before.** Disposition: **closed at CI-W0** (all six hold; see "CI-W0" below). The evidence, in order:
  1. every `@v7` and `@v6` step resolves;
  2. `setup-node` gives Node `v24.x` and prints no auto-cache line;
  3. `setup-dotnet` installs 8.0.x;
  4. the suite totals match the last green master run;
  5. the four artifacts are non-empty, and no step prints "No files were found";
  6. gitleaks and docs check out the full history.
- **N3: the two Claude workflows are not exercised by a master push.** Disposition: **closed at CI-W0, with a residual.** The planned evidence cannot arrive: "Claude Code Review" is disabled in the repository's settings, so no pull request fires it (see "CI-W0" below).

**Other notes**
- **N1:** the new `setup-dotnet` would reject today's `global.json` (`8.0.0` + `rollForward`) **if** it read the file. It does not, because `dotnet-version` is explicit, and W3 sets `10.0.100`, which is valid. No action.
- **N4:** `ROADMAP.md`'s "CI already warns that GitHub's Node 20 action runtime is deprecated" becomes stale. It is rewritten at close. `@types/node` moves in W4.

### Wave 0 GATE: master `c2c21fde`, after both merges and the plan's "final + 1"
- Build: 0 errors, 126 warnings.
- Unit: **1864** (1848 + 14 goldens + 2 L-11).
- DemoSeed: **180**.
- Regression `Category!=Docker`: **162** (148 + 14).
- FROZEN-CHECK line 1, the older pins against `df582c20`: prints nothing.
- The `Import` grep prints exactly one line.
- The version grep over the W0 frozen files prints nothing.
- `Directory.Build.props` is unchanged.
- CA2100 was not re-measured: the diff is test-only and contains no SQL.
- Both merged worktrees are pruned; `git worktree list` shows only the main checkout.

### CI-W0: green, so A0 = `719e7996`

*Plain language.* CI-W0 is the first check on GitHub's Linux machines. It runs every test, including the database tests that cannot run on this computer, with the new GitHub Actions and Node 24. It came back green. Its commit, `719e7996`, is now **A0**. The wave-0 pins are frozen from A0, and every later wave is checked against it.

- **The run:** `45997941910`. It ran as draft PR #71 on `ci/s149-w0` at `719e7996`; GitHub tested the merge commit `9322b699` onto `d092595f`. **All seven jobs are green.** PR #71 is closed unmerged, `ci/s149-w0` is deleted, and `origin/master` is still `d092595f`.
- **Suites, read from the trx files by name:**
  - Unit 1864 of 1864;
  - DemoSeed 180 of 180;
  - Regression **2992 of 2992**, with the Docker facts included and every fact executed: S148's 2978 plus wave 0's 14;
  - Smoke 10 of 10;
  - frontend: vitest 122 files and 1405 tests; e2e 7 passed.
- **The wave-0 pins in CI:** all 14 `TokenPathPinTests` facts (L-1 … L-13 with L-5b) passed by name. Unit has no failure, so G-1 … G-5 and L-11 passed too.
- **Build:** 0 errors and 126 warnings. CA2100 has 114 distinct sites against the ratchet's baseline of 115. The step prints its "count fell" warning, as it did at S148's close (see "Found, not fixed").

**TASK-14902's N2, settled: the gates check as much as before**
1. **Every new Action resolves.** `checkout@v7` in all 7 jobs, `setup-dotnet@v6` ×2, `setup-node@v7` ×2, `setup-python@v7` ×2, and `upload-artifact@v7` ×4.
2. **Node is v24.22.3** (npm 11.19.1), taken from the runner's tool cache. No cache-restore or cache-save line appears, which confirms there is no npm auto-cache.
3. **`setup-dotnet` installs SDK 8.0.428.**
4. **The suite totals match** S148's close run, plus wave 0's additions.
5. **Artifacts:**
   - `test-results` is 2.59 MB, three trx files;
   - `coverage-cobertura` is 307 KB;
   - `smoke-test-results` is 2.6 KB, 10 tests;
   - `lizard-ccn-report` holds a **0-byte** report, but S148's close run `45781374620` had the same 150-byte artifact, so the Actions bump did not cause it (see "Found, not fixed");
   - no step prints "No files were found".
6. **Full history:** gitleaks and docs both check out with `fetch-depth: 0`.

**Disposition:** closed. The Actions and Node 24 change nothing about what CI checks.

**TASK-14902's N3, settled differently than planned.** No pull request can exercise the two Claude workflows today:
- "Claude Code Review" is **disabled in the repository's settings** (`disabled_manually`); its last run was 2026-09-30.
- "Claude Code" runs only when a comment mentions `@claude`.

So PR #71 fired neither. **Disposition:** closed, with a residual. The new `checkout@v7` step in both files first runs when the owner re-enables the review workflow or mentions `@claude`. A failure there would block no other job.

**Found, not fixed (both in CI, neither caused by S149)**
- **The complexity report is empty.** `lizard src/ -l cs -C 15 --warnings_only` finishes in 0.1 s and writes nothing, here and at S148's close. So for at least one sprint the "Cyclomatic complexity report" job has been green while measuring nothing.
  - **Cause `(unproven)`:** lizard's C# language is named `csharp`, so `-l cs` probably selects no files. The evidence that would prove it: the same command with `-l csharp` prints functions.
  - **Disposition:** proposed for W5's CI change (TASK-14916), after W3, because the runtime move can change what lizard sees. This needs a "final + n" ruling at W5's dispatch.
- **The CA2100 ratchet baseline is one too high** (115, while the build shows 114). This is the step's own "ratchet down" warning, carried since S148.
  - **Disposition:** ratchet it at W5, after W3's build re-measures the count on .NET 10.
