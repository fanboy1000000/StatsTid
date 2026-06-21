using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;
using StatsTid.Tests.Regression.PhaseE;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S90 / TASK-9004 (ADR-034 / B3) — the corrections-manifest semantics: <c>/recalculate</c> reads
/// its diff baseline from <c>payroll_export_records.current_effective_lines</c> (NOT the request
/// body), the baseline EVOLVES so a 2nd correction diffs against the 1st correction's result (no
/// double-count), <c>original_lines</c> stays immutable, and a never-exported month is rejected.
///
/// <para>
/// RED-on-old: pre-S90, <c>RetroactiveCorrectionService.RecalculateAsync</c> REQUIRED the caller to
/// supply <c>previousExportLines</c> and there was no <c>payroll_export_records</c> table to read or
/// to evolve — so the baseline was caller-fixed (original-only) and a 2nd correction double-counted
/// the 1st correction's delta. These tests drive the SAME repository round-trip the service does
/// (read-baseline → update-baseline-to-corrected) and assert the evolving-baseline mechanism; the
/// never-exported rejection is driven through <c>RecalculateAsync</c> directly (it throws BEFORE the
/// rule-engine calc, so no HTTP stand-up is needed — the existing recalc tests likewise avoid the
/// HTTP calc path).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RetroactiveCorrectionManifestTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    private const string OrgId = "ORG_CORRMAN";

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);
        await PayrollExportRecordsTestSchema.ApplyAsync(_harness.ConnectionString);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
              VALUES (@id, 'Correction Manifest Test Org', 'STYRELSE', @path)
              ON CONFLICT DO NOTHING", conn);
        cmd.Parameters.AddWithValue("id", OrgId);
        cmd.Parameters.AddWithValue("path", $"/{OrgId}/");
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ── helpers ──────────────────────────────────────────────────────────────

    private PayrollExportRecordRepository Repo() => new(_harness.Factory);

    private static PayrollExportLine Line(
        string employeeId, int year, int month, decimal hours, decimal amount,
        string wageType = "SLS_0110") => new()
    {
        EmployeeId = employeeId,
        WageType = wageType,
        Hours = hours,
        Amount = amount,
        PeriodStart = new DateOnly(year, month, 1),
        PeriodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
        OkVersion = "OK24",
        SourceTimeType = "NORMAL_HOURS",
    };

    /// <summary>
    /// Seeds a first-export record directly (the TASK-9002 atomic export path is exercised by
    /// <see cref="PayrollExportRecordTests"/>; here we only need the row to exist with
    /// original_lines == current_effective_lines, which is the first-export invariant).
    /// </summary>
    private async Task SeedExportRecordAsync(string employeeId, int year, int month, IReadOnlyList<PayrollExportLine> lines)
    {
        var ordered = PayrollExportManifest.OrderLines(lines);
        var json = PayrollExportManifest.Serialize(ordered);
        var hash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
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
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("month", month);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(string original, string current)> ReadRowAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"SELECT original_lines::text, current_effective_lines::text
              FROM payroll_export_records WHERE employee_id=@e AND year=@y AND month=@m", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1));
    }

    /// <summary>
    /// Drives ONE correction's baseline lifecycle exactly as <c>RecalculateAsync</c> does (B3):
    /// read current_effective_lines as the diff baseline, then UPDATE it to the corrected lines in a
    /// tx. Returns the baseline that was read (the diff input). The pure correction diff is
    /// correctedHours − baselineHours, so the returned baseline IS what determines double-count vs
    /// no-double-count.
    /// </summary>
    private async Task<IReadOnlyList<PayrollExportLine>> ApplyCorrectionAsync(
        string employeeId, int year, int month, IReadOnlyList<PayrollExportLine> correctedLines)
    {
        var repo = Repo();
        var baseline = await repo.TryReadCurrentEffectiveLinesAsync(employeeId, year, month);
        Assert.NotNull(baseline); // a correction requires a prior export

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var rows = await PayrollExportRecordRepository.UpdateCurrentEffectiveLinesAsync(
            conn, tx, employeeId, year, month, correctedLines);
        Assert.Equal(1, rows);
        await tx.CommitAsync();

        return baseline!;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  1. The baseline is READ from the record (not the request body)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Correction_ReadsBaselineFromRecord_NotFromRequestBody()
    {
        const string emp = "EMP_READ";
        var exported = new[] { Line(emp, 2026, 1, hours: 10m, amount: 1000m) };
        await SeedExportRecordAsync(emp, 2026, 1, exported);

        // RED-on-old: pre-S90 the baseline was the caller-supplied previousExportLines (and there was
        // no record to read at all). Now the service reads it from current_effective_lines.
        var baseline = await Repo().TryReadCurrentEffectiveLinesAsync(emp, 2026, 1);

        Assert.NotNull(baseline);
        var line = Assert.Single(baseline!);
        Assert.Equal(10m, line.Hours);
        Assert.Equal(1000m, line.Amount);
        Assert.Equal("SLS_0110", line.WageType);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. THE HEADLINE B3 PROOF — sequential corrections diff against the
    //     UPDATED baseline, NOT the original (no double-count of correction #1).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SequentialCorrections_DiffAgainstUpdatedBaseline_NoDoubleCount()
    {
        const string emp = "EMP_SEQ";
        const int year = 2026, month = 2;

        // First export: 10h.
        await SeedExportRecordAsync(emp, year, month, new[] { Line(emp, year, month, hours: 10m, amount: 1000m) });

        // Correction #1: corrected to 12h. The diff baseline read = the original 10h → delta +2.
        var corrected1 = new[] { Line(emp, year, month, hours: 12m, amount: 1200m) };
        var baseline1 = await ApplyCorrectionAsync(emp, year, month, corrected1);
        var delta1 = corrected1.Sum(l => l.Hours) - baseline1.Sum(l => l.Hours);
        Assert.Equal(10m, baseline1.Sum(l => l.Hours)); // correction #1 diffs against the ORIGINAL
        Assert.Equal(2m, delta1);

        // Correction #2: corrected to 13h. The diff baseline MUST now be correction #1's result (12h),
        // NOT the original (10h). delta2 = 13 − 12 = +1 (correct), NOT 13 − 10 = +3 (the B3 bug).
        var corrected2 = new[] { Line(emp, year, month, hours: 13m, amount: 1300m) };
        var baseline2 = await ApplyCorrectionAsync(emp, year, month, corrected2);
        var delta2 = corrected2.Sum(l => l.Hours) - baseline2.Sum(l => l.Hours);

        Assert.Equal(12m, baseline2.Sum(l => l.Hours)); // ← the load-bearing assertion: baseline EVOLVED to #1's result
        Assert.Equal(1m, delta2);                       // ← +1 (no double-count), NOT +3
        Assert.NotEqual(3m, delta2);                    // explicit: the original-only baseline bug is GONE
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. A /recalculate of a NEVER-exported month → clean rejection, no emit.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Recalculate_NeverExportedMonth_ThrowsNotFound_NoCorrectionEmitted()
    {
        const string emp = "EMP_NEVER";
        const int year = 2026, month = 3;

        var service = BuildCorrectionService();
        var profile = BuildProfile(emp);

        // No payroll_export_records row exists for (emp, 2026-03) → there is nothing to correct.
        // The read-baseline guard throws BEFORE the rule-engine calc, so no HTTP stand-up is needed.
        var ex = await Assert.ThrowsAsync<PayrollExportNotFoundException>(() => service.RecalculateAsync(
            profile,
            entries: [],
            absences: [],
            periodStart: new DateOnly(year, month, 1),
            periodEnd: new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
            previousFlexBalance: 0m,
            reason: "test",
            actorId: "admin-test"));

        Assert.Contains("ikke sendt til lønkørsel", ex.Message);
        Assert.Contains("genåbn", ex.Message);

        // Nothing was written: no record, no correction event, no audit row.
        Assert.Equal(0, await CountRecordsAsync(emp, year, month));
        Assert.Equal(0, await CountCorrectionEventsAsync(emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. original_lines is immutable; current_effective_lines tracks the latest.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OriginalLines_StaysImmutable_AcrossCorrections_CurrentTracksLatest()
    {
        const string emp = "EMP_IMMUT";
        const int year = 2026, month = 4;

        var original = new[] { Line(emp, year, month, hours: 10m, amount: 1000m) };
        await SeedExportRecordAsync(emp, year, month, original);
        var (origBefore, currBefore) = await ReadRowAsync(emp, year, month);
        Assert.Equal(origBefore, currBefore); // first-export invariant

        // Two corrections evolve current_effective_lines.
        await ApplyCorrectionAsync(emp, year, month, new[] { Line(emp, year, month, hours: 12m, amount: 1200m) });
        await ApplyCorrectionAsync(emp, year, month, new[] { Line(emp, year, month, hours: 13m, amount: 1300m) });

        var (origAfter, currAfter) = await ReadRowAsync(emp, year, month);

        // original_lines NEVER changed.
        Assert.Equal(origBefore, origAfter);
        Assert.Contains("\"hours\":10", origAfter.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

        // current_effective_lines tracks the LATEST correction (13h), not the original.
        Assert.NotEqual(origAfter, currAfter);
        var latest = await Repo().TryReadCurrentEffectiveLinesAsync(emp, year, month);
        Assert.NotNull(latest);
        Assert.Equal(13m, latest!.Sum(l => l.Hours));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. BLOCKER 2 — the baseline-update + event/audit are MANDATORY, not
    //     best-effort-swallowed. A failure in the transactional block must FAIL
    //     the call (not phantom Success=true) AND leave the baseline unchanged.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TransactionalBlockFailure_SurfacesError_AndLeavesBaselineUnchanged()
    {
        const string emp = "EMP_TXFAIL";
        const int year = 2026, month = 5;

        // A real export record (10h) with current==original.
        var exported = new[] { Line(emp, year, month, hours: 10m, amount: 1000m) };
        await SeedExportRecordAsync(emp, year, month, exported);
        var (_, currBefore) = await ReadRowAsync(emp, year, month);

        // Inject a failure INSIDE the transactional block: the audit row's target_org_id FKs
        // organizations(org_id). A profile whose OrgId does NOT exist makes _auditRepo.InsertAsync
        // throw (FK violation 23503) AFTER the FOR UPDATE baseline read but inside the load-bearing
        // tx — a realistic "transient DB error in the event/audit/baseline write".
        var service = await BuildWorkingCorrectionServiceAsync(); // calc succeeds (stub rule-engine)
        // AgreementCode/Position match the seeded wage-type mappings (HK / '') so the calc's export-line
        // mapping succeeds and we DEFINITELY reach the transactional block; OrgId is nonexistent so the
        // audit-row FK fires INSIDE that block (not before it).
        var profile = BuildProfile(emp) with { AgreementCode = "HK", Position = "", OrgId = "ORG_DOES_NOT_EXIST" };

        // RED-on-old: pre-fix, the generic catch swallowed this into Success=true (only a LogWarning),
        // the tx rolled back (no event/audit/baseline-update), and the caller got correction lines
        // anyway → the NEXT correction would diff against the STALE 10h baseline (the B3 double-count).
        // Post-fix the failure PROPAGATES (the call surfaces an error, not Success=true).
        await Assert.ThrowsAnyAsync<Exception>(() => service.RecalculateAsync(
            profile,
            entries: [],
            absences: [],
            periodStart: new DateOnly(year, month, 1),
            periodEnd: new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
            previousFlexBalance: 0m,
            reason: "trigger tx-block failure",
            actorId: "admin-test"));

        // The baseline is UNCHANGED — the tx rolled back, so a subsequent correction still diffs
        // against the original 10h (NOT a half-applied / desynced state).
        var (_, currAfter) = await ReadRowAsync(emp, year, month);
        Assert.Equal(currBefore, currAfter);
        var baseline = await Repo().TryReadCurrentEffectiveLinesAsync(emp, year, month);
        Assert.NotNull(baseline);
        Assert.Equal(10m, baseline!.Sum(l => l.Hours)); // still the original — un-advanced

        // No correction event was committed (the tx rolled back).
        Assert.Equal(0, await CountCorrectionEventsAsync(emp));
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. BLOCKER 3 — concurrent /recalculate for the same (employee, month)
    //     serialize on the baseline row: the baseline read is now INSIDE the
    //     correction tx under SELECT … FOR UPDATE, so a second reader blocks
    //     until the first commits, then reads the FIRST correction's result.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentBaselineRead_UnderForUpdate_SerializesOnTheRow()
    {
        const string emp = "EMP_LOCK";
        const int year = 2026, month = 6;

        await SeedExportRecordAsync(emp, year, month, new[] { Line(emp, year, month, hours: 10m, amount: 1000m) });

        // Tx1 takes the FOR UPDATE lock on the (emp, month) row and HOLDS it (does not commit yet).
        await using var conn1 = _harness.Factory.Create();
        await conn1.OpenAsync();
        await using var tx1 = await conn1.BeginTransactionAsync();
        var lockedBaseline = await PayrollExportRecordRepository.TryReadCurrentEffectiveLinesForUpdateAsync(
            conn1, tx1, emp, year, month);
        Assert.NotNull(lockedBaseline);
        Assert.Equal(10m, lockedBaseline!.Sum(l => l.Hours));

        // A second connection attempts the SAME FOR UPDATE read with a short statement_timeout. While
        // tx1 holds the lock it MUST block → the timeout fires (proving the read is genuinely under a
        // row lock, i.e. a concurrent /recalculate would serialize rather than diff a stale baseline).
        await using (var conn2 = _harness.Factory.Create())
        {
            await conn2.OpenAsync();
            await using (var setTimeout = new NpgsqlCommand("SET statement_timeout = 1500", conn2))
                await setTimeout.ExecuteNonQueryAsync();
            await using var tx2 = await conn2.BeginTransactionAsync();
            var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
                await PayrollExportRecordRepository.TryReadCurrentEffectiveLinesForUpdateAsync(
                    conn2, tx2, emp, year, month));
            Assert.Equal("57014", blocked.SqlState); // query_canceled (statement_timeout) — it BLOCKED
            await tx2.RollbackAsync();
        }

        // Tx1 advances the baseline to 12h and commits, releasing the lock.
        await PayrollExportRecordRepository.UpdateCurrentEffectiveLinesAsync(
            conn1, tx1, emp, year, month, new[] { Line(emp, year, month, hours: 12m, amount: 1200m) });
        await tx1.CommitAsync();

        // Now a fresh FOR UPDATE read succeeds and sees tx1's COMMITTED result (12h), NOT the stale 10h
        // — the second correction would diff against 12h (no double-count).
        await using (var conn3 = _harness.Factory.Create())
        {
            await conn3.OpenAsync();
            await using var tx3 = await conn3.BeginTransactionAsync();
            var afterCommit = await PayrollExportRecordRepository.TryReadCurrentEffectiveLinesForUpdateAsync(
                conn3, tx3, emp, year, month);
            Assert.NotNull(afterCommit);
            Assert.Equal(12m, afterCommit!.Sum(l => l.Hours));
            await tx3.CommitAsync();
        }
    }

    // ── service / profile builders + counters ─────────────────────────────────

    /// <summary>
    /// Builds a correction service whose rule-engine calc actually COMPLETES (stub HTTP handler +
    /// seeded wage-type mappings), so a test can drive <see cref="RetroactiveCorrectionService.RecalculateAsync"/>
    /// PAST the calc and INTO the transactional block (unlike <see cref="BuildCorrectionService"/>,
    /// whose NoopHttpClientFactory throws on the first HTTP calc call — fine for the never-exported
    /// path that rejects before calc, but not for exercising the tx block).
    /// </summary>
    private async Task<RetroactiveCorrectionService> BuildWorkingCorrectionServiceAsync()
    {
        var factory = _harness.Factory;
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        await Segmentation.TestFixtures.SeedWageTypeMappingsAsync(factory);
        var calc = Segmentation.TestFixtures.BuildPcs(factory, eventStore);
        return new RetroactiveCorrectionService(
            calc,
            factory,
            eventStore,
            new AuditProjectionRepository(factory),
            new RetroactiveCorrectionRequestedAuditMapper(),
            new PayrollExportRecordRepository(factory),
            NullLogger<RetroactiveCorrectionService>.Instance);
    }

    private RetroactiveCorrectionService BuildCorrectionService()
    {
        var factory = _harness.Factory;
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>()).Build();
        var mapping = new PayrollMappingService(factory, NullLogger<PayrollMappingService>.Instance);
        var calc = new PeriodCalculationService(
            new NoopHttpClientFactory(),
            mapping,
            eventStore,
            factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance);
        return new RetroactiveCorrectionService(
            calc,
            factory,
            eventStore,
            new AuditProjectionRepository(factory),
            new RetroactiveCorrectionRequestedAuditMapper(),
            new PayrollExportRecordRepository(factory),
            NullLogger<RetroactiveCorrectionService>.Instance);
    }

    private static EmploymentProfile BuildProfile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = "AC",
        OkVersion = "OK24",
        EmploymentCategory = "FULL_TIME",
        Position = "Fuldmægtig",
        OrgId = OrgId,
    };

    private async Task<int> CountRecordsAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id=@e AND year=@y AND month=@m", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountCorrectionEventsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type='RetroactiveCorrectionRequested' AND stream_id LIKE @s", conn);
        cmd.Parameters.AddWithValue("s", $"retro-correction-{employeeId}-%");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // A NO-OP IHttpClientFactory: the never-exported test throws BEFORE any HTTP calc call, so this
    // client is never actually invoked (it would throw if it were — a guard that the guard ran first).
    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoopHandler());
        private sealed class NoopHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => throw new InvalidOperationException("HTTP calc must not be reached on the never-exported path.");
        }
    }
}
