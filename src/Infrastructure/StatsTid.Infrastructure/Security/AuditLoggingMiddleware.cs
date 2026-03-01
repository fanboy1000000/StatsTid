using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public sealed class AuditLoggingMiddleware
{
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
}
