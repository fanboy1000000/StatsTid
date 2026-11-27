# S149 plan, draft 1 — the planner's report

*2026-11-27, `planner` (Opus 5.5). The plan: `.claude/plans/PLAN-s149.md` (draft 1). Its helper:
`.claude/plans/s149/compare-openapi.mjs`. Nothing outside `.claude/plans/**` was touched; nothing was committed.*

## In plain words

The refinement's shape holds: pins on .NET 8 first, then IdentityModel alone, then the other libraries one by one,
then the runtime last, the frontend beside it, and Dependabot with `ignore` rules at the end. Re-checking every target
against the registries today changed four targets (React Router 8, not 7; Swashbuckle 10.3.1, not 11; jsdom 30 and
jest-dom 7, not 31 and 8; TypeScript stays 5.9) and found one hidden breakage the refinement did not know about: the
database-driver upgrade silently turns a performance test's query counter into a counter of zero, so two of its four
checks would pass without measuring anything. The plan repairs it in the same wave.

## What I verified, and how

- **Repo facts at `d092595f`** by opening each file: the 16 csproj and their package lines, `global.json`,
  `Directory.Build.props`, the 7 Dockerfiles, the three workflows and `dependabot.yml`, the 18 Testcontainers fixtures,
  the auth files, the four serialized surfaces, the OpenAPI filter and its tests, the Npgsql date sites, the S106 query
  counter, the frontend's `package.json`, lockfile versions, ESLint config and router usage, the ROADMAP lines at HEAD
  (`git show d092595f:ROADMAP.md`) and the working-tree Tier-2 drafts (`git diff`).
- **NuGet** — flat-container indexes (newest per major), nuspecs (target frameworks, dependency groups), registration
  API (publish dates; `1900-01-01` = unlisted). **npm** — `npm view` dist-tags, `peerDependencies`, `engines`, `time`.
  **Actions** — `git ls-remote --tags` and the releases API (first release per major). **Node** — `nodejs/Release`
  `schedule.json`, `nodejs.org/dist/index.json`. **.NET** — the 8.0/10.0/11.0 `releases.json`; `dotnet --list-sdks`.
  **Images** — `dotnet-docker` `manifest.json` and the runtime-deps 10.0 noble Dockerfile.
- **Release notes read:** Npgsql 10.0 (`date`/`time` → `DateOnly`/`TimeOnly`; tracing tags renamed), Npgsql source at
  v8.0.3 and v10.0.4 (`NpgsqlActivitySource.cs`; `PgSerializerOptions.cs:57` infinity still on), Swashbuckle 10.0.0
  and 11.0.0, IdentityModel 8.0.0 (`CaseSensitiveClaimsIdentity`), the xunit VS adapter 4.0.0, coverlet's changelog,
  react-router's v8.0.0 entry, Testcontainers' `PostgreSqlBuilder.cs` at 4.17.0, the action majors.
- **The brief's named questions, settled:** JwtBearer 10.0.15 floors IdentityModel **8.19.2**; Npgsql 10.0.4 supports
  **net8** and defaults `date` to **`DateOnly`**; the newest Swashbuckle on net8 is **10.3.1**, on Microsoft.OpenApi
  **2.7.5**; Testcontainers 4.17.0 targets **net8/9/10** (+ netstandard2.0/2.1); xunit.runner.visualstudio **4.0.1**
  runs v1/v2/v3 tests (its nuspec); coverlet 10 needs the **.NET 8 SDK/runtime**; Test.Sdk 18.12.0 has a **net8.0**
  group; Vite 8 + plugin-react 6 + vitest 5 are **peer-compatible** (vitest 5 needs Node ≥ 22.12 and `@types/node`
  ≥ 22); typescript-eslint peers `typescript <6.1.0` and openapi-typescript `^5.x`, so **no TypeScript 6 or 7**; ESLint
  10 works with the existing **flat config**; jest-dom 8 peers **vitest 5 only** and is young; React 19 is accepted by
  every **Radix** package and **@testing-library/react 16.3.3**; react-router's newest is **8.5.3** (React ≥ 19.2.7,
  Node ≥ 22.22, `react-router-dom` removed); the action majors are checkout **v7**, setup-dotnet **v6**, setup-node
  **v7**, setup-python **v7**, upload-artifact **v7**, claude-code-action **v1**; **Node 26 is Active LTS** since
  2026-10-28 (to 2029-04-30).
- **The comparison script** was run read-only twice: `df582c20`'s spec against the working one (equal; 116 paths, 227
  schemas) and against a copy with one `nullable: true` deleted (exit 1, the pointer named).

## Hold-backs (each with its `ignore` rule text in the plan's hold-back table)

| Hold-back | Why |
|---|---|
| .NET 11 and `Microsoft.AspNetCore.*` / `Microsoft.Extensions.*` 11; `dotnet/sdk` and `dotnet/aspnet` 11 images | Owner Q2 = .NET 10 |
| Npgsql 11 | Young major (2026-11-20), net10-only; 10.0.4 shipped the same day |
| Swashbuckle 11 | Young major (2026-11-10), net10/net11 only, Microsoft.OpenApi 3.x (a second filter rewrite) |
| jsdom 31 | Young major (2026-11-05) |
| @testing-library/jest-dom 8 | Young major (2026-11-19) |
| TypeScript 6 and 7 | openapi-typescript peers `^5.x`; typescript-eslint peers `<6.1.0` |
| @types/node 25+ and Node 26 | Local parity with Node 24 (no Dependabot rule possible for `node-version`) |
| PostgreSQL 17/18 | Out of scope; not scanned by Dependabot (no compose ecosystem) |

## Brief contradictions found (detail and file:line in the plan's section)

1. **BC-1** — the filter's "364 sites" are ten lines (`Nullable` ×2, `Reference` ×8); the rewrite surface is ~20 lines.
2. **BC-1b** — "`ResponseStrictTypesFilterTests` … assertions unedited" cannot hold: the test asserts on members
   Microsoft.OpenApi 2.x removes. Reported, not bent (DV-2 / NR-2).
3. **BC-2** — "no test holds a literal": `SegmentSerializationParityTests.cs:77-91` holds four (PlannedSegment).
4. **BC-3** — Testcontainers 4.17.0 marks the parameterless `PostgreSqlBuilder()` obsolete; all 18 fixtures use it.
5. **BC-4** — Swashbuckle's target is 10.3.1, not 11.0.1 (also in the draft `SPRINT-149.md:22`).
6. **BC-5** — React Router's newest major is 8 (v8 removed `react-router-dom`); also `SPRINT-149.md:27`.
7. **BC-6** — jsdom 31 and jest-dom 8 are young majors → 30.1.3 and 7.0.1.
8. **BC-7** — TypeScript 6 is unsupported too (openapi-typescript `^5.x`).
9. **BC-9** — a third Npgsql exposure: renamed tracing tags make `DbCommandCounter` count zero; two facts would pass
   vacuously (`S106SeedScalePerfTests.cs:154`, `:303-304`). Also `time` → `TimeOnly`, not only `date`.
10. **BC-10** — JwtBearer 8.0.8 is also referenced by the Unit test csproj (`:15`); two non-Docker host tests already
    run the real middleware locally.
11. **BC-11** — the ROADMAP dependency item (HEAD `:700`, not ~715) says "CI runs no vulnerability scan"; `ci.yml:107-118`
    has gated on High/Critical NuGet findings since S39.
12. **BC-12** — the docs list misses `docs/FRONTEND.md:5`, `:7`, ADR-011 `:73`, ADR-040 `:105`, PAT-012 `:62`.
13. **BC-8, BC-13** — confirmations recorded (JwtBearer 10's floor; Microsoft.OpenApi 2.x; BCrypt 4.2.0 with 4.2.1
    unlisted). Minor drift: `CopenhagenBusinessDate.cs:212-240` is `:205-241`.

Also found in the Orchestrator's working-tree drafts: the Tier-2 Impact Assessment (working-tree `ROADMAP.md:117-138`)
names the affected sprints but has no line for splits/merges, phase-sprint ranges or coverage-tracker projections,
which `docs/WORKFLOW.md:169-173` asks for (O-T1, NR-6).

## Needs a ruling (each drafted in the plan as its lean)

- **NR-1** — event goldens for all 113 registered types plus a permanent coverage fact. *Lean: yes.* (And no separate
  goldens for the two SHA-256 id derivations.)
- **NR-2** — accept translating `ResponseStrictTypesFilterTests` one-for-one, with the whole-document comparison as the
  unedited proof. *Lean: yes.*
- **NR-3 (owner action)** — install the .NET 10.0.4xx SDK on the owner's machine before W3, so local and CI build with
  the same SDK band (local is 10.0.302; CI's `10.0.x` gets 10.0.404). *Lean: yes; the owner decides, since it changes
  their machine.*
- **NR-4** — xunit 2.9.3's new analyzer warnings in test projects: report and register, do not fix in S149. *Lean: yes.*
- **NR-5** — read rule 3's "supported older major" as "hold every young major" (jsdom 31, jest-dom 8, Swashbuckle 11,
  Npgsql 11), revisiting at eight weeks. *Lean: yes.*
- **NR-6** — add the three missing parts to the Tier-2 Impact Assessment. *Lean: yes.*

## Declared deviations the plan asks Step 0b to confirm

DV-1 login pins and a seventh security check point (`CaseSensitiveClaimsIdentity`) · DV-2 the filter-test
translation · DV-3 a `PlanInputs` golden · DV-4 SSH.NET's override removed if clean · DV-5 react-router 8 · DV-6
Mvc.Testing pinned exactly · DV-7 jsdom 30 / jest-dom 7 / TypeScript 5.9 · DV-8 Swashbuckle 10.3.1 · DV-9 the 18
builders moved to the image constructor · DV-10 the query-counter repair · DV-11 W4 merges after CI-W3 · DV-12 the
"small items" split by owner.
