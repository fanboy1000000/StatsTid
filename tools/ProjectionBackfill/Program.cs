// =============================================================================
// S27 / TASK-2705 — One-shot Projection Backfill (CLI wrapper)
// =============================================================================
//
// Thin CLI wrapper around the canonical
// `StatsTid.Infrastructure.ProjectionBackfillService`. The SQL + tx + replay
// logic lives in the service so it can ALSO be invoked from Backend.Api
// startup (S27 Step 7a cycle 1 BLOCKER fix) and from the regression test
// suite, with a single source of truth.
//
// CLI:
//   dotnet run --project tools/ProjectionBackfill -- --connection "<connstr>"
// Or env var:
//   POSTGRES_CONNECTION_STRING="Host=...;..." dotnet run --project tools/ProjectionBackfill
//
// Exit codes:
//   0  success
//   2  missing connection string
// (any unhandled exception bubbles up with the default non-zero exit code)

using Microsoft.Extensions.Logging;
using StatsTid.Infrastructure;

string? connStr = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--connection")
    {
        connStr = args[i + 1];
        break;
    }
}
connStr ??= Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

if (string.IsNullOrWhiteSpace(connStr))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/ProjectionBackfill -- --connection <postgres-conn-string>");
    Console.Error.WriteLine("       (or set POSTGRES_CONNECTION_STRING env var)");
    return 2;
}

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    }));

var logger = loggerFactory.CreateLogger<ProjectionBackfillService>();
var service = new ProjectionBackfillService(new DbConnectionFactory(connStr), logger);
var result = await service.RunAsync();

Console.WriteLine($"Scanned: {result.Scanned}");
Console.WriteLine($"time_entries_projection: inserted={result.InsertedTime}, conflicts={result.ConflictsTime}");
Console.WriteLine($"absences_projection: inserted={result.InsertedAbsences}, conflicts={result.ConflictsAbsences}");
Console.WriteLine($"stream_version fallback warnings: {result.FallbackWarnings}");
if (result.UnknownEventTypes > 0)
{
    Console.WriteLine($"unknown event types skipped: {result.UnknownEventTypes}");
}
return 0;
