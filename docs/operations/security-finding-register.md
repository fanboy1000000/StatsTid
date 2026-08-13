<!-- anchor-sprint: 129 -->
# SEC — Security Finding Register

**Status**: LIVE (created S129, 2026-08-13). **Owner**: Orchestrator + PM. **Sweep baseline SHA**: `e955e13`.

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
| SEC-009 | Self-approval and the HR/GlobalAdmin org-scope fallback classification, carried unresolved since S125. | Self-approval + ORG_SCOPE_FALLBACK class | ruled-revisit | Medium | A01 / EoP | known—should-be-revisited | carried since SPRINT-125; `approval_method` classification | →#sec-009 |
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
