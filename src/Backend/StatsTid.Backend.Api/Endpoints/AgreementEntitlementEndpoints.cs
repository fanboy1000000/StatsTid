using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// TASK 1B-2 -- Sub-resource entitlement endpoints scoped under an agreement config.
/// Routes: <c>/api/agreement-configs/{configId}/entitlements[/{entitlementConfigId}]</c>.
/// All endpoints require <c>GlobalAdminOnly</c> authorization.
///
/// <para>
/// POST derives <c>agreement_code</c> and <c>ok_version</c> from the parent agreement config --
/// the body only provides entitlement-specific fields. PUT/DELETE validate that the targeted
/// <c>entitlementConfigId</c> belongs to the parent's natural key.
/// </para>
///
/// <para>
/// All mutations check <c>entitlementsReadOnly</c> (count of sibling configs sharing the same
/// agreement_code + ok_version &gt; 1) and return 409 if true -- edits must target the canonical
/// (ACTIVE) config to avoid ambiguity about which config "owns" the entitlements.
/// </para>
///
/// <para>
/// Transaction / audit / outbox / error-handling patterns mirror
/// <see cref="EntitlementConfigEndpoints"/> verbatim.
/// </para>
/// </summary>
public static class AgreementEntitlementEndpoints
{
    public static WebApplication MapAgreementEntitlementEndpoints(this WebApplication app)
    {
        // ===================================================================
        // 1. GET /api/agreement-configs/{configId:guid}/entitlements
        //    Returns open entitlements matching parent's (agreement_code, ok_version).
        //    404 if parent agreement config not found.
        // ===================================================================
        app.MapGet("/api/agreement-configs/{configId:guid}/entitlements", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            CancellationToken ct) =>
        {
            var parent = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (parent is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            var entitlements = await entitlementConfigRepo.GetOpenByAgreementAsync(
                parent.AgreementCode, parent.OkVersion, ct);

            return Results.Ok(entitlements.Select(MapToResponse).ToList());
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<IEnumerable<EntitlementConfigResponse>>(StatusCodes.Status200OK); // S118 / TASK-11800 — BARE array

        // ===================================================================
        // 2. POST /api/agreement-configs/{configId:guid}/entitlements
        //    Create an entitlement config scoped to the parent's (agreement_code, ok_version).
        //    Derives agreement_code and ok_version FROM THE PARENT -- body only provides
        //    entitlement-specific fields.
        //
        //    Mirrors EntitlementConfigEndpoints POST (lines 145-311) verbatim:
        //    same-day-only-edit validator, AcquireLockAsync, SupersedeAndCreateAsync,
        //    23505 handling, audit + outbox + audit_projection, ETag on 201.
        // ===================================================================
        app.MapPost("/api/agreement-configs/{configId:guid}/entitlements", async (
            Guid configId,
            CreateChildEntitlementRequest body,
            AgreementConfigRepository agreementConfigRepo,
            EntitlementConfigRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EntitlementConfigCreated> createdMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Resolve parent agreement config.
            var parent = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (parent is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            // 2. Check entitlementsReadOnly -- 409 if multiple sibling configs share this
            //    (agreement_code, ok_version).
            var siblingConfigs = await agreementConfigRepo.GetByAgreementAsync(
                parent.AgreementCode, parent.OkVersion, ct);
            if (siblingConfigs.Count > 1)
            {
                return Results.Conflict(new
                {
                    error = "Entitlements are read-only because multiple configs share this agreement code and version",
                });
            }

            // 3. Derive agreement_code and ok_version from parent.
            var agreementCode = parent.AgreementCode;
            var okVersion = parent.OkVersion;

            // 4. Same-day-only-edit validator (cycle 3 symmetric forbid).
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var requestedEffectiveFrom = body.EffectiveFrom ?? today;
            if (requestedEffectiveFrom != today)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "effective_from must equal today (same-day-only edits permitted)",
                    suppliedEffectiveFrom = requestedEffectiveFrom,
                    today = today,
                });
            }

            // 4b. S73 / TASK-7301 (SPRINT-73 R2 construction-enforcement, the S68-B1 lesson):
            //     CARE_DAY/SENIOR_DAY are FULL-DAY-ONLY per the D-A owner ruling -- the SAME
            //     422 guard as the primary admin surface (one shared predicate; a guard on
            //     only one of the two config-writing surfaces would be the wiring-drift class).
            if (FullDayOnlyGuard.IsViolated(body.EntitlementType, body.FullDayOnly, out var fullDayError))
                return Results.UnprocessableEntity(fullDayError!);

            var streamId = $"entitlement-config-{body.EntitlementType}-{agreementCode}-{okVersion}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // 5. Acquire the natural-key lock on the currently-open row (if any).
                //    For POST, we expect null (Case A). If an open row exists, the admin
                //    should be using PUT -- surface 409 (resource exists, wrong verb).
                var existingOpen = await repo.AcquireLockAsync(
                    conn, tx, body.EntitlementType, agreementCode, okVersion, ct);
                if (existingOpen is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "An open entitlement config already exists for this natural key; use PUT to edit.",
                        currentState = MapToResponse(existingOpen),
                    });
                }

                // 6. Build the new config row.
                var newConfigId = Guid.NewGuid();
                var newConfig = new EntitlementConfig
                {
                    ConfigId = newConfigId,
                    EntitlementType = body.EntitlementType,
                    AgreementCode = agreementCode,
                    OkVersion = okVersion,
                    AnnualQuota = body.AnnualQuota,
                    AccrualModel = body.AccrualModel,
                    ResetMonth = body.ResetMonth,
                    CarryoverMax = body.CarryoverMax,
                    ProRateByPartTime = body.ProRateByPartTime,
                    IsPerEpisode = body.IsPerEpisode,
                    MinAge = body.MinAge,
                    Description = body.Description,
                    FullDayOnly = body.FullDayOnly, // S73 / TASK-7301 (R2)
                    EffectiveFrom = today,
                };

                SaveEntitlementConfigResult saveResult;
                try
                {
                    saveResult = await repo.SupersedeAndCreateAsync(
                        conn, tx, newConfig, predecessor: null, expectedCurrentVersion: null, ct);
                }
                catch (PostgresException ex) when (
                    ex.SqlState == "23505" && ex.ConstraintName == "idx_ec_natural_key_history")
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        message = "A closed-today row already exists for this natural key; the history-uniqueness index forbids INSERT at the same effective_from.",
                    }, statusCode: 412);
                }
                catch (PostgresException ex) when (
                    ex.SqlState == "23505" && ex.ConstraintName == "idx_ec_natural_key_open")
                {
                    await tx.RollbackAsync(ct);
                    var currentState = await repo.GetCurrentOpenAsync(
                        body.EntitlementType, agreementCode, okVersion, ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        message = "Another entitlement config was created concurrently for this natural key; refresh and retry.",
                        currentState = currentState is null ? null : MapToResponse(currentState),
                    }, statusCode: 412);
                }

                var persistedConfig = saveResult.Config;
                var persistedVersion = saveResult.Version;

                // 7. Audit (CREATED -- version_before = null per ADR-019 D8) + outbox emission
                //    (ADR-018 D3 atomic same-tx).
                await repo.AppendAuditAsync(
                    conn, tx,
                    persistedConfig.ConfigId,
                    body.EntitlementType, agreementCode, okVersion,
                    action: "CREATED",
                    previousData: null,
                    newData: JsonSerializer.Serialize(body),
                    versionBefore: null,
                    versionAfter: persistedVersion,
                    actorId, actorRole, ct);

                var createdEvent = new EntitlementConfigCreated
                {
                    ConfigId = persistedConfig.ConfigId,
                    EntitlementType = persistedConfig.EntitlementType,
                    AgreementCode = persistedConfig.AgreementCode,
                    OkVersion = persistedConfig.OkVersion,
                    EffectiveFrom = persistedConfig.EffectiveFrom,
                    EffectiveTo = persistedConfig.EffectiveTo,
                    RowVersion = persistedVersion,
                    AnnualQuota = persistedConfig.AnnualQuota,
                    AccrualModel = persistedConfig.AccrualModel,
                    ResetMonth = persistedConfig.ResetMonth,
                    CarryoverMax = persistedConfig.CarryoverMax,
                    ProRateByPartTime = persistedConfig.ProRateByPartTime,
                    IsPerEpisode = persistedConfig.IsPerEpisode,
                    MinAge = persistedConfig.MinAge,
                    Description = persistedConfig.Description,
                    FullDayOnly = persistedConfig.FullDayOnly, // S73 / TASK-7301 (R2, additive-nullable)
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, createdEvent, ct);

                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(createdEvent.OccurredAt));
                var auditRow = createdMapper.Map(createdEvent, auditCtx);
                await auditRepo.InsertAsync(conn, tx, createdEvent.EventId, outboxId, createdEvent.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{persistedVersion}\"";
                return Results.Created(
                    $"/api/agreement-configs/{configId}/entitlements/{persistedConfig.ConfigId}",
                    MapToResponse(persistedConfig));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<EntitlementConfigResponse>(StatusCodes.Status201Created); // S118 / TASK-11800

        // ===================================================================
        // 3. PUT /api/agreement-configs/{configId:guid}/entitlements/{entitlementConfigId:guid}
        //    Edit an entitlement config scoped to the parent's (agreement_code, ok_version).
        //    Mirrors EntitlementConfigEndpoints PUT verbatim: admin-strict If-Match,
        //    AcquireLockAsync, reset_month/accrual_model immutability guard,
        //    SupersedeAndCreateAsync, dual-emission on Case B, single-emission on Case C.
        // ===================================================================
        app.MapPut("/api/agreement-configs/{configId:guid}/entitlements/{entitlementConfigId:guid}", async (
            Guid configId,
            Guid entitlementConfigId,
            UpdateChildEntitlementRequest body,
            AgreementConfigRepository agreementConfigRepo,
            EntitlementConfigRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EntitlementConfigCreated> createdMapper,
            IAuditProjectionMapper<EntitlementConfigSuperseded> supersededMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Resolve parent agreement config.
            var parent = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (parent is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            // 2. Check entitlementsReadOnly -- 409 if multiple sibling configs.
            var siblingConfigs = await agreementConfigRepo.GetByAgreementAsync(
                parent.AgreementCode, parent.OkVersion, ct);
            if (siblingConfigs.Count > 1)
            {
                return Results.Conflict(new
                {
                    error = "Entitlements are read-only because multiple configs share this agreement code and version",
                });
            }

            var agreementCode = parent.AgreementCode;
            var okVersion = parent.OkVersion;

            // 3. Same-day-only-edit validator (cycle 3 symmetric forbid).
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            if (body.EffectiveFrom != today)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "effective_from must equal today (same-day-only edits permitted)",
                    suppliedEffectiveFrom = body.EffectiveFrom,
                    today = today,
                });
            }

            // 3b. S73 / TASK-7301 (SPRINT-73 R2 construction-enforcement): a PUT must not
            //     silently un-rule the D-A full-day-only ruling -- flag false/ABSENT -> 422.
            if (FullDayOnlyGuard.IsViolated(body.EntitlementType, body.FullDayOnly, out var fullDayError))
                return Results.UnprocessableEntity(fullDayError!);

            // 4. Admin-strict If-Match parse -- 428 if missing or If-None-Match: * supplied.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var streamId = $"entitlement-config-{body.EntitlementType}-{agreementCode}-{okVersion}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // 5. Acquire the natural-key lock on the open row. PUT requires a predecessor;
                //    a null result means the row was soft-deleted (or never existed) -- 409.
                var predecessor = await repo.AcquireLockAsync(
                    conn, tx, body.EntitlementType, agreementCode, okVersion, ct);
                if (predecessor is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "No open entitlement config exists for this natural key; use POST to create.",
                    });
                }

                // 6. Validate {entitlementConfigId} matches the open row's ConfigId.
                //    404 if mismatch -- "Entitlement does not belong to this agreement config".
                if (predecessor.ConfigId != entitlementConfigId)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new
                    {
                        error = "Entitlement does not belong to this agreement config",
                        urlEntitlementConfigId = entitlementConfigId,
                        currentOpenConfigId = predecessor.ConfigId,
                    });
                }

                // 7. Validate the open row's (agreement_code, ok_version) matches parent's.
                //    404 if mismatch (defense-in-depth -- AcquireLockAsync already scoped by
                //    parent-derived agreement_code + okVersion, so this is unreachable under
                //    correct usage).
                if (!string.Equals(predecessor.AgreementCode, agreementCode, StringComparison.Ordinal) ||
                    !string.Equals(predecessor.OkVersion, okVersion, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new
                    {
                        error = "Entitlement does not belong to this agreement config",
                    });
                }

                // 8. reset_month / accrual_model immutability guard.
                if (body.ResetMonth != predecessor.ResetMonth ||
                    !string.Equals(body.AccrualModel, predecessor.AccrualModel, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "reset_month and accrual_model are agreement-defining and cannot be edited via admin CRUD; create a new ok_version row instead",
                        supplied = new { reset_month = body.ResetMonth, accrual_model = body.AccrualModel },
                        immutable = new[] { "reset_month", "accrual_model" },
                    });
                }

                // 9. Build the new config row and call SupersedeAndCreateAsync.
                var newConfig = new EntitlementConfig
                {
                    ConfigId = Guid.NewGuid(), // only used on Case B (cross-day INSERT)
                    EntitlementType = body.EntitlementType,
                    AgreementCode = agreementCode,
                    OkVersion = okVersion,
                    AnnualQuota = body.AnnualQuota,
                    AccrualModel = body.AccrualModel,
                    ResetMonth = body.ResetMonth,
                    CarryoverMax = body.CarryoverMax,
                    ProRateByPartTime = body.ProRateByPartTime,
                    IsPerEpisode = body.IsPerEpisode,
                    MinAge = body.MinAge,
                    Description = body.Description,
                    FullDayOnly = body.FullDayOnly, // S73 / TASK-7301 (R2 version-survival)
                    EffectiveFrom = today,
                };

                SaveEntitlementConfigResult saveResult;
                try
                {
                    saveResult = await repo.SupersedeAndCreateAsync(
                        conn, tx, newConfig, predecessor, expectedCurrentVersion: expectedVersion, ct);
                }
                catch (OptimisticConcurrencyException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = ex.ExpectedVersion,
                        actualVersion = ex.ActualVersion,
                        currentState = MapToResponse(predecessor),
                    }, statusCode: 412);
                }
                catch (InvalidProfileSupersessionException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.BadRequest(new { error = ex.Message });
                }

                var isCrossDay = saveResult.SupersededConfigId is not null;

                if (isCrossDay)
                {
                    // -- Case B -- dual-emission per ADR-019 D1.

                    // Emission 1: predecessor close -- SUPERSEDED audit + EntitlementConfigSuperseded
                    await repo.AppendAuditAsync(
                        conn, tx,
                        predecessor.ConfigId,
                        body.EntitlementType, agreementCode, okVersion,
                        action: "SUPERSEDED",
                        previousData: JsonSerializer.Serialize(predecessor),
                        newData: $"{{\"supersededByConfigId\":\"{saveResult.Config.ConfigId}\"}}",
                        versionBefore: predecessor.Version,
                        versionAfter: predecessor.Version,
                        actorId, actorRole, ct);

                    var supersededEvent = new EntitlementConfigSuperseded
                    {
                        ConfigId = predecessor.ConfigId,
                        EntitlementType = predecessor.EntitlementType,
                        AgreementCode = predecessor.AgreementCode,
                        OkVersion = predecessor.OkVersion,
                        EffectiveFrom = predecessor.EffectiveFrom,
                        EffectiveTo = today,
                        RowVersion = predecessor.Version,
                        SupersededByConfigId = saveResult.Config.ConfigId,
                        FullDayOnly = predecessor.FullDayOnly, // S73 / TASK-7301 (R2, additive-nullable)
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var supersededOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, supersededEvent, ct);

                    var supersededAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(supersededEvent.OccurredAt));
                    var supersededAuditRow = supersededMapper.Map(supersededEvent, supersededAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, supersededEvent.EventId, supersededOutboxId, supersededEvent.EventType, supersededAuditRow, supersededAuditCtx, ct);

                    // Emission 2: new row CREATED audit + EntitlementConfigCreated outbox.
                    await repo.AppendAuditAsync(
                        conn, tx,
                        saveResult.Config.ConfigId,
                        body.EntitlementType, agreementCode, okVersion,
                        action: "CREATED",
                        previousData: null,
                        newData: JsonSerializer.Serialize(body),
                        versionBefore: null,
                        versionAfter: saveResult.Version,
                        actorId, actorRole, ct);
                }
                else
                {
                    // -- Case C -- same-day in-place UPDATE. Single UPDATED audit.
                    await repo.AppendAuditAsync(
                        conn, tx,
                        saveResult.Config.ConfigId,
                        body.EntitlementType, agreementCode, okVersion,
                        action: "UPDATED",
                        previousData: JsonSerializer.Serialize(predecessor),
                        newData: JsonSerializer.Serialize(body),
                        versionBefore: predecessor.Version,
                        versionAfter: saveResult.Version,
                        actorId, actorRole, ct);
                }

                var createdEvent = new EntitlementConfigCreated
                {
                    ConfigId = saveResult.Config.ConfigId,
                    EntitlementType = saveResult.Config.EntitlementType,
                    AgreementCode = saveResult.Config.AgreementCode,
                    OkVersion = saveResult.Config.OkVersion,
                    EffectiveFrom = saveResult.Config.EffectiveFrom,
                    EffectiveTo = saveResult.Config.EffectiveTo,
                    RowVersion = saveResult.Version,
                    AnnualQuota = saveResult.Config.AnnualQuota,
                    AccrualModel = saveResult.Config.AccrualModel,
                    ResetMonth = saveResult.Config.ResetMonth,
                    CarryoverMax = saveResult.Config.CarryoverMax,
                    ProRateByPartTime = saveResult.Config.ProRateByPartTime,
                    IsPerEpisode = saveResult.Config.IsPerEpisode,
                    MinAge = saveResult.Config.MinAge,
                    Description = saveResult.Config.Description,
                    FullDayOnly = saveResult.Config.FullDayOnly, // S73 / TASK-7301 (R2, additive-nullable)
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                var createdOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, createdEvent, ct);

                var createdAuditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(createdEvent.OccurredAt));
                var createdAuditRow = createdMapper.Map(createdEvent, createdAuditCtx);
                await auditRepo.InsertAsync(conn, tx, createdEvent.EventId, createdOutboxId, createdEvent.EventType, createdAuditRow, createdAuditCtx, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
                return Results.Ok(MapToResponse(saveResult.Config));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<EntitlementConfigResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        // ===================================================================
        // 4. DELETE /api/agreement-configs/{configId:guid}/entitlements/{entitlementConfigId:guid}
        //    Soft-delete an entitlement config scoped to the parent.
        //    Mirrors EntitlementConfigEndpoints DELETE verbatim: admin-strict If-Match,
        //    SELECT ... FOR UPDATE by entitlementConfigId, validate parent membership,
        //    check effective_to IS NULL, version match, SoftDeleteAsync, audit + outbox.
        // ===================================================================
        app.MapDelete("/api/agreement-configs/{configId:guid}/entitlements/{entitlementConfigId:guid}", async (
            Guid configId,
            Guid entitlementConfigId,
            AgreementConfigRepository agreementConfigRepo,
            EntitlementConfigRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EntitlementConfigSoftDeleted> softDeletedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Resolve parent agreement config.
            var parent = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (parent is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            // 2. Check entitlementsReadOnly -- 409 if multiple sibling configs.
            var siblingConfigs = await agreementConfigRepo.GetByAgreementAsync(
                parent.AgreementCode, parent.OkVersion, ct);
            if (siblingConfigs.Count > 1)
            {
                return Results.Conflict(new
                {
                    error = "Entitlements are read-only because multiple configs share this agreement code and version",
                });
            }

            var agreementCode = parent.AgreementCode;
            var okVersion = parent.OkVersion;

            // 3. Admin-strict If-Match parse.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // 4. Look up the row by entitlementConfigId under SELECT ... FOR UPDATE.
                EntitlementConfig? row = null;
                await using (var lockCmd = new NpgsqlCommand(
                    "SELECT * FROM entitlement_configs WHERE config_id = @configId FOR UPDATE",
                    conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("configId", entitlementConfigId);
                    await using var reader = await lockCmd.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        row = new EntitlementConfig
                        {
                            ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
                            EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
                            AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
                            OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
                            AnnualQuota = reader.GetDecimal(reader.GetOrdinal("annual_quota")),
                            AccrualModel = reader.GetString(reader.GetOrdinal("accrual_model")),
                            ResetMonth = reader.GetInt32(reader.GetOrdinal("reset_month")),
                            CarryoverMax = reader.GetDecimal(reader.GetOrdinal("carryover_max")),
                            ProRateByPartTime = reader.GetBoolean(reader.GetOrdinal("pro_rate_by_part_time")),
                            IsPerEpisode = reader.GetBoolean(reader.GetOrdinal("is_per_episode")),
                            MinAge = reader.IsDBNull(reader.GetOrdinal("min_age"))
                                ? null
                                : reader.GetInt32(reader.GetOrdinal("min_age")),
                            Description = reader.IsDBNull(reader.GetOrdinal("description"))
                                ? null
                                : reader.GetString(reader.GetOrdinal("description")),
                            // S73 / TASK-7301 (R2): keeps the DELETE audit's previousData honest.
                            FullDayOnly = reader.GetBoolean(reader.GetOrdinal("full_day_only")),
                            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                            Version = reader.GetInt64(reader.GetOrdinal("version")),
                            EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
                            EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
                                ? null
                                : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
                        };
                    }
                }

                if (row is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Entitlement config not found" });
                }

                // 5. Validate (agreement_code, ok_version) matches parent -- 404 if mismatch.
                if (!string.Equals(row.AgreementCode, agreementCode, StringComparison.Ordinal) ||
                    !string.Equals(row.OkVersion, okVersion, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new
                    {
                        error = "Entitlement does not belong to this agreement config",
                    });
                }

                // 6. 409 disjoint -- already closed (soft-deleted or superseded).
                if (row.EffectiveTo is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "Entitlement config is already closed (soft-deleted or superseded); cannot delete.",
                        effectiveTo = row.EffectiveTo,
                    });
                }

                // 7. 412 stale -- If-Match version doesn't match.
                if (row.Version != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = expectedVersion,
                        actualVersion = row.Version,
                        currentState = MapToResponse(row),
                    }, statusCode: 412);
                }

                // 8. Soft-delete via repo (stamps effective_to = today).
                var closedRow = await repo.SoftDeleteAsync(conn, tx, row, today, ct);

                // 9. DELETED audit + EntitlementConfigSoftDeleted outbox.
                await repo.AppendAuditAsync(
                    conn, tx,
                    closedRow.ConfigId,
                    closedRow.EntitlementType, closedRow.AgreementCode, closedRow.OkVersion,
                    action: "DELETED",
                    previousData: JsonSerializer.Serialize(row),
                    newData: null,
                    versionBefore: row.Version,
                    versionAfter: row.Version,
                    actorId, actorRole, ct);

                var streamId = $"entitlement-config-{closedRow.EntitlementType}-{closedRow.AgreementCode}-{closedRow.OkVersion}";
                var softDeletedEvent = new EntitlementConfigSoftDeleted
                {
                    ConfigId = closedRow.ConfigId,
                    EntitlementType = closedRow.EntitlementType,
                    AgreementCode = closedRow.AgreementCode,
                    OkVersion = closedRow.OkVersion,
                    EffectiveFrom = closedRow.EffectiveFrom,
                    EffectiveTo = closedRow.EffectiveTo,
                    RowVersion = closedRow.Version,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, softDeletedEvent, ct);

                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(softDeletedEvent.OccurredAt));
                var auditRow = softDeletedMapper.Map(softDeletedEvent, auditCtx);
                await auditRepo.InsertAsync(conn, tx, softDeletedEvent.EventId, outboxId, softDeletedEvent.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                // 204 No Content -- no body, no ETag header (resource gone).
                return Results.NoContent();
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces(StatusCodes.Status204NoContent); // S118 / TASK-11800 — declared-204 (no body, intentionally)

        return app;
    }

    // -- Response Mapping --

    // S118 / TASK-11800 (owner ruling #2): the byte-identical 16-member copy collapsed into
    // the ONE shared EntitlementConfigResponse shape (BYTE-IDENTICAL wire JSON — this copy
    // already carried fullDayOnly in the same position).
    private static EntitlementConfigResponse MapToResponse(EntitlementConfig c) =>
        EntitlementConfigResponse.FromModel(c);

    // -- Request DTOs (co-located) --

    /// <summary>
    /// POST request body for sub-resource entitlement creation. Does NOT include
    /// agreement_code or ok_version -- those are derived from the parent agreement config.
    /// <see cref="EffectiveFrom"/> is OPTIONAL -- when omitted, the endpoint defaults it to
    /// today; any other supplied date results in 422.
    /// </summary>
    private sealed class CreateChildEntitlementRequest
    {
        public required string EntitlementType { get; init; }
        public required decimal AnnualQuota { get; init; }
        public required string AccrualModel { get; init; }
        public required int ResetMonth { get; init; }
        public required decimal CarryoverMax { get; init; }
        public required bool ProRateByPartTime { get; init; }
        public required bool IsPerEpisode { get; init; }
        public int? MinAge { get; init; }
        public string? Description { get; init; }
        // S73 / TASK-7301 (R2): ABSENT deserializes to false -> 422 for CARE_DAY/SENIOR_DAY.
        public bool FullDayOnly { get; init; }
        public DateOnly? EffectiveFrom { get; init; }
    }

    /// <summary>
    /// PUT request body for sub-resource entitlement update. Does NOT include
    /// agreement_code or ok_version -- those are derived from the parent agreement config.
    /// <see cref="EffectiveFrom"/> is REQUIRED and must equal today per same-day-only-edit
    /// validator. reset_month / accrual_model must match the predecessor (immutability guard).
    /// </summary>
    private sealed class UpdateChildEntitlementRequest
    {
        public required string EntitlementType { get; init; }
        public required decimal AnnualQuota { get; init; }
        public required string AccrualModel { get; init; }
        public required int ResetMonth { get; init; }
        public required decimal CarryoverMax { get; init; }
        public required bool ProRateByPartTime { get; init; }
        public required bool IsPerEpisode { get; init; }
        public int? MinAge { get; init; }
        public string? Description { get; init; }
        // S73 / TASK-7301 (R2 version-survival): the full config shape round-trips the flag.
        public bool FullDayOnly { get; init; }
        public required DateOnly EffectiveFrom { get; init; }
    }
}
