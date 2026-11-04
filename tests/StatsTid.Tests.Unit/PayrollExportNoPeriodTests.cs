using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S147 / TASK-14716-T (SEC-054 step 2; AC-054-5 / P-4) — NON-Docker unit pins for the rule that
/// <see cref="PayrollExportService.ExportAsync"/> requires an approval period.
///
/// <para>
/// <b>In plain language.</b> Until S147 the service exported a month without checking that a second person had
/// approved it, whenever the caller named no approval period (that is what the two raw payroll routes did). Those routes are
/// gone; the service now treats "no period" as a programming error and refuses before it touches the database, so
/// every export record comes from an approved month.
/// </para>
///
/// <para>
/// <b>Why this is a Unit fact.</b> The refusal is thrown BEFORE the transaction opens (<c>PayrollExportService.cs</c>,
/// after the settlement guard and the month-spanning check), so no connection is ever made. The service here is wired,
/// as in <see cref="SettlementLineDeliveryGuardTests"/>, to a database that does not exist (port 1): a fact that never
/// reached the database has written nothing, by construction. If the refusal were ever moved behind the connection, the
/// call would fail with a connection error instead and these facts would fail on the exception type.
/// </para>
/// </summary>
public sealed class PayrollExportNoPeriodTests
{
    private const string UnreachableDb =
        "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1;Command Timeout=1";

    private static PayrollExportLine NormalLine() => new()
    {
        EmployeeId = "emp1",
        WageType = "SLS_0110",
        Hours = 7.4m,
        Amount = 1234.56m,
        PeriodStart = new DateOnly(2026, 1, 1),
        PeriodEnd = new DateOnly(2026, 1, 31),
        OkVersion = "OK24",
        SourceTimeType = "NORMAL_HOURS",
    };

    private static PayrollExportContext ContextWith(Guid? periodId) => new()
    {
        Source = "EXPORT",
        PeriodId = periodId,
        ActorId = "test-actor",
        ActorRole = "GlobalAdmin",
        ResolvedTargetOrgId = "ORG_X",
    };

    /// <summary>
    /// AC-054-5 (P-4), kind N (names <c>PayrollExportService.NoPeriodMessage</c>, which this sprint creates). An export
    /// context with <c>PeriodId = null</c> throws <see cref="InvalidOperationException"/> carrying exactly
    /// <see cref="PayrollExportService.NoPeriodMessage"/>. Nothing was written: the throw precedes any connection, and
    /// the database this service points at does not exist, so a fact that returned this exception never reached it.
    ///
    /// <para><b>Red conditions (mutation M-13, "drop the null-period check").</b> Replace the throw in
    /// <c>PayrollExportService.ExportAsync</c> (the <c>if (context.PeriodId is not { } periodId || periodId == Guid.Empty)</c>
    /// block) with <c>var periodId = context.PeriodId.GetValueOrDefault();</c>. The API is unchanged and the file compiles;
    /// the call now opens a connection to the unreachable database and fails with a connection exception, so
    /// <c>ThrowsAsync&lt;InvalidOperationException&gt;</c> fails on the exception type.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_NullPeriod_Refuses()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { NormalLine() }, ContextWith(null)));

        Assert.Equal(PayrollExportService.NoPeriodMessage, ex.Message);
    }

    /// <summary>
    /// AC-054-5 (P-4), kind N. The same refusal for an EMPTY period id (<see cref="Guid.Empty"/>, the "no period" meaning
    /// the manifest id also gets). Same docstring, same mutation: under M-13 the empty id is no longer refused, the call
    /// reaches the unreachable database, and <c>ThrowsAsync&lt;InvalidOperationException&gt;</c> fails on the exception type.
    /// </summary>
    [Fact]
    public async Task ExportAsync_EmptyGuidPeriod_Refuses()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { NormalLine() }, ContextWith(Guid.Empty)));

        Assert.Equal(PayrollExportService.NoPeriodMessage, ex.Message);
    }

    private static PayrollExportService BuildService()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var factory = new DbConnectionFactory(UnreachableDb);
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        return new PayrollExportService(
            new NoopHttpClientFactory(),
            factory,
            eventStore,
            new AuditProjectionRepository(factory),
            new PayrollExportGeneratedAuditMapper(),
            configuration,
            NullLogger<PayrollExportService>.Instance);
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
