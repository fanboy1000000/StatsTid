using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints;

public static class AgreementConfigEndpoints
{
    public static WebApplication MapAgreementConfigEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/agreement-configs — List all configs, optionally filter by status
        //    The list response includes `version` per row so the frontend can compose
        //    `If-Match: "<version>"` for the next mutation without a separate by-id GET.
        // ═══════════════════════════════════════════
        app.MapGet("/api/agreement-configs", async (
            string? status,
            AgreementConfigRepository agreementConfigRepo,
            CancellationToken ct) =>
        {
            IReadOnlyList<AgreementConfigEntity> configs;

            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalized = status.Trim().ToUpperInvariant();
                if (normalized is not ("DRAFT" or "ACTIVE" or "ARCHIVED"))
                    return Results.BadRequest(new { error = "Invalid status filter. Must be DRAFT, ACTIVE, or ARCHIVED" });

                configs = await agreementConfigRepo.GetByStatusAsync(normalized, ct);
            }
            else
            {
                configs = await agreementConfigRepo.GetAllAsync(ct);
            }

            return Results.Ok(configs.Select(MapEntityToResponse).ToList());
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<IEnumerable<AgreementConfigResponse>>(StatusCodes.Status200OK); // S118 / TASK-11800 — BARE array

        // ═══════════════════════════════════════════
        // 2. GET /api/agreement-configs/{configId:guid} — Get single config by ID
        //    Sets `ETag: "<version>"` so the frontend can use it as the next If-Match.
        // ═══════════════════════════════════════════
        app.MapGet("/api/agreement-configs/{configId:guid}", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var entity = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (entity is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            // Fetch open entitlement configs for this agreement's (agreement_code, ok_version).
            var entitlements = await entitlementConfigRepo.GetOpenByAgreementAsync(
                entity.AgreementCode, entity.OkVersion, ct);

            // Entitlements are read-only when another agreement_configs row shares the same
            // (agreement_code, ok_version) — edits must target the canonical (ACTIVE) config.
            var siblingConfigs = await agreementConfigRepo.GetByAgreementAsync(
                entity.AgreementCode, entity.OkVersion, ct);
            var entitlementsReadOnly = siblingConfigs.Count > 1;

            context.Response.Headers.ETag = $"\"{entity.Version}\"";
            return Results.Ok(MapEntityToResponseWithEntitlements(entity, entitlements, entitlementsReadOnly));
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<AgreementConfigWithEntitlementsResponse>(StatusCodes.Status200OK); // S118 / TASK-11800 — ruling #2: embedded rows now carry fullDayOnly

        // ═══════════════════════════════════════════
        // 3. GET /api/agreement-configs/{agreementCode}/{okVersion} — Get all versions for agreement
        //    List response — `version` per row in body (no single ETag header — there are
        //    multiple rows). Frontend composes If-Match from the row it intends to mutate.
        // ═══════════════════════════════════════════
        app.MapGet("/api/agreement-configs/{agreementCode}/{okVersion}", async (
            string agreementCode,
            string okVersion,
            AgreementConfigRepository agreementConfigRepo,
            CancellationToken ct) =>
        {
            var configs = await agreementConfigRepo.GetByAgreementAsync(agreementCode, okVersion, ct);
            return Results.Ok(configs.Select(MapEntityToResponse).ToList());
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<IEnumerable<AgreementConfigResponse>>(StatusCodes.Status200OK); // S118 / TASK-11800 — BARE array

        // ═══════════════════════════════════════════
        // 4. POST /api/agreement-configs — Create new DRAFT config
        //    Sets `ETag: "1"` on the 201 response (DB DEFAULT for first-create) so the
        //    next PUT/Publish/Archive can If-Match on this version. No If-* parsing —
        //    create endpoints have no preceding row to assert against.
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs", async (
            AgreementConfigRequest request,
            AgreementConfigRepository agreementConfigRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<AgreementConfigCreated> createdMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (valid, validationError) = ValidateRequest(request);
            if (!valid)
                return Results.BadRequest(new { error = validationError });

            if (!TryParseNormModel(request.NormModel, out var normModel))
                return Results.BadRequest(new { error = "Invalid normModel. Must be WEEKLY_HOURS or ANNUAL_ACTIVITY" });

            var entity = BuildEntityFromRequest(request, normModel, actor.ActorId ?? "system");

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3 atomic-outbox shape).
            // Create uses the v2 AppendAuditAsync overload — the first-create has no prior
            // version-transition to record (version_before is NULL by design).
            //
            // S118 / TASK-11800 (owner ruling #1, the dead-branch class): the INSERT now runs
            // via CreateReturningAsync (INSERT … RETURNING *) INSIDE this same transaction —
            // the post-commit re-read and its `created is not null ? … : {configId}` fork are
            // structurally dead. The audit/outbox/audit-projection appends stay in-tx AFTER
            // the INSERT, byte-order unchanged (moving them would be a P3 violation).
            Guid configId;
            AgreementConfigEntity created;
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                created = await agreementConfigRepo.CreateReturningAsync(conn, tx, entity, ct);
                configId = created.ConfigId;

                await agreementConfigRepo.AppendAuditAsync(
                    conn, tx, configId, "CREATED", null, SerializeForAudit(request),
                    actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN", ct);

                var @event = new AgreementConfigCreated
                {
                    ConfigId = configId,
                    AgreementCode = request.AgreementCode,
                    OkVersion = request.OkVersion,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx projection write — atomic with the
                // agreement_configs row + outbox row per ADR-018 D3/D13).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{configId}", @event, ct);

                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt));
                var auditRow = createdMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);
            }

            // ETag for the 201 response — sourced from the RETURNING-hydrated row (DB DEFAULT 1
            // on first-create; the `?? 1L` fallback died with the re-read, ruling #1).
            context.Response.Headers.ETag = $"\"{created.Version}\"";
            return Results.Created($"/api/agreement-configs/{configId}",
                MapEntityToResponse(created));
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<AgreementConfigResponse>(StatusCodes.Status201Created); // S118 / TASK-11800 — ruling #1: ALWAYS the full entity

        // ═══════════════════════════════════════════
        // 5. POST /api/agreement-configs/{configId:guid}/clone — Clone existing config as new DRAFT
        //    Sets `ETag: "1"` on the 201 response. No If-* parsing — clone is a create-from-source.
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs/{configId:guid}/clone", async (
            Guid configId,
            string? agreementCode,
            string? okVersion,
            AgreementConfigRepository agreementConfigRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<AgreementConfigCloned> clonedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var source = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (source is null)
                return Results.NotFound(new { error = "Source agreement config not found" });

            var cloneAgreementCode = !string.IsNullOrWhiteSpace(agreementCode) ? agreementCode : source.AgreementCode;
            var cloneOkVersion = !string.IsNullOrWhiteSpace(okVersion) ? okVersion : source.OkVersion;

            var cloneEntity = new AgreementConfigEntity
            {
                ConfigId = Guid.Empty, // Will be assigned by repo
                AgreementCode = cloneAgreementCode,
                OkVersion = cloneOkVersion,
                Status = AgreementConfigStatus.DRAFT,
                WeeklyNormHours = source.WeeklyNormHours,
                NormPeriodWeeks = source.NormPeriodWeeks,
                NormModel = source.NormModel,
                AnnualNormHours = source.AnnualNormHours,
                MaxFlexBalance = source.MaxFlexBalance,
                FlexCarryoverMax = source.FlexCarryoverMax,
                HasOvertime = source.HasOvertime,
                HasMerarbejde = source.HasMerarbejde,
                OvertimeThreshold50 = source.OvertimeThreshold50,
                OvertimeThreshold100 = source.OvertimeThreshold100,
                EveningSupplementEnabled = source.EveningSupplementEnabled,
                NightSupplementEnabled = source.NightSupplementEnabled,
                WeekendSupplementEnabled = source.WeekendSupplementEnabled,
                HolidaySupplementEnabled = source.HolidaySupplementEnabled,
                EveningStart = source.EveningStart,
                EveningEnd = source.EveningEnd,
                NightStart = source.NightStart,
                NightEnd = source.NightEnd,
                EveningRate = source.EveningRate,
                NightRate = source.NightRate,
                WeekendSaturdayRate = source.WeekendSaturdayRate,
                WeekendSundayRate = source.WeekendSundayRate,
                HolidayRate = source.HolidayRate,
                OnCallDutyEnabled = source.OnCallDutyEnabled,
                OnCallDutyRate = source.OnCallDutyRate,
                CallInWorkEnabled = source.CallInWorkEnabled,
                CallInMinimumHours = source.CallInMinimumHours,
                CallInRate = source.CallInRate,
                TravelTimeEnabled = source.TravelTimeEnabled,
                WorkingTravelRate = source.WorkingTravelRate,
                NonWorkingTravelRate = source.NonWorkingTravelRate,
                MaxDailyHours = source.MaxDailyHours,
                MinimumRestHours = source.MinimumRestHours,
                RestPeriodDerogationAllowed = source.RestPeriodDerogationAllowed,
                WeeklyMaxHoursReferencePeriod = source.WeeklyMaxHoursReferencePeriod,
                VoluntaryUnsocialHoursAllowed = source.VoluntaryUnsocialHoursAllowed,
                // S122 / TASK-12200 field-loss fix: the four overtime-governance fields were
                // dropped from the clone, so a cloned config PERSISTED the CLR-default compensation
                // model instead of the source's. The repo InsertSql writes default_compensation_model,
                // so the copied value lands.
                DefaultCompensationModel = source.DefaultCompensationModel,
                EmployeeCompensationChoice = source.EmployeeCompensationChoice,
                MaxOvertimeHoursPerPeriod = source.MaxOvertimeHoursPerPeriod,
                OvertimeRequiresPreApproval = source.OvertimeRequiresPreApproval,
                CreatedBy = actor.ActorId ?? "system",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ClonedFromId = source.ConfigId,
                Description = source.Description,
            };

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3 atomic-outbox shape).
            // S118 / TASK-11800 (owner ruling #1): clone shares the agreement-config repo
            // create path — CreateReturningAsync kills the same post-commit re-read fork here.
            // The audit/outbox/audit-projection appends stay in-tx AFTER the INSERT, byte-order
            // unchanged.
            Guid newConfigId;
            AgreementConfigEntity created;
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                created = await agreementConfigRepo.CreateReturningAsync(conn, tx, cloneEntity, ct);
                newConfigId = created.ConfigId;

                await agreementConfigRepo.AppendAuditAsync(
                    conn, tx, newConfigId, "CLONED", null, $"{{\"sourceConfigId\":\"{configId}\"}}",
                    actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN", ct);

                var @event = new AgreementConfigCloned
                {
                    ConfigId = newConfigId,
                    SourceConfigId = configId,
                    AgreementCode = cloneAgreementCode,
                    OkVersion = cloneOkVersion,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx projection write).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{newConfigId}", @event, ct);

                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt));
                var auditRow = clonedMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);
            }

            // ETag for the 201 response — sourced from the RETURNING-hydrated row (same
            // fork-death as POST /create, ruling #1).
            context.Response.Headers.ETag = $"\"{created.Version}\"";
            return Results.Created($"/api/agreement-configs/{newConfigId}",
                MapEntityToResponse(created));
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<AgreementConfigResponse>(StatusCodes.Status201Created); // S118 / TASK-11800 — ruling #1: ALWAYS the full entity

        // ═══════════════════════════════════════════
        // 6. PUT /api/agreement-configs/{configId:guid} — Update DRAFT config
        //    Admin-strict If-Match: "<version>" required (rejects If-None-Match: *).
        //    Stale → 412 with body {expectedVersion, actualVersion, currentState}.
        //    Missing → 428 Precondition Required with hint.
        //    Sets ETag: "<new-version>" on 200.
        // ═══════════════════════════════════════════
        app.MapPut("/api/agreement-configs/{configId:guid}", async (
            Guid configId,
            AgreementConfigRequest request,
            AgreementConfigRepository agreementConfigRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<AgreementConfigUpdated> updatedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // 1. Parse If-Match (admin-strict mode — rejects If-None-Match: *).
            //    Missing or malformed → 428 Precondition Required with the helper's hint.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence + status check. Same pre-check as before — gives the
            //    caller a clean 404/409 before we open a tx, AND lets us echo `currentState`
            //    in any subsequent 412 response below.
            var existing = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            if (existing.Status != AgreementConfigStatus.DRAFT)
                return Results.Json(new { error = "Only DRAFT configs can be updated" }, statusCode: 409);

            var (valid, validationError) = ValidateRequest(request);
            if (!valid)
                return Results.BadRequest(new { error = validationError });

            if (!TryParseNormModel(request.NormModel, out var normModel))
                return Results.BadRequest(new { error = "Invalid normModel. Must be WEEKLY_HOURS or ANNUAL_ACTIVITY" });

            var updatedEntity = BuildEntityFromRequest(request, normModel, existing.CreatedBy, existing.ClonedFromId);

            // 3. Atomic state-change + audit (with version-transition pair) + outbox enqueue
            //    (ADR-018 D3). The v3 UpdateDraftAsync(conn, tx, configId, expectedVersion, ...)
            //    enforces ETag/If-Match optimistic concurrency under SELECT ... FOR UPDATE
            //    and surfaces OptimisticConcurrencyException on stale version OR concurrent
            //    state change (e.g. row published between pre-check and our FOR UPDATE).
            SaveAgreementConfigResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await agreementConfigRepo.UpdateDraftAsync(
                        conn, tx, configId, expectedVersion, updatedEntity, ct);

                    // v3 audit overload — captures (versionBefore = expectedVersion,
                    // versionAfter = saveResult.Version) into agreement_config_audit's new
                    // version_before / version_after columns (TASK-2501 schema).
                    await agreementConfigRepo.AppendAuditAsync(
                        conn, tx, configId, "UPDATED",
                        SerializeForAudit(existing), SerializeForAudit(request),
                        actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN",
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    var @event = new AgreementConfigUpdated
                    {
                        ConfigId = configId,
                        AgreementCode = request.AgreementCode,
                        OkVersion = request.OkVersion,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{configId}", @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var auditRow = updatedMapper.Map(@event, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (OptimisticConcurrencyException ex)
            {
                // ADR-019 (pending) / ADR-018 D7: 412 Precondition Failed with the actual
                // current state surfaced for the caller's retry logic.
                var currentState = await agreementConfigRepo.GetByIdAsync(configId, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapEntityToResponse(currentState),
                }, statusCode: 412);
            }

            // 4. Set ETag for the next If-Match and return the post-write snapshot.
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(MapEntityToResponse(saveResult.Config));
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<AgreementConfigResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        // ═══════════════════════════════════════════
        // 7. POST /api/agreement-configs/{configId:guid}/publish — Publish DRAFT → ACTIVE
        //    Admin-strict If-Match required. Same 412/428/ETag contract as PUT.
        //    Atomically archives prior ACTIVE for (agreement_code, ok_version) + activates
        //    DRAFT in a single tx. Concurrent state-change (S24 Step 7a P1) now manifests
        //    as 412 (OptimisticConcurrencyException) — replaces the (Guid?, bool) tuple.
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs/{configId:guid}/publish", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<AgreementConfigPublished> publishedMapper,
            IAuditProjectionMapper<AgreementConfigArchived> archivedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // 1. Parse If-Match.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence read — surfaces 404 for genuinely-missing configs and
            //    captures (agreement_code, ok_version) for outbox event payloads. Status is
            //    NOT pre-checked: a stale-If-Match request against a non-DRAFT row must
            //    surface as 412 via v3 PublishAsync's OCE (Step 7a cycle 1 P2 fix), not as
            //    409 — frontend banner-with-retry only triggers on 412.
            var existing = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            // 3. Atomic publish (ADR-018 D3): archive prior ACTIVE + activate DRAFT + audit
            //    (with version-transition pair) + outbox enqueue, all in a single tx. The v3
            //    PublishAsync(conn, tx, configId, expectedVersion, ...) enforces ETag/If-Match
            //    + status='DRAFT' under SELECT ... FOR UPDATE; concurrent change manifests as
            //    OptimisticConcurrencyException → 412 (S24 Step 7a P1 fix, restated under
            //    the v3 contract).
            //
            //    When the publish supersedes a prior ACTIVE config (saveResult.ArchivedId
            //    non-null), ADR-019 D1 mandates that we also emit a matching ARCHIVED audit
            //    row + AgreementConfigArchived outbox event for the archived config — both
            //    inside this same tx so the supersession atomicity holds end-to-end (Step 7a
            //    cycle 1 B1 fix).
            SaveAgreementConfigResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await agreementConfigRepo.PublishAsync(
                        conn, tx, configId, expectedVersion, actor.ActorId ?? "system", ct);

                    await agreementConfigRepo.AppendAuditAsync(
                        conn, tx, configId, "PUBLISHED",
                        null, $"{{\"archivedConfigId\":\"{saveResult.ArchivedId}\"}}",
                        actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN",
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    var @event = new AgreementConfigPublished
                    {
                        ConfigId = configId,
                        AgreementCode = existing.AgreementCode,
                        OkVersion = existing.OkVersion,
                        ArchivedConfigId = saveResult.ArchivedId,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write).
                    var publishOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{configId}", @event, ct);

                    var publishAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var publishAuditRow = publishedMapper.Map(@event, publishAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, publishOutboxId, @event.EventType, publishAuditRow, publishAuditCtx, ct);

                    // ADR-019 D1: when a prior ACTIVE was archived as part of this publish,
                    // emit the matching ARCHIVED audit + outbox for the archived config_id.
                    if (saveResult.ArchivedId is { } archivedId &&
                        saveResult.ArchivedVersion is { } archivedVersion)
                    {
                        // S121 / TASK-12100 (deferred defect #3): previous/new data are JSON
                        // documents (::jsonb cast in the repo) — bare strings 22P02'd and
                        // rolled back EVERY supersession publish. Hand-built single-key JSON
                        // per the file convention (CLONED/PUBLISHED sites). The supersession
                        // leg's previous status is structurally ACTIVE — the repo archives
                        // only status='ACTIVE' rows on this path.
                        await agreementConfigRepo.AppendAuditAsync(
                            conn, tx, archivedId, "ARCHIVED",
                            "{\"status\":\"ACTIVE\"}", "{\"status\":\"ARCHIVED\"}",
                            actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN",
                            versionBefore: archivedVersion - 1, versionAfter: archivedVersion, ct);

                        var archivedEvent = new AgreementConfigArchived
                        {
                            ConfigId = archivedId,
                            AgreementCode = existing.AgreementCode,
                            OkVersion = existing.OkVersion,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        // S44 TASK-4413: capture outbox_id for audit_projection insert
                        // (dual-emit — second audit row for the superseded config).
                        var archivedOutboxId = await outbox.EnqueueAndReturnIdAsync(
                            conn, tx, $"agreement-config-{archivedId}", archivedEvent, ct);

                        var archivedAuditCtx = new AuditProjectionContext(
                            ActorId: actor.ActorId,
                            ActorPrimaryOrgId: actor.OrgId,
                            CorrelationId: actor.CorrelationId,
                            OccurredAt: new DateTimeOffset(archivedEvent.OccurredAt));
                        var archivedAuditRow = archivedMapper.Map(archivedEvent, archivedAuditCtx);
                        await auditRepo.InsertAsync(conn, tx, archivedEvent.EventId, archivedOutboxId, archivedEvent.EventType, archivedAuditRow, archivedAuditCtx, ct);
                    }

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (OptimisticConcurrencyException ex)
            {
                var currentState = await agreementConfigRepo.GetByIdAsync(configId, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapEntityToResponse(currentState),
                }, statusCode: 412);
            }

            // 4. Set ETag for the next If-Match (now pointing at the activated row's
            //    post-publish version) and return the publish-result envelope.
            //    S118 / TASK-11800: the bespoke lifecycle envelope is now a named record —
            //    ALL keys always emitted, archivedConfigId/publishedAt nullable-valued
            //    (nullable-always-present, never optional-key).
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new AgreementConfigPublishResponse(
                ConfigId: configId,
                Status: "ACTIVE",
                ArchivedConfigId: saveResult.ArchivedId,
                PublishedAt: saveResult.Config.PublishedAt));
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<AgreementConfigPublishResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        // ═══════════════════════════════════════════
        // 8. POST /api/agreement-configs/{configId:guid}/archive — Archive ACTIVE/DRAFT config
        //    Admin-strict If-Match required. Same 412/428/ETag contract as PUT.
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs/{configId:guid}/archive", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<AgreementConfigArchived> archivedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // 1. Parse If-Match.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 2. Pre-flight existence read — surfaces 404 for genuinely-missing configs and
            //    captures (agreement_code, ok_version) for the outbox event payload. The audit
            //    row's previousData status comes from the FOR-UPDATE-locked row via
            //    saveResult.PreviousStatus (S121 / TASK-12100), NOT from this racy pre-flight
            //    read. Already-archived is NOT pre-checked: a stale-If-Match request against an already-ARCHIVED row
            //    must surface as 412 via v3 ArchiveAsync's OCE (Step 7a cycle 1 P2 fix), not
            //    as 409 — frontend banner-with-retry only triggers on 412.
            var existing = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            // 3. Atomic archive (ADR-018 D3) — v3 ArchiveAsync(conn, tx, configId,
            //    expectedVersion, ...) enforces ETag/If-Match optimistic concurrency. The v3
            //    path also rejects already-ARCHIVED via OCE so callers see a uniform 412
            //    contract on both stale-version and double-archive cases.
            SaveAgreementConfigResult saveResult;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    saveResult = await agreementConfigRepo.ArchiveAsync(
                        conn, tx, configId, expectedVersion, actor.ActorId ?? "system", ct);

                    // S121 / TASK-12100 (deferred defect #3): previous/new data are JSON
                    // documents (::jsonb cast in the repo) — bare strings 22P02'd and rolled
                    // back EVERY direct archive. Hand-built single-key JSON per the file
                    // convention (CLONED/PUBLISHED sites). previousData carries the TRUE
                    // pre-archive status (ACTIVE or DRAFT) from the FOR-UPDATE-locked row
                    // (saveResult.PreviousStatus — the sanctioned repo result-member
                    // extension), NOT the racy pre-flight `existing` read.
                    await agreementConfigRepo.AppendAuditAsync(
                        conn, tx, configId, "ARCHIVED",
                        $"{{\"status\":\"{saveResult.PreviousStatus}\"}}", "{\"status\":\"ARCHIVED\"}",
                        actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN",
                        versionBefore: expectedVersion, versionAfter: saveResult.Version, ct);

                    var @event = new AgreementConfigArchived
                    {
                        ConfigId = configId,
                        AgreementCode = existing.AgreementCode,
                        OkVersion = existing.OkVersion,
                        ActorId = actor.ActorId,
                        ActorRole = actor.ActorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx projection write).
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"agreement-config-{configId}", @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt));
                    var auditRow = archivedMapper.Map(@event, auditCtx);
                    await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            catch (OptimisticConcurrencyException ex)
            {
                var currentState = await agreementConfigRepo.GetByIdAsync(configId, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = currentState is null ? null : MapEntityToResponse(currentState),
                }, statusCode: 412);
            }

            // S118 / TASK-11800: the bespoke lifecycle envelope is now a named record —
            // ALL keys always emitted, archivedAt nullable-valued (nullable-always-present).
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new AgreementConfigArchiveResponse(
                ConfigId: configId,
                Status: "ARCHIVED",
                ArchivedAt: saveResult.Config.ArchivedAt));
        }).RequireAuthorization("GlobalAdminOnly")
        .Produces<AgreementConfigArchiveResponse>(StatusCodes.Status200OK); // S118 / TASK-11800

        return app;
    }

    // ── Response Mapping ──

    // S118 / TASK-11800 (PAT-012 retrofit Pass 5): the anonymous shape became the named
    // 48-member AgreementConfigResponse record — an EXACT shape-copy (same member names, same
    // order, same nullability; camelCase via JsonSerializerDefaults.Web — BYTE-IDENTICAL wire
    // JSON). Also embedded (untyped) in the 412 error-body `currentState` envelopes, which
    // stay anonymous per the S118 exclusions.
    private static AgreementConfigResponse MapEntityToResponse(AgreementConfigEntity e) => new(
        ConfigId: e.ConfigId,
        AgreementCode: e.AgreementCode,
        OkVersion: e.OkVersion,
        Status: e.Status.ToString(),
        // Row-version optimistic-concurrency token (TASK-2501 schema, ADR-019 pending).
        // Surfaced in body for list responses where multiple rows preclude a single ETag
        // header; by-id GET also sets the matching ETag header.
        Version: e.Version,
        // Norm settings
        WeeklyNormHours: e.WeeklyNormHours,
        NormPeriodWeeks: e.NormPeriodWeeks,
        NormModel: e.NormModel.ToString(),
        AnnualNormHours: e.AnnualNormHours,
        // Flex settings
        MaxFlexBalance: e.MaxFlexBalance,
        FlexCarryoverMax: e.FlexCarryoverMax,
        // Overtime settings
        HasOvertime: e.HasOvertime,
        HasMerarbejde: e.HasMerarbejde,
        OvertimeThreshold50: e.OvertimeThreshold50,
        OvertimeThreshold100: e.OvertimeThreshold100,
        // Supplement toggles
        EveningSupplementEnabled: e.EveningSupplementEnabled,
        NightSupplementEnabled: e.NightSupplementEnabled,
        WeekendSupplementEnabled: e.WeekendSupplementEnabled,
        HolidaySupplementEnabled: e.HolidaySupplementEnabled,
        // Supplement time windows
        EveningStart: e.EveningStart,
        EveningEnd: e.EveningEnd,
        NightStart: e.NightStart,
        NightEnd: e.NightEnd,
        // Supplement rates
        EveningRate: e.EveningRate,
        NightRate: e.NightRate,
        WeekendSaturdayRate: e.WeekendSaturdayRate,
        WeekendSundayRate: e.WeekendSundayRate,
        HolidayRate: e.HolidayRate,
        // On-call duty
        OnCallDutyEnabled: e.OnCallDutyEnabled,
        OnCallDutyRate: e.OnCallDutyRate,
        // Call-in work
        CallInWorkEnabled: e.CallInWorkEnabled,
        CallInMinimumHours: e.CallInMinimumHours,
        CallInRate: e.CallInRate,
        // Travel time
        TravelTimeEnabled: e.TravelTimeEnabled,
        WorkingTravelRate: e.WorkingTravelRate,
        NonWorkingTravelRate: e.NonWorkingTravelRate,
        // Working time compliance
        MaxDailyHours: e.MaxDailyHours,
        MinimumRestHours: e.MinimumRestHours,
        RestPeriodDerogationAllowed: e.RestPeriodDerogationAllowed,
        WeeklyMaxHoursReferencePeriod: e.WeeklyMaxHoursReferencePeriod,
        VoluntaryUnsocialHoursAllowed: e.VoluntaryUnsocialHoursAllowed,
        // Metadata
        CreatedBy: e.CreatedBy,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt,
        PublishedAt: e.PublishedAt,
        ArchivedAt: e.ArchivedAt,
        ClonedFromId: e.ClonedFromId,
        Description: e.Description);

    /// <summary>
    /// Extended response mapper for the GET by-ID endpoint — includes inline entitlements and
    /// the read-only flag. The parent agreement config's ETag is still the sole HTTP ETag header;
    /// each entitlement's <c>version</c> field is in the body only (for the frontend to compose
    /// child <c>If-Match</c> headers on per-entitlement mutations).
    /// </summary>
    private static AgreementConfigWithEntitlementsResponse MapEntityToResponseWithEntitlements(
        AgreementConfigEntity e,
        IReadOnlyList<EntitlementConfig> entitlements,
        bool entitlementsReadOnly) => new(
        ConfigId: e.ConfigId,
        AgreementCode: e.AgreementCode,
        OkVersion: e.OkVersion,
        Status: e.Status.ToString(),
        Version: e.Version,
        // Norm settings
        WeeklyNormHours: e.WeeklyNormHours,
        NormPeriodWeeks: e.NormPeriodWeeks,
        NormModel: e.NormModel.ToString(),
        AnnualNormHours: e.AnnualNormHours,
        // Flex settings
        MaxFlexBalance: e.MaxFlexBalance,
        FlexCarryoverMax: e.FlexCarryoverMax,
        // Overtime settings
        HasOvertime: e.HasOvertime,
        HasMerarbejde: e.HasMerarbejde,
        OvertimeThreshold50: e.OvertimeThreshold50,
        OvertimeThreshold100: e.OvertimeThreshold100,
        // Supplement toggles
        EveningSupplementEnabled: e.EveningSupplementEnabled,
        NightSupplementEnabled: e.NightSupplementEnabled,
        WeekendSupplementEnabled: e.WeekendSupplementEnabled,
        HolidaySupplementEnabled: e.HolidaySupplementEnabled,
        // Supplement time windows
        EveningStart: e.EveningStart,
        EveningEnd: e.EveningEnd,
        NightStart: e.NightStart,
        NightEnd: e.NightEnd,
        // Supplement rates
        EveningRate: e.EveningRate,
        NightRate: e.NightRate,
        WeekendSaturdayRate: e.WeekendSaturdayRate,
        WeekendSundayRate: e.WeekendSundayRate,
        HolidayRate: e.HolidayRate,
        // On-call duty
        OnCallDutyEnabled: e.OnCallDutyEnabled,
        OnCallDutyRate: e.OnCallDutyRate,
        // Call-in work
        CallInWorkEnabled: e.CallInWorkEnabled,
        CallInMinimumHours: e.CallInMinimumHours,
        CallInRate: e.CallInRate,
        // Travel time
        TravelTimeEnabled: e.TravelTimeEnabled,
        WorkingTravelRate: e.WorkingTravelRate,
        NonWorkingTravelRate: e.NonWorkingTravelRate,
        // Working time compliance
        MaxDailyHours: e.MaxDailyHours,
        MinimumRestHours: e.MinimumRestHours,
        RestPeriodDerogationAllowed: e.RestPeriodDerogationAllowed,
        WeeklyMaxHoursReferencePeriod: e.WeeklyMaxHoursReferencePeriod,
        VoluntaryUnsocialHoursAllowed: e.VoluntaryUnsocialHoursAllowed,
        // Metadata
        CreatedBy: e.CreatedBy,
        CreatedAt: e.CreatedAt,
        UpdatedAt: e.UpdatedAt,
        PublishedAt: e.PublishedAt,
        ArchivedAt: e.ArchivedAt,
        ClonedFromId: e.ClonedFromId,
        Description: e.Description,
        // Inline entitlements — open rows for this (agreement_code, ok_version) pair.
        // Each entitlement includes its own version for the frontend to compose child If-Match.
        Entitlements: entitlements.Select(MapEntitlementToResponse).ToList(),
        // True when another agreement_configs row shares this (agreement_code, ok_version),
        // meaning entitlement edits should be disabled on this config's detail page to avoid
        // ambiguity about which config "owns" the entitlements.
        EntitlementsReadOnly: entitlementsReadOnly);

    /// <summary>
    /// Maps an <see cref="EntitlementConfig"/> to the inline response shape used by the
    /// GET by-ID endpoint's <c>entitlements</c> array.
    /// S118 / TASK-11800 (owner ruling #2, the drift-repair class): this was the DRIFTED
    /// 15-member inline copy that OMITTED <c>fullDayOnly</c> — it now delegates to the ONE
    /// shared <see cref="EntitlementConfigResponse.FromModel"/> shape (the by-id embedded
    /// rows GAIN <c>fullDayOnly</c>, additive; read-side display-only this pass).
    /// </summary>
    private static EntitlementConfigResponse MapEntitlementToResponse(EntitlementConfig c) =>
        EntitlementConfigResponse.FromModel(c);

    // ── Validation ──

    private static (bool Valid, string? Error) ValidateRequest(AgreementConfigRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.AgreementCode))
            return (false, "AgreementCode is required");
        if (string.IsNullOrWhiteSpace(r.OkVersion))
            return (false, "OkVersion is required");
        if (r.WeeklyNormHours <= 0 || r.WeeklyNormHours > 50)
            return (false, "WeeklyNormHours must be > 0 and <= 50");
        if (r.NormPeriodWeeks < 1)
            return (false, "NormPeriodWeeks must be >= 1");
        if (r.MaxFlexBalance < 0)
            return (false, "MaxFlexBalance must be >= 0");
        if (r.FlexCarryoverMax < 0)
            return (false, "FlexCarryoverMax must be >= 0");
        if (r.OvertimeThreshold50 < 0)
            return (false, "OvertimeThreshold50 must be >= 0");
        if (r.OvertimeThreshold100 < r.OvertimeThreshold50)
            return (false, "OvertimeThreshold100 must be >= OvertimeThreshold50");
        if (r.EveningRate < 0 || r.NightRate < 0 || r.WeekendSaturdayRate < 0 || r.WeekendSundayRate < 0 || r.HolidayRate < 0)
            return (false, "Supplement rates must be >= 0");
        if (r.OnCallDutyRate < 0 || r.CallInRate < 0 || r.WorkingTravelRate < 0 || r.NonWorkingTravelRate < 0)
            return (false, "Rates must be >= 0");
        if (r.CallInMinimumHours < 0)
            return (false, "CallInMinimumHours must be >= 0");
        if (r.EveningStart < 0 || r.EveningStart > 23)
            return (false, "EveningStart must be 0-23");
        if (r.EveningEnd < 0 || r.EveningEnd > 23)
            return (false, "EveningEnd must be 0-23");
        if (r.NightStart < 0 || r.NightStart > 23)
            return (false, "NightStart must be 0-23");
        if (r.NightEnd < 0 || r.NightEnd > 23)
            return (false, "NightEnd must be 0-23");
        if (string.IsNullOrWhiteSpace(r.NormModel))
            return (false, "NormModel is required");

        return (true, null);
    }

    private static bool TryParseNormModel(string value, out NormModel result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return Enum.TryParse(value.Trim(), ignoreCase: true, out result);
    }

    // ── Entity Builder ──

    private static AgreementConfigEntity BuildEntityFromRequest(
        AgreementConfigRequest r, NormModel normModel, string createdBy, Guid? clonedFromId = null)
    {
        return new AgreementConfigEntity
        {
            ConfigId = Guid.Empty, // Assigned by repo
            AgreementCode = r.AgreementCode,
            OkVersion = r.OkVersion,
            Status = AgreementConfigStatus.DRAFT,
            WeeklyNormHours = r.WeeklyNormHours,
            NormPeriodWeeks = r.NormPeriodWeeks,
            NormModel = normModel,
            AnnualNormHours = r.AnnualNormHours,
            MaxFlexBalance = r.MaxFlexBalance,
            FlexCarryoverMax = r.FlexCarryoverMax,
            HasOvertime = r.HasOvertime,
            HasMerarbejde = r.HasMerarbejde,
            OvertimeThreshold50 = r.OvertimeThreshold50,
            OvertimeThreshold100 = r.OvertimeThreshold100,
            EveningSupplementEnabled = r.EveningSupplementEnabled,
            NightSupplementEnabled = r.NightSupplementEnabled,
            WeekendSupplementEnabled = r.WeekendSupplementEnabled,
            HolidaySupplementEnabled = r.HolidaySupplementEnabled,
            EveningStart = r.EveningStart,
            EveningEnd = r.EveningEnd,
            NightStart = r.NightStart,
            NightEnd = r.NightEnd,
            EveningRate = r.EveningRate,
            NightRate = r.NightRate,
            WeekendSaturdayRate = r.WeekendSaturdayRate,
            WeekendSundayRate = r.WeekendSundayRate,
            HolidayRate = r.HolidayRate,
            OnCallDutyEnabled = r.OnCallDutyEnabled,
            OnCallDutyRate = r.OnCallDutyRate,
            CallInWorkEnabled = r.CallInWorkEnabled,
            CallInMinimumHours = r.CallInMinimumHours,
            CallInRate = r.CallInRate,
            TravelTimeEnabled = r.TravelTimeEnabled,
            WorkingTravelRate = r.WorkingTravelRate,
            NonWorkingTravelRate = r.NonWorkingTravelRate,
            MaxDailyHours = r.MaxDailyHours,
            MinimumRestHours = r.MinimumRestHours,
            RestPeriodDerogationAllowed = r.RestPeriodDerogationAllowed,
            WeeklyMaxHoursReferencePeriod = r.WeeklyMaxHoursReferencePeriod,
            VoluntaryUnsocialHoursAllowed = r.VoluntaryUnsocialHoursAllowed,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ClonedFromId = clonedFromId,
            Description = r.Description,
        };
    }

    // ── Audit Serialization ──

    private static string SerializeForAudit(AgreementConfigRequest r) =>
        JsonSerializer.Serialize(new
        {
            r.AgreementCode, r.OkVersion, r.WeeklyNormHours, r.NormPeriodWeeks, r.NormModel, r.AnnualNormHours,
            r.MaxFlexBalance, r.FlexCarryoverMax, r.HasOvertime, r.HasMerarbejde,
            r.OvertimeThreshold50, r.OvertimeThreshold100,
            r.EveningSupplementEnabled, r.NightSupplementEnabled, r.WeekendSupplementEnabled, r.HolidaySupplementEnabled,
            r.EveningStart, r.EveningEnd, r.NightStart, r.NightEnd,
            r.EveningRate, r.NightRate, r.WeekendSaturdayRate, r.WeekendSundayRate, r.HolidayRate,
            r.OnCallDutyEnabled, r.OnCallDutyRate, r.CallInWorkEnabled, r.CallInMinimumHours, r.CallInRate,
            r.TravelTimeEnabled, r.WorkingTravelRate, r.NonWorkingTravelRate,
            r.MaxDailyHours, r.MinimumRestHours, r.RestPeriodDerogationAllowed,
            r.WeeklyMaxHoursReferencePeriod, r.VoluntaryUnsocialHoursAllowed, r.Description,
        });

    private static string SerializeForAudit(AgreementConfigEntity e) =>
        JsonSerializer.Serialize(new
        {
            e.AgreementCode, e.OkVersion, e.WeeklyNormHours, e.NormPeriodWeeks,
            NormModel = e.NormModel.ToString(), e.AnnualNormHours,
            e.MaxFlexBalance, e.FlexCarryoverMax, e.HasOvertime, e.HasMerarbejde,
            e.OvertimeThreshold50, e.OvertimeThreshold100,
            e.EveningSupplementEnabled, e.NightSupplementEnabled, e.WeekendSupplementEnabled, e.HolidaySupplementEnabled,
            e.EveningStart, e.EveningEnd, e.NightStart, e.NightEnd,
            e.EveningRate, e.NightRate, e.WeekendSaturdayRate, e.WeekendSundayRate, e.HolidayRate,
            e.OnCallDutyEnabled, e.OnCallDutyRate, e.CallInWorkEnabled, e.CallInMinimumHours, e.CallInRate,
            e.TravelTimeEnabled, e.WorkingTravelRate, e.NonWorkingTravelRate,
            e.MaxDailyHours, e.MinimumRestHours, e.RestPeriodDerogationAllowed,
            e.WeeklyMaxHoursReferencePeriod, e.VoluntaryUnsocialHoursAllowed, e.Description,
        });

    // ── Request DTO ──

    private sealed class AgreementConfigRequest
    {
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
        public string? Description { get; init; }
        public required string NormModel { get; init; }

        // Norm settings
        public required decimal WeeklyNormHours { get; init; }
        public required int NormPeriodWeeks { get; init; }
        public required decimal AnnualNormHours { get; init; }

        // Flex settings
        public required decimal MaxFlexBalance { get; init; }
        public required decimal FlexCarryoverMax { get; init; }

        // Overtime settings
        public required bool HasOvertime { get; init; }
        public required bool HasMerarbejde { get; init; }
        public required decimal OvertimeThreshold50 { get; init; }
        public required decimal OvertimeThreshold100 { get; init; }

        // Supplement toggles
        public required bool EveningSupplementEnabled { get; init; }
        public required bool NightSupplementEnabled { get; init; }
        public required bool WeekendSupplementEnabled { get; init; }
        public required bool HolidaySupplementEnabled { get; init; }

        // Supplement time windows (hour of day)
        public required int EveningStart { get; init; }
        public required int EveningEnd { get; init; }
        public required int NightStart { get; init; }
        public required int NightEnd { get; init; }

        // Supplement rates
        public required decimal EveningRate { get; init; }
        public required decimal NightRate { get; init; }
        public required decimal WeekendSaturdayRate { get; init; }
        public required decimal WeekendSundayRate { get; init; }
        public required decimal HolidayRate { get; init; }

        // On-call duty
        public required bool OnCallDutyEnabled { get; init; }
        public required decimal OnCallDutyRate { get; init; }

        // Call-in work
        public required bool CallInWorkEnabled { get; init; }
        public required decimal CallInMinimumHours { get; init; }
        public required decimal CallInRate { get; init; }

        // Travel time
        public required bool TravelTimeEnabled { get; init; }
        public required decimal WorkingTravelRate { get; init; }
        public required decimal NonWorkingTravelRate { get; init; }

        // Working time compliance
        public decimal MaxDailyHours { get; init; } = 13.0m;
        public decimal MinimumRestHours { get; init; } = 11.0m;
        public bool RestPeriodDerogationAllowed { get; init; }
        public int WeeklyMaxHoursReferencePeriod { get; init; } = 17;
        public bool VoluntaryUnsocialHoursAllowed { get; init; } = true;
    }
}
