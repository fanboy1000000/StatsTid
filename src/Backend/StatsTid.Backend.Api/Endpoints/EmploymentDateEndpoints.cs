using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// HR admin surface for the employment lifecycle dates. FOUR endpoints, all <c>HROrAbove</c>;
/// cross-org binding is load-bearing in every handler — the policy proves role + scope shape but
/// does NOT bind the actor to the target's organisation (FAIL-001: the validators use
/// <c>FindAll</c>, not <c>FindFirst</c>, on scopes). Neither date ever appears in any
/// Employee-facing DTO / JWT / export.
///   <list type="bullet">
///     <item><description>
///       <b>GET /api/admin/employees/{employeeId}/employment-start-date</b> (S60 / TASK-6006 /
///       ADR-030) — active-only read via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/>; ETag from <c>users.version</c>.
///     </description></item>
///     <item><description>
///       <b>PUT /api/admin/employees/{employeeId}/employment-start-date</b> (S60) — active-only
///       write via <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/>, admin-strict
///       If-Match (ADR-019 D2), one atomic tx (ADR-018 D3); null clears the date.
///     </description></item>
///     <item><description>
///       <b>GET /api/admin/employees/{employeeId}/employment-end-date</b> (S70 / TASK-7002 /
///       ADR-033 slice 3a) — terminated-INCLUSIVE read via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/> (R9c
///       allowlist surface), so HR can address a deactivated leaver's row; ETag from
///       <c>users.version</c>.
///     </description></item>
///     <item><description>
///       <b>PUT /api/admin/employees/{employeeId}/employment-end-date</b> (S70) —
///       terminated-INCLUSIVE write via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/>, with the
///       R1 deactivation lifecycle, the R7a active-settlement 409 guard and the R12 employee
///       advisory lock in ONE atomic tx; self-target writes are 403-rejected for ALL actors
///       (S70 Step-7a W1 — a second administrator performs self-departures).
///     </description></item>
///   </list>
/// </summary>
public static class EmploymentDateEndpoints
{
    public static WebApplication MapEmploymentDateEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/employees/{employeeId}/employment-start-date
        //
        // RBAC: HROrAbove + OrgScopeValidator. The ONLY employment-start read surface —
        // it never appears in any Employee-facing DTO / JWT / export. ETag stamped from
        // users.version so the subsequent PUT composes If-Match coherently.
        // 404 when no active user. 403 on cross-org.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employees/{employeeId}/employment-start-date", async (
            string employeeId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
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
                employmentStartDate = user.EmploymentStartDate,
                version,
            });
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 2. PUT /api/admin/employees/{employeeId}/employment-start-date
        //
        // RBAC: HROrAbove + OrgScopeValidator. Admin-strict If-Match (ADR-019 D2).
        // Atomic: FOR-UPDATE re-read + version check, SetEmploymentStartDateAsync (bumps
        // users.version), users_audit UPDATED row — all one tx (ADR-018 D3).
        // employmentStartDate may be null (clears an unknown start date).
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper admin-strict)
        //   • 412 — OptimisticConcurrencyException (stale version)
        //   • 404 — no active user (pre-check or KeyNotFoundException)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/employment-start-date", async (
            string employeeId,
            SetEmploymentStartDateRequest body,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
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
                // mirrors the birth-date PUT pattern).
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
                    newVersion = await userRepo.SetEmploymentStartDateAsync(
                        conn, tx, employeeId, body.EmploymentStartDate, expectedVersion, ct);
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

                // users_audit UPDATED row — full-row JSONB snapshot captures
                // employment_start_date (init.sql: users_audit stores whole-row
                // previous/new data; no schema change needed). password_hash deliberately
                // excluded — mirrors the birth-date PUT audit row.
                var previousData = JsonSerializer.Serialize(new { employmentStartDate = lockedUser.EmploymentStartDate });
                var newData = JsonSerializer.Serialize(new { employmentStartDate = body.EmploymentStartDate });
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
                    employmentStartDate = body.EmploymentStartDate,
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

        // ═══════════════════════════════════════════
        // 3. GET /api/admin/employees/{employeeId}/employment-end-date
        //
        // S70 / TASK-7002 / ADR-033 slice 3a (SPRINT-70 R1/R9). RBAC: HROrAbove + the
        // terminated-INCLUSIVE OrgScopeValidator path (R9c allowlist surface #1) — an HR
        // operator must be able to fetch a DEACTIVATED leaver's current end date + a coherent
        // If-Match token before correcting or clearing it (R1(c) is the only reactivation
        // path for lifecycle-deactivated leavers). ETag stamped from users.version.
        // employment_end_date never appears in any Employee-facing DTO / JWT / export.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employees/{employeeId}/employment-end-date", async (
            string employeeId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var hit = await userRepo.GetByIdWithVersionIncludingTerminatedAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee not found" });
            var (user, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            return Results.Ok(new
            {
                employeeId = user.UserId,
                employmentEndDate = user.EmploymentEndDate,
                endDateDeactivated = user.EndDateDeactivated,
                isActive = user.IsActive,
                version,
            });
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 4. PUT /api/admin/employees/{employeeId}/employment-end-date
        //
        // S70 / TASK-7002 / ADR-033 slice 3a — set / clear / correct users.employment_end_date
        // with the R1 deactivation lifecycle, ALL in ONE atomic tx (ADR-018 D3), in the R12
        // order: ADR-032 D4 employee advisory lock FIRST → FOR-UPDATE terminated-inclusive
        // re-read → every guard re-evaluated in-lock (If-Match + the R7a no-active-settlement
        // check) → guarded write → R1(e) side effects → R10 event + ADR-026 audit → commit.
        // The advisory lock (pg_advisory_xact_lock(hashtext('employee-' || id)) — the SAME key
        // VacationSettlementService.SettleAsync / the reconcile retrofit / Step A hold) is what
        // serializes this endpoint against the settlement path; `users FOR UPDATE` alone would
        // not (the settlement pass does not row-lock users).
        //
        // R1 decision table (computed by ComputeEndDateLifecycle, persisted by the repo):
        //   (a) set, date already passed (Copenhagen business date > endDate), active row
        //       → same-tx is_active=false + end_date_deactivated=true + R1(e) side effects;
        //   (b) set, future-dated (today <= endDate; the end date is the LAST employed day)
        //       → store the date only, NO flip (the Step-A poller flips it — TASK-7005);
        //   (c) clear → reactivate ONLY when end_date_deactivated=true (then reset it);
        //       clearing on a manually-deactivated user clears the date, does NOT reactivate;
        //   (d) set on an already-manually-inactive user (is_active=false, provenance false)
        //       → records the date but claims NO provenance and does not change is_active;
        //   correction on a lifecycle-deactivated row (provenance=true) re-evaluates the SAME
        //       rule: still-passed date → stays deactivated (provenance kept); corrected to an
        //       unpassed date → the lifecycle basis is gone → reactivate + reset provenance
        //       (the poller re-flips when the new date passes) — the only coherent tuple: an
        //       inactive/provenance=true/future-date row would be unreachable by every other
        //       writer (see R1(f) below).
        //
        // R1(f) DELIBERATE coherence point: the admin general user PUT filters is_active=TRUE
        // (AdminEndpoints ~L1020; UserRepository.cs:94-97 soft-delete semantic), so it can
        // neither edit nor reactivate a deactivated user — R1(c) on THIS endpoint is the ONLY
        // reactivation path for lifecycle-deactivated leavers. That is by design.
        //
        // R7a correction guard (fail-closed until 3b's reversal infra): the change is REJECTED
        // 409 when an active (non-REVERSED) vacation_settlements row of ANY type/trigger exists
        // for any ferieår the change affects — affected = ferieår(old end date) and
        // ferieår(new end date), each when non-null, per R6. Reverse-then-re-settle is 3b.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper admin-strict)
        //   • 412 — version mismatch (stale If-Match), on ACTIVE and DEACTIVATED rows alike
        //   • 409 — R7a active-settlement conflict
        //   • 404 — user_id does not exist at all (a deactivated leaver is NOT a 404 here)
        //   • 403 — terminated-inclusive OrgScopeValidator denial
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/employment-end-date", async (
            string employeeId,
            SetEmploymentEndDateRequest body,
            UserRepository userRepo,
            ReportingLineRepository reportingLineRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EmployeeEmploymentEndDateSet> endDateAuditMapper,
            AuditProjectionRepository auditRepo,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S70 Step-7a W1 (internal Reviewer) — self-target exclusion, BEFORE any DB work and
            // for ALL actors (active or terminated; deterministic, fail-closed). The end-date PUT
            // is the first INACTIVE-row write surface: without this, a lifecycle-deactivated HR
            // actor's still-valid JWT (8h lifetime, no revocation) could PUT its OWN
            // employmentEndDate: null and permanently self-reinstate via the R1(c) reactivation.
            // A second administrator performs legitimate self-departures; the GET above stays
            // self-readable.
            if (string.Equals(actor.ActorId, employeeId, StringComparison.Ordinal))
                return Results.Json(new
                {
                    error = "Access denied",
                    reason = "Own employment end date cannot be modified; a second administrator must perform this change",
                }, statusCode: 403);

            // R9c allowlist surface — terminated-INCLUSIVE validator (HROrAbove + subtree
            // binding; the shared ValidateEmployeeAccessAsync would 403 a deactivated leaver).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, ct);
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
                // (1) R12 — the ADR-032 D4 employee advisory lock FIRST, before any read or
                // guard, held to commit. Serializes this mutation against Step A (flip), Step B
                // (settle), the manual resolve and the reconcile-payout writers on the SAME key.
                await EmployeeConsumptionLock.AcquireAsync(conn, tx, employeeId, ct);

                // (2) FOR-UPDATE terminated-inclusive re-read — the canonical snapshot for the
                // If-Match precondition, the R1 lifecycle decision, the event's old_* payload
                // AND the users_audit previous_data (closes the stale-snapshot race).
                var lockedHit = await userRepo.GetByIdWithVersionIncludingTerminatedAsync(conn, tx, employeeId, ct);
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

                // (3) R7a guard, re-evaluated IN-LOCK (R12): any active (non-REVERSED)
                // settlement row — ANY entitlement type, ANY trigger (fail-closed; mirrors the
                // R3/R8 any-trigger principle) — for an affected ferieår rejects the change.
                var affectedYears = AffectedFerieaar(lockedUser.EmploymentEndDate, body.EmploymentEndDate);
                if (affectedYears.Length > 0)
                {
                    await using var guardCmd = new NpgsqlCommand(
                        """
                        SELECT entitlement_type, entitlement_year, settlement_state
                        FROM vacation_settlements
                        WHERE employee_id = @employeeId
                          AND entitlement_year = ANY(@years)
                          AND settlement_state <> 'REVERSED'
                        LIMIT 1
                        """, conn, tx);
                    guardCmd.Parameters.AddWithValue("employeeId", employeeId);
                    guardCmd.Parameters.AddWithValue("years", affectedYears);
                    await using var guardReader = await guardCmd.ExecuteReaderAsync(ct);
                    if (await guardReader.ReadAsync(ct))
                    {
                        var conflictType = guardReader.GetString(0);
                        var conflictYear = guardReader.GetInt32(1);
                        var conflictState = guardReader.GetString(2);
                        await guardReader.DisposeAsync();
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "An active settlement exists for a ferieår this end-date change affects; " +
                                    "the change is rejected fail-closed (SPRINT-70 R7a).",
                            conflictingSettlement = new
                            {
                                entitlementType = conflictType,
                                entitlementYear = conflictYear,
                                settlementState = conflictState,
                            },
                            affectedEntitlementYears = affectedYears,
                            hint = "Reverse-then-re-settle requires the slice-3b reversal infrastructure; " +
                                   "until then an end-date set/clear/correction touching a settled ferieår is not supported.",
                        }, statusCode: 409);
                    }
                }

                // (4) The R1 lifecycle decision — a pure function of the LOCKED row + the
                // Copenhagen business date (the boundary-comparison authority; mirrors the
                // SettlementCloseService convention — NEVER raw UTC/CURRENT_DATE).
                var today = CopenhagenToday(timeProvider);
                var (newIsActive, newEndDateDeactivated) = ComputeEndDateLifecycle(
                    body.EmploymentEndDate, lockedUser.IsActive, lockedUser.EndDateDeactivated, today);
                var isDeactivating = lockedUser.IsActive && !newIsActive;

                // (5) Guarded write — the endpoint computed the tuple; the repo persists it
                // (version-bumped per ADR-018 D7 so a held ETag never survives the transition).
                long newVersion;
                try
                {
                    newVersion = await userRepo.SetEmploymentEndDateIncludingTerminatedAsync(
                        conn, tx, employeeId, body.EmploymentEndDate,
                        newEndDateDeactivated, newIsActive, expectedVersion, ct);
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

                // (6) R1(e) — every lifecycle deactivation reuses the EXISTING user-deactivation
                // side-effect path (AdminEndpoints manual-PUT precedent, S52/ADR-027): emit
                // ReportingLineManagerDeactivated for each active line managed by the leaver,
                // inside the SAME tx as the is_active flip.
                if (isDeactivating)
                {
                    var managedLines = await reportingLineRepo.GetDirectReportsAsync(conn, tx, employeeId, ct);
                    foreach (var line in managedLines)
                    {
                        var deactivatedEvent = new ReportingLineManagerDeactivated
                        {
                            ReportingLineId = line.ReportingLineId,
                            EmployeeId = line.EmployeeId,
                            ManagerId = line.ManagerId,
                            TreeRootOrgId = line.TreeRootOrgId,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        await outbox.EnqueueAsync(conn, tx, $"reporting-line-{line.EmployeeId}", deactivatedEvent, ct);
                    }
                }

                // (7) R10 — EmployeeEmploymentEndDateSet (set/clear/correction discriminated by
                // the old/new pair) on employee-{id} via the outbox + the ADR-026 audit
                // projection row, SAME tx. Admin actor from the JWT.
                var endDateEvent = new EmployeeEmploymentEndDateSet
                {
                    EmployeeId = employeeId,
                    OldEndDate = lockedUser.EmploymentEndDate,
                    NewEndDate = body.EmploymentEndDate,
                    OldIsActive = lockedUser.IsActive,
                    NewIsActive = newIsActive,
                    VersionBefore = lockedVersion,
                    VersionAfter = newVersion,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                var outboxId = await outbox.EnqueueAndReturnIdAsync(
                    conn, tx, $"employee-{employeeId}", endDateEvent, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(endDateEvent.OccurredAt, DateTimeKind.Utc)),
                    ResolvedTargetOrgId: lockedUser.PrimaryOrgId);
                var rowData = endDateAuditMapper.Map(endDateEvent, auditCtx);
                await auditRepo.InsertAsync(
                    conn, tx, endDateEvent.EventId, outboxId, endDateEvent.EventType, rowData, auditCtx, ct);

                // (8) users_audit UPDATED row — full lifecycle-tuple before/after snapshot
                // (mirrors the S60 employment-start PUT audit row exactly).
                var previousData = JsonSerializer.Serialize(new
                {
                    employmentEndDate = lockedUser.EmploymentEndDate,
                    endDateDeactivated = lockedUser.EndDateDeactivated,
                    isActive = lockedUser.IsActive,
                });
                var newData = JsonSerializer.Serialize(new
                {
                    employmentEndDate = body.EmploymentEndDate,
                    endDateDeactivated = newEndDateDeactivated,
                    isActive = newIsActive,
                });
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
                    employmentEndDate = body.EmploymentEndDate,
                    endDateDeactivated = newEndDateDeactivated,
                    isActive = newIsActive,
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

    // ── S70 / TASK-7002 — pure lifecycle helpers (unit-tested; no I/O) ──

    /// <summary>
    /// The R1 deactivation-lifecycle decision — a PURE function of the FOR-UPDATE'd row state,
    /// the requested end date and the Copenhagen business date. Returns the
    /// (<c>is_active</c>, <c>end_date_deactivated</c>) tuple to persist. See the PUT handler's
    /// decision-table comment for the R1(a)–(d) mapping; <c>employment_end_date</c> is the LAST
    /// day employed, so "passed" means <paramref name="copenhagenToday"/> is STRICTLY after it.
    /// </summary>
    public static (bool IsActive, bool EndDateDeactivated) ComputeEndDateLifecycle(
        DateOnly? newEndDate, bool oldIsActive, bool oldEndDateDeactivated, DateOnly copenhagenToday)
    {
        if (newEndDate is null)
        {
            // R1(c) clear: reactivate ONLY on lifecycle provenance (then reset it); a
            // manually-deactivated user keeps is_active=false. Provenance always resets —
            // with no end date there is nothing for it to claim.
            return oldEndDateDeactivated ? (true, false) : (oldIsActive, false);
        }

        var passed = copenhagenToday > newEndDate.Value;

        if (oldIsActive)
        {
            // R1(a) already-passed → same-tx deactivate with provenance;
            // R1(b) future-dated (incl. endDate == today: still the last EMPLOYED day) → no flip.
            return passed ? (false, true) : (true, false);
        }

        if (oldEndDateDeactivated)
        {
            // Correction on a lifecycle-deactivated row: deterministic re-evaluation of the
            // SAME rule. Still-passed → stays deactivated (provenance kept); unpassed → the
            // lifecycle basis is gone → reactivate + reset (the Step-A poller re-flips later).
            return passed ? (false, true) : (true, false);
        }

        // R1(d) manually-inactive: record the date, claim NO provenance, leave is_active alone.
        return (false, false);
    }

    /// <summary>
    /// R6 ferieår resolution, executable: the entitlement year containing <paramref name="date"/>
    /// (VACATION <c>reset_month</c> = 9, uniform by DB CHECK per S68 B1 — the ferieår starting
    /// 1 Sep of <c>entitlementYear</c>).
    /// </summary>
    public static int FerieaarOf(DateOnly date) => date.Month >= 9 ? date.Year : date.Year - 1;

    /// <summary>
    /// The R7a affected-ferieår set: ferieår(old end date) and ferieår(new end date), each when
    /// non-null, de-duplicated. Empty when both dates are null (clearing a never-set date).
    /// </summary>
    public static int[] AffectedFerieaar(DateOnly? oldEndDate, DateOnly? newEndDate)
    {
        var years = new HashSet<int>();
        if (oldEndDate is { } o) years.Add(FerieaarOf(o));
        if (newEndDate is { } n) years.Add(FerieaarOf(n));
        return years.ToArray();
    }

    // ── Europe/Copenhagen business-date helper ──
    // Mirrors the SettlementCloseService file-scoped convention (ADR-033 D3 boundary-timezone;
    // the Orchestrator may later hoist both into the follow-up (v) shared helper). The injected
    // TimeProvider is the test seam (PAT-008).

    private static readonly TimeZoneInfo CopenhagenZone = ResolveCopenhagenZone();

    private static DateOnly CopenhagenToday(TimeProvider timeProvider)
    {
        var copenhagenNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenZone);
        return DateOnly.FromDateTime(copenhagenNow.DateTime);
    }

    private static TimeZoneInfo ResolveCopenhagenZone()
    {
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    // ── Request DTO ──

    /// <summary>PUT employment-start body. <c>EmploymentStartDate</c> may be null
    /// (clear an unknown start date).</summary>
    private sealed record SetEmploymentStartDateRequest
    {
        public DateOnly? EmploymentStartDate { get; init; }
    }

    /// <summary>PUT employment-end body. <c>EmploymentEndDate</c> may be null
    /// (clear — R1(c) provenance-guarded reactivation).</summary>
    private sealed record SetEmploymentEndDateRequest
    {
        public DateOnly? EmploymentEndDate { get; init; }
    }
}
