using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StatsTid.Auth;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure.Security;

public sealed class AuditLoggingMiddleware
{
    /// <summary>
    /// HttpContext.Items key used by endpoints that compute a segment manifest (TASK-2008) to
    /// hand the resulting <c>ManifestId</c> back to the audit middleware. When present, the
    /// manifest id is serialised into the <c>audit_log.details</c> JSONB column as
    /// <c>{"manifest_id":"&lt;guid&gt;"}</c>. When absent, <see cref="AuditLogEntry.Details"/>
    /// stays null, producing JSON identical to today (ADR-016 D10, amended 2026-04-29).
    /// </summary>
    public const string ManifestIdItemKey = "audit:manifest_id";

    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AuditLogRepository auditRepo)
    {
        // Skip health endpoints
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        await _next(context);

        try
        {
            var actorContext = context.GetActorContext();

            var entry = new AuditLogEntry
            {
                ActorId = actorContext.ActorId,
                ActorRole = actorContext.ActorRole,
                Action = $"{context.Request.Method} {context.Request.Path}",
                Resource = context.Request.Path.Value ?? "/",
                CorrelationId = actorContext.CorrelationId,
                HttpMethod = context.Request.Method,
                HttpPath = context.Request.Path.Value,
                HttpStatus = context.Response.StatusCode,
                Result = context.Response.StatusCode < 400 ? "success" : "failure",
                Details = BuildDetailsPayload(context),
                IpAddress = context.Connection.RemoteIpAddress?.ToString()
            };

            await auditRepo.AppendAsync(entry);
        }
        catch (Exception ex)
        {
            // Audit logging failures must never block the response
            _logger.LogWarning(ex, "Failed to write audit log entry");
        }
    }

    /// <summary>
    /// Builds the JSONB payload for the <c>audit_log.details</c> column. Returns <c>null</c>
    /// (not an empty object) when no fields contribute, so the on-disk JSON is byte-identical
    /// to pre-Sprint-20 audit rows whenever no endpoint stuffs anything into Items.
    /// </summary>
    private static string? BuildDetailsPayload(HttpContext context)
    {
        if (!context.Items.TryGetValue(ManifestIdItemKey, out var manifestObj) || manifestObj is null)
        {
            return null;
        }

        var manifestId = manifestObj switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => Guid.Empty
        };

        if (manifestId == Guid.Empty)
        {
            return null;
        }

        return JsonSerializer.Serialize(new { manifest_id = manifestId.ToString() });
    }
}
