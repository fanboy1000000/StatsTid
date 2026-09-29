using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S144 / TASK-14405 (TASK-14403's definition of done, service level) — a mid-month change of an
/// employee's AGREEMENT CODE is now a segment boundary the payroll service can SEE, so under the
/// LIVE rule set the month is REFUSED instead of being silently paid under one agreement.
///
/// <para>
/// <b>The story, in plain language.</b> An employee is employed all of March 2026. On 16 March
/// their agreement code changes (say from HK to AC). Before S144 the calculation never looked at
/// <c>user_agreement_codes</c> for boundaries, so March was one segment under one agreement and
/// payroll got wrong lines for half the month — with no warning. Now the service hydrates the
/// agreement-code dates into the planner; the live rule set cannot evaluate its whole-window
/// rules in two pieces, so the calculation THROWS a structured split refusal naming
/// <c>AgreementCodeChange</c>, and — because the correction service plans BEFORE it opens its
/// transaction — nothing is written: the export baseline is untouched, no correction event is
/// queued, no manifest is persisted.
/// </para>
///
/// <para>
/// <b>Harness contract (M-14).</b> The three refusal facts and the straddle-safe fact build
/// <see cref="PeriodCalculationService"/> with the NEW optional constructor parameter
/// <c>userAgreementCodeRepo</c> set to a real <see cref="UserAgreementCodeRepository"/> over the
/// harness factory. Without it the hydration is skipped regardless of the production code, the
/// service sees one segment, and the facts could not detect mutation M-14 (hydration dropped);
/// the parameter is what makes them exercise the wiring.
/// </para>
///
/// <para>
/// Live set = <c>new RuleRegistry().GetAll()</c> (four AlignedWindow rules, OVERTIME_CALC first;
/// a straddle-safe set would refuse nothing). All facts are Docker-gated: CI-verified, never
/// locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RecalcBlockedLiveRulesetTests : IAsyncLifetime
{
    private const string OrgId = "STY_S144_BLOCKED";
    private const string InitialAgreementCode = "HK";
    private const string SuccessorAgreementCode = "AC";
    private const string OkVersion = "OK24";

    // March 2026 sits entirely on the OK24 side of the OK24→OK26 transition (2026-04-01), so the
    // ONLY interior boundary in every fact below is the one the fact seeds. 16 March is a Monday.
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);
    private const int Year = 2026;
    private const int Month = 3;

    private static readonly IReadOnlyList<RuleClassification> LiveSet = new RuleRegistry().GetAll();

    private TestFixtures.DockerHarness _harness = null!;
    private PostgresEventStore _eventStore = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        _eventStore = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("payroll"));
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7a. /recalculate path, live set, agreement-code change → refusal, nothing written
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (7a) The correction service (<c>RecalculateAsync</c>, as <c>/api/payroll/recalculate</c>
    /// drives it) refuses a month with a mid-month agreement-code change: a
    /// <see cref="PlannerInvariantViolation"/> that is a split refusal naming
    /// <see cref="BoundaryCause.AgreementCodeChange"/>. The employee is employed all month with NO
    /// profile change and no employment edge, so the agreement-code date is the only interior
    /// boundary. Nothing is written: <c>payroll_export_records.current_effective_lines</c> is
    /// byte-for-byte unchanged, no <c>RetroactiveCorrectionRequested</c> event, and no new
    /// <c>segment_manifests</c> row (the count stays at the ONE row the original export wrote).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): mutation M-14 —
    /// <c>PeriodCalculationService.BuildPlanForLegacyCallersAsync</c> skips the
    /// <c>AgreementCodeEffectiveDates</c> hydration (passes <c>null</c>). The planner sees one
    /// segment, no refusal is thrown, the correction writes lines, and
    /// <c>Assert.ThrowsAsync&lt;PlannerInvariantViolation&gt;</c> trips. The PCS in this fact is
    /// built WITH <c>userAgreementCodeRepo</c>, so the mutation (not the harness) is what is
    /// detected.
    /// </summary>
    [Fact]
    public async Task Recalculate_MidMonthAgreementCodeChange_LiveSet_RefusesWithAgreementCodeCause_WritesNothing()
    {
        const string employeeId = "EMP-S144-LIVE-AGR-RECALC";
        await SeedEmployeeAsync(employeeId);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        // The ORIGINAL export happens BEFORE the agreement change exists: one segment, succeeds.
        var first = await RunShimAsync(BuildPcs(), employeeId, entries);
        Assert.True(first.Result.Success, first.Result.ErrorMessage);
        await PersistFirstExportRecordAsync(employeeId, first.Result.ExportLines);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId);
        Assert.Equal(1, await CountManifestsAsync(employeeId));

        // HR records an agreement change effective mid-March, afterwards.
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var service = BuildCorrectionService(BuildPcs());
        var ex = await Assert.ThrowsAsync<PlannerInvariantViolation>(() =>
            service.RecalculateAsync(
                Profile(employeeId), entries, Array.Empty<AbsenceEntry>(),
                periodStart: Mar01, periodEnd: Mar31, previousFlexBalance: 0m,
                reason: "S144 pin — agreement change recorded after export", actorId: "hr-s144"));

        Assert.True(ex.IsSplitRefusal);
        Assert.Equal(2, ex.EmployedSegmentCount);
        Assert.Equal(new[] { BoundaryCause.AgreementCodeChange }, ex.InteriorBoundaryCauses);

        // Nothing written.
        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId));
        Assert.Equal(0, await CountOutboxAsync("RetroactiveCorrectionRequested", employeeId));
        Assert.Equal(1, await CountManifestsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7b. The S137 behaviour, unchanged: a mid-month PROFILE change still refuses
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (7b) NO-REGRESSION pin of S137 behaviour — a mid-month <c>employee_profiles</c> change
    /// (a part-time-fraction change while employed all month) still refuses through
    /// <c>RecalculateAsync</c>, now as a structured split refusal naming
    /// <see cref="BoundaryCause.EmployeeProfileChange"/>, and writes nothing. It is the control
    /// that shows S144's new source did not change or shadow the existing one.
    ///
    /// Red conditions: NONE — this fact is GREEN before and after S144 (the planner has refused a
    /// profile-change split since S137), so it has no RED and no mutation. It exists so that a
    /// change to the tie-break or to the hydration cannot silently alter the profile case. (It does
    /// use the S144 members <c>IsSplitRefusal</c> / <c>InteriorBoundaryCauses</c>, so it compiles
    /// only after TASK-14402 merges.) Docker-gated: CI-verified.
    /// </summary>
    [Fact]
    public async Task Recalculate_MidMonthProfileChange_LiveSet_StillRefuses_NoRegression()
    {
        const string employeeId = "EMP-S144-LIVE-PROFILE-RECALC";
        await SeedEmployeeAsync(employeeId);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        var first = await RunShimAsync(BuildPcs(), employeeId, entries);
        Assert.True(first.Result.Success, first.Result.ErrorMessage);
        await PersistFirstExportRecordAsync(employeeId, first.Result.ExportLines);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId);
        Assert.Equal(1, await CountManifestsAsync(employeeId));

        await SupersedeFractionAsync(employeeId, newFraction: 0.800m, effectiveFrom: Mar16);

        var service = BuildCorrectionService(BuildPcs());
        var ex = await Assert.ThrowsAsync<PlannerInvariantViolation>(() =>
            service.RecalculateAsync(
                Profile(employeeId), entries, Array.Empty<AbsenceEntry>(),
                periodStart: Mar01, periodEnd: Mar31, previousFlexBalance: 0m,
                reason: "S144 no-regression pin — profile change", actorId: "hr-s144"));

        Assert.True(ex.IsSplitRefusal);
        Assert.Equal(2, ex.EmployedSegmentCount);
        Assert.Equal(new[] { BoundaryCause.EmployeeProfileChange }, ex.InteriorBoundaryCauses);

        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId));
        Assert.Equal(0, await CountOutboxAsync("RetroactiveCorrectionRequested", employeeId));
        Assert.Equal(1, await CountManifestsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7c. The planless /calculate-and-export path refuses too
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (7c) The planless <c>CalculateWithOutcomeAsync</c> (what <c>/api/payroll/calculate-and-export</c>
    /// calls) on the agreement-code case, live set: it THROWS the split refusal naming
    /// <see cref="BoundaryCause.AgreementCodeChange"/>, and no manifest is persisted for the
    /// employee — the export never starts on a month that cannot be paid correctly.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): mutation M-14 — the
    /// hydration in <c>BuildPlanForLegacyCallersAsync</c> is skipped (both the shim and this
    /// planless overload build their plan through it). The month calculates as one segment,
    /// <c>Assert.ThrowsAsync&lt;PlannerInvariantViolation&gt;</c> trips (and a manifest would be
    /// written). The PCS is built WITH <c>userAgreementCodeRepo</c>.
    /// </summary>
    [Fact]
    public async Task CalculateWithOutcome_Planless_MidMonthAgreementCodeChange_LiveSet_Refuses_NoManifest()
    {
        const string employeeId = "EMP-S144-LIVE-AGR-PLANLESS";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        var ex = await Assert.ThrowsAsync<PlannerInvariantViolation>(() =>
            RunShimAsync(BuildPcs(), employeeId, entries));

        Assert.True(ex.IsSplitRefusal);
        Assert.Equal(2, ex.EmployedSegmentCount);
        Assert.Equal(new[] { BoundaryCause.AgreementCodeChange }, ex.InteriorBoundaryCauses);
        Assert.Equal(0, await CountManifestsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7d. The boundary itself, independent of the refusal (straddle-safe set)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (7d) THE ONE STRADDLE-SAFE FACT. Under <see cref="TestFixtures.StraddleSafeRuleSet"/> (which
    /// can be split, so nothing refuses) the same agreement-code case calculates in two segments and
    /// the persisted manifest's <c>boundary_cause_summary</c> contains <c>AgreementCodeChange</c>.
    /// The date (16 March) has NO coinciding profile change — one cause survives per date, so a
    /// shared date would record the higher-ranked cause and hide the one under test.
    ///
    /// This pins the BOUNDARY independent of the refusal: the planner learns about the agreement
    /// change whether or not the rule set later refuses to act on it. It becomes QUAL-150's
    /// reachable case the day QUAL-149 (the norm/overtime rules' split refusal) lands and the live
    /// set stops refusing — then a calculated month with an agreement change is real, and this
    /// manifest is what says why it has two segments.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): mutation M-14 — the
    /// hydration is skipped. The month is then ONE segment and its manifest's
    /// <c>boundary_cause_summary</c> lacks <c>AgreementCodeChange</c>, tripping
    /// <c>Assert.Contains("AgreementCodeChange", manifest.BoundaryCauseSummary)</c>. The PCS is
    /// built WITH <c>userAgreementCodeRepo</c>.
    /// </summary>
    [Fact]
    public async Task Calculate_MidMonthAgreementCodeChange_StraddleSafe_ManifestRecordsAgreementCodeChange()
    {
        const string employeeId = "EMP-S144-STRADDLE-AGR";
        await SeedEmployeeAsync(employeeId);
        // The seeded wage-type mappings cover HK only; the successor agreement needs its own row
        // so the second segment's export lines can be mapped.
        await SeedSuccessorWageTypeMappingAsync();
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        var outcome = await RunShimAsync(BuildPcs(TestFixtures.StraddleSafeRuleSet), employeeId, entries);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("AgreementCodeChange", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);
    }

    // ─── Seeding ─────────────────────────────────────────────────────────

    private async Task SeedEmployeeAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // users + employee_profiles + user_agreement_codes (history-covering '0001-01-01' rows) +
        // the organizations FK parent. Employment window both-NULL (unbounded): employed all month.
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, InitialAgreementCode, OkVersion, partTimeFraction: 1.0m);
    }

    /// <summary>Closes the live agreement-code row at <paramref name="effectiveFrom"/> (end-exclusive)
    /// and inserts the successor — written directly so the test controls the dates.</summary>
    private async Task SupersedeAgreementCodeAsync(string employeeId, string newAgreementCode, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var close = new NpgsqlCommand(
            """
            UPDATE user_agreement_codes SET effective_to = @from
            WHERE user_id = @id AND effective_to IS NULL
            """, conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @id, @code, @from, NULL, 2)
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("code", newAgreementCode);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        await tx.CommitAsync();
    }

    /// <summary>Closes the live profile row at <paramref name="effectiveFrom"/> (end-exclusive)
    /// and inserts the successor with a new fraction (same shape as the S137 live-set fact).</summary>
    private async Task SupersedeFractionAsync(string employeeId, decimal newFraction, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var close = new NpgsqlCommand(
            """
            UPDATE employee_profiles SET effective_to = @from
            WHERE employee_id = @id AND effective_to IS NULL
            """, conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @id, @fraction, NULL, @from, NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @id))
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("fraction", newFraction);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        await tx.CommitAsync();
    }

    private async Task SeedSuccessorWageTypeMappingAsync()
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description)
            VALUES ('NORMAL_HOURS', 'SLS_0110', @ok, @code, '', NULL)
            ON CONFLICT (time_type, ok_version, agreement_code, position) WHERE effective_to IS NULL DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("code", SuccessorAgreementCode);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>The FIRST-export record (<c>original_lines == current_effective_lines</c>), written
    /// with the production manifest serializer — the S90 idiom (see
    /// <c>RetroactiveCorrectionWindowTests.PersistFirstExportRecordAsync</c>).</summary>
    private async Task PersistFirstExportRecordAsync(string employeeId, IReadOnlyList<PayrollExportLine> lines)
    {
        var ordered = PayrollExportManifest.OrderLines(lines);
        var json = PayrollExportManifest.Serialize(ordered);
        var hash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source
            ) VALUES (
                @id, NULL, @emp, @year, @month, NOW(),
                @lines::jsonb, @lines::jsonb, @hash, 'CALCULATE_AND_EXPORT'
            )
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", Year);
        cmd.Parameters.AddWithValue("month", Month);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ─── Reads ───────────────────────────────────────────────────────────

    private async Task<string> ReadCurrentEffectiveLinesAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", Year);
        cmd.Parameters.AddWithValue("m", Month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row");
        return reader.GetString(0);
    }

    private async Task<int> CountManifestsAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountOutboxAsync(string eventType, string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE event_type = @t AND event_payload::text LIKE '%' || @e || '%'
            """, conn);
        cmd.Parameters.AddWithValue("t", eventType);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private sealed record PersistedManifest(IReadOnlyList<System.Text.Json.JsonElement> Segments, IReadOnlyList<string> BoundaryCauseSummary);

    private async Task<PersistedManifest> LoadManifestAsync(Guid manifestId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT segments_jsonb::text, boundary_cause_summary
            FROM segment_manifests
            WHERE manifest_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", manifestId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no segment_manifests row for {manifestId}");
        using var doc = System.Text.Json.JsonDocument.Parse(reader.GetString(0));
        var segments = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        var summary = ((string[])reader.GetValue(1)).ToList();
        return new PersistedManifest(segments, summary);
    }

    // ─── Wiring (the production DI shapes, hand-built) ───────────────────

    /// <summary>
    /// The payroll-host PCS shape INCLUDING the S144 optional parameter
    /// <c>userAgreementCodeRepo</c> — a real <see cref="UserAgreementCodeRepository"/> over the
    /// harness factory. Leaving it out would skip the agreement-code hydration whatever the
    /// production code does, so the facts could not detect mutation M-14.
    /// </summary>
    private PeriodCalculationService BuildPcs(IReadOnlyList<RuleClassification>? classifications = null)
    {
        var httpFactory = new SingleClientFactory(new TestFixtures.StubHandler(TestFixtures.DefaultRuleEngineHandler));
        var wtmRepo = new WageTypeMappingRepository(_harness.Factory);
        var mappingService = new PayrollMappingService(
            _harness.Factory, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();
        var userAgreementCodeRepo = new UserAgreementCodeRepository(_harness.Factory);

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _eventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(classifications ?? LiveSet),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_harness.Factory, userAgreementCodeRepo),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory),
            userAgreementCodeRepo: userAgreementCodeRepo);
    }

    private RetroactiveCorrectionService BuildCorrectionService(PeriodCalculationService pcs)
        => new(
            pcs,
            _harness.Factory,
            _eventStore,
            new AuditProjectionRepository(_harness.Factory),
            new RetroactiveCorrectionRequestedAuditMapper(),
            new PayrollExportRecordRepository(_harness.Factory),
            NullLogger<RetroactiveCorrectionService>.Instance);

    private static async Task<PeriodCalculationService.PeriodCalculationOutcome> RunShimAsync(
        PeriodCalculationService pcs, string employeeId, IReadOnlyList<TimeEntry> entries)
    {
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export and /recalculate).
        return await pcs.CalculateWithOutcomeAsync(
            Profile(employeeId), entries, Array.Empty<AbsenceEntry>(), Mar01, Mar31, previousFlexBalance: 0m);
#pragma warning restore CS0618
    }

    private static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = InitialAgreementCode,
        OkVersion = OkVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Copied from the six private twins (promoting one would touch a file outside this
    /// task's scope): an in-memory <see cref="IRuleClassificationProvider"/> over a fixed set.</summary>
    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
