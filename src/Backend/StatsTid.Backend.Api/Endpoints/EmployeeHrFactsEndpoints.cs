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
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// The two facts HR records about a person (ADR-043 D4; HRP-025, HRP-026): the overtime regime (who
/// earns overtime, a merarbejde claim or nothing) and whether the person is on the flex scheme. Read
/// as of a day, and written as a dated row.
///
/// <para><b>Access.</b> Both routes are <c>HROrAbove</c> and check the person through the org-scope
/// validator's employee check at the LocalHR floor (FAIL-001: a scope below HR covering the person's
/// organisation never admits; docs/SECURITY.md). That check also refuses a departed person acting on
/// their own record and treats a person past the leaver grace as gone. A person may read their own
/// facts; nobody writes them: the regime decides the person's own pay, so the write refuses a self
/// actor with 403, every role, GlobalAdmin included, and a second administrator makes the change
/// (RES-003, through the shared predicate of <see cref="ApprovalSelfGuard"/>).</para>
///
/// <para><b>Each write is one transaction</b> (ADR-018 D3, ADR-026 D2): the person's <c>users</c> row
/// locked, its organisation compared with the one the scope check authorised (SEC-062; 409 when a
/// transfer committed in between), the person's facts rows locked, then the row, its
/// <c>employee_hr_facts_audit</c> row, the <c>EmployeeHrFactsChanged</c> outbox event on the stream
/// <c>employee-hr-facts-{employeeId}</c> and the audit-projection row; commit. Any failure keeps none of
/// them. Lock order: the users row, then the facts rows, nothing else.</para>
///
/// <para><b>No pay machinery.</b> No calculation reads either fact yet, so these writes take no
/// pay-inputs lock and no other advisory lock, land no row on HR's worklist, are not frozen into a
/// month's proof and are not a payroll boundary (the facts live in their own table so that an edit is
/// not an employee-profile change). The first reader brings all of that with it (R-21).</para>
/// </summary>
public static class EmployeeHrFactsEndpoints
{
    private const string PutRoute = "PUT /api/admin/employees/{employeeId}/hr-facts";

    /// <summary>The one date-free reason the write answers a self request with (ADR-040 D7).</summary>
    private const string SelfHrFactsReason =
        "Own HR facts cannot be modified; a second administrator must perform this change";

    public static WebApplication MapEmployeeHrFactsEndpoints(this WebApplication app)
    {
        // GET /api/admin/employees/{employeeId}/hr-facts?asOf=YYYY-MM-DD — the facts as of a day
        // (default the Copenhagen today) and, as an ETag, the open row's version when one exists.
        app.MapGet("/api/admin/employees/{employeeId}/hr-facts", async (
            string employeeId,
            DateOnly? asOf,
            EmployeeHrFactsRepository factsRepo,
            EmployeeHrFactsResolver resolver,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var day = asOf ?? CopenhagenBusinessDate.Today(timeProvider);

            // The open row first, then the day's facts: a write landing between the two reads leaves the
            // ETag stale, which the next PUT answers with 412, never a fresh ETag over stale values.
            var live = await factsRepo.GetLiveAsync(employeeId, ct);
            var facts = await resolver.ResolveAsync(employeeId, day, ct);

            if (live is not null)
                context.Response.Headers.ETag = $"\"{live.Version}\"";
            return Results.Ok(new EmployeeHrFactsResponse(
                EmployeeId: employeeId,
                AsOf: day,
                AgreementCode: facts.AgreementCode,
                AllowedOvertimeRegimes: facts.AllowedOvertimeRegimes,
                OvertimeRegime: facts.OvertimeRegime,
                OnFlexScheme: facts.OnFlexScheme,
                EffectiveFrom: facts.EffectiveFrom,
                LiveVersion: live?.Version,
                LiveEffectiveFrom: live?.EffectiveFrom));
        }).RequireAuthorization("HROrAbove")
        .Produces<EmployeeHrFactsResponse>(StatusCodes.Status200OK);

        // PUT /api/admin/employees/{employeeId}/hr-facts — record the two facts from a date.
        //   If-None-Match: * creates the first row; If-Match: "<version>" changes the open row.
        //   A date equal to the open row's start updates it in place (UPDATED); a later date ends it and
        //   opens a new row (SUPERSEDED); values equal to the open row's write nothing.
        //   403 scope or self · 428 precondition · 400 date or value · 404 person gone under the lock ·
        //   409 organisation drift (SEC-062) or If-None-Match: * on an existing row · 412 stale If-Match.
        app.MapPut("/api/admin/employees/{employeeId}/hr-facts", async (
            string employeeId,
            SaveHrFactsRequest request,
            EmployeeHrFactsRepository factsRepo,
            UserAgreementCodeRepository agreementCodeRepo,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EmployeeHrFactsChanged> auditMapper,
            AuditProjectionRepository auditRepo,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // (1) Scope, at the LocalHR floor. The sibling also returns the organisation it authorised,
            // which the drift guard compares with the locked users row (SEC-062). A departed self is
            // refused here with the validator's own reason, before the self refusal.
            var (allowed, reason, authorisedOrgId) = await scopeValidator.ValidateEmployeeAccessWithOrgAsync(
                actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // (2) Nobody records their own HR facts (RES-003): the regime decides the person's own pay.
            // Unconditional, every role, GlobalAdmin included; before every other check and the
            // transaction, so nothing further is read, locked or written.
            if (ApprovalSelfGuard.IsSelf(actor, employeeId))
            {
                loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.EmployeeHrFactsEndpoints.SelfChange").LogWarning(
                    "Access denied ({Clause}): Actor={ActorId} Role={ActorRole} Route={Route} CorrelationId={CorrelationId}",
                    AdminEndpoints.SelfChangeClauseName, actor.ActorId, actor.ActorRole, PutRoute, actor.CorrelationId);
                return Results.Json(new { error = "Access denied", reason = SelfHrFactsReason }, statusCode: 403);
            }

            // (3) The precondition: If-None-Match: * (no open row) or If-Match: "<version>"; 428 otherwise.
            if (!EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // (4) The date: omitted means today, read once (PAT-028); a later date is refused.
            // shortcut(pending): an HR-facts write is dated today or earlier and never before the current row, so a known future change cannot be pre-recorded and history before the current row cannot be corrected; upgrade with the first reader
            var today = CopenhagenBusinessDate.Today(timeProvider);
            var effectiveFrom = request.EffectiveFrom ?? today;
            var errors = new List<FieldValidationError>();
            var dateError = ProfileAlignmentValidator.ValidateEffectiveFromTemporality(effectiveFrom, today);
            if (dateError is not null)
                errors.Add(dateError);

            // (5) The regime: null, or one of the five values allowed by the agreement in force on the
            // save's date. The agreement is read only when the date stands, and a value is compared with
            // the five before any of it reaches SQL. The flex answer has no rule.
            if (request.OvertimeRegime is { } regime)
            {
                var code = dateError is null
                    ? EmployeeHrFactsRules.OvertimeRegimeRefusal(
                        regime, await agreementCodeRepo.GetByUserIdAtAsync(employeeId, effectiveFrom, ct))
                    : EmployeeHrFactsRules.IsOvertimeRegime(regime) ? null : "UNKNOWN_VALUE";
                if (code is not null)
                    errors.Add(new FieldValidationError(nameof(request.OvertimeRegime), code, null));
            }
            if (errors.Count > 0)
                return ValidationFailed(errors);

            var regimeToSave = request.OvertimeRegime;
            var flexToSave = request.OnFlexScheme;

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // The users row locked first: it must still be in the organisation the scope check
                // authorised (a null authorisation fails closed), or a transfer committed in between.
                var lockedEmployee = await userRepo.GetByIdWithVersionAsync(conn, tx, employeeId, ct);
                if (lockedEmployee is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }
                var employeeOrgId = lockedEmployee.Value.User.PrimaryOrgId;
                if (authorisedOrgId is null || !string.Equals(employeeOrgId, authorisedOrgId, StringComparison.Ordinal))
                    return await OrganisationDriftGuard.RefuseAsync(tx, loggerFactory, actor, PutRoute, ct);

                var live = await factsRepo.LockReturningLiveAsync(conn, tx, employeeId, ct);

                string action;
                EmployeeHrFactsRow written;
                Guid? precedingFactsId = null;
                if (live is null)
                {
                    if (expectedVersion is not null)
                    {
                        await tx.RollbackAsync(ct);
                        return ConcurrencyFailed(expectedVersion, actualVersion: null);
                    }
                    written = await factsRepo.InsertAsync(conn, tx, employeeId, regimeToSave, flexToSave, effectiveFrom, version: 1, ct);
                    action = "CREATED";
                }
                else
                {
                    if (expectedVersion is null)
                    {
                        // If-None-Match: * is create-only: never a blind overwrite of a recorded value.
                        await tx.RollbackAsync(ct);
                        return Results.Json(new { error = "HR facts already exist", currentVersion = live.Version }, statusCode: 409);
                    }
                    if (expectedVersion.Value != live.Version)
                    {
                        await tx.RollbackAsync(ct);
                        return ConcurrencyFailed(expectedVersion, live.Version);
                    }
                    // The second half of the date limit (the shortcut at step 4).
                    if (effectiveFrom < live.EffectiveFrom)
                    {
                        await tx.RollbackAsync(ct);
                        return ValidationFailed([new FieldValidationError(
                            nameof(request.EffectiveFrom), "BEFORE_CURRENT_ROW", [live.EffectiveFrom])]);
                    }
                    // Values equal to the open row's write nothing, whatever the date: the date defaults
                    // to today, so an idle save would otherwise add a row.
                    if (string.Equals(regimeToSave, live.OvertimeRegime, StringComparison.Ordinal)
                        && flexToSave == live.OnFlexScheme)
                    {
                        await tx.RollbackAsync(ct);
                        context.Response.Headers.ETag = $"\"{live.Version}\"";
                        return Results.Ok(ToResponse(live));
                    }

                    if (effectiveFrom == live.EffectiveFrom)
                    {
                        written = await factsRepo.UpdateInPlaceAsync(conn, tx, live.FactsId, regimeToSave, flexToSave, ct);
                        action = "UPDATED";
                    }
                    else
                    {
                        await factsRepo.CloseAsync(conn, tx, live.FactsId, effectiveFrom, ct);
                        written = await factsRepo.InsertAsync(
                            conn, tx, employeeId, regimeToSave, flexToSave, effectiveFrom, version: live.Version + 1, ct);
                        action = "SUPERSEDED";
                        precedingFactsId = live.FactsId;
                    }
                }

                await factsRepo.InsertAuditAsync(conn, tx, action, before: live, written,
                    actor.ActorId ?? "unknown", actor.ActorRole ?? "unknown", ct);

                var @event = new EmployeeHrFactsChanged
                {
                    EmployeeId = employeeId,
                    FactsId = written.FactsId,
                    Action = action,
                    OvertimeRegime = written.OvertimeRegime,
                    OnFlexScheme = written.OnFlexScheme,
                    EffectiveFrom = written.EffectiveFrom,
                    EffectiveTo = written.EffectiveTo,
                    PrecedingFactsId = precedingFactsId,
                    VersionBefore = live?.Version,
                    VersionAfter = written.Version,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-hr-facts-{employeeId}", @event, ct);

                // The projection targets the person's organisation as the users lock found it.
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(@event.OccurredAt),
                    ResolvedTargetOrgId: employeeOrgId);
                var auditRow = auditMapper.Map(@event, auditCtx);
                await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{written.Version}\"";
                return Results.Ok(ToResponse(written));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove")
        .Produces<EmployeeHrFactsUpdatedResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>The house 400: <c>{ error, fields: [ { field, code, nearestValid } ] }</c>, dates
    /// ISO-8601, at most one error per field.</summary>
    private static IResult ValidationFailed(IReadOnlyList<FieldValidationError> errors) => Results.BadRequest(new
    {
        error = "HR facts validation failed",
        fields = errors.Select(e => new
        {
            field = e.Field,
            code = e.Code,
            nearestValid = e.NearestValid?.Select(d => d.ToString("O")).ToArray(),
        }).ToArray(),
    });

    private static IResult ConcurrencyFailed(long? expectedVersion, long? actualVersion) => Results.Json(new
    {
        error = "Concurrency precondition failed",
        expectedVersion,
        actualVersion,
    }, statusCode: 412);

    private static EmployeeHrFactsUpdatedResponse ToResponse(EmployeeHrFactsRow row) => new(
        EmployeeId: row.EmployeeId,
        FactsId: row.FactsId,
        EffectiveFrom: row.EffectiveFrom,
        OvertimeRegime: row.OvertimeRegime,
        OnFlexScheme: row.OnFlexScheme,
        Version: row.Version);

    /// <summary>
    /// PUT /api/admin/employees/{employeeId}/hr-facts request body. <c>EffectiveFrom</c>: the first
    /// day the values apply, today or earlier and not before the open row's start; omitted means today.
    /// <c>OvertimeRegime</c>: null (not recorded) or one of OVERTIME, EXEMPT, CONTRACTUAL,
    /// DISCRETIONARY, NONE that the person's agreement allows on that date. <c>OnFlexScheme</c>: true,
    /// false or null (not recorded). Both values are saved as sent.
    /// </summary>
    private sealed class SaveHrFactsRequest
    {
        public DateOnly? EffectiveFrom { get; init; }
        public string? OvertimeRegime { get; init; }
        public bool? OnFlexScheme { get; init; }
    }
}
