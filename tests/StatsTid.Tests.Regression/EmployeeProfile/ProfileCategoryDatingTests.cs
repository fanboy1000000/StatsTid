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
///   <item><b>Dated-cell read posture</b> (FLIPPED by S138 / TASK-13804 — see the fact's own
///     doc): the hydrated read returns the DATED cell, full stop. The S137 leg that NULLed the
///     dated cell and expected a fall-back to the live <c>users</c> value could not survive the
///     NOT-NULL tightening — the state it described is no longer representable — so it is
///     flipped to assert the DB now REFUSES that write (23502). The "diverged dated cell wins"
///     leg is kept and strengthened: divergence is the normal S138 shape, not a lab trick.</item>
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
        // Zero NULL dated categories anywhere (live + history alike). Since S138 / TASK-13804
        // the column is NOT NULL, so this is belt-and-braces over a DB constraint — kept
        // deliberately: it is the pin that says the write paths POPULATE the value, and it
        // would fail LOUDLY here rather than only at some future INSERT if one ever stopped.
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
    // Dated-cell read posture — the dated cell is the authority (S138)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>FLIPPED by S138 / TASK-13804</b> (was
    /// <c>Read_NullDatedCategory_FallsBackToLive_DivergedDatedCategory_Wins</c>).
    ///
    /// <para>
    /// Leg (a) — WAS: "NULL the dated cell → the read degrades to the live <c>users</c> value"
    /// (S137's ruled fail-safe, expressed as <c>COALESCE(dated, live)</c>). It is now the exact
    /// opposite assertion: the column is NOT NULL, so the UPDATE that manufactured a missed
    /// write is REFUSED by the database (23502). The leg could not merely be deleted — it was
    /// the pin for a posture, and the posture reversed, so it must pin the reversal. WHY the
    /// reversal: with the category editable per date, a dated value may legitimately differ
    /// from <c>users.employment_category</c> (which is only the cache of the row covering
    /// TODAY), so falling back to live would MISLABEL a historical read instead of rescuing it.
    /// </para>
    ///
    /// <para>
    /// Leg (b) — UNCHANGED in expectation, stronger in meaning: a dated cell that differs from
    /// live WINS. Under S137 divergence was impossible through production writes (the direct
    /// UPDATE was a falsification instrument only); under S138 it is the ordinary shape a
    /// backdated category change leaves behind, so this leg now pins real behaviour.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Read_DatedCategoryCell_IsAuthoritative_NullDatedCellRejectedByNotNull()
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

        // (a) S138 flip: manufacturing a "missed write" is no longer possible — the NOT NULL
        // column refuses it (23502 not_null_violation). The read cannot see a NULL dated cell,
        // so it needs no fail-safe; the failure surfaces at the write, where the bug is.
        var nullEx = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            """
            UPDATE employee_profiles SET employment_category = NULL
            WHERE employee_id = @p0 AND effective_to IS NULL
            """, employeeId));
        Assert.Equal("23502", nullEx.SqlState);

        // ...and the row still reads its own dated value, untouched by the refused UPDATE.
        var intactProfile = await _repo.GetByEmployeeIdAsync(employeeId);
        Assert.NotNull(intactProfile);
        Assert.Equal(NonDefaultCategory, intactProfile!.EmploymentCategory);

        // (b) A dated cell that differs from the live users value WINS — the dated cell is the
        // authority. Under S138 this is the ordinary shape of a backdated category change (the
        // live column follows only the row covering TODAY); the direct UPDATE just puts the row
        // in that shape without going through the writer.
        await ExecAsync(
            """
            UPDATE employee_profiles SET employment_category = 'S138DatedWins'
            WHERE employee_id = @p0 AND effective_to IS NULL
            """, employeeId);
        var datedProfile = await _repo.GetByEmployeeIdAsync(employeeId);
        Assert.NotNull(datedProfile);
        Assert.Equal("S138DatedWins", datedProfile!.EmploymentCategory);
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
