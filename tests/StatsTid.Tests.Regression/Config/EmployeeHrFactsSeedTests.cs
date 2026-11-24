using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14836-T (AC-C11e, T-BC-2, ruling 24) — the starting HR facts that <c>init.sql</c> gives the people
/// it creates, and nobody else.
///
/// <para>
/// <b>In plain language.</b> The 19 demo people each start with one HR-facts record from the beginning of time: HK
/// and PROSA people earn "overtime", AC people start with no regime recorded (their category "Standard" has no
/// merarbejde row), and every one of the 19 is on the flex scheme. A person a test (or later the product) creates
/// gets no record, even when <c>init.sql</c> is applied again to the same database: the seed names its 19 people by
/// list, so a re-applied schema never gives a row to a user a test is about to delete (which would fail with a
/// foreign-key error, 23503).
/// </para>
///
/// <para>
/// <b>Kind F (CI, Docker; CI-pending).</b> Red conditions: on <c>1cc0cb1d</c> the seed does not exist,
/// <c>employee_hr_facts</c> is empty, and the first assertion (19 rows) fails with 0. The re-apply arm also fails
/// on any seed written over every user (the test user would have a row). Product code between the readings:
/// <c>init.sql</c>, applied and re-applied.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeHrFactsSeedTests : IAsyncLifetime
{
    private static readonly string[] SeededIds =
    {
        "admin01", "admin02", "ladm01", "ladm02", "hr01", "hr02", "mgr01", "mgr02", "mgr03",
        "emp001", "emp002", "emp003", "emp004", "emp005", "emp006", "emp007", "emp008", "emp009", "emp010",
    };

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

    private sealed record Seeded(string Employee, string? Regime, bool? Flex, DateOnly From, DateOnly? To, long Version);

    [Fact]
    public async Task InitSqlUsers_SeededFromAgreementAndCategory()
    {
        var cs = _harness.ConnectionString;

        var rows = await ReadAsync(cs);
        Assert.Equal(19, rows.Count);
        Assert.Equal(SeededIds.OrderBy(i => i, StringComparer.Ordinal).ToArray(),
            rows.Select(r => r.Employee).OrderBy(i => i, StringComparer.Ordinal).ToArray());
        Assert.All(rows, r =>
        {
            Assert.Equal((DateOnly.MinValue, (DateOnly?)null, 1L, (bool?)true), (r.From, r.To, r.Version, r.Flex));
        });
        Assert.Equal("OVERTIME", Single(rows, "emp002").Regime); // HK
        Assert.Equal("OVERTIME", Single(rows, "emp003").Regime); // PROSA
        Assert.Null(Single(rows, "emp001").Regime);              // AC, category Standard: no value
        Assert.Equal(11, rows.Count(r => r.Regime == "OVERTIME"));
        Assert.Equal(8, rows.Count(r => r.Regime is null));

        // The added arm (ruling 24): a user created after the seed gets no row, even when init.sql is applied again.
        const string testUser = "usr_s148_seed_probe";
        await RegressionSeed.SeedEmployeeAsync(cs, testUser, "STY01", "HK");
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(cs);

        var after = await ReadAsync(cs);
        Assert.Equal(19, after.Count);
        Assert.DoesNotContain(after, r => r.Employee == testUser);
        Assert.Equal(SeededIds.OrderBy(i => i, StringComparer.Ordinal).ToArray(),
            after.Select(r => r.Employee).OrderBy(i => i, StringComparer.Ordinal).ToArray());

        // Clean-up: the user has no facts row, so its delete meets no 23503.
        await ExecAsync(cs, "DELETE FROM employee_hr_facts WHERE employee_id = @u", testUser);
        await ExecAsync(cs, "DELETE FROM employee_profiles WHERE employee_id = @u", testUser);
        await ExecAsync(cs, "DELETE FROM user_agreement_codes WHERE user_id = @u", testUser);
        await ExecAsync(cs, "DELETE FROM user_organisations WHERE user_id = @u", testUser);
        await ExecAsync(cs, "DELETE FROM users WHERE user_id = @u", testUser);
    }

    private static Seeded Single(IEnumerable<Seeded> rows, string employee) =>
        Assert.Single(rows, r => r.Employee == employee);

    private static async Task<IReadOnlyList<Seeded>> ReadAsync(string cs)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employee_id, overtime_regime, on_flex_scheme, effective_from, effective_to, version
            FROM employee_hr_facts ORDER BY employee_id
            """, conn);
        var list = new List<Seeded>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Seeded(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetBoolean(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                reader.GetInt64(5)));
        }
        return list;
    }

    private static async Task ExecAsync(string cs, string sql, string user)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant at every call site; the value goes
        // through a parameter, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("u", user);
        await cmd.ExecuteNonQueryAsync();
    }
}
