using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.Skema;
using static StatsTid.Tests.Regression.Segmentation.ReplayTestKit;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14840-T — a cleared cell and the payroll boundary (addendum A1 AC-CL-20, the three cases of D-13 and the
/// reopen case). In plain terms: payroll reads the hours an employee has registered, and a cell the employee emptied must
/// not be paid and must not show up as a zero-hour line; a calculation made BEFORE a clear must still reproduce exactly
/// (it reads the rows it recorded by id, retired or not); a month already sent to payroll cannot be changed by a clear or
/// reopened; and a clear in a month that was reopened after the next month was exported is allowed, with the next month's
/// follow-up flag already raised and its export record left alone.
///
/// <para>Each fact has its own Postgres (the real Backend host for the saves, the clear and the reopen; the real Payroll
/// host with the real Rule Engine for fact (1); the replay kit's calculation service for fact (2)). The clock is the real
/// one (the months are past). The approved and exported months are written straight into the tables, as the other payroll
/// facts do: the send and approve routes are not under test here. Docker-gated: <b>CI-pending</b>. "Base" is
/// <c>728f4702</c>, where the <c>cleared</c> member is ignored.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaCellClearPayrollTests : SkemaCellClearTestBase, IAsyncLifetime
{
    private static readonly DateOnly Jun1 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);
    private static readonly DateOnly Tue29Sep = new(2026, 9, 29);

    private sealed class RealClockFixture : SkemaCellClearFixture
    {
        protected override bool PinClock => false;
    }

    public SkemaCellClearPayrollTests() : base(new RealClockFixture()) { }

    public Task InitializeAsync() => Fx.InitializeAsync();

    public Task DisposeAsync() => Fx.DisposeAsync();

    // ── Seeding ──

    /// <summary>A whole calendar month as an approval period of <paramref name="status"/>, and (when
    /// <paramref name="exported"/>) its <c>payroll_export_records</c> row (the "sent to payroll" lock), linked to the period.</summary>
    private async Task<(Guid PeriodId, string Hash)> SeedMonthAsync(
        string emp, string org, string agreement, int year, int month, string status, bool exported)
    {
        var start = new DateOnly(year, month, 1);
        var periodId = Guid.NewGuid();
        await ExecAsync(
            """
            INSERT INTO approval_periods (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                                          agreement_code, ok_version, submitted_at, submitted_by, approved_by, approved_at)
            VALUES (@p, @e, @o, @s, @en, 'MONTHLY', @st, @ac, 'OK26', NOW(), @e, 'ROOT-S148', NOW())
            """, ("p", periodId), ("e", emp), ("o", org), ("s", start), ("en", start.AddMonths(1).AddDays(-1)), ("st", status), ("ac", agreement));
        var hash = $"h-{year}-{month:00}";
        if (exported)
            await ExecAsync(
                """
                INSERT INTO payroll_export_records (export_id, period_id, employee_id, year, month, exported_at,
                                                    original_lines, current_effective_lines, content_hash, source)
                VALUES (gen_random_uuid(), @p, @e, @y, @m, NOW(), '[]'::jsonb, '[]'::jsonb, @h, 'CALCULATE_AND_EXPORT')
                """, ("p", periodId), ("e", emp), ("y", year), ("m", month), ("h", hash));
        return (periodId, hash);
    }

    // ── AC-CL-20 (1) ──

    /// <summary>
    /// AC-CL-20 (1). June 2026: PRJ-A 7,4 h on Wednesday 10 June is cleared and PRJ-B 7,4 h posted in the SAME request; the
    /// month is then approved and calculated by the real Payroll host with the real Rule Engine. Payroll reads PRJ-B only:
    /// the <c>NORMAL_HOURS</c> lines total 7,40 h (PRJ-A's retired row would make it 14,80), none of them has zero hours
    /// (no 0-hour <c>NORMAL_HOURS</c> line), and the calculation's manifest froze PRJ-B's event id and not the retired
    /// PRJ-A row's.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so both entries are current and the hours total 14,80 (the
    /// first assertion after the 200 trips); the manifest holds both ids.</para>
    /// </summary>
    [Fact]
    public async Task ClearedEntry_NotReadByPayroll_NoZeroHourLine()
    {
        var emp = await SeedEmployeeAsync("HK");
        await SaveOkAsync(emp, Save(entries: new[] { (Wed10, "PRJ-A", 7.4m) }), "PRJ-A", EmployeeClient(emp, "HK"));
        var prjA = Assert.Single(await EntriesAsync(emp));
        await SaveOkAsync(emp,
            Save(entries: new[] { (Wed10, "PRJ-B", 7.4m) }, clearEntries: new[] { (Wed10, "PRJ-A") }),
            "clear PRJ-A and post PRJ-B", EmployeeClient(emp, "HK"));
        var prjB = (await EntriesAsync(emp)).Single(r => r.Task == "PRJ-B");
        await SeedMonthAsync(emp, Org, "HK", 2026, 6, "APPROVED", exported: false);
        await TestFixtures.SeedWageTypeMappingsAsync(Fx.Db);
        await using var payroll = new PayrollHostFactory(Fx.ConnectionString, new RuleRegistry().GetAll(), useRealRuleEngine: true);

        var client = payroll.CreateClient();
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: "ROOT-S148", name: "ROOT-S148", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC", orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(new
            {
                profile = new EmploymentProfile
                {
                    EmployeeId = emp,
                    AgreementCode = "HK",
                    OkVersion = "OK26",
                    EmploymentCategory = "Standard",
                    PartTimeFraction = 1.0m,
                    OrgId = Org,
                },
                entries = Array.Empty<TimeEntry>(),
                absences = Array.Empty<AbsenceEntry>(),
                periodStart = Jun1,
                periodEnd = Jun30,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var rsp = await client.SendAsync(request);

        var json = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {json}");
        var result = JsonSerializer.Deserialize<PeriodCalculationResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var normal = result.RuleResults.SelectMany(r => r.LineItems).Where(li => li.TimeType == "NORMAL_HOURS").ToList();
        async Task<long> FrozenAsync(Guid id) => await ScalarLongAsync(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e AND strpos(plan_inputs_jsonb::text, @id) > 0",
            ("e", emp), ("id", id.ToString()));
        var frozenB = await FrozenAsync(prjB.EventId);
        var frozenA = await FrozenAsync(prjA.EventId);
        Assert.Multiple(
            () => Assert.Equal(7.40m, normal.Sum(li => li.Hours)),
            () => Assert.DoesNotContain(normal, li => li.Hours <= 0m),
            () => Assert.Equal(1L, frozenB),
            () => Assert.Equal(0L, frozenA));
    }

    // ── AC-CL-20 (2) ──

    /// <summary>
    /// AC-CL-20 (2). A calculation made before the clear (June 2026, PRJ-A 7,4 h on Wednesday 10 June, calculated, not
    /// exported; the month then reopened) replays to its recorded result after the cell is cleared through the real save,
    /// and the replay's rows include the retired row: the replay reads the manifest's rows BY EVENT ID, unfiltered, so a
    /// retired row is still found. A FRESH calculation of the month differs (it no longer reads the retired row).
    ///
    /// <para>Kind F in this form (it asserts that the clear retired the row, which on base it does not; the ledger counts it
    /// R because the replay half is green on base, where nothing is cleared). Red conditions: on base the retire assertion
    /// trips. The replay half is the guard: a by-id read that gained the current-row predicate would refuse the replay
    /// (<c>replay-entries-missing</c>) and trip the <c>Success</c> assertion.</para>
    /// </summary>
    [Fact]
    public async Task ReplayOfACalculationBeforeTheClear_Reproduces()
    {
        var emp = await SeedEmployeeAsync("HK");
        await SaveOkAsync(emp, Save(entries: new[] { (Wed10, "PRJ-A", 7.4m) }), "PRJ-A", EmployeeClient(emp, "HK"));
        var harness = Fx.Harness;
        await TestFixtures.SeedWageTypeMappingsAsync(Fx.Db);
        var approval = new FakeApprovalReader();
        var pcs = BuildPcs(harness, new ReplayEchoRuleEngine(), approval, RealHoursReader(harness));
        var forward = await ForwardAsync(pcs, emp, Jun1, Jun30);

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp, "HK"), emp, Save(clearEntries: new[] { (Wed10, "PRJ-A") })), HttpStatusCode.OK, "the clear");
        Assert.NotNull(Assert.Single(await EntriesAsync(emp)).SupersededBy);

        var replayPcs = BuildPcs(harness, new ReplayEchoRuleEngine(), approval, RealHoursReader(harness));
        var replay = await replayPcs.ReplayAsync(forward.ManifestId);
        var fresh = await FreshAsync(pcs, emp, Jun1, Jun30);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
        Assert.True(fresh.Success);
        Assert.NotEqual(Canonical(forward.Result, withManifestIds: false), Canonical(fresh, withManifestIds: false));
    }

    // ── AC-CL-20 (3) ──

    /// <summary>
    /// AC-CL-20 (3). June 2026 EXPORTED: a clear answers 409 (the approved month's lock) and the row stays current with no
    /// clear event; a reopen by the leader answers 409 <c>kind = "payroll-locked"</c> (the month is corrections-only after
    /// the export) and the period stays APPROVED.
    ///
    /// <para>Kind R. Red conditions: none on base; fails if a clear is accepted in an exported month (200, the row retired,
    /// a <c>TimeEntryCleared</c>) or if the reopen lock is lost.</para>
    /// </summary>
    [Fact]
    public async Task ExportedMonth_ClearAndReopen409_RowStaysCurrent()
    {
        var (emp, leader) = await SeedEmployeeUnderLeaderAsync();
        var client = ClientFor(emp, StatsTidRoles.Employee, LeaderOrg, "HK");
        await SaveOkAsync(emp, Save(entries: new[] { (Wed10, "PRJ-1", 3.0m) }), "PRJ-1", client);
        var (periodId, _) = await SeedMonthAsync(emp, LeaderOrg, "HK", 2026, 6, "APPROVED", exported: true);

        var clear = await PostSaveAsync(client, emp, Save(clearEntries: new[] { (Wed10, "PRJ-1") }));
        var reopen = await ClientFor(leader, StatsTidRoles.LocalLeader, LeaderOrg, "HK")
            .PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "S148 pin" });

        await AssertStatusAsync(clear, HttpStatusCode.Conflict, "the clear in an exported month");
        var locked = await RefusalAsync(reopen, HttpStatusCode.Conflict);
        Assert.Equal("payroll-locked", locked.GetProperty("kind").GetString());
        Assert.Null(Assert.Single(await EntriesAsync(emp)).SupersededBy);
        Assert.Empty(await OutboxAsync(emp, "TimeEntryCleared"));
        Assert.Equal(
            "APPROVED", await ScalarTextAsync("SELECT status FROM approval_periods WHERE period_id = @p", ("p", periodId)));
    }

    private async Task<string> ScalarTextAsync(string sql, params (string Name, object? Value)[] ps)
    {
        await using var conn = new Npgsql.NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): the SQL is a compile-time test constant; values go through parameters.
#pragma warning disable CA2100
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    // ── AC-CL-20 (4) ──

    /// <summary>
    /// AC-CL-20 (4). September 2026 approved and not exported, October 2026 exported, September reopened by the leader
    /// (which raises October's non-blocking <c>PRIOR_MONTH_REOPENED</c> follow-up row, October's opening payroll week
    /// being Monday 28 September to Sunday 4 October): a clear of PRJ-1 on Tuesday 29 September 2026 answers 200 and
    /// retires the row; October's follow-up row is still open (one <c>EXPORTED_MONTH</c> row for October, unresolved,
    /// carrying the trigger); October's export record is untouched (same content hash, lines and export time).
    ///
    /// <para>Kind F, by the retire assertion. Red conditions: on base the <c>cleared</c> member is ignored, so the entry
    /// stays current and <c>Assert.NotNull</c> on its retire marker trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearAfterPriorMonthReopen_NextMonthFlagged_ExportUntouched()
    {
        Assert.Equal(DayOfWeek.Tuesday, Tue29Sep.DayOfWeek);
        var (emp, leader) = await SeedEmployeeUnderLeaderAsync();
        var client = ClientFor(emp, StatsTidRoles.Employee, LeaderOrg, "HK");
        await SaveOkAsync(emp, Save(entries: new[] { (Tue29Sep, "PRJ-1", 3.0m) }, month: 9), "PRJ-1 on 29 September", client);
        var (september, _) = await SeedMonthAsync(emp, LeaderOrg, "HK", 2026, 9, "APPROVED", exported: false);
        await SeedMonthAsync(emp, LeaderOrg, "HK", 2026, 10, "APPROVED", exported: true);
        var reopen = await ClientFor(leader, StatsTidRoles.LocalLeader, LeaderOrg, "HK")
            .PostAsJsonAsync($"/api/approval/{september}/reopen", new { reason = "S148 pin" });
        await AssertStatusAsync(reopen, HttpStatusCode.OK, "the September reopen");
        Assert.Equal(
            1L, await ScalarLongAsync(
                """
                SELECT COUNT(*) FROM hr_backdate_worklist
                WHERE employee_id = @e AND kind = 'EXPORTED_MONTH' AND year = 2026 AND month = 10 AND resolved_at IS NULL
                  AND strpos(triggers::text, 'PRIOR_MONTH_REOPENED') > 0
                """, ("e", emp)));
        var exportBefore = await ExportSnapshotAsync(emp);

        await AssertStatusAsync(
            await PostSaveAsync(client, emp, Save(clearEntries: new[] { (Tue29Sep, "PRJ-1") }, month: 9)), HttpStatusCode.OK, "the clear");

        Assert.NotNull(Assert.Single(await EntriesAsync(emp)).SupersededBy);
        Assert.Equal(
            1L, await ScalarLongAsync(
                """
                SELECT COUNT(*) FROM hr_backdate_worklist
                WHERE employee_id = @e AND kind = 'EXPORTED_MONTH' AND year = 2026 AND month = 10 AND resolved_at IS NULL
                  AND strpos(triggers::text, 'PRIOR_MONTH_REOPENED') > 0
                """, ("e", emp)));
        Assert.Equal(exportBefore, await ExportSnapshotAsync(emp));
    }

    private async Task<string> ExportSnapshotAsync(string emp) => await ScalarTextAsync(
        """
        SELECT string_agg(export_id::text || '|' || content_hash || '|' || original_lines::text || '|'
                          || current_effective_lines::text || '|' || exported_at::text, ';' ORDER BY year, month)
        FROM payroll_export_records WHERE employee_id = @e
        """, ("e", emp));
}
