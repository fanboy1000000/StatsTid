# PLAN — Sprint 149: the dependency refresh — every dependency current, on .NET 10, and nothing changed

*Draft 3.2, **FINAL**, 2026-11-27: drafts 1–3 by the `planner` (Opus 5.5), drafts 3.1 and 3.2 by the Orchestrator; Step 0b
cycles 1–5 absorbed (the five "Step 0b cycle N → …" sections). From here the plan changes only by ruling; each change is a
"final + n" row of the Revision history.*

| | |
|---|---|
| **Status** | **FINAL (draft 3.2)**. Step 0b READY on both lenses at cycle 5 (2026-11-27): Codex READY at cycles 4 and 5 (0 B / 0 W / 0 N), Fable 5.2 APPROVED (READY) at cycle 5 with one cosmetic NOTE (this header, now updated). Every finding of cycles 1–4 ruled and CLOSED. No code has changed. |
| **Theme** | Bring every dependency to current in one deliberate refresh — NuGet, npm, GitHub Actions, CI's Node, the .NET runtime and its base images (.NET 8 → .NET 10) — with **no behaviour change**: no pay figure, seeded row, API contract, persisted hash or access decision may move. Proven by pins, some new and taken on .NET 8 before anything moves. PostgreSQL stays on 16. |
| **Owner rulings in force** | "Lets do all the updates now" (2026-11-26). **Q1 = (a) "Updates are S149"** (2026-11-27): S149 is the refresh alone; S150 takes the old S149 (QUAL-284 first, then stage 1); this **supersedes** the 2026-11-26 ruling "QUAL-284 first in S149". **Q2 = (a) ".NET 10"** (2026-11-27): .NET 11 and its framework-tied packages are recorded hold-backs with `ignore` rules. **NR-3 = (b) "Install it"** (2026-11-27): the Orchestrator installs the .NET 10.0.4xx SDK on the owner's machine before W3 (O-0). Standing: reseed, don't migrate; the complete fix, without padding; owner questions one at a time, explained first; review cap 5 per lens; every review on both lenses, saved verbatim before anyone acts on it; FAIL-009 (2026-11-26): the routing guard fails closed, the close guard stays fail-open. |
| **Predecessor** | S148: close `df582c20`, CI GREEN `45781374620` (all seven jobs); C-5 `d092595f`, CI GREEN `45815604149` (`docs/sprints/SPRINT-149.md:9`, the Orchestrator's draft log). |
| **Plan base** | master **`d092595f`** (HEAD). Working tree at plan time: `ROADMAP.md` and `docs/operations/quality-finding-register.md` modified (the Orchestrator's Tier-2 drafts, O-T1…O-T3), `docs/sprints/SPRINT-149.md` untracked. `git diff --stat` touches only those two files; no code has changed. Every file:line in this plan is at `d092595f` unless marked. |
| **Base commit (Step 7a)** | **`df582c20`**, the S148 close commit (`docs/WORKFLOW.md:38`), so the post-close commit `d092595f` falls inside S149's Step-7a diff. |
| **Orchestrator model** | **Opus 5.5 seat**, never switched (owner ruling 2026-09-29, `docs/WORKFLOW.md` § Model Routing, "the seat never switches"). **Drafting:** `planner` (Opus 5.5). **Judgment:** `reviewer` and `adjudicator` on **Fable 5.2** (`claude-fable-5-2`, the review floor). **Implementation by role name:** `backend-infrastructure` (Opus); `security`, `test-qa`, `ux`, `constraint-validator`, `trace` (Sonnet); `sweep` (Haiku). **External lens:** Codex. The client version and each tier's resolved id are read at the first spawn of each tier (`docs/WORKFLOW.md` § "Version check"). |
| **Refinement** | `.claude/refinements/REFINEMENT-s149-dependency-refresh.md` **r2, READY** (2026-11-27); Step 4 cycle 1: Codex 0 B / 3 W / 2 N, Fable 0 B / 7 W / 9 N, all absorbed (`REF:250-266`). "REF:n" below = the refinement's line n. |
| **Entropy scan (Step 0a)** | Done 2026-11-27 at `d092595f` (`SPRINT-149.md:14`). |
| **Baseline** | S148 final (`docs/sprints/SPRINT-148.md:16`): Unit **1848** · DemoSeed **180** · Regression **2978** (CI; **148** non-Docker locally) · Smoke **10** · vitest **122 files / 1405** · e2e green (its count is not in S148's log; it is read at CI-W0) · build 0 errors / **126** warnings · CA2100 **114** locally, **115** in CI's ratchet (`.github/workflows/ci.yml:146`). Re-measured at the first GATE, never carried forward. |
| **Trials** | The Ponytail-ideas trial, **second measured sprint** (M1–M6; § "The Ponytail trial"). The routing guard's first sprint failing closed (FAIL-009 ruling). |
| **Size** | **20 dispatch ids** (TASK-14900 … 14919; six of them — 14909–14911 and 14917–14919 — reserved for the runtime wave's fallout and DROPPED if unused) **+ 3 paired passes** (14903-T, 14904-T, 14905-T). |
| **CI budget** | One evidence run per wave (CI-W0 … CI-W4) + C-2 on K + the close push + C-5's push = **8 runs ≈ 18 h** of CI at ~2 h 17 min of Regression each (`REF:59`). Local GATE builds run one at a time, in the foreground. |

---

## Plain-language summary (read this first)

**What S149 is.** A maintenance sprint held to the same evidence standard as a feature sprint. StatsTid has run on
.NET 8 since the start; Microsoft stopped supporting .NET 8 on 10 November 2026, and CI still runs Node 20, which
reached end of life on 30 April 2026. Dependabot (GitHub's update robot) opened 34 update proposals in May that
nobody triaged, and those 34 were only part of the picture: it opens a handful per kind of package, so updates such
as Vite 5 → 8 never appeared. The owner asked for all of it now. S149 moves every dependency to current and changes
nothing the product does.

**Why that is hard to prove.** Some changes would be invisible to today's tests. Payroll stores a fingerprint (a
SHA-256 hash) of each export so it can recognise a repeated export as the same one. Today's tests compute the expected
fingerprint on whatever runtime they run on, so if .NET 10 produced different bytes, both sides would move together and
every test would stay green. So the first thing S149 does — before anything moves — is write those bytes down as fixed
text ("literal goldens") on .NET 8. Every later step must reproduce them exactly.

**The order, and why.** One cause per step, so that a red test points at one thing:
0. **Wave 0 — the pins, on .NET 8.** The literal goldens; pins on the login path (a login token through the real
   middleware); CI's GitHub Actions and Node move to current. Nothing in the product changes.
1. **Wave 1 — the login-token library alone** (Microsoft's IdentityModel 7 → 8), owned by the security agent, with a
   written check. .NET 10's login library requires IdentityModel 8, so it moves first, on its own.
2. **Wave 2 — the other libraries, one per task, still on .NET 8:** the database driver (Npgsql), the API-description
   generator (Swashbuckle), the test tools, the password-hash library.
3. **Wave 3 — the runtime itself, last:** .NET 8 → 10 everywhere. By then nothing else is moving.
4. **Wave 4 — the frontend,** independent of the backend: Vite, Vitest, ESLint, React 19, React Router, Playwright.
5. **Wave 5 — Dependabot set up so the backlog cannot quietly rebuild,** and the records.

**What is deliberately held back, and why.** The version rule (`REF:61-65`): take the newest version that works with
the rest, but not a major version younger than about eight weeks where an older supported major exists, and not a
version a tool we depend on cannot handle. Verified today, that holds back: .NET 11 and its framework packages (owner
Q2); Npgsql 11 and Swashbuckle 11 (both released in November; Swashbuckle 11 also needs .NET 10 or 11 and a second
rewrite of our OpenAPI filter); jsdom 31 and jest-dom 8 (November); TypeScript 6 and 7 (our OpenAPI type generator only
supports TypeScript 5); Node 26 (local parity); PostgreSQL 17/18 (a database major is its own sprint). Each gets a
Dependabot `ignore` rule and a ROADMAP line, so it is a recorded choice, not an oversight.

**What the reviewer should know this draft changed from the refinement** (each is a declared deviation, DV-n, or a
brief contradiction, BC-n, below): React Router's newest major is **8**, not 7 (v8 removed the `react-router-dom`
package, which is why `npm outdated` reported 7.18 as "latest"); Swashbuckle's target is **10.3.1**, not 11.0.1;
the database driver's upgrade also silently disables a performance test's query counter, which this plan repairs;
the OpenAPI filter's unit tests cannot keep their assertions word for word (the library removed the members they
read), so a stronger whole-document comparison carries the "unchanged" proof instead.

**What it costs.** About eighteen hours of CI across eight runs, two agents at a time at most, one SDK install on the
owner's machine (ruled), and no product change.

---

## How the waves are built — read before the ledger

1. **The pins first and alone.** Wave 0 adds tests and moves CI tooling only. Its pins are captured on .NET 8 and
   committed; **no wave-1 merge before CI-W0 is green.**
2. **One concern per task, one cause per wave.** Each wave's CI evidence run (CI-Wn) runs on master after that wave's
   merges, i.e. a base holding only that wave on top of earlier waves already seen green.
3. **The frozen pins.** The older pins (PIN-1's goldens, the committed demo seed, `SegmentSerializationParityTests`,
   `GoldenLegacyPinTests`) are frozen from `df582c20`; TASK-14900's and TASK-14901's files from **A0, the sha CI-W0 ran
   green on** (not the merge sha, in case a Linux-only red forces a fix after the merge). No task edits them, ever.
   "Unedited" is checked mechanically by FROZEN-CHECK (merge checklist item 3) at every GATE and checkpoint. A frozen
   pin that goes red is a defect in the upgrade, fixed in the product or the upgrade, **never by re-pinning**
   (`REF:186`, assumption 3).
4. **The frontend wave runs beside the backend waves, isolated (Block W4).** TASK-14912 … 14915 stack on one fixed
   branch, `s149/w4`, created from A0 and kept in **one worktree until it merges**; they never take master. Each task's
   gate runs on that branch; CI-W4 runs on its tip (W0 + W4 only). **W4 merges into master only when CI-W4 and CI-W3 are
   both green**, so W1–W3's evidence bases stay backend-only. C-2 on K is where the two halves meet (e2e is the only
   suite that sees both; `REF:122`). W4 is exempt from Block A's fast-forward instruction, from checklist item 8's
   merge-then-GATE-on-master, and from item 11's main-checkout-only rule.
5. **At most two Anthropic agents at once.** Pairs: W0 {14900, 14901} then {14902}; W1 {14903, 14912}; W2 {14904,
   14905} (W4 pauses), then the -T passes, then {14906}, then {14907}; W3 {14908 → fallout} beside {W4's next task}.
   A paired `-T` pass counts as one agent.
6. **Worktrees.** Every backend task runs with `isolation: "worktree"`, branched from LOCAL master (Block A's first
   instruction). A `-T` pass works on its implementer's branch after the implementer returns; W4's tasks work in the
   W4 worktree the same way. **After each wave's merges, the merged worktrees are pruned, unlinking any `node_modules`
   junction first** (merged worktrees filled the disk in S148); the W4 worktree is pruned after W4's merge. No
   `git stash` anywhere. The session cwd stays at the repo root.
7. **The runtime wave is one stack.** O-0 first (the 10.0.4xx SDK installed, owner ruling NR-3). Then TASK-14908 (the
   mechanical flip) and any fallout task (14909–14911, 14917–14919) share one branch and are reviewed and merged as
   one, after the union's review.
8. **Version targets are contract.** The target column of the version table is the contract; a newer **patch** of the
   same major.minor released mid-sprint may be taken and is reported; anything else is a STOP and report.

**Shared files and their order** (w = wave)

| File | Tasks, in merge order |
|---|---|
| `.github/workflows/ci.yml` | 14902 (w0: action majors, Node 24 at `:285`, `:339`) → 14908 (w3: `dotnet-version` at `:102`, `:227`) |
| `src/Auth/StatsTid.Auth/StatsTid.Auth.csproj` | 14903 (w1: IdentityModel references, JwtBearer `:9` → 8.0.34) → 14908 (w3: `:13` TFM, JwtBearer → 10.0.15) |
| `tests/StatsTid.Tests.Unit/StatsTid.Tests.Unit.csproj` | 14900 (w0: the `Goldens.props` `Import` line only; frozen) → 14903 (w1: JwtBearer `:15`, with `StatsTid.Auth`'s, else NU1605) → 14906 (w2: test tools, legacy pins `:25-26`) → 14908 (w3: `:4`, JwtBearer) |
| `Directory.Build.props` | the Orchestrator only, in W3, and only if the restore audit fires (F-10, the pre-ruled N5 line) |
| `tests/StatsTid.Tests.Unit/Goldens.props` | 14900 only (frozen from A0) |
| `tests/StatsTid.Tests.Smoke/StatsTid.Tests.Smoke.csproj` | 14903-T (w1: JsonWebTokens `:14`) → 14904 (w2: Npgsql `:19`) → 14906 (w2: tools, legacy pins `:29-30`) → 14908 (w3: `:4`) |
| `tests/StatsTid.Tests.Regression/StatsTid.Tests.Regression.csproj` | 14904 (w2: Npgsql `:17`) → 14906 (w2: `:13`, `:16`, `:18-20`, legacy pins `:27-28`, SSH.NET `:30-36`) → 14908 (w3: `:4`, Mvc.Testing `:14`, Logging.Abstractions `:15`, comment `:77`) |
| `tests/StatsTid.Tests.DemoSeed/StatsTid.Tests.DemoSeed.csproj` | 14906 (w2: `:13-16`, legacy pins `:19-20`) → 14908 (w3: `:4`) |
| `tools/StatsTid.DemoSeed/StatsTid.DemoSeed.csproj` | 14904 (w2: Npgsql `:27`) → 14908 (w3: `:5`) |
| `src/Infrastructure/StatsTid.Infrastructure/StatsTid.Infrastructure.csproj` | 14904 (w2: Npgsql `:10`) → 14908 (w3: `:14`) |
| `src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj` | 14905 (w2: Swashbuckle `:13`) → 14907 (w2: BCrypt `:9`) → 14908 (w3: `:17`) |
| `src/Backend/StatsTid.Backend.Api/ResponseStrictTypesFilter.cs` | 14905 only (its "Swashbuckle 6.6.2" and ".NET 8" comments, `:23`, `:27`, `:41`, `:172`, are rewritten there, so the w3 sweep skips it) |
| The 18 Testcontainers fixture files (BC-3; `git grep -n "new PostgreSqlBuilder()" -- tests` → 18) | 14906 only (the builder line of each) |
| `tests/StatsTid.Tests.Regression/Performance/S106SeedScalePerfTests.cs` | 14904-T only |
| `frontend/package.json`, `package-lock.json` | 14912 → 14913 → 14914 → 14915 (w4, in that order) |

---

## The version table (verified 2026-11-27, not taken from the refinement)

**How each target was verified.** NuGet: the flat-container index (`api.nuget.org/v3-flatcontainer/<id>/index.json`)
for the newest version per major, the nuspec (`…/<id>/<ver>/<id>.nuspec`) for target frameworks and dependency groups,
and the registration API for publish dates (an unlisted version shows `1900-01-01`). npm: `npm view <pkg> dist-tags`,
`npm view <pkg>@<ver> peerDependencies engines`, `npm view <pkg> time`. Actions: `git ls-remote --tags` and the
GitHub releases API for each major's first release date. Node: `nodejs/Release` `schedule.json` and
`nodejs.org/dist/index.json`. .NET: `dotnetcli.blob.core.windows.net/dotnet/release-metadata/{8.0,10.0,11.0}/releases.json`.
Images: `dotnet/dotnet-docker` `manifest.json` and `src/runtime-deps/10.0/noble/amd64/Dockerfile`. Release notes read:
Npgsql 9.0 and 10.0, Swashbuckle 10.0.0 and 11.0.0, IdentityModel 8.0.0, xunit VS adapter 4.0.0, coverlet's changelog,
react-router's `CHANGELOG.md` (v8.0.0), and the action majors.

### NuGet

| Package | Where (file:line) | Current | Target (wave) | Why (verified) |
|---|---|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | `src/Auth/StatsTid.Auth/StatsTid.Auth.csproj:9`; `tests/StatsTid.Tests.Unit/StatsTid.Tests.Unit.csproj:15` | 8.0.8 | **8.0.34** (W1) → **10.0.15** (W3) | Framework-tied (rule 2). 8.0.8 floors IdentityModel **7.1.2**, 8.0.34 floors **7.7.3**, 10.0.15 floors **8.19.2** (each nuspec: `Microsoft.IdentityModel.Protocols.OpenIdConnect`). 11.0.0 is .NET 11 → held. |
| Microsoft.IdentityModel.Protocols.OpenIdConnect, System.IdentityModel.Tokens.Jwt (the family: + JsonWebTokens, Tokens, Logging, Protocols) | `StatsTid.Auth.csproj` (new explicit references) | 7.1.2 (transitive) | **8.23.0** (W1) | Newest 8.x (no 9.x exists). Nuspec groups: net8.0, net9.0, net10.0 (+ netstandard2.0, net462, net472, net6.0); Tokens 8.23.0 depends on `Microsoft.Bcl.Cryptography` 10.0.2 on every TFM. Explicit, so production sits on 8.23.0, not on JwtBearer 10's 8.19.2 floor (`REF:85`). Published 2026-09-18. |
| Microsoft.IdentityModel.JsonWebTokens | `tests/StatsTid.Tests.Smoke/StatsTid.Tests.Smoke.csproj:14` | 7.5.1 | **8.23.0** (W1) | Same family, same version. |
| Npgsql | `src/Infrastructure/StatsTid.Infrastructure/StatsTid.Infrastructure.csproj:10`; Regression csproj `:17`; Smoke csproj `:19`; `tools/StatsTid.DemoSeed/StatsTid.DemoSeed.csproj:27` | 8.0.3 | **10.0.4** (W2) | Newest supporting net8: 10.0.4's nuspec has net8.0, net9.0, net10.0 groups. Breaking: `date` → `DateOnly`, `time` → `TimeOnly` on non-generic reads (`ExecuteScalar`, `GetValue`); tracing tags renamed to OpenTelemetry names (BC-9); infinity conversions still on by default (`PgSerializerOptions.cs:57` at v10.0.4). **11.0.0 held:** published 2026-11-20, net10.0-only, and 10.0.4 shipped the same day (10.x is supported). |
| Swashbuckle.AspNetCore | `src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj:13` | 6.6.2 (Microsoft.OpenApi 1.6.14) | **10.3.1** (W2) | Newest supporting net8: groups net8.0, net9.0, net10.0; depends on **Microsoft.OpenApi 2.7.5** (2.x confirmed). OpenAPI 3.1 output is opt-in, so the document stays 3.0 (`nullable: true` survives). **11.0.x held:** 11.0.0 published 2026-11-10; groups net10.0 and net11.0 only; Microsoft.OpenApi **3.11.0** (a second filter rewrite). |
| BCrypt.Net-Next | Backend.Api csproj `:9` | 4.1.0 | **4.2.0** (W2) | Newest listed (4.2.1 is unlisted). |
| xunit | the four test csproj (`Unit :17`, `Regression :19`, `Smoke :21`, `DemoSeed :15`) | 2.5.3 | **2.9.3** (W2) | Newest v2 (published 2025-01-08); brings xunit.analyzers 1.18.0. xunit v3 is out of scope (`REF:138`). |
| xunit.runner.visualstudio | the four test csproj | 2.5.3 | **4.0.1** (W2) | Its nuspec: "Capable of running xUnit.net v1, v2, and v3 tests. Supports … .NET 8 or later." 4.0.0 (2026-08-15) added xunit v3 4.0 support; not a young major. |
| Microsoft.NET.Test.Sdk | the four test csproj | 17.8.0 | **18.12.0** (W2) | net8.0 group (Microsoft.CodeCoverage, TestHost 18.12.0); 18.0 shipped 2025-10. |
| coverlet.collector | the four test csproj | 6.0.0 | **10.1.1** (W2) | Changelog: minimum .NET 8 SDK and runtime since 8.0.0 (2026-02); .NET 10 support in 10.0.0 (2026-04). |
| Testcontainers.PostgreSql | Regression csproj `:18` | 3.9.0 | **4.17.0** (W2) | Groups net8.0, net9.0, net10.0; floors **SSH.NET 2026.0.0**. Its parameterless `PostgreSqlBuilder()` is `[Obsolete]` (BC-3). |
| SSH.NET | Regression csproj `:36` | 2026.0.0 (explicit CVE override, comment `:30-35`) | **removed if clean** (W2; DV-4), else 2026.0.1 | Its only reason was Testcontainers 3.9's SSH.NET 2023.0.0; 4.17.0 floors 2026.0.0. |
| System.Net.Http, System.Text.RegularExpressions | the four test csproj (`Unit :25-26`, `Regression :27-28`, `Smoke :29-30`, `DemoSeed :19-20`) | 4.3.4, 4.3.1 | **removed if clean** (W2) | Their reason (`Regression csproj :22-26`) was Test.Sdk 17.8.0's old transitive packages; removed if `dotnet list package --vulnerable --include-transitive` stays clean without them (`REF:109`). |
| Microsoft.AspNetCore.Mvc.Testing | Regression csproj `:14` | `8.0.*` (resolves 8.0.34) | **10.0.15**, exact (W3; DV-6) | Framework-tied. Exact, so restores are reproducible and Dependabot can manage it. 11.0.0 held. |
| Microsoft.Extensions.Logging.Abstractions | Regression csproj `:15` | 8.0.3 | **10.0.15** (W3) | Framework-tied; also forced: Npgsql 10.0.4's net10.0 group needs ≥ 10.0.0, so 8.0.3 would be a package downgrade (NU1605) on net10. 11.0.0 held. |
| *(transitive, named for the record)* Microsoft.OpenApi; Microsoft.Extensions.ApiDescription.Server | via Swashbuckle | 1.6.14; 6.0.5 | 2.7.5; 8.0.0 (net8) / 10.0.0 (net10) | Not referenced directly; no explicit pin (smallest change). |

### npm (`frontend/package.json`; "current" = installed, from `package-lock.json`)

| Package | Current | Target (W4 task) | Why (verified) |
|---|---|---|---|
| vite | 5.4.21 | **8.4.0** (14912) | Engines `^20.19 \|\| >=22.12`; 8.0.0 shipped 2026-03-12. |
| @vitejs/plugin-react | 4.7.0 | **6.3.0** (14912) | Peers `vite ^8.0.0`. |
| vitest | 4.0.18 | **5.1.2** (14912) | Peers `vite ^6.4 \|\| ^7 \|\| ^8`, optional `@types/node ^22 \|\| >=24` (an installed optional peer must satisfy it, so @types/node moves in the same task); engines `^22.12 \|\| ^24 \|\| >=26` (Node 20 unsupported, so CI's Node 24 must land first, W0). 5.0.0 shipped 2026-09-03 (12 weeks). |
| jsdom | 28.1.0 | **30.1.3** (14912) | **31 held:** 31.0.0 shipped 2026-11-05 (young). 30.x engines `^22.22.2 \|\| ^24.15.0 \|\| >=26` (local 24.19 ✓). |
| @types/node | 20.19.43 | **24.19.4** (14912) | Follows CI and local Node 24 (`REF:82`). 25+/26 held with Node 26. |
| eslint | 9.39.4 | **10.15.0** (14912) | Engines `^20.19 \|\| ^22.13 \|\| >=24`. Our config is already flat (`frontend/eslint.config.mjs`), only `no-restricted-syntax` with the TS parser. |
| @typescript-eslint/parser | 8.62.1 | **8.74.0** (14912) | Peers `eslint ^8.57 \|\| ^9 \|\| ^10`, **`typescript >=4.8.4 <6.1.0`**. |
| typescript | 5.9.3 | **5.9.3 — held** (14912) | **6 and 7 held:** openapi-typescript 7.13.0 (latest) peers `typescript ^5.x`; typescript-eslint 8.74.0 peers `<6.1.0`, so 7 is out on both counts (`REF:118`). |
| openapi-typescript | 7.13.0 | 7.13.0 (current) | Latest. Generator unchanged, so `api-types.ts` can only change if the spec changes. |
| react, react-dom | 18.3.1 | **19.3.1** (14913) | react-dom peers `react ^19.3.1`. |
| @types/react, @types/react-dom | 18.3.28, 18.3.7 | **19.3.3, 19.3.0** (14913) | @types/react-dom peers `@types/react ^19.3.0`. |
| @testing-library/react | 16.3.2 | **16.3.3** (14913) | Peers React 18 or 19, `@testing-library/dom ^10`. |
| @testing-library/user-event | 14.6.1 | **14.6.13** (14913) | Patch. |
| @testing-library/jest-dom | 6.9.1 | **7.0.1** (14913) | Peers `vitest >= 0.32`, `@testing-library/dom >=10 <11`; engines node ≥ 22. **8 held:** 8.0.0 shipped 2026-11-19 (young; peers `vitest >=5 <6`). |
| @radix-ui/react-dialog, -dropdown-menu, -select, -tabs, -toast, -tooltip | 1.1.15, 2.1.16, 2.2.6, 1.1.13, 1.2.15, 1.2.8 | **1.3.1, 2.2.1, 2.4.1, 1.2.1, 1.3.1, 1.4.1** (14913) | Each peers `react ^16.8 \|\| ^17 \|\| ^18 \|\| ^19`. |
| @fontsource/ibm-plex-sans | 5.2.8 | **5.3.0** (14913) | Minor. |
| react-router-dom → **react-router** | 6.30.3 | **react-router 8.5.3**; `react-router-dom` removed (14914; DV-5) | v8.0.0 (2026-06-17, five months old) **removed the `react-router-dom` package**: imports move to `react-router` (`RouterProvider`/`HydratedRouter` from `react-router/dom`); ESM-only; peers React ≥ 19.2.7; engines node ≥ 22.22.0. |
| @playwright/test | 1.61.0 | **1.65.1** (14915) | Engines node ≥ 20. |

### GitHub Actions (all three workflows)

| Action | Where | Current | Target (W0) | Why |
|---|---|---|---|---|
| actions/checkout | `ci.yml:21, 53, 92, 223, 281, 335, 404`; `claude.yml:34`; `claude-code-review.yml:30` | v4 | **v7** (7.0.1) | v7.0.0 2026-06-18. v5+ run on Node 24 (runner ≥ 2.327.1; GitHub-hosted runners qualify). |
| actions/setup-dotnet | `ci.yml:100, 225` | v4 | **v6** (6.1.0) | v6.0.0 2026-07-16; v5 removed support for older .NET (8 and 10 unaffected). |
| actions/setup-node | `ci.yml:283, 337` | v4 | **v7** (7.1.0) | v7.0.0 2026-07-14. v5+ auto-cache only npm when `packageManager` is set; our `package.json` has none, so no caching change. |
| actions/setup-python | `ci.yml:23, 408` | v5 | **v7** (7.1.0) | v7.0.0 2026-07-20 (removed the `pip-install` input; unused here). |
| actions/upload-artifact | `ci.yml:43, 200, 207, 261, 267, 381, 388` | v4 | **v7** (7.0.2) | v7.0.0 2026-02-26. |
| anthropics/claude-code-action | `claude.yml:40`; `claude-code-review.yml:36` | v1 | **v1** (1.0.290; unchanged) | v1 is the newest major. |

### Runtime, images, Node, database

| Item | Where | Current | Target | Why |
|---|---|---|---|---|
| .NET SDK | `global.json:3-4` (`8.0.0`, `latestFeature`) | 8.0.423 local | **`10.0.100` + `latestFeature`** (W3), resolving the **10.0.4xx** band locally (O-0) and in CI (`10.0.x`) | .NET 10 is LTS to 2028-11-14, latest SDK 10.0.404, runtime 10.0.15; local SDKs today 8.0.423 and **10.0.302** (`dotnet --list-sdks`), so O-0 installs 10.0.4xx side by side (NR-3 ruled). .NET 8 EOL 2026-11-10 (release metadata). .NET 11 (2026-11-10, EOL 2028-11-14) held (Q2). |
| Target framework | 16 csproj (`git ls-files '*.csproj'`) | net8.0 | **net10.0** (W3) | — |
| Base images | 7 Dockerfiles: `FROM …/sdk:8.0` (line 1) and `…/aspnet:8.0` (line 14; RuleEngine `:12`, mocks `:6`) | 8.0 = Debian 12 bookworm-slim | **sdk:10.0 / aspnet:10.0** = Ubuntu 24.04 noble (W3) | The `10.0` tag maps to noble (`manifest.json`); runtime-deps 10.0 noble installs `libicu74`, `tzdata`, `tzdata-legacy`, so `Europe/Copenhagen` resolves (`CopenhagenBusinessDate.cs:205-234`). `apt-get install curl` (each runtime stage) works on Ubuntu. |
| CI setup-dotnet | `ci.yml:102, 227` | `8.0.x` | **`10.0.x`** (W3) | — |
| Node (CI) | `ci.yml:285, 339` | 20 | **24** (W0) | 20 EOL 2026-04-30; 24 LTS to 2028-04-30 (maintenance since 2026-10-20); local 24.19.0. **26 held** for local parity (LTS since 2026-10-28, to 2029-04-30; `REF:82`). |
| PostgreSQL | `docker/docker-compose.yml:9`; `ci.yml:79`; 18 Testcontainers fixtures | 16-alpine | **16-alpine** (held, out of scope) | `REF:137`. Supported upstream to November 2028. |

### Hold-backs and their `ignore` rules (W5, TASK-14916)

| Hold-back | Reason | Revisit | Dependabot rule (ecosystem) |
|---|---|---|---|
| .NET 11-tied packages | Owner Q2 = .NET 10 | the .NET 11 move | nuget: `- dependency-name: "Microsoft.AspNetCore.*"` / `versions: [">= 11.0.0"]`; `- dependency-name: "Microsoft.Extensions.*"` / `versions: [">= 11.0.0"]` |
| .NET 11 images | Owner Q2 | the .NET 11 move | docker: `- dependency-name: "dotnet/sdk"` / `versions: [">= 11.0"]`; `- dependency-name: "dotnet/aspnet"` / `versions: [">= 11.0"]` |
| Npgsql 11 | Young major (2026-11-20), net10-only; 10.x patched the same day | ≥ 2027-01-15 | nuget: `- dependency-name: "Npgsql"` / `versions: [">= 11.0.0"]` |
| Swashbuckle 11 | Young major (2026-11-10); ASP.NET Core 11 era; Microsoft.OpenApi 3.x means a second filter rewrite | ≥ 2027-01-05, or with .NET 11 | nuget: `- dependency-name: "Swashbuckle.AspNetCore*"` / `versions: [">= 11.0.0"]` |
| jsdom 31 | Young major (2026-11-05) | ≥ 2026-12-31 | npm: `- dependency-name: "jsdom"` / `versions: [">= 31.0.0"]` |
| @testing-library/jest-dom 8 | Young major (2026-11-19) | ≥ 2027-01-14 | npm: `- dependency-name: "@testing-library/jest-dom"` / `versions: [">= 8.0.0"]` |
| TypeScript 6 and 7 | Tool support: openapi-typescript peers `^5.x`; typescript-eslint peers `<6.1.0` | when openapi-typescript supports 6+ | npm: `- dependency-name: "typescript"` / `versions: [">= 6.0.0"]` |
| @types/node 25+ (with Node 26) | Follows Node 24 (local parity) | with the Node 26 move | npm: `- dependency-name: "@types/node"` / `versions: [">= 25.0.0"]` |
| Node 26 in CI | Local parity (`REF:82`) | when local Node moves | **no rule possible:** `setup-node`'s `node-version` is not a Dependabot-managed field → ROADMAP line only |
| PostgreSQL 17/18 | A database major is a behaviour change (`REF:137`) | its own sprint | **no rule needed:** compose and fixture strings are not scanned (no `docker-compose` ecosystem, `REF:126`) → ROADMAP line only |
| .NET SDK in `global.json` | — | — | no `dotnet-sdk` ecosystem is configured, so nothing proposes an 11 SDK → no rule |

---

## The proof design — the "nothing changed" pins

### P-1. The W0 literal goldens (TASK-14900; Unit, non-Docker, captured locally on .NET 8)

**Which surfaces, and why these.** A *persisted serialized surface* is one whose bytes are stored and later hashed,
compared as text, or re-read by a replay. From the code:

| Id | Surface | Code | Why it matters |
|---|---|---|---|
| **G-1** | `EventSerializer.Serialize` / `Deserialize` with its options (camelCase, `JsonStringEnumConverter`, `WhenWritingNull`) | `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs:9-14`, `:239-250`; the type map `:16-237` | Every event in the store is these bytes, and replay reads them back (DEP-003, ADR-005). |
| **G-2** | `PayrollExportManifest.OrderLines`, `Serialize`, `Deserialize`, `ComputeContentHash` (Web defaults, SHA-256, uppercase hex) | `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollExportManifest.cs:27-65` | `payroll_export_records.content_hash`: same key + same hash = idempotent; different hash = 409 (ADR-034 D1). |
| **G-3** | `HoursDigest.ComputeEntriesDigest` / `ComputeAbsencesDigest` and the line forms `EntryLine` / `AbsenceLine` (internal; `InternalsVisibleTo` Unit, Payroll csproj `:18`) | `…/Payroll/Services/HoursDigest.cs:80-119`, hash `:124` | The frozen proof's digests (`PlanInputs.EntriesDigest/AbsencesDigest`); a replay recomputes and compares them (`PeriodCalculationService.cs:1806-1807`, `:3356`, `:3363`). |
| **G-4** *(plan-added, DV-3)* | `PlanInputs` under `PeriodCalculationService.JsonOptions` | options `PeriodCalculationService.cs:328-333`; written `PostgresSegmentManifestStore.cs:104-105`, re-read `:313`, `:330` | The month's frozen inputs (`segment_manifests`), re-read by replay. `PlannedSegment` under the same options is already pinned by literals (`tests/StatsTid.Tests.Unit/Segmentation/SegmentSerializationParityTests.cs:77-91`); `PlanInputs` is not. |
| **G-5** *(plan-added, DV-3; NR-1 re-ruled)* | The two SHA-256 id derivations: an entitlement config's `ConfigId` and a midnight-crossing stint id | `DefaultEntitlementConfigs.cs:53-63` (input `:55`); `MidnightCrossingNormalizer.cs:184`, `:199-204` (key `:201-202`) | Both format text and hash it. The config ids are seeded; a stint id is the fallback continuity link when an entry carries none (production readers set `SourceStintId = EventId`, `PeriodCalculationService.cs:3781`, `ComplianceEndpoints.cs:211`, so the derivation is a fallback). |

*Why not more.* The audit snapshots (`AuditJson`, `SnapshotJsonOptions`, e.g. `CustomaryDayRepository.cs:18`,
`VacationSettlementEndpoints.cs:1625`) go into JSONB columns that are displayed, never hashed or byte-compared, and
they use the same System.Text.Json writer with Web defaults that G-2 pins. *(Draft 1 left the two id derivations
unpinned on the ground that "a stability fact" already covered them. It does not: `DefaultEntitlementConfigTests.cs:176`
compares two calls in the same process, and G-1 serialises fixed ids without running the derivation. Hence G-5.)*

**What each golden asserts.**
- **G-1, per event type.** For **every key of `EventTypeMap`** (113 at `d092595f`; NR-1): one fixed instance; (1)
  `Serialize(instance)` equals the committed literal for that key; (2) `Serialize(Deserialize(key, literal))` equals
  the literal (the replay read of stored bytes); (3) **coverage:** the golden keys equal the map's keys (read by
  reflection on the private field — the test may not add an accessor to `src`).
- **G-2.** At least four `PayrollExportLine`s in non-sorted input order (two differing only in `Amount`; a negative
  amount; hours `7.40` and `0.5`): the `OrderLines` order, the `Serialize` literal, the `ComputeContentHash` literal (64
  uppercase hex), `Deserialize(literal)` equal to the lines and re-serialising to the literal; and the empty list
  (`[]` and its hash).
- **G-3.** At least three entry rows and three absence rows (a null start/end; a `TimeOnly` at 100-ns precision; hours
  `7.40`, `8.0`, `0.25`; typed text null, empty, and with Danish letters; the voluntary flag both ways): each line
  literal, each digest literal (64 lowercase hex), and both empty-list digests.
- **G-4.** One fully populated `PlanInputs` (pieces, config runs, event ids, digests, the optional members set): the
  `Serialize` literal and the deserialise-then-serialise literal.
- **G-5.** Two literals through public paths: the `ConfigId` of one config from
  `DefaultEntitlementConfigs.GetConfigsForAgreement("HK", "OK24")` (the VACATION entry); and the `SourceStintId` both
  halves carry after `MidnightCrossingNormalizer.Split` on a midnight-crossing entry whose `SourceStintId` is null
  (e.g. 22:00 → 06:00, hours `8.0`).

**The fixed instances (the contract; the factory is a hint).** Every public settable member set to a deterministic,
non-default value — including `DomainEventBase`'s `EventId` and `OccurredAt`, which otherwise default to
`Guid.NewGuid()` and `DateTime.UtcNow` (`src/SharedKernel/StatsTid.SharedKernel/Events/DomainEventBase.cs:5-6`) — and
every collection with at least two elements. Across the set, the values must exercise: Danish letters and the
characters the default encoder escapes (e.g. `"Ærø Å/ø <&'\"+>"`); decimal scale (`7.40`, `8.0`, `-0.25`); `DateTime`
of kind Utc and Unspecified; `DateOnly`; `TimeOnly`; an enum (as a string); a `Guid`; and at least one null optional
member (so `WhenWritingNull` is pinned). *Hint (HOW):* a test-side reflection factory that fills members by type, with
a per-type override table for the shape cases.

**Where.** New folder `tests/StatsTid.Tests.Unit/Serialization/Goldens/`: the facts, the factory, and one data file per
surface (G-1: one line per key, `<key>\t<json>`, sorted by key; the JSON is single-line). The items that copy the data
files to the output directory live in a **dedicated file, `tests/StatsTid.Tests.Unit/Goldens.props`, imported by the
Unit csproj with one `Import` line** (Codex cycle-2 NOTE), so FROZEN-CHECK can diff the whole file. **Line endings:**
this repository has no `.gitattributes` and `core.autocrlf` is `true` here, so a Windows checkout turns LF into CRLF;
the facts read the data files line by line and trim a trailing `\r`.

**How the literals are captured (run once, paste, commit).** On `master` at the S148-close runtime (net8.0, SDK
8.0.423): the agent runs the facts with the data files absent; each fails with a "golden capture" message and writes
the actual text to a file under the test output directory. The agent checks it (every key present; no `0000…` or
"now"-looking value; escapes as described), copies it **unedited** into the data files, and re-runs green. The
capture is a generated artifact: no hand edit. CI-W0 re-runs them on Linux; a red there is a platform difference and a
STOP (report), not a re-capture.

**The frozen files name no runtime version (Fable cycle-2 W1).** They outlive the runtime move and nobody may edit them,
while TASK-14908's verifying grep (`\.NET 8|net8\.0` over `tests`) must print nothing after it. So every docstring,
comment, test name and data-file header in TASK-14900's and TASK-14901's files says **"the pre-refresh runtime"**,
citing ADR-045, never ".NET 8" or "net8.0". This plan's prose names the version; the frozen files may not. Each task's
5a review runs that grep over its frozen paths before A0, and it must print nothing.

**Non-vacuity (local mutations on TASK-14900's reviewed tip; § "The mutation list").** M-1 … M-4, M-10, M-11 each
turn their golden red.

### P-2. The login pins (TASK-14901; plan-added, DV-1)

*Why:* IdentityModel 7 → 8 (W1) and JwtBearer 8 → 10 (W3) change the library that validates every login token, and
unit tests read tokens with the legacy handler (`REF:31`). Two existing non-Docker host tests run the real middleware
(`tests/StatsTid.Tests.Regression/Security/ExternalSendAuthorizationAndEnvelopeTests.cs:57-60`,
`…/RuleClassificationsLeastPrivilegeAcceptTests.cs:43`), but neither carries `scopes` nor any negative case. So the
security agent's written points become executable facts on the pre-refresh runtime (.NET 8) *before* W1, and stay unedited after W1 and W3.

**Where:** `tests/StatsTid.Tests.Regression/Security/TokenPathPinTests.cs` — a minimal in-memory host
(`WebApplication.CreateBuilder`, `UseTestServer` from `Microsoft.AspNetCore.TestHost`, which Mvc.Testing already
restores, Regression csproj `:14`), `AddStatsTidJwtAuth` (`src/Auth/StatsTid.Auth/JwtValidationSetup.cs:20-91`), and
one probe endpoint returning `GetActorContext()` (`src/Auth/StatsTid.Auth/ActorContext.cs:17-55`). Non-Docker, so it
runs in the local GATE. Plus `tests/StatsTid.Tests.Unit/Security/PasswordHashPinTests.cs`.

| Fact | Asserts |
|---|---|
| L-1 | A token minted by `JwtTokenService` (legacy handler, `JwtTokenService.cs:46-53`) with sub, name, role, employee id, agreement code, org id and **two** scopes passes; the probe's `ActorId`, `ActorRole`, `OrgId` and both `Scopes` (every field) equal what was minted (`scopes` JSON array, `JwtTokenService.cs:43`; read at `ActorContext.cs:29-52`). |
| L-2 | Claim types arrive unmapped: `sub` present and `ClaimTypes.NameIdentifier` absent; `role` present; `User.IsInRole(role)` true (`JwtValidationSetup.cs:27`, `:72`, `:83-84`). |
| L-3 / L-4 | Wrong issuer → 401; wrong audience → 401 (`:75-78`). |
| L-5 / L-6 | Expired 5 min ago → 401; expired 30 s ago → 200 (the 1-minute skew, `:81-82`). |
| L-7 | Signed with a different key → 401 (`:79-80`). |
| L-8 | An unsigned token (`alg: none`) → 401. |
| L-9a | The key-length minimum **at issuance**, as the pre-refresh runtime behaves today: expected, minting with a 31-byte key throws and a 32-byte key succeeds (the dev key is 49 bytes, `:18`). If today differs, the fact records what today does and the agent reports it. |
| L-9b | The key-length minimum **at the validator** (Codex W2): two HS256 tokens built independently of the signing library — header and payload base64url-encoded by the test, the signature computed with `System.Security.Cryptography.HMACSHA256` — one signed with a 31-byte key, one with a 32-byte key, each sent through `AddStatsTidJwtAuth` configured (`Jwt:SigningKey`) with the matching key. The two outcomes (status, and whether the probe was reached) are **captured on the pre-refresh runtime before W1 and frozen as literals**, whatever they are; W1 and W3 must reproduce them, and W3's security consult re-reads them for JwtBearer 10. |
| L-10 | A token minted by `JsonWebTokenHandler` (the Smoke helpers' path, `tests/StatsTid.Tests.Smoke/SmokeTests.cs:57`, `PayrollCalcAuditSmokeTests.cs:315`) gives the same `ActorContext` as L-1. |
| L-11 | BCrypt: `Verify(<the seed password>, <the seeded hash>)` is true and a wrong password false (the `$2a$11$…` hash seeded for every demo user, `docker/postgres/init.sql:1162`); `HashPassword` then `Verify` round-trips (`AdminEndpoints.cs:914`, `AuthEndpoints.cs:69`). |

### P-3. The API contract (W2, TASK-14905)

The two commands (named "P-3 (i)" and "P-3 (ii)" wherever they are run):
- **(i) The allow-listed parsed comparison.** `git show df582c20:docs/api/openapi.json > <scratch>/openapi.base.json`;
  `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi <scratch>/openapi.new.json`;
  `node .claude/plans/s149/compare-openapi.mjs <scratch>/openapi.base.json <scratch>/openapi.new.json`. The script (Node,
  because Python is not installed locally) parses both, removes the allow-listed JSON pointers, compares the trees and
  prints every other differing pointer; exit 0 only when none. **Allow-list: AL-1 `/openapi`** (the version string;
  OpenAPI.NET 2.x writes its own 3.0.x patch). **Array rule (cycle 1 N7, cycle 2 Codex W):** only the JSON Schema
  `required` keyword is a set — a `required` array whose parent is a schema, known by position (a value of
  `components.schemas` or `properties`; an `items`, `not` or `additionalProperties`; an element of `allOf`/`oneOf`/
  `anyOf`; a `schema` under a parameter, header or media type). Its members must be strings, compared without coercion;
  a non-string, added, removed or duplicate member is each a reported difference. Every other array is order-sensitive,
  including any array named `required` inside `example`, `examples`, `default` or `enum`, because order there can reach
  `api-types.ts`. Any other difference is a STOP: the filter is made to reproduce the old output, or the Orchestrator
  rules on that pointer in writing (sprint log), never by silently widening the list. *Sanity-checked for draft 3,
  read-only:* the base against itself → exit 0 (116 paths, 227 schemas); one `nullable: true` deleted → exit 1; one
  schema `required` reversed → exit 0; one member removed → exit 1; one `enum` reversed → exit 1; `"label"` changed to
  `["label"]` → exit 1; an example payload's `required` array reordered → exit 1 (its unreordered copy → exit 0).
- **(ii) `api-types.ts` empty diff.** `npm run gen:api`, then
  `git diff --exit-code --ignore-cr-at-eol df582c20 -- frontend/src/lib/api-types.ts` prints nothing. openapi-typescript
  7.13.0 is current, so W4 does not change the generator. CI's freshness gate (`ci.yml:291-300`, Linux) arbitrates line
  endings.

**Where they run (N1: the runtime is the second thing that can move the document):** TASK-14905 runs (i) itself on a
temp path before returning, so only AL-1 is left at its merge; the Orchestrator's REGEN at that merge runs (i) and
(ii) and commits `docs/api/openapi.json`; **W3's GATE** runs (i) on .NET 10; **C-2** runs (i) and (ii) on K.

- **The spec tests.** `SpecRuntimeMatcherTests` (`tests/StatsTid.Tests.Regression/Contracts/SpecRuntimeMatcherTests.cs`,
  pure System.Text.Json) runs **unedited**. `ResponseStrictTypesFilterTests`
  (`tests/StatsTid.Tests.Unit/OpenApi/ResponseStrictTypesFilterTests.cs`) **cannot**: it asserts on Microsoft.OpenApi
  1.x members that 2.x removes (`.Reference`, `.Nullable`, string `.Type`, e.g. `:74-75`, `:277-281`). It is translated
  one-for-one (same fact names, same count, each assertion re-expressed in the 2.x model) with a translation table in
  TASK-14905-T's report that also marks, **per fact, whether its shape occurs in the real document** (N6). A shape the
  real document does not contain is not covered by (i), so each such fact gets its own mutation (M-8a, M-8b, … named in
  the table) beside M-8 (DV-2, NR-2).
- **CI:** the drift gate (`tools/check_openapi_sync.py`, `ci.yml:157`) and the convention gate run at CI-W2 onward.

### P-4. The security agent's written check (TASK-14903; re-read for JwtBearer 10 at W3's security consult)

Saved verbatim as `.claude/reviews/S149-TASK-14903-security-check.md` before the merge. For each point: what the
code does (file:line), what IdentityModel 8.23.0 / JwtBearer 8.0.34 changes (with its source), the pin that covers it
(P-2 or an existing fact), and the verdict.
1. **Inbound claim mapping** — `JwtValidationSetup.cs:27` and `:72` (FAIL-001); L-2.
2. **The HMAC key-length minimum** — signing key `:80`, dev key `:18`; L-9a at issuance, L-9b at the validator.
3. **Lifetime, issuer and audience validation** — `:73-85`, skew `:82`; L-3 … L-7.
4. **The default handler** — JwtBearer 8+ validates with `JsonWebTokenHandler` (no `UseSecurityTokenValidators` set
   anywhere in `src`); unchanged in 8.0.34 and 10.0.15.
5. **The `scopes` JSON-array claim** — `JwtTokenService.cs:43`; `ActorContext.cs:29-52`; L-1.
6. **Issuance on the legacy handler** — `JwtTokenService.cs:46-53` (`System.IdentityModel.Tokens.Jwt` 8.23.0); L-1, L-10.
7. *(plan-added, DV-1)* **`CaseSensitiveClaimsIdentity`** — IdentityModel 8.0's breaking change: claim lookups become
   case-sensitive. Census `git grep -n -E "FindFirst|FindAll|HasClaim|RequireClaim|IsInRole|ClaimTypes\." -- src`, each
   hit classified (exact casing / not a claim lookup); e.g. `ActorContext.cs:19-23`,
   `DenialLoggingAuthorizationResultHandler.cs:125`, Payroll `Program.cs:512`, `:719`.

Plus: `dotnet list package --include-transitive` shows every `Microsoft.IdentityModel.*` and
`System.IdentityModel.Tokens.Jwt` at **8.23.0** in every project that resolves them.

### P-5. The Npgsql inventory and the hidden third exposure (TASK-14904 / -T)

**Census command** (both tasks; the true counts reported):
`git grep -n -E "ExecuteScalar(Async)?\(|\.GetValue\(|\.GetValues\(|GetFieldValue<object>|GetProviderSpecificValue" -- src tools tests`
At `d092595f`: `ExecuteScalar` 96 in `src`, 1 in `tools`, 557 in `tests`; `GetValue(s)` 5 in `src` (4 are
`PropertyInfo.GetValue`, `AuditProjectionBackfillService.cs:296-312`; 1 reader, `PostgresSegmentManifestStore.cs:241`, a
text array) and 49 in `tests`. Every hit is classified: **(a)** reads a `date` or `time` column untyped → changed
(10.x returns `DateOnly`/`TimeOnly` there; `timestamp`, `timestamptz` and `interval` are unchanged); **(b)** not a date
or time → unchanged, one-line reason; **(c)** a date/time read already typed (`GetDateTime`, `GetFieldValue<T>`) →
unchanged. Known (a) hits: `tests/…/Config/EntitlementConfigEndpointTests.cs:666`,
`…/Config/WageTypeMappingEndpointTests.cs:379`, `…/UserAgreementCode/AdminEndpointsAgreementCodeTests.cs:318-323`;
to classify: `…/Config/EndExclusiveMigrationTests.cs:248` (a `ToString()` of every column), and the untyped returns at
e.g. `…/HrFollowUp/EffectiveDateBoundaryTests.cs:692`, `…/Migrations/BackdateWorklistMigrationTests.cs:366`,
`…/ReportingLine/DelegationEffectiveFromClockPinTests.cs:603`, `…/Leaver/LeaverPastGraceByDateTests.cs:655`. The
`src` hits read strings and numbers (spot-checked, e.g. `EmployeeProfileRepository.cs:1747-1748`,
`UserAgreementCodeRepository.cs:812-813`); the census confirms. `time` columns exist
(`docker/postgres/init.sql:932-941`, the office hours).

**The infinity default (a)** stays on in 10.x. The workaround stays; its three comments become true-now
(`EmployeeProfileRepository.cs:704-723`, `EmployeeProfileSeeder.cs:70-75`, `UserOrganisationRepository.cs:52-56` say
"Npgsql 8 maps…"). The SQL-side assertions stay: `ProfileCreateSingleWritePathTests.cs:170-196` (`isfinite`) and
`UserAgreementCodeBackfillSeederTests.cs`.

**The third exposure (BC-9).** Npgsql 10 renamed its tracing tags. `DbCommandCounter`
(`tests/StatsTid.Tests.Regression/Performance/S106SeedScalePerfTests.cs:797-830`) filters on `db.connection_string` and
reads `db.statement`; at v10.0.4 those tags no longer exist (`server.port`, `db.query.text` replace them), so the counter
counts **0**. Then `Assert.Equal(4, count)` (`:114`, `:192`) fails loudly, but `count <= 5` (`:154`) and
`Assert.Equal(count10, count200)` with `count200 <= 12` (`:303-304`) **pass vacuously**. TASK-14904-T re-points the
counter (filter on `server.port`, statement from `db.query.text`, command spans only — Npgsql 10 also traces physical
connection opens) and adds a non-vacuity assertion (`count > 0`) beside each measured block. Docker-gated (`:51`), so
judged at CI-W2: the four bounds hold with non-zero counts.

**Both majors' breaking changes (N3).** The move crosses Npgsql 9.0 and 10.0, so TASK-14904's brief classifies every
item of both "Breaking changes" sections (`npgsql.org/doc/release-notes/9.0.html`, `…/10.0.html`) as applies /
does not apply, with the evidence. Pre-classified at `d092595f`: **9.0** — .NET Standard 2.0 dropped (n/a, net8);
SSL callbacks, Server Compatibility Mode and Load Table Composites obsoleted (none used: `git grep` finds none);
**`Connection Lifetime` now defaults to 1 hour** (applies: pooled connections recycle hourly; no behaviour this sprint
pins depends on connection age); replication options (n/a). **10.0** — .NET 6 dropped (n/a); `date`/`time` mapping
(above); `cidr` → `IPNetwork` (no `cidr` column); tracing and metrics renamed (BC-9); a harmless `libgssapi_krb5`
log line on Linux without Kerberos (may appear in container logs); root-CA-only TLS (no TLS configured); COPY timeout
semantics (no COPY used); `PostgresException.BatchCommand` null by default (14 `NpgsqlBatchCommand` uses in tests, none
reads the exception's property); `DataTypeName` precedence (unused); text COPY return types and slim-builder JSON (n/a).

### P-6. The existing pins

- **PIN-1** (`tests/StatsTid.Tests.Regression/PayPin/DemoPayLinesPinTests.cs:44-49`): 2/2 at every checkpoint. It covers
  six employee-months with no flex payout and no sick day (`REF:48`), so **Regression is the wider pay guard**.
- **PIN-2** (`ConfigSeedParityTests.cs:68`): green at every checkpoint, no re-pin.
- **`CommittedDemoSeedTests`** (`tests/StatsTid.Tests.DemoSeed/CommittedDemoSeedTests.cs:22-30`): green locally at every
  GATE (DemoSeed suite, non-Docker) and in CI.
- **`SegmentSerializationParityTests`** (Unit, literals `:77-91`) and **`GoldenLegacyPinTests`** (DemoSeed, with its
  `Golden/` files) are existing literal pins.

Their files, and PIN-1's golden texts and the committed seed, are in FROZEN-CHECK (checklist item 3). PIN-1's and
PIN-2's harness files (`DemoWorldPayRun.cs`, `ConfigSeedParityTests.cs`) are not frozen: TASK-14906 changes their
Testcontainers builder line (BC-3), nothing else.

### P-7. The suite counts

| Suite | S148 close | Expected after W0 | W1 | W2 | W3 | W4 | K (C-2) |
|---|---|---|---|---|---|---|---|
| Unit | 1848 | 1848 + g (G-1's 113 theory rows ×2 + coverage + G-2…G-5 + L-11; exact g in 14900/14901's reports) | = | = (± only if xunit 2.9.3 changes theory-row discovery — explained by name, never "noise") | = | = | = |
| DemoSeed | 180 | = | = | = | = | = | = |
| Regression (CI) | 2978 | 2978 + l (L-1 … L-10, L-9a and L-9b counted apart) | = | = (+ any fact a census names) | = | = | = |
| Regression non-Docker (local) | 148 | 148 + l | = | = | = | = | = |
| Smoke (CI) | 10 | = | = | = | = | = | = |
| vitest | 122 files / 1405 | = | = | = | = | = (harness edits only, § TASK-14913) | = |
| e2e (CI) | green | green; count recorded (O-1) | = | = | = | = | = |

Every Docker fact is judged **executed** (not skipped) from the CI trx files, by name.

### P-8. Gates that could go quiet

A gate that silently stops matching is worse than a red one.
- **CA2100 ratchet** (`ci.yml:130-155`) counts `path(line,col): warning CA2100` lines; a changed log format would read
  0 and only *warn* (`:153-154`). At CI-W3 and C-2 the line `CA2100 distinct sites: N (baseline 115)` is read: N must be
  plausible (> 0) and ≤ 115. A rise needs an explicit owner ruling (`REF:214`).
- **The vulnerable-package step** (`ci.yml:107-118`) greps `dotnet list package --vulnerable` output; at CI-W3 its log is
  read by eye (per-project lines and table format unchanged under SDK 10).
- **The no-`as` lint** (`npm run lint`, `frontend/eslint.config.mjs`): mutation M-9 proves it still bites after ESLint 10.
- **The Npgsql query counter:** P-5.
- **SDK 10's restore audit (N5, pre-ruled).** Since SDK 9 the NuGet audit covers transitive packages, so a Low or
  Moderate advisory (NU1901/NU1902) could fail restore in a warn-as-error project, where CI's policy is to warn
  (`ci.yml:107-118`: High/Critical fail, Low/Moderate warn). **Only if it fires**, the flip sweep reports the line
  under "Needs a decision" and edits nothing for it; the **Orchestrator** then applies F-10
  (`<WarningsNotAsErrors>NU1901;NU1902</WarningsNotAsErrors>` in `Directory.Build.props`) under the Small Tasks Exception
  and records the advisory (Fable cycle-2 N1). NU1903/NU1904 (High, Critical) stay errors.

### The mutation list (local; never by reverting on the branch)

**Where each runs (Codex W3).** In a detached worktree at the **reviewed task tip** — the commit that holds the task's
tests and fixtures; for a paired pass, the completed pair's tip. The agent runs its mutations on its own tip before
returning; if a review fix-up changes the tests, the fixtures or the mutated file, they are re-run on the new reviewed
tip. **The sha used is recorded** in the task's sprint-log merge row. **Exception, M-9** (Fable cycle-2 N2): a detached
worktree has no `node_modules`, so M-9 runs inside the W4 worktree by Block A's patch method — insert the mutation,
lint, `git checkout --` the file — at 14912's tip.

| Id | Task (tip) | Mutation (keep the API, break one behaviour) | Expected red |
|---|---|---|---|
| M-1 | 14900 | `EventSerializer` options: drop `DefaultIgnoreCondition` (`EventSerializer.cs:13`) | G-1 (the null-omission instance at least) |
| M-2 | 14900 | `ManifestJsonOptions`: `WriteIndented = true` (`PayrollExportManifest.cs:29`) | G-2 (literal and hash) |
| M-3 | 14900 | `HoursDigest`'s number form keeps trailing zeros | G-3 |
| M-4 | 14900 | `PeriodCalculationService.JsonOptions`: drop the string-enum converter (`PeriodCalculationService.cs:332`) | G-4 |
| M-10 | 14900 | `DeriveStintId`'s key formats hours as `{entry.Hours:F2}` (`MidnightCrossingNormalizer.cs:201-202`) | G-5 (stint id) |
| M-11 | 14900 | `DeterministicGuid`'s input drops its `entitlement:` prefix (`DefaultEntitlementConfigs.cs:55`) | G-5 (config id) |
| M-5 | 14901 | `ValidateAudience = false` (`JwtValidationSetup.cs:77`) | L-4 |
| M-6 | 14901 | `MapInboundClaims = true` (`:72`) | L-2 |
| M-7 | 14901 | `ClockSkew = TimeSpan.FromMinutes(10)` (`:82`) | L-5 |
| M-8 | the 14905 pair | `WrapAsNullableComplex` no longer marks the wrapper nullable (`ResponseStrictTypesFilter.cs:373`, 2.x equivalent) | the translated `ClrNullableComplexMember_EmitsAsNullableAllOfWrapper_AndIsRequired` and P-3 (i) |
| M-8a … | the 14905 pair | one per translated fact whose shape the real document lacks (N6; named in the translation table) | that fact |
| M-9 | 14912 (in the W4 worktree, patch method) | insert `const x = 1 as unknown` into a FULL-tier file (e.g. `src/hooks/useForest.ts`) | `npm run lint` exits non-zero |
| ~~M-12~~ | — | **Dropped at Step 0b cycle 3** (both lenses: an uncommitted mutation cannot show in a commit-to-commit diff, and before A0 the line does not apply). A whole-file diff of a committed file needs no mutation proof. | — |

No CI mutation run is planned: every pin above is local, and the Docker-side counter (P-5) gets its non-vacuity from
an assertion, not a mutation. L-9b has no mutation: its literals are whatever the pre-refresh runtime does, and their
job is to detect movement.

---

## The merge checklist — every task, paired pass and stack

1. **Report read.** "Brief contradictions", "Declared deviations", "Not built / not checked", "Found, not fixed",
   "Shortcuts taken" each dispositioned: factual ones by the Orchestrator; a requirement, invariant or ruling question
   to the `adjudicator`.
2. **Build.** 0 errors. Warnings reported against **126**, each new one by rule id and project. **CA2100 ≤ 114 locally**
   (CI ratchet 115). Production warn-as-error and the opt-out list unchanged: `git diff df582c20 -- Directory.Build.props`
   prints nothing (`Directory.Build.props:22`, `:48-79`, `:102-110`), except F-10's one line if the restore audit fired
   (P-8). A new test-project CA2100 gets the QUAL-073 justified suppression.
3. **FROZEN-CHECK** (N2; A0 = the sha CI-W0 ran green on; before A0 exists, only the first line applies). Lines 1 and 2
   print nothing; line 3 prints exactly one line, a whole-line match of the `Import` (an anchored pattern; `git grep` has no `-x`) with no `Condition` or other attribute, so a disabled or altered import fails it (Codex cycle-3 NOTE); and the local pins (G-1…G-5, L-1…L-11,
   `CommittedDemoSeedTests`, `GoldenLegacyPinTests`, `SegmentSerializationParityTests`) are green:
   ```
   git diff --stat df582c20 HEAD -- tests/StatsTid.Tests.Regression/PayPin/Golden docker/postgres/99-demo-seed.sql tests/StatsTid.Tests.Unit/Segmentation/SegmentSerializationParityTests.cs tests/StatsTid.Tests.DemoSeed/GoldenLegacyPinTests.cs tests/StatsTid.Tests.DemoSeed/Golden tests/StatsTid.Tests.DemoSeed/CommittedDemoSeedTests.cs
   git diff --stat <A0> HEAD -- tests/StatsTid.Tests.Unit/Serialization/Goldens tests/StatsTid.Tests.Unit/Goldens.props tests/StatsTid.Tests.Regression/Security/TokenPathPinTests.cs tests/StatsTid.Tests.Unit/Security/PasswordHashPinTests.cs
   git grep -nE '^\s*<Import Project="Goldens\.props" */>\s*$' HEAD -- tests/StatsTid.Tests.Unit/StatsTid.Tests.Unit.csproj
   ```
   The whole `Goldens.props` is diffed, so no change to the copy items can fall outside the diff's context (Codex
   cycle-2 NOTE). A whole-file diff of a committed file needs no mutation proof, so M-12 is dropped (Step 0b cycle 3). A
   disabled import would also fail loudly: the golden facts could not find their data files.
4. **Step 5α — `constraint-validator`** on `git diff <base>...<branch>`; scope = the task's authorized files (a paired
   branch or stack: the union; any `src/**` change in a `-T` pass's own commits is a violation).
5. **Security consult** where marked: a `security` spawn, told it is read-only, reads the brief and the diff, returns
   the negative-test list and any residual; saved as `.claude/reviews/S149-TASK-<n>-security-consult.md`.
6. **Step 5a — internal lens** (`reviewer`, Fable 5.2) with the pasted brief; saved as
   `.claude/reviews/S149-TASK-<n>-5a-reviewer.md` **before acting**. Concrete-case rule; an `(unproven)` NOTE is recorded
   with its evidence requirement.
7. **Step 5a — Codex** on the rows marked, inside the task's worktree with the commit range named:
   `codex exec "<prompt naming <base>..<tip>>" < /dev/null --output-last-message <file>`; saved as
   `.claude/reviews/S149-TASK-<n>-5a-codex.md` (+ `.log`). If its sandbox blocks its shell, the inlined-source fallback,
   and the verdict says so.
8. **Merge, then GATE** on master; the sprint-log row `task · branch · build · 5α · consult · 5a-R · 5a-C · mutation
   sha · merged sha`; the handoff row. **An empty 5α or 5a cell blocks the merge.** Cap: 5 cycles per lens per task.
   *W4 instead:* each task's review runs on its diff from the previous W4 tip and its GATE on `s149/w4`; the row's
   "merged sha" is its W4 tip; the one merge into master follows CI-W4 and CI-W3 green (Block W4).
9. **Shortcuts (PAT-032)** registered and re-tagged in the same merge; the code's `shortcut` comments checked both ways.
10. **M4 at merge** *(new; S148's register row found the per-task M4 entries were not kept,
    `docs/operations/model-routing-register.md:129`)*: every `(unproven)` NOTE either lens raised is written into the
    task's sprint-log entry at the merge, with its evidence requirement and disposition.
11. **Wave end:** prune the wave's merged worktrees, unlinking `node_modules` junctions first; `git worktree list` shows
    only the main checkout — and, until W4 merges, the W4 worktree — before the next wave dispatches.

| Task | 5α | Security consult | 5a internal | 5a Codex |
|---|---|---|---|---|
| 14900 | ✓ | — | ✓ | ✓ payroll export (the content hash and digests the proof rests on) |
| 14901 | ✓ | ✓ (are L-1…L-11, with L-9a and L-9b, the right negative set?) | ✓ | ✓ JWT/auth |
| 14902 | ✓ | — | ✓ (a CI edit can silently disable a gate) | — |
| 14903 + -T | ✓ (union) | — (the security agent is the implementer; its check is P-4) | ✓ | ✓ JWT/auth |
| 14904 + -T | ✓ (union) | — | ✓ | ✓ the data access every pay input passes through (discretion) |
| 14905 + -T | ✓ (union) | — | ✓ | ✓ the integration contract (discretion) |
| 14906 | ✓ | — | ✓ | — (test-only; the count check is its guard) |
| 14907 | ✓ | — | ✓ | ✓ password verification (JWT/auth category) |
| the W3 stack (14908 + any of 14909–14911, 14917–14919) | ✓ (union) | ✓ (P-4's seven points re-read for JwtBearer 10.0.15, L-9b's frozen outcomes included) | ✓ | ✓ everything runs on it |
| 14912, 14913, 14914, 14915 | ✓ | — | ✓ | — |
| 14916 (Orchestrator) | self-check | — | at Step 7a | at Step 7a |

**GATE (the Orchestrator's post-merge gate).** Build `-m:2` in the foreground, one at a time (a backgrounded build can be
reaped for memory); Unit; DemoSeed; Regression `Category!=Docker`; FROZEN-CHECK. When the frontend changed:
`npx tsc --noEmit`, `npm run lint`, `npm run typecheck:e2e`, `npm run build`, `npx vitest run` (`docs/WORKFLOW.md:29`).
**W3's GATE adds P-3 (i) on .NET 10** (N1). Exit statuses read from the unpiped command.

**REGEN (Orchestrator; `docs/**` is Orchestrator-only).** At TASK-14905's merge:
`dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi` (writes `docs/api/openapi.json`), then P-3 (i)
against it and P-3 (ii). **Expected drift: AL-1 only in `openapi.json`; none in `api-types.ts`.**

---

## Standing constraints — pasted verbatim into every agent prompt

**Block A — every agent** (a W4 brief replaces the first instruction with Block W4).
```
★ FIRST, BEFORE ANY WORK: check your worktree against master.
Run `git log --oneline -1` and compare with the main checkout's LOCAL master — not `origin/master`,
which lags by design because this project pushes only at sprint close. Fast-forward if you are
behind, and say in your report what you found.
```
```
Do NOT use `git stash` — it is repository-global, not per-worktree, and will collide with
other agents running concurrently. To prove a RED by reverting a production change:
    git diff > <scratch>/mychange.patch
    git checkout -- <the production files>
    ... run the test, capture the failure output ...
    git apply <scratch>/mychange.patch

KINDS. Every new fact says in its docstring which kind it is:
 R  a regression pin (an existing behaviour kept). Almost every fact this sprint is R: S149 changes no
    behaviour. Prove it can fail by ONE named mutation from the plan's M-list, in a worktree at YOUR TIP —
    the commit that holds your tests and fixtures (a paired pass: the completed pair's tip; a W4 task: your
    tip on s149/w4): `git worktree add --detach <path outside the repo> <that sha>`, apply the mutation,
    run the fact, capture the assertion failure, `git -C <path> checkout -- .`, `git worktree remove
    --force <path>`, `git worktree prune` (never cp/Copy-Item a backup back: FAIL-005). Report the sha.
    If a fix-up later changes your tests, fixtures or the mutated file, re-run on the new tip. A compile
    error never counts. (A mutation that needs node_modules runs in your own worktree by the patch method
    above, then `git checkout --` the file.)
 F  an assertion that FAILS ON TODAY'S CODE (rare this sprint; name the assertion under "Red conditions").
In a FROZEN file (below), no docstring, comment, test name or data header names a runtime version: write
"the pre-refresh runtime" (ADR-045), never ".NET 8" or "net8.0". The file outlives the runtime move and no
one may edit it afterwards.
TEST NAMES ARE CONTRACT where a brief gives them; if you must rename, report old → new.

FROZEN PINS. Never edited by any task: tests/StatsTid.Tests.Unit/Serialization/Goldens/**,
tests/StatsTid.Tests.Unit/Goldens.props (and the Unit csproj's Import line for it),
tests/StatsTid.Tests.Regression/Security/TokenPathPinTests.cs, tests/StatsTid.Tests.Unit/Security/PasswordHashPinTests.cs,
tests/StatsTid.Tests.Regression/PayPin/Golden/**,
docker/postgres/99-demo-seed.sql, tests/StatsTid.Tests.Unit/Segmentation/SegmentSerializationParityTests.cs,
tests/StatsTid.Tests.DemoSeed/GoldenLegacyPinTests.cs, tests/StatsTid.Tests.DemoSeed/Golden/** and
tests/StatsTid.Tests.DemoSeed/CommittedDemoSeedTests.cs. If one goes red, STOP and report: the upgrade is
wrong, not the pin. Never re-pin, never re-capture.

CENSUS RULE. Where a brief names a census COMMAND (`git grep`, so bin/obj never match), classify EVERY hit as
(a) authorized for edit, (b) expected and unchanged (one-line reason), or (c) generated (regenerated, never
hand-edited). Report the true counts. An unclassified hit is a STOP and report, never an edit.

VERSIONS. The target versions in your brief are contract. A newer PATCH of the same major.minor released
since 2026-11-27 may be taken and is reported. Anything else is a STOP and report.

GREP GATES run over `src tests frontend/src tools` only; hits under docs/** and .claude/** are history.
```
- **`docs/CONVENTIONS.md` verbatim** — the Orchestrator pastes it into every prompt.
- **KB entries:** the ledger's "KB pasted" column, pasted whole.
- **Docker is unavailable locally.** Docker facts are reported as **"CI-pending"**; the sprint log flips one to
  "CI-verified" only with the run id that showed it green. Smoke and e2e run only in CI.
- **Python is not installed locally:** `tools/check_docs.py`, `tools/check_openapi_*.py` are CI-only.
- **Read exit status from the unpiped command.**
- **Never edit a generated file by hand:** `docs/api/openapi.json`, `frontend/src/lib/api-types.ts`,
  `docs/generated/db-schema.md`. **No agent edits `docs/**`.**
- **Reseed, don't migrate.** No schema change is expected this sprint; one would be a STOP.
- **Comments say what is true now** (PAT-032): a comment naming an old version ("Npgsql 8 maps…", ".NET 8 minimal-API",
  "Swashbuckle 6.6.2 drops…") that your task makes false is rewritten in the same change, version-free where it can be.
- **Report anything the brief does not name.** **Contract vs. hint:** deviate from a hint freely and say so; from a
  contract only by reporting.

**Block B — every implementer and `ux` brief, verbatim from `docs/AGENTS.md:328-364`:** the SMALLEST COMPLETE CHANGE
block (identity `f57be923033c5ab9`, `docs/knowledge-base/patterns/PAT-032-…md:116`) and the BRIEF CHALLENGE block. Each
brief then names, from the per-task notes, its **never-extras**, its **"Not built"** list and its **likely
`shortcut(pending)` limits**.

**Block D — every paired `-T` pass:** you are on another agent's branch. Edit only the listed files and **never
`src/**`**; a production defect is reported, not fixed. Change an expected value only where the brief lists it as
intentional; any other value that changes is a **STOP and report**. The frozen pins are never touched.

**Block S — every `sweep` brief:** the exact edit table, every site listed before editing, a per-file summary after,
the verifying `git grep`; a site that does not match the pattern goes under "Needs a decision", untouched. A sweep makes
no conditional edit: anything that would need a judgment (e.g. an NU1901/NU1902 line in the restore output) is
reported under "Needs a decision" for the Orchestrator.

**Block W4 — every W4 brief (TASK-14912 … 14915), in place of Block A's first instruction** (Codex W1):
```
★ YOU WORK ON THE FRONTEND BRANCH s149/w4, NOT ON MASTER.
The Orchestrator created s149/w4 from <A0, the sha CI-W0 ran green on> and keeps it in ONE worktree,
<absolute path>, until W4 merges. Work only there, with absolute paths.
FIRST: run `git log --oneline -1` there and confirm HEAD is <the tip this brief names: A0 for
TASK-14912, the previous W4 task's reviewed tip after that>. Say in your report what you found.
Do NOT fast-forward, merge, rebase or cherry-pick from master. W1–W3 land on master meanwhile; W4's
evidence run (CI-W4) must hold W0 + W4 only.
Your gate runs on this branch: npx tsc --noEmit, npm run lint, npm run typecheck:e2e, npm run build,
npx vitest run (the backend here is W0's and unchanged). Commit your work on s149/w4. Nothing of yours
reaches master until the Orchestrator merges the whole branch after CI-W4 and CI-W3 are both green.
```
*The Orchestrator's side:* each W4 task is reviewed on its diff from the previous W4 tip and gated on the branch;
after TASK-14915, CI-W4 runs on the branch tip; when CI-W4 and CI-W3 are both green, `s149/w4` is merged into master,
the full GATE (backend and frontend) and FROZEN-CHECK run on master, and the W4 worktree is pruned (its
`node_modules` junction unlinked first).

---

## Task ledger

*Each brief will be `.claude/plans/s149/TASK-<id>.md`, pasted whole with Blocks A, B, D, S or W4 as they apply, the plan's
sections it names, and the KB entries listed. The sprint log carries a "## Task ledger" with DONE / CUT / DEFERRED /
DROPPED for every id below (close guard, `docs/WORKFLOW.md:47`). Cross-domain labels are written as the sprint log
will carry them (`docs/AGENTS.md:72-85`).*

| Task | Wave | Role (model) | What | Authorized files | KB pasted | Depends on | Size |
|---|---|---|---|---|---|---|---|
| **TASK-14900** | 0 | `test-qa` (Sonnet) | **The persisted-bytes goldens G-1 … G-5** (P-1), captured on the pre-refresh runtime and committed; M-1 … M-4, M-10, M-11 (M-12 dropped at Step 0b cycle 3). **The brief says:** the Unit csproj's import is written exactly `<Import Project="Goldens.props" />`, with no condition (FROZEN-CHECK line 3 matches the exact line); the frozen files name no runtime version ("the pre-refresh runtime", ADR-045); 5a checks `\.NET 8\|net8\.0` prints nothing over them | new files under `tests/StatsTid.Tests.Unit/Serialization/Goldens/**`; new `tests/StatsTid.Tests.Unit/Goldens.props`; one `Import` line in the Unit csproj | DEP-003, ADR-005, ADR-034, PAT-032 | — | M |
| **TASK-14901** | 0 | `test-qa` | **The login pins L-1 … L-11** (P-2; L-9b's two outcomes captured on the pre-refresh runtime and frozen); M-5 … M-7. **The brief says:** the frozen files name no runtime version ("the pre-refresh runtime", ADR-045); 5a checks `\.NET 8\|net8\.0` prints nothing over them | new `tests/StatsTid.Tests.Regression/Security/TokenPathPinTests.cs`, new `tests/StatsTid.Tests.Unit/Security/PasswordHashPinTests.cs` | ADR-007, ADR-009, FAIL-001 | — | S–M |
| **TASK-14902** | 0 | `sweep` (Haiku) — `CI workflows (cross-domain authorized)` | **CI hygiene, exact spec:** every `uses:` to the newest major per the Actions table; `node-version: '20'` → `'24'` (`ci.yml:285`, `:339`); nothing else | `.github/workflows/ci.yml`, `claude.yml`, `claude-code-review.yml` | — | — | XS |
| **TASK-14903** | 1 | `security` (Sonnet) — `Security & Compliance (extended into src/Auth/StatsTid.Auth/StatsTid.Auth.csproj and the JwtBearer line of tests/StatsTid.Tests.Unit/StatsTid.Tests.Unit.csproj, and into JwtValidationSetup.cs and JwtTokenService.cs only if the check finds a change is needed, cross-domain authorized)` | **IdentityModel 7.1.2 → 8.23.0** through explicit same-version references in `StatsTid.Auth` (the family per the NuGet table); **JwtBearer 8.0.8 → 8.0.34 in both places at once** (one version across the graph, else NU1605: Unit references Infrastructure → Auth, Fable W1); the **written check P-4**; the solution builds | `StatsTid.Auth.csproj`; Unit csproj `:15`; conditionally the two `.cs` files | ADR-007, ADR-009, FAIL-001 | CI-W0 green | S |
| TASK-14903-T | 1 (paired) | `test-qa` on 14903's branch | Smoke csproj `:14` JsonWebTokens → 8.23.0; every auth fact and the login pins run unedited; a new fact only where P-4 names a gap | the Smoke csproj; new test files P-4 names | ADR-007, ADR-009 | 14903 returned | S |
| **TASK-14904** | 2 | `backend-infrastructure` (Opus) — `Backend + Infrastructure (extended into the Npgsql PackageReference line of tests/StatsTid.Tests.Regression, tests/StatsTid.Tests.Smoke and tools/StatsTid.DemoSeed csproj, cross-domain authorized)` | **Npgsql 8.0.3 → 10.0.4** in all four projects at once (one version across the graph, else NU1605); the `src`/`tools` half of P-5's census; every item of the 9.0 and 10.0 breaking-changes sections classified (P-5, N3); the three infinity comments true-now | Infrastructure csproj `:10`; Regression csproj `:17`; Smoke csproj `:19` (and its comment `:15-18`, which names 8.0.3); DemoSeed tool csproj `:27`; the comment lines in P-5; any (a) hit in `src`/`tools` | ADR-001, ADR-040 | CI-W1 green | M |
| TASK-14904-T | 2 (paired) | `test-qa` on 14904's branch | The `tests` half of P-5's census; every (a) hit fixed; **`DbCommandCounter` re-pointed with non-vacuity** (P-5); the infinity facts still assert in SQL; the S106 comments `:24-28`, `:797-799` true-now | `tests/**` (each file named by the census) | ADR-001 | 14904 returned | M–L |
| **TASK-14905** | 2 | `backend-infrastructure` | **Swashbuckle 6.6.2 → 10.3.1**; `ResponseStrictTypesFilter` and `OpenApiSpecGenerator` rewritten for Microsoft.OpenApi 2.x; the document stays OpenAPI 3.0; no build-time document generation (see notes). **The brief says:** the Unit project is expected red until 14905-T (its filter tests read 1.x members), so this task builds `src` only; before returning it runs P-3 (i) on a temp path, so only AL-1 is left at the merge (Fable W1) | Backend.Api csproj `:13`; `src/Backend/StatsTid.Backend.Api/ResponseStrictTypesFilter.cs`; `…/OpenApiSpecGenerator.cs`; `…/Program.cs:74-102` (the `AddSwaggerGen` block) | PAT-012, PAT-010 | CI-W1 green | M |
| TASK-14905-T | 2 (paired) | `test-qa` on 14905's branch | `ResponseStrictTypesFilterTests` translated one-for-one with a translation table that marks, per fact, whether its shape occurs in the real document (DV-2, N6); M-8, and M-8a … for each shape the document lacks; `SpecRuntimeMatcherTests` untouched | `tests/StatsTid.Tests.Unit/OpenApi/ResponseStrictTypesFilterTests.cs` | PAT-012 | 14905 returned | S–M |
| **TASK-14906** | 2 | `test-qa` | **Test tooling:** xunit 2.9.3, xunit.runner.visualstudio 4.0.1, Test.Sdk 18.12.0, coverlet 10.1.1 in the four test csproj; **Testcontainers 4.17.0** with the 18 builders moved to `new PostgreSqlBuilder(<image>)` (BC-3); SSH.NET and the two legacy pins removed if `--vulnerable --include-transitive` stays clean (DV-4) | the four test csproj; the builder lines of the 18 fixture files | FAIL-002 | 14904 merged | M |
| **TASK-14907** | 2 | `backend-infrastructure` | **BCrypt.Net-Next 4.1.0 → 4.2.0**; L-11 green unedited | Backend.Api csproj `:9` | ADR-007 | 14905 merged | XS |
| **TASK-14908** | 3 | `sweep` (Haiku) — `Platform flip (cross-domain authorized): global.json, the 16 csproj, the 7 Dockerfiles, ci.yml's dotnet-version, the framework-tied package lines and version-bound comments` | **The mechanical flip** from the exact table below; reports every build error, new warning and restore-audit line (NU19xx) verbatim, an NU1901/NU1902 line under "Needs a decision" (F-10 is the Orchestrator's) | as the table | — | CI-W2 green; **O-0 done** | S |
| TASK-14909 | 3 (stacked) | reserved: `backend-infrastructure` (src/Backend, src/Infrastructure, src/Orchestrator, src/Integrations/StatsTid.Integrations.External, docker/mock-*; cross-domain as declared at dispatch) | Compiler/analyzer fallout in those projects, if the flip reports any | named at dispatch from the flip's report | — | 14908 returned | ? |
| TASK-14910 | 3 (stacked) | reserved: `payroll-integration` (Opus) | Fallout in `src/Integrations/StatsTid.Integrations.Payroll` | named at dispatch | — | 14908 returned | ? |
| TASK-14911 | 3 (stacked) | reserved: `test-qa` — `Test & QA (extended into tools/**, cross-domain authorized)` | Fallout in `tests/**`, `tools/**` | named at dispatch | — | 14908 returned | ? |
| TASK-14917 | 3 (stacked) | reserved: `rule-engine` (Opus) | Fallout in `src/RuleEngine/**` and `src/SharedKernel/**/Calendar/**` (warn-as-error, Fable W2) | named at dispatch | — | 14908 returned | ? |
| TASK-14918 | 3 (stacked) | reserved: `data-model` (Sonnet) — `Data Model (extended into the rest of src/SharedKernel/** outside Calendar/ and Security/, cross-domain authorized)` | Fallout in the rest of `src/SharedKernel/**` | named at dispatch | — | 14908 returned | ? |
| TASK-14919 | 3 (stacked) | reserved: `security` — `Security & Compliance (extended into src/Auth/**, cross-domain authorized)` | Fallout in `src/Auth/**` and `src/SharedKernel/**/Security/**` | named at dispatch | — | 14908 returned | ? |
| **TASK-14912** | 4 | `ux` (Sonnet) | **Toolchain:** vite 8.4.0, @vitejs/plugin-react 6.3.0, vitest 5.1.2, jsdom 30.1.3, @types/node 24.19.4, eslint 10.15.0, @typescript-eslint/parser 8.74.0; typescript stays 5.9.3 (held); M-9 | `frontend/package.json`, `package-lock.json`, `vite.config.ts`; `eslint.config.mjs` and `src/test/setup.ts` only where the new majors require | — (`docs/FRONTEND.md`) | CI-W0 green; on `s149/w4` from A0 (Block W4, as are 14913–14915) | M |
| **TASK-14913** | 4 | `ux` | **React 19 family:** react, react-dom 19.3.1; @types/react 19.3.3, @types/react-dom 19.3.0; @testing-library/react 16.3.3, user-event 14.6.13, jest-dom 7.0.1; the six Radix minors; fontsource 5.3.0 | package files; `src/**` where React 19 removes an API; test harness lines per the notes | — (`docs/FRONTEND.md`) | 14912 | M–L |
| **TASK-14914** | 4 | `ux` | **react-router 8.5.3** replacing react-router-dom 6.30.3 (DV-5). **Count references, not imports** (N4): `grep -rn "react-router-dom" src e2e` → 60 lines in 54 files at `d092595f` = 54 imports, the four `vi.mock('react-router-dom')` / `vi.importActual` strings (`src/pages/__tests__/ArsoversigtPage.test.tsx:30-31`, `…/ArsoversigtPage.childSickAllowance.test.tsx:17-18`; allowed harness edits) and two comments; every one moves to `react-router` (`react-router/dom` only for `RouterProvider`/`HydratedRouter`, none used today); `App.tsx:2`, `:64-66` keep `BrowserRouter` | package files; the 54 referencing files | — (`docs/FRONTEND.md`) | 14913 | M |
| **TASK-14915** | 4 | `ux` | **@playwright/test 1.61.0 → 1.65.1**; `typecheck:e2e` green | package files; `playwright.config.ts` and `e2e/**` only if the new version requires | — | 14914 | XS |
| **TASK-14916** | 5 | Orchestrator | **`dependabot.yml`**: grouped (minor+patch / major per ecosystem), every hold-back's `ignore` rule from the hold-back table, the docker entries consolidated with `directories:` (the `/docker` entry has no Dockerfile at that path, `.github/dependabot.yml:52-63`), no compose ecosystem | `.github/dependabot.yml` | — | CI-W3 green, W4 merged | S |

**O-0 — before TASK-14908 dispatches (Orchestrator; owner ruling NR-3 (b) "Install it", 2026-11-27).** Install the
.NET 10.0.4xx SDK on the owner's machine, side by side with 8.0.423 and 10.0.302 (nothing uninstalled). Record
`dotnet --list-sdks` in the sprint log showing a 10.0.4xx entry. With F-1's `10.0.100` + `latestFeature`, local builds
then resolve the newest installed 10.0 band (4xx) and CI's `10.0.x` resolves 10.0.4xx too, so both build with the same
band; the GATE line names the resolved SDK (`dotnet --version` at the repo root).

**TASK-14908's exact table (Block S).**

| # | Site | From | To |
|---|---|---|---|
| F-1 | `global.json:3` | `"8.0.0"` | `"10.0.100"` (`rollForward: latestFeature` kept; NR-3 ruled, O-0 done first) |
| F-2 | `<TargetFramework>` in all 16 csproj (`git grep -n "<TargetFramework>net8.0" -- '*.csproj'` → 16) | `net8.0` | `net10.0` |
| F-3 | the 7 Dockerfiles' two `FROM` lines | `mcr.microsoft.com/dotnet/sdk:8.0`, `…/aspnet:8.0` | `…/sdk:10.0`, `…/aspnet:10.0` |
| F-4 | `ci.yml:102`, `:227` | `'8.0.x'` | `'10.0.x'` |
| F-5 | JwtBearer at `StatsTid.Auth.csproj:9`, Unit csproj `:15` | `8.0.34` | `10.0.15` |
| F-6 | Mvc.Testing, Regression csproj `:14` | `8.0.*` | `10.0.15` |
| F-7 | Logging.Abstractions, Regression csproj `:15` | `8.0.3` | `10.0.15` |
| F-8 | the ".NET 8" comments: 22 `src/Backend/StatsTid.Backend.Api/Contracts/*.cs` (one line each; `git grep -n "\.NET 8" -- src/Backend/StatsTid.Backend.Api/Contracts` → 22), `tests/StatsTid.Tests.Regression/Contracts/ContractAssert.cs:22`, `tests/StatsTid.Tests.Unit/Security/AuthorizationPolicyTests.cs:179`, `:266` | ".NET 8 minimal-API …" / ".NET 8 host" | version-free: "the minimal-API …" / "the .NET host" |
| F-9 | path comments: Regression csproj `:77`; `AuditProjectionParityTests.cs:35`; `AuditProjectionVisibilityTests.cs:28`; `BugCorrectionHistorySchemaTests.cs:68`, `:82-83` | `bin/Debug/net8.0` | `bin/Debug/net10.0` |
| F-10 | *not the sweep's edit:* **the Orchestrator** applies it under the Small Tasks Exception, **only if** the sweep reports NU1901 or NU1902 failing a warn-as-error project (Fable cycle-2 N1): `Directory.Build.props`, the first `PropertyGroup` (`:13-40`) | — | `<WarningsNotAsErrors>NU1901;NU1902</WarningsNotAsErrors>`; the advisory recorded in the sprint log. NU1903/NU1904 stay errors. |

Verifying grep after the flip: `git grep -n -E "net8\.0|\.NET 8|sdk:8\.0|aspnet:8\.0|'8\.0\.x'" -- src tests tools docker .github global.json`
prints nothing (no code literal names `net8.0`; every hit at `d092595f` is a TFM, a FROM line or a comment).

**The fallout rule (W3).** Each new compiler or analyzer warning is fixed in code, or suppressed per rule with a cited
reason (a `NoWarn` entry in that project's csproj naming the register row, or a `#pragma` at the site with its reason).
`Directory.Build.props` changes by F-10 at most. The warn-as-error opt-out list is never widened. CA2100 may not rise
above the ratchet. C# 14's contextual `field` matters only inside property accessors; the `field` identifiers at e.g. `ConfigEndpoints.cs:693-709` are parameters and anonymous-type
members, outside accessors.

---

## Per-task contract notes — never-extras, "Not built", likely shortcuts

*The SMALLEST COMPLETE CHANGE block (Block B) applies in every implementer brief. "Never-extras" is the machinery an
invariant, ADR, KB entry, ruling or checklist demands — the trial's M5 watch list. "Likely shortcut" is a limit the
refinement already accepts — the M6 list.*

| Task | Never-extras (M5 watch) | Not built | Likely `shortcut(pending)` (M6) | Measured / `(unproven)` owed |
|---|---|---|---|---|
| 14900 | every map key covered; the coverage fact; the replay-read assertion; the null-omission case; G-5 through public paths; M-1…M-4, M-10, M-11 on the reviewed tip | no golden for audit snapshots | none | the data files' sizes; G-1's key count (113 expected); the mutation sha |
| 14901 | the negative cases L-3…L-8; both minting handlers (L-1, L-10); L-9a and L-9b, L-9b's tokens built without the signing library; M-5…M-7 on the reviewed tip | no browser login (e2e covers it) | none | L-9b's two captured outcomes; the mutation sha |
| 14902 | every `uses:` line; both Node lines | no workflow logic change | none | the action versions seen in CI-W0's logs |
| 14903 | the seven-point written check; the `--include-transitive` proof for every project, Unit included; L-1…L-11 green unedited | no switch to `JsonWebTokenHandler` for issuance (`REF:92`) | the legacy issuance handler stays (record as a "revisit" line if the check recommends moving) | — |
| 14904 (+T) | the complete census both halves; both releases' breaking changes classified; one Npgsql version across the graph; the SQL-side infinity asserts; the counter's non-vacuity | no `LegacyDateAndTimeResolverFactory` (the release notes' opt-out); no `NpgsqlDataSource` | none expected | the census counts by class; the counter's counts at CI-W2 |
| 14905 (+T) | `nullable`, `required`, `enum`, nullable items and the allOf wrapper reproduced; the document stays 3.0; P-3 (i) clean apart from AL-1 before return; the translation table's shape column and its extra mutations | no move to ASP.NET Core 10's built-in OpenAPI (`REF:139`); no OpenAPI 3.1 | none | **watch item `(unproven)`:** Swashbuckle 10 brings `Microsoft.Extensions.ApiDescription.Server` 8.0/10.0; if it makes `dotnet build` launch the app to generate a document, set `<OpenApiGenerateDocuments>false</OpenApiGenerateDocuments>` and report. *Hint:* the maintainers recommend 9.0.6 as a stepping stone before 10 (Swashbuckle 10.0.0 release notes); regenerating once at 9.0.6 separates Swashbuckle's own changes from OpenAPI.NET 2's. |
| 14906 | all four test csproj on one version each; 18 of 18 builders moved; the vulnerable check with the pins removed | no xunit v3 (`REF:138`) | none | **new xunit.analyzers (1.18.0) warnings reported by rule and count, not fixed** (NR-4) |
| 14907 | L-11 unedited | — | none | — |
| W3 stack | O-0 first; the fallout rule; CA2100 ≤ ratchet; the verifying grep; P-3 (i) on .NET 10; F-10 by the Orchestrator, only if the sweep reports it | no Postgres change; no `dotnet-sdk` Dependabot ecosystem | one per accepted suppression, registered | the warning delta by rule; any NU19xx line; the image-level smoke and e2e results (Ubuntu 24.04, ICU 74) |
| 14912 | M-9; every frontend gate | no TypeScript 6/7 | none | vitest wall time before/after (descriptive) |
| 14913 | **test edits allowed only in harness lines** (`act`/`waitFor` wrappers, render helpers, `src/test/setup.ts`); **an expected value never changes** — one that would is a STOP; each edited test listed with its reason | — | none | the list of harness edits |
| 14914 | all 60 references (imports, the four mock strings, the comments); the single splat route `App.tsx:168` checked under v7+ relative-splat rules; e2e navigation | no data router (`createBrowserRouter`) | none | — |
| 14915 | `typecheck:e2e` | — | none | — |

---

## Declared deviations from the refinement (for Step 0b to confirm)

- **DV-1 — plan-added W0 login pins and a seventh check point.** TASK-14901 (P-2, with L-9b at the validator) and P-4
  point 7 (`CaseSensitiveClaimsIdentity`). The refinement's W0 has only the goldens (`REF:72-78`) but requires that
  "where today's tests cannot detect such a change, new pins are added before anything moves" (`REF:12`); no existing
  test carries `scopes` or a negative case through the real middleware.
- **DV-2 — `ResponseStrictTypesFilterTests` translated, not unedited** (`REF:201`). See BC-1b and NR-2.
- **DV-3 — G-4 and G-5, plan-added goldens:** the `PlanInputs` the replay re-reads, and the two SHA-256 id
  derivations; neither is named in `REF:39-41`.
- **DV-4 — SSH.NET's explicit reference removed if clean,** instead of the patch (`REF:109`), by the same test the
  refinement applies to the legacy pins.
- **DV-5 — react-router 8, not 7** (`REF:55`, `:120`), by the version rule; the `react-router-dom` package is gone in v8.
- **DV-6 — Mvc.Testing pinned exactly (`10.0.15`)** instead of a floating `10.0.*`.
- **DV-7 — jsdom 30, jest-dom 7 and TypeScript 5.9 as targets** (`REF:55` lists 31, 8, 7): rule 3 (young majors) and the
  tool-support clause; recorded hold-backs with `ignore` rules.
- **DV-8 — Swashbuckle 10.3.1, not 11.0.1** (`REF:53`); `REF:100` ("the newest supporting net8") already says 10.3.1.
- **DV-9 — the 18 Testcontainers builders move to the image constructor** (`REF:108` implies no change).
- **DV-10 — `DbCommandCounter` repaired in W2** (P-5); not in the refinement.
- **DV-11 — W4 isolated on `s149/w4` (Block W4) and merged only after CI-W4 and CI-W3 are green**, so W1–W3's
  evidence bases stay backend-only (`REF:122` leaves it open).
- **DV-12 — the SSH.NET, legacy-pin and BCrypt "small items" (`REF:109`) are split by owner:** the test-csproj ones
  join TASK-14906 (same files, same agent, and Test.Sdk 18 / Testcontainers 4 are what remove their reasons); BCrypt is
  TASK-14907.

---

## Acceptance criteria → verifying test → task

*Kind: R regression pin (an existing behaviour kept) · F fails today. "CI" = Docker-gated or composed-stack, "CI-pending"
until a checkpoint shows it green. **Owner** = the task whose merge closes the AC.*

**Proof that nothing changed (`REF:193-205`)**

| AC | Verified by | Kind | Owner |
|---|---|---|---|
| **AC-G1** the event goldens: every map key, serialise + replay-read literal, coverage | `PersistedBytesGoldenTests` (G-1 facts; names in 14900's brief) | R (local; M-1) | 14900 |
| **AC-G2** the manifest literal and content hash | G-2 facts | R (local; M-2) | 14900 |
| **AC-G3** the hours digests and line forms | G-3 facts | R (local; M-3) | 14900 |
| **AC-G4** the `PlanInputs` literal (DV-3) | G-4 facts | R (local; M-4) | 14900 |
| **AC-G6** the two derived-id literals (G-5; DV-3, NR-1) | G-5 facts | R (local; M-10, M-11) | 14900 |
| **AC-G5** G-1…G-5 green **unedited** after W1, W2, W3 and on K; `Goldens.props` unchanged and still imported; no runtime version named in a frozen file | FROZEN-CHECK (checklist item 3: a whole-file diff of `Goldens.props` and an exact-line match of its `Import`) + TASK-14908's grep over the frozen paths + the facts at CI-W1, -W2, -W3, C-2 | R | the Orchestrator at each checkpoint |
| **AC-P1** PIN-1 2/2, no re-pin | `DemoPayLinesPinTests.World20260115_December2025_EqualsGolden`, `…World20260715_June2026_EqualsGolden`; FROZEN-CHECK | R (CI) | the Orchestrator at each checkpoint |
| **AC-P2** PIN-2 green, no re-pin | `ConfigSeedParityTests.EveryCompiledAgreementOkPosition_DatabaseConfigEqualsCompiled` | R (CI) | ditto |
| **AC-P3** the committed demo seed | `CommittedDemoSeedTests.CommittedSql_EqualsTheGeneratorAtThePinnedDefaults`; FROZEN-CHECK | R (local + CI) | ditto |
| **AC-P4** every suite's count equals S148's close or each difference is explained by name (P-7) | the trx files by name; the vitest summary; the e2e log | — | C-2 |
| **AC-O1** `api-types.ts` empty diff against `df582c20` | P-3 (ii); CI freshness gate | R | 14905 (REGEN); C-2 |
| **AC-O2** the generated document equal after parsing apart from AL-1; schema `required` arrays compared as sets of strings (no coercion), every other array order-sensitive | P-3 (i) exit 0, output in the sprint log | R | 14905 (its pre-return run and REGEN); W3's GATE (on .NET 10); C-2 |
| **AC-O3** `SpecRuntimeMatcherTests` green unedited; `ResponseStrictTypesFilterTests` green, translated one-for-one with the shape column (DV-2, N6) | the two classes; the translation table; M-8 and M-8a … | R | 14905-T |
| **AC-S1** login pins L-1…L-11 green on .NET 8, L-9b's outcomes captured as literals | `TokenPathPinTests`, `PasswordHashPinTests` | R (local; M-5…M-7) | 14901 |
| **AC-S2** the written check covers P-4's seven points | `.claude/reviews/S149-TASK-14903-security-check.md` | — | 14903 |
| **AC-S3** the IdentityModel family at 8.23.0 in `--include-transitive` | `dotnet list package --include-transitive` output in the report | — | 14903 |
| **AC-S4** L-1…L-11 green unedited after W1 and W3 (L-9b's outcomes re-read at W3's security consult for JwtBearer 10); Regression's auth facts, Smoke 10 and e2e logins green | the facts at CI-W1, CI-W3, C-2; the consult artifact | R (CI) | 14903 (W1), the W3 stack |
| **AC-N1** the Npgsql census complete, every hit classified, every (a) fixed | the two reports' census tables | — | 14904-T |
| **AC-N2** the infinity facts still assert in SQL | `ProfileCreateSingleWritePathTests`, `UserAgreementCodeBackfillSeederTests` | R (CI) | 14904-T |
| **AC-N3** the S106 counter counts non-zero and its four bounds hold | `S106SeedScalePerfTests` (the four measured facts, with the added `count > 0`) | R (CI) | 14904-T |

**Versions and platform (`REF:207-215`)**

| AC | Verified by | Owner |
|---|---|---|
| **AC-V1** 16/16 csproj `net10.0`; `global.json` 10.0.x; 7/7 Dockerfiles `sdk:10.0`/`aspnet:10.0`; CI `setup-dotnet` 10.0.x; local and CI on the 10.0.4xx band | the verifying grep (TASK-14908); O-0's `dotnet --list-sdks`; the GATE's `dotnet --version`; CI's setup-dotnet log | the W3 stack |
| **AC-V2** `dotnet list package --outdated` and `npm outdated` empty apart from the recorded hold-backs | both outputs at C-1, in the sprint log | C-1 |
| **AC-V3** `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev` clean, or each finding recorded | both outputs at C-1; CI's vulnerable step | C-1 |
| **AC-V4** build 0 errors; warn-as-error and opt-outs unchanged (F-10 the only possible `Directory.Build.props` line); warnings reported against 126; CA2100 ≤ 115 (a rise needs an owner ruling) | GATE lines; the CI ratchet line (P-8) | every GATE; C-2 |
| **AC-V5** all workflows on the newest action majors; CI on Node 24 | TASK-14902's grep; CI-W0's job logs | 14902 |
| **AC-T1** test tooling at the table's versions; 18/18 builders on the image constructor; the pins removed or each kept with its reason | the csproj diff; the vulnerable check; the census | 14906 |
| **AC-F1** frontend at the table's versions; tsc, lint, typecheck:e2e, build, vitest 122/1405 green on `s149/w4` and after its merge; M-9 red; no `react-router-dom` reference left | the W4 gates; CI-W4; the merge GATE | 14912 … 14915 |
| **AC-F2** e2e green on W0+W4 and on K | CI-W4; C-2 | 14915; C-2 |

**Dependabot and records (`REF:217-223`)**

| AC | Verified by | Owner |
|---|---|---|
| **AC-D1** `dependabot.yml` grouped; every hold-back's `ignore` rule matches the ROADMAP hold-back list | side-by-side check in the sprint log | 14916 |
| **AC-D2** the 34 PRs (#2–#35, all opened 2026-05-24) closed, each with a comment naming the replacing commit; the merged branch deleted (done) | `gh pr list --state open --author app/dependabot` empty of them | C-5 |
| **AC-R1** the docs updated (O-items) | the close commit | C-4 |
| **AC-R2** the superseding ruling recorded and every "S149 = stage 1 / QUAL-284 first" reference re-pointed: `ROADMAP.md:46, 59, 70-72, 313, 316` (HEAD numbering) and `docs/operations/quality-finding-register.md:541` | O-T2 | the plan commit |

---

## CI evidence checkpoints and the close sequence

*Plain language:* Docker cannot run on this machine, so every Docker-gated fact, Smoke and e2e are first seen in CI.
Each wave is run in CI once, on a throwaway branch, before the next wave builds on it. The sprint is then reviewed on a
fixed commit K that has itself run green, and the close commit (documents only, on top of K) is pushed and watched.

**The mechanism (every checkpoint).** `git push origin <sha>:refs/heads/ci/s149-<name>`; open a **draft** PR to master
whose first body line is "EVIDENCE ONLY — S149 checkpoint <name>; never merge"; watch with ONE background command and
its completion notification (never polled; a watcher reaped for low memory is not restarted, the run is read by hand);
confirm `headSha == <sha>`. **Judge from the trx files by name, not by job colour:** `gh run download <run> -n
test-results` and `-n smoke-test-results`; per suite the passed / failed / not-executed counts, every Docker fact
executed, and the named facts of that checkpoint; vitest from the frontend job's summary line; e2e from its job log.
The Claude review workflow also fires on the PR (`claude-code-review.yml:3-5`); it is not one of the seven jobs and is
not evidence. Then close the PR unmerged, delete the remote and local branch, and record the run id; only then do the
facts it showed green flip from "CI-pending" to "CI-verified". Red where green was expected → a fix task through the
merge checklist → repeat.

| Run | Base (holds) | Expected — and read by name |
|---|---|---|
| **CI-W0** | master after 14900, 14901, 14902 | All seven jobs on Node 24 and the new action majors. G-1…G-5 and L-11 (Unit), L-1…L-10 (Regression non-Docker) green; PIN-1, PIN-2, `CommittedDemoSeedTests`; **record** the e2e count, the CA2100 line value and the counts (O-1). **Its green sha is A0** (the frozen-pins anchor and `s149/w4`'s base). **No wave-1 merge before this is green.** |
| **CI-W1** | + the 14903 pair | L-1…L-11 unedited; every Regression auth/host fact executed and green (the Docker host tests mint with the legacy handler and validate on IdentityModel 8.23.0); Smoke 10 (minted with `JsonWebTokenHandler` 8.23.0); e2e logins. |
| **CI-W2** | + 14904 pair, 14905 pair, 14906, 14907 | Every Regression Docker fact executed on Testcontainers 4.17.0 and Npgsql 10.0.4; AC-N2, AC-N3 (non-zero counts); the OpenAPI drift and convention gates; the frontend's `api-types.ts` freshness gate; the vulnerable step clean without the removed pins; goldens, PIN-1, PIN-2. If red with mixed causes, split by task before fixing. |
| **CI-W3** | + the W3 stack | Everything on .NET 10 (setup-dotnet's log shows 10.0.4xx): goldens and login pins unedited; PIN-1, PIN-2; Smoke and e2e on the Ubuntu 24.04 images (ICU 74, tzdata; the only suites that exercise the images, `REF:58`); P-8's CA2100, vulnerable-step and restore-audit reads. |
| **CI-W4** | `s149/w4`'s tip: A0 + 14912…14915 only | The frontend job (tsc, lint, e2e typecheck, build, vitest 122/1405); e2e green against the .NET 8 backend; the other jobs green (backend unchanged). When CI-W4 **and** CI-W3 are green, `s149/w4` merges into master (Block W4); the full GATE and FROZEN-CHECK run there. |

**C-1 — the close candidate.** After W5 (TASK-14916) and the build-dependent O-items: the records written with the
Step-7a outcome and CI ids as marked placeholders; AC-V2 and AC-V3's outputs taken; commit **K** on master, message
`S149 close candidate: <summary>`, **without** the phrase "sprint close" (the guard reads the whole Bash command). The
sprint log's "## Task ledger" names every TASK id with a disposition (reserved fallout ids unused → DROPPED). Not pushed.

**C-2 — the checkpoint on K**, branch `ci/s149-k`: all seven jobs; every pin of P-1…P-6; P-7's counts; P-8's reads.
Locally on K: FROZEN-CHECK, **P-3 (i)** (`--openapi <temp>` + `compare-openapi.mjs`: AL-1 only) and **P-3 (ii)**
(`npm run gen:api` + the `api-types.ts` diff against `df582c20`: empty). Red → fix → K′ → repeat. **Before Step 7a:** `git grep -nE "shortcut\s*\(\s*pending" -- '*.cs' '*.ts' '*.tsx' '*.js' '*.jsx' '*.mjs' '*.cjs' '*.sql' '*.ps1' '*.psm1' '*.py' '*.sh' '*.yml' '*.yaml' ':!docs' ':!tools/check_docs.py'`
prints nothing (`docs/WORKFLOW.md:38`).

**C-3 — Step 7a, both lenses.** Base **`df582c20`** (`codex review --base df582c20`; commits exist, so the default prompt);
`reviewed-against-commit: <K>`. The floor: `reviewer` on Fable 5.2 (`reviewed-by-model: claude-fable-5-2`); if Fable is
at its usage limit, the owner's interim direction (a labelled Opus stand-in plus the narrow
`SPRINT-149-review-floor-WAIVED.md`, which the owner authorizes). Artifacts saved before acting:
`.claude/reviews/SPRINT-149-step7a-{codex,reviewer}.md`. A code-touching fix makes K′ → C-2 again → a verification cycle.

**C-4 — the close.** `S149 sprint close — …`, a documents-only child of K, run as
`cd <root> && git add … && git commit -m "S149 sprint close…"` with no other `cd` or subshell before the commit. **No
commit sits between K and the close commit:** every record after K stays uncommitted and goes into the close commit.
The guard's gates (`docs/WORKFLOW.md:41-47`): both Step-7a artifacts with `verdict:` and a `reviewed-against-commit`
prefixing HEAD, and the floor line (or the narrow waiver); CI-health (the last push run on master, `45815604149` on
`d092595f`, is green); not a second consecutive CI-pending; no untracked files under `src/`, `tests/`, `tools/`,
`frontend/`; **no worktree but the main checkout**; every TASK id with a ledger disposition. Confirm
`git diff --stat K HEAD` lists only documents (`docs/**`, and `ROADMAP.md` if a record lands there). Push; watch CI on the close sha to green.

**C-5 — follow-up.** The `**Test Verified**` line with the close run id; the model-routing register's S149 row (M1–M6,
"log ALLOW N / transcripts M"); **the 34 Dependabot PRs** — now that their replacements are on `origin/master`, each
closed with a comment naming the replacing commit (Dependabot may close some itself after the config lands; those get
the comment too); the handoff block "closed"; push; **watch this eighth run to green**; teardown (worktrees, local and
evidence branches).

---

## The Ponytail trial — what each task records (M1–M6)

*The register's counting rules apply (`docs/operations/model-routing-register.md:83`): a chain is one lens on one scope;
cycle 1 is its first review; a repeated finding counts once; adjudications, Constraint Validator files and security
consults are not reviews; a number the records cannot supply is "not reconstructable", never estimated.*

- **Every task with a 5a row** records in its sprint-log entry: **M1** the cycles of each chain; **M2** each chain's
  cycle-1 B/W/N; **M4** every `(unproven)` NOTE with its evidence requirement and disposition — **at the merge**
  (checklist item 10); **M5** any "Not built" item overruled at merge and any review finding of missing machinery,
  traced (counted only when it traces to the block, with the never-extras column as the reference); **M6** each
  `shortcut(pending)` registered and any check-5 false positive.
- **The sprint** records: M1 for Step 7a per lens; M3 post-close defects and every CI run by id; the spawn
  reconciliation "log ALLOW N / transcripts M", each gap explained.
- **A note for the three-sprint read:** S149 is a refresh, not a feature sprint; its M2/M5 are not comparable cell for
  cell with S148's, and the register row says so.

---

## Orchestrator docs items (O; `docs/**` is Orchestrator-only)

| # | When | Item |
|---|---|---|
| **O-T1** | the plan commit | **The Tier-2 Impact Assessment** (`docs/WORKFLOW.md:167-176`). Drafted in the working tree (`ROADMAP.md`, "Impact Assessment — S149 becomes the dependency refresh"). All four parts (`docs/WORKFLOW.md:169-173`) are now present (NR-6 applied): affected sprints; no split or merge; the § 1 item-4 program's stages each one sprint later; no coverage-tracker change (working-tree `ROADMAP.md:117-140`). Verify at the plan commit. |
| **O-T2** | the plan commit | **The superseding ruling re-pointed** at `ROADMAP.md:46, 59, 70-72, 313, 316` (HEAD numbering) and the QUAL-284 row (`docs/operations/quality-finding-register.md:541`). Drafted in the working tree; verify each line, and that the row now says S150. |
| **O-T3** | the plan commit (text) | **The ROADMAP dependency item** (HEAD `:700-744`): S149 closes **part 3**; parts **1** (inventory), **2** and **4** (central package management) stay. Drafted (+5 lines near `:768`). **Also correct** HEAD `:727-729` ("CI runs no vulnerability scan: no `dotnet list package --vulnerable`"): `ci.yml:107-118` has failed CI on High/Critical NuGet findings since S39 (the SSH.NET override's comment, Regression csproj `:30-35`, records it firing on 2026-08-13). Part 2's remaining pieces are `npm audit`, container scanning and Dependabot alerts (BC-11). |
| **O-0** | before TASK-14908 | **The 10.0.4xx SDK installed side by side (owner ruling NR-3 (b)); `dotnet --list-sdks` recorded showing it** (see the step under the ledger). |
| O-1 | CI-W0 | The goldens' coverage (113 keys), the counts, the **e2e baseline count**, the CA2100 line value, and **A0** (the green sha), in the sprint log; `s149/w4` created from A0. |
| O-2 | after TASK-14903 | The security check saved verbatim and summarised in the sprint log (AC-S2, AC-S3). |
| O-3 | after the 14904 pair | The census counts by class; `docs/knowledge-base/decisions/ADR-040-…md:105` ("which Npgsql 8 would send as `-infinity`") gets a dated note: still true on 10.x. |
| O-4 | after the 14905 pair | REGEN; AC-O1 and AC-O2 outputs in the sprint log (again at W3's GATE and C-2); `PAT-012-…md:62` ("Swashbuckle 6.6.2 emits …") gets a dated note on how 10.3.1 / OpenAPI.NET 2 emit nullable items. |
| O-5 | after W3 | **ADR-045** (free: `docs/knowledge-base/decisions/` ends at ADR-044) — the platform move: .NET 10 LTS (Q2), the version rule, the hold-backs, and the proof standard for a no-behaviour-change refresh (goldens on the old runtime first; one concern per task; runtime last; one evidence run per wave); binding stated in event terms, not sprint numbers (`docs/WORKFLOW.md:183-203`). Its INDEX row (CI-checked by `tools/check_docs.py`). `docs/ARCHITECTURE.md:12-18` and `:173` (".NET 8", "C# 12" → ".NET 10", "C# 14"). `docs/SECURITY.md:275` and FAIL-001 (`:1` title, `:19`, `:28`, `:57`; its INDEX title `docs/knowledge-base/INDEX.md:112`) → "ASP.NET Core 8 and later", plus IdentityModel 8's `CaseSensitiveClaimsIdentity`. The file name stays (links). |
| O-6 | after W4 | `docs/ARCHITECTURE.md:138`, `:179` and `docs/FRONTEND.md:5`, `:7` (React 19; react-router 8 with `react-router` imports); a dated note on ADR-011 `:73` ("React 18"). *(The refinement's docs list misses FRONTEND.md and ADR-011; BC-12.)* |
| O-7 | C-1 | ROADMAP: one backlog line per hold-back (reason, revisit trigger, the `ignore` rule's text), plus xunit v3, PostgreSQL 17/18, ASP.NET Core 10's built-in OpenAPI generator (out of scope), and the dependency item's remaining parts. `docs/QUALITY.md` re-grade (what moved and why). The sprint log and its INDEX row. **Correct the draft log:** `SPRINT-149.md:22` ("Swashbuckle 6 → 11") and `:27` ("React Router 7"). Register rows for any accepted suppression or shortcut, and for xunit's new analyzer warnings (NR-4); F-10's advisory if it fired. |
| O-8 | C-5 | The 34 PRs (AC-D2); the model-routing register row; the handoff closed. |

**Housekeeping at the plan commit:** `.claude/plans/PLAN-s149.md`, `.claude/plans/s149/compare-openapi.mjs` and the
briefs are committed (S144–S148 precedent; `.claude/plans/` is tracked), together with O-T1…O-T3.

---

## Step 0b — what the lenses must verify

*Cycle 1 checked items 1–11 of draft 1 (both reviews answer them). Cycle 2 verifies the absorption: each row of
§ "Step 0b cycle 1 → draft 2" landed where it says, and introduced nothing contradictory. In particular:*
1. **Block W4** against Block A, checklist items 8 and 11, the CI-W4 row and DV-11 (Codex W1).
2. **L-9b** against P-4 point 2, AC-S1/AC-S4 and the W3 consult row (Codex W2).
3. **Mutation provenance** in Block A's KINDS and the mutation list's "Task (tip)" column (Codex W3).
4. **TASK-14903's Unit line** and **TASK-14905's red-Unit note and pre-return P-3 (i)** (Fable W1).
5. **The fallout rows 14917–14919** against the warn-as-error projects (`Directory.Build.props:48-49`, `:102-109`) (Fable W2).
6. **G-5** and the corrected "Why not more" (Fable W3; NR-1).
7. **FROZEN-CHECK**'s three lines against Block A's frozen list (draft 3: `Goldens.props` and its `Import`); P-3 (i) at
   W3's GATE and C-2 (draft 3: schema-only `required` sets); F-10's conditions and owner (draft 3: the Orchestrator);
   the `required`-as-set rule in `compare-openapi.mjs`; the eight-run budget (N1, N2, N5, N7, N8).

---

## Cut order (if the sprint must shrink)

The backend waves are one chain: JwtBearer 10 requires IdentityModel 8 (W1), and W3 is the point of the sprint. The
frontend wave is the most separable (the brief's lean, confirmed: nothing in W0–W3 depends on it, and C-2 is the only
place they meet). Cut from the end of W4:
1. **TASK-14914, react-router 8** — the widest code churn (54 files) and v8's new defaults; stays on react-router-dom
   6.30.x (the `version-6` tag is 6.30.6) with a temporary `ignore`.
2. **TASK-14913, the React 19 family** — only after 1 (react-router 8 needs React ≥ 19.2.7).
3. **TASK-14912 and 14915** — the toolchain and Playwright; W4 is then whole in S150.

Because W4 lives on `s149/w4` until its merge, a cut drops tasks from the branch's tip and nothing on master changes.

**Not cuttable:** W0 (the pins are the proof), W1, W2 (Swashbuckle 6.6.2's behaviour on net10 is unproven, and
cutting Npgsql or the test tools would leave the runtime wave with mixed causes), W3, W5.

---

## Handoff block (the Orchestrator writes it at every gate into `SPRINT-149.md`)

| | |
|---|---|
| **As of** | <date, master sha> |
| **Phase** | <wave / gate / CI-Wn / C-n> |
| **Tasks** | `task · worktree/branch · build · 5α · consult · 5a-R · 5a-C · mutation sha · merged sha` — one row per task, pass and stack |
| **Pins** | A0 (CI-W0's green sha, the frozen-pins anchor); G-1 key count; L-9b's captured outcomes; last green run per pin |
| **SDK** | O-0 done? `dotnet --list-sdks` 10.0.4xx line; the GATE's `dotnet --version` |
| **Pending gates** | <next gate and its open boxes> |
| **CI** | CI-W0 · CI-W1 · CI-W2 · CI-W3 · CI-W4 · C-2 · close · C-5 push <run, headSha, result each> |
| **Worktrees** | <standing worktrees; pruned after wave n?>; `s149/w4`'s worktree and tip |
| **Judgment model** | Fable 5.2 available? <yes / limited since …>; stand-in artifacts so far |
| **Open rulings** | <id · question · who> |
| **Next action** | <one line> |

---

## Brief contradictions (claim · what the code shows · file:line at `d092595f`)

*Claims in the refinement about the existing code or the outside world that the evidence contradicts. I went by the
evidence. None changes a requirement; BC-1b is a criterion that cannot be met as worded and is reported, not bent.*

- **BC-1 (`REF:44`) · "The filter uses `OpenApiSchema.Nullable` and `.Reference` at 364 sites
  (`ResponseStrictTypesFilter.cs:345, 368-374`)"** · the file has 376 lines; `Nullable` is set at `:345` and `:373` and
  `Reference` is used at `:123`, `:176`, `:253`, `:310`, `:335`, `:370`, `:371`, `:374` — ten lines. The 2.x rewrite
  surface is those plus the string `Type` (`:175`, `:349`, `:372`), `Enum` (`:187`), `Required` (`:195`), `Properties`
  (`:141-153`, `:274-275`), `AllOf` (`:268-269`, `:374`) and the two filter interfaces (`:111`): about twenty lines.
- **BC-1b (`REF:201`) · "`ResponseStrictTypesFilterTests` green, with assertions unedited"** · the test asserts on
  Microsoft.OpenApi 1.x members (`using Microsoft.OpenApi.Models`, `:6`; `Reference`, `:74-75`, `:277`, `:281`;
  `Type == "object"`, `:278`; `Nullable`, `:279`) that 2.x removes, so it cannot compile unedited. Reported as DV-2/NR-2;
  the "unchanged" proof moves to the whole-document comparison (AC-O2), which is stronger.
- **BC-2 (`REF:42`) · "No test holds a literal expected string or hash"** · mostly true, but
  `tests/StatsTid.Tests.Unit/Segmentation/SegmentSerializationParityTests.cs:77-91` holds four literal serialized
  `PlannedSegment` strings (under `PeriodCalculationService.JsonOptions` and the EventSerializer's options), asserted at
  `:110`, `:125`, `:139`, `:151`; and `S145ShapeRoundTripTests.cs:80` and `RoleConfigOverrideRetiredMemberTests.cs:40-93`
  hold literal *input* JSON. No literal exists for the manifest hash, the digests, `PlanInputs`, or most event types.
- **BC-3 (`REF:108`) · "Testcontainers 4, whose builders already set the image"** · all 18 fixtures do set it
  (`.WithImage` or an `ImageTag` constant), but all 18 call the parameterless `new PostgreSqlBuilder()` (e.g.
  `tests/StatsTid.Tests.Regression/CalculateAndExportScopeTests.cs:111`), which 4.17.0 marks `[Obsolete]`
  (`src/Testcontainers.PostgreSql/PostgreSqlBuilder.cs:39-42` at tag 4.17.0): 18 new CS0618 warnings unless moved to
  `new PostgreSqlBuilder(image)` (DV-9).
- **BC-4 (`REF:53`) · "Swashbuckle 6.6.2→11.0.1"** · 11.0.x targets net10.0 and net11.0 only, depends on
  Microsoft.OpenApi 3.11.0, and shipped 2026-11-10; the newest supporting net8 is 10.3.1 (Microsoft.OpenApi 2.7.5). The
  draft log repeats it (`SPRINT-149.md:22`).
- **BC-5 (`REF:55`, `:120`) · "react-router-dom 6.30→7.18", "react-router 7"** · the newest major is **react-router
  8.5.3** (8.0.0 on 2026-06-17); v8 removed `react-router-dom`, which is why its "latest" reads 7.18.7. The draft log
  says "React Router 7" (`SPRINT-149.md:27`).
- **BC-6 (`REF:55`) · "jsdom 28→31", "jest-dom 6→8"** · both newest majors are under eight weeks old (jsdom 31.0.0
  2026-11-05; jest-dom 8.0.0 2026-11-19, which also peers `vitest >=5 <6`); rule 3 gives 30.1.3 and 7.0.1 (DV-7).
- **BC-7 (`REF:55`, `:118`) · "typescript 5.9→7.0" / "going to 7 only if … support it"** · they do not, and TypeScript 6
  is not supported either: openapi-typescript 7.13.0 peers `typescript ^5.x`; typescript-eslint 8.74.0 peers
  `>=4.8.4 <6.1.0`. Target stays 5.9.3.
- **BC-8 (`REF:53`) · "IdentityModel.JsonWebTokens 7.5.1→8.23.0", "Microsoft.OpenApi 2.x", "JwtBearer 10's IdentityModel
  floor"** · confirmed: JwtBearer 10.0.15 floors 8.19.2; Swashbuckle 10.3.1 → Microsoft.OpenApi 2.7.5. Not a
  contradiction; recorded because `REF:3` asked for it.
- **BC-9 (`REF:33-35`) · "There are two real dependences on driver defaults"** · there is a third: Npgsql 10 renamed its
  tracing tags (`NpgsqlActivitySource.cs` at v8.0.3 emits `db.connection_string`, `db.statement`; at v10.0.4
  `server.port`, `db.query.text`), and `DbCommandCounter` filters on the old names
  (`S106SeedScalePerfTests.cs:814`, `:823`, `:827`): two facts fail loudly (`:114`, `:192`), two pass vacuously (`:154`, `:303-304`).
  Also, `time` columns (`init.sql:932-941`) change to `TimeOnly`, not only `date`.
- **BC-10 (`REF:29`, `:31`) · "`StatsTid.Auth` references JwtBearer 8.0.8" / "only host-level tests exercise the real
  path"** · JwtBearer 8.0.8 is also referenced directly by `tests/StatsTid.Tests.Unit/StatsTid.Tests.Unit.csproj:15`
  (it must move in W1 and W3 too); and two **non-Docker** host-level classes already exercise the real path locally
  (`ExternalSendAuthorizationAndEnvelopeTests.cs:57-60`, `RuleClassificationsLeastPrivilegeAcceptTests.cs:43`), though
  neither carries `scopes`.
- **BC-11 (`REF:141`, `:143`) · "`ROADMAP.md` ~:715-741"; part 2 "a CI gate on vulnerable packages"** · the item starts
  at HEAD `:700` (its part list `:725-741`); its sentence "CI runs no vulnerability scan: no `dotnet list package
  --vulnerable`" (HEAD `:727-729`) is false: `ci.yml:107-118` runs it and fails on High/Critical. Part 2's NuGet gate
  exists; `npm audit`, container scanning and Dependabot alerts do not (O-T3).
- **BC-12 (`REF:129-134`) · the docs list** · misses `docs/FRONTEND.md:5` ("React 18"), `:7` ("react-router-dom v6"),
  ADR-011 `:73` ("React 18"), ADR-040 `:105` ("Npgsql 8") and PAT-012 `:62` ("Swashbuckle 6.6.2"). Added as O-3, O-4,
  O-6.
- **BC-13 (`REF:53`) · "BCrypt 4.1.0→4.2.0"** · confirmed; a 4.2.1 exists but is unlisted on nuget.org.
- **Minor citation drift, no effect:** `CopenhagenBusinessDate.cs:212-240` (`REF:236`) — the resolver is `:205-234`, the
  fix advice `:236-241`.

---

## Rulings on draft 1's questions (all six ruled; kept for the record)

- **NR-1 — How wide the event goldens go.** *(a)* every key of `EventTypeMap` (113) plus a coverage fact that fails when
  a new type is registered without a golden (every future event adds one golden line; the next runtime move inherits the
  proof); *(b)* only types with an emit site today; *(c)* (a) without the coverage fact. **RULED (Orchestrator,
  2026-11-27, before Step 0b): (a), as drafted.** *Second half — the two SHA-256 id derivations
  (`DefaultEntitlementConfigs.cs:55-56`, `MidnightCrossingNormalizer.cs:201-203`):* draft 1 leaned "no" on the ground
  that a stability fact already covered them; Fable's Step 0b W3 showed that ground false (the fact compares two calls in
  one process; G-1 never runs the derivation). **RE-RULED after Step 0b cycle 1 (Orchestrator, 2026-11-27): pin both —
  G-5, two literals captured on .NET 8.**
- **NR-2 — DV-2, the `ResponseStrictTypesFilterTests` criterion.** *(a)* translate one-for-one, with the whole-document
  comparison carrying "unchanged"; *(b)* a frozen Microsoft.OpenApi 1.x copy (impossible); *(c)* JSON-level probe pins
  in W0 (duplicates AC-O2). A wording change to a criterion, not its intent, so the Orchestrator's
  (`docs/AGENTS.md:256-260`). **RULED (Orchestrator, 2026-11-27, before Step 0b): (a), as drafted**; sharpened after
  cycle 1 by N6's shape column and its extra mutations.
- **NR-3 — The local SDK band vs CI's.** Locally 10.0.302 (band 3xx); CI's `10.0.x` installs 10.0.404 (band 4xx);
  analyzers and the compiler can differ between bands. *(a)* accept the skew; *(b)* the refinement's settings plus the
  10.0.4xx SDK installed on the owner's machine before W3; *(c)* pin exactly via `global-json-file`. **RULED by the
  owner (2026-11-27, AskUserQuestion): (b) "Install it"** — O-0; `global.json` stays `10.0.100` + `latestFeature`, CI
  stays `10.0.x`.
- **NR-4 — xunit 2.9.3's analyzer warnings** (xunit.analyzers 1.18.0, in non-warn-as-error test projects). *(a)* report
  by rule and count, register one QUAL row, fix later; *(b)* fix them in TASK-14906 (assertion rewrites, colliding with
  the frozen pins). **RULED (Orchestrator, 2026-11-27, before Step 0b): (a), as drafted.**
- **NR-5 — "supported" in rule 3.** *(a)* hold every young major, revisit at eight weeks; *(b)* hold only where the
  older line is demonstrably maintained (adopting jsdom 31 and jest-dom 8 now). Swashbuckle 11 is held either way (it
  could only move in W3, which must stay the runtime alone). **RULED (Orchestrator, 2026-11-27, before Step 0b): (a), as
  drafted.**
- **NR-6 — O-T1's three missing parts.** *(a)* add a line each; *(b)* leave it. **RULED (Orchestrator, 2026-11-27,
  before Step 0b): (a), as drafted**, applied to the ROADMAP working tree the same day (O-T1).

No new question arises from cycle 1: every finding was ruled by the Orchestrator, and NR-3 by the owner.

---

## Step 0b cycle 1 → draft 2

*Reviews: `.claude/reviews/S149-0b-codex-c1.raw.md` (Codex: 0 B / 3 W, READY-WITH-WARNINGS) and
`.claude/reviews/S149-0b-reviewer-c1.md` (Fable 5.2: 0 B / 3 W / 8 N, READY-WITH-WARNINGS). Every finding was ruled by
the Orchestrator; none is disputed.*

| Finding | Ruling | Where it landed |
|---|---|---|
| **Codex W1** — W4's isolation conflicts with Block A's fast-forward, checklist item 8 and item 11 | Exempt W4; stack 14912–14915 on a fixed W0-based branch with its own gates and one kept worktree; merge only after CI-W4 and CI-W3 are green; one pasted instruction block | Block W4 (new); "How the waves are built" items 4 and 6; checklist items 8 and 11; ledger 14912; CI-W4 row; DV-11; cut order note |
| **Codex W2** — L-9 tests issuance, not the validator | Keep L-9 (now L-9a); add independently built 31- and 32-byte HS256 tokens through `AddStatsTidJwtAuth`, outcomes captured on .NET 8 and frozen; re-read at W3's consult | P-2 L-9b; P-4 point 2; ledger 14901; per-task notes; consult row for the W3 stack; AC-S1, AC-S4 |
| **Codex W3** — mutation worktrees start from master, before the tests exist | Branch from the reviewed task tip holding the tests and fixtures (a paired pass: the pair's tip); record the sha | Block A KINDS; the mutation list's preamble and "Task (tip)" column; checklist item 8's row; handoff block |
| **Codex item 10 / Fable N8** — the budget omits C-5's push | Eight runs | Header CI budget; summary; C-5 ("this eighth run"); handoff block |
| **Fable W1** — 14903 and 14905 hand back branches that cannot build | Unit csproj `:15` moves to TASK-14903 (NU1605, as 14904's Npgsql); 14903-T keeps the Smoke line; 14905's brief: Unit red until -T, build `src` only, run P-3 (i) before returning | Ledger 14903, 14903-T, 14905; shared-files Unit row; P-3 "Where they run"; per-task notes |
| **Fable W2** — no fallout owner for SharedKernel, RuleEngine, Auth | Reserved rows `rule-engine`, `data-model`, `security` (cross-domain); DROPPED if unused | Ledger 14917–14919; Size; waves item 7; consult row |
| **Fable W3** — the stated reason for not pinning the id derivations is false | Pin both (G-5); correct "Why not more"; keep the true point (`SourceStintId = EventId` in production, so the derivation is a fallback) | P-1 table, "Why not more", G-5; M-10, M-11; AC-G6; NR-1; DV-3 |
| **Fable N1** — the contract is compared only on .NET 8 | P-3 (i) at W3's GATE; both P-3 commands named at C-2 | P-3 "Where they run"; GATE; C-2; AC-O1, AC-O2 |
| **Fable N2** — the "unedited" diff misses frozen files | One FROZEN-CHECK over every frozen path; base `df582c20` for the older pins, A0 = CI-W0's green sha for the W0 pins; `GoldenLegacyPinTests` in Block A | Checklist item 3; Block A FROZEN PINS; waves item 3; P-6; AC-G5, AC-P1, AC-P3; GATE; O-1 |
| **Fable N3** — only Npgsql 10.0's notes cited | 14904 classifies both 9.0's and 10.0's breaking changes; both pages cited | P-5 "Both majors' breaking changes" (pre-classified); ledger 14904; per-task notes |
| **Fable N4** — 14914 counts imports, not references | Name the four mock strings as allowed harness edits; count references | Ledger 14914 (60 references at `d092595f`: 54 imports, 4 mock lines, 2 comments); per-task notes; AC-F1 |
| **Fable N5** — SDK 10's restore audit may fail restore on Low/Moderate | Pre-ruled: `WarningsNotAsErrors` NU1901;NU1902 in `Directory.Build.props`, only if it fires, recorded; owned by the W3 flip | P-8; F-10; ledger 14908's label; checklist item 2; fallout rule; shared files; AC-V4 |
| **Fable N6** — translated filter facts may test shapes the real document lacks | Translation table marks shape-in-document; each absent shape gets its own mutation | P-3 spec tests; mutation list M-8a …; ledger 14905-T; AC-O3 |
| **Fable N7** — array order in the comparison | `required` arrays compared as sets; every other array order-sensitive | P-3 (i); `compare-openapi.mjs` (re-sanity-checked five ways, read-only) |
| **NR-3 (owner)** | (b) "Install it" | O-0 (under the ledger and in the O-items); version table SDK row; F-1; AC-V1; header rulings; NR-3 |

## Step 0b cycle 2 → draft 3

*Reviews: `.claude/reviews/S149-0b-codex-c2.raw.md` (Codex: its three cycle-1 warnings CLOSED; 1 W, 1 NOTE
(unproven)) and `.claude/reviews/S149-0b-reviewer-c2.md` (Fable 5.2: its eleven cycle-1 findings CLOSED; 1 W, 3 N).
Both READY-WITH-WARNINGS; every finding ruled by the Orchestrator; none disputed.*

| Finding | Ruling | Where it landed |
|---|---|---|
| **Codex W** — the set comparison coerces members to strings (`"label"` → `["label"]` passed) and reorders example arrays named `required` | Set semantics only for the schema `required` keyword, by position; string members, no coercion; non-string, added, removed and duplicate members each reported; example payloads order-sensitive | `compare-openapi.mjs` (rewritten walk); P-3 (i) array rule and sanity list (seven checks plus a control, all as expected); AC-O2 |
| **Codex NOTE (unproven)** — the `grep "Goldens"` diff check depends on layout | The copy items live in a dedicated `Goldens.props`, imported by the Unit csproj; FROZEN-CHECK diffs the whole file and checks the `Import` | P-1 "Where"; FROZEN-CHECK; Block A FROZEN PINS; ledger 14900; shared files; AC-G5; M-12 |
| **Fable W1** — the runtime wave's `\.NET 8\|net8\.0` grep would hit frozen files | Frozen files name no runtime version ("the pre-refresh runtime", ADR-045); each W0 task's 5a runs the grep over them before A0; the plan's wording that invited the version is fixed | P-1 (new paragraph); L-9a, L-9b; Block A KINDS; ledger 14900, 14901; AC-G5 |
| **Fable N1** — F-10 is a judgment inside a Haiku sweep | The sweep reports NU1901/NU1902 under "Needs a decision"; the Orchestrator applies F-10 under the Small Tasks Exception and records it | P-8; F-10 row; Block S; ledger 14908's label; shared files; per-task notes (W3) |
| **Fable N2** — M-9 in a detached worktree has no `node_modules` | M-9 uses Block A's patch method inside the W4 worktree | Mutation list preamble and M-9 row; Block A KINDS |
| **Fable N3** — growth proportionate | No action | — |
| **Fable cycle-2 table** — the version table's head cites only Npgsql 10.0 | Add 9.0 | Version table head |

## Step 0b cycle 3 → draft 3.1 (Orchestrator)

*Reviews: `.claude/reviews/S149-0b-codex-c3.raw.md` (Codex: its cycle-2 WARNING CLOSED, tested with `"label"` → `["label"]`, → `42` and `required` → a string; 1 W, 1 NOTE (unproven)) and `.claude/reviews/S149-0b-reviewer-c3.md` (Fable 5.2: its four cycle-2 items CLOSED; the helper run against twelve variants, all as expected; 1 W). Both READY-WITH-WARNINGS. The edits are small and mechanical, so the Orchestrator made them (draft 3.1).*

| Finding | Ruling | Where it landed |
|---|---|---|
| **Codex W / Fable W1** (the same defect) — M-12 cannot go red: its mutation is uncommitted while FROZEN-CHECK line 2 compares two commits, and before A0 the line does not apply | **Drop M-12** (Fable's option (a)). A whole-file diff of a committed file is complete by construction | Mutation list (M-12 struck through); FROZEN-CHECK note; TASK-14900 row; AC-G5 |
| **Codex NOTE (unproven)** — the `Import` check is a substring match, so `<Import Project="Goldens.props" Condition="false" />` would pass | FROZEN-CHECK line 3 becomes an exact-line match (no attribute besides `Project`; anchored pattern since draft 3.2); TASK-14900 writes the import exactly. A disabled import also fails loudly, since the golden facts lose their data files | FROZEN-CHECK line 3 and its note; TASK-14900 row |

## Step 0b cycle 4 → draft 3.2 (Orchestrator)

*Reviews: `.claude/reviews/S149-0b-codex-c4.raw.md` (Codex: both cycle-3 findings CLOSED, no new finding; READY) and `.claude/reviews/S149-0b-reviewer-c4.md` (Fable 5.2: its cycle-3 WARNING CLOSED; 1 new W; READY-WITH-WARNINGS).*

| Finding | Ruling | Where it landed |
|---|---|---|
| **Fable W1** — FROZEN-CHECK line 3 uses `git grep -x`, which does not exist (`error: unknown switch 'x'`, exit 129, git 2.55.0.windows.3); the draft-3.1 test used plain `grep` | Anchor the pattern: `git grep -nE '^\s*<Import Project="Goldens\.props" */>\s*$'`. The Orchestrator re-tested it on this machine's git in a throwaway repository: it prints only the plain import, matches neither a commented-out copy nor `Condition="false"`, and still matches a CRLF file (the `\s*$` absorbs the carriage return; the Unit csproj is LF in the index) | FROZEN-CHECK line 3 and item 3's expectation; the cycle-3 mapping row |

---

## Revision history

| Version | Date | By | Change |
|---|---|---|---|
| Draft 1 | 2026-11-27 | `planner` (Opus 5.5) | First draft from refinement r2 (READY). Versions re-verified against nuget.org, npm, GitHub releases, the Node schedule and .NET release metadata. Twelve declared deviations (DV-1 … DV-12), thirteen brief contradictions (BC-1 … BC-13), six questions (NR-1 … NR-6). `compare-openapi.mjs` drafted and sanity-checked both ways. |
| *(annotation)* | 2026-11-27 | Orchestrator | NR-1, NR-2, NR-4, NR-5 and NR-6 ruled (a) as drafted, before Step 0b; NR-6 applied to the ROADMAP working tree. |
| Draft 2 | 2026-11-27 | `planner` (Opus 5.5) | Step 0b cycle 1 absorbed: Codex 3 W, Fable 3 W / 8 N, all ruled (table above). Owner ruling NR-3 (b): O-0 installs the 10.0.4xx SDK. New: Block W4, L-9b, G-5, FROZEN-CHECK, F-10, reserved fallout rows 14917–14919, mutation provenance, P-3 at W3 and C-2, `required`-as-set in the helper. Budget eight runs. Twenty dispatch ids. |
| **FINAL** | 2026-11-27 | Orchestrator | Step 0b cycle 5: both lenses READY (`.claude/reviews/S149-0b-codex-c5.raw.md`, `.claude/reviews/S149-0b-reviewer-c5.md`). Header and Status row updated (Fable cycle-5 NOTE). The plan is final at draft 3.2. |
| Draft 3.2 | 2026-11-27 | Orchestrator | Step 0b cycle 4 absorbed (§ above): FROZEN-CHECK line 3 anchored, since `git grep` has no `-x`. |
| Draft 3.1 | 2026-11-27 | Orchestrator | Step 0b cycle 3 absorbed (§ above): M-12 dropped; the `Goldens.props` import checked by an exact-line match. |
| Draft 3 | 2026-11-27 | `planner` (Opus 5.5) | Step 0b cycle 2 absorbed: Codex 1 W / 1 N, Fable 1 W / 3 N, all ruled (§ above). Schema-only, string-strict `required` sets in the helper (seven sanity checks); `Goldens.props` and M-12; frozen files name no runtime version; F-10 moves to the Orchestrator; M-9 by the patch method; Npgsql 9.0 in the version table's head. |
