using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// S148 T14b FU-T (5a ruling T12) - the screens' set-based reader of the dated employment record (<see cref="DatedRecordTimeline"/>)
/// answers every day exactly as the per-day resolver the rest of the system uses (<see cref="EmploymentProfileResolver"/>).
///
/// <para>
/// <b>In plain language.</b> Teamoversigt, the balance and compliance read an employee's record for a whole month in three queries
/// (<c>DatedRecordTimeline</c>) instead of asking the resolver once per day. It is a second reader of the same rule (QUAL-registered,
/// M), so this fact keeps the two from drifting: over one employee's March 2026 it compares the two for every day from Friday 27
/// February to Thursday 2 April. The record has overlaps (where two organisation rows or two agreement-code rows cover a day, the
/// latest start wins, so Tuesday 10 March reads the later agreement-code row and Friday 20 March the later organisation row; overlapping
/// profile rows are not tested, see below) and each of the three gap kinds: no profile (before 1 March and from 26
/// March), a profile without its organisation row (5-7 March), and a profile without its agreement-code row (16-17 March).
/// </para>
///
/// <para>
/// <b>The mapping of outcomes.</b> The resolver answers a profile, <c>null</c> or throws <see cref="EmployeeProfileNotFoundException"/>;
/// <see cref="DatedRecordTimeline.On"/> answers a record or a null record with the missing table named. A profile equals a record when
/// the agreement code, position and organisation are equal; resolver <c>null</c> (no profile row covers the day) corresponds to a null
/// record naming <c>employment_profile</c>; the resolver's exception (a profile covers, the organisation or the agreement-code row does
/// not) corresponds to a null record naming <c>organisation</c> or <c>agreement_code</c>.
/// <b>Overlapping profile rows are deliberately absent:</b> a first run showed the two readers differ there (the timeline takes the
/// latest start; the resolver's profile query has no ORDER BY and returned the earlier row). Non-overlap of profile rows is a writer
/// invariant, so this is reported, not pinned. Docker-gated: CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatedRecordTimelineParityTests : DatedConfigDockerFixture
{
    private const string Employee = "t12_e1";
    private const string OrgA = "T12ORGA";
    private const string OrgB = "T12ORGB";
    private const string OrgC = "T12ORGC";

    /// <summary>
    /// <b>Kind N (CI).</b> T12. For each day from 2026-02-27 to 2026-04-02, <c>DatedRecordTimeline.On</c> equals the resolver's outcome
    /// under the mapping above; and the fixture really holds what it claims (explicit expectations, derived from "the latest start wins"
    /// and the row dates, not read back): 3 March reads (HK, POS-A, T12ORGA); 10 March (overlap) reads (PROSA, POS-B, T12ORGB); 20 March
    /// (overlap) reads (AC, POS-B, T12ORGC); 6 March misses the organisation, 16 March the agreement code, 27 February and 26 March the
    /// profile.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; N: no divergence is known). Any day on which the two readers differ, for example a
    /// reader that took the earliest start where rows overlap, ignored an end date, or answered a record where the organisation row is
    /// missing, appears in the mismatch list and fails the first assertion; the explicit-expectation assertions fail if the fixture ever
    /// stops containing an overlap or one of the gap kinds.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EveryDay_OnEqualsTheResolver_OverlapAndThreeGapKinds()
    {
        await SeedRecordAsync();
        var resolver = new EmploymentProfileResolver(Db, new UserAgreementCodeRepository(Db));
        var from = new DateOnly(2026, 2, 27);
        var to = new DateOnly(2026, 4, 2);

        DatedRecordTimeline timeline;
        await using (var conn = Db.Create())
        {
            await conn.OpenAsync();
            timeline = await DatedRecordTimeline.ReadAsync(conn, new[] { Employee }, from, to);
        }

        var mismatches = new List<string>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (record, missing) = timeline.On(Employee, day);

            DatedRecord? resolvedRecord = null;
            var resolverGap = false;
            var resolverNull = false;
            try
            {
                var profile = await resolver.GetByEmployeeIdAtAsync(Employee, day);
                if (profile is null)
                    resolverNull = true;
                else
                    resolvedRecord = new DatedRecord(profile.AgreementCode, profile.Position, profile.OrgId!);
            }
            catch (EmployeeProfileNotFoundException)
            {
                resolverGap = true;
            }

            var agree = resolvedRecord is not null
                ? record == resolvedRecord && missing is null
                : resolverNull
                    ? record is null && missing == "employment_profile"
                    : resolverGap && record is null && missing is "organisation" or "agreement_code";
            if (!agree)
                mismatches.Add($"{day:yyyy-MM-dd}: On={record?.ToString() ?? "null"}/{missing}; resolver={resolvedRecord?.ToString() ?? (resolverNull ? "null" : "gap")}");
        }

        Assert.True(mismatches.Count == 0, "Days on which the two readers differ:\n" + string.Join("\n", mismatches));

        Assert.Equal((new DatedRecord("HK", "POS-A", OrgA), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 3)));
        Assert.Equal((new DatedRecord("PROSA", "POS-B", OrgB), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 10)));
        Assert.Equal((new DatedRecord("AC", "POS-B", OrgC), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 20)));
        Assert.Equal(((DatedRecord?)null, (string?)"organisation"), timeline.On(Employee, new DateOnly(2026, 3, 6)));
        Assert.Equal(((DatedRecord?)null, (string?)"agreement_code"), timeline.On(Employee, new DateOnly(2026, 3, 16)));
        Assert.Equal(((DatedRecord?)null, (string?)"employment_profile"), timeline.On(Employee, new DateOnly(2026, 2, 27)));
        Assert.Equal(((DatedRecord?)null, (string?)"employment_profile"), timeline.On(Employee, new DateOnly(2026, 3, 26)));
    }

    /// <summary>The employee with the overlap and the three gap kinds (the dates are on the class remark).</summary>
    private async Task SeedRecordAsync()
    {
        foreach (var org in new[] { OrgA, OrgB, OrgC })
        {
            await ExecAsync(
                """
                INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version)
                VALUES (@org, @org, 'ORGANISATION', NULL, @path, 'HK', 'OK24')
                ON CONFLICT (org_id) DO NOTHING
                """, ("org", org), ("path", $"/{org}/"));
        }

        await RegressionSeed.SeedEmployeeAsync(ConnectionString, Employee, OrgA, "HK", "OK24");
        await ExecAsync("DELETE FROM employee_profiles WHERE employee_id = @e", ("e", Employee));
        await ExecAsync("DELETE FROM user_organisations WHERE user_id = @e", ("e", Employee));
        await ExecAsync("DELETE FROM user_agreement_codes WHERE user_id = @e", ("e", Employee));

        // Profile rows: POS-A from 1 March to 10 March (exclusive), POS-B from 10 March to 26 March. They do not overlap: the resolver's
        // profile read has no tie-break for overlapping profile rows (it reads the first row the database returns), so no parity is
        // defined there; the overlaps below are on the two tables whose tie-break the resolver states (latest start wins).
        await InsertProfileAsync("POS-A", "2026-03-01", "2026-03-10");
        await InsertProfileAsync("POS-B", "2026-03-10", "2026-03-26");

        // Organisation rows: A to 5 March (exclusive), B from 8 March (open), C 20-21 March (overlapping B).
        await InsertOrganisationAsync(OrgA, "2026-01-01", "2026-03-05");
        await InsertOrganisationAsync(OrgB, "2026-03-08", null);
        await InsertOrganisationAsync(OrgC, "2026-03-20", "2026-03-22");

        // Agreement-code rows: HK to 16 March (exclusive), PROSA 10-11 March (overlapping HK), AC from 18 March (open).
        await InsertAgreementCodeAsync("HK", "2026-01-01", "2026-03-16");
        await InsertAgreementCodeAsync("PROSA", "2026-03-10", "2026-03-12");
        await InsertAgreementCodeAsync("AC", "2026-03-18", null);
    }

    private Task InsertProfileAsync(string position, string from, string? to) =>
        ExecAsync(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from, effective_to, employment_category)
            VALUES (@e, 1.000, @position, @from::date, @to::date, (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, ("e", Employee), ("position", position), ("from", from), ("to", (object?)to ?? DBNull.Value));

    private Task InsertOrganisationAsync(string org, string from, string? to) =>
        ExecAsync(
            """
            INSERT INTO user_organisations (assignment_id, user_id, org_id, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, @org, @from::date, @to::date, 1)
            """, ("e", Employee), ("org", org), ("from", from), ("to", (object?)to ?? DBNull.Value));

    private Task InsertAgreementCodeAsync(string code, string from, string? to) =>
        ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @e, @code, @from::date, @to::date, 1)
            """, ("e", Employee), ("code", code), ("from", from), ("to", (object?)to ?? DBNull.Value));
}
