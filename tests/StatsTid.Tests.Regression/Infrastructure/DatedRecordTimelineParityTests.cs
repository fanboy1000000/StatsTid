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
/// latest start wins, so Tuesday 10 March reads the later agreement-code row, Thursday 12 March the later profile row, Friday 20 March the later organisation row, and Sunday 22 and Monday 23 March the later profile row, which also ends later) and each of the three gap kinds: no profile (before 1 March and from 26
/// March), a profile without its organisation row (5-7 March), and a profile without its agreement-code row (16-17 March).
/// </para>
///
/// <para>
/// <b>The mapping of outcomes.</b> The resolver answers a profile, <c>null</c> or throws <see cref="EmployeeProfileNotFoundException"/>;
/// <see cref="DatedRecordTimeline.On"/> answers a record or a null record with the missing table named. A profile equals a record when
/// the agreement code, position and organisation are equal; resolver <c>null</c> (no profile row covers the day) corresponds to a null
/// record naming <c>employment_profile</c>; the resolver's exception (a profile covers, the organisation or the agreement-code row does
/// not) corresponds to a null record naming <c>organisation</c> or <c>agreement_code</c>.
/// Overlapping profile rows are included (one nested inside the other, one partly overlapping and ending later): the resolver's profile query takes the latest start (QUAL-263), as the timeline does, so the
/// two agree there. The outcome mapping does not compare the part-time fraction, so the overlapping row's fraction (0.500) is
/// present but not checked here. Docker-gated, so CI is the verifier.
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
    /// and the row dates, not read back): 3 March reads (HK, POS-A, T12ORGA); 10 March (overlap) reads (PROSA, POS-B, T12ORGB); 12 March (profile overlap: POS-B and POS-C both
    /// cover it) reads (HK, POS-C, T12ORGB); 22 March (partial profile overlap: POS-B, which ends 24 March, and POS-D, which starts 22 March and ends 26 March, both cover it; the organisation row C has ended, so B) reads (AC, POS-D, T12ORGB); 25 March (POS-D alone) reads the same; 20 March
    /// (overlap) reads (AC, POS-B, T12ORGC); 6 March misses the organisation, 16 March the agreement code, 27 February and 26 March the
    /// profile.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; N: the fixture includes overlapping profile rows, the divergence QUAL-263 pinned, in two shapes: POS-C nested inside POS-B and ends earlier, POS-D starts later AND ends later than POS-B). Any day on which the two readers differ, for example a
    /// resolver whose profile read takes the earliest start where profile rows overlap (12 and 13 March; red by rule), or has no order there
    /// (red only while the database returns POS-B first, which it does on this fixture because POS-C is inserted last; not guaranteed), a resolver ordered by end date, ascending (22 and 23 March; the nested POS-C row ends earlier, so 12 and 13 March stay right) or descending (12 and 13 March; POS-C ends before POS-B), a resolver where the most recently inserted row wins (22 and 23 March: POS-D is inserted before POS-B), a
    /// reader that took the earliest start where other rows overlap, ignored an end date, or answered a record where the organisation row is
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
        Assert.Equal((new DatedRecord("HK", "POS-C", OrgB), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 12)));
        Assert.Equal((new DatedRecord("AC", "POS-B", OrgC), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 20)));
        Assert.Equal((new DatedRecord("AC", "POS-B", OrgC), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 21)));
        Assert.Equal((new DatedRecord("AC", "POS-D", OrgB), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 22)));
        Assert.Equal((new DatedRecord("AC", "POS-D", OrgB), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 23)));
        Assert.Equal((new DatedRecord("AC", "POS-D", OrgB), (string?)null), timeline.On(Employee, new DateOnly(2026, 3, 25)));
        Assert.Equal(((DatedRecord?)null, (string?)"organisation"), timeline.On(Employee, new DateOnly(2026, 3, 6)));
        Assert.Equal(((DatedRecord?)null, (string?)"agreement_code"), timeline.On(Employee, new DateOnly(2026, 3, 16)));
        Assert.Equal(((DatedRecord?)null, (string?)"employment_profile"), timeline.On(Employee, new DateOnly(2026, 2, 27)));
        Assert.Equal(((DatedRecord?)null, (string?)"employment_profile"), timeline.On(Employee, new DateOnly(2026, 3, 26)));
    }

    /// <summary>The employee with the overlaps and the three gap kinds (the dates are on the class remark).</summary>
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

        // Profile rows: POS-A from 1 March to 10 March (exclusive), POS-B from 10 March to 24 March, POS-C 12-14 March (exclusive)
        // inside POS-B, so 12 and 13 March are covered by two rows and the latest start (POS-C) wins (QUAL-263); POS-D from 22 March
        // to 26 March (exclusive) overlaps the end of POS-B, so 22 and 23 March are covered by two rows and the latest start (POS-D)
        // wins although POS-D also ENDS later (24 and 25 March are POS-D alone). The profile gap from 26 March stays. The fractions of
        // POS-C and POS-D differ from 1.000 so the rows are not interchangeable; the outcome mapping does not compare them.
        // The insert order is deliberate, for two different readers: POS-C follows POS-B, so a resolver with no ORDER BY reads POS-B
        // first and is red on 12 and 13 March (on this fixture's plan; an unordered query's order is not guaranteed); POS-D PRECEDES POS-B, so a "most recently inserted wins" reader reads POS-B and is
        // red on 22 and 23 March, where POS-D (the later start) is the right answer. Do not reorder.
        await InsertProfileAsync("POS-A", "2026-03-01", "2026-03-10");
        await InsertProfileAsync("POS-D", "2026-03-22", "2026-03-26", 0.750m);
        await InsertProfileAsync("POS-B", "2026-03-10", "2026-03-24");
        await InsertProfileAsync("POS-C", "2026-03-12", "2026-03-14", 0.500m);

        // Organisation rows: A to 5 March (exclusive), B from 8 March (open), C 20-21 March (overlapping B).
        await InsertOrganisationAsync(OrgA, "2026-01-01", "2026-03-05");
        await InsertOrganisationAsync(OrgB, "2026-03-08", null);
        await InsertOrganisationAsync(OrgC, "2026-03-20", "2026-03-22");

        // Agreement-code rows: HK to 16 March (exclusive), PROSA 10-11 March (overlapping HK), AC from 18 March (open).
        await InsertAgreementCodeAsync("HK", "2026-01-01", "2026-03-16");
        await InsertAgreementCodeAsync("PROSA", "2026-03-10", "2026-03-12");
        await InsertAgreementCodeAsync("AC", "2026-03-18", null);
    }

    private Task InsertProfileAsync(string position, string from, string? to, decimal fraction = 1.000m) =>
        ExecAsync(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from, effective_to, employment_category)
            VALUES (@e, @fraction, @position, @from::date, @to::date, (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, ("e", Employee), ("position", position), ("fraction", fraction), ("from", from), ("to", (object?)to ?? DBNull.Value));

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
