// =============================================================================
// S27 / TASK-2705 — One-shot Projection Backfill (CLI wrapper)
// S43 / TASK-4304 — Extended with --target audit_projection flag for ADR-026
// =============================================================================
//
// Thin CLI wrapper around the canonical projection backfill services. SQL +
// tx + replay logic lives in the services (`ProjectionBackfillService` for
// time/absences, `AuditProjectionBackfillService` for audit) so they can ALSO
// be invoked from Backend.Api startup and from regression tests, with a
// single source of truth.
//
// CLI:
//   dotnet run --project tools/ProjectionBackfill -- --connection "<connstr>"
//   dotnet run --project tools/ProjectionBackfill -- --connection "<connstr>" --target audit_projection
//   dotnet run --project tools/ProjectionBackfill -- --connection "<connstr>" --target all
// Or env var:
//   POSTGRES_CONNECTION_STRING="Host=...;..." dotnet run --project tools/ProjectionBackfill
//
// --target options:
//   time_absences (default)  — backfill time_entries_projection +
//                              absences_projection (S27 pattern)
//   audit_projection         — backfill audit_projection (S43 / ADR-026 D7);
//                              Sub-Sprint 1 has no registered mappers so all
//                              events count as `NoMapper` (Sub-Sprint 2 fills)
//   all                      — run both targets sequentially
//
// Exit codes:
//   0  success
//   2  missing connection string
//   3  unknown --target value

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Audit;
using StatsTid.SharedKernel.Audit;

string? connStr = null;
string target = "time_absences";
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--connection" && i + 1 < args.Length)
    {
        connStr = args[i + 1];
    }
    else if (args[i] == "--target" && i + 1 < args.Length)
    {
        target = args[i + 1];
    }
}
connStr ??= Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/ProjectionBackfill -- --connection <postgres-conn-string> [--target time_absences|audit_projection|all]");
    Console.Error.WriteLine("       (or set POSTGRES_CONNECTION_STRING env var)");
    return 2;
}

if (target != "time_absences" && target != "audit_projection" && target != "all")
{
    Console.Error.WriteLine($"Unknown --target value '{target}'. Valid: time_absences, audit_projection, all.");
    return 3;
}

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    }));

var dbFactory = new DbConnectionFactory(connStr);

if (target == "time_absences" || target == "all")
{
    var logger = loggerFactory.CreateLogger<ProjectionBackfillService>();
    var service = new ProjectionBackfillService(dbFactory, logger);
    var result = await service.RunAsync();
    Console.WriteLine($"Scanned: {result.Scanned}");
    Console.WriteLine($"time_entries_projection: inserted={result.InsertedTime}, conflicts={result.ConflictsTime}");
    Console.WriteLine($"absences_projection: inserted={result.InsertedAbsences}, conflicts={result.ConflictsAbsences}");
    Console.WriteLine($"stream_version fallback warnings: {result.FallbackWarnings}");
    if (result.UnknownEventTypes > 0)
    {
        Console.WriteLine($"unknown event types skipped: {result.UnknownEventTypes}");
    }
}

if (target == "audit_projection" || target == "all")
{
    // Minimal DI for audit backfill — Sub-Sprint 2 adds mapper registrations
    // alongside the endpoint cutover. For Sub-Sprint 1 the registry resolves
    // to nothing, so backfill counters all roll into NoMapper.
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddSimpleConsole());
    services.AddSingleton(dbFactory);
    services.AddSingleton<AuditProjectionRepository>();
    services.AddSingleton<IAuditProjectionMapperRegistry, AuditProjectionMapperRegistry>();
    services.AddSingleton<AuditProjectionBackfillService>();
    using var sp = services.BuildServiceProvider();
    var auditService = sp.GetRequiredService<AuditProjectionBackfillService>();
    var auditResult = await auditService.RunAsync();
    Console.WriteLine($"audit_projection: scanned={auditResult.Scanned}, inserted={auditResult.Inserted}, conflicts={auditResult.Conflicts}, noMapper={auditResult.NoMapper}, preS22Skipped={auditResult.PreS22Skipped}, unknown={auditResult.UnknownEventTypes}, errors={auditResult.DeserializationErrors}");
}

return 0;
