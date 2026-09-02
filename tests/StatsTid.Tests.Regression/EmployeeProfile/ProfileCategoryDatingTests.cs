using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S137 / TASK-13704 — pins for the ADR-040 D4 dated <c>employment_category</c> column on
/// <c>employee_profiles</c> (the READ half; editability is Increment 3):
///
/// <list type="bullet">
///   <item><b>Case C pin</b> — a post-migration supersession carries the correct dated
///     category on BOTH rows: the closed predecessor keeps it and the successor row (born in
///     <c>InsertLiveRowAsync</c>) copies it from <c>users</c> same-tx, so dated == live holds
///     ACROSS a supersession (the S137 invariant; the write-path half the original
///     agreement-code precedent was missing — the review's BLOCKER lesson).</item>
///   <item><b>Census pin</b> — after every production write path has run (the app-boot
///     EmployeeProfileSeeder, <c>CreateAsync</c>, <c>SupersedeAndCreateAsync</c> Cases A and
///     C, and the AdminEndpoints 4-way-atomic user-create INSERT), NO profile row carries a
///     NULL category and every row's dated value equals its user's live value.</item>
///   <item><b>COALESCE read posture</b> — the hydrated read PREFERS the dated cell and
///     degrades to the live <c>users</c> value on NULL (a missed write can never crash the
///     read or mislabel — it falls back to the definitionally-correct live value; the ruled
///     Reviewer-B1 fail-safe). Both directions falsifiable: a NULLed dated cell reads the
///     live value; a deliberately-diverged dated cell WINS over live.</item>
///   <item><b><c>GetEffectiveFromDatesAsync</c> fenceposts</b> — the S137 payroll planner's
///     EmployeeProfileChange boundary feed (TASK-13702): strictly-after lower bound
///     (a row AT <c>afterExclusive</c> is NOT a boundary — the segment already starts
///     there), inclusive upper bound, ascending order, history + live rows alike.</item>
/// </list>
///
/// <para>
/// Repo-direct facts use the raw <see cref="EmployeeProfileRepository"/> against the
/// per-test container; the census fact additionally drives the real
/// <c>POST /api/admin/users</c> through the WAF host — the
/// <see cref="EmployeeProfileLifecycleTests"/> conventions throughout.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileCategoryDatingTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey =
        "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>A NON-default category (users DEFAULT is 'Standard') so a write path that
    /// forgot the copy-from-users could never pass by coincidence of defaults.</summary>
    private const string NonDefaultCategory = "Fuldmægtig";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private EmployeeProfileRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // CreateClient triggers Program.cs host build → EmployeeProfileSeeder backfills one
        // live profile row per seed user — the SEEDER write path the census fact counts on.
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
    // Case C pin — dated == live across a supersession
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Post-migration Case C: predecessor created (with the dated category copied from
    /// users), backdated to yesterday, then superseded at today. The closed predecessor
    /// KEEPS its dated category and the successor row carries the same (copied same-tx from
    /// users inside <c>InsertLiveRowAsync</c>) — dated == live across the supersession.
    /// </summary>
    [Fact]
    public async Task SupersedeAndCreate_CaseC_SuccessorCarriesDatedCategory_DatedEqualsLiveAcrossSupersession()
    {
        var employeeId = await CreateUserWithoutProfileAsync(NonDefaultCategory);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        // Predecessor via the production CreateAsync path (stamps effective_from = today).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.CreateAsync(conn, tx, new EmployeeProfileCreateRequest(
                EmployeeId: employeeId, PartTimeFraction: 1.000m, Position: null));
            await tx.CommitAsync();
        }

        // Backdate the predecessor so SupersedeAndCreateAsync(today) routes to Case C
        // (the EmployeeProfileLifecycleTests Case C convention).
        await ExecAsync(
            """
            UPDATE employee_profiles SET effective_from = @p0
            WHERE employee_id = @p1 AND effective_to IS NULL
            """, yesterday, employeeId);

        SaveEmployeeProfileResult result;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            result = await _repo.SupersedeAndCreateAsync(conn, tx,
                new EmployeeProfileSupersedeRequest(
                    EmployeeId: employeeId,
                    PartTimeFraction: 0.800m,
                    Position: "Specialist",
                    EffectiveFrom: today),
                expectedVersion: 1L);
            await tx.CommitAsync();
        }
        Assert.Equal(SaveEmployeeProfileOutcome.Superseded, result.Outcome);

        // Closed predecessor: dated category intact.
        Assert.Equal(NonDefaultCategory, await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = @p0 AND effective_to IS NOT NULL
            """, employeeId));
        // Successor (live) row: dated category copied from users — NOT NULL, NOT default.
        Assert.Equal(NonDefaultCategory, await ScalarStringAsync(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = @p0 AND effective_to IS NULL
            """, employeeId));
        // dated == live, asserted against users itself (not a literal twice).
        Assert.Equal(0, await CountAsync(
            """
            SELECT COUNT(*) FROM employee_profiles ep
            JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.employee_id = @p0
              AND ep.employment_category IS DISTINCT FROM u.employment_category
            """, employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Census pin — no NULL categories after every production write path
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exercises all four production INSERT paths — the app-boot seeder (already ran at
    /// host build), <c>CreateAsync</c>, <c>SupersedeAndCreateAsync</c> Case A and Case C
    /// (both serve <c>InsertLiveRowAsync</c>), and the AdminEndpoints 4-way-atomic
    /// user-create — then takes the census: ZERO NULL dated categories, and every row
    /// (history rows included) equals its user's live value.
    /// </summary>
    [Fact]
    public async Task Census_AfterEveryProductionWritePath_NoNullCategories_DatedEqualsLive()
    {
        // Path 2: CreateAsync (path 1, the seeder, ran at host build in InitializeAsync).
        var createUser = await CreateUserWithoutProfileAsync(NonDefaultCategory);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.CreateAsync(conn, tx, new EmployeeProfileCreateRequest(
                EmployeeId: createUser, PartTimeFraction: 1.000m, Position: null));
            await tx.CommitAsync();
        }

        // Path 3a: SupersedeAndCreateAsync Case A (net-new via InsertLiveRowAsync).
        var caseAUser = await CreateUserWithoutProfileAsync("Chefkonsulent");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var res = await _repo.SupersedeAndCreateAsync(conn, tx,
                new EmployeeProfileSupersedeRequest(caseAUser, 1.000m, null, today),
                expectedVersion: null);
            Assert.Equal(SaveEmployeeProfileOutcome.Created, res.Outcome);
            await tx.CommitAsync();
        }

        // Path 3b: Case C on the same user (successor via InsertLiveRowAsync; also leaves
        // a HISTORY row in the census population).
        await ExecAsync(
            """
            UPDATE employee_profiles SET effective_from = @p0
            WHERE employee_id = @p1 AND effective_to IS NULL
            """, today.AddDays(-1), caseAUser);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var res = await _repo.SupersedeAndCreateAsync(conn, tx,
                new EmployeeProfileSupersedeRequest(caseAUser, 0.900m, "Konsulent", today),
                expectedVersion: 1L);
            Assert.Equal(SaveEmployeeProfileOutcome.Superseded, res.Outcome);
            await tx.CommitAsync();
        }

        // Path 4: the AdminEndpoints user-create 4-way-atomic profile INSERT (the ONE
        // authorized Backend touch — the real HTTP surface, not a repo call).
        var client = AuthorizedClient();
        var postRsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = "emp_s137_post",
            username = "emp_s137_post",
            password = "S137-Census-Pw!1",
            displayName = "S137 Census POST User",
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.True(postRsp.IsSuccessStatusCode,
            $"POST /api/admin/users failed: {(int)postRsp.StatusCode} {await postRsp.Content.ReadAsStringAsync()}");

        // ── The census ────────────────────────────────────────────────────────
        // Non-empty population sanity (seeder rows + the four writes above).
        Assert.True(await CountAsync("SELECT COUNT(*) FROM employee_profiles") >= 5,
            "Census population unexpectedly small — the write paths above did not all run.");
        // Zero NULL dated categories anywhere (live + history alike).
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM employee_profiles WHERE employment_category IS NULL"));
        // dated == live for every row — the S137 invariant, checked against users itself.
        Assert.Equal(0, await CountAsync(
            """
            SELECT COUNT(*) FROM employee_profiles ep
            JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.employment_category IS DISTINCT FROM u.employment_category
            """));
    }

    // ═════════════════════════════════════════════════════════════════════
    // COALESCE read posture — dated preferred, live fallback on NULL
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The hydrated read's <c>COALESCE(ep.employment_category, u.employment_category)</c>,
    /// falsified in BOTH directions: (a) a NULLed dated cell degrades to the live users
    /// value — never a crash or mislabel (the ruled fail-safe posture for a missed write);
    /// (b) a deliberately-diverged dated cell WINS over live (proves the COALESCE argument
    /// order — dated is preferred, not merely present).
    /// </summary>
    [Fact]
    public async Task Read_NullDatedCategory_FallsBackToLive_DivergedDatedCategory_Wins()
    {
        var employeeId = await CreateUserWithoutProfileAsync(NonDefaultCategory);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.CreateAsync(conn, tx, new EmployeeProfileCreateRequest(
                EmployeeId: employeeId, PartTimeFraction: 1.000m, Position: null));
            await tx.CommitAsync();
        }

        // (a) Simulate a missed write: NULL the dated cell → read returns the live value.
        await ExecAsync(
            """
            UPDATE employee_profiles SET employment_category = NULL
            WHERE employee_id = @p0 AND effective_to IS NULL
            """, employeeId);
        var fallbackProfile = await _repo.GetByEmployeeIdAsync(employeeId);
        Assert.NotNull(fallbackProfile);
        Assert.Equal(NonDefaultCategory, fallbackProfile!.EmploymentCategory);

        // (b) Diverge the dated cell → the dated value wins over live (COALESCE order).
        // Divergence is impossible via production writes this increment (dated == live by
        // construction); the direct UPDATE is the falsification instrument only.
        await ExecAsync(
            """
            UPDATE employee_profiles SET employment_category = 'S137DatedWins'
            WHERE employee_id = @p0 AND effective_to IS NULL
            """, employeeId);
        var datedProfile = await _repo.GetByEmployeeIdAsync(employeeId);
        Assert.NotNull(datedProfile);
        Assert.Equal("S137DatedWins", datedProfile!.EmploymentCategory);
    }

    // ═════════════════════════════════════════════════════════════════════
    // GetEffectiveFromDatesAsync — the planner's boundary feed fenceposts
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Strictly-after semantics on the lower bound (a profile row taking effect ON
    /// <c>afterExclusive</c> creates no interior boundary — the segment already starts
    /// there), inclusive upper bound, ascending order, and history + live rows alike.
    /// Chain under test: rows at 2026-01-01 (closed), 2026-02-01 (closed), 2026-03-01 (live).
    /// </summary>
    [Fact]
    public async Task GetEffectiveFromDates_StrictlyAfterLowerBound_InclusiveUpperBound_Ascending()
    {
        var employeeId = await CreateUserWithoutProfileAsync("Standard");
        // Direct-SQL history chain (end-exclusive [from, to) per ADR-018 D9): exactly one
        // live row (the partial-unique live index), distinct effective_from values (the
        // history unique index).
        await ExecAsync(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to, employment_category)
            VALUES (@p0, 1.000, '2026-01-01', '2026-02-01', 'Standard'),
                   (@p0, 0.900, '2026-02-01', '2026-03-01', 'Standard'),
                   (@p0, 0.800, '2026-03-01', NULL,         'Standard')
            """, employeeId);

        // Lower bound EXCLUSIVE: the 2026-01-01 row sits exactly AT afterExclusive → out.
        // Upper bound INCLUSIVE: the 2026-03-01 row sits exactly AT toInclusive → in.
        var dates = await _repo.GetEffectiveFromDatesAsync(
            employeeId, afterExclusive: new DateOnly(2026, 1, 1), toInclusive: new DateOnly(2026, 3, 1));
        Assert.Equal(
            new[] { new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1) },
            dates);

        // A day before the first row: all three change dates fall strictly inside — and
        // come back ASCENDING (history rows and the live row alike).
        var all = await _repo.GetEffectiveFromDatesAsync(
            employeeId, afterExclusive: new DateOnly(2025, 12, 31), toInclusive: new DateOnly(2026, 12, 31));
        Assert.Equal(
            new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1) },
            all);

        // Range past the last change date: empty, not an error.
        var none = await _repo.GetEffectiveFromDatesAsync(
            employeeId, afterExclusive: new DateOnly(2026, 3, 1), toInclusive: new DateOnly(2026, 12, 31));
        Assert.Empty(none);

        // Scoping: another employee's chain never leaks in.
        var other = await CreateUserWithoutProfileAsync("Standard");
        await ExecAsync(
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, effective_from, effective_to, employment_category)
            VALUES (@p0, 1.000, '2026-02-15', NULL, 'Standard')
            """, other);
        var scoped = await _repo.GetEffectiveFromDatesAsync(
            employeeId, afterExclusive: new DateOnly(2026, 1, 1), toInclusive: new DateOnly(2026, 3, 1));
        Assert.DoesNotContain(new DateOnly(2026, 2, 15), scoped);
    }

    // ─── Helpers (the EmployeeProfileLifecycleTests conventions) ────────────

    /// <summary>
    /// Brand-new user via direct DB insert (NOT AdminEndpoints POST, which would also
    /// insert a profile row) with an EXPLICIT employment_category, so the copy-from-users
    /// assertions are falsifiable against the 'Standard' column default.
    /// </summary>
    private async Task<string> CreateUserWithoutProfileAsync(string employmentCategory)
    {
        var userId = "emp_s137_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await ExecAsync(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category, is_active)
            VALUES (@p0, @p0, 'dev-only', 'S137 Category User', NULL,
                    'STY01', 'AC', 'OK24', @p1, TRUE)
            """, userId, employmentCategory);
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
            employeeId: "ADMIN_S137_QA",
            name: "S137 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values
        // go through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql, params object[] args)
        => Convert.ToInt32(await ScalarAsync(sql, args));

    private async Task<string?> ScalarStringAsync(string sql, params object[] args)
        => (string?)await ScalarAsync(sql, args);

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values
        // go through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }
}
