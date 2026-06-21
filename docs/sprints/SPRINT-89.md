# Sprint 89 — Leader reopen on Teamoversigt (payroll-lock Phase 1)

| Field | Value |
|-------|-------|
| **Sprint** | 89 |
| **Status** | closed |
| **Start Date** | 2026-06-21 |
| **End Date** | 2026-06-21 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes (`tsc --noEmit` + `npm run build` clean) |
| **Test Verified** | yes — FE vitest 495/495; .NET tiers unchanged (FE-only) **AND CI GREEN run `27906298705`, all 7 jobs** (incl. build-and-test + e2e) |

## Sprint Goal
**Phase 1 of the "reopen until sent to payroll" decision** (`REFINEMENT-reopen-until-payroll-lock.md`, owner-ruled 2026-06-21): show the **Genåbn** (reopen) control to a **LocalLeader** on the Teamoversigt page, instead of restricting it to LocalHR+. FE-only — the backend reopen Leader+ arm already authorizes it (`ApprovalEndpoints.cs:1607-1630`), and `SkemaPage` already exposes leader reopen (`SkemaPage.tsx:1115`); this makes Teamoversigt consistent and delivers the owner's intent that a leader can reopen an approved month. **Phase 2 (the payroll-export lock + idempotency + atomic refactor) is a separate, later sprint** — until it exists there is no exported-state to gate on, and a leader cannot trigger an export (`/calculate-and-export` is LocalAdmin+, manual, no automation, no FE caller), so Phase 1 carries no payroll risk.

## Refinement & owner rulings
`.claude/refinements/REFINEMENT-reopen-until-payroll-lock.md` — dual-lens Step-4 reviewed (both lenses confirmed the grounding; BLOCKERs on the Phase-2 atomicity mechanism + corrections-manifest absorbed into Phase 2 scope). Owner rulings: OQ-1 lock-at-export-committed; OQ-2 corrections-only post-lock; OQ-3 export anticipatory; OQ-4 Phase 1 now / Phase 2 later; OQ-5 atomic refactor; OQ-6 raw endpoints under lock+idempotency. **This sprint = OQ-4 Phase 1 only.**

## Entropy Scan (Step 0a)
| Check | Result |
|-------|--------|
| check_docs hard checks | CLEAN (through S88) |
| Working tree | CLEAN at S88 tip `016d25a` |

## Plan Review (Step 0b)
Covered by the refinement's Step-4 dual-lens review (the change is the trivial, explicitly-safe Phase-1 slice the review isolated). Step-7a re-verifies the diff.

## Task Log

### TASK-8901 — show Genåbn to leader on Teamoversigt (FE)
| Field | Value |
|-------|-------|
| **ID** | TASK-8901 |
| **Status** | DONE |
| **Agent** | Orchestrator (Small Tasks Exception — single-component FE change + tests) |
| **Components** | `frontend/src/pages/approval/TeamOversigt.tsx`, `__tests__/{TeamOversigt,TeamRowDetail}.test.tsx` |

**Description**: Drop the `isHrPlus` gate on both reopen controls (the row-level `Genåbn` and the detail-footer `Genåbn måned`) so the control shows to any leader who sees the row (the Teamoversigt roster IS the designated-approver set, so backend reopen is always authorized for a visible row). Removed the now-dead `isHrPlus` derivation, the `role`/`hasMinRole` plumbing, and the `TeamRowDetail.isHrPlus` prop. Updated the `isDecided` doc comment. **No status-logic change** (reopen still shows for decided rows; a REJECTED reopen still 409s server-side — a pre-existing dead-button noted as a follow-up, NOT changed here).

**Validation**:
- [x] FE vitest 495/495; the two reopen-visibility tests INVERTED to assert a LocalLeader now SEES Genåbn / Genåbn måned (RED on the old `isHrPlus` gate, which returned null for a leader); a "LocalHR also sees it" test retained.
- [x] `tsc --noEmit` clean; `npm run build` clean.
- [x] No backend/schema/event change; .NET tiers untouched.

**Files Changed**: `frontend/src/pages/approval/TeamOversigt.tsx`, `frontend/src/pages/approval/__tests__/TeamOversigt.test.tsx`, `frontend/src/pages/approval/__tests__/TeamRowDetail.test.tsx`

## External Review (Step 7a)
| Lens | Verdict | Artifact |
|------|---------|----------|
| Internal Reviewer | APPROVE — clean minimal slice; 0 BLOCKER/WARNING; 3 cosmetic NOTEs | `.claude/reviews/SPRINT-89-step7a-reviewer.md` |
| External Codex | APPROVE — "Clean — no findings; scoped tests + tsc pass" | `.claude/reviews/SPRINT-89-step7a-codex.md` |

Both lenses clean (0 BLOCKER). NOTEs absorbed pre-commit: the stale `── Reopen (LocalHR+ only) ──` comment fixed; the pre-existing REJECTED-row dead-button (reopen 409s server-side) disclosed as a follow-up (NOT introduced by this diff — `isDecided` already mapped REJECTED before it). Codex independently ran `tsc` + the scoped tests.

## Test Summary
| Tier | S88 | S89 | Δ |
|------|-----|-----|---|
| Unit | 856 | 856 | 0 (FE-only) |
| Regression | 1015 | 1015 | 0 (FE-only) |
| Smoke | 6 | 6 | 0 |
| Frontend (vitest) | 495 | 495 | 0 (2 reopen tests inverted; no net count change) |
| e2e | 3 | 3 | 0 |

## Sprint Retrospective
**Phase 1 of the "reopen until sent to payroll" decision — shipped as the small, safe slice.** A LocalLeader now sees the Genåbn control on Teamoversigt (the backend already authorized it; SkemaPage already did it). The substance of this work was NOT the code (a ~15-line gate removal) — it was the **refinement that preceded it**: the dual-lens analysis established that "sent to payroll" is not a state that exists in this system today, that the export path has no idempotency (a latent duplicate-pay gap independent of reopen), and that reopen was already inconsistent across pages. That analysis decomposed the owner's instinct into a trivially-safe Phase 1 (this sprint) and a real payroll-export-lock capability (Phase 2, a dedicated sprint, owner-ruled: lock-at-export-committed / corrections-only / atomic refactor / raw-endpoints-under-lock). FE-only; .NET tiers untouched; both Step-7a lenses clean.

**NEXT:** Phase 2 (the payroll-export lock) when prioritised; or the standing Frontend A−→A candidate. Durable: SPRINT-89.md + REFINEMENT-reopen-until-payroll-lock.md.

## Follow-ups
- **Phase 2** (dedicated sprint): the `PeriodExportedToPayroll` event + `approval_periods.payroll_exported_at` projection + atomic export refactor (OQ-5) + idempotency-token wiring + per-(employee,month) export-manifest persistence + the reopen lock gate (`payroll_exported_at IS NULL`) + raw-endpoint coverage (OQ-6) + a SYSTEM_TARGET §H / ADR amendment for post-export immutability.
- **Pre-existing nit:** the reopen control shows for REJECTED rows too (a leader/HR click 409s server-side) — tighten to APPROVED-only when next touching this surface.
