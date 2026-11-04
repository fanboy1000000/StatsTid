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
/// S147 / TASK-14716-T fix-up (owner ruling R-G9) — <see cref="PayrollExportService.ExportAsync"/> exports exactly one
/// (employee, year, month) per call, and refuses anything else with
/// <see cref="PayrollExportService.MoreThanOneMonthMessage"/>.
///
/// <para>
/// <b>In plain language.</b> Payroll's record is one employee's one calendar month. A call that mixes months or employees (or
/// whose flex-ledger write is for a different month than its lines) is a programming error; the service refuses it before it
/// reads or writes anything, rather than quietly exporting the first month it sees.
/// </para>
///
/// <para>
/// <b>Seam.</b> As in <see cref="PayrollExportNoPeriodTests"/>, the service points at a database that does not exist (port 1).
/// The refusal comes before any connection, so a fact that got the exact exception never reached the database and wrote
/// nothing. Every call carries a valid period and (for the flex fact) a manifest id, so no other refusal can be what throws.
/// </para>
///
/// <para>
/// <b>Red conditions, all facts (kind N: they name <c>MoreThanOneMonthMessage</c>).</b> Mutation: in the one-month guard of
/// <c>ExportAsync</c>, replace <c>throw new InvalidOperationException(MoreThanOneMonthMessage);</c> with a no-op, so the call
/// proceeds with the first key. It compiles; the call then opens a connection to the unreachable database and fails with
/// <c>NpgsqlException</c>, so <c>ThrowsAsync&lt;InvalidOperationException&gt;</c> fails on the exception type.
/// </para>
/// </summary>
public sealed class PayrollExportMoreThanOneMonthTests
{
    private static PayrollExportLine Line(string employeeId, int year, int month) => new()
    {
        EmployeeId = employeeId,
        WageType = "SLS_0110",
        Hours = 7.4m,
        Amount = 1000m,
        PeriodStart = new DateOnly(year, month, 1),
        PeriodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
        OkVersion = "OK24",
        SourceTimeType = "NORMAL_HOURS",
    };

    private static PayrollExportContext ContextWith(FlexLedgerWrite? flex = null) => new()
    {
        Source = "CALCULATE_AND_EXPORT",
        PeriodId = Guid.NewGuid(),
        ActorId = "test-actor",
        ActorRole = "GlobalAdmin",
        ResolvedTargetOrgId = "ORG_X",
        FlexLedger = flex,
        ManifestId = flex is null ? null : Guid.NewGuid(),
    };

    /// <summary>(a) One employee's lines in two months (April and May) are refused. Kind N; Red conditions: see the class note.</summary>
    [Fact]
    public async Task ExportAsync_OneEmployeeTwoMonths_Refuses()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExportAsync(
            new[] { Line("emp1", 2026, 4), Line("emp1", 2026, 5) }, ContextWith()));

        Assert.Equal(PayrollExportService.MoreThanOneMonthMessage, ex.Message);
    }

    /// <summary>(b) Two employees' lines in one month are refused. Kind N; Red conditions: see the class note.</summary>
    [Fact]
    public async Task ExportAsync_TwoEmployeesOneMonth_Refuses()
    {
        var svc = BuildService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExportAsync(
            new[] { Line("emp1", 2026, 4), Line("emp2", 2026, 4) }, ContextWith()));

        Assert.Equal(PayrollExportService.MoreThanOneMonthMessage, ex.Message);
    }

    /// <summary>
    /// (c) A flex-ledger write for April with lines in May is refused (the key is the flex write's, so the May line does not
    /// belong to it). The write is built without a database: its under-lock recheck is a no-op delegate that is never
    /// called, because the throw precedes the transaction. Kind N; Red conditions: see the class note (with the throw
    /// removed the call goes on to the connection and fails with <c>NpgsqlException</c>).
    /// </summary>
    [Fact]
    public async Task ExportAsync_FlexWriteForOneMonthLinesInAnother_Refuses()
    {
        var svc = BuildService();
        var flex = new FlexLedgerWrite
        {
            EmployeeId = "emp1",
            Year = 2026,
            Month = 4,
            Provenance = "LEDGER_START",
            OpeningBalance = 0m,
            ClosingBalance = 0m,
            RecheckUnderLock = (_, _, _) => Task.CompletedTask,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExportAsync(
            new[] { Line("emp1", 2026, 5) }, ContextWith(flex)));

        Assert.Equal(PayrollExportService.MoreThanOneMonthMessage, ex.Message);
    }

    private static PayrollExportService BuildService()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var factory = new DbConnectionFactory("Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1;Command Timeout=1");
        return new PayrollExportService(
            new NoopHttpClientFactory(),
            factory,
            new PostgresEventStore(factory, new OutboxServiceContext("payroll")),
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
