# SPRINT-130 — Security remediation (the S129 fix-next backlog)

| Field | Value |
|-------|-------|
| **Type** | Remediation (code) — fixes the S129-swept findings, owner-approved fix-next order |
| **Build Verified** | ✅ `dotnet build StatsTid.sln` clean (0 errors) after each task |
| **Test Verified** | Regression suite is testcontainer-backed → runs in CI (this machine has no container runtime); new tests written to the harness. Baseline moves by the added tests. |
| **Source** | `docs/operations/security-finding-register.md` (SEC-NNN) + `ROADMAP.md` security backlog |

Fix-next order (owner-approved 2026-08-14): **SEC-009** → SEC-020 → SEC-027 → SEC-032 → SEC-033 →
SEC-015 → SEC-023 → SEC-021 → SEC-019 → SEC-028/031/034/035.

## Task 1 — SEC-009 self-approval / segregation-of-duties guard (DONE, 2026-08-14)

**The fix (keystone).** An HR/LocalAdmin/GlobalAdmin whose org-scope covered their own home org could
approve / reject / (leader-)reopen their OWN monthly period via the org-scope authorization leg, which
lacked the `actor != employee` self-guard the unit-leader leg enforces — mis-audited as
`ORG_SCOPE_FALLBACK`. Owner ruled: block self on manager DECISIONS (approve / reject / reopen-of-
`APPROVED`), no exemption; ALLOW the pre-approval self-undo (reopen of one's own `EMPLOYEE_APPROVED`),
`employee-approve`, `send`. (Matches Økonomistyrelsen funktionsadskillelse.)

**Structural, not per-instance (RES-003 item 2).**
- Choke point: a fail-closed self-guard as the first statement of the terminal
  `DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync` (all overloads funnel here) →
  edge / unit-leader / vikar legs self-exclude by default; the per-path SQL exclusions become
  defence-in-depth.
- The org-scope/HR-fallback leg (which bypasses the predicate — SEC-009's exact path): the shared
  `ApprovalSelfGuard.IsSelf` (`Ordinal` id-equality + null short-circuit) at the three manager-decision
  endpoints, placed BEFORE the status check (self on an ineligible status → 403, not a state-leaking
  409).
- Reopen scoped to the `APPROVED` source state in the Leader arm only (the employee arm + the
  `EMPLOYEE_APPROVED` self-undo untouched).
- Denial emits a structured WARNING log (no `audit_log`/outbox row — a denied action is not a domain
  event).

**Files:** `src/Infrastructure/StatsTid.Infrastructure/DesignatedApproverAuthorizer.cs`,
`src/Backend/StatsTid.Backend.Api/Endpoints/Helpers/ApprovalSelfGuard.cs` (new),
`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs`,
`tests/StatsTid.Tests.Regression/Approval/SelfApprovalGuardTests.cs` (new).

**Tests (RES-003 item 1 — the audit as a matrix):** per-leg self-pair + other-actor differential (the
org-scope self case is a genuine 200→403); positive-self-match; guard-ordering; the reopen split;
no-over-block (send/employee-approve); and a **direct choke-point contract pin**
(`IsEffectiveApproverOrUnitLeaderAsync(x, x) == false` while `x` holds real authority over another).

**Governance.** `refine-requirements` gate → dual-lens Step-4 (Codex BLOCKER [choke point] + internal
WARNING [reopen over-block] absorbed; Codex cycle-2 APPROVED) → delegated to the Backend/Security
domain agent → dual-lens Step-5a implementation review (Codex APPROVED-WITH-WARNINGS / internal
0-BLOCKER; the one warning — pin the choke point directly — closed by the added contract test) →
build clean. **RES-003 CLOSED.** SEC-009 register status → fixed.

## Remaining fix-next tasks
SEC-020, 027, 032, 033, 015, 023, 021, 019, 028/031/034/035 — in the ROADMAP security backlog, to be
picked up in fix-next order.
