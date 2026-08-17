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

## Task 4 — SEC-032 Position-Override cross-tenant config write (FIXED, 2026-08-17)

**The finding.** Position-Override config (`position_override_configs`) sets working-hours norms (weekly
norm, flex balances, norm-period weeks) per **agreement + OK-version + position** — it is GLOBAL config:
the table has no org/institution column (only a partial-unique index on the triple `WHERE status='ACTIVE'`),
and resolution (`ConfigResolutionService` → `GetActiveAsync(agreementCode, okVersion, position)`) ignores
org, so one row governs EVERY institution on that agreement+position. Yet the four write endpoints were
floored only at `LocalAdminOrAbove` — so a LocalAdmin at any single institution could rewrite norms all
institutions inherit (a cross-tenant elevation-of-privilege). Every sibling global-config surface
(`AgreementConfigEndpoints`, `EntitlementConfigEndpoints`) is uniformly `GlobalAdminOnly`.

**The fix — owner-ruled OQ-1(a) + OQ-2 (both the recommended paths).** A pure authorization-floor change,
no schema/resolution/event/audit touch:
- The 4 write endpoints in `PositionOverrideEndpoints.cs` raised `LocalAdminOrAbove`→`GlobalAdminOnly`:
  POST create `:164`, PUT update `:294`, deactivate `:394`, activate `:523`.
- The 3 GET reads (`:29/:48/:64`) STAY `LocalAdminOrAbove` (OQ-2 — view-only transparency; reads carry no
  EoP). Note the reads are unfiltered (a LocalAdmin GET returns all global overrides, not just their own) —
  acceptable for global, non-personal config; recorded so the read-floor decision was made on accurate terms.
- **Per-institution org-binding redesign (OQ-1 option b) DECLINED by owner** — that would change what a
  Position-Override *means* (global → per-institution): a schema + resolution + semantics change, a separate
  domain sprint, not a security fix. `GlobalAdminOnly` gates on the role claim (the existing sibling
  mechanism); no scope machinery introduced (that is SEC-036 territory, deferred).

**SEC-034 stays OPEN post-fix.** The PUT re-key state-confusion (SEC-034) lives in the same PUT handler;
raising the floor makes it GlobalAdmin-reachable only — it does NOT close SEC-034. Recorded so "PUT
hardened" isn't misread as "SEC-034 closed."

**Tests (both review lenses — the non-vacuity point was load-bearing).**
- `SEC032PositionOverrideAuthorizationTests` (new) + a `CreateLocalAdminClient` helper on the shared
  `SpecRuntimeTestSupport`. The helper mints an OTHERWISE-VALID LocalAdmin token (same dev key / iss / aud as
  the GlobalAdmin client + a genuine `ORG_ONLY` RoleScope), differing only in role — so a 403 is provably the
  `GlobalAdminOnly` ROLE gate rejecting a valid LocalAdmin, not a hidden auth failure (a 401 would be
  vacuous). Four independent `[Fact]`s assert LocalAdmin→**403** on each write (RED-before-green: 200/201 on
  the old floor); a GlobalAdmin lifecycle test drives create→update→deactivate→activate all green (positive
  control); and a read-floor lock asserts LocalAdmin still gets **200** from GET list, pinning the OQ-2
  decision so a later blanket-tightening can't silently flip the reads.
- These are testcontainer-backed → **CI-deferred** (no Docker on this machine). Locally verified instead: the
  `AuthorizationPolicy` unit suite (17/17), including the meta-test that every `RequireAuthorization("...")`
  literal in `src/` resolves to a registered policy — which proves the four `GlobalAdminOnly` swaps are valid
  registered policies, not typos.

**Governance:** refine-requirements + dual-lens Step-4 (0 BLOCKER; Codex WARNING [assert all four writes] +
internal WARNING [lock the read floor] + NOTEs absorbed) → owner ruled OQ-1(a)/OQ-2 → domain-agent
implementation → dual-lens Step-5a (Codex APPROVED-WITH-WARNINGS [one comment-accuracy nit, fixed] /
internal 0-BLOCKER 0-WARNING) → build clean.

## Task 5 — SEC-033 money-adjacent config-value validation (FIXED app-layer, 2026-08-17)

**The finding.** Config numbers that drive pay + working-time compliance (norm hours, minimum rest, accrual
quotas, flex balances) were accepted with no server-side range/negativity check at the three admin config
write surfaces. Flagship real corruption: `MinimumRestHours = 0` **disables** the daily-rest compliance
check (`RestPeriodRule` tests `restHours < MinimumRestHours`; nothing is `< 0`).

**The fix — owner-ruled OQ-1(a) + OQ-2 (app-layer, lower-bounds + domain-sets only).**
- **SharedKernel relocation (architectural):** the `NormPeriodWeeks` valid set `{1,2,4,8,12}` moved from
  `RuleEngine.Api.NormCheckRule` to `SharedKernel.Models.AgreementRuleConfig.ValidNormPeriodWeeks`, so the
  Backend validators can reuse it WITHOUT a `Backend→RuleEngine` reference (ARCHITECTURE.md hard rule #2 —
  HTTP-only boundary). `NormCheckRule`'s fallback-to-1-week behavior is byte-unchanged (pinned by a test).
- **AgreementConfig:** EXTENDED the existing private `ValidateRequest` (not a parallel validator) —
  `MinimumRestHours>0`, `AnnualNormHours>0`, `MaxDailyHours>0`, and `NormPeriodWeeks ∈ valid set` (was `≥1`).
  Existing `WeeklyNormHours ≤ 50` kept. `WeeklyMaxHoursReferencePeriod` excluded (inert — the rule hardcodes
  48h and never reads the field).
- **Entitlement + Position-Override:** new pure `EntitlementConfigValidator` / `PositionOverrideValidator`
  (public, unit-testable), wired into BOTH POST and PUT before persist, returning **400** for range errors
  (existing **422** statutory guards untouched — deliberate split). Entitlement: `AnnualQuota≥0`,
  `CarryoverMax≥0` (0 allowed), `ResetMonth ∈ 1..12` non-VACATION, `MinAge≥0` if supplied. PositionOverride:
  `null` = "don't override" is skipped (not rejected); supplied values `WeeklyNormHours>0`,
  `NormPeriodWeeks ∈ set`, flex `≥0`; no upper cap (200 is a real seeded flex value).

**Deferred (owner-ruled, recorded in the register's pre-production ledger — NOT dropped):**
- **DB CHECK backstop** (OQ-1 a): app-layer closes the user-reachable hole; the DB-level guard on the 3
  tables (+ the SEC-037 surface) — the house pattern `entitlement_configs` already uses — is deferred.
- **Fat-finger upper ceilings** (OQ-2): shipped lower-bounds + domain-sets only; upper ceilings need domain
  **agreement-truth** (Phase-B-class analysis), so deferred — owner: *"note that we have it as a follow up…
  it requires agreement truth and deep analysis."*
- **SEC-037 (new adjacent finding):** `LocalAgreementProfileMigrator` imports parsed legacy values into
  `local_agreement_profiles` (a different table) unvalidated — recorded OPEN in the register; out of scope.

**Tests.** 53 new **unit** tests ran locally GREEN (validators + the relocated constant + NormCheckRule
fallback-preservation) — full `Tests.Unit` suite 914 passing. Endpoint-wiring tests
(`SEC033AdminConfigValidationTests`) are testcontainer-backed → CI-deferred. **Test-debt follow-up (Codex
Step-5a WARNING, accepted):** the endpoint tests are POST-only + omit negative `CarryoverMax`/`FlexCarryoverMax`
endpoint cases — the validator logic is unit-proven and both lenses confirmed the PUT wiring is pre-persist,
so the gap is CI-regression protection for the PUT wiring only; add PUT + those negatives to the endpoint
suite in a later pass.

**Governance:** refine-requirements + dual-lens Step-4 (found **5 BLOCKERs** across two rev cycles — assumption
wrong, existing validator misstated, field inventory incomplete, illegal anchor dependency — all absorbed;
surfaced SEC-037) → owner ruling OQ-1(a)/OQ-2 → domain-agent implementation → dual-lens Step-5a (Codex
APPROVED-WITH-WARNINGS [error-shape matches existing `{error}` convention — accepted; endpoint test-debt —
recorded] / internal 0-BLOCKER 0-WARNING) → build clean; 53 unit tests green.

## Remaining fix-next tasks
SEC-015, 023, 021, 019, 028/031/034/035 — in the ROADMAP security backlog, in fix-next order.
(Still OPEN and tracked: SEC-034 [task-4 note], SEC-036 [ledger], SEC-037 [new, register], and the two
SEC-033 deferrals [ledger].)
