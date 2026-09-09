using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S30 / TASK-3010 MARQUEE D-test — the load-bearing Step-7a-equivalent invariant for
/// Phase 4d-2 entitlement-policy versioned history (ADR-021 D2 + ADR-016 D5b "fifth
/// pattern"): an admin edit to the live entitlement config TODAY must NOT shift the
/// quota observed by quota-check / balance-summary lookups for an entitlement year
/// that started BEFORE today. The two-step consumption pattern (TASK-3008 + ADR-021)
/// must:
///
/// <list type="number">
///   <item>Read the LIVE (open) row to obtain the (frozen-per-natural-key)
///   <c>ResetMonth</c>;</item>
///   <item>Derive <c>entitlementYearStart</c> from <c>ResetMonth</c> + the absence /
///   summary date;</item>
///   <item>Issue <c>GetByTypeAtAsync(asOfDate=entitlementYearStart)</c> to fetch the
///   row that was in effect at year-start — NOT the freshly-superseding live row.</item>
/// </list>
///
/// <para>
/// <b>Scenario</b>: AC VACATION at OK24 has <c>annual_quota=25</c>, <c>reset_month=9</c>,
/// <c>effective_from='0001-01-01'</c> per the init.sql seed. Today is some date in
/// May 2026 → the in-flight entitlement year-Y started <c>2025-09-01</c>. An admin
/// cross-day-supersedes the AC VACATION config today, raising
/// <c>annual_quota: 25 → 27</c> (new row at <c>effective_from=today</c>).
/// Then we simulate Skema's quota check at the year-Y start: it MUST observe 25 (the
/// year-start row, which is the closed predecessor), NOT 27 (the freshly-live row).
/// </para>
///
/// <para>
/// Direct-orchestration harness — mirrors S29's marquee
/// <c>ReplayDeterminismTests.ReplayAsync_StableUnderWtmMutation_ExportLinesByteIdentical</c>
/// shape: the contract under test (year-start-asOfDate-respecting dated read) is a
/// property of <see cref="EntitlementConfigRepository.GetByTypeAtAsync(string, string, string, DateOnly, CancellationToken)"/>
/// + the matching consumption sites; proving it at the repository surface is the
/// minimum sufficient harness for the replay-deterministic invariant. HTTP-level
/// coverage of the admin-CRUD wire shape lives in
/// <see cref="EntitlementConfigEndpointTests"/>.
/// </para>
///
/// <para>
/// <b>FAILS without versioned history</b>: pre-S30 the table had no
/// <c>effective_from</c> column and the only row was overwritten in-place by the
/// admin edit, so any read would return 27. The PASS criterion proves the
/// effective-dating + dated-read contract closes ADR-016 D10 for entitlements.
/// </para>
///
/// <para>
/// <b>S140 / TASK-14002 (QUAL-153/154) — a DETERMINISM conversion, NOT a seam pin.</b>
/// <see cref="EntitlementConfigRepository"/> reads NO clock anywhere — no <c>TimeProvider</c>,
/// no raw wall-clock read, no <c>CURRENT_DATE</c>/<c>NOW()</c> in any of its SQL (verified by
/// grep over the repository file). The defect this task fixes was entirely inside the TEST: a
/// raw wall-clock read of today's date made the marquee's own "which
/// entitlement year contains today" branch — and its precondition assertion — a function of the
/// REAL calendar day the suite happened to run on. Once a year, on 1 September itself, "today"
/// EQUALS the entitlement year-start, the precondition <c>today &gt; entitlementYearStart</c> goes
/// from true to false, and the whole suite hard-fails — not because anything in the product broke,
/// but because the test's own wall-clock read landed on its one degenerate day. Converting this
/// suite to a constant anchor does NOT reach any product seam: a reviewer must NOT read these pins
/// as proving TASK-14001's <c>TimeProvider</c> plumbing reaches this path, because it never did and
/// still does not — there is nothing here for a seam to reach. What the conversion buys is exactly
/// what determinism buys anywhere: the fact and its precondition are true on every calendar day the
/// suite runs, not 364 of 365.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EntitlementQuotaCheckUsesYearStartTests : IAsyncLifetime
{
    private const string EntitlementType = "VACATION";
    private const string AgreementCode = "AC";
    private const string OkVersion = "OK24";

    /// <summary>
    /// S140 / TASK-14002 (PAT-008 naming convention, reused here for cross-suite consistency —
    /// this suite has no OK-version or weekday dependency of its own). A NON-BOUNDARY anchor: March
    /// is nowhere near the VACATION reset month (September), so the existing marquee fact's
    /// "today is strictly after entitlement year-start" precondition holds for a documented,
    /// checkable reason rather than by the luck of which day the suite happened to run on. Self-
    /// checked once, by <see cref="Anchor_IsWednesday_OnOk24Side"/>.
    /// </summary>
    private static readonly DateOnly F = new(2025, 3, 12);

    /// <summary>
    /// The SEPARATE anchor <see cref="EntitlementQuotaCheck_OnResetDayItself_YearBranchSelectsCurrentYear"/>
    /// needs: EXACTLY 1 September, the one calendar day <see cref="F"/> is deliberately chosen to
    /// avoid. 2025-09-01 is a MONDAY — irrelevant to this suite's arithmetic (there is no working-day
    /// norm here, unlike the absence-seeding suites PAT-008 otherwise guards), so only the OK-version
    /// side is self-checked for this anchor, by the same fact.
    /// </summary>
    private static readonly DateOnly SeptFirst = new(2025, 9, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private EntitlementConfigRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Apply the full canonical init.sql so we get the 30 seed entitlement_configs rows
        // at effective_from='0001-01-01' + the partial-unique-index + history-unique-index
        // baked into the post-S30 schema. The marquee invariant is observed against a
        // RealSeed shape — not a hand-rolled fixture — so any drift in the seed values
        // (e.g. AC VACATION quota changes legally to a non-25 value) surfaces here first.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _repo = new EntitlementConfigRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>Locks the facts the two marquee facts below lean on without re-deriving them.</summary>
    [Fact]
    public void Anchor_IsWednesday_OnOk24Side()
    {
        Assert.Equal(DayOfWeek.Wednesday, F.DayOfWeek);
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(F));
        // SeptFirst's weekday is immaterial here (see the field doc); only the OK-version side
        // is asserted, so a future OK-version cutover move cannot silently drag this anchor
        // across it without a visible failure.
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(SeptFirst));
    }

    [Fact]
    public async Task EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig()
    {
        // ─── Step 1: anchor "today" (F — see the class/field docs) + verify seed shape ───
        var today = F; // S140/TASK-14002: was a raw wall-clock read of today's date.

        // The init.sql seed sets AC VACATION OK24 to annual_quota=25, reset_month=9,
        // effective_from='0001-01-01'. If any of these drift, the test breaks here
        // (intentional — sentinels for unannounced agreement-rule changes).
        var seedLive = await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.NotNull(seedLive);
        Assert.Equal(25m, seedLive!.AnnualQuota);
        Assert.Equal(9, seedLive.ResetMonth);
        Assert.Equal(new DateOnly(1, 1, 1), seedLive.EffectiveFrom);
        Assert.Null(seedLive.EffectiveTo);

        // ─── Step 2: derive year-Y-start from reset_month + today ────────────
        // AC VACATION reset_month=9 → entitlement year starts Sept 1; "year-Y" is the
        // entitlement year that *contains* today. If today.Month >= 9 → year-Y started
        // this calendar year; else → it started in the previous calendar year.
        int entitlementYear = today.Month >= seedLive.ResetMonth ? today.Year : today.Year - 1;
        var entitlementYearStart = new DateOnly(entitlementYear, seedLive.ResetMonth, 1);

        // Step-5a Reviewer W-2: tie this fact's OWN reproduction of the branch to the REAL
        // product resolver (EntitlementPeriodResolver.Resolve, EntitlementPeriodResolver.cs:120)
        // instead of asserting only against the locally-recomputed expression above — otherwise a
        // regression inside the resolver itself could leave this fact green while the product
        // disagreed with the test's private arithmetic. RED condition: this assertion fails if the
        // resolver's per-type dispatch, its AccrualStart geometry, or its entitlement-year
        // arithmetic ever diverges from this fact's manual derivation for ANY reason (e.g. a
        // VACATION/SPECIAL_HOLIDAY mixup, an off-by-one in BuildResetMonth). At this NON-boundary
        // anchor (F=2025-03-12, month 3) it does NOT discriminate a `>=`-vs-`>` operator flip —
        // month 3 is neither >= nor > 9, so both operators pick the SAME previous year; that
        // specific boundary edge is what
        // EntitlementQuotaCheck_OnResetDayItself_YearBranchSelectsCurrentYear pins, at the one
        // anchor (SeptFirst) where the two operators actually disagree.
        var resolvedAtF = EntitlementPeriodResolver.Resolve(EntitlementType, seedLive.ResetMonth, today);
        Assert.Equal(new DateOnly(2024, 9, 1), resolvedAtF.AccrualStart);
        Assert.Equal(entitlementYearStart, resolvedAtF.AccrualStart);

        // With F fixed in March, "today" is never the pathological boundary where
        // today == year-start (that edge is pinned SEPARATELY and on purpose, by
        // EntitlementQuotaCheck_OnResetDayItself_YearBranchSelectsCurrentYear below — the two
        // are deliberately NOT folded into one fact). This assertion is therefore a checkable
        // fact about the constant F, not a 364-of-365-days gamble against the wall clock: RED
        // condition — if F were ever moved onto or past 1 September, this would need
        // recomputing, and CI would say so immediately rather than once a year.
        Assert.True(today > entitlementYearStart,
            "Marquee precondition: today must be strictly after the entitlement year-start; " +
            $"today={today:yyyy-MM-dd}, year-start={entitlementYearStart:yyyy-MM-dd}");

        // ─── Step 3: admin edits the live AC VACATION config TODAY (25 → 27) ──
        // Cross-day supersession via SupersedeAndCreateAsync (the predecessor's
        // effective_from is '0001-01-01' which is < today, so Case B fires).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var predecessor = await _repo.AcquireLockAsync(
                conn, tx, EntitlementType, AgreementCode, OkVersion);
            Assert.NotNull(predecessor);
            Assert.Equal(25m, predecessor!.AnnualQuota);

            var newConfig = new EntitlementConfig
            {
                ConfigId = Guid.NewGuid(),
                EntitlementType = EntitlementType,
                AgreementCode = AgreementCode,
                OkVersion = OkVersion,
                AnnualQuota = 27m, // ← the edited value
                AccrualModel = predecessor.AccrualModel,
                ResetMonth = predecessor.ResetMonth, // immutable per natural key
                CarryoverMax = predecessor.CarryoverMax,
                ProRateByPartTime = predecessor.ProRateByPartTime,
                IsPerEpisode = predecessor.IsPerEpisode,
                MinAge = predecessor.MinAge,
                Description = predecessor.Description,
                EffectiveFrom = today,
            };

            var saveResult = await _repo.SupersedeAndCreateAsync(
                conn, tx, newConfig, predecessor, expectedCurrentVersion: predecessor.Version);
            // Cross-day supersession (Case B): IsCreated=false, predecessor closed,
            // new row INSERTed at version 1, SupersededConfigId points at predecessor.
            Assert.False(saveResult.IsCreated);
            Assert.NotNull(saveResult.SupersededConfigId);
            Assert.Equal(predecessor.ConfigId, saveResult.SupersededConfigId);

            await tx.CommitAsync();
        }

        // ─── Step 4: verify live row NOW reflects the edited value (27) ──────
        var postEditLive = await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.NotNull(postEditLive);
        Assert.Equal(27m, postEditLive!.AnnualQuota);
        Assert.Equal(today, postEditLive.EffectiveFrom);

        // And the predecessor is closed at effective_to=today (S30 cross-day supersession).
        // Looked up via config_id (set by AcquireLockAsync above) rather than by
        // effective_from, because the seed's effective_from='0001-01-01' rendering can vary
        // across Npgsql DateOnly conversion edges (BC vs AD wrap) — config_id is the stable
        // surrogate key.
        var allRows = await ReadAllForNaturalKeyAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.Equal(2, allRows.Count); // closed predecessor + new live row
        var predecessorAfterEdit = allRows.Single(r => r.EffectiveTo == today);
        Assert.NotNull(predecessorAfterEdit);
        Assert.Equal(25m, predecessorAfterEdit.AnnualQuota);
        Assert.Equal(seedLive.ConfigId, predecessorAfterEdit.ConfigId);
        Assert.Equal(seedLive.EffectiveFrom, predecessorAfterEdit.EffectiveFrom);

        // ─── Step 5: LOAD-BEARING ASSERTION — Skema quota check at year-Y-start ─
        // Skema's TASK-3008 two-step pattern:
        //   Step 1: GetCurrentOpenAsync (already done above, postEditLive.ResetMonth=9)
        //   Step 2: GetByTypeAtAsync(asOfDate = year-Y-start)
        //
        // The dated read MUST resolve to the row that was effective at year-Y-start
        // (the now-closed predecessor at effective_from='0001-01-01', effective_to=today).
        // Per the end-exclusive predicate
        //   effective_from <= asOfDate AND (effective_to IS NULL OR effective_to > asOfDate)
        // we need year-Y-start (e.g. 2025-09-01) to satisfy:
        //   '0001-01-01' <= 2025-09-01   ✓
        //   effective_to (today) > 2025-09-01   ✓ (today is in May 2026, after Sept 1 2025)
        // → predecessor wins. annual_quota = 25 (the year-start value), NOT 27.
        var configAtYearStart = await _repo.GetByTypeAtAsync(
            EntitlementType, AgreementCode, OkVersion, entitlementYearStart);
        Assert.NotNull(configAtYearStart);
        Assert.Equal(25m, configAtYearStart!.AnnualQuota); // ← the load-bearing assertion
        Assert.Equal(predecessorAfterEdit.ConfigId, configAtYearStart.ConfigId);

        // ─── Step 6: balance-summary view for the in-flight month ─────────────
        // Balance summary (BalanceEndpoints.cs:120 two-step) reports total_quota for the
        // current month. With today in entitlement-year-Y, total_quota must use the
        // year-start row's annual_quota = 25, NOT 27. Same dated read, same assertion.
        var configForSummary = await _repo.GetByTypeAtAsync(
            EntitlementType, AgreementCode, OkVersion, entitlementYearStart);
        Assert.NotNull(configForSummary);
        Assert.Equal(25m, configForSummary!.AnnualQuota);

        // ─── Step 7: forward-looking probe — a hypothetical absence on/after the ─
        // NEXT reset boundary (e.g. 2026-09-15 when today < 2026-09-01) would land
        // in the entitlement year that started 2026-09-01, which is on/after today
        // (the new row's effective_from). At that asOfDate the new row wins → 27.
        // This pins the forward-only forward-symmetry of the dated read (the year-Y
        // immutability does NOT freeze future years). We compute the hypothetical
        // next year-start date and check via the same dated read.
        var nextYearStart = new DateOnly(entitlementYear + 1, seedLive.ResetMonth, 1);
        if (today <= nextYearStart) // skip if we're already past it (unlikely)
        {
            var configAtNextYearStart = await _repo.GetByTypeAtAsync(
                EntitlementType, AgreementCode, OkVersion, nextYearStart);
            Assert.NotNull(configAtNextYearStart);
            // today < nextYearStart and new row's effective_from=today < nextYearStart →
            // new row covers nextYearStart per the end-exclusive predicate (effective_to
            // IS NULL).
            Assert.Equal(27m, configAtNextYearStart!.AnnualQuota);
            Assert.Equal(postEditLive.ConfigId, configAtNextYearStart.ConfigId);
        }
    }

    /// <summary>
    /// S140 / TASK-14002 (QUAL-153/154) — the SEPARATE fact for the reset day itself. Deliberately
    /// NOT folded into <see cref="EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig"/>:
    /// that marquee's own precondition (<c>today &gt; entitlementYearStart</c>) is written to
    /// EXCLUDE this exact day, because a same-day admin edit at the boundary would make the
    /// dated-read assertion degenerate for reasons unrelated to what THIS fact pins.
    ///
    /// <para>
    /// <b>What this pins — CORRECTED (Step-5a Reviewer W-2).</b> The first cut of this fact
    /// reproduced the year-branch expression (<c>today.Month &gt;= seedLive.ResetMonth ? … : …</c>)
    /// INSIDE the test and asserted the result of its own copy — a flip of the product's operator
    /// in <see cref="EntitlementPeriodResolver.Resolve"/> (<c>EntitlementPeriodResolver.cs:120</c>)
    /// would have left this fact green, because the fact never called the product code at all. It
    /// now calls <see cref="EntitlementPeriodResolver.Resolve"/> directly: on 1 September itself
    /// (<see cref="SeptFirst"/>) the resolver's <c>asOf.Month &gt;= resetMonth</c> comparison must
    /// select THIS year (2025), so <c>AccrualStart</c> (the VACATION entitlement-year start) must
    /// equal <see cref="SeptFirst"/> exactly — the day the reset happens IS day one of the new
    /// year, not the last day of the old one. This suite used to inherit exactly this calendar
    /// date from the real wall clock once a year; QUAL-154 PREDICTED (never itself observed — no
    /// CI run is on record failing this way) that it would eventually hard-fail on its own
    /// precondition assert rather than on a meaningful product check. Pinning it on a CONSTANT
    /// makes the edge run in CI on every commit, not once a year by accident.
    /// </para>
    ///
    /// <para>
    /// <b>RED condition.</b> If <see cref="EntitlementPeriodResolver"/>'s VACATION/calendar branch
    /// (<c>EntitlementPeriodResolver.cs:120</c>, <c>asOf.Month &gt;= resetMonth ? asOf.Year :
    /// asOf.Year - 1</c>) were ever changed from <c>&gt;=</c> to <c>&gt;</c>, then at
    /// <see cref="SeptFirst"/> (month 9, ResetMonth 9) <c>9 &gt; 9</c> is false, so the resolver
    /// would compute the entitlement year as <c>SeptFirst.Year - 1</c> (2024) instead of 2025, and
    /// <c>AccrualStart</c> would land a full year early (2024-09-01) — both assertions on the
    /// resolver's result below would fail. This is a GENUINE product pin (it calls real product
    /// code and can fail from a real product regression), unlike the marquee fact's parallel
    /// resolver-tie assertion, which at the NON-boundary anchor F cannot discriminate this specific
    /// operator flip (see that assertion's own comment).
    /// </para>
    ///
    /// <para>
    /// <b>The dated CONFIG read below is a SANITY check, not a second RED condition (Step-5a
    /// Reviewer W-2 correction).</b> An earlier revision of this comment claimed the seed row's
    /// <c>effective_from = '0001-01-01'</c> being treated as covering <see cref="SeptFirst"/> under
    /// an INCLUSIVE lower bound (<c>effective_from &lt;= asOfDate</c>) was a second, independent RED
    /// condition — that claim was false: <c>0001-01-01</c> is so far below <c>2025-09-01</c> that
    /// the comparison holds under an inclusive OR an exclusive bound alike
    /// (<c>EntitlementConfigRepository.cs:139</c>), so a bound-direction regression could not be
    /// caught here. The read is kept anyway as a sanity check — it confirms the ordinary
    /// dated-config lookup still resolves to the always-open seed row on the one calendar day this
    /// suite otherwise treats specially — but its own failure would point at something unrelated
    /// (a broken query, a wrong natural key), not at the boundary this fact exists to pin.
    /// </para>
    ///
    /// <para>
    /// <b>Still Docker-gated, and why.</b> <see cref="EntitlementPeriodResolver.Resolve"/> itself is
    /// a pure function (no I/O, no wall-clock — see its own class doc) and needs no database at
    /// all; the resolver-call assertions above WOULD run fine as a Unit test. This fact stays under
    /// <c>[Trait("Category","Docker")]</c> regardless because (a) it shares this Docker-gated class
    /// with the marquee fact and the harness/schema setup in <c>InitializeAsync</c>, and (b) it
    /// still performs a real Postgres read (<c>seedLive</c>'s <c>ResetMonth</c>, and the sanity
    /// dated-config read) that needs the DockerHarness regardless of the resolver call. A follow-up
    /// could lift a resolver-only duplicate of the RED-condition assertion into
    /// <c>StatsTid.Tests.Unit</c> so the boundary pin also runs where Docker is unavailable — not
    /// done here (out of this task's `tests/StatsTid.Tests.Regression` scope for this file).
    /// </para>
    ///
    /// <para>
    /// This fact makes NO product edit (no supersession) — it is a pure read at the boundary, kept
    /// deliberately simple so its RED condition stays attributable to the resolver branch it names,
    /// not entangled with the same-day-edit degenerate case the marquee fact excludes.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EntitlementQuotaCheck_OnResetDayItself_YearBranchSelectsCurrentYear()
    {
        var today = SeptFirst; // exactly 1 September — the reset day itself.

        var seedLive = await _repo.GetCurrentOpenAsync(EntitlementType, AgreementCode, OkVersion);
        Assert.NotNull(seedLive);
        Assert.Equal(9, seedLive!.ResetMonth);

        // THE genuine product pin (Step-5a Reviewer W-2 fix): call the REAL resolver rather than
        // reproducing its branch inside the test. See the RED condition in the doc comment above.
        var resolved = EntitlementPeriodResolver.Resolve(EntitlementType, seedLive.ResetMonth, today);
        Assert.Equal(today, resolved.AccrualStart);
        Assert.Equal(today.Year, resolved.EntitlementYear);

        // Sanity check, NOT a boundary-discriminating pin (see the doc comment) — the dated
        // CONFIG read still resolves to the always-open seed row on the reset day itself.
        var configAtYearStart = await _repo.GetByTypeAtAsync(
            EntitlementType, AgreementCode, OkVersion, resolved.AccrualStart);
        Assert.NotNull(configAtYearStart);
        Assert.Equal(25m, configAtYearStart!.AnnualQuota);
        Assert.Equal(seedLive.ConfigId, configAtYearStart.ConfigId);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<List<EntitlementConfig>> ReadAllForNaturalKeyAsync(
        string entitlementType, string agreementCode, string okVersion)
    {
        var rows = new List<EntitlementConfig>();
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM entitlement_configs
            WHERE entitlement_type = @entitlementType
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
            ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new EntitlementConfig
            {
                ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
                EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
                AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
                OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
                AnnualQuota = reader.GetDecimal(reader.GetOrdinal("annual_quota")),
                AccrualModel = reader.GetString(reader.GetOrdinal("accrual_model")),
                ResetMonth = reader.GetInt32(reader.GetOrdinal("reset_month")),
                CarryoverMax = reader.GetDecimal(reader.GetOrdinal("carryover_max")),
                ProRateByPartTime = reader.GetBoolean(reader.GetOrdinal("pro_rate_by_part_time")),
                IsPerEpisode = reader.GetBoolean(reader.GetOrdinal("is_per_episode")),
                MinAge = reader.IsDBNull(reader.GetOrdinal("min_age"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("min_age")),
                Description = reader.IsDBNull(reader.GetOrdinal("description"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("description")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                Version = reader.GetInt64(reader.GetOrdinal("version")),
                EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
                EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
                    ? null
                    : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
            });
        }
        return rows;
    }
}
