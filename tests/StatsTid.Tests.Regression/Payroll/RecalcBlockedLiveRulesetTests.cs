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
/// S144 / TASK-14405 (TASK-14403's definition of done, service level), re-expected at S145 Stage B (TASK-14522-T,
/// refinement B2) — a mid-month change of an employee's AGREEMENT CODE is a segment boundary the payroll
/// service can SEE, and, since Stage B, the LIVE rule set PAYS such a month part by part instead of refusing it.
///
/// <para>
/// <b>The story, in plain language.</b> An employee is employed all of March 2026. On Monday 16 March
/// their agreement code changes (say from HK to AC). Before S144 the calculation never looked at
/// <c>user_agreement_codes</c> for boundaries, so March was one segment under one agreement and
/// payroll got wrong lines for half the month — with no warning. S144 made the service hydrate the
/// agreement-code dates into the planner; the live rule set then could not evaluate its whole-window
/// rules in two pieces, so the calculation THREW a structured split refusal naming
/// <c>AgreementCodeChange</c>, and nothing was written. S145 Stage B lifted that refusal (QUAL-149):
/// payroll now hands the planner only the six rules it runs, and the three of them that read a whole
/// week or period are timeline-aware, so the month plans into two parts and every line is mapped under
/// the wage-type codes of the part it falls in — 1–15 March under HK's mapping, 16–31 March under AC's
/// (QUAL-150). The correction commits, and its audit event links the new manifest, which carries both parts.
/// (An agreement change in the MIDDLE of a week, say on a Wednesday, still refuses — the week check, pinned
/// in the host suites.)
/// </para>
///
/// <para>
/// <b>Harness contract (M-14).</b> The three facts and the straddle-safe fact build
/// <see cref="PeriodCalculationService"/> with the NEW optional constructor parameter
/// <c>userAgreementCodeRepo</c> set to a real <see cref="UserAgreementCodeRepository"/> over the
/// harness factory. Without it the hydration is skipped regardless of the production code, the
/// service sees one segment, and the facts could not detect mutation M-14 (hydration dropped);
/// the parameter is what makes them exercise the wiring.
/// </para>
///
/// <para>
/// <b>Distinct codes (S145 B2).</b> The successor mapping seeds a distinct, test-only wage-type code
/// (<see cref="AcNormalHoursWageType"/>) for AC's NORMAL_HOURS, so the two parts' codes differ and a
/// per-part key is observable in the exported lines.
/// </para>
///
/// <para>
/// Live set = <c>new RuleRegistry().GetAll()</c> (three AlignedWindow rules, NORM_CHECK_WEEKLY first since S145 B1;
/// since S145 B2 payroll hands the planner only the six rules it evaluates, so none of the three reaches planning).
/// All facts are Docker-gated: CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RecalcBlockedLiveRulesetTests : IAsyncLifetime
{
    private const string OrgId = "STY_S144_BLOCKED";
    private const string InitialAgreementCode = "HK";
    private const string SuccessorAgreementCode = "AC";
    private const string OkVersion = "OK24";

    /// <summary>S145 B2: a distinct, test-only wage-type code for AC's NORMAL_HOURS (HK's is SLS_0110), so the
    /// two parts of a split month map under different, observable codes.</summary>
    private const string AcNormalHoursWageType = "SLS_TEST_AC_0110";
    private const string HkNormalHoursWageType = "SLS_0110";

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
    // 7a. /recalculate path, live set, Monday agreement-code change → recalculates per part
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S145 B2: was <c>Recalculate_MidMonthAgreementCodeChange_LiveSet_RefusesWithAgreementCodeCause_WritesNothing</c></b>
    /// (S144 refused the month; Stage B pays it). (7a) The correction service (<c>RecalculateAsync</c>, as
    /// <c>/api/payroll/recalculate</c> drives it) recalculates a month with an agreement-code change on
    /// Monday 16 March: the correction COMMITS; the lines dated 1–15 March map under HK's mapping
    /// (<see cref="HkNormalHoursWageType"/>) and those dated 16–31 March under AC's
    /// (<see cref="AcNormalHoursWageType"/>, a distinct test-only code). One <c>RetroactiveCorrectionRequested</c>
    /// event is written and it LINKS the new manifest (R-57): a second manifest, with two EMPLOYED segments and
    /// <c>AgreementCodeChange</c>, whose frozen pieces hold both agreements, HK then AC. March 2026 opens on a
    /// Sunday, so its opening week starts on Mon 23 Feb and the frozen pieces ALSO hold the context piece for
    /// 23–28 Feb (HK), which comes first: three pieces, the context piece first, then 1–15 March (HK) and
    /// 16–31 March (AC).
    ///
    /// <para>
    /// The event's <c>AgreementCode</c> names the FIRST part of a split month (HK), by design (R-11 / R-12): the
    /// one-line summary on the audit event names the first agreement only, and an auditor follows the event's
    /// manifest id to see both parts. Adding list fields would change the event schema, which is outside
    /// Stage B, so the gap is registered instead (QUAL-211).
    /// </para>
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). MB-2 (TASK-14528: every EMPLOYED
    /// segment hydrated with the FIRST EMPLOYED segment's wage-type key): the 16–31 March lines map under HK's
    /// code, so the AC-code assertion trips. MB-7 (the evaluated-set filter disabled): the planner sees
    /// NORM_CHECK_WEEKLY as AlignedWindow again and <c>RecalculateAsync</c> throws
    /// <see cref="PlannerInvariantViolation"/> before any assertion. Mutation M-14 (hydration skipped) leaves one
    /// segment, so the two-segment and AC-code assertions trip.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recalculate_MondayAgreementCodeChange_LiveSet_RecalculatesWithPerSegmentCodes() // S145 B2: was Recalculate_MidMonthAgreementCodeChange_LiveSet_RefusesWithAgreementCodeCause_WritesNothing
    {
        const string employeeId = "EMP-S144-LIVE-AGR-RECALC";
        await SeedEmployeeAsync(employeeId);
        // S145 B2: AC's NORMAL_HOURS maps to a distinct test-only code, so the per-part key is observable.
        await SeedSuccessorWageTypeMappingAsync();
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        // The ORIGINAL export happens BEFORE the agreement change exists: one segment, succeeds.
        var first = await RunShimAsync(BuildPcs(HoursOf(entries)), employeeId);
        Assert.True(first.Result.Success, first.Result.ErrorMessage);
        await PersistFirstExportRecordAsync(employeeId, first.Result.ExportLines);
        Assert.Equal(1, await CountManifestsAsync(employeeId));

        // HR records an agreement change effective Monday 16 March, afterwards.
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var service = BuildCorrectionService(BuildPcs(HoursOf(entries)));
        var result = await service.RecalculateAsync(
            Profile(employeeId),
            periodStart: Mar01, periodEnd: Mar31, previousFlexBalance: 0m,
            reason: "S145 B2 pin — agreement change recorded after export", actorId: "hr-s145");

        // S145 B2: the correction commits.
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, await CountOutboxAsync("RetroactiveCorrectionRequested", employeeId));
        Assert.Equal(2, await CountManifestsAsync(employeeId));

        // S145 B2: the exported lines map per part — 1–15 March under HK's code, 16–31 March under AC's.
        var linesAfterJson = await ReadCurrentEffectiveLinesAsync(employeeId);
        var lines = PayrollExportManifest.Deserialize(linesAfterJson);
        Assert.Equal(entries.Count, lines.Count);
        var hkLines = lines.Where(l => l.PeriodStart == Mar01).ToList();
        var acLines = lines.Where(l => l.PeriodStart == Mar16).ToList();
        Assert.Equal(entries.Count, hkLines.Count + acLines.Count);
        Assert.Equal(entries.Count(e => e.Date < Mar16), hkLines.Count);
        Assert.Equal(entries.Count(e => e.Date >= Mar16), acLines.Count);
        Assert.All(hkLines, l => Assert.Equal(HkNormalHoursWageType, l.WageType));
        Assert.All(hkLines, l => Assert.Equal(Mar16.AddDays(-1), l.PeriodEnd));
        Assert.All(acLines, l => Assert.Equal(AcNormalHoursWageType, l.WageType));
        Assert.All(acLines, l => Assert.Equal(Mar31, l.PeriodEnd));

        // S145 B2 (R-57): the event links the NEW manifest (not the first export's), and that manifest carries both parts.
        using var payload = System.Text.Json.JsonDocument.Parse(await ReadCorrectionEventPayloadAsync(employeeId));
        var linked = payload.RootElement.GetProperty("manifestId").GetGuid();
        Assert.NotEqual(Guid.Empty, linked);
        Assert.NotEqual(first.ManifestId, linked);
        // The summary label names the FIRST part of a split month (R-11 / R-12, QUAL-211).
        Assert.Equal(InitialAgreementCode, payload.RootElement.GetProperty("agreementCode").GetString());

        var manifest = await LoadManifestAsync(linked);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.All(manifest.Segments, s => Assert.False(s.TryGetProperty("employmentStatus", out _)));
        Assert.Contains("AgreementCodeChange", manifest.BoundaryCauseSummary);

        // Three frozen pieces: the context piece (23–28 Feb, HK) first, then the two in-period parts, HK then AC.
        var pieces = await ReadFrozenPiecesAsync(linked);
        Assert.Equal(3, pieces.Count);
        Assert.Equal((new DateOnly(2026, 2, 23), new DateOnly(2026, 2, 28), InitialAgreementCode), pieces[0]);
        Assert.Equal((Mar01, Mar16.AddDays(-1), InitialAgreementCode), pieces[1]);
        Assert.Equal((Mar16, Mar31, SuccessorAgreementCode), pieces[2]);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7b. A mid-month PROFILE change recalculates too (S145 B2)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S145 B2: was <c>Recalculate_MidMonthProfileChange_LiveSet_StillRefuses_NoRegression</c></b> (the S137
    /// no-regression pin of the refusal, retired with it). (7b) A mid-month <c>employee_profiles</c> change
    /// (a part-time-fraction change while employed all month) recalculates through <c>RecalculateAsync</c>:
    /// the correction COMMITS (one <c>RetroactiveCorrectionRequested</c> event), and the new manifest, which
    /// the event links, holds two EMPLOYED segments and <c>EmployeeProfileChange</c>. A fraction change keeps
    /// the wage-type key, so there is no code assertion here (the code facts are 7a and 7c).
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated: CI-verified). MB-7 (the evaluated-set filter disabled): the planner
    /// refuses the split again, <c>RecalculateAsync</c> throws <see cref="PlannerInvariantViolation"/> before the
    /// first assertion. MB-3 (the Unit mutation of the per-plan lane covering only the first EMPLOYED segment): the
    /// frozen pieces lack the second part, so the piece assertion trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recalculate_MidMonthProfileChange_LiveSet_Recalculates() // S145 B2: was Recalculate_MidMonthProfileChange_LiveSet_StillRefuses_NoRegression
    {
        const string employeeId = "EMP-S144-LIVE-PROFILE-RECALC";
        await SeedEmployeeAsync(employeeId);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        var first = await RunShimAsync(BuildPcs(HoursOf(entries)), employeeId);
        Assert.True(first.Result.Success, first.Result.ErrorMessage);
        await PersistFirstExportRecordAsync(employeeId, first.Result.ExportLines);
        Assert.Equal(1, await CountManifestsAsync(employeeId));

        await SupersedeFractionAsync(employeeId, newFraction: 0.800m, effectiveFrom: Mar16);

        var service = BuildCorrectionService(BuildPcs(HoursOf(entries)));
        var result = await service.RecalculateAsync(
            Profile(employeeId),
            periodStart: Mar01, periodEnd: Mar31, previousFlexBalance: 0m,
            reason: "S145 B2 pin — profile change recorded after export", actorId: "hr-s145");

        // S145 B2: the correction commits (it used to refuse with a split refusal and write nothing).
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, await CountOutboxAsync("RetroactiveCorrectionRequested", employeeId));
        Assert.Equal(2, await CountManifestsAsync(employeeId));

        using var payload = System.Text.Json.JsonDocument.Parse(await ReadCorrectionEventPayloadAsync(employeeId));
        var linked = payload.RootElement.GetProperty("manifestId").GetGuid();
        Assert.NotEqual(first.ManifestId, linked);

        // S145 B2: two EMPLOYED segments, and the cause is the profile change.
        var manifest = await LoadManifestAsync(linked);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.All(manifest.Segments, s => Assert.False(s.TryGetProperty("employmentStatus", out _)));
        Assert.Contains("EmployeeProfileChange", manifest.BoundaryCauseSummary);
        var pieces = await ReadFrozenPiecesAsync(linked);
        Assert.Equal((Mar16, Mar31, InitialAgreementCode), pieces[^1]);
        Assert.Equal((Mar01, Mar16.AddDays(-1), InitialAgreementCode), pieces[^2]);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7c. The planless /calculate-and-export path pays the split month too
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S145 B2: was <c>CalculateWithOutcome_Planless_MidMonthAgreementCodeChange_LiveSet_Refuses_NoManifest</c>.</b>
    /// (7c) The planless <c>CalculateWithOutcomeAsync</c> (what <c>/api/payroll/calculate-and-export</c> calls)
    /// on the Monday agreement-code case, live set: the outcome SUCCEEDS; its manifest records
    /// <c>AgreementCodeChange</c> and two EMPLOYED segments; and the export lines map per part, as 7a
    /// (1–15 March under HK's code, 16–31 March under AC's distinct test-only code).
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). MB-2 (every EMPLOYED segment hydrated
    /// with the first one's key): the 16–31 March lines map under HK's code, tripping the AC-code assertion. MB-7
    /// (the evaluated-set filter disabled): the call throws <see cref="PlannerInvariantViolation"/> before any
    /// assertion. Mutation M-14 (hydration skipped): one segment, so the two-segment assertion trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateWithOutcome_Planless_MondayAgreementCodeChange_LiveSet_CalculatesWithPerSegmentCodes() // S145 B2: was CalculateWithOutcome_Planless_MidMonthAgreementCodeChange_LiveSet_Refuses_NoManifest
    {
        const string employeeId = "EMP-S144-LIVE-AGR-PLANLESS";
        await SeedEmployeeAsync(employeeId);
        await SeedSuccessorWageTypeMappingAsync(); // S145 B2: the distinct AC code
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        var outcome = await RunShimAsync(BuildPcs(HoursOf(entries)), employeeId);

        // S145 B2: the month calculates (it used to throw the split refusal) and persists its one manifest.
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(1, await CountManifestsAsync(employeeId));
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("AgreementCodeChange", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);

        // S145 B2: the lines map per part, under that part's own agreement's code.
        var lines = outcome.Result.ExportLines.ToList();
        Assert.Equal(entries.Count, lines.Count);
        var hkLines = lines.Where(l => l.PeriodStart == Mar01).ToList();
        var acLines = lines.Where(l => l.PeriodStart == Mar16).ToList();
        Assert.Equal(entries.Count(e => e.Date < Mar16), hkLines.Count);
        Assert.Equal(entries.Count(e => e.Date >= Mar16), acLines.Count);
        Assert.All(hkLines, l => Assert.Equal(HkNormalHoursWageType, l.WageType));
        Assert.All(acLines, l => Assert.Equal(AcNormalHoursWageType, l.WageType));
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
    /// change whether or not the rule set later refuses to act on it. Since S145 Stage B the live
    /// path calculates such a month (<c>Recalculate_MondayAgreementCodeChange_LiveSet_RecalculatesWithPerSegmentCodes</c>,
    /// in this class), and this manifest is what says why it has two segments.
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
        // The full schema also seeds an AC NORMAL_HOURS row under HK's code; this upserts it to the
        // successor mapping (a distinct, test-only NORMAL_HOURS code, S145 B2): since S145 A4 / Stage B each EMPLOYED
        // segment maps under its own dated agreement key (QUAL-150), so the second part uses it.
        // This fact asserts the manifest only; the per-part codes are asserted by the recalculate
        // and planless facts above.
        await SeedSuccessorWageTypeMappingAsync();
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31);

        var outcome = await RunShimAsync(BuildPcs(HoursOf(entries), TestFixtures.StraddleSafeRuleSet), employeeId);

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

    /// <summary>S145 B2: the successor (AC) mapping for NORMAL_HOURS with a DISTINCT, test-only wage-type
    /// code (<see cref="AcNormalHoursWageType"/>) — the HK code is SLS_0110 — so the key of each part of a
    /// split month is observable. Upserts the live row (the full schema already seeds an AC row).</summary>
    private async Task SeedSuccessorWageTypeMappingAsync()
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // S145 B2: a distinct test-only AC code, upserted, with an explicit effective_from (was
        // SLS_0110 and ON CONFLICT DO NOTHING, a no-op over the full schema's AC row).
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from)
            VALUES ('NORMAL_HOURS', @wt, @ok, @code, '', NULL, DATE '2020-01-01')
            ON CONFLICT (time_type, ok_version, agreement_code, position) WHERE effective_to IS NULL
            DO UPDATE SET wage_type = EXCLUDED.wage_type
            """, conn);
        cmd.Parameters.AddWithValue("wt", AcNormalHoursWageType);
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

    /// <summary>S145 B2: the <c>RetroactiveCorrectionRequested</c> event payload (the latest one for the employee).</summary>
    private async Task<string> ReadCorrectionEventPayloadAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload::text FROM outbox_events
            WHERE event_type = 'RetroactiveCorrectionRequested' AND stream_id LIKE @s
            ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"retro-correction-{employeeId}-%");
        var payload = await cmd.ExecuteScalarAsync() as string;
        Assert.NotNull(payload);
        return payload!;
    }

    /// <summary>S145 B2: the manifest's frozen pieces (<c>plan_inputs_jsonb.pieces</c>), in stored order:
    /// the context pieces first, then each EMPLOYED segment's piece.</summary>
    private async Task<IReadOnlyList<(DateOnly From, DateOnly To, string AgreementCode)>> ReadFrozenPiecesAsync(Guid manifestId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT plan_inputs_jsonb::text FROM segment_manifests WHERE manifest_id = @id", conn);
        cmd.Parameters.AddWithValue("id", manifestId);
        var json = await cmd.ExecuteScalarAsync() as string;
        Assert.NotNull(json);
        using var doc = System.Text.Json.JsonDocument.Parse(json!);
        return doc.RootElement.GetProperty("pieces").EnumerateArray()
            .Select(p => (
                DateOnly.Parse(p.GetProperty("from").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                DateOnly.Parse(p.GetProperty("to").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                p.GetProperty("agreementCode").GetString()!))
            .ToList();
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
    private PeriodCalculationService BuildPcs(
        IRecordedHoursReader hoursReader, IReadOnlyList<RuleClassification>? classifications = null)
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
            new PostgresSegmentManifestStore(_harness.Factory, _eventStore, _eventStore),
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(classifications ?? LiveSet),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_harness.Factory, userAgreementCodeRepo),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory),
            userAgreementCodeRepo: userAgreementCodeRepo,
            // S145 R14: the in-memory hours reader (payroll reads its own hours).
            hoursReader: hoursReader,
            // S145 R26: this suite is not about the prior-month check; M−1 is treated as APPROVED
            priorPeriodApprovalReader: new TestFixtures.AlwaysApprovedPriorPeriods());
    }

    /// <summary>
    /// S145 R14: the in-memory reader that hands PCS the hours these facts used to pass as call
    /// arguments (time entries only, no absences).
    /// </summary>
    private static InMemoryRecordedHoursReader HoursOf(IEnumerable<TimeEntry> entries) => new(entries);

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
        PeriodCalculationService pcs, string employeeId)
    {
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export and /recalculate).
        return await pcs.CalculateWithOutcomeAsync(
            Profile(employeeId), Mar01, Mar31, previousFlexBalance: 0m);
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
