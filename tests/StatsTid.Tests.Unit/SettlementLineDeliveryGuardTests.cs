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
/// S69 / TASK-6907 (ADR-033 slice 1b, Step-0b B3 / TASK-6906) — NON-Docker unit tests for the
/// fail-closed OUTBOUND delivery guard in
/// <see cref="StatsTid.Integrations.Payroll.Services.PayrollExportService"/>
/// (<c>GuardSettlementLineDelivery</c>). The guard runs at the START of <c>ExportAsync</c>, BEFORE the
/// <c>outbox_messages</c> INSERT (the outbound boundary) and BEFORE any HTTP — so a refusal throws
/// synchronously with NO database touched, which is what makes these tests DB-free.
///
/// <para>
/// Two independent locks (W5): (1) settlement-line external delivery is disabled and FAIL-CLOSED — a
/// settlement line cannot enter the outbox unless <c>Settlement:LineDeliveryEnabled</c> is exactly
/// "true" (absent ⇒ disabled); (2) a placeholder sentinel lønart (<c>SLS_TBD_*</c>) is refused
/// UNCONDITIONALLY, even if delivery were enabled. The settlement-line discriminator is the line's
/// DATA (a sentinel <c>WageType</c> prefix OR the §24 <c>SourceTimeType</c>), NOT a caller-supplied
/// flag — so a caller cannot bypass it. Mixed batches fail the WHOLE call (so normal lines in the same
/// batch are NOT delivered either); a batch of only non-settlement lines passes the guard untouched.
/// </para>
/// </summary>
public sealed class SettlementLineDeliveryGuardTests
{
    private const string GuardMessageFragment = "Settlement export line delivery is disabled";

    // S90 / TASK-9002: ExportAsync now takes a PayrollExportContext. The guard runs first, so
    // its content is irrelevant to these guard tests; a null PeriodId keeps the (unreachable here)
    // B2 APPROVED re-check out of the picture.
    private static PayrollExportContext ExportContext() =>
        new() { Source = "EXPORT", PeriodId = null, ActorId = "test-actor", ResolvedTargetOrgId = "ORG_X" };

    // A normal (non-settlement) line — the guard is a no-op for it.
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

    // A settlement line carrying the placeholder sentinel lønart (the staged §24 line's shape).
    private static PayrollExportLine SentinelLine() => new()
    {
        EmployeeId = "emp1",
        WageType = "SLS_TBD_S24",
        Hours = 5m,
        Amount = 0m,
        PeriodStart = new DateOnly(2025, 12, 31),
        PeriodEnd = new DateOnly(2025, 12, 31),
        OkVersion = "OK24",
        SourceTimeType = "VACATION_SETTLEMENT_PAYOUT",
    };

    // A NON-sentinel settlement line (a future real lønart) — identified by the §24 SourceTimeType.
    private static PayrollExportLine NonSentinelSettlementLine() => new()
    {
        EmployeeId = "emp1",
        WageType = "SLS_9999_REAL",
        Hours = 5m,
        Amount = 0m,
        PeriodStart = new DateOnly(2025, 12, 31),
        PeriodEnd = new DateOnly(2025, 12, 31),
        OkVersion = "OK24",
        SourceTimeType = "VACATION_SETTLEMENT_PAYOUT",
    };

    // ════════════════════════════════════════════════════════════════════════
    // (a) A sentinel line is refused even with delivery ENABLED (unconditional sentinel lock).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SentinelLine_DeliveryEnabled_StillThrows()
    {
        var svc = BuildService(deliveryEnabled: true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { SentinelLine() }, ExportContext()));
        Assert.Contains(GuardMessageFragment, ex.Message);
        Assert.Contains("SLS_TBD_S24", ex.Message); // the sentinel-specific branch
    }

    [Fact]
    public async Task SentinelLine_DeliveryConfigAbsent_Throws()
    {
        var svc = BuildService(deliveryEnabled: null); // config key absent ⇒ disabled
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { SentinelLine() }, ExportContext()));
        Assert.Contains(GuardMessageFragment, ex.Message);
    }

    // ════════════════════════════════════════════════════════════════════════
    // (b) A NON-sentinel settlement line is gated by config, fail-closed (absent ⇒ disabled).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonSentinelSettlementLine_ConfigAbsent_Throws_FailClosed()
    {
        var svc = BuildService(deliveryEnabled: null); // absent ⇒ disabled (fail-closed default)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { NonSentinelSettlementLine() }, ExportContext()));
        Assert.Contains(GuardMessageFragment, ex.Message);
    }

    [Fact]
    public async Task NonSentinelSettlementLine_DeliveryExplicitlyFalse_Throws()
    {
        var svc = BuildService(deliveryEnabled: false); // not "true" ⇒ disabled
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { NonSentinelSettlementLine() }, ExportContext()));
    }

    // ════════════════════════════════════════════════════════════════════════
    // (c) The §24 time-type discriminator is not caller-bypassable (a non-sentinel wage_type still
    //     gets caught via SourceTimeType).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SettlementLine_IdentifiedByTimeType_NotJustWageType()
    {
        // WageType is NOT a sentinel, but SourceTimeType IS the §24 settlement type ⇒ still a settlement
        // line ⇒ disabled by default. Proves the discriminator is the line's data, not a caller flag.
        var svc = BuildService(deliveryEnabled: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { NonSentinelSettlementLine() }, ExportContext()));
        Assert.Contains(GuardMessageFragment, ex.Message);
    }

    // ════════════════════════════════════════════════════════════════════════
    // (d) Mixed batch (normal + a sentinel) ⇒ the WHOLE call throws (normal lines NOT delivered either).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MixedBatch_NormalPlusSentinel_ThrowsForWholeBatch()
    {
        var svc = BuildService(deliveryEnabled: false);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ExportAsync(new[] { NormalLine(), SentinelLine() }, ExportContext()));
        Assert.Contains(GuardMessageFragment, ex.Message);
        // (The throw happens before WriteToOutboxAsync, so neither line reaches the outbox — verified
        // DB-side in the Docker companion ReconcileEmitterMutualExclusionTests / emitter suite; here we
        // assert the call fails as a unit so the normal line is not separately delivered.)
    }

    // ════════════════════════════════════════════════════════════════════════
    // (e) A batch of ONLY non-settlement lines passes the guard untouched (guard is a no-op). We prove
    //     the guard did NOT block it by asserting the failure (DB unreachable) is NOT the guard's throw.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonSettlementOnlyBatch_GuardIsNoOp_DoesNotThrowGuard()
    {
        // DB points nowhere; if the guard let the batch through (correct), ExportAsync proceeds to the
        // outbox write and fails with a CONNECTION error — NOT the guard's InvalidOperationException.
        var svc = BuildService(deliveryEnabled: null, connectionString: "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1;Command Timeout=1");

        var ex = await Record.ExceptionAsync(() => svc.ExportAsync(new[] { NormalLine() }, ExportContext()));
        Assert.NotNull(ex); // it fails at the DB (proves the guard did not short-circuit it)
        Assert.False(
            ex is InvalidOperationException ioe && ioe.Message.Contains(GuardMessageFragment),
            $"The guard must be a no-op for a non-settlement-only batch, but it threw the guard exception: {ex.Message}");
    }

    // ─────────────────────────────── construction ───────────────────────────────

    private static PayrollExportService BuildService(
        bool? deliveryEnabled, string connectionString = "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1;Command Timeout=1")
    {
        var cfgItems = new Dictionary<string, string?>();
        if (deliveryEnabled is not null)
            cfgItems["Settlement:LineDeliveryEnabled"] = deliveryEnabled.Value ? "true" : "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfgItems).Build();

        // S90 / TASK-9002: the atomic refactor added 3 ctor deps. None is reached by the guard
        // tests (the guard throws first), so a real PostgresEventStore / repository / mapper wired
        // to the (unreachable) bad connection string is fine — they are never invoked.
        var factory = new DbConnectionFactory(connectionString);
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

    /// <summary>An IHttpClientFactory whose client never actually sends (the guard throws before any
    /// HTTP is attempted in the disabled paths; this is here only to satisfy the constructor).</summary>
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
