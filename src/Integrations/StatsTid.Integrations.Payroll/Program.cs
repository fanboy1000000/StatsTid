using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddSingleton<IEventStore>(sp => new PostgresEventStore(sp.GetRequiredService<DbConnectionFactory>()));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PayrollMappingService>();
builder.Services.AddSingleton<PayrollExportService>();
builder.Services.AddSingleton<PeriodCalculationService>();

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

app.MapPost("/api/payroll/calculate-and-export", async (
    CalculateAndExportRequest request,
    PeriodCalculationService calculator,
    PayrollExportService export,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    // Forward auth header and correlation ID for traceability
    var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
    Guid? correlationId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var corrValues)
        && Guid.TryParse(corrValues.FirstOrDefault(), out var parsedCorr)
            ? parsedCorr
            : null;

    var result = await calculator.CalculateAsync(
        request.Profile,
        request.Entries,
        request.Absences,
        request.PeriodStart,
        request.PeriodEnd,
        request.PreviousFlexBalance,
        authHeader,
        correlationId,
        ct);

    if (!result.Success)
        return Results.UnprocessableEntity(result);

    // If calculation produced export lines, send to payroll
    if (result.ExportLines.Count > 0)
    {
        var exportResult = await export.ExportAsync(result.ExportLines, ct);
        if (!exportResult.Success)
        {
            return Results.UnprocessableEntity(new
            {
                result.EmployeeId,
                result.PeriodStart,
                result.PeriodEnd,
                result.AgreementCode,
                result.OkVersion,
                result.RuleResults,
                result.ExportLines,
                Success = false,
                ErrorMessage = "Calculation succeeded but payroll export failed",
                ExportId = exportResult.ExportId
            });
        }
    }

    return Results.Ok(result);
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

public sealed class CalculateAndExportRequest
{
    public required EmploymentProfile Profile { get; init; }
    public required List<TimeEntry> Entries { get; init; }
    public required List<AbsenceEntry> Absences { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public decimal PreviousFlexBalance { get; init; }
}
