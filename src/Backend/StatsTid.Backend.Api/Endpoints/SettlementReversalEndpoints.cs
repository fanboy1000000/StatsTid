using System.Globalization;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S71 / TASK-7102 (ADR-033 D4/D5; SPRINT-71 R4/R12 + owner D-A/D-B) — the operator-authorized
/// settlement REVERSAL surface:
///
/// <para>
/// <b>POST /api/admin/employees/{employeeId}/settlement-reversal</b> — drives
/// <see cref="SettlementReversalService.ReverseAsync"/> with the OPERATOR-EXPLICIT mode (the
/// endpoint never infers it; ADR-013 — reversal is operator-authorized, never automatic):
/// <c>BARE</c> (REVERSED + the R3 durable not-due marker; TERMINAL in 3b) or
/// <c>REVERSE_AND_SUPERSEDE</c> (REVERSED + a superseding settlement in the SAME tx, optionally
/// subsuming an end-date correction — null WITH the <c>hasEndDateCorrection</c> flag means
/// CLEAR, triggering the R1(c) provenance-guarded reactivation).
/// </para>
///
/// <para><b>Preconditions — the R4 two-aggregate shape (DECLARED):</b></para>
/// <list type="bullet">
///   <item><description>the <c>If-Match</c> HEADER carries the SETTLEMENT row's ADR-019 version
///   (admin-strict; missing/malformed → 428) — the endpoint's primary aggregate;</description></item>
///   <item><description>the BODY carries <c>expectedSettlementSequence</c> (R2/B1 — the CAS
///   binds the GENERATION; row versions restart at 1 per generation, so the version alone is
///   ABA-prone) — missing → 422;</description></item>
///   <item><description>when a mode applies an end-date mutation
///   (<c>hasEndDateCorrection: true</c>), the BODY additionally carries
///   <c>expectedUserVersion</c> — the <c>users.version</c> precondition (HTTP has ONE If-Match
///   header, so the second aggregate's token rides the body; missing → 428, admin-strict parity
///   with the PUT's 412/428 semantics).</description></item>
/// </list>
///
/// <para>
/// <b>Self-target exclusion (R4, inherited from S70 Step-7a W1):</b> ANY mode carrying an
/// end-date correction 403s <c>actor == employee</c> BEFORE any DB work — exactly like the
/// end-date PUT (the self-reinstatement hole must not reopen through this second lifecycle
/// writer; the service re-checks as defense-in-depth). A bare reversal carries no end-date
/// mutation and is NOT excluded.
/// </para>
///
/// <para>
/// <b>The D13 go-live gate (DECLARED):</b> the endpoint resolves <c>Settlement:GoLiveDate</c>
/// with the <c>SettlementCloseService</c> parse shape (strict ISO <c>yyyy-MM-dd</c>;
/// present-but-unparseable FAILS CLOSED to unconfigured). Unconfigured = DORMANT: a
/// <c>REVERSE_AND_SUPERSEDE</c> request is refused 409 — the supersession leg would SETTLE, and
/// nothing settles pre-go-live (D13); passing a null floor to the service would WAIVE the
/// go-live clause in the eligibility predicates, so the endpoint refuses instead of forwarding.
/// BARE reversal stays allowed dormant (it settles nothing). When configured, the parsed date
/// is passed as the command's <c>SupersedeGoLiveFloor</c> (close-service parity).
/// </para>
///
/// <para><b>Error mapping (the <see cref="SettlementReversalFailure"/> discriminators; every
/// error body carries the machine-readable <c>failure</c> field + the service's reason):</b></para>
/// <list type="bullet">
///   <item><description>404 — <c>NoActiveRow</c> / <c>UserNotFound</c>;</description></item>
///   <item><description>409 — <c>SequenceMismatch</c> (with <c>actualSettlementSequence</c>),
///   <c>CarryoverWritingRow</c> (D-A zero-bucket), <c>ReconciledRow</c> (R4 exclusion),
///   <c>AffectedSpanConflict</c> (the B2/R13 guard — the reason NAMES every blocker),
///   <c>SupersedeNotEligible</c>, <c>SupersedeCarryoverConflict</c>, and the dormant-go-live
///   supersession refusal;</description></item>
///   <item><description>412 — <c>CasConflict</c> (stale settlement If-Match, with
///   <c>actualVersion</c>) / <c>UserVersionConflict</c> (stale <c>expectedUserVersion</c>, with
///   <c>actualUserVersion</c>);</description></item>
///   <item><description>403 — <c>SelfTarget</c> (service defense-in-depth; normally caught at
///   the endpoint) / validator denial;</description></item>
///   <item><description>422 — body-shape violations (unknown mode, bare+correction,
///   correctedEndDate without the flag, missing sequence/year);</description></item>
///   <item><description>428 — missing/malformed If-Match; missing expectedUserVersion on an
///   end-date-correcting mode.</description></item>
/// </list>
///
/// <para>
/// On success (200) the body carries BOTH aggregates' outcomes (reversed row + optional
/// successor + user lifecycle outcome + VOIDed request ids); NO ETag is stamped (DECLARED — two
/// aggregates were mutated, a single header token would be ambiguous; the body carries every
/// version).
/// </para>
/// </summary>
public static class SettlementReversalEndpoints
{
    private const string VacationType = "VACATION";

    public static WebApplication MapSettlementReversalEndpoints(this WebApplication app)
    {
        app.MapPost("/api/admin/employees/{employeeId}/settlement-reversal", async (
            string employeeId,
            SettlementReversalRequestBody body,
            SettlementReversalService reversalService,
            OrgScopeValidator scopeValidator,
            IConfiguration configuration,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Mode — operator-explicit, never inferred (422 on anything unknown).
            var modeRaw = (body.Mode ?? string.Empty).Trim().ToUpperInvariant();
            SettlementReversalMode mode;
            switch (modeRaw)
            {
                case "BARE":
                    mode = SettlementReversalMode.Bare;
                    break;
                case "REVERSE_AND_SUPERSEDE":
                    mode = SettlementReversalMode.ReverseAndSupersede;
                    break;
                default:
                    return Results.UnprocessableEntity(new
                    {
                        error = "mode must be 'BARE' or 'REVERSE_AND_SUPERSEDE' (the operator states the " +
                                "reversal mode explicitly — it is never inferred).",
                        mode = body.Mode,
                    });
            }

            // R4 self-target exclusion — ANY mode carrying an end-date correction, BEFORE any
            // DB work and for ALL actors (mirrors the end-date PUT's S70 Step-7a W1 guard
            // verbatim: a lifecycle-deactivated HR actor's still-valid JWT must not self-
            // reinstate through this second lifecycle writer).
            if (body.HasEndDateCorrection
                && string.Equals(actor.ActorId, employeeId, StringComparison.Ordinal))
            {
                return Results.Json(new
                {
                    error = "Access denied",
                    reason = "Own employment end date cannot be modified; a second administrator must perform this change",
                }, statusCode: 403);
            }

            // Terminated-INCLUSIVE validator (owner D-B: HROrAbove for ALL four 3b verbs, each
            // with the terminated-inclusive path — the reversal target is typically a leaver).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match — the SETTLEMENT row version (ADR-019).
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedSettlementVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            // Body shape (422/428 before any DB work — these would otherwise surface as the
            // service's ArgumentException caller-contract throws).
            if (body.EntitlementYear is null)
                return Results.UnprocessableEntity(new { error = "entitlementYear is required." });
            if (body.ExpectedSettlementSequence is null)
                return Results.UnprocessableEntity(new
                {
                    error = "expectedSettlementSequence is required (SPRINT-71 R2/B1 — the reversal binds " +
                            "the exact settlement GENERATION; versions restart per generation).",
                });
            if (mode == SettlementReversalMode.Bare && body.HasEndDateCorrection)
                return Results.UnprocessableEntity(new
                {
                    error = "A bare reversal carries no end-date correction — the corrected-end-date input " +
                            "belongs to the REVERSE_AND_SUPERSEDE mode.",
                });
            if (!body.HasEndDateCorrection && body.CorrectedEndDate is not null)
                return Results.UnprocessableEntity(new
                {
                    error = "correctedEndDate was supplied without hasEndDateCorrection — ambiguous " +
                            "(null correctedEndDate WITH the flag means 'clear the end date').",
                });
            if (body.HasEndDateCorrection && body.ExpectedUserVersion is null)
                return Results.Json(new
                {
                    error = "An end-date-correcting reversal carries BOTH expected versions (SPRINT-71 R4 " +
                            "two-aggregate preconditions) — expectedUserVersion is required in the body.",
                }, statusCode: 428);

            var entitlementType = string.IsNullOrWhiteSpace(body.EntitlementType)
                ? VacationType
                : body.EntitlementType.Trim();

            // The D13 go-live gate — see the class doc. BARE stays allowed dormant.
            var goLiveDate = ResolveGoLiveDate(configuration);
            if (mode == SettlementReversalMode.ReverseAndSupersede && goLiveDate is null)
            {
                return Results.Json(new
                {
                    error = "Settlement go-live is not configured (or not a valid ISO yyyy-MM-dd date) — a " +
                            "superseding settlement cannot be created while the settlement machinery is " +
                            "DORMANT (ADR-033 D13: nothing settles pre-go-live; fail-closed).",
                    failure = "SupersedeGoLiveDormant",
                    hint = "Use the BARE mode to park the tuple with the durable not-due marker, or have " +
                           "ops configure Settlement:GoLiveDate.",
                }, statusCode: 409);
            }

            var command = new SettlementReversalCommand
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = body.EntitlementYear.Value,
                ExpectedSettlementSequence = body.ExpectedSettlementSequence.Value,
                ExpectedSettlementVersion = expectedSettlementVersion,
                Mode = mode,
                HasEndDateCorrection = body.HasEndDateCorrection,
                CorrectedEndDate = body.CorrectedEndDate,
                ExpectedUserVersion = body.ExpectedUserVersion,
                SupersedeGoLiveFloor = goLiveDate,
                ActorId = actor.ActorId ?? "unknown",
                ActorRole = actor.ActorRole ?? "unknown",
                ActorOrgId = actor.OrgId,
                CorrelationId = actor.CorrelationId,
            };

            var result = await reversalService.ReverseAsync(command, ct);

            if (!result.Succeeded)
                return MapFailure(result);

            var reversed = result.ReversedRow!;
            return Results.Ok(new
            {
                employeeId,
                entitlementType,
                entitlementYear = body.EntitlementYear.Value,
                reversalKind = result.SupersedingRow is null ? "BARE" : "SUPERSEDED",
                reversedSequence = reversed.Sequence,
                reversedVersion = reversed.Version,
                bareReversalNotDue = reversed.BareReversalNotDue,
                successor = result.SupersedingRow is { } successorRow
                    ? (object)new
                    {
                        sequence = successorRow.Sequence,
                        settlementState = successorRow.SettlementState,
                        trigger = successorRow.Trigger,
                        version = successorRow.Version,
                    }
                    : null,
                voidedRequestIds = result.VoidedRequestIds,
                userVersionAfter = result.UserVersionAfter,
                userIsActiveAfter = result.UserIsActiveAfter,
            });
        }).RequireAuthorization("HROrAbove");

        return app;
    }

    /// <summary>The 7104 contract delta mapping (SPRINT-71 Implementation Record): every error
    /// body carries the machine-readable <c>failure</c> discriminator + the service's reason.</summary>
    private static IResult MapFailure(SettlementReversalResult result)
    {
        var failure = result.Failure.ToString();
        return result.Failure switch
        {
            SettlementReversalFailure.NoActiveRow or
            SettlementReversalFailure.UserNotFound => Results.NotFound(new
            {
                error = result.FailureReason,
                failure,
            }),

            SettlementReversalFailure.SequenceMismatch => Results.Json(new
            {
                error = result.FailureReason,
                failure,
                actualSettlementSequence = result.ActualSettlementSequence,
                actualVersion = result.ActualSettlementVersion,
            }, statusCode: 409),

            SettlementReversalFailure.CasConflict => Results.Json(new
            {
                error = result.FailureReason,
                failure,
                actualVersion = result.ActualSettlementVersion,
            }, statusCode: 412),

            SettlementReversalFailure.UserVersionConflict => Results.Json(new
            {
                error = result.FailureReason,
                failure,
                actualUserVersion = result.ActualUserVersion,
            }, statusCode: 412),

            SettlementReversalFailure.SelfTarget => Results.Json(new
            {
                error = "Access denied",
                failure,
                reason = result.FailureReason,
            }, statusCode: 403),

            // CarryoverWritingRow (D-A), ReconciledRow (R4), AffectedSpanConflict (B2/R13 —
            // the reason NAMES every blocker), SupersedeNotEligible, SupersedeCarryoverConflict.
            _ => Results.Json(new
            {
                error = result.FailureReason,
                failure,
            }, statusCode: 409),
        };
    }

    /// <summary>
    /// The D13 go-live resolution — the <c>SettlementCloseService</c> parse shape VERBATIM:
    /// strict ISO <c>yyyy-MM-dd</c> via <c>TryParseExact</c> (never the permissive TryParse —
    /// a locale-ambiguous value must fail closed to dormant, not activate a supersession on a
    /// misread date); unconfigured OR present-but-unparseable ⇒ null (DORMANT). PURE; unit-pinned.
    /// </summary>
    public static DateOnly? ResolveGoLiveDate(IConfiguration configuration)
    {
        var raw = configuration["Settlement:GoLiveDate"];
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// POST body (the R4 command shape over the wire). <c>EntitlementType</c> optional (defaults
    /// to VACATION); <c>Mode</c> ∈ BARE / REVERSE_AND_SUPERSEDE (required, explicit);
    /// <c>HasEndDateCorrection</c> + <c>CorrectedEndDate</c> express the subsumed end-date
    /// mutation (the flag disambiguates "no change" from "clear"); <c>ExpectedUserVersion</c> is
    /// the second aggregate's precondition (required iff the correction flag is set). The
    /// settlement row's version rides the <c>If-Match</c> HEADER.
    /// </summary>
    private sealed record SettlementReversalRequestBody
    {
        public string? EntitlementType { get; init; }
        public int? EntitlementYear { get; init; }
        public int? ExpectedSettlementSequence { get; init; }
        public string? Mode { get; init; }
        public bool HasEndDateCorrection { get; init; }
        public DateOnly? CorrectedEndDate { get; init; }
        public long? ExpectedUserVersion { get; init; }
    }
}
