# Docs & Governance Cleanup Program

**Status**: PHASES 1–2 COMPLETE (2026-08-13) — WS1 committed `3055886`, WS3 committed `ab3d6d9`, WS3b tail committed `7c516c9`. NEXT: Phase 3 = WS5 security sweep · **Owner**: Orchestrator + PM
**Why this exists**: a mid-session replan (per WORKFLOW.md Replanning Protocol). Several governance
and documentation threads opened while closing S128 and scoping the security sweep; this doc is the
**single source of truth** for all of them so nothing is lost across sessions. It supersedes the
scattered state (S128 Open follow-ups, the S129 security refinement, and in-conversation decisions).

**Plain-language goal**: get the docs clean and internally consistent *before* we lean harder on
AI-only development — because for this project the docs ARE the shared memory the agents run on, so
a stale doc is an agent acting on wrong information.

---

## Decisions already made (record, so they are not re-litigated)

- **D1 — Project framing.** StatsTid is a learning project in active development, not deployed; the
  production-grade Danish state SaaS is the design TARGET. (Landed in CLAUDE.md + `docs/CONVENTIONS.md`.)
- **D2 — Explanation standard.** Decisions/information must be explained so a product manager can
  understand AND learn from them. (In `docs/CONVENTIONS.md`, injected into every agent prompt.)
- **D3 — Priority model → INVARIANTS, not a ranking.** The old 1–9 "lower never compromises higher"
  order is replaced by: a co-equal **set of inviolable invariants** (a path that compromises any is
  invalid → find another path; genuine unresolvable conflict escalates to the owner), plus a short
  **ranked trade-off tier** for the things actually balanced (usability/UX, then shipping cadence),
  with **CI/CD named as the enforcement layer**, not a priority. Rationale: 7 of the 9 old items were things we
  would never trade — ranking non-negotiables against each other was meaningless, which is why the
  order never had a written rationale. **Full rename** chosen (named invariants, drop ordinals) so
  the docs read clean for AI-only development — accepting the doc-wide `P#` migration cost.
- **D4 — Sequence.** Governance model → thorough docs review (with the `P#` migration folded in) →
  security sweep. The security sweep reads the docs as input, so it runs against clean docs; it
  renumbers to the sprint after the docs sprint.
- **D5 — Docs-review scope.** "Live-truth" docs only (everything agents/Orchestrator treat as
  current truth). The 128 historical sprint logs get an index/freshness check only, not a
  line-by-line re-read.

---

## Workstreams

### WS1 — Governance model finalization (CLAUDE.md + CONVENTIONS.md) — PHASE 1
- [x] SYSTEM ROLE reframed to "target"; blocks moved to `docs/CONVENTIONS.md`; step-5 injection
      mandate; doc-map row. *(done, committed `3055886`)*
- [x] **Invariant-model rewrite** (D3): canonical model in `docs/CONVENTIONS.md` (invariant set +
      find-another-path + escalation + ranked trade-offs + CI/CD as enforcement); CLAUDE.md carries
      a compact named summary + pointer. *(done, uncommitted — the model lives in CONVENTIONS.md so
      it reaches agents; Step-7a Codex catch)*
- [x] Doc-map cleanup: Operations table split (durable vs historical/research); `FAIL-001` pin
      removed; "Maintaining this file" rule added; Agent-Architecture / How-to-Use overlap de-duped.
      *(done, committed `3055886`)*
- [x] Dual-lens review (architectural → mandatory): cycle 1 internal BLOCKED / Codex
      APPROVED-WITH-WARNINGS (the "priority order" term in CONVENTIONS.md + the model not reaching
      agents + stale checkboxes) → absorbed → cycle 2 BOTH APPROVED-WITH-WARNINGS, 0 blockers
      (residual: enumeration lists + a "delivery"/"shipping cadence" term slip) → absorbed. Converged.
- [x] Commit + push the governance change. *(committed `3055886`)*

### WS2 — `P#` rename migration (doc-wide) — FOLDED INTO WS3
Depends on WS1's rename landing. **CRITICAL nuance (found at WS3 start): `P#` is overloaded across
three meanings, and only ONE is migrated:**
1. **Priority-order rank** (`P7 = security`, the abolished 1–9 list) → MIGRATE to the invariant
   vocabulary wherever a live-truth doc asserts it as current.
2. **Review-severity labels** (`P1`/`P2`/`P3`/`P4` on Codex/Reviewer findings) → a DIFFERENT,
   still-valid system → LEAVE ALONE.
3. **Historical references inside the 128 sprint logs** → accurate to their era; rewriting
   immutable record is wrong → OUT OF SCOPE.
Highest-value target: AGENTS.md's agent-prompt templates embed the full 1–9 list (lines ~116, 226,
336, 474) — those are what actually reach agents. Plus WORKFLOW.md's pointer and scattered KB refs.
**Executed as part of the WS3 per-doc pass** (one touch per file), not as a separate sweep.

### WS3 — Thorough docs review — PHASE 2 (the big one)
Method (makes "thorough" verifiable): inventory → shared rubric → cluster fan-out (read-only
reviewers FIND; Orchestrator FIXES, since docs are Orchestrator-only) → findings register → fixes →
verify. Exit: every inventory row marked clean or fixed.

**The "clean" rubric — every finding cites file:line + category:**
- **A. Accuracy vs current code** — stale/wrong file paths, class/endpoint names, counts, behaviour
  claims vs the working tree at HEAD.
- **B. Priority-model migration** — flag live-truth assertions of the OLD ranked model; migrate to
  the invariant vocabulary (or a pointer to CONVENTIONS.md). Respect the three-meaning nuance in WS2
  (leave severity labels + sprint-log history alone).
- **C. Broken/stale citations** — drifted file:line refs, renamed/moved files, dead KB-id pins.
- **D. Internal consistency** — contradictions within a doc or against the canon (CLAUDE.md /
  CONVENTIONS.md / ARCHITECTURE.md).
- **E. Explanation standard / jargon** — undefined jargon or shorthand on first use that impedes a
  PM reader (note, don't over-flag).
- **F. Superseded-status accuracy** (KB) — an ADR/RES marked accepted that's actually superseded, or
  vice versa.

**Classification note:** individual ADR/PAT/DEP/FAIL/RES entries are append-only DECISION RECORDS
(like sprint logs) — review their STATUS + citations + priority-refs, do NOT rewrite the decision
prose. The actively-consulted live-truth docs (rewrite for correctness) are the canon set below.

**Inventory & clusters (live-truth only; sprint logs = index/freshness check, not line-by-line):**
- [x] **C1 Governance canon** — `AGENTS.md`, `WORKFLOW.md`, `QUALITY.md` (highest migration density).
- [x] **C2 Architecture/domain canon** — `ARCHITECTURE.md`, `SECURITY.md`, `FRONTEND.md`,
      `SYSTEM_TARGET.md`, `ROADMAP.md`, `SYSTEM_DOCUMENTATION.md`, `references/danish-agreements.md`.
- [x] **C3 KB decisions** — ADR-001…038 (status + citations + priority-refs only).
- [x] **C4 KB patterns/deps/failures/resolutions + INDEX** — PAT-001…018, DEP-001…004, FAIL-001…006,
      RES-001…003, `knowledge-base/INDEX.md`.
- [x] **C5 Operations durable + sprint index** — `legacy-db-upgrade-runbook`, `performance-finding-register`,
      `audit-projection-catalog`; `sprints/INDEX.md` freshness. (`docs-governance-program.md` = this doc, skip.)
- [x] Findings register (below) populated; fixes applied; verify pass. *(core `ab3d6d9`; tail 2026-08-13)*

#### WS3 findings register
*(populated as cluster reviews return; Orchestrator applies fixes after the full set is in)*

**C1 — Governance canon (returned).** AGENTS.md is the migration epicentre:
- `AGENTS.md:116–120, 226–235, 336–345, 474–483` [B] — FOUR agent-prompt templates embed the full
  ranked 1–9 list. Now stale AND redundant (CONVENTIONS.md is injected verbatim). Replace each with
  a short pointer to the invariant model in CONVENTIONS.md.
- `AGENTS.md:264` [B] — finding-format field `Priority: P[N]` → `Invariant / Trade-off: [name | n/a]`
  (the BLOCKER/WARNING/NOTE severity system itself stays).
- `AGENTS.md:184–190` & `302–307` [B] — the two Reviewer/Plan-Review trigger tables key rows on
  `P1…P7`; rename to invariant names.
- `AGENTS.md:245–252` [B] — Reviewer checklist items `P1 —…P8 —`; reframe by invariant name.
- `AGENTS.md:173, 200, 210, 296` [B] — vocabulary: "priority violation/order" → "invariant …".
- `AGENTS.md:42` [B, optional] — UX "secondary priority" concept survives; leave or lightly reword.
- **`AGENTS.md:184–190 vs 302–307` [D — ADJUDICATION, not mechanical]:** the invariants are
  co-equal, yet the Reviewer table buckets Security into the OPTIONAL tier while Plan-Review makes it
  MANDATORY. The mandatory/optional split is itself an artifact of the old ranking. **Owner/Orchestrator
  must decide** whether Security-invariant tasks are Reviewer-MANDATORY, and the tables must stop
  implying the invariants are ranked.
- `WORKFLOW.md:5` [C/B] — "For the priority order, see CLAUDE.md" → invariant model + CONVENTIONS.md.
- `WORKFLOW.md:79` [B/D] — KB-categories row "RES … when P2 conflicts with P9" → invariant terms
  (recurs in KB INDEX + CLAUDE.md doc-map — check there too).
- `WORKFLOW.md:21, 43, 14` [B, minor] — "priority order from CLAUDE.md" → CONVENTIONS.md injection;
  drop "(P8)" from the CI-health rationale; "priority alignment" → "invariant alignment".
- `QUALITY.md` — effectively CLEAN: all `P#` refs are dated historical sprint-log provenance (leave
  as-is); one optional [E] gloss suggesting a one-line note that P-numbers are the retired scheme.

**C2 — Architecture/domain canon (returned).** CLAUDE.md + CONVENTIONS.md + danish-agreements.md
clean. Real staleness found (needs a quick code-check before fixing where marked ✓verify):
- `SYSTEM_TARGET.md:141-142,150` [A/D] — auth model still says `ORG_AND_DESCENDANTS` + subtree/
  prefix matching; superseded by ADR-035 flat exact-org-set (SECURITY.md:76,102). `:183` still
  describes the S94-retired 428 `ORG_SCOPE_FALLBACK` gate; `:118` the S95-retired tree-root model.
  → update Section F to the flat/Organisation-scoped model.
- `ARCHITECTURE.md:13-43` [A] — service ports listed `:8081–8086`; actual is host 5200–5700 →
  container 8080 (compose ✓, contradicts FRONTEND.md:257). `:96` lists retired `TimerEndpoints` +
  omits ~13 live endpoint groups. `:132` stale primary colour `#0059B3` (S57→`#066b43`). `:203-238`
  KB tables stop at ADR-014/PAT-006. `:172` "387+ tests".
- `SYSTEM_DOCUMENTATION.md` (docs/system_documentation.md) — comprehensively FROZEN at Sprint 15
  (self-admits). Holds the old P1–P9 order as current (`:676,690-706,721,813-817` [B]) + many stale
  specifics (`ORG_AND_DESCENDANTS`, `#0059B3`, "29 event types" vs ~105, timer live, wrong SLS
  wage-type table vs danish-agreements). **DECISION: refresh vs. loud "SUPERSEDED — see canon" banner.**
- `FRONTEND.md:188-209,283` [A] — "18 hooks" (28 on disk, ~11 missing); `:74-103,272` omits
  `ui/Drawer.tsx`, "19 components" → 20. ✓verify counts.
- `SECURITY.md:154-157` [A] — policy list names 4, `AuthorizationPolicies.cs` defines 6 (missing
  `HROrAbove`, `LeaderOrAbove`). `:159-160` [D] prefix-scope reads as current → add "(dormant
  post-ADR-035)".
- `ROADMAP.md:120` [A] — "Current position (as of S67)"; completed table ends S69 while HEAD is S128
  — the "rolling detail" is ~59 sprints behind. **DECISION: advance the ledger, or freeze it
  explicitly and point to sprints/INDEX.md for S70+.** (Phase projections stay frozen — correct.)
- `danish-agreements.md:167-168` [D, low] — `PARENTAL_LEAVE` + `SICK_DAY` both → `SLS_0540`;
  ✓verify vs init.sql, annotate if intentional.

**C3 — KB decisions / ADRs (returned).** These are append-only records → fix STATUS/cross-refs +
one broken citation; do NOT rewrite historical reasoning.
- [F] `ADR-036:7` status "accepted" but its own note + ADR-038 say superseded → flip to
  "superseded by ADR-038 (S102)".
- [F] `ADR-038` overstates "supersedes ADR-035" — ADR-035's scope model is preserved (ADR-038 D5) →
  soften to "supersedes in part".
- [F] `ADR-017` D2/D2.1 have NO back-ref to the ADR-018 amendment that superseded them → add forward-note.
- [F] `ADR-027` has NO back-ref to ADR-035 (which retired the tree/styrelse boundary at S95) → add one.
- [F, low] `ADR-021:97` D6 "MONTHLY_ACCRUAL dead code" superseded by ADR-030, no forward-note → add.
- [B] 11 ADRs cite retired "Priority #N" as historical decision-reasoning (`ADR-002,003,004,005,007,
  016,017,021,023,029,035`). **Approach: a single old-Priority#N→invariant mapping note** (rather than
  editing 11 append-only records), EXCEPT:
- [B/C] `ADR-035:18,60` — BROKEN citation: "CLAUDE.md Priority #7 stays intact" points at a section
  that no longer exists → re-anchor to the "Security & access control" invariant.
- [format, low] `ADR-018:3`, `ADR-019:3` use inline `**Status**:` vs the table-row style of the other 36.
- Excluded correctly: `ADR-022:150`, `ADR-026:5` (severity labels). Clean: the 030–034 vacation chain,
  001/006/009/011/013/014/015/020/024/028/037/038, all accurate.

**C4 — KB patterns/deps/failures/resolutions + INDEX (returned).** All code-citation spot-checks
PASS (KB is accurate vs code). Fixes are status/vocab/title drift:
- [F] `FAIL-004:174,165-167` — Agent-Guidance still says the residual "needs an owner ruling" but the
  file's Status (`:7`) + header (`:153`) say RULED-AND-FIXED (2026-07-30) → reconcile the tail.
- [F/INDEX] `INDEX:71` `PAT-012` status `approved` vs file `active` → INDEX to `active`.
- [C] `RES-002:1` H1 still "enforced at the Teamoversigt surface only" — stale post-S128 (3 gates
  exist) → update to the INDEX phrasing; `:6` Sprint field "124" → add S128.
- [B] `INDEX:84-88` RES table has a column literally titled **"Priorities"** with `P2 vs P9` / `P7 vs
  P9` values → migrate to invariant vocab (this is the structured-metadata twin of WORKFLOW.md:79).
- [B] `PAT-005:32, PAT-006:28, PAT-007:26, PAT-008:41` — present-tense "supports P1/P5/P8/P9/P2"
  rationale → invariant/trade-off/enforcement vocab. `PAT-012:59,68` "P6 authority gap" ✓verify
  (may be a finding-label, not governance).
- [B, low] `RES-001:14,17,18`, `RES-002:31,34` inline `Priority #N` in historical Context prose →
  covered by the mapping-note approach; `priority-conflict` tag stays valid.
- [C] `DEP-004:186` UI-Pages table lists retired `ApprovalDashboard` as live + predates newer pages
  → note stale (its own drift-note covers only endpoint families).
- [C, low] `INDEX` Tag/Domain indexes (`:103-214`) frozen ~S17, omit newer entries — known debt, note.

**C5 — Operations dossiers + sprint index (returned).** F-series statuses + F4 defect pins accurate;
staleness is in counts/pins/anchor:
- [A] `audit-projection-catalog.md:144` total "71 rows" → **78** (74 interface + 4 TBD; matches 78
  `IAuditProjectionMapper` impls in src/); `:72` "Retrofit candidates (42)" → **67**; `:146-148`
  "49 of 53" closure → "74 of 78"; `:29-34` `TBD-cross-process-deferred` RESOLVED S45 (pin
  `RetroactiveCorrectionService.cs:222`→~283/302); 4 DI-registered mappers uncatalogued
  (ReportingLine ×3 + dead `UserEnhederChanged`) → add rows or scope out.
- [C] `performance-finding-register.md:72` `GetPageAsync` → `QueryByOrgScopeAsync` (:153); `:59`
  `ReadAllAsync` pin `:113`→`:195`; `:57` ApprovalPeriodRepository pins drifted ~+48 lines. (F-series
  dispositions substantively correct — pins only.)
- [A] `legacy-db-upgrade-runbook.md:157` "~30 tables" → **67** (init.sql). Operator query would misfire.
- [C/D] `sprints/INDEX.md:3` `anchor-sprint: 124` → **128** (trips check_docs ANCHOR_SLACK=3 against
  a file whose rows already reach S128). Recency + all four recent test-count sums verified CLEAN.
- [B] CLEAN across cluster (no ranked-order assertion; only this program doc, out of scope).

---

### WS3 disposition (Orchestrator) — 3 buckets
1. **Mechanical / clear** — the whole `P#`→invariant migration (incl. AGENTS.md 4-lists-→-1-pointer),
   the KB status/title fixes, the operations count/pin fixes, ARCHITECTURE ports, SECURITY policy
   list, FRONTEND counts. Approach for the ~15 historical `Priority #N` refs in append-only ADR/PAT/RES
   prose: **one central old→new mapping note**, not per-line edits.
2. **Verify-then-fix** — counts already cross-checked by the reviewers (78 mappers, 28 hooks, 67
   tables, ~105 events); Orchestrator re-confirms the few load-bearing ones before writing.
3. **Owner decisions — RESOLVED** —
   (a) `SYSTEM_DOCUMENTATION.md` → **DELETED** (owner 2026-08-13): frozen-at-S15, no forcing
       function, no reader, content duplicated in the canon; a stale onboarding doc is negative
       value. Doc-map row removed. (Future onboarding, if needed → generate from canon; parked in
       ROADMAP loose-ideas.)
   (b) `ROADMAP.md` → **REPURPOSED** (owner 2026-08-13) into a living forward-view + deferred
       backlog + loose-ideas parking lot; seeded from current deferrals. Its old jobs ceded to their
       real homes (stack→ARCHITECTURE, decisions→ADRs, ledger→sprints/INDEX, planning→sprint logs).
       Forcing function added to WORKFLOW.md (sprint-close routes deferred items into the Backlog).
   (c) C1 adjudication: **Security-invariant tasks are Reviewer-MANDATORY** (Orchestrator ruling,
       recommended — co-equal invariants get consistent review; matches the Plan-Review table).
       Applied in the WS3b AGENTS.md migration; owner may flip.

### WS3 execution split
- **WS3a (doc-map restructure)** — ROADMAP repurpose + SYSTEM_DOCUMENTATION delete + CLAUDE.md
  doc-map (2 rows) + WORKFLOW.md forcing-function. *(done, committed `ab3d6d9`)*
- **WS3b (P# migration + staleness fixes)** — *(DONE, committed `ab3d6d9` — the high-value core)*:
  - CONVENTIONS.md gained the authoritative legacy `Priority #N → invariant` mapping (so historical
    append-only refs decode without rewriting them).
  - AGENTS.md: 4 embedded ranked lists → pointer to CONVENTIONS; both trigger tables migrated to
    invariant names with **ALL co-equal invariants MANDATORY-review** (the consistent form of ruling
    (c) — Security no longer optional); checklist + finding-format + vocab migrated. Verified: 0
    remaining ranked-order refs in AGENTS.md.
  - WORKFLOW.md: pointer + RES-category description + 3 vocab spots migrated.
  - KB INDEX: `Priorities` column → `Invariant tension` (values migrated); RES section header;
    PAT-012 status `approved`→`active`.
  - KB entries: RES-002 stale H1 title + Sprint field; FAIL-004 self-contradiction reconciled;
    ADR-036 status `accepted`→`superseded by ADR-038`; ADR-035 broken `Priority #7` citations →
    Security invariant.
  - Operations: catalog 71→78 + retrofit 42→67 + closure 49/53→74/78; runbook ~30→67 tables;
    sprints/INDEX anchor 124→128.
  - Canon: ARCHITECTURE service ports 8081–8086 → host 5200–5700 (+container-8080 note), retired
    Timer, re-skin colour, de-brittled test count; SECURITY policy list 4→6 + prefix-dormant note;
    SYSTEM_TARGET auth model corrected to ADR-035 flat + S94/S95 (subtree/428/tree-root retired).
  - Verified live-truth docs clean of ranked-order assertions (QUALITY historical prose + SECURITY
    severity labels correctly left; mapping note covers historical KB refs).
- **WS3 dual-lens review** *(cycle 1: internal APPROVED-WITH-WARNINGS / Codex BLOCKED; cycle 2:
  both clear)*: internal verified the migration faithful + factual corrections correct; Codex caught
  2 BLOCKERs — (B1) MANDATORY-any-invariant collided with the unconditional SKIP rows → precedence
  note added (SKIP wins for trivial changes); (B2) a real error of mine — the SYSTEM_TARGET approval
  line kept a leader-by-org-scope fallback that ADR-035 retired → corrected to designated-edge OR
  HR/Admin org-scope. Internal's warning (the `refine-requirements` skill still cited "priority
  order") also fixed. Codex cycle-2 APPROVED.
- **WS3b minor tail** *(DONE 2026-08-13 — all items applied as mechanical fixes off the converged
  WS3 register; every count/pin re-verified against code at HEAD before writing)*:
  - FRONTEND.md: hooks table rebuilt **18 → 28** (phantom `useAbsences` removed — it never matched a
    file on disk; 11 real hooks added with verified descriptions), UI components **19 → 20** +
    `Drawer` row, orphaned-pages line corrected (`AbsenceRegistration`/`WeeklyView` deleted from
    source since), frozen S82 test-count pin de-brittled (→ sprints/INDEX + `npm run test`).
  - ARCHITECTURE.md: the hand-copied KB tables (frozen at ADR-014/PAT-006/DEP-004) replaced with a
    pointer to the CI-checked `knowledge-base/INDEX.md` + a short foundational reading order — the
    tables were a drift trap by construction.
  - ADR cross-refs: ADR-017 gained an Amended-by header row + D2/D2.1 forward-notes (→ ADR-018);
    ADR-021 D6 forward-note (→ ADR-030 activated `MONTHLY_ACCRUAL`); ADR-027 gained the missing
    ADR-035 reshape banner (S92→S95 retired its styrelse framing; D6 disposition table); ADR-038
    "supersedes ADR-035" softened to **in part** (D5 preserves the scope model); ADR-018/019 inline
    Status → table style (stale "pending approval/review" clauses dropped — both shipped).
  - performance-finding-register: verification found **14 of 22 pins exact**; the rest fixed —
    `GetPageAsync` → `QueryByOrgScopeAsync` (`:153`), `ReadAllAsync` `:113`→`:195` (stale at
    authoring, pre-F5 line), 3× ApprovalPeriodRepository +44, BalanceEndpoints `:904`→`:927`,
    db-schema range +1, AppLayout pin; 2 vanished files annotated in place (the F1 refinement doc —
    transient `.claude/refinements/` scratch — and the deleted F6 spec).
  - danish-agreements `SLS_0540`: **verified faithful to init.sql** (PARENTAL_LEAVE rows S1-era,
    SICK_DAY rows S9; the natural key excludes `wage_type`, so the sharing is structurally legal) —
    but intentionality is UNEVIDENCED (different sprints, never registered as a collision, no test).
    Annotated in the doc + flagged as a Phase B source-verification item.
  - DEP-004: UI-pages table given a drift note (ApprovalDashboard deleted S88;
    UserManagement/OrgManagement merged S109; route scheme renamed) — table retained as method record.
  - C4 stragglers the core missed, caught at tail-verify: **PAT-005/006/007/008** present-tense
    "supports P#" rationale migrated to invariant/enforcement/trade-off vocabulary. PAT-012's
    "P6 authority gap" (`:59,68`) adjudicated: historical finding-label prose inside S120/S122
    records → left, covered by the CONVENTIONS.md legacy map (the D5 append-only rule).
- Exit **MET** (2026-08-13): every row in the inventory checked — clean or fixed. Sprint logs:
  index/freshness check only (D5), verified clean under C5.

### WS4 — S128 follow-ups (recorded in SPRINT-128.md; not at risk of loss)
- [ ] FU-A — tier-probe spurious "Access denied" log noise (a non-logging classification path).
- [ ] FU-B — RES-002 9-read remainder (7 lack month params; also feeds WS5).
- [ ] FU-C — TASK-12802 loader-evidence rerun (needs a docker-capable machine OR the native
      rule-engine; see WS6).
- [ ] FU-D — SkemaPage 7203-pin vitest flake watch (graduates to a finding only on recurrence).
- [ ] FU-E — environment facts (recorded; actionable bit = native rule-engine for full UI testing).

### WS5 — Security threat-model sweep — PHASE 3 (was "S129"; renumbers after the docs sprint)
- [ ] Finalize refinement rev 2 + cycle-2 dual-lens verification (`.claude/refinements/REFINEMENT-s129-security-sweep.md`).
- [ ] Vendor the skill (`security.md` + `security-checklist.md` only; no hooks; no `--fix`; invoke-by-name).
- [ ] Build the SEC register with the corrected 12-read census + revisit rows ("known — should be
      revisited": prior rulings are re-attacked, not shielded).
- [ ] Run the sweep (static-analysis only; no live probing of the local stack) → adversarial
      verification → owner adjudication → remediation-sprint proposal.

### WS6 — Environment / infra (parked / on demand)
- [ ] Native stack back up (backend-api + Vite) when UI testing is wanted (Postgres already up).
- [ ] Native rule-engine on :5200 + finish the demo load (also produces FU-C's evidence).
- [ ] Docker on the VDI = external IT ticket (nested virtualization) — owner action, may be declined.

### Cross-cutting — git hygiene
Landed as planned: WS1 = `3055886` (governance model), WS3 = `ab3d6d9` (docs review), WS3b tail =
`7c516c9`, all on top of the S128 close (`8c182e9`). Keep `origin/master` current.

---

## Phase gate

The owner confirmed this capture and gave the go for Phase 1 (2026-08-12). Phases 1–2 (WS1 + WS3
incl. the tail) closed 2026-08-13. **Next: Phase 3 = WS5 (security threat-model sweep)** — it runs
through the `refine-requirements` gate and dual-lens verification when picked up, reading the
now-clean docs as input (the point of the D4 sequencing). WS4 follow-ups stay parked in
SPRINT-128.md; WS6 stays on-demand. This doc is updated as items close and reviewed at each entropy
scan.
