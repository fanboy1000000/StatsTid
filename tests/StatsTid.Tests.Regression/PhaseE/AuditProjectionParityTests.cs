using System.Reflection;
using System.Text.RegularExpressions;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Audit;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S44f — Phase E Test #1 (Catalog <-> DI <-> EventSerializer parity).
///
/// <para>
/// Parses <c>docs/operations/audit-projection-catalog.md</c> at test time
/// and asserts the 3-way lockstep invariant per ADR-026 D7:
/// </para>
/// <list type="bullet">
///   <item><description>Test 1a: every <c>interface</c> catalog row has a
///   matching EventSerializer registration (the event_type resolves to a
///   known <c>Type</c>).</description></item>
///   <item><description>Test 1b: every <c>interface</c> catalog row has a
///   matching <c>IAuditProjectionMapper&lt;T&gt;</c> implementation in the
///   <c>StatsTid.Backend.Api</c> assembly.</description></item>
///   <item><description>Test 1c: exactly 6 <c>TBD-*</c> deferred rows exist
///   (1 cross-process + 1 unemitted + 4 adr025) — catches silent
///   additions/removals of TBD markers.</description></item>
/// </list>
///
/// <para>
/// PLAIN regression test — NOT Docker-gated (no DB, no containers).
/// </para>
/// </summary>
public sealed class AuditProjectionParityTests
{
    /// <summary>
    /// Navigate from bin/Debug/net8.0 up to the repo root to find the catalog.
    /// </summary>
    private static readonly string CatalogPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "operations", "audit-projection-catalog.md");

    /// <summary>
    /// Parsed catalog row for the 7-column markdown table.
    /// </summary>
    private record CatalogRow(
        string EventType,
        string VisibilityScope,
        string TargetOrgResolution,
        string TargetResourceId,
        string DetailsShape,
        string MapperKind,
        string SprintLanded);

    /// <summary>
    /// Parses the 7-column markdown table from the catalog file. Extracts
    /// rows from both the "New events" and "Retrofit candidates" tables.
    /// Skips header rows (lines starting with <c>|--</c>) and column-header
    /// rows (lines containing <c>event_type</c> as a header label).
    /// </summary>
    private static List<CatalogRow> ParseCatalog()
    {
        var lines = File.ReadAllLines(CatalogPath);
        var rows = new List<CatalogRow>();

        foreach (var line in lines)
        {
            // Must be a table row: starts with | and contains ` delimited event type
            if (!line.StartsWith("|")) continue;
            // Skip separator rows
            if (line.StartsWith("|--") || line.StartsWith("| --")) continue;

            var cells = line.Split('|', StringSplitOptions.None);
            // A 7-column table row has 9 parts (empty before first | and after last |)
            if (cells.Length < 9) continue;

            // cells[1] = event_type, cells[2] = visibility_scope, ...
            var eventType = cells[1].Trim().Trim('`');
            var visibilityScope = cells[2].Trim();
            var targetOrgResolution = cells[3].Trim();
            var targetResourceId = cells[4].Trim();
            var detailsShape = cells[5].Trim();
            var mapperKind = cells[6].Trim();
            var sprintLanded = cells[7].Trim();

            // Skip header rows (the column name row)
            if (eventType == "event_type" || eventType.Contains("Column")) continue;

            // Must have a non-empty event_type
            if (string.IsNullOrEmpty(eventType)) continue;

            rows.Add(new CatalogRow(
                eventType, visibilityScope, targetOrgResolution,
                targetResourceId, detailsShape, mapperKind, sprintLanded));
        }

        return rows;
    }

    /// <summary>
    /// Gets the EventSerializer's private EventTypeMap dictionary via reflection.
    /// </summary>
    private static Dictionary<string, Type> GetEventTypeMap()
    {
        var field = typeof(EventSerializer).GetField(
            "EventTypeMap", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var map = field!.GetValue(null) as Dictionary<string, Type>;
        Assert.NotNull(map);
        return map!;
    }

    /// <summary>
    /// Filters catalog rows to those with <c>mapper_kind</c> containing
    /// "interface" (not TBD-*).
    /// </summary>
    private static List<CatalogRow> GetInterfaceRows(List<CatalogRow> rows)
    {
        return rows.Where(r =>
            r.MapperKind.Contains("interface", StringComparison.OrdinalIgnoreCase) &&
            !r.MapperKind.StartsWith("**TBD", StringComparison.OrdinalIgnoreCase) &&
            !r.MapperKind.StartsWith("TBD", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Every catalog row with <c>mapper_kind = interface</c> must have a
    /// matching entry in <c>EventSerializer</c>'s known-type dictionary.
    /// Drift between the catalog and EventSerializer registration means a
    /// mapper was cataloged but the event class was never registered (or
    /// vice versa).
    /// </summary>
    [Fact]
    public void AllInterfaceCatalogRows_HaveMatchingEventSerializerEntry()
    {
        var catalog = ParseCatalog();
        var interfaceRows = GetInterfaceRows(catalog);
        var eventTypeMap = GetEventTypeMap();

        Assert.NotEmpty(interfaceRows);

        var missing = new List<string>();
        foreach (var row in interfaceRows)
        {
            if (!eventTypeMap.ContainsKey(row.EventType))
                missing.Add(row.EventType);
        }

        Assert.True(missing.Count == 0,
            $"Catalog interface rows without EventSerializer registration: [{string.Join(", ", missing)}]");
    }

    /// <summary>
    /// Every catalog row with <c>mapper_kind = interface</c> must have at
    /// least one <c>IAuditProjectionMapper&lt;T&gt;</c> implementation in the
    /// <c>StatsTid.Backend.Api</c> assembly for the resolved event type.
    /// </summary>
    [Fact]
    public void AllInterfaceCatalogRows_HaveMatchingMapperImplementation()
    {
        var catalog = ParseCatalog();
        var interfaceRows = GetInterfaceRows(catalog);
        var eventTypeMap = GetEventTypeMap();

        // Reference assembly containing the mapper implementations
        var backendAssembly = typeof(StatsTid.Backend.Api.AuditMappers.OrganizationCreatedAuditMapper).Assembly;

        Assert.NotEmpty(interfaceRows);

        var missing = new List<string>();
        foreach (var row in interfaceRows)
        {
            if (!eventTypeMap.TryGetValue(row.EventType, out var eventType))
            {
                // Already caught by Test 1a; skip here to avoid cascading noise
                continue;
            }

            var closedMapperType = typeof(IAuditProjectionMapper<>).MakeGenericType(eventType);
            var implementations = backendAssembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && closedMapperType.IsAssignableFrom(t))
                .ToList();

            if (implementations.Count == 0)
                missing.Add($"{row.EventType} (no impl of IAuditProjectionMapper<{eventType.Name}>)");
        }

        Assert.True(missing.Count == 0,
            $"Catalog interface rows without mapper implementation: [{string.Join(", ", missing)}]");
    }

    /// <summary>
    /// Exactly 6 TBD-* deferred rows must exist in the catalog:
    /// 1 <c>TBD-cross-process-deferred</c>, 1 <c>TBD-defined-but-unemitted</c>,
    /// 4 <c>TBD-adr025-implementation-pending</c>. Catches silent
    /// additions/removals of TBD markers.
    /// </summary>
    [Fact]
    public void AllTbdDeferredRows_AreExplicitlyMarked()
    {
        var catalog = ParseCatalog();

        // Find rows where mapper_kind starts with TBD- (with or without bold markdown)
        var tbdRows = catalog.Where(r =>
        {
            var kind = r.MapperKind.TrimStart('*');
            return kind.StartsWith("TBD-", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        Assert.Equal(6, tbdRows.Count);

        // Verify the expected composition: 1 cross-process + 1 unemitted + 4 adr025
        var crossProcess = tbdRows.Where(r => r.MapperKind.Contains("TBD-cross-process-deferred")).ToList();
        Assert.Single(crossProcess);

        var unemitted = tbdRows.Where(r => r.MapperKind.Contains("TBD-defined-but-unemitted")).ToList();
        Assert.Single(unemitted);

        var adr025 = tbdRows.Where(r => r.MapperKind.Contains("TBD-adr025-implementation-pending")).ToList();
        Assert.Equal(4, adr025.Count);
    }
}
