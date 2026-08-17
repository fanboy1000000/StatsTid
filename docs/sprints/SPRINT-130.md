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

## Task 2 — SEC-020 Auth:UseDatabase fail-closed (DONE, 2026-08-14)

**The fix.** `Auth:UseDatabase` defaulted to FALSE, so a missing env var routed login to a hardcoded
in-memory credential table (`admin01/"admin"` = GlobalAdmin). Owner ruled **(a) minimal**: flip the
default, keep the in-memory branch behind an explicit opt-in (don't delete it).
- `Program.cs:375` — `GetValue<bool>("Auth:UseDatabase", false)` → `..., true)`. Absent config now
  selects DB/BCrypt → no seeded match → `Unauthorized`; never the hardcoded table.
- The in-memory `else` branch (`AuthEndpoints.cs:77-103`) is UNCHANGED — reachable only by an explicit
  `Auth:UseDatabase=false`.

**Tests (both review-lens warnings absorbed).**
- `InMemoryAuthWebApplicationFactory` (new) injects `Auth:UseDatabase=false` at the SAME early
  host-config layer (`IHostBuilder.ConfigureHostConfiguration` via `CreateHost`) the true-factory uses
  — the TASK-3001 gotcha: `ConfigureAppConfiguration`/`UseSetting`/`WithWebHostBuilder` fire after the
  `:375` read and would silently no-op. `S118LoginSpecRuntimeTests`'s in-memory case repointed to it
  (still asserts 200 + orgId:null + admin01=GlobalAdmin).
- **Mandatory behavioral fail-closed test** `Login_Post_DefaultFactory_FailsClosed_...`: default factory
  (fail-closed DB mode), `admin01/admin` → **401** (genuine RED-before-green — 200 on the old default;
  admin01 is BCrypt("password") so "admin" fails). 401 asserted directly.

**Docs:** `SECURITY.md:15` dual-mode note updated (default DB-backed/fail-closed; in-memory = explicit
opt-in). Register SEC-020 → fixed; ROADMAP #2 done.

**Governance:** refine-requirements + dual-lens Step-4 (0 BLOCKER; 2 complementary WARNINGs — mandatory
behavioral test + host-config-layer mechanism — absorbed) → domain-agent implementation → dual-lens
Step-5a (Codex APPROVED / internal review) → build clean.

## Task 3 — SEC-027 self-minted GlobalAdmin over the shared key (MITIGATED, 2026-08-17)

**The finding.** Any service holding the shared JWT signing key can mint itself a `GlobalAdmin` token
that passes every admin gate — there is no per-service identity. The concrete instance:
`HttpRuleClassificationProvider` minted `role: "GlobalAdmin"` for its service-to-service fetch of
`GET /api/rules/classifications` (which only needs `Authenticated`, and is role-insensitive) — the
codebase's ONLY GlobalAdmin s2s mint.

**The fix — owner-ruled (a) minimal (least-privilege now, structural residual recorded).**
- `HttpRuleClassificationProvider.cs:123` — mint `role: StatsTidRoles.Employee` (the lowest role), NOT
  GlobalAdmin. The distinct service subject (`system:payroll-classification-provider`) is preserved.
  This removes the active over-privilege; grep confirms no s2s GlobalAdmin mint remains in `src/`.
- **NOT marked "fixed" (both lenses, Codex Step-4 BLOCKER).** SEC-027 is a *capability* finding ("no
  per-service identity"), which persists after lowering one token's role. Register status → **MITIGATED**;
  the shared-key/per-service-identity residual is split into a NEW first-class finding **SEC-036** (OPEN,
  ref ADR-007 — which documents the accepted shared-HMAC trade-off, so deferring is defensible; a future
  key rotation can't silently close it). SEC-036 is in the pre-production revisit ledger + ROADMAP.

**Tests (the load-bearing positive proof — both review lenses).**
- `RuleClassificationsLeastPrivilegeAcceptTests` (new, Regression) — hosts the **real RuleEngine
  in-process** via `WebApplicationFactory` (RuleEngine has no DB, so **no container needed**; injects
  `Jwt:*` via `ConfigureHostConfiguration` per the TASK-3001 gotcha). Mints a token faithful to the
  provider's (Employee role, same subject) with the real `JwtTokenService`, GETs `/api/rules/classifications`,
  asserts **200 + non-empty classifications** — proving the privilege reduction did NOT break payroll
  wage-type classification. Negative control: no token → **401** (proves auth is really enforced, so the
  200 is earned). **Ran locally, 2/2 passed** — the accept path is live-proven, not merely deferred to CI.
- `HttpRuleClassificationProviderTests` (Unit) — added a pin: the minted bearer decodes to
  `role == Employee`, `!= GlobalAdmin`, service subject preserved. 7/7 passed locally.

**Governance:** refine-requirements + dual-lens Step-4 (Codex BLOCKER [don't mark fixed] + both-lens
residual-tracking/accept-test warnings absorbed) → domain-agent implementation → dual-lens Step-5a
(Codex APPROVED / internal review) → build clean; accept-test locally green.

## Remaining fix-next tasks
SEC-032, 033, 015, 023, 021, 019, 028/031/034/035 — in the ROADMAP security backlog, in fix-next order.
