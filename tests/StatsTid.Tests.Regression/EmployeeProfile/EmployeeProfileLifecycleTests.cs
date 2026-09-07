using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S33 / TASK-3312 — Phase 4d-3 Part 2 D-test suite for the employee-profile
/// versioning lifecycle introduced this sprint:
///
/// <list type="bullet">
///   <item>
///     <b>SupersedeAndCreateAsync 3-case routing (TASK-3302 / ADR-020 D2).</b>
///     Repo-direct tests for Case A (no live row → INSERT),
///     Case B (same-day in-place edit → version+1),
///     Case C (cross-day edit → close predecessor + INSERT successor).
///   </item>
///   <item>
///     <b>SoftDeleteAsync (TASK-3303 / ADR-023 D8).</b> Predecessor version
///     UNCHANGED on soft-delete; stale-If-Match-after-delete returns 404 NOT
///     412 (row-disappearance idempotency); audit row carries
///     version_before == version_after == predecessor.version.
///   </item>
///   <item>
///     <b>PUT endpoint cross-day routing (TASK-3308).</b> Endpoint emits
///     EmployeeProfileSuperseded (NOT EmployeeProfileUpdated) when routing
///     fires Case C; audit row action = 'SUPERSEDED'.
///   </item>
///   <item>
///     <b>PUT validator (TASK-3308 / ADR-023 D8 narrowing).</b> Both
///     backdated AND future-dated <c>EffectiveFrom</c> → 422 with structured
///     <c>provided</c>/<c>expected</c> body.
///   </item>
///   <item>
///     <b>DELETE endpoint admin-strict If-Match (TASK-3308 / ADR-019 D2).</b>
///     412 on stale; 428 on missing.
///   </item>
///   <item>
///     <b>Consumption fail-modes (TASK-3306 / TASK-3307 / ADR-023 D3).</b>
///     Compliance fail-closed → 500; Balance graceful-fallback → 200 with
///     central-config WeeklyNormHours.
///   </item>
///   <item>
///     <b>Audit-action enum coherence (TASK-3308 / init.sql:514).</b>
///     SoftDelete audit row carries action='DELETED' (matches the existing
///     CHECK constraint), NOT 'SOFT_DELETED' which would violate the
///     constraint.
///   </item>
/// </list>
///
/// <para>
/// HTTP-level tests use the WAF&lt;Program&gt; harness from S27
/// (<see cref="StatsTidWebApplicationFactory"/>). Repo-direct tests use the
/// raw <see cref="EmployeeProfileRepository"/> against the per-test
/// container — same pattern as
/// <see cref="StatsTid.Tests.Regression.Concurrency.AgreementConfigConcurrencyTests"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeProfileLifecycleTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey =
        "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private EmployeeProfileRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // CreateClient triggers Program.cs host build → EmployeeProfileSeeder
        // backfills one live profile row per seed user (admin01/hr01/mgr01/
        // ladm01/emp001/emp002/emp003). Subsequent direct DB writes via the
        // repo use the same connection factory the WAF host uses.
        _ = _factory.CreateClient();
        _repo = new EmployeeProfileRepository(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // SupersedeAndCreateAsync 3-case routing (TASK-3302 / ADR-020 D2)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Case A — no live row. expectedVersion=null lets the repo INSERT a
    /// fresh row at version=1, effective_from = request date,
    /// effective_to = NULL. Outcome must be Created.
    /// </summary>
    [Fact]
    public async Task SupersedeAndCreate_CaseA_NoLiveRow_Inserts()
    {
        // Create a fresh user without a profile row by DELETE-ing the
        // backfilled row (the seeder filled in admin01..emp003 + any fresh
        // user added by AdminEndpoints POST). We bypass the POST and use a
        // raw insert + raw delete to leave a row-less employee.
        var employeeId = await CreateUserWithoutProfileAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var req = new EmployeeProfileSupersedeRequest(
            EmployeeId: employeeId,
            PartTimeFraction: 0.800m,
            Position: "Specialist",
            EffectiveFrom: today);

        var result = await _repo.SupersedeAndCreateAsync(conn, tx, req, expectedVersion: null);
        await tx.CommitAsync();

        Assert.Equal(SaveEmployeeProfileOutcome.Created, result.Outcome);
        Assert.Equal(1L, result.Version);

        // Verify the row is the one the repo claims (and only one live row).
        await using var verifyConn = _harness.Factory.Create();
        await verifyConn.OpenAsync();
        // S53/TASK-5306 (a7aee58): employee_profiles.weekly_norm_hours removed
        // (universal 37h norm; only part_time_fraction varies per employee).
        // Column dropped from SELECT; downstream ordinals shift down by one.
        await using var checkCmd = new NpgsqlCommand(
            """
            SELECT version, part_time_fraction, position, effective_from, effective_to
            FROM employee_profiles
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, verifyConn);
        checkCmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await checkCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Case A should have inserted a live row.");
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(0.800m, reader.GetDecimal(1));
        Assert.Equal("Specialist", reader.GetString(2));
        Assert.Equal(today, reader.GetFieldValue<DateOnly>(3));
        Assert.True(reader.IsDBNull(4));
        Assert.False(await reader.ReadAsync(), "Exactly one live row expected.");
    }

    /// <summary>
    /// Case B — same-day in-place edit. Predecessor's effective_from equals
    /// request.EffectiveFrom → UPDATE in place, version bumps from N → N+1,
    /// profile_id and effective_from unchanged.
    /// </summary>
    [Fact]
    public async Task SupersedeAndCreate_CaseB_SameDayEdit_UpdatesInPlace_BumpsVersion()
    {
        // Build a row at effective_from = today (the only way for Case B to
        // fire on a same-day edit). Use the repo's Case A path to set this
        // up cleanly.
        var employeeId = await CreateUserWithoutProfileAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid initialProfileId;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var seedReq = new EmployeeProfileSupersedeRequest(
                EmployeeId: employeeId,
                PartTimeFraction: 1.000m,
                Position: null,
                EffectiveFrom: today);
            var seedResult = await _repo.SupersedeAndCreateAsync(conn, tx, seedReq, expectedVersion: null);
            initialProfileId = seedResult.ProfileId;
            Assert.Equal(SaveEmployeeProfileOutcome.Created, seedResult.Outcome);
            await tx.CommitAsync();
        }

        // Same-day edit — Case B.
        SaveEmployeeProfileResult editResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var editReq = new EmployeeProfileSupersedeRequest(
                EmployeeId: employeeId,
                PartTimeFraction: 0.750m,
                Position: "Department Head",
                EffectiveFrom: today);
            editResult = await _repo.SupersedeAndCreateAsync(conn, tx, editReq, expectedVersion: 1L);
            await tx.CommitAsync();
        }

        Assert.Equal(SaveEmployeeProfileOutcome.Updated, editResult.Outcome);
        Assert.Equal(2L, editResult.Version);
        // Case B preserves the predecessor's profile_id.
        Assert.Equal(initialProfileId, editResult.ProfileId);

        // Verify: still exactly one live row, effective_from unchanged, version=2.
        await using var verifyConn = _harness.Factory.Create();
        await verifyConn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT version, effective_from, profile_id
            FROM employee_profiles
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, verifyConn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(today, reader.GetFieldValue<DateOnly>(1));
        Assert.Equal(initialProfileId, reader.GetGuid(2));
    }

    /// <summary>
    /// Case C — cross-day edit. Predecessor's effective_from is earlier than
    /// request.EffectiveFrom → close the predecessor (effective_to =
    /// request.EffectiveFrom, version unchanged) AND insert a new live row
    /// at version=1 with a fresh profile_id.
    /// </summary>
    [Fact]
    public async Task SupersedeAndCreate_CaseC_CrossDayEdit_ClosesPredecessorInsertsSuccessor()
    {
        // S33 in-flight defect fix: post the S33 seeder change (CreateAsync /
        // AdminEndpoints POST / EmployeeProfileSeeder all stamp effective_from
        // = today instead of the schema DEFAULT '0001-01-01'), the backfilled
        // emp001 row is at effective_from = today. To exercise Case C cross-day
        // routing this test must explicitly backdate the predecessor's
        // effective_from to a past date (yesterday) via direct SQL so the
        // SupersedeAndCreateAsync(EffectiveFrom=today) call sees a strictly-
        // less-than predecessor and routes to Case C.
        const string employeeId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        Guid predecessorProfileId;
        long predecessorVersion;
        DateOnly predecessorEffectiveFrom;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            // Backdate the predecessor to force Case C routing.
            await using (var backdateCmd = new NpgsqlCommand(
                """
                UPDATE employee_profiles
                   SET effective_from = @yesterday
                 WHERE employee_id = @employeeId AND effective_to IS NULL
                """, conn))
            {
                backdateCmd.Parameters.AddWithValue("employeeId", employeeId);
                backdateCmd.Parameters.AddWithValue("yesterday", yesterday);
                await backdateCmd.ExecuteNonQueryAsync();
            }
            await using var preCmd = new NpgsqlCommand(
                """
                SELECT profile_id, version, effective_from
                FROM employee_profiles
                WHERE employee_id = @employeeId AND effective_to IS NULL
                """, conn);
            preCmd.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await preCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            predecessorProfileId = reader.GetGuid(0);
            predecessorVersion = reader.GetInt64(1);
            predecessorEffectiveFrom = reader.GetFieldValue<DateOnly>(2);
        }

        // Case C edit.
        SaveEmployeeProfileResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var req = new EmployeeProfileSupersedeRequest(
                EmployeeId: employeeId,
                PartTimeFraction: 0.800m,
                Position: "Specialist",
                EffectiveFrom: today);
            result = await _repo.SupersedeAndCreateAsync(
                conn, tx, req, expectedVersion: predecessorVersion);
            await tx.CommitAsync();
        }

        Assert.Equal(SaveEmployeeProfileOutcome.Superseded, result.Outcome);
        // Step 7a P1 absorption: Case C successor inherits predecessor.Version + 1
        // (ETag monotonicity across supersession; ADR-019 D2 contract holds).
        Assert.Equal(predecessorVersion + 1, result.Version);
        Assert.NotEqual(predecessorProfileId, result.ProfileId);

        // Verify: closed predecessor row carries effective_to=today + version unchanged.
        await using (var verifyConn = _harness.Factory.Create())
        {
            await verifyConn.OpenAsync();
            await using var closedCmd = new NpgsqlCommand(
                """
                SELECT version, effective_from, effective_to
                FROM employee_profiles
                WHERE profile_id = @profileId
                """, verifyConn);
            closedCmd.Parameters.AddWithValue("profileId", predecessorProfileId);
            await using var reader = await closedCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(predecessorVersion, reader.GetInt64(0));
            Assert.Equal(predecessorEffectiveFrom, reader.GetFieldValue<DateOnly>(1));
            Assert.Equal(today, reader.GetFieldValue<DateOnly>(2));
        }

        // Verify: new live row at effective_from=today + version=predecessor+1.
        await using (var verifyConn = _harness.Factory.Create())
        {
            await verifyConn.OpenAsync();
            // S53/TASK-5306 (a7aee58): employee_profiles.weekly_norm_hours
            // removed (universal 37h norm); trailing column + its assert dropped.
            await using var liveCmd = new NpgsqlCommand(
                """
                SELECT version, effective_from, effective_to
                FROM employee_profiles
                WHERE employee_id = @employeeId AND effective_to IS NULL
                """, verifyConn);
            liveCmd.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await liveCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            // Step 7a P1 absorption: Case C successor row inherits predecessor.Version + 1
            Assert.Equal(predecessorVersion + 1, reader.GetInt64(0));
            Assert.Equal(today, reader.GetFieldValue<DateOnly>(1));
            Assert.True(reader.IsDBNull(2));
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // SoftDelete (TASK-3303 / ADR-023 D8)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Audit row written by the DELETE endpoint carries
    /// version_before == version_after == predecessor.version per ADR-023 D8
    /// (soft-delete does NOT bump the version — row-state-change, not
    /// field-mutation). The repo's SoftDeleteAsync returns the predecessor's
    /// (unchanged) version; the endpoint stamps both audit columns from that
    /// single value.
    /// </summary>
    [Fact]
    public async Task SoftDelete_PredecessorVersionUnchanged_AuditRowHasVersionBeforeEqualsVersionAfter()
    {
        const string employeeId = "emp001";
        var client = AuthorizedClient();

        // Capture predecessor version (= 1 from seeder).
        var getRsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var predecessorVersion = (await getRsp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt64();

        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", $"\"{predecessorVersion}\"");
        var delRsp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        // Predecessor row: version unchanged.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var rowCmd = new NpgsqlCommand(
            """
            SELECT version
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_to IS NOT NULL
            ORDER BY effective_to DESC
            LIMIT 1
            """, conn))
        {
            rowCmd.Parameters.AddWithValue("employeeId", employeeId);
            var stored = (long)(await rowCmd.ExecuteScalarAsync())!;
            Assert.Equal(predecessorVersion, stored);
        }

        // Audit row: action='DELETED', version_before = version_after = predecessor.version.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT version_before, version_after
            FROM employee_profile_audit
            WHERE employee_id = @employeeId AND action = 'DELETED'
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "Expected a DELETED audit row.");
            Assert.Equal(predecessorVersion, reader.GetInt64(0));
            Assert.Equal(predecessorVersion, reader.GetInt64(1));
        }
    }

    /// <summary>
    /// Stale If-Match retry after a successful soft-delete returns 404 (NOT
    /// 412) per ADR-023 D8 row-disappearance idempotency — the partial-
    /// unique-index <c>WHERE effective_to IS NULL</c> matches no row, so the
    /// repo's UPDATE returns 0 rows and the probe sees no live row →
    /// KeyNotFoundException → endpoint maps to 404. Deliberate divergence
    /// from sibling ADR-019 D8 endpoints which bump version + return 412.
    /// </summary>
    [Fact]
    public async Task SoftDelete_StaleIfMatchAfterSoftDelete_Returns404NotConflict412()
    {
        const string employeeId = "emp001";
        var client = AuthorizedClient();

        var delReq1 = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq1.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var delRsp1 = await client.SendAsync(delReq1);
        Assert.Equal(HttpStatusCode.NoContent, delRsp1.StatusCode);

        // Retry the same DELETE with the SAME If-Match: "1".
        var delReq2 = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq2.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var delRsp2 = await client.SendAsync(delReq2);

        Assert.Equal(HttpStatusCode.NotFound, delRsp2.StatusCode);
        Assert.NotEqual(HttpStatusCode.PreconditionFailed, delRsp2.StatusCode);
    }

    /// <summary>
    /// EmployeeProfileSoftDeleted outbox event lands on
    /// <c>employee-profile-{employeeId}</c> stream in the same atomic tx
    /// as the audit row (ADR-018 D3). Payload carries the predecessor's
    /// profile_id + close-date + row-version (NAMED <c>RowVersion</c>, NOT
    /// <c>Version</c>, to avoid shadowing DomainEventBase's event-schema
    /// version field).
    /// </summary>
    [Fact]
    public async Task SoftDelete_EmitsEmployeeProfileSoftDeletedOnUser_OutboxStreamReadsBack()
    {
        const string employeeId = "emp001";
        var client = AuthorizedClient();

        // Snapshot the seeded row's profile_id BEFORE soft-delete so we can
        // assert the event payload carries it.
        Guid expectedProfileId;
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT profile_id FROM employee_profiles
                WHERE employee_id = @id AND effective_to IS NULL
                """, conn);
            cmd.Parameters.AddWithValue("id", employeeId);
            expectedProfileId = (Guid)(await cmd.ExecuteScalarAsync())!;
        }

        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var delRsp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        // Read back the latest outbox event on the per-employee stream.
        await using var conn2 = new NpgsqlConnection(_harness.ConnectionString);
        await conn2.OpenAsync();
        await using var outboxCmd = new NpgsqlCommand(
            """
            SELECT event_type, event_payload
            FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'EmployeeProfileSoftDeleted'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn2);
        outboxCmd.Parameters.AddWithValue("streamId", $"employee-profile-{employeeId}");
        await using var reader = await outboxCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected an EmployeeProfileSoftDeleted outbox event.");
        var rawPayload = reader.GetString(1);
        using var payloadDoc = JsonDocument.Parse(rawPayload);
        Assert.Equal(employeeId,
            payloadDoc.RootElement.GetProperty("employeeId").GetString());
        Assert.Equal(expectedProfileId,
            payloadDoc.RootElement.GetProperty("profileId").GetGuid());
        Assert.Equal(1L, payloadDoc.RootElement.GetProperty("rowVersion").GetInt64());
    }

    /// <summary>
    /// Audit row's <c>action</c> column on soft-delete must be the literal
    /// string <c>'DELETED'</c> — matches the existing CHECK constraint at
    /// init.sql:514 <c>action IN ('CREATED','UPDATED','DELETED','SUPERSEDED')</c>.
    /// Locking the schema-CHECK-constraint match per refinement cycle 2
    /// Codex BLOCKER absorption — if the endpoint ever changes to write
    /// 'SOFT_DELETED' (the natural-naming alternative), the INSERT throws
    /// 23514 + tx rolls back + the request fails.
    /// </summary>
    [Fact]
    public async Task SoftDelete_AuditAction_IsDELETED_NotSoftDeleted()
    {
        const string employeeId = "emp001";
        var client = AuthorizedClient();

        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var delRsp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM employee_profile_audit
            WHERE employee_id = @id AND action = 'DELETED'
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(1L, count);
    }

    // ═════════════════════════════════════════════════════════════════════
    // PUT cross-day routing — emits EmployeeProfileSuperseded (not Updated)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// emp001's seeded row is effective_from='0001-01-01'; today is later
    /// than that so a PUT with EffectiveFrom=today routes through Case C
    /// (cross-day supersession). The endpoint must emit
    /// EmployeeProfileSuperseded on the <c>employee-profile-{employeeId}</c>
    /// stream AND write an audit row with action='SUPERSEDED', NOT 'UPDATED'.
    /// </summary>
    [Fact]
    public async Task PUT_CrossDayEdit_EmitsEmployeeProfileSuperseded_NotUpdated()
    {
        const string employeeId = "emp001";
        var client = AuthorizedClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        // S33 in-flight defect fix: post-seeder-stamp-today, the predecessor's
        // effective_from = today; PUT at today would route to Case B (Updated).
        // Backdate the predecessor to yesterday so PUT(today) routes to Case C
        // (Superseded) — the case this test exercises.
        await using (var backdateConn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await backdateConn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE employee_profiles
                   SET effective_from = @yesterday
                 WHERE employee_id = @employeeId AND effective_to IS NULL
                """, backdateConn);
            cmd.Parameters.AddWithValue("employeeId", employeeId);
            cmd.Parameters.AddWithValue("yesterday", yesterday);
            await cmd.ExecuteNonQueryAsync();
        }

        var rsp = await PutEmployeeProfileAsync(client, employeeId,
            effectiveFrom: today,
            weeklyNormHours: 32.0m, partTimeFraction: 0.750m, position: "Specialist",
            ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Outbox: latest event on the per-employee stream is Superseded.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT event_type FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type IN ('EmployeeProfileSuperseded', 'EmployeeProfileUpdated')
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn))
        {
            outboxCmd.Parameters.AddWithValue("streamId", $"employee-profile-{employeeId}");
            var latest = (string?)await outboxCmd.ExecuteScalarAsync();
            Assert.Equal("EmployeeProfileSuperseded", latest);
        }

        // Audit row: action='SUPERSEDED' (latest for this employee).
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT action FROM employee_profile_audit
            WHERE employee_id = @id
              AND action IN ('UPDATED', 'SUPERSEDED')
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("id", employeeId);
            var action = (string?)await auditCmd.ExecuteScalarAsync();
            Assert.Equal("SUPERSEDED", action);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // PUT validator (ADR-023 D8 narrowing — 422 on backdated AND future-dated)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>PREMISE FLIPPED IN S138 / TASK-13802 (ADR-040 D8 as amended 2026-09-02).</b>
    ///
    /// <para>
    /// OLD pin (S33): a PUT with <c>EffectiveFrom = yesterday</c> returned 422 with a structured
    /// body naming <c>provided</c> + <c>expected</c> — the ADR-023 D8 same-day-ONLY-edit rule.
    /// NEW behaviour: BACKDATING IS THE FEATURE. HR can record that a change actually took effect
    /// on a past date, so yesterday is a legal, routed write; only the FUTURE is still refused,
    /// and its 422 body is now DATE-FREE (it shares a shape with the employment-start-floor
    /// refusal, which must never echo the hire date).
    /// </para>
    ///
    /// <para>
    /// The pin is kept — rewritten to assert the NEW truth rather than deleted — so the flip is
    /// visible in the suite rather than silently absent.
    /// </para>
    ///
    /// <para>
    /// <b>Which case this routes, corrected against the seeder (S138 CI).</b> The first rewrite of
    /// this pin assumed emp001's seeded live row starts TODAY and therefore expected case E (insert
    /// BEFORE the first row, producing a CLOSED row <c>[yesterday, today)</c>). That premise is
    /// wrong: <c>EmployeeProfileSeeder</c> deliberately stamps <c>effective_from = 0001-01-01</c>
    /// on backfilled rows, so that pre-deployment periods resolve instead of failing closed. A
    /// write dated yesterday therefore lands INSIDE the covering row and routes the SPLIT case:
    /// the predecessor is closed at yesterday (<c>[0001-01-01, yesterday)</c>, old values intact)
    /// and a new OPEN row <c>[yesterday, ∞)</c> carries the corrected values. Both halves are
    /// asserted below, because the split is the whole point — the correction must not overwrite
    /// what was true before it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PUT_BackdatedEffectiveFrom_NowWritesDatedHistory()
    {
        var client = AuthorizedClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        var rsp = await PutEmployeeProfileAsync(client, "emp001",
            effectiveFrom: yesterday,
            weeklyNormHours: 37.0m, partTimeFraction: 0.500m, position: "Backdated",
            ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The whole timeline, oldest first: the predecessor must be CLOSED at yesterday with its
        // old values intact, and the new row must be OPEN from yesterday with the corrected ones.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, effective_to, part_time_fraction, position
            FROM employee_profiles
            WHERE employee_id = 'emp001'
            ORDER BY effective_from
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(DateOnly From, DateOnly? To, decimal Fraction, string? Position)>();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetFieldValue<DateOnly>(0),
                await reader.IsDBNullAsync(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.GetDecimal(2),
                await reader.IsDBNullAsync(3) ? null : reader.GetString(3)));
        }

        Assert.Equal(2, rows.Count);

        // Predecessor: the seeder's 0001-01-01 row, now CLOSED at the correction's date. End-
        // exclusive (ADR-018 D9), so it covers everything up to but not including yesterday.
        Assert.Equal(new DateOnly(1, 1, 1), rows[0].From);
        Assert.Equal(yesterday, rows[0].To);
        Assert.Equal(1.000m, rows[0].Fraction); // the seeder's exact value — untouched, not merely "not the new one" (post-close Codex W)
        Assert.Null(rows[0].Position);          // the seeder's exact value; the same PUT precondition (If-Match "1") already requires the unmodified row

        // The correction: OPEN from yesterday, carrying the new values. It covers today, which is
        // why the live cache and the as-of-today read follow it.
        Assert.Equal(yesterday, rows[1].From);
        Assert.Null(rows[1].To);
        Assert.Equal(0.500m, rows[1].Fraction);
        Assert.Equal("Backdated", rows[1].Position);
        Assert.True(rows[1].From < today, "the corrected row must start in the past.");
    }

    /// <summary>
    /// PUT with EffectiveFrom = tomorrow → 422. S138 / TASK-13802 narrows what this pins: the
    /// rejection is no longer "two-sided same-day-only" (backdating is now legal — see the flipped
    /// pin above) but the standing FUTURE-dating refusal (ADR-040 D8 amendment: future-dating
    /// needs the "current ≠ live" read model and moves to Increment 4). Status only is asserted —
    /// the body is deliberately DATE-FREE now.
    /// </summary>
    [Fact]
    public async Task PUT_FutureDatedEffectiveFrom_Returns422()
    {
        var client = AuthorizedClient();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var rsp = await PutEmployeeProfileAsync(client, "emp001",
            effectiveFrom: tomorrow,
            weeklyNormHours: 37.0m, partTimeFraction: 1.000m, position: null,
            ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // DELETE endpoint If-Match (ADR-019 D2 admin-strict)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DELETE with stale If-Match → 412 with a structured body carrying
    /// <c>expectedVersion</c> (the caller's value) + <c>actualVersion</c>
    /// (the live row's stored version). Mirrors the PUT 412 contract.
    /// </summary>
    [Fact]
    public async Task DELETE_StaleIfMatch_Returns412()
    {
        var client = AuthorizedClient();

        // emp001's seeded row is version=1. If-Match: "3" is stale.
        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, "/api/admin/employee-profiles/emp001");
        delReq.Headers.TryAddWithoutValidation("If-Match", "\"3\"");
        var rsp = await client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3L, body.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(1L, body.GetProperty("actualVersion").GetInt64());
    }

    /// <summary>
    /// DELETE without If-Match header → 428 Precondition Required per
    /// EtagHeaderHelper admin-strict mode (mirrors PUT 428 contract).
    /// </summary>
    [Fact]
    public async Task DELETE_MissingIfMatch_Returns428()
    {
        var client = AuthorizedClient();
        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, "/api/admin/employee-profiles/emp001");
        var rsp = await client.SendAsync(delReq);
        Assert.Equal((HttpStatusCode)428, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Consumption fail-modes (ADR-023 D3)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compliance is the PCS-routed rule-engine caller (POST /api/rules/check-compliance).
    /// After a soft-delete the employment-profile resolver returns null, and ComplianceEndpoints
    /// is contractually FAIL-CLOSED (ADR-023 D3): it throws EmployeeProfileNotFoundException at
    /// the <c>?? throw</c> guard (ComplianceEndpoints.cs:147-148) instead of silently substituting
    /// the old hardcoded 37.0m norm. The anti-property this test LOCKS against is the pre-S33
    /// behaviour — a soft-deleted profile quietly producing a 200 on the central-config default.
    ///
    /// <para><b>Why this test was rewired (S133 / TASK-13304, QUAL-017 — verification theater).</b>
    /// The previous version asserted "any 5xx" (<c>StatusCode &gt;= 500</c>). But the fail-closed
    /// guard throws BEFORE the rule-engine hop, and in the WAF harness that hop is UNSTUBBED
    /// (nothing serves <c>/api/rules/check-compliance</c>), so the hop ALSO faults with a 5xx.
    /// Two different sources produced the same 5xx surface, so the assertion could not tell them
    /// apart: mutate the guard to silently default and the flow would still reach the (faulting)
    /// hop and still surface a 5xx — the test stayed green on the very regression it names. Worse,
    /// its <c>catch (HttpRequestException)</c> arm asserted NOTHING (a bare <c>return</c>), so any
    /// transport fault was an automatic pass. It could not go RED. (PAT-014 false-green class.)</para>
    ///
    /// <para><b>The fix.</b> Stub the rule-engine hop to a well-formed 200 ComplianceCheckResult
    /// (the Adr032ConsumptionPinTests / SkemaFullDayOnlyGuardTests convention). The hop can now
    /// NEVER be a fault source, so the ONLY way the endpoint returns anything other than a clean
    /// 200 is the resolver-null fail-closed guard firing. <b>Falsifiability:</b> replace
    /// <c>?? throw new EmployeeProfileNotFoundException(...)</c> with a silent default (the pre-S33
    /// regression) → the flow reaches the stubbed hop → 200 → this test goes RED on the
    /// <c>NotEqual(OK)</c> lock (or the fail-closed-signal assertion in the throw arm).</para>
    /// </summary>
    [Fact]
    public async Task Compliance_SoftDeletedProfile_FailsClosed_DoesNotSilentlyDefaultTo200()
    {
        const string employeeId = "emp001";

        // The rule-engine hop is stubbed to a valid 200 (see CreateComplianceRuleStubbedClient):
        // this REMOVES the incidental-5xx source so the resolver-null guard is the ONLY thing that
        // can make the response non-200. Booting the stubbed host re-runs the startup seeder — but
        // emp001 already has a LIVE profile from the primary host's boot, so the seeder's
        // "users lacking a live row" query skips it. We soft-delete AFTER this boot, so no seeder
        // re-backfills the row we are about to remove (the seeder treats a soft-deleted row —
        // effective_to NOT NULL — as "missing a live row" and would otherwise re-insert one).
        var client = CreateComplianceRuleStubbedClient();

        // Soft-delete emp001's live profile (seeded version = 1).
        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var delRsp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        // A far-future query date: the just-closed predecessor row carries effective_to=today, so
        // an asOfDate strictly after today is outside its window and the resolver returns null —
        // the exact precondition the fail-closed guard exists to catch.
        var farFuture = DateTime.UtcNow.AddYears(5);
        var url = $"/api/compliance/{employeeId}/period?year={farFuture.Year}&month={farFuture.Month}";

        HttpResponseMessage? rsp = null;
        Exception? thrown = null;
        try
        {
            rsp = await client.GetAsync(url);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        if (thrown is null)
        {
            // The Development-environment developer-exception-page middleware maps the unhandled
            // EmployeeProfileNotFoundException to a 500 response. The load-bearing assertion is
            // NotEqual(OK): with the hop stubbed to 200, a 200 here can ONLY mean the guard was
            // bypassed and the profile silently defaulted — the anti-property we lock against.
            Assert.NotEqual(HttpStatusCode.OK, rsp!.StatusCode);
            Assert.True(
                (int)rsp.StatusCode >= 500,
                $"Expected the fail-closed 5xx from the resolver-null guard; got {(int)rsp.StatusCode} {rsp.StatusCode}.");
        }
        else
        {
            // If the pipeline instead surfaces the unhandled exception as a transport fault, it can
            // ONLY be the guard here — the hop is stubbed to 200 and never faults. Assert it is the
            // SPECIFIC fail-closed signal (EmployeeProfileNotFoundException's message), so this arm
            // pins the real guard rather than passing on any throw (the old bare-return defect).
            var flattened = FlattenExceptionMessages(thrown);
            Assert.Contains("no employee profile found", flattened, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Balance is a graceful-degradation HTTP consumer per ADR-023 D3 —
    /// when resolver returns null, the fallback chain
    /// <c>datedProfile?.WeeklyNormHours ?? dbConfig?.WeeklyNormHours ??
    /// CentralAgreementConfigs ?? 37.0m</c> still produces a value. The
    /// summary endpoint returns 200 with the central-config default rather
    /// than 500.
    /// </summary>
    [Fact]
    public async Task Balance_SoftDeletedProfile_FallsThroughAgreementConfigChain_Returns200WithDefaultNorm()
    {
        const string employeeId = "emp001";
        var client = AuthorizedClient();

        // Soft-delete the profile.
        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var delRsp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        // Balance summary for a far-future month so the resolver returns
        // null (predecessor closed at today, asOfDate beyond effective_to).
        var farFuture = DateTime.UtcNow.AddYears(5);
        var rsp = await client.GetAsync(
            $"/api/balance/{employeeId}/summary?year={farFuture.Year}&month={farFuture.Month}");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        // The endpoint computes normHoursExpected from weeklyNormHours; we
        // don't bind to a specific normHoursExpected value (depends on the
        // far-future weekday count) but we DO bind that the request succeeded
        // — i.e. the fall-through chain produced a non-null weeklyNormHours
        // and the endpoint didn't throw EmployeeProfileNotFoundException.
        // This is the load-bearing differentiator from the Compliance test
        // above per ADR-023 D3 split.
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("normHoursExpected", out _));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a brand-new user via direct DB insert (NOT through
    /// AdminEndpoints POST which would also insert a profile row). Leaves
    /// the new user without any employee_profiles row, so Case A is
    /// reachable for that user.
    /// </summary>
    private async Task<string> CreateUserWithoutProfileAsync()
    {
        var userId = "emp_s33_ltc_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@userId, @userId, 'dev-only', 'Lifecycle Fresh User', NULL,
                    'STY01', 'AC', 'OK24', TRUE)
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    private static string MintGlobalAdminToken()
    {
        var settings = new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        };
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: "ADMIN_S33_QA",
            name: "S33 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    /// <summary>
    /// A GlobalAdmin client whose host has the whole <see cref="IHttpClientFactory"/> replaced by a
    /// stub that returns a well-formed 200 ComplianceCheckResult for <c>/api/rules/check-compliance</c>
    /// (the Adr032ConsumptionPinTests / SkemaFullDayOnlyGuardTests convention). Used by the QUAL-017
    /// fail-closed test so the rule-engine hop is NOT a 5xx source and the resolver-null guard is the
    /// sole non-200 path. Derived via <c>WithWebHostBuilder</c> so the primary host is untouched.
    /// </summary>
    private HttpClient CreateComplianceRuleStubbedClient()
    {
        var stubbedFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IHttpClientFactory>(new ComplianceRuleStubFactory())));
        var client = stubbedFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    private static string FlattenExceptionMessages(Exception ex)
    {
        var sb = new StringBuilder();
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
            sb.Append(cur.Message).Append(" | ");
        return sb.ToString();
    }

    private sealed class ComplianceRuleStubFactory : IHttpClientFactory
    {
        // Whole-factory replacement (Skema convention): supplies the BaseAddress the production
        // named-client registration would set, so the endpoint's relative-URI PostAsJsonAsync
        // composes correctly and is intercepted by the stub handler below.
        public HttpClient CreateClient(string name) =>
            new(new ComplianceRuleStubHandler(), disposeHandler: false)
            {
                BaseAddress = new Uri("http://rule-engine:8080"),
            };
    }

    private sealed class ComplianceRuleStubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/api/rules/check-compliance", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            // A well-formed, deserializable ComplianceCheckResult (success, no violations). The
            // point is only that the hop returns a clean 200 — so if the guard were bypassed, the
            // endpoint would produce a silently-defaulted 200 this test then catches.
            const string body =
                "{\"ruleId\":\"REST_PERIOD\",\"employeeId\":\"stub\",\"success\":true,\"violations\":[],\"warnings\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static async Task<HttpResponseMessage> PutEmployeeProfileAsync(
        HttpClient client, string employeeId,
        DateOnly effectiveFrom,
        decimal weeklyNormHours, decimal partTimeFraction, string? position,
        string? ifMatch)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom,
                weeklyNormHours,
                partTimeFraction,
                position,
            }),
        };
        if (ifMatch is not null)
            req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }
}
