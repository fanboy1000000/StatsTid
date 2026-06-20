# Sprint 86 — Medarbejder-administration hifi: inline write affordances + visual-fidelity pass

| Field | Value |
|-------|-------|
| **Sprint** | 86 |
| **Status** | complete |
| **Start Date** | 2026-06-20 |
| **End Date** | 2026-06-20 |
| **CI Verified** | ✅ GREEN — run [`27852937661`](https://github.com/fanboy1000000/StatsTid/actions/runs/27852937661) on `e7d65ef`, all 7 jobs (incl. the e2e driving the real page in chromium — a real-browser check of the inline affordances; clean, no flake). |
| **Orchestrator Approved** | yes — 2026-06-20 |
| **Build Verified** | yes — `npm run build` clean; `tsc --noEmit` clean |
| **Test Verified** | yes — FE vitest 476 (+8); admin classes 104/104 independently; .NET tiers unchanged (FE-only) → full pyramid + e2e on CI |

## Sprint Goal
Close the remaining delta between the already-built `admin/ledelseslinjer` ("Medarbejder administration") page and the `design_handoff_medarbejder_administration/` hifi prototype. **FE-only — no backend/schema change** (the per-person approver model + all reads/writes already exist via ADR-027/S74–S77; the page already implements ~80–90% of the hifi). The delta is the hifi's **inline row-level write affordances** (Skift / + Tildel godkender / + Vikar / Afslut on the tree + inline orphan-card assign) — currently routed only through the EditPersonDrawer — plus two visual/behaviour gaps (approver-away annotation; picker Enter-to-pick) and a bounded fidelity audit.

Refinement: `.claude/refinements/REFINEMENT-medarbejder-admin-hifi.md` (dual-lens reviewed; BLOCKER-free). Owner rulings 2026-06-20: OQ-1 add inline affordances; OQ-2 keep the enforcement toggle; OQ-3 bounded visual audit.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| check_docs hard checks | CLEAN | db-schema in sync; KB INDEX complete; through S85 |
| Working tree | CLEAN | at the sealed-S85 tip (sprint-start = S85's close-polish commit) |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (FE redesign + the shared-mutation-core/lazy-mount/ETag-hydration design; the S76b single-save-path risk class) |
| **External Codex** | invoked 2026-06-20 — cycle 1: 0 BLOCKER, 4 WARNING, 1 NOTE; cycle 2: "Clean — resolved" |
| **Internal Reviewer** | invoked 2026-06-20 — cycle 1: 0 BLOCKER, 2 WARNING, 4 NOTE |
| **BLOCKERs resolved before Step 1** | n/a (0 BLOCKER); WARNINGs folded in |

### Findings (cycle 1)
Both lenses converged (0 BLOCKER) on the central design correction:
- **WARNING (both) — the extraction was too loose.** Don't carve new shared hooks out of `ApproverSection`/`VikarSection` (they're already the single mutation cores + own local optimistic mirrors; rewriting them re-tests the S76b-hardened drawer path). **RESOLVED**: reuse = mount the existing section components inline with row-shaped props; extract a helper only if duplication is unavoidable.
- **WARNING (Reviewer) — the ETag hydration lives in `LifecycleSections` (`:93/:99-102`), NOT `ApproverSection`** (prop). The inline path must replicate the `fetchEmployeeLines`→PRIMARY→version resolve or it silently becomes an If-Match-less second save path. **RESOLVED** (named in Phase 2).
- **WARNING (Codex) — lazy-mount wording:** the row buttons render eagerly (cheap); the CONTROLLER mounts on trigger. **RESOLVED**.
- **WARNING (Codex) — omitted hifi item:** the inline vikar NAME should be a link → that person's record (`ledelseslinjer-tree.jsx:70` vs plain text `:189`). **RESOLVED** (Phase 1 + AC).
- **NOTE (Reviewer) — away-lookup uses the memoized `byId` index** (O(1)/row, the S77 lesson); **RESOLVED**. Codex suggested splitting 8601 into 8601a/8601b; Reviewer (stronger): one coherent task — Phase 1/2 share the same component/files (the away-annotation lands in the Phase-2 approver-block rework), splitting creates a merge seam. **Kept as one sequential task.**
- **NOTE (both) — FE-only CONFIRMED** (all 4 writes exist under the ADR-027/S76/S83 guarded backend; no ADR/priority conflict). The absence-assertions to invert + the stale READ-ONLY comment are real.

### Resolution
All WARNINGs folded into TASK-8601. 0 BLOCKER. Cycle-2 (verification of the extraction/ETag framing) runs before Step 1.

## Architectural Constraints
- [x] P1 — no backend/schema change; the ADR-027 model + endpoints unchanged (8 modified + 3 new files, all under `frontend/src/pages/admin/`)
- [x] P7 — security: inline affordances hit the SAME guarded endpoints (S78/S83); the 409/403/412 handling reused from the sections; cycle-prevention forbidden set preserved (server `excludeEmployeeId` authoritative)
- [x] P9 — usability: hifi gaps closed (away annotation, Enter-to-pick, vikar link) + the inline quick-actions
- [x] No second save path (the S76b lesson): inline controls lazy-mount the EXISTING `ApproverSection`/`VikarSection` cores — ONE mutation implementation (Step-7a both lenses verified)
- [x] 2000-row tree perf preserved (S77 O(n) `medarbejderTree.ts` untouched); controller + forbidden-set lazy; away-lookup O(1) via memoized `byId`

## Task Log

### TASK-8601 — FE delta: visual gaps + inline write affordances + tests

| Field | Value |
|-------|-------|
| **ID** | TASK-8601 |
| **Status** | planned |
| **Agent** | UX/Frontend |
| **Components** | `frontend/src/pages/admin/MedarbejderAdministration.tsx` (+ `.module.css`), `editPerson/` sections (extract shared cores), `PersonPickerDialog`, `useReportingLines`, FE tests |
| **KB Refs** | ADR-027 (model), ADR-011 (FE design system), S76b (single-save-path), S82 (a11y kit) |
| **Orchestrator Approved** | no |

**Description**:
- **Phase 1 — visual-fidelity audit + gaps.** Compare the current page to `design_handoff_medarbejder_administration/design_files/` (the `ledelseslinjer-*.jsx` + `ledelseslinjer.css` — port only the ACTIVE styles per the README's dead-block warning) and close genuine gaps using the StatsTid tokens. Confirmed-absent items to add: (1) the **approver-away annotation** "· pt. \<vikar\> (vikar)" on the Godkendes-af block in BOTH the tree row AND the drawer's ApproverSection — derive "is my approver away?" via `byId[structuralApproverId]?.outgoingVikar != null` using the EXISTING memoized `byId` index (`MedarbejderAdministration.tsx:363`), O(1)/row (not a per-row scan — the S77 O(n²) lesson); (2) the picker's **Enter-picks-first-result**; (3) the inline **vikar NAME as a link** → opens that person's record (hifi `ledelseslinjer-tree.jsx:70`; currently plain text at `:189-192`).
- **Phase 2 — inline row-level write affordances** (the net-new surface; the tree `PersonRow` is read-only today — correct the stale "READ-ONLY this phase" header comment `:26-34`). **REUSE the EXISTING drawer section components inline** — `ApproverSection` / `VikarSection` (in `editPerson/`) ARE already the single mutation cores (each owns its `useReportingLines` calls + local optimistic mirrors); **mount them inline on the row with row-shaped props** (Step-0b: do NOT carve new shared hooks out of them — that would mean rewriting the sections + the drawer caller + re-testing the S76b-hardened drawer path; extract a small shared helper ONLY if duplication is otherwise unavoidable). The section's `local*` mirror is the source of truth between click and the roster refetch — do NOT double-drive from roster state.
  - **ETag hydration (the regression-prone spot):** the resolve lives in `LifecycleSections` (`:93/:99-102/:161`), NOT `ApproverSection` (which receives the ETag as a PROP). So the inline path must replicate the `fetchEmployeeLines`→pick the active PRIMARY line→`"${primary.version}"` If-Match resolve (mount a `LifecycleSections`-style resolve, or a small shared resolve helper) BEFORE assign — never assign from roster state (it has no line version; first-assign `If-None-Match:*` vs reassign `If-Match` is exactly that branch, `ApproverSection.tsx:89-94`).
  - Approver block: **"Skift"** (has approver) + **"+ Tildel godkender"** (red dashed, no approver) → `PersonPickerDialog` → assign (ETag-resolved as above).
  - Manager rows not away: **"+ Vikar"** → inline `VikarForm` below → create-vikar.
  - Inline VIKAR line: **"Afslut"** → end-vikar.
  - **Orphan card** rows: inline **"+ Tildel godkender"** → picker → assign.
  - **Lazy-mount the CONTROLLER** (the dialog/form/mutation section per active row) on trigger — the visible row buttons/triggers render eagerly (cheap, must show the affordance); only the stateful section mounts on click, NOT eagerly across ~2000 rows.
  - Reuse the drawer's 409/403/412 error handling + the existing `onChanged → loadRoster(...)` refetch on success.
- **Phase 3 — tests + a11y.** Fresh FE vitest for the inline affordances + handlers; **invert the current absence-assertions** (`MedarbejderAdministration.test.tsx:282` asserts these controls are absent — update them) and verify RED-on-old where they assert new behaviour; a11y (focus-visible, Escape, dialog semantics, the kit Radix patterns from S82); `tsc` clean.

**Validation Criteria**:
- [ ] Phase-1 gaps closed incl. approver-away annotation (tree + drawer, via the memoized `byId`), Enter-to-pick, and the inline vikar-name link → that person's record; bounded audit recorded.
- [ ] Inline Skift / + Tildel / + Vikar / Afslut on tree rows + orphan-card inline assign, by mounting the EXISTING `ApproverSection`/`VikarSection` components inline (no new save path; no rewrite of the drawer-hardened sections), the controller lazy-mounted on trigger, the reassign ETag-resolved via the `LifecycleSections` resolve (never from roster state).
- [ ] Enforcement toggle kept; stale READ-ONLY header comment (`:26-34`) corrected.
- [ ] FE vitest green (absence-assertions inverted — `MedarbejderAdministration.test.tsx:282/292-294`; new affordance tests RED-on-old); a11y (kit Radix Dialog, focus-visible, Escape); `tsc` clean; no backend change.
- [ ] 2000-row tree renders performantly (S77 O(n) `medarbejderTree.ts` untouched); picker caps-at-60 + server-search intact.

**Files Changed**: `frontend/src/pages/admin/MedarbejderAdministration.tsx` (+ `.module.css`), `frontend/src/pages/admin/editPerson/**` (shared-core extraction), `frontend/src/pages/admin/PersonPickerDialog` (Enter-to-pick), `frontend/src/hooks/**` (if a shared mutation hook is extracted), `frontend/src/pages/admin/__tests__/**`

---

### TASK-8602 — Validate + Step-7a + docs + close (Orchestrator)

| Field | Value |
|-------|-------|
| **ID** | TASK-8602 |
| **Status** | planned |
| **Agent** | Orchestrator |
| **Components** | validation, `docs/QUALITY.md`, `ROADMAP.md`, `docs/FRONTEND.md` (if a pattern is worth recording), `docs/sprints/SPRINT-86.md` |
| **Orchestrator Approved** | no |

**Description**: FE build + vitest + tsc; the page renders (optionally drive it against the demo stack). Step-7a dual-lens (FE-contract: single-save-path, ETag, a11y, hifi fidelity, no backend change). Docs (QUALITY/ROADMAP/SPRINT-86; FRONTEND.md if a reusable pattern emerges). Commit + push + CI-verify; MEMORY.

**Validation Criteria**:
- [ ] FE pyramid green; CI green on push (the .NET tiers unaffected — FE-only).
- [ ] Docs updated; `check_docs` green.

**Files Changed**: `docs/**`, `ROADMAP.md`

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Frontend vitest | 476 (+8) | green — admin classes 104/104 independently re-run; the absence-assertions inverted (RED on the pre-S86 read-only row) + 8 new affordance tests |
| `tsc --noEmit` / `npm run build` | — | clean |
| .NET (unit/regression/smoke) + E2E | unchanged | FE-only → exercised on CI (the e2e drives the real page in chromium) |

**RED-on-old:** reverting the source fails exactly the 9 S86 behaviour tests (16 unchanged pass); restored → 25/25 (the page test file) + 476/476 (full FE). The new tests pin the ETag arms (`If-Match "9"` reassign / `If-None-Match:*` orphan first-assign), +Vikar create→refetch, Afslut→endVikar→refetch, the vikar-name link, Enter-to-pick, and the `byId` away annotation. Mocks referentially stable (PAT-007).

## Step-7a artifacts
`.claude/reviews/SPRINT-86-step7a-codex.md` + `-reviewer.md` — both verdict PASS, 0 BLOCKER / 0 WARNING (Reviewer 2 non-blocking NOTEs).

## Sprint Retrospective

**What went well**: The reframe held — this was NOT the data-model migration the handoff implied (ADR-027 did that in S74–S77); the real delta was the FE inline write surface. The Step-0b external lens's correction was decisive: **reuse the existing `ApproverSection`/`VikarSection` cores inline (don't carve new hooks)** + **the ETag resolve lives in `LifecycleSections` not `ApproverSection`** — implemented exactly, so Step-7a came back clean on the S76b single-save-path + optimistic-concurrency, the two highest-risk areas. The "Afslut" two-click (confirm via the mounted section) over a destructive-on-render one-click was the right safety/fidelity trade (both lenses agreed). Lazy controller + lazy forbidden-set kept the 2000-row tree O(n).

**What to improve**: Could not eyeball the live stack at implementation time (backend was down) — vitest + tsc + build + the CI e2e (real chromium) are the verification; a manual visual spot-check against the prototype is a recommended follow-up. The away-annotation string now has two renderers (page-level inline + in-section drawer) — a small duplication worth a cross-ref comment.

**Knowledge produced**: No new KB entry (an FE delta over an established page). Pattern worth noting (candidate for `docs/FRONTEND.md` if it recurs): *reuse a drawer section's mutation core inline by lazy-mounting it with additive opt-in props + a shared ETag-resolve helper, rather than extracting a new hook* — preserves the single-save-path while exposing quick-actions.
