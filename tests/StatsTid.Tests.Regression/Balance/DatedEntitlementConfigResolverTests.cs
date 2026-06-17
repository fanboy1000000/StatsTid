using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Balance;

/// <summary>
/// S81 / TASK-8101 (R7) — characterization suite for the hoisted
/// <see cref="DatedEntitlementConfigResolver"/>, pinning ALL graceful terminals of the dated-config
/// resolution that the YearOverview handler formerly inlined (the three local functions
/// <c>ResolveAgreementAtAsync</c> / <c>ResolveFallbackLiveAsync</c> / <c>ResolveDatedConfigAsync</c>
/// + their shared per-request caches). The extraction is a pure mechanical lift; these tests lock
/// the behaviour at the resolver level so the cutover (and the TASK-8102 consumers) cannot drift.
///
/// <para>
/// <b>Terminals covered (R7).</b>
/// <list type="bullet">
///   <item><description>(a) <b>dated hit</b> — a dated row covers the ferieår start ⇒ that row.</description></item>
///   <item><description>(b) <b>dated-miss → today-agreement liveConfig</b> — agreement at the ferieår
///     start == today's, no dated row ⇒ the passed-in <c>liveConfig</c> (the
///     <c>string.Equals(agreement, todayAgreementCode)</c> branch).</description></item>
///   <item><description>(c) <b>dated-miss → DIFFERENT historical-agreement live row</b> — agreement at
///     the ferieår start differs from today's, no dated row, a live open row exists for the historical
///     agreement ⇒ that row (the <c>ResolveFallbackLiveAsync</c> live-OkVersion terminal).</description></item>
///   <item><description>(d) <b>configless probe-anchor bootstrap</b> — the two reads the YearOverview
///     bootstrap loop composes (<c>ResolveAgreementAtAsync(anchor)</c> +
///     <c>ResolveFallbackLiveAsync(type, anchorAgreement)</c>) recover a configured historical
///     agreement's live row when today's agreement is configless.</description></item>
///   <item><description>(e) <b>empty-row terminal</b> — no live row for the (type, agreement) ⇒
///     <c>ResolveFallbackLiveAsync</c> returns null (and <c>ResolveDatedConfigAsync</c> falls back to
///     <c>liveConfig</c>), the precondition for the YearOverview graceful empty row (never 500).</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why Docker-gated (not Moq-mocked).</b> The two repositories
/// (<see cref="EntitlementConfigRepository"/>, <see cref="UserAgreementCodeRepository"/>) are sealed
/// with non-virtual methods and the test projects carry no mocking package, so the "mocked repos"
/// intent (SPRINT-81 R7) is realised by seeding the two real tables directly (deterministic rows,
/// independent of Program.cs seeders) and exercising the resolver against them. This is FAST relative
/// to the full HTTP YearOverview suite — no host boot, no auth, no projections — while characterizing
/// the exact production read path. The existing <see cref="YearOverviewTests"/> regression suite
/// continues to prove the ENDPOINT is unchanged.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatedEntitlementConfigResolverTests : IAsyncLifetime
{
    private const string Type = "VACATION";
    private const string Ok = "OK24";

    // The init.sql seed creates emp001 (+ its org + a live AC user_agreement_codes row), so the
    // user_agreement_codes.user_id FK is satisfied. Each test cleans ONLY its own synthetic rows.
    private const string Employee = "emp001";

    // SYNTHETIC agreement codes that the init.sql seed NEVER mints (seed uses AC/HK/PROSA), so no
    // seeded entitlement_configs / user_agreement_codes row can ever match these reads — the tests
    // see ONLY the rows they seed. (The pre-existing emp001 AC live agreement row is on a DIFFERENT
    // code than these, and is dated earlier than FerieaarStart's window in the cross-agreement cases.)
    private const string TodayAgreement = "S81TODAY";
    private const string HistoricalAgreement = "S81HIST";

    // The ferieår start under test (the dated-read anchor) and a clearly-later "today".
    private static readonly DateOnly FerieaarStart = new(2025, 9, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private DatedEntitlementConfigResolverFactory _factory = null!;
    private EntitlementConfigRepository _ecRepo = null!;
    private UserAgreementCodeRepository _agRepo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _ecRepo = new EntitlementConfigRepository(_harness.Factory);
        _agRepo = new UserAgreementCodeRepository(_harness.Factory);
        _factory = new DatedEntitlementConfigResolverFactory(_ecRepo, _agRepo);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Seed helpers — raw SQL against the real tables (deterministic, no seeders).
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-test isolation: drop the seeded + any prior-test agreement-code rows for the test
    /// employee (so the resolver's GetByUserIdAtAsync sees ONLY what the test seeds) and any
    /// entitlement_configs rows under the SYNTHETIC test agreement codes. The container is shared
    /// across tests on the same harness, so each test must start from a known empty slate for its
    /// own keys. (Other employees' / other agreements' seed rows are untouched.)
    /// </summary>
    private async Task ResetTestRowsAsync()
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using (var c1 = new NpgsqlCommand(
            "DELETE FROM user_agreement_codes WHERE user_id = @uid", conn))
        {
            c1.Parameters.AddWithValue("uid", Employee);
            await c1.ExecuteNonQueryAsync();
        }
        await using var c2 = new NpgsqlCommand(
            "DELETE FROM entitlement_configs WHERE agreement_code = ANY(@codes)", conn);
        c2.Parameters.AddWithValue("codes", new[] { TodayAgreement, HistoricalAgreement });
        await c2.ExecuteNonQueryAsync();
    }

    private async Task SeedAgreementCodeAsync(
        string agreementCode, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (
                assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (@id, @uid, @ac, @from, @to, 1)
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("uid", Employee);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedConfigAsync(
        string agreementCode, decimal annualQuota,
        DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        var id = Guid.NewGuid();
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_configs (
                config_id, entitlement_type, agreement_code, ok_version,
                annual_quota, accrual_model, reset_month, carryover_max,
                pro_rate_by_part_time, is_per_episode, min_age, description,
                full_day_only, effective_from, effective_to, version)
            VALUES (
                @id, @type, @ac, @ok,
                @quota, 'MONTHLY_ACCRUAL', 9, 5,
                FALSE, FALSE, NULL, NULL,
                FALSE, @from, @to, 1)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("type", Type);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", Ok);
        cmd.Parameters.AddWithValue("quota", annualQuota);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        cmd.Parameters.AddWithValue("to", (object?)effectiveTo ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    // A throwaway non-matching liveConfig sentinel, so a fallback-to-liveConfig terminal is
    // unmistakable (a unique quota the seeded rows never carry).
    private static EntitlementConfig LiveSentinel(decimal quota = 999m) => new()
    {
        ConfigId = Guid.NewGuid(),
        EntitlementType = Type,
        AgreementCode = TodayAgreement,
        OkVersion = Ok,
        AnnualQuota = quota,
        AccrualModel = "MONTHLY_ACCRUAL",
        ResetMonth = 9,
        CarryoverMax = 5m,
        ProRateByPartTime = false,
        IsPerEpisode = false,
        EffectiveFrom = new DateOnly(2020, 1, 1),
    };

    // liveAgreementCode is a DISTINCT sentinel ("S81LIVE") ≠ TodayAgreement, so the
    // agreement-by-date cache test can discriminate a cache hit from a re-read fallback (a re-read
    // after the row is deleted would return S81LIVE, not the cached TodayAgreement). For the
    // dated/fallback terminals the liveAgreementCode is never reached (the seeded dated rows cover
    // the anchor), so it does not perturb those cases.
    private const string LiveAgreement = "S81LIVE";

    private DatedEntitlementConfigResolver NewResolver() =>
        _factory.Create(Employee, Ok, LiveAgreement, TodayAgreement);

    // ════════════════════════════════════════════════════════════════════════
    // (a) Dated hit — a dated row covers the ferieår start ⇒ that row is returned.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveDatedConfig_DatedRowCoversAnchor_ReturnsDatedRow()
    {
        await ResetTestRowsAsync();
        // Today's-agreement dated row covering FerieaarStart, quota 25.
        await SeedConfigAsync(TodayAgreement, annualQuota: 25m, FerieaarStart, effectiveTo: null);
        await SeedAgreementCodeAsync(TodayAgreement, new DateOnly(2020, 1, 1), effectiveTo: null);

        var resolver = NewResolver();
        var result = await resolver.ResolveDatedConfigAsync(Type, FerieaarStart, Ok, LiveSentinel());

        Assert.Equal(25m, result.AnnualQuota);           // the dated row, NOT the live sentinel.
        Assert.Equal(TodayAgreement, result.AgreementCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // (b) Dated-miss → today-agreement liveConfig — agreement at the anchor == today's,
    //     no dated row covers the anchor ⇒ the passed-in liveConfig.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveDatedConfig_DatedMiss_SameAgreementAsToday_ReturnsLiveConfig()
    {
        await ResetTestRowsAsync();
        // A today's-agreement row exists but only AFTER the anchor (so the dated read at FerieaarStart
        // misses), and the agreement-code history resolves to today's at the anchor.
        await SeedConfigAsync(TodayAgreement, annualQuota: 25m,
            effectiveFrom: FerieaarStart.AddYears(1), effectiveTo: null);
        await SeedAgreementCodeAsync(TodayAgreement, new DateOnly(2020, 1, 1), effectiveTo: null);

        var resolver = NewResolver();
        var live = LiveSentinel(quota: 777m);
        var result = await resolver.ResolveDatedConfigAsync(Type, FerieaarStart, Ok, live);

        Assert.Same(live, result);                       // exact liveConfig pass-through.
        Assert.Equal(777m, result.AnnualQuota);
    }

    // ════════════════════════════════════════════════════════════════════════
    // (c) Dated-miss → DIFFERENT historical-agreement live row (ResolveFallbackLiveAsync,
    //     the live-OkVersion terminal).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveDatedConfig_DatedMiss_HistoricalAgreement_ReturnsFallbackLiveRow()
    {
        await ResetTestRowsAsync();
        // At FerieaarStart the employee was on the HISTORICAL agreement; today they are on AC.
        // No dated row covers the anchor, but a LIVE (open) row exists for the historical agreement.
        await SeedAgreementCodeAsync(HistoricalAgreement,
            new DateOnly(2020, 1, 1), effectiveTo: new DateOnly(2026, 1, 1));
        await SeedAgreementCodeAsync(TodayAgreement,
            new DateOnly(2026, 1, 1), effectiveTo: null);
        // Live open row for the historical agreement only (no dated row at the anchor), quota 30.
        await SeedConfigAsync(HistoricalAgreement, annualQuota: 30m,
            effectiveFrom: new DateOnly(2027, 1, 1), effectiveTo: null);

        var resolver = NewResolver();
        var live = LiveSentinel(quota: 777m);
        var result = await resolver.ResolveDatedConfigAsync(Type, FerieaarStart, Ok, live);

        Assert.Equal(30m, result.AnnualQuota);           // the historical-agreement LIVE row.
        Assert.Equal(HistoricalAgreement, result.AgreementCode);
        Assert.NotSame(live, result);
    }

    // ════════════════════════════════════════════════════════════════════════
    // (d) Configless probe-anchor bootstrap — the two reads the YearOverview bootstrap loop
    //     composes recover a configured historical agreement's live row.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ProbeBootstrap_ResolvesHistoricalAgreementAndItsLiveRow()
    {
        await ResetTestRowsAsync();
        // Today's agreement (AC) is configless; the September ferieår covering the selected year
        // BEGAN under the configured HISTORICAL agreement (HK) which HAS a live row.
        await SeedAgreementCodeAsync(HistoricalAgreement,
            new DateOnly(2020, 1, 1), effectiveTo: new DateOnly(2026, 1, 1));
        await SeedAgreementCodeAsync(TodayAgreement,
            new DateOnly(2026, 1, 1), effectiveTo: null);
        await SeedConfigAsync(HistoricalAgreement, annualQuota: 22m,
            effectiveFrom: new DateOnly(2020, 1, 1), effectiveTo: null);

        var resolver = NewResolver();

        // Step 1 of the bootstrap: the anchor's agreement code (the Sep year-1 anchor).
        var anchorAgreement = await resolver.ResolveAgreementAtAsync(FerieaarStart);
        Assert.Equal(HistoricalAgreement, anchorAgreement);
        Assert.NotEqual(TodayAgreement, anchorAgreement);   // the bootstrap's "differs from today's" gate.

        // Step 2: the historical agreement's live row (resetMonth discovery + fallback terminal).
        var altLive = await resolver.ResolveFallbackLiveAsync(Type, anchorAgreement);
        Assert.NotNull(altLive);
        Assert.Equal(22m, altLive!.AnnualQuota);
        Assert.Equal(9, altLive.ResetMonth);                // resetMonth recovered for the matrix loop.
    }

    // ════════════════════════════════════════════════════════════════════════
    // (e) Empty-row terminal — no live row for the (type, agreement) ⇒ ResolveFallbackLiveAsync
    //     returns null (cached), and ResolveDatedConfigAsync falls back to liveConfig.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveFallbackLive_NoLiveRow_ReturnsNull()
    {
        await ResetTestRowsAsync();
        // No entitlement_configs rows at all for the historical agreement.
        var resolver = NewResolver();

        var result = await resolver.ResolveFallbackLiveAsync(Type, HistoricalAgreement);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveDatedConfig_DatedMiss_HistoricalAgreementNoLiveRow_FallsBackToLiveConfig()
    {
        await ResetTestRowsAsync();
        // At the anchor the employee was on the historical agreement, today on AC; NEITHER a dated row
        // NOR a live row exists for the historical agreement ⇒ the chain bottoms out at liveConfig
        // (the empty-row precondition; never 500).
        await SeedAgreementCodeAsync(HistoricalAgreement,
            new DateOnly(2020, 1, 1), effectiveTo: new DateOnly(2026, 1, 1));
        await SeedAgreementCodeAsync(TodayAgreement,
            new DateOnly(2026, 1, 1), effectiveTo: null);

        var resolver = NewResolver();
        var live = LiveSentinel(quota: 555m);
        var result = await resolver.ResolveDatedConfigAsync(Type, FerieaarStart, Ok, live);

        Assert.Same(live, result);
        Assert.Equal(555m, result.AnnualQuota);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Cache behaviour — the per-request caches that make the resolver per-request (not a
    // singleton). A second call for the same key must not re-issue the repo read; a null
    // fallback result is cached too.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FallbackLive_NullResult_IsCached_AndSurvivesLaterSeed()
    {
        await ResetTestRowsAsync();
        var resolver = NewResolver();

        // First call: no live row ⇒ null (cached).
        var first = await resolver.ResolveFallbackLiveAsync(Type, HistoricalAgreement);
        Assert.Null(first);

        // Seed a live row AFTER the first (now-cached-null) read.
        await SeedConfigAsync(HistoricalAgreement, annualQuota: 40m,
            effectiveFrom: new DateOnly(2020, 1, 1), effectiveTo: null);

        // The cached null is returned — the read is NOT re-issued (bounded reads, byte-identical to
        // the former local function's caching).
        var second = await resolver.ResolveFallbackLiveAsync(Type, HistoricalAgreement);
        Assert.Null(second);

        // A FRESH resolver (new request) sees the seeded row — caches are request-scoped.
        var freshResolver = NewResolver();
        var fresh = await freshResolver.ResolveFallbackLiveAsync(Type, HistoricalAgreement);
        Assert.NotNull(fresh);
        Assert.Equal(40m, fresh!.AnnualQuota);
    }

    [Fact]
    public async Task AgreementByDate_IsCached_AndSurvivesLaterSeed()
    {
        await ResetTestRowsAsync();
        await SeedAgreementCodeAsync(TodayAgreement, new DateOnly(2020, 1, 1), effectiveTo: null);
        var resolver = NewResolver();

        var first = await resolver.ResolveAgreementAtAsync(FerieaarStart);
        Assert.Equal(TodayAgreement, first);

        // Mutate the underlying row out from under the resolver (delete it entirely). A FRESH read
        // for the same date would now fall back to the live cache (LiveAgreement / "S81LIVE" here, via
        // the factory's liveAgreementCode operand); but the cached value for FerieaarStart is returned
        // unchanged — proving the per-date cache short-circuits the repo read.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var del = new NpgsqlCommand(
                "DELETE FROM user_agreement_codes WHERE user_id = @uid", conn);
            del.Parameters.AddWithValue("uid", Employee);
            await del.ExecuteNonQueryAsync();
        }
        var second = await resolver.ResolveAgreementAtAsync(FerieaarStart);
        Assert.Equal(first, second);   // cached TodayAgreement, NOT a re-read (which would now miss).
    }
}
