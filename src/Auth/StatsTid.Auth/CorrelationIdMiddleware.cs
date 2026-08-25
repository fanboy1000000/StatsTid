using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace StatsTid.Auth;

/// <summary>
/// Mints (or adopts an inbound) correlation id per HTTP request and makes it ambient two ways:
/// <list type="bullet">
/// <item><description><b>For downstream code + outbound hops</b> — writes the id to
/// <see cref="HttpContext.Items"/> under <see cref="ItemKey"/> and echoes it on the RESPONSE
/// header, so callers and the Backend→rule-engine forwarder can read it (QUAL-065).</description></item>
/// <item><description><b>For logs</b> — opens an <see cref="ILogger.BeginScope{TState}(TState)"/>
/// carrying the id around the rest of the pipeline (QUAL-008), so every log line emitted <i>within
/// this correlated HTTP operation</i> carries the id automatically — no per-call-site plumbing.
/// </description></item>
/// </list>
///
/// <para><b>Honest scope of the log-scope (QUAL-008):</b> the scope covers only work that runs
/// inside the request pipeline. Startup seeders/backfills, hosted <c>BackgroundService</c>s, and
/// queue/outbox consumers run OUTSIDE any request and are NOT covered — their logs carry no
/// correlation id from this mechanism. Rendering the scoped id in the DEFAULT console sink also
/// requires the host's logging config to enable <c>IncludeScopes</c> (structured sinks such as
/// Serilog/OpenTelemetry capture scopes without it) — a host-config concern, not this middleware's.</para>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// The <see cref="HttpContext.Items"/> key under which the resolved correlation id (a
    /// <see cref="Guid"/>) is stored. Read by the Backend→rule-engine forwarder to propagate a
    /// frontend-originated (header-less) id across the hop (QUAL-065). This same string doubles as
    /// the structured-log scope property name, so log sinks surface it as "CorrelationId".
    /// </summary>
    public const string ItemKey = "CorrelationId";

    // The logger is optional so existing direct-construction unit tests (new CorrelationIdMiddleware(next))
    // keep compiling; in the real pipeline UseMiddleware<T> injects ILogger<CorrelationIdMiddleware> from
    // the root provider. When absent we fall back to the no-op NullLogger (BeginScope becomes a no-op).
    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger ?? NullLogger<CorrelationIdMiddleware>.Instance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        Guid correlationId;

        if (context.Request.Headers.TryGetValue(HeaderName, out var headerValue) &&
            Guid.TryParse(headerValue.FirstOrDefault(), out var parsed))
        {
            correlationId = parsed;
        }
        else
        {
            correlationId = Guid.NewGuid();
        }

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();

        // QUAL-008: carry the SAME id already placed in Items (do NOT mint a second) as an ambient
        // log scope around the remainder of the pipeline. The scope opens BEFORE _next and disposes
        // AFTER it, so every log emitted while handling this request is stamped with the id, and the
        // scope never leaks past the request boundary.
        using (_logger.BeginScope(new Dictionary<string, object> { [ItemKey] = correlationId }))
        {
            await _next(context);
        }
    }
}
