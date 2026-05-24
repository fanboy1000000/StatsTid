using System.Reflection;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S44f — Phase E Test #4 (Per-class visibility scope enforcement).
///
/// <para>
/// For every catalog row with <c>mapper_kind = interface</c>: resolves the
/// event type from EventSerializer, finds the mapper implementation via
/// reflection, instantiates it, creates a minimal test event via
/// <see cref="Activator.CreateInstance(Type)"/>, invokes
/// <c>Map(event, ctx)</c>, and asserts that the returned
/// <see cref="AuditProjectionRowData.VisibilityScope"/> matches the
/// catalog's expected value.
/// </para>
///
/// <para>
/// PLAIN regression test — NOT Docker-gated (no DB, no containers).
/// </para>
/// </summary>
public sealed class AuditProjectionVisibilityTests
{
    /// <summary>
    /// Navigate from bin/Debug/net8.0 up to the repo root to find the catalog.
    /// </summary>
    private static readonly string CatalogPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "operations", "audit-projection-catalog.md");

    private record CatalogRow(string EventType, string VisibilityScope, string MapperKind);

    /// <summary>
    /// Parses the 7-column markdown table from the catalog file, extracting
    /// only the columns needed for this test.
    /// </summary>
    private static List<CatalogRow> ParseCatalog()
    {
        var lines = File.ReadAllLines(CatalogPath);
        var rows = new List<CatalogRow>();

        foreach (var line in lines)
        {
            if (!line.StartsWith("|")) continue;
            if (line.StartsWith("|--") || line.StartsWith("| --")) continue;

            var cells = line.Split('|', StringSplitOptions.None);
            if (cells.Length < 9) continue;

            var eventType = cells[1].Trim().Trim('`');
            var visibilityScope = cells[2].Trim();
            var mapperKind = cells[6].Trim();

            if (eventType == "event_type" || eventType.Contains("Column")) continue;
            if (string.IsNullOrEmpty(eventType)) continue;

            rows.Add(new CatalogRow(eventType, visibilityScope, mapperKind));
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
    /// Converts the catalog's wire-format visibility scope string to the
    /// <see cref="AuditVisibilityScope"/> enum value.
    /// </summary>
    private static AuditVisibilityScope ParseVisibilityScope(string wireValue) => wireValue switch
    {
        "TENANT_TARGETED" => AuditVisibilityScope.TenantTargeted,
        "GLOBAL_TENANT_VISIBLE" => AuditVisibilityScope.GlobalTenantVisible,
        "GLOBAL_ADMIN_ONLY" => AuditVisibilityScope.GlobalAdminOnly,
        _ => throw new ArgumentException($"Unknown visibility scope: {wireValue}"),
    };

    /// <summary>
    /// Every mapper must return the <c>VisibilityScope</c> declared in the
    /// catalog. This test instantiates each mapper and event via reflection,
    /// invokes <c>Map</c>, and asserts the returned scope matches the
    /// catalog's declared value.
    ///
    /// <para>
    /// For TENANT_TARGETED events that resolve <c>target_org_id</c> from
    /// <c>context.ResolvedTargetOrgId</c> (and may throw when it is null),
    /// the test context provides a non-null sentinel value. The test only
    /// asserts <c>VisibilityScope</c>, not the target resolution itself.
    /// </para>
    /// </summary>
    [Fact]
    public void AllMappers_ReturnCorrectVisibilityScopePerCatalog()
    {
        var catalog = ParseCatalog();
        var eventTypeMap = GetEventTypeMap();
        var mapperAssemblies = new[]
        {
            typeof(StatsTid.Backend.Api.AuditMappers.OrganizationCreatedAuditMapper).Assembly,
            typeof(StatsTid.Infrastructure.AuditMappers.RetroactiveCorrectionRequestedAuditMapper).Assembly,
        };

        // Filter to interface rows only
        var interfaceRows = catalog.Where(r =>
            r.MapperKind.Contains("interface", StringComparison.OrdinalIgnoreCase) &&
            !r.MapperKind.TrimStart('*').StartsWith("TBD", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(interfaceRows);

        var failures = new List<string>();

        // Build a test context with ResolvedTargetOrgId set for TENANT_TARGETED
        // mappers that use it (some mappers throw if it is null).
        var contextWithOrg = new AuditProjectionContext(
            ActorId: "test-actor",
            ActorPrimaryOrgId: "ORG_TEST",
            CorrelationId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            ResolvedTargetOrgId: "ORG_TEST");

        foreach (var row in interfaceRows)
        {
            if (!eventTypeMap.TryGetValue(row.EventType, out var eventType))
            {
                // Already caught by Test 1a; skip to avoid cascading noise
                failures.Add($"{row.EventType}: not found in EventSerializer");
                continue;
            }

            // Find mapper implementation in Backend.Api assembly
            var closedMapperType = typeof(IAuditProjectionMapper<>).MakeGenericType(eventType);
            var mapperImplType = mapperAssemblies
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.IsClass && !t.IsAbstract && closedMapperType.IsAssignableFrom(t));

            if (mapperImplType is null)
            {
                failures.Add($"{row.EventType}: no mapper implementation found");
                continue;
            }

            try
            {
                // Instantiate mapper (parameterless ctor — all mappers are pure)
                var mapper = Activator.CreateInstance(mapperImplType);
                Assert.NotNull(mapper);

                // Create a minimal event instance (DomainEventBase has defaults
                // for EventId/OccurredAt; required string properties will be null
                // but mappers only serialize them — they don't reject null payloads
                // for the fields we're testing).
                var testEvent = Activator.CreateInstance(eventType);
                Assert.NotNull(testEvent);

                // Invoke Map via reflection (T varies per mapper)
                var mapMethod = closedMapperType.GetMethod("Map");
                Assert.NotNull(mapMethod);

                var result = (AuditProjectionRowData?)mapMethod!.Invoke(
                    mapper, new object[] { testEvent!, contextWithOrg });
                Assert.NotNull(result);

                var expectedScope = ParseVisibilityScope(row.VisibilityScope);
                if (result!.VisibilityScope != expectedScope)
                {
                    failures.Add(
                        $"{row.EventType}: expected {expectedScope} ({row.VisibilityScope}), " +
                        $"got {result.VisibilityScope} ({result.VisibilityScope.ToWireValue()})");
                }
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                failures.Add($"{row.EventType}: Map threw {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
            }
            catch (Exception ex)
            {
                failures.Add($"{row.EventType}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Visibility scope mismatches or errors ({failures.Count}):\n" +
            string.Join("\n", failures));
    }
}
