using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Organisation;

/// <summary>
/// S148 / TASK-14820-T (AC-C13f Docker half; AC-C13i) — a transfer dated into the past tells HR.
///
/// <para>
/// <b>In plain language.</b> If HR moves someone from a date inside a month that payroll has already
/// received, that month lands on HR's correction worklist with the reason "organisation transfer"; if the
/// date falls in a holiday year that has already been settled, that year is flagged. Nothing is
/// recalculated automatically (no payroll block, no holiday-balance revaluation): HR decides.
/// </para>
///
/// <para>
/// <b>Fixture.</b> Fixed clock Tuesday 10 November 2026; a <see cref="TestSupport.RegressionSeed"/> person
/// in STY01 moved to STY02 by a GlobalAdmin. The month and the settlement are seeded by SQL as
/// <c>ProfileBackdatingEndpointTests</c> does. Docker-gated and CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TransferHandOffTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _host = _factory.WithFixedToday(Clock);
        _ = _host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>F.</b> June 2026 was exported for the person; a move STY01 → STY02 dated Monday 25 May 2026
    /// lands ONE EXPORTED_MONTH row for (2026, 6) whose single trigger is <c>ORGANISATION_TRANSFER</c>,
    /// dated 2026-05-25, carrying the transfer event's id; and the row derives no recalculation block.
    /// <para><b>Red conditions.</b> Before C-13 a transfer writes no worklist row: the
    /// <c>Assert.Single</c> on the EXPORTED_MONTH rows fails (and <c>ORGANISATION_TRANSFER</c> is not
    /// even a known trigger kind).</para>
    /// </summary>
    [Fact]
    public async Task TransferBeforeExportedMonth_RowWithTransferTrigger_NoBlock()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01");
        await SeedExportRecordAsync(cs, userId, 2026, 6);

        var rsp = await PutUserAsync(GlobalAdminClient(_host), cs, userId,
            new { primaryOrgId = "STY02", effectiveFrom = "2026-05-25" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var row = Assert.Single(await ReadWorklistRowsAsync(cs, userId, "EXPORTED_MONTH"));
        Assert.Equal((2026, 6), (row.Year, row.Month));
        var trigger = Assert.Single(row.Triggers);
        Assert.Equal("ORGANISATION_TRANSFER", trigger.GetProperty("kind").GetString());
        Assert.Equal("2026-05-25", trigger.GetProperty("effectiveFrom").GetString());
        var evt = Assert.Single(await ReadEventsAsync(cs, userId, "UserOrganisationTransferred"));
        Assert.Equal(evt.EventId, trigger.GetProperty("eventId").GetGuid());
        Assert.Null(BackdateWorklistDerivation.RecalcBlockedByForTrigger(
            "ORGANISATION_TRANSFER", new DateOnly(2026, 5, 25), 2026, 6));
    }

    /// <summary>
    /// <b>F.</b> The person's VACATION year 2025 is settled (an ACTIVE YEAR_END settlement whose valuation
    /// boundary is 2026-12-31, the <c>ProfileBackdatingEndpointTests</c> shape); a move dated Monday
    /// 1 June 2026 lands ONE SETTLED_YEAR row (VACATION, 2025) with the <c>ORGANISATION_TRANSFER</c>
    /// trigger dated 2026-06-01. No <c>EntitlementBalanceRevalued</c> event is written and the person's
    /// balance row keeps its exact values (total 25, used 5).
    /// <para><b>Red conditions.</b> Before C-13 a transfer writes no worklist row: the
    /// <c>Assert.Single</c> on the SETTLED_YEAR rows fails.</para>
    /// </summary>
    [Fact]
    public async Task TransferIntoSettledYear_SettledYearRow_NoRevaluation()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01");
        await ExecAsync(cs,
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot)
            VALUES (@e, 'VACATION', 2025, 1, 'SETTLED', 'YEAR_END',
                    jsonb_build_object('settlementBoundaryDate', '2026-12-31'))
            """, ("e", userId));
        await ExecAsync(cs,
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used)
            VALUES (gen_random_uuid(), @e, 'VACATION', 2025, 25, 5)
            """, ("e", userId));

        var rsp = await PutUserAsync(GlobalAdminClient(_host), cs, userId,
            new { primaryOrgId = "STY02", effectiveFrom = "2026-06-01" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var row = Assert.Single(await ReadWorklistRowsAsync(cs, userId, "SETTLED_YEAR"));
        Assert.Equal(("VACATION", 2025), (row.EntitlementType, row.EntitlementYear));
        var trigger = Assert.Single(row.Triggers);
        Assert.Equal("ORGANISATION_TRANSFER", trigger.GetProperty("kind").GetString());
        Assert.Equal("2026-06-01", trigger.GetProperty("effectiveFrom").GetString());

        Assert.Equal(0L, await ScalarLongAsync(cs,
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'EntitlementBalanceRevalued'"));
        Assert.Equal((25m, 5m), await ReadBalanceAsync(cs, userId));
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private sealed record WorklistRow(
        int? Year, int? Month, string? EntitlementType, int? EntitlementYear, IReadOnlyList<JsonElement> Triggers);

    private static async Task<IReadOnlyList<WorklistRow>> ReadWorklistRowsAsync(string cs, string employeeId, string kind)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT year, month, entitlement_type, entitlement_year, triggers::text
            FROM hr_backdate_worklist
            WHERE employee_id = @e AND kind = @k ORDER BY created_at, worklist_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("k", kind);
        var rows = new List<WorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var triggers = JsonSerializer.Deserialize<JsonElement>(reader.GetString(4)).EnumerateArray().ToList();
            rows.Add(new WorklistRow(
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                triggers));
        }
        return rows;
    }

    private static Task SeedExportRecordAsync(string cs, string employeeId, int year, int month) =>
        ExecAsync(cs,
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month,
                 original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @e, @y, @m, '[]'::jsonb, '[]'::jsonb, @hash)
            """, ("e", employeeId), ("y", year), ("m", month), ("hash", $"hash-{year}-{month}"));

    private static async Task ExecAsync(string cs, string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant at every call site; values go
        // through parameters, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(decimal Total, decimal Used)> ReadBalanceAsync(string cs, string employeeId)
    {
        await using var conn = new NpgsqlConnection(cs);
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
}
