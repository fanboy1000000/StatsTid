# ADR-013: Retroactive Corrections Are Single-Period (No Cascade)

| Field | Value |
|-------|-------|
| **ID** | ADR-013 |
| **Status** | approved |
| **Sprint** | S11 |
| **Domains** | Payroll Integration, Rule Engine |
| **Tags** | retroactive, corrections, flex, carryover, payroll, cascade |

## Context

When a retroactive correction changes a period's calculation results (e.g., due to an OK version transition or data fix), the flex balance for that period may change. This flex delta could theoretically cascade forward to all subsequent periods, since each period's flex calculation depends on the previous period's ending balance.

The question: should the system automatically cascade retroactive corrections to all downstream periods?

## Decision

**Retroactive corrections are single-period. No automatic cascade to future periods.**

When a correction produces a flex balance delta:
1. The `CorrectionExportLine` includes a `FlexDelta` field showing the flex balance impact
2. The correction event (`RetroactiveCorrectionRequested`) records the flex delta for audit
3. The system does **not** automatically re-run subsequent periods
4. An administrator can manually trigger corrections for downstream periods if needed

## Rationale

1. **Bounded blast radius**: Automatic cascade could touch dozens of periods per correction, each generating payroll deltas. A single data fix could cascade across months of payroll history.
2. **Determinism preservation (P2)**: Each correction is an explicit, auditable action. Automatic cascades introduce implicit side effects that are harder to trace and verify.
3. **Payroll safety (P6)**: Each correction line maps to payroll adjustments. Uncontrolled cascade risks generating incorrect payroll deltas that are expensive to reverse.
4. **Operational control**: Administrators need to review and approve corrections before they affect payroll. Automatic cascade bypasses this review.
5. **Simplicity**: The correction service remains a single-invocation function. No recursive re-evaluation, no queue of downstream periods, no risk of infinite loops.

## Consequences

- Administrators must manually identify and correct downstream periods when a flex delta is significant
- The `FlexDelta` field on `CorrectionExportLine` provides the signal for whether downstream correction is needed
- Future enhancement: a "cascade assistant" UI could suggest downstream periods affected by a flex delta, but execution remains manual

## Alternatives Considered

1. **Full automatic cascade**: Re-run all subsequent periods automatically. Rejected — unbounded blast radius, payroll safety risk.
2. **Cascade with approval gate**: Queue downstream corrections for admin approval. Rejected — adds significant complexity (correction queue, partial state, approval workflow for cascades) for a rare scenario.
