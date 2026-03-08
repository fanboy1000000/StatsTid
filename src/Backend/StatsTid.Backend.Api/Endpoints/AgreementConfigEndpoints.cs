using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints;

public static class AgreementConfigEndpoints
{
    public static WebApplication MapAgreementConfigEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/agreement-configs — List all configs, optionally filter by status
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
        // ═══════════════════════════════════════════
        app.MapGet("/api/agreement-configs/{configId:guid}", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            CancellationToken ct) =>
        {
            var entity = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (entity is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            return Results.Ok(MapEntityToResponse(entity));
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 3. GET /api/agreement-configs/{agreementCode}/{okVersion} — Get all versions for agreement
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
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs", async (
            AgreementConfigRequest request,
            AgreementConfigRepository agreementConfigRepo,
            IEventStore eventStore,
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

            var configId = await agreementConfigRepo.CreateAsync(entity, ct);

            // Audit
            await agreementConfigRepo.AppendAuditAsync(
                configId, "CREATE", null, SerializeForAudit(request),
                actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN", ct);

            // Emit event
            var @event = new AgreementConfigCreated
            {
                ConfigId = configId,
                AgreementCode = request.AgreementCode,
                OkVersion = request.OkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            await eventStore.AppendAsync($"agreement-config-{configId}", @event, ct);

            // Re-read to get DB-generated timestamps
            var created = await agreementConfigRepo.GetByIdAsync(configId, ct);

            return Results.Created($"/api/agreement-configs/{configId}",
                created is not null ? MapEntityToResponse(created) : new { configId });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 5. POST /api/agreement-configs/{configId:guid}/clone — Clone existing config as new DRAFT
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs/{configId:guid}/clone", async (
            Guid configId,
            string? agreementCode,
            string? okVersion,
            AgreementConfigRepository agreementConfigRepo,
            IEventStore eventStore,
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
                CreatedBy = actor.ActorId ?? "system",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ClonedFromId = source.ConfigId,
                Description = source.Description,
            };

            var newConfigId = await agreementConfigRepo.CreateAsync(cloneEntity, ct);

            // Audit
            await agreementConfigRepo.AppendAuditAsync(
                newConfigId, "CLONE", null, $"{{\"sourceConfigId\":\"{configId}\"}}",
                actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN", ct);

            // Emit event
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
            await eventStore.AppendAsync($"agreement-config-{newConfigId}", @event, ct);

            // Re-read to get DB-generated timestamps
            var created = await agreementConfigRepo.GetByIdAsync(newConfigId, ct);

            return Results.Created($"/api/agreement-configs/{newConfigId}",
                created is not null ? MapEntityToResponse(created) : new { configId = newConfigId });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 6. PUT /api/agreement-configs/{configId:guid} — Update DRAFT config
        // ═══════════════════════════════════════════
        app.MapPut("/api/agreement-configs/{configId:guid}", async (
            Guid configId,
            AgreementConfigRequest request,
            AgreementConfigRepository agreementConfigRepo,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

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

            var updated = await agreementConfigRepo.UpdateDraftAsync(configId, updatedEntity, ct);
            if (!updated)
                return Results.Json(new { error = "Failed to update — config may no longer be in DRAFT status" }, statusCode: 409);

            // Audit
            await agreementConfigRepo.AppendAuditAsync(
                configId, "UPDATE", SerializeForAudit(existing), SerializeForAudit(request),
                actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN", ct);

            // Emit event
            var @event = new AgreementConfigUpdated
            {
                ConfigId = configId,
                AgreementCode = request.AgreementCode,
                OkVersion = request.OkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            await eventStore.AppendAsync($"agreement-config-{configId}", @event, ct);

            // Re-read to get updated timestamps
            var refreshed = await agreementConfigRepo.GetByIdAsync(configId, ct);

            return Results.Ok(refreshed is not null ? MapEntityToResponse(refreshed) : new { configId });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 7. POST /api/agreement-configs/{configId:guid}/publish — Publish DRAFT → ACTIVE
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs/{configId:guid}/publish", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var existing = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            if (existing.Status != AgreementConfigStatus.DRAFT)
                return Results.Json(new { error = "Only DRAFT configs can be published" }, statusCode: 409);

            var archivedId = await agreementConfigRepo.PublishAsync(configId, actor.ActorId ?? "system", ct);

            // PublishAsync returns null if publication failed (e.g. concurrent status change)
            // Re-read to verify it actually became ACTIVE
            var published = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (published is null || published.Status != AgreementConfigStatus.ACTIVE)
                return Results.Json(new { error = "Failed to publish — config may no longer be in DRAFT status" }, statusCode: 409);

            // Audit
            await agreementConfigRepo.AppendAuditAsync(
                configId, "PUBLISH", null, $"{{\"archivedConfigId\":\"{archivedId}\"}}",
                actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN", ct);

            // Emit event
            var @event = new AgreementConfigPublished
            {
                ConfigId = configId,
                AgreementCode = existing.AgreementCode,
                OkVersion = existing.OkVersion,
                ArchivedConfigId = archivedId,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            await eventStore.AppendAsync($"agreement-config-{configId}", @event, ct);

            return Results.Ok(new
            {
                configId,
                status = "ACTIVE",
                archivedConfigId = archivedId,
                publishedAt = published.PublishedAt,
            });
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 8. POST /api/agreement-configs/{configId:guid}/archive — Archive ACTIVE/DRAFT config
        // ═══════════════════════════════════════════
        app.MapPost("/api/agreement-configs/{configId:guid}/archive", async (
            Guid configId,
            AgreementConfigRepository agreementConfigRepo,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var existing = await agreementConfigRepo.GetByIdAsync(configId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Agreement config not found" });

            if (existing.Status == AgreementConfigStatus.ARCHIVED)
                return Results.Json(new { error = "Config is already archived" }, statusCode: 409);

            var archived = await agreementConfigRepo.ArchiveAsync(configId, actor.ActorId ?? "system", ct);
            if (!archived)
                return Results.Json(new { error = "Failed to archive config" }, statusCode: 409);

            // Audit
            await agreementConfigRepo.AppendAuditAsync(
                configId, "ARCHIVE", existing.Status.ToString(), "ARCHIVED",
                actor.ActorId ?? "system", actor.ActorRole ?? "GLOBAL_ADMIN", ct);

            // Emit event
            var @event = new AgreementConfigArchived
            {
                ConfigId = configId,
                AgreementCode = existing.AgreementCode,
                OkVersion = existing.OkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            await eventStore.AppendAsync($"agreement-config-{configId}", @event, ct);

            // Re-read to get updated timestamps
            var refreshed = await agreementConfigRepo.GetByIdAsync(configId, ct);

            return Results.Ok(new
            {
                configId,
                status = "ARCHIVED",
                archivedAt = refreshed?.ArchivedAt,
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
            r.TravelTimeEnabled, r.WorkingTravelRate, r.NonWorkingTravelRate, r.Description,
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
            e.TravelTimeEnabled, e.WorkingTravelRate, e.NonWorkingTravelRate, e.Description,
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
    }
}
