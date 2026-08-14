# SPRINT-129 — Security threat-model sweep (WS5 / Phase 3)

| Field | Value |
|-------|-------|
| **Type** | AUDIT sprint (design/analysis only — no product code) |
| **Build Verified** | N/A (audit sprint; no code change) |
| **Test Verified** | **3269 carried from S128 — NOT re-executed** (audit sprint; per the design-only precedents S28/S32/S36/S38/S67). Source: S128 CI run `31485462948`, close `3af7291`. |
| **Sweep baseline SHA** | `e955e13` (the PRE-REGISTER baseline — the SEC register + this file are committed AFTER it, so the sweep worktree checked out from `e955e13` contains neither; load-bearing for the calibration control). |
| **Refinement** | `.claude/refinements/REFINEMENT-s129-security-sweep.md` (rev 6) — **tracked mirror for cross-device resume: `SPRINT-129-refinement-rev6.md`** (the `.claude/` original is gitignored) |
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

## RESUME STATE (2026-08-13 — for a different-device pickup)

**Done + committed + pushed:** TASK-A (skill `4f41995`, Codex-approved) · TASK-B (register +
inventory + this sprint doc `0be8c15`) · SEC-026 SSH.NET CVE CI-health fix (`0110c3a`, build-and-test
CONFIRMED green; smoke was still running at last check — verify on resume). Latest `origin/master`
tip carries all of it.

**The one gate to TASK-C — the owner-held calibration manifest.** Already sealed + given to the owner
in-conversation (out of band, so it's not in the repo): **SEC-020, SEC-021, SEC-025**, baseline
`e955e13`, hash `98622971e25ec663fc17375aa374d77707974369b7f5d939e7ba28d00853022e`. On resume, the
owner confirms they hold this; then the sweep runs.

**To run the sweep (TASK-C) — the recipe (from the tracked refinement `SPRINT-129-refinement-rev6.md`):**
1. `git worktree add <path> e955e13` — the sweep worktree (answer-bearing docs absent there).
2. Fan-out read-only (`Explore`-profile, no shell) agents per slice (i–v); fixed per-slice trust-
   boundary prompt templates (no per-hole slot); Orchestrator is sole ledger writer + SEC-id assignor.
3. Ledger `results.tsv` (schema in the refinement); discovery/calibration slices code-anchored only,
   no answer-artifact citation; revisit slice (iv) handed its SEC target out of band + excluded from
   the calibration count.
4. Codex regenerates + diffs the coverage inventory (falsifiability backstop); archive
   calibration-slice prompts verbatim for the external prompt-audit.
5. TASK-D refute panel per candidate (Critical/High double-refuted: agent + Codex).
6. Score calibration (≥2 of 3 rediscovered code-anchored) → TASK-E owner adjudication → fill the
   adjudication records below → remediation-sprint proposal.

## TASK-C — the sweep (RUN — round 1 complete, 2026-08-14)

Ran per the recipe: worktree `git worktree add --detach <path> e955e13`, isolation verified (register,
sprint doc, refinement snapshot, and the untracked refinement all ABSENT from the worktree; the
threat-model skill present). Five read-only `Explore`-profile discovery/revisit agents, one per slice,
blind to the answer key; Orchestrator sole ledger writer. Ledger: 36 findings (gitignored sweep dir).

### Calibration — PASS 3/3 (the method's self-test)
All THREE held-out calibration holes were independently rediscovered by the discovery slices, code-
anchored, with no answer-artifact citation: **SEC-025** (localStorage bearer token) by slice (v) at
`AuthContext.tsx:109`; **SEC-020** (`Auth:UseDatabase` fail-open) by slice (iii) at
`Program.cs:371`/`AuthEndpoints.cs:77-93`; **SEC-021** (Orchestrator task IDOR) by slice (ii) at
`Program.cs:66-70`. Miss rate 0/3. The bar (≥2/3) is exceeded — the sweep method independently finds
known holes.

### Coverage (round 1)
Slice (i) tiers: all 137 Backend endpoints' verb→policy→floor mapped + auth-core deep-read (1 finding,
6 areas ruled clean). (ii) service↔service: 4 services + all HTTP-client sites (5 findings, SSRF ruled
out). (iii) deploy/CI: 3 workflows + both compose + mocks + 7 Dockerfiles + all appsettings (9
findings). (iv) revisit: all 8 ruled residuals re-attacked with fresh evidence. (v) browser-auth:
bounded pass clean except the token-storage class. Round-2 candidates: the deeper bodies of the
GlobalAdmin-config endpoints + settlement/reversal internals (floors confirmed, bodies not fully
traced); the full frontend sweep.

### TASK-D — refute panel (adversarial verification)
The five new/overturned High findings each passed a fresh refuter (given only claim+evidence);
**SEC-009 and SEC-027 double-refuted (agent + Codex)**. All five CONFIRMED. Severity nuance applied:
SEC-015 scoped to dev/CI/demo (a production fail-fast exists); SEC-019 tempered (the `claude-code-
action` has a built-in write-access actor check, so the workflow-layer gap is defense-in-depth).

## Adjudication records (round 1) — OWNER RULING PENDING on each

*Prior disposition · sweep verdict · evidence · refuter · recommended disposition. Owner rules
re-ratified / overturned / accepted / fix-next-sprint on each.*

### Revisit residuals (Groups 1–3)
- **sec-001** JWT-role-revoke TOCTOU — RE-RATIFIED (bounded, auditable; role read outside the 2 held advisories, `DesignatedApproverAuthorizer.cs:221`). Rec: **carry** (fix bundles with SEC-003).
- **sec-002** user-deactivation 3-paths/2-domains — RE-RATIFIED (flag non-corrupting; companion-state drift residual). Rec: **carry**.
- **sec-003** JWT 8h no revocation — RE-RATIFIED + noted as the AMPLIFIER turning SEC-001/002 into ≤8h windows. Rec: **fix-next** (short TTL / revocation list) — platform.
- **sec-004** secondary-principal sibling escape — **DOWNGRADE**: structurally CLOSED by the S92 flatten (`ValidateSameOrganisationAsync` = exact `primary_org` equality). Rec: **close** (fix the stale "tree" error text only).
- **sec-006** 9-read tier-gate remainder — RE-RATIFIED (timing rule, not an access boundary; tracked RES-002). Rec: **fix-next** (finish the tier gate) or **accept**.
- **sec-009** HR/GlobalAdmin self-approval — **OVERTURN (worse)**: fully reachable, structurally unguarded on the org-scope leg, mis-audited as `ORG_SCOPE_FALLBACK`; GlobalAdmin unconditional. Triple-confirmed (slice i + slice iv + refuter + Codex). RES-003 class OPEN, owner-unruled. Rec: **FIX-NEXT (highest priority)** — add the `actor != employee` self-guard on the org-scope approve/reject/reopen legs.
- **sec-013** prefetched-authority fail-open — **DOWNGRADE**: mitigated in current code (resolver routes out-of-scope to live SQL; the GATE source fails CLOSED). Rec: **close**.
- **sec-014** governance-hop confused-deputy — RE-RATIFIED (rule is pure, no data reachable; missing defense-in-depth only). Rec: **accept** or **fix-next** (bind subject↔employeeId at the rule engine).

### Deployment-config (Group 4)
- **sec-015** committed JWT signing key + code fallback — CONFIRMED (dev/CI/demo; prod fail-fast exists). Rec: **fix-next** (env-only key, remove the code fallback).
- **sec-016** committed DB password — CONFIRMED. Rec: **accept (hobby)** / cleanup at go-serious.
- **sec-017** shared demo password incl GlobalAdmin — CONFIRMED. Rec: **accept (hobby)**.
- **sec-018** unauth mock services disclose payloads — CONFIRMED (dev harness). Rec: **accept**.
- **sec-019** workflow secret on untrusted triggers — CONFIRMED but TEMPERED (action's built-in write-access check is the real gate). Rec: **fix-next (cheap)** — add an `author_association` gate as defense-in-depth.

### Swept-unruled (Group 5) + new
- **sec-020** `Auth:UseDatabase` fail-open (admin01/admin=GlobalAdmin) — CONFIRMED (calibration). Rec: **FIX-NEXT** — default the flag to TRUE / remove the in-memory admin table.
- **sec-021** Orchestrator task-read IDOR — CONFIRMED (calibration). Rec: **fix-next** — add an ownership/scope check.
- **sec-022** Orchestrator `/execute` + raw-auth-forward — **SPLIT**: the `/execute` scope gate is SOUND (overturned-as-safe); only the raw-bearer-forward half stands (Low, blast-radius). Rec: **downgrade** to the forward half.
- **sec-023** `external/send` unfloored JSON relay — CONFIRMED (mock downstream in this config). Rec: **fix-next** — add a role floor + schema.
- **sec-024** RuleEngine can't org-scope (config disclosure) — CONFIRMED (Authenticated-only, no DB). Rec: **accept** or **fix-next** (different control at the boundary).
- **sec-025** frontend localStorage bearer token — CONFIRMED (calibration). Rec: **fix-next / round-2** — the browser-token-storage redesign.
- **sec-027** service self-mints GlobalAdmin over shared key — CONFIRMED ×2 (agent + Codex): any key-holder (incl. low-trust External) mints GlobalAdmin; no per-service audience/identity. Rec: **FIX-NEXT (high)** — per-service identity / drop the self-minted GlobalAdmin.
- **sec-028** CI workflow no `permissions:` block — CONFIRMED (Low; fork PRs read-only bounds it). Rec: **fix-next (cheap)**.
- **sec-029** containers run as root — CONFIRMED (Info). Rec: **accept** / hardening backlog.
- **sec-030** UI role/scope hydrated from client-writable localStorage — CONFIRMED (Medium; UI-gating only, backend enforces). Rec: **accept** (rides with SEC-025).

## Owner adjudication (2026-08-14) — RULED

Owner approved the proposed fix-next order and the hobby-stage accepts.
- **FIX-NEXT (remediation sprint, in priority order):** SEC-009 → SEC-020 → SEC-027 → SEC-015 →
  SEC-023 → SEC-019 → SEC-021 → SEC-028.
- **ACCEPTED (hobby-stage — cleanup deferred to IF the project goes serious):** SEC-016, SEC-017,
  SEC-018, SEC-029.
- **CLOSED (downgraded — better than recorded; no fix needed beyond cosmetics):** SEC-004 (fix stale
  "tree" error text only), SEC-013.
- **CARRIED (re-ratified, no action this cycle):** SEC-001, SEC-002, SEC-003 (platform: token TTL /
  revocation — bundles with any auth-lifetime work), SEC-006 (RES-002 tier-gate remainder), SEC-014,
  SEC-022 (forward half), SEC-024, SEC-030.
- **ROUND-2:** SEC-025 (browser token-storage redesign) folds into the round-2 frontend sweep.

## Remediation-sprint proposal (the "next remediation sprint" — S130 candidate)

Per the ROADMAP rolling-detail rule this is proposed, not hard-numbered. Scope = the 8 FIX-NEXT rows,
severity × invariant-impact ordered. All are small, bounded fixes:
1. **SEC-009** — add the `actor != employee` self-guard to the org-scope approve/reject/reopen legs
   (`ApprovalEndpoints.cs:235/302/431/1495`); pin with a RED test (HR-self-approve denied); keep the
   `ORG_SCOPE_FALLBACK` audit but distinguish a would-be self-approval. **The keystone fix.**
2. **SEC-020** — default `Auth:UseDatabase` to TRUE (fail closed) and/or delete the in-memory admin
   credential table; a missing env var must not serve `admin01/admin`.
3. **SEC-027** — give service-to-service tokens a per-service identity (audience/subject) and drop the
   self-minted `GlobalAdmin`; the classification fetch needs no admin role.
4. **SEC-015** — env-only signing key; remove the code `DevFallbackSigningKey`; rotate the committed
   dev key.
5. **SEC-023** — add a role floor + a typed schema to `external/send`.
6. **SEC-019** — add an `author_association` gate to `claude.yml` (defense-in-depth atop the action's
   own actor check).
7. **SEC-021** — add an ownership/scope check to `GET /orchestrator/tasks/{id}`.
8. **SEC-028** — add a least-privilege `permissions:` block to `ci.yml`.
Not an audit-sprint task (audit proposes, remediation fixes). Entered in the ROADMAP backlog.

## TASK-C round 2 (2026-08-14) — deeper config bodies + full frontend

Same `e955e13` worktree isolation. 2 agents (config/settlement bodies + full frontend) + 1 refuter.
Calibration already validated in round 1 — round 2 is pure coverage extension.

**Cleared (strong positives):** the money-adjacent **settlement / reversal / termination flows are
robust** (quantities copied from row/snapshot, never client-recomputed; forfeit/waived/feriehindring/
§21 bounded guards; per-employee advisory lock + in-lock re-read + CAS). Injection + mass-assignment
clean. The **full frontend is clean** — zero XSS sinks, no open redirects, no prototype pollution, no
token/PII leakage, clean deps.

**New findings (register Group 8) — OWNER RULING PENDING:**
- **sec-032** Position-Override cross-tenant config write — CONFIRMED ×2, refuter **upgraded Medium→High**:
  all 7 routes only `LocalAdminOrAbove`, no org-scope check in any handler, resource is globally keyed,
  `ConfigResolutionService.cs:153` resolves it per-agreement/position with NO orgId → a single-institution
  LocalAdmin changes norm/flex for that position across ALL institutions. **Rec: FIX-NEXT (High) — add
  GlobalAdminOnly (match the siblings) OR a real org-scope binding.** New High → recommend slotting into
  the remediation list after SEC-009/020/027.
- **sec-033** config-value validation gap (PositionOverride + AgreementConfig + EntitlementConfig) —
  CONFIRMED. Rec: **fix-next** — add range/negativity validation + DB CHECK backstops on the
  money-adjacent numeric fields.
- **sec-034** Position-Override PUT re-key — CONFIRMED (Possible). Rec: **fix-next (cheap, rides with
  SEC-032/033 in the same file)** or accept.
- **sec-035** supersession audit-row omission — CONFIRMED (Possible, repo-invariant-dependent). Rec:
  **verify the repo invariant; fix-next if reproducible**, else close.
- **sec-031** missing CSP — CONFIRMED (Low). Rec: **fix-next (cheap)** — add a CSP header/meta.

**Round-2 residue (round-3 candidates, if ever):** persistence/outbox consumers + a dependency audit
were not swept. The sweep is otherwise comprehensive across the HTTP surface, auth chain, deploy, and
frontend.

### Round-2 owner ruling (2026-08-14) — RULED
Owner approved: **SEC-032 joins fix-next at #4** (after SEC-009/020/027); **SEC-033** fix-next;
**SEC-034/035** verify-then-fix / ride along; **SEC-031** cheap add. Final remediation order (10 items)
is in the ROADMAP security backlog. No item is CLOSED-as-accepted from round 2 (all are fix-next or
verify-then-fix).

---

## SWEEP COMPLETE (2026-08-14)

Two rounds, calibration **3/3**, ~46 ledger findings, adversarial refute panel (6 High double/single-
refuted, all CONFIRMED). Coverage: 137 Backend endpoints + auth chain (round 1) · service↔service +
deploy/CI + revisit (round 1) · config-CRUD bodies + settlement money flows + full frontend (round 2).
Net register: **2 overturns-worse (SEC-009, and SEC-032 upgraded to High), 2 downgrades/closes
(SEC-004, SEC-013), 1 split (SEC-022), 10 fix-next, the rest accepted/carried.** The audit sprint held
its contract: **no product code changed, 3269 test baseline carried** (S128 CI `31485462948`). The
follow-on is the remediation sprint (ROADMAP backlog); the audit method (skill + register + calibration
+ worktree isolation + refute panel) is now a repeatable harness for future sweeps.
