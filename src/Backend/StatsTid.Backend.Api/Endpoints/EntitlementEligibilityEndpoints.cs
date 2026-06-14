using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S59 / TASK-5906 / ADR-029 — HR admin surface for (A) per-employee CHILD_SICK
/// entitlement eligibility and (B) the GDPR-sensitive <c>birth_date</c> (DOB) that
/// drives the age-derived SENIOR_DAY gate. Three endpoints, all
/// <c>HROrAbove</c> + <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/>
/// (cross-org binding is load-bearing per the EmployeeProfileEndpoints precedent —
/// the policy proves role + scope shape but does NOT bind the actor to the target
/// employee's organisation):
///   <list type="bullet">
///     <item><description>
///       <b>PUT /api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}</b>
///       — set <c>eligible</c> true/false for an employee. Conditional-write per ADR-019:
///       <c>If-None-Match: *</c> for first-create (opt-in absent-row default ⇒ ineligible,
///       refinement R1), <c>If-Match: "&lt;version&gt;"</c> for a subsequent toggle. Rejects
///       any <c>entitlementType</c> other than <c>CHILD_SICK</c> with 422 (SENIOR_DAY is fully
///       age-derived and is NEVER recorded here — refinement line 117; this scope guard lives
///       at the endpoint, not the DB, mirroring role_config_overrides). EffectiveFrom is
///       server-stamped to today (UTC) per ADR-023 D8. In one transaction (ADR-018 D3):
///       <see cref="EmployeeEntitlementEligibilityRepository.SupersedeAndCreateAsync"/>
///       (which writes the table-level eligibility audit row) + the
///       <see cref="EmployeeEntitlementEligibilitySet"/> outbox enqueue + the ADR-026
///       audit-projection row.
///     </description></item>
///     <item><description>
///       <b>GET /api/admin/employees/{employeeId}/birth-date</b> — HR-only DOB read,
///       ETag-stamped from <c>users.version</c> so the subsequent PUT can compose If-Match.
///       This is the ONLY read surface for DOB; it is access-controlled exactly like the
///       write (TASK-5909). DOB never appears in any Employee-facing DTO / JWT / export.
///     </description></item>
///     <item><description>
///       <b>PUT /api/admin/employees/{employeeId}/birth-date</b> — HR-only DOB write,
///       admin-strict If-Match (ADR-019 D2). In one transaction: FOR-UPDATE re-read +
///       version check, <see cref="UserRepository.SetBirthDateAsync"/> (bumps
///       <c>users.version</c>), and a <c>users_audit</c> UPDATED row (full-row JSONB snapshot
///       captures <c>birth_date</c> per init.sql — no audit-table change needed). DOB erasure
///       (Article 17) is deferred WITH ADR-025 D3 — not built this sprint.
///     </description></item>
///   </list>
/// </summary>
public static class EntitlementEligibilityEndpoints
{
    /// <summary>The only entitlement_type settable via the eligibility admin API this sprint
    /// (SENIOR_DAY is age-derived via DOB, never a manual toggle — refinement line 117).</summary>
    private const string SettableEntitlementType = "CHILD_SICK";

    public static WebApplication MapEntitlementEligibilityEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. PUT /api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}
        //
        // RBAC: HROrAbove + OrgScopeValidator (cross-org binding). Conditional write:
        //   • If-None-Match: *          → first-create (Case A; expectedVersion = null)
        //   • If-Match: "<version>"     → toggle existing (Case B same-day / Case C cross-day)
        // Rejects entitlementType ∉ {CHILD_SICK} with 422 (scope guard). EffectiveFrom is
        // server-stamped to today (UTC) per ADR-023 D8 — admins cannot back/forward-date.
        //
        // Error mapping:
        //   • 422 — entitlementType not settable (scope guard) OR backdate (repo defense)
        //   • 428 — missing / malformed precondition (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (stale If-Match version, ADR-019 D2)
        //   • 409 — EligibilityAlreadyExistsException (If-None-Match: * but a live row exists;
        //           create-only path, no blind overwrite — S59 Step-7a BLOCKER 1)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}", async (
            string employeeId,
            string entitlementType,
            SetEntitlementEligibilityRequest body,
            EmployeeEntitlementEligibilityRepository eligibilityRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EmployeeEntitlementEligibilitySet> auditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Cross-org binding — HROrAbove policy alone is not enough; bind the actor's
            // scopes to the target employee's organisation (FAIL-001: validator uses
            // FindAll, not FindFirst, on scopes). S76 B1: LocalHR floor — the ADMITTING scope
            // must itself be HR+ (a sub-HR mixed-role scope covering the org cannot admit).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Scope guard — only CHILD_SICK is settable; SENIOR_DAY and any other type are
            // rejected (resolves the Codex scope-leak: HR cannot gate VACATION / CARE_DAY /
            // SENIOR_DAY here). Case-sensitive Ordinal — entitlement_type is an identifier.
            if (!string.Equals(entitlementType, SettableEntitlementType, StringComparison.Ordinal))
            {
                return Results.UnprocessableEntity(new
                {
                    error = $"entitlementType '{entitlementType}' is not settable via this endpoint.",
                    settable = new[] { SettableEntitlementType },
                    hint = "SENIOR_DAY is age-derived (DOB), never a manual eligibility toggle.",
                });
            }

            // Conditional precondition — If-None-Match: * (first-create, expectedVersion=null)
            // or If-Match: "<version>" (toggle existing). 428 on missing / malformed / both.
            // Mirrors the ConfigEndpoints lifecycle PUT (the eligibility row's existence is
            // the create-vs-update signal — there is no separate POST /create surface because
            // the toggle IS the create; opt-in absent-row default ⇒ ineligible).
            if (!EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
            var streamId = $"employee-entitlement-eligibility-{employeeId}-{entitlementType}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                var @event = new EmployeeEntitlementEligibilitySet
                {
                    EmployeeId = employeeId,
                    EntitlementType = entitlementType,
                    Eligible = body.Eligible,
                    EffectiveFrom = effectiveFrom,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };

                SaveEligibilityResult result;
                try
                {
                    // Repo routes Case A (create, expectedVersion=null) / Case B (same-day
                    // in-place) / Case C (cross-day supersede) under SELECT ... FOR UPDATE,
                    // and writes the table-level employee_entitlement_eligibility_audit row in
                    // this same (conn, tx).
                    result = await eligibilityRepo.SupersedeAndCreateAsync(
                        conn, tx, @event, expectedVersion, actorId, actorRole, ct);
                }
                catch (OptimisticConcurrencyException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = ex.ExpectedVersion,
                        actualVersion = ex.ActualVersion,
                    }, statusCode: 412);
                }
                catch (EligibilityAlreadyExistsException ex)
                {
                    // S59 / Step-7a BLOCKER 1 — If-None-Match: * is create-only. A live row
                    // already exists, so this would be a blind overwrite of an HR-set value
                    // (lost update). Reject with 409 and hand back the current version so the
                    // client can GET-then-If-Match. The actual If-Match flow goes through the
                    // 412 path above; this is purely the create-on-existing rejection.
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Eligibility row already exists",
                        currentVersion = ex.CurrentVersion,
                        hint = "If-None-Match: * is create-only. Re-read the current eligibility (GET) and retry with If-Match: \"<version>\".",
                    }, statusCode: 409);
                }
                catch (InvalidEligibilitySupersessionException ex)
                {
                    // Defense-in-depth — EffectiveFrom is server-stamped to today (UTC), so a
                    // backdate is structurally impossible unless a predecessor's effective_from
                    // is in the future. Map to 422 if it ever fires.
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }

                // Atomic-outbox emission (same tx as the repo write + its audit row,
                // ADR-018 D3). Resolve the employee's org for the ADR-026 TENANT_TARGETED
                // audit-projection row (mapper is pure; endpoint resolves the lookup).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

                var auditUser = await userRepo.GetByIdAsync(conn, tx, employeeId, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"Audit projection: employee {employeeId} not found or inactive."));
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{result.Version}\"";
                return Results.Ok(new
                {
                    employeeId,
                    entitlementType,
                    eligible = body.Eligible,
                    effectiveFrom,
                    version = result.Version,
                });
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 1b. GET /api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}
        //
        // S59 / Step-7a BLOCKER 1 — read-then-If-Match support. RBAC: HROrAbove +
        // OrgScopeValidator (cross-org binding), exactly like the PUT (ADR-007). Returns the
        // CURRENT LIVE eligibility state + the version as an ETag so the UI can compose a
        // coherent If-Match on the subsequent toggle.
        //
        //   • Live row present  → 200 { eligible, effectiveFrom, version } + ETag "<version>".
        //                         Client toggles with If-Match: "<version>".
        //   • No live row       → 200 { eligible:false (absent-row default, refinement R1),
        //                         rowExists:false } with NO ETag. Client creates with
        //                         If-None-Match: *.
        //
        // Restricted to CHILD_SICK like the PUT (scope guard, 422 otherwise). This GET returns
        // eligibility ONLY — never DOB (DOB has its own HR-only surface; no GDPR DOB leak here).
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}", async (
            string employeeId,
            string entitlementType,
            EmployeeEntitlementEligibilityRepository eligibilityRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Cross-org binding — same as the PUT (HROrAbove policy proves role + scope shape
            // but does NOT bind the actor to the target employee's org; FAIL-001). S76 B1: LocalHR floor.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Scope guard — CHILD_SICK only, mirroring the PUT (SENIOR_DAY is age-derived).
            if (!string.Equals(entitlementType, SettableEntitlementType, StringComparison.Ordinal))
            {
                return Results.UnprocessableEntity(new
                {
                    error = $"entitlementType '{entitlementType}' is not readable via this endpoint.",
                    settable = new[] { SettableEntitlementType },
                    hint = "SENIOR_DAY is age-derived (DOB), never a manual eligibility toggle.",
                });
            }

            var live = await eligibilityRepo.GetLiveAsync(employeeId, entitlementType, ct);
            if (live is null)
            {
                // No live row → opt-in absent-row default (ineligible), NO ETag. The client
                // uses If-None-Match: * to create (the create-only PUT path).
                return Results.Ok(new
                {
                    employeeId,
                    entitlementType,
                    eligible = false,
                    rowExists = false,
                });
            }

            var row = live.Value;
            context.Response.Headers.ETag = $"\"{row.Version}\"";
            return Results.Ok(new
            {
                employeeId,
                entitlementType,
                eligible = row.Eligible,
                effectiveFrom = row.EffectiveFrom,
                rowExists = true,
                version = row.Version,
            });
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 2. GET /api/admin/employees/{employeeId}/birth-date
        //
        // RBAC: HROrAbove + OrgScopeValidator. The ONLY DOB read surface — DOB never
        // appears in any Employee-facing DTO / JWT / export (TASK-5909). ETag stamped
        // from users.version so the subsequent PUT composes If-Match coherently.
        // 404 when no active user. 403 on cross-org.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employees/{employeeId}/birth-date", async (
            string employeeId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S76 B1: HROrAbove policy → LocalHR floor.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var hit = await userRepo.GetByIdWithVersionAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee not found" });
            var (user, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            return Results.Ok(new
            {
                employeeId = user.UserId,
                birthDate = user.BirthDate,
                version,
            });
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 3. PUT /api/admin/employees/{employeeId}/birth-date
        //
        // RBAC: HROrAbove + OrgScopeValidator. Admin-strict If-Match (ADR-019 D2).
        // Atomic: FOR-UPDATE re-read + version check, SetBirthDateAsync (bumps
        // users.version), users_audit UPDATED row — all one tx (ADR-018 D3). birthDate
        // may be null (clears an unknown DOB). DOB erasure (Article 17) deferred with
        // ADR-025 D3.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper admin-strict)
        //   • 412 — OptimisticConcurrencyException (stale version)
        //   • 404 — no active user (pre-check or KeyNotFoundException)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/birth-date", async (
            string employeeId,
            SetBirthDateRequest body,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S76 B1: HROrAbove policy → LocalHR floor.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match — 428 if missing / malformed / If-None-Match: *.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // FOR-UPDATE re-read — canonical snapshot for the If-Match precondition
                // AND the users_audit previous_data JSONB (closes the stale-snapshot race;
                // mirrors the AdminEndpoints user PUT pattern).
                var lockedHit = await userRepo.GetByIdWithVersionAsync(conn, tx, employeeId, ct);
                if (lockedHit is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }
                var (lockedUser, lockedVersion) = lockedHit.Value;

                if (lockedVersion != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = lockedVersion,
                    }, statusCode: 412);
                }

                long newVersion;
                try
                {
                    newVersion = await userRepo.SetBirthDateAsync(
                        conn, tx, employeeId, body.BirthDate, expectedVersion, ct);
                }
                catch (OptimisticConcurrencyException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = ex.ExpectedVersion,
                        actualVersion = ex.ActualVersion,
                    }, statusCode: 412);
                }
                catch (KeyNotFoundException)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }

                // users_audit UPDATED row — full-row JSONB snapshot captures birth_date
                // (init.sql: users_audit stores whole-row previous/new data; no schema
                // change needed). password_hash deliberately excluded.
                var previousData = JsonSerializer.Serialize(new { birthDate = lockedUser.BirthDate });
                var newData = JsonSerializer.Serialize(new { birthDate = body.BirthDate });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO users_audit (
                        user_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @userId, 'UPDATED',
                        @previousData::jsonb, @newData::jsonb,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("userId", employeeId);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    auditCmd.Parameters.AddWithValue("versionBefore", lockedVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", newVersion);
                    auditCmd.Parameters.AddWithValue("actorId", actorId);
                    auditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                return Results.Ok(new
                {
                    employeeId,
                    birthDate = body.BirthDate,
                    version = newVersion,
                });
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove");

        return app;
    }

    // ── Request DTOs ──

    /// <summary>PUT eligibility body — just the boolean. entitlement_type + employeeId are
    /// route params; EffectiveFrom is server-stamped (ADR-023 D8).</summary>
    private sealed record SetEntitlementEligibilityRequest
    {
        public bool Eligible { get; init; }
    }

    /// <summary>PUT DOB body. <c>BirthDate</c> may be null (clear an unknown DOB).</summary>
    private sealed record SetBirthDateRequest
    {
        public DateOnly? BirthDate { get; init; }
    }
}
