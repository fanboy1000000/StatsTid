using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Organisation;

/// <summary>
/// S148 / TASK-14820-T (AC-C13e; F (CI)) — the dated read: the resolver answers a day's organisation from
/// the person's organisation history, never from today's.
///
/// <para>
/// <b>In plain language.</b> If Ida was in STY01 until 15 June and in STY02 from then on, a lookup for
/// Friday 12 June must say STY01 and one for Monday 15 June STY02. If a person has a profile for a day but
/// no organisation covers that day, the lookup fails loudly (an exception) instead of guessing; it must not
/// quietly say "no profile", and it must not paste today's organisation onto a past day.
/// </para>
///
/// <para>
/// <b>Fixture.</b> A raw schema, no host (<c>EmploymentProfileResolverDateOverlayTests</c>'s harness):
/// <see cref="RegressionSeed"/> makes each person, then the organisation rows are replaced by the exact
/// history the fact needs. Dates are written through Npgsql <see cref="DateOnly"/> parameters and read
/// back in C#. The resolver opens its own connection and has no seam, which is why this is a Docker fact
/// (BC-1, ruled 1.4). CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentProfileResolverOrganisationTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private EmploymentProfileResolver _resolver = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _resolver = new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory));
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>F.</b> Person A: profile and agreement code from the start of history; organisation rows STY01
    /// [0001-01-01, 2026-06-15) and STY02 [2026-06-15, open); the live column says STY02. The resolver at
    /// Friday 12 June 2026 returns <c>OrgId</c> STY01 and at Monday 15 June STY02. Person B: a profile from
    /// the start of history but ONE organisation row starting 2026-06-15; the resolver at 12 June throws
    /// <see cref="EmployeeProfileNotFoundException"/> (it does not return null).
    /// <para><b>Red conditions.</b> Before C-13 the resolver reads the live column: A's 12 June answers
    /// STY02, so <c>Assert.Equal("STY01", ...)</c> fails; and B's 12 June returns a profile (the live
    /// column is set) instead of throwing, so <c>Assert.ThrowsAsync</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task TwoDatedRows_EachDaysOrganisation_ThrowsWhenUncovered()
    {
        var a = "usr_s148_res_a_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var b = "usr_s148_res_b_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var june12 = new DateOnly(2026, 6, 12);
        var june15 = new DateOnly(2026, 6, 15);

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(conn, a, "STY02");
            await RegressionSeed.SeedEmployeeAsync(conn, b, "STY02");
            await ReplaceOrganisationRowsAsync(conn, a,
                (DateOnly.MinValue, june15, "STY01"), (june15, null, "STY02"));
            await ReplaceOrganisationRowsAsync(conn, b, (june15, null, "STY02"));
        }

        var onTwelfth = await _resolver.GetByEmployeeIdAtAsync(a, june12);
        Assert.NotNull(onTwelfth);
        Assert.Equal("STY01", onTwelfth!.OrgId);

        var onFifteenth = await _resolver.GetByEmployeeIdAtAsync(a, june15);
        Assert.NotNull(onFifteenth);
        Assert.Equal("STY02", onFifteenth!.OrgId);

        await Assert.ThrowsAsync<EmployeeProfileNotFoundException>(
            () => _resolver.GetByEmployeeIdAtAsync(b, june12));
    }

    private static async Task ReplaceOrganisationRowsAsync(
        NpgsqlConnection conn, string userId, params (DateOnly From, DateOnly? To, string OrgId)[] rows)
    {
        await using (var del = new NpgsqlCommand("DELETE FROM user_organisations WHERE user_id = @u", conn))
        {
            del.Parameters.AddWithValue("u", userId);
            await del.ExecuteNonQueryAsync();
        }
        var version = 1L;
        foreach (var row in rows)
        {
            await using var ins = new NpgsqlCommand(
                """
                INSERT INTO user_organisations (assignment_id, user_id, org_id, effective_from, effective_to, version)
                VALUES (gen_random_uuid(), @u, @o, @from, @to, @v)
                """, conn);
            ins.Parameters.AddWithValue("u", userId);
            ins.Parameters.AddWithValue("o", row.OrgId);
            ins.Parameters.AddWithValue("from", row.From);
            ins.Parameters.AddWithValue("to", (object?)row.To ?? DBNull.Value);
            ins.Parameters.AddWithValue("v", version++);
            await ins.ExecuteNonQueryAsync();
        }
    }
}
