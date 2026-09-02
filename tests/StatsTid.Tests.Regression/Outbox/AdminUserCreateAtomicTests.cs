using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S31 / TASK-3110 D-tests — 4-way atomicity contract on
/// <c>POST /api/admin/users</c> (TASK-3108 AdminEndpoints extension). The extended
/// handler commits four operations in a single transaction per ADR-018 D3:
///
/// <list type="bullet">
///   <item><description>(1) <c>users</c> INSERT</description></item>
///   <item><description>(2) <c>employee_profiles</c> INSERT (S31 invariant: every
///   active user has exactly one live profile row)</description></item>
///   <item><description>(3) <c>UserCreated</c> outbox event on stream
///   <c>user-{userId}</c></description></item>
///   <item><description>(4) <c>EmployeeProfileCreated</c> outbox event on stream
///   <c>employee-profile-{userId}</c></description></item>
/// </list>
///
/// <para>
/// Two tests: a happy-path 4-way emit (POST succeeds → all four operations land) and
/// a negative duplicate-username path (POST is rejected by the pre-flight 409 → NO
/// new rows in <c>users</c> or <c>employee_profiles</c>, NO new outbox events on
/// either stream). The negative case is the load-bearing atomicity pin: a partial
/// rollback (e.g. only users INSERTed) would manifest here as a leaked row or event.
/// </para>
///
/// <para>
/// The pre-flight existence check in <c>AdminEndpoints.cs:319-327</c> runs OUTSIDE
/// the transaction and short-circuits before <c>BeginTransactionAsync</c> — the
/// duplicate-username test therefore pins the "no leaked state" invariant via the
/// short-circuit path. A genuine in-tx rollback (e.g. on outbox throw) is covered
/// by the retired <c>Outbox.AdminAtomicTests</c>' related sub-shape (i) test against
/// <c>POST /api/admin/organizations</c> + S26 <c>TxContractTests</c>.
/// </para>
///
/// <para>
/// S136 / S137 hire-date pins (bottom of the class): the optional
/// <c>employmentStartDate</c> is stored verbatim when supplied and — since the S137 /
/// TASK-13708 owner ruling (2026-09-02) — DEFAULTS to the profile row's
/// <c>effective_from</c> (today) when omitted, with the CREATED <c>users_audit</c> row
/// recording the effective value plus an <c>employmentStartDateDefaulted</c> marker.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminUserCreateAtomicTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

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

    // ═════════════════════════════════════════════════════════════════════════
    // Happy path — POST /api/admin/users emits all four operations atomically.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task AdminUserCreate_AtomicallyCreatesProfileRowAndEmitsEvent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Unique userId per test run — keeps xUnit parallel test execution clean.
        var newUserId = "emp_s31_atomic_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var body = new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S31 Atomic Test User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };

        var rsp = await client.PostAsJsonAsync("/api/admin/users", body);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        // ── DB assertions: all four operations landed.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) users row
        await using (var usersCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE user_id = @userId", conn))
        {
            usersCmd.Parameters.AddWithValue("userId", newUserId);
            Assert.Equal(1L, Convert.ToInt64(await usersCmd.ExecuteScalarAsync()));
        }

        // (2) employee_profiles row with S31 defaults (part_time_fraction=1.000,
        //     position=NULL, version=1).
        // S53/TASK-5306 (a7aee58): employee_profiles.weekly_norm_hours removed
        // (universal 37h norm); column dropped from SELECT, ordinals shift down by one.
        await using (var profileCmd = new NpgsqlCommand(
            """
            SELECT part_time_fraction, position, version
            FROM employee_profiles
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("employeeId", newUserId);
            await using var reader = await profileCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                $"Expected one live employee_profiles row for '{newUserId}'.");
            Assert.Equal(1.000m, reader.GetDecimal(0));
            Assert.True(reader.IsDBNull(1), "Position should default to NULL for new users.");
            Assert.Equal(1L, reader.GetInt64(2));
            // Partial-unique-index guarantees exactly one live row.
            Assert.False(await reader.ReadAsync(),
                $"Expected exactly one live row for '{newUserId}', found more than one.");
        }

        // (3) UserCreated outbox event on stream user-{userId}.
        var userStreamId = $"user-{newUserId}";
        await using (var userEventCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'UserCreated'
            """, conn))
        {
            userEventCmd.Parameters.AddWithValue("streamId", userStreamId);
            Assert.Equal(1L, Convert.ToInt64(await userEventCmd.ExecuteScalarAsync()));
        }

        // (4) EmployeeProfileCreated outbox event on stream employee-profile-{userId}.
        var profileStreamId = $"employee-profile-{newUserId}";
        await using (var profileEventCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'EmployeeProfileCreated'
            """, conn))
        {
            profileEventCmd.Parameters.AddWithValue("streamId", profileStreamId);
            Assert.Equal(1L, Convert.ToInt64(await profileEventCmd.ExecuteScalarAsync()));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Negative — duplicate username pre-flight 409: NO rows + NO outbox events.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task AdminUserCreate_OnDuplicateUsername_RollsBackAllFourOperations()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // First, create a baseline user that we can collide against. Use a unique id so
        // sibling parallel tests don't collide on this row.
        var seedUserId = "emp_s31_dup_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var seedBody = new
        {
            userId = seedUserId,
            username = seedUserId,
            password = "TestPassword123!",
            displayName = "S31 Dup-Seed User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };
        var seedRsp = await client.PostAsJsonAsync("/api/admin/users", seedBody);
        Assert.Equal(HttpStatusCode.Created, seedRsp.StatusCode);

        // Now POST a SECOND user that re-uses the same username. New userId, same
        // username → endpoint pre-flight at AdminEndpoints.cs:319-327 returns 409.
        var collidingUserId = "emp_s31_collide_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var collideBody = new
        {
            userId = collidingUserId,    // DIFFERENT from seed's userId
            username = seedUserId,       // SAME as seed's username → triggers 409
            password = "AnotherPassword!",
            displayName = "S31 Collision Attempt",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };

        var collideRsp = await client.PostAsJsonAsync("/api/admin/users", collideBody);
        Assert.Equal(HttpStatusCode.Conflict, collideRsp.StatusCode);

        // ── Atomicity assertions: the colliding userId must have left no trace
        //    in any of the four mutation surfaces.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) NO users row with the colliding userId.
        await using (var usersCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE user_id = @userId", conn))
        {
            usersCmd.Parameters.AddWithValue("userId", collidingUserId);
            Assert.Equal(0L, Convert.ToInt64(await usersCmd.ExecuteScalarAsync()));
        }

        // (2) NO employee_profiles row with the colliding employeeId.
        await using (var profileCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM employee_profiles
            WHERE employee_id = @employeeId
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("employeeId", collidingUserId);
            Assert.Equal(0L, Convert.ToInt64(await profileCmd.ExecuteScalarAsync()));
        }

        // (3) NO UserCreated outbox event on stream user-{collidingUserId}.
        var userStreamId = $"user-{collidingUserId}";
        await using (var userEventCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @streamId", conn))
        {
            userEventCmd.Parameters.AddWithValue("streamId", userStreamId);
            Assert.Equal(0L, Convert.ToInt64(await userEventCmd.ExecuteScalarAsync()));
        }

        // (4) NO EmployeeProfileCreated outbox event on stream
        //     employee-profile-{collidingUserId}.
        var profileStreamId = $"employee-profile-{collidingUserId}";
        await using (var profileEventCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @streamId", conn))
        {
            profileEventCmd.Parameters.AddWithValue("streamId", profileStreamId);
            Assert.Equal(0L, Convert.ToInt64(await profileEventCmd.ExecuteScalarAsync()));
        }

        // Defense in depth — the seed user's row is still present (proves the 409
        // path didn't accidentally cascade a delete on the colliding-username row).
        await using (var seedSurvivesCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE user_id = @userId", conn))
        {
            seedSurvivesCmd.Parameters.AddWithValue("userId", seedUserId);
            Assert.Equal(1L, Convert.ToInt64(await seedSurvivesCmd.ExecuteScalarAsync()));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // S136 / ADR-040 — optional employmentStartDate on create: stored + audited.
    // S137 / TASK-13708 — omitted ⇒ defaults to the profile's effective_from.
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create WITH <c>employmentStartDate</c> ⇒ the value lands VERBATIM in
    /// <c>users.employment_start_date</c> AND in the CREATED <c>users_audit.new_data</c>
    /// payload, with the S137 <c>employmentStartDateDefaulted</c> marker <c>false</c>
    /// (the admin supplied it; the system did not default it). The audit half is
    /// load-bearing: <c>new_data</c> is a hand-enumerated subset (not a full-row
    /// snapshot), so a field missing there is a field whose origin is unprovable after
    /// the fact (Auditability invariant).
    /// </summary>
    [Fact]
    public async Task AdminUserCreate_WithEmploymentStartDate_StoresAndAuditsIt()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var newUserId = "emp_s136_esd_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var hireDate = new DateOnly(2026, 3, 1);

        var body = new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S136 Hire-Date Test User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
            employmentStartDate = hireDate,
        };

        var rsp = await client.PostAsJsonAsync("/api/admin/users", body);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Stored: users.employment_start_date carries the sent date.
        await using (var usersCmd = new NpgsqlCommand(
            "SELECT employment_start_date FROM users WHERE user_id = @userId", conn))
        {
            usersCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await usersCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"Expected a users row for '{newUserId}'.");
            Assert.False(reader.IsDBNull(0), "employment_start_date should be stored, not NULL.");
            Assert.Equal(hireDate, reader.GetFieldValue<DateOnly>(0));
        }

        // Audited: the CREATED users_audit row's new_data contains the hire date
        // (DateOnly serializes as ISO yyyy-MM-dd) AND the S137 marker says it was
        // SUPPLIED, not defaulted.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT new_data->>'employmentStartDate',
                   (new_data->>'employmentStartDateDefaulted')::boolean
            FROM users_audit
            WHERE user_id = @userId AND action = 'CREATED'
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                $"Expected a CREATED users_audit row for '{newUserId}'.");
            Assert.Equal("2026-03-01", reader.GetString(0));
            Assert.False(reader.IsDBNull(1),
                "new_data must carry employmentStartDateDefaulted (S137 / TASK-13708).");
            Assert.False(reader.GetBoolean(1),
                "A supplied hire date must audit as employmentStartDateDefaulted=false.");
        }
    }

    /// <summary>
    /// Create WITHOUT <c>employmentStartDate</c> ⇒ the stored hire date IS the live
    /// profile row's <c>effective_from</c> (today, UTC) — never NULL from this path.
    ///
    /// <para>
    /// FLIPPED PIN — S137 / TASK-13708, OWNER RULING 2026-09-02 ("default the hire date
    /// to the profile date at admin create"). The S136 version of this test
    /// (<c>AdminUserCreate_WithoutEmploymentStartDate_StoresNull</c>) asserted the OLD
    /// expectation: omitted ⇒ <c>users.employment_start_date IS NULL</c> (ADR-040 D2
    /// "unbounded past") and <c>new_data->>'employmentStartDate'</c> audited as JSON null.
    /// That shape is now WRONG by ruling, so this test is RED against the S136 handler by
    /// construction. WHY it changed (plain language): S137 made profile effective dates
    /// payroll segment boundaries; a hire without a recorded hire date got a profile row
    /// dated today inside an unbounded employment window, so the creation month held a
    /// mid-month profile boundary with no employment edge to explain it — the planner
    /// refused the month and the compliance check fell into the old profile-not-found 500.
    /// Recording the hire date makes that same date an EmploymentStarted edge, which
    /// outranks the profile-change cause and makes the first month plannable.
    /// </para>
    ///
    /// <para>
    /// Pins: (1) users.employment_start_date == live employee_profiles.effective_from,
    /// both non-NULL, read in ONE statement so they are compared on the same snapshot;
    /// (2) the CREATED users_audit row carries that EFFECTIVE date with
    /// <c>employmentStartDateDefaulted = true</c> (an auditor can tell a defaulted date
    /// from a coincidentally-supplied one); (3) the EmployeeProfileCreated outbox event's
    /// effectiveFrom equals the same date (row/event parity, ADR-018 D3 — now shared-
    /// variable by construction in the handler); (4) the other hand-enumerated audit fields
    /// and the S31 single-live-profile invariant are unchanged. Seeded/legacy NULLs are
    /// NOT in scope here — the ruling changes only what a NEW admin create stores.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminUserCreate_WithoutEmploymentStartDate_DefaultsToProfileEffectiveFrom()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());

        var newUserId = "emp_s137_dflt_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        // Deliberately the pre-S136 body shape — no employmentStartDate key at all.
        var body = new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S137 Defaulted-Hire-Date Test User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };

        var rsp = await client.PostAsJsonAsync("/api/admin/users", body);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) Stored: users.employment_start_date == the LIVE profile row's effective_from,
        //     neither NULL. One statement, one snapshot — the two columns are compared as
        //     the database holds them, not via two reads that could straddle midnight.
        DateOnly effectiveFrom;
        await using (var joinCmd = new NpgsqlCommand(
            """
            SELECT u.employment_start_date, p.effective_from
            FROM users u
            JOIN employee_profiles p
              ON p.employee_id = u.user_id AND p.effective_to IS NULL
            WHERE u.user_id = @userId
            """, conn))
        {
            joinCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await joinCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                $"Expected a users row joined to one live employee_profiles row for '{newUserId}'.");
            Assert.False(reader.IsDBNull(0),
                "employment_start_date must NOT be NULL when omitted — S137 / TASK-13708 defaults it " +
                "to the profile's effective_from (the S136 'omitted ⇒ NULL' behaviour is retired).");
            Assert.False(reader.IsDBNull(1), "The live profile row must carry effective_from.");
            var storedStart = reader.GetFieldValue<DateOnly>(0);
            effectiveFrom = reader.GetFieldValue<DateOnly>(1);
            Assert.Equal(effectiveFrom, storedStart);
            // "Hired today": the shared value is today (UTC). ±1 day tolerates a midnight
            // straddle between the POST and this read; anything else is a wrong default.
            var utcToday = DateOnly.FromDateTime(DateTime.UtcNow);
            Assert.InRange(storedStart.DayNumber, utcToday.DayNumber - 1, utcToday.DayNumber + 1);
            Assert.False(await reader.ReadAsync(),
                $"Expected exactly one live profile row for '{newUserId}', found more than one.");
        }
        var effectiveFromIso = effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // (2) Audited: the CREATED users_audit row records the EFFECTIVE (defaulted) date —
        //     not JSON null as in S136 — plus the marker that says the system defaulted it.
        //     The pre-existing hand-enumerated fields survive.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT new_data->>'employmentStartDate',
                   (new_data->>'employmentStartDateDefaulted')::boolean,
                   new_data->>'displayName',
                   new_data->>'agreementCode'
            FROM users_audit
            WHERE user_id = @userId AND action = 'CREATED'
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                $"Expected a CREATED users_audit row for '{newUserId}'.");
            Assert.False(reader.IsDBNull(0),
                "new_data.employmentStartDate must be the effective stored date, not JSON null.");
            Assert.Equal(effectiveFromIso, reader.GetString(0));
            Assert.False(reader.IsDBNull(1),
                "new_data must carry employmentStartDateDefaulted (S137 / TASK-13708).");
            Assert.True(reader.GetBoolean(1),
                "An omitted hire date must audit as employmentStartDateDefaulted=true.");
            Assert.Equal("S137 Defaulted-Hire-Date Test User", reader.GetString(2));
            Assert.Equal("AC", reader.GetString(3));
        }

        // (3) Row/event parity: the EmployeeProfileCreated event on the profile stream
        //     carries the SAME effectiveFrom as the row (and therefore as the hire date).
        //     The handler now feeds one variable to the row, the event and the hire date.
        var profileStreamId = $"employee-profile-{newUserId}";
        await using (var profileEventCmd = new NpgsqlCommand(
            """
            SELECT event_payload ->> 'effectiveFrom'
            FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'EmployeeProfileCreated'
            """, conn))
        {
            profileEventCmd.Parameters.AddWithValue("streamId", profileStreamId);
            var eventEffectiveFrom = await profileEventCmd.ExecuteScalarAsync();
            Assert.Equal(effectiveFromIso, eventEffectiveFrom);
        }

        // (4) Everything else unchanged: the S31 live profile row still rides the same tx.
        await using (var profileCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM employee_profiles
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("employeeId", newUserId);
            Assert.Equal(1L, Convert.ToInt64(await profileCmd.ExecuteScalarAsync()));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
            employeeId: "ADMIN_S31_QA",
            name: "S31 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
