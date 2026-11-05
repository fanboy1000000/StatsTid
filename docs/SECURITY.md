# StatsTid Security Reference

> JWT patterns, RBAC model, scope validation, and known gotchas for agent context.

> **Finding register:** the durable, cross-session list of security weaknesses — the revocation-residual
> map below plus the sprint-log holes, deployment-config class, and swept-unruled findings — is
> indexed in [docs/operations/security-finding-register.md](operations/security-finding-register.md)
> (SEC-series, "revisit not shield"). This doc remains the source of truth for the auth/RBAC/scope
> model; the register routes across all of it and carries the S129-sweep adjudications.

## Authentication

- **Algorithm**: JWT HMAC-SHA256, shared secret across all 5 API services via Docker environment variables
- **Token configuration**: `MapInboundClaims = false`, `NameClaimType = "sub"`, `RoleClaimType = "role"`
- **Dual-mode login (fail-closed default, SEC-020 / S130)**: `Auth:UseDatabase` selects DB-backed BCrypt
  verification (the **default = `true`**, fail-closed) vs the in-memory hardcoded dev credentials. The
  in-memory table is now an **explicit opt-in** (`Auth:UseDatabase=false`) — a missing/absent config can
  no longer serve the hardcoded `admin01/admin = GlobalAdmin`. (Was: default `false`, so an unset env
  var silently exposed the dev credentials — the SEC-020 fail-open, fixed S130.)
- **Reference**: [ADR-007](knowledge-base/decisions/ADR-007-jwt-auth-rbac-correlation-ids.md)

## Custom JWT Claims

All claim constants are defined in `src/SharedKernel/StatsTid.SharedKernel/Security/StatsTidClaims.cs`.

| Constant | Claim Name | Description |
|----------|------------|-------------|
| `EmployeeId` | `employee_id` | Unique employee identifier |
| `Role` | `role` | Primary role string (e.g. `GlobalAdmin`, `Employee`) |
| `AgreementCode` | `agreement_code` | Collective agreement code (e.g. `AC`, `HK`, `PROSA`) |
| `OrgId` | `org_id` | Primary organization unit ID |
| `Scopes` | `scopes` | JSON array of `RoleScope` objects (see below) |

## RBAC Model

### 5 Roles

Defined in `src/SharedKernel/StatsTid.SharedKernel/Security/StatsTidRoles.cs`.

| Role | Hierarchy Level | Description |
|------|:-:|-------------|
| `GlobalAdmin` | 1 | System-wide administration |
| `LocalAdmin` | 2 | Organization unit administration |
| `LocalHR` | 3 | HR functions within an organization unit |
| `LocalLeader` | 4 | Team/department leader |
| `Employee` | 5 | Standard employee |

**`GetHierarchyLevel(role)`** returns the numeric level (lower = higher privilege). Unknown roles return `int.MaxValue`.

**`IsAtLeast(actualRole, requiredRole)`** returns `true` when `GetHierarchyLevel(actualRole) <= GetHierarchyLevel(requiredRole)`. This is the canonical backend role comparison.

### Legacy Role Mappings

For backward compatibility with existing events and audit logs:

| Legacy Role | Maps to Level |
|-------------|:-:|
| `Admin` | 1 |
| `Manager` | 4 |
| `ReadOnly` | 6 |

These aliases remain in `StatsTidRoles` and will be removed once all consumers migrate.

### Frontend Role Check

Defined in `frontend/src/lib/roles.ts`:

- **`ROLE_LEVELS`**: `Record<string, number>` mapping role names to hierarchy levels (same values as backend)
- **`hasMinRole(userRole, minRole)`**: Returns `true` when `ROLE_LEVELS[userRole] <= ROLE_LEVELS[minRole]`. Returns `false` for `null` or unknown roles (defaults to 99 for unknown user roles, 0 for unknown min roles).
- **`RequireRole` guard component**: Wraps routes, renders `ForbiddenPage` if `hasMinRole` fails.

## Scope Validation

### RoleScope Structure

Defined in `src/SharedKernel/StatsTid.SharedKernel/Security/RoleScope.cs`:

```csharp
public sealed record RoleScope(string Role, string? OrgId, string ScopeType)
```

| Field | Values | Description |
|-------|--------|-------------|
| `Role` | Any `StatsTidRoles` constant | The role granted in this scope |
| `OrgId` | Organization ID or `null` | The org unit this scope applies to (`null` for GLOBAL) |
| `ScopeType` | `GLOBAL`, `ORG_ONLY` | Breadth of the scope. **S93/ADR-035 slice 2: `ORG_AND_DESCENDANTS` REMOVED** — role-scope coverage is now EXACT Organisation-set membership (the union of a user's `ORG_ONLY` rows; no subtree inheritance). A non-GLOBAL (`ORG_ONLY`) grant's `OrgId` must be an `ORGANISATION` (a MAO is rejected at grant — OQ1). |

Embedded in JWT as the `scopes` claim. Reference: [ADR-009](knowledge-base/decisions/ADR-009-scope-embedded-jwt.md), [ADR-035](knowledge-base/decisions/ADR-035-flat-authority-model.md) (flat role-scope).

### ActorContext Extraction

Defined in `src/Auth/StatsTid.Auth/ActorContext.cs` (moved from `src/Infrastructure/StatsTid.Infrastructure/Security/` in commit b4fc670; see ARCHITECTURE.md "Auth" section).

```csharp
public sealed record ActorContext(
    string? ActorId, string? ActorRole, Guid CorrelationId,
    string? OrgId = null, RoleScope[]? Scopes = null);
```

Extraction logic (`GetActorContext` extension on `HttpContext`):

1. **ActorId**: Reads from `sub`, then `ClaimTypes.NameIdentifier`, then `employee_id` (fallback chain).
2. **ActorRole**: Reads from `role` claim.
3. **OrgId**: Reads from `org_id` claim.
4. **CorrelationId**: Reads from `HttpContext.Items` (set by `CorrelationIdMiddleware`), falls back to `Guid.NewGuid()`.
5. **Scopes**: Uses `FindAll(StatsTidClaims.Scopes)` (NOT `FindFirst`) to collect all scope claims. Parses both JSON arrays `[{...}]` and individual JSON objects `{...}` because JWT bearer middleware splits `JsonArray` claims into separate claim entries.

### OrgScopeValidator

Defined in `src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs`.

Service-layer enforcement called explicitly from API endpoints. Validates that the actor's `RoleScope` entries grant access to the target employee's organization unit. **S93/ADR-035 slice 2:** access is now **exact Organisation-set membership** — `CoversOrg` is GLOBAL + exact-path-equality (the `ORG_AND_DESCENDANTS` prefix branch is removed), and `GetAccessibleOrgsAsync` returns the assigned set with NO `GetDescendantsAsync` subtree expansion. The materialized path (`/MIN01/STY02/`) + `text_pattern_ops` index + `GetDescendantsAsync` are now **dormant for authority** (formal retirement with the tree machinery in S95). References: [ADR-035](knowledge-base/decisions/ADR-035-flat-authority-model.md), [ADR-008](knowledge-base/decisions/ADR-008-materialized-path-org-hierarchy.md).

**The departed-self clause (S147, QUAL-230 T5a; owner OQ-2 and Q-C = (b)).** A person who has left cannot act on their own record with a login that is still valid, whatever their role. "Left" means deactivated, or past the last working day in Copenhagen; the last day itself still counts as employed. The rule is one predicate, `DepartedSubject` (`src/SharedKernel/StatsTid.SharedKernel/Security/DepartedSubject.cs`):
- `IsDeparted` tests whether the person has left.
- `IsSelf` compares ids Ordinal; an empty or null id never matches.
- `DenyReason` holds no date and no id (ADR-040 D7).

**Where the predicate runs:**
- In the identity arm of both validator overloads (`OrgScopeValidator.cs:115`, `:286`). On the leader-inclusive overload it runs *before* the HR gate, so an HR-tier leaver gets this reason and not the HR one. The validator reads the date from the injected `TimeProvider` (`CopenhagenBusinessDate.Today`), at most once per call. *(S147 Step 7a, TASK-14725: it reads it for two clauses, this departed-self clause and the rule that treats a subject past the manager grace by date as switched off (see "HR only from day 6" below). So it now reads the date for every subject with a recorded end date. A request that straddles Copenhagen midnight can see two dates, the PAT-028 residual in ADR-040's amendment; the outcome then equals a request wholly on one of the two days.)*
- Through that check it covers the 32 write routes that use the shared check, in three hosts.
- Thirteen inline guards cover the write routes the validator does not reach, plus a derived guard on `/remove`.
- **The S147 census (TASK-14703; Step 0b P-1, the subject-key rule).** A write route is guarded when the departed person's own id is the *subject key* of a row it inserts, updates or closes: `users.user_id`, `role_assignments.user_id`, `unit_leaders.user_id`, `reporting_lines.employee_id` or `manager_vikar.absent_approver_id`. An id that only *holds* authority over someone else (a manager, an acting manager, a stand-in, `/remove`'s replacement) is left to TASK-14712's acting-user rule, which makes a departed holder inert from day 1. Of the 84 write routes in the three hosts:
  - **32** reach the employee through the shared check;
  - **19** are guarded another way: 13 inline guards, 2 by the self-service floor below (row-preferences, compensation-choice), and 4 by retirement;
  - **33** are classified out: 31 with no employee subject, 1 that cannot address an existing person, and the anonymous login.

  Of the four retirements, TASK-14710 retired the two project selection routes (`POST`/`DELETE /api/projects/{orgId}/select/{projectId}`). The other two are the raw Payroll `/export` and `/export-period`, which TASK-14716 retired (SEC-054; merged `169e9980`, its Docker facts CI-verified at CI-W4b, run `41825042200`). Until that merge they were `GlobalAdminOnly` routes with no departed-self check, so 17 of the 19 were closed; now all 19 are. The census's counts stay as taken; the live inventory is four routes smaller, 80 write routes in the three hosts (84 − 4 = 32 + 15 + 33; by the census command at `94bd32be`: Backend 76, Payroll 3, Orchestrator 1). The owner kept this door-by-door design rather than one check at each service's front door (Q-C = (b)).
- **The self-service write floor (TASK-14710; `Endpoints/Helpers/DepartedSubjectWriteFloor.cs`).** It covers the own-record writes an Employee-role token reaches through the validator's own-data branch, where the identity clause is never consulted. It refuses when the subject has left AND (the actor is the subject OR the actor is below LocalHR). It runs at seven sites:
  - Skema save, advisory and in-lock;
  - time entry, advisory and in-lock;
  - send / employee-approve;
  - reopen's Employee arm;
  - row-preferences;
  - compensation-choice.

  One message per clause. The departed-self clause answers with `DepartedSubject.DenyReason`, the same as the validator. A below-LocalHR actor on another departed subject gets ADR-040 D3's "Writes for a deactivated employee require LocalHR or above". When both apply, the self message wins. The log names the clause.

**Scope and consequences:**
- Acting on *other* people stays governed by the existing rules, SEC-003's accepted residual. A departed person's edge-based approver authority ends on day 1 (TASK-14712). Their org-scope authority during the grace is SEC-057. A stand-in appointed before the manager left keeps approving until its end date, or until HR revokes it or removes the manager (owner Q-B = (c)); a stand-in who has herself left grants nothing, and the month goes back to the manager she covered (`ReportingLineRepository.cs:1290-1306`).
- **HR only from day 6 (TASK-14713, QUAL-230 T5b).** From day 6, decided by date (S147 Step 7a, TASK-14725: whether or not Step A has switched the leaver off yet; Step A's switch-off stays as a second guard), five surfaces admit the leaver through the terminated-inclusive validator (`ValidateEmployeeAccessIncludingTerminatedAsync`), which treats a subject past the grace by date as switched off (`OrgScopeValidator.cs:349-350`):
  - approve, reject and reopen's leader arm, at the LocalHR floor;
  - the Skema month view, HR tier only (the person's own read still answers 404);
  - payroll `/calculate-and-export`, at the LocalAdmin floor.

  The manager's own edge and any leader-scope token are refused, from Copenhagen midnight on day 6 by date. The shared approver predicate refuses every manager path (edge, acting manager, stand-in, unit leader; `DesignatedApproverAuthorizer.cs:401`), and the validator admits only a LocalHR-or-above scope (S147 Step 7a, SEC-058, CLOSED). HR keeps every route it uses to finish the month; on the active-only routes HR gets from day 6 the answer it gets after the switch-off, "Target employee not found". On every one of these surfaces an **employed-month bound runs after the gate**. A month without one employed day gets a date-free 422 (`month_outside_employment_period`), so a refused actor sees the same refusal for every month and no date leaks. Approve, reject and reopen lock through a terminated-inclusive tree-lock overload that takes the same key as the shared helper, and they audit against the subject's own organisation. A below-HR actor on a switched-off subject now reads "Terminated-employee access requires LocalHR or above", as S138's send does. That reveals the person has left, but not when.
- Reads through the shared validator are refused for a departed self too. This is intended, and neither refusal below is a regression:
  - Payroll `calculate-and-export` is refused for a departed self.
  - Orchestrator `/execute` is refused for a departed self holding a non-Employee role. An Employee-only leaver's token still reaches `/execute` through the own-data branch. That is acceptable: it computes, and changes nothing on the person's own record.
- An actor whose only role is Employee is answered by the own-data branch first, so the identity clause is not reached. The self-service write floor covers that case.
- The Payroll and Orchestrator hosts register `TimeProvider.System` from S147. Their test hosts must override it with a fixed provider (PAT-008).

**The active-but-departed window (recorded 2026-10-30, TASK-14719 review NOTE 5; updated after TASK-14710).** A leaver is switched to inactive only by the leaver job (`SettlementCloseService`, polling every 5 minutes, and once at host start).
- **From TASK-14710 the switch-off fires on day 6** (`ManagerGraceCalendarDays = 5`). So from Copenhagen midnight after the last working day until then, the person is still `is_active` but already departed. That is D+1 to D+5 by design (the manager's grace), plus any poll lag. *(Since S147 Step 7a (TASK-14725), the poll lag matters only for the leaver's own exposure, below. The manager's authority and every leader-tier org-scope check end at day 6 by date, whatever the flag says (SEC-058).)*
- **Login is refused from day 1** under database auth: the end date has passed, so the same generic 401 as a wrong password, logged `employment_ended`.
- **The remaining exposure** is a token minted on the last working day, valid for at most its 480-minute lifetime (SEC-003).
- **What that token can do on the person's own record:**
  - writes are refused by the identity clause and the self-service write floor;
  - reads through the shared validator are refused;
  - an Employee-only token's own-data reads are allowed (K-13).
- **The controls on the person's own record** are the identity clause, the floor and the login refusal together. That is why each tests the end date and not only the active flag.

**When adding a write route keyed on the caller's own id:** route it through the shared validator, or add the inline guard before the route's first write. If an Employee-role token can reach it through the own-data branch, also call `DepartedSubjectWriteFloor`.

### The mixed-role scope-floor invariant (S76 / B1)

**CRITICAL invariant:** an ADMIN scope-admission check must floor *per scope* by role — admission may only be granted by a scope whose `Role` meets the endpoint's policy floor. The original `OrgScopeValidator` methods (`ValidateOrgAccessAsync`, `ValidateEmployeeAccessAsync`, `GetAccessibleOrgsAsync`, `ValidateEmployeeAccessIncludingTerminatedAsync`) and `HasGlobalScope` admitted via ANY covering scope **regardless of that scope's role**. A mixed-role actor (e.g. `LocalAdmin@STY-A` who also holds a non-admin scope in STY-B) therefore passed an admin gate for STY-B via the *non-admin* scope → a cross-styrelse data/write leak.

**The rule:** every admin (LocalAdminOrAbove / HROrAbove / GlobalAdmin) read or write must route through a role-FLOORED scope check, at the floor matching its `RequireAuthorization` policy (LocalAdminOrAbove → `LocalAdmin`; HROrAbove → `LocalHR`; GlobalAdmin gates require a GLOBAL scope at `GlobalAdmin`). The floored variants take a `roleFloor` and skip scopes below it (`!IsAtLeast(scope.Role, roleFloor) → continue`). The no-floor overloads remain for genuinely non-admin callers (Employee/Leader reads admit via any covering scope by design). **Exception, deliberately null-floored:** the leader year-overview reads (`BalanceEndpoints`) — a Leader-in-scope SHOULD see their active report's overview (ADR-027 R9c). *(A leaver past the grace is not an active report. From day 6 by date the validator treats them as switched off, and a switched-off target is admitted only through a LocalHR-or-above scope (`OrgScopeValidator.cs:385`), so a leader no longer sees a leaver's year overview; HR still does. S147 Step 7a, TASK-14725.)*

**Lesson:** a scoped/structural review trace cannot enumerate every scope-admission caller — only an external whole-codebase trace found the full set (config/project/global/audit/settlement/end-date writes across 3 review cycles). When changing a scope-admission surface, sweep EVERY caller, not just the diff ([[review-lens-complementarity]]).

### Medarbejder-administration opened to LocalHR + the secondary-principal binding (S91)

**Decision (owner-approved, S91):** the "Medarbejder administration" reporting-line tree page and its ~12 backend endpoints were lowered from LocalAdmin → LocalHR (policy `LocalAdminOrAbove` → `HROrAbove`, in-handler floor `StatsTidRoles.LocalAdmin` → `StatsTidRoles.LocalHR`) so HR keeps employee management after the old `UserManagement` ("Medarbejdere", LocalHR) page was removed. A LocalHR actor can now reassign approvers, manage vikariering, delete employees (with reassignment), and set tree enforcement — **bounded to their own org subtree** (the per-scope floor invariant above is preserved on every lowered endpoint; cross-styrelse stays blocked; the cross-org `PUT admin/users/{userId}` transfer floors BOTH old + new org).

**Accepted characteristic (S91 Step-7a, Codex P1 ×4 — owner-accepted, documented, follow-up tracked):** the lowered tree-WRITE endpoints floor-check the PRIMARY target (the employee's org) but bind the SECONDARY principal (the assigned manager / create-approver / replacement-approver / vikar) only via `ValidateSameTreeAsync` (structural same-MINISTRY/STYRELSE tree), NOT a floored scope check against the actor's subtree. So a LocalHR scoped to a SUB-org can name a secondary principal in a SIBLING sub-org of the SAME styrelse (an intra-styrelse lateral assignment) — outside their own subtree but inside the styrelse. This is the **pre-existing LocalAdmin model** (unchanged code; S91 only admits HR to it). It is accepted because containment stays intra-styrelse and a naive tightening would break legitimate UP-TREE manager assignment (a manager is an ancestor, outside an `ORG_AND_DESCENDANTS` sub-org scope). **FOLLOW-UP:** a dedicated sprint to scope-check the secondary principal for BOTH tiers with an "ancestors + own-subtree, not lateral siblings" rule.

### Segregation of duties: who may sign what (S130, S147)

**Why.** A month passes payroll on two signatures: the employee's, given when the month is sent (or by HR on the employee's behalf), and the approver's. Segregation of duties means those two signatures come from two different people, and nobody decides their own case. Both state tenders analysed in 2026-10 require it (EM A1 (e), VD 35).

**The rules, as built:**
1. **Nobody approves or rejects their own month, or reopens it once approved** (SEC-009, S130). `ApprovalSelfGuard` checks this at approve, reject and reopen (`ApprovalEndpoints.cs:357`, `:653`, `:1891`), and the choke point sits in `IsEffectiveApproverOrUnitLeaderAsync`.
2. **Whoever sent a month can neither approve nor reject it** (SEC-053, S147; owner OQ-1 = (a): strict, no exception). The sender is `employee_approved_by`, or `submitted_by` on a legacy row (`ApprovalSenderGuard.SenderOf`). The rule is checked three times:
   - at the door, as a 403 with a structured WARNING (`ApprovalEndpoints.cs:375`, `:666`, re-checked on the conflict paths `:538`, `:771`);
   - inside the transaction that writes the decision, as a predicate on the conditional UPDATE (`ApprovalPeriodRepository.cs:1939`, `COALESCE(employee_approved_by, submitted_by) IS DISTINCT FROM @actorId`), so a race cannot slip past the door check;
   - at payroll, where an approval turns into money (`PayrollExportService.cs:544-553`, inside the B2 re-assert `AssertPeriodApprovedForUpdateAsync`, `:487-557`, at `94bd32be`; a null sender passes).

   **Reopen is not guarded**, because it signs nothing and clears both stamps. The demo loader approves as a second identity.
3. **A leader cannot decide their own overtime** (SEC-056; owner Q-A = (a); S147 TASK-14705, merged `b2a65fb8`). A leader can neither approve nor reject an overtime pre-approval that names themselves, and cannot compensate their own overtime balance (`OvertimeEndpoints.cs:387`, `:483`, `:566`, through `ApprovalSelfGuard.IsSelf`). The refusal is a 403 with a date-free reason and a WARNING under its own log category. It is checked before the scope check and the status check, so it also answers first for a departed leader (the Orchestrator's ruling (a)), and it does not reveal a row's state. *Creating* a pre-approval that names oneself stays allowed: a request is not a decision, and someone else decides it.

**Outside these rules (recorded, not hidden):**
- **No longer outside (S147):** the raw GlobalAdmin exports (`/api/payroll/export`, `/export-period`), which skipped the approval check, were retired by TASK-14716 (SEC-054, CLOSED). `ExportAsync` now refuses an export that names no approval period (`PayrollExportService.cs:228-231`), so rule 2's payroll checkpoint runs on every export made since TASK-14716, on the exported employee's own whole-month approval (`:524-553`). A correction is not an export: it takes its own path (the next bullet) and needs only an existing export record (`RetroactiveCorrectionService.cs:211-223`), so a record the raw routes wrote before S147, with no approved month behind it, can still be corrected until the database is reseeded. A second caller of `ExportAsync` must not copy its conflict exception's message into a response: the period-identity message names the approval's employee and dates (R-G6). `/calculate-and-export` copies it (`Payroll/Program.cs:501`), but that message cannot occur there, because the route reads the approval by the request's own employee and whole month (`:296`, `:310-311`).
- Retroactive corrections take their own path (`RetroactiveCorrectionService.cs`).
- Acting on someone else's record is governed by the scope rules above.

### Flat approval authority (S94 / ADR-035 slice 3)

**The approval predicate is now `CanApprove(actor, emp) = IsEffectiveDesignatedApprover (edge) OR HasHrAdminScopeOverEmpOrg(actor, emp)`.** The unfloored leader-by-org-scope approval branch is RETIRED: approve / reject / the reopen LEADER arm gate the org-scope fallback through `ValidateEmployeeAccessAsync(actor, period.EmployeeId, StatsTidRoles.LocalHR, ct)` — HR/Admin (LocalHR/LocalAdmin/Global) scope over the employee's CURRENT Organisation. A non-designated in-scope **LEADER can no longer approve** (must hold the reporting edge); HR/Admin is the pure-OR fallback; an **orphan** employee (no manager edge) is approved by in-scope HR/Admin. The S78 in-lock edge re-eval is preserved (it keys on the now-floored `orgScopeAllowed`).

**REQUIRED-mode enforcement is RETIRED (ADR-027 D11 retired).** The `reporting_line_tree_settings` table + the GET/PUT `/settings` admin endpoints + the FE PREFERRED↔REQUIRED toggle + the 428 `ORG_SCOPE_FALLBACK` confirm gate + `confirmFallback` + `explicit_fallback_confirmation` (column/model/events) are all deleted. `approval_method` + `designated_approver_id` are RETAINED for audit (`ORG_SCOPE_FALLBACK` remains a valid classification). **NOTE:** the S91 ("set tree enforcement") and S78 ("re-classifies the REQUIRED-mode `confirmFallback` gate") text below is HISTORICAL — that enforcement gate was retired in S94; the in-lock edge re-eval discipline itself stays. The same-tree / `tree_root` machinery (referenced below) was RETIRED in **S95**: post-S92 `tree_root == primary_org` (the walk always returns the user's own org), so `ResolveTreeRootOrgIdAsync` was deleted, `ValidateSameTreeAsync` → `ValidateSameOrganisationAsync` (direct `primary_org` equality), and the in-lock advisory now derives its key from `primary_org` directly — the **lock DOMAIN is unchanged (the Organisation)**, so every serialization guarantee below is preserved (the advisory prefix string + the denormalized-Organisation columns are kept; the per-subject lock-matrix premise was superseded). A new Organisation-home guard (user create/transfer reject a non-`ORGANISATION` `primary_org`) makes the invariant by-construction. **The flat-authority reform (S92–S95) is COMPLETE.** **S96 cosmetic rename:** the advisory prefix is now `reporting-org-` and the denormalized columns are `organisation_id` (`TreeRootOrgId` model/event fields → `OrganisationId`) — no behaviour change. S96 also restored the inactive-org-home `is_active` fail-closed in `ValidateSameOrganisationAsync`/`DeriveEmployeeTreeRootInTxAsync` (repository-level defense-in-depth; the assign endpoint already 403'd via the org-scope gate's pre-existing `is_active` filter).

**S97 Enhed = ZERO authority (ADR-036).** **[QUAL-011: PARTIALLY SUPERSEDED by ADR-038 (S102–S105) — see the ADR-038 authority note after the S110 paragraph below. The `enheder`/`user_enheder` tables named here were REPLACED by `units`/`unit_leaders`; the SCOPE-path claim still holds, but a secondary/peer unit-leader is now an APPROVAL exception per D4/S105.]** The structured `enheder`/`user_enheder` metadata is PURE DISPLAY — it appears in NO authority path (`OrgScopeValidator`, `RoleScope.CoversOrg`, `DesignatedApproverAuthorizer.CanApprove`/`IsEffectiveDesignatedApprover`, `ValidateEmployeeAccessAsync`). A shared enhed grants no cross-user visibility/approval/scope; the roster/search enhed aggregation + the `?enhedId` filter sit INSIDE the users-first `accessibleOrgs` bound (keyed by `enhed_id`, not name) → no cross-org leak via a same-name enhed. Every enhed endpoint is `ValidateOrgAccessAsync(LocalHR)`-floored over the correct Organisation (org-scope-contained; the managed-list's selectable orgs = `GetAccessibleOrgsAsync(actor, LocalHR)`). Set-tags re-floors against the `FOR UPDATE`-locked org (the transfer-TOCTOU, Step-7a BLOCKER); the same-Organisation tag invariant is command-layer-enforced (a transfer CLEARS tags in-tx). Pinned by RED tests (cross-org tag reject + shared-enhed-no-CanApprove + the out-of-scope-HR 403 + the scope-leak).

**S100 — the Enhed HIERARCHY is STILL zero authority (ADR-036 S100 amendment).** Enhed gained a `parent_enhed_id` tree (within each Organisation) + a derived level, but the invariant is UNCHANGED and both-lens-verified clean BY CONSTRUCTION: `parent_enhed_id` enters NO authority path. A shared ANCESTOR/DESCENDANT enhed grants NOTHING — pinned by a RED test (`S100EnhedHierarchyTests`: A tagged to a parent enhed, B to its child → `IsEffectiveDesignatedApprover(A,B)` false AND `ValidateEmployeeAccessAsync(A,B,LocalHR)` denies). The tree-mutating endpoints (create-child / the new `PUT …/move` / delete) stay `ValidateOrgAccessAsync(LocalHR)`-floored + serialize on a per-Organisation advisory lock `pg_advisory_xact_lock(hashtext('enhed-org-' || organisation_id))` (distinct from the S95 `reporting-org-`; the cycle CTE runs under the held lock). The move is `If-Match`-versioned; `organisation_id` is immutable on move. Delete re-parents children up via per-child `EnhedMoved` events (the `user_enheder` rows stay — S97 projection-filter unchanged).

**S98 org-structure lifecycle (ADR-037).** The NEW consequential structural ops — org **soft-delete** (`DELETE /api/admin/organizations/{id}`) + **move** (`PUT …/{id}/move`) — are **GlobalAdmin-floored** (`HasGlobalScope`, the same gate as MAO-create; a LocalAdmin/LocalHR hits the in-handler 403); the existing Organisation create/rename stay LocalAdmin+. The aggregated tree (`GET …/tree`) is visibility-bounded via `GetAccessibleOrgsAsync` (the global aggregates are filtered in the C# forest assembly — no cross-org count/enhed leak; a stale `ORG_ONLY@MAO` token → empty forest, fail-closed). **The soft-delete enforcement point for the home guards is `OrganizationRepository.GetByIdAsync`'s `is_active=TRUE` filter** — a soft-deleted org returns null → create/transfer reject it (no separate guard; observed as a 403 from the org-scope gate, which resolves via the same filtered read ahead of the home-guard's 400). The soft-delete + move serialize via in-tx `SELECT … FOR UPDATE` (no `version` column → no If-Match; GlobalAdmin low-contention). **Residuals (GlobalAdmin-initiated, sub-second, recoverable):** the create/transfer-vs-delete window (a user read active pre-commit, inserted post-delete-commit) — accepted+documented; the move-vs-delete-of-new-parent race — CLOSED (the new parent MAO is locked in-tx). **Invariant dependency:** the org-move's no-cascade path-rewrite assumes Organisations are LEAVES (enforced by the create/move gates, NOT a DB constraint); a future org-nesting change MUST grow a `materialized_path` cascade in `ReparentAsync`.

**S110 — the MAO-delete-vs-child-orgs guard + the create-side TOCTOU close.** The S98 MAO soft-delete was blocked only on active USERS in the subtree, not on active child Organisations → an empty MAO with active child orgs could be deleted, orphaning them under an inactive MAO. **The delete now also blocks (422) on active child Organisations** (`OrganizationRepository.CountActiveChildOrganisationsAsync` — the `materialized_path LIKE … is_active=TRUE` subtree form, under the existing in-tx `FOR UPDATE`; 2-level model → "direct child" == "subtree" today, pinned). **And the create/transfer-vs-delete residual above is now CLOSED for the MAO-child case:** the org-CREATE-under-MAO path takes an in-tx `FOR SHARE` lock on the parent MAO + re-reads `is_active` before the INSERT (`LockActiveByIdForShareAsync`) → `FOR SHARE` conflicts with the delete's `FOR UPDATE`, so create-vs-delete serialize, and a create that loses the race 422s on the inactive re-read (the FK's `FOR KEY SHARE` proved existence, not activeness — this is the symmetric guard it couldn't provide). Org-MOVE already locks+rechecks the target MAO's `is_active`. Pinned by `S110MaoDeleteGuardTests` (incl. a `pg_blocking_pids` concurrency barrier proving the create-after-count ordering cannot commit an active org under a soft-deleted MAO).

**S102–S105 — the Enhedsspor org-model rework (ADR-038) — the AS-BUILT unit-authority model (QUAL-011).**
ADR-038 (S102 design; S103/S104 backend; S105 the approval paths) **replaced** the S97/S100 `enheder`/`user_enheder`
display metadata with a typed 7-level `units` hierarchy + explicit `unit_leaders` + `users.unit_id` (the old tables
are gone). The authority split is now two-part, and it is NOT "units have zero authority" as the S97/S100 paragraphs
above stated:
- **SCOPE path stays unit-free (ADR-038 D5 — the LOCKED Organisation scope boundary).** `OrgScopeValidator`,
  `RoleScope.CoversOrg`, and `ValidateEmployeeAccessAsync` do NOT read `unit_id`/`unit_leaders` — a shared unit
  (ancestor/descendant/sibling) grants NO cross-user visibility or scope. This is pinned BY CONSTRUCTION by
  `tests/StatsTid.Tests.Unit/ArchitectureConstraints/UnitAuthorityAbsenceTests.cs` (which DOES exist — widened in
  S104 to `UnitId`/`UnitLeaders`; the finding's "pinned to a test that no longer exists" was inaccurate on this point).
- **APPROVAL path admits the unit-leader exception (ADR-038 D4 — wired in S105).** A **secondary/peer** unit-leader
  is a legitimate approver — `DesignatedApproverAuthorizer.IsUnitLeaderApproverAsync` (`:179`), documented in the code
  as "the FIRST time `unit_leaders` legitimately enters authority." It is an exception scoped by the same Organisation
  boundary (a leader of a PARENT/GRANDPARENT/SIBLING unit holds no `unit_leaders` row for the employee's own unit, so
  the walk does not widen). So the current model = primary designated-edge leader **OR** secondary unit-leader
  (D4 exception) **OR** same-Organisation vikar **OR** HR/Admin org-scope — all within the D5 Organisation boundary.

### In-lock authorization serialization + the revocation-residual map (S78 / ADR-027 D18)

The approve/reject/reopen endpoints originally checked authorization (org-scope + the designated-edge resolver) on a **separate committed connection BEFORE** the write tx — a check-then-act TOCTOU: a revocation in the sub-second window before commit was not caught. S78 hardened this:

**The pattern (share-the-revoker-lock + in-tx re-evaluation).** True serialization of an authorization re-check against a concurrent revocation requires the action path to **hold the same lock the revoker holds**. The reporting-line write mutators serialize on the `reporting-org-{organisationId}` xact advisory (renamed from `reporting-tree-{treeRoot}` in S96; same domain = the Organisation); so the approve path acquires THAT advisory first, then re-evaluates the designated-edge authorization (+ re-classifies the REQUIRED-mode `confirmFallback` gate) STRICTLY under it. Because the action tx holds the advisory, a concurrent key-sharing revoke BLOCKS before its commit → the in-tx re-read (even on its own connection) observes the frozen committed state. A revoke committed *after* the pre-tx check but caught by the in-tx re-eval → 403.

**The drift-guard (the tree-key is derived from mutable membership).** The `reporting-tree` key derives from the employee's current `primary_org_id` (mutable). A shared **drift-guarded acquire** derives → acquires the advisory → re-derives under the lock; on drift (a transfer committed mid-acquire) it **rolls back + retries the whole request** on a fresh READ COMMITTED tx (≤3 → a pinned 409). A mid-tx release is impossible (`pg_advisory_xact_lock` is xact-scoped); session locks were rejected (explicit-unlock/cancellation/pooling failure modes). This is applied to EVERY employee-current-root tree mutator + the live transfer endpoint, so all concurrent pairs hold the same current key.

**The org-scope half is NOT DB-lockable.** `OrgScopeValidator` reads the actor's scopes from the **JWT `scopes` claim**, not the DB — so a scope "revocation" mutates nothing a DB lock can serialize (the grant lives in the bearer token until expiry). Only the designated-EDGE half is DB-row-based + serializable.

**The NAMED revocation-residual map — the S83 "full edge-auth pass" closed the tractable subset (ADR-027 D19); the rest are owner-accepted, all NON-corrupting:**
- ~~the **self-service `DELETE /delegate`**~~ **CLOSED (S83 R1).** Now runs `ReadCommitted` under the shared drift-guarded `AcquireRevokeTreeLocksAsync` (a pre-lock persisted-root probe + the same conditional dual-key as the admin revoke); it no longer fails to share the key.
- ~~the **admin-vikar-REVOKE post-transfer key-mismatch**~~ **CLOSED (S83 R2), modulo one named corner.** The revoke now ALSO acquires the subject's employee-current-root advisory (id-sorted, drift-guarded) when the subject is active, so it serializes against a concurrent transfer into a new tree. The persisted `manager_vikar.tree_root_org_id` remains the authoritative revoke-authority + lock anchor (revoke-safety preserved). **Named residual:** when the manager is *inactive*, the current root is not derivable (the derive filters `is_active=TRUE`), so the revoke falls back to persisted-only — non-corrupting (the only current≠persisted divergence is a transfer, which requires an *active* employee, so the inactive-during-transfer corner is near-impossible, and the revoke only ENDS an edge).
- **role-assignment deactivation** — owner-ACCEPTED as a non-corrupting policy (S83 R3, NOT closed). A real non-serialized window exists: the approve/reject in-lock re-check reads role state on its own connection with no RBAC lock (`src/Infrastructure/StatsTid.Infrastructure/DesignatedApproverAuthorizer.cs:131-145`) and role-revoke takes no advisory (`AdminEndpoints.cs:1804/1810`), so a role-revoke can commit between the re-check and the approval commit. Deliberately NOT dragged into the per-employee tree lock — RBAC scope is a separate bounded context from the reporting edge (P1). Blast radius is a stale-authority approval, reversible via operator `reopen` (operator-initiated, not auto-healed), fully audit-traceable. The deactivation arm (`users.is_active=FALSE`) IS covered: the in-lock `active+LeaderOrAbove` predicate fails closed (pinned by test, S83/TASK-8303).
- **user deactivation** (`users.is_active`) — owner-ACCEPTED platform residual. Written by 3 paths across two lock domains: `ReportingLineEndpoints.cs:1484` (remove, tree-lock domain) + `UserRepository.cs:445` (employment-end-date, employee-lock domain) + `SettlementCloseService.cs:517` (background Step-A flip, employee-lock domain). Fully serializing them would require tree-locking a background service across lock domains — a platform-scope effort (this enumerated count supersedes the S78 "~4 paths" estimate).
- the **JWT-scope revocation** (token-lifetime) — owner-ACCEPTED platform residual. Scopes are minted into the JWT at login (`JwtSettings.cs:8`, `ExpirationMinutes = 480` / 8h) with no runtime invalidation; a true fix needs a short token TTL / a revocation list — a platform feature.

S83 closed R1 + R2 (modulo the inactive-manager corner) and ruled R3 accepted; R4/R5 stay named platform residuals. Per the owner ruling (OQ-3), this lifts Reporting-Line & Approval Routing to a **tightened A−** (a justified, named residual map) — NOT a flat A while accepted (not closed) residuals stand (the S77 over-claim guard).

## Authorization Patterns

- All endpoints MUST chain `.RequireAuthorization()` with one of the defined policies:
  - `"Authenticated"` -- any valid JWT
  - `"EmployeeOrAbove"` -- Employee (level 5) or higher
  - `"LeaderOrAbove"` -- Leader (level 4) or higher
  - `"HROrAbove"` -- HR (level 3) or higher
  - `"LocalAdminOrAbove"` -- LocalAdmin (level 2) or higher
  - `"GlobalAdminOnly"` -- GlobalAdmin (level 1) only
- Scope-embedded JWT enables stateless authorization without DB lookups for role checks ([ADR-009](knowledge-base/decisions/ADR-009-scope-embedded-jwt.md))
- Org hierarchy uses materialized path `/MIN01/STY02/` with `text_pattern_ops` index ([ADR-008](knowledge-base/decisions/ADR-008-materialized-path-org-hierarchy.md))
- `OrgScopeValidator` provides the second layer: even with a valid role, access is denied if the actor's scopes do not cover the target org unit. **Authority is exact-org-set membership (ADR-035 flat model); materialized-path prefix/subtree matching is dormant for authorization** (the path above is retained for hierarchy display/navigation).

## Known Gotchas

### FAIL-001: JWT Claim Remapping

.NET 8 JWT bearer middleware maps standard claims by default (e.g. `sub` becomes `ClaimTypes.NameIdentifier`). This silently breaks lookups for custom claim names. **Fix**: Set `MapInboundClaims = false` on `JwtBearerOptions`. Reference: [FAIL-001](knowledge-base/failures/FAIL-001-jwt-claim-remapping-dotnet8.md).

### FindFirst vs FindAll on Array Claims

**CRITICAL**: Microsoft JWT bearer middleware splits `JsonClaimValueTypes.JsonArray` values into individual claims with the same claim type. Calling `FindFirst("scopes")` returns only ONE `RoleScope` object, not the full array. You MUST use `FindAll("scopes")` and parse each claim value separately (handling both `[{...}]` array and `{...}` object formats). This caused HTTP 403 bugs where users with multiple scopes appeared to have only one.

### Anonymous Type Serialization

NEVER serialize `RoleScope` via anonymous types (e.g. `new { role = ..., orgId = ..., scopeType = ... }`). Anonymous types produce camelCase properties by default, but `RoleScope` deserialization expects PascalCase (`Role`, `OrgId`, `ScopeType`). This mismatch causes silent deserialization failures and empty scopes. Always use the typed `RoleScope` record. Lesson from Sprint 6.

## Audit Trail

- **Append-only `audit_log` table** in PostgreSQL: records all security-relevant operations
- **Domain event tracking**: All domain events extend `DomainEventBase` with `ActorId`, `ActorRole`, `CorrelationId` (nullable, backward compatible). Reference: [PAT-004](knowledge-base/patterns/PAT-004-domain-events-extend-base-with-actor-tracking.md)
- **Role assignment audit**: `role_assignment_audit` table tracks all role grant/revoke operations with actor context
- **Correlation IDs**: Every HTTP request gets a `CorrelationId` (from header or auto-generated), threaded through all events and logs for end-to-end traceability
