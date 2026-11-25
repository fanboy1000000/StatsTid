using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Worklist;

/// <summary>
/// S148 / TASK-14851-T (AC-Q268g; QUAL-268) - re-creating a profile into a month payroll has already recorded tells HR.
///
/// <para>
/// <b>In plain language.</b> When HR closes a profile, a month payroll already recorded lands on HR's worklist (the close's own hand-off,
/// <see cref="ProfileCloseHandOffTests"/>). When HR then re-creates a profile dated inside that month, the figures sent for the month may
/// change again, so the same worklist row gains a second reason, "profile change", pointing at the create event. It does not gain an
/// "employment category change" reason: the re-create keeps the category the person already had.
/// </para>
///
/// <para>
/// <b>Fixture.</b> A Backend host with the clock fixed at Tuesday 31 March 2026 and no leaver-flip job (the close hand-off's fixture).
/// The actor is a LocalHR scoped to STY01 and is not the subject. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileRecreateHandOffTests : IAsyncLifetime
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
    /// <b>F (CI).</b> The clock fixed at Tuesday 31 March 2026; March 2026 exported; the close answers 204 (March listed with one
    /// <c>PROFILE_CHANGE</c> trigger naming the <c>EmployeeProfileSoftDeleted</c> event); the re-create dated 2026-03-31 answers 200. Still
    /// <b>one</b> EXPORTED_MONTH row for (2026, 3), now with <b>two</b> triggers, the second <c>PROFILE_CHANGE</c> with <c>effectiveFrom</c>
    /// 2026-03-31 naming the <c>EmployeeProfileCreated</c> event's id; no SETTLED_YEAR row; no <c>EMPLOYMENT_CATEGORY_CHANGE</c> trigger.
    /// <para><b>Product code between the readings:</b> the close and its hand-off, the re-create's create branch, routed write and worklist call.</para>
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428, so the 200 assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task RecreateOnTue31Mar2026_MarchExported_ListsMarch_ProfileChangeTrigger_CreatedEvent()
    {
        var emp = await SeedPersonAsync(Cs, "STY01");
        await SeedExportRecordAsync(emp, 2026, 3);
        var client = LocalHrClient(_host, "hr_s148_q268_hand_off", "STY01");

        var close = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{emp}");
        close.Headers.TryAddWithoutValidation("If-Match", "\"1\""); // the seed leaves users.version at 1
        using (var closed = await client.SendAsync(close))
            Assert.True(closed.StatusCode == HttpStatusCode.NoContent,
                $"the close: {(int)closed.StatusCode}: {await closed.Content.ReadAsStringAsync()}");
        var afterClose = Assert.Single(await ReadRowsAsync(emp));
        var closeTrigger = Assert.Single(afterClose.Triggers);
        Assert.Equal("PROFILE_CHANGE", closeTrigger.Kind);

        var create = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{emp}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom = "2026-03-31",
                partTimeFraction = 1.000m,
                position = (string?)null,
                employmentCategory = (string?)null,
            }),
        };
        create.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using (var created = await client.SendAsync(create))
            Assert.True(created.StatusCode == HttpStatusCode.OK,
                $"the re-create: {(int)created.StatusCode}: {await created.Content.ReadAsStringAsync()}");

        var rows = await ReadRowsAsync(emp);
        var march = Assert.Single(rows);
        Assert.Equal("EXPORTED_MONTH", march.Kind);
        Assert.Equal((2026, 3), (march.Year, march.Month));
        Assert.Equal(2, march.Triggers.Count);
        Assert.Equal((closeTrigger.Kind, closeTrigger.EventId), (march.Triggers[0].Kind, march.Triggers[0].EventId));
        var second = march.Triggers[1];
        Assert.Equal("PROFILE_CHANGE", second.Kind);
        Assert.Equal(new DateOnly(2026, 3, 31), second.EffectiveFrom);
        Assert.Equal(await CreatedEventIdAsync(emp), second.EventId);
        Assert.DoesNotContain(rows, r => r.Kind == "SETTLED_YEAR");
        Assert.DoesNotContain(march.Triggers, t => t.Kind == "EMPLOYMENT_CATEGORY_CHANGE");
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

    private async Task<Guid> CreatedEventIdAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id FROM outbox_events
            WHERE stream_id = @s AND event_type = 'EmployeeProfileCreated' ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-profile-{employeeId}");
        var value = await cmd.ExecuteScalarAsync();
        Assert.True(value is Guid, $"expected an EmployeeProfileCreated event on employee-profile-{employeeId}.");
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
