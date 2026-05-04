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

app.MapPost("/api/external/send", async (HttpRequest request, ExternalApiClient client, HttpContext context, CancellationToken ct) =>
{
    var actor = context.GetActorContext();
    var payload = await request.ReadFromJsonAsync<object>(ct);
    var result = await client.SendAsync(payload!, actor.CorrelationId, ct);
    return result.Success
        ? Results.Ok(new { success = true, messageId = result.MessageId, status = "delivered" })
        : Results.UnprocessableEntity(new { success = false, error = result.ErrorMessage });
}).RequireAuthorization("Authenticated");

app.Run();
