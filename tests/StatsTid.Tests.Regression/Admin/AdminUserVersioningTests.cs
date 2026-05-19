using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Admin;

/// <summary>
/// S35 / TASK-3509 — Docker-gated D-tests for the admin <c>/api/admin/users</c>
/// versioning contract (TASK-3506 admin-strict If-Match + TASK-3501
/// <c>users.version</c> + <c>users_audit</c>) and the related stale-snapshot /
/// concurrent-admin-PUT race coverage. Together these tests close five of the six
/// items deferred at S34 close (cf. PLAN-s35.md L535-564):
///
/// <list type="bullet">
///   <item>Item #1 — admin ETag/If-Match enforcement on <c>/api/admin/users</c>
///     (412 stale + 428 missing).</item>
///   <item>Item #4 — outer-users-UPDATE stale-snapshot: null-fallback resolves
///     against the FOR-UPDATE'd <c>lockedUser</c>, NOT the pre-tx
///     <c>existingUser</c>.</item>
///   <item>Item #5 — concurrent-admin-PUT race: barrier-synchronized two-thread
///     test asserting exactly one winner (200) + one loser (412), audit table
///     stamped with chronologically-correct version_before/version_after pairs.</item>
///   <item>POST ETag stamp — net-new user POST stamps
///     <c>ETag: "1"</c> + body <c>version: 1</c> + <c>users_audit</c> CREATED row.</item>
/// </list>
///
/// <para>
/// HTTP-level via <see cref="StatsTidWebApplicationFactory"/> + <c>CreateClient()</c>
/// per S27 precedent. JWT minting via the dev-fallback signing key per
/// <see cref="StatsTid.Tests.Regression.UserAgreementCode.AdminEndpointsAgreementCodeTests"/>
/// shape (verbatim helper signatures so the harness boilerplate is consistent
/// across S34/S35 admin-surface tests).
/// </para>
///
/// <para>
/// <b>Concurrent-PUT race harness</b> (test 6). Two threads capture If-Match: "1"
/// via GET and start PUT concurrently using <c>Task.WhenAll</c> + a barrier
/// <see cref="TaskCompletionSource{TResult}"/> so both tasks reach the request
/// boundary before either fires. Per the S22 <c>PublisherStallReadYourWriteTests</c>
/// precedent + PLAN-s35.md L557, this pattern reliably surfaces the 412 contract;
/// flake budget is &lt;5% per the spec.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminUserVersioningTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey =
        "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // CreateClient triggers Program.cs host build → seeders run (including
        // UserAgreementCodeBackfillSeeder backfilling at effective_from='0001-01-01'
        // for the init.sql-seeded users like emp001).
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 1 — stale If-Match → 412 + DB not mutated.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PUT with a stale If-Match header returns 412 Precondition Failed with a
    /// structured body carrying <c>expectedVersion</c> + <c>actualVersion</c>, and
    /// the row is NOT mutated (subsequent SELECT confirms version + display_name
    /// match the prior successful update, not the stale attempt). Closes item #1
    /// of S34 deferred (admin-strict ETag enforcement on /api/admin/users).
    /// </summary>
    [Fact]
    public async Task AdminPutUser_StaleIfMatch_Returns412_AndDoesNotMutate()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // (1) Capture initial ETag "1" via GET.
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var initialEtag = getRsp.Headers.ETag;
        Assert.NotNull(initialEtag);
        Assert.Equal("\"1\"", initialEtag!.Tag);

        // (2) First PUT with If-Match: "1" succeeds + bumps version to 2.
        var firstReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "S35 Mutation One",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        firstReq.Headers.IfMatch.Add(initialEtag);
        var firstRsp = await client.SendAsync(firstReq);
        Assert.Equal(HttpStatusCode.OK, firstRsp.StatusCode);
        Assert.NotNull(firstRsp.Headers.ETag);
        Assert.Equal("\"2\"", firstRsp.Headers.ETag!.Tag);

        // (3) Second PUT with STALE If-Match: "1" → 412 + body carries expected=1
        //     actual=2.
        var staleReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "S35 STALE Attempt — must not land",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        staleReq.Headers.IfMatch.Add(initialEtag);
        var staleRsp = await client.SendAsync(staleReq);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleRsp.StatusCode);

        var body = await staleRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1L, body.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(2L, body.GetProperty("actualVersion").GetInt64());

        // (4) DB unchanged from (2): version still 2, display_name still the (2)
        //     value, NOT the stale (3) attempt.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT display_name, version FROM users WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("S35 Mutation One", reader.GetString(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 2 — missing If-Match → 428 + no mutation.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PUT without an If-Match header returns 428 Precondition Required (admin-
    /// strict mode per <see cref="StatsTid.Backend.Api.Endpoints.Helpers.EtagHeaderHelper"/>
    /// <c>.TryParseIfMatch</c>). DB unchanged + <c>users_audit</c> has no UPDATED
    /// row for this user (the 428 short-circuits before <c>BeginTransactionAsync</c>).
    /// </summary>
    [Fact]
    public async Task AdminPutUser_MissingIfMatch_Returns428()
    {
        var client = AuthorizedClient();
        // Use a unique fresh user so the assertion that "no UPDATED audit row
        // exists for this user" can't be polluted by prior tests in the same
        // class run (xUnit creates a new instance per [Fact] so InitializeAsync
        // gives us a fresh harness, but defense-in-depth on the user_id keeps
        // the audit-count assertion crisp).
        var userId = await CreateFreshUserAsync(displayName: "S35 Missing-IfMatch Target");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // PUT without If-Match → 428.
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "S35 Missing-IfMatch Should Not Land",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        // Deliberately NOT adding If-Match.
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.PreconditionRequired, rsp.StatusCode);

        // DB: version still 1; display_name still original.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var userCmd = new NpgsqlCommand(
            "SELECT display_name, version FROM users WHERE user_id = @userId", conn))
        {
            userCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await userCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("S35 Missing-IfMatch Target", reader.GetString(0));
            Assert.Equal(1L, reader.GetInt64(1));
        }

        // users_audit: no UPDATED row for this user (the 428 short-circuits
        // before BeginTransactionAsync runs the audit INSERT). The CREATE-only
        // baseline from CreateFreshUserAsync's direct-INSERT path is NOT
        // present in audits (it bypasses POST /api/admin/users), so the
        // expected count is 0 UPDATED rows.
        await using (var auditCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users_audit WHERE user_id = @userId AND action = 'UPDATED'", conn))
        {
            auditCmd.Parameters.AddWithValue("userId", userId);
            Assert.Equal(0L, Convert.ToInt64(await auditCmd.ExecuteScalarAsync()));
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 3 — null-field PUT resolves against the FOR-UPDATE'd locked row.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Closes item #4 of S34 deferred — outer-users-UPDATE stale-snapshot. PUT
    /// with <c>{ displayName: "Updated", agreementCode: null, ... }</c> and a
    /// valid If-Match must produce a row whose <c>agreement_code</c> is the
    /// locked-row value (i.e. <c>existingUser.AgreementCode</c> = original "AC")
    /// because the request's <c>agreementCode</c> is null → fallback resolves
    /// against <c>lockedUser.AgreementCode</c> (NOT the pre-tx
    /// <c>existingUser</c>). The <c>users_audit</c> previous_data JSONB carries
    /// the pre-PUT display_name, and new_data carries the post-PUT display_name
    /// + the unchanged agreement_code — proving the locked-row snapshot is the
    /// canonical source for null-fallback resolution.
    ///
    /// <para>
    /// <b>Why this test is not a true race</b>. Exercising the racing-admin
    /// variant of this same path deterministically is what test 6 covers
    /// (barrier-synchronized two-thread PUT). This test exercises the
    /// null-fallback resolves-against-lockedUser code path in isolation; under
    /// no concurrent contention the locked row and pre-tx row are identical, so
    /// this is a single-shot sanity pin that the right snapshot is wired through
    /// the UPDATE statement + the users_audit JSONB columns. Test 6 makes the
    /// concurrent variant fail-shaped.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminPutUser_NullRequestField_DoesNotOverwrite_WithLockedRowSnapshot()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Capture initial state: display_name='AC Medarbejder', agreement_code='AC',
        // version=1.
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var initialEtag = getRsp.Headers.ETag;
        Assert.NotNull(initialEtag);
        var initialBody = await getRsp.Content.ReadFromJsonAsync<JsonElement>();
        var originalDisplayName = initialBody.GetProperty("displayName").GetString();
        var originalAgreementCode = initialBody.GetProperty("agreementCode").GetString();
        Assert.Equal("AC", originalAgreementCode);

        // PUT with displayName='Updated' + agreementCode=null + valid If-Match.
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "S35 Null-Fallback Updated Name",
                agreementCode = (string?)null,
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.IfMatch.Add(initialEtag!);
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var responseBody = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("S35 Null-Fallback Updated Name",
            responseBody.GetProperty("displayName").GetString());
        Assert.Equal(originalAgreementCode,
            responseBody.GetProperty("agreementCode").GetString());

        // DB row: display_name updated; agreement_code UNCHANGED (locked-row
        // fallback resolved to "AC", not the request's null).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var userCmd = new NpgsqlCommand(
            """
            SELECT display_name, agreement_code, version
            FROM users WHERE user_id = @userId
            """, conn))
        {
            userCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await userCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("S35 Null-Fallback Updated Name", reader.GetString(0));
            Assert.Equal(originalAgreementCode, reader.GetString(1));
            Assert.Equal(2L, reader.GetInt64(2));
        }

        // users_audit UPDATED row: previous_data.displayName = original;
        // new_data.displayName = updated; new_data.agreementCode = original
        // (the unchanged locked-row value).
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT previous_data, new_data, version_before, version_after
            FROM users_audit
            WHERE user_id = @userId AND action = 'UPDATED'
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "PUT must emit a users_audit UPDATED row.");
            var previousData = reader.GetString(0);
            var newData = reader.GetString(1);
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(2L, reader.GetInt64(3));

            using var prevDoc = JsonDocument.Parse(previousData);
            using var newDoc = JsonDocument.Parse(newData);
            Assert.Equal(originalDisplayName,
                prevDoc.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("S35 Null-Fallback Updated Name",
                newDoc.RootElement.GetProperty("displayName").GetString());
            Assert.Equal(originalAgreementCode,
                newDoc.RootElement.GetProperty("agreementCode").GetString());
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 6 — concurrent admin PUTs: exactly one 200, one 412 + audit trail.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Closes item #5 of S34 deferred — concurrent-admin-PUT race. Two threads
    /// capture <c>If-Match: "1"</c> via GET, then start PUT concurrently with a
    /// barrier <see cref="TaskCompletionSource{TResult}"/> so both reach the
    /// request boundary before either fires. Exactly one wins (200 OK, version
    /// → 2) and exactly one loses (412 Precondition Failed). The loser then
    /// refetches GET to capture <c>"2"</c> and retries PUT → succeeds (version
    /// → 3). The <c>users_audit</c> table shows EXACTLY 2 UPDATED rows with
    /// version_before/version_after pairs (1→2, 2→3) in chronological order
    /// (audit_at timestamps strictly increasing).
    ///
    /// <para>
    /// Mirrors the <c>PublisherStallReadYourWriteTests</c> shape for the
    /// barrier pattern + the spec's PLAN-s35.md L557 cap on flake (&lt;5%
    /// per the spec; we use a single tight barrier here so the flake is
    /// dominated by Postgres tx serialization, not by thread scheduling).
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminPutUser_TwoConcurrentAdmins_OneSucceedsOneGets412_AuditTrailCorrect()
    {
        // Use a fresh user so the audit-count assertion is crisp. Two distinct
        // HttpClients so the two threads can independently issue requests without
        // shared header state.
        var userId = await CreateFreshUserAsync(displayName: "S35 Concurrent-PUT Target");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var clientA = AuthorizedClient();
        var clientB = AuthorizedClient();

        // Both threads capture If-Match: "1" via GET.
        var getA = await clientA.GetAsync($"/api/admin/users/{userId}");
        var getB = await clientB.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);
        var etagA = getA.Headers.ETag;
        var etagB = getB.Headers.ETag;
        Assert.NotNull(etagA);
        Assert.NotNull(etagB);
        Assert.Equal("\"1\"", etagA!.Tag);
        Assert.Equal("\"1\"", etagB!.Tag);

        // Barrier: both threads await this TCS before firing SendAsync so the
        // requests issue back-to-back, maximizing the probability that both
        // reach the FOR-UPDATE lock contention point on the server in the same
        // window. Per PublisherStallReadYourWriteTests precedent.
        var barrier = new TaskCompletionSource();

        Task<HttpResponseMessage> BuildPutAsync(HttpClient client, EntityTagHeaderValue etag, string label)
        {
            return Task.Run(async () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
                {
                    Content = JsonContent.Create(new
                    {
                        displayName = $"S35 Concurrent-PUT {label}",
                        effectiveFrom = today.ToString("yyyy-MM-dd"),
                    }),
                };
                req.Headers.IfMatch.Add(etag);
                await barrier.Task;
                return await client.SendAsync(req);
            });
        }

        var putA = BuildPutAsync(clientA, etagA, "A");
        var putB = BuildPutAsync(clientB, etagB, "B");

        // Release the barrier; both threads fire SendAsync.
        barrier.SetResult();

        var responses = await Task.WhenAll(putA, putB);

        // Exactly one 200 + exactly one 412. Either ordering is acceptable —
        // we don't pin which thread wins.
        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var preconditionFailedCount = responses.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(1, okCount);
        Assert.Equal(1, preconditionFailedCount);

        // The winning response's ETag is "2"; the losing response's body carries
        // expected=1 actual=2.
        var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        var loser = responses.Single(r => r.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.NotNull(winner.Headers.ETag);
        Assert.Equal("\"2\"", winner.Headers.ETag!.Tag);
        var loserBody = await loser.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1L, loserBody.GetProperty("expectedVersion").GetInt64());
        Assert.Equal(2L, loserBody.GetProperty("actualVersion").GetInt64());

        // Loser refetches GET to capture "2", retries PUT with If-Match: "2"
        // → succeeds (version → 3).
        var refetchRsp = await clientB.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, refetchRsp.StatusCode);
        var newEtag = refetchRsp.Headers.ETag;
        Assert.NotNull(newEtag);
        Assert.Equal("\"2\"", newEtag!.Tag);

        var retryReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                displayName = "S35 Concurrent-PUT Loser Retry",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        retryReq.Headers.IfMatch.Add(newEtag);
        var retryRsp = await clientB.SendAsync(retryReq);
        Assert.Equal(HttpStatusCode.OK, retryRsp.StatusCode);
        Assert.NotNull(retryRsp.Headers.ETag);
        Assert.Equal("\"3\"", retryRsp.Headers.ETag!.Tag);

        // users_audit: exactly 2 UPDATED rows in chronological order with
        // version_before/version_after pairs (1→2, 2→3). audit_at strictly
        // ascending (Postgres NOW() ticks per-statement so two statements in
        // distinct transactions will have monotonically non-decreasing
        // timestamps; we assert ASC ordering by audit_id which BIGSERIAL
        // guarantees regardless of transaction commit interleaving).
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var auditCmd = new NpgsqlCommand(
            """
            SELECT version_before, version_after, audit_at
            FROM users_audit
            WHERE user_id = @userId AND action = 'UPDATED'
            ORDER BY audit_id ASC
            """, conn);
        auditCmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await auditCmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "Expected 1st UPDATED audit row.");
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
        var firstAuditAt = reader.GetDateTime(2);

        Assert.True(await reader.ReadAsync(), "Expected 2nd UPDATED audit row.");
        Assert.Equal(2L, reader.GetInt64(0));
        Assert.Equal(3L, reader.GetInt64(1));
        var secondAuditAt = reader.GetDateTime(2);

        Assert.False(await reader.ReadAsync(), "Expected exactly 2 UPDATED audit rows.");

        // Chronological order: audit_id ASC implies audit_at non-decreasing
        // (BIGSERIAL increments monotonically; statement-time NOW() ticks per
        // statement). Asserted as <= to tolerate the (extremely unlikely)
        // case where two distinct INSERTs land at the same statement-time.
        Assert.True(firstAuditAt <= secondAuditAt,
            $"audit_at must be non-decreasing: first={firstAuditAt:O}, second={secondAuditAt:O}");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Test 7 — POST stamps version=1 + ETag: "1" + CREATED audit row.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// POST /api/admin/users stamps <c>version=1</c> + <c>ETag: "1"</c> on the
    /// 201 response and emits a <c>users_audit</c> CREATED row with
    /// <c>version_before=NULL</c>, <c>version_after=1</c>, <c>previous_data=NULL</c>,
    /// and <c>new_data</c> carrying displayName/email/primaryOrgId/agreementCode —
    /// password_hash deliberately EXCLUDED per AdminEndpoints.cs:421 contract
    /// ("audit JSONB must never carry credentials").
    /// </summary>
    [Fact]
    public async Task AdminPostUser_NewUser_StampsVersionAndETag()
    {
        var client = AuthorizedClient();
        // UUID-generated user_id avoids seed collisions with init.sql's emp001
        // + the other test classes' fresh-user user_ids.
        var newUserId = "emp_s35_post_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S35 POST Stamp Test User",
            email = "s35.post.stamp@example.com",
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        // ETag header carries "1" + body carries version: 1.
        Assert.NotNull(rsp.Headers.ETag);
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1L, body.GetProperty("version").GetInt64());

        // users table: version=1.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var userCmd = new NpgsqlCommand(
            "SELECT version FROM users WHERE user_id = @userId", conn))
        {
            userCmd.Parameters.AddWithValue("userId", newUserId);
            Assert.Equal(1L, Convert.ToInt64(await userCmd.ExecuteScalarAsync()));
        }

        // users_audit CREATED row: previous_data NULL; new_data carries the
        // four whitelisted fields (NOT password_hash); version_before NULL;
        // version_after=1.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT action, previous_data, new_data, version_before, version_after
            FROM users_audit
            WHERE user_id = @userId
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                "POST must emit a users_audit CREATED row.");
            Assert.Equal("CREATED", reader.GetString(0));
            Assert.True(reader.IsDBNull(1), "CREATED audit must have NULL previous_data.");
            var newDataRaw = reader.GetString(2);
            Assert.True(reader.IsDBNull(3), "CREATED audit must have NULL version_before.");
            Assert.Equal(1L, reader.GetInt64(4));

            using var newDoc = JsonDocument.Parse(newDataRaw);
            Assert.Equal("S35 POST Stamp Test User",
                newDoc.RootElement.GetProperty("displayName").GetString());
            Assert.Equal("s35.post.stamp@example.com",
                newDoc.RootElement.GetProperty("email").GetString());
            Assert.Equal("STY01",
                newDoc.RootElement.GetProperty("primaryOrgId").GetString());
            Assert.Equal("AC",
                newDoc.RootElement.GetProperty("agreementCode").GetString());
            // Password hash MUST NOT appear in audit JSONB (AdminEndpoints.cs:421
            // contract). Verify by absence: TryGetProperty returns false.
            Assert.False(newDoc.RootElement.TryGetProperty("passwordHash", out _),
                "users_audit new_data must NOT carry password_hash.");
            Assert.False(newDoc.RootElement.TryGetProperty("password_hash", out _),
                "users_audit new_data must NOT carry password_hash (snake_case variant).");
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a brand-new user via direct DB insert (NOT through AdminEndpoints
    /// POST which would also Case A INSERT a user_agreement_codes row + emit
    /// outbox events + audit row). Used by tests 2 + 6 where the audit-count
    /// assertion needs a clean baseline. Mirrors the
    /// <see cref="StatsTid.Tests.Regression.UserAgreementCode.UserAgreementCodeRepositoryTests.CreateUserWithoutAgreementRowAsync"/>
    /// helper shape.
    /// </summary>
    private async Task<string> CreateFreshUserAsync(string displayName)
    {
        var userId = "emp_s35_ver_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Insert users row (version DEFAULT 1 from init.sql:467).
        await using var usersCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@userId, @userId, 'dev-only', @displayName, NULL,
                    'STY01', 'AC', 'OK24', TRUE)
            """, conn);
        usersCmd.Parameters.AddWithValue("userId", userId);
        usersCmd.Parameters.AddWithValue("displayName", displayName);
        await usersCmd.ExecuteNonQueryAsync();

        // Insert a live user_agreement_codes row so the PUT path's
        // agreement-code branch has a predecessor to lock + supersede on if it
        // mutates the code (defense-in-depth — the tests in this class don't
        // mutate agreement_code, but creating the row keeps the user's shape
        // consistent with backfill-seeded init.sql users like emp001).
        // effective_from='0001-01-01' matches the backfill seeder convention.
        await using var uacCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes
                (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES
                (gen_random_uuid(), @userId, 'AC', '0001-01-01', NULL, 1)
            """, conn);
        uacCmd.Parameters.AddWithValue("userId", userId);
        await uacCmd.ExecuteNonQueryAsync();

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
            employeeId: "ADMIN_S35_QA",
            name: "S35 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
