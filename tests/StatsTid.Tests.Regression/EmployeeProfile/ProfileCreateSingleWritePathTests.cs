using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S143 / TASK-14308 (QUAL-177, owner ruling OQ-4) — the pins for <b>one write path, two dates</b>.
///
/// <para>
/// <b>The problem, in plain language.</b> There used to be THREE ways an <c>employee_profiles</c> row
/// got created: <c>EmployeeProfileRepository.CreateAsync</c>, which four tests described in detail,
/// and two inline INSERT statements — one in the boot backfill seeder, one in the admin create-person
/// endpoint — which were what production actually ran. <c>CreateAsync</c> had ZERO production callers.
/// A method nothing executes cannot be kept honest by its tests: it can drift from the real paths
/// indefinitely while every assertion about it keeps passing, and by S143 it had — its own comment
/// claimed the schema default <c>'0001-01-01'</c> while the code had stamped today since S33. The
/// owner ruled that <c>CreateAsync</c> becomes the one write path both real creators use.
/// </para>
///
/// <para>
/// <b>Scope note, so this class is not read as claiming more than it does.</b> "One write path" means
/// the two CREATE-A-PERSON routes, not every INSERT into <c>employee_profiles</c>.
/// <c>SupersedeAndCreateAsync</c> Case A also produces a net-new live row for a profile-less employee
/// — deliberately, because it is the DATED WRITER: it locks the whole timeline and routes through
/// <c>TemporalWriteRouter</c>, which a first-row create has no timeline to need. That route keeps its
/// own pins in <c>ProfileCategoryDatingTests</c>.
/// </para>
///
/// <para>
/// <b>Why that needed a signature change rather than a move, which is what this class pins.</b> The
/// two real creators need DIFFERENT dates, and each wrong choice reintroduces a defect the project
/// has already paid for once:
/// <list type="bullet">
///   <item><description><b>The seeder must anchor at <c>0001-01-01</c>.</b> It backfills employees who
///     already existed, so their HISTORICAL periods must resolve. The profile resolver picks the row
///     with <c>effective_from &lt;= asOfDate</c>; a today-stamped backfill row covers nothing before
///     today, so every pre-deployment calculation finds no profile and PCS/Compliance fail closed with
///     a 500. S33 Step 7a found and fixed exactly that.</description></item>
///   <item><description><b>The admin create must stamp today, exactly ONCE.</b> The endpoint computes a
///     single <c>effectiveFrom</c> above its transaction and gives it to the users row, the profile row
///     and the <c>EmployeeProfileCreated</c> event — the S137 owner ruling "ONE date for the whole
///     create", made so a midnight straddle between two separate clock reads cannot leave those three a
///     day apart. Had <c>CreateAsync</c> kept its internal clock read, routing the admin path through it
///     would have added a SECOND read inside the create whose whole point is that there is one.</description></item>
/// </list>
/// So the date is now a REQUIRED argument on <c>EmployeeProfileCreateRequest</c>, and these facts pin
/// the two answers side by side. <b>The two RED conditions</b> for the two ways this consolidation could
/// have been done wrong, both of which compile and both of which look tidy:
/// <list type="bullet">
///   <item><description>Route both callers through the UNCHANGED <c>CreateAsync</c> (which stamps today):
///     fact 1's seeder assertion reads the booted host's day instead of <c>0001-01-01</c>.</description></item>
///   <item><description>Keep the date internal and make it the <c>0001-01-01</c> anchor for everyone:
///     fact 1's admin assertions read <c>0001-01-01</c> instead of <c>2026-07-16</c>.</description></item>
/// </list>
/// </para>
///
/// <para>
/// Every expected date here is a LITERAL. None is obtained by calling the production date helper, which
/// would be the code under test computing its own expected answer and would pass against any
/// implementation, right or wrong.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileCreateSingleWritePathTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey =
        "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>An init.sql-seeded ORGANISATION — employees live on Organisations, never on MAOs
    /// (S95 / ADR-035 slice 4), and the create-person endpoint rejects anything else with a 400.</summary>
    private const string SeededOrg = "STY01";

    /// <summary>
    /// The backfill anchor, as a literal. This is what a row means when it says "this profile was
    /// already in force and we do not know since when" — the only truthful stamp for a backfill, and
    /// the one that keeps historical reads resolvable.
    /// </summary>
    private static readonly DateOnly BackfillAnchor = new(1, 1, 1);

    /// <summary>
    /// The Copenhagen calendar day at <see cref="BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen"/>
    /// (2026-07-15 22:30 UTC, which is 00:30 on the 16th in Copenhagen under CEST). A literal, and
    /// deliberately a day on which the UTC calendar and the Danish calendar DISAGREE, so the admin
    /// assertions below discriminate the calendar as well as the value.
    /// </summary>
    private static readonly DateOnly AdminCreateDanishDay = new(2026, 7, 16);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here: each fact builds its own instant-pinned host, whose first
        // CreateClient() runs Program.cs's startup seeders — including the profile backfill this
        // class is half about.
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static int Seq;
    private static string NextId(string prefix) => $"{prefix}_{Interlocked.Increment(ref Seq)}";

    // ═════════════════════════════════════════════════════════════════════
    // Fact 1 — the two dates, through the one write path
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// On ONE booted host, at ONE pinned instant: every profile row the boot seeder backfilled is
    /// anchored at <c>0001-01-01</c>, and the profile row an admin create produces seconds later is
    /// stamped with the Danish day — the same single date its users row and its event carry.
    ///
    /// <para><b>Why one fact and not two.</b> The distinction IS the subject. Split across two facts,
    /// a change that collapsed the two dates into one could be "fixed" by editing whichever fact went
    /// red, and the contrast would quietly disappear. Asserted together, on the same host and the same
    /// clock, the only way to satisfy both is to keep the two callers passing different dates.</para>
    ///
    /// <para><b>The admin half also asserts that its three dated cells AGREE</b>
    /// (<c>users.employment_start_date</c> == the profile row's <c>effective_from</c> == the
    /// <c>EmployeeProfileCreated</c> event's <c>effectiveFrom</c>). Note precisely what that does and
    /// does not prove: under a FIXED clock it proves the three cells carry the same, correct day, but
    /// it CANNOT prove the handler read the clock only once — a second read of a pinned clock returns
    /// the same value. The "exactly once" half of the S137 ruling is pinned separately, by
    /// <see cref="AdminUserCreate_ReadsTheClockOnce_AllDatedCellsAgree"/>, which uses a clock that
    /// answers with a different day on every read. This fact's name claims only what it shows.</para>
    ///
    /// <para><b>The seeder's anchor is asserted in SQL, with <c>isfinite</c>, not by reading a
    /// <c>DateOnly</c> back.</b> Npgsql maps <c>DateOnly.MinValue</c> to Postgres <c>-infinity</c> on
    /// the way in AND converts <c>-infinity</c> back to <c>DateOnly.MinValue</c> on the way out, so a
    /// round-trip assertion passes over changed data — it cannot tell the finite anchor from the
    /// sentinel. An earlier draft of this fact did exactly that and did not notice the column had
    /// stopped holding <c>'0001-01-01'</c>. Asserting in SQL removes the client-side conversion from
    /// the loop entirely.</para>
    /// </summary>
    [Fact]
    public async Task OneWritePath_SeederAnchorsAtYearOne_AdminCreateStampsTheDanishDay()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        using var client = Client(host);

        // ── The seeder half. Its rows are identified by the actor on their CREATED audit row
        //    ('SYSTEM_SEED'), which is the seeder's own signature and survives the S143 rewrite.
        var seededRows = await CountAsync(
            """
            SELECT COUNT(*) FROM employee_profiles p
            JOIN employee_profile_audit a
              ON a.profile_id = p.profile_id AND a.action = 'CREATED' AND a.actor_id = 'SYSTEM_SEED'
            """);
        Assert.True(seededRows > 0,
            "The booted host must have backfilled at least one profile row, or the anchor assertion " +
            "below is vacuous (init.sql seeds users and NO employee_profiles rows).");

        // EVERY backfilled row is the FINITE date 0001-01-01 — not -infinity, and not "most of them".
        //
        // WHAT CARRIES THE ASSERTION: the equality, evaluated IN SQL. `-infinity = DATE '0001-01-01'`
        // is false in Postgres, so the equality alone already rejects the sentinel. What it needed
        // rescuing from was the C# side: Npgsql converts `-infinity` BACK to DateOnly.MinValue on
        // read, so an `Assert.Equal(new DateOnly(1,1,1), reader.GetFieldValue<DateOnly>(...))` passes
        // over a column holding the sentinel. An earlier draft of this fact did exactly that and saw
        // nothing. Moving the comparison into SQL takes that conversion out of the loop.
        //
        // `isfinite(...)` is therefore REDUNDANT — it cannot fail where the equality passes, because
        // equality to a finite literal implies finiteness. It is kept deliberately, as a legend that
        // names the hazard at the assertion site rather than as a second check, and so that loosening
        // the equality later (to a range, say) cannot silently drop sentinel rejection with it. It is
        // not a second guarantee and this comment does not claim it is.
        //
        // Counting the rows that FAIL means a defect shows up as a non-zero count rather than as a
        // population that quietly shrank to match. Its LIMIT, stated rather than left to be
        // discovered: rows the JOIN excludes are invisible here either way — a seeder-written profile
        // whose audit row went missing is simply not in this population. That case is what
        // SeederBackfill_EveryActiveUserHasItsProfile_ItsAuditRowAndItsEvent exists to catch, which is
        // why that fact derives its population from `users` instead.
        Assert.Equal(0L, await CountAsync(
            """
            SELECT COUNT(*) FROM employee_profiles p
            JOIN employee_profile_audit a
              ON a.profile_id = p.profile_id AND a.action = 'CREATED' AND a.actor_id = 'SYSTEM_SEED'
            WHERE NOT (p.effective_from = DATE '0001-01-01' AND isfinite(p.effective_from))
            """));

        // Row/event parity for the backfill (ADR-018 D3): the event says the same anchor the row
        // carries. Both now come from ONE constant in the seeder; before S143 they were two
        // independently-written literals that happened to match. This is the assertion the sentinel
        // defect broke — the row would have held -infinity while the event still said 0001-01-01,
        // because System.Text.Json has no infinity special case.
        Assert.Equal(0L, await CountAsync(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE event_type = 'EmployeeProfileCreated'
              AND event_payload ->> 'actorId' = 'SYSTEM_SEED'
              AND event_payload ->> 'effectiveFrom' <> '0001-01-01'
            """));

        // ── The admin half, on the same host and the same clock.
        var employeeId = NextId("s143_wp");
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = employeeId,
            username = employeeId,
            password = "TestPassword123!",
            displayName = "S143 Single Write Path",
            email = (string?)null,
            primaryOrgId = SeededOrg,
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var joinCmd = new NpgsqlCommand(
            """
            SELECT u.employment_start_date, p.effective_from
            FROM users u
            JOIN employee_profiles p ON p.employee_id = u.user_id AND p.effective_to IS NULL
            WHERE u.user_id = @userId
            """, conn))
        {
            joinCmd.Parameters.AddWithValue("userId", employeeId);
            await using var reader = await joinCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                $"Expected a users row joined to exactly one live employee_profiles row for '{employeeId}'.");
            // The admin path stamps TODAY — the Danish day — NOT the seeder's anchor. Against a
            // consolidation that made the anchor universal this reads 0001-01-01.
            Assert.Equal(AdminCreateDanishDay, reader.GetFieldValue<DateOnly>(1));
            // ...and the users row stored the SAME value. Under this fixed clock that shows the two
            // cells AGREE on the right day; it does NOT show the handler read the clock once (a
            // pinned clock answers identically however many times it is asked). See the
            // AdminUserCreate_ReadsTheClockOnce_AllDatedCellsAgree fact below for that half.
            Assert.Equal(AdminCreateDanishDay, reader.GetFieldValue<DateOnly>(0));
            Assert.False(await reader.ReadAsync(),
                $"Expected exactly one live profile row for '{employeeId}', found more than one.");
        }

        // The third consumer of that one date: the event that commits with the row.
        await using (var eventCmd = new NpgsqlCommand(
            """
            SELECT event_payload ->> 'effectiveFrom'
            FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'EmployeeProfileCreated'
            """, conn))
        {
            eventCmd.Parameters.AddWithValue("streamId", $"employee-profile-{employeeId}");
            Assert.Equal("2026-07-16", await eventCmd.ExecuteScalarAsync());
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Fact 2 — the seeder's atomic unit survived the rewrite
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every row the backfill seeder writes still comes with its CREATED audit row AND its
    /// <c>EmployeeProfileCreated</c> outbox event — one of each, per row.
    ///
    /// <para><b>Why this is worth its own fact.</b> The S143 change replaced the seeder's own INSERT
    /// statement with a call into a repository method. That is exactly the kind of edit that silently
    /// loses a companion write: the row keeps appearing, so the obvious assertions stay green, while
    /// the audit row or the event quietly stops being emitted. Auditability is an inviolable invariant
    /// here — a state-changing write emits its event and its audit row in the SAME transaction
    /// (ADR-018 D5, ADR-026) — so the counts are pinned as a triple rather than assumed.</para>
    ///
    /// <para><b>The set of expected profiles is derived from <c>users</c>, never from the audit
    /// table.</b> An earlier draft counted profiles by JOINing them to their <c>SYSTEM_SEED</c> audit
    /// rows — which is circular: a profile that lost BOTH its audit row and its event simply drops out
    /// of the population being counted, so the totals still balance and the fact passes while the
    /// audit chain has a hole in it. The population here is "every active user", which is the seeder's
    /// own selection predicate and is independent of anything the write path emits; each member is
    /// then checked for exactly one live profile row, exactly one CREATED audit row, and exactly one
    /// <c>EmployeeProfileCreated</c> event.</para>
    ///
    /// <para>The companion checks are written as "count the rows that FAIL", so a missing companion
    /// shows up as a non-zero failure count rather than as a population that quietly shrank.</para>
    /// </summary>
    [Fact]
    public async Task SeederBackfill_EveryActiveUserHasItsProfile_ItsAuditRowAndItsEvent()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        _ = host.CreateClient();

        // The independent expectation: the S31 invariant is that every ACTIVE user has exactly one
        // live profile row. `users` is populated by init.sql before any of this sprint's code runs,
        // so this count cannot be moved by a defect in the write path under test.
        var activeUsers = await CountAsync("SELECT COUNT(*) FROM users WHERE is_active = TRUE");
        Assert.True(activeUsers > 0,
            "init.sql must have seeded active users, or every assertion below is vacuous.");

        Assert.Equal(0L, await CountAsync(
            """
            SELECT COUNT(*) FROM users u
            WHERE u.is_active = TRUE
              AND (SELECT COUNT(*) FROM employee_profiles p
                   WHERE p.employee_id = u.user_id AND p.effective_to IS NULL) <> 1
            """));

        // One CREATED audit row per live profile row, and one event on that employee's stream.
        // Driven off employee_profiles — so a profile with NO companions is still in the population
        // and still fails, which is the case the previous shape could not see.
        Assert.Equal(0L, await CountAsync(
            """
            SELECT COUNT(*) FROM employee_profiles p
            WHERE p.effective_to IS NULL
              AND (SELECT COUNT(*) FROM employee_profile_audit a
                   WHERE a.profile_id = p.profile_id AND a.action = 'CREATED') <> 1
            """));

        Assert.Equal(0L, await CountAsync(
            """
            SELECT COUNT(*) FROM employee_profiles p
            WHERE p.effective_to IS NULL
              AND (SELECT COUNT(*) FROM outbox_events e
                   WHERE e.stream_id = 'employee-profile-' || p.employee_id
                     AND e.event_type = 'EmployeeProfileCreated') <> 1
            """));

        // Non-vacuity floor for the SEEDER specifically: the assertions above hold for any create
        // path, so pin that the backfill actually ran and left its own signature.
        Assert.True(
            await CountAsync(
                "SELECT COUNT(*) FROM employee_profile_audit WHERE action = 'CREATED' AND actor_id = 'SYSTEM_SEED'") > 0,
            "The booted host must have backfilled at least one profile row for this fact to be about the seeder.");

        // The audit row records the version the write actually produced. A net-new profile row is
        // version 1 and has no predecessor, so version_before is NULL — the ADR-019 D8
        // every-CREATE-gets-a-CREATED-audit-row shape, unchanged by the rewrite.
        Assert.Equal(0L, await CountAsync(
            """
            SELECT COUNT(*) FROM employee_profile_audit
            WHERE action = 'CREATED' AND actor_id = 'SYSTEM_SEED'
              AND (version_after <> 1 OR version_before IS NOT NULL)
            """));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Fact 3 — ONE clock read for the whole create (a clock that can tell)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Under a clock that answers with a DIFFERENT DAY on every single read, the create's dated cells
    /// still all carry the same date — which is only possible if the handler asked the clock once.
    ///
    /// <para><b>Why a fixed clock cannot pin this, and why that matters here.</b> The S137 owner ruling
    /// is "ONE date for the whole create": the handler reads the Copenhagen day once, above the
    /// transaction, and feeds that single value to the users row's hire date, the profile row's
    /// <c>effective_from</c>, the agreement-code row and the <c>EmployeeProfileCreated</c> event. The
    /// ruling exists because two separate clock reads either side of midnight would date one employee
    /// record on two different days. A PINNED clock cannot detect a second read — it returns the same
    /// instant however often it is asked — so every fixed-instant assertion in this file is blind to
    /// exactly the defect the ruling was written to prevent. That defect was live in this task: before
    /// S143, <c>CreateAsync</c> read the Copenhagen clock itself, so routing the admin path through it
    /// unchanged would have put a SECOND read inside the one create that must have one.</para>
    ///
    /// <para><b>The clock.</b> <see cref="DayPerReadTimeProvider"/> returns its start instant on the
    /// first call and one day later on each subsequent call, so ANY two distinct reads land on
    /// different Copenhagen days no matter which reads they are. That last property is what makes the
    /// fact robust: other components (the startup seeders, the agreement-code repository) share this
    /// provider and consume reads too, so the ABSOLUTE day the handler sees is not predictable — which
    /// is why this fact asserts only that the four dated cells AGREE, never which day they name.
    /// One read ⇒ they agree by construction. Two reads ⇒ they cannot.</para>
    ///
    /// <para><b>RED condition</b>: revert <c>CreateAsync</c> to reading the clock itself and the profile
    /// row's <c>effective_from</c> lands a day after the users row's hire date.</para>
    /// </summary>
    [Fact]
    public async Task AdminUserCreate_ReadsTheClockOnce_AllDatedCellsAgree()
    {
        using var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(
                    new DayPerReadTimeProvider(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen))));
        using var client = Client(host);

        var employeeId = NextId("s143_once");
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = employeeId,
            username = employeeId,
            password = "TestPassword123!",
            displayName = "S143 One Clock Read",
            email = (string?)null,
            primaryOrgId = SeededOrg,
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT u.employment_start_date,
                   p.effective_from,
                   c.effective_from,
                   (SELECT e.event_payload ->> 'effectiveFrom'
                    FROM outbox_events e
                    WHERE e.stream_id = 'employee-profile-' || u.user_id
                      AND e.event_type = 'EmployeeProfileCreated')
            FROM users u
            JOIN employee_profiles p ON p.employee_id = u.user_id AND p.effective_to IS NULL
            JOIN user_agreement_codes c ON c.user_id = u.user_id AND c.effective_to IS NULL
            WHERE u.user_id = @userId
            """, conn);
        cmd.Parameters.AddWithValue("userId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(),
            $"Expected one users row joined to one live profile row and one live agreement row for '{employeeId}'.");

        // The profile row's date is the reference: every other dated cell of this create must equal it.
        // No literal day is asserted — see the doc for why the absolute day is not predictable here.
        var profileDate = reader.GetFieldValue<DateOnly>(1);
        Assert.Equal(profileDate, reader.GetFieldValue<DateOnly>(0));
        Assert.Equal(profileDate, reader.GetFieldValue<DateOnly>(2));
        Assert.False(reader.IsDBNull(3),
            $"Expected one EmployeeProfileCreated event on stream 'employee-profile-{employeeId}'.");
        Assert.Equal(profileDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetString(3));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static HttpClient Client(WebApplicationFactory<Program> host)
    {
        var c = host.CreateClient();
        c.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GlobalAdminToken());
        return c;
    }

    private static string GlobalAdminToken() => new JwtTokenService(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    }).GenerateToken(
        employeeId: "admin_s143_writepath",
        name: "S143 Write-Path Admin",
        role: StatsTidRoles.GlobalAdmin,
        agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    /// <summary>
    /// A clock that moves a WHOLE DAY on every read: call <c>n</c> answers <c>start + (n-1) days</c>.
    ///
    /// <para>Deliberately not a "realistic" clock. Its one job is to make a second read DETECTABLE:
    /// with a one-day step, any two distinct reads name two distinct Copenhagen days regardless of
    /// which reads they happen to be, so a fact can assert "these dated cells agree" and have that
    /// assertion mean "the clock was read once". A smaller step (say an hour) would not do: two reads
    /// could easily land on the same day and the defect would slip through. A counter alone would not
    /// do either, because the host's startup seeders share this provider and consume reads, so a raw
    /// call count is not attributable to the request under test.</para>
    ///
    /// <para>Deliberately local to this file rather than promoted into <c>Hosting/</c>: it is a
    /// single-purpose falsification instrument, not a seam other facts should reach for by default.
    /// Kept beside the one fact that uses it until a second caller earns the promotion.</para>
    /// </summary>
    private sealed class DayPerReadTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _start;
        private int _reads;

        public DayPerReadTimeProvider(DateTimeOffset start) => _start = start;

        public override DateTimeOffset GetUtcNow()
            => _start.AddDays(Interlocked.Increment(ref _reads) - 1);
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // compile-time test constant; no interpolation, no user input
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
