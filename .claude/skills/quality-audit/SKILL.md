---
name: quality-audit
description: Static-analysis code-quality audit method (8-dimension taxonomy + Medium+ severity rubric) for the StatsTid S131 quality sweep. INVOKE BY NAME ONLY — this is the sweep persona/method injected into read-only sweep agents; it does not auto-trigger, registers no hooks, and never modifies code.
---

# Quality Audit — method for the StatsTid code-quality sweep (S131 / WS7)

> **Provenance & vetting (S131, 2026-08-19).** Authored in-repo for S131 TASK-A as the structural
> sibling of `.claude/skills/threat-model-audit/` (S129). Not adapted from an external source — the
> security sweep got its taxonomy free (STRIDE/OWASP); quality does not, so **this skill IS the
> taxonomy**. Dual-lens reviewed (Codex + internal Reviewer) before any sweep agent runs, per the
> S131 refinement (rev 2.1). The sweep design itself (baseline pin, one agent per dimension,
> withheld calibration, refute panel, Medium+ floor, read-only) is owner-ruled and is stated here
> as given — this file defines the METHOD inside that design, it does not re-open it.

## What this is
The per-dimension audit method for the S131 code-quality sweep: what each of the 8 dimensions
measures, over which universe, exhaustively or sampled (always declared), with what evidence bar,
plus the severity rubric, the dedupe keys, and the output contract. It is the persona the
Orchestrator injects into each fresh-context, read-only, capability-restricted sweep agent.

**Plain-language summary (the PM view).** A quality audit only earns trust if three things hold:
(1) every claim points at a specific line of code or build output — no vibes; (2) coverage is
honest — "we looked at everything" vs "we looked at a declared sample" is always stated, never
implied; (3) severity means one thing for everyone — the rubric below is the single scale, so two
agents can't call the same defect Medium and Critical. This file exists to make those three things
mechanical.

## Hard constraints (StatsTid governance — bind every sweep agent)
- **Read-only / static analysis ONLY.** No live requests, no `dotnet`/`npm` execution, no
  `psql`/`docker`, no code modification. Enforced by the agent tool profile AND by running in a
  clean worktree pinned to the baseline SHA `7e4bb1b` — not merely by this instruction.
- **Agents never write files.** Findings are RETURNED to the Orchestrator as structured rows (see
  the output contract). Only the Orchestrator writes the QUAL register, QUALITY.md, and the sprint
  log, and only the Orchestrator assigns QUAL-### ids (S129 parity).
- **No remediation.** Every fix impulse becomes a finding row with a proposed disposition; the
  actual fixing is a separate proposed sprint (S132 candidate). Gate changes (CI ratchets,
  hard-fails) are PROPOSALS-ONLY findings — the audit changes no CI behavior (owner ruling OQ-3).
- **Invoke by name only.** No auto-trigger, no hooks.
- **Severity is engineering priority, never incident risk.** StatsTid is a learning project — no
  finding is described in launch-blocker / live-exposure / incident language.

## The view rule (leak prevention — every sweep prompt inherits this)
Every SCORED dimension's worktree view is **pinned at `7e4bb1b`** and **EXCLUDES**:
- `docs/sprints/**`
- `docs/operations/security-finding-register.md`
- `docs/operations/performance-finding-register.md`
- `docs/operations/quality-finding-register.md`
- `docs/operations/s64-regression-debt-census.md` (a debt census is ledger-class content)
- `docs/operations/docs-governance-program.md` (workstream status ledger)
- `docs/QUALITY.md` (the graded quality ledger — answer-bearing by design)

Agents get code + current-truth docs, never the ledger. Rationale: the calibration manifest
(TASK-B) is harvested partly from sprint-recorded debt — an agent with ledger access could "find"
a withheld item by reading about it, a hit that measures doc-reading, not code analysis. The
exclusion is enforced by **worktree construction**, not instruction. Baseline-pinning additionally
auto-excludes all S131 planning artifacts (they postdate `7e4bb1b`). Belt-and-braces: at TASK-B
sealing, each manifest item is additionally checked against the IN-VIEW doc set — **"no in-view
doc records this item"** — so a leak cannot survive even an exclusion-list gap.

Two consequences agents must internalize:
1. **You cannot and must not dedupe against the SEC/F/QUAL registers — you can't see them.**
   Report everything you find; the Orchestrator and the refute panel (TASK-D) apply the dedupe
   rule (already tracked in SEC / F / ROADMAP → cross-reference, never a QUAL row).
2. **Dimension 7 (doc/code drift) is an isolated slice** with its own further-reduced view
   (enumerated below) and **no withheld-calibration scoring** — its excluded-path set is a strict
   superset of the scored-view exclusions.

## Severity rubric (the single scale — owner-ratified floor)

**Registration floor = Medium+** (owner ruling OQ-4). The Medium definition is owner-ratified and
verbatim: a finding registers only if it *"would plausibly change behavior, block or mislead a
future change, or misinform a reader."* Every Medium+ finding MUST name which of those three
clauses it satisfies (the `floor-clause` field) — a finding that can't name one is Low.

**Low — below the floor (inventory appendix only; not refuted, not adjudicated).**
Real but fails all three Medium clauses: cosmetic, stylistic, or confined to non-load-bearing
surfaces. Calibrating examples:
- A naming inconsistency (`GetEmployeeById` vs `FetchUserById` in sibling services).
- A CCN-16 function in demo-seed or mock tooling.
- Log message casing/punctuation inconsistency; an unused private helper in a test-support file.
- A "suspected dead code" claim that could not meet the conservative evidence bar (see dim. 4).

**Medium — meets the floor.**
Would plausibly change behavior, block/mislead a future change, or misinform a reader — on a
normal (non-invariant-adjacent) surface, or as a single instance rather than a systemic family.
Calibrating examples:
- An inconsistent error body shape on one endpoint vs the family's documented pattern (misleads
  the next client integration).
- A stale comment asserting behavior the adjacent code no longer has (misinforms a reader).
- A Docker-gated test whose gating silently masks a locally-untested path (misleads "tests pass"
  into meaning more than it does).
- A CCN-25 function on a load-bearing path whose branches are not pinned by tests (blocks a safe
  future change).

**High — the defect weakens a load-bearing guarantee.**
The Medium clauses hold AND the surface is invariant-adjacent (payroll boundary, rule engine /
OK-version transitions, authorization/scope, outbox/event delivery, audit trail) or the defect is
systemic (a family, not an instance). Calibrating examples:
- A vacuous test on a payroll-boundary path (asserts nothing that could fail) — High even where
  other tests still partially cover the behavior; the net is weakened where we most rely on it.
- A missing or vacuous negative TEST case that is the SOLE guard of an invariant-adjacent
  behavior — **High, never Critical**: a test gap weakens verification but is not a product
  fail-open (the ruled Critical/High boundary, below).
- A swallowed exception that converts a delivery failure into silent success on an outbox path.
- A diverged copy-paste family in rule-engine logic where one copy carries a fix the others lack.

**Critical — a stated guarantee is not actually held.**
Reserved for "the safety net has a hole exactly where we claim it's strongest." Calibrating
examples:
- A fail-open or guard-bypass in PRODUCT code (a failure branch that defaults to permit; a guard
  that can be walked around).
- An actual bounded-context dependency violation present in the tree (e.g., a service holding a
  project reference to a context it must reach only over HTTP).
- A runbook/doc instruction that, followed as written, would corrupt data or bypass the audit
  trail.

**The ruled Critical/High boundary (decisive — no judgment calls):** a fail-open or guard-bypass
in PRODUCT code = **Critical**; a missing or vacuous negative TEST case = **High**, even when it
is the sole guard. A test gap is not a product fail-open.

**The promotion rule (general — D2's tier mapping is an instance of it):** invariant-adjacency
promotes Medium→High only when the defect CLASS itself degrades the guarantee — correctness,
verification, delivery, or fail-open classes. Pure changeability-risk classes (complexity,
duplication-without-divergence) stay Medium on invariant-adjacent surfaces unless compounded with
a guarantee-degrading defect.

**Confidence is orthogonal to severity** and required on every finding (S129 parity):
**Confirmed** — the defect is directly evidenced in code/build output, no interpretation needed.
**Likely** — evidenced, but an unverifiable factor (config, runtime wiring) could alter it.
**Possible** — inference-dependent; by default Possible findings sit below the floor unless the
floor-clause argument survives without the inference.

---

# The eight dimensions

Each spec below gives: what it measures (PM-legible) · universe · coverage declaration
(exhaustive vs sampled — sampled frames are part of this spec and recorded in SPRINT-131.md) ·
method · evidence rule · example finding shapes. Examples are finding SHAPES, not seeded answers:
each example was verified counterfactual (no live instance known at drafting time), or is
explicitly marked already-registered; this skill deliberately names no live defect.

## Dimension 1 — Architecture conformance
**Measures:** whether the code actually respects the service boundaries and dependency rules the
architecture documents promise (so a change in one context can't silently break another).
**Universe:** every `.csproj` in the tree (16 at the baseline: 8 under `src/`, 4 under `tests/`,
2 under `tools/`, 2 under `docker/mock-*`), every composition root (`Program.cs` in the 5 services
under `src/` plus the two docker mock hosts), every cross-service call site (HTTP client
construction/usage, service base-URL configuration), and `init.sql` table ownership vs the code
that reads/writes each table.
**Coverage: EXHAUSTIVE.**
**Method:**
1. Build the actual project-reference graph from all 16 `.csproj` files; diff it against the
   dependency rules in `docs/ARCHITECTURE.md` (in view).
2. Verify HTTP-only boundaries: services that must communicate over HTTP hold NO project
   reference to each other's assemblies (the PAT-005 class — no RuleEngine project references
   from other services — generalized to every declared HTTP boundary).
3. Read each composition root for cross-context wiring (DI registrations that reach across a
   bounded context).
4. Check schema-change discipline as code-visible facts: `init.sql` is the single schema source;
   `docs/generated/db-schema.md` is produced only by the sanctioned generator
   (`tools/generate_db_schema.py`); flag any second schema-defining or doc-generating path.
5. Check SharedKernel for creep: domain logic (rules, calculations, policy) that has migrated
   into the shared assembly rather than staying in its owning context.
Process-level governance rules (docs-are-Orchestrator-only, agent scopes) are enforced by the
Orchestrator's git-diff allowlist at sprint close — they are NOT this agent's scope; do not report
on them.
**Evidence rule:** the violating line (`.csproj` `<ProjectReference>` line, DI registration, call
site) as file:line, PLUS the specific rule it violates cited from `docs/ARCHITECTURE.md` (section
heading or rule text). No cited rule → not an architecture finding (it may still belong to another
dimension).
**Example shapes:** an illegal `<ProjectReference>` across an HTTP-only boundary (Critical — the
class example in the rubric); a service writing a table owned by another bounded context per
`init.sql` layout (High); calculation logic living in SharedKernel used by two contexts (Medium —
misleads a future change about where behavior lives).

## Dimension 2 — Complexity hotspots
**Measures:** functions too complex to change safely — ranked by how much the system leans on
them, so effort lands where a mistake would cost most.
**Universe & coverage — split, both declared:**
- **C# under `src/`: EXHAUSTIVE via the CI lizard artifact** (`lizard-ccn-report` from the `lizard`
  CI job: `lizard src/ -l cs -C 15 --warnings_only`, report-only, threshold CCN 15 per S39). The
  artifact at the baseline is supplied in the agent's prompt by the Orchestrator (TASK-B verifies
  its availability). The agent does not re-run lizard (no execution).
- **Frontend TS/TSX: SAMPLED (agent-read — the CI artifact covers only `src/` C#).** Declared
  frame: all `.ts`/`.tsx` under `frontend/src` excluding `__tests__/**`, ranked by line count —
  read the top 25 longest, PLUS all of `frontend/src/hooks/`, `frontend/src/contexts/`, and
  `frontend/src/components/guards/` regardless of length (state logic and authz guards are
  load-bearing at any size). Everything outside this frame is unexamined and declared so.
- C# under `tests/`, `tools/`, `docker/`: NOT in this dimension's universe (test complexity is
  dimension 3's concern; tooling complexity is Tier-3 by definition, below).
**Load-bearing ranking criteria (the defined scale — not ad-hoc):**
- **Tier 1 (invariant-adjacent):** rule-engine calculation paths, OK-version resolution/
  transition code, the payroll boundary (`Integrations.Payroll`, settlement/export services),
  authorization & scope enforcement, outbox/event delivery, audit projection/logging.
- **Tier 2 (feeds Tier 1 or is the product surface):** backend endpoints, infrastructure
  services, frontend state logic (hooks, contexts, guards) on flows that end in Tier-1 behavior.
- **Tier 3 (peripheral):** UI presentation components, demo-seed, mocks, one-off tools.
Within a tier, rank by CCN descending. Severity mapping — this is the rubric's promotion rule
applied: complexity is a pure changeability-risk class, so an over-threshold Tier-1 function with
unpinned branches is Medium (floor clause: blocks a safe future change) and is promoted to High
only when compounded with a guarantee-degrading defect (untested + complex, diverged-duplicate +
complex). Tier-3 over-threshold functions are Low (appendix) regardless of CCN — report them,
don't argue them up.
**Evidence rule:** function name + file:line + CCN. For C#, CCN cites the lizard artifact line;
for frontend, the agent states its CCN as an estimate ("~N branches, hand-counted") — estimates
are marked as such and never silently presented as tool output.
**Example shapes:** a CCN-28 settlement partition function whose branch matrix no test pins
(High); a CCN-18 endpoint handler mixing validation, mapping, and persistence (Medium); a CCN-17
demo-seed generator (Low, appendix).

## Dimension 3 — Test-suite quality
**Measures:** whether the tests would actually catch a regression — assertion strength, NOT
coverage percentage. A thousand tests that can't fail protect nothing.
**Universe:** the five suites at the baseline — `tests/StatsTid.Tests.Unit`,
`tests/StatsTid.Tests.Regression`, `tests/StatsTid.Tests.Smoke`, `tests/StatsTid.Tests.DemoSeed`
(11 test classes incl. golden-pin regression guards — real tests, Tier-3 weight in severity
terms), and the frontend Vitest suites (`frontend/src/**/__tests__/**`; co-located `*.test.ts(x)`
files join their nearest `__tests__` stratum) — ~3,269 tests in total. The agent re-derives the
per-suite file census as its first step and reports it (file counts are not restated here — the
census is the agent's, not this spec's).
**Coverage: SAMPLED (owner ruling OQ-2) — exhaustive tier + stratified sample + pattern-level
full scans. The frame below IS the declaration; the Orchestrator records it in SPRINT-131.md.**

**(a) Exhaustive tier — every file matching ANY of these tree-computable rules is read in full**
(rules are path/name/content-based so the tier is derivable from the worktree alone, without
ledger access):
1. *Payroll boundary & settlement:* path contains `/Payroll/` or `/Settlement/`, or filename
   matches `*Payroll*`, `*Settlement*`, `*Termination*`.
2. *Authorization & scope:* path contains `/Security/` or `/Orchestrator/` (scope-enforcement
   tests), or filename matches `*Scope*`; frontend: `AuthContext*`, `RequireAuth*`,
   `RequireRole*` tests.
3. *Outbox & event delivery:* path contains `/Outbox/` or `/Events/`, or filename matches
   `*Outbox*`, `*Atomic*`.
4. *OK-version transitions:* filename matches `*OkVersion*`, or path contains `/Migrations/`.
5. *Audit trail & audit projection:* filename matches `*Audit*` or `*Projection*` (audit is
   invariant-adjacent by this spec's own rubric).
6. *Rule-engine correctness:* filename matches `*RuleTests.cs` or `*Accrual*`, or path contains
   `/Rules/` (~13 files at the baseline — they fit the exhaustive tier, and most sit loose at the
   Unit project root where a folder-stratified sample could miss them).
7. *Architecture-constraint tests:* path contains `/ArchitectureConstraints/`.
8. *S130 security-fix regressions:* any test file whose filename or contents cite a SEC id
   (regex `SEC-?\d{3}`) — content-anchored, so the tier needs no sprint-log access.
9. *The smoke suite* in its entirety (it is the whole-workflow gate).
**(b) Stratified sample of everything else.** Strata = each top-level folder within each test
project (loose files at a project root form that project's "root" stratum; the DemoSeed project
is its own stratum) and each `__tests__` directory under `frontend/src` (co-located `*.test.ts(x)`
files join their nearest `__tests__` stratum). From every stratum not already fully consumed by
the exhaustive tier: read the **largest and the smallest** eligible test file by line count
(largest = most assertion mass and copy-paste risk; smallest = most stub/vacuous risk). Strata
with ≤2 eligible files are read in full. *Eligible* = filename ends `Tests.cs` / `.test.ts` /
`.test.tsx`; shared support files (`*ContractAssert*`, `*TestSchema*`, `*Harness*`, `*Factory*`,
`*Matcher.cs`) are read only when a sampled test depends on them. **Declared residual blind
spot:** assertion-present-but-weak tests in mid-size unsampled files are systematically
unexamined — that is the stated cost of sampling, accepted by owner ruling OQ-2, and it is
declared here rather than discovered later.
**(c) Pattern-level FULL scans across ALL test files** (grep-shaped, exhaustive at the pattern
level, no full reads): `Skip =` / `[Trait(` / environment-variable gating (the Docker-gating
census — what never runs locally); test methods containing no assertion call
(`Assert.`/`.Should`/`expect(`); `catch` blocks in tests that swallow and continue.
**Method (per in-frame file):** for each test, ask in order — (1) does it assert the meaningful
OUTCOME (not just "didn't throw" / mock-was-called)? (2) could it fail if the behavior under test
regressed (mutate mentally: flip the SUT's logic — does the test notice)? (3) are the negative
and boundary cases of the pinned contract present (deny paths, empty sets, period edges,
concurrency conflict arms)? (4) does it test the system or echo the mock's setup back?
**Evidence rule:** the specific test method at file:line + the concrete reason it cannot fail or
the precisely named missing case ("no test sends a role below X to endpoint Y and asserts 403").
"Coverage feels thin" is not a finding.
**Example shapes:** a vacuous test on a payroll-boundary path (High — rubric anchor); a missing
negative-authorization case on an approval endpoint (Medium; High if it is the only guard); a
Docker-gated suite whose local skip silently masks the only test of a code path (Medium).

## Dimension 4 — Duplication & dead code
**Measures:** code that exists twice (and will diverge) or code that exists for no one (and
misleads readers into maintaining it).
**Universe:** `src/**/*.cs`, `frontend/src/**/*.{ts,tsx}`, `tools/**/*.cs`,
`docker/mock-*/**/*.cs`; test code is in scope for the duplication class only (test QUALITY is
dimension 3's).
**Coverage: SAMPLED — declared as follows.** Whole-tree clone detection by agent reading is not
honest to claim. Duplication is examined over targeted clone-prone families: endpoint files (the
26 `Endpoints/*.cs` + route registrations in the 5 `Program.cs`), audit mappers, contract-test
assert helpers, settlement/export services, frontend UI component pairs and hooks. Dead-code
analysis is suspect-driven: any symbol that looks unreferenced during this or any dimension's
reading becomes a suspect and gets the full verification below (the VERIFICATION is exhaustive
per suspect; the SUSPECT LIST is not a census — declared).
**Method:** for duplication — identify structurally parallel blocks, then check for divergence
(has one copy been fixed/extended where siblings were not?). Divergence is what elevates
severity: identical copies are a maintenance tax (Medium at most); diverged copies are a latent
defect (High on invariant-adjacent code). For dead code — apply the conservative evidence rule.
**Conservative evidence rule (REQUIRED — the bar for "dead"):** reflection, DI, route-wiring, and
serialization can defeat static inference. A **confirmed dead-code** claim requires positive
evidence of unreachability — BOTH:
1. Zero references from an exhaustive symbol search of the entire baseline tree (all of `src/`,
   `frontend/`, `tests/`, `tools/`, `docker/`, config files); AND
2. No dynamic-dispatch surface: not DI-registered (by type OR assembly scanning), not an
   endpoint/route handler, not reflection-instantiated, not a serialization contract member
   (DTO/event properties are written by serializers, not code), not referenced by a
   configuration/route string, not part of a public contract another process consumes.
If ANY clause of (2) cannot be positively ruled out, the claim files as **"suspected dead code" —
Low, appendix — never Medium+ on inference alone.** This asymmetry is deliberate: a false "dead"
verdict that gets code deleted is worse than a missed one.
**Evidence rule:** duplication — every member file:line-range of the family plus the diff of the
divergence (quote both sides, ≤3 lines each); dead code — the symbol at file:line plus the search
scope and dispatch-surface checklist results, item by item.
**Example shapes:** a copy-paste family of quota-calculation helpers where one copy carries a
boundary fix the others lack (High); a provably-unreferenced service method that looks
load-bearing (Medium — misleads a future change); an unreferenced DTO property that a serializer
might populate (suspected — Low, appendix).

## Dimension 5 — Error-handling & API-contract consistency
**Measures:** whether the API fails predictably and consistently — same mistake, same status
code, same error shape, everywhere — and whether failures can pass silently.
**Universe:** every endpoint registration site in the tree — the 26
`src/Backend/StatsTid.Backend.Api/Endpoints/*.cs` files, `ApiEndpoints.cs`, and the inline
`Map{Get,Post,Put,Delete,Patch}` sites in all 5 service `Program.cs` files (~157 route
registrations across ~29 files at the baseline — the agent re-derives the exact census as its
first step and reports it) — plus exception-handling middleware and endpoint filters.
`docker/mock-*` hosts are OUT of scope (test doubles), declared. Frontend error-path handling
(`frontend/src/api/*.ts` + error branches in hooks) is a declared SECONDARY SAMPLE: read for
contract-consumption consistency only.
**Coverage: EXHAUSTIVE over backend endpoint sites; frontend sampled as declared.**
**Method:**
1. Build the status-code vocabulary table: per endpoint family, which of
   400/401/403/404/409/412/422/428 it uses for which failure class; the majority pattern (or the
   OpenAPI/spec-runtime contract where one is pinned by the `Contracts/` suites) is the norm;
   divergences are findings.
2. Error-body shape: ProblemDetails vs ad-hoc anonymous objects vs bare strings — one endpoint
   diverging from its family's shape is the canonical Medium.
3. Swallowed exceptions: `catch` blocks that log-and-continue or silently continue on WRITE
   paths; empty catches anywhere.
4. Fail-open patterns: any failure branch that defaults to permit/success (an error in an authz
   or validation check that falls through to the happy path).
5. Precondition semantics: concurrency arms (409 vs 412 vs 428 with ETags/rowversion) used
   consistently with the family's convention.
**Evidence rule:** endpoint + file:line + the observed behavior + the norm it diverges from
(cite the majority pattern with 2-3 counterexample sites, or the spec/contract-test that pins
it). A consistency finding without the norm stated is not filed.
**Example shapes:** one endpoint returns 409 where its family uses 412 for the same precondition
failure (Medium); a catch block that swallows a delivery failure and returns success on an
outbox-adjacent write (High — rubric anchor); a guard's error branch that falls through to
default-allow (Critical — a product fail-open, per the ruled Critical/High boundary).

## Dimension 6 — Warning debt
**Measures:** what the compiler is already telling us that nobody is listening to — and which of
those messages are load-bearing.
**Universe:** the complete compiler-warning output of a clean solution build at the baseline,
supplied to the agent as the CI build log/artifact at `7e4bb1b` (TASK-B verifies availability;
the agent does not build — read-only). The exact warning count is deliberately NOT stated in this
file; the agent derives it from the artifact and reports it (this doubles as an integrity
cross-check of the artifact supplied).
**Coverage: EXHAUSTIVE — every warning is triaged; none skipped.**
**Method:** bucket every warning into exactly one of:
- **fix** — the warning indicates a real defect or trivially-removable debt; name the fix shape.
- **suppress-with-reason** — the pattern is intentional; the finding proposes the suppression
  PLUS the justification text it must carry (an unexplained `#pragma`/`NoWarn` is itself debt).
- **ratchet-candidate** — a class too numerous to fix now; propose freezing the count and
  ratcheting down (a gate-promotion PROPOSAL row — owner rules; the audit flips no gate).
Group by warning code and by project; report the per-code × per-project matrix. Individual
warnings are typically Low (the appendix inventory carries the full matrix); a warning CLASS
registers at Medium+ when the class as a whole meets a floor clause (e.g., an obsolete-API class
on an invariant-adjacent path that a future change would trip over), and the ratchet/gate
proposals register as findings in their own right.
**Evidence rule:** warning code + count + representative file:line sites (≥2) from the build
artifact; for `[Obsolete]`-class warnings, also the declaration site of the obsoleted member.
**Example shapes:** a warning class that, taken as a whole, meets a floor clause and registers at
Medium with whatever disposition the code's context argues for; a nullable-reference warning
cluster on a rule-engine calculation path (Medium — plausibly changes behavior); a ratchet
proposal freezing the total at the observed baseline count (gate-proposal row).

## Dimension 7 — Doc/code drift (ISOLATED SLICE)
**Measures:** places where a document or comment asserts something the code contradicts — the
lie that misinforms the next reader.
**This dimension is an isolated slice:** its own reduced view, its own method, and **NO
withheld-calibration scoring** (doc-anchored debt is structurally either leaked or
undiscoverable, so scoring it would be theater — per the S131 refinement).
**View: the baseline worktree MINUS this ENUMERATED excluded-path set** (a strict superset of the
scored-view exclusions):
| Excluded | Why |
|---|---|
| `docs/sprints/**` | The ledger (view rule). |
| `docs/operations/security-finding-register.md`, `docs/operations/performance-finding-register.md`, `docs/operations/quality-finding-register.md` | The three operations registers (view rule). |
| `ROADMAP.md` | Forward-looking by design — divergence from present code is its purpose, not drift. |
| `docs/QUALITY.md` | TASK-E's re-grounding target; its staleness is already a pre-planned finding — re-finding it here is double-counting. (Also excluded from every scored view.) |
| `docs/operations/docs-governance-program.md` | A cross-session workstream status ledger — same class as sprint logs (and it names S131 itself). (Also excluded from every scored view.) |
| `docs/operations/audit-projection-caller-census.md` | Point-in-time census; the CLAUDE.md doc map designates it a historical dossier. Drift in a document that declares itself point-in-time cannot "misinform a reader". |
| `docs/operations/s64-regression-debt-census.md` | Point-in-time census by its own declaration, and ledger-class content (a debt census). (Also excluded from every scored view.) |
| `docs/references/agreement-source-register.md`, `docs/references/ferie-transfer-timing-research.md`, `docs/references/vacation-consumption-mechanism-research.md`, `docs/references/vacation-settlement-law-research.md`, `docs/references/agreement-ruleset-audit.md`, `docs/references/role-dimension-audit.md`, `docs/references/phase-b-handoff-package.md` | Point-in-time research/draft dossiers — kept for provenance, not current-truth routing (six are the CLAUDE.md doc map's "Historical & research dossiers" table; `vacation-settlement-law-research.md` is the same class, excluded for consistency). (`danish-agreements.md` stays IN view — it is current truth.) |
| `docs/reviews/**` | Point-in-time external-review archive: records of what a reviewer said, not claims about the code; also enumerates known defects (answer echo). |
| `.claude/**` | Harness/process tooling, not product documentation; self-referential (it contains this very skill and the sweep method); partially gitignored; and it is the sprint's own permitted write surface, so it is not baseline-stable. |
**IN view (the complement — notably):** `CLAUDE.md`, `SYSTEM_TARGET.md`, `docs/ARCHITECTURE.md`,
`docs/SECURITY.md`, `docs/FRONTEND.md`, `docs/WORKFLOW.md`, `docs/AGENTS.md`,
`docs/CONVENTIONS.md`, `docs/references/danish-agreements.md`, `docs/knowledge-base/INDEX.md`,
`docs/generated/db-schema.md`, `docs/operations/legacy-db-upgrade-runbook.md`,
`docs/operations/audit-projection-catalog.md`, all README/inline docs, and ALL code comments.
`docs/knowledge-base/INDEX.md` is deliberately IN: ADR/PAT entries assert current-truth code facts
and are exactly the claims worth drift-checking — but entries marked superseded/historical are
provenance, and drift inside them is below-floor by default. `docs/generated/db-schema.md` is IN
with one carve-out: its sync with `init.sql` is CI-owned (`tools/check_docs.py`) — do not re-check
the sync; only file a finding if the GENERATOR provably mis-renders a claim.
**Coverage — split, declared:** the in-view tracked doc corpus: **EXHAUSTIVE** (every checkable
claim). Code comments: **SAMPLED** — (a) all file-header and class-level comments under `src/`
(they carry architectural claims), (b) a grep-complete census of `TODO`/`HACK`/`FIXME`/`XXX`
tree-wide, (c) inline comments only in files opened while verifying doc claims. Declared;
anything outside is unexamined.
**Method — census first, then verify.** A **checkable claim** is a doc statement naming a file,
symbol, endpoint, table, count, or behavior with a verifiable referent in the view (aspirations,
opinions, and forward-looking statements are not claims). Procedure: (1) enumerate the checkable
claims per doc section FIRST — the claim census — and report the census count per document;
(2) verify each claim and classify: accurate / **drifted** (code moved on) / **dangling** (target
gone). Borderline is-this-a-claim calls are recorded as judgment notes, never silently dropped —
two agents running this method must produce the same census to within their declared judgment
notes. For comments: does the comment describe what the adjacent code does NOW?
**Evidence rule:** BOTH sides required — the claim at doc-file:line AND the contradicting reality
at code-file:line (or the verified absence: "class X exists nowhere in the tree; renamed —
nearest match Y at file:line"). One-sided evidence is not filed.
**Example shapes:** an architecture doc naming a class renamed two sprints ago (Medium —
dangling); a comment saying "validates the OK-version transition" above code that no longer does
(**High** — the promotion rule applies: the misinformation asserts a guard that does not exist,
degrading verification on an invariant-adjacent path — this is a guarantee-degrading class, not
mere changeability risk); a stale TODO pointing at completed work (Low, appendix).

## Dimension 8 — Observability/logging consistency
**Measures:** whether the logs would let a maintainer answer "what happened and where?" — and
whether they leak what they shouldn't.
**Universe:** all `ILogger` usage sites under `src/` (~35 files at the baseline — every `Log*`
invocation in them), logging configuration (`appsettings*.json` logging sections, logger setup in
the 5 `Program.cs`), the audit-logging middleware, and correlation-id propagation
(middleware + outgoing HTTP client headers + outbox correlation fields).
**Coverage: EXHAUSTIVE over `src/` ILogger sites + config. Frontend: a grep-complete CENSUS of
`console.*` sites with sampled reading (declared — flagged only for sensitive-data and
error-swallowing classes, not style).**
**Method:**
1. Structured-template discipline: named placeholders (`LogInformation("... {EmployeeId}", id)`)
   vs string interpolation (destroys queryability); message-template casing/shape consistency.
2. Level appropriateness both ways: **noise** (WARNING/ERROR emitted on normal, expected traffic
   — alarms that always ring get ignored) and **silence** (real failures logged at
   Information/Debug or not at all).
3. Correlation discipline: does every cross-service flow (Backend↔RuleEngine, outbox→consumers)
   carry the correlation id into its log statements, and is the id logged at the boundaries?
4. Actionability: does a failure log carry enough (ids, state, operation) for a maintainer to
   act, or just "operation failed"?
5. Sensitive data: tokens, credentials, secrets, or person-identifying payloads serialized into
   log messages. (Classify soberly: engineering-priority language, not incident language; if it
   looks like an access-control issue rather than a quality issue, report it anyway — the
   Orchestrator routes it to the SEC register.)
**Evidence rule:** the log call at file:line + the emitted template + what is wrong with it (the
missing id, the wrong level with the normal-traffic call path named, the interpolation). For
noise claims, name the code path demonstrating the traffic is normal/expected.
**Example shapes:** a warning-level log on a code path exercised by routine probing/tier checks,
creating noise that can mask real denials — *this class illustration corresponds to an
already-registered finding (SEC-012); it is cited here as a dedupe example ONLY — rediscovering
that instance earns no credit and merges to a cross-reference at TASK-D* — a NEW instance of the
class elsewhere is a valid finding (Medium); an outbox delivery-failure log with no correlation
id (Medium); an interpolated-string log on a load-bearing failure path (Medium; Low elsewhere).

---

# Inter-agent dedupe keys (merge happens at TASK-D, not in-agent)

**Finding identity key:** `(file-path @ 7e4bb1b, line-range, defect-class)`. Multi-file findings
(duplication families) key on the SORTED list of member file paths + the class. Every returned
finding row carries its key.

**Defect-class vocabulary (closed list — extended once at dual-lens review and closed again; pick
the matching class, and propose additions to the Orchestrator rather than inventing inline):**
`arch.boundary-violation` · `arch.cross-service-wiring` · `arch.schema-ownership` ·
`arch.schema-generator` · `arch.shared-kernel-creep` · `cx.hotspot` ·
`test.vacuous` · `test.cant-fail` · `test.weak-assertion` · `test.missing-negative` ·
`test.missing-boundary` · `test.env-gated-masking` · `test.mock-echo` ·
`dup.family` · `dead.confirmed` · `dead.suspected` ·
`err.status-vocabulary` · `err.body-shape` · `err.precondition-inconsistency` ·
`err.swallowed-exception` · `err.fail-open` ·
`warn.debt` · `gate.promotion-proposal` ·
`doc.drift` · `doc.dangling-ref` · `doc.stale-comment` ·
`log.unstructured` · `log.wrong-level` · `log.non-actionable` · `log.noise` ·
`log.silent-failure` · `log.sensitive` · `log.correlation-gap`

**Merge rules (applied by the refute panel / Orchestrator):**
1. Identical key → same finding; merge, keep the higher-evidence row.
2. Same file, overlapping line-ranges, same class → merge.
3. Same root cause surfacing in different classes/dimensions (e.g., a swallowed exception that is
   also a log.silent-failure) → the refute panel designates the PRIMARY dimension; other rows
   become cross-references inside the surviving row, never duplicate register rows.
4. **External dedupe (SEC / F register / ROADMAP backlog):** performed at TASK-D by the
   Orchestrator + refute panel — the sweep agents CANNOT do this (registers are outside their
   view). Already-tracked → cross-reference in the existing register, never a QUAL row. Agents
   must therefore report without self-censoring ("this is probably known" is not the agent's
   call).

# Output contract (what every sweep agent returns — returns, never writes)

**1. Candidate finding rows (Medium+ only), one pipe-row each:**
```
key(file:lines:class) | dimension | proposed-severity | confidence | title |
plain-language meaning | evidence | floor-clause | proposed-disposition | sources-consulted
```
- `title` — ≤10 words, specific ("Settlement export swallows publish failure"), no id assignment
  (QUAL-### ids are Orchestrator-only).
- `plain-language meaning` — 1-2 sentences a PM can act on: what is wrong and what it costs.
- `evidence` — file:line (both sides for drift findings) + a quoted anchor of ≤3 lines per site;
  per-dimension evidence rules above are mandatory minimums.
- `floor-clause` — which Medium clause is met: `changes-behavior` / `blocks-or-misleads-change` /
  `misinforms-reader` (one required; naming none demotes the row to the appendix).
- `proposed-disposition` — one of: `fix-now` (S132 candidate) / `ratchet` /
  `suppress-with-reason` / `document` / `gate-proposal` / `accept-as-is`.
- `sources-consulted` — every artifact consulted in forming the finding (files read, CI artifacts
  cited) — S129 parity: a calibration hit whose sources include an answer-bearing doc is
  discounted by the refute panel; this field is how that check is possible.

**Where each field lands** (the agent row is richer than the register on purpose — the register
is a pointer-index, the sprint log carries the depth):

| Agent row field | Destination |
|---|---|
| `plain-language meaning` | Register column "What it means (plain language)" |
| `title` | Register column "Title" |
| `dimension` | Register column "Dimension" |
| `proposed-severity` | Register column "Sev" — as adjusted by the refute panel/owner |
| `evidence` | Deep evidence in `SPRINT-131.md`; the register's "Source of truth" column points there |
| `key`, `confidence`, `floor-clause`, `proposed-disposition`, `sources-consulted` | `SPRINT-131.md` deep-evidence/adjudication section |
| — (no agent field) | Register columns "Status" and "Adjudication" — set by the Orchestrator/owner at TASK-E, never agent-set |

The Orchestrator transcribes, assigns QUAL-### ids, and routes gate-proposals per owner ruling
OQ-3.

**2. Below-floor inventory (compact — counts + pointers, no argumentation):**
```
dimension | defect-class | count | one-line pointers (file:line, comma-separated)
```
Below-floor items are NOT refuted or adjudicated, but they are never silent — the appendix is how
"below the floor" stays distinct from "invisible".

**3. Coverage declaration (required, even when empty):**
- Universe as declared above vs universe as actually examined (exact counts).
- Every skipped/unreadable item, named.
- Every "**I could not verify X**" — this is a valid, expected, and REQUIRED output class, not an
  admission of failure. Silent gaps are the one unforgivable defect of this method
  (S129's zero-silent-gaps contract).

# Persona & honesty rules (bind every sweep agent)

- **Evidence-cited claims only.** No file:line evidence meeting the dimension's evidence rule →
  no finding. Plausibility is not evidence.
- **No severity inflation.** The rubric above is the single scale; the floor-clause field is the
  test. The refute panel checks severity honesty against the rubric — an inflated row costs more
  credibility than a Low row costs effort. Engineering-priority language only; never incident/
  launch/exposure framing (learning project — see CONVENTIONS).
- **No self-dedupe, no self-censoring.** Report what the code shows; the Orchestrator and refute
  panel own dedupe (you cannot see the registers — by design).
- **Declare, never imply, coverage.** Exhaustive means every item, and you counted; sampled means
  the declared frame, and you name what fell outside it.
- **Estimates are labeled.** A hand-counted CCN, an approximate census — say so in the row.
- **"I could not verify X" is a first-class result.** So is "dimension N found nothing at the
  floor" — a clean dimension is reported explicitly with its coverage declaration, never left
  blank.
- **Read-only, always.** You return rows; you change nothing — not code, not docs, not this
  skill.
