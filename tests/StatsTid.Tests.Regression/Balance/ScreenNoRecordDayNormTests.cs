using System.Net;
using StatsTid.Tests.Regression.Contracts;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S148 T14b FU-T (5a ruling T2) - the month's expected hours on Teamoversigt and the balance summary never fail on a
/// weekday that no employment record covers, only because the agreement had nothing published yet.
///
/// <para>
/// <b>In plain language.</b> Publishing a new agreement defaults to "applies from today", so a person hired onto a brand-new
/// agreement on Monday 16 March 2026 has no record before that Monday and the agreement has nothing in force before it.
/// Before the fix, the leader's Teamoversigt for March and the person's balance page both answered an error naming a missing
/// configuration, for the whole team. Those earlier weekdays are a stand-in on the screen anyway (QUAL-226), so they now count
/// at the agreement's configuration at the month's end: 22 weekdays x 7.40 = <b>162.80</b>. When the agreement has nothing in
/// force all month and the person has no record, Teamoversigt counts those days 0 and answers 200 with <b>0</b>; the balance
/// summary answers the named 500 <c>config_not_in_force</c>, which is not the month figure: it comes from the balance's
/// separate merarbejde-flag read at the month's end, which still refuses by name because the agreement has nothing published
/// in force (the Orchestrator's ruling on FU-2's brief contradiction 2). The refusal body is B4's, written out below from
/// TASK-14825's brief as <c>ScreenConfigFailClosedTests</c> asserts it.
/// </para>
///
/// <para>
/// Docker-gated: CI-verified, never locally green. A run on a throwaway local PostgreSQL 16 cluster is a pre-check only.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScreenNoRecordDayNormTests : ScreenNormFixture
{
    private const string Org = "STY02";

    /// <summary>TASK-14825's brief, item 6 (B4): the fixed body of "no configuration in force".</summary>
    private const string NotInForceBody =
        """{"error":"config_not_in_force","reason":"No published agreement configuration is in force for this date. A configuration administrator must publish one."}""";

    /// <summary>
    /// <b>Kind N (CI).</b> T2, the new-hire arm. A test-unique agreement (OK24, 37 h) created and published through the routes
    /// applying from Monday 16 March 2026, and an employee on it whose profile and agreement-code rows start on that Monday (so
    /// no record covers any earlier day). Teamoversigt for March 2026 (22 weekdays) answers 200 with <c>normExpected</c>
    /// <b>162.80</b> and the balance summary answers 200 with <c>normHoursExpected</c> <b>162.80</b>.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending). At <c>043c5f35</c> the first no-record weekday (Monday 2 March) asks
    /// the lookup for a day before the agreement's first version, which refuses: both screens answer the named 500
    /// <c>config_not_in_force</c> and the status assertions fail (FU-2 measured exactly this on both arms). N: the ruling made
    /// this the screen's behaviour; the Unit facts of <c>ComputeMonthNormNoRecordDayTests</c> carry the F proof.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NewHireOnNewAgreement_NoRecordBeforeHire_Shows162Point80()
    {
        using var admin = AdminAt(Clock);
        var host = ScreenHost();
        var code = UniqueCode("T2NH");
        await CreateAndPublishAsync(admin, ConfigBody(code, "OK24"), "2026-03-16");
        var leader = await SeedLeaderAsync(host, "t2nh_lead", Org);
        await SeedRosterMemberAsync("t2nh_e1", Org, "t2nh_lead", code, "OK24");
        await ExecAsync(
            "UPDATE employee_profiles SET effective_from = DATE '2026-03-16' WHERE employee_id = @e",
            ("e", "t2nh_e1"));
        await ExecAsync(
            "UPDATE user_agreement_codes SET effective_from = DATE '2026-03-16' WHERE user_id = @e",
            ("e", "t2nh_e1"));

        var rows = await TeamRowsAsync(leader, 2026, 3);
        var balance = await BalanceNormAsync(host, "t2nh_e1", Org, 2026, 3);

        Assert.Equal(162.80m, rows["t2nh_e1"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(162.80m, balance);
    }

    /// <summary>
    /// <b>Kind N (CI).</b> T2, the zero arm. An employee with no profile at all on an agreement that has no published version
    /// (test-unique code <c>T2ZERO</c>, OK24): Teamoversigt for March 2026 answers 200 with <c>normExpected</c> <b>0</b> (every
    /// weekday counts 0.00 h); the balance summary answers 500 with exactly B4's named body (<c>config_not_in_force</c>), from its
    /// merarbejde-flag read at the month's end, not from the month figure.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending). At <c>043c5f35</c> Teamoversigt refuses the first weekday with the same
    /// named 500, so the 200 assertion fails; the balance arm is the same 500 before and after (the flag read is outside the ruling).
    /// </para>
    /// </summary>
    [Fact]
    public async Task NoRecordAndNothingInForce_TeamoversigtShowsZero_BalanceRefusedByName()
    {
        var host = ScreenHost();
        var leader = await SeedLeaderAsync(host, "t2z_lead", Org);
        await SeedRosterMemberAsync("t2z_e1", Org, "t2z_lead", "T2ZERO", "OK24");
        await ExecAsync("DELETE FROM employee_profiles WHERE employee_id = @e", ("e", "t2z_e1"));

        var rows = await TeamRowsAsync(leader, 2026, 3);
        using var employee = S120ContractAssert.EmployeeClient(host, "t2z_e1", Org);
        using var balance = await employee.GetAsync("/api/balance/t2z_e1/summary?year=2026&month=3");
        var balanceText = await balance.Content.ReadAsStringAsync();

        Assert.Equal(0m, rows["t2z_e1"].GetProperty("normExpected").GetDecimal());
        Assert.Equal(HttpStatusCode.InternalServerError, balance.StatusCode);
        Assert.Equal(NotInForceBody, balanceText);
    }
}
