using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.TestSupport;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Worklist;

/// <summary>
/// S148 / TASK-14823-T (AC-C2c hand-off half, AC-C2f, AC-C2g, AC-C2i, AC-C2j, AC-C2t) - a configuration change
/// dated into the past tells HR which paid months and settled holiday years it reaches.
///
/// <para>
/// <b>In plain language.</b> When an administrator saves a configuration (a new agreement version, a position
/// override, an institution's local profile) dated before today, the days from that date on may already have
/// been paid. Every month payroll already received that the change reaches, for each employee it applies to,
/// lands on HR's correction worklist with the reason "configuration changed" (<c>CONFIG_PUBLISHED</c>), once per
/// month however many ways the employee matches. A settled holiday year it reaches is flagged, never revalued.
/// Nothing is recalculated automatically: a GlobalAdmin does that. These facts pin who is listed and which
/// months, and, as importantly, who and which months are not.
/// </para>
///
/// <para>
/// <b>Fixture.</b> <see cref="DatedConfigDockerFixture"/> (one container per fact), a GlobalAdmin client on a host
/// whose clock is fixed at Tuesday 10 November 2026 and whose token's employee id is
/// <c>s148_dated_gadmin</c>. People are made by <see cref="RegressionSeed"/> (HK or AC, organisations from the
/// literal <c>'0001-01-01'</c>), exports and settlements by SQL, and every write goes through the real route.
/// Every date below is a literal whose weekday was checked; every expected kind and date is a literal, never a
/// production constant. Docker-gated: CI-pending, never locally green.
/// </para>
///
/// <para>
/// <b>Expected values come from the refinement (REF:454, :457-:461) and ADR-043 D12, not from the code.</b> A
/// bounded stretch selects the months from its first day's month to the month holding the Monday after its last
/// week, so a stretch ending Monday 30 March 2026 (exclusive end) widens into April, while one ending Sunday
/// 29 March does not. OK24 governs until 31 March 2026, OK26 from 1 April 2026 (the OK-version resolver).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigPublishHandOffTests : DatedConfigDockerFixture
{
    /// <summary>The fixed clock: Tuesday 10 November 2026.</summary>
    private static readonly DateOnly Clock = new(2026, 11, 10);

    /// <summary>The employee id <see cref="DatedConfigDockerFixture"/>'s admin token carries.</summary>
    private const string AdminId = "s148_dated_gadmin";

    private const string Kind = "CONFIG_PUBLISHED";

    // ─── AC-C2g ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI).</b> AC-C2g. A person on HK in STY01, January to May 2026 exported; an HK OK24 draft published
    /// dated Monday 5 January 2026 lists January, February, March and April 2026 (April because its first week,
    /// Monday 30 March to Sunday 5 April, settles in April), and not May. Each row carries one
    /// <c>CONFIG_PUBLISHED</c> trigger dated 2026-01-05, the publish event's id and the admin's id.
    /// <para><b>Red conditions (before TASK-14823, no publish lands a worklist row, and <c>CONFIG_PUBLISHED</c> is
    /// not a known kind).</b> The month assertion fails with an empty list. "May not listed" holds today too and
    /// is the discriminating half once the rows exist (an OK24 reach that ignored the end of OK24 would list
    /// May). The serial premise is the fixture's: one container per fact.</para>
    /// </summary>
    [Fact]
    public async Task Ok24PublishMonday5January2026_ListsJanuaryToApril_NotMay()
    {
        var admin = AdminAt(Clock);
        var person = await SeedPersonAsync(ConnectionString, "STY01", "HK");
        foreach (var month in new[] { 1, 2, 3, 4, 5 })
            await SeedExportRecordAsync(person, 2026, month);

        var (configId, version) = await CreateDraftAsync(admin, ConfigBody("HK", "OK24"));
        await PublishOkAsync(admin, configId, version, "2026-01-05");

        var rows = await ReadRowsAsync(person);
        AssertExportedMonths(rows, "2026-01-05", await EventIdAsync($"agreement-config-{configId}", "AgreementConfigPublished"),
            (2026, 1), (2026, 2), (2026, 3), (2026, 4));
        Assert.DoesNotContain(rows, r => r.Month == 5);
        Assert.Equal(4, rows.Count);
    }

    // ─── AC-C2f ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI).</b> AC-C2f (override). E1 holds <c>SPECIALIST</c> all year, E2 holds <c>TEACHING_STAFF</c>; both
    /// are on AC with March 2026 exported. An (AC, OK24, <c>SPECIALIST</c>) override created dated Monday
    /// 2 March 2026 (<c>maxFlexBalance</c> only) lists E1's March and nothing for E2.
    /// <para><b>Red conditions.</b> Before TASK-14823 the create lands no row: <c>AssertExportedMonths</c> on E1
    /// fails with an empty list. "E2 nothing" holds today and is the discriminating half afterwards (a reach
    /// that ignored the position would list E2).</para>
    /// </summary>
    [Fact]
    public async Task OverrideListsOnlyPositionHolders()
    {
        var admin = AdminAt(Clock);
        var e1 = await SeedHolderAsync("SPECIALIST");
        var e2 = await SeedHolderAsync("TEACHING_STAFF");
        await SeedExportRecordAsync(e1, 2026, 3);
        await SeedExportRecordAsync(e2, 2026, 3);

        var (overrideId, _) = await CreateOverrideAsync(
            admin, OverrideBody("AC", "OK24", "SPECIALIST", maxFlexBalance: 150m, weeklyNormHours: null, appliesFrom: "2026-03-02"));

        var eventId = await EventIdAsync($"position-override-{overrideId}", "PositionOverrideCreated");
        AssertExportedMonths(await ReadRowsAsync(e1), "2026-03-02", eventId, (2026, 3));
        Assert.Empty(await ReadRowsAsync(e2));
    }

    /// <summary>
    /// <b>F (CI).</b> AC-C2f (local profile, by the dated organisation). E3 was in STY01 until Monday 1 June 2026
    /// and then in STY02 (moved out after March); E4 was in STY02 until 1 June and then in STY01 (moved in after
    /// March); both on HK, March 2026 exported. STY01's (HK, OK24) local profile written dated Monday 2 March 2026
    /// lists E3's March and nothing for E4, although E4 is in STY01 today and E3 is not.
    /// <para><b>Red conditions.</b> Before TASK-14823 the write lands no row: E3's assertion fails with an empty
    /// list. "E4 nothing" holds today; afterwards it fails if the match reads <c>users.primary_org_id</c> (today's
    /// organisation) instead of the dated organisation.</para>
    /// </summary>
    [Fact]
    public async Task LocalProfileListsByDatedOrganisation()
    {
        var admin = AdminAt(Clock);
        var e3 = await SeedPersonAsync(ConnectionString, "STY01", "HK");
        var e4 = await SeedPersonAsync(ConnectionString, "STY02", "HK");
        await MoveOrganisationAsync(e3, "STY02", new DateOnly(2026, 6, 1));
        await MoveOrganisationAsync(e4, "STY01", new DateOnly(2026, 6, 1));
        await SeedExportRecordAsync(e3, 2026, 3);
        await SeedExportRecordAsync(e4, 2026, 3);

        using var rsp = await PutLocalProfileAsync(admin, "STY01", "HK", "OK24", "2026-03-02");
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"local profile write expected 200, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");

        var eventId = await EventIdAsync("local-agreement-profile-STY01-HK-OK24", "LocalAgreementProfileChanged");
        AssertExportedMonths(await ReadRowsAsync(e3), "2026-03-02", eventId, (2026, 3));
        Assert.Empty(await ReadRowsAsync(e4));
    }

    // ─── AC-C2j ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI).</b> AC-C2j (1). A person on AC holds <c>SPECIALIST</c> through Sunday 29 March 2026 and
    /// <c>TEACHING_STAFF</c> from Monday 30 March; January to April 2026 exported. An (AC, OK24, <c>SPECIALIST</c>)
    /// override created dated Monday 5 January 2026 lists January, February and March, and not April: the match
    /// ends on a Sunday, so its last week settles in March.
    /// <para><b>Red conditions.</b> Before TASK-14823 no row lands: the month assertion fails with an empty list.
    /// "April not listed" is the discriminating half afterwards (a reach that always widened a bounded stretch
    /// to the next month, or that treated the 30 March position change as a match, would list April).</para>
    /// </summary>
    [Fact]
    public async Task PositionMatchEndsSunday29March_AprilNotListed()
    {
        var admin = AdminAt(Clock);
        var person = await SeedHolderAsync("SPECIALIST");
        await ClosePositionAsync(person, new DateOnly(2026, 3, 30));
        await InsertProfileRowAsync(person, "TEACHING_STAFF", new DateOnly(2026, 3, 30), to: null);
        foreach (var month in new[] { 1, 2, 3, 4 })
            await SeedExportRecordAsync(person, 2026, month);

        var (overrideId, _) = await CreateOverrideAsync(
            admin, OverrideBody("AC", "OK24", "SPECIALIST", maxFlexBalance: 150m, weeklyNormHours: null, appliesFrom: "2026-01-05"));

        var rows = await ReadRowsAsync(person);
        AssertExportedMonths(rows, "2026-01-05", await EventIdAsync($"position-override-{overrideId}", "PositionOverrideCreated"),
            (2026, 1), (2026, 2), (2026, 3));
        Assert.DoesNotContain(rows, r => r.Month == 4);
    }

    /// <summary>
    /// <b>F (CI).</b> AC-C2j (2). A person on HK in STY01 moved to STY02 effective Monday 30 March 2026; January to
    /// April 2026 exported. STY01's (HK, OK24) local profile written dated Monday 5 January 2026 lists January,
    /// February and March, and not April.
    /// <para><b>Red conditions.</b> As the position arm: no row lands before TASK-14823; afterwards "April not
    /// listed" fails if the organisation match is widened past the Sunday.</para>
    /// </summary>
    [Fact]
    public async Task OrganisationMatchEndsSunday29March_AprilNotListed()
    {
        var admin = AdminAt(Clock);
        var person = await SeedPersonAsync(ConnectionString, "STY01", "HK");
        await MoveOrganisationAsync(person, "STY02", new DateOnly(2026, 3, 30));
        foreach (var month in new[] { 1, 2, 3, 4 })
            await SeedExportRecordAsync(person, 2026, month);

        using var rsp = await PutLocalProfileAsync(admin, "STY01", "HK", "OK24", "2026-01-05");
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"local profile write expected 200, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");

        var rows = await ReadRowsAsync(person);
        AssertExportedMonths(rows, "2026-01-05",
            await EventIdAsync("local-agreement-profile-STY01-HK-OK24", "LocalAgreementProfileChanged"),
            (2026, 1), (2026, 2), (2026, 3));
        Assert.DoesNotContain(rows, r => r.Month == 4);
    }

    // ─── AC-C2c (hand-off half) ──────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI).</b> AC-C2c, the hand-off half (the pay half is TASK-14828-T's). A person on HK in STY01 with June
    /// 2026 exported; an HK OK26 draft published dated Monday 25 May 2026 (before June) leaves June's export record
    /// exactly as it was (content hash, both line columns, exported-at), lands June's row with one
    /// <c>CONFIG_PUBLISHED</c> trigger, one <c>BackdateWorklistRowCreated</c> event on <c>employee-{person}</c>
    /// whose <c>triggerKind</c> is <c>CONFIG_PUBLISHED</c> and whose trigger event is the publish's own
    /// <c>AgreementConfigPublished</c>, and one ADR-026 audit row for that event targeting the person's
    /// organisation (STY01) and the person.
    /// <para><b>Red conditions.</b> Before TASK-14823 the publish lands no row: <c>Assert.Single</c> on the
    /// worklist rows fails with zero rows (and no event and no audit row exist). The unchanged-record assertion is
    /// an R: a hand-off that touched the record would fail it.</para>
    /// </summary>
    [Fact]
    public async Task PublishBeforeExportedMonth_LinesUnchanged_RowWithEventAndAudit()
    {
        var admin = AdminAt(Clock);
        var person = await SeedPersonAsync(ConnectionString, "STY01", "HK");
        await SeedExportRecordAsync(person, 2026, 6);
        var before = await ReadExportRecordAsync(person, 2026, 6);
        Assert.Equal("hash-2026-6", before.ContentHash);

        var (configId, version) = await CreateDraftAsync(admin, ConfigBody("HK", "OK26"));
        await PublishOkAsync(admin, configId, version, "2026-05-25");

        var publishEventId = await EventIdAsync($"agreement-config-{configId}", "AgreementConfigPublished");
        var rows = await ReadRowsAsync(person);
        AssertExportedMonths(rows, "2026-05-25", publishEventId, (2026, 6));
        Assert.Single(rows);

        Assert.Equal(before, await ReadExportRecordAsync(person, 2026, 6));

        var created = await CountAsync(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @s AND event_type = 'BackdateWorklistRowCreated'
              AND event_payload ->> 'triggerKind' = 'CONFIG_PUBLISHED'
              AND event_payload ->> 'triggerEventId' = @t
            """, ("s", $"employee-{person}"), ("t", publishEventId.ToString()));
        Assert.Equal(1, created);
        var createdEventId = await EventIdAsync($"employee-{person}", "BackdateWorklistRowCreated");
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_id = @e AND event_type = 'BackdateWorklistRowCreated'",
            ("e", createdEventId)));
        Assert.Equal("STY01", await ScalarAsync<string>(
            "SELECT target_org_id FROM audit_projection WHERE event_id = @e", ("e", createdEventId)));
        Assert.Equal(person, await ScalarAsync<string>(
            "SELECT target_resource_id FROM audit_projection WHERE event_id = @e", ("e", createdEventId)));
    }

    // ─── AC-C2i ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI).</b> AC-C2i. A person on HK in STY01 whose VACATION year 2025 is settled (an ACTIVE YEAR_END
    /// settlement whose valuation boundary is 2026-12-31; balance total 25, used 5); STY01's (HK, OK26) local
    /// profile written dated Monday 1 June 2026 lands ONE SETTLED_YEAR row (VACATION, 2025) with one
    /// <c>CONFIG_PUBLISHED</c> trigger dated 2026-06-01, writes no <c>EntitlementBalanceRevalued</c> event and
    /// leaves the balance at 25 / 5. No month was exported, so no EXPORTED_MONTH row lands.
    /// <para><b>Red conditions.</b> Before TASK-14823 the write lands no row: <c>Assert.Single</c> on the
    /// SETTLED_YEAR rows fails with zero rows. The no-revaluation assertions are R: a hand-off that revalued would
    /// fail them.</para>
    /// </summary>
    [Fact]
    public async Task LocalProfileWriteIntoSettledYear_SettledYearRow_NoRevaluation()
    {
        var admin = AdminAt(Clock);
        var person = await SeedPersonAsync(ConnectionString, "STY01", "HK");
        await ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot)
            VALUES (@e, 'VACATION', 2025, 1, 'SETTLED', 'YEAR_END',
                    jsonb_build_object('settlementBoundaryDate', '2026-12-31'))
            """, ("e", person));
        await ExecAsync(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used)
            VALUES (gen_random_uuid(), @e, 'VACATION', 2025, 25, 5)
            """, ("e", person));

        using var rsp = await PutLocalProfileAsync(admin, "STY01", "HK", "OK26", "2026-06-01");
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"local profile write expected 200, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");

        var rows = await ReadRowsAsync(person);
        var row = Assert.Single(rows, r => r.Kind == "SETTLED_YEAR");
        Assert.Equal(("VACATION", 2025), (row.EntitlementType, row.EntitlementYear));
        var trigger = Assert.Single(row.Triggers);
        Assert.Equal(Kind, trigger.GetProperty("kind").GetString());
        Assert.Equal("2026-06-01", trigger.GetProperty("effectiveFrom").GetString());
        Assert.Equal(
            await EventIdAsync("local-agreement-profile-STY01-HK-OK26", "LocalAgreementProfileChanged"),
            trigger.GetProperty("eventId").GetGuid());
        Assert.Equal(AdminId, trigger.GetProperty("actorId").GetString());
        Assert.Single(rows);

        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE event_type = 'EntitlementBalanceRevalued'"));
        var balance = await ReadBalanceAsync(person);
        Assert.Equal((25m, 5m), balance);
    }

    // ─── AC-C2t ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI).</b> AC-C2t (once per month; ruling A8). A person on AC holds <c>SPECIALIST</c> until Tuesday
    /// 10 February 2026, <c>TEACHING_STAFF</c> on Wednesday 11 February, and <c>SPECIALIST</c> again from Thursday
    /// 12 February; February 2026 exported. An (AC, OK24, <c>SPECIALIST</c>) override created dated Monday
    /// 5 January 2026 matches in two separate stretches that both reach February (the first widens to Monday
    /// 16 February). February's row holds exactly ONE trigger and exactly one <c>BackdateWorklistRowCreated</c>
    /// was emitted for the person.
    /// <para><b>Red conditions.</b> Before TASK-14823 no row lands: <c>Assert.Single</c> on the rows fails. After
    /// an implementation that lists per stretch, February's row would hold two triggers (and two events), so the
    /// single-trigger and single-event assertions fail.</para>
    /// </summary>
    [Fact]
    public async Task TwoMatchingIntervalsReachingOneMonth_OneTrigger()
    {
        var admin = AdminAt(Clock);
        var person = await SeedHolderAsync("SPECIALIST");
        await ClosePositionAsync(person, new DateOnly(2026, 2, 11));
        await InsertProfileRowAsync(person, "TEACHING_STAFF", new DateOnly(2026, 2, 11), to: new DateOnly(2026, 2, 12));
        await InsertProfileRowAsync(person, "SPECIALIST", new DateOnly(2026, 2, 12), to: null);
        await SeedExportRecordAsync(person, 2026, 2);

        var (overrideId, _) = await CreateOverrideAsync(
            admin, OverrideBody("AC", "OK24", "SPECIALIST", maxFlexBalance: 150m, weeklyNormHours: null, appliesFrom: "2026-01-05"));

        var rows = await ReadRowsAsync(person);
        AssertExportedMonths(rows, "2026-01-05", await EventIdAsync($"position-override-{overrideId}", "PositionOverrideCreated"),
            (2026, 2));
        Assert.Single(rows);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'BackdateWorklistRowCreated'",
            ("s", $"employee-{person}")));
    }

    // ─── assertions ──────────────────────────────────────────────────────────────

    /// <summary>The person's rows are exactly the EXPORTED_MONTH rows for <paramref name="months"/> (any order), each
    /// with one trigger of kind <c>CONFIG_PUBLISHED</c> (the literal), the given date, the write's own event and the
    /// admin's id. Rows of other kinds fail the month comparison only through the callers' count assertions.</summary>
    private static void AssertExportedMonths(
        IReadOnlyList<WorklistRow> rows, string effectiveFrom, Guid eventId, params (int Year, int Month)[] months)
    {
        var exported = rows.Where(r => r.Kind == "EXPORTED_MONTH").ToList();
        Assert.Equal(months.OrderBy(m => m).ToArray(), exported.Select(r => (r.Year!.Value, r.Month!.Value)).OrderBy(m => m).ToArray());
        foreach (var row in exported)
        {
            var trigger = Assert.Single(row.Triggers);
            Assert.Equal(Kind, trigger.GetProperty("kind").GetString());
            Assert.Equal(effectiveFrom, trigger.GetProperty("effectiveFrom").GetString());
            Assert.Equal(eventId, trigger.GetProperty("eventId").GetGuid());
            Assert.Equal(AdminId, trigger.GetProperty("actorId").GetString());
        }
    }

    // ─── fixtures and reads ──────────────────────────────────────────────────────

    private sealed record WorklistRow(
        string Kind, int? Year, int? Month, string? EntitlementType, int? EntitlementYear, IReadOnlyList<JsonElement> Triggers);

    private sealed record ExportRecord(string ContentHash, string OriginalLines, string CurrentLines, DateTime ExportedAt);

    private async Task<IReadOnlyList<WorklistRow>> ReadRowsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT kind, year, month, entitlement_type, entitlement_year, triggers::text
            FROM hr_backdate_worklist WHERE employee_id = @e ORDER BY created_at, worklist_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var rows = new List<WorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WorklistRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                JsonSerializer.Deserialize<JsonElement>(reader.GetString(5)).EnumerateArray().ToList()));
        }
        return rows;
    }

    /// <summary>The one event of <paramref name="eventType"/> on <paramref name="streamId"/> (a fresh container holds one).</summary>
    private async Task<Guid> EventIdAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_id FROM outbox_events WHERE stream_id = @s AND event_type = @t ORDER BY outbox_id", conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("t", eventType);
        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return Assert.Single(ids);
    }

    private Task SeedExportRecordAsync(string employeeId, int year, int month) =>
        ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month,
                 original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @e, @y, @m,
                    '[{"wageType":"SLS_0110","hours":7.4}]'::jsonb, '[{"wageType":"SLS_0110","hours":7.4}]'::jsonb, @hash)
            """, ("e", employeeId), ("y", year), ("m", month), ("hash", $"hash-{year}-{month}"));

    private async Task<ExportRecord> ReadExportRecordAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT content_hash, original_lines::text, current_effective_lines::text, exported_at
            FROM payroll_export_records WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "expected the seeded payroll_export_records row.");
        return new ExportRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3));
    }

    private async Task<(decimal Total, decimal Used)> ReadBalanceAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT total_quota, used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = 'VACATION' AND entitlement_year = 2025
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "expected the seeded entitlement_balances row.");
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    /// <summary>A person on AC in STY01 holding <paramref name="position"/> from the start of time (the profile row is the seed's).</summary>
    private async Task<string> SeedHolderAsync(string position)
    {
        var userId = "usr_s148_cfg_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(ConnectionString, userId, "STY01", agreementCode: "AC", position: position);
        return userId;
    }

    /// <summary>Ends the person's open profile row, end-exclusive, on <paramref name="at"/>.</summary>
    private Task ClosePositionAsync(string employeeId, DateOnly at) =>
        ExecAsync(
            "UPDATE employee_profiles SET effective_to = @at WHERE employee_id = @e AND effective_to IS NULL",
            ("e", employeeId), ("at", at));

    /// <summary>Adds a profile row at <paramref name="position"/> over [<paramref name="from"/>, <paramref name="to"/>), open when null.</summary>
    private Task InsertProfileRowAsync(string employeeId, string position, DateOnly from, DateOnly? to) =>
        ExecAsync(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, position, effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @e, 1.000, @p, @from, @to, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, ("e", employeeId), ("p", position), ("from", from), ("to", (object?)to ?? DBNull.Value));

    /// <summary>Moves the person to <paramref name="toOrg"/> from <paramref name="effective"/>: the open organisation row ends
    /// (end-exclusive) and a new open row starts; <c>users.primary_org_id</c> follows (every move here is before the clock).</summary>
    private async Task MoveOrganisationAsync(string employeeId, string toOrg, DateOnly effective)
    {
        await ExecAsync(
            "UPDATE user_organisations SET effective_to = @d WHERE user_id = @u AND effective_to IS NULL",
            ("u", employeeId), ("d", effective));
        await ExecAsync(
            """
            INSERT INTO user_organisations (assignment_id, user_id, org_id, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @o, @d, NULL, 1)
            """, ("u", employeeId), ("o", toOrg), ("d", effective));
        await ExecAsync("UPDATE users SET primary_org_id = @o WHERE user_id = @u", ("u", employeeId), ("o", toOrg));
    }

    /// <summary>The local-profile first create (<c>If-None-Match: *</c>) with <c>maxFlexBalance</c> only.</summary>
    private static async Task<HttpResponseMessage> PutLocalProfileAsync(
        HttpClient admin, string orgId, string agreementCode, string okVersion, string effectiveFrom)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/config/{orgId}/profile/{agreementCode}/{okVersion}")
        {
            Content = new StringContent(
                $$"""{"effectiveFrom":"{{effectiveFrom}}","maxFlexBalance":30.0}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await admin.SendAsync(request);
    }
}
