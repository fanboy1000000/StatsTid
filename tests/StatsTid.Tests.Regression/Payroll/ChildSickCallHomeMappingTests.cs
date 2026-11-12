using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14811-T (QUAL-236), AC-S0-2e's mapping rows — every agreement has a payroll mapping for the child's
/// call-home day. In plain terms: payroll turns each kind of hours into a wage code through the
/// <c>wage_type_mappings</c> table, and a line with no mapping row is skipped with only a log warning
/// (<c>PeriodCalculationService.cs:3956-3963</c>). So if one agreement lacked its row, that agreement's call-home hours
/// would go unpaid without any export failing. From 1 April 2026 (OK26) the call-home hours are exported under
/// <c>SLS_0530</c> (provisional, the code the child's first sick day uses), so each of the five agreements needs an OK26
/// row.
///
/// <para>Docker-gated, <b>CI-pending</b> (CI-W2); never run locally. The schema is the SHIPPED <c>docker/postgres/init.sql</c>
/// applied whole (the shape of <c>WageTypeMappingRegressionTests</c>), so the seeds are exactly the shipped ones.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ChildSickCallHomeMappingTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// For each of AC, HK, PROSA, AC_RESEARCH and AC_TEACHING, exactly one open <c>wage_type_mappings</c> row maps
    /// <c>CHILD_SICK_CALL_HOME</c> under OK26 to <c>SLS_0530</c> (the generic, position-less row).
    ///
    /// <para><b>Kind F.</b> Red conditions: the per-agreement row assertion. The base has no call-home mapping row, so
    /// every agreement reads zero rows. Under mutation M-23 (the five rows dropped; CI-M2b) the same assertion fails for
    /// every agreement.</para>
    /// </summary>
    [Fact]
    public async Task EveryAgreement_HasAnOk26Sls0530Row()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        foreach (var agreement in new[] { "AC", "HK", "PROSA", "AC_RESEARCH", "AC_TEACHING" })
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT wage_type FROM wage_type_mappings
                WHERE time_type = 'CHILD_SICK_CALL_HOME' AND ok_version = 'OK26'
                  AND agreement_code = @a AND position = '' AND effective_to IS NULL
                """, conn);
            cmd.Parameters.AddWithValue("a", agreement);
            var wageTypes = new List<string>();
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    wageTypes.Add(r.GetString(0));

            Assert.True(
                wageTypes.SequenceEqual(new[] { "SLS_0530" }),
                $"{agreement}/OK26: expected exactly one open CHILD_SICK_CALL_HOME mapping, to SLS_0530; found [{string.Join(", ", wageTypes)}]");
        }
    }
}
