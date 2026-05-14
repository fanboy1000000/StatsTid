using Npgsql;
using StatsTid.Infrastructure;
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
/// </summary>
[Trait("Category", "Docker")]
public sealed class EntitlementQuotaCheckUsesYearStartTests : IAsyncLifetime
{
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

    [Fact]
    public async Task EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig()
    {
        // ─── Step 1: anchor "today" + verify seed shape ──────────────────────
        const string EntitlementType = "VACATION";
        const string AgreementCode = "AC";
        const string OkVersion = "OK24";
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

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

        // Skip the marquee on the pathological boundary where today == year-start
        // (in which case year-start IS the supersession date and the assertion
        // degenerates — not the invariant we're testing). The window of validity
        // is "today > year-start" — give us at least one day of straddle. In a
        // calendar year this is true on 364 out of 365 days, so the skip is rare.
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
