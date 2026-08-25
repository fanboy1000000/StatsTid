using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Auth;

/// <summary>
/// The observed record of a policy-authorization DENIAL, stashed in <see cref="HttpContext.Items"/>
/// under <see cref="DenialLoggingAuthorizationResultHandler.ItemKey"/> by the result handler and
/// read back by the before-authz audit-row middleware (which lives in the Infrastructure assembly).
/// It carries only non-secret facts — never claims, token, or body.
/// </summary>
/// <param name="Outcome">"Forbidden" (403) or "Challenged" (401).</param>
/// <param name="StatusCode">The HTTP status the denial produced (403 or 401).</param>
/// <param name="PolicyId">A stable, non-PII identifier for the route's authorization policy.</param>
/// <param name="RouteClass">Routine-read (log-only) vs admin/mutating (auditable).</param>
/// <param name="ActorId">The authenticated subject id, or null when unauthenticated (anonymous).</param>
/// <param name="ActorRole">The authenticated role, or null when unauthenticated.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Path">The request path (resource), never the query/body.</param>
/// <param name="CorrelationId">The request correlation id (Guid.Empty when none was resolved).</param>
public sealed record PolicyDenialAudit(
    string Outcome,
    int StatusCode,
    string PolicyId,
    DenialRouteClass RouteClass,
    string? ActorId,
    string? ActorRole,
    string Method,
    string Path,
    Guid CorrelationId);

/// <summary>
/// <b>QUAL-009 / SEC-038 — the policy-denial trace.</b>
///
/// <para><b>Why this exists (plain language):</b> when ASP.NET's authorization layer refuses a
/// request (a 403 "forbidden" or 401 "challenge"), it short-circuits the pipeline BEFORE the audit
/// middleware runs — so historically a denial produced no application log and no audit row, and the
/// cause of a 403 could not be reconstructed afterward. This handler restores that visibility:
/// it OBSERVES every final authorization decision and, on a denial, emits a structured, redacted,
/// correlation-linked log line (and annotates the request so the audit-row middleware can persist a
/// row on the sensitive routes).</para>
///
/// <para><b>It decides NOTHING.</b> It is a decorator over the framework's built-in
/// <see cref="AuthorizationMiddlewareResultHandler"/>: on EVERY path (success, challenge, forbid) it
/// delegates the ACTUAL result handling to that default handler. Removing this decorator would
/// change observability only — never the allow/deny outcome or the response.</para>
///
/// <para><b>Redaction:</b> the log carries the outcome, a stable policy identifier, the route class,
/// the method + path (resource), the actor id/role, the status, and the correlation id — and NOTHING
/// ELSE. No claims, no token, no request body. Identifiers are run through
/// <see cref="LogSanitizer.Sanitize"/> to strip CR/LF (log-forging, CWE-117).</para>
///
/// <para><b>Level discipline (owner OQ-1a):</b> routine read-scope 403s log at
/// <see cref="LogLevel.Information"/>; admin-strict / mutating denials log at
/// <see cref="LogLevel.Warning"/>.</para>
///
/// <para>Registered as the single <see cref="IAuthorizationMiddlewareResultHandler"/> (replacing the
/// framework default) inside <c>AuthorizationPolicies.AddStatsTidPolicies</c>, so every host that
/// wires the StatsTid policies gets the trace automatically — no host can be missed.</para>
/// </summary>
public sealed class DenialLoggingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>
    /// <see cref="HttpContext.Items"/> key under which a <see cref="PolicyDenialAudit"/> is stashed
    /// on a denial. The before-authz audit-row middleware reads it; its ABSENCE means "not a policy
    /// denial" (allowed request, or a non-authorization short-circuit) → no row is written.
    /// </summary>
    public const string ItemKey = "authz:policy_denial";

    // The genuine framework handler. We hold one instance and delegate ALL result handling to it —
    // we never write to the response ourselves. (It is stateless; a single instance is safe.)
    private readonly AuthorizationMiddlewareResultHandler _default = new();
    private readonly ILogger<DenialLoggingAuthorizationResultHandler> _logger;

    public DenialLoggingAuthorizationResultHandler(ILogger<DenialLoggingAuthorizationResultHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden || authorizeResult.Challenged)
        {
            // OBSERVE ONLY. A failure to log/annotate must NEVER change the outcome or throw into
            // the pipeline — the 403/401 still returns, unmodified.
            try
            {
                ObserveDenial(context, authorizeResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Policy-denial trace failed to record a denial (QUAL-009 / SEC-038); the authorization outcome is unaffected.");
            }
        }

        // Delegate the ACTUAL result handling to the built-in handler on EVERY path:
        //   success   → it calls next(context) and the request proceeds;
        //   challenged→ it writes 401;
        //   forbidden → it writes 403.
        // This decorator changes the request flow in no way.
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private void ObserveDenial(HttpContext context, PolicyAuthorizationResult result)
    {
        var routeClass = PolicyDenialClassifier.ClassifyRoute(context);
        var outcome = result.Challenged ? "Challenged" : "Forbidden";
        var status = result.Challenged
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;
        var policyId = PolicyDenialClassifier.DescribePolicies(context);

        // Actor identity from claims ONLY (no token, no full claim dump). Unauthenticated
        // (a Challenge) yields a null actor id/role → logged as "anonymous"/"(none)".
        var actorId = context.User?.FindFirst("sub")?.Value
                      ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? context.User?.FindFirst(StatsTidClaims.EmployeeId)?.Value;
        var actorRole = context.User?.FindFirst(StatsTidClaims.Role)?.Value;

        var correlationId =
            context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var cid) && cid is Guid g
                ? g
                : Guid.Empty;

        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "/";

        // Annotate the request so the before-authz audit-row middleware (which regains control after
        // the authorization short-circuit) can decide whether to persist a row. Written BEFORE we
        // delegate to the default handler, so it is visible by the time control unwinds.
        context.Items[ItemKey] = new PolicyDenialAudit(
            Outcome: outcome,
            StatusCode: status,
            PolicyId: policyId,
            RouteClass: routeClass,
            ActorId: actorId,
            ActorRole: actorRole,
            Method: method,
            Path: path,
            CorrelationId: correlationId);

        var level = routeClass == DenialRouteClass.RoutineRead ? LogLevel.Information : LogLevel.Warning;

        // The correlation id also rides the ambient log scope (QUAL-008); we include it explicitly
        // so a plain-text line is self-contained even where scopes are not rendered.
        _logger.Log(
            level,
            "Authorization denied: outcome={Outcome} status={Status} policy={Policy} routeClass={RouteClass} method={Method} path={Path} actor={Actor} actorRole={ActorRole} correlationId={CorrelationId}",
            outcome,
            status,
            LogSanitizer.Sanitize(policyId),
            routeClass,
            method,
            LogSanitizer.Sanitize(path),
            LogSanitizer.Sanitize(actorId) ?? "anonymous",
            LogSanitizer.Sanitize(actorRole) ?? "(none)",
            correlationId);
    }
}
