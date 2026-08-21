<!-- anchor-sprint: 132 -->
# SEC — Security Finding Register

> **S132 update (2026-08-21) — the ruling-5 SEC items remediated (increment 1 of the fix-next program;
> dual-lens-reviewed, merged uncommitted).** **SEC-039** (← QUAL-061: rule-response body no longer logged —
> employment-data log-leak redacted to status+ruleId+employeeId) · **SEC-040** (← QUAL-063: failed logins now
> observable — structured WARNING with username+reason+IP+correlationId, CR/LF-sanitized, generic 401 kept,
> no response-side enumeration oracle) · **SEC-041** (← QUAL-049: bulk-import catch-all now logs server-side +
> returns a generic 500, no raw `ex.Message` leak). **SEC-004** — already CLOSED-by-reform; the owed
> belt-and-braces confirming test (exact-Organisation binding, sibling-sub-org case) is now LANDED. Detail:
> `docs/sprints/SPRINT-132.md`. S132-discovered SEC-adjacent follow-ups (login timing side-channel; app-wide
> username-log CR/LF audit; the InvalidOperationException detail echo) are tracked there for registration.

**Status**: LIVE — **S129 sweep COMPLETE (2 rounds, 2026-08-14)**; now the durable cross-session
security register. **Owner**: Orchestrator + PM. **Sweep baseline SHA**: `e955e13`. **Fix-next
(owner-ruled)**: **SEC-009 ✅** (S130 choke point; RES-003 CLOSED) → **SEC-020 ✅ FIXED (S130,
fail-closed default)** → **SEC-027 ✅ MITIGATED (S130 — least-privilege s2s token; capability residual →
SEC-036, pre-production)** → **SEC-032 ✅ FIXED (S130 — 4 write endpoints → GlobalAdminOnly; reads stay
LocalAdmin; SEC-034 in the same PUT handler FIXED at task 10)** → **SEC-033 ✅ FIXED (S130, app-layer — 3-surface
value validation; DB-CHECK backstop + fat-finger ceilings → ledger; SEC-037 migrator surface split off)** →
**SEC-015 ✅ MITIGATED (S130, re-adjudication — prod fail-closed already in place + pinned S19; committed
dev-key rotation → ledger per owner dev/test-friction guidance; shared-key capability = SEC-036)** →
**SEC-023 ✅ FIXED (S130, thorough — floor → GlobalAdminOnly + envelope guard; new External test harness;
real per-field schema deferred → SendAsync when the contract exists)** → **SEC-021 ✅ FIXED (S130, Option A —
per-task read scope check + claim-based GlobalAdmin bypass [terminated-subject defect fixed]; 404 posture;
non-admin read path enabled-but-unused)** → **SEC-019 ✅ + SEC-028 ✅ FIXED (S130 config batch — Claude-workflow
author-association gates + CI `contents:read`); SEC-031 CSP DEFERRED → prod server header (ledger)** →
**SEC-034 ✅ + SEC-035 ✅ FIXED (S130 sprint close — PUT identity-immutability 409 [phantom-audit fix] +
fail-loud supersession-audit helper).** ✅ **S130 FIX-NEXT BACKLOG COMPLETE** — all 10 fix-next items
dispositioned (fixed / mitigated / deferred-with-recorded-residual).

> **ROUND-1 SWEEP COMPLETE (2026-08-14) — see `docs/sprints/SPRINT-129.md` for the full adjudication
> records.** Calibration **PASS 3/3** (the 3 held-out holes independently rediscovered from code).
> Refute panel: 5 High findings CONFIRMED (SEC-009 + SEC-027 double-refuted, agent + Codex). Net
> changes: **SEC-009 OVERTURNED (worse — the top fix priority)**; **SEC-004 + SEC-013 downgraded/closed**
> (better than recorded); **SEC-022 split** (the `/execute` gate is sound; only the raw-auth-forward
> stands); rest re-ratified. New confirmed: **SEC-027…030** (below). **Owner ruling on each disposition
> is PENDING.**

**What this is (plain language).** A single, durable list of every security weakness we know about or
find, so none is lost and each is *re-attacked over time rather than quietly assumed settled*. It is a
**pointer index**: each row is a one-line summary + a citation to the real source of truth (a
SECURITY.md section, a sprint ruling, or a knowledge-base id) and — once the S129 sweep adjudicates it
— a citation to the tracked adjudication record in `docs/sprints/SPRINT-129.md` that says *why* the
current disposition holds. The register never restates the full reasoning; it routes you to it.

**Why "revisit, not shield" (the governing idea).** Prior owner rulings are recorded as
`known — should-be-revisited`, NOT as closed. The S129 sweep re-attacks each with fresh adversarial
evidence; the owner then re-adjudicates (`re-ratified` / `overturned` / `carried—no-new-evidence`).
This is the difference between a security posture that decays and one that stays honest. See the
sprint refinement (`.claude/refinements/REFINEMENT-s129-security-sweep.md`, TASK-B/C/D) for the
sweep's calibration control, the clean-worktree isolation, and the refute-panel verification.

**Stakes framing.** StatsTid is a hobby/learning project — nothing is deployed, no real data. Severity
labels (Critical/High/…) rank *engineering priority*, not live-incident risk. A finding is still
fixed-or-escalated, never dismissed by the hobby framing (see `docs/CONVENTIONS.md`).

## Columns
`SEC-id` · **what it means** (one plain-language line) · `title` · `origin` (`ruled-revisit` /
`swept-unruled` / `sweep-NEW`) · `severity` · `OWASP/STRIDE` · `status` · `source of truth` (the
citation) · `adjudication` (→ `SPRINT-129.md#sec-id`, filled by the sweep).

**Statuses**: `NEW` · `known—should-be-revisited` · `re-ratified` · `overturned` ·
`carried—no-new-evidence` · `accepted(new ruling)` · `fixed(cites S1XX task id — never in-sprint)`.

---

## Group 1 — SECURITY.md revocation-residual map (owner-accepted; re-attack)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-001 | A role can be revoked in the split-second before an approval commits, so a just-revoked approver's action still lands. | Role-assignment deactivation window (non-serialized) | ruled-revisit | Medium | A01 / EoP | known—should-be-revisited | SECURITY.md "In-lock authorization serialization" (S83 R3); `DesignatedApproverAuthorizer` + `AdminEndpoints` role-revoke | →#sec-001 |
| SEC-002 | A user can be deactivated by three different code paths across two lock domains; fully serializing them is platform-scope, so a stale-authority window exists. | User-deactivation across 3 write paths / 2 lock domains | ruled-revisit | Medium | A01 / EoP | known—should-be-revisited | SECURITY.md revocation-residual map (S83 R4); `ReportingLineEndpoints` / `UserRepository` / `SettlementCloseService` | →#sec-002 |
| SEC-003 | JWTs live 8 hours with no revocation list, so a token stays valid after the grant behind it is gone. | JWT 8h expiry, no revocation list | ruled-revisit | Medium | A07 / Spoofing | known—should-be-revisited | SECURITY.md (S83 R5); `JwtSettings` (`ExpirationMinutes = 480`) | →#sec-003 |
| SEC-004 | HR scoped to a sub-org can name a manager/vikar in a *sibling* sub-org of the same styrelse — outside their own subtree but inside the styrelse. | S91 secondary-principal lateral binding | ruled-revisit | Medium | A01 / EoP | **CLOSED — premise retired (owner ruling 2026-08-20)**: QUAL-085 established that this residual's entire premise (the nested sub-org/styrelse model + `ValidateSameTreeAsync` + `ORG_AND_DESCENDANTS`) was removed by the S92–S95 flat-authority reform; the live check is **exact-Organisation equality**, which makes the sibling-sub-org binding structurally impossible. Closed by reform, not by a fix. The committed "dedicated tightening sprint" follow-up is now moot. **Belt-and-braces owed (S132 cheap item):** a one-line test asserting exact-Organisation equality holds on secondary-principal binding. | SECURITY.md "…secondary-principal binding (S91)" + `:126` (drifted — QUAL-085 fixes the doc); S131 adjudication record | →#sec-004 |
| SEC-005 | In a sub-second window, an org can be created/transferred just as it is being deleted, briefly orphaning a user (non-MAO cases). | S98 create/transfer-vs-delete window (non-MAO) | ruled-revisit | Low | A01 / TOCTOU | known—should-be-revisited | SECURITY.md "S98 org-structure lifecycle" (residuals) | →#sec-005 |

## Group 2 — Sprint-log holes (deferred/carried rulings; re-attack)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-006 | Nine sibling read endpoints were not fully tier-gated; 3 gated at S128, 9 remain (7 lack month params). | RES-002 9-read remainder | ruled-revisit | Medium | A01 / Info-disclosure | known—should-be-revisited | RES-002; SPRINT-128 R2 (census 6→12, 9 open) | →#sec-006 |
| SEC-007 | A legacy SUBMITTED period's approvability path was ruled but should be re-checked for a bypass. | R6 legacy-SUBMITTED approvability | ruled-revisit | Low | A01 / EoP | known—should-be-revisited | SPRINT-127 R6 | →#sec-007 |
| SEC-008 | The reopen action reads authority via a different fork than approve/reject — a possible drift. | Reopen read-fork | ruled-revisit | Low | A01 / EoP | known—should-be-revisited | SPRINT-128 R4 | →#sec-008 |
| SEC-009 | Self-approval and the HR/GlobalAdmin org-scope fallback classification, carried unresolved since S125. | Self-approval + ORG_SCOPE_FALLBACK class | ruled-revisit | High (overturned) | A01 / EoP | **fixed (S130, 2026-08-14)** — choke point in `IsEffectiveApproverOrUnitLeaderAsync` + `ApprovalSelfGuard` at the 3 decision endpoints + differential test matrix; RES-003 CLOSED | RES-003 (S130 close note); `ApprovalEndpoints.cs`/`DesignatedApproverAuthorizer.cs` | →SPRINT-129#sec-009 |
| SEC-010 | A background backfill service writes without taking the advisory lock the online paths take. | ProjectionBackfillService §3.4 unlocked writes | ruled-revisit | Low | A04 / TOCTOU | known—should-be-revisited | ProjectionBackfillService §3.4 | →#sec-010 |
| SEC-011 | A natural-key probe on a non-whole-month range may leak existence/state. | Non-whole-month natural-key probe residual | ruled-revisit | Low | A01 / Info-disclosure | known—should-be-revisited | SPRINT-128 census residual | →#sec-011 |
| SEC-012 | A tier-probe path logs "Access denied" noise via a non-logging classification path — a signal/quality issue that may mask real denials. | S128 FU-A tier-probe log noise | ruled-revisit | Info | A09 / Repudiation | known—should-be-revisited — **S131 re-observed the class (2026-08-19)**: the adjacent instance surface is `OrgScopeValidator.cs:449-455` (every scope denial logs at Warning, incl. the routine employee-own-data arm at `:70`); not double-filed as a QUAL row per the dedupe rule. | SPRINT-128 FU-A; S131 adjudication record | →#sec-012 |

## Group 3 — Convention / architecture residuals (re-attack)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-013 | An in-memory mirror of a SQL authority predicate fails OPEN if a fact is omitted, rather than fail-closed. | PrefetchedAuthorityFacts fails-open | ruled-revisit | Medium | A01 / EoP | known—should-be-revisited | RES-003 item 4 | →#sec-013 |
| SEC-014 | A composed service→service hop (`check-overtime-governance`) is unproved end-to-end for authority. | check-overtime-governance composed hop | ruled-revisit | Low | A01 / Spoofing | known—should-be-revisited | WORKFLOW.md service↔service list | →#sec-014 |

## Group 4 — Deployment-config class (re-attack)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-015 | The compose dev JWT signing key is committed/shared — anyone with it can mint tokens for a dev instance. | Compose dev JWT signing key | ruled-revisit | Medium | A02 / Spoofing | **MITIGATED (S130, 2026-08-17, re-adjudication — no new code)**: a REAL production deploy that omits `Jwt:SigningKey` fails closed (`JwtValidationSetup` throws outside Development; all 5 services; pinned by `JwtValidationSetupTests` since S19/TASK-1905). The dev/demo compose stack runs Production-mode but explicitly supplies the committed well-known key, so IT stays spoofable **by design** — acceptable (dev/demo, no real data), conditioned on compose never being a real production stack. Rotating the committed key (→ injected secrets) would break the test harness (**S131 census-exact: 94 files** — see the pre-production ledger row) + compose + local `dotnet run`, so it is DEFERRED per owner guidance → pre-production ledger. **Narrow slice only — the shared-key *capability* (any key-holder mints any role) is SEC-036, OPEN.** | `docker/docker-compose.yml:4`; `JwtValidationSetup.cs:29-53`; cross-ref SEC-036 | →#sec-015 |
| SEC-016 | The `statstid_dev` DB password is a committed dev default. | statstid_dev DB password | ruled-revisit | Low | A02 | known—should-be-revisited | `docker/docker-compose*.yml` | →#sec-016 |
| SEC-017 | Demo logins use universal known passwords. | Universal demo passwords | ruled-revisit | Low | A07 | known—should-be-revisited | demo seed | →#sec-017 |
| SEC-018 | The mock services stand in for external systems and may accept/return anything unauthenticated. | Mock services trust posture | ruled-revisit | Info | A05 | known—should-be-revisited | `docker/mock-*` | →#sec-018 |
| SEC-019 | GitHub workflows run with a real `ANTHROPIC_API_KEY` on comment/PR triggers in a public repo — an *active* external surface. | Workflow secret on public triggers | ruled-revisit | Medium | A08 / Info-disclosure | **FIXED (S130, 2026-08-18)** — both Claude workflows gated to a trusted `author_association` ∈ {OWNER,MEMBER,COLLABORATOR} so an external actor can't invoke Claude with the repo's key: `claude.yml` (the real exposure — comment/review events run in base-repo context with secrets even on a fork-PR comment) gets a PER-EVENT gate in each `if:` arm + the imprecise `issues: assigned` trigger dropped; `claude-code-review.yml` gets a job-level PR-author gate (fork PRs already get no secret). NOTE: bot-authored PRs (Dependabot, assoc. NONE) are now out of auto-review scope (intended). | `.github/workflows/claude.yml`, `claude-code-review.yml` | →#sec-019 |

## Group 5 — Swept-unruled (surfaced by the cycle-1 review lenses; never ruled)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-020 | If a deployment forgets the `Auth:UseDatabase` env var it silently defaults to a hardcoded plaintext credential table including `admin01/admin` = GlobalAdmin. | Auth:UseDatabase fail-open | swept-unruled | High | A07 / Spoofing | **fixed (S130, 2026-08-14)** — `Program.cs` default flipped false→**true** (fail-closed); in-memory branch kept behind explicit `Auth:UseDatabase=false` opt-in (owner ruling a); behavioral RED-test (admin01/admin→401) + `InMemoryAuthWebApplicationFactory` | `Program.cs:375`; `SECURITY.md:15`; S130 | →#sec-020 |
| SEC-021 | The Orchestrator's task-read endpoint has no ownership/scope check — any authenticated user can read any task (IDOR). | Orchestrator task-read IDOR | swept-unruled | High | A01 / Info-disclosure | **FIXED (S130, 2026-08-18, Option A per-task scope)** — owner ruled A (keep `EmployeeOrAbove` floor, enable a future non-admin "read your own task" flow): `GET /tasks/{id}` now scope-checks the task's subject employee (reusing `ValidateEmployeeAccessAsync`) with a **claim-based GlobalAdmin bypass decided FIRST** (fixes a defect where the active-subject validator denied even a GlobalAdmin a task about a terminated employee); every denial AND not-found → **404** (hide existence). `input_data` now hydrated (System.Text.Json → `JsonElement`) so the subject resolves; hydration is fail-closed (non-object/malformed JSON → null → deny, never a 500). 22 Docker-free unit tests + 7 Docker-gated endpoint tests. **Residual:** the non-admin read path is enabled but has no consumer yet (frontend hits only `/health`; `/execute` is synchronous) — wire a real task-polling UI to it when built. | `Orchestrator/Program.cs` (read handler); `OrchestratorScopeHelpers.cs` (`EvaluateReadAccessAsync`/`IsGlobalAdmin`) | →#sec-021 |
| SEC-022 | The Orchestrator `/execute` uses the unfloored access overload AND forwards the caller's raw Authorization header downstream (confused deputy). | Orchestrator /execute unfloored + raw-auth forward | swept-unruled | High | A01+A10 / EoP | **SPLIT (S131 evidence, 2026-08-19)** — the unfloored-overload half is **FIXED (S130 incidental)**: at baseline `7e4bb1b` the Orchestrator validates via per-employee scope checks (`Orchestrator/Program.cs:57,:94-97`, the SEC-021 scope helpers) and the 2-arg unfloored `GetAccessibleOrgsAsync` has **zero production callers tree-wide** (S131 census); QUAL-040 (delete the dead overload) is the closing fix. The **raw-Authorization-forward half stays OPEN** (`Program.cs:53` reads the header; `TaskDispatcher.cs:52-55` forwards it). | S129 review byproduct; S131 QUAL-040 + adjudication record; Orchestrator `Program.cs` | →#sec-022 |
| SEC-023 | `POST /api/external/send` forwards caller-supplied arbitrary JSON to the external system with no role floor or scope. | external/send no floor | swept-unruled | Medium | A10 / Tampering | **FIXED (S130, 2026-08-17, thorough)** — floor raised `Authenticated`→`GlobalAdminOnly` (matching sibling `/api/payroll/export`); envelope guard added (256 KB cap BEFORE deserialization → 413; object-shape → 400); new in-process External test harness + 9 tests (Employee/Leader→403, GlobalAdmin→200, non-object→400, oversized→413; no-token→401 control), all Docker-free. **Real per-field payload schema DEFERRED** — no external contract exists yet (the receiver is a mock); when it does, enforce at `ExternalApiClient.SendAsync` (the internal `EventConsumerService` outbox-drain also reaches the external system, bypassing the endpoint). | `Integrations.External/Program.cs`; sibling `Payroll/Program.cs:157`; deferred → SendAsync | →#sec-023 |
| SEC-024 | RuleEngine.Api's endpoints are "Authenticated"-only; with no DB it cannot enforce org-scope, so the boundary needs a different control. | RuleEngine.Api structurally cannot org-scope | swept-unruled | Medium | A01 / EoP | NEW | S129 review byproduct; RuleEngine.Api endpoints (DEP-001/ADR-002 no-DB) | →#sec-024 |
| SEC-025 | The frontend stores bearer tokens in `localStorage`, so any XSS becomes token theft — the browser is part of the auth chain. | Frontend localStorage bearer tokens | swept-unruled | Medium | A07+A03 / Info-disclosure | NEW | S129 review byproduct; `AuthContext.tsx` / `api.ts` | →#sec-025 |

---

## Group 6 — found during S129 (incidental / CI gate)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-026 | The regression test suite pulled in SSH.NET 2023.0.0 (via Testcontainers), which carries a High CVE (SCP path traversal) — no real exposure (test-only, no untrusted-SCP download) but it failed the repo-wide CI vulnerable-package gate. | SSH.NET transitive CVE-2026-48798 | sweep-NEW (CI-incidental) | High | A06 / Tampering | **fixed** (forced patched SSH.NET 2026.0.0 in the Regression project, per the S39 transitive-CVE-override convention; build + scan verified clean) | CVE-2026-48798 / GHSA-q939-rpr3-3284; `tests/StatsTid.Tests.Regression/*.csproj` | this commit |

## Group 7 — new findings confirmed by the round-1 sweep (2026-08-14)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-027 | Any service holding the shared JWT key (even the low-trust External integration) can mint itself a `GlobalAdmin` token that passes every admin gate — there's no per-service identity. | Service self-mints GlobalAdmin over shared key | sweep-NEW | High | A07 / Spoofing-EoP | **MITIGATED (S130, 2026-08-17)** — the ONLY active GlobalAdmin s2s mint (`HttpRuleClassificationProvider`) lowered to a least-privilege Employee token; the shared-key CAPABILITY (any key-holder can still mint any role) stays OPEN → **SEC-036**. NOT closed. | `HttpRuleClassificationProvider.cs:123`; residual → SEC-036 | →SPRINT-129#sec-027 |
| **SEC-036** | The shared symmetric JWT key + shared `aud`/`iss` (`"statstid"`) means ANY service (or any key-holder) can mint a token with ANY role, and `GlobalAdminOnly` gates on the role claim ALONE (no scope) — so there is no per-service identity isolating a low-trust service from admin gates. Persists even with a perfectly-secret, rotated production key. | Shared-key s2s trust model — no per-service identity | sweep-NEW (SEC-027 residual) | High (capability) | A07+A01 / Spoofing-EoP | **OPEN — pre-production revisit** (ref ADR-007, which documents the shared-HMAC trade-off as accepted for a single deployment) | ADR-007; `JwtValidationSetup.cs:57-78`; `ScopeAuthorizationHandler.cs:19-24` (bare-role passes `GlobalAdminOnly`) | pre-production ledger |
| **SEC-037** | `LocalAgreementProfileMigrator` imports **parsed legacy DB values** (norm/flex hours) into `local_agreement_profiles` with NO range/negativity validation — a money-adjacent config surface fed from untrusted legacy data. A separate table from the three SEC-033 config tables, so out of SEC-033's scope; same validation class. | Legacy-migrator config-value validation gap | sweep-NEW (SEC-033 adjacent, 2026-08-17) | Medium | A04 / Tampering | **OPEN — found during SEC-033 dual-lens (Codex cycle-2)**; ruling pending. Reachable only via the legacy-DB migration path (not a user-facing endpoint). **S131 reachability annotation (2026-08-19): the migrator has NO production invocation path at all** — no DI registration, no hosted service, no tool, no runbook step; only tests construct it (QUAL-039). Deleting it would close this finding outright; keeping it makes the shared value-validator the fix. | `LocalAgreementProfileMigrator.cs:443,460`; `local_agreement_profiles` (init.sql:832); S131 QUAL-039 | (new — S130) |
| SEC-028 | The CI workflow declares no least-privilege `permissions:` block, so its jobs inherit the default (broader) GitHub token scope. | CI no permissions block | sweep-NEW | Low | A05 / EoP | **FIXED (S130, 2026-08-18)** — top-level `permissions: contents: read` on `ci.yml` (unlisted scopes → `none`). Verified all 7 jobs are read-only (upload-artifact uses the Actions runtime token, not this scope); a future write-needing job takes a job-scoped override. | `.github/workflows/ci.yml:13` | →SPRINT-129#sec-028 |
| SEC-029 | Container images run as root (no `USER`), so an RCE in any service runs as uid 0 inside its container. | Containers run as root | sweep-NEW | Info | A05 / EoP | CONFIRMED — ruling pending | 7× `**/Dockerfile` | →SPRINT-129#sec-029 |
| SEC-030 | The UI derives the logged-in role/scope from client-writable localStorage, so tampering flips the rendered role (UI gating only — the backend still enforces). | UI role from client-writable storage | sweep-NEW | Medium | A01 / Tampering | CONFIRMED (rides with SEC-025) — ruling pending | `AuthContext.tsx:39` | →SPRINT-129#sec-030 |

## Group 8 — round-2 sweep (2026-08-14: deeper config bodies + full frontend)

> Round 2 cleared the highest-stakes area: the settlement/reversal/termination **money flows are
> robust** (quantities copied from snapshots, never client-recomputed; bounded guards; per-employee
> advisory lock + CAS), and injection + mass-assignment are clean. The **full frontend is clean** (no
> XSS sinks, redirects, prototype pollution, or data leakage). The new findings are in config-CRUD
> bodies + one FE header gap.

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-032 | A LocalAdmin of ONE institution can edit Position-Override config (norm/flex hours) that resolves for EVERY institution on that agreement+position — the endpoint is only LocalAdmin-floored with no org-scope check, while all sibling global config is GlobalAdmin-only. | Position-Override cross-tenant config write | sweep-NEW (r2) | **High** | A01 / EoP | **FIXED (S130, 2026-08-17)** — the 4 write endpoints (create/update/deactivate/activate) raised `LocalAdminOrAbove`→`GlobalAdminOnly`, matching every sibling global-config surface; the 3 reads stay `LocalAdminOrAbove` (view-only transparency, owner ruling OQ-2). Root cause: global config (no org dimension) was floored below its siblings; owner declined the per-institution-org-binding redesign (OQ-1(a)). **SEC-034 (same PUT handler) — FIXED at S130 task 10** (reframed by review: it was an audit-integrity bug, not a re-key/500). | `PositionOverrideEndpoints.cs:164/294/394/523`; `ConfigResolutionService.cs:153` | →SPRINT-129#sec-032 |
| SEC-033 | Money-adjacent config numbers (norm hours, min rest, accrual quotas) have no server-side range/negativity validation and no DB CHECK — an admin can set values that corrupt overtime/vacation/norm calc (e.g. `MinimumRestHours=0`, negative norm). | Config-value validation gap (3 families) | sweep-NEW (r2) | Medium | A04 / Tampering | **FIXED (S130, 2026-08-17, app-layer)** — server-side range/negativity + domain-set validation added at all 3 write surfaces (POST+PUT), closing the user-reachable corruption incl. the `MinimumRestHours=0` flagship; the `NormPeriodWeeks` valid-set relocated to SharedKernel. Owner ruling OQ-1(a)/OQ-2: NO DB CHECK and NO upper "fat-finger" ceilings this task — both DEFERRED to the pre-production ledger (ceilings need domain agreement-truth). SEC-037 (the legacy-migrator surface) is a separate OPEN finding. | `AgreementConfigEndpoints.cs` (extended `ValidateRequest`); `EntitlementConfigValidator.cs`; `PositionOverrideValidator.cs`; `AgreementRuleConfig.cs:13` (relocated set) | →SPRINT-129#sec-033 |
| SEC-034 | A Position-Override PUT can re-key an active override to a different agreement/position slot without re-checking the active-uniqueness index. | Position-Override PUT re-key state-confusion | sweep-NEW (r2) | Low | A04 / Tampering | **FIXED (S130, 2026-08-18)** — **reframed by review:** the repo's `UpdateAsync` never writes the identity columns (no re-key, no 500); the real defect was AUDIT INFIDELITY — a mismatched-triple PUT stamped the audit row / `PositionOverrideUpdated` event / projection with the BODY's (wrong) identity (a "phantom-identity" audit). Fix (owner ruling): the PUT now REJECTS an identity change with **409** BEFORE any emit (naming the 3 immutable fields), and sources `updatedEntity`'s identity from the stored row (defence-in-depth). Value-only PUT unaffected (200). | `PositionOverrideEndpoints.cs` (PUT guard); evidence: repo `UpdateAsync` SET clause omits identity | →SPRINT-129#sec-034 |
| SEC-035 | On a config publish-supersession, the archive of the prior version emits its audit row only if a version field is non-null — a null could leave a silent audit gap on a lifecycle transition. | Supersession audit-row omission | sweep-NEW (r2) | Low | A09 / Repudiation | **FIXED (S130, 2026-08-18, fail-loud hardening)** — **NOT reproducible today** (both lenses verified: `AgreementConfigRepository` assigns `archivedId`+`archivedVersion` together from one `RETURNING config_id, version` row → they can't diverge). Owner ruled harden anyway: the emission gate is now a testable helper `EvaluateArchivalAudit(id, version)` — both-set→Emit, both-null→skip, **divergence→throw** → the publish tx's inner `catch{Rollback;throw}` rolls back the WHOLE publish (fail-closed; Auditability preserved). Unit-tested (the only way to reach the throw). Happy path already covered by existing tests. | `AgreementConfigEndpoints.cs` (`EvaluateArchivalAudit` + wiring) | →SPRINT-129#sec-035 |
| SEC-031 | The frontend ships no Content-Security-Policy header — a defense-in-depth gap (no impact today given zero XSS sinks, but no cap if one ever appears). | Missing Content-Security-Policy | sweep-NEW (r2) | Low | A05 / Tampering | **DEFERRED — pre-production (S130, owner ruling c, 2026-08-18)** — a `<meta>` CSP in `index.html` is structurally incapable of being both dev-safe and strict (Vite injects inline `<script>` in dev AND the prod build → forces `script-src 'unsafe-inline'`, defeating the anti-XSS value), and the app has no serving layer to carry a real header. The complete strict policy (nonce/hash `script-src`, `frame-ancestors 'none'`, `report-to`, real `connect-src`) is a **server/CDN response header** for a real deployment → ledger. | `frontend/index.html`; deferred → prod server header | →SPRINT-129#sec-031 |

## Group 9 — mirrored from the S131 quality sweep (owner-ruled 2026-08-20)

> Five S131 quality findings with a genuine access-control / disclosure dimension, mirrored here (owner
> ruling 5a) so the security posture stays complete and each is re-attackable at future security sweeps.
> Each is ALSO a QUAL row — the primary evidence + remediation lives there; these are the SEC-register
> cross-references. Stakes framing per CONVENTIONS: engineering priority, not incident risk (hobby
> project, nothing deployed). Remediation rides with S132.

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-038 | Authorization denials decided at the ASP.NET policy layer produce no application log AND no `audit_log` row (the audit middleware runs after authorization and, tree-wide, only the Backend registers it) — a 403's cause and even its occurrence can't be established afterwards. | Policy-layer denials leave no trace + Backend-only audit middleware | sweep-NEW (S131 → QUAL-009/003) | High | A09 / Repudiation | **OPEN — owner-mirrored 2026-08-20**; remediation = QUAL-009/003 in S132 (add denial logging + rule the audit-row decision; the Backend-only-audit design question is settled at remediation time). | `ScopeAuthorizationHandler.cs:9-67`; `Backend Program.cs:484-487`; QUAL-009/003 | S131 register + adjudication |
| SEC-039 | Failure-path logs write the rule engine's whole JSON response body — employee id plus per-day hours/rates/balances — into the application log, making the log stream an unmanaged second copy of employment data with no redaction/retention treatment. | Employment data logged verbatim on failure paths | sweep-NEW (S131 → QUAL-061) | Medium | A09 / Info-disclosure | **OPEN — owner-mirrored 2026-08-20**; remediation = QUAL-061 in S132. | `PeriodCalculationService.cs:1399-1485`; `OrchestratorControlLoop.cs:86-87`; QUAL-061 | S131 register + adjudication |
| SEC-040 | Failed logins emit no application log line and the attempted username reaches no store, so unknown-user can't be distinguished from wrong-password (the `audit_log` row records the 401 + IP but with a null actor). | Failed-login logging gap | sweep-NEW (S131 → QUAL-063) | Medium | A09 / Repudiation | **OPEN — owner-mirrored 2026-08-20**; remediation = QUAL-063 in S132. | `AuthEndpoints.cs:23-31,90-91`; QUAL-063 | S131 register + adjudication |
| SEC-041 | The bulk reporting-line import returns the raw .NET exception message in a 500 response body (`detail = ex.Message`) — an information-disclosure shape, and the tree's only endpoint that puts exception text on the wire. | Raw exception text in a 500 response body | sweep-NEW (S131 → QUAL-049) | Medium | A09 / Info-disclosure | **OPEN — owner-mirrored 2026-08-20**; remediation = QUAL-049 in S132 (shape the 500 + log server-side). | `ReportingLineEndpoints.cs:1144-1147`; QUAL-049 | S131 register + adjudication |
| SEC-042 | `docs/SECURITY.md` asserts the unit dimension "appears in NO authority path" (naming `DesignatedApproverAuthorizer`) — substantively false since S105 deliberately wired the unit-leader approval edge; the claim is pinned to a test and tables that no longer exist. A security reference stating an authority model the code contradicts. | SECURITY.md asserts a false authority model | sweep-NEW (S131 → QUAL-011) | High | A01 / posture-doc drift | **OPEN — owner-mirrored 2026-08-20**; remediation = QUAL-011 in S132 (correct the SECURITY.md paragraph to the S105/ADR-038 as-built authority model). | `SECURITY.md:134-138`; `UnitAuthorityAbsenceTests.cs:12-20`; QUAL-011 | S131 register + adjudication |

## Notes on SEC-026 and calibration

> **Note on SEC-026 status vs "fixed never in-sprint."** The register's `fixed(… never in-sprint)`
> rule is about not remediating *sweep-discovered* findings inside the audit sprint. SEC-026 was NOT
> found by the sweep method — it surfaced from the CI vulnerable-package gate going red repo-wide
> (a newly-published advisory), so it was fixed immediately as **enforcement-layer maintenance**
> (a red vuln gate blocks every commit), via the project's existing transitive-CVE-override pattern.
> Distinct from the audit's remediation-deferral discipline.

## Notes
- **`sweep-NEW` rows** are appended here as the S129 sweep confirms new findings (refute-panel
  verdict CONFIRMED before entry).
- **The 3 calibration holes** are chosen by the Orchestrator from the swept-unruled set (SEC-020…025)
  and pre-registered in an **owner-held manifest** before the sweep — NOT flagged in this register
  (that would defeat the control). Which three are calibration lives only in the owner's manifest.
- **Publication note (Codex W).** This register is public (owner ruling 2026-08-12: repo stays public,
  no redaction). That is an accepted *publication risk*, not a claim the code is free of weaknesses —
  the rows above are exactly the weaknesses. Attack-detail raw evidence stays in the gitignored sweep
  dir for hygiene, not secrecy.

## Pre-production revisit ledger

*Deliberate hobby-stage / minimal-fix choices that CLOSE the finding for now but leave a residual that
MUST be reconsidered before any move toward production. Recorded here (owner request, 2026-08-14) so
the deferred decision is tied to a production-readiness gate, not silently permanent. See CONVENTIONS.md
"Project Status & Intent" — the go-serious hardening pass is owed work, deferred by choice.*

| Item | The choice we took | What to revisit before production |
|------|--------------------|-----------------------------------|
| **SEC-020** (fixed S130) | Owner ruling (a) MINIMAL: flipped `Auth:UseDatabase` to fail-closed by default, but KEPT the in-memory hardcoded credential table (`admin01/"admin"=GlobalAdmin`, …) in source, reachable via explicit `Auth:UseDatabase=false`. | **Remove the in-memory hardcoded credential table entirely (option b) — DB-only auth.** Hardcoded credentials in source, even behind an opt-in, do not belong in a production build. Delete the `else` branch (`AuthEndpoints.cs:77-103`), rework/remove `S118LoginSpecRuntimeTests`'s in-memory case, ensure every environment has DB-seeded auth. |
| **SEC-036** (SEC-027 residual, S130) | Owner ruling (a) MINIMAL on SEC-027: least-privileged the one active GlobalAdmin s2s mint, but the shared-key s2s trust model is UNCHANGED — any key-holder can still mint any role; `GlobalAdminOnly` gates on the role claim alone (no scope). ADR-007 documents the shared-HMAC key as accepted "for a single deployment." | **Give service-to-service tokens a per-service identity so a low-trust service can't assert admin.** Two paths: (b) make `GlobalAdminOnly` require a real GLOBAL `RoleScope` (not just the role claim) — closes the bare-role admin-gate hole (breaks the opt-in in-memory admin login + `GlobalAdminOnly_AcceptsGlobalAdminToken`, so it's its own scoped task); and/or (c) per-service `aud`/`iss` + validation so a token minted by one service isn't accepted by another. Amend ADR-007 when done. |
| **SEC-033 DB-CHECK backstop** (fixed S130 app-layer) | Owner ruling OQ-1 (a): added server-side value validation at the three config write endpoints (app-layer), but did NOT add DB CHECK constraints — even though `entitlement_configs` already carries domain CHECKs as the house "data-layer backstop for any other write path." | **Add DB CHECK constraints** on the money-adjacent numbers of `agreement_config`, `entitlement_config`, `position_override_configs` (+ the SEC-037 `local_agreement_profiles` surface), so a corrupting value is refused on EVERY path (incl. seeders/migrator), not only the HTTP endpoints. A schema migration (greenfield CHECK + idempotent legacy `ALTER … ADD CONSTRAINT` with `NOT VALID`/preflight + `generate_db_schema.py` regen + runbook). |
| **SEC-033 fat-finger ceilings** (fixed S130 lower-bounds only) | Owner ruling OQ-2: shipped non-negativity + domain-set validation (closes every corruption example), but deliberately NO invented upper ceilings. Owner: *"note that we have it as a follow up… but it requires agreement truth and deep analysis."* | **Add upper-bound (fat-finger) ceilings** on the money-adjacent config numbers — but ONLY with domain-expert **agreement truth** (Phase-B-class analysis), NOT guessed. E.g. a real max for `WeeklyNormHours`/`MinimumRestHours`/`AnnualNormHours`/`MaxDailyHours`/`AnnualQuota` per agreement. Requires the domain-source work, so it is deferred to that engagement. |
| **SEC-015** (MITIGATED S130) | Re-adjudication: real production already fails closed without a configured key (pinned since S19); but the committed well-known JWT key `StatsTid_Sprint3_DevKey_...` is shared across `docker-compose.yml`, the `DevFallbackSigningKey` const, and — **S131 census-exact: 94 files** (91 test files under three constant names [`DevFallbackSigningKey` ×86, `DevSigningKey` ×4, `JwtSigningKey` ×1 — 86+4+1=91] + compose + `JwtValidationSetup.cs` + `.gitleaks.toml`, which documents the string as deliberately non-secret). Rotating it now would break the test harness + compose + friction-free local dev — deferred per owner guidance. **S131 subnote:** 28 test files construct their own JWT bootstrap inline despite shared factories existing (`SpecRuntimeTestSupport`, `S120ContractAssert`) — consolidating onto the factories FIRST shrinks the rotation blast radius; fold into the same pass. | **Rotate the committed well-known JWT key to injected secrets** (env/secret store), no key literal in source/compose/tests. Same secrets-hygiene pass as SEC-016/017. NOTE: this only removes the *well-known* property; the shared-key trust-model capability (any key-holder mints any role) is **SEC-036** — do both in the pre-production JWT hardening. |
| SEC-016 / SEC-017 (accepted) | Committed dev DB password (`statstid_dev`) + shared demo password (`"password"`, incl. GlobalAdmin seed users). | Rotate to environment secrets; no committed credentials in a production config/seed. |
| SEC-018 (accepted) | Unauthenticated mock payroll/external services trusted by the integration flow. | Replace mocks with authenticated real integrations (or gate them off) before production. |
| SEC-029 (accepted) | Container images run as root (no `USER`). | Add a non-root `USER` to the Dockerfiles for the production image set. |
| **SEC-031** (DEFERRED S130) | Owner ruling (c): NO CSP shipped now. A `<meta>` CSP in `index.html` can't be both dev-safe and strict — Vite injects inline `<script>` in dev AND the prod build, forcing `script-src 'unsafe-inline'` (which defeats the anti-XSS value); and there is no serving layer to carry a real header. | **Add the strict CSP as a production web-server/CDN response HEADER** when there is a real deployment: nonce/hash-based `script-src` (no `'unsafe-inline'`), `frame-ancestors 'none'` (clickjacking — header-only, can't be done in a meta tag), `object-src 'none'`, `base-uri`/`form-action 'self'`, `report-to`, and `connect-src` scoped to the deployment's real origins. Verify against the built artifact in a browser (CI currently only exercises the Vite dev server). |

*(This ledger grows as later fix-next items take a minimal path with a deferred residual.)*
