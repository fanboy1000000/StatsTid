# Sprint 134 — Audit-scope + Observability + Docs (fix-next program, increment 3 / final)

| Field | Value |
|-------|-------|
| **Sprint** | 134 |
| **Status** | complete |
| **Start Date** | 2026-08-25 |
| **End Date** | 2026-08-25 |
| **Orchestrator Approved** | yes — 2026-08-25 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors (Release, final merged tree) |
| **Test Verified** | Unit green locally (994 pass, incl. the 24 denial-trace tests); Docker-gated regression + the DemoSeed CI step **CI-pending** — no local Docker, verifies in the CI run this sprint's push triggers (established no-local-Docker close posture, per S132/S133; Step-7a Reviewer no-action NOTE). **Precondition checked at push (2026-08-25):** S133's run `32736662066` **FAILED** at the OpenAPI drift gate — S133 added `TimeEntry.sourceStintId` without regenerating `docs/api/openapi.json`, and the gate stopped the run before unit/DemoSeed/regression executed, so S133's CI-pending verification never ran. Remediated in this push (spec regenerated; sole delta = the TimeEntry schema — S134's Contracts edits are doc-only and don't perturb the spec). The push-triggered run therefore verifies S132+S133+S134 regression together. |

## Sprint Goal
Increment 3 (final) of the S131 fix-next program. Close the last cluster of ratified High findings — the
audit trail's **structural wiring** gaps and the doc-canon rot — so auditability + observability hold and the
docs that FEED the agents stop lying. Three distinct wiring defects: audit middleware runs AFTER authz (no
denial trace — QUAL-009/SEC-038); the Payroll host registers NO audit middleware (no calc audit rows —
QUAL-003 half A); the manifest-id enrichment is never called (no ADR-016 D10 linkage — QUAL-003 half B).
Plus observability (QUAL-008 ambient correlation id + QUAL-065 cross-service propagation) and the doc pass.

**Refinement**: `.claude/refinements/REFINEMENT-s134-audit-obs-docs.md` (READY — dual-lens Step-4 clean; owner
ruled OQ-1 admin/mutating-row + OQ-2 pull-QUAL-065 + OQ-3 defer-PAT-pass, 2026-08-25). **Baseline HEAD**:
`a8dbb25` (S133 close). **NOTE:** S133's Docker-gated CI-green is a precondition for THIS sprint's CLOSE
(guard #2) — confirm before close.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Baseline `a8dbb25` clean; no stale paths introduced. |
| Pattern compliance spot-check | CLEAN | `FindFirst("scopes")` (FAIL-001): 0 in src. |
| Orphan detection | CLEAN | `StampAuditContext` (`PeriodCalculationService.cs:811`) IS a known orphan — that IS QUAL-003 (this sprint wires it). |
| Documentation drift | noted | The INDEX drift (S129/S130 stale-in-progress; S131/S132/S133 rows) is a S134 task (TASK-13401). |
| Quality grade review | stable | Re-graded at close. |

**Audit-wiring confirmed in code (Step-0a):** `Backend/Program.cs:486` `UseAuthorization()` → `:487`
`UseMiddleware<AuditLoggingMiddleware>()` (middleware after authz → QUAL-009). `Payroll Program.cs:64`
registers only `AuditProjectionRepository` (no `AuditLogRepository`/middleware → QUAL-003).
`CalculateWithOutcomeAsync` (`:292`, returns `PeriodCalculationOutcome`), `StampAuditContext` (`:811`).

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — touches the payroll-export path (QUAL-003 edits the `calculate-and-export` endpoint body) + the authz pipeline (QUAL-009) + cross-service transport (QUAL-065). |
| **External Codex** | invoked 2026-08-25 — cycle 1: 2 BLOCKER/2 WARNING; cycle 2: 1 NEW BLOCKER (result-handler must delegate to the default); cycle 3: **"Cleared - no residual findings."** (inline) |
| **Internal Reviewer** | invoked 2026-08-25 — cycle 1: 1 BLOCKER/7 WARNING/4 NOTE; cycle 2: **READY-TO-DECOMPOSE** (all 12 absorptions verified; its one NOTE = the same delegation point Codex blocked, already fixed) |
| **BLOCKERs resolved before Step 1** | **yes** — Codex cycle 3 cleared; Reviewer cycle 2 READY-TO-DECOMPOSE. Step-0b COMPLETE 2026-08-25. |

### Findings (cycle 1)

_Codex (external):_
- BLOCKER — TASK-13403 must state the explicit code change for the before-authz denial row *(SUPERSEDED by Reviewer WARNING-3 — it's a NEW denial-gated middleware, NOT a move of the existing one; moving would double-write for allowed requests)*.
- BLOCKER — TASK-13403 lacks the cross-domain label *(subsumed by Reviewer WARNING-7 — all 5 hosts + `src/Auth/**`, not just Backend)*.
- WARNING — TASK-13405 code-comment edits lack a cross-domain label. WARNING — QUAL-009 needs a deny-by-default classification test. NOTE — TASK-13404 Step-5a + zero-output assertion + probes correctly present.

_Internal Reviewer (code-verified):_
- **BLOCKER — TASK-13404:** the `CalculateAsync→CalculateWithOutcomeAsync` switch is impossible as written — the endpoint (`Payroll/Program.cs:249`) calls the `[Obsolete]` PLANLESS `CalculateAsync` (`:571`); `CalculateWithOutcomeAsync` (`:292`) is PLAN-FIRST and there is NO planless outcome overload (plan-building is the private `BuildPlanForLegacyCallersAsync` `:628`). **Fix:** add a planless `CalculateWithOutcomeAsync` overload mirroring the `:571` shim exactly → zero-output-change by construction.
- WARNING-2 — `IAuthorizationMiddlewareResultHandler` does NOT exist in src; must be authored + registered (replacing the default) in all 5 hosts that call `AddStatsTidPolicies`.
- WARNING-3 — the denial-ROW middleware is a NEW before-authz middleware (not the existing post-authz `AuditLoggingMiddleware`); must be **denial-gated** (write only when the Items reason is present) or an allowed admin request gets TWO rows; needs `AuditLogRepository` per-host (only Backend + Payroll have it).
- WARNING-4 — the row-count probe mis-attributes: `RetroactiveCorrectionService` writes `audit_projection` (different table) on `/recalculate` (never called by calc-and-export). Assert exactly ONE `audit_log` row FILTERED by this calc's manifest-id, not a raw count.
- WARNING-5 — TASK-13404 primary agent is **Payroll Integration**, not API Integration (all edits under `Integrations/**/Payroll/**`).
- WARNING-6/7 — TASK-13402 (`src/Auth/**` CorrelationIdMiddleware + `Backend/**/Http/` forwarder) + TASK-13403 (5 host `Program.cs` + `src/Auth/**` ScopeAuthorizationHandler) need explicit cross-domain-authorized labels.
- WARNING-8 — 13403 ∥ 13404 collide on `Payroll/Program.cs` + 13403 depends on 13404's repo → **serialize 13402 → 13404 → 13403**; 13405 parallel.
- NOTE-9 — QUAL-065: read the ambient id from `HttpContext.Items[CorrelationIdMiddleware.ItemKey]` (`:30`), not the response header (the forwarder reads Request.Headers → drops a frontend-minted id). NOTE-11 — QUAL-093 same over-fix hazard: `BalanceEndpoints.cs:632` bundles VACATION (Sep correct) + SPECIAL_HOLIDAY — fix only the SPECIAL_HOLIDAY half. NOTE-12 — positive: the StampAuditContext→ManifestIdItemKey→details chain, the post-`_next` swallowing try/catch (payroll boundary), and the 116/117 citations all confirmed.

### Resolution
All absorbed by plan edit (cycle 1): (1) TASK-13404 — the planless `CalculateWithOutcomeAsync` overload seam + the probe's manifest-id-filtered single-row assertion + reassigned to **Payroll Integration**; (2) TASK-13403 — reframed as **author + register a custom `IAuthorizationMiddlewareResultHandler` tree-wide** (annotates the Items reason + emits the log) + a **NEW denial-gated before-authz row middleware** (no double-write; per-host `AuditLogRepository` DI enumerated / scoped to admin-mutating hosts) + the cross-domain label (5 host `Program.cs` + `src/Auth/**`) + the classification test; (3) TASK-13402 — cross-domain label (`src/Auth/**` + `Backend/**/Http/**`) + the Items-key seam; (4) TASK-13405 — code-comment cross-domain label + the QUAL-093 half-fix guard; (5) sequencing serialized 13402→13404→13403 (13405 parallel). Cycle-2 dual-lens verification runs before Step 1.

## Architectural Constraints Verified
- [x] Architectural integrity — audit/authz/correlation middleware ordering coherent across all 5 hosts (both Step-7a lenses)
- [x] Domain correctness / payroll boundary — QUAL-003 changes ZERO export payload / SLS lines (Step-5a-cleared + `AuditWiringExportParityTests` byte-identity; byte-identical by construction)
- [x] Auditability — denial trace + calc-audit + the ADR-016 D10 manifest-id linkage now exist end-to-end
- [x] Security & access control — denial capture is post-decision (the result-handler decorates + delegates, decides nothing); no false-positive on allowed requests; redaction applied (Reviewer-verified)
- [x] Integration isolation & delivery — audit + denial-row middleware both run post-`_next` in a swallowing try/catch (never touch the export tx)
- [x] CI/CD enforcement — composed-stack S73 probes (D10 cross-host + cross-service trace) authored (CI-gated); docs source-verified

## Scope & Task Decomposition

**In scope** (per ROADMAP program record + the READY refinement + owner rulings): QUAL-003 · QUAL-009/SEC-038 ·
QUAL-008 · **QUAL-065** (pulled in, OQ-2) · doc pass QUAL-010/011(=SEC-042)/012/080/090/093 · INDEX backfill.
**Out:** QUAL-145 → domain-semantics track; the PAT-authoring pass → deferred KB pass (OQ-3); QUAL-016
bespoke + QUAL-036 residual → their increments.

**Execution order (Reviewer WARNING-8 — 13403 ∥ 13404 collide on `Payroll/Program.cs`; 13403's denial-row
middleware consumes 13404's `AuditLogRepository`):** **TASK-13402 → TASK-13404 → TASK-13403**, with
**TASK-13405 in parallel** (docs + code-comments are the only truly disjoint file set) and TASK-13401
(housekeeping) anytime. TASK-13406 closes.

### TASK-13401 — Housekeeping (Orchestrator, docs)
INDEX backfill: add accurate rows for S129 (mark complete), S130 (complete), S131, S132, S133 to
`docs/sprints/INDEX.md` (S129/S130 currently stale-"in-progress"). Record QUAL-065 pulled into S134 + QUAL-145
routed to the domain-semantics track in the register.

### TASK-13402 — Observability: ambient correlation id + cross-service propagation — `Auth + Backend HTTP (cross-domain authorized: src/Auth/StatsTid.Auth/CorrelationIdMiddleware.cs + src/Backend/StatsTid.Backend.Api/Http/RuleEngineHeaderForwardingHandler.cs)` — QUAL-008 + QUAL-065 — SEQUENCED FIRST
QUAL-008: push a logging scope (`ILogger.BeginScope`) stamping the correlation id already in
`HttpContext.Items[CorrelationIdMiddleware.ItemKey]` (set at `CorrelationIdMiddleware.cs:30`) in the shared
`CorrelationIdMiddleware` (registered before auth in all 5 hosts) so the id is ambient on every log within a
correlated HTTP operation (**honest invariant — NOT startup/background/queue logs**). QUAL-065: fix
`RuleEngineHeaderForwardingHandler.CopyInboundHeader` (`:66`, today reads `Request.Headers`) to carry the
AMBIENT id from `Items` (the middleware writes the minted id to `Items` + the RESPONSE header but not the
inbound request headers — why a frontend-minted id drops at the hop). **Composed-stack cross-service trace
probe (S73):** a frontend-originated (header-less) action shows ONE id across Backend + rule-engine logs.
Sequenced first (conservative — the denial trace can read the Items id independently, but establishing it
first is clean).

### TASK-13403 — Audit: policy-denial trace — `Security & Compliance (extended into the 5 host Program.cs files [Backend/RuleEngine/Orchestrator/Payroll/External] + src/Auth/** ScopeAuthorizationHandler, cross-domain authorized)` — QUAL-009 / SEC-038 — SEQUENCED AFTER TASK-13404
Two pieces (neither exists yet — both authored):
1. **AUTHOR + register a custom `IAuthorizationMiddlewareResultHandler`** (none exists in src — replaces
   ASP.NET's default via `AddSingleton<IAuthorizationMiddlewareResultHandler,…>()`) in **all 5 hosts that call
   `AddStatsTidPolicies`**. It sees the FINAL aggregated `PolicyAuthorizationResult` (the individual
   `ScopeAuthorizationHandler` must NOT emit — a non-`Succeed` there isn't a final denial → would false-positive
   on allowed requests). On a denial it: (a) emits the structured, redacted, correlation-linked LOG (anonymous/
   system actor when unauthenticated; stable policy id; resource id/type only; NO claims/token/body — SEC-040
   `SanitizeForLog`), **level-disciplined** (`Information` for routine read-scope 403s, `Warning` for
   admin/mutating — aligns with the SEC-012 over-warning class); (b) annotates the denial reason + the route
   classification into `HttpContext.Items` for the row middleware. **It DECORATES, not replaces:** after its
   annotation/logging it MUST delegate to the built-in `AuthorizationMiddlewareResultHandler` for the ACTUAL
   result handling on EVERY path (success → the request proceeds; `Challenged` → 401; `Forbidden` → 403), so
   swapping the default never changes allowed-request or 401/403 behavior (Codex cycle-2 BLOCKER). It only
   OBSERVES the result + emits the trace; it decides nothing.
2. **A NEW denial-gated, before-authz audit-ROW middleware** (distinct from the existing POST-authz
   `AuditLoggingMiddleware:38` — do NOT move that one; moving would double-write a row for every ALLOWED
   admin request). It writes an `audit_log` row **only when the Items denial reason is present** (denial-gated
   → no double-write) AND **only for ADMIN-STRICT/MUTATING routes** (OQ-1(a): policy-metadata/attribute
   classification with a **deny-by-default guard** — a new sensitive route can't silently drop to log-only).
   **Best-effort** (the 403 still returns; swallowing try/catch logs its own write failure). Needs
   `AuditLogRepository` resolvable per-host — today only **Backend** (`Program.cs:100`) + **Payroll** (via
   TASK-13404) register it; **scope this row middleware to the hosts with admin/mutating routes** (or enumerate
   the per-host DI) so it never runs where the repo is unregistered.
**Tests (falsifiability):** a denial produces the trace (RED if the result-handler emission is removed); the
classification is proven — a routine READ-scope denial writes NO row (log only), an admin/mutating denial
writes exactly one row, and an UNKNOWN/new route defaults to auditable (deny-by-default), NOT log-only.

### TASK-13404 — Audit: Payroll-host audit middleware + manifest-id linkage (Payroll Integration Agent) — QUAL-003 — STEP-5a HIGH-RISK — SEQUENCED AFTER TASK-13402
(i) Register `AuditLogRepository` in the Payroll host DI (`Payroll Program.cs:64` area — today only
`AuditProjectionRepository`); (ii) register `AuditLoggingMiddleware` in the Payroll host (OQ-4b "build the
link"; ADR-016 NOT amended). (iii) **The endpoint's calc call is NOT a drop-in switch** — `Program.cs:249`
calls the `[Obsolete]` PLANLESS `CalculateAsync` (`:571`), while `CalculateWithOutcomeAsync` (`:292`) is
PLAN-FIRST and there is **no planless outcome overload**. **Add a planless `CalculateWithOutcomeAsync` overload
that mirrors the `:571` shim EXACTLY** (builds the plan via `BuildPlanForLegacyCallersAsync` `:628`, returns
the `PeriodCalculationOutcome` instead of `.Result`); the endpoint calls THAT, then `StampAuditContext` (`:811`
→ `ManifestIdItemKey`) so the manifest-id is stamped before the middleware persists. Building the plan via the
same shim is what makes zero-export-change true **by construction**. The D10 join reads `audit_log.details`
(the column `BuildDetailsPayload` `:75-92` writes — NOT ADR-016's stated `payload_jsonb`; the probe queries
`details`).
**Composed-stack smoke probe (S73):** drive the calc endpoint; assert **exactly ONE `audit_log` row FILTERED
by THIS calc's manifest-id** (`details`) — a raw row-count is wrong (the middleware also captures the other
in-request Payroll HTTP calls; and `RetroactiveCorrectionService` writes `audit_projection`, a DIFFERENT table,
only on `/recalculate` which calc-and-export never calls — do NOT account for it here) — and that the D10 join
returns that row. **Step-5a high-risk override FIRES** (edits the export endpoint body) → dual-lens per-task
review + **explicit regression assertion: ZERO change to the export payload / SLS lines** (payroll boundary;
the audit middleware runs post-`_next` in a swallowing try/catch — never touches the export tx/payload).

### TASK-13405 — Doc pass — `docs/` rows: Orchestrator; code-comment rows: `Backend (cross-domain authorized: src/Backend/**/Contracts/*Responses.cs + src/Backend/**/Endpoints/BalanceEndpoints.cs — comment-only, zero behavior change)` — QUAL-010/011/012/080/090/093 — PARALLEL
`docs/`: **QUAL-010** — `danish-agreements.md:117` (SPECIAL_HOLIDAY / særlige-feriedage reset month Sep→**Jan**,
cite both named sources; **DO NOT touch line 116 VACATION — its September is CORRECT**). **QUAL-011/SEC-042** —
`SECURITY.md:134-138` false authority model → the S105/ADR-038 as-built unit-leader edge (one fix, both rows).
**QUAL-012** — `legacy-db-upgrade-runbook.md` pointers → real DDL. **QUAL-080** — `ARCHITECTURE.md` nonexistent
folders + renamed class. Code comments (cross-domain, comment-only): **QUAL-090** — inspect all 17
`Contracts/*Responses.cs` "authority: init.sql" comments; fix ONLY the 3–4 wrong anchors. **QUAL-093** —
`BalanceEndpoints.cs:632-633` bundles VACATION + SPECIAL_HOLIDAY as "reset September"; **fix ONLY the
SPECIAL_HOLIDAY half (→ January) — VACATION-September is CORRECT** (mirror the QUAL-010 line-116 guard). Each
fix SOURCE-verified (anchors resolve; Jan cites both sources; authority vs S105/ADR-038) — not
`check_docs.py`-only.

### TASK-13406 — Validate + re-grade + close (Orchestrator)
`dotnet build` clean; suites green in CI (Docker/Python gates); QUALITY.md re-grade (Domain-Correctness +
Security + Backend-API + Documentation-canon); register rows flipped `fixed(S134)`; the fix-next PROGRAM's
ratified High/Critical/gate set fully closed (residuals on their tracks); Step-7a dual-lens; confirm S133 CI
green (guard #2).

## Execution status (updated 2026-08-25)
- **TASK-13403 (QUAL-009 / SEC-038 denial trace + QUAL-008 render fold-in)** — ✅ COMPLETE + MERGED. New: `DenialLoggingAuthorizationResultHandler` (custom `IAuthorizationMiddlewareResultHandler` that DECORATES the default — observes + logs + annotates `Items` on denial, DELEGATES the real result-handling on every path [success/challenge/forbid], decides nothing; registered inside `AddStatsTidPolicies` so all 5 hosts get it by construction), `PolicyDenialClassifier` (deny-by-default: RoutineRead only when positively proven, else AdminOrMutating), `LogSanitizer` (shared CR/LF strip), `PolicyDenialAuditRowMiddleware` (before-authz, denial-GATED — no double-write; admin/mutating only; best-effort; registered before `UseAuthorization` in Backend + Payroll). Level discipline (Info read / Warning admin-mutating); redaction (no claims/token/body). **QUAL-008 render fold-in:** `IncludeScopes=true` in all 5 hosts (closes QUAL-008 in the default sink). 24 unit tests green (no Docker) + 1 Docker-gated regression (CI). Full sln build **0 errors**, 994 unit pass. **Merge:** the Payroll `Program.cs` hunk was reconciled by hand onto 13404's version (git apply --reject; the middleware inserted before `UseAuthorization`). **FOLLOW-UP:** `LogSanitizer` (new, `src/Auth`) duplicates SEC-040's `private SanitizeForLog` in Backend `AuthEndpoints.cs` — consolidate the Backend one onto the shared helper (register as a small QUAL). Proposed PAT (denial-trace decorator) → deferred KB pass. QUAL-008/009 + SEC-038 → `fixed(S134)`.
- **TASK-13404 (QUAL-003 Payroll audit + manifest link)** — ✅ COMPLETE + MERGED + **Step-5a DUAL-LENS CLEARED**. Payroll host now registers `AuditLogRepository` + `AuditLoggingMiddleware` (post-auth, post-`_next` swallowing — never touches the export tx); a NEW planless `CalculateWithOutcomeAsync` overload mirrors the legacy `:571` shim EXACTLY (same `BuildPlanForLegacyCallersAsync` + core) so the export is **byte-identical by construction**; the endpoint calls it + `StampAuditContext` → the manifest-id lands in `audit_log.details` → the ADR-016 D10 join works. Regression parity test (byte-identical SLS, manifest normalized out) + S73 composed-stack smoke probe (exactly ONE audit_log row filtered by this manifest-id + D10 join returns it) — both Docker/CI-gated. Full sln build 0 errors, 970 unit pass. **Step-5a:** Codex "Cleared - no findings"; Reviewer **APPROVE-WITH-WARNINGS** (no invariant traded; the 1 WARNING = the `Directory.Build.props` QUAL-069 comment gone stale again → **FIXED by Orchestrator** [:249/CalculateAsync/:199 → :272/CalculateWithOutcomeAsync/:216]; NOTEs = awareness: the host now audits every non-/health request, consistent with Backend). **QUAL-069 posture preserved** (new overload `[Obsolete]` keeps exactly one live CS0618 → opt-out stays load-bearing). QUAL-003 → `fixed(S134)`.
- **TASK-13402 (QUAL-008 + QUAL-065)** — ✅ COMPLETE + MERGED. `CorrelationIdMiddleware` opens an `ILogger.BeginScope` with the `Items` id (ambient within a correlated request); `RuleEngineHeaderForwardingHandler.ForwardCorrelationId` now falls back to the ambient `Items` id when the inbound header is absent (fixes the frontend-minted-id cross-service drop). S73 cross-service trace probe added (CI-gated); QUAL-008 unit test green locally. Backend build 0 errors. **⚠ FOLD-IN → TASK-13403:** the scope makes the id AVAILABLE but the default console sink renders it only with `IncludeScopes=true` — TASK-13403 (which already edits all 5 host `Program.cs`) must set `IncludeScopes=true` tree-wide to actually CLOSE QUAL-008 in the default sink. Agent also replaced one regression test whose premise QUAL-065 inverts (legitimate; the "context-less → forward nothing" invariant stays pinned). Proposed PAT (ambient-correlation-id) → deferred KB pass.
- **TASK-13405a (docs/ — Orchestrator)** — ✅ COMPLETE. **QUAL-010** (`danish-agreements.md:117` SPECIAL_HOLIDAY Sep→**Jan**; line 116 VACATION untouched). **QUAL-080** (`ARCHITECTURE.md`: RuleEngine `Services/`→`Config/`+`Rules/`+`Contracts/`; Infra `Repositories/`→project root; `EventStoreRepository`→`PostgresEventStore` — verified vs the real tree). **QUAL-011/SEC-042** — SECURITY.md authority section corrected to the **ADR-038/S105 as-built** (verified in code: `DesignatedApproverAuthorizer.cs:179` is "the FIRST time `unit_leaders` legitimately enters authority" — D4 secondary-unit-leader approval exception; D5 keeps units scope-free, pinned by `UnitAuthorityAbsenceTests` which DOES exist — the finding's "test no longer exists" detail was inaccurate). **QUAL-012** — runbook `~L###` pointers → DURABLE grep references (the QUAL-090 root-cause fix applied here too; the S97 enheder row marked OBSOLETE — ADR-038 greenfield-reseeded units, no migration). Each fix SOURCE-verified, not `check_docs.py`-only.
- **TASK-13401 (housekeeping / INDEX backfill)** — ✅ COMPLETE. `docs/sprints/INDEX.md`: S129 + S130 flipped stale-"in-progress" → **complete** (closes `04a0121` / `7e4bb1b`); S131 + S132 rows ADDED (were missing); S133 row already present. (Register QUAL-065-in-scope + QUAL-145-routed recorded at close.)
- **TASK-13405b (code comments — QUAL-090 + QUAL-093)** — ✅ COMPLETE + MERGED (9 Backend files, comments-only, 0-errors build). QUAL-093: `BalanceEndpoints.cs:632` VACATION-Sep kept / SPECIAL_HOLIDAY→Jan. **QUAL-090 REGISTER CORRECTION:** the finding's "3-4 wrong anchors" undercounted — verification (mandated by the task) found the drift is SYSTEMATIC (a monotonic +16→+101 offset as `init.sql` grew); ALL 8 numeric-anchor `*Responses.cs` files were stale (4 pointed at grossly unrelated tables — the ones the finding named; the rest were off by the growing offset). Fixed ALL genuinely-wrong anchors against the live `init.sql` (fixing only 4 would leave QUAL-090 half-done). **FOLLOW-UP (root cause):** absolute `init.sql:NNN` anchors WILL rot again → migrate to durable CONSTRAINT-NAME / `table.column` refs + a `check_docs.py` anchor-resolution CI check (register as a new QUAL). Today's re-sync closes QUAL-090's stated defect; the durable fix is the follow-up.

## Falsifiability method
QUAL-009: a denial produces the log/row; RED if the result-handler emission is removed. QUAL-003: the
composed-stack probe asserts ROW-COUNT + manifest-id match (can't pass on an unrelated row). QUAL-008/065: a
cross-service trace probe shows one id end-to-end. Doc fixes: source-grounded (anchors resolve; the statutory
value cites its sources). Docker/Python gates in CI.

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | QUAL-010 corrects the særlige-feriedage reset-month doc fact (Jan); no rule-engine behavior change. |
| Wage type mappings produce correct SLS codes | pending | QUAL-003 asserts ZERO change to the export/SLS lines (Step-5a regression gate). |
| Overtime/supplement determinism | N/A | Unchanged. |
| Absence effects correct | N/A | Unchanged. |
| Retroactive recalculation stable | pending | The audit-middleware-in-Payroll must not perturb the correction path (row-count probe). |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — both lenses, 2026-08-25 |
| **Sprint-start commit** | `a8dbb25` (`a8dbb25f827c997e7eb8bd778ff01ccdd8e0783a`) |
| **Command** | Codex `codex review "<prompt>"` (full uncommitted diff via git); internal Reviewer Agent on the full diff |
| **Review Cycles** | 1 (both clean/near-clean; no BLOCKER) |
| **Findings** | 0 BLOCKER; 1 WARNING (absorbed); 3 no-action NOTEs |
| **Resolution** | all resolved |

### Findings
- **Codex — PASS (Clean):** "preserves authorization and payroll boundaries while adding denial-gated, best-effort observability and audit wiring." Artifact: `.claude/reviews/SPRINT-134-step7a-codex.md`.
- **Reviewer — CLOSE-WITH-WARNINGS:** no invariant at risk, no domain behaviour changed, no dishonest disposition. **WARNING** — the `Directory.Build.props` QUAL-069 comment line-anchors drifted (`:272`→ actual `:282`) when 13403's Payroll hunk was reconciled in after the comment was written — the very anchor-rot class the sprint fixes. **ABSORBED:** the comment now references the call BY NAME (durable, no line number). NOTEs (classifier HR exclusion; hardcoded policy set as documented safe-failure; row middleware only where `AuditLogRepository` is registered) — no action. Artifact: `.claude/reviews/SPRINT-134-step7a-reviewer.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 994 | all passing locally (incl. 24 denial-trace tests + the QUAL-008 scope test) |
| Regression (Docker-gated) | — | **CI-pending** — no local Docker; the D10 + cross-service + denial-trace probes verify in CI |
| DemoSeed | 153 | CI (runs in `build-and-test` per S133 QUAL-096) |
| **Full solution build** | — | `dotnet build StatsTid.sln` Release **0 errors** on the final merged tree |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 6 (13401 housekeeping · 13402 obs · 13403 denial · 13404 payroll-audit · 13405a docs + 13405b comments · 13406 close) |
| Domain agents dispatched | 4 code/doc agents + the Step-5a + Step-7a Reviewer passes |
| Reviewer/External findings | Step-0b: Codex 3 cycles (BLOCKERs→cleared) + Reviewer 2 cycles; Step-5a (QUAL-003): Codex 0 + Reviewer APPROVE-WITH-WARNINGS; Step-7a: Codex 0 + Reviewer 1W — all absorbed |
| Re-dispatches | 0 (all outputs accepted after review; the 13403 Payroll `Program.cs` hunk hand-reconciled at merge) |
| Real production bugs surfaced | 0 (S134 is additive; the doc pass corrected canon, no behaviour bug) |
| First-Pass Rate | 100% |

## Sprint Retrospective

**What went well:** the heavy Step-0b dual-lens earned its keep — the Reviewer's code-grounding caught a payroll-boundary trap (the calc-overload switch was impossible as written; the fix — a planless outcome overload mirroring the shim — made zero-export-change true *by construction*), and corrected the denial-audit design away from a double-writing middleware to a decorate-and-delegate handler. The doc pass turned up two sweep inaccuracies (QUAL-090's "3-4" was systematic drift; QUAL-011's "test no longer exists" was wrong) and one landmine (QUAL-010 line-116 vs 117) — all caught by source-verifying rather than trusting the finding text. The program's re-attack-at-fix-time discipline corrected the register repeatedly across all three increments.

**What to improve:** I shipped a line-anchor drift in the QUAL-069 comment *within the sprint that fixes line-anchor drift* — because I re-synced it before a later task shifted the lines. Lesson (now applied): reference call sites by NAME, never by line number, and re-verify any anchor written before all merges land. The worktree-branches-from-last-commit issue forced hand-reconciliation again (13403's Payroll hunk) — a mid-sprint commit would avoid it, but the commit-when-asked rule + the sprint-close model made manual dedup the right call.

**Knowledge produced:** two proposed PATs recorded as rationale (ambient-correlation-id; denial-trace decorate-and-delegate) → deferred to the KB pass with S133's set. Two S134-surfaced follow-ups registered (LogSanitizer consolidation; a `check_docs.py` anchor-resolution CI check — the QUAL-090/012 root-cause gate).

**Program close:** the S131 fix-next PROGRAM (S132 correctness core → S133 test integrity → S134 audit/obs/docs) is COMPLETE. Every ratified Critical/High/gate row is fixed or on a named track.
