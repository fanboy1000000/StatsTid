using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.External.Services;
using StatsTid.SharedKernel.Interfaces;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));

// ── Outbox: dual-binding per ADR-018 D3 + per-service publisher per D2/D6 ──
// External owns integration-delivery-* streams per ADR-018 D6 stream-ownership
// table. Today External does not write events; the publisher polls an empty
// partition until forward-looking event-emit sites land in later S22 phases.
builder.Services.AddSingleton(new OutboxServiceContext("external"));
builder.Services.AddSingleton<PostgresEventStore>(sp => new PostgresEventStore(
    sp.GetRequiredService<DbConnectionFactory>(),
    sp.GetRequiredService<OutboxServiceContext>()));
builder.Services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddSingleton<IOutboxEnqueue>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddHostedService<OutboxPublisher>();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<ExternalApiClient>();
builder.Services.AddSingleton<DeliveryTracker>();
builder.Services.AddHostedService<EventConsumerService>();

builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "external-integration" }));

// SEC-023: outbound external dispatch is a privileged integration action — the same class as
// its sibling /api/payroll/export (GlobalAdminOnly). It forwards a caller-supplied JSON envelope
// to the external system, and the Orchestrator's TaskDispatcher forwards the caller's JWT, so the
// endpoint floor is the control point. The floor was "Authenticated" (any valid JWT, incl. the
// lowest Employee role); it is raised to GlobalAdminOnly. The body is also size-capped and
// shape-checked (object envelope only) BEFORE it is forwarded. The real per-field payload schema
// is deferred (no external contract exists yet); a valid JSON object forwards unchanged.
app.MapPost("/api/external/send", async (HttpRequest request, ExternalApiClient client, HttpContext context, CancellationToken ct) =>
{
    const long MaxBodyBytes = 256 * 1024; // SEC-023: 256 KB envelope cap.

    // (1) Size cap BEFORE deserialization. A declared Content-Length over the cap is rejected
    //     here, before ReadFromJsonAsync buffers the body — a check AFTER the read is decorative.
    if (request.ContentLength is > MaxBodyBytes)
    {
        return Results.Json(
            new { error = "Request body exceeds the 256 KB limit." },
            statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    // Backstop for a chunked / no-Content-Length body the pre-check above cannot see: cap the
    // streamed read itself so an oversize body fails at the cap (413) rather than buffering to OOM.
    var maxBodyFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (maxBodyFeature is { IsReadOnly: false })
    {
        maxBodyFeature.MaxRequestBodySize = MaxBodyBytes;
    }

    var actor = context.GetActorContext();

    // (2) Read the body.
    JsonElement payload;
    try
    {
        payload = await request.ReadFromJsonAsync<JsonElement>(ct);
    }
    catch (BadHttpRequestException ex)
    {
        // Kestrel enforced the MaxRequestBodySize backstop mid-read (oversize → 413), or the
        // request was otherwise malformed at the transport level. Surface its status, not a 500.
        return Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Request body is not valid JSON." });
    }

    // (3) Object-shape check: a bare string / number / array / null top-level body is never a
    //     valid message envelope. A valid JSON object forwards unchanged (NO per-field schema —
    //     the external contract does not exist yet; that enforcement is a deferred future task).
    if (payload.ValueKind != JsonValueKind.Object)
    {
        return Results.BadRequest(new { error = "Request body must be a JSON object." });
    }

    // (4) Forward unchanged.
    var result = await client.SendAsync(payload, actor.CorrelationId, ct);
    return result.Success
        ? Results.Ok(new { success = true, messageId = result.MessageId, status = "delivered" })
        : Results.UnprocessableEntity(new { success = false, error = result.ErrorMessage });
}).RequireAuthorization("GlobalAdminOnly"); // SEC-023: raised from "Authenticated".

app.Run();
