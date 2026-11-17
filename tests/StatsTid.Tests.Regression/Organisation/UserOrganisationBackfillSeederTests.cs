using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Organisation;

/// <summary>
/// S148 / TASK-14820-T (AC-C13l, plan-added; N (CI); M-41) — the startup backfill of the organisation
/// history, pinned directly.
///
/// <para>
/// <b>In plain language.</b> Every person who existed before the dated organisation (the seed and demo people
/// among them) gets, at startup, one first organisation row running from the start of history and holding
/// the institution they belong to today, with its audit row and its event. Switched-off people are skipped
/// and a second start adds nothing. The seeder is the only source of those people's rows, so a break in it
/// (here: it forgets the audit row) would otherwise surface only as a distant failure elsewhere.
/// </para>
///
/// <para>
/// <b>Fixture.</b> A raw schema, no host (so the host's own start-up run has not touched the users): the
/// harness's connection factory and event store feed <see cref="UserOrganisationBackfillSeeder.SeedAsync"/>
/// as in <c>UserAgreementCodeBackfillSeederTests</c>. The <c>init.sql</c> users are unseeded here too and are
/// seeded by the same call, so every assertion about "the people this fact made" filters by user id and the
/// count assertions compare tables with each other, not with a fixed number.
/// <b>Storage pitfall.</b> The seeder writes the start as a finite SQL date <c>0001-01-01</c>, which
/// Npgsql reads back as <see cref="DateOnly.MinValue"/>; the facts compare in C# and do not depend on whether
/// a column holds <c>-infinity</c> or <c>0001-01-01</c>. CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class UserOrganisationBackfillSeederTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>N.</b> Three active users without organisation rows (in STY01, STY02, STY03) and one inactive user;
    /// after <c>SeedAsync</c> each active user has exactly one open row from the start of history holding
    /// their <c>users.primary_org_id</c>, <b>one CREATED audit row by <c>SYSTEM_SEED</c></b>, and one
    /// <c>UserOrganisationSeeded</c> event on <c>user-{id}</c>; the inactive user has none of the three.
    /// <para><b>M-41 argument</b> ("the backfill seeder writes the row and the event but no audit row"):
    /// under M-41 each active user still has its row and its event, but the audit query returns 0, so
    /// <c>Assert.Equal(1L, auditCount)</c> (the first assertion on the audit table) fails: the must-trip
    /// cell (NR-P29). <b>Red conditions</b> (N, not F): the type exists; before C-13 there is no seeder
    /// and no table, so this cannot be run against that code; its RED is the mutation, not a revert.</para>
    /// </summary>
    [Fact]
    public async Task Backfill_ActiveUsersGetOneRowFrom00010101_WithAuditRowAndEvent_InactiveSkipped()
    {
        var cs = _harness.ConnectionString;
        var active = new[]
        {
            (Id: NewId("a"), Org: "STY01"),
            (Id: NewId("b"), Org: "STY02"),
            (Id: NewId("c"), Org: "STY03"),
        };
        var inactive = NewId("i");
        foreach (var u in active)
            await InsertUserAsync(cs, u.Id, u.Org, isActive: true);
        await InsertUserAsync(cs, inactive, "STY01", isActive: false);

        await UserOrganisationBackfillSeeder.SeedAsync(_harness.Factory, _harness.EventStore, NullLogger.Instance);

        foreach (var (id, org) in active)
        {
            var rows = await ReadRowsAsync(cs, id);
            var row = Assert.Single(rows);
            Assert.Equal((DateOnly.MinValue, (DateOnly?)null, org), (row.From, row.To, row.OrgId));

            Assert.Equal(1L, await CountAsync(cs,
                "SELECT COUNT(*) FROM user_organisations_audit WHERE user_id = @u AND action = 'CREATED' AND actor_id = 'SYSTEM_SEED'",
                id));
            Assert.Equal(1L, await CountAsync(cs,
                "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'UserOrganisationSeeded'",
                $"user-{id}"));
        }

        Assert.Empty(await ReadRowsAsync(cs, inactive));
        Assert.Equal(0L, await CountAsync(cs, "SELECT COUNT(*) FROM user_organisations_audit WHERE user_id = @u", inactive));
        Assert.Equal(0L, await CountAsync(cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", $"user-{inactive}"));
    }

    /// <summary>
    /// <b>N.</b> The idempotency shape of <c>UserAgreementCodeBackfillSeederTests</c>: after the first run
    /// the open organisation rows, the CREATED audit rows by <c>SYSTEM_SEED</c> and the
    /// <c>UserOrganisationSeeded</c> events are equal in number (and there is at least one); a second
    /// <c>SeedAsync</c> changes none of the three counts.
    /// <para><b>M-41 argument:</b> under M-41 the rows and events are written but no audit row, so the
    /// first <c>Assert.Equal(rows, auditRows)</c> fails: the must-trip cell (NR-P29). <b>Red conditions</b>
    /// (N): the mutation, not a revert.</para>
    /// </summary>
    [Fact]
    public async Task Backfill_SecondRun_AddsNothing()
    {
        var cs = _harness.ConnectionString;
        await InsertUserAsync(cs, NewId("s1"), "STY01", isActive: true);
        await InsertUserAsync(cs, NewId("s2"), "STY02", isActive: true);

        await UserOrganisationBackfillSeeder.SeedAsync(_harness.Factory, _harness.EventStore, NullLogger.Instance);

        var rows = await CountAsync(cs, "SELECT COUNT(*) FROM user_organisations WHERE effective_to IS NULL");
        var audit = await CountAsync(cs,
            "SELECT COUNT(*) FROM user_organisations_audit WHERE action = 'CREATED' AND actor_id = 'SYSTEM_SEED'");
        var events = await CountAsync(cs,
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'UserOrganisationSeeded'");
        Assert.True(rows >= 2, "the first run must have seeded at least the two users this fact made.");
        Assert.Equal(rows, audit);
        Assert.Equal(rows, events);

        await UserOrganisationBackfillSeeder.SeedAsync(_harness.Factory, _harness.EventStore, NullLogger.Instance);

        Assert.Equal(rows, await CountAsync(cs, "SELECT COUNT(*) FROM user_organisations WHERE effective_to IS NULL"));
        Assert.Equal(audit, await CountAsync(cs,
            "SELECT COUNT(*) FROM user_organisations_audit WHERE action = 'CREATED' AND actor_id = 'SYSTEM_SEED'"));
        Assert.Equal(events, await CountAsync(cs,
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'UserOrganisationSeeded'"));
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static string NewId(string tag) => $"usr_s148_seed_{tag}_" + Guid.NewGuid().ToString("N").Substring(0, 6);

    private static async Task InsertUserAsync(string cs, string userId, string orgId, bool isActive)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@u, @u, 'dev-only', 'S148 Seeder Test User', NULL, @o, 'AC', 'OK24', @a)
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("o", orgId);
        cmd.Parameters.AddWithValue("a", isActive);
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed record Row(DateOnly From, DateOnly? To, string OrgId);

    private static async Task<IReadOnlyList<Row>> ReadRowsAsync(string cs, string userId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT effective_from, effective_to, org_id FROM user_organisations WHERE user_id = @u ORDER BY effective_from",
            conn);
        cmd.Parameters.AddWithValue("u", userId);
        var rows = new List<Row>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new Row(
                reader.GetFieldValue<DateOnly>(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2)));
        }
        return rows;
    }

    private static async Task<long> CountAsync(string cs, string sql, string? parameter = null)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant at every call site; values go
        // through parameters, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        if (parameter is not null)
        {
            // The one named parameter is @u (a user id) or @s (a stream id), whichever the SQL names.
            cmd.Parameters.AddWithValue(sql.Contains("@s", StringComparison.Ordinal) ? "s" : "u", parameter);
        }
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
