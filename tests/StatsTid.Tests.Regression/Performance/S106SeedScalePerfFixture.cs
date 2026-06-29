using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Performance;

/// <summary>
/// SPRINT-106 / TASK-10605 — the shared, expensive seed for <see cref="S106SeedScalePerfTests"/>. Boots
/// ONE Postgres testcontainer, applies the full <c>init.sql</c> schema, and bulk-seeds a
/// Demoministeriet-scale dataset ONCE for all the perf measurements (xUnit <c>IClassFixture</c>):
///
/// <list type="bullet">
///   <item>ONE MAO (<c>PERF_MAO</c>) over FIVE Organisations <c>PERF_O1..PERF_O5</c> sized
///     2000 / 600 / 250 / 250 / 250 = <b>3350 active users</b> (the DemoSeed <c>full</c> per-tree
///     targets).</item>
///   <item>A typed unit tree of <b>depth 5</b> per Organisation (direktion → omrade → kontor → team →
///     enhed; 15 units/org = 75 units), with ~80% of users homed in a leaf <c>enhed</c> and ~20% homed
///     directly at the Organisation.</item>
///   <item>A base approval scenario in <c>PERF_O3</c>: an edge manager + a leaf unit carrying two
///     designated leaders, so <see cref="AddPendingScenarioAsync"/> can attach K pending employees
///     (each → 3 candidate approvers) for the tile-count N+1 characterization.</item>
/// </list>
///
/// <para>Bulk SQL (<c>generate_series</c>) — fast + deterministic — so the perf test exercises the reads
/// at realistic volume WITHOUT the slow API-driven DemoSeed loader (the task's "OR run the DemoSeed"
/// latitude). The init.sql baseline demo tree is also present and harmless (the perf reads measure the
/// bounded-round-trips property, not exact totals; the helpers below count the PERF_* scope).</para>
/// </summary>
public sealed class S106SeedScalePerfFixture : IAsyncLifetime
{
    public const string Mao = "PERF_MAO";
    public const string Org1 = "PERF_O1";
    public const string Org3 = "PERF_O3";
    public const string Org1Path = "/PERF_MAO/PERF_O1/";
    public const string Org3Path = "/PERF_MAO/PERF_O3/";

    // The PERF_O3 base approval scenario (created in the seed; pending employees attach to it).
    private const string Org3EdgeManager = "perf_o3_em";
    private const string Org3Leader1 = "perf_o3_l1";
    private const string Org3Leader2 = "perf_o3_l2";
    private const string PendingPrefix = "perf_o3_p"; // disjoint from bulk "perf_o3_<digit>" + scenario _em/_l1/_l2

    // (orgId, shortLabel, targetUsers) — the DemoSeed `full` per-tree sizing.
    private static readonly (string Org, string Short, int Target)[] OrgPlan =
    {
        (Org1, "O1", 2000),
        ("PERF_O2", "O2", 600),
        (Org3, "O3", 250),
        ("PERF_O4", "O4", 250),
        ("PERF_O5", "O5", 250),
    };

    private TestFixtures.DockerHarness _harness = null!;
    private Guid _org3LeafUnit;

    public string ConnectionString { get; private set; } = null!;
    public int Port { get; private set; }
    public DbConnectionFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        ConnectionString = _harness.ConnectionString;
        Port = new NpgsqlConnectionStringBuilder(ConnectionString).Port;
        Factory = new DbConnectionFactory(ConnectionString);

        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Seed
    // ════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        // (1) MAO + the five Organisations.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('PERF_MAO','Perf Demoministeriet','MAO',          NULL,      '/PERF_MAO/',         'AC','OK24'),
                ('PERF_O1', 'Perf Org 1',           'ORGANISATION','PERF_MAO','/PERF_MAO/PERF_O1/', 'HK','OK24'),
                ('PERF_O2', 'Perf Org 2',           'ORGANISATION','PERF_MAO','/PERF_MAO/PERF_O2/', 'AC','OK24'),
                ('PERF_O3', 'Perf Org 3',           'ORGANISATION','PERF_MAO','/PERF_MAO/PERF_O3/', 'AC','OK24'),
                ('PERF_O4', 'Perf Org 4',           'ORGANISATION','PERF_MAO','/PERF_MAO/PERF_O4/', 'HK','OK24'),
                ('PERF_O5', 'Perf Org 5',           'ORGANISATION','PERF_MAO','/PERF_MAO/PERF_O5/', 'AC','OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // (2) The depth-5 unit tree per Organisation + the bulk users assigned to its leaf units.
        foreach (var (org, shortLabel, target) in OrgPlan)
        {
            var (units, leaves) = BuildUnitTree(org, shortLabel);
            await InsertUnitsAsync(conn, units);
            await InsertBulkUsersAsync(conn, org, shortLabel, target, leaves);
            if (org == Org3)
                _org3LeafUnit = leaves[0];
        }

        // (3) The PERF_O3 base approval scenario: an edge manager (org-homed) + a leaf unit with two
        //     designated leaders. Pending employees attach to these via AddPendingScenarioAsync.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id, unit_id, is_active, agreement_code, ok_version) VALUES
                (@em, @em, '$2a$11$fake', 'Perf O3 EdgeMgr', 'PERF_O3', NULL,    TRUE, 'AC','OK24'),
                (@l1, @l1, '$2a$11$fake', 'Perf O3 Leader1', 'PERF_O3', @leaf,   TRUE, 'AC','OK24'),
                (@l2, @l2, '$2a$11$fake', 'Perf O3 Leader2', 'PERF_O3', @leaf,   TRUE, 'AC','OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("em", Org3EdgeManager);
            cmd.Parameters.AddWithValue("l1", Org3Leader1);
            cmd.Parameters.AddWithValue("l2", Org3Leader2);
            cmd.Parameters.AddWithValue("leaf", _org3LeafUnit);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
                (@em, 'LOCAL_LEADER', 'PERF_O3', 'ORG_ONLY', 'PERF'),
                (@l1, 'LOCAL_LEADER', 'PERF_O3', 'ORG_ONLY', 'PERF'),
                (@l2, 'LOCAL_LEADER', 'PERF_O3', 'ORG_ONLY', 'PERF')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("em", Org3EdgeManager);
            cmd.Parameters.AddWithValue("l1", Org3Leader1);
            cmd.Parameters.AddWithValue("l2", Org3Leader2);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO unit_leaders (unit_id, user_id) VALUES (@leaf, @l1), (@leaf, @l2)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("leaf", _org3LeafUnit);
            cmd.Parameters.AddWithValue("l1", Org3Leader1);
            cmd.Parameters.AddWithValue("l2", Org3Leader2);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Builds a balanced depth-5 unit tree (direktion → 2 omrade → 4 kontor → 4 team →
    /// 4 enhed = 15 units) for one Organisation. Returns the rows + the leaf (enhed) unit ids.</summary>
    private static (List<SeedUnit> Units, List<Guid> Leaves) BuildUnitTree(string org, string shortLabel)
    {
        var units = new List<SeedUnit>();
        var leaves = new List<Guid>();
        SeedUnit Add(Guid? parent, string type, string name)
        {
            var u = new SeedUnit(Guid.NewGuid(), org, parent, type, $"Perf {shortLabel} {name}");
            units.Add(u);
            return u;
        }

        var dir = Add(null, "direktion", "Direktion");
        for (var a = 1; a <= 2; a++)
        {
            var omrade = Add(dir.Id, "omrade", $"Omraade {a}");
            for (var k = 1; k <= 2; k++)
            {
                var kontor = Add(omrade.Id, "kontor", $"Kontor {a}-{k}");
                var team = Add(kontor.Id, "team", $"Team {a}-{k}");
                var enhed = Add(team.Id, "enhed", $"Enhed {a}-{k}");
                leaves.Add(enhed.Id);
            }
        }
        return (units, leaves);
    }

    private static async Task InsertUnitsAsync(NpgsqlConnection conn, List<SeedUnit> units)
    {
        foreach (var u in units)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name)
                VALUES (@id, @org, @parent, @type, @name)
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("id", u.Id);
            cmd.Parameters.AddWithValue("org", u.Org);
            cmd.Parameters.AddWithValue("parent", (object?)u.Parent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("type", u.Type);
            cmd.Parameters.AddWithValue("name", u.Name);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>Bulk-inserts <paramref name="target"/> active users for one Organisation via
    /// <c>generate_series</c>: ~80% round-robin across the org's leaf units, ~20% homed directly at the
    /// Organisation (<c>unit_id</c> NULL). Display names carry "Perf {short}" so the search term "Perf"
    /// matches them.</summary>
    private static async Task InsertBulkUsersAsync(
        NpgsqlConnection conn, string org, string shortLabel, int target, List<Guid> leaves)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id, unit_id, is_active, agreement_code, ok_version)
            SELECT @prefix || g, @prefix || g, '$2a$11$fake', @dname || ' ' || g, @org,
                   CASE WHEN g % 5 = 0 THEN NULL ELSE (@leaves)[1 + (g % @nleaves)] END,
                   TRUE, 'HK', 'OK24'
            FROM generate_series(1, @target) g
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("prefix", $"perf_{shortLabel.ToLowerInvariant()}_");
        cmd.Parameters.AddWithValue("dname", $"Perf {shortLabel}");
        cmd.Parameters.AddWithValue("org", org);
        cmd.Parameters.Add(new NpgsqlParameter("leaves", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = leaves.ToArray() });
        cmd.Parameters.AddWithValue("nleaves", leaves.Count);
        cmd.Parameters.AddWithValue("target", target);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Pending-scenario mutators (PERF_O3) for the tile-count characterization
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Ensures EXACTLY <paramref name="count"/> pending employees exist under the PERF_O3 leaf
    /// unit: each homed in the 2-leader unit, with an active PRIMARY edge to the edge manager and a
    /// SUBMITTED approval period. Candidate approvers per pending employee = {edge manager, leader1,
    /// leader2} = 3. Idempotent (clears first, then inserts the requested count).</summary>
    public async Task AddPendingScenarioAsync(int count)
    {
        await ClearPendingScenarioAsync();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        var periodEnd = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var periodStart = periodEnd.AddDays(-30);

        for (var i = 1; i <= count; i++)
        {
            var pid = $"{PendingPrefix}{i}";
            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id, unit_id, is_active, agreement_code, ok_version)
                VALUES (@id, @id, '$2a$11$fake', 'Perf O3 Pending ' || @id, 'PERF_O3', @leaf, TRUE, 'AC','OK24');
                INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
                VALUES (@id, 'EMPLOYEE', 'PERF_O3', 'ORG_ONLY', 'PERF');
                INSERT INTO reporting_lines (employee_id, manager_id, organisation_id, relationship, effective_from, source, created_by)
                VALUES (@id, @em, 'PERF_O3', 'PRIMARY', '2026-01-01', 'MANUAL', 'PERF');
                INSERT INTO approval_periods
                    (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, submitted_at, submitted_by)
                VALUES (gen_random_uuid(), @id, 'PERF_O3', @start, @end, 'MONTHLY', 'SUBMITTED', 'AC','OK24', NOW(), @id);
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", pid);
                cmd.Parameters.AddWithValue("leaf", _org3LeafUnit);
                cmd.Parameters.AddWithValue("em", Org3EdgeManager);
                cmd.Parameters.AddWithValue("start", periodStart);
                cmd.Parameters.AddWithValue("end", periodEnd);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    /// <summary>Removes every pending-scenario user (and its period/edge/role) — FK-safe order. The base
    /// scenario (edge manager + the two leaders + the unit) persists.</summary>
    public async Task ClearPendingScenarioAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            DELETE FROM approval_periods WHERE employee_id LIKE 'perf_o3_p%';
            DELETE FROM reporting_lines WHERE employee_id LIKE 'perf_o3_p%' OR manager_id LIKE 'perf_o3_p%';
            DELETE FROM role_assignments WHERE user_id LIKE 'perf_o3_p%';
            DELETE FROM users WHERE user_id LIKE 'perf_o3_p%';
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Count helpers
    // ════════════════════════════════════════════════════════════════════════

    public async Task<int> CountActiveUsersInOrgAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE primary_org_id = @org AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("org", orgId);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<int> CountAllActiveUsersAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE is_active = TRUE", conn);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }

    private sealed record SeedUnit(Guid Id, string Org, Guid? Parent, string Type, string Name);
}
