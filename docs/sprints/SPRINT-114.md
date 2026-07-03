# Sprint 114 — DemoSeed units upgrade: the demo world exercises the full Enhedsspor spine

| Field | Value |
|-------|-------|
| **Sprint** | 114 |
| **Status** | complete — CI GREEN `28628276455` (all 7 jobs, 2026-07-03; the full regression on CI's clean services-postgres CLOSES the 42 deferred fixed-port tests) |
| **Start Date** | 2026-07-02 |
| **End Date** | 2026-07-03 |
| **Orchestrator Approved** | yes — 2026-07-03 (Step 7a: Codex 1B [the stray pre-S114 layout-polish files — RESOLVED by separate commit `5004eae`] + Reviewer APPROVED-WITH-WARNINGS 0B/1W [same finding, same resolution]; the S114 core CLEAN both lenses) |
| **Build Verified** | yes — `dotnet build` 0 errors (S114 scope: DemoSeed tool + tests only) |
| **Test Verified** | yes (local): 861 unit + **1203 regression** (1161 run locally [2 FAIL-002 sheds isolation-cleared 4/4 — `PositionOverrideAtomicTests`, untouched by S114] + **the 42 fixed-port tests CI-DEFERRED with cause**: they require the baseline :5432 postgres, which would tear down the owner's live mid-testing demo stack; S114 touches ZERO regression surface and the 42 ran 42/42 at the S113 close; the push CI re-runs the full suite on a clean services-postgres) + 6 smoke (rides CI) + **55 demoseed** (+26: golden pin + unit derivation + load planner) + 553 fe; **pyramid 861u+1203r+6s+55demoseed+553fe = 2678 (+26)**; LIVE proof: 452 units/3,231 homed/442 leaders/0 4xx + idempotent re-run + UI screenshots; **CI GREEN `28628276455` (all 7 jobs — the FULL regression incl. the 42 fixed-port tests on CI's clean services-postgres, closing the deferral)** |

## Sprint Goal
Owner ask (mid-UI-testing): "Upgrade the demo seed and make sure the test orgs use all the levels in the org structure." Today the demo world (S84 manifest) has 3,231 people + 3,226 reporting edges but ZERO units — the merged admin page renders demo orgs as one flat name list, and NO `enhed`-type unit exists anywhere in the system. S114 makes the 5 demo styrelser (STYX1–5) exercise the full spine (direktion › område › kontor › team › enhed) with real leaders, grouped reports, and small deliberate messiness — demo tooling only, zero product-code change.

Refinement: `.claude/refinements/REFINEMENT-demoseed-units.md` — **READY, review CLOSED at cycle 3** (cycle-1: Codex 2B [single-unit-membership conflict; loader order] + Reviewer 1B [the leaf-split amber-flood]; cycle-2: Codex 1B [depth-via-manifest dishonest — `managerCount` is generator-internal, span hardcoded]; cycle-3: Codex RESOLVED + Reviewer APPROVED-WITH-WARNINGS [the depth-cap/assertion constraint, absorbed]). The design that survived: **derive units FROM the existing per-org reporting-edge tree** (manager m → unit U(m); m homed in U(m); members = m + m's NON-manager reports; manager-reports = CHILD UNITS), with all 5 levels arising NATURALLY from a deeper generated manager tree via an **optional per-tree span/depth override** (absence = byte-exact legacy path, golden-pinned).

**Explicit exclusions:** NO product/schema/authority code; NO baseline (init.sql) changes — STY02's 4-unit tree is fixture-pinned; NO edge re-pointing or post-hoc leader promotion; NO CI wiring changes (the loader stays a manual tool). The owner's live hand-made test units ("Test dr"/"Test område"/"Test kontor") are volume-local and will be discarded at the next fresh demo load — flagged, accepted.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| Demo-world state (live DB) | flat | units: 4 baseline (STY02, init.sql-pinned) + 2 owner-created; ALL 3,231 demo people unit-less; `enhed` type unused system-wide |
| Generator determinism | verified | ONE seeded `_rng`, fixed order; a separate post-pass on a derived `Random` leaves existing draws untouched |
| Depth math | verified (cycle-2/3) | `managerCount` = generator-internal `round(n×0.14)`; span = `ScaleConfig.TargetSpan` (7 full). Span 4 ⇒ STYX1's 280 managers layer 1+4+16+64+195 = exactly 5 (headcount unchanged); smallest styrelser (~14 managers) need the DEPTH-FORCING knob (span alone can't reach 5) |
| API invariants | verified | leaders 422 non-members; **re-homing STRIPS leaderships (D3, silent)**; sibling active-name 409; server-GUID unit ids; PARTIAL-RANK strict |
| DemoSeed test suite | 18 methods (~29 cases) | structural pins read the SHIPPED `full`/`smoke` scales directly → deliberate pin updates are in scope |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | OPTIONAL by the table (tools+tests only; no P1/P3/P4/P7/schema/payroll) — **run anyway** (Orchestrator discretion: a 3-cycle refinement signals subtlety; the review verifies the cycle-3 constraints landed in the plan) |
| **External Codex** | cycle 1 (2026-07-02): **0B/1W/3N** — (W) TASK-11400 checklist narrower than its description → explicit live-proof deferral checkbox added. Transcription of all cycle-3 constraints confirmed; TASK-11401 sequence confirmed executable; one-agent shape confirmed right-sized |
| **Internal Reviewer** | cycle 1 (2026-07-02): **0B/1W/5N, APPROVED-WITH-WARNINGS** — (W) the golden-pin baseline mechanism unspecified (a post-change self-comparison verifies NOTHING) → pinned: capture the golden artifact from the PRE-CHANGE generator as the FIRST implementation step. NOTEs absorbed: the final fresh-demo-reload step (leave the owner a working stack); KB refs → ADR-038/SECURITY.md; the SQL byte-identity should PASS by construction (positional manager set, edges API-loaded) |
| **BLOCKERs resolved before Step 1** | **yes — ZERO BLOCKERs both lenses; both WARNINGs absorbed by plan edit** |

---

### TASK-11400 — DemoSeed: generator + loader + verifier + tests + manifests
| Field | Value |
|-------|-------|
| **ID** | TASK-11400 |
| **Status** | complete (2026-07-03; one usage-limit interruption at the reading phase, resumed clean) — **GOLDEN-FIRST honored**: pre-change artifact committed with sha256s (`Golden/golden-legacy-smoke.{sql,manifest.json}`; `[JsonIgnore(WhenWritingNull)]` on `UnitPlans` keeps legacy comparison whole-file byte-exact). `TreeProfile` gains `UnitSpanOverride` (depth-forced layered spine, ZERO RNG draws — pure index math) + `ManagerCountOverride` (smoke only); per-org: STYX1 span4 `[1,4,16,64,185]`, STYX2 span3, STYX3/4/5 span2, smoke span2+mc6 — all depths 0–4 exactly, generation-time assertion throws exit-3. Unit derivation = post-pass on `Random(seed ^ 0x01145EED)`; membership/leader-is-member/PARTIAL-RANK/name-uniqueness asserted at generation. Messy ledger recorded in-manifest (2 leaderless + 3–5 sideways per org full; smoke 2+1 — disjointness-capped, ledger-exact). Loader: forest-probe-create parent-first → roster-probe homing (declared adaptation: `UserDetailResponse` has no `unitId` → the SKIP probe is the org roster read; the per-user GET supplies the fresh ETag) → leaders LAST; `UnitLoadPlanner` pure-logic unit-tested; old manifests skip cleanly. Verifier: exact ledger counts + totality + leader-is-member. **Tests 29→55 cases (18→37 methods); ZERO pre-existing pins changed (manager sets/headcounts held by design — verified by the green run). `99-demo-seed.sql` regenerated BYTE-IDENTICAL (git diff empty) — the by-construction argument held. Orchestrator re-ran: 55/55; scope clean (zero src/docker changes).** Findings: the gitignored smoke manifest was S103-stale (now fresh); the loader's forest/roster wire-shape dependency DECLARED for future fork-B retrofits. 2 PROPOSED KB entries accepted → close |
| **Agent** | Demo Tooling (cross-domain authorized: `tools/StatsTid.DemoSeed/**`, `tests/StatsTid.Tests.DemoSeed/**`, `docker/postgres/99-demo-seed.sql` [regen-only, byte-identity TESTED]) |
| **Components** | DemoGenerator, ScaleConfig, DemoLoader, DemoVerifier, DemoManifest, both manifests |
| **KB Refs** | ADR-038 (single-unit membership, D1; + its S104 PARTIAL-RANK and S105 D4 amendments), docs/SECURITY.md (the D4 unit-leader approval section) |

**Description** (the refinement §§1-6 verbatim-binding; highlights):
- **Generator:** optional per-tree span/depth override (absence = byte-exact legacy); the unit-derivation post-pass on a derived `Random` (never touching `_rng`); depth→type TOTAL and CAPPED at 4 (**generation-time assertion per org: max manager depth == 4 AND ≥1 manager at every depth 0–4 — fail generation, never the load**); membership rule: m homed in U(m), members = m + non-manager reports, manager-reports = child units; deterministic Danish unit names with per-parent suffixes (the 409 constraint); messiness small/deliberate/counted — ~2 leaderless units + ~3-5 cross-unit sideways cases per org, **sideways cases from NON-manager leaf members ONLY** (the D3 strip decapitation hazard), placed in DISJOINT units.
- **Loader (canonical stage order):** units parent-first via forest-probe-then-create (match org+parent-chain+name; never delete; idempotent) → home ALL members probe-first (GET → skip-if-homed → PUT with the FETCHED ETag; never blanket If-Match "1") → appoint leaders LAST. Zero 4xx expected.
- **Verifier:** all-5-types-per-org; leader-is-member; homing totality; leaderless-unit count == deliberate count EXACTLY; messy-case counts; zero-4xx.
- **Tests:** golden-subset pin — **MECHANISM PINNED (Step-0b Reviewer): capture the golden artifact (a committed hash or golden file of the no-override people/edges/activity output) from the PRE-CHANGE generator AS THE FIRST IMPLEMENTATION STEP, before touching any generator code; the test asserts post-change no-override output == THAT artifact** (a test written after the change comparing the changed code to itself verifies nothing); unit-derivation determinism; the generation-time assertion RED case; the shipped-scale structural pins DELIBERATELY updated; per-parent name uniqueness.
- **Manifests:** full + smoke regenerated with per-org overrides (all 5 levels each, incl. smoke); headcounts unchanged (~3,231 full).

**Validation Criteria**:
- [ ] `dotnet build` 0 errors; DemoSeed suite green (updated pins + new tests, incl. the golden-subset pin [pre-change-captured artifact] and the assertion RED case)
- [ ] Generator: same-manifest double-run byte-identical; no-override config output identical to the PRE-CHANGE golden artifact
- [ ] Both manifests regenerate deterministically; each demo org's manifest tree has manager depths 0–4 exactly
- [ ] Loader/verifier: compile + unit-level coverage of the stage order and probe logic; **LIVE load/idempotency/verifier proof explicitly DEFERRED to TASK-11401** (Step-0b Codex — it needs the fresh stack)

---

### TASK-11401 — Orchestrator: live end-to-end validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11401 |
| **Status** | in progress (2026-07-03) — **LIVE PROOF COMPLETE, all green:** fresh `down -v` → demo compose up (rebuild, all healthy) → `load` full: **452 units created / 3,231 homed / 442 leaders / 10 deliberately leaderless (2×5 orgs) / ZERO unit-stage 4xx**. **Idempotency re-run: 0 created (452 matched) / 0 homed (3,231 skipped-correct) / zero 4xx — no 412 storm, no duplicates.** DB spot-proof: every demo styrelse uses ALL 5 types at the EXACT pinned layerings (STYX1 1/4/16/64/185; STYX2 1/3/9/27/41; STYX3/4 1/2/4/8/19; STYX5 1/2/4/8/18); 0 unit-less demo people; baseline STY02 untouched (4 units). **UI screenshots captured: the org-level structure tree (no more flat list) + Direktionen rendering the design exactly — the LEDER badge (Emil Christensen, with a seeded-vikar badge rendering in context), 7 grouped direct reports beneath him, 4 OMRÅDE child units with their leaders.** **S105 D4 ACTIVATION (the refinement §6 statement): appointing the 442 derived unit leaders deliberately activates the unit-leader approval path in the demo world — aligned with (not duplicating) their existing designated-approver authority; divergence arises exactly at the deliberate cross-unit exceptions, D4's intended secondary path.** Close machinery: suites fresh (861u + 55 demoseed + 553fe); the regression runs LOCALLY MINUS the 42 fixed-port tests (they require tearing down the owner's live demo stack for the baseline :5432 postgres — S114 touches zero regression surface; the 42 are unchanged since their 42/42 at S113 close and re-verify in the push CI on a clean services-postgres; the owner's mid-testing stack stays UP — the sprint's own final-state rule) |
| **Agent** | Orchestrator |
| **Components** | live demo stack, sprint log, close gates |
| **KB Refs** | FAIL-002 (compose/:5432 discipline), FAIL-003 |

**Description**: Fresh live proof: `down -v` → demo compose up → `load` (full) → DB spot-checks (5 types per org; homing totality; leaderless == deliberate) → **UI screenshots: the design's tree on a demo org (unit rows with leader names; expansion = leaders → grouped reports → child units; the deliberate messy cases rendering as designed — leaderless banner incl. the expected refererer-opad-til-in-unit quirk; amber cross-unit with a valid "Ret" target)** → loader RE-RUN idempotency proof (skips, no 412s, no duplicates) → the S105 approval-path sanity check (a derived unit leader sees/acts on an in-unit member's period). Then suites (`sprint-test-validation` delta), Step 7a dual-lens, close through the 5 gates. NOTE: the demo stack occupies :5432 — `down -v` before any fixed-port regression runs (FAIL-002 ops rule), and the close-time regression run sequences AFTER the live-proof stack is torn down or against a fresh compose Postgres. **FINAL step after close (Step-0b Reviewer): bring the demo stack BACK UP and reload the NEW demo world — the owner is mid-UI-testing and must be left with a working, upgraded stack, not an empty port.** The sprint-log narrative carries the refinement §6 S105-activation statement explicitly.

**Validation Criteria**:
- [ ] Live load: zero 4xx; all AC screenshots captured; re-run idempotent
- [ ] Full pyramid green (the DemoSeed tier's new counts recorded in the delta table); Step 7a both lenses; close + push + CI green

---

## Phase Plan
- **Phase 1:** TASK-11400 (one agent — the tool is one coherent surface; generator feeds loader)
- **Phase 2:** TASK-11401 (Orchestrator; requires the live stack — the current demo stack gets `down -v`'d and reloaded fresh with the new manifests)

## External Review (Step 7a)
| Lens | Result |
|------|--------|
| **External Codex** (`codex review`, full uncommitted diff; re-ran the DemoSeed suite 55/55) | **cycle 1: 1 BLOCKER** — the tree carried the pre-S114 layout-polish product files (the owner's earlier cramped-UI request, validated then, left for visual approval) inside a demo-tooling-only sprint. **RESOLVED as demanded: committed separately (`5004eae`)** — the close commit is demo tooling + docs only. All hard constraints otherwise clean. Artifact: `.claude/reviews/SPRINT-114-step7a-codex.md` |
| **Internal Reviewer** (the 3-cycle refinement + Step-0b instance; re-ran 55/55) | **cycle 1: APPROVED-WITH-WARNINGS 0B/1W/4N** — the same layout-files finding (resolved identically). **Golden honesty verified 3 independent ways incl. content forensics; legacy path byte-equivalent at every branch; all 8 generation-time assertions present + RED-proven; the D3 re-homing hazard proven IMPOSSIBLE by construction** (same-unit PUT = no-strip no-op; appointments always follow homing). Artifact: `.claude/reviews/SPRINT-114-step7a-reviewer.md` |

## Test Summary
| Suite | S113 | S114 | Delta |
|-------|------|------|-------|
| Unit | 861 | 861 | 0 |
| Regression (Docker) | 1203 | 1203 (1161 local + 42 fixed-port CI-deferred-with-cause) | 0 |
| Smoke | 6 | 6 (rides CI) | 0 |
| DemoSeed | 29 | **55** | **+26** (golden pin 2 + unit derivation 17 + load planner 7) |
| Frontend (vitest) | 553 | 553 | 0 |
| **Total** | **2652** | **2678** | **+26** |

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance | N/A — demo tooling only; no rule/agreement/payroll surface |
| Wage type mapping correctness | N/A |
| Event sourcing / audit | Exercised, not modified — the loader drives the REAL admin APIs (unit/homing/leader events flow through the production paths) |
