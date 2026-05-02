using System.Data;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Validators;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class ConfigEndpoints
{
    public static WebApplication MapConfigEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // IMPORTANT: Map literal routes BEFORE parameterized routes
        // to prevent "/api/config/constraints" from matching {orgId}
        // ═══════════════════════════════════════════

        // 5. GET /api/config/constraints — Central constraint reference (ADR-014: DB-backed)
        app.MapGet("/api/config/constraints", async (
            AgreementConfigRepository agreementConfigRepo,
            CancellationToken ct) =>
        {
            var activeConfigs = await agreementConfigRepo.GetByStatusAsync("ACTIVE", ct);

            var constraints = activeConfigs.Select(entity => new
            {
                agreementCode = entity.AgreementCode,
                okVersion = entity.OkVersion,
                weeklyNormHours = entity.WeeklyNormHours,
                maxFlexBalance = entity.MaxFlexBalance,
                flexCarryoverMax = entity.FlexCarryoverMax,
                hasOvertime = entity.HasOvertime,
                hasMerarbejde = entity.HasMerarbejde,
                eveningSupplementEnabled = entity.EveningSupplementEnabled,
                nightSupplementEnabled = entity.NightSupplementEnabled,
                weekendSupplementEnabled = entity.WeekendSupplementEnabled,
                holidaySupplementEnabled = entity.HolidaySupplementEnabled,
                onCallDutyEnabled = entity.OnCallDutyEnabled,
                onCallDutyRate = entity.OnCallDutyRate,
            }).ToList();

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
            var mergedConfig = await configService.ResolveAsync(orgId, org.AgreementCode, org.OkVersion, ct: ct);

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

        // ═══════════════════════════════════════════
        // S21 ADR-017 D5: profile-shaped endpoints (replace per-row POST/GET-local/DELETE)
        // ═══════════════════════════════════════════

        // PUT /api/config/{orgId}/profile/{agreementCode}/{okVersion}
        // Create-or-supersede the local agreement profile for the (org, agreement, OK-version).
        // Concurrency: requires `If-Match: "<currentProfileId>"` for supersession OR
        // `If-None-Match: *` for first creation (ADR-017 D2.1). Returns 412 on stale state.
        // Validation: ProfileAlignmentValidator runs against changed fields (ADR-017 D9a) →
        // 400 with structured per-field errors on misalignment.
        // Transaction (ADR-017 D6): the profile UPDATE/INSERT and the audit-row INSERT are
        // committed in a single PostgreSQL transaction via the repo's in-transaction overload.
        // The LocalAgreementProfileChanged event is appended via IEventStore after the profile
        // transaction commits successfully (the event store owns its own transaction; same DB).
        app.MapPut("/api/config/{orgId}/profile/{agreementCode}/{okVersion}", async (
            string orgId,
            string agreementCode,
            string okVersion,
            ProfileSaveRequest request,
            DbConnectionFactory connectionFactory,
            LocalAgreementProfileRepository profileRepo,
            ProfileAlignmentValidator alignmentValidator,
            OrgScopeValidator scopeValidator,
            IEventStore eventStore,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // 1. Org-scope validation (P7).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // 2. Parse the ETag/If-Match concurrency precondition (ADR-017 D2.1).
            //    `If-Match: "<guid>"` -> supersede that specific predecessor.
            //    `If-None-Match: *`  -> assert no current profile exists (first creation).
            //    Exactly one of the two MUST be supplied.
            if (!TryParseConcurrencyPrecondition(context, out var expectedCurrentProfileId, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 3. Build the candidate profile from the request body.
            var newProfileId = Guid.NewGuid();
            var candidate = new LocalAgreementProfile
            {
                ProfileId = newProfileId,
                OrgId = orgId,
                AgreementCode = agreementCode,
                OkVersion = okVersion,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = null,
                WeeklyNormHours = request.WeeklyNormHours,
                MaxFlexBalance = request.MaxFlexBalance,
                FlexCarryoverMax = request.FlexCarryoverMax,
                MaxOvertimeHoursPerPeriod = request.MaxOvertimeHoursPerPeriod,
                OvertimeRequiresPreApproval = request.OvertimeRequiresPreApproval,
                CreatedBy = actor.ActorId ?? "system",
                CreatedAt = DateTime.UtcNow,
            };

            // 4. Compute the field delta vs. the predecessor (or against NULL-defaults on
            //    first creation). The delta drives both the alignment validator's
            //    changed-fields input and the audit/event payload (ADR-017 D6).
            LocalAgreementProfile? predecessor = null;
            if (expectedCurrentProfileId is not null)
                predecessor = await profileRepo.GetCurrentOpenAsync(orgId, agreementCode, okVersion, ct);
            var changedFields = ComputeChangedFields(predecessor, candidate);
            var changedFieldsForValidator = changedFields.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.New,
                StringComparer.Ordinal);

            // 4b. No-scheduled-future rejection (ADR-017 D2; D11 fixture #15). Profile
            //     activations are "today onwards" only — admins set calendar reminders and
            //     edit on the day rather than scheduling future profiles. UTC "today"
            //     matches the repository's effective_to stamping (Phase-4 hardening
            //     sub-sprint per D2.2 revisits TimeProvider/IClock injection).
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var temporalityError = ProfileAlignmentValidator.ValidateEffectiveFromTemporality(
                candidate.EffectiveFrom, today);
            if (temporalityError is not null)
            {
                return Results.BadRequest(new
                {
                    error = "Profile alignment validation failed",
                    fields = new[]
                    {
                        new
                        {
                            field = temporalityError.Field,
                            code = temporalityError.Code,
                            nearestValid = temporalityError.NearestValid?.Select(d => d.ToString("O")).ToArray(),
                        },
                    },
                });
            }

            // 5. Per-field alignment validation (ADR-017 D9a).
            var validation = alignmentValidator.Validate(candidate, changedFieldsForValidator);
            if (!validation.IsValid)
            {
                return Results.BadRequest(new
                {
                    error = "Profile alignment validation failed",
                    fields = validation.Errors.Select(e => new
                    {
                        field = e.Field,
                        code = e.Code,
                        nearestValid = e.NearestValid?.Select(d => d.ToString("O")).ToArray(),
                    }),
                });
            }

            // 6. Single-transaction profile + audit write (ADR-017 D6).
            //    The repo's in-transaction overload performs the lock + close + insert; we
            //    insert the audit row on the same conn+tx and commit together. On
            //    OptimisticConcurrencyException we surface 412 with the actual current state.
            Guid persistedProfileId;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
                try
                {
                    persistedProfileId = await profileRepo.SupersedeAndCreateAsync(
                        conn, tx, expectedCurrentProfileId, candidate, ct);

                    var auditAction = expectedCurrentProfileId is null ? "CREATED" : "SUPERSEDED";
                    var deltaJson = JsonSerializer.Serialize(changedFields);
                    await using var auditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO local_agreement_profile_audit
                            (profile_id, action, delta_jsonb, actor_id, actor_role)
                        VALUES (@profileId, @action, @delta::jsonb, @actorId, @actorRole)
                        """, conn, tx);
                    auditCmd.Parameters.AddWithValue("profileId", persistedProfileId);
                    auditCmd.Parameters.AddWithValue("action", auditAction);
                    auditCmd.Parameters.AddWithValue("delta", deltaJson);
                    auditCmd.Parameters.AddWithValue("actorId", actor.ActorId ?? "system");
                    auditCmd.Parameters.AddWithValue("actorRole", actor.ActorRole ?? StatsTidRoles.LocalAdmin);
                    await auditCmd.ExecuteNonQueryAsync(ct);

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
                // ADR-017 D2.1: 412 Precondition Failed with the freshly-fetched current state.
                var currentState = await profileRepo.GetCurrentOpenAsync(orgId, agreementCode, okVersion, ct);
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedProfileId = ex.ExpectedProfileId,
                    actualProfileId = ex.ActualProfileId,
                    currentState = MapProfileResponse(currentState),
                }, statusCode: 412);
            }

            // 7. Append the LocalAgreementProfileChanged event AFTER the profile transaction
            //    commits successfully. This is the post-commit "best-effort" shape, NOT the
            //    same-DB-transaction shape ADR-017 D6 originally specified.
            //
            //    Cycle-2 review found that placing the event-append inside the caller's
            //    RepeatableRead transaction caused a snapshot-stale conflict on
            //    stream_version: a second concurrent admin would read MAX(stream_version)
            //    from a snapshot taken BEFORE the first admin's event INSERT was visible,
            //    leading to duplicate-key violations on (stream_id, stream_version). The
            //    self-contained AppendAsync overload uses its own fresh transaction, so it
            //    always sees the latest committed version.
            //
            //    Residual risk (S21 known limitation, tracked in Phase-4): if the process
            //    crashes between tx.CommitAsync above and AppendAsync below, the audit and
            //    profile rows persist with no corresponding event. Phase-4 hardening will
            //    redesign via the transactional-outbox pattern (insert into outbox_events
            //    in the profile tx; separate publisher drains to event store) — same
            //    atomic guarantee without MVCC snapshot conflicts.
            var streamId = $"local-agreement-profile-{orgId}-{agreementCode}-{okVersion}";
            var @event = new LocalAgreementProfileChanged
            {
                ProfileId = persistedProfileId,
                OrgId = orgId,
                AgreementCode = agreementCode,
                OkVersion = okVersion,
                EffectiveFrom = candidate.EffectiveFrom,
                ChangedFields = changedFields,
                PrecedingProfileId = expectedCurrentProfileId,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            await eventStore.AppendAsync(streamId, @event, ct);

            // 8. Return 200 with the new profile and an ETag for the next If-Match.
            var saved = new LocalAgreementProfile
            {
                ProfileId = persistedProfileId,
                OrgId = candidate.OrgId,
                AgreementCode = candidate.AgreementCode,
                OkVersion = candidate.OkVersion,
                EffectiveFrom = candidate.EffectiveFrom,
                EffectiveTo = candidate.EffectiveTo,
                WeeklyNormHours = candidate.WeeklyNormHours,
                MaxFlexBalance = candidate.MaxFlexBalance,
                FlexCarryoverMax = candidate.FlexCarryoverMax,
                MaxOvertimeHoursPerPeriod = candidate.MaxOvertimeHoursPerPeriod,
                OvertimeRequiresPreApproval = candidate.OvertimeRequiresPreApproval,
                CreatedBy = candidate.CreatedBy,
                CreatedAt = candidate.CreatedAt,
            };
            context.Response.Headers.ETag = $"\"{persistedProfileId}\"";
            return Results.Ok(MapProfileResponse(saved));
        }).RequireAuthorization("LocalAdminOrAbove");

        // GET /api/config/{orgId}/profile/{agreementCode}/{okVersion}
        // Returns the current open profile (ADR-017 D5). 404 if none exists. Includes
        // ETag header for use as the next If-Match value.
        app.MapGet("/api/config/{orgId}/profile/{agreementCode}/{okVersion}", async (
            string orgId,
            string agreementCode,
            string okVersion,
            LocalAgreementProfileRepository profileRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var profile = await profileRepo.GetCurrentOpenAsync(orgId, agreementCode, okVersion, ct);
            if (profile is null)
                return Results.NotFound(new { error = "No active local agreement profile for this org/agreement/OkVersion." });

            context.Response.Headers.ETag = $"\"{profile.ProfileId}\"";
            return Results.Ok(MapProfileResponse(profile));
        }).RequireAuthorization("EmployeeOrAbove");

        // GET /api/config/{orgId}/profile/{agreementCode}/{okVersion}/history
        // Returns closed predecessor profiles, most-recently-closed first (ADR-017 D5).
        // No ETag — history rows are immutable.
        app.MapGet("/api/config/{orgId}/profile/{agreementCode}/{okVersion}/history", async (
            string orgId,
            string agreementCode,
            string okVersion,
            LocalAgreementProfileRepository profileRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var history = await profileRepo.GetHistoryAsync(orgId, agreementCode, okVersion, ct);
            return Results.Ok(history.Select(MapProfileResponse));
        }).RequireAuthorization("EmployeeOrAbove");

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

    /// <summary>
    /// Parses the ETag/If-Match precondition from request headers (ADR-017 D2.1). Returns
    /// the expected current profile id (Guid for supersession, null for first creation
    /// signalled by If-None-Match: *). Either header MUST be present and exactly one parses.
    /// </summary>
    private static bool TryParseConcurrencyPrecondition(
        HttpContext context, out Guid? expectedCurrentProfileId, out string? error)
    {
        expectedCurrentProfileId = null;
        error = null;

        var ifMatch = context.Request.Headers.IfMatch.ToString();
        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        var hasIfMatch = !string.IsNullOrWhiteSpace(ifMatch);
        var hasIfNoneMatch = !string.IsNullOrWhiteSpace(ifNoneMatch);

        if (!hasIfMatch && !hasIfNoneMatch)
        {
            error = "Missing If-Match: \"<profileId>\" (for supersession) or If-None-Match: * (for first creation).";
            return false;
        }
        if (hasIfMatch && hasIfNoneMatch)
        {
            error = "Send exactly one of If-Match or If-None-Match — not both.";
            return false;
        }

        if (hasIfNoneMatch)
        {
            if (!ifNoneMatch.Trim().Equals("*", StringComparison.Ordinal))
            {
                error = "If-None-Match must be exactly '*' (first-creation precondition).";
                return false;
            }
            expectedCurrentProfileId = null;
            return true;
        }

        // If-Match: "<guid>" (or bare guid). Strip surrounding quotes / whitespace.
        var raw = ifMatch.Trim().Trim('"');
        if (!Guid.TryParse(raw, out var parsed))
        {
            error = $"If-Match header is not a valid GUID: '{ifMatch}'.";
            return false;
        }
        expectedCurrentProfileId = parsed;
        return true;
    }

    /// <summary>
    /// Computes the per-field old/new pair dictionary between the predecessor profile (or null
    /// for first creation) and the candidate profile. Only the five overridable columns are
    /// inspected. Fields with identical values are excluded; identity is "both null OR equal
    /// non-null values" — value equality uses default decimal/bool comparisons. Used as the
    /// changed-fields input to <see cref="ProfileAlignmentValidator"/>, the audit-row
    /// delta_jsonb payload, and the LocalAgreementProfileChanged event payload.
    /// </summary>
    private static Dictionary<string, FieldChange> ComputeChangedFields(
        LocalAgreementProfile? predecessor, LocalAgreementProfile candidate)
    {
        var changed = new Dictionary<string, FieldChange>(StringComparer.Ordinal);

        AddIfChanged(changed, "WeeklyNormHours", predecessor?.WeeklyNormHours, candidate.WeeklyNormHours);
        AddIfChanged(changed, "MaxFlexBalance", predecessor?.MaxFlexBalance, candidate.MaxFlexBalance);
        AddIfChanged(changed, "FlexCarryoverMax", predecessor?.FlexCarryoverMax, candidate.FlexCarryoverMax);
        AddIfChanged(changed, "MaxOvertimeHoursPerPeriod", predecessor?.MaxOvertimeHoursPerPeriod, candidate.MaxOvertimeHoursPerPeriod);
        AddIfChanged(changed, "OvertimeRequiresPreApproval", predecessor?.OvertimeRequiresPreApproval, candidate.OvertimeRequiresPreApproval);

        return changed;
    }

    private static void AddIfChanged<T>(
        Dictionary<string, FieldChange> bag, string field, T? oldValue, T? newValue)
        where T : struct
    {
        if (Nullable.Equals(oldValue, newValue))
            return;
        bag[field] = new FieldChange(
            JsonSerializer.SerializeToElement(oldValue),
            JsonSerializer.SerializeToElement(newValue));
    }

    private static object? MapProfileResponse(LocalAgreementProfile? profile)
    {
        if (profile is null) return null;
        return new
        {
            profileId = profile.ProfileId,
            orgId = profile.OrgId,
            agreementCode = profile.AgreementCode,
            okVersion = profile.OkVersion,
            effectiveFrom = profile.EffectiveFrom,
            effectiveTo = profile.EffectiveTo,
            weeklyNormHours = profile.WeeklyNormHours,
            maxFlexBalance = profile.MaxFlexBalance,
            flexCarryoverMax = profile.FlexCarryoverMax,
            maxOvertimeHoursPerPeriod = profile.MaxOvertimeHoursPerPeriod,
            overtimeRequiresPreApproval = profile.OvertimeRequiresPreApproval,
            createdBy = profile.CreatedBy,
            createdAt = profile.CreatedAt,
        };
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
        ["LEAVE_WITH_PAY"] = "Tjenestefri m. løn",
        ["LEAVE_WITHOUT_PAY"] = "Tjenestefri u. løn"
    };

    // ── Request DTOs (co-located) ──

    private sealed class AbsenceTypeVisibilityRequest
    {
        public required string AbsenceType { get; init; }
        public bool IsHidden { get; init; }
    }

    /// <summary>
    /// PUT /api/config/{orgId}/profile/{agreementCode}/{okVersion} request body. The five
    /// nullable fields map directly to the overridable columns of LocalAgreementProfile —
    /// NULL means "inherit central." EffectiveFrom is required and must align per
    /// LocalAgreementProfileAlignmentPolicies for any field whose value changed.
    /// </summary>
    private sealed class ProfileSaveRequest
    {
        public required DateOnly EffectiveFrom { get; init; }
        public decimal? WeeklyNormHours { get; init; }
        public decimal? MaxFlexBalance { get; init; }
        public decimal? FlexCarryoverMax { get; init; }
        public decimal? MaxOvertimeHoursPerPeriod { get; init; }
        public bool? OvertimeRequiresPreApproval { get; init; }
    }
}
