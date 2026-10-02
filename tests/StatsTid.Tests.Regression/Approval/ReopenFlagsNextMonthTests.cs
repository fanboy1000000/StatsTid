using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S145 / TASK-14504 (refinement A6, R5(c), R31c; plan facts RO1–RO4) — reopening a month must
/// flag the NEXT month when that month has already been sent to payroll and its first week
/// contains days of the reopened one.
///
/// <para><b>Plain language.</b> Payroll is paid for whole Monday-to-Sunday weeks, and the week that
/// straddles two months is paid with the LATER month. 1 October 2026 is a Thursday, so the week
/// Monday 28 September to Sunday 4 October is settled by the October run: October's pay includes
/// 28–30 September. Now suppose September is approved but NOT yet exported, and a leader reopens it
/// to correct those days, while October has ALREADY been exported. October's export was computed
/// from September's old figures, and nothing tells HR. The reopen therefore writes an
/// <c>EXPORTED_MONTH</c> row for October on the HR follow-up list (ADR-040 D8; HRP-001), in the same
/// transaction as the reopen, so the list is never behind the state. The row carries a NEW trigger
/// kind, working name <c>PRIOR_MONTH_REOPENED</c>, that is deliberately NON-blocking: nothing about
/// October's own data prevents re-planning it (the payroll re-plan refusal is about a profile or
/// agreement split INSIDE the month, which a reopen is not), so <c>recalcBlockedBy</c> stays empty.
/// The row's event is the <c>PeriodReopened</c> event itself, which is the causal link back to
/// what HR must look at.</para>
///
/// <para><b>Four cases, each derived from the spec and not from the code.</b>
/// RO1 — October exported, its first week holds 28–30 September: one October row.
/// RO2 — October NOT exported (even though approved): nothing, because an approved-but-not-exported
/// month reads its inputs afresh at its own export.
/// RO3 (R31c) — October AND November exported: exactly October. October ends on Saturday 31 October,
/// so a hook that borrowed the backdate widening (R5(a): round the upper bound up to the next Monday)
/// would reach 2 November and flag November as well. The reopen of September has no business with
/// November, and R36 rules that the hook does NOT inherit the widening.
/// RO4 (R31c guard) — May 2026 reopened, June exported: nothing. 1 June 2026 is a MONDAY, so June's
/// opening week holds no May day at all.</para>
///
/// <para>HTTP and SQL only: the reopen is a real <c>POST /api/approval/{id}/reopen</c> by a leader,
/// the result is read back through the real <c>GET /api/hr/backdate-worklist</c>, and the trigger
/// kind is a plain string (the constant it will become does not exist yet, and a pin must not need
/// the implementation to compile). Topology mirrors <see cref="ReopenPayrollLockTests"/>: STY02
/// Organisation, a PRIMARY manager edge granting the leader reopen. Every fact starts its own
/// container, so no cleanup is needed.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ReopenFlagsNextMonthTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Emp = "s145_emp";            // STY02 — the employee whose month is reopened
    private const string Mgr = "s145_mgr";            // STY02 — PRIMARY manager of Emp (the leader who reopens)
    private const string AdminActor = "s145_admin";   // GLOBAL_ADMIN — reads the worklist only
    private const string TreeRootSty02 = "STY02";

    /// <summary>The trigger kind the reopen hook writes — a contract NAME from the refinement (A6),
    /// held as a string because the repository constant is created by the sprint.</summary>
    private const string PriorMonthReopenedKind = "PRIOR_MONTH_REOPENED";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await SeedUsersAsync(conn);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  RO1 — October exported, its opening week holds days of September: one October row
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// September (approved, not exported) is reopened while October is exported. October's opening
    /// week (Monday 28 September to Sunday 4 October) contains 28–30 September, so exactly ONE
    /// <c>EXPORTED_MONTH</c> row lands on October, with trigger kind <c>PRIOR_MONTH_REOPENED</c>,
    /// pointing at October's export record and its baseline hash, linked to the
    /// <c>PeriodReopened</c> event by id, and NOT blocking the re-plan (<c>recalcBlockedBy</c> empty
    /// through the GET, at row and at trigger level). The row's creation is itself audited: one
    /// <c>BackdateWorklistRowCreated</c> event (kind, month, trigger kind and trigger event id) and
    /// its ADR-026 audit row. Nothing is written for September itself, which has no export record.
    ///
    /// <para><b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). Mutation
    /// <b>MD-48</b>: the R5(c) hook removed from the reopen handler in
    /// <c>ApprovalEndpoints.cs</c>. This is also TODAY's code (no hook exists), so the fact is red
    /// before the implementation: the GET answers an empty array and <c>Assert.Single</c> on the rows
    /// trips ("the collection was empty"). Other wrong hooks trip later assertions, not an MD: a
    /// blocking kind such as <c>PROFILE_CHANGE</c> trips the trigger-kind assertion; linking any event
    /// id but the <c>PeriodReopened</c> one trips the event-id assertion; writing through anything but
    /// the repository (no audited Created event) trips the event or audit-row counts.</para>
    /// </summary>
    [Fact]
    public async Task Reopen_ApprovedNotExportedSeptember_OctoberExported_WritesOctoberRow_PriorMonthReopened_Unblocked()
    {
        // The calendar premise, stated: 1 October 2026 is a Thursday, so October's first week is
        // Mon 28 September to Sun 4 October and holds three September days.
        Assert.Equal(DayOfWeek.Thursday, new DateOnly(2026, 10, 1).DayOfWeek);

        var september = await SeedMonthAsync(2026, 9, exported: false);
        var october = await SeedMonthAsync(2026, 10, exported: true);

        var reopenedEventId = await ReopenAsync(september.PeriodId, september.Start);

        using var doc = await GetWorklistAsync();
        var row = Assert.Single(doc.RootElement.EnumerateArray());

        Assert.Equal("EXPORTED_MONTH", row.GetProperty("kind").GetString());
        Assert.Equal(Emp, row.GetProperty("employeeId").GetString());
        Assert.Equal(2026, row.GetProperty("year").GetInt32());
        Assert.Equal(10, row.GetProperty("month").GetInt32());
        Assert.Equal(october.ExportId, row.GetProperty("exportId").GetGuid());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(1L, row.GetProperty("version").GetInt64());

        var trigger = Assert.Single(row.GetProperty("triggers").EnumerateArray());
        Assert.Equal(PriorMonthReopenedKind, trigger.GetProperty("kind").GetString());
        Assert.Equal(reopenedEventId, trigger.GetProperty("eventId").GetGuid());
        Assert.Equal(october.ContentHash, trigger.GetProperty("baselineContentHash").GetString());

        // Non-blocking, through the GET: no register id at row level, none at trigger level, and the
        // export has not moved since the baseline was captured.
        Assert.Equal(JsonValueKind.Array, row.GetProperty("recalcBlockedBy").ValueKind);
        Assert.Empty(row.GetProperty("recalcBlockedBy").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, trigger.GetProperty("recalcBlockedBy").ValueKind);
        Assert.False(trigger.GetProperty("recalculatedSince").GetBoolean());

        // Auditability: the row's creation is an event on the employee stream (ADR-018 D3) and an
        // ADR-026 audit row — an unaudited row would be a result nobody can reconstruct.
        var created = await CountAsync(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowCreated'
              AND event_payload ->> 'kind' = 'EXPORTED_MONTH'
              AND (event_payload ->> 'month')::int = 10
              AND event_payload ->> 'triggerKind' = @p1
              AND event_payload ->> 'triggerEventId' = @p2
              AND (event_payload ->> 'appended')::boolean = FALSE
            """, $"employee-{Emp}", PriorMonthReopenedKind, reopenedEventId.ToString());
        Assert.Equal(1, created);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowCreated' AND target_resource_id = @p0", Emp));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @p0", Emp));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  RO2 (guard) — October NOT exported: nothing
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Guard.</b> September is reopened while October is merely APPROVED, not exported. October's
    /// opening week still holds days of September, but October has not been sent to payroll, so its
    /// next export reads the dated history afresh and there is nothing stale to flag. The reopen
    /// answers 200, and NOTHING reaches the worklist (no row, no Created event). The approved
    /// October is deliberate: it separates "October is exported" (the condition) from "October
    /// exists" (a weaker one a wrong hook could key on).
    ///
    /// <para><b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). No MD — a guard,
    /// kept green by the hook's export condition. A hook that no longer requires M+1's export
    /// record (for instance one that fires on an approved or merely existing October) either writes
    /// an October row, which trips the empty-list assertion, or fails the row's export-id key and
    /// turns the reopen into a 5xx, which trips the 200 check.</para>
    /// </summary>
    [Fact]
    public async Task Reopen_ApprovedNotExportedSeptember_OctoberNotExported_WritesNothing()
    {
        // The calendar premise, stated: 1 October 2026 is a Thursday, so October's first week is
        // Mon 28 September to Sun 4 October and holds three September days.
        Assert.Equal(DayOfWeek.Thursday, new DateOnly(2026, 10, 1).DayOfWeek);

        var september = await SeedMonthAsync(2026, 9, exported: false);
        await SeedMonthAsync(2026, 10, exported: false); // approved, NOT sent to payroll

        await ReopenAsync(september.PeriodId, september.Start);

        await AssertNothingWrittenToWorklistAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  RO3 (R31c) — October AND November exported: October only
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R31c. September (approved, not exported) is reopened while BOTH October and November are
    /// exported. Exactly one row, for October: the reopen reaches the very next month only. The
    /// calendar is chosen to bite: October ends on Saturday 31 October, so its last week runs into
    /// 1 November, and a hook that wrote through the R5(a) widening (rounding the upper bound up to
    /// the next Monday) would reach 2 November and flag November too. R36 rules that the reopen hook
    /// does not inherit that widening.
    ///
    /// <para><b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). Two mutations,
    /// both tripping the same month-set assertion. <b>MD-48</b> (the hook removed; also TODAY's
    /// code): no row exists, expected <c>[(2026, 10)]</c>, actual <c>[]</c>. <b>MD-49</b> (the hook
    /// writes through the widened writer): November is flagged as well, expected
    /// <c>[(2026, 10)]</c>, actual <c>[(2026, 10), (2026, 11)]</c>.</para>
    /// </summary>
    [Fact]
    public async Task Reopen_ApprovedNotExportedSeptember_OctoberAndNovemberExported_WritesOctoberOnly()
    {
        // The calendar premise, stated: 1 October 2026 is a Thursday, so October's first week is
        // Mon 28 September to Sun 4 October and holds three September days.
        Assert.Equal(DayOfWeek.Thursday, new DateOnly(2026, 10, 1).DayOfWeek);
        // The premise that makes November reachable by a widened writer: October ends mid-week.
        Assert.Equal(DayOfWeek.Saturday, new DateOnly(2026, 10, 31).DayOfWeek);

        var september = await SeedMonthAsync(2026, 9, exported: false);
        var october = await SeedMonthAsync(2026, 10, exported: true);
        await SeedMonthAsync(2026, 11, exported: true);

        var reopenedEventId = await ReopenAsync(september.PeriodId, september.Start);

        using var doc = await GetWorklistAsync();
        var rows = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(
            new[] { (2026, 10) },
            rows.Select(r => (r.GetProperty("year").GetInt32(), r.GetProperty("month").GetInt32())).ToArray());

        // …and that one row is the real thing, not merely a row in the right month.
        var row = rows.Single();
        Assert.Equal("EXPORTED_MONTH", row.GetProperty("kind").GetString());
        Assert.Equal(october.ExportId, row.GetProperty("exportId").GetGuid());
        var trigger = Assert.Single(row.GetProperty("triggers").EnumerateArray());
        Assert.Equal(PriorMonthReopenedKind, trigger.GetProperty("kind").GetString());
        Assert.Equal(reopenedEventId, trigger.GetProperty("eventId").GetGuid());
        Assert.Empty(row.GetProperty("recalcBlockedBy").EnumerateArray());

        // The table agrees with the GET: no November row, open or resolved, hidden anywhere.
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @p0", Emp));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @p0 AND year = 2026 AND month = 11", Emp));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  RO4 (R31c guard) — May 2026 reopened, June exported, June starts on a Monday: nothing
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R31c <b>guard</b>. May 2026 (approved, not exported) is reopened while June is exported.
    /// 1 June 2026 is a Monday and 31 May a Sunday, so June's opening week (1–7 June) holds no May
    /// day: June's pay was computed without touching any May hours, and nothing about it can have
    /// gone stale. The reopen answers 200 and NOTHING reaches the worklist. This is the condition
    /// "M+1's opening week contains days of M" pinned on its own, separate from the export condition
    /// RO2 pins.
    ///
    /// <para><b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). No MD — a guard,
    /// kept green by the hook's opening-week condition. A hook that fires whenever M+1 is exported,
    /// whatever the calendar, writes a June row and trips the empty-list assertion.</para>
    /// </summary>
    [Fact]
    public async Task Reopen_ApprovedNotExportedMay2026_JuneExported_WritesNothing()
    {
        // The premise: June 2026 opens on a Monday, so no May day sits in its opening week.
        Assert.Equal(DayOfWeek.Monday, new DateOnly(2026, 6, 1).DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, new DateOnly(2026, 5, 31).DayOfWeek);

        var may = await SeedMonthAsync(2026, 5, exported: false);
        await SeedMonthAsync(2026, 6, exported: true);

        await ReopenAsync(may.PeriodId, may.Start);

        await AssertNothingWrittenToWorklistAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Shared assertions and flow
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reopens <paramref name="periodId"/> as the leader and PROVES the reopen happened (200, status
    /// DRAFT, exactly one <c>PeriodReopened</c> event) — so a "nothing was written" assertion after it
    /// can never pass on a refused or failed request. Returns the <c>PeriodReopened</c> event id.
    /// </summary>
    private async Task<Guid> ReopenAsync(Guid periodId, DateOnly periodStart)
    {
        var rsp = await LeaderClient().PostAsJsonAsync($"/api/approval/{periodId}/reopen", new { reason = "S145 pin" });
        var body = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"reopen answered {(int)rsp.StatusCode}: {body}");
        Assert.Equal("DRAFT", await ReadStatusAsync(periodId));

        var ids = await ReadGuidsAsync(
            "SELECT event_id FROM outbox_events WHERE stream_id = @p0 AND event_type = 'PeriodReopened'",
            $"approval-{Emp}-{periodStart:yyyy-MM-dd}");
        return Assert.Single(ids);
    }

    private async Task AssertNothingWrittenToWorklistAsync()
    {
        using var doc = await GetWorklistAsync();
        Assert.Empty(doc.RootElement.EnumerateArray());
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @p0", Emp));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowCreated'",
            $"employee-{Emp}"));
    }

    private async Task<JsonDocument> GetWorklistAsync()
    {
        var rsp = await AdminClient().GetAsync($"/api/hr/backdate-worklist?employeeId={Emp}");
        var body = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"worklist GET answered {(int)rsp.StatusCode}: {body}");
        var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind); // a BARE array, not an envelope
        return doc;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / SQL helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private sealed record SeededMonth(Guid PeriodId, DateOnly Start, Guid ExportId, string ContentHash);

    /// <summary>
    /// Seeds one whole calendar month for <see cref="Emp"/> as an APPROVED, MONTHLY approval period
    /// and, when <paramref name="exported"/>, its <c>payroll_export_records</c> row (the "sent to
    /// payroll" lock — the Payroll service is the real writer; the test stages the state directly)
    /// linked to that period with a distinct content hash. The approved-but-not-exported shape is
    /// exactly "September in the fact's story".
    /// </summary>
    private async Task<SeededMonth> SeedMonthAsync(int year, int month, bool exported)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var periodId = Guid.NewGuid();
        await ExecAsync(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
            VALUES
                (@p0, @p1, 'STY02', @p2, @p3, 'MONTHLY', 'APPROVED', 'HK', 'OK24', NOW(), @p1)
            """, periodId, Emp, start, end);

        var exportId = Guid.Empty;
        var hash = $"h-{year}-{month:00}";
        if (exported)
        {
            exportId = Guid.NewGuid();
            await ExecAsync(
                """
                INSERT INTO payroll_export_records
                    (export_id, period_id, employee_id, year, month, exported_at,
                     original_lines, current_effective_lines, content_hash, source)
                VALUES
                    (@p0, @p1, @p2, @p3, @p4, NOW(), '[]'::jsonb, '[]'::jsonb, @p5, 'CALCULATE_AND_EXPORT')
                """, exportId, periodId, Emp, year, month, hash);
        }

        return new SeededMonth(periodId, start, exportId, hash);
    }

    private async Task SeedUsersAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@emp, @emp, '$2a$11$fake', 'S145 Emp', 's145_emp@test.dk', 'STY02', 'HK', 'OK24', TRUE),
                (@mgr, @mgr, '$2a$11$fake', 'S145 Mgr', 's145_mgr@test.dk', 'STY02', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", Emp);
            cmd.Parameters.AddWithValue("mgr", Mgr);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                (@mgr, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@emp, 'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", Emp);
            cmd.Parameters.AddWithValue("mgr", Mgr);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the same-Organisation, same-tree edge that
        // grants the leader reopen (the ReopenPayrollLockTests topology).
        await new ReportingLineRepository(_dbFactory).AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = Emp,
            ManagerId = Mgr,
            OrganisationId = TreeRootSty02,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
    }

    private async Task<string> ReadStatusAsync(Guid periodId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<List<Guid>> ReadGuidsAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        var result = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetGuid(0));
        return result;
    }

    private async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  HTTP / token helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private HttpClient LeaderClient() => ClientWithToken(MintLeaderToken(Mgr, "STY02"));

    private HttpClient AdminClient() => ClientWithToken(MintGlobalAdminToken(AdminActor));

    private HttpClient ClientWithToken(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string MintLeaderToken(string userId, string orgId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
    }

    private static string MintGlobalAdminToken(string userId)
    {
        var scopes = new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") };
        return NewTokenService().GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: "STY02", scopes: scopes);
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });
}
