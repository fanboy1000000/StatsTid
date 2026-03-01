var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var receivedPayloads = new List<object>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mock-external" }));

app.MapPost("/api/external/receive", async (HttpRequest request) =>
{
    var body = await request.ReadFromJsonAsync<object>();
    receivedPayloads.Add(body!);
    Console.WriteLine($"[Mock External] Received payload #{receivedPayloads.Count}: {body}");
    return Results.Ok(new { success = true, messageId = Guid.NewGuid(), receivedAt = DateTime.UtcNow });
});

app.MapGet("/api/external/received", () => Results.Ok(receivedPayloads));

app.Run();
