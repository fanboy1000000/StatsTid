using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S145 / TASK-14515 — shared building blocks for the replay pins
/// (<c>ReplayFrozenInputsTests</c>, <c>ManifestProjectionRebuildPlanInputsTests</c>,
/// <c>EmployeeProfileForwardCalcAsOfTests</c>).
///
/// <para>
/// <b>Why a kit and not <see cref="TestFixtures.BuildPcs"/>.</b> <c>BuildPcs</c> is edited by the
/// open-wave paired passes (14508-T, 14512-T; R50), and the sentinel replay (R56) needs a PCS whose
/// approval reader, window resolver, hours reader and agreement-code repository are all injected,
/// which <c>BuildPcs</c> does not take. This kit therefore builds the PCS itself, by NAMED
/// constructor arguments, over the same fixtures (<see cref="TestFixtures.DockerHarness"/>,
/// <see cref="TestFixtures.RuleSet"/>, <see cref="TestFixtures.SeedWageTypeMappingsAsync"/>).
/// </para>
///
/// <para>
/// <b>Plain language.</b> The pieces here are stand-ins that make a replay provable: a fake
/// "was last month approved?" answer that can be swapped, sentinels that fail the moment a replay
/// asks today's world anything, a dated part-time-fraction history that can be edited after the
/// calculation, and a seeder that writes hour rows the way a Skema save does (so a re-save
/// supersedes the old row instead of adding to it).
/// </para>
/// </summary>
internal static class ReplayTestKit
{
    public const string Agreement = "HK";
    public const string Ok = "OK26"; // every date used by these pins is on or after 2026-04-01

    // A deliberately unreachable database for the R56 agreement-code sentinel: port 1 refuses at
    // once, Timeout=1 caps any retry. Every read of UserAgreementCodeRepository opens a connection
    // (UserAgreementCodeRepository.cs:306-307), so a replay that reads it fails immediately.
    public const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=statstid_unreachable;Username=x;Password=x;Timeout=1";

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── PCS construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a real <see cref="PeriodCalculationService"/> over the harness database, talking to
    /// <paramref name="engine"/>. Every collaborator the pins need to control is a NAMED argument.
    /// A profile resolver without a window resolver is refused by the PCS constructor (S137), so
    /// when only a profile resolver is given an unbounded window is supplied.
    /// </summary>
    public static PeriodCalculationService BuildPcs(
        TestFixtures.DockerHarness harness,
        ReplayEchoRuleEngine engine,
        IPriorPeriodApprovalReader? approval,
        IRecordedHoursReader? hours,
        IEmploymentProfileResolver? profileResolver = null,
        IEmploymentWindowResolver? windowResolver = null,
        UserAgreementCodeRepository? agreementCodeRepo = null)
    {
        if (profileResolver is not null && windowResolver is null)
            windowResolver = new MutableWindowResolver(new EmploymentWindow(null, null));

        var httpFactory = new SingleClientFactory(new TestFixtures.StubHandler(engine.Handle));
        var wtmRepo = new WageTypeMappingRepository(harness.Factory);
        var mappingService = new PayrollMappingService(
            harness.Factory, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            new PostgresSegmentManifestStore(harness.Factory, harness.EventStore, harness.EventStore),
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryClassifications(TestFixtures.RuleSet),
            profileResolver: profileResolver,
            employmentWindowResolver: windowResolver,
            userAgreementCodeRepo: agreementCodeRepo,
            hoursReader: hours,
            priorPeriodApprovalReader: approval);
    }

    /// <summary>The real hours reader over the harness projection tables.</summary>
    public static ProjectionHoursReader RealHoursReader(TestFixtures.DockerHarness harness) =>
        new(harness.Factory, new TimeEntryProjectionRepository(harness.Factory), new AbsenceProjectionRepository(harness.Factory));

    /// <summary>Applies the harness schema, the hours projection tables and the wage-type mappings.</summary>
    public static async Task PrepareDatabaseAsync(TestFixtures.DockerHarness harness)
    {
        await ProjectionSchemaTestFixture.ApplyAsync(harness.ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(harness.Factory);
    }

    // ── Calculation helpers ──────────────────────────────────────────────────

    /// <summary>
    /// A forward calculation through the planless entry point (the per-plan lane's entry), asserting
    /// the preconditions every replay fact relies on: success, a complete audit chain (so the
    /// manifest row exists), and that the echo reached the result (so the fact is not vacuous).
    /// </summary>
    public static async Task<PeriodCalculationService.PeriodCalculationOutcome> ForwardAsync(
        PeriodCalculationService pcs, string employeeId, DateOnly periodStart, DateOnly periodEnd,
        decimal openingFlexBalance = 0m)
    {
#pragma warning disable CS0618 // The planless entry registers the WtmNaturalKey hydrator (S29 precedent).
        var outcome = await pcs.CalculateWithOutcomeAsync(
            TestFixtures.Profile(employeeId, Ok), periodStart, periodEnd,
            previousFlexBalance: openingFlexBalance);
#pragma warning restore CS0618
        Assert.True(outcome.Result.Success, "the forward calculation must succeed");
        Assert.Equal(PeriodCalculationService.AuditState.Complete, outcome.AuditState);
        Assert.NotEqual(Guid.Empty, outcome.ManifestId);
        AssertEchoReached(outcome.Result);
        return outcome;
    }

    /// <summary>A forward calculation whose result only is needed (a "fresh calculation").</summary>
    public static async Task<PeriodCalculationResult> FreshAsync(
        PeriodCalculationService pcs, string employeeId, DateOnly periodStart, DateOnly periodEnd,
        decimal openingFlexBalance = 0m)
    {
#pragma warning disable CS0618
        return await pcs.CalculateAsync(
            TestFixtures.Profile(employeeId, Ok), periodStart, periodEnd,
            previousFlexBalance: openingFlexBalance);
#pragma warning restore CS0618
    }

    /// <summary>The fixture is vacuous unless the echo reached the compared result.</summary>
    public static void AssertEchoReached(PeriodCalculationResult result)
    {
        var lines = result.RuleResults.SelectMany(r => r.LineItems).Select(l => l.TimeType).ToList();
        Assert.Contains(lines, t => t.StartsWith("ECHO_PIECE|", StringComparison.Ordinal));
        Assert.Contains(lines, t => t.StartsWith("ECHO_CUTOFF|", StringComparison.Ordinal));
        Assert.Contains(lines, t => t.StartsWith("ECHO_ENTRY|", StringComparison.Ordinal));
        Assert.Contains(lines, t => t.StartsWith("ECHO_PROFILE|", StringComparison.Ordinal));
    }

    /// <summary>
    /// The full <see cref="PeriodCalculationResult"/> (every rule result's line items, and the
    /// export lines) as canonical JSON. With <paramref name="withManifestIds"/> false every
    /// <c>manifestId</c> member is removed: a FRESH calculation always mints a new manifest id, so
    /// "differs" must compare modulo that id or it could never fail.
    /// </summary>
    public static string Canonical(PeriodCalculationResult result, bool withManifestIds)
    {
        var node = JsonSerializer.SerializeToNode(result, CanonicalOptions)!;
        if (!withManifestIds) StripManifestIds(node);
        return node.ToJsonString();
    }

    private static void StripManifestIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("manifestId");
                foreach (var kv in obj.ToList()) StripManifestIds(kv.Value);
                break;
            case JsonArray arr:
                foreach (var item in arr) StripManifestIds(item);
                break;
        }
    }

    /// <summary>
    /// Asserts the call is refused BY NAME: it throws <see cref="ReplayRefusedException"/> whose
    /// <c>Reason</c> is the kebab-case name (for example <c>replay-entries-missing</c>). Any other
    /// exception, or success, fails the fact.
    /// </summary>
    public static async Task AssertRefusedAsync(string expectedReason, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<ReplayRefusedException>(act);
        Assert.Equal(expectedReason, ex.Reason);
    }

    // ── Raw SQL helpers (tamper, delete, read) ───────────────────────────────

    public static async Task ExecAsync(TestFixtures.DockerHarness harness, string sql, params (string Name, object Value)[] args)
    {
        await using var conn = harness.Factory.Create();
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): every caller passes a compile-time test constant;
        // values travel as parameters, never as text.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        var rows = await cmd.ExecuteNonQueryAsync();
        Assert.True(rows > 0, $"expected the statement to touch a row: {sql}");
    }

    /// <summary><c>segment_manifests.plan_inputs_jsonb</c> as text, or null for a NULL slot.</summary>
    public static async Task<string?> ReadPlanInputsAsync(TestFixtures.DockerHarness harness, Guid manifestId)
    {
        await using var conn = harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT plan_inputs_jsonb::text FROM segment_manifests WHERE manifest_id = @id", conn);
        cmd.Parameters.AddWithValue("id", manifestId);
        var scalar = await cmd.ExecuteScalarAsync();
        Assert.NotNull(scalar); // no row at all would be a fixture failure, not a NULL slot
        return scalar is DBNull ? null : (string)scalar!;
    }

    // ── Fakes and sentinels ──────────────────────────────────────────────────

    /// <summary>
    /// The "was the previous month approved?" answer, swappable between calls. Records the periods
    /// asked about. <c>// S145 R26:</c> a fact that is not about the prior-month check treats M-1
    /// as APPROVED.
    /// </summary>
    public sealed class FakeApprovalReader : IPriorPeriodApprovalReader
    {
        private readonly List<(DateOnly Start, DateOnly End)> _asked = new();
        public string? Status { get; set; }
        public IReadOnlyList<(DateOnly Start, DateOnly End)> Asked => _asked;

        public FakeApprovalReader(string? status = "APPROVED") => Status = status;

        public Task<string?> GetApprovalStatusAsync(
            string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
        {
            _asked.Add((periodStart, periodEnd));
            return Task.FromResult(Status);
        }
    }

    /// <summary>R42/R56 sentinel: fails the moment anything consults it.</summary>
    public sealed class ThrowingApprovalReader : IPriorPeriodApprovalReader
    {
        public Task<string?> GetApprovalStatusAsync(
            string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct) =>
            throw new InvalidOperationException(
                "SENTINEL: a replay consulted IPriorPeriodApprovalReader (R42: replay enters evaluation only).");
    }

    /// <summary>
    /// Re-spawn 1 sentinel (14512 Step 5a WARNING 1): since TASK-14513 a replay never reads the dated profile
    /// resolver (it replays the frozen pieces), so this one fails the moment anything consults it.
    /// </summary>
    public sealed class ThrowingProfileResolver : IEmploymentProfileResolver
    {
        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
            string employeeId, DateOnly asOfDate, CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "SENTINEL: a replay consulted IEmploymentProfileResolver (replay runs from the frozen pieces, not the live profile).");
    }

    /// <summary>R42/R56 sentinel: fails the moment anything consults it.</summary>
    public sealed class ThrowingWindowResolver : IEmploymentWindowResolver
    {
        public Task<EmploymentWindowStatus> GetStatusAsync(string employeeId, DateOnly date, CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "SENTINEL: a replay consulted IEmploymentWindowResolver (R42/R41: the cutoff is frozen, not re-derived).");

        public Task<IReadOnlyList<EmploymentWindow>> GetWindowsAsync(
            string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            throw new InvalidOperationException(
                "SENTINEL: a replay consulted IEmploymentWindowResolver (R42/R41: the cutoff is frozen, not re-derived).");
    }

    /// <summary>
    /// R42/R56 sentinel for the hours reader: <c>ReadCurrentAsync</c> throws (a replay must not read
    /// today's current rows), <c>ReadByEventIdsAsync</c> delegates to the real reader (a replay must
    /// read the recorded ids).
    /// </summary>
    public sealed class CurrentReadSentinelHoursReader : IRecordedHoursReader
    {
        private readonly IRecordedHoursReader _real;
        public CurrentReadSentinelHoursReader(IRecordedHoursReader real) => _real = real;

        public Task<RecordedHours> ReadCurrentAsync(
            string employeeId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct) =>
            throw new InvalidOperationException(
                "SENTINEL: a replay called IRecordedHoursReader.ReadCurrentAsync (R42: it reads by recorded id).");

        public Task<RecordedHours> ReadByEventIdsAsync(
            string employeeId, IReadOnlyList<Guid> entryEventIds, IReadOnlyList<Guid> absenceEventIds, CancellationToken ct) =>
            _real.ReadByEventIdsAsync(employeeId, entryEventIds, absenceEventIds, ct);
    }

    /// <summary>
    /// An employment window that can be changed after a calculation (the leaver fact removes the end
    /// date). Both PCS instances of a fact share one instance, so a replay that re-derived anything
    /// from the live window would see the change.
    /// </summary>
    public sealed class MutableWindowResolver : IEmploymentWindowResolver
    {
        public EmploymentWindow Window { get; set; }
        public MutableWindowResolver(EmploymentWindow window) => Window = window;

        public Task<EmploymentWindowStatus> GetStatusAsync(string employeeId, DateOnly date, CancellationToken ct = default) =>
            throw new NotSupportedException("Planning must use the range-scoped GetWindowsAsync.");

        public Task<IReadOnlyList<EmploymentWindow>> GetWindowsAsync(
            string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            // Same contract as the real resolver: only spells overlapping [from, to]; empty = fully not employed.
            var w = Window;
            var overlaps = (w.Start is null || w.Start <= to) && (w.End is null || w.End >= from);
            return Task.FromResult<IReadOnlyList<EmploymentWindow>>(
                overlaps ? new[] { w } : Array.Empty<EmploymentWindow>());
        }
    }

    /// <summary>
    /// A DATED part-time-fraction history with the real resolver's semantics: the row in force on a
    /// day is the latest row whose <c>From</c> is on or before it. Rows can be added after a
    /// calculation (a backdate). Both PCS instances of a fact share one instance.
    /// </summary>
    public sealed class DatedProfileResolver : IEmploymentProfileResolver
    {
        private readonly List<(DateOnly From, decimal Fraction, string? Position, int Seq)> _rows = new();
        private readonly List<DateOnly> _asked = new();

        public IReadOnlyList<DateOnly> AskedDates => _asked;

        public DatedProfileResolver(decimal fractionFromTheBeginning)
            => AddRow(new DateOnly(2020, 1, 1), fractionFromTheBeginning);

        public void AddRow(DateOnly from, decimal fraction, string? position = null) =>
            _rows.Add((from, fraction, position, _rows.Count));

        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(string employeeId, DateOnly asOfDate, CancellationToken ct = default)
        {
            _asked.Add(asOfDate);
            var row = _rows.Where(r => r.From <= asOfDate).OrderBy(r => r.From).ThenBy(r => r.Seq).LastOrDefault();
            if (row.Seq == 0 && row.From == default) return Task.FromResult<EmploymentProfile?>(null);
            return Task.FromResult<EmploymentProfile?>(new EmploymentProfile
            {
                EmployeeId = employeeId,
                AgreementCode = Agreement,
                OkVersion = OkVersionResolver.ResolveVersion(asOfDate),
                EmploymentCategory = "Standard",
                PartTimeFraction = row.Fraction,
                Position = row.Position,
            });
        }
    }

    // ── Hour-row seeding (a Skema save, at repository level) ─────────────────

    /// <summary>
    /// Writes hour rows through <see cref="TimeEntryProjectionRepository.InsertAsync"/> /
    /// <see cref="AbsenceProjectionRepository.InsertAsync"/>, exactly as a save does, with a
    /// strictly increasing <c>outbox_id</c>. Skema rows (the default) of the same cell supersede
    /// each other (C-R2); a <c>TIME_POST</c> row is additive. Absences are seeded on weekdays only
    /// (a zero-norm day is refused by the product, S138/QUAL-153).
    /// </summary>
    public sealed class HoursSeeder
    {
        private readonly TestFixtures.DockerHarness _harness;
        private readonly TimeEntryProjectionRepository _entries;
        private readonly AbsenceProjectionRepository _absences;
        private long _outboxId;

        public HoursSeeder(TestFixtures.DockerHarness harness)
        {
            _harness = harness;
            _entries = new TimeEntryProjectionRepository(harness.Factory);
            _absences = new AbsenceProjectionRepository(harness.Factory);
        }

        public long LastOutboxId => _outboxId;

        public async Task<Guid> EntryAsync(
            string employeeId, DateOnly date, decimal hours, string source = HoursSource.Skema,
            string? taskId = "T1", TimeOnly? start = null, TimeOnly? end = null)
        {
            var ev = new TimeEntryRegistered
            {
                EmployeeId = employeeId,
                Date = date,
                Hours = hours,
                StartTime = start,
                EndTime = end,
                TaskId = taskId,
                AgreementCode = Agreement,
                OkVersion = Ok,
                Source = source,
            };
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _entries.InsertAsync(conn, tx, ev, ++_outboxId);
            await tx.CommitAsync();
            return ev.EventId;
        }

        public async Task<Guid> AbsenceAsync(
            string employeeId, DateOnly date, string absenceType, decimal hours, string source = HoursSource.Skema)
        {
            Assert.False(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                "absences are seeded on weekdays only (S138/QUAL-153)");
            var ev = new AbsenceRegistered
            {
                EmployeeId = employeeId,
                Date = date,
                AbsenceType = absenceType,
                Hours = hours,
                AgreementCode = Agreement,
                OkVersion = Ok,
                Source = source,
            };
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _absences.InsertAsync(conn, tx, ev, ++_outboxId);
            await tx.CommitAsync();
            return ev.EventId;
        }

        /// <summary>One entry per weekday in <c>[from, to]</c> except <paramref name="skip"/>; returns date → event id.</summary>
        public async Task<Dictionary<DateOnly, Guid>> WeekdaysAsync(
            string employeeId, DateOnly from, DateOnly to, decimal hours = 7.4m, params DateOnly[] skip)
        {
            var ids = new Dictionary<DateOnly, Guid>();
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                if (skip.Contains(d)) continue;
                ids[d] = await EntryAsync(employeeId, d, hours);
            }
            return ids;
        }
    }

    // ── Small private helpers ────────────────────────────────────────────────

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class InMemoryClassifications : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryClassifications(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }

    public static string Fmt(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);
}
