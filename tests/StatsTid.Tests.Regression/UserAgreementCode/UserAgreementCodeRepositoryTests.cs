using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.UserAgreementCode;

/// <summary>
/// S34 / TASK-3414 — Repository-direct D-tests for
/// <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/> ADR-020 D2
/// 3-case routing (Case A INSERT, Case B same-day UPDATE-in-place, Case C
/// cross-day close-predecessor + insert-successor) plus the cache-canonical
/// agreement assertion that pins the
/// <c>users.agreement_code</c> denormalized cache against the live
/// <c>user_agreement_codes</c> row after PUT.
///
/// <para>
/// The 3-case routing tests use the raw
/// <see cref="UserAgreementCodeRepository"/> against the per-test container —
/// same pattern as <c>EmployeeProfileLifecycleTests</c>. The cache-canonical
/// test rides the HTTP PUT endpoint (TASK-3407) because the cache write
/// happens in the endpoint's atomic tx, not in the repo.
/// </para>
///
/// <para>
/// <b>Case C successor version contract</b> per S33 Step 7a P1 +
/// <c>UserAgreementCodeRepository.InsertLiveRowAsync</c> xmldoc: successor
/// inherits <c>predecessor.Version + 1</c> (NOT 1) because the natural key
/// is single-column <c>user_id</c> and the version must carry ETag
/// monotonicity alone.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class UserAgreementCodeRepositoryTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey =
        "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private UserAgreementCodeRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot the host so the seeder + DI graph stand up; backfill seeder will
        // populate live user_agreement_codes rows for the init.sql-seeded users.
        _ = _factory.CreateClient();
        _repo = new UserAgreementCodeRepository(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // SupersedeAndCreateAsync 3-case routing
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Case A — no live row. <c>expectedVersion=null</c> lets the repo INSERT a
    /// fresh row at <c>version=1</c>, <c>effective_from = request.EffectiveFrom</c>,
    /// <c>effective_to = NULL</c>. Outcome must be Created.
    /// </summary>
    [Fact]
    public async Task SupersedeAndCreate_CaseA_NoLiveRow_Inserts()
    {
        // Brand-new user inserted via direct SQL (does NOT go through admin POST
        // which would also Case A INSERT a user_agreement_codes row).
        var userId = await CreateUserWithoutAgreementRowAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var req = new UserAgreementCodeSupersedeRequest(
            UserId: userId,
            AgreementCode: "AC",
            EffectiveFrom: today);

        var result = await _repo.SupersedeAndCreateAsync(conn, tx, req, expectedVersion: null);
        await tx.CommitAsync();

        Assert.Equal(SaveUserAgreementCodeOutcome.Created, result.Outcome);
        Assert.Equal(1L, result.Version);

        // Verify: exactly one live row at the values we asked for.
        await using var verifyConn = _harness.Factory.Create();
        await verifyConn.OpenAsync();
        await using var checkCmd = new NpgsqlCommand(
            """
            SELECT assignment_id, agreement_code, effective_from, effective_to, version
            FROM user_agreement_codes
            WHERE user_id = @userId AND effective_to IS NULL
            """, verifyConn);
        checkCmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await checkCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Case A should have inserted a live row.");
        Assert.Equal(result.AssignmentId, reader.GetGuid(0));
        Assert.Equal("AC", reader.GetString(1));
        Assert.Equal(today, reader.GetFieldValue<DateOnly>(2));
        Assert.True(reader.IsDBNull(3));
        Assert.Equal(1L, reader.GetInt64(4));
        Assert.False(await reader.ReadAsync(), "Exactly one live row expected.");
    }

    /// <summary>
    /// Case B — same-day in-place edit. Predecessor's <c>effective_from</c>
    /// equals <c>request.EffectiveFrom</c> → UPDATE in place,
    /// <c>version</c> N → N+1, <c>assignment_id</c> and <c>effective_from</c>
    /// unchanged.
    /// </summary>
    [Fact]
    public async Task SupersedeAndCreate_CaseB_SameDayEdit_UpdatesInPlace_BumpsVersion()
    {
        var userId = await CreateUserWithoutAgreementRowAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Build a Case A row at effective_from = today (so the next call routes
        // through Case B).
        Guid initialAssignmentId;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var seedReq = new UserAgreementCodeSupersedeRequest(
                UserId: userId,
                AgreementCode: "AC",
                EffectiveFrom: today);
            var seedResult = await _repo.SupersedeAndCreateAsync(conn, tx, seedReq, expectedVersion: null);
            initialAssignmentId = seedResult.AssignmentId;
            Assert.Equal(SaveUserAgreementCodeOutcome.Created, seedResult.Outcome);
            await tx.CommitAsync();
        }

        // Same-day edit — Case B.
        SaveUserAgreementCodeResult editResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var editReq = new UserAgreementCodeSupersedeRequest(
                UserId: userId,
                AgreementCode: "HK",
                EffectiveFrom: today);
            editResult = await _repo.SupersedeAndCreateAsync(conn, tx, editReq, expectedVersion: 1L);
            await tx.CommitAsync();
        }

        Assert.Equal(SaveUserAgreementCodeOutcome.Updated, editResult.Outcome);
        Assert.Equal(2L, editResult.Version);
        // Case B preserves the predecessor's assignment_id (UPDATE-in-place).
        Assert.Equal(initialAssignmentId, editResult.AssignmentId);

        // Verify: still exactly one live row, agreement_code updated,
        // effective_from unchanged, version bumped to 2.
        await using var verifyConn = _harness.Factory.Create();
        await verifyConn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT version, agreement_code, effective_from, assignment_id
            FROM user_agreement_codes
            WHERE user_id = @userId AND effective_to IS NULL
            """, verifyConn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal("HK", reader.GetString(1));
        Assert.Equal(today, reader.GetFieldValue<DateOnly>(2));
        Assert.Equal(initialAssignmentId, reader.GetGuid(3));
        Assert.False(await reader.ReadAsync(), "Case B must preserve the live-row invariant.");
    }

    /// <summary>
    /// Case C — cross-day edit. Predecessor's <c>effective_from</c> is strictly
    /// earlier than <c>request.EffectiveFrom</c> → close the predecessor
    /// (<c>effective_to = request.EffectiveFrom</c>, <c>version</c> unchanged)
    /// AND insert a new live row at
    /// <c>version = predecessor.Version + 1</c> (S33 Step 7a P1 ETag-monotonicity
    /// absorption).
    /// </summary>
    [Fact]
    public async Task SupersedeAndCreate_CaseC_CrossDayEdit_SuccessorInheritsPredecessorVersionPlus1()
    {
        // emp001 has a backfilled live row at effective_from='0001-01-01' (per
        // the TASK-3403 backfill seeder ran at WAF startup). Today is strictly
        // greater than '0001-01-01' so a today-effective edit routes to Case C.
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Guid predecessorAssignmentId;
        long predecessorVersion;
        DateOnly predecessorEffectiveFrom;
        string predecessorAgreementCode;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var preCmd = new NpgsqlCommand(
                """
                SELECT assignment_id, version, effective_from, agreement_code
                FROM user_agreement_codes
                WHERE user_id = @userId AND effective_to IS NULL
                """, conn);
            preCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await preCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "emp001 must have a backfilled user_agreement_codes live row post-WAF startup.");
            predecessorAssignmentId = reader.GetGuid(0);
            predecessorVersion = reader.GetInt64(1);
            predecessorEffectiveFrom = reader.GetFieldValue<DateOnly>(2);
            predecessorAgreementCode = reader.GetString(3);
            // Sanity: '0001-01-01' from the backfill seeder.
            Assert.True(predecessorEffectiveFrom < today,
                $"Predecessor effective_from {predecessorEffectiveFrom:yyyy-MM-dd} must be strictly earlier than today {today:yyyy-MM-dd} for Case C routing.");
        }

        // Case C edit: AC → HK effective today.
        SaveUserAgreementCodeResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var req = new UserAgreementCodeSupersedeRequest(
                UserId: userId,
                AgreementCode: "HK",
                EffectiveFrom: today);
            result = await _repo.SupersedeAndCreateAsync(
                conn, tx, req, expectedVersion: predecessorVersion);
            await tx.CommitAsync();
        }

        Assert.Equal(SaveUserAgreementCodeOutcome.Superseded, result.Outcome);
        // S33 Step 7a P1 absorption: Case C successor inherits predecessor.Version + 1.
        Assert.Equal(predecessorVersion + 1, result.Version);
        Assert.NotEqual(predecessorAssignmentId, result.AssignmentId);

        // Verify: closed predecessor row carries effective_to=today + version unchanged.
        await using (var verifyConn = _harness.Factory.Create())
        {
            await verifyConn.OpenAsync();
            await using var closedCmd = new NpgsqlCommand(
                """
                SELECT version, agreement_code, effective_from, effective_to
                FROM user_agreement_codes
                WHERE assignment_id = @assignmentId
                """, verifyConn);
            closedCmd.Parameters.AddWithValue("assignmentId", predecessorAssignmentId);
            await using var reader = await closedCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(predecessorVersion, reader.GetInt64(0));
            Assert.Equal(predecessorAgreementCode, reader.GetString(1));
            Assert.Equal(predecessorEffectiveFrom, reader.GetFieldValue<DateOnly>(2));
            Assert.Equal(today, reader.GetFieldValue<DateOnly>(3));
        }

        // Verify: new live row at effective_from=today + version=predecessor+1.
        await using (var verifyConn = _harness.Factory.Create())
        {
            await verifyConn.OpenAsync();
            await using var liveCmd = new NpgsqlCommand(
                """
                SELECT version, agreement_code, effective_from, effective_to, assignment_id
                FROM user_agreement_codes
                WHERE user_id = @userId AND effective_to IS NULL
                """, verifyConn);
            liveCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await liveCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(predecessorVersion + 1, reader.GetInt64(0));
            Assert.Equal("HK", reader.GetString(1));
            Assert.Equal(today, reader.GetFieldValue<DateOnly>(2));
            Assert.True(reader.IsDBNull(3));
            Assert.Equal(result.AssignmentId, reader.GetGuid(4));
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Cache-canonical contract (refinement cycle 1 Reviewer WARNING 2 absorption)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// After PUT mutates agreement_code, the denormalized
    /// <c>users.agreement_code</c> cache MUST match the live
    /// <c>user_agreement_codes</c> row. The cache UPDATE rides the same atomic
    /// tx as the <c>user_agreement_codes</c> routing per the
    /// <see cref="UserAgreementCodeRepository"/> canonical-write contract
    /// (xmldoc on the class). A drift between the two would cause JWT mint /
    /// live-only consumers to disagree with the canonical history table.
    /// </summary>
    [Fact]
    public async Task AdminPutUser_UsersAgreementCodeCacheAgreesWithUserAgreementCodesLiveRow_AfterPUT()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // S35/TASK-3506 (a5e3ce0): /api/admin/users PUT is admin-strict If-Match
        // (ADR-019 D2) — 428 without the header. GET the live ETag, then PUT with
        // If-Match (same idiom as AdminUserVersioningTests).
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        // emp001 seeded at agreement_code='AC'. Flip to 'HK' via PUT.
        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        putReq.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Read both — they MUST match.
        string cacheValue;
        await using (var cacheCmd = new NpgsqlCommand(
            "SELECT agreement_code FROM users WHERE user_id = @userId", conn))
        {
            cacheCmd.Parameters.AddWithValue("userId", userId);
            cacheValue = (string)(await cacheCmd.ExecuteScalarAsync())!;
        }

        string canonicalValue;
        await using (var canonCmd = new NpgsqlCommand(
            """
            SELECT agreement_code FROM user_agreement_codes
            WHERE user_id = @userId AND effective_to IS NULL
            """, conn))
        {
            canonCmd.Parameters.AddWithValue("userId", userId);
            canonicalValue = (string)(await canonCmd.ExecuteScalarAsync())!;
        }

        Assert.Equal("HK", cacheValue);
        Assert.Equal("HK", canonicalValue);
        Assert.Equal(canonicalValue, cacheValue);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a brand-new user via direct DB insert (NOT through
    /// AdminEndpoints POST which would also Case A INSERT a
    /// user_agreement_codes row). Leaves the new user without any
    /// user_agreement_codes row so Case A is reachable for that user.
    /// </summary>
    private async Task<string> CreateUserWithoutAgreementRowAsync()
    {
        var userId = "emp_s34_uac_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@userId, @userId, 'dev-only', 'S34 Fresh User', NULL,
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
            employeeId: "ADMIN_S34_QA",
            name: "S34 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
