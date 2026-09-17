using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.UserAgreementCode;

/// <summary>
/// S34 / TASK-3414 — AdminEndpoints HTTP-level D-tests for TASK-3407
/// (PUT + POST <c>/api/admin/users</c>) versioned-history routing:
///
/// <list type="bullet">
///   <item><b>POST Case A</b> — new user → 6-way atomic INSERT including
///     <c>user_agreement_codes</c> Case A row + <c>UserAgreementCodeSeeded</c>
///     outbox event; login JWT works and
///     <see cref="EmploymentProfileResolver.GetByEmployeeIdAtAsync"/>(today)
///     returns the seeded code (Codex BLOCKER 2 absorption).</item>
///   <item><b>PUT validator backdated / future-dated</b> — both reject with 422
///     when <c>agreementCode</c> mutates (ADR-023 D8 same-day-only narrowing,
///     mirrors S33 employee-profile PUT validator).</item>
///   <item><b>PUT dual-emission Case C</b> — cross-day agreement_code change
///     emits BOTH <c>UserAgreementCodeChanged</c> AND
///     <c>UserAgreementCodeSuperseded</c> on the <c>user-{userId}</c> stream,
///     audit row carries <c>action='SUPERSEDED'</c> with populated
///     <c>version_before</c>/<c>version_after</c> columns (refinement cycle 1
///     Reviewer WARNING 4 + S25 publish-supersession precedent).</item>
/// </list>
///
/// <para>
/// HTTP-level via <see cref="StatsTidWebApplicationFactory"/> + <c>CreateClient()</c>
/// per S27 precedent. JWT signing uses the dev-fallback key per
/// <see cref="StatsTid.Tests.Regression.EmployeeProfile.EmployeeProfileLifecycleTests"/>
/// shape.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminEndpointsAgreementCodeTests : IAsyncLifetime
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
        // UserAgreementCodeBackfillSeeder backfilling at effective_from='0001-01-01').
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // POST /api/admin/users — Case A INSERT + Seeded event (Codex BLOCKER 2)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Net-new admin-created user gets BOTH a <c>users</c> row AND a
    /// <c>user_agreement_codes</c> Case A INSERT row atomically; the POST emits
    /// <c>UserAgreementCodeSeeded</c> on the <c>user-{userId}</c> stream
    /// (matching the backfill seeder semantic — no predecessor). The seeded row's
    /// <c>effective_from</c> is today (admin-POST today-stamp convention per
    /// AdminEndpoints.cs:432) — strictly NOT '0001-01-01' which is the seeder
    /// bootstrap-only convention. Subsequent
    /// <see cref="EmploymentProfileResolver.GetByEmployeeIdAtAsync"/>(today)
    /// succeeds — proving the new row is reachable through the dated lookup
    /// path consumed by PCS / Compliance / etc.
    ///
    /// <para>
    /// <b>S142 / TASK-14202 — the host is now pinned and "today" is a literal.</b> This test used to
    /// compute its own <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> and compare the server's stamp
    /// against it. That agreed with the product only because BOTH derived the UTC calendar day; once
    /// the product moved to the Europe/Copenhagen day (all users are Danish), the two would have
    /// disagreed for the hour or two each night when the calendars differ — a test that fails on the
    /// clock rather than on the code. The host is pinned to
    /// <see cref="BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen"/> (2026-07-15 22:30 UTC
    /// = 00:30 on 16 July in Copenhagen) and the expected date is the literal 2026-07-16, so the
    /// assertion is both deterministic and RED against the pre-S142 handler.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminPostUser_NewUserGetsBothUsersRowAndUserAgreementCodesCaseAInsert_EmitsSeededEvent()
    {
        // S142 / TASK-14202 — pinned to an exact INSTANT. WithFixedToday(DateOnly) pins UTC
        // midnight, the one moment of every day at which the UTC and Copenhagen calendars agree, so
        // it cannot express this fact.
        var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        var client = AuthorizedClient(host);
        var newUserId = "emp_s34_post_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S34 Post Case A Test User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) users row landed.
        await using (var usersCmd = new NpgsqlCommand(
            "SELECT agreement_code FROM users WHERE user_id = @userId", conn))
        {
            usersCmd.Parameters.AddWithValue("userId", newUserId);
            var cacheCode = (string?)await usersCmd.ExecuteScalarAsync();
            Assert.Equal("AC", cacheCode);
        }

        // (2) user_agreement_codes Case A row landed — version=1, today-stamp.
        // The Danish calendar day at the pinned instant, as a LITERAL. Never
        // CopenhagenBusinessDate.Today(...) — that would let the helper under test supply its own
        // expected answer, which passes against any implementation, right or wrong.
        var danishToday = new DateOnly(2026, 7, 16);
        const string danishTodayIso = "2026-07-16";
        await using (var uacCmd = new NpgsqlCommand(
            """
            SELECT agreement_code, effective_from, effective_to, version
            FROM user_agreement_codes
            WHERE user_id = @userId
            """, conn))
        {
            uacCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await uacCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "POST must create a user_agreement_codes row.");
            Assert.Equal("AC", reader.GetString(0));
            Assert.Equal(danishToday, reader.GetFieldValue<DateOnly>(1));
            Assert.True(reader.IsDBNull(2), "Case A insert must leave effective_to NULL.");
            Assert.Equal(1L, reader.GetInt64(3));
            Assert.False(await reader.ReadAsync(), "Exactly one user_agreement_codes row expected.");
        }

        // (3) user_agreement_codes_audit CREATED row landed.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM user_agreement_codes_audit
            WHERE user_id = @userId
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", newUserId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "POST must emit a CREATED audit row.");
            Assert.Equal("CREATED", reader.GetString(0));
            Assert.True(reader.IsDBNull(1), "CREATED audit must have NULL version_before.");
            Assert.Equal(1L, reader.GetInt64(2));
        }

        // (4) UserAgreementCodeSeeded outbox event landed on user-{userId}.
        await using (var outboxCmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'UserAgreementCodeSeeded'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn))
        {
            outboxCmd.Parameters.AddWithValue("streamId", $"user-{newUserId}");
            var rawPayload = (string?)await outboxCmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrEmpty(rawPayload),
                "POST must emit a UserAgreementCodeSeeded event matching the backfill semantic.");
            using var payloadDoc = JsonDocument.Parse(rawPayload!);
            Assert.Equal(newUserId, payloadDoc.RootElement.GetProperty("userId").GetString());
            Assert.Equal("AC", payloadDoc.RootElement.GetProperty("agreementCode").GetString());
            Assert.Equal(danishTodayIso,
                payloadDoc.RootElement.GetProperty("effectiveFrom").GetString());
            Assert.Equal(1L, payloadDoc.RootElement.GetProperty("rowVersion").GetInt64());
        }

        // (5) EmploymentProfileResolver.GetByEmployeeIdAtAsync(today) returns the
        // seeded code — proving the row is reachable through the dated lookup path
        // (PCS / Compliance / Balance / Skema / Overtime consumption sites).
        var repo = new UserAgreementCodeRepository(_harness.Factory);
        var resolver = new EmploymentProfileResolver(_harness.Factory, repo);
        // employee_profiles row was also INSERTed atomically by the POST (4-way → 6-way
        // atomicity per TASK-3407), so the resolver's JOIN should succeed.
        var resolved = await resolver.GetByEmployeeIdAtAsync(newUserId, danishToday);
        Assert.NotNull(resolved);
        Assert.Equal("AC", resolved!.AgreementCode);
    }


    // ═════════════════════════════════════════════════════════════════════
    // S142 / TASK-14202 — the endpoint and the repository must share ONE calendar
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The fact this sprint exists for, on the agreement-code aggregate.</b> All StatsTid users
    /// are Danish. Copenhagen runs one hour ahead of UTC in winter (CET) and two in summer (CEST),
    /// so between Danish midnight and UTC midnight the UTC calendar day is still YESTERDAY in
    /// Denmark. Until S142 the admin create derived its dates from the UTC day, so an HR admin
    /// registering a new hire at 00:30 local dated that person's agreement interval — and their
    /// whole record — one day early, every single night.
    ///
    /// <para>
    /// <b>Why this test asserts TWO things, and why the endpoint and the repository ship in one
    /// commit.</b> Two different pieces of code answer "what day is it?" on this path:
    /// <c>AdminEndpoints.cs</c> STAMPS <c>user_agreement_codes.effective_from</c> (census row 1),
    /// and <c>UserAgreementCodeRepository.Today()</c> ASKS which row covers today (census row 52) —
    /// the question behind <c>users.agreement_code</c> and behind <c>GetCurrentAsync</c>, which is
    /// what the LOGIN TOKEN's agreement code is minted from. The two assertions below nail the
    /// stamp and the question to the SAME Danish day:
    /// </para>
    /// <list type="number">
    ///   <item><description>the stored <c>effective_from</c> is the Danish day (a literal) — this
    ///   is what fails against the pre-S142 handler, which writes the UTC day;</description></item>
    ///   <item><description>the row does <b>not</b> cover the UTC day, and <b>does</b> answer the
    ///   repository's own "today" — so a future change that moved only one of the two is caught
    ///   here rather than in production.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>What a split would actually look like</b> (recorded because it is quieter than it sounds):
    /// the cache refresh writes <c>COALESCE(todaysCode, agreement_code)</c>, so
    /// <c>users.agreement_code</c> would KEEP the value the users INSERT wrote rather than going
    /// empty, and <c>AuthEndpoints</c> falls back to that cache when the canonical read returns null
    /// — logging a warning about an "inconsistent state" and minting a working token anyway. The
    /// damage is therefore not a visible failure but a silent disagreement that heals itself at UTC
    /// midnight. A defect that hides is worse than one that fails, which is why it gets a pin.
    /// </para>
    ///
    /// <para>
    /// Docker-gated, and Docker is unavailable on the authoring machine (standing project
    /// constraint) — CI-verified, not claimed green here.
    /// </para>
    /// </summary>
    [Fact]
    public Task AdminPostUser_SummerEveningAfterDanishMidnight_StampsTheDanishDay_AndTheRepositoryAgrees()
        // 2026-07-15 22:30 UTC = 2026-07-16 00:30 in Copenhagen (CEST, UTC+2).
        // Expected values are LITERALS. Deriving them from CopenhagenBusinessDate would be the
        // helper under test grading its own answer, and would pass against a wrong implementation.
        => AssertCreateStampsTheDanishDayAsync(
            BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen,
            expectedDanishDay: new DateOnly(2026, 7, 16),
            utcDayAtThatInstant: new DateOnly(2026, 7, 15),
            idSuffix: "summer");

    /// <summary>
    /// The winter twin of the test above, at 2026-01-15 23:30 UTC = 2026-01-16 00:30 in Copenhagen
    /// (CET, UTC+1). Both seasons are pinned deliberately: a "fix" that hardcoded a +01:00 offset
    /// instead of converting through the real Europe/Copenhagen zone would pass the winter case and
    /// fail the summer one (22:30 + 1h is still the 15th), which is the QUAL-005 defect class the
    /// shared helper was written to prevent. Docker-gated; CI-verified.
    /// </summary>
    [Fact]
    public Task AdminPostUser_WinterEveningAfterDanishMidnight_StampsTheDanishDay_AndTheRepositoryAgrees()
        => AssertCreateStampsTheDanishDayAsync(
            BoundaryInstants.WinterEveningAlreadyTomorrowInCopenhagen,
            expectedDanishDay: new DateOnly(2026, 1, 16),
            utcDayAtThatInstant: new DateOnly(2026, 1, 15),
            idSuffix: "winter");

    /// <summary>
    /// The shared body of the two facts above. Parameterised as a private helper rather than an
    /// xUnit <c>[Theory]</c> because <c>DateTimeOffset</c> / <c>DateOnly</c> are not xUnit-
    /// serializable theory arguments; two thin <c>[Fact]</c>s keep the literals visible at the call
    /// site and add no analyzer noise.
    /// </summary>
    private async Task AssertCreateStampsTheDanishDayAsync(
        DateTimeOffset pinnedInstant,
        DateOnly expectedDanishDay,
        DateOnly utcDayAtThatInstant,
        string idSuffix)
    {
        var host = _factory.WithFixedInstant(pinnedInstant);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        var newUserId = $"emp_s142_{idSuffix}_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S142 Copenhagen-day create",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) THE STAMP. The agreement interval starts on the Danish day the admin is living in.
        //     RED against the pre-S142 handler, which writes utcDayAtThatInstant instead.
        await using (var uacCmd = new NpgsqlCommand(
            "SELECT effective_from FROM user_agreement_codes WHERE user_id = @userId", conn))
        {
            uacCmd.Parameters.AddWithValue("userId", newUserId);
            var stored = (DateOnly?)await uacCmd.ExecuteScalarAsync();
            Assert.NotNull(stored);
            Assert.Equal(expectedDanishDay, stored!.Value);
        }

        // (2) THE QUESTION. A repository on the SAME pinned clock must find that row when it asks
        //     for "today" — and must NOT find it on the UTC day, which is the day the two calendars
        //     disagree about. Together these two say: the writer's calendar IS the reader's
        //     calendar. If a later change moved one side only, exactly one of them breaks.
        var repo = new UserAgreementCodeRepository(_harness.Factory, new FixedTimeProvider(pinnedInstant));
        Assert.Equal("AC", await repo.GetCurrentAsync(newUserId));
        Assert.Null(await repo.GetByUserIdAtAsync(newUserId, utcDayAtThatInstant));
    }

    // ═════════════════════════════════════════════════════════════════════
    // PUT validator — backdated + future-dated EffectiveFrom (ADR-023 D8)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>FLIPPED by S138 / TASK-13802 (ADR-040 D8 as amended 2026-09-02) — RED-on-old.</b>
    /// <b>OLD expectation (S33/ADR-023 D8 same-day-only narrowing):</b> PUT with
    /// <c>EffectiveFrom = yesterday</c> AND a mutating <c>agreementCode</c> returned
    /// <b>422</b> with a body naming <c>provided</c> + <c>expected</c>.
    /// <b>NEW:</b> a BACKDATED agreement code is a legal, audited correction — "this
    /// employee was actually on HK from yesterday, not today". The writer splits the
    /// dated timeline at the requested date instead of refusing it, so the request
    /// returns <b>200</b> and history tells the truth on both sides of the split.
    /// FUTURE-dating stays 422 (the sibling fact below) — that half of D8 is deferred
    /// to Increment 4 with the "current ≠ live" read model.
    ///
    /// <para>
    /// What the split looks like here: <c>emp001</c> is seeded with one OPEN
    /// <c>user_agreement_codes</c> row at <c>'AC'</c> covering all of time
    /// (<c>effective_from = '0001-01-01'</c>, the TASK-3403 backfill anchor). Backdating
    /// to yesterday is router case C′ on an OPEN covering row: the predecessor is closed
    /// at yesterday and a new open row <c>[yesterday, ∞)</c> carries <c>'HK'</c>. Because
    /// that new row also covers TODAY, the denormalised <c>users.agreement_code</c> cache
    /// follows it (the repository refreshes the cache from the row covering today — never
    /// from the request), which is what the dated resolver reads back below.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PUT_BackdatedEffectiveFrom_SplitsTheDatedTimeline()
    {
        var client = AuthorizedClient();
        // S142 / TASK-14202 — DELIBERATELY NOT pinned, unlike the facts above and below. Every date
        // here is client-supplied and every assertion resolves at a client-supplied date, so nothing
        // is compared against the server's own "today"; the router picks its case from the
        // PREDECESSOR row's effective_from ('0001-01-01'), never from a clock. A shift of the
        // server's calendar by one day therefore cannot change any outcome asserted below — it only
        // makes "yesterday" a two-day backdate, which is still a backdate. Left alone on purpose so
        // the next reader does not "fix" it into a pin it does not need.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        var twoDaysAgo = today.AddDays(-2);

        // S35 / TASK-3506 — admin-strict If-Match required on PUT. Capture the ETag via
        // GET first (without If-Match the endpoint returns 428 before any validator runs).
        var getRsp = await client.GetAsync("/api/admin/users/emp001");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        // emp001 seeded at agreement_code='AC' — correct it to 'HK' as of YESTERDAY.
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/users/emp001")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = yesterday.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(req);

        // OLD: UnprocessableEntity. NEW: the correction is recorded.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var repo = new UserAgreementCodeRepository(_harness.Factory);
        var resolver = new EmploymentProfileResolver(_harness.Factory, repo);

        // Both sides of the split resolve to the truth: the predecessor still answers for
        // the days before the correction, the successor from the correction date onward.
        var before = await resolver.GetByEmployeeIdAtAsync("emp001", twoDaysAgo);
        Assert.NotNull(before);
        Assert.Equal("AC", before!.AgreementCode);

        var atCorrection = await resolver.GetByEmployeeIdAtAsync("emp001", yesterday);
        Assert.NotNull(atCorrection);
        Assert.Equal("HK", atCorrection!.AgreementCode);

        var now = await resolver.GetByEmployeeIdAtAsync("emp001", today);
        Assert.NotNull(now);
        Assert.Equal("HK", now!.AgreementCode);

        // The live cache follows the row covering TODAY (repository-owned, never the
        // request value) — so a same-day read of the denormalised column agrees.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT agreement_code FROM users WHERE user_id = 'emp001'", conn);
        Assert.Equal("HK", (string?)await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// <b>REPLACED by S141 / TASK-14112 (ADR-040 D8 amendment, owner ruling 2026-09-11) —
    /// RED until the S141 wave-2 gate.</b> <b>OLD expectation</b> (pre-S141, ADR-023 D8
    /// same-day-only narrowing): PUT with <c>EffectiveFrom = tomorrow</c> AND a mutating
    /// <c>agreementCode</c> returned 422 — future-dating was refused symmetrically with
    /// backdating. <b>NEW (owner ruling — HR can schedule an employment change ahead of
    /// time):</b> this is the users-PUT twin of
    /// <see cref="PUT_BackdatedEffectiveFrom_SplitsTheDatedTimeline"/> above, just on the far
    /// side of today instead of the near side — router Shape 1
    /// (<c>REFINEMENT-s141-increment4-and-the-settlement-anchor.md</c> section B3 /
    /// <c>TemporalWriteRouterTests.S141_FutureWrite_OnOpenRow_SplitsAndSupersedes</c>): emp001's
    /// single open row (<c>'AC'</c> from '0001-01-01') is split at TOMORROW — closed there, and a
    /// new open row from tomorrow carries <c>'HK'</c>.
    /// <para>
    /// <b>Why this is a real assertion, not a bare status-code check.</b> The scheduled code must
    /// NOT take effect early: resolving TODAY must still answer <c>'AC'</c> (the split closes the
    /// predecessor row AT tomorrow, so it still covers every day up to and including today), while
    /// resolving TOMORROW must answer the newly scheduled <c>'HK'</c>. A write that silently
    /// no-opped, or one that (wrongly) changed today's value early, would both still return 200 and
    /// would both be caught by these two resolutions disagreeing with what the split must produce.
    /// </para>
    /// <para>
    /// <b>S142 / TASK-14202 — the host is pinned, and that is what keeps this test about the
    /// FUTURE.</b> It used to compute <c>tomorrow</c> from the machine's own UTC clock. That made
    /// the word "future" mean "one day after whatever day CI happened to run on", which is exactly
    /// the shape S138 lost two CI runs to. Worse, once the product moved to the Europe/Copenhagen
    /// business day, a run in the late-UTC-evening window would have sent the server's OWN current
    /// day and silently converted a future-dating test into a same-day one — passing, and proving
    /// something else. The host is now pinned to
    /// <see cref="BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen"/>, where the Danish day
    /// is 2026-07-16, and the scheduled date is the literal 2026-07-17.
    /// </para>
    ///
    /// <para>
    /// <b>Docker-gated.</b> Docker is unavailable on the authoring machine (standing project
    /// constraint), so this is CI-verified, not claimed green here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PUT_FutureDatedEffectiveFrom_SplitsTheDatedTimeline()
    {
        var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        var client = AuthorizedClient(host);
        // LITERALS. 2026-07-15 22:30 UTC is 2026-07-16 00:30 in Copenhagen, so the server's business
        // day is the 16th and a change dated the 17th is genuinely scheduled ahead of it.
        var today = new DateOnly(2026, 7, 16);
        var tomorrow = new DateOnly(2026, 7, 17);

        // S35 / TASK-3506 — admin-strict If-Match required (see backdated test
        // above for rationale).
        var getRsp = await client.GetAsync("/api/admin/users/emp001");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        var req = new HttpRequestMessage(HttpMethod.Put, "/api/admin/users/emp001")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = tomorrow.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(req);

        // OLD: UnprocessableEntity. NEW: the scheduled change is recorded, not refused.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var repo = new UserAgreementCodeRepository(_harness.Factory);
        var resolver = new EmploymentProfileResolver(_harness.Factory, repo);

        // The scheduled code has NOT taken effect yet — today still resolves to the old value.
        var stillToday = await resolver.GetByEmployeeIdAtAsync("emp001", today);
        Assert.NotNull(stillToday);
        Assert.Equal("AC", stillToday!.AgreementCode);

        // ...but the scheduled date itself resolves to the new one.
        var scheduled = await resolver.GetByEmployeeIdAtAsync("emp001", tomorrow);
        Assert.NotNull(scheduled);
        Assert.Equal("HK", scheduled!.AgreementCode);

        // S142 / TASK-14202 — and the denormalised cache, which the repository refreshes from the
        // row covering ITS OWN "today" (the Copenhagen day), must still read the OLD code. This is
        // the half of the mechanism the endpoint cannot see: a scheduled change that leaked into
        // users.agreement_code would reach every live-only consumer — and the login token's
        // fallback — a day early.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cacheCmd = new NpgsqlCommand(
            "SELECT agreement_code FROM users WHERE user_id = 'emp001'", conn);
        Assert.Equal("AC", (string?)await cacheCmd.ExecuteScalarAsync());
    }

    // ═════════════════════════════════════════════════════════════════════
    // PUT dual-emission ordering — Case C cross-day supersession
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Case C cross-day PUT (predecessor seeded at '0001-01-01', request
    /// EffectiveFrom=today) emits BOTH:
    /// <list type="bullet">
    ///   <item><c>UserAgreementCodeChanged</c> — narrow signal, always emits
    ///     when agreement_code mutated (preserved S33 contract).</item>
    ///   <item><c>UserAgreementCodeSuperseded</c> — Case C lifecycle event,
    ///     emitted ADDITIONALLY (dual emission per S25 publish-supersession
    ///     precedent).</item>
    /// </list>
    /// The audit row carries <c>action='SUPERSEDED'</c> with populated
    /// <c>version_before</c>/<c>version_after</c> (S33 EmployeeProfile precedent).
    /// Stream-id is <c>user-{userId}</c> for both events.
    ///
    /// <para>
    /// <b>Consumer dedupe contract</b> (refinement cycle 2 Reviewer WARNING 2):
    /// downstream consumers MUST dedupe Changed + Superseded on Case C — the
    /// narrow Changed signal is for steady-state replay-data trail walkers
    /// while Superseded carries the predecessor close + successor open
    /// lifecycle transition. Consumers selecting on
    /// <c>event_type='UserAgreementCodeChanged'</c> alone see every change;
    /// consumers selecting on Superseded see only cross-day transitions.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminPutUserCrossDayAgreementCodeChange_EmitsBothChangedAndSupersededEvents_AndAuditActionSUPERSEDED()
    {
        var client = AuthorizedClient();
        const string userId = "emp001";
        // S142 / TASK-14202 — DELIBERATELY NOT pinned (see the backdated fact above for the full
        // reasoning). "Cross-day" here means the write's date differs from the PREDECESSOR row's
        // date ('0001-01-01'), which is a comparison between two stored/supplied values; the
        // server's own calendar day takes no part in it, and every value asserted below comes back
        // out of the request.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // S35 / TASK-3506 — admin-strict If-Match required on PUT. Capture
        // ETag via GET first; the new GET endpoint stamps ETag: "<version>".
        var getRsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var etag = getRsp.Headers.ETag;
        Assert.NotNull(etag);

        // emp001's user_agreement_codes row was backfilled by the seeder at
        // effective_from='0001-01-01' < today → Case C routing on this PUT.
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(new
            {
                agreementCode = "HK",
                effectiveFrom = today.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.IfMatch.Add(etag!);
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Both events present on the user-{userId} stream.
        var streamId = $"user-{userId}";
        await using (var bothCmd = new NpgsqlCommand(
            """
            SELECT event_type FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type IN ('UserAgreementCodeChanged', 'UserAgreementCodeSuperseded')
            ORDER BY outbox_id ASC
            """, conn))
        {
            bothCmd.Parameters.AddWithValue("streamId", streamId);
            var types = new List<string>();
            await using var reader = await bothCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                types.Add(reader.GetString(0));
            Assert.Contains("UserAgreementCodeChanged", types);
            Assert.Contains("UserAgreementCodeSuperseded", types);
        }

        // Superseded payload carries the predecessor + successor identities.
        await using (var supersededCmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId
              AND event_type = 'UserAgreementCodeSuperseded'
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn))
        {
            supersededCmd.Parameters.AddWithValue("streamId", streamId);
            var rawPayload = (string?)await supersededCmd.ExecuteScalarAsync();
            Assert.False(string.IsNullOrEmpty(rawPayload));
            using var payloadDoc = JsonDocument.Parse(rawPayload!);
            Assert.Equal(userId, payloadDoc.RootElement.GetProperty("userId").GetString());
            Assert.Equal("AC", payloadDoc.RootElement.GetProperty("oldAgreementCode").GetString());
            Assert.Equal("HK", payloadDoc.RootElement.GetProperty("newAgreementCode").GetString());
            // End-exclusive convention: predecessorEffectiveTo == newEffectiveFrom == today.
            Assert.Equal(today.ToString("yyyy-MM-dd"),
                payloadDoc.RootElement.GetProperty("predecessorEffectiveTo").GetString());
            Assert.Equal(today.ToString("yyyy-MM-dd"),
                payloadDoc.RootElement.GetProperty("newEffectiveFrom").GetString());
            // S33 Step 7a P1 absorption: successor version = predecessor.Version + 1.
            var versionBefore = payloadDoc.RootElement.GetProperty("versionBefore").GetInt64();
            var versionAfter = payloadDoc.RootElement.GetProperty("versionAfter").GetInt64();
            Assert.Equal(versionBefore + 1, versionAfter);
        }

        // Audit row: action='SUPERSEDED' with populated version_before/version_after.
        await using (var auditCmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after FROM user_agreement_codes_audit
            WHERE user_id = @userId
              AND action IN ('UPDATED', 'SUPERSEDED')
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn))
        {
            auditCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await auditCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "PUT must emit a UPDATED or SUPERSEDED audit row.");
            Assert.Equal("SUPERSEDED", reader.GetString(0));
            Assert.False(reader.IsDBNull(1), "Case C audit must have non-null version_before.");
            Assert.False(reader.IsDBNull(2), "Case C audit must have non-null version_after.");
            Assert.Equal(reader.GetInt64(1) + 1, reader.GetInt64(2));
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    /// <summary>
    /// S142 / TASK-14202 — the same authorized client against a DERIVED host (one pinned to a fixed
    /// instant). Note the boot-order rule that comes with every <c>WithWebHostBuilder</c>-derived
    /// host: its first <c>CreateClient()</c> re-runs <c>Program.cs</c>'s startup seeders against the
    /// same Postgres container, so any "absent state" a test depends on must be created after this
    /// call, never before. The facts here create their own users, so nothing is at risk.
    /// </summary>
    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
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
