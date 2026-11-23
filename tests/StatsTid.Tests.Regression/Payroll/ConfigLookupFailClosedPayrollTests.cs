using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C1d, CI arm; ADR-043 D12; TASK-14827's BC-3, ruled (a)) - when payroll cannot read the
/// agreement configuration the route refuses with a fixed 503 and writes nothing.
///
/// <para>
/// <b>In plain language.</b> The Payroll host asks the configuration lookup for the rules in force. If the database the
/// lookup reads cannot be reached, payroll must not fall back to values built into the code: the month answers a fixed
/// "retry later" 503, and nothing is calculated, written or exported. (The Unit arm, in the Unit project, proves the service
/// stops and calls no engine; this arm proves the route's answer through the real host.)
/// </para>
///
/// <para>
/// <b>How the read is made to fail.</b> The Payroll host registers <c>IAgreementConfigLookup</c> as its
/// <c>ConfigResolutionService</c> singleton, so one replacement reaches every site: the REAL class built over repositories on
/// an unreachable connection (opening it throws a database error, which the lookup turns into its "unavailable" outcome).
/// Everything else runs on the fixture's real database and the real engine.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigLookupFailClosedPayrollTests : DatedPayrollFixture
{
    private const string Unreachable = "Host=localhost;Port=1;Database=na;Username=na;Password=na;Timeout=1;Command Timeout=1";
    private const string OrgId = "STY01";

    /// <summary>The ruled body, taken from TASK-14827's brief (item 7), never read back from a report.</summary>
    private const string RuledBody =
        """{"success":false,"error":"The agreement configuration could not be read, so the period could not be calculated. Nothing was calculated, written or exported. Retry when the configuration database is reachable.","kind":"payroll-config-unavailable"}""";

    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1d, CI arm. An HK full-timer with 7.4 h on every June weekday, June APPROVED; the payroll host's
    /// lookup repositories are on an unreachable connection. <c>calculate-and-export</c> June answers <b>503</b> with exactly
    /// the ruled body (the three members <c>success</c> false, <c>error</c> and <c>kind</c> <c>payroll-config-unavailable</c>,
    /// compared as JSON), and nothing is written: no <c>segment_manifests</c> row, no <c>payroll_export_records</c> row, no
    /// <c>audit_projection</c> row and no <c>outbox_events</c> row for the employee's month (the table counts equal their
    /// before-values).
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before the stack nothing in payroll read a lookup:
    /// the month was calculated on compiled values and the route answered 200 with lines (the status assertion fails).
    /// </para>
    /// </summary>
    [Fact]
    public async Task DatabaseReadFailure_Unavailable_NothingWritten()
    {
        const string employeeId = "EMP-S148-PAY-LOOKUPDOWN";
        await SeedEmployeeAsync(employeeId, OrgId, "HK");
        await SeedWeekdayHoursAsync(employeeId, "HK", Ok26, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, OrgId, "HK", Ok26, Jun01, Jun30);
        var before = await WriteCountsAsync();

        var response = await CalculateAndExportAsync(
            employeeId, "HK", Ok26, OrgId, Jun01, Jun30, configureServices: UnreadableLookup);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.Status);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(RuledBody), JsonNode.Parse(response.Json)),
            $"expected the ruled body, got: {response.Json}");
        Assert.Equal(before, await WriteCountsAsync());
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", employeeId)));
    }

    private static void UnreadableLookup(IServiceCollection services)
    {
        services.RemoveAll<ConfigResolutionService>();
        services.AddSingleton(_ =>
        {
            var broken = new DbConnectionFactory(Unreachable);
            return new ConfigResolutionService(
                new AgreementConfigRepository(broken),
                new LocalConfigurationRepository(broken),
                new PositionOverrideRepository(broken),
                new LocalAgreementProfileRepository(broken),
                NullLogger<ConfigResolutionService>.Instance);
        });
    }

    /// <summary>The row counts of every table a calculation writes to: manifests, export records, audit rows and outbox messages.</summary>
    private async Task<(long Manifests, long Exports, long Audit, long Outbox)> WriteCountsAsync() => (
        await CountAsync("SELECT COUNT(*) FROM segment_manifests"),
        await CountAsync("SELECT COUNT(*) FROM payroll_export_records"),
        await CountAsync("SELECT COUNT(*) FROM audit_projection"),
        await CountAsync("SELECT COUNT(*) FROM outbox_events"));
}
