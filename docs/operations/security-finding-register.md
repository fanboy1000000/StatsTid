<!-- anchor-sprint: 129 -->
# SEC — Security Finding Register

**Status**: LIVE — **S129 sweep COMPLETE (2 rounds, 2026-08-14)**; now the durable cross-session
security register. **Owner**: Orchestrator + PM. **Sweep baseline SHA**: `e955e13`. **Fix-next
(owner-ruled)**: **SEC-009 ✅ FIXED (S130, choke point + matrix; RES-003 CLOSED)** → 020 → 027 → 032 →
033 → 015 → 023 → 021 → 019 → 028/031/034/035 (remaining in the ROADMAP backlog → remediation sprint).

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
| SEC-004 | HR scoped to a sub-org can name a manager/vikar in a *sibling* sub-org of the same styrelse — outside their own subtree but inside the styrelse. | S91 secondary-principal lateral binding | ruled-revisit | Medium | A01 / EoP | known—should-be-revisited | SECURITY.md "Medarbejder-administration … secondary-principal binding (S91)"; written follow-up tracked | →#sec-004 |
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
| SEC-012 | A tier-probe path logs "Access denied" noise via a non-logging classification path — a signal/quality issue that may mask real denials. | S128 FU-A tier-probe log noise | ruled-revisit | Info | A09 / Repudiation | known—should-be-revisited | SPRINT-128 FU-A | →#sec-012 |

## Group 3 — Convention / architecture residuals (re-attack)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-013 | An in-memory mirror of a SQL authority predicate fails OPEN if a fact is omitted, rather than fail-closed. | PrefetchedAuthorityFacts fails-open | ruled-revisit | Medium | A01 / EoP | known—should-be-revisited | RES-003 item 4 | →#sec-013 |
| SEC-014 | A composed service→service hop (`check-overtime-governance`) is unproved end-to-end for authority. | check-overtime-governance composed hop | ruled-revisit | Low | A01 / Spoofing | known—should-be-revisited | WORKFLOW.md service↔service list | →#sec-014 |

## Group 4 — Deployment-config class (re-attack)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-015 | The compose dev JWT signing key is committed/shared — anyone with it can mint tokens for a dev instance. | Compose dev JWT signing key | ruled-revisit | Medium | A02 / Spoofing | known—should-be-revisited | `docker/docker-compose*.yml` | →#sec-015 |
| SEC-016 | The `statstid_dev` DB password is a committed dev default. | statstid_dev DB password | ruled-revisit | Low | A02 | known—should-be-revisited | `docker/docker-compose*.yml` | →#sec-016 |
| SEC-017 | Demo logins use universal known passwords. | Universal demo passwords | ruled-revisit | Low | A07 | known—should-be-revisited | demo seed | →#sec-017 |
| SEC-018 | The mock services stand in for external systems and may accept/return anything unauthenticated. | Mock services trust posture | ruled-revisit | Info | A05 | known—should-be-revisited | `docker/mock-*` | →#sec-018 |
| SEC-019 | GitHub workflows run with a real `ANTHROPIC_API_KEY` on comment/PR triggers in a public repo — an *active* external surface. | Workflow secret on public triggers | ruled-revisit | Medium | A08 / Info-disclosure | known—should-be-revisited | `.github/workflows/claude*.yml` (×2) | →#sec-019 |

## Group 5 — Swept-unruled (surfaced by the cycle-1 review lenses; never ruled)

| SEC | What it means (plain language) | Title | Origin | Sev | OWASP/STRIDE | Status | Source of truth | Adjudication |
|-----|--------------------------------|-------|--------|-----|--------------|--------|-----------------|--------------|
| SEC-020 | If a deployment forgets the `Auth:UseDatabase` env var it silently defaults to a hardcoded plaintext credential table including `admin01/admin` = GlobalAdmin. | Auth:UseDatabase fail-open | swept-unruled | High | A07 / Spoofing | NEW | S129 review byproduct; `Program.cs` default + `AuthEndpoints` cred table | →#sec-020 |
| SEC-021 | The Orchestrator's task-read endpoint has no ownership/scope check — any authenticated user can read any task (IDOR). | Orchestrator task-read IDOR | swept-unruled | High | A01 / Info-disclosure | NEW | S129 review byproduct; Orchestrator `Program.cs` | →#sec-021 |
| SEC-022 | The Orchestrator `/execute` uses the unfloored access overload AND forwards the caller's raw Authorization header downstream (confused deputy). | Orchestrator /execute unfloored + raw-auth forward | swept-unruled | High | A01+A10 / EoP | NEW | S129 review byproduct; Orchestrator `Program.cs` | →#sec-022 |
| SEC-023 | `POST /api/external/send` forwards caller-supplied arbitrary JSON to the external system with no role floor or scope. | external/send no floor | swept-unruled | Medium | A10 / Tampering | NEW | S129 review byproduct; Integrations.External `Program.cs` | →#sec-023 |
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
| SEC-027 | Any service holding the shared JWT key (even the low-trust External integration) can mint itself a `GlobalAdmin` token that passes every admin gate — there's no per-service identity. | Service self-mints GlobalAdmin over shared key | sweep-NEW | High | A07 / Spoofing-EoP | CONFIRMED ×2 (agent+Codex) — ruling pending | `HttpRuleClassificationProvider.cs:117`; `JwtValidationSetup.cs:73-84` | →SPRINT-129#sec-027 |
| SEC-028 | The CI workflow declares no least-privilege `permissions:` block, so its jobs inherit the default (broader) GitHub token scope. | CI no permissions block | sweep-NEW | Low | A05 / EoP | CONFIRMED (fork-PR read-only bounds it) — ruling pending | `.github/workflows/ci.yml:3` | →SPRINT-129#sec-028 |
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
| SEC-032 | A LocalAdmin of ONE institution can edit Position-Override config (norm/flex hours) that resolves for EVERY institution on that agreement+position — the endpoint is only LocalAdmin-floored with no org-scope check, while all sibling global config is GlobalAdmin-only. | Position-Override cross-tenant config write | sweep-NEW (r2) | **High** | A01 / EoP | CONFIRMED ×2 (agent+refuter, refuter upgraded Medium→High) — ruling pending | `PositionOverrideEndpoints.cs:164`; `ConfigResolutionService.cs:153` | →SPRINT-129#sec-032 |
| SEC-033 | Money-adjacent config numbers (norm hours, min rest, accrual quotas) have no server-side range/negativity validation and no DB CHECK — an admin can set values that corrupt overtime/vacation/norm calc (e.g. `MinimumRestHours=0`, negative norm). | Config-value validation gap (3 families) | sweep-NEW (r2) | Medium | A04 / Tampering | CONFIRMED — ruling pending | `PositionOverrideEndpoints.cs:93`; `AgreementConfigEndpoints.cs:862`; `EntitlementConfigEndpoints.cs:164` | →SPRINT-129#sec-033 |
| SEC-034 | A Position-Override PUT can re-key an active override to a different agreement/position slot without re-checking the active-uniqueness index. | Position-Override PUT re-key state-confusion | sweep-NEW (r2) | Low | A04 / Tampering | CONFIRMED (Possible) — ruling pending | `PositionOverrideEndpoints.cs:205-220` | →SPRINT-129#sec-034 |
| SEC-035 | On a config publish-supersession, the archive of the prior version emits its audit row only if a version field is non-null — a null could leave a silent audit gap on a lifecycle transition. | Supersession audit-row omission | sweep-NEW (r2) | Low | A09 / Repudiation | CONFIRMED (Possible; repo-invariant-dependent) — ruling pending | `AgreementConfigEndpoints.cs:515-528` | →SPRINT-129#sec-035 |
| SEC-031 | The frontend ships no Content-Security-Policy header — a defense-in-depth gap (no impact today given zero XSS sinks, but no cap if one ever appears). | Missing Content-Security-Policy | sweep-NEW (r2) | Low | A05 / Tampering | CONFIRMED — ruling pending | `frontend/index.html:1-12` | →SPRINT-129#sec-031 |

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
