# Sprint 57 — UI Re-skin: adopt oes.dk (Økonomistyrelsen) color palette

| Field | Value |
|-------|-------|
| **Sprint** | 57 |
| **Status** | complete |
| **Start Date** | 2026-05-31 |
| **End Date** | 2026-05-31 |
| **Orchestrator Approved** | yes — 2026-05-31 |
| **Build Verified** | yes — `tsc --noEmit` clean (0 errors) |
| **Test Verified** | yes — 128 frontend vitest passing (presentation-only sprint; no backend/test-count change) |
| **Sprint-start commit** | `0c45709` (S56-era doc-health close) |

## Sprint Goal
Re-skin the StatsTid frontend default theme to Økonomistyrelsen's (oes.dk) visual identity —
green-led (`#066b43`) on a warm-neutral gray scale with charcoal text — **adjusted to pass
WCAG 2.1 AA** (ADR-011 requirement). Default theme only; per-tenant theming stays deferred
(ADR-025 D6). Full migration: complete the token system, remap all tokens, and migrate the
hardcoded colors that bypass tokens today.

Refinement + dual-lens Step-4 review: `.claude/refinements/REFINEMENT-s57-oes-palette.md`.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | tools/check_docs.py green at sprint start |
| Pattern compliance | CLEAN | n/a for a presentation-only sprint |
| Orphan detection | DEBT (pre-existing) | several unrouted legacy pages (TimeRegistration/WeeklyView/EntitlementConfigEditor) carry hardcoded colors; migrate or skip per scope |
| Documentation drift | CLEAN | docs current post-S56 reconciliation |
| Quality grade review | n/a | Frontend grade re-assessed at sprint close |

## Plan Review (Step 0b)
**SKIP — superseded by the refinement Step-4 dual-lens review** (Codex + Reviewer Agent, both
convergent, 2 BLOCKERs absorbed into the plan before any code). P9 presentation-only sprint;
no schema/auth/payroll/rule-engine surface. Rationale recorded here per WORKFLOW SKIP convention.

## Key decisions (from refinement)
- Primary = green `#066b43`; links `#3e72a6`. AA-safe palette (text-role tokens darkened):
  success `#0f766e`, warning `#8a6a00`, secondary-text/gray-500 `#6b6b6e`. All ≥4.5:1 on white.
- **Complete the token system**: add the ~12 tokens components reference but tokens.css never
  defined (`--color-border`, `--color-danger`, `--color-text-secondary`, `--color-bg-hover`,
  `--color-bg-muted`, `--color-primary-hover`, `--color-border-strong`, etc.). Defining these
  auto-resolves ~316 `var(--token, #fallback)` refs to oes values.
- Migrate the ~124 truly-hardcoded hexes (no `var()`) across ~25–31 files to tokens.
- Amend ADR-011 palette section (+ fix the "inspired by designsystem.dk" framing line).

## Task Log
_(in progress — recorded as tasks complete)_

### TASK-5701 — AA-safe oes token system (foundation) — COMPLETE
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator (design-token contract / cross-cutting) |
| **Components** | `frontend/src/styles/tokens.css` |
**Description**: Rewrote tokens.css with the AA-safe oes palette + all previously-phantom tokens
(`--color-border`, `--color-danger`, `--color-text-secondary`, `--color-bg-hover/-muted`,
`--color-primary-hover`, `--color-border-strong`, `--color-gray-50`, `--color-bg-subtle`, …).
Defining these auto-resolved ~316 `var(--token, #fallback)` refs to oes values. All text tokens
verified ≥4.5:1 on white (primary 6.58, link 5.05, success 5.47, warning 5.07, info 5.08, error 5.89).

### TASK-5702 — Migrate hardcoded colors → tokens — COMPLETE
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | UX Agent |
| **Components** | 25 files under `frontend/src` (`*.module.css` + 3 inline-style `.tsx`) |
**Description**: Migrated ~124 hardcoded hexes (old action-blue → green primary; status/gray/border
hues → semantic tokens). Orchestrator validation: residual color-hex grep = **0**; undefined-token
grep = **0** (after adding gray-50/bg-subtle); vitest 128 green; `tsc --noEmit` clean.

### TASK-5703 — Amend ADR-011 palette section — COMPLETE
| Field | Value |
|-------|-------|
| **Status** | complete |
| **Agent** | Orchestrator (docs) |
| **Components** | `docs/knowledge-base/decisions/ADR-011-...md` |
**Description**: Added S57 amendment note (oes-derived AA-safe palette supersedes the color-token
values; typography/spacing/component-strategy unchanged; per-tenant theming stays deferred per ADR-025 D6).

## External Review (Step 7a)
| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex gpt-5.5 + internal Reviewer Agent), 2026-05-31 |
| **Reviewed-against** | `0c45709` (sprint-start) |
| **Cycles** | 1 per lens (no BLOCKERs) |
| **Verdicts** | Codex APPROVED-WITH-WARNINGS; Reviewer APPROVED |
| **Findings** | 0 BLOCKER. 1 convergent AA WARNING **fixed** (`--color-info` #1e7796→#1a6a86; info-on-info-light was 4.15:1). MondayDatePicker selected-state WARNING = false alarm (verified distinguishable). Reject-hover flattening = accepted cosmetic (deferred). |

Artifacts: `.claude/reviews/SPRINT-57-step7a-{codex,reviewer}.md`.

## Validation summary
- residual hardcoded color hex outside tokens.css = **0**; undefined `var(--color-X)` refs = **0**.
- vitest **128/128**; `tsc --noEmit` clean.
- every text token ≥4.5:1 on white + white-on-fill ≥4.5:1; all status-on-light pairs ≥4.7 after the info fix.
- presentation-only — no rule-engine/payroll/security/event/logic change.
