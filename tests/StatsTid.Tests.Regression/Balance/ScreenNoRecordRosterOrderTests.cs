using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S148 T14b FU-4T (5a ruling U1, the wired arm) - Teamoversigt asks a refused configuration at most once per agreement and
/// OK version per month, whatever order the roster is computed in.
///
/// <para>
/// <b>In plain language.</b> A brand-new agreement X applies from Monday 16 March 2026. Teamoversigt lists the leader's team
/// by employee id. Person <c>a</c> held another agreement (HK) until 10 March, has no record on 11-15 March and is on X from the
/// 16th, so the first refusal of X comes on Wednesday 11 March. Person <c>b</c> is on X from the 16th with nothing before, so
/// <c>b</c>'s first weekday, 2 March, asks X too. Nothing in force on 11 March means nothing in force on 2 March, so the
/// lookup must be refused exactly once for the month. Both people's month is 22 weekdays x 7.40 = <b>162.80</b>.
/// </para>
///
/// <para>
/// <b>Seam.</b> The test host's <see cref="IAgreementConfigLookup"/> is replaced by a decorator around the real
/// <see cref="ConfigResolutionService"/> that counts the refusals of X; no production change. The roster is ordered by
/// <c>u.user_id</c> (<c>ApprovalPeriodRepository.GetTeamOverviewRosterAsync</c>, <c>ORDER BY u.user_id</c>), so <c>t4t_a</c> is
/// computed before <c>t4t_b</c>.
/// </para>
///
/// <para>Docker-gated: CI-verified, never locally green. A run on a throwaway local PostgreSQL 16 cluster is a pre-check only.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScreenNoRecordRosterOrderTests : ScreenNormFixture
{
    private const string Org = "STY02";

    /// <summary>
    /// <b>Kind N (CI).</b> One month computed, the later-first-gap person first: exactly one refusal of X, and both figures 162.80.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending). At <c>e1787c56</c> the memo covered a refusal on d only from d onward:
    /// <c>t4t_a</c>'s refusal on 11 March was remembered through 15 March only, so <c>t4t_b</c>'s 2 March asked again and the count
    /// is 2. N: the F proof is the Unit fact in <c>ComputeMonthNormMemoBoundTests</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Teamoversigt_LaterFirstGapPersonFirst_RefusesTheNewAgreementOnce()
    {
        var refusals = new RefusalCounter();
        var host = Factory.WithFixedToday(Clock).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IAgreementConfigLookup>(sp =>
                new CountingLookup(sp.GetRequiredService<ConfigResolutionService>(), refusals))));
        _ = host.CreateClient();

        var code = UniqueCode("T4T");
        refusals.Code = code;
        using var admin = AdminAt(Clock);
        await CreateAndPublishAsync(admin, ConfigBody(code, "OK24"), "2026-03-16");
        var leader = await SeedLeaderAsync(host, "t4t_lead", Org);
        await SeedRosterMemberAsync("t4t_a", Org, "t4t_lead", code, "OK24");
        await SeedRosterMemberAsync("t4t_b", Org, "t4t_lead", code, "OK24");

        // b: the profile and agreement-code rows start on Monday 16 March.
        await ExecAsync("UPDATE employee_profiles SET effective_from = DATE '2026-03-16' WHERE employee_id = @e", ("e", "t4t_b"));
        await ExecAsync("UPDATE user_agreement_codes SET effective_from = DATE '2026-03-16' WHERE user_id = @e", ("e", "t4t_b"));

        // a: HK to Tuesday 10 March, no record 11-15 March, X from Monday 16 March.
        // (the open row is closed first: one live row per person)
        await ExecAsync(
            "UPDATE employee_profiles SET effective_to = DATE '2026-03-10' WHERE employee_id = @e",
            ("e", "t4t_a"));
        await ExecAsync(
            """
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, position,
                                           effective_from, effective_to, version, employment_category)
            SELECT gen_random_uuid(), employee_id, part_time_fraction, position,
                   DATE '2026-03-16', NULL, 1, employment_category
            FROM employee_profiles WHERE employee_id = @e
            """, ("e", "t4t_a"));
        await ExecAsync(
            "UPDATE user_agreement_codes SET agreement_code = 'HK', effective_to = DATE '2026-03-10' WHERE user_id = @e",
            ("e", "t4t_a"));
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, @code, DATE '2026-03-16', NULL, 1)
            """, ("e", "t4t_a"), ("code", code));

        refusals.Count = 0; // only the screen's own lookups count, not the publish's
        var rows = await TeamRowsAsync(leader, 2026, 3);

        Assert.Equal(162.80m, rows["t4t_a"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(162.80m, rows["t4t_b"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(1, refusals.Count);
    }

    private sealed class RefusalCounter
    {
        public string Code { get; set; } = "";
        public int Count;
    }

    /// <summary>The real lookup, counting each refusal of the test's agreement.</summary>
    private sealed class CountingLookup(IAgreementConfigLookup inner, RefusalCounter counter) : IAgreementConfigLookup
    {
        public async Task<AgreementRuleConfig> ResolveAsync(
            string orgId, string agreementCode, string okVersion, string? position, DateOnly asOf, CancellationToken ct = default)
        {
            try { return await inner.ResolveAsync(orgId, agreementCode, okVersion, position, asOf, ct); }
            catch (NoConfigInForceException) when (Count(agreementCode)) { throw; }
        }

        public async Task<AgreementRuleConfig> ResolveCentralAsync(
            string agreementCode, string okVersion, DateOnly asOf, CancellationToken ct = default)
        {
            try { return await inner.ResolveCentralAsync(agreementCode, okVersion, asOf, ct); }
            catch (NoConfigInForceException) when (Count(agreementCode)) { throw; }
        }

        /// <summary>Counts the refusal and answers false, so the exception filter never catches it.</summary>
        private bool Count(string agreementCode)
        {
            if (agreementCode == counter.Code)
                Interlocked.Increment(ref counter.Count);
            return false;
        }
    }
}
