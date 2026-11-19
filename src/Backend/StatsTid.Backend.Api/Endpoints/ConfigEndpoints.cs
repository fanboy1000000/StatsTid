using System.Data;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Validators;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
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

            var constraints = activeConfigs.Select(entity => new ConfigConstraintResponse(
                AgreementCode: entity.AgreementCode,
                OkVersion: entity.OkVersion,
                WeeklyNormHours: entity.WeeklyNormHours,
                MaxFlexBalance: entity.MaxFlexBalance,
                HasOvertime: entity.HasOvertime,
                HasMerarbejde: entity.HasMerarbejde,
                EveningSupplementEnabled: entity.EveningSupplementEnabled,
                NightSupplementEnabled: entity.NightSupplementEnabled,
                WeekendSupplementEnabled: entity.WeekendSupplementEnabled,
                HolidaySupplementEnabled: entity.HolidaySupplementEnabled,
                OnCallDutyEnabled: entity.OnCallDutyEnabled,
                OnCallDutyRate: entity.OnCallDutyRate)).ToList();

            return Results.Ok(constraints);
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<IEnumerable<ConfigConstraintResponse>>(StatusCodes.Status200OK); // S119 / TASK-11900 — BARE array

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

            return Results.Ok(new EffectiveConfigResponse(
                OrgId: orgId,
                AgreementCode: mergedConfig.AgreementCode,
                OkVersion: mergedConfig.OkVersion,
                WeeklyNormHours: mergedConfig.WeeklyNormHours,
                MaxFlexBalance: mergedConfig.MaxFlexBalance,
                HasOvertime: mergedConfig.HasOvertime,
                HasMerarbejde: mergedConfig.HasMerarbejde,
                EveningSupplementEnabled: mergedConfig.EveningSupplementEnabled,
                NightSupplementEnabled: mergedConfig.NightSupplementEnabled,
                WeekendSupplementEnabled: mergedConfig.WeekendSupplementEnabled,
                HolidaySupplementEnabled: mergedConfig.HolidaySupplementEnabled,
                OnCallDutyEnabled: mergedConfig.OnCallDutyEnabled,
                OnCallDutyRate: mergedConfig.OnCallDutyRate));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<EffectiveConfigResponse>(StatusCodes.Status200OK); // S119 / TASK-11900

        // ═══════════════════════════════════════════
        // S21 ADR-017 D5: profile-shaped endpoints (replace per-row POST/GET-local/DELETE)
        // ═══════════════════════════════════════════

        // PUT /api/config/{orgId}/profile/{agreementCode}/{okVersion}
        // Create-or-supersede the local agreement profile for the (org, agreement, OK-version).
        // Concurrency: requires `If-Match: "<version>"` for supersession or in-place edit OR
        // `If-None-Match: *` for first creation (ADR-018 D7, RFC 7232 quoted). Returns 412 on
        // stale state.
        // Validation: ProfileAlignmentValidator runs against changed fields (ADR-017 D9a) →
        // 400 with structured per-field errors on misalignment.
        // Transaction (ADR-018 D3): one PostgreSQL transaction at READ COMMITTED whose first
        // statement is the pay-inputs lock (S148 PIL); the profile UPDATE/INSERT via the repo's
        // in-transaction overload, the audit-row INSERT, the outbox enqueue and the worklist
        // hand-off (S148 T19: the exported months and settled holiday years the new profile
        // reaches, for the organisation's employees on the agreement code) commit together. A
        // separate per-service OutboxPublisher drains outbox_events to the canonical event store
        // at-least-once (ADR-018 D4).
        app.MapPut("/api/config/{orgId}/profile/{agreementCode}/{okVersion}", async (
            string orgId,
            string agreementCode,
            string okVersion,
            ProfileSaveRequest request,
            DbConnectionFactory connectionFactory,
            LocalAgreementProfileRepository profileRepo,
            ProfileAlignmentValidator alignmentValidator,
            OrgScopeValidator scopeValidator,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<LocalAgreementProfileChanged> auditMapper,
            AuditProjectionRepository auditRepo,
            HrBackdateWorklistRepository worklistRepo,
            ILoggerFactory loggerFactory,
            // S142 / TASK-14201 — the server-"today" seam. DI supplies TimeProvider.System in
            // production; a fixed provider in tests. Injected so the Copenhagen business date
            // in step 4b is pinnable (PAT-008); it was previously read off the ambient wall
            // clock, which no test could pin.
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.ConfigEndpoints.ProfilePut");

            // 1. Org-scope validation (P7). S76 / TASK-7600 B1: this is a LocalAdminOrAbove
            //    writer — route through the LocalAdmin floor so a non-admin scope (e.g. a
            //    Leader scope covering this org via a mixed-role JWT) can NEVER satisfy the
            //    admin gate (FAIL-001 mixed-role scope leak).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // 2. Parse the ETag/If-Match concurrency precondition (ADR-018 D7).
            //    `If-Match: "<version>"` -> supersede / update-in-place at that version.
            //    `If-None-Match: *`      -> assert no current profile exists (first creation).
            //    Exactly one of the two MUST be supplied. Profile-flexible mode — admin
            //    endpoints (S25 Phase 4c Part 2) use the strict overload that rejects
            //    If-None-Match: *.
            if (!EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar(
                    context.Request, out var expectedCurrentVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // 3. Build the candidate profile from the request body. Version is `required init`
            //    on the model (ADR-018 D7); the value here is a placeholder — the repository
            //    assigns the authoritative version (1 for first-create / supersede-as-new;
            //    predecessor.Version + 1 for UPDATE-in-place).
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
                MaxOvertimeHoursPerPeriod = request.MaxOvertimeHoursPerPeriod,
                OvertimeRequiresPreApproval = request.OvertimeRequiresPreApproval,
                CreatedBy = actor.ActorId ?? "system",
                CreatedAt = DateTime.UtcNow,
                Version = 1,
            };

            // 4. Compute the field delta vs. the predecessor (or against NULL-defaults on
            //    first creation). The delta drives the alignment validator's changed-fields
            //    input, the audit/event payload, AND the three-way audit-action routing
            //    (ADR-018 D9 — see step 6 below).
            LocalAgreementProfile? predecessor = null;
            if (expectedCurrentVersion is not null)
                predecessor = await profileRepo.GetCurrentOpenAsync(orgId, agreementCode, okVersion, ct);
            var changedFields = ComputeChangedFields(predecessor, candidate);
            var changedFieldsForValidator = changedFields.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.New,
                StringComparer.Ordinal);

            // 4b. No-scheduled-future rejection (ADR-017 D2; D11 fixture #15). Profile
            //     activations are "today onwards" only — admins set calendar reminders and
            //     edit on the day rather than scheduling future profiles.
            //
            //     S142 / TASK-14201 (census row 14): "today" is the EUROPE/COPENHAGEN calendar
            //     day, read off the INJECTED TimeProvider (TimeProvider.System in production).
            //     This is a strict-inequality FUTURE rejection — past dates stay legal — so
            //     the UTC day did not merely shift the boundary, it told a Danish admin
            //     working after local midnight that the day on their own calendar was "in the
            //     future". The value is also echoed back as `nearestValid`, so a UTC day put a
            //     wrong remediation date in the 400 body. (The stale comment this replaces
            //     claimed UTC "today" matched the repository's effective_to stamping; that
            //     claim was the S142 defect, not a justification.) MondayDatePicker.tsx is the
            //     browser half of the same gate and moves to Copenhagen in the same commit.
            var today = CopenhagenBusinessDate.Today(timeProvider);
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

            // 6. Single-transaction profile + audit + outbox + worklist write (ADR-018 D3).
            //    Lock order (Block C): the pay-inputs lock (0) → the open profile row FOR UPDATE
            //    → the worklist rows. The repo's in-transaction overload performs the row lock +
            //    (close+insert | update-in-place | no-op short-circuit) + (route
            //    same-day-vs-supersession). The route then checks that the row the repo locked is
            //    the one step 4 read (6a), inserts the audit row, enqueues the
            //    LocalAgreementProfileChanged outbox event and lands the worklist hand-off on the
            //    same conn+tx and commits together — UNLESS the repo reports IsNoOp = true (S23 /
            //    TASK-2304 same-day no field changes), in which case the audit, the outbox and the
            //    hand-off are skipped (Codex Step 0b cycle-3 P2 fix). On
            //    OptimisticConcurrencyException we surface 412 with the actual current state; on
            //    InvalidProfileSupersessionException we surface 400 (backdate-before-predecessor
            //    per ADR-018 D9).
            Guid persistedProfileId;
            long persistedVersion;
            bool persistedIsNoOp;
            try
            {
                await using var conn = connectionFactory.Create();
                await conn.OpenAsync(ct);
                // READ COMMITTED, stated explicitly (PAT-015; refinement :602): the first statement
                // waits for the pay-inputs lock, and every read after it, the row lock and the
                // worklist read, must see what committed during the wait; under REPEATABLE READ the
                // snapshot would predate it. Two writers of one profile are still serialized by the
                // row lock and the partial unique index, and every writer by the exclusive
                // pay-inputs lock.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                try
                {
                    // (0) S148 PIL — the pay-inputs lock exclusive, the FIRST statement, taken even
                    // when the save turns out to be a no-op (decided only under the row lock); nothing
                    // is held before it on this or any other connection (the scope check and the
                    // step-4 read above have finished on their own connections). Lock order: pay inputs
                    // → the open profile row FOR UPDATE → the worklist rows.
                    await PayInputsLock.AcquireExclusiveAsync(conn, tx, ct);

                    var saveResult = await profileRepo.SupersedeAndCreateAsync(
                        conn, tx, expectedCurrentVersion, candidate, ct);
                    persistedProfileId = saveResult.ProfileId;
                    persistedVersion = saveResult.Version;
                    persistedIsNoOp = saveResult.IsNoOp;

                    // 6a. The stale predecessor (S148 ruling A9, Case A; auditability). The audit
                    //     action, the field delta, the alignment validation and PrecedingProfileId all
                    //     come from step 4's read, made before this transaction. If another save
                    //     replaced that row while this one waited for the lock, the If-Match check can
                    //     still pass (a supersession restarts the version at 1), and the audit row and
                    //     event would describe replacing a row that is no longer there. So the row the
                    //     repo locked must be the row step 4 read: same profile id and version, or both
                    //     absent. Otherwise 412, the route's existing answer; the catch below rolls the
                    //     transaction back, so nothing is written.
                    if (saveResult.LockedPredecessorProfileId != predecessor?.ProfileId
                        || saveResult.LockedPredecessorVersion != predecessor?.Version)
                    {
                        throw new OptimisticConcurrencyException(
                            "The current profile changed after this request read it; refresh and retry.",
                            expectedVersion: expectedCurrentVersion,
                            actualVersion: saveResult.LockedPredecessorVersion);
                    }

                    if (!persistedIsNoOp)
                    {
                        // Three-way audit-action routing (ADR-018 D9):
                        //   CREATED    — no predecessor (first-create path).
                        //   MODIFIED   — predecessor exists with same effective_from (UPDATE-in-place
                        //                same-day edit, at least one field changed).
                        //   SUPERSEDED — predecessor exists with earlier effective_from
                        //                (close-then-insert).
                        var auditAction = predecessor is null
                            ? "CREATED"
                            : (predecessor.EffectiveFrom == candidate.EffectiveFrom ? "MODIFIED" : "SUPERSEDED");
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

                        // Enqueue the LocalAgreementProfileChanged outbox event INSIDE the
                        // profile transaction (ADR-018 D3 — replaces the S21 post-commit
                        // AppendAsync shape). The publisher drains outbox_events to the
                        // canonical event store under its own ReadCommitted transaction with
                        // FOR UPDATE serialization on event_streams (ADR-018 D4), eliminating
                        // the S21 cycle-2 RepeatableRead snapshot conflict that drove the
                        // post-commit shape originally. PrecedingProfileId is the predecessor's
                        // profile_id when there was one (for the SUPERSEDED audit path) — it is
                        // intentionally null on first-create AND on UPDATE-in-place (same-day
                        // edit retains the same profile_id).
                        var streamId = $"local-agreement-profile-{orgId}-{agreementCode}-{okVersion}";
                        var @event = new LocalAgreementProfileChanged
                        {
                            ProfileId = persistedProfileId,
                            OrgId = orgId,
                            AgreementCode = agreementCode,
                            OkVersion = okVersion,
                            EffectiveFrom = candidate.EffectiveFrom,
                            ChangedFields = changedFields,
                            PrecedingProfileId = predecessor is not null && auditAction == "SUPERSEDED"
                                ? predecessor.ProfileId
                                : null,
                            ActorId = actor.ActorId,
                            ActorRole = actor.ActorRole,
                            CorrelationId = actor.CorrelationId,
                        };
                        // S44 TASK-4413c: capture outbox_id for audit_projection insert
                        // (ADR-026 D2 sync-in-tx projection write — atomic with the
                        // profile row + outbox row per ADR-018 D3/D13).
                        var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

                        var auditCtx = new AuditProjectionContext(
                            ActorId: actor.ActorId,
                            ActorPrimaryOrgId: actor.OrgId,
                            CorrelationId: actor.CorrelationId,
                            OccurredAt: new DateTimeOffset(@event.OccurredAt),
                            ResolvedTargetOrgId: @event.OrgId);
                        var auditRow = auditMapper.Map(@event, auditCtx);
                        await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                        // The hand-off (S148 T19, Q-2 = (a)), last before the commit: the exported
                        // months and settled holiday years the new profile reaches, for every employee
                        // on the agreement code in this organisation on such a day (the dated
                        // organisation), listed on HR's worklist under CONFIG_PUBLISHED, pointing at
                        // this event and dated the profile's effective date. The LocalAdmin who saves
                        // is not who recalculates; the worklist is how that person learns. Flag only.
                        await worklistRepo.WriteForConfigurationChangeAsync(
                            conn, tx,
                            new ConfigurationChange(agreementCode, okVersion, candidate.EffectiveFrom, OrgId: orgId),
                            @event.EventId, actor.ActorId ?? "unknown", ct);
                    }

                    // Always commit — on no-op the tx held only the two locks, no writes.
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
                // ADR-018 D7: 412 Precondition Failed with the freshly-fetched current state
                //             and the version mismatch surfaced for the caller's retry logic.
                //
                // S23 / TASK-2305 NOTE-1: the recovery fetch runs AFTER the primary write tx
                // already raised OptimisticConcurrencyException. If GetCurrentOpenAsync also
                // throws (e.g. transient DB outage), surfacing that as an unhandled exception
                // would mask the original concurrency error and produce a 500 the caller
                // can't act on. Wrap the recovery in a narrow catch (Postgres/Npgsql only —
                // OperationCanceledException and programming bugs propagate as before per
                // Codex Step 0b NOTE) and degrade currentState to null with a warn-log.
                LocalAgreementProfile? currentState;
                try
                {
                    currentState = await profileRepo.GetCurrentOpenAsync(orgId, agreementCode, okVersion, ct);
                }
                catch (PostgresException recoveryEx)
                {
                    logger.LogWarning(recoveryEx,
                        "412 recovery GetCurrentOpenAsync failed for {OrgId}/{AgreementCode}/{OkVersion}; degrading currentState to null",
                        orgId, agreementCode, okVersion);
                    currentState = null;
                }
                catch (NpgsqlException recoveryEx)
                {
                    logger.LogWarning(recoveryEx,
                        "412 recovery GetCurrentOpenAsync failed for {OrgId}/{AgreementCode}/{OkVersion}; degrading currentState to null",
                        orgId, agreementCode, okVersion);
                    currentState = null;
                }
                return Results.Json(new
                {
                    error = "Concurrency precondition failed",
                    expectedVersion = ex.ExpectedVersion,
                    actualVersion = ex.ActualVersion,
                    currentState = MapProfileResponse(currentState),
                }, statusCode: 412);
            }
            catch (InvalidProfileSupersessionException ex)
            {
                // ADR-018 D9 backdate guard: the new profile cannot start before the
                // predecessor's effective_from (strict-less under end-exclusive). 400 carries
                // the structured error so the UI can surface a precise message.
                return Results.BadRequest(new
                {
                    error = "Invalid profile supersession",
                    message = ex.Message,
                });
            }

            // 7. Return 200 with the new profile and an ETag for the next If-Match. The ETag
            //    wire format is `"<version>"` (RFC 7232 quoted, ADR-018 D7) — was profile_id
            //    in S21 (ADR-017 D2.1).
            //
            //    ADR-018 D9 MODIFIED branch: the repository's UpdateInPlaceAsync preserves
            //    the predecessor's created_by / created_at across same-day edits (only the
            //    4 overridable columns + version are mutated). Echo those preserved values
            //    here so the PUT response matches what a subsequent GET returns. INSERT
            //    paths (first-create + close-then-insert supersession) take candidate.* as
            //    the row's values — InsertProfileAsync binds them directly.
            var isInPlaceEdit = predecessor is not null
                && predecessor.EffectiveFrom == candidate.EffectiveFrom;
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
                MaxOvertimeHoursPerPeriod = candidate.MaxOvertimeHoursPerPeriod,
                OvertimeRequiresPreApproval = candidate.OvertimeRequiresPreApproval,
                CreatedBy = isInPlaceEdit ? predecessor!.CreatedBy : candidate.CreatedBy,
                CreatedAt = isInPlaceEdit ? predecessor!.CreatedAt : candidate.CreatedAt,
                Version = persistedVersion,
            };
            context.Response.Headers.ETag = $"\"{persistedVersion}\"";
            return Results.Ok(MapProfile(saved));
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<LocalAgreementProfileResponse>(StatusCodes.Status200OK); // S119 / TASK-11900 — success only; the 400/412/428 error bodies stay UNDECLARED (the exclusion boundary)

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

            context.Response.Headers.ETag = $"\"{profile.Version}\"";
            return Results.Ok(MapProfile(profile));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<LocalAgreementProfileResponse>(StatusCodes.Status200OK); // S119 / TASK-11900

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
            return Results.Ok(history.Select(MapProfile));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<IEnumerable<LocalAgreementProfileResponse>>(StatusCodes.Status200OK); // S119 / TASK-11900 — BARE array (unmaterialized Select)

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

            // Filter absence types by visibility.
            // S147 / TASK-14701 (QUAL-235) — the list is the ONE shared label map (the private copy
            // that lived here is gone), so every labelled code, særlige feriedage
            // (SPECIAL_HOLIDAY_ALLOWANCE) included, is listed for the org to hide or show. Served
            // in the map's display order.
            var absenceTypes = StatsTid.Backend.Api.Services.AbsenceTypeLabels.InDisplayOrder
                .Where(kv => !hiddenTypes.Contains(kv.Key))
                .Select(kv => new AbsenceTypeResponse(Type: kv.Key, Label: kv.Value))
                .ToList();

            return Results.Ok(absenceTypes);
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<IEnumerable<AbsenceTypeResponse>>(StatusCodes.Status200OK); // S119 / TASK-11900 — BARE array

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

            // S76 / TASK-7600 B1: LocalAdminOrAbove writer → LocalAdmin floor (mixed-role
            // scope-leak close; a covering non-admin scope cannot satisfy this admin gate).
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            await visibilityRepo.SetVisibilityAsync(
                orgId, request.AbsenceType, request.IsHidden, actor.ActorId ?? "system", ct);

            return Results.Ok(new AbsenceTypeVisibilityResponse(
                OrgId: orgId, AbsenceType: request.AbsenceType, IsHidden: request.IsHidden));
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<AbsenceTypeVisibilityResponse>(StatusCodes.Status200OK); // S119 / TASK-11900

        return app;
    }

    /// <summary>
    /// Computes the per-field old/new pair dictionary between the predecessor profile (or null
    /// for first creation) and the candidate profile. Only the four overridable columns are
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

    /// <summary>
    /// S119 / TASK-11900: the typed profile mapper — used at the THREE success sites (PUT 200,
    /// GET 200, history rows). The record is an exact shape-copy of the pre-S119 anonymous
    /// object (member names, order, casing, nullability — byte-identical wire JSON).
    /// ADR-018 D7: <c>version</c> is surfaced on the body so the frontend can round-trip it
    /// as the next If-Match; the wire ETag header carries the same value quoted (RFC 7232).
    /// </summary>
    private static LocalAgreementProfileResponse MapProfile(LocalAgreementProfile profile) =>
        new(
            ProfileId: profile.ProfileId,
            OrgId: profile.OrgId,
            AgreementCode: profile.AgreementCode,
            OkVersion: profile.OkVersion,
            EffectiveFrom: profile.EffectiveFrom,
            EffectiveTo: profile.EffectiveTo,
            WeeklyNormHours: profile.WeeklyNormHours,
            MaxFlexBalance: profile.MaxFlexBalance,
            MaxOvertimeHoursPerPeriod: profile.MaxOvertimeHoursPerPeriod,
            OvertimeRequiresPreApproval: profile.OvertimeRequiresPreApproval,
            CreatedBy: profile.CreatedBy,
            CreatedAt: profile.CreatedAt,
            Version: profile.Version);

    /// <summary>
    /// S119 / TASK-11900 — the 412 ERROR-body-only mapper (the exclusion boundary, P4 hard
    /// criterion): the 412 <c>currentState</c> envelope stays anonymous/UNDECLARED in the spec
    /// (no .Produces for any non-2xx). The record's serialization is embedded erased to
    /// <c>object?</c> — wire-identical to the pre-S119 anonymous projection (same members,
    /// order, casing), with null preserved for the degraded-recovery path.
    /// </summary>
    private static object? MapProfileResponse(LocalAgreementProfile? profile) =>
        profile is null ? null : MapProfile(profile);

    // ── Request DTOs (co-located) ──

    private sealed class AbsenceTypeVisibilityRequest
    {
        public required string AbsenceType { get; init; }
        public bool IsHidden { get; init; }
    }

    /// <summary>
    /// PUT /api/config/{orgId}/profile/{agreementCode}/{okVersion} request body. The four
    /// nullable fields map directly to the overridable columns of LocalAgreementProfile —
    /// NULL means "inherit central." EffectiveFrom is required and must align per
    /// LocalAgreementProfileAlignmentPolicies for any field whose value changed.
    /// </summary>
    private sealed class ProfileSaveRequest
    {
        public required DateOnly EffectiveFrom { get; init; }
        public decimal? WeeklyNormHours { get; init; }
        public decimal? MaxFlexBalance { get; init; }
        public decimal? MaxOvertimeHoursPerPeriod { get; init; }
        public bool? OvertimeRequiresPreApproval { get; init; }
    }
}
