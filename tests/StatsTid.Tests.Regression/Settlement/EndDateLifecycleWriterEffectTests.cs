using System.Data;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7104 fix-forward (Step-5a cycle-1 NOTE; SPRINT-71 R4 — the
/// ONE-lifecycle-write-implementation rule). Docker-gated pins on the SHARED
/// <see cref="EmploymentEndDateLifecycleWriter"/>'s FULL WRITE-EFFECT SET, driven directly
/// (conn/tx + the R12 employee advisory lock per the caller contract) — not through the PUT:
///
/// <list type="bullet">
///   <item><description>the versioned user write outcome (the persisted lifecycle tuple + the
///   ADR-018 D7 version bump);</description></item>
///   <item><description>the R10 <c>EmployeeEmploymentEndDateSet</c> payload on
///   <c>employee-{id}</c> incl. the FULL version pair and old/new transition;</description></item>
///   <item><description>the <c>users_audit</c> UPDATED row shape (the lifecycle-tuple
///   previous/new JSON fields, version pair, operator actor);</description></item>
///   <item><description>the ADR-026 <c>audit_projection</c> row;</description></item>
///   <item><description>the R1(e) <c>ReportingLineManagerDeactivated</c> emission for a
///   DEACTIVATING flip — one per ACTIVE managed line, none for expired lines, none on a
///   non-deactivating write.</description></item>
/// </list>
///
/// <para><b>Why this suite exists (the Step-5a NOTE):</b> during the transitional window the
/// lifecycle-write choreography exists TWICE (this writer + the S70 PUT handler), and the unit
/// parity suite (<c>EndDateLifecycleWriterParityTests</c>) only pins the PURE decision — it
/// cannot discriminate reporting-line emissions, audit rows, or version-pair drift. This suite
/// pins the WRITER's effects directly against the same contract
/// <c>EmploymentEndDateLifecycleTests</c> pins for the PUT. TRUE two-writer parity becomes
/// by-construction at TASK-7102's refactor, when the PUT delegates to this writer and the
/// second implementation disappears — at that point these tests keep guarding the single
/// shared implementation.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EndDateLifecycleWriterEffectTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string OperatorId = "hr_s71_writer_op";
    private const string OperatorRole = "LocalHR";

    /// <summary>The Copenhagen business date passed to ApplyAsync (PAT-008: the writer takes
    /// the resolved date — no TimeProvider seam needed at this level).</summary>
    private static readonly DateOnly Clock = new(2026, 3, 5);

    /// <summary>Already passed at <see cref="Clock"/> ⇒ the R1(a) deactivating flip.</summary>
    private static readonly DateOnly PassedEndDate = new(2026, 2, 28);

    /// <summary>Future at <see cref="Clock"/> ⇒ R1(b) store-only, no flip.</summary>
    private static readonly DateOnly FutureEndDate = new(2026, 12, 31);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1(a) deactivating flip — the FULL write-effect set in one tx.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeactivatingFlip_FullWriteEffectSet()
    {
        var leaver = await SeedEmployeeAsync();
        var reportA = await SeedEmployeeAsync();
        var reportB = await SeedEmployeeAsync();
        var formerReport = await SeedEmployeeAsync();
        await SeedReportingLineAsync(employeeId: reportA, managerId: leaver);
        await SeedReportingLineAsync(employeeId: reportB, managerId: leaver);
        // An EXPIRED line (effective_to set) must NOT receive an R1(e) emission.
        await SeedReportingLineAsync(employeeId: formerReport, managerId: leaver,
            effectiveTo: new DateOnly(2025, 12, 31));

        var result = await ApplyInOwnTxAsync(leaver, PassedEndDate, expectedUserVersion: 1);

        // (1) The structured outcome: the version pair + the full old/new lifecycle tuple.
        Assert.Equal(1L, result.VersionBefore);
        Assert.Equal(2L, result.VersionAfter);
        Assert.Null(result.OldEndDate);
        Assert.True(result.OldIsActive);
        Assert.False(result.OldEndDateDeactivated);
        Assert.False(result.NewIsActive);
        Assert.True(result.NewEndDateDeactivated);

        // (2) The versioned user write outcome — the persisted tuple matches the decision.
        var user = await ReadUserTupleAsync(leaver);
        Assert.Equal(PassedEndDate, user.EndDate);
        Assert.False(user.IsActive);
        Assert.True(user.EndDateDeactivated);
        Assert.Equal(2L, user.Version);

        // (3) The R10 event payload — full transition incl. the version pair, operator actor.
        var payload = await ReadLatestOutboxPayloadAsync(
            $"employee-{leaver}", "EmployeeEmploymentEndDateSet");
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            var root = doc.RootElement;
            Assert.Equal(leaver, root.GetProperty("employeeId").GetString());
            Assert.False(root.TryGetProperty("oldEndDate", out var oldEnd)
                         && oldEnd.ValueKind != JsonValueKind.Null);
            Assert.Equal(PassedEndDate.ToString("yyyy-MM-dd"), root.GetProperty("newEndDate").GetString());
            Assert.True(root.GetProperty("oldIsActive").GetBoolean());
            Assert.False(root.GetProperty("newIsActive").GetBoolean());
            Assert.Equal(1L, root.GetProperty("versionBefore").GetInt64());
            Assert.Equal(2L, root.GetProperty("versionAfter").GetInt64());
            Assert.Equal(OperatorId, root.GetProperty("actorId").GetString());
            Assert.Equal(OperatorRole, root.GetProperty("actorRole").GetString());
        }

        // (4) The ADR-026 audit-projection row.
        Assert.Equal(1L, await CountAsync(
            "audit_projection",
            "event_type = 'EmployeeEmploymentEndDateSet' AND target_resource_id = @r AND target_org_id = @o",
            ("r", leaver), ("o", OrgId)));

        // (5) The users_audit UPDATED row — the full lifecycle-tuple before/after shape,
        // version pair, operator actor.
        var audit = await ReadUsersAuditAsync(leaver);
        Assert.Equal("UPDATED", audit.Action);
        Assert.Equal(1L, audit.VersionBefore);
        Assert.Equal(2L, audit.VersionAfter);
        Assert.Equal(OperatorId, audit.ActorId);
        Assert.Equal(OperatorRole, audit.ActorRole);
        using (var prev = JsonDocument.Parse(audit.PreviousData))
        {
            Assert.Equal(JsonValueKind.Null, prev.RootElement.GetProperty("employmentEndDate").ValueKind);
            Assert.False(prev.RootElement.GetProperty("endDateDeactivated").GetBoolean());
            Assert.True(prev.RootElement.GetProperty("isActive").GetBoolean());
        }
        using (var next = JsonDocument.Parse(audit.NewData))
        {
            Assert.Equal(PassedEndDate.ToString("yyyy-MM-dd"),
                next.RootElement.GetProperty("employmentEndDate").GetString());
            Assert.True(next.RootElement.GetProperty("endDateDeactivated").GetBoolean());
            Assert.False(next.RootElement.GetProperty("isActive").GetBoolean());
        }

        // (6) R1(e) — ONE ReportingLineManagerDeactivated per ACTIVE managed line, on the
        // reporting-line-{employee} stream, none for the expired line.
        foreach (var report in new[] { reportA, reportB })
        {
            var sideEffect = await ReadLatestOutboxPayloadAsync(
                $"reporting-line-{report}", "ReportingLineManagerDeactivated");
            Assert.NotNull(sideEffect);
            using var doc = JsonDocument.Parse(sideEffect!);
            Assert.Equal(leaver, doc.RootElement.GetProperty("managerId").GetString());
            Assert.Equal(report, doc.RootElement.GetProperty("employeeId").GetString());
            Assert.Equal(OperatorId, doc.RootElement.GetProperty("actorId").GetString());
        }
        Assert.Equal(0L, await CountAsync(
            "outbox_events", "stream_id = @s AND event_type = 'ReportingLineManagerDeactivated'",
            ("s", $"reporting-line-{formerReport}")));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1(b) future-dated store-only — versioned write + event + audit, but NO flip and
    // NO R1(e) emission (the deactivation side effect is strictly flip-gated).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NonDeactivatingWrite_NoReportingLineEmission_VersionPairStillExact()
    {
        var leaver = await SeedEmployeeAsync();
        var report = await SeedEmployeeAsync();
        await SeedReportingLineAsync(employeeId: report, managerId: leaver);

        var result = await ApplyInOwnTxAsync(leaver, FutureEndDate, expectedUserVersion: 1);

        Assert.Equal(1L, result.VersionBefore);
        Assert.Equal(2L, result.VersionAfter);
        Assert.True(result.NewIsActive);
        Assert.False(result.NewEndDateDeactivated);

        var user = await ReadUserTupleAsync(leaver);
        Assert.Equal(FutureEndDate, user.EndDate);
        Assert.True(user.IsActive);
        Assert.False(user.EndDateDeactivated);
        Assert.Equal(2L, user.Version);

        // No flip ⇒ no R1(e) side effect; the R10 event + audit rows still land exactly once.
        Assert.Equal(0L, await CountAsync(
            "outbox_events", "stream_id = @s AND event_type = 'ReportingLineManagerDeactivated'",
            ("s", $"reporting-line-{report}")));
        var payload = await ReadLatestOutboxPayloadAsync(
            $"employee-{leaver}", "EmployeeEmploymentEndDateSet");
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            Assert.True(doc.RootElement.GetProperty("oldIsActive").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("newIsActive").GetBoolean());
            Assert.Equal(1L, doc.RootElement.GetProperty("versionBefore").GetInt64());
            Assert.Equal(2L, doc.RootElement.GetProperty("versionAfter").GetInt64());
        }
        var audit = await ReadUsersAuditAsync(leaver);
        Assert.Equal("UPDATED", audit.Action);
        Assert.Equal(1L, audit.VersionBefore);
        Assert.Equal(2L, audit.VersionAfter);
    }

    // ─────────────────────────────── drive ───────────────────────────────

    /// <summary>Drives <see cref="EmploymentEndDateLifecycleWriter.ApplyAsync"/> per its caller
    /// contract: own committed ReadCommitted tx with the R12 employee advisory lock acquired
    /// FIRST (the same key every settlement-family writer takes).</summary>
    private async Task<EmploymentEndDateLifecycleResult> ApplyInOwnTxAsync(
        string employeeId, DateOnly? newEndDate, long expectedUserVersion)
    {
        var writer = _factory.Services.GetRequiredService<EmploymentEndDateLifecycleWriter>();
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx))
            {
                lockCmd.Parameters.AddWithValue("employeeId", employeeId);
                await lockCmd.ExecuteScalarAsync();
            }
            var result = await writer.ApplyAsync(
                conn, tx, employeeId, newEndDate, expectedUserVersion,
                OperatorId, OperatorRole, OrgId, correlationId: null, Clock);
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync();
            throw;
        }
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s71_wfx_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task SeedReportingLineAsync(
        string employeeId, string managerId, DateOnly? effectiveTo = null)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, organisation_id, relationship,
                 effective_from, effective_to, created_by)
            VALUES (@employeeId, @managerId, @treeRoot, 'PRIMARY', @from, @to, 'test_s71_wfx')
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("managerId", managerId);
        cmd.Parameters.AddWithValue("treeRoot", OrgId);
        cmd.Parameters.AddWithValue("from", new DateOnly(2024, 1, 1));
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(DateOnly? EndDate, bool EndDateDeactivated, bool IsActive, long Version)>
        ReadUserTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_end_date, end_date_deactivated, is_active, version
            FROM users WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"user row expected for {employeeId}");
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
    }

    private sealed record UsersAuditRow(
        string Action, string PreviousData, string NewData,
        long VersionBefore, long VersionAfter, string ActorId, string ActorRole);

    private async Task<UsersAuditRow> ReadUsersAuditAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, previous_data::text, new_data::text,
                   version_before, version_after, actor_id, actor_role
            FROM users_audit
            WHERE user_id = @id AND action = 'UPDATED'
            ORDER BY audit_id DESC
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"users_audit UPDATED row expected for {employeeId}");
        var row = new UsersAuditRow(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetString(5), reader.GetString(6));
        Assert.False(await reader.ReadAsync(),
            "exactly ONE users_audit UPDATED row expected (one write, one audit row)");
        return row;
    }

    private async Task<string?> ReadLatestOutboxPayloadAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId AND event_type = @eventType
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        cmd.Parameters.AddWithValue("eventType", eventType);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private async Task<long> CountAsync(string table, string whereClause, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table} WHERE {whereClause}", conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
