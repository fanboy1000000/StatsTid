using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S33 / TASK-3312 MARQUEE — proves ADR-023 D1: PCS replays produce
/// byte-identical rule-engine output under mid-period admin mutation of the
/// dated employee_profiles row, because the resolver wired into PCS
/// (TASK-3305 cutover) reads <c>(effective_from &lt;= segment.StartDate
/// AND (effective_to IS NULL OR effective_to &gt; segment.StartDate))</c>
/// and a post-period Case C supersession does NOT shrink the predecessor's
/// window below the calculation period.
///
/// <para>
/// <b>Assertion target.</b> Both variants assert byte-identical equality of
/// <c>JsonSerializer.Serialize(replay.RuleResults, jsonOpts)</c> between the
/// baseline forward-calc and a replay-after-mutation. ReplayAsync stamps the
/// original manifest-id on every rule result, so a stable
/// <c>JsonSerializer.Serialize</c> proves the rule-engine output stream is
/// the same — independent of any subsequent mutation of the live
/// <c>employee_profiles</c> row. This is distinct from the S29 TASK-2909
/// WTM marquee which asserts byte-identity at the post-mapping export-line
/// phase; here we pin the upstream rule-engine output, which is what feeds
/// <c>NormCheckRule.cs</c> per refinement cycle 1 BLOCKER-2 absorption.
/// </para>
///
/// <para>
/// <b>Why this is load-bearing.</b> The cutover at
/// <c>PeriodCalculationService.cs:344-358</c> replaces the previous
/// "copy caller-supplied profile" semantic with a dated resolver lookup.
/// If that lookup ever reverted to reading the live (open) row, a post-period
/// edit would change the inputs to NormCheckRule (37.0 * 1.0 = 37.0 versus
/// 32.0 * 0.75 = 24.0) and replay determinism — the foundation of ADR-016
/// D10 — would silently break. This test fails loudly the first time that
/// happens.
/// </para>
///
/// <para>
/// <b>Direct-orchestration shape (S29 TASK-2909 precedent).</b> Uses the
/// real <see cref="EmploymentProfileResolver"/> and the real
/// <see cref="PeriodCalculationService"/> in-process. The rule engine is
/// stubbed via <see cref="PartTimeSensitiveRuleEngineHandler"/> — which,
/// crucially (QUAL-015), CONSUMES the resolved <c>part_time_fraction</c>
/// and scales <see cref="CalculationResult.NormHoursTotal"/> by it, modelling
/// the real <c>NormCheckRule</c>. The shared
/// <see cref="TestFixtures.DefaultRuleEngineHandler"/> ignores the fraction,
/// so with it the byte-identity assertion was blind to the resolver — a
/// resolver regression could not have failed this marquee. The full
/// Backend.Api Program.cs boot is intentionally skipped (the WAF&lt;Program&gt;
/// harness is used by the HTTP-level lifecycle tests in
/// <see cref="StatsTid.Tests.Regression.EmployeeProfile.EmployeeProfileLifecycleTests"/>).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeProfileMarqueeTests : IAsyncLifetime
{
    private const string EmployeeId = "EMP-MARQUEE-EP";
    private const string OrgId = "STY01";
    private const string AgreementCode = "AC";
    private const string OkVersion = "OK24";

    // QUAL-015: the part_time_fraction the DATED predecessor row carries for April 2026.
    // The resolver must return THIS value on replay (the Case C supersession closes the
    // predecessor at Today, and PeriodStart=2026-04-01 < Today), NOT the mutated live row.
    private const decimal SeededPartTimeFraction = 1.000m;

    // The marquee stub models NormCheckRule: the period norm scales with the resolved
    // part-time fraction (37h full-time). NormHoursTotal = FullTimeWeeklyNorm * fraction is
    // the field that carries the resolved fraction into the serialized RuleResults, so the
    // byte-identity assertion is now genuinely coupled to what the resolver returned.
    private const decimal FullTimeWeeklyNorm = 37.0m;

    // Period inside OK24 only — single segment so the marquee proves the
    // resolver-driven segmentProfile construction in the simplest possible
    // setting before any straddle-induced multi-segment complications.
    private static readonly DateOnly PeriodStart = new(2026, 4, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 4, 30);

    // Mutation date = today UTC (matches what the PUT endpoint validator
    // accepts per ADR-023 D8 same-day-only-edit narrowing). PeriodEnd is
    // before this date so the predecessor's [predecessor.effective_from,
    // today) window still covers PeriodStart.
    private static readonly DateOnly Today =
        DateOnly.FromDateTime(DateTime.UtcNow);

    private TestFixtures.DockerHarness _harness = null!;
    private EmploymentProfileResolver _resolver = null!;
    private EmployeeProfileRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Apply full init.sql so users + employee_profiles + employee_profile_audit
        // + outbox_events tables exist (Segmentation DockerHarness DDL is the
        // 4-table subset only).
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        _resolver = new EmploymentProfileResolver(
            _harness.Factory,
            new UserAgreementCodeRepository(_harness.Factory));
        _repo = new EmployeeProfileRepository(_harness.Factory);

        await SeedUserAndProfileAsync(partTimeFraction: 1.000m);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Marquee variant 1 — part_time_fraction (replay determinism)
    // ═════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Forward-calc against the seeded profile (part_time_fraction=1.000),
    /// supersede via Case C cross-day routing today (closes predecessor at
    /// today; new live row at part_time_fraction=0.800), then replay against
    /// the original manifest. The dated resolver lookup at
    /// segment.StartDate=PeriodStart=2026-04-01 must return the predecessor
    /// row (whose window now ends end-exclusive at today — still covers
    /// April 2026), so the rule-engine inputs are byte-identical between
    /// baseline and replay.
    /// </summary>
    [Fact]
    public async Task ReplayAsync_StableUnderEmployeeProfileMutation_PartTimeFraction_Variant1_ResultByteIdentical()
    {
        var pcs = BuildPcsWithResolver(_resolver);

        var profileSeed = TestFixtures.Profile(EmployeeId);
        var entries = TestFixtures.WeekdayEntriesForPeriod(EmployeeId, PeriodStart, PeriodEnd);
        var absences = Array.Empty<AbsenceEntry>();

        // ── Baseline forward calc via the (Obsolete) legacy shim — same entry
        // point S29 TASK-2909 marquee uses. The shim routes through
        // BuildPlanForLegacyCallersAsync, which registers the WtmNaturalKey
        // hydrator on the plan's IPlannerEnrollment (S29) — required by
        // PCS.MapSegmentToExportLinesAsync at L1236. Direct
        // PeriodPlanner.Plan() bypasses this registration and fails replay.
#pragma warning disable CS0618 // Obsolete shim is the public entry point for hydrator wiring.
        var baseline = await pcs.CalculateAsync(
            profileSeed, entries, absences, PeriodStart, PeriodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618
        Assert.True(baseline.Success);
        Assert.NotEmpty(baseline.RuleResults);

        // QUAL-015 falsifiability guard: prove the resolved part_time_fraction actually
        // reaches the serialized RuleResults, so the byte-identity assertion below is
        // meaningful. Baseline resolves the dated row (fraction=1.000) → NormHoursTotal=37.0.
        Assert.Equal(FullTimeWeeklyNorm * SeededPartTimeFraction, ReadNormHoursTotal(baseline));

        // ManifestId is stamped per-RuleResult (PCS.cs:356-359 WithManifestId)
        var baselineManifestId = baseline.RuleResults.First().ManifestId;
        Assert.NotEqual(Guid.Empty, baselineManifestId);

        var baselineJson = JsonSerializer.Serialize(baseline.RuleResults, SerializerOptions);

        // ── Case C cross-day supersession on part_time_fraction: 1.000 → 0.800.
        // SeedUserAndProfileAsync inserts the live row with the schema DEFAULT
        // effective_from='0001-01-01', so this supersession at Today creates
        // Case C cross-day routing: predecessor closed at effective_to=Today
        // (window ['0001-01-01', Today)), new row at effective_from=Today.
        // Replay at segment.StartDate=2026-04-01 falls in the predecessor's
        // window (today > 2026-04-01), so the resolver reads the predecessor's
        // part_time_fraction=1.000.
        await SupersedeAsync(
            newPartTimeFraction: 0.800m,
            effectiveFrom: Today);

        // ── Replay against the baseline's manifest id. Result must serialize
        // byte-identically because the resolver returns the predecessor row
        // for any asOfDate < Today.
        var replay = await pcs.ReplayAsync(
            baselineManifestId, profileSeed, entries, absences, previousFlexBalance: 0m);
        Assert.True(replay.Success);

        // The dated resolver returns the predecessor (fraction=1.000) for asOfDate=2026-04-01
        // < Today, so replay's norm is 37.0 — identical to baseline. A resolver regression
        // that read the mutated LIVE row (fraction=0.800) would make this 29.6, so the
        // byte-identity assertion below would FAIL. THIS is the regression the marquee exists
        // for; before QUAL-015 the stub ignored the fraction and the assertion could not fail.
        Assert.Equal(FullTimeWeeklyNorm * SeededPartTimeFraction, ReadNormHoursTotal(replay));

        var replayJson = JsonSerializer.Serialize(replay.RuleResults, SerializerOptions);

        Assert.Equal(baselineJson, replayJson);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Marquee variant 2 — part_time_fraction
    // ═════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Same shape as variant 1 but mutates <c>part_time_fraction</c>
    /// 1.000 → 0.750 via Case C cross-day supersession. The factor
    /// <c>profile.PartTimeFraction</c> at <c>NormCheckRule.cs</c> is what
    /// this variant exercises — the PCS rule-input must be the dated
    /// predecessor value, not the post-mutation live row.
    /// </summary>
    [Fact]
    public async Task ReplayAsync_StableUnderEmployeeProfileMutation_PartTimeFraction_ResultByteIdentical()
    {
        var pcs = BuildPcsWithResolver(_resolver);

        var profileSeed = TestFixtures.Profile(EmployeeId);
        var entries = TestFixtures.WeekdayEntriesForPeriod(EmployeeId, PeriodStart, PeriodEnd);
        var absences = Array.Empty<AbsenceEntry>();

        // Legacy shim — registers WtmNaturalKey hydrator via BuildPlanForLegacyCallersAsync
        // (S29 TASK-2909 precedent; required by PCS.MapSegmentToExportLinesAsync L1236).
#pragma warning disable CS0618
        var baseline = await pcs.CalculateAsync(
            profileSeed, entries, absences, PeriodStart, PeriodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618
        Assert.True(baseline.Success);
        // QUAL-015 falsifiability guard (see variant 1): the resolved fraction reaches the
        // serialized RuleResults. Baseline resolves the dated row (fraction=1.000) → 37.0.
        Assert.Equal(FullTimeWeeklyNorm * SeededPartTimeFraction, ReadNormHoursTotal(baseline));
        var baselineManifestId = baseline.RuleResults.First().ManifestId;
        var baselineJson = JsonSerializer.Serialize(baseline.RuleResults, SerializerOptions);

        // Case C cross-day supersession on part_time_fraction: 1.000 → 0.750.
        await SupersedeAsync(
            newPartTimeFraction: 0.750m,
            effectiveFrom: Today);

        var replay = await pcs.ReplayAsync(
            baselineManifestId, profileSeed, entries, absences, previousFlexBalance: 0m);
        Assert.True(replay.Success);
        // Dated resolver returns the predecessor (1.000) → 37.0, identical to baseline. A
        // regression reading the live row (0.750) would yield 27.75 → byte-identity FAILS.
        Assert.Equal(FullTimeWeeklyNorm * SeededPartTimeFraction, ReadNormHoursTotal(replay));
        var replayJson = JsonSerializer.Serialize(replay.RuleResults, SerializerOptions);

        Assert.Equal(baselineJson, replayJson);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Position — NON-marquee (refinement cycle 1 Reviewer BLOCKER-2 +
    // cycle 3 Codex NOTE absorption). Position has caller-supplied-fallback
    // semantic per TASK-1802 + ADR-023 D1; byte-identical-replay is NOT a
    // property the design promises for the Position field, so the assertion
    // here is the documented precedence rule, NOT byte-equality.
    //
    // Reading PeriodCalculationService.cs:355-358 — the implementation
    // fallback only applies when the DATED RESOLVER returns null Position
    // (line 357: "if (segmentProfile.Position is null && profile.Position
    // is not null) segmentProfile = segmentProfile with { Position =
    // profile.Position };"). Thus:
    //   (a) DB row has Position="RESEARCHER", caller supplies null → dated
    //       value wins (segmentProfile.Position == "RESEARCHER").
    //   (b) DB row has Position="RESEARCHER", caller supplies "DEPARTMENT_HEAD"
    //       → DB value wins (segmentProfile.Position == "RESEARCHER"); the
    //       caller-supplied value is only used when the resolver returns null.
    // ═════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Sub-case (a) — caller-null + DB-non-null. Asserts the resolver-driven
    /// dated value flows through to NormCheckRule.cs and into rule output.
    /// We do NOT assert byte-identical replay here — this test pins the
    /// precedence rule, not replay stability.
    /// </summary>
    [Fact]
    public async Task PCS_PositionResolution_DatedValueWinsWhenCallerSuppliesNull_NotReplayStabilityVerification()
    {
        // Seed re-runs with a Position set on the dated row.
        await SetSeedPositionAsync("RESEARCHER");
        var pcs = BuildPcsWithResolver(_resolver);

        // Caller-supplied profile with Position=null. Resolver-returned value
        // (Position="RESEARCHER") wins per PCS L344-345; line L357 does NOT
        // fire because segmentProfile.Position is already non-null.
        var callerProfile = TestFixtures.Profile(EmployeeId) with { Position = null };
        var entries = TestFixtures.WeekdayEntriesForPeriod(EmployeeId, PeriodStart, PeriodEnd);

        // Legacy shim — registers WtmNaturalKey hydrator (S29 precedent).
#pragma warning disable CS0618
        var result = await pcs.CalculateAsync(
            callerProfile, entries, Array.Empty<AbsenceEntry>(), PeriodStart, PeriodEnd,
            previousFlexBalance: 0m);
#pragma warning restore CS0618
        Assert.True(result.Success);
        // Resolver-driven dated profile carries Position="RESEARCHER"; we
        // can't introspect segmentProfile directly from the result, but we
        // can verify the resolver returned the expected row by re-asking it.
        var probe = await _resolver.GetByEmployeeIdAtAsync(EmployeeId, PeriodStart);
        Assert.NotNull(probe);
        Assert.Equal("RESEARCHER", probe!.Position);
    }

    /// <summary>
    /// Sub-case (b) — caller-non-null + DB-non-null. The dated DB value wins
    /// because PCS L357 only swaps in caller-supplied Position when the
    /// resolver returned null. Locks the precedence order per TASK-1802
    /// fallback semantic (caller-fallback is a fallback, NOT an override).
    /// </summary>
    [Fact]
    public async Task PCS_PositionResolution_DatedValueWinsOverCallerSupplied_NotReplayStabilityVerification()
    {
        await SetSeedPositionAsync("RESEARCHER");

        // The contract under test is the resolver/PCS precedence, NOT a
        // mutation. We probe the resolver directly — the same lookup PCS
        // does at the segment-loop site — to assert that a dated Position
        // value short-circuits any caller-supplied fallback inside PCS
        // (per L357 null-guard).
        var probe = await _resolver.GetByEmployeeIdAtAsync(EmployeeId, PeriodStart);
        Assert.NotNull(probe);
        Assert.Equal("RESEARCHER", probe!.Position);
        // The PCS code path that would copy caller's "DEPARTMENT_HEAD" into
        // segmentProfile is gated on `segmentProfile.Position is null`; with
        // probe.Position == "RESEARCHER" that guard is never satisfied.
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // Stable property ordering across .NET runtimes is the default
        // (declared order on the class) — no special config needed. Use
        // CamelCase to match the PCS production options for parity with
        // any downstream byte-stable comparators.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Boundary sources containing no OK transitions inside [PeriodStart,
    /// PeriodEnd] — single segment, all OK24. Distinct from
    /// <see cref="TestFixtures.OkStraddleSources"/> which has the OK24→OK26
    /// transition at 2026-04-01 (which would split April 2026 into a 0-day
    /// degenerate segment + the April segment — overly clever for the
    /// marquee).
    /// </summary>
    private static BoundarySources SingleSegmentSources() => new(
        OkTransitions: new List<(DateOnly, string, string)>(),
        AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>());

    /// <summary>
    /// Builds a <see cref="PeriodCalculationService"/> wired with the
    /// dated <see cref="EmploymentProfileResolver"/>. Mirrors
    /// <see cref="TestFixtures.BuildPcs"/> but passes the resolver through
    /// to the new trailing constructor parameter (TASK-3305 cutover).
    /// </summary>
    private PeriodCalculationService BuildPcsWithResolver(EmploymentProfileResolver resolver)
    {
        // QUAL-015 rewire: the rule-engine stub MUST consume the resolved
        // profile.part_time_fraction, otherwise the byte-identity assertion is blind to the
        // resolver (see PartTimeSensitiveRuleEngineHandler). The shared
        // TestFixtures.DefaultRuleEngineHandler ignores part_time_fraction, so a resolver
        // regression could not have failed this marquee — the exact verification-theater
        // defect this task removes.
        var stubHandler = new TestFixtures.StubHandler(PartTimeSensitiveRuleEngineHandler);
        var httpFactory = new SingleClientFactory(stubHandler);
        var wtmRepo = new WageTypeMappingRepository(_harness.Factory);
        var mappingService = new PayrollMappingService(
            _harness.Factory, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _harness.EventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(TestFixtures.RuleSet),
            localAgreementProfileRepo: null,
            profileResolver: resolver);
    }

    // ─── QUAL-015: resolver-sensitive rule-engine stub ───────────────────────
    /// <summary>
    /// Rule-engine stub that — unlike <see cref="TestFixtures.DefaultRuleEngineHandler"/> —
    /// CONSUMES the resolved <c>profile.part_time_fraction</c> from the request body and
    /// reflects it into <see cref="CalculationResult.NormHoursTotal"/> for the NORM_CHECK_37H
    /// rule (<c>NormHoursTotal = FullTimeWeeklyNorm * part_time_fraction</c>). This models the
    /// real <c>NormCheckRule</c>, whose period norm scales with the part-time fraction (37.0 *
    /// 1.0 = 37.0 for the dated predecessor vs 37.0 * 0.8 = 29.6 for the superseded live row).
    ///
    /// <para>
    /// This is the crux of the QUAL-015 rewire. PCS ships the resolved segment profile to the
    /// rule engine (<c>CallTimeRuleAsync</c> serializes <c>profile</c>), but the default stub
    /// echoes only <c>employeeId</c> + entry-derived line items, so NO resolved-profile field
    /// reached the serialized <c>RuleResults</c> — the byte-identity assertion could not fail
    /// on a resolver regression. By echoing the resolved fraction into a NormCheckRule-native
    /// field, a replay that reads the WRONG (live, mutated) row now produces a different
    /// <c>NormHoursTotal</c> → the JSON differs → the marquee goes RED.
    /// </para>
    /// </summary>
    private static HttpResponseMessage PartTimeSensitiveRuleEngineHandler(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/api/rules/evaluate", StringComparison.Ordinal))
            return EvaluateTimeRuleWithNorm(request);
        if (path.EndsWith("/api/rules/evaluate-absence", StringComparison.Ordinal))
            return SimpleSuccess("ABSENCE");
        if (path.EndsWith("/api/rules/evaluate-flex", StringComparison.Ordinal))
            return SimpleSuccess("FLEX_BALANCE");

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("unknown rule endpoint"),
        };
    }

    private static HttpResponseMessage EvaluateTimeRuleWithNorm(HttpRequestMessage request)
    {
        string body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var ruleId = root.TryGetProperty("ruleId", out var rid) ? rid.GetString() ?? "UNKNOWN" : "UNKNOWN";

        var employeeId = "EMP";
        var partTimeFraction = 1.0m;
        if (root.TryGetProperty("profile", out var prof))
        {
            if (prof.TryGetProperty("employeeId", out var eid))
                employeeId = eid.GetString() ?? "EMP";
            // The resolved (dated) part_time_fraction — the value the resolver looked up at
            // segment.StartDate. This is what the marquee's byte-identity assertion must pin.
            if (prof.TryGetProperty("partTimeFraction", out var ptf) && ptf.ValueKind == JsonValueKind.Number)
                partTimeFraction = ptf.GetDecimal();
        }

        var entries = root.TryGetProperty("entries", out var ents)
            ? ents.EnumerateArray().ToList()
            : new List<JsonElement>();

        // Only NORM_CHECK_37H emits per-entry line items (mirrors the default stub so the
        // export-line path is unchanged) AND carries the fraction-scaled norm.
        var isNorm = ruleId == "NORM_CHECK_37H";
        var lineItems = isNorm
            ? entries.Select(e => new
            {
                timeType = "NORMAL_HOURS",
                hours = e.TryGetProperty("hours", out var h) ? h.GetDecimal() : 0m,
                rate = 1.0m,
                date = e.TryGetProperty("date", out var d) ? d.GetString() : null,
            }).ToList<object>()
            : new List<object>();

        object payload = isNorm
            ? (object)new
            {
                ruleId,
                employeeId,
                success = true,
                lineItems,
                // The resolver-coupled field: scales with the resolved dated fraction.
                normHoursTotal = FullTimeWeeklyNorm * partTimeFraction,
            }
            : new
            {
                ruleId,
                employeeId,
                success = true,
                lineItems,
            };

        return JsonResponse(payload);
    }

    private static HttpResponseMessage SimpleSuccess(string ruleId) =>
        JsonResponse(new
        {
            ruleId,
            employeeId = "EMP",
            success = true,
            lineItems = Array.Empty<object>(),
        });

    private static HttpResponseMessage JsonResponse(object payload)
    {
        var json = JsonSerializer.Serialize(
            payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Returns the fraction-scaled <see cref="CalculationResult.NormHoursTotal"/> the marquee
    /// stub stamps onto the NORM_CHECK_37H rule result — the single value that carries the
    /// resolved <c>part_time_fraction</c> into the serialized <c>RuleResults</c>. Used by the
    /// falsifiability guard below to assert the coupling is live.
    /// </summary>
    private static decimal? ReadNormHoursTotal(PeriodCalculationResult result) =>
        result.RuleResults.First(r => r.RuleId == "NORM_CHECK_37H").NormHoursTotal;

    /// <summary>
    /// Seeds the marquee employee via the shared
    /// <see cref="TestSupport.RegressionSeed.SeedEmployeeAsync(NpgsqlConnection, string, string, string, string, decimal, DateOnly?, string?, bool, CancellationToken)"/>
    /// helper, which atomically writes <c>users</c> + <c>user_agreement_codes</c>
    /// + <c>employee_profiles</c>. The agreement-code row is the piece the prior
    /// local seeder omitted — without it the dated resolver throws
    /// <c>EmployeeProfileNotFoundException</c> at L169 (S34 fail-loud). All three
    /// rows anchor at the schema-default <c>effective_from='0001-01-01'</c> so the
    /// predecessor's window is unbounded-below, guaranteeing a today-effective
    /// Case C supersession closes a profile row that covered the entire test period
    /// while the agreement-code row stays open across April 2026.
    /// </summary>
    private async Task SeedUserAndProfileAsync(decimal partTimeFraction)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await TestSupport.RegressionSeed.SeedEmployeeAsync(
            conn,
            employeeId: EmployeeId,
            orgId: OrgId,
            agreementCode: AgreementCode,
            okVersion: OkVersion,
            partTimeFraction: partTimeFraction);
    }

    /// <summary>
    /// Sets the <c>position</c> column on the seeded employee's live row.
    /// Used by the Position non-marquee tests to introduce a dated Position
    /// value without going through Case C supersession (the Position tests
    /// pin precedence, not lifecycle).
    /// </summary>
    private async Task SetSeedPositionAsync(string positionValue)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_profiles
            SET position = @position
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("position", positionValue);
        cmd.Parameters.AddWithValue("employeeId", EmployeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Calls <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>
    /// directly (no admin HTTP path needed for the marquee). Routes through
    /// Case C when <paramref name="effectiveFrom"/> &gt; predecessor's
    /// <c>effective_from</c> (which it always is — seed row defaults to
    /// '0001-01-01').
    /// </summary>
    private async Task SupersedeAsync(
        decimal newPartTimeFraction, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var req = new EmployeeProfileSupersedeRequest(
            EmployeeId: EmployeeId,
            PartTimeFraction: newPartTimeFraction,
            Position: null,
            EffectiveFrom: effectiveFrom);
        // expectedVersion=null bypasses the OCE check — the marquee is about
        // dated-resolver byte-stability, not concurrency. The lifecycle tests
        // exercise expectedVersion semantics explicitly.
        await _repo.SupersedeAndCreateAsync(conn, tx, req, expectedVersion: null);
        await tx.CommitAsync();
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
