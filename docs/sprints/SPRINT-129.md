# SPRINT-129 — Security threat-model sweep (WS5 / Phase 3)

| Field | Value |
|-------|-------|
| **Type** | AUDIT sprint (design/analysis only — no product code) |
| **Build Verified** | N/A (audit sprint; no code change) |
| **Test Verified** | **3269 carried from S128 — NOT re-executed** (audit sprint; per the design-only precedents S28/S32/S36/S38/S67). Source: S128 CI run `31485462948`, close `3af7291`. |
| **Sweep baseline SHA** | `e955e13` (the PRE-REGISTER baseline — the SEC register + this file are committed AFTER it, so the sweep worktree checked out from `e955e13` contains neither; load-bearing for the calibration control). |
| **Refinement** | `.claude/refinements/REFINEMENT-s129-security-sweep.md` (rev 6) |
| **Skill** | `.claude/skills/threat-model-audit/` (TASK-A, `4f41995`, Codex-approved) |
| **Register** | `docs/operations/security-finding-register.md` (TASK-B) |

## Plan Review (Step 0b) — decision

The plan (the refinement) went through **five dual-lens cycles** (Codex external + internal Reviewer)
before any execution — the Step-0b bar is over-met. Convergence: 3B → 3B → 1B → 0-structural → 3
one-line nits → clean. The load-bearing catch (cycles 3–5) was that the calibration control leaked its
own answers; closed **structurally** via the clean-worktree isolation pinned to `e955e13`. Both OQs
resolved (repo public/no-redaction, 2026-08-12; bounded browser-auth in round 1, 2026-08-13). No open
plan blocker at kickoff.

## TASK-A — vendored skill (DONE)

`/autoresearch:security` method (`zhongpei/autoresearch-skills`, MIT © 2026 Udit Goenka) adapted into a
READ-ONLY, hook-free, invoke-by-name skill; `--fix`/auto-remediation stripped; a first-draft
calibration answer-key leak caught + fixed; Codex external-lens **VERDICT APPROVED** (all 5 checks).

## TASK-B — SEC register + coverage inventory (DONE)

Register live with 25 rows (SEC-001…025): 5 SECURITY.md revocation residuals, 7 sprint-log holes, 2
convention residuals, 5 deployment-config, 6 swept-unruled. Full "revisit, not shield" harvest per the
refinement; every row cites a source of truth and points to its adjudication anchor below.

### Commit-pinned coverage inventory (the sweep universe @ `e955e13`)

Reproducibly generated (the `rg` extractions in the refinement, run at baseline). Every cell must end
round 1 **examined** (ledger-row-anchored) or **explicitly owner-deferred** — zero silent gaps. Codex
independently regenerates + diffs this before the sweep (falsifiability backstop).

| Slice | Cells | Count | Notes |
|-------|-------|-------|-------|
| (i) tiers | Backend.Api endpoints | **137** | across ~25 `Endpoints/*.cs` + `ApiEndpoints.cs` |
| (i) tiers | auth chain | 4 named | `OrgScopeValidator`, `DesignatedApproverAuthorizer`, `ApprovalReadTier`, JWT mint/validate |
| (ii) service↔service | Orchestrator endpoints | **3** | + token-forwarding paths |
| (ii) service↔service | HTTP-client sites | **26** | `AddHttpClient`/`HttpClient`/`BaseAddress` + consumers |
| (ii) service↔service | RuleEngine.Api endpoints | **9** | Authenticated-only; no-DB boundary (SEC-024) |
| (ii) service↔service | Payroll / External endpoints | **6 / 2** | Payroll 5+health; External send/health |
| (iii) deploy/CI | compose / mock / Dockerfile | **2 / 12 / 7** | |
| (iii) deploy/CI | appsettings / launchSettings | **14 / 7** | |
| (iii) deploy/CI | GH workflows | **3** | `ci.yml`, `claude.yml`, `claude-code-review.yml` |
| (iv) revisit | SEC register rows | **25** | SEC-001…025 (handed to revisit agents by the Orchestrator, out of band) |
| (v) browser-auth (bounded) | token storage / XSS sinks / proxy / CORS | named | `AuthContext.tsx`, `api.ts`, `vite.config.ts` proxy |
| middleware / auth-config | across `Program.cs` + `*Auth*.cs` | **33** | policy definitions + `RequireAuthorization` sites |

*(Counts are the mechanical extraction totals at `e955e13`; the sweep decomposes high-count cells —
e.g. the 137 endpoints — into per-file/per-boundary examination rows in the ledger.)*

## TASK-C — the sweep (PENDING — owner handoff: the calibration manifest)

**Blocked on the one owner touchpoint:** the Orchestrator selects 3 calibration holes from the
swept-unruled set (SEC-020…025) and hands the owner a **sealed manifest** (ids + evidence hash) to
hold; the sweep then runs blind and is scored after round 1. Until the owner holds the manifest, the
calibration control is not independent, so the sweep does not start. Then: fan-out read-only agents in
the `e955e13` worktree → ledger → refute panel (TASK-D) → owner adjudication (TASK-E) → remediation
proposal.

---

## Adjudication records (filled by the sweep — TASK-D/E)

*Each SEC row's durable, PM-readable record: prior disposition · new adversarial evidence (src/
file:line + commit) · finder verdict · refuter verdict(s) · disagreement resolution · owner decision +
rationale · date · remediation pointer. Populated as the sweep adjudicates each row; empty anchors
below are the destinations the register points to.*

<!-- Group 1 --> ### sec-001 · ### sec-002 · ### sec-003 · ### sec-004 · ### sec-005
<!-- Group 2 --> ### sec-006 · ### sec-007 · ### sec-008 · ### sec-009 · ### sec-010 · ### sec-011 · ### sec-012
<!-- Group 3 --> ### sec-013 · ### sec-014
<!-- Group 4 --> ### sec-015 · ### sec-016 · ### sec-017 · ### sec-018 · ### sec-019
<!-- Group 5 --> ### sec-020 · ### sec-021 · ### sec-022 · ### sec-023 · ### sec-024 · ### sec-025
