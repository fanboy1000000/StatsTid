using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class ConfigEndpoints
{
    // ── Valid config areas ──
    private static readonly HashSet<string> ValidConfigAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        "WORKING_TIME", "FLEX_RULES", "ORG_STRUCTURE", "LOCAL_AGREEMENT", "OPERATIONAL"
    };

    // ── Known agreement/version pairs for constraint endpoint ──
    private static readonly (string AgreementCode, string OkVersion)[] KnownAgreementVersionPairs =
    {
        ("AC", "OK24"), ("HK", "OK24"), ("PROSA", "OK24"),
        ("AC", "OK26"), ("HK", "OK26"), ("PROSA", "OK26"),
    };

    public static WebApplication MapConfigEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // IMPORTANT: Map literal routes BEFORE parameterized routes
        // to prevent "/api/config/constraints" from matching {orgId}
        // ═══════════════════════════════════════════

        // 5. GET /api/config/constraints — Central constraint reference
        app.MapGet("/api/config/constraints", () =>
        {
            var constraints = new List<object>();

            foreach (var (agreementCode, okVersion) in KnownAgreementVersionPairs)
            {
                var central = ConfigResolutionService.GetCentralConfig(agreementCode, okVersion);
                if (central is null) continue;

                constraints.Add(new
                {
                    agreementCode = central.AgreementCode,
                    okVersion = central.OkVersion,
                    weeklyNormHours = central.WeeklyNormHours,
                    maxFlexBalance = central.MaxFlexBalance,
                    flexCarryoverMax = central.FlexCarryoverMax,
                    hasOvertime = central.HasOvertime,
                    hasMerarbejde = central.HasMerarbejde,
                    eveningSupplementEnabled = central.EveningSupplementEnabled,
                    nightSupplementEnabled = central.NightSupplementEnabled,
                    weekendSupplementEnabled = central.WeekendSupplementEnabled,
                    holidaySupplementEnabled = central.HolidaySupplementEnabled,
                    onCallDutyEnabled = central.OnCallDutyEnabled,
                    onCallDutyRate = central.OnCallDutyRate,
                });
            }

            return Results.Ok(constraints);
        }).RequireAuthorization("EmployeeOrAbove");

        // ═══════════════════════════════════════════
        // Parameterized org-scoped config endpoints
        // ═══════════════════════════════════════════

        // 1. GET /api/config/{orgId} — Get effective (merged) config for org
        app.MapGet("/api/config/{orgId}", async (
            string orgId,
            ConfigResolutionService configService,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate actor scope covers org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Read the org to get agreementCode and okVersion
            var org = await orgRepo.GetByIdAsync(orgId, ct);
            if (org is null)
                return Results.NotFound(new { error = $"Organization '{orgId}' not found" });

            // Resolve merged config (central + local overrides)
            var mergedConfig = await configService.ResolveAsync(orgId, org.AgreementCode, org.OkVersion, ct);

            return Results.Ok(new
            {
                orgId,
                agreementCode = mergedConfig.AgreementCode,
                okVersion = mergedConfig.OkVersion,
                weeklyNormHours = mergedConfig.WeeklyNormHours,
                maxFlexBalance = mergedConfig.MaxFlexBalance,
                flexCarryoverMax = mergedConfig.FlexCarryoverMax,
                hasOvertime = mergedConfig.HasOvertime,
                hasMerarbejde = mergedConfig.HasMerarbejde,
                eveningSupplementEnabled = mergedConfig.EveningSupplementEnabled,
                nightSupplementEnabled = mergedConfig.NightSupplementEnabled,
                weekendSupplementEnabled = mergedConfig.WeekendSupplementEnabled,
                holidaySupplementEnabled = mergedConfig.HolidaySupplementEnabled,
                onCallDutyEnabled = mergedConfig.OnCallDutyEnabled,
                onCallDutyRate = mergedConfig.OnCallDutyRate,
            });
        }).RequireAuthorization("EmployeeOrAbove");

        // 2. GET /api/config/{orgId}/local — Get local overrides only (raw DB values)
        app.MapGet("/api/config/{orgId}/local", async (
            string orgId,
            LocalConfigurationRepository localConfigRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate actor scope covers org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var localConfigs = await localConfigRepo.GetByOrgAsync(orgId, ct);

            var response = localConfigs.Select(c => new
            {
                configId = c.ConfigId,
                configArea = c.ConfigArea,
                configKey = c.ConfigKey,
                configValue = c.ConfigValue,
                effectiveFrom = c.EffectiveFrom,
                effectiveTo = c.EffectiveTo,
                version = c.Version,
                isActive = c.IsActive,
                agreementCode = c.AgreementCode,
                okVersion = c.OkVersion,
            });

            return Results.Ok(response);
        }).RequireAuthorization("EmployeeOrAbove");

        // 3. POST /api/config/{orgId} — Create/update local config override
        app.MapPost("/api/config/{orgId}", async (
            string orgId,
            CreateLocalConfigRequest request,
            ConfigResolutionService configService,
            LocalConfigurationRepository localConfigRepo,
            OrganizationRepository orgRepo,
            OrgScopeValidator scopeValidator,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate actor scope covers org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Validate configArea
            if (!ValidConfigAreas.Contains(request.ConfigArea))
                return Results.BadRequest(new { error = $"Invalid configArea. Must be one of: {string.Join(", ", ValidConfigAreas)}" });

            // Resolve the central config for validation
            var centralConfig = ConfigResolutionService.GetCentralConfig(request.AgreementCode, request.OkVersion);
            if (centralConfig is null)
                return Results.BadRequest(new { error = $"No central configuration found for {request.AgreementCode}/{request.OkVersion}" });

            // Validate the override against central constraints
            var (valid, validationError) = configService.ValidateLocalOverride(request.ConfigKey, request.ConfigValue, centralConfig);
            if (!valid)
                return Results.BadRequest(new { error = validationError });

            // Create the local configuration
            var config = new LocalConfiguration
            {
                ConfigId = Guid.NewGuid(),
                OrgId = orgId,
                ConfigArea = request.ConfigArea.ToUpperInvariant(),
                ConfigKey = request.ConfigKey,
                ConfigValue = request.ConfigValue,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo,
                Version = 1,
                AgreementCode = request.AgreementCode,
                OkVersion = request.OkVersion,
                CreatedBy = actor.ActorId ?? "system",
                IsActive = true,
            };

            var configId = await localConfigRepo.CreateAsync(config, ct);

            // Write audit record
            await localConfigRepo.AppendAuditAsync(
                configId, "CREATED", null, request.ConfigValue,
                actor.ActorId ?? "system", actor.ActorRole ?? StatsTidRoles.LocalAdmin, ct);

            // Emit LocalConfigurationChanged event
            var streamId = $"config-{orgId}-{request.ConfigArea.ToUpperInvariant()}";
            var @event = new LocalConfigurationChanged
            {
                ConfigId = configId,
                OrgId = orgId,
                ConfigArea = request.ConfigArea.ToUpperInvariant(),
                ConfigKey = request.ConfigKey,
                ConfigValue = request.ConfigValue,
                PreviousValue = null,
                AgreementCode = request.AgreementCode,
                OkVersion = request.OkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            return Results.Created($"/api/config/{orgId}/local", new { configId });
        }).RequireAuthorization("LocalAdminOrAbove");

        // 4. DELETE /api/config/{orgId}/{configId} — Deactivate local config
        app.MapDelete("/api/config/{orgId}/{configId}", async (
            string orgId,
            Guid configId,
            LocalConfigurationRepository localConfigRepo,
            OrgScopeValidator scopeValidator,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Validate actor scope covers org
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Validate config exists and belongs to the org
            var existingConfig = await localConfigRepo.GetByIdAsync(configId, ct);
            if (existingConfig is null)
                return Results.NotFound(new { error = "Local configuration not found" });

            if (!string.Equals(existingConfig.OrgId, orgId, StringComparison.Ordinal))
                return Results.Json(new { error = "Access denied", reason = "Configuration does not belong to this organization" }, statusCode: 403);

            if (!existingConfig.IsActive)
                return Results.BadRequest(new { error = "Configuration is already deactivated" });

            // Deactivate
            await localConfigRepo.DeactivateAsync(configId, ct);

            // Write audit record
            await localConfigRepo.AppendAuditAsync(
                configId, "DEACTIVATED", existingConfig.ConfigValue, null,
                actor.ActorId ?? "system", actor.ActorRole ?? StatsTidRoles.LocalAdmin, ct);

            // Emit LocalConfigurationChanged event
            var streamId = $"config-{orgId}-{existingConfig.ConfigArea}";
            var @event = new LocalConfigurationChanged
            {
                ConfigId = configId,
                OrgId = orgId,
                ConfigArea = existingConfig.ConfigArea,
                ConfigKey = existingConfig.ConfigKey,
                ConfigValue = "",
                PreviousValue = existingConfig.ConfigValue,
                AgreementCode = existingConfig.AgreementCode,
                OkVersion = existingConfig.OkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            return Results.Ok(new { configId, deactivated = true });
        }).RequireAuthorization("LocalAdminOrAbove");

        // ═══════════════════════════════════════════
        // Sprint 9: Absence type configuration endpoints
        // ═══════════════════════════════════════════

        // GET /api/config/{orgId}/absence-types — Get available absence types for agreement
        app.MapGet("/api/config/{orgId}/absence-types", async (
            string orgId,
            string agreementCode,
            string okVersion,
            OrganizationRepository orgRepo,
            AbsenceTypeVisibilityRepository visibilityRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Get visibility overrides for this org
            var visibilityEntries = await visibilityRepo.GetByOrgAsync(orgId, ct);
            var hiddenTypes = new HashSet<string>(
                visibilityEntries.Where(v => v.IsHidden).Select(v => v.AbsenceType),
                StringComparer.Ordinal);

            // Filter absence types by visibility
            var absenceTypes = AbsenceTypeLabels
                .Where(kv => !hiddenTypes.Contains(kv.Key))
                .Select(kv => new { type = kv.Key, label = kv.Value })
                .ToList();

            return Results.Ok(absenceTypes);
        }).RequireAuthorization("EmployeeOrAbove");

        // POST /api/config/{orgId}/absence-types/visibility — Toggle absence type visibility
        app.MapPost("/api/config/{orgId}/absence-types/visibility", async (
            string orgId,
            AbsenceTypeVisibilityRequest request,
            AbsenceTypeVisibilityRepository visibilityRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await visibilityRepo.SetVisibilityAsync(
                orgId, request.AbsenceType, request.IsHidden, actor.ActorId ?? "system", ct);

            return Results.Ok(new { orgId, absenceType = request.AbsenceType, isHidden = request.IsHidden });
        }).RequireAuthorization("LocalAdminOrAbove");

        return app;
    }

    // ── Danish absence type labels ──
    private static readonly Dictionary<string, string> AbsenceTypeLabels = new(StringComparer.Ordinal)
    {
        ["SICK_DAY"] = "Sygedag",
        ["VACATION"] = "Ferie",
        ["CARE_DAY"] = "Omsorgsdag",
        ["CHILD_SICK_DAY"] = "Barns 1. sygedag",
        ["CHILD_SICK_DAY_2"] = "Barns 2. sygedag",
        ["CHILD_SICK_DAY_3"] = "Barns 3. sygedag",
        ["PARENTAL_LEAVE"] = "Barsel",
        ["SENIOR_DAY"] = "Seniordag",
        ["LEAVE_WITH_PAY"] = "Tjenestefri m. l\u00f8n",
        ["LEAVE_WITHOUT_PAY"] = "Tjenestefri u. l\u00f8n"
    };

    // ── Request DTOs (co-located) ──

    private sealed class AbsenceTypeVisibilityRequest
    {
        public required string AbsenceType { get; init; }
        public bool IsHidden { get; init; }
    }

    private sealed class CreateLocalConfigRequest
    {
        public required string ConfigArea { get; init; }
        public required string ConfigKey { get; init; }
        public required string ConfigValue { get; init; }
        public required DateOnly EffectiveFrom { get; init; }
        public DateOnly? EffectiveTo { get; init; }
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
    }
}
