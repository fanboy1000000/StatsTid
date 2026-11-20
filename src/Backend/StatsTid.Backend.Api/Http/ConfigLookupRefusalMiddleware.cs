using StatsTid.Infrastructure;

namespace StatsTid.Backend.Api.Http;

/// <summary>
/// The Backend's one answer to the configuration lookup's two refusals (P-13; <see cref="IAgreementConfigLookup"/>):
/// every route that reads a configuration, directly or through the per-day norm, names the refusal the same
/// way, and no route or service catches either exception itself.
/// <list type="bullet">
///   <item><description><see cref="ConfigLookupUnavailableException"/> (a configuration read failed) → 503
///   <c>{ "error": "Agreement configuration service unavailable" }</c>, the house shape of a dependency
///   outage.</description></item>
///   <item><description><see cref="NoConfigInForceException"/> (no published configuration is in force on the
///   day asked for) → 500 <c>{ "error": "config_not_in_force", "reason": "…" }</c>, the house shape of a named
///   data gap.</description></item>
/// </list>
/// Both bodies are fixed, value-free and date-free (ADR-040 D7); the lookup has already logged the refusal
/// with its agreement, OK version and day, so nothing is logged here. Every other exception passes through
/// untouched, and once the response has started the exception propagates as it is. Registered directly after
/// <see cref="StatsTid.Infrastructure.Security.AuditLoggingMiddleware"/>, so the audit row records the
/// answered status. A write rolls back as with any exception: the refusal leaves the handler before its commit.
/// </summary>
public sealed class ConfigLookupRefusalMiddleware
{
    private const string UnavailableError = "Agreement configuration service unavailable";
    private const string NotInForceError = "config_not_in_force";
    private const string NotInForceReason =
        "No published agreement configuration is in force for this date. A configuration administrator must publish one.";

    private readonly RequestDelegate _next;

    public ConfigLookupRefusalMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ConfigLookupUnavailableException) when (!context.Response.HasStarted)
        {
            await Results.Json(
                    new { error = UnavailableError },
                    statusCode: StatusCodes.Status503ServiceUnavailable)
                .ExecuteAsync(context);
        }
        catch (NoConfigInForceException) when (!context.Response.HasStarted)
        {
            await Results.Json(
                    new { error = NotInForceError, reason = NotInForceReason },
                    statusCode: StatusCodes.Status500InternalServerError)
                .ExecuteAsync(context);
        }
    }
}
