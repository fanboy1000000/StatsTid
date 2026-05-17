using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S31 / TASK-3107 + S33 / TASK-3308 — Phase 4d-3 Part 2 admin CRUD surface for the
/// authoritative employee profile store. Three endpoints under
/// <c>/api/admin/employee-profiles/{employeeId}</c>:
///   <list type="bullet">
///     <item><description>GET — read the live row, ETag-stamped per ADR-019 D2. UNCHANGED in S33
///       (refinement cycle 1 Reviewer BLOCKER-1 absorption: no <c>?asOf=</c> extension).</description></item>
///     <item><description>
///       PUT — S33 extends with required <c>EffectiveFrom: DateOnly</c>. Routes through
///       <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/> (TASK-3302) under
///       admin-strict If-Match + atomic outbox. Emits <see cref="EmployeeProfileUpdated"/> on
///       Case B (same-day in-place edit) or <see cref="EmployeeProfileSuperseded"/> on Case C
///       (cross-day supersession) per ADR-020 D2. Audit row action mirrors the outcome
///       (<c>UPDATED</c> vs <c>SUPERSEDED</c>).
///     </description></item>
///     <item><description>
///       DELETE — S33 / TASK-3308 NEW. Soft-deletes the live row via
///       <see cref="EmployeeProfileRepository.SoftDeleteAsync"/> (TASK-3303) under admin-strict
///       If-Match. Audit row action <c>DELETED</c> with
///       <c>version_before = version_after = predecessor.version</c> per ADR-023 D8
///       (soft-delete is row-state-change, not field-mutation — version intentionally NOT
///       bumped). Emits <see cref="EmployeeProfileSoftDeleted"/> in the same tx (ADR-018 D3).
///       Returns 204 No Content.
///     </description></item>
///   </list>
///
/// <para>
/// <b>Step 0b cycle 1 Codex BLOCKER fix — cross-org HR data-leak prevention.</b>
/// All three endpoints carry <c>RequireAuthorization("HROrAbove")</c> AND an explicit
/// <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/> binding to the target
/// <c>employeeId</c>. The policy alone proves role + scope shape but does NOT bind
/// the actor to the target employee's organisation — without OrgScopeValidator an HR
/// user from org X could read/edit/delete profiles of employees in org Y. The cross-org
/// binding is load-bearing.
/// </para>
///
/// <para>
/// <b>ADR-019 admin-strict If-Match contract.</b> PUT + DELETE require
/// <c>If-Match: "&lt;version&gt;"</c> via <see cref="EtagHeaderHelper.TryParseIfMatch"/>
/// (admin-strict mode rejects <c>If-None-Match: *</c>). 428 on missing/malformed;
/// 412 on stale (with structured <c>expectedVersion</c> + <c>actualVersion</c> body
/// per ADR-019 D2); 404 when no live row exists for the employee.
/// </para>
///
/// <para>
/// <b>Case A 404 pre-check on PUT (Step 0b Reviewer BLOCKER-3 absorption).</b>
/// PUT is an admin <i>edit</i> surface — it does NOT create net-new profiles. Before
/// routing through <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>,
/// the endpoint reads the live row via
/// <see cref="EmployeeProfileRepository.GetByEmployeeIdAsync(NpgsqlConnection, NpgsqlTransaction?, string, CancellationToken)"/>;
/// when null, returns 404 immediately. <c>SupersedeAndCreateAsync</c>'s Case A (no-live-row
/// INSERT) is reachable only from <c>AdminEndpoints</c> POST <c>/api/admin/users</c>
/// (S31 TASK-3108 4-way atomicity), NEVER from PUT.
/// </para>
///
/// <para>
/// <b>EffectiveFrom validator (ADR-023 D8 narrowing).</b> PUT rejects both backdated AND
/// future-dated <c>EffectiveFrom</c> with 422. Only <c>DateOnly.FromDateTime(DateTime.UtcNow)</c>
/// is accepted. <c>DateTime.UtcNow</c> (not local time) aligns with the frontend's
/// <c>new Date().toISOString().slice(0,10)</c> UTC extraction. This is the cycle-3
/// same-day-only-edit precedent from S29 WTM + S30 EntitlementConfig, narrowed further
/// per ADR-023 D8 — cross-day edits route through Case C inside the repo, but only when
/// today's UTC date is strictly later than the predecessor's <c>effective_from</c>; the
/// validator prevents the admin from picking an arbitrary date.
/// </para>
///
/// <para>
/// <b>ADR-023 D8 soft-delete divergence.</b> DELETE soft-deletes the live row by stamping
/// <c>effective_to = NOW()::date</c> with the predecessor's <c>version</c> column
/// UNCHANGED — soft-delete is row-state-change, not field-mutation. The audit row
/// accordingly carries <c>version_before = version_after = predecessor.version</c>
/// (deliberate divergence from sibling ADR-019 D8 endpoints — agreement_configs,
/// wage_type_mappings, entitlement_configs — which all bump <c>version + 1</c> on
/// soft-delete). A retry with stale If-Match after a successful soft-delete hits 404
/// (the row "disappeared" from live reads per the partial-unique-index predicate),
/// NOT 412 — this is intentional, locked by D-test
/// <c>SoftDelete_StaleIfMatchAfterSoftDelete_Returns404NotConflict412</c> in TASK-3312.
/// </para>
/// </summary>
public static class EmployeeProfileEndpoints
{
    public static WebApplication MapEmployeeProfileEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/employee-profiles/{employeeId}
        //
        // RBAC: HROrAbove policy + OrgScopeValidator binding (Step 0b BLOCKER fix).
        // Returns 404 when no live row exists. On success, sets ETag: "<version>" so
        // the admin UI can compose If-Match on the subsequent PUT / DELETE.
        //
        // S33 / TASK-3308 — UNCHANGED. Refinement cycle 1 Reviewer BLOCKER-1
        // absorption: no `?asOf=` extension on the GET signature.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            EmployeeProfileRepository repository,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding. HROrAbove alone is not enough;
            // bind the actor's scopes to the target employee's organisation.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(
                actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Step 7a P2 fix — atomic row+version read. Read body fields AND the
            // `version` column from the same SELECT so the ETag stamped on the
            // response matches the data serialized. Pre-fix used two reads
            // (GetByEmployeeIdAsync + ReadLiveVersionAsync) — a concurrent admin
            // edit between the two could have returned stale fields with a NEWER
            // ETag, letting the next If-Match overwrite the racing change.
            var hit = await repository.GetByEmployeeIdWithVersionAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee profile not found" });
            var (profile, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            return Results.Ok(new
            {
                employeeId = profile.EmployeeId,
                weeklyNormHours = profile.WeeklyNormHours,
                partTimeFraction = profile.PartTimeFraction,
                position = profile.Position,
                isPartTime = profile.IsPartTime,
                version,
            });
        }).RequireAuthorization("HROrAbove");

        // ═══════════════════════════════════════════
        // 2. PUT /api/admin/employee-profiles/{employeeId}
        //
        // RBAC: HROrAbove + OrgScopeValidator (Step 0b BLOCKER fix).
        // Admin-strict If-Match required (ADR-019 D2). Atomic-outbox semantic:
        // (UPDATE-in-place OR close-predecessor + INSERT-new) + INSERT audit row +
        // outbox enqueue all happen in one tx (ADR-018 D3). Returns 200 with new ETag
        // on success.
        //
        // S33 / TASK-3308 changes:
        //   • DTO gains required `EffectiveFrom: DateOnly`; validator rejects anything
        //     other than today (UTC) with 422 per ADR-023 D8.
        //   • Case A 404 pre-check (Step 0b Reviewer BLOCKER-3 absorption) — fail 404
        //     BEFORE routing through SupersedeAndCreateAsync when no live row exists.
        //   • Routes through SupersedeAndCreateAsync (TASK-3302) and discriminates on
        //     `SaveEmployeeProfileOutcome`: Updated → emit EmployeeProfileUpdated +
        //     audit action 'UPDATED' (Case B same-day); Superseded → emit
        //     EmployeeProfileSuperseded + audit action 'SUPERSEDED' (Case C cross-day).
        //
        // Error mapping:
        //   • 422 — backdated or future-dated EffectiveFrom (validator) OR
        //           InvalidProfileSupersessionException defense-in-depth
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (stale version, ADR-019 D2)
        //   • 404 — no live row for the employee (pre-check OR KeyNotFoundException)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            UpdateEmployeeProfileRequest body,
            EmployeeProfileRepository repository,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding (mirrors GET above).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(
                actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // S33 / TASK-3308 — EffectiveFrom validator (ADR-023 D8 same-day-only-edit
            // narrowing). DateTime.UtcNow aligns with the frontend's
            // `new Date().toISOString().slice(0,10)` UTC extraction. Rejects both
            // backdated AND future-dated values with 422.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (body.EffectiveFrom != today)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "EffectiveFrom must equal today (UTC).",
                    provided = body.EffectiveFrom,
                    expected = today,
                });
            }

            // Admin-strict If-Match parse — 428 if missing / malformed / If-None-Match: *
            // (per EtagHeaderHelper.TryParseIfMatch admin-strict mode).
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var streamId = $"employee-profile-{employeeId}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // Step 0b Reviewer BLOCKER-3 absorption — Case A 404 pre-check.
                // PUT is an admin EDIT surface; it must NOT create a net-new row. We
                // also need the predecessor's profile_id + effective_from + version
                // for the EmployeeProfileSuperseded payload on the Case C branch.
                // Single in-tx SQL gathers all four fields needed: payload fields for
                // audit `previous_data`, plus predecessor metadata.
                PredecessorSnapshot? preUpdate;
                await using (var preCmd = new NpgsqlCommand(
                    """
                    SELECT profile_id, weekly_norm_hours, part_time_fraction, position,
                           effective_from, version
                    FROM employee_profiles
                    WHERE employee_id = @employeeId
                      AND effective_to IS NULL
                    """, conn, tx))
                {
                    preCmd.Parameters.AddWithValue("employeeId", employeeId);
                    await using var preReader = await preCmd.ExecuteReaderAsync(ct);
                    if (!await preReader.ReadAsync(ct))
                    {
                        preUpdate = null;
                    }
                    else
                    {
                        preUpdate = new PredecessorSnapshot(
                            ProfileId: preReader.GetGuid(0),
                            WeeklyNormHours: preReader.GetDecimal(1),
                            PartTimeFraction: preReader.GetDecimal(2),
                            Position: preReader.IsDBNull(3) ? null : preReader.GetString(3),
                            EffectiveFrom: preReader.GetFieldValue<DateOnly>(4),
                            Version: preReader.GetInt64(5));
                    }
                }

                if (preUpdate is null)
                {
                    // PUT is edit-only; no live row → 404 BEFORE routing through
                    // SupersedeAndCreateAsync's Case A branch (which would otherwise
                    // INSERT a fresh row given expectedVersion = null — but here
                    // expectedVersion is non-null from admin-strict If-Match, so
                    // Case A would actually throw OptimisticConcurrencyException with
                    // ActualVersion=null mapping to 412. We pre-empt with the cleaner
                    // 404 contract per Step 0b Reviewer BLOCKER-3).
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                // Atomic supersede-or-update with admin-strict If-Match enforcement.
                // The repository's SELECT ... FOR UPDATE locks the predecessor row;
                // routing fires Case B (same-day, expectedVersion bumps) or Case C
                // (cross-day, predecessor closed + new live row at version=1).
                SaveEmployeeProfileResult result;
                try
                {
                    var supersedeRequest = new EmployeeProfileSupersedeRequest(
                        EmployeeId: employeeId,
                        WeeklyNormHours: body.WeeklyNormHours,
                        PartTimeFraction: body.PartTimeFraction,
                        Position: body.Position,
                        EffectiveFrom: body.EffectiveFrom);
                    result = await repository.SupersedeAndCreateAsync(
                        conn, tx, supersedeRequest, expectedVersion, ct);
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
                catch (InvalidProfileSupersessionException ex)
                {
                    // Defense-in-depth — the validator rejects anything other than
                    // today (UTC) before we reach the repo, and the repo only throws
                    // this on req.EffectiveFrom < predecessor.EffectiveFrom (i.e. a
                    // backdate). Reaching here would require the predecessor's
                    // effective_from to be in the future relative to today (UTC),
                    // which is structurally impossible under the validator above.
                    // Map to 422 if it ever fires.
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }

                // S33 / TASK-3308 — outcome-discriminated emission.
                // Case A (Created) is unreachable here: the pre-check above returns 404
                // when no live row exists; even if it didn't, expectedVersion is
                // non-null and SupersedeAndCreateAsync would throw OCE in Case A.
                if (result.Outcome == SaveEmployeeProfileOutcome.Created)
                {
                    // Defense-in-depth: should never happen under the pre-check above.
                    await tx.RollbackAsync(ct);
                    throw new InvalidOperationException(
                        $"PUT routed through Case A (Created) for employee_id='{employeeId}' " +
                        "despite Case A 404 pre-check. This is a programming error.");
                }

                var profileId = result.ProfileId;
                var newVersion = result.Version;
                var auditAction = result.Outcome == SaveEmployeeProfileOutcome.Updated
                    ? "UPDATED"
                    : "SUPERSEDED";

                // Audit row.
                //
                // Case B (Updated): version_before = expectedVersion = predecessor.Version;
                //   version_after  = newVersion = predecessor.Version + 1.
                // Case C (Superseded): the closed predecessor's version is UNCHANGED
                //   (close is lifecycle, not content edit — per SupersedeAndCreateAsync
                //   xmldoc) and the new live row starts at version=1. We record the
                //   transition on the new row as version_before = predecessor.Version,
                //   version_after = result.Version (= 1). This matches the ADR-019 D8
                //   "version-transition columns" semantic — the audit row narrates the
                //   visible state delta on the employee's profile lineage.
                var previousData = JsonSerializer.Serialize(new
                {
                    weeklyNormHours = preUpdate.WeeklyNormHours,
                    partTimeFraction = preUpdate.PartTimeFraction,
                    position = preUpdate.Position,
                });
                var newData = JsonSerializer.Serialize(new
                {
                    weeklyNormHours = body.WeeklyNormHours,
                    partTimeFraction = body.PartTimeFraction,
                    position = body.Position,
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profile_audit (
                        profile_id, employee_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @profileId, @employeeId, @action,
                        @previousData::jsonb, @newData::jsonb,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("action", auditAction);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    auditCmd.Parameters.AddWithValue("versionBefore", expectedVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", newVersion);
                    auditCmd.Parameters.AddWithValue("actorId", actorId);
                    auditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                // Atomic-outbox emission (same tx as UPDATE + audit per ADR-018 D3).
                // Event type discriminated by SaveEmployeeProfileOutcome per ADR-020 D2.
                if (result.Outcome == SaveEmployeeProfileOutcome.Updated)
                {
                    // Case B — same-day in-place edit. EmployeeProfileUpdated carries
                    // post-mutation payload + version-before/after pair (S31 contract,
                    // preserved verbatim).
                    var updatedEvent = new EmployeeProfileUpdated
                    {
                        ProfileId = profileId,
                        EmployeeId = employeeId,
                        WeeklyNormHours = body.WeeklyNormHours,
                        PartTimeFraction = body.PartTimeFraction,
                        Position = body.Position,
                        VersionBefore = expectedVersion,
                        VersionAfter = newVersion,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(conn, tx, streamId, updatedEvent, ct);
                }
                else
                {
                    // Case C — cross-day supersession. EmployeeProfileSuperseded carries
                    // predecessor/successor identity + temporal validity transition
                    // (predecessor's effective_to = req.EffectiveFrom end-exclusive per
                    // ADR-018 D9; new row's effective_from = req.EffectiveFrom) +
                    // post-mutation payload of the new row + both versions.
                    var supersededEvent = new EmployeeProfileSuperseded
                    {
                        PredecessorProfileId = preUpdate.ProfileId,
                        NewProfileId = profileId,
                        EmployeeId = employeeId,
                        PredecessorEffectiveFrom = preUpdate.EffectiveFrom,
                        PredecessorEffectiveTo = body.EffectiveFrom,
                        NewEffectiveFrom = body.EffectiveFrom,
                        WeeklyNormHours = body.WeeklyNormHours,
                        PartTimeFraction = body.PartTimeFraction,
                        Position = body.Position,
                        PredecessorVersion = preUpdate.Version,
                        NewVersion = newVersion,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    await outbox.EnqueueAsync(conn, tx, streamId, supersededEvent, ct);
                }

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                var isPartTime = body.PartTimeFraction < 1.0m;
                return Results.Ok(new
                {
                    employeeId,
                    weeklyNormHours = body.WeeklyNormHours,
                    partTimeFraction = body.PartTimeFraction,
                    position = body.Position,
                    isPartTime,
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
        // 3. DELETE /api/admin/employee-profiles/{employeeId}
        //
        // S33 / TASK-3308 — NEW endpoint.
        //
        // RBAC: HROrAbove + OrgScopeValidator (mirrors GET/PUT — Step 0b BLOCKER fix).
        // Admin-strict If-Match required (ADR-019 D2). Atomic-outbox semantic: soft-
        // delete UPDATE + INSERT audit row (action='DELETED') + outbox enqueue all
        // happen in one tx (ADR-018 D3). Returns 204 No Content on success.
        //
        // ADR-023 D8 soft-delete divergence: the predecessor's `version` column is
        // UNCHANGED — soft-delete is row-state-change, not field-mutation; the
        // partial-unique-index `idx_employee_profiles_live` makes the row "disappear"
        // from live reads, so bumping version is redundant. Audit row carries
        // version_before = version_after = predecessor.version per ADR-019 D8 for
        // DELETE actions.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (live row exists, version differs)
        //   • 404 — KeyNotFoundException (no live row — also the retry-after-delete
        //           case per ADR-023 D8 row-disappearance idempotency)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapDelete("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            EmployeeProfileRepository repository,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding (mirrors GET / PUT).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(
                actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match parse — 428 if missing / malformed / If-None-Match: *
            // (per EtagHeaderHelper.TryParseIfMatch admin-strict mode).
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var streamId = $"employee-profile-{employeeId}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // Snapshot pre-delete payload state for the audit row's `previous_data`
                // JSONB. In-tx read so the audit reflects the same row the UPDATE
                // will close. The repository's SoftDeleteAsync independently enforces
                // optimistic concurrency via `AND version = @expectedVersion` in the
                // UPDATE — we don't need to pre-validate, just snapshot for audit.
                var preDelete = await repository.GetByEmployeeIdAsync(conn, tx, employeeId, ct);
                if (preDelete is null)
                {
                    // Defense-in-depth — SoftDeleteAsync will also raise
                    // KeyNotFoundException for this branch, but pre-empting avoids
                    // doing a no-op UPDATE first. Matches PUT pre-check semantic.
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                Guid profileId;
                long predecessorVersion;
                try
                {
                    // SoftDeleteAsync returns (profile_id, version) where version is
                    // UNCHANGED from the predecessor's value per ADR-023 D8.
                    var (returnedProfileId, returnedVersion) = await repository.SoftDeleteAsync(
                        conn, tx, employeeId, expectedVersion, ct);
                    profileId = returnedProfileId;
                    predecessorVersion = returnedVersion;
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
                    // Stale If-Match retry after a successful soft-delete also lands
                    // here per ADR-023 D8 row-disappearance idempotency (locked by
                    // TASK-3312 D-test).
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                // Audit row — action='DELETED' per init.sql:514 CHECK constraint;
                // version_before = version_after = predecessor.version per ADR-023 D8
                // (no version bump on soft-delete — deliberate divergence from sibling
                // ADR-019 D8 endpoints). new_data = NULL (the row no longer carries a
                // logically "current" payload after the close).
                var previousData = JsonSerializer.Serialize(new
                {
                    weeklyNormHours = preDelete.WeeklyNormHours,
                    partTimeFraction = preDelete.PartTimeFraction,
                    position = preDelete.Position,
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profile_audit (
                        profile_id, employee_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @profileId, @employeeId, 'DELETED',
                        @previousData::jsonb, NULL,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("versionBefore", predecessorVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", predecessorVersion);
                    auditCmd.Parameters.AddWithValue("actorId", actorId);
                    auditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                // Atomic-outbox emission (same tx as UPDATE + audit per ADR-018 D3).
                // EmployeeProfileSoftDeleted carries the predecessor's profile_id,
                // the close-date (effective_to = today), and the row-version (NAMED
                // `RowVersion` — NOT `Version` — to avoid shadowing DomainEventBase's
                // event-schema-version field, same disambiguation as S30
                // EntitlementConfigSoftDeleted). Actor / correlation come from
                // DomainEventBase init.
                var softDeletedEvent = new EmployeeProfileSoftDeleted
                {
                    ProfileId = profileId,
                    EmployeeId = employeeId,
                    EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow),
                    RowVersion = predecessorVersion,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                await outbox.EnqueueAsync(conn, tx, streamId, softDeletedEvent, ct);

                await tx.CommitAsync(ct);

                return Results.NoContent();
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

    // ── Request DTO ──

    /// <summary>
    /// PUT request body.
    ///
    /// <para>
    /// <b>S33 / TASK-3308 — named-record syntax + required <c>EffectiveFrom</c>.</b>
    /// Refinement cycle 1 dual-lens BLOCKER + Step 0b Reviewer W absorption: the S31
    /// DTO was a positional record (<c>WeeklyNormHours, PartTimeFraction, Position</c>);
    /// converting to named-record syntax avoids breaking any positional-pattern matches
    /// that might exist downstream, and the new <c>EffectiveFrom</c> field is required
    /// — admins must explicitly state the effective date for ADR-020 D2 routing
    /// (Case B same-day in-place vs Case C cross-day supersession). The endpoint
    /// validator narrows accepted values to <c>DateOnly.FromDateTime(DateTime.UtcNow)</c>
    /// per ADR-023 D8 same-day-only-edit.
    /// </para>
    ///
    /// <para>
    /// All four fields are required — admins must re-state the full edit shape rather
    /// than patching individual fields (mirrors the S29 WTM / S30 EntitlementConfig
    /// admin PUT precedent).
    /// </para>
    /// </summary>
    private sealed record UpdateEmployeeProfileRequest
    {
        /// <summary>S33 — required. Validator narrows to today (UTC) per ADR-023 D8.</summary>
        public DateOnly EffectiveFrom { get; init; }
        public decimal WeeklyNormHours { get; init; }
        public decimal PartTimeFraction { get; init; }
        public string? Position { get; init; }
    }

    /// <summary>
    /// Local snapshot record for the predecessor row's state read in-tx by the PUT
    /// handler BEFORE routing through <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>.
    /// Carries enough fields to (a) build the audit row's <c>previous_data</c> JSONB
    /// and (b) hydrate the <see cref="EmployeeProfileSuperseded"/> event payload on
    /// Case C (cross-day supersession) without a second SELECT.
    /// </summary>
    private sealed record PredecessorSnapshot(
        Guid ProfileId,
        decimal WeeklyNormHours,
        decimal PartTimeFraction,
        string? Position,
        DateOnly EffectiveFrom,
        long Version);
}
