using System.Data.Common;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S148 T14b FU-T (5a ruling T6): when the database holding the agreement configuration cannot be read while
/// payroll works out where one configuration stops and the next starts, the month is refused with the lookup's own
/// named outcome, never a raw database error.
///
/// <para>
/// <b>Plain language.</b> Payroll promised one named answer for an unreadable configuration database: "retry later"
/// (the 503 <c>payroll-config-unavailable</c>). Before this fix, the three reads of configuration dates that cut the
/// month (agreement-config versions, position-override versions, local-profile activations) bypassed that and gave an
/// anonymous 500. Nothing is calculated wrongly either way; the fix makes the answer the named one. The route's
/// mapping of <see cref="ConfigLookupUnavailableException"/> to that 503 body is already proven by AC-C1d
/// (<c>ConfigLookupFailClosedPayrollTests</c>, Unit and Docker), so no route-level fact is repeated here.
/// </para>
///
/// <para>
/// <b>Seam.</b> The real <c>PeriodCalculationService</c> built by <see cref="EmploymentWindowPcsFixture.BuildPcs"/>
/// with exactly one date repository, over a connection to an unreachable address (the string
/// <c>PayrollExportEmptyCallTests</c> uses): the read fails with an <c>NpgsqlException</c> without a database. The
/// three arms are the three repositories; the context-day reads of the same wrapper cannot fail alone without a
/// database, because the in-period read of the same repository always runs first and fails first.
/// </para>
///
/// <para>
/// <b>Red conditions.</b> F: at <c>043c5f35</c> each arm throws the raw <c>Npgsql.NpgsqlException</c> out of the
/// calculation, so <c>Assert.ThrowsAsync&lt;ConfigLookupUnavailableException&gt;</c> fails with
/// <c>Actual: typeof(Npgsql.NpgsqlException)</c>.
/// </para>
/// </summary>
public sealed class ConfigDateReadFailureTests
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1;Command Timeout=1";

    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    [Theory]
    [InlineData("agreementConfig")]
    [InlineData("positionOverride")]
    [InlineData("localAgreementProfile")]
    public async Task FailingConfigurationDateRead_IsRefusedAsConfigLookupUnavailable_NothingCalculated(string repository)
    {
        var factory = new DbConnectionFactory(UnreachableDatabase);
        var engine = new EmploymentWindowPcsFixture.RecordingRuleEngine();
        var pcs = EmploymentWindowPcsFixture.BuildPcs(
            engine,
            agreementConfigRepo: repository == "agreementConfig" ? new AgreementConfigRepository(factory) : null,
            positionOverrideRepo: repository == "positionOverride" ? new PositionOverrideRepository(factory) : null,
            localAgreementProfileRepo: repository == "localAgreementProfile" ? new LocalAgreementProfileRepository(factory) : null);

        // A position, so the override read is asked; the profile carries an organisation, so the local-profile read is.
        var profile = EmploymentWindowPcsFixture.Profile("EMP-T6-DATES") with { Position = "T6-POSITION" };

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var thrown = await Assert.ThrowsAsync<ConfigLookupUnavailableException>(
            () => pcs.CalculateWithOutcomeAsync(profile, Mar01, Mar31));
#pragma warning restore CS0618

        Assert.IsAssignableFrom<DbException>(thrown.InnerException);
        Assert.Empty(engine.Calls);
    }
}
