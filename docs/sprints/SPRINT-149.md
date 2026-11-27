# Sprint 149 — the dependency refresh: every dependency current, on .NET 10, and nothing changed

| | |
|---|---|
| **Status** | **OPEN** (opened 2026-11-27). The plan is **FINAL** (draft 3.2) after Step 0b READY on both lenses at cycle 5; wave 0 is next. No code has changed yet. |
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
