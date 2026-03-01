using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PayrollMappingService>();
builder.Services.AddSingleton<PayrollExportService>();

builder.Services.AddStatsTidJwtAuth(builder.Configuration);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payroll-integration" }));

app.MapPost("/api/payroll/export", async (PayrollExportRequest request, PayrollMappingService mapping, PayrollExportService export, CancellationToken ct) =>
{
    var lines = await mapping.MapCalculationResultAsync(request.CalculationResult, request.Profile, ct);

    if (lines.Count == 0)
        return Results.BadRequest(new { success = false, error = "No mappable line items" });

    var result = await export.ExportAsync(lines, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/payroll/export-period", async (PayrollPeriodExportRequest request, PayrollMappingService mapping, PayrollExportService export, CancellationToken ct) =>
{
    var allLines = new List<PayrollExportLine>();

    foreach (var calcResult in request.CalculationResults)
    {
        var lines = await mapping.MapCalculationResultAsync(calcResult, request.Profile, ct);
        allLines.AddRange(lines);
    }

    if (allLines.Count == 0)
        return Results.BadRequest(new { success = false, error = "No mappable line items in period" });

    var result = await export.ExportAsync(allLines, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
}).RequireAuthorization("Authenticated");

app.Run();

public sealed class PayrollExportRequest
{
    public required CalculationResult CalculationResult { get; init; }
    public required EmploymentProfile Profile { get; init; }
}

public sealed class PayrollPeriodExportRequest
{
    public required List<CalculationResult> CalculationResults { get; init; }
    public required EmploymentProfile Profile { get; init; }
}
