using System.Data;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Validators;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// An institution's customary days (ADR-043 D9): a paid day off no agreement requires (5 June from
/// noon, 24 December) or a closure day, recorded by the institution's own administrators. The
/// routes: list, create, withdraw (set the end) and reinstate (a new row from a later date).
///
/// <para><b>Access.</b> Every route is <c>LocalAdminOrAbove</c> and checks the organisation in the
/// address through the LocalAdmin floor first (FAIL-001: a non-admin scope covering the organisation
/// never admits; docs/SECURITY.md). A row is reachable only through its own organisation's address:
/// any other answers 404. A write whose organisation is soft-deleted after that check is refused at
/// its organisation lock with the check's own 403 "Organization not found".</para>
///
/// <para><b>Each write is one transaction</b> (ADR-018 D3, ADR-026 D2), READ COMMITTED: the
/// organisation's active row locked <c>FOR NO KEY UPDATE</c> first, then (withdraw, reinstate) the custom's
/// row <c>FOR UPDATE</c>; the checks decided under those locks; the <c>customary_days</c> row, its
/// <c>customary_days_audit</c> row, the <c>CustomaryDayChanged</c> outbox event on the stream
/// <c>customary-days-{orgId}</c> and the audit-projection row; commit. Any failure keeps none of
/// them. The organisation lock serialises the organisation's customs writers, so the overlap guard
/// (one row per day and agreement scope in force at a time) holds when two administrators save at
/// once.</para>
///
/// <para><b>No pay machinery.</b> No calculation reads customary days until the stage-1 reader, so
/// these writes take no pay-inputs lock and no other advisory lock, land no row on HR's worklist
/// and are not frozen into a month's proof: no recorded month can be stale on a value nothing
/// reads. The reader, its hand-off and its lock arrive together.</para>
/// </summary>
public static class CustomaryDayEndpoints
{
    // The init.sql CHECK vocabularies of customary_days.kind, .part and .paid_mode, compared ordinally.
    private static readonly string[] Kinds = ["PAID_DAY_OFF", "CLOSURE_DAY"];
    private static readonly string[] Parts = ["WHOLE_DAY", "FROM_TIME", "HOURS"];
    private static readonly string[] PaidModes = ["GIVEN_TO_ALL", "ON_REQUEST"];

    // part_hours is NUMERIC(4,2): the column's own limit, not a domain ceiling (SEC-033, owner
    // ruling OQ-2: no invented ceiling).
    private const decimal PartHoursColumnMax = 99.99m;

    public static WebApplication MapCustomaryDayEndpoints(this WebApplication app)
    {
        // GET /api/config/{orgId}/customary-days — every row of the organisation, withdrawn ones
        // included (the editor lists a stopped custom so it can be reinstated).
        app.MapGet("/api/config/{orgId}/customary-days", async (
            string orgId,
            CustomaryDayRepository customsRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var rows = await customsRepo.GetByOrgAsync(orgId, ct);
            return Results.Ok(rows.Select(ToResponse).ToList());
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<IEnumerable<CustomaryDayResponse>>(StatusCodes.Status200OK); // a bare array

        // POST /api/config/{orgId}/customary-days — create. 201 with the row.
        app.MapPost("/api/config/{orgId}/customary-days", async (
            string orgId,
            CreateCustomaryDayRequest request,
            CustomaryDayRepository customsRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<CustomaryDayChanged> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // The value check, before any transaction: a bad value is a 400, never a CHECK's 500.
            // The agreement scope is the one rule that needs a read; it is made here, on its own
            // connection, and handed to the pure check.
            var agreementCodeKnown = request.AgreementCode is null
                || await customsRepo.AgreementCodeExistsAsync(request.AgreementCode, ct);
            var errors = ValidateCreate(request, agreementCodeKnown);
            if (errors.Count > 0)
                return ValidationFailed(errors);

            var candidate = new NewCustomaryDay(
                OrgId: orgId,
                AgreementCode: request.AgreementCode,
                RecurringMonth: request.RecurringMonth,
                RecurringDay: request.RecurringDay,
                SingleDate: request.SingleDate,
                Kind: request.Kind!,
                Part: request.Part!,
                PartFromTime: request.PartFromTime,
                PartHours: request.PartHours,
                PaidMode: request.PaidMode,
                ValidFrom: request.ValidFrom!.Value,
                CreatedBy: actor.ActorId ?? "unknown");

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                if (!await customsRepo.LockOrganisationAsync(conn, tx, orgId, ct))
                {
                    await tx.RollbackAsync(ct);
                    return OrganisationNotFound();
                }

                var conflictingCustomId = await customsRepo.FindOverlapAsync(conn, tx, candidate, ct);
                if (conflictingCustomId is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Overlap(conflictingCustomId.Value);
                }

                var created = await customsRepo.InsertAsync(conn, tx, candidate, ct);
                await RecordAsync(conn, tx, customsRepo, outbox, auditMapper, auditRepo, actor, orgId,
                    "CREATED", before: null, created, versionBefore: null, precedingCustomId: null, ct);
                await tx.CommitAsync(ct);
                return Results.Json(ToResponse(created), statusCode: StatusCodes.Status201Created);
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<CustomaryDayResponse>(StatusCodes.Status201Created);

        // POST /api/config/{orgId}/customary-days/{customId}/withdraw — set the end. 200 with the row.
        app.MapPost("/api/config/{orgId}/customary-days/{customId:guid}/withdraw", async (
            string orgId,
            Guid customId,
            WithdrawCustomaryDayRequest request,
            CustomaryDayRepository customsRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<CustomaryDayChanged> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (request.ValidTo is not { } validTo)
                return ValidationFailed([new FieldValidationError(nameof(request.ValidTo), "REQUIRED", null)]);

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                if (!await customsRepo.LockOrganisationAsync(conn, tx, orgId, ct))
                {
                    await tx.RollbackAsync(ct);
                    return OrganisationNotFound();
                }

                var current = await customsRepo.LockByIdAsync(conn, tx, orgId, customId, ct);
                if (current is null)
                {
                    await tx.RollbackAsync(ct);
                    return NotFound();
                }
                if (current.ValidTo is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Customary day is already withdrawn" }, statusCode: 409);
                }
                if (validTo <= current.ValidFrom)
                {
                    await tx.RollbackAsync(ct);
                    // No day follows DateOnly.MaxValue, so a row starting on it gets no nearestValid.
                    // Create and reinstate refuse that start (DATE_OUT_OF_RANGE), so only a row written
                    // outside these routes reaches this.
                    IReadOnlyList<DateOnly>? nearestValid = current.ValidFrom == DateOnly.MaxValue
                        ? null
                        : [current.ValidFrom.AddDays(1)];
                    return ValidationFailed([new FieldValidationError(
                        nameof(request.ValidTo), "VALID_TO_NOT_AFTER_VALID_FROM", nearestValid)]);
                }

                // The row is locked, open and the organisation's, so the update finds it.
                var withdrawn = await customsRepo.WithdrawAsync(conn, tx, orgId, customId, validTo, ct)
                    ?? throw new InvalidOperationException($"Customary day {customId} was not open under its row lock.");
                await RecordAsync(conn, tx, customsRepo, outbox, auditMapper, auditRepo, actor, orgId,
                    "WITHDRAWN", before: current, withdrawn, versionBefore: current.Version, precedingCustomId: null, ct);
                await tx.CommitAsync(ct);
                return Results.Ok(ToResponse(withdrawn));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<CustomaryDayResponse>(StatusCodes.Status200OK);

        // POST /api/config/{orgId}/customary-days/{customId}/reinstate — a NEW row from a date on or
        // after the withdrawn row's end, with its day, kind, part, part values, mode and agreement
        // scope. The withdrawn row is not changed, so the gap between stays a dated "no custom". 201
        // with the new row.
        app.MapPost("/api/config/{orgId}/customary-days/{customId:guid}/reinstate", async (
            string orgId,
            Guid customId,
            ReinstateCustomaryDayRequest request,
            CustomaryDayRepository customsRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<CustomaryDayChanged> auditMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var (allowed, reason) = await scopeValidator.ValidateOrgAccessAsync(actor, orgId, StatsTidRoles.LocalAdmin, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            if (request.ValidFrom is not { } validFrom)
                return ValidationFailed([new FieldValidationError(nameof(request.ValidFrom), "REQUIRED", null)]);
            // The create's rule (ValidateCreate): a start on the last day of the date type is unstoppable.
            if (validFrom == DateOnly.MaxValue)
                return ValidationFailed([new FieldValidationError(nameof(request.ValidFrom), "DATE_OUT_OF_RANGE", null)]);

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                if (!await customsRepo.LockOrganisationAsync(conn, tx, orgId, ct))
                {
                    await tx.RollbackAsync(ct);
                    return OrganisationNotFound();
                }

                var withdrawn = await customsRepo.LockByIdAsync(conn, tx, orgId, customId, ct);
                if (withdrawn is null)
                {
                    await tx.RollbackAsync(ct);
                    return NotFound();
                }
                if (withdrawn.ValidTo is not { } withdrawnTo)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Customary day is not withdrawn" }, statusCode: 409);
                }
                if (validFrom < withdrawnTo)
                {
                    await tx.RollbackAsync(ct);
                    return ValidationFailed([new FieldValidationError(
                        nameof(request.ValidFrom), "BEFORE_WITHDRAWAL", [withdrawnTo])]);
                }

                var candidate = new NewCustomaryDay(
                    OrgId: orgId,
                    AgreementCode: withdrawn.AgreementCode,
                    RecurringMonth: withdrawn.RecurringMonth,
                    RecurringDay: withdrawn.RecurringDay,
                    SingleDate: withdrawn.SingleDate,
                    Kind: withdrawn.Kind,
                    Part: withdrawn.Part,
                    PartFromTime: withdrawn.PartFromTime,
                    PartHours: withdrawn.PartHours,
                    PaidMode: withdrawn.PaidMode,
                    ValidFrom: validFrom,
                    CreatedBy: actor.ActorId ?? "unknown");

                // The withdrawn row itself never matches: it ends on or before validFrom.
                var conflictingCustomId = await customsRepo.FindOverlapAsync(conn, tx, candidate, ct);
                if (conflictingCustomId is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Overlap(conflictingCustomId.Value);
                }

                var reinstated = await customsRepo.InsertAsync(conn, tx, candidate, ct);
                await RecordAsync(conn, tx, customsRepo, outbox, auditMapper, auditRepo, actor, orgId,
                    "REINSTATED", before: withdrawn, reinstated, versionBefore: null, precedingCustomId: withdrawn.CustomId, ct);
                await tx.CommitAsync(ct);
                return Results.Json(ToResponse(reinstated), statusCode: StatusCodes.Status201Created);
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("LocalAdminOrAbove")
        .Produces<CustomaryDayResponse>(StatusCodes.Status201Created);

        return app;
    }

    /// <summary>
    /// The write's record, on the caller's connection and transaction, after the row is written: the
    /// <c>customary_days_audit</c> row, the <c>CustomaryDayChanged</c> outbox event carrying the row's
    /// state after the write, on the organisation's stream (ADR-018 D3), and the audit-projection row
    /// (ADR-026 D2). The mapper reads the organisation from the payload; the context names the same
    /// organisation, the route's.
    /// </summary>
    private static async Task RecordAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CustomaryDayRepository customsRepo,
        IOutboxEnqueue outbox,
        IAuditProjectionMapper<CustomaryDayChanged> auditMapper,
        AuditProjectionRepository auditRepo,
        ActorContext actor,
        string orgId,
        string action,
        CustomaryDayRow? before,
        CustomaryDayRow after,
        long? versionBefore,
        Guid? precedingCustomId,
        CancellationToken ct)
    {
        await customsRepo.InsertAuditAsync(conn, tx, action, before, after, versionBefore,
            actor.ActorId ?? "unknown", actor.ActorRole ?? "unknown", ct);

        var @event = new CustomaryDayChanged
        {
            CustomId = after.CustomId,
            OrgId = after.OrgId,
            Action = action,
            AgreementCode = after.AgreementCode,
            RecurringMonth = after.RecurringMonth,
            RecurringDay = after.RecurringDay,
            SingleDate = after.SingleDate,
            Kind = after.Kind,
            Part = after.Part,
            PartFromTime = after.PartFromTime,
            PartHours = after.PartHours,
            PaidMode = after.PaidMode,
            ValidFrom = after.ValidFrom,
            ValidTo = after.ValidTo,
            PrecedingCustomId = precedingCustomId,
            VersionBefore = versionBefore,
            VersionAfter = after.Version,
            ActorId = actor.ActorId,
            ActorRole = actor.ActorRole,
            CorrelationId = actor.CorrelationId,
        };
        var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, $"customary-days-{orgId}", @event, ct);

        var auditCtx = new AuditProjectionContext(
            ActorId: actor.ActorId,
            ActorPrimaryOrgId: actor.OrgId,
            CorrelationId: actor.CorrelationId,
            OccurredAt: new DateTimeOffset(@event.OccurredAt),
            ResolvedTargetOrgId: orgId);
        var auditRow = auditMapper.Map(@event, auditCtx);
        await auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);
    }

    /// <summary>
    /// The create's value check, pure: every field with a violation, at most one error per field
    /// (its first failure), named by the request member. The rules that depend on <c>Part</c> or
    /// <c>Kind</c> are skipped when that field is itself refused. Not refused: a past or future date
    /// (except a start on 9999-12-31, below), 29 February in a recurring custom (a real day in leap
    /// years), and <c>FROM_TIME</c> where no office hours are set (office hours live on the profile
    /// per agreement and OK version; the stage-1 reader combines the two).
    /// </summary>
    private static List<FieldValidationError> ValidateCreate(CreateCustomaryDayRequest r, bool agreementCodeKnown)
    {
        var errors = new List<FieldValidationError>();
        void Refuse(string field, string code)
        {
            if (!errors.Exists(e => string.Equals(e.Field, field, StringComparison.Ordinal)))
                errors.Add(new FieldValidationError(field, code, null));
        }
        void Presence(string field, bool given, bool needed)
        {
            if (needed && !given)
                Refuse(field, "REQUIRED");
            else if (!needed && given)
                Refuse(field, "NOT_ALLOWED");
        }
        // Null is REQUIRED, never read as a default (an omitted non-nullable DateOnly binds as 0001-01-01).
        bool Vocabulary(string field, string? value, string[] allowed, bool required)
        {
            if (value is null)
            {
                if (required)
                    Refuse(field, "REQUIRED");
                return false;
            }
            if (allowed.Contains(value, StringComparer.Ordinal))
                return true;
            Refuse(field, "UNKNOWN_VALUE");
            return false;
        }

        if (r.ValidFrom is null)
            Refuse(nameof(r.ValidFrom), "REQUIRED");
        // A start on DateOnly.MaxValue (9999-12-31), the last day the date type holds, has no later
        // end date, so the table's CHECK (valid_to > valid_from) would leave the row in force for ever.
        // The type's limit, not a domain ceiling (SEC-033, owner ruling OQ-2).
        else if (r.ValidFrom == DateOnly.MaxValue)
            Refuse(nameof(r.ValidFrom), "DATE_OUT_OF_RANGE");

        // The day: exactly one form, a single date or a month with a day.
        var monthGiven = r.RecurringMonth is not null;
        var dayGiven = r.RecurringDay is not null;
        if ((r.SingleDate is not null) == (monthGiven || dayGiven))
            Refuse(nameof(r.SingleDate), "DAY_FORM_INVALID");
        if (monthGiven != dayGiven)
            Refuse(monthGiven ? nameof(r.RecurringDay) : nameof(r.RecurringMonth), "DAY_INCOMPLETE");
        if (r.RecurringMonth is < 1 or > 12)
            Refuse(nameof(r.RecurringMonth), "NOT_A_CALENDAR_DAY");
        if (r.RecurringDay is { } day)
        {
            // Measured in a leap year, so 29 February is allowed; without a valid month, 1-31.
            var lastDay = r.RecurringMonth is >= 1 and <= 12 ? DateTime.DaysInMonth(2024, r.RecurringMonth.Value) : 31;
            if (day < 1 || day > lastDay)
                Refuse(nameof(r.RecurringDay), "NOT_A_CALENDAR_DAY");
        }

        var kindValid = Vocabulary(nameof(r.Kind), r.Kind, Kinds, required: true);
        var partValid = Vocabulary(nameof(r.Part), r.Part, Parts, required: true);
        Vocabulary(nameof(r.PaidMode), r.PaidMode, PaidModes, required: false);

        if (partValid)
        {
            Presence(nameof(r.PartFromTime), r.PartFromTime is not null, string.Equals(r.Part, "FROM_TIME", StringComparison.Ordinal));
            Presence(nameof(r.PartHours), r.PartHours is not null, string.Equals(r.Part, "HOURS", StringComparison.Ordinal));
        }
        if (kindValid)
            Presence(nameof(r.PaidMode), r.PaidMode is not null, string.Equals(r.Kind, "PAID_DAY_OFF", StringComparison.Ordinal));

        if (r.PartFromTime is { } fromTime && fromTime.Ticks % TimeSpan.TicksPerMinute != 0)
            Refuse(nameof(r.PartFromTime), "NOT_WHOLE_MINUTE");

        if (r.PartHours is { } hours)
        {
            var code = hours <= 0 ? "MUST_BE_POSITIVE"
                : decimal.Round(hours, 2) != hours ? "TOO_MANY_DECIMALS"
                : hours > PartHoursColumnMax ? "EXCEEDS_STORAGE_LIMIT"
                : null;
            if (code is not null)
                Refuse(nameof(r.PartHours), code);
        }

        // "" is not null: it names no agreement, so it is an unknown code.
        if (r.AgreementCode is not null && !agreementCodeKnown)
            Refuse(nameof(r.AgreementCode), "UNKNOWN_VALUE");

        return errors;
    }

    /// <summary>The house 400: <c>{ error, fields: [ { field, code, nearestValid } ] }</c>, dates ISO-8601.</summary>
    private static IResult ValidationFailed(IReadOnlyList<FieldValidationError> errors) => Results.BadRequest(new
    {
        error = "Customary day validation failed",
        fields = errors.Select(e => new
        {
            field = e.Field,
            code = e.Code,
            nearestValid = e.NearestValid?.Select(d => d.ToString("O")).ToArray(),
        }).ToArray(),
    });

    private static IResult Overlap(Guid conflictingCustomId) =>
        Results.Json(new { error = "Customary day overlaps an existing one", conflictingCustomId }, statusCode: 409);

    private static IResult NotFound() => Results.NotFound(new { error = "Customary day not found" });

    // The scope validator's own refusal for an unknown or soft-deleted organisation
    // (OrgScopeValidator.ValidateOrgAccessAsync), given when the soft-delete commits between that
    // check and the write's organisation lock.
    private static IResult OrganisationNotFound() =>
        Results.Json(new { error = "Access denied", reason = "Organization not found" }, statusCode: 403);

    private static CustomaryDayResponse ToResponse(CustomaryDayRow row) => new(
        CustomId: row.CustomId,
        OrgId: row.OrgId,
        AgreementCode: row.AgreementCode,
        RecurringMonth: row.RecurringMonth,
        RecurringDay: row.RecurringDay,
        SingleDate: row.SingleDate,
        Kind: row.Kind,
        Part: row.Part,
        PartFromTime: row.PartFromTime,
        PartHours: row.PartHours,
        PaidMode: row.PaidMode,
        ValidFrom: row.ValidFrom,
        ValidTo: row.ValidTo,
        Version: row.Version,
        CreatedBy: row.CreatedBy,
        CreatedAt: row.CreatedAt,
        UpdatedAt: row.UpdatedAt);

    // ── Request DTOs (co-located) ──
    // ValidFrom, ValidTo, Kind and Part are nullable on purpose: null answers REQUIRED, where an
    // omitted non-nullable DateOnly would bind as 0001-01-01 and pass as the start of history.

    /// <summary>
    /// POST /api/config/{orgId}/customary-days request body. The day is a <c>SingleDate</c>, or a
    /// <c>RecurringMonth</c> with a <c>RecurringDay</c>. <c>Kind</c>: PAID_DAY_OFF or CLOSURE_DAY;
    /// <c>Part</c>: WHOLE_DAY, FROM_TIME (with <c>PartFromTime</c>, whole minutes) or HOURS (with
    /// <c>PartHours</c>); <c>PaidMode</c> (GIVEN_TO_ALL or ON_REQUEST) exactly for a paid day off.
    /// <c>AgreementCode</c> optionally narrows the custom to one agreement. A clock time is
    /// <c>"HH:mm:ss"</c> on the wire.
    /// </summary>
    private sealed class CreateCustomaryDayRequest
    {
        public string? AgreementCode { get; init; }
        public int? RecurringMonth { get; init; }
        public int? RecurringDay { get; init; }
        public DateOnly? SingleDate { get; init; }
        public string? Kind { get; init; }
        public string? Part { get; init; }
        public TimeOnly? PartFromTime { get; init; }
        public decimal? PartHours { get; init; }
        public string? PaidMode { get; init; }
        public DateOnly? ValidFrom { get; init; }
    }

    /// <summary>POST …/customary-days/{customId}/withdraw request body: the end, end-exclusive
    /// (ADR-018 D8), after the row's <c>validFrom</c>.</summary>
    private sealed class WithdrawCustomaryDayRequest
    {
        public DateOnly? ValidTo { get; init; }
    }

    /// <summary>POST …/customary-days/{customId}/reinstate request body: the new row's start, on or
    /// after the withdrawn row's end.</summary>
    private sealed class ReinstateCustomaryDayRequest
    {
        public DateOnly? ValidFrom { get; init; }
    }
}
