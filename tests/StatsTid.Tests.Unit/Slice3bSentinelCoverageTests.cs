using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S71 / TASK-7105 (SPRINT-71 R11) — sentinel coverage for the NEW §26 placeholder lønart
/// <c>SLS_TBD_S26</c> + the unwired-delivery census assertion. R11 pins that the deliverable is
/// coverage TESTS, not new guard code: the EXISTING <c>SLS_TBD_</c> prefix refusal in
/// <see cref="PayrollExportService"/> (the single outbound delivery point, before the
/// <c>outbox_messages</c> INSERT) already rejects every sentinel unconditionally — these tests
/// prove it holds for the §26 lønart exactly as <see cref="SettlementLineDeliveryGuardTests"/>
/// proved it for <c>SLS_TBD_S24</c>, and the census test proves (the S69 "delivery-unwired"
/// proof, source-level) that NO Payroll delivery path reads the staged
/// <c>settlement_export_lines</c> at all.
/// </summary>
public sealed class Slice3bSentinelCoverageTests
{
    private const string GuardMessageFragment = "Settlement export line delivery is disabled";

    // The §26 staged-line shape: the placeholder sentinel lønart + the NEW time_type. NOTE the
    // SourceTimeType is VACATION_TERMINATION_PAYOUT — NOT the §24 VACATION_SETTLEMENT_PAYOUT the
    // guard's secondary discriminator matches — so the SLS_TBD_ prefix is the LOAD-BEARING lock
    // here (exactly the R11 claim under test).
    private static PayrollExportLine S26SentinelLine() => new()
    {
        EmployeeId = "emp1",
        WageType = "SLS_TBD_S26",
        Hours = 12.5m,
        Amount = 0m,
        PeriodStart = new DateOnly(2026, 3, 31),
        PeriodEnd = new DateOnly(2026, 3, 31),
        OkVersion = "OK24",
        SourceTimeType = "VACATION_TERMINATION_PAYOUT",
    };

    // ════════════════════════════════════════════════════════════════════════
    // (1) The SLS_TBD_S26 sentinel is refused at the outbound point even with delivery ENABLED.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task S26SentinelLine_DeliveryEnabled_StillThrows()
    {
        var svc = BuildService(deliveryEnabled: true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { S26SentinelLine() }));
        Assert.Contains(GuardMessageFragment, ex.Message);
        Assert.Contains("SLS_TBD_S26", ex.Message); // the sentinel-specific (unconditional) branch
    }

    // ════════════════════════════════════════════════════════════════════════
    // (2) …and with the delivery config absent (the fail-closed default).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task S26SentinelLine_DeliveryConfigAbsent_Throws()
    {
        var svc = BuildService(deliveryEnabled: null); // config key absent ⇒ disabled
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { S26SentinelLine() }));
        Assert.Contains(GuardMessageFragment, ex.Message);
        Assert.Contains("SLS_TBD_S26", ex.Message);
    }

    // ════════════════════════════════════════════════════════════════════════
    // (3) The unwired-delivery CENSUS (the S69 proof, asserted at source level): the ONLY Payroll
    //     source file that touches the staged settlement_export_lines is the staging repository —
    //     no delivery path (PayrollExportService / SlsExportFormatter / the endpoints) reads it,
    //     so a staged line CANNOT reach the outbound boundary through any wired path, independent
    //     of the D13 gate and the sentinel guard.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnwiredDeliveryCensus_OnlyTheStagingRepository_AccessesSettlementExportLines()
    {
        var payrollSrc = Path.Combine(LocateRepoRoot(), "src", "Integrations", "StatsTid.Integrations.Payroll");
        Assert.True(Directory.Exists(payrollSrc), $"Payroll source directory not found at '{payrollSrc}'.");

        // SQL ACCESS sites (FROM/INTO/UPDATE/DELETE on the staged-line table) — not mere mentions
        // (the emitter's collision error MESSAGES legitimately name the table; they read nothing).
        string[] accessShapes =
        {
            "FROM settlement_export_lines",
            "INTO settlement_export_lines",
            "UPDATE settlement_export_lines",
            "DELETE FROM settlement_export_lines",
        };

        var accessing = Directory.EnumerateFiles(payrollSrc, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f =>
            {
                var src = File.ReadAllText(f);
                return accessShapes.Any(shape => src.Contains(shape, StringComparison.OrdinalIgnoreCase));
            })
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // The staging seam is the SOLE SQL reader/writer. If this set ever grows, the new
        // touchpoint must be census-reviewed against the unwired-delivery invariant
        // (ADR-033 slice 1b/3b) — in particular, NO delivery path may gain a staged-line read.
        Assert.Equal(new[] { "SettlementInboxLineRepository.cs" }, accessing!);
    }

    /// <summary>The companion census direction: the outbound delivery point itself
    /// (<see cref="PayrollExportService"/>) carries the <c>SLS_TBD_</c> prefix guard and never
    /// references the staged-line table — pinned textually so a refactor that moves or weakens the
    /// prefix refusal fails THIS test, not just the behavioral ones above.</summary>
    [Fact]
    public void UnwiredDeliveryCensus_OutboundPoint_HasPrefixGuard_AndNoStagedLineRead()
    {
        var exportServicePath = Path.Combine(
            LocateRepoRoot(), "src", "Integrations", "StatsTid.Integrations.Payroll",
            "Services", "PayrollExportService.cs");
        var source = File.ReadAllText(exportServicePath);

        Assert.Contains("SLS_TBD_", source, StringComparison.Ordinal);          // the prefix sentinel lives here
        Assert.DoesNotContain("settlement_export_lines", source, StringComparison.Ordinal); // and it never reads staged lines
    }

    // ─────────────────────────────── construction ───────────────────────────────

    private static PayrollExportService BuildService(
        bool? deliveryEnabled,
        string connectionString = "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1;Command Timeout=1")
    {
        var cfgItems = new Dictionary<string, string?>();
        if (deliveryEnabled is not null)
            cfgItems["Settlement:LineDeliveryEnabled"] = deliveryEnabled.Value ? "true" : "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfgItems).Build();

        return new PayrollExportService(
            new NoopHttpClientFactory(),
            new DbConnectionFactory(connectionString),
            configuration,
            NullLogger<PayrollExportService>.Instance);
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker", "postgres", "init.sql")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from AppContext.BaseDirectory='{AppContext.BaseDirectory}'.");
    }

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoopHandler());
        private sealed class NoopHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
