using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.Integrations.External.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ExternalApiClient>();
builder.Services.AddSingleton<DeliveryTracker>();
builder.Services.AddHostedService<EventConsumerService>();

builder.Services.AddStatsTidJwtAuth(builder.Configuration);
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
