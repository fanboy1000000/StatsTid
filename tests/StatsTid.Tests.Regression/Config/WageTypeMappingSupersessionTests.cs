using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S29 / TASK-2909 D-tests #1–#5 — the wage-type-mapping supersession lifecycle, driven
/// through the SHIPPED HTTP endpoints (<see cref="WageTypeMappingEndpoints"/>) via
/// <see cref="StatsTidWebApplicationFactory"/> against a real Postgres testcontainer.
///
/// <para>
/// <b>S133 / TASK-13302 (QUAL-020) — rewire away from verification theatre.</b> The prior
/// version of these tests called the real repository for the state change but then
/// <i>hand-wrote its own audit rows, its own <c>INSERT INTO outbox_events</c>, and (for the
/// zero-width reopen) its own <c>UPDATE … SET effective_to = NULL</c></i> — re-implementing
/// the endpoint's job inside the test body. As a result the real emitter
/// (<c>WageTypeMappingEndpoints</c> POST/PUT/DELETE: the action routing, the
/// <c>IOutboxEnqueue</c> call, and the <c>IAuditProjectionMapper</c> projection write) was
/// exercised by NOTHING, and the tests would have stayed green even if that emitter were
/// deleted. Now every side effect under assertion — the state rows, the audit action, and
/// the outbox event — is produced by shipped code reached over the wire, exactly as a real
/// GlobalAdmin request produces it. What the test still does directly is DATA SETUP only
/// (seeding a dated predecessor row the same-day-only-edit validator will not let an
/// endpoint create) and READ-BACK assertions.
/// </para>
///
/// <list type="bullet">
///   <item>#1: PUT same-day → in-place UPDATE (version bump), UPDATED audit + WageTypeMappingUpdated outbox.</item>
///   <item>#2: PUT cross-day → predecessor closed + new row inserted, SUPERSEDED audit + WageTypeMappingSuperseded outbox.</item>
///   <item>#3: <see cref="WageTypeMappingRepository.GetByKeyAtAsync"/> dated read across closed/open/boundary — a pure
///   REPO-read contract with no emitter, so it stays a direct real-repo call (it already exercises the shipped SUT).</item>
///   <item>#4: D2 Case B (DELETE then POST, predecessor effective_from &lt; today) → fresh INSERT; DELETED→CREATED audit chain.</item>
///   <item>#5: D2 Case C (POST, DELETE, POST same day) → zero-width row UPDATE-and-reopen (version bump), UPDATED audit (not CREATED/SUPERSEDED).</item>
/// </list>
///
/// JWT minting follows the dev-fallback signing-key pattern (Development host env → dev
/// fallback key fires), verbatim from <see cref="WageTypeMappingEndpointTests"/>. The
/// <c>GlobalAdminOnly</c> policy requires the GlobalAdmin role on the JWT.
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingSupersessionTests : IAsyncLifetime
{
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";
    private const string Position = "";

    // Verbatim from JwtValidationSetup.DevFallbackSigningKey (same as WageTypeMappingEndpointTests).
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WageTypeMappingRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Full production schema — the real emitter (Program.cs DI + init.sql tables:
        // wage_type_mappings, wage_type_mapping_audit, outbox_events, audit_projection) must
        // all be present for the endpoint's atomic write to run.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _repo = new WageTypeMappingRepository(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #1 — PUT same-day → in-place UPDATE, UPDATED audit + Updated outbox.
    //
    // Falsifiability: the audit action + outbox event_type are chosen by the endpoint's
    // same-day branch. Flip that branch to SUPERSEDED / WageTypeMappingSuperseded and both
    // the audit-action assertion and the outbox event_type assertion go RED. Remove the
    // outbox.EnqueueAndReturnIdAsync call and the outbox count drops to 0 → RED.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SameDayEdit_ViaPut_InPlaceUpdate_BumpsVersion_EmitsUpdatedAuditAndOutbox()
    {
        var timeType = NewTimeType("SAMEDAY");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Data setup: one open row at effective_from = today, version = 1.
        await SeedOpenRowAsync(timeType, effectiveFrom: today, wageType: "SLS_0110", description: "original");

        var client = AdminClient();
        var rsp = await PutAsync(client, timeType,
            wageType: "SLS_0110", description: "updated-same-day",
            effectiveFrom: today, ifMatchValue: "1");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"2\"", rsp.Headers.ETag!.Tag); // in-place update bumped version 1 → 2

        // State: still exactly one row, now version 2 with the new description, still open.
        Assert.Equal(1L, await CountRowsForNaturalKeyAsync(timeType));
        var row = await ReadRowAsync(timeType, today);
        Assert.NotNull(row);
        Assert.Equal(2L, row!.Version);
        Assert.Equal("updated-same-day", row.Description);
        Assert.Null(row.EffectiveTo);

        // Audit produced by the endpoint: UPDATED with version pair 1 → 2.
        var (audAction, audBefore, audAfter) = await ReadLatestAuditAsync(timeType);
        Assert.Equal("UPDATED", audAction);
        Assert.Equal(1L, audBefore);
        Assert.Equal(2L, audAfter);

        // Outbox produced by the endpoint's real IOutboxEnqueue.
        Assert.Equal(1L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingUpdated"));
        Assert.Equal(0L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingSuperseded"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #2 — PUT cross-day → close predecessor + insert new row, SUPERSEDED
    // audit + WageTypeMappingSuperseded outbox. THE headline supersession case.
    //
    // Falsifiability: the SUPERSEDED action + Superseded event are the endpoint's cross-day
    // branch (WageTypeMappingEndpoints PUT, isCrossDay==true). Break the branch (emit
    // UPDATED) → both the audit-action and outbox event_type assertions go RED. Skip the
    // predecessor-close and the "2 rows" / predecessor.effective_to assertions go RED.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task CrossDayEdit_ViaPut_ClosesPredecessor_InsertsNewRow_EmitsSupersededAuditAndOutbox()
    {
        var timeType = NewTimeType("CROSSDAY");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var predecessorEffectiveFrom = new DateOnly(2020, 1, 1); // mirrors init.sql backfill epoch

        // Data setup: an open, day-old predecessor (the endpoint's validator forbids creating
        // a non-today effective_from, so this must be seeded directly).
        await SeedOpenRowAsync(timeType, effectiveFrom: predecessorEffectiveFrom, wageType: "SLS_0110", description: "original");

        var client = AdminClient();
        var rsp = await PutAsync(client, timeType,
            wageType: "SLS_0110", description: "new",
            effectiveFrom: today, ifMatchValue: "1");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag); // the new open row starts at version 1

        // State: 2 rows — closed predecessor + new open row.
        Assert.Equal(2L, await CountRowsForNaturalKeyAsync(timeType));

        var predecessor = await ReadRowAsync(timeType, predecessorEffectiveFrom);
        Assert.NotNull(predecessor);
        Assert.Equal(today, predecessor!.EffectiveTo);
        Assert.Equal("original", predecessor.Description);
        Assert.Equal(1L, predecessor.Version);

        var newRow = await ReadRowAsync(timeType, today);
        Assert.NotNull(newRow);
        Assert.Null(newRow!.EffectiveTo);
        Assert.Equal("new", newRow.Description);
        Assert.Equal(1L, newRow.Version);

        // Audit produced by the endpoint: exactly one SUPERSEDED with version pair 1 → 1.
        var audits = await ReadAllAuditAsync(timeType);
        var superseded = audits.Where(a => a.Action == "SUPERSEDED").ToList();
        Assert.Single(superseded);
        Assert.Equal(1L, superseded[0].VersionBefore);
        Assert.Equal(1L, superseded[0].VersionAfter);

        // Outbox produced by the endpoint's real IOutboxEnqueue.
        Assert.Equal(1L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingSuperseded"));
        Assert.Equal(0L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingUpdated"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #3 — GetByKeyAtAsync(asOfDate) across closed / open / boundary. This is a pure
    // REPO-read contract (no emitter), so it drives the shipped repo method DIRECTLY and
    // asserts its real output — it was never verification theatre. Data is seeded raw because
    // arbitrary-dated history rows cannot be produced through the same-day-only endpoints.
    //
    // Falsifiability: change GetByKeyAtAsync's end-exclusive predicate and case (d) (the
    // boundary) flips from "open-row" to "closed-row"; a wrong effective_from bound flips (a).
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task GetByKeyAtAsync_HistoryAndOpenRows_ResolvesCorrectlyAcrossDates()
    {
        var timeType = NewTimeType("DATED");

        // Closed history row at [2024-01-01, 2024-06-01); then open row at [2024-06-01, NULL).
        await InsertRawAsync(timeType, "SLS_0110", description: "closed-row",
            effectiveFrom: new DateOnly(2024, 1, 1), effectiveTo: new DateOnly(2024, 6, 1), version: 1);
        await InsertRawAsync(timeType, "SLS_0220", description: "open-row",
            effectiveFrom: new DateOnly(2024, 6, 1), effectiveTo: null, version: 1);

        // (a) before earliest → NULL.
        var beforeEarliest = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position, asOfDate: new DateOnly(2023, 12, 1));
        Assert.Null(beforeEarliest);

        // (b) within closed range → the closed row.
        var withinClosed = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position, asOfDate: new DateOnly(2024, 3, 15));
        Assert.NotNull(withinClosed);
        Assert.Equal("closed-row", withinClosed!.Description);
        Assert.Equal("SLS_0110", withinClosed.WageType);

        // (c) within open range → the open row.
        var withinOpen = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position, asOfDate: new DateOnly(2024, 9, 1));
        Assert.NotNull(withinOpen);
        Assert.Equal("open-row", withinOpen!.Description);
        Assert.Equal("SLS_0220", withinOpen.WageType);
        Assert.Null(withinOpen.EffectiveTo);

        // (d) boundary asOfDate == open row's effective_from: end-exclusive predicate excludes
        // the closed row (effective_to = 2024-06-01 is NOT > 2024-06-01) and includes the open row.
        var atBoundary = await _repo.GetByKeyAtAsync(
            timeType, OkVersion, AgreementCode, Position, asOfDate: new DateOnly(2024, 6, 1));
        Assert.NotNull(atBoundary);
        Assert.Equal("open-row", atBoundary!.Description);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #4 — D2 Case B: DELETE (soft-close today) then POST re-create, predecessor
    // effective_from < today → fresh INSERT (new open row). Audit chain DELETED → CREATED,
    // never SUPERSEDED. Both legs driven through the real endpoints.
    //
    // Falsifiability: the DELETE and POST each emit their own audit + outbox via shipped code.
    // If the POST mis-routed Case B into the reopen (Case C) branch the "2 rows" assertion
    // goes RED; if it emitted SUPERSEDED the DoesNotContain-SUPERSEDED assertion goes RED.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task CaseB_DeleteThenRecreate_PredecessorBeforeToday_FreshInsert_DeletedThenCreatedAudit()
    {
        var timeType = NewTimeType("CASEB");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var pastEffectiveFrom = new DateOnly(2024, 1, 1);

        await SeedOpenRowAsync(timeType, effectiveFrom: pastEffectiveFrom, wageType: "SLS_0110", description: "original-seed");

        var client = AdminClient();

        // DELETE → soft-close (effective_to = today); DELETED audit + WageTypeMappingDeleted outbox.
        var delRsp = await DeleteAsync(client, timeType, ifMatchValue: "1");
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        // POST re-create same natural key — Case B: closed-today predecessor with
        // effective_from < today → fresh INSERT at version 1; CREATED audit + Created outbox.
        var postRsp = await PostAsync(client, timeType,
            wageType: "SLS_0110", description: "recreated-case-B", effectiveFrom: today);
        Assert.Equal(HttpStatusCode.Created, postRsp.StatusCode);
        Assert.Equal("\"1\"", postRsp.Headers.ETag!.Tag);

        // State: 2 rows — closed predecessor + new open row.
        Assert.Equal(2L, await CountRowsForNaturalKeyAsync(timeType));
        var predecessor = await ReadRowAsync(timeType, pastEffectiveFrom);
        Assert.NotNull(predecessor);
        Assert.Equal(today, predecessor!.EffectiveTo);

        var newRow = await ReadRowAsync(timeType, today);
        Assert.NotNull(newRow);
        Assert.Null(newRow!.EffectiveTo);
        Assert.Equal("recreated-case-B", newRow.Description);
        Assert.Equal(1L, newRow.Version);

        // Audit chain produced by the endpoints: DELETED then CREATED, never SUPERSEDED.
        var audits = await ReadAllAuditAsync(timeType);
        Assert.Contains(audits, a => a.Action == "DELETED");
        Assert.Contains(audits, a => a.Action == "CREATED");
        Assert.DoesNotContain(audits, a => a.Action == "SUPERSEDED");

        // Outbox produced by the endpoints.
        Assert.Equal(1L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingDeleted"));
        Assert.Equal(1L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingCreated"));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #5 — D2 Case C: POST (create today), DELETE (zero-width close), POST again.
    // The final POST hits a closed-today row whose effective_from == today, so the endpoint
    // UPDATE-and-reopens IN PLACE (version bump, single row) rather than inserting — UPDATED
    // audit, WageTypeMappingUpdated outbox, NOT a SUPERSEDED and NOT a second CREATED for the
    // reopen. The reopen UPDATE is now performed by the SHIPPED endpoint (previously the test
    // hand-wrote the `UPDATE … SET effective_to = NULL` itself).
    //
    // Falsifiability: if Case C wrongly inserted a second row the "1 row" assertion goes RED;
    // if it emitted CREATED/SUPERSEDED for the reopen the "latest audit == UPDATED" assertion
    // and the outbox event_type assertion go RED; if it failed to bump the version the
    // version==2 assertion goes RED.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task CaseC_CreateDeleteRecreateSameDay_ZeroWidthReopen_UpdatesInPlace_EmitsUpdatedAudit()
    {
        var timeType = NewTimeType("CASEC");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var client = AdminClient();

        // POST create today (Case A) → version 1, CREATED audit + Created outbox.
        var create = await PostAsync(client, timeType,
            wageType: "SLS_0110", description: "first-create-today", effectiveFrom: today);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal("\"1\"", create.Headers.ETag!.Tag);

        // DELETE → zero-width close (effective_from == effective_to == today); DELETED audit.
        var del = await DeleteAsync(client, timeType, ifMatchValue: "1");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // POST again — Case C UPDATE-and-reopen: single row, version 2, UPDATED audit + Updated outbox.
        var reopen = await PostAsync(client, timeType,
            wageType: "SLS_0110", description: "recreated-case-C", effectiveFrom: today);
        Assert.Equal(HttpStatusCode.Created, reopen.StatusCode);
        Assert.Equal("\"2\"", reopen.Headers.ETag!.Tag);

        // State: exactly one row (reopen, not fresh insert), version 2, open, new description.
        Assert.Equal(1L, await CountRowsForNaturalKeyAsync(timeType));
        var final = await ReadRowAsync(timeType, today);
        Assert.NotNull(final);
        Assert.Null(final!.EffectiveTo);
        Assert.Equal(2L, final.Version);
        Assert.Equal("recreated-case-C", final.Description);

        // Audit produced by the endpoints: the reopen is an UPDATE (latest action UPDATED),
        // a DELETED exists from the middle step, and there is NO SUPERSEDED anywhere.
        var (latestAction, _, _) = await ReadLatestAuditAsync(timeType);
        Assert.Equal("UPDATED", latestAction);
        var audits = await ReadAllAuditAsync(timeType);
        Assert.Contains(audits, a => a.Action == "DELETED");
        Assert.DoesNotContain(audits, a => a.Action == "SUPERSEDED");

        // Outbox produced by the endpoints — the reopen emits WageTypeMappingUpdated.
        Assert.Equal(1L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingUpdated"));
        Assert.Equal(0L, await CountOutboxAsync(StreamId(timeType), "WageTypeMappingSuperseded"));
    }

    // ─── HTTP helpers (drive the real endpoints) ───────────────────────────────

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintAdminToken());
        return client;
    }

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, string timeType,
        string wageType, string description, DateOnly effectiveFrom, string? ifMatchValue)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/wage-type-mappings")
        {
            Content = JsonContent.Create(new
            {
                timeType,
                wageType,
                okVersion = OkVersion,
                agreementCode = AgreementCode,
                position = Position,
                description,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            }),
        };
        if (ifMatchValue is not null)
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchValue}\"");
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string timeType,
        string wageType, string description, DateOnly effectiveFrom)
    {
        return await client.PostAsJsonAsync("/api/admin/wage-type-mappings", new
        {
            timeType,
            wageType,
            okVersion = OkVersion,
            agreementCode = AgreementCode,
            position = Position,
            description,
            effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
        });
    }

    private static async Task<HttpResponseMessage> DeleteAsync(
        HttpClient client, string timeType, string? ifMatchValue)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/admin/wage-type-mappings?timeType={timeType}&okVersion={OkVersion}&agreementCode={AgreementCode}&position=");
        if (ifMatchValue is not null)
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchValue}\"");
        return await client.SendAsync(req);
    }

    private static string MintAdminToken()
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
            employeeId: "ADMIN_S133_QA",
            name: "S133 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: AgreementCode);
    }

    // ─── Data-setup + read-back helpers (talk to the DB directly) ──────────────

    private static string NewTimeType(string prefix) =>
        $"WTM_S29_{prefix}_" + Guid.NewGuid().ToString("N").Substring(0, 8);

    private static string StreamId(string timeType) =>
        $"wage-type-mapping-{AgreementCode}-{OkVersion}-{timeType}";

    /// <summary>
    /// Seeds one currently-open row for the natural key at the given effective_from. Used to
    /// stage a predecessor the same-day-only-edit endpoint validator will not let us create
    /// through HTTP. Pure data setup — no orchestration.
    /// </summary>
    private Task SeedOpenRowAsync(string timeType, DateOnly effectiveFrom, string wageType, string description) =>
        InsertRawAsync(timeType, wageType, description, effectiveFrom, effectiveTo: null, version: 1);

    private async Task InsertRawAsync(
        string timeType, string wageType, string description,
        DateOnly effectiveFrom, DateOnly? effectiveTo, long version)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings (
                mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                description, effective_from, effective_to, version)
            VALUES (
                gen_random_uuid(), @tt, @wt, @ok, @ac, @pos,
                @desc, @ef, @et, @v)
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("wt", wageType);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("pos", Position);
        cmd.Parameters.AddWithValue("desc", description);
        cmd.Parameters.AddWithValue("ef", effectiveFrom);
        cmd.Parameters.AddWithValue("et", (object?)effectiveTo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("v", version);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> CountRowsForNaturalKeyAsync(string timeType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = @pos
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("pos", Position);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<WageTypeMapping?> ReadRowAsync(string timeType, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT description, version, effective_to
            FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
              AND position = @pos AND effective_from = @ef
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("pos", Position);
        cmd.Parameters.AddWithValue("ef", effectiveFrom);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new WageTypeMapping
        {
            TimeType = timeType,
            WageType = "", // not needed by assertions
            OkVersion = OkVersion,
            AgreementCode = AgreementCode,
            Position = Position,
            Description = reader.IsDBNull(0) ? null : reader.GetString(0),
            Version = reader.GetInt64(1),
            EffectiveFrom = effectiveFrom,
            EffectiveTo = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
        };
    }

    private async Task<(string Action, long? VersionBefore, long? VersionAfter)> ReadLatestAuditAsync(string timeType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM wage_type_mapping_audit
            WHERE time_type = @tt
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "No audit row found");
        var action = reader.GetString(0);
        long? before = reader.IsDBNull(1) ? null : reader.GetInt64(1);
        long? after = reader.IsDBNull(2) ? null : reader.GetInt64(2);
        return (action, before, after);
    }

    private async Task<List<(string Action, long? VersionBefore, long? VersionAfter)>> ReadAllAuditAsync(string timeType)
    {
        var rows = new List<(string Action, long? VersionBefore, long? VersionAfter)>();
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM wage_type_mapping_audit
            WHERE time_type = @tt
            ORDER BY audit_id ASC
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var action = reader.GetString(0);
            long? before = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            long? after = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            rows.Add((action, before, after));
        }
        return rows;
    }

    private async Task<long> CountOutboxAsync(string streamId, string eventType)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @sid AND event_type = @et", conn);
        cmd.Parameters.AddWithValue("sid", streamId);
        cmd.Parameters.AddWithValue("et", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
