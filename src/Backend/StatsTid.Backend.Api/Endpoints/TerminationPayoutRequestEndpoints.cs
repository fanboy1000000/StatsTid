using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S71 / TASK-7102 (ADR-033 slice 3b; SPRINT-71 R6/R12 + owner D-B) — the §26 stk.1
/// <i>efter anmodning</i> payout-request surface:
///
/// <para>
/// <b>POST /api/admin/employees/{employeeId}/termination-payout-request</b> — HR records the
/// leaver's §26 payout REQUEST against the EXACT settled TERMINATION settlement row. The
/// request — not the settlement — is what drives the staged <c>SLS_TBD_S26</c> line (the
/// TASK-7105 consumer); this endpoint therefore emits <see cref="TerminationPayoutRequested"/>
/// ONLY when a line should stage (every guard below holds).
/// </para>
///
/// <para>
/// <b>One atomic tx under the R12 employee advisory lock (lock FIRST, every guard re-evaluated
/// in-lock — the S70 B1 lesson):</b> HROrAbove + the terminated-INCLUSIVE
/// <see cref="OrgScopeValidator"/> (the target is normally a deactivated leaver) → admin-strict
/// If-Match (the SETTLEMENT row's version, ADR-019) → lock → in-lock guards → request row
/// (state OPEN, the 7104 repository) + <see cref="TerminationPayoutRequested"/> (outbox) + the
/// ADR-026 audit-projection row → commit.
/// </para>
///
/// <para><b>In-lock guard chain (declared order; wrong-row guards are 422):</b></para>
/// <list type="number">
///   <item><description>employee exists (terminated-inclusive read) — else 404;</description></item>
///   <item><description>an ACTIVE (non-REVERSED) settlement row exists for
///   (employee, type, year) — else 404;</description></item>
///   <item><description>R2/B1 GENERATION binding: the active row's sequence equals the body's
///   <c>expectedSettlementSequence</c> — else 422 with the actual sequence (DECLARED: 422, the
///   wrong-row bucket per the task contract; the reversal endpoint's analogous mismatch is 409
///   per the 7104 contract delta — different aggregate semantics, both carry the actual
///   sequence). Checked BEFORE the version comparison (versions restart per
///   generation);</description></item>
///   <item><description><c>trigger = TERMINATION</c> — else 422 (a YEAR_END row holds no §26
///   claim);</description></item>
///   <item><description><c>settlement_state = SETTLED</c> — else 422 (a PENDING_REVIEW
///   TERMINATION row holds an unresolved §7-shaped claim, not a payable
///   crystallization);</description></item>
///   <item><description>snapshot <c>CrystallizedDays &gt; 0</c> AND a non-default snapshot
///   <c>SettlementBoundaryDate</c> (the R11 lønart-resolution anchor — fail-closed: a line with
///   no resolvable dated key must never stage; DECLARED guard) — else 422;</description></item>
///   <item><description>the employee's CURRENT (in-lock) end date is non-null and has PASSED on
///   the Copenhagen business date (end date = the LAST employed day ⇒ passed means today is
///   STRICTLY after it; re-read live per the B1 lesson, never trusted from the snapshot) — else
///   422;</description></item>
///   <item><description>If-Match equals the active row's version — else 412;</description></item>
///   <item><description>no live (non-voided) request exists for the settlement row — else 409
///   (in-lock pre-check; the partial-unique index + the repository's typed exception are the
///   constraint backstop).</description></item>
/// </list>
///
/// <para>
/// <b>Quantities are COPIED, never recomputed (ADR-033 D3):</b> the event's
/// <c>CrystallizedDays</c>/<c>SettlementBoundaryDate</c> come verbatim from the ACTIVE row's
/// immutable snapshot. NO statutory-deadline validation is applied to <c>requestDate</c> —
/// slice Step-0 gate (ii) found the law-research trail silent on §26 deadline semantics, so 3b
/// ships structural guards only (the recorded follow-up owns deadline semantics).
/// </para>
///
/// <para>
/// <b>Declared shape decisions:</b> <c>entitlementType</c> is optional in the body and defaults
/// to VACATION (the only type the 3b TERMINATION close produces); <c>requestDate</c> is
/// REQUIRED (the §26 anmodning evidence date); there is NO self-target exclusion on this
/// endpoint (R4's exclusion is end-date-mutation-specific; R6/D-B pin none here). The 201
/// response carries the request row + the snapshot-copied quantities and an ETag of the request
/// row's version.
/// </para>
/// </summary>
public static class TerminationPayoutRequestEndpoints
{
    private const string VacationType = "VACATION";
    private const string TerminationTrigger = "TERMINATION";
    private const string StateSettled = "SETTLED";

    public static WebApplication MapTerminationPayoutRequestEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/employees/{employeeId}/termination-payout-request", async (
            string employeeId,
            CreateTerminationPayoutRequestBody body,
            UserRepository userRepo,
            VacationSettlementRepository settlementRepo,
            TerminationPayoutRequestRepository requestRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<TerminationPayoutRequested> requestAuditMapper,
            AuditProjectionRepository auditRepo,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Terminated-INCLUSIVE validator (SPRINT-70 R9c allowlist family) — the §26 target
            // is normally a DEACTIVATED leaver; HROrAbove + subtree binding unchanged (FAIL-001:
            // cross-org binding is load-bearing, the policy alone does not bind the org).
            // S76 B1 fix-forward (cycle 2): LocalHR per-scope floor — a mixed HR@A + Leader@B JWT
            // can no longer raise a §26 request against an ACTIVE B employee via the Leader scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Body shape (422 before any DB work).
            if (body.EntitlementYear is null)
                return Results.UnprocessableEntity(new { error = "entitlementYear is required." });
            if (body.ExpectedSettlementSequence is null)
                return Results.UnprocessableEntity(new
                {
                    error = "expectedSettlementSequence is required (SPRINT-71 R2 — the request binds the " +
                            "EXACT settlement row, never the bare year-tuple).",
                });
            if (body.RequestDate is null)
                return Results.UnprocessableEntity(new
                {
                    error = "requestDate is required (the §26 stk.1 anmodning evidence date).",
                });
            var entitlementType = string.IsNullOrWhiteSpace(body.EntitlementType)
                ? VacationType
                : body.EntitlementType.Trim();
            var entitlementYear = body.EntitlementYear.Value;
            var expectedSequence = body.ExpectedSettlementSequence.Value;

            // Admin-strict If-Match — the SETTLEMENT row's ADR-019 version token; 428 if
            // missing / malformed / If-None-Match.
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // (1) R12 — the ADR-032 D4 employee advisory lock FIRST, before ANY read (the
                // SAME key the close service / reversal service / resolve / end-date PUT take),
                // held to commit. Serializes request-vs-reversal: a reversal committing while
                // this handler parks VOIDs/reverses state this handler then re-reads in-lock.
                await EmployeeConsumptionLock.AcquireAsync(conn, tx, employeeId, ct);

                // (2) In-lock employee re-read (terminated-inclusive — a deactivated leaver is
                // the NORMAL case): the end-date-passed guard input + the audit target org.
                var user = await userRepo.GetByIdIncludingTerminatedAsync(conn, tx, employeeId, ct);
                if (user is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }

                // (3) In-lock ACTIVE settlement row read — the request's subject.
                var current = await settlementRepo.GetActiveAsync(
                    conn, tx, employeeId, entitlementType, entitlementYear, ct);
                if (current is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new
                    {
                        error = "No active settlement found for this (employee, type, year).",
                    });
                }

                // (4) R2/B1 generation binding — BEFORE any version comparison (settlement-row
                // versions restart at 1 per generation; the sequence is what discriminates a
                // superseded generation from its successor).
                if (current.Sequence != expectedSequence)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "The active settlement row for this tuple is not the commanded sequence — " +
                                "the row this request was built against has been superseded; refresh and " +
                                "re-record against the current row (SPRINT-71 R2).",
                        expectedSettlementSequence = expectedSequence,
                        actualSettlementSequence = current.Sequence,
                    });
                }

                // (5) Wrong-row guards (422) — §26 pays the SETTLED TERMINATION crystallization.
                if (!string.Equals(current.Trigger, TerminationTrigger, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "A §26 payout request applies only to TERMINATION settlements.",
                        trigger = current.Trigger,
                        settlementState = current.SettlementState,
                    });
                }
                if (!string.Equals(current.SettlementState, StateSettled, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "The TERMINATION settlement is not SETTLED — an unresolved row holds a " +
                                "§7-shaped claim (resolve it via WAIVED), not a payable §26 crystallization.",
                        settlementState = current.SettlementState,
                    });
                }

                // (6) Snapshot quantities — COPIED, never recomputed (ADR-033 D3). Fail-closed:
                // a missing/zero CrystallizedDays or a default SettlementBoundaryDate (the R11
                // dated-lønart anchor) must never stage a line.
                var snapshot = DeserializeSnapshot(current.SnapshotJson);
                if (snapshot?.CrystallizedDays is not { } crystallizedDays || crystallizedDays <= 0m)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "The settlement snapshot carries no positive CrystallizedDays — there is " +
                                "no §26 quantity to request (zero-crystallized terminations stage no line).",
                        crystallizedDays = snapshot?.CrystallizedDays,
                    });
                }
                if (snapshot.SettlementBoundaryDate == default)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "The settlement snapshot carries no SettlementBoundaryDate — the §26 line's " +
                                "dated lønart resolution (SPRINT-71 R11 asOf anchor) would be unresolvable; " +
                                "fail-closed (ADR-020).",
                    });
                }

                // (7) End date passed — the CURRENT in-lock user state (the B1 lesson: live
                // predicates, never snapshot-trusted). The end date is the LAST employed day,
                // so "passed" means the Copenhagen business date is STRICTLY after it.
                var today = CopenhagenToday(timeProvider);
                if (user.EmploymentEndDate is not { } endDate || endDate >= today)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "The employee's employment end date has not passed (or is not set) — a §26 " +
                                "payout request applies to a COMPLETED termination.",
                        employmentEndDate = user.EmploymentEndDate,
                    });
                }

                // (8) If-Match precondition on the settlement row version (ADR-019) — AFTER the
                // sequence binding (step 4), so a stale generation never reads as a version race.
                if (current.Version != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = current.Version,
                    }, statusCode: 412);
                }

                // (9) One non-voided request per settlement row (R6) — in-lock pre-check for the
                // clean 409; the partial-unique index backs it.
                var existing = await requestRepo.GetActiveBySettlementAsync(
                    conn, tx, employeeId, entitlementType, entitlementYear, current.Sequence, ct);
                if (existing is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "A live (non-voided) §26 payout request already exists for this settlement row.",
                        existingRequest = new
                        {
                            requestId = existing.RequestId,
                            state = existing.State,
                            requestDate = existing.RequestDate,
                            recordedBy = existing.RecordedBy,
                        },
                    }, statusCode: 409);
                }

                // (10) The request row (state OPEN — the 7104 repository; constraint backstop:
                // the typed duplicate exception maps to the same 409 shape).
                TerminationPayoutRequestRow created;
                try
                {
                    created = await requestRepo.CreateAsync(conn, tx, new TerminationPayoutRequestRow
                    {
                        EmployeeId = employeeId,
                        EntitlementType = entitlementType,
                        EntitlementYear = entitlementYear,
                        SettlementSequence = current.Sequence,
                        State = TerminationPayoutRequestRow.StateOpen,
                        RequestDate = body.RequestDate.Value,
                        RecordedBy = actorId,
                        EvidenceNote = body.EvidenceNote,
                        Version = 1,
                    }, ct);
                }
                catch (DuplicateActiveTerminationPayoutRequestException)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "A live (non-voided) §26 payout request already exists for this settlement row.",
                    }, statusCode: 409);
                }

                // (11) TerminationPayoutRequested (the 7101 shape) — quantities copied from the
                // row's SNAPSHOT — on employee-{id} via the outbox + the ADR-026 audit row, SAME
                // tx. Actor-context shape mirrors the end-date PUT / lifecycle writer (the
                // OPERATOR's org as ActorPrimaryOrgId; the employee's org as the resolved target).
                var requestedEvent = new TerminationPayoutRequested
                {
                    EmployeeId = employeeId,
                    EntitlementType = entitlementType,
                    EntitlementYear = entitlementYear,
                    SettlementSequence = current.Sequence,
                    RequestDate = body.RequestDate.Value,
                    EvidenceNote = body.EvidenceNote,
                    CrystallizedDays = crystallizedDays,
                    SettlementBoundaryDate = snapshot.SettlementBoundaryDate,
                    ActorId = actor.ActorId,
                    ActorRole = actor.ActorRole,
                    CorrelationId = actor.CorrelationId,
                };
                var outboxId = await outbox.EnqueueAndReturnIdAsync(
                    conn, tx, $"employee-{employeeId}", requestedEvent, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(requestedEvent.OccurredAt, DateTimeKind.Utc)),
                    ResolvedTargetOrgId: user.PrimaryOrgId);
                var rowData = requestAuditMapper.Map(requestedEvent, auditCtx);
                await auditRepo.InsertAsync(
                    conn, tx, requestedEvent.EventId, outboxId, requestedEvent.EventType, rowData, auditCtx, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{created.Version}\"";
                return Results.Json(new
                {
                    requestId = created.RequestId,
                    employeeId,
                    entitlementType,
                    entitlementYear,
                    settlementSequence = created.SettlementSequence,
                    state = created.State,
                    requestDate = created.RequestDate,
                    evidenceNote = created.EvidenceNote,
                    crystallizedDays,
                    settlementBoundaryDate = snapshot.SettlementBoundaryDate,
                    version = created.Version,
                }, statusCode: 201);
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

    // ── snapshot read (tolerant — the VacationSettlementEndpoints.DeserializeSnapshot shape;
    // a malformed stored snapshot degrades to null and the guards fail closed, never throw) ──

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private static VacationSettlementSnapshot? DeserializeSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;
        try { return JsonSerializer.Deserialize<VacationSettlementSnapshot>(snapshotJson, SnapshotJsonOptions); }
        catch (JsonException) { return null; }
    }

    // ── Europe/Copenhagen business-date helper (the EmploymentDateEndpoints file-scoped
    // convention; the injected TimeProvider is the PAT-008 test seam) ──

    private static readonly TimeZoneInfo CopenhagenZone = ResolveCopenhagenZone();

    private static DateOnly CopenhagenToday(TimeProvider timeProvider)
    {
        var copenhagenNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), CopenhagenZone);
        return DateOnly.FromDateTime(copenhagenNow.DateTime);
    }

    private static TimeZoneInfo ResolveCopenhagenZone()
    {
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// POST body. <c>EntitlementType</c> optional (defaults to VACATION — the only type the 3b
    /// TERMINATION close produces); <c>EntitlementYear</c> + <c>ExpectedSettlementSequence</c>
    /// bind the EXACT settlement row (SPRINT-71 R2); <c>RequestDate</c> is the REQUIRED §26
    /// anmodning evidence date (no statutory-deadline validation in 3b — slice Step-0 gate (ii)
    /// trail-silent fallback); <c>EvidenceNote</c> optional free text.
    /// </summary>
    private sealed record CreateTerminationPayoutRequestBody
    {
        public string? EntitlementType { get; init; }
        public int? EntitlementYear { get; init; }
        public int? ExpectedSettlementSequence { get; init; }
        public DateOnly? RequestDate { get; init; }
        public string? EvidenceNote { get; init; }
    }
}
