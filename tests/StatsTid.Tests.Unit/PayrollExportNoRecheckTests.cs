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
/// S148 / TASK-14847-T (W5, BC-5) - <see cref="PayrollExportService.ExportAsync"/> refuses a calculating context that
/// carries a flex-ledger write and a manifest id but no pay-inputs recheck.
///
/// <para>
/// <b>In plain language.</b> The calculating route's recording must check, inside its turn on the pay-inputs lock, that the
/// inputs it calculated with are still in force. A caller that forgot to hand over that check would record without it, the
/// silent path the lock exists to close. The service therefore refuses such a call by name instead of skipping the check.
/// </para>
///
/// <para>
/// <b>Seam.</b> As in <see cref="PayrollExportMoreThanOneMonthTests"/>, the service points at a database that does not exist
/// (port 1). The refusal comes before any connection, so a fact that got the exact exception never reached the database and
/// wrote nothing. The context carries a period, a flex-ledger write for the lines' month and a manifest id, so no other
/// refusal can be what throws.
/// </para>
///
/// <para>
/// <b>Kind N</b> (it names <c>PayInputsRecheck</c>, which TASK-14831 created). <b>Red conditions: M-54</b> (the guard that
/// refuses a flex-ledger write without a recheck is removed). It compiles; the call then opens a connection to the
/// unreachable database and fails with <c>NpgsqlException</c>, so <c>ThrowsAsync&lt;InvalidOperationException&gt;</c> fails on
/// the exception type.
/// </para>
/// </summary>
public sealed class PayrollExportNoRecheckTests
{
    private const string Employee = "emp-bc5";

    /// <summary>
    /// A calculating context (flex-ledger write, manifest id, period) without a <c>PayInputsRecheck</c> throws
    /// <see cref="InvalidOperationException"/> naming <c>PayInputsRecheck</c>, before any connection opens.
    /// </summary>
    [Fact]
    public async Task ExportAsync_CalculatingContextWithoutPayInputsRecheck_Refuses_NothingWritten()
    {
        var svc = BuildService();
        var line = new PayrollExportLine
        {
            EmployeeId = Employee,
            WageType = "SLS_0110",
            Hours = 7.4m,
            Amount = 1000m,
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            OkVersion = "OK24",
            SourceTimeType = "NORMAL_HOURS",
        };
        var context = new PayrollExportContext
        {
            Source = "CALCULATE_AND_EXPORT",
            PeriodId = Guid.NewGuid(),
            ActorId = "test-actor",
            ActorRole = "GlobalAdmin",
            ResolvedTargetOrgId = "ORG_X",
            ManifestId = Guid.NewGuid(),
            FlexLedger = new FlexLedgerWrite
            {
                EmployeeId = Employee,
                Year = 2026,
                Month = 3,
                Provenance = "LEDGER_START",
                OpeningBalance = 0m,
                ClosingBalance = 0m,
                RecheckUnderLock = (_, _, _) => Task.CompletedTask,
            },
            PayInputsRecheck = null,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExportAsync(new[] { line }, context));

        Assert.Contains("PayInputsRecheck", ex.Message, StringComparison.Ordinal);
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
