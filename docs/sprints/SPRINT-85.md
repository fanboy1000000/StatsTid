# Sprint 85 — Fix the roles/grant+revoke audit defect + close the unmasked privilege escalation (security)

| Field | Value |
|-------|-------|
| **Sprint** | 85 |
| **Status** | complete |
| **Start Date** | 2026-06-19 |
| **End Date** | 2026-06-20 |
| **Orchestrator Approved** | yes — 2026-06-20 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors (touched projects 0 warnings; pre-existing warn-opt-out debt unchanged) |
| **Test Verified** | yes — 8/8 security classes green independently (6 new RoleAssignmentGrantRevokeEndpointTests + 2 reconciled AdminAtomicTests); agent ran 27/27 (incl. DesignatedApproverAuthority); db-schema in sync; fresh-seed CHECK validation 0 violations; full pyramid → CI on push |

## Sprint Goal
Fix the pre-existing production defect discovered in S84 — `POST /api/admin/roles/grant` + `/revoke` 500 on every call (broken `role_assignment_audit` INSERT) — **AND close the privilege-escalation hole the fix unmasks** (the grant guard keys "global" off `OrgId is null` not `ScopeType=='GLOBAL'`, so a working endpoint would let a LocalAdmin mint an effective-global scope). Add the missing success-path + escalation tests (the existing test used a wrong-shaped fixture + forced a pre-INSERT rollback), reconcile that fixture, add a DB-CHECK backstop, and do a bounded audit-INSERT sweep. **Security fix (P7) + a small additive schema change.**

Refinement: `.claude/refinements/REFINEMENT-roles-grant-audit-defect.md` (dual-lens reviewed; Codex caught the escalation BLOCKER). Owner rulings 2026-06-19: OQ-1 bounded sweep; OQ-2 add the DB CHECK.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| check_docs hard checks | CLEAN | db-schema in sync; KB INDEX complete; through S84 |
| Working tree | CLEAN | at S84 tip `c376c40` |
| **CHECK-predicate pre-validation** | CLEAN | `(scope_type='GLOBAL')=(org_id IS NULL)` holds for ALL 3,827 live role_assignments (baseline + 3,350 demo): 0 violations, 0 escalation-shaped rows → the additive CHECK + legacy ALTER are safe |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P7 auth/RBAC + a live privilege-escalation + an additive schema change) |
| **External Codex** | invoked 2026-06-19 — cycle 1: 1 BLOCKER, 3 WARNING, 3 NOTE; cycle 2: "Clean — BLOCKERs resolved" |
| **Internal Reviewer** | invoked 2026-06-19 — cycle 1: 0 BLOCKER, 3 WARNING, 2 NOTE |
| **BLOCKERs resolved before Step 1** | yes — the role_id↔scope escalation vector B (folded into guard 2(B) + CHECK (ii) + escalation-B test) |

### Findings (cycle 1)

_Codex:_
- **BLOCKER** — guard incomplete for vector B: a `role_id='GLOBAL_ADMIN'` row with non-GLOBAL scope still mints `ActorRole=GlobalAdmin` (role→JWT-role mapping; `GlobalAdminOnly` ignores scope). **RESOLVED**: guard 2(B) + CHECK (ii) `role_id<>'GLOBAL_ADMIN' OR scope_type='GLOBAL'` + escalation-B test; the `:1650` hierarchy check already 403s lower actors (confirmed, test it).
- WARNING — revoke keys off `org_id` not `scope_type`. **RESOLVED** (revoke guard bullet).
- WARNING — shape CHECK alone doesn't catch vector B. **RESOLVED** (CHECK ii).
- WARNING — a test inserts `LOCAL_LEADER/NULL/GLOBAL`; the shape CHECK allows it (no break). Noted.
- NOTE — audit INSERT fix shape correct; FE caller exists (`RoleManagement.tsx`); no ADR conflict; SECURITY.md:106 requires GlobalAdmin gates to need a GLOBAL scope (supports the fix).

_Internal Reviewer:_
- (0 BLOCKER) — plan sound; escalation confirmed real (`RoleScope.CoversOrg:13` true for any GLOBAL; `OrgScopeValidator` short-circuits on GLOBAL).
- WARNING — name the test HOST: the new success/escalation tests need a WAF endpoint harness in `tests/StatsTid.Tests.Regression`, NOT the direct-orchestration `AdminAtomicTests`. **RESOLVED**.
- WARNING — grant returns **201** (`Results.Created`), not 200. **RESOLVED**.
- WARNING — the `AdminAtomicTests` inline INSERT must be fully rewritten (not tweaked) — action='GRANT', no actor_id/actor_role, UUID audit_id all violate the real schema. **RESOLVED**.
- NOTE — FE shapes already match (no FE change; `useAdmin.ts`); consider extracting the audit INSERT to `RoleAssignmentRepository` to kill the test-mirror coupling (recorded as optional follow-up; kept inline for the minimal security diff).

### Resolution
All folded into TASK-8501. 1 BLOCKER resolved (vector B). Cycle-2 (verification) runs before Step 1.

## Architectural Constraints
- [x] P3 — event-sourcing/audit: fix isolated to the `role_assignment_audit` INSERT; `RoleAssignmentGranted/Revoked` event + `audit_projection` (ADR-026) emission unchanged, same tx (both lenses confirmed)
- [x] P4 — version-correctness: n/a
- [x] P7 — security/access-control: BOTH escalation vectors closed (shape guard + role↔scope guard, `HasGlobalScope` role-floored) + 2 DB-CHECK backstops; revoke keyed on scope_type
- [x] P8 — CI/CD: targeted security classes green; full pyramid + fresh smoke → CI on push
- [x] Schema: TWO additive CHECKs on `role_assignments` + idempotent guarded legacy ALTER + ledger row + db-schema regen (pre-validated 0 violations on 3,827 live rows)

## Task Log

### TASK-8501 — The fix (audit INSERT + escalation guard + DB CHECK + tests + sweep)

| Field | Value |
|-------|-------|
| **ID** | TASK-8501 |
| **Status** | planned |
| **Agent** | Security/Backend + Data Model (cross-scope: endpoints + init.sql + tests — one coupled security fix) |
| **Components** | `AdminEndpoints.cs` (grant `:~1614`/revoke `:~1760`), `docker/postgres/init.sql` (role_assignments CHECK + legacy ALTER), `docs/generated/db-schema.md` (regen), tests |
| **KB Refs** | ADR-007/009 (RBAC), ADR-018 (outbox/tx), ADR-026 (audit_projection) |
| **Orchestrator Approved** | no |

**Description**:
1. **Audit INSERT (grant + revoke)** → fix to the real schema (`init.sql:663`): `INSERT INTO role_assignment_audit (assignment_id, action, actor_id, actor_role, details) VALUES (@assignmentId, 'GRANTED'/'REVOKED', @actorId, @actorRole, @details)` — `audit_id` BIGSERIAL + `timestamp` DEFAULT auto (omit both); `actor_id`=`actor.ActorId ?? "system"`, `actor_role`=`actor.ActorRole ?? <sensible default>`; `details` a small JSON object via `NpgsqlDbType.Jsonb` / `::jsonb` (matching the `users_audit`/`*_audit` idiom). action `'GRANTED'`/`'REVOKED'` (NOT `'GRANT'/'REVOKE'`).
2. **Escalation guard (grant `:~1628`) — TWO vectors (Step-0b Codex BLOCKER added vector B):**
   - **(A) shape:** gate on `ScopeType`, not `OrgId is null`: `ScopeType=='GLOBAL'` ⟹ require `OrgId IS NULL` AND `HasGlobalScope(actor)`; `ScopeType!='GLOBAL'` ⟹ require `OrgId` non-null + the existing org-scope check.
   - **(B) role↔scope coupling:** an inherently-global `role_id` (`GLOBAL_ADMIN`) ⟹ require `ScopeType=='GLOBAL'` + `OrgId IS NULL` + `HasGlobalScope(actor)` — because `AuthEndpoints` maps `role_id`→the JWT primary role (`ActorRole`) and `GlobalAdminOnly` checks the ROLE, not the scope; so a `GLOBAL_ADMIN` row with a non-GLOBAL scope still mints an effective GlobalAdmin. The existing privilege-hierarchy check (`:1650-1654`) already 403s a *lower* actor granting `GLOBAL_ADMIN` (confirm + test that), so this is defense-in-depth + closes the semantically-inconsistent shape; still mandatory.
   - **Revoke guard (`:~1790`):** read the stored `scope_type` and gate global-ness on `scope_type=='GLOBAL'` (not `org_id is null`) — Codex WARNING; lower-risk (de-privileging) but key it consistently.
3. **DB CHECKs (OQ-2 + the vector-B backstop)** → TWO additive CHECKs on `role_assignments` in init.sql (fresh) + idempotent guarded legacy ALTERs + `tools/generate_db_schema.py` regen: (i) `((scope_type='GLOBAL') = (org_id IS NULL))` [shape]; (ii) `(role_id <> 'GLOBAL_ADMIN' OR scope_type = 'GLOBAL')` [role↔scope]. **Both pre-validated against 3,827 live rows: 0 violations** (0 GLOBAL_ADMIN-with-non-GLOBAL-scope, 0 non-GLOBAL_ADMIN-with-GLOBAL-scope).
4. **Tests** — the new success-path + escalation tests go in a **WAF endpoint-test class in `tests/StatsTid.Tests.Regression`** (the harness that boots the API + hits the real init.sql schema — e.g. the pattern in `DesignatedApproverAuthorityTests`/`EmployeeProfileEndpointTests`), NOT the direct-orchestration `AdminAtomicTests` (Reviewer WARNING). Cover: grant→**201** (`Results.Created`, `:1737`) + role_assignments row + role_assignment_audit row (action='GRANTED', actor_id/actor_role, details JSON); revoke→**200** + is_active=FALSE + action='REVOKED'; **escalation A: LocalAdmin granting `{scopeType:'GLOBAL', orgId:'STY01'}`→403**; **escalation B: granting `{roleId:'GLOBAL_ADMIN', scopeType:'ORG_ONLY', orgId:'STY01'}`→403** (+ confirm a lower actor granting GLOBAL_ADMIN is 403 via the hierarchy check); all RED on current code, green after. **Reconcile** `AdminAtomicTests.cs` fixture DDL (`:255-262`) AND fully rewrite the inline INSERT (`:157-158`) to the real schema (real column list `assignment_id, action='GRANTED', actor_id, actor_role, details`; not a tweak — the current INSERT violates 4 columns/CHECK) so the rollback test agrees on the table; keep it green.
5. **Bounded sweep (OQ-1)** → grep `INSERT INTO *_audit` across `src/`; cross-check columns/CHECK-values vs schema; fix any other confirmed mismatch (or record "none found").
6. **Audit INSERT kept INLINE** (decision recorded): smaller/lower-risk diff for a security fix; the new WAF tests exercise the REAL endpoint against the REAL schema (not a mirror fixture), which breaks the test-mirror coupling that masked the bug — so the recurrence class is addressed without the larger repo-extraction refactor (recorded as an optional follow-up: move grant/revoke audit writes into `RoleAssignmentRepository`).
7. **FE: no change needed** — `RoleManagement.tsx` / `useAdmin.ts` already POST the matching shapes (`{userId, roleId, orgId?, scopeType, expiresAt?}` / `{userId, assignmentId}`); the endpoint is a LIVE caller that 500s today. The fresh smoke should exercise the grant path end-to-end. (Optional UX polish — clear/disable org when GLOBAL is selected — deferred.)

**Validation Criteria**:
- [ ] grant→201 / revoke→200 + persist role_assignments + role_assignment_audit rows correctly (one tx); the `RoleAssignmentGranted/Revoked` event + audit_projection unchanged.
- [ ] Escalation A (GLOBAL+org) 403 + Escalation B (GLOBAL_ADMIN+non-GLOBAL) 403 + lower-actor-grants-GLOBAL_ADMIN 403; success-path grant/revoke; all RED-on-old, green after (WAF endpoint-test class).
- [ ] `AdminAtomicTests` fixture DDL + inline INSERT reconciled to the real schema; the rollback test still green.
- [ ] Both CHECKs in init.sql + guarded legacy ALTERs + db-schema regen green; 0 existing-row violations (re-confirm on the fresh smoke).
- [ ] Bounded sweep done; result recorded.
- [ ] `dotnet build` 0/0.

**Files Changed**: `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs`, `docker/postgres/init.sql`, `docs/generated/db-schema.md`, `tests/**`

---

### TASK-8502 — Validate + docs + Step-7a + close (Orchestrator)

| Field | Value |
|-------|-------|
| **ID** | TASK-8502 |
| **Status** | planned |
| **Agent** | Orchestrator |
| **Components** | validation, `docs/QUALITY.md`, `ROADMAP.md` (mark the S84 roles/grant follow-up DONE), `docs/sprints/SPRINT-85.md` |
| **Orchestrator Approved** | no |

**Description**: Build + the Admin/auth regression classes (`AdminAtomicTests`, the new tests, `DesignatedApproverAuthorityTests`) + a **fresh smoke** (schema change → greenfield init.sql with the new CHECK). Step-5a high-risk external review (auth). Update QUALITY/ROADMAP (the S84 `roles/grant` follow-up → DONE; Security/Backend-API grade note). Step-7a dual-lens. Commit + push + CI-verify; MEMORY.

**Validation Criteria**:
- [ ] Full pyramid green (incl. fresh smoke); CI green on push.
- [ ] Docs updated; ROADMAP follow-up marked DONE; `check_docs` green.

**Files Changed**: `docs/**`, `ROADMAP.md`

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| New WAF security tests (`RoleAssignmentGrantRevokeEndpointTests`) | 6 | green — grant→201, revoke→200, escalation A 403, escalation B 403, hierarchy 403, GlobalAdmin GLOBAL grant→201 |
| Reconciled `AdminAtomicTests` | 2 | green (fixture DDL + inline INSERT aligned to real schema) |
| `DesignatedApproverAuthorityTests` (regression-adjacent) | 19 | green (agent run) |
| Unit / full Regression / Smoke / FE / E2E | unchanged surface | → CI on push (the additive CHECKs are pre-validated 0-violation; all test `role_assignments` INSERTs audited CHECK-conformant) |

**RED-on-old proof (two-part — the strong discriminator):** Part 1 (original code) — all success/revoke + both escalation tests FAIL (500 from the audit bug). Part 2 (audit INSERT fixed, guard left OLD) — escalations A & B return **201 + a minted row** (the live escalation), proving the escalation tests pin the GUARD, not just the audit bug. After the full fix: 8/8 green. db-schema in sync; fresh-seed loads the baseline with both CHECKs (0 violations).

## Sprint Retrospective

**What went well**: [[review-lens-complementarity]] was decisive AGAIN — Step-0b's external lens caught a SECOND escalation vector (the `role_id='GLOBAL_ADMIN'` + non-GLOBAL-scope path that the role→JWT-role mapping turns into effective GlobalAdmin) that the shape-only guard + the internal lens both missed; folded in pre-code (guard B + CHECK ii). The agent's two-part RED-on-old proof (audit-fixed-but-guard-old → 201) is the gold standard for pinning a security guard, not just the surface bug. The bug's root — an inline endpoint INSERT diverging from the schema, mirrored by a private test fixture so nothing exercised the real path — is now closed by WAF tests against the real schema.

**What to improve**: The original S84-discovered defect was framed as a 1-line audit fix; it expanded (correctly) into a 2-vector security fix + 2 DB CHECKs once the review surfaced that fixing the 500 UNMASKS the escalation. The lesson: "make the broken thing work" on an auth surface always warrants asking "what does making it work expose?" The inline-SQL/test-mirror coupling that caused the masking is resolved for these endpoints but exists elsewhere — the optional `RoleAssignmentRepository` extraction is a recorded follow-up to kill the pattern class.

**Knowledge produced**: No new KB entry (a contained security fix). The S84 `roles/grant` ROADMAP follow-up is marked DONE. Optional follow-up recorded: extract role grant/revoke audit writes to `RoleAssignmentRepository` (eliminates the inline-SQL test-mirror coupling).
