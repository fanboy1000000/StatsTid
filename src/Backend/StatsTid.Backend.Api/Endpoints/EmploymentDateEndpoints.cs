using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

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
///       <b>PUT /api/admin/employees/{employeeId}/employment-end-date</b> (S70; S71 / TASK-7102
///       refactor) — terminated-INCLUSIVE write via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/>, with the
///       R1 deactivation lifecycle (delegated to the SHARED
///       <see cref="EmploymentEndDateLifecycleWriter"/> — SPRINT-71 R4 one-implementation; the
///       S70 transitional inline choreography is deleted), the SPRINT-71 R13 range-widened
///       active-settlement 409 guard (full <c>[min..max]</c> ferieår span, with a
///       machine-readable reversal pointer on the 409) and the R12 employee advisory lock in ONE
///       atomic tx; self-target writes are 403-rejected for ALL actors (S70 Step-7a W1 — a
///       second administrator performs self-departures).
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

            // S76 B1: HROrAbove policy → LocalHR floor (a sub-HR scope covering the employee's
            // org cannot satisfy this HR data gate).
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

            // S76 B1 fix-forward (cycle 2): LocalHR per-scope floor — the sensitive end-date READ
            // (employment_end_date never appears in any Employee-facing DTO) must not be served to
            // a mixed HR@A + Leader@B JWT for an ACTIVE B employee via the Leader scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
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
        // S70 / TASK-7002 / ADR-033 slice 3a (S71 / TASK-7102 refactor) — set / clear / correct
        // users.employment_end_date with the R1 deactivation lifecycle, ALL in ONE atomic tx
        // (ADR-018 D3), in the R12 order: ADR-032 D4 employee advisory lock FIRST → FOR-UPDATE
        // terminated-inclusive re-read → every guard re-evaluated in-lock (If-Match + the
        // R7a/R13 no-active-settlement span check) → the SHARED EmploymentEndDateLifecycleWriter
        // (SPRINT-71 R4 ONE-implementation: guarded versioned write → R1(e) side effects → R10
        // event + ADR-026 audit + users_audit) → commit. The S70 inline lifecycle-write
        // choreography was DELETED here — the writer is the single implementation, consumed by
        // both this PUT and the slice-3b reversal service's subsumed correction.
        // The advisory lock (pg_advisory_xact_lock(hashtext('employee-' || id)) — the SAME key
        // VacationSettlementService.SettleAsync / the reconcile retrofit / Step A / the reversal
        // service hold) is what serializes this endpoint against the settlement path;
        // `users FOR UPDATE` alone would not (the settlement pass does not row-lock users).
        //
        // R1 decision table: see EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle (the
        // CANONICAL host of the pure decision; this class's same-named member delegates to it
        // for the S70 unit suites). R1(f) DELIBERATE coherence point: the admin general user PUT
        // filters is_active=TRUE (AdminEndpoints ~L1020; UserRepository.cs:94-97 soft-delete
        // semantic), so it can neither edit nor reactivate a deactivated user — R1(c) on THIS
        // endpoint is the ONLY reactivation path for lifecycle-deactivated leavers. By design.
        //
        // R7a correction guard, R13 RANGE-WIDENED (SPRINT-71): the change is REJECTED 409 when
        // an active (non-REVERSED) vacation_settlements row of ANY type/trigger exists for ANY
        // ferieår in the FULL span [min(ferieår(old), ferieår(new)) .. max(...)] — a backward or
        // forward correction crossing an INTERMEDIATE settled ferieår no longer bypasses the
        // guard. The 409 carries a machine-readable reversal pointer (the slice-3b reversal
        // endpoint + every blocking row's identity/sequence/version) so an operator/UI can
        // route the correction through reverse-then-re-settle. This PUT NEVER silently reverses.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper admin-strict)
        //   • 412 — version mismatch (stale If-Match), on ACTIVE and DEACTIVATED rows alike
        //   • 409 — R7a/R13 active-settlement conflict (with the reversal pointer)
        //   • 404 — user_id does not exist at all (a deactivated leaver is NOT a 404 here)
        //   • 403 — terminated-inclusive OrgScopeValidator denial
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/employment-end-date", async (
            string employeeId,
            SetEmploymentEndDateRequest body,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            EmploymentEndDateLifecycleWriter lifecycleWriter,
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
            // S76 B1 fix-forward (cycle 2): LocalHR per-scope floor — the end-date WRITE (the R1
            // lifecycle/reactivation surface) must not be reachable by a mixed HR@A + Leader@B JWT
            // for an ACTIVE B employee via the Leader scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
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

                // (2) FOR-UPDATE terminated-inclusive re-read — the 404/412 pre-checks and the
                // R7a/R13 guard's old-end-date input (closes the stale-snapshot race). The shared
                // writer re-reads the SAME row in the same tx for its own canonical snapshot —
                // idempotent under the row lock this read already took.
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

                // (3) R7a guard, R13 RANGE-WIDENED (SPRINT-71), re-evaluated IN-LOCK (R12): any
                // active (non-REVERSED) settlement row — ANY entitlement type, ANY trigger
                // (fail-closed) — for ANY ferieår in the FULL [min..max] span rejects the change.
                // ALL blockers are fetched (not just the first) so the 409 can carry the complete
                // machine-readable reversal pointer (R7a-409 contract, SPRINT-71).
                var affectedYears = AffectedFerieaarSpan(lockedUser.EmploymentEndDate, body.EmploymentEndDate);
                if (affectedYears.Length > 0)
                {
                    var blockers = new List<(string Type, int Year, int Sequence, string State, long Version)>();
                    await using (var guardCmd = new NpgsqlCommand(
                        """
                        SELECT entitlement_type, entitlement_year, sequence, settlement_state, version
                        FROM vacation_settlements
                        WHERE employee_id = @employeeId
                          AND entitlement_year = ANY(@years)
                          AND settlement_state <> 'REVERSED'
                        ORDER BY entitlement_year, entitlement_type, sequence
                        """, conn, tx))
                    {
                        guardCmd.Parameters.AddWithValue("employeeId", employeeId);
                        guardCmd.Parameters.AddWithValue("years", affectedYears);
                        await using var guardReader = await guardCmd.ExecuteReaderAsync(ct);
                        while (await guardReader.ReadAsync(ct))
                        {
                            blockers.Add((guardReader.GetString(0), guardReader.GetInt32(1),
                                guardReader.GetInt32(2), guardReader.GetString(3), guardReader.GetInt64(4)));
                        }
                    }
                    if (blockers.Count > 0)
                    {
                        var first = blockers[0];
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "An active settlement exists for a ferieår this end-date change affects; " +
                                    "the change is rejected fail-closed (SPRINT-70 R7a).",
                            conflictingSettlement = new
                            {
                                entitlementType = first.Type,
                                entitlementYear = first.Year,
                                settlementState = first.State,
                            },
                            // SPRINT-71 R7a-409 reversal pointer: EVERY blocking row's identity +
                            // settlement-row sequence + version — exactly what the reversal
                            // endpoint's body (expectedSettlementSequence) and If-Match need.
                            blockingSettlements = blockers.Select(b => new
                            {
                                entitlementType = b.Type,
                                entitlementYear = b.Year,
                                sequence = b.Sequence,
                                settlementState = b.State,
                                version = b.Version,
                            }).ToArray(),
                            affectedEntitlementYears = affectedYears,
                            reversalEndpoint = $"/api/admin/employees/{employeeId}/settlement-reversal",
                            hint = "Route the correction through the slice-3b reversal endpoint: POST reversalEndpoint " +
                                   "with a blocking row's entitlementType/entitlementYear/expectedSettlementSequence " +
                                   "(If-Match: its version) — reverse-then-re-settle subsumes this end-date " +
                                   "correction; the explicit bare-reversal mode parks the tuple instead.",
                        }, statusCode: 409);
                    }
                }

                // (4)–(8) — the SHARED lifecycle writer (SPRINT-71 R4 one-implementation): the
                // R1 decision off the Copenhagen business date, the guarded versioned write, the
                // R1(e) ReportingLineManagerDeactivated side effects, the R10 event + ADR-026
                // audit-projection row and the users_audit UPDATED row — byte-identical to the
                // S70 inline choreography this delegation replaced (pinned by the S70 lifecycle
                // suite + EndDateLifecycleWriterEffectTests).
                EmploymentEndDateLifecycleResult lifecycle;
                try
                {
                    lifecycle = await lifecycleWriter.ApplyAsync(
                        conn, tx, employeeId, body.EmploymentEndDate, expectedVersion,
                        actorId, actorRole, actor.OrgId, actor.CorrelationId,
                        CopenhagenToday(timeProvider), ct);
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

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{lifecycle.VersionAfter}\"";
                return Results.Ok(new
                {
                    employeeId,
                    employmentEndDate = body.EmploymentEndDate,
                    endDateDeactivated = lifecycle.NewEndDateDeactivated,
                    isActive = lifecycle.NewIsActive,
                    version = lifecycle.VersionAfter,
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
    /// The R1 deactivation-lifecycle decision — since the S71 / TASK-7102 refactor a THIN
    /// DELEGATION to the CANONICAL implementation,
    /// <see cref="EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle"/> (SPRINT-71 R4
    /// one-implementation — the S70 transitional duplicate died here). The public symbol is
    /// retained because the S70 unit suite (<c>EmploymentEndDateLifecycleLogicTests</c>) and the
    /// 7104 parity suite (<c>EndDateLifecycleWriterParityTests</c>) reference it; parity is now
    /// by-construction. See the writer's doc for the R1(a)–(d) decision table.
    /// </summary>
    public static (bool IsActive, bool EndDateDeactivated) ComputeEndDateLifecycle(
        DateOnly? newEndDate, bool oldIsActive, bool oldEndDateDeactivated, DateOnly copenhagenToday)
        => EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            newEndDate, oldIsActive, oldEndDateDeactivated, copenhagenToday);

    /// <summary>
    /// R6 ferieår resolution, executable: the entitlement year containing <paramref name="date"/>
    /// (VACATION <c>reset_month</c> = 9, uniform by DB CHECK per S68 B1 — the ferieår starting
    /// 1 Sep of <c>entitlementYear</c>).
    /// </summary>
    public static int FerieaarOf(DateOnly date) => date.Month >= 9 ? date.Year : date.Year - 1;

    /// <summary>
    /// The S70 R7a affected-ferieår PAIR: ferieår(old end date) and ferieår(new end date), each
    /// when non-null, de-duplicated. Empty when both dates are null (clearing a never-set date).
    /// SUPERSEDED for the PUT's guard by <see cref="AffectedFerieaarSpan"/> (SPRINT-71 R13 — the
    /// pair misses INTERMEDIATE ferieårs a multi-year correction crosses); retained because the
    /// S70 unit suite pins it and it documents the pre-R13 semantics.
    /// </summary>
    public static int[] AffectedFerieaar(DateOnly? oldEndDate, DateOnly? newEndDate)
    {
        var years = new HashSet<int>();
        if (oldEndDate is { } o) years.Add(FerieaarOf(o));
        if (newEndDate is { } n) years.Add(FerieaarOf(n));
        return years.ToArray();
    }

    /// <summary>
    /// SPRINT-71 R13 — the RANGE-WIDENED affected-ferieår set: the FULL inclusive span
    /// <c>[min(ferieår(old), ferieår(new)) .. max(...)]</c>, so a forward OR backward end-date
    /// correction crossing an INTERMEDIATE ferieår holding an active settlement row 409s instead
    /// of silently bypassing the guard. A null date contributes no pivot (single-pivot span = a
    /// single year — identical to the S70 pair semantics for set/clear); both null ⇒ empty.
    /// Ascending order. PURE; the in-tx twin lives in
    /// <c>SettlementReversalService.GetOtherActiveRowsInCorrectionSpanAsync</c> (the B2 guard).
    /// </summary>
    public static int[] AffectedFerieaarSpan(DateOnly? oldEndDate, DateOnly? newEndDate)
    {
        var pivots = new List<int>(2);
        if (oldEndDate is { } o) pivots.Add(FerieaarOf(o));
        if (newEndDate is { } n) pivots.Add(FerieaarOf(n));
        if (pivots.Count == 0) return [];
        var low = pivots.Min();
        var high = pivots.Max();
        return Enumerable.Range(low, high - low + 1).ToArray();
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
