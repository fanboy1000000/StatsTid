using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S97 / TASK-9707 / ADR-035 (Step-0b BLOCKER A) — the Enhed migration "no user loses metadata" +
/// idempotency D-test for <see cref="EnhedBackfillSeeder.SeedAsync"/>. The seeder migrates the legacy
/// free-text <c>employee_profiles.enhed_label</c> projection column into the structured
/// <c>enheder</c> table + the <c>user_enheder</c> link, via the dedicated <c>EnhedCreated</c> /
/// <c>UserEnhederChanged</c> events.
///
/// <para>
/// <b>The CI greenfield baseline is all-NULL <c>enhed_label</c> by design</b> (S92 deliberately did not
/// pre-seed it; <c>EmployeeProfileSeeder</c> inserts NULL), so a run against the bare init.sql baseline
/// migrates ZERO and proves nothing about the migration path. This test therefore SEEDS its own labeled
/// live profiles (non-blank <c>enhed_label</c>, ORGANISATION primary_org) so the guarantee is
/// non-vacuous, then asserts:
/// <list type="number">
///   <item>ONE active enhed per DISTINCT (org, label);</item>
///   <item>ONE <c>user_enheder</c> row per labeled user (no labeled user loses metadata);</item>
///   <item>RE-RUN is idempotent — no duplicate enheder, no duplicate tags, no duplicate events.</item>
/// </list>
/// </para>
///
/// <para>Idioms mirror <see cref="StatsTid.Tests.Regression.UserAgreementCode.UserAgreementCodeBackfillSeederTests"/>
/// (boot the WAF, then drive the seeder explicitly).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S97EnhedBackfillSeederTests : IAsyncLifetime
{
    private const string Sty01 = "STY01"; // ORGANISATION under MAO MIN01
    private const string Sty02 = "STY02"; // a DIFFERENT ORGANISATION under MAO MIN01

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private EnhedRepository _enhedRepo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot the org tree (MIN01/STY01/STY02) + the (no-op greenfield) seeder
        _enhedRepo = new EnhedRepository(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Seeds 3 labeled users across 2 Organisations with 2 distinct labels in STY01 (two users share
    /// "Drift", one carries "Udvikling") + one labeled user in STY02 ("Drift" again — a SAME label name
    /// in a DIFFERENT org, which must be a SEPARATE enhed). After the backfill:
    /// <list type="bullet">
    ///   <item>3 active enheder exist — (STY01,'Drift'), (STY01,'Udvikling'), (STY02,'Drift') — the
    ///         same-name-different-org pair is NOT merged;</item>
    ///   <item>every labeled user has exactly ONE <c>user_enheder</c> row tagging the matching enhed;</item>
    ///   <item>a SECOND <c>SeedAsync</c> is a no-op: enhed count, tag count, and the EnhedCreated /
    ///         UserEnhederChanged event counts are all UNCHANGED.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Backfill_MigratesLabelsToEnheder_AndIsIdempotent()
    {
        // ── Seed labeled live profiles (the CI baseline is all-NULL → seed our own). ──
        var u1 = await SeedLabeledUserAsync("s97seed_a", Sty01, "Drift");
        var u2 = await SeedLabeledUserAsync("s97seed_b", Sty01, "Drift");      // shares STY01/Drift with u1
        var u3 = await SeedLabeledUserAsync("s97seed_c", Sty01, "Udvikling");  // distinct STY01 label
        var u4 = await SeedLabeledUserAsync("s97seed_d", Sty02, "Drift");      // SAME name, DIFFERENT org
        var labeledUsers = new[] { u1, u2, u3, u4 };

        // ── 1st backfill. ──
        await RunBackfillAsync();

        // One active enhed per DISTINCT (org, label) — 3 of them (STY02/Drift is NOT merged with STY01/Drift).
        Assert.Equal(1, await CountActiveEnhederAsync(Sty01, "Drift"));
        Assert.Equal(1, await CountActiveEnhederAsync(Sty01, "Udvikling"));
        Assert.Equal(1, await CountActiveEnhederAsync(Sty02, "Drift"));

        var enhederAfterFirst = await CountSeededEnhederAsync(labeledUsers);
        Assert.Equal(3, enhederAfterFirst);

        // Every labeled user has exactly ONE tag pointing at the matching (org, label) enhed.
        foreach (var (userId, orgId, label) in new[]
                 {
                     (u1, Sty01, "Drift"), (u2, Sty01, "Drift"),
                     (u3, Sty01, "Udvikling"), (u4, Sty02, "Drift"),
                 })
        {
            var ids = await _enhedRepo.GetUserActiveEnhedIdsAsync(userId);
            Assert.Single(ids);
            Assert.Equal(await ActiveEnhedIdAsync(orgId, label), ids[0]);
        }

        // Snapshot the post-first-run event counts.
        var createdEventsAfterFirst = await CountEventTypeAsync("EnhedCreated");
        var taggedEventsAfterFirst = await CountEventStreamPrefixAsync("UserEnhederChanged", labeledUsers);

        // ── 2nd backfill — must be a pure no-op. ──
        await RunBackfillAsync();

        // No duplicate enheder.
        Assert.Equal(1, await CountActiveEnhederAsync(Sty01, "Drift"));
        Assert.Equal(1, await CountActiveEnhederAsync(Sty01, "Udvikling"));
        Assert.Equal(1, await CountActiveEnhederAsync(Sty02, "Drift"));
        Assert.Equal(3, await CountSeededEnhederAsync(labeledUsers));

        // No duplicate tags (still exactly one per labeled user).
        foreach (var userId in labeledUsers)
            Assert.Single(await _enhedRepo.GetUserActiveEnhedIdsAsync(userId));

        // No duplicate events.
        Assert.Equal(createdEventsAfterFirst, await CountEventTypeAsync("EnhedCreated"));
        Assert.Equal(taggedEventsAfterFirst, await CountEventStreamPrefixAsync("UserEnhederChanged", labeledUsers));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seeding + DB reads
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Drives <see cref="EnhedBackfillSeeder.SeedAsync"/> with the DI-resolved
    /// <see cref="IOutboxEnqueue"/> (the concrete <c>PostgresEventStore</c> bound with its
    /// <c>OutboxServiceContext</c> per ADR-018 D3 — the harness-constructed event store lacks that
    /// context, so we resolve from the booted WAF's container).</summary>
    private async Task RunBackfillAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxEnqueue>();
        await EnhedBackfillSeeder.SeedAsync(
            _harness.Factory, outbox, _enhedRepo, NullLogger.Instance);
    }

    /// <summary>Seeds a fresh user with a live (effective_to NULL) <c>employee_profiles</c> row carrying
    /// a non-blank <c>enhed_label</c> on an ORGANISATION-typed org. Reuses <see cref="RegressionSeed"/>
    /// for the users/profile/agreement-code triple, then sets the label on the profile.</summary>
    private async Task<string> SeedLabeledUserAsync(string baseId, string orgId, string label)
    {
        var userId = baseId + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, userId, orgId, "AC", "OK24", ensureOrg: false);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE employee_profiles SET enhed_label = @label WHERE employee_id = @u AND effective_to IS NULL",
            conn);
        cmd.Parameters.AddWithValue("label", label);
        cmd.Parameters.AddWithValue("u", userId);
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task<int> CountActiveEnhederAsync(string orgId, string name)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM enheder WHERE organisation_id = @org AND lower(name) = lower(@name) AND deleted_at IS NULL",
            conn);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("name", name);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<Guid> ActiveEnhedIdAsync(string orgId, string name)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT enhed_id FROM enheder WHERE organisation_id = @org AND lower(name) = lower(@name) AND deleted_at IS NULL",
            conn);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("name", name);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Counts the DISTINCT active enheder tagged onto the supplied labeled users (so the count is
    /// scoped to THIS test's seed, not any baseline rows).</summary>
    private async Task<int> CountSeededEnhederAsync(string[] userIds)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(DISTINCT ue.enhed_id)
            FROM user_enheder ue
            JOIN enheder e ON e.enhed_id = ue.enhed_id
            WHERE ue.user_id = ANY(@ids) AND e.deleted_at IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("ids", userIds);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountEventTypeAsync(string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = @t", conn);
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountEventStreamPrefixAsync(string eventType, string[] userIds)
    {
        var streams = userIds.Select(u => $"user-{u}").ToArray();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = @t AND stream_id = ANY(@s)", conn);
        cmd.Parameters.AddWithValue("t", eventType);
        cmd.Parameters.AddWithValue("s", streams);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
