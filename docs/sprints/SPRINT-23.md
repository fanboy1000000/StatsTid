# Sprint 23 — D2.2 ETag/If-Match Propagation Across Admin-Write Surfaces (Phase 4 Hardening, Sibling to S22)

| Field | Value |
|-------|-------|
| **Sprint** | 23 |
| **Status** | placeholder (pre-S22 commit; S23 detail planning happens after S22 ships) |
| **Start Date** | TBD (after S22 close) |
| **End Date** | TBD |
| **Orchestrator Approved** | n/a (placeholder) |
| **Build Verified** | n/a |
| **Test Verified** | n/a |

## Sprint Goal

Propagate the row-version-as-ETag pattern S22 establishes on `local_agreement_profiles` to the four other admin-write surfaces with silent lost-update races today:

1. `agreement_configs` (DRAFT in-place edits + DRAFT→ACTIVE publish + ACTIVE→ARCHIVED clone — three distinct races per Reviewer Step 0b W2)
2. `position_overrides`
3. `wage_type_mappings`
4. `entitlement_configs`

The split from S22 was made at S22 Step 0b plan review (2026-05-03) once the Reviewer surfaced (a) Q3-Q5 coupling on `IEventStore` evolution, (b) `agreement_configs`'s three distinct race conditions, and (c) the divergent ETag-shape between S21's `profile_id`-as-ETag and the new row-version pattern. Bundling the propagation into S22 risked the cycle-cap death-spiral pattern S21 documented; sequencing them gives the row-version pattern a published exemplar before propagation.

## Pre-Sprint Anchoring (drafted alongside SPRINT-22.md, 2026-05-03)

- **S22 prerequisites that S23 consumes**: ADR-018's row-version-as-ETag shape, `If-Match: <version>` HTTP convention, the in-tx outbox enqueue path, audit-action enum extensions where applicable.
- **ADR-019** is the projected ADR number for S23 (covers the propagation pattern + Q10 shared-helper-vs-replication decision).
- **Sprint-start commit**: TBD (= S22 sprint-close commit).

## Open Architectural Questions (to be detailed at S23 sprint start)

These questions are deferred from S22's plan review (cycle 1) and will be resolved in ADR-019 once S22 ships.

1. **Q-23-1 (Q10 from S22)** — Shared `OptimisticConcurrencyHelper` vs per-repo replication of the row-version check. Trade-off: centralization risks coupling to lifecycle divergence (DRAFT/ACTIVE/ARCHIVED on `agreement_configs`); replication risks drift.
2. **Q-23-2 (Q11 from S22)** — `agreement_configs`'s three distinct races:
   - DRAFT in-place edit: standard If-Match on `version`.
   - DRAFT→ACTIVE publish: state-machine transition; needs If-Match on the DRAFT being published.
   - ACTIVE→ARCHIVED clone: the new ACTIVE replaces the old; the OLD ACTIVE's row-version must match what the publisher saw.
   Three distinct token-handling paths; ADR-019 enumerates them.
3. **Q-23-3** — Frontend ETag/If-Match wiring for the four surfaces. S21's `useConfig` hook is the pattern to extend; S23 surfaces each get equivalent handling.
4. **Q-23-4** — Test matrix: minimum 2 scenarios per surface × 4 surfaces = 8 baseline; `agreement_configs`'s three races likely need 4-6 alone, total floor closer to 12-14.

## Scope Boundary (preliminary, refined at S23 sprint start)

### In scope
- ADR-019 covering the propagation pattern + agreement_configs three-race resolution.
- Schema migration: add `version BIGINT NOT NULL DEFAULT 1` to `agreement_configs`, `position_overrides`, `wage_type_mappings`, `entitlement_configs`.
- Repository updates: each repo gains row-version checks on UPDATE; `OptimisticConcurrencyException` thrown on mismatch.
- Endpoint updates: each PUT/POST gains `ETag` header on GET responses; `If-Match` precondition on writes; 412 on mismatch with current state body.
- `agreement_configs` DRAFT/publish/clone three-race handling per Q-23-2.
- Frontend `useConfig`-style hooks for the four surfaces: `useAgreementConfig`, `usePositionOverride`, `useWageTypeMapping`, `useEntitlementConfig` — each carries an ETag and re-fetches on 412.
- Regression coverage per Q-23-4.

### Out of scope
- Touching S22's S22-shipped pattern (read-only consumer).
- Versioned-history for non-dated boundary sources (Phase 4 X-1/X-2/X-3).
- UI/UX polish on optimistic-concurrency error rendering (Phase 5).

## Planning Entrypoint

This sprint detail is drafted post-S22-close. S23 starts with:

1. **Step 0a entropy scan** — completed at S23 sprint start, not now.
2. **Step 0b plan review** — completed at S23 sprint start.
3. **ADR-019** — covers the propagation pattern.
4. **Migration plan** — 4 schema migrations (one per surface).
5. **Task decomposition** — `TASK-23NN` entries.

## References

- [SPRINT-22.md](SPRINT-22.md) — foundation sprint, S23 prerequisite
- [docs/knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md](../knowledge-base/decisions/ADR-017-local-agreement-configuration-as-a-profile.md) — D2.1 ETag pattern reference
- [ROADMAP.md](../../ROADMAP.md) — Phase 4 placement (sibling to S22 / X-1/X-2/X-3)
