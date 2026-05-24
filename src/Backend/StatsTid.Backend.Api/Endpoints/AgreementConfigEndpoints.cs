using System.Text.Json;
using StatsTid.Auth;
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
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 2. GET /api/agreement-configs/{configId:guid} — Get single config by ID
        //    Sets `ETag: "<version>"` so the frontend can use it as the next If-Match.
        // ═══════════════════════════════════════════
        app.MapGet("/api/agreement-configs/{configId:guid}", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var entity = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (entity is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            context.Response.Headers.ETag = $"\"{entity.Version}\"";
            return Results.Ok(MapEntityToResponse(entity));
        }).RequireAuthorization("GlobalAdminOnly");

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
        }).RequireAuthorization("GlobalAdminOnly");

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
            // Create still uses the v2 CreateAsync + v2 AppendAuditAsync overloads — the
            // first-create has no prior version-transition to record (version_before is
            // NULL by design). The 201 response carries ETag: "1" since the DB DEFAULT is 1.
            Guid configId;
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                configId = await agreementConfigRepo.CreateAsync(conn, tx, entity, ct);

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

            // Re-read to get DB-generated timestamps (post-commit; outside the write tx).
            var created = await agreementConfigRepo.GetByIdAsync(configId, ct);

            // ETag for the 201 response. Sourced from the DB column when re-read succeeds;
            // falls back to the static "1" (DB DEFAULT) if the re-read returned null.
            context.Response.Headers.ETag = $"\"{(created?.Version ?? 1L)}\"";
            return Results.Created($"/api/agreement-configs/{configId}",
                created is not null ? MapEntityToResponse(created) : new { configId });
        }).RequireAuthorization("GlobalAdminOnly");

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
                CreatedBy = actor.ActorId ?? "system",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ClonedFromId = source.ConfigId,
                Description = source.Description,
            };

            // Atomic state-change + audit + outbox enqueue (ADR-018 D3 atomic-outbox shape).
            Guid newConfigId;
            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);

                newConfigId = await agreementConfigRepo.CreateAsync(conn, tx, cloneEntity, ct);

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

            // Re-read to get DB-generated timestamps (post-commit; outside the write tx).
            var created = await agreementConfigRepo.GetByIdAsync(newConfigId, ct);

            // ETag for the 201 response. Same fallback shape as POST /create.
            context.Response.Headers.ETag = $"\"{(created?.Version ?? 1L)}\"";
            return Results.Created($"/api/agreement-configs/{newConfigId}",
                created is not null ? MapEntityToResponse(created) : new { configId = newConfigId });
        }).RequireAuthorization("GlobalAdminOnly");

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
        }).RequireAuthorization("GlobalAdminOnly");

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
                        await agreementConfigRepo.AppendAuditAsync(
                            conn, tx, archivedId, "ARCHIVED",
                            "ACTIVE", "ARCHIVED",
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
            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new
            {
                configId,
                status = "ACTIVE",
                archivedConfigId = saveResult.ArchivedId,
                publishedAt = saveResult.Config.PublishedAt,
            });
        }).RequireAuthorization("GlobalAdminOnly");

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
            //    captures (agreement_code, ok_version) for the outbox event payload + the
            //    pre-archive status string for the audit row's previousData. Already-archived
            //    is NOT pre-checked: a stale-If-Match request against an already-ARCHIVED row
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

                    await agreementConfigRepo.AppendAuditAsync(
                        conn, tx, configId, "ARCHIVED",
                        existing.Status.ToString(), "ARCHIVED",
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

            context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
            return Results.Ok(new
            {
                configId,
                status = "ARCHIVED",
                archivedAt = saveResult.Config.ArchivedAt,
            });
        }).RequireAuthorization("GlobalAdminOnly");

        return app;
    }

    // ── Response Mapping ──

    private static object MapEntityToResponse(AgreementConfigEntity e) => new
    {
        configId = e.ConfigId,
        agreementCode = e.AgreementCode,
        okVersion = e.OkVersion,
        status = e.Status.ToString(),
        // Row-version optimistic-concurrency token (TASK-2501 schema, ADR-019 pending).
        // Surfaced in body for list responses where multiple rows preclude a single ETag
        // header; by-id GET also sets the matching ETag header.
        version = e.Version,
        // Norm settings
        weeklyNormHours = e.WeeklyNormHours,
        normPeriodWeeks = e.NormPeriodWeeks,
        normModel = e.NormModel.ToString(),
        annualNormHours = e.AnnualNormHours,
        // Flex settings
        maxFlexBalance = e.MaxFlexBalance,
        flexCarryoverMax = e.FlexCarryoverMax,
        // Overtime settings
        hasOvertime = e.HasOvertime,
        hasMerarbejde = e.HasMerarbejde,
        overtimeThreshold50 = e.OvertimeThreshold50,
        overtimeThreshold100 = e.OvertimeThreshold100,
        // Supplement toggles
        eveningSupplementEnabled = e.EveningSupplementEnabled,
        nightSupplementEnabled = e.NightSupplementEnabled,
        weekendSupplementEnabled = e.WeekendSupplementEnabled,
        holidaySupplementEnabled = e.HolidaySupplementEnabled,
        // Supplement time windows
        eveningStart = e.EveningStart,
        eveningEnd = e.EveningEnd,
        nightStart = e.NightStart,
        nightEnd = e.NightEnd,
        // Supplement rates
        eveningRate = e.EveningRate,
        nightRate = e.NightRate,
        weekendSaturdayRate = e.WeekendSaturdayRate,
        weekendSundayRate = e.WeekendSundayRate,
        holidayRate = e.HolidayRate,
        // On-call duty
        onCallDutyEnabled = e.OnCallDutyEnabled,
        onCallDutyRate = e.OnCallDutyRate,
        // Call-in work
        callInWorkEnabled = e.CallInWorkEnabled,
        callInMinimumHours = e.CallInMinimumHours,
        callInRate = e.CallInRate,
        // Travel time
        travelTimeEnabled = e.TravelTimeEnabled,
        workingTravelRate = e.WorkingTravelRate,
        nonWorkingTravelRate = e.NonWorkingTravelRate,
        // Working time compliance
        maxDailyHours = e.MaxDailyHours,
        minimumRestHours = e.MinimumRestHours,
        restPeriodDerogationAllowed = e.RestPeriodDerogationAllowed,
        weeklyMaxHoursReferencePeriod = e.WeeklyMaxHoursReferencePeriod,
        voluntaryUnsocialHoursAllowed = e.VoluntaryUnsocialHoursAllowed,
        // Metadata
        createdBy = e.CreatedBy,
        createdAt = e.CreatedAt,
        updatedAt = e.UpdatedAt,
        publishedAt = e.PublishedAt,
        archivedAt = e.ArchivedAt,
        clonedFromId = e.ClonedFromId,
        description = e.Description,
    };

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
