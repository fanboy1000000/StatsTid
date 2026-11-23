using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C1d, Unit arm; ADR-043 D12; B8 (a)) - when payroll cannot read the agreement
/// configuration it refuses by name, calls no engine and writes nothing.
///
/// <para>
/// <b>In plain language.</b> Payroll asks the configuration lookup for the rules in force before it calculates a
/// month. If the lookup says it cannot read them (the database is unreachable), payroll must not fall back to
/// values built into the code: the calculation stops with that very answer, the Rule Engine is never called and
/// no manifest is written. The route turns the answer into its fixed 503 (the CI arm of this fact, in the
/// Regression project's host-level fact).
/// </para>
///
/// <para>
/// <b>Kind: F (Unit).</b> <b>Red conditions:</b> before the stack nothing in payroll asked a lookup, so the
/// month was calculated on the compiled values: no exception is thrown and <c>Assert.ThrowsAsync</c> fails
/// ("no exception was thrown"), the engine received the timeline call and the store recorded one write. The
/// exception type is asserted exactly (<c>ThrowsAsync</c> accepts no subtype and no other type).
/// </para>
/// </summary>
public sealed class ConfigLookupFailClosedPayrollTests
{
    private const string EmployeeId = "EMP-S148-LOOKUPDOWN";

    [Fact]
    public async Task DatabaseReadFailure_Unavailable_NothingWritten()
    {
        var engine = new RecordingRuleEngine();
        var store = new InMemorySegmentManifestStore();
        var pcs = BuildPcs(engine, store, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31), configLookup: new UnreadableLookup());

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var thrown = await Assert.ThrowsAsync<ConfigLookupUnavailableException>(
            () => pcs.CalculateWithOutcomeAsync(Profile(EmployeeId), Mar01, Mar31));
#pragma warning restore CS0618

        Assert.Equal("central configuration", thrown.Layer);
        Assert.Empty(engine.Calls);
        Assert.Equal(0, store.PersistCalls);
        Assert.Empty(store.All);
    }

    /// <summary>A lookup whose database read fails, as the outcome the real lookup raises on a database error.</summary>
    private sealed class UnreadableLookup : IAgreementConfigLookup
    {
        public Task<AgreementRuleConfig> ResolveAsync(
            string orgId, string agreementCode, string okVersion, string? position, DateOnly asOf,
            CancellationToken ct = default)
            => throw new ConfigLookupUnavailableException("central configuration", new InvalidOperationException("connection refused"));

        public Task<AgreementRuleConfig> ResolveCentralAsync(
            string agreementCode, string okVersion, DateOnly asOf, CancellationToken ct = default)
            => throw new ConfigLookupUnavailableException("central configuration", new InvalidOperationException("connection refused"));
    }
}
