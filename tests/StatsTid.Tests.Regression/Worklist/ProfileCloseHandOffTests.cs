using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Worklist;

/// <summary>
/// S148 / TASK-14821-T (AC-PIL-g (3), plan-added; B-4; BC-5, ruled 1.7) - closing a profile tells HR.
///
/// <para>
/// <b>In plain language.</b> Closing a profile ends, from today, the record of a person's hours fraction, position
/// and category. If payroll has already recorded the current month, the figures sent may be stale, so that month
/// now lands on HR's worklist under the profile-change reason, dated today and pointing at the close event. This is
/// the recording-first half of AC-PIL-g, pinned in the wave that builds the call rather than waiting for wave 5's
/// race tests (which are TASK-14831-T's, AC-PIL-g (1) and (2)).
/// </para>
///
/// <para>
/// <b>Fixture.</b> A Backend host with the clock fixed at Tuesday 31 March 2026 and no leaver-flip job. The actor is a
/// LocalHR scoped to STY01 and is not the subject. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileCloseHandOffTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 3, 31);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    private string Cs => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
        _factory = new StatsTidWebApplicationFactory(Cs, withoutLeaverJob: true);
        _host = _factory.WithFixedToday(Today);
        _ = _host.CreateClient(); // boot seeders before any fact seeds (PAT-008)
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>F (CI).</b> The clock fixed at Tuesday 31 March 2026; March 2026 exported; <c>DELETE
    /// /api/admin/employee-profiles/{id}</c> answers 204: one EXPORTED_MONTH row (2026, 3) with a single trigger of kind
    /// <c>PROFILE_CHANGE</c> (a literal), <c>effectiveFrom</c> 2026-03-31, <c>eventId</c> the
    /// <c>EmployeeProfileSoftDeleted</c> event's id (read from <c>outbox_events</c> on <c>employee-profile-{id}</c>); no
    /// SETTLED_YEAR row.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> The profile close writes no worklist row at all:
    /// <c>Assert.Single(rows)</c> fails with zero rows. A close that raised a settled-year row too (the call was ruled
    /// out, TASK-14821 § 6) would fail the zero-SETTLED_YEAR assertion.</para>
    /// </summary>
    [Fact]
    public async Task ProfileCloseOnTue31Mar2026_MarchExported_ListsMarch_ProfileChangeTrigger_SoftDeletedEvent()
    {
        var emp = await SeedPersonAsync(Cs, "STY01");
        await SeedExportRecordAsync(emp, 2026, 3);
        var client = LocalHrClient(_host, "hr_s148_close_hand_off", "STY01");

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{emp}");
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\""); // the seed leaves users.version at 1
        var rsp = await client.SendAsync(req);

        Assert.True(rsp.StatusCode == HttpStatusCode.NoContent,
            $"expected 204, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");

        var rows = await ReadRowsAsync(emp);
        var march = Assert.Single(rows);
        Assert.Equal("EXPORTED_MONTH", march.Kind);
        Assert.Equal((2026, 3), (march.Year, march.Month));
        var trigger = Assert.Single(march.Triggers);
        Assert.Equal("PROFILE_CHANGE", trigger.Kind);
        Assert.Equal(new DateOnly(2026, 3, 31), trigger.EffectiveFrom);
        Assert.Equal(await SoftDeletedEventIdAsync(emp), trigger.EventId);
        Assert.DoesNotContain(rows, r => r.Kind == "SETTLED_YEAR");
    }

    private sealed record WorklistRow(string Kind, int? Year, int? Month, IReadOnlyList<StoredWorklistTrigger> Triggers);

    private async Task<IReadOnlyList<WorklistRow>> ReadRowsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT kind, year, month, triggers::text FROM hr_backdate_worklist WHERE employee_id = @e ORDER BY created_at, worklist_id",
            conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var rows = new List<WorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WorklistRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                WorklistTriggerJson.Deserialize(reader.GetString(3))));
        }
        return rows;
    }

    private async Task<Guid> SoftDeletedEventIdAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id FROM outbox_events
            WHERE stream_id = @s AND event_type = 'EmployeeProfileSoftDeleted' ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-profile-{employeeId}");
        var value = await cmd.ExecuteScalarAsync();
        Assert.True(value is Guid, $"expected an EmployeeProfileSoftDeleted event on employee-profile-{employeeId}.");
        return (Guid)value!;
    }

    private async Task SeedExportRecordAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month,
                 original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @e, @y, @m, '[]'::jsonb, '[]'::jsonb, @hash)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        cmd.Parameters.AddWithValue("hash", $"hash-{year}-{month}");
        await cmd.ExecuteNonQueryAsync();
    }
}
