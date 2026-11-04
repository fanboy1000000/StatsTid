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
/// S147 / TASK-14716-T fix-up (owner ruling R-G9) — a call with nothing to export does not touch the database.
/// The service is wired to a database that does not exist (port 1), as in <see cref="SettlementLineDeliveryGuardTests"/>:
/// a connection attempt would throw, so a returned result proves none was made.
/// </summary>
public sealed class PayrollExportEmptyCallTests
{
    /// <summary>
    /// R-G9, kind F (unit). No lines, no flex-ledger write and a valid period return <c>Success = true</c> with an empty
    /// <c>ExportId</c>, and no connection is attempted.
    ///
    /// <para><b>Red conditions.</b> On master <c>19c1e6a1</c> <c>ExportAsync</c> opens a connection and a transaction
    /// even for an empty call, so against the unreachable database it throws
    /// <c>NpgsqlException: Failed to connect to 127.0.0.1:1</c> and never returns a result. Any change that reaches the
    /// database before the early return fails it the same way.</para>
    /// </summary>
    [Fact]
    public async Task ExportAsync_NoLinesNoFlexWrite_ReturnsSuccessWithEmptyExportId_WithoutConnecting()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var factory = new DbConnectionFactory("Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1;Command Timeout=1");
        var svc = new PayrollExportService(
            new NoopHttpClientFactory(),
            factory,
            new PostgresEventStore(factory, new OutboxServiceContext("payroll")),
            new AuditProjectionRepository(factory),
            new PayrollExportGeneratedAuditMapper(),
            configuration,
            NullLogger<PayrollExportService>.Instance);

        var result = await svc.ExportAsync(
            Array.Empty<PayrollExportLine>(),
            new PayrollExportContext
            {
                Source = "CALCULATE_AND_EXPORT",
                PeriodId = Guid.NewGuid(),
                ActorId = "test-actor",
                ActorRole = "GlobalAdmin",
                ResolvedTargetOrgId = "ORG_X",
            });

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.ExportId);
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
