using System;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StatsTid.Auth;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure.Security;

/// <summary>
/// <b>QUAL-009 / SEC-038 — the denial audit-ROW writer.</b>
///
/// <para><b>Why a SEPARATE middleware, registered BEFORE authorization:</b> the existing
/// <see cref="AuditLoggingMiddleware"/> runs <i>after</i> <c>UseAuthorization</c>, so on a policy
/// denial the authorization middleware short-circuits and that middleware never runs — which is the
/// whole QUAL-009 gap. This middleware is placed BEFORE <c>UseAuthorization</c>, so it is still on
/// the call stack when authorization short-circuits: it calls <c>next</c> (which runs authorization,
/// and — only if authorization passes — everything downstream), and when control returns it inspects
/// the request for a denial annotation left by
/// <see cref="DenialLoggingAuthorizationResultHandler"/>.</para>
///
/// <para><b>No double-write:</b> it is DENIAL-GATED. An allowed request leaves no annotation, so
/// nothing is written here — the allowed-request audit row is still the sole responsibility of
/// <see cref="AuditLoggingMiddleware"/> downstream. Denied and allowed are mutually exclusive (the
/// denial short-circuits before the downstream middleware), so exactly one component writes for any
/// given request.</para>
///
/// <para><b>Row scope (owner OQ-1a):</b> a row is written ONLY for admin-strict / mutating denials.
/// Routine read-scope denials get the structured log (emitted by the result handler) but no row. The
/// gate is <b>deny-by-default</b>: a denial is skipped ONLY when it was positively classified
/// <see cref="DenialRouteClass.RoutineRead"/>; an unknown/new route (classified
/// <see cref="DenialRouteClass.AdminOrMutating"/>) is always written.</para>
///
/// <para><b>Best-effort:</b> the DB write is wrapped in a swallowing try/catch that logs its own
/// failure. An audit-row write must never change the already-decided 403/401 the client receives.</para>
/// </summary>
public sealed class PolicyDenialAuditRowMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PolicyDenialAuditRowMiddleware> _logger;

    public PolicyDenialAuditRowMiddleware(RequestDelegate next, ILogger<PolicyDenialAuditRowMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AuditLogRepository auditRepo)
    {
        // Run the rest of the pipeline — crucially INCLUDING UseAuthorization, which sits just
        // downstream. On a denial it short-circuits and control returns here with the annotation set.
        await _next(context);

        var entry = BuildRowIfAuditable(context);
        if (entry is null)
            return; // denial-gated + routine-read-excluded → nothing to persist.

        try
        {
            await auditRepo.AppendAsync(entry);
        }
        catch (Exception ex)
        {
            // Best-effort: a failed audit-row write must never disturb the already-sent 403/401.
            _logger.LogWarning(ex,
                "Failed to write policy-denial audit row (QUAL-009 / SEC-038); the denial response is unaffected.");
        }
    }

    /// <summary>
    /// Pure decision: returns the <see cref="AuditLogEntry"/> to persist for this request, or
    /// <c>null</c> when no row should be written. Split out from <see cref="InvokeAsync"/> so the
    /// row/no-row gate is unit-testable without a database.
    /// <list type="bullet">
    /// <item><description>No <see cref="PolicyDenialAudit"/> annotation → allowed request (or a
    /// non-authorization short-circuit) → <c>null</c> (no row).</description></item>
    /// <item><description>Annotation present but classified
    /// <see cref="DenialRouteClass.RoutineRead"/> → log-only → <c>null</c> (no row).</description></item>
    /// <item><description>Otherwise (admin-strict / mutating / unknown) → an <c>audit_log</c> failure
    /// row. Deny-by-default: the write is skipped ONLY for a proven routine read.</description></item>
    /// </list>
    /// </summary>
    internal static AuditLogEntry? BuildRowIfAuditable(HttpContext context)
    {
        if (!context.Items.TryGetValue(DenialLoggingAuthorizationResultHandler.ItemKey, out var raw)
            || raw is not PolicyDenialAudit d)
        {
            return null; // not a policy denial → the allowed-request path owns any row.
        }

        // Deny-by-default: audit everything that was NOT positively proven a routine read.
        if (d.RouteClass == DenialRouteClass.RoutineRead)
            return null;

        return new AuditLogEntry
        {
            ActorId = d.ActorId,
            ActorRole = d.ActorRole,
            Action = $"{d.Method} {d.Path}",
            Resource = d.Path,
            CorrelationId = d.CorrelationId == Guid.Empty ? null : d.CorrelationId,
            HttpMethod = d.Method,
            HttpPath = d.Path,
            HttpStatus = d.StatusCode,
            Result = "failure",
            Details = JsonSerializer.Serialize(new
            {
                denial = "policy-authorization",
                outcome = d.Outcome,
                policy = d.PolicyId,
            }),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
        };
    }
}
