using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S144 / TASK-14401 — schema pins for the S144 widening of <c>hr_backdate_worklist</c>, run on the
/// FULL canonical schema (<see cref="StatsTidWebApplicationFactory.ApplyFullSchemaAsync"/>).
///
/// <para><b>Plain language.</b> S144 lets HR/Global Admin record a third resolution verb,
/// <c>HANDLED_MANUALLY</c>, and makes every resolution remember WHICH known limitation
/// (<c>QUAL-149</c>) blocked an automatic recalculation at that moment
/// (<c>resolution_blocked_by TEXT[]</c>; the empty set <c>{}</c> means "nothing blocked"). The
/// database itself must enforce the shape, so no code path can write a half-recorded resolution:
/// a resolved row MUST carry a stamp (possibly empty), an open row MUST NOT.</para>
///
/// <para>Spec (refinement rulings R3): named CHECK <c>hr_backdate_worklist_resolution_check</c>
/// accepts exactly <c>RECALCULATED</c>, <c>DISMISSED</c>, <c>HANDLED_MANUALLY</c>;
/// <c>hr_backdate_worklist_resolution_paired</c> requires the stamp NULL iff the row is open.</para>
///
/// <para><b>Red conditions (all speak SQL only, so they compile today and fail on behaviour).</b>
/// (1) Leave the verb CHECK at the two S138 verbs → the HANDLED_MANUALLY insert throws 23514 and
/// the acceptance fact fails. (2) Leave <c>_paired</c> unwidened → the resolved-with-NULL-stamp
/// insert is ACCEPTED and no 23514 is thrown; likewise the open-with-a-stamp insert. (3) Rename the
/// pairing constraint → the <c>_paired</c> <c>ConstraintName</c> assertions fail; rename the verb
/// CHECK away from <c>hr_backdate_worklist_resolution_check</c> → the unknown-verb name assertion
/// fails (Postgres' auto-name for the old inline column CHECK is the same string, so that assertion
/// pins the name while the HANDLED_MANUALLY acceptance pins the widening).
/// (4) Drop <c>resolution_blocked_by</c> → every insert throws 42703, failing the acceptance fact and
/// turning the 23514 facts into the wrong SqlState. (5) Drop the verb CHECK entirely → the
/// unknown-verb insert is accepted.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class BackdateWorklistS144SchemaTests : IAsyncLifetime
{
    private const string Employee = "emp_s144_schema";
    private const string OneTriggerJson =
        """[{"kind":"PROFILE_CHANGE","eventId":"11111111-1111-1111-1111-111111111111","effectiveFrom":"2026-03-10","appendedAt":"2026-09-03T08:00:00+00:00","actorId":"hr01","baselineContentHash":"h1"}]""";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, Employee, "STY_S144_SCHEMA");
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task ResolvedRow_HandledManually_WithStamp_IsAccepted_NonEmptyAndEmpty()
    {
        // A non-empty block set.
        await InsertResolvedAsync(month: 1, resolution: "HANDLED_MANUALLY", stampSql: "'{QUAL-149}'::text[]");
        // The empty set is a REAL value ("nothing blocked"), distinct from NULL ("still open").
        await InsertResolvedAsync(month: 2, resolution: "HANDLED_MANUALLY", stampSql: "'{}'::text[]");

        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE month = 1 AND resolution = 'HANDLED_MANUALLY' AND resolution_blocked_by = '{QUAL-149}'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE month = 2 AND resolution = 'HANDLED_MANUALLY' AND resolution_blocked_by = '{}'"));

        // The two S138 verbs stay accepted (with a stamp).
        await InsertResolvedAsync(month: 3, resolution: "RECALCULATED", stampSql: "'{}'::text[]");
        await InsertResolvedAsync(month: 4, resolution: "DISMISSED", stampSql: "'{QUAL-149}'::text[]");
        Assert.Equal(4, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));
    }

    [Fact]
    public async Task ResolvedRow_WithNullStamp_IsRefused_23514_OnEveryVerb()
    {
        var month = 1;
        foreach (var verb in new[] { "RECALCULATED", "DISMISSED", "HANDLED_MANUALLY" })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(
                () => InsertResolvedAsync(month++, verb, stampSql: "NULL"));
            Assert.Equal("23514", ex.SqlState);
            Assert.Equal("hr_backdate_worklist_resolution_paired", ex.ConstraintName);
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));
    }

    [Fact]
    public async Task OpenRow_WithAStamp_IsRefused_23514_NonEmptyAndEmpty()
    {
        // The stamp is NULL iff the row is open — even the empty set is a stamp.
        foreach (var (month, stamp) in new[] { (1, "'{QUAL-149}'::text[]"), (2, "'{}'::text[]") })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await ExecAsync(
                    $"""
                    INSERT INTO hr_backdate_worklist
                        (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by, resolution_blocked_by)
                    VALUES (@p0, '{Employee}', 'EXPORTED_MONTH', 2026, {month}, @p1, @p2::jsonb, 'hr01', {stamp})
                    """, Guid.NewGuid(), Guid.NewGuid(), OneTriggerJson);
            });
            Assert.Equal("23514", ex.SqlState);
            Assert.Equal("hr_backdate_worklist_resolution_paired", ex.ConstraintName);
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));
    }

    [Fact]
    public async Task ResolvedRow_UnknownVerb_IsRefused_23514_ByTheNamedVerbCheck()
    {
        // A fully well-formed resolved row (stamp present) whose ONLY defect is the verb.
        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => InsertResolvedAsync(month: 1, resolution: "IGNORED", stampSql: "'{}'::text[]"));
        Assert.Equal("23514", ex.SqlState);
        Assert.Equal("hr_backdate_worklist_resolution_check", ex.ConstraintName);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist"));
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private Task InsertResolvedAsync(int month, string resolution, string stampSql) =>
        // CA2100-justified (QUAL-073 ratchet): every interpolated fragment is a compile-time test
        // constant (an int, a verb literal, a stamp literal) — never user input.
        ExecAsync(
            $"""
            INSERT INTO hr_backdate_worklist
                (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by,
                 resolved_at, resolved_by, resolution, resolution_reason, resolution_blocked_by)
            VALUES (@p0, '{Employee}', 'EXPORTED_MONTH', 2026, {month}, @p1, @p2::jsonb, 'hr01',
                    NOW(), 'hr01', '{resolution}', 'S144 schema pin', {stampSql})
            """, Guid.NewGuid(), Guid.NewGuid(), OneTriggerJson);

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
