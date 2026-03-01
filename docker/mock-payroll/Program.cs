var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var receivedExports = new List<object>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "mock-payroll" }));

app.MapPost("/api/payroll/receive", async (HttpRequest request) =>
{
    var body = await request.ReadFromJsonAsync<object>();
    receivedExports.Add(body!);
    Console.WriteLine($"[Mock Payroll] Received export #{receivedExports.Count}: {body}");
    return Results.Ok(new { success = true, exportId = Guid.NewGuid(), receivedAt = DateTime.UtcNow });
});

app.MapGet("/api/payroll/received", () => Results.Ok(receivedExports));

app.Run();
