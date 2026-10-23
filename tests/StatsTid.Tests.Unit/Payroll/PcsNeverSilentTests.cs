using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S145 / TASK-14514 (rulings R21, R25, R26, R42) — "optional but never silent". Payroll's calculation core
/// (<see cref="PeriodCalculationService"/>) takes two new dependencies as OPTIONAL constructor parameters, because
/// many fixtures build it by hand; but an absent dependency must never quietly change what is paid.
///
/// <para>
/// <b>Plain language.</b>
/// <list type="bullet">
/// <item><b>The approval reader</b> answers "was last month approved?". A month whose opening week reaches days of
/// the previous month may only be calculated once that month is APPROVED. If the host forgot to register the
/// reader, skipping the check would let an unapproved month's neighbour be paid, silently, so PCS throws an
/// <see cref="InvalidOperationException"/> naming the missing registration. When the previous month has no
/// employed day in the opening week (June 2026 starts on a Monday), there is nothing to check and no reader is
/// needed.</item>
/// <item><b>The hours reader</b> is payroll's own read of the recorded hours. Without it a calculation would see
/// no hours at all: a month "paid" as zero. So any calculation without it throws, naming the registration, BEFORE
/// any read (the classification fetch, the history and window reads, the hours read).</item>
/// <item><b>Replay is different (R42).</b> A replay proves what WAS calculated, from frozen inputs. Whether last
/// month is approved TODAY cannot change whether this month's result is reproducible, so replay enters evaluation
/// only and never consults the approval reader: a replay with no approval reader succeeds.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Fixture.</b> A DB-free PCS (the <see cref="EmploymentWindowPcsFixture"/> idiom: a recording stub engine, an
/// in-memory event store, no mapping service because the stub returns no line items). The windowless PCS treats
/// every day as EMPLOYED, so October's opening week (Monday 28 September) reaches EMPLOYED September days. The
/// hours reader is an in-memory fake over <see cref="TimeEntryProjectionRow"/> lists. Per R26 the approval fake
/// answers APPROVED where the fact is not about the check.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (these pins name types TASK-14508 / 14512 create, so RED by MUTATION at the A.4 gate,
/// never by reverting; <c>docs/AGENTS.md</c> § "RED evidence"): MU-28 (a null approval reader skips the check)
/// trips fact 1 (no exception); MU-29 (throw whenever the reader is null) trips fact 2 (an exception in June);
/// MU-30 (a null hours reader yields empty hours) trips fact 3 (no exception); MU-38 (replay throws on a null
/// approval reader, because it runs the calculation rule) trips fact 4.
/// </para>
/// </summary>
public sealed class PcsNeverSilentTests
{
    private const string Emp = "EMP-S145-NEVERSILENT";

    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    // ═════════════════════════════════════════════════════════════════════
    // 1. October, September days in the opening week, no approval reader
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// October 2026's opening week is Monday 28 September - Sunday 4 October, so it holds EMPLOYED September days
    /// (a windowless PCS treats every day as EMPLOYED). With NO approval reader registered, the calculation throws
    /// <see cref="InvalidOperationException"/> naming <c>IPriorPeriodApprovalReader</c>, and it does so before any
    /// rule call (nothing was sent to the engine). The hours reader IS registered, so the only missing registration
    /// is the one named.
    /// </summary>
    [Fact]
    public async Task NoApprovalReader_OctoberWithEmployedSeptemberDays_Throws_NamesRegistration()
    {
        var engine = new StubEngine();
        var pcs = BuildPcs(engine, new InMemorySegmentManifestStore(), hours: new InMemoryHours(), approval: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CalculateAsync(pcs, Oct01, Oct31));

        Assert.Contains("IPriorPeriodApprovalReader", ex.Message);
        Assert.Empty(engine.Paths);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. June 2026 starts on a Monday: nothing to check, no reader needed
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// June 2026 starts on a Monday (1 June), so its opening week holds no day of May: no EMPLOYED previous-month
    /// day exists and no approval is needed. With no approval reader registered the calculation SUCCEEDS, and it
    /// really ran (the engine received the per-plan timeline call), so the success is not the trivial result of a
    /// skipped calculation.
    /// </summary>
    [Fact]
    public async Task NoApprovalReader_June2026_Succeeds()
    {
        var engine = new StubEngine();
        var pcs = BuildPcs(engine, new InMemorySegmentManifestStore(), hours: new InMemoryHours(), approval: null);

        var outcome = await CalculateAsync(pcs, Jun01, Jun30);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Contains("/api/rules/evaluate-timeline", engine.Paths);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. No hours reader: never calculate on no hours
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// With NO hours reader registered, any calculation throws <see cref="InvalidOperationException"/> naming
    /// <c>IRecordedHoursReader</c>, BEFORE any read (R25): the engine received no call, the approval fake was never
    /// asked, and the employment-window resolver was never consulted. June 2026 is used so the approval reader
    /// would not be needed anyway: the only missing registration is the hours reader.
    /// </summary>
    [Fact]
    public async Task NoHoursReader_Calculation_Throws_NamesRegistration()
    {
        var engine = new StubEngine();
        var approval = new CountingApproval();
        var windows = new FakeWindowResolver(new EmploymentWindow(null, null));
        var pcs = BuildPcs(engine, new InMemorySegmentManifestStore(), hours: null, approval: approval, windows: windows);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CalculateAsync(pcs, Jun01, Jun30));

        Assert.Contains("IRecordedHoursReader", ex.Message);
        Assert.Empty(engine.Paths);
        Assert.Equal(0, approval.Calls);
        Assert.Empty(windows.Calls);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4. Replay enters evaluation only (R42)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R42. A fresh calculation of October runs on a PCS with an APPROVED fake and an in-memory hours reader; its
    /// manifest is persisted into the in-memory manifest store (the audit state is Complete, S146 R-22). A SECOND PCS
    /// over the same manifest store and the same hours, but with a NULL approval reader,
    /// then replays that manifest by id and SUCCEEDS: replay enters evaluation only (TASK-14512 Do 2) and never
    /// consults the approval reader, because "is September approved today" cannot change whether October's result is
    /// reproducible. Replay really evaluated (the engine received the timeline call), so the success is not
    /// vacuous. (The replay loads the manifest through <c>ISegmentManifestStore.TryLoadAsync</c>, which the in-memory
    /// store answers without a database, which is what lets a unit fixture replay. S146 TASK-14604-T: this cite
    /// replaces the pre-S146 pointer into <c>PeriodCalculationService.LoadManifestAsync</c>.)
    /// </summary>
    [Fact]
    public async Task NoApprovalReader_Replay_Succeeds()
    {
        var store = new InMemorySegmentManifestStore();
        var hours = new InMemoryHours(
            Row(new DateOnly(2026, 9, 28), 9.0m, 1), Row(new DateOnly(2026, 9, 29), 9.0m, 2),
            Row(new DateOnly(2026, 9, 30), 9.0m, 3), Row(Oct01, 7.4m, 4), Row(new DateOnly(2026, 10, 2), 7.4m, 5));

        // The forward calculation: approval answered APPROVED by the fake (R26: not a fact about the check).
        var forwardEngine = new StubEngine();
        var forwardPcs = BuildPcs(forwardEngine, store, hours, approval: new AlwaysApprovedPriorPeriods());
        var forward = await CalculateAsync(forwardPcs, Oct01, Oct31);
        Assert.True(forward.Result.Success, forward.Result.ErrorMessage);
        Assert.NotNull(store.Manifest);

        // The replay: a null approval reader, a fresh engine.
        var replayEngine = new StubEngine();
        var replayPcs = BuildPcs(replayEngine, store, hours, approval: null);
        var replay = await replayPcs.ReplayAsync(forward.ManifestId);

        Assert.True(replay.Success, replay.ErrorMessage);
        Assert.Contains("/api/rules/evaluate-timeline", replayEngine.Paths);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Harness
    // ═════════════════════════════════════════════════════════════════════

    private static async Task<PeriodCalculationService.PeriodCalculationOutcome> CalculateAsync(
        PeriodCalculationService pcs, DateOnly periodStart, DateOnly periodEnd)
    {
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        return await pcs.CalculateWithOutcomeAsync(Profile(Emp), periodStart, periodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618
    }

    private static PeriodCalculationService BuildPcs(
        StubEngine engine,
        ISegmentManifestStore store,
        IRecordedHoursReader? hours,
        IPriorPeriodApprovalReader? approval,
        IEmploymentWindowResolver? windows = null) =>
        new(
            new SingleClientHttpFactory(engine),
            // Never dereferenced: the stub engine returns no line items, so no mapping lookup happens.
            mappingService: null!,
            store,
            Configuration(),
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new Classifications(RuleSet),
            localAgreementProfileRepo: null,
            profileResolver: null,
            employmentWindowResolver: windows,
            employeeProfileRepo: null,
            hoursReader: hours,
            priorPeriodApprovalReader: approval);

    private static TimeEntryProjectionRow Row(DateOnly date, decimal hours, long outboxId) => new()
    {
        EventId = Guid.NewGuid(),
        EmployeeId = Emp,
        Date = date,
        Hours = hours,
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc),
        OutboxId = outboxId,
    };

    // S145 R26: this suite is not about the prior-month check; M−1 is treated as APPROVED
    private sealed class AlwaysApprovedPriorPeriods : IPriorPeriodApprovalReader
    {
        public Task<string?> GetApprovalStatusAsync(
            string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct) =>
            Task.FromResult<string?>("APPROVED");
    }

    /// <summary>Counts how often PCS asks (the "before any read" assertion).</summary>
    private sealed class CountingApproval : IPriorPeriodApprovalReader
    {
        public int Calls { get; private set; }

        public Task<string?> GetApprovalStatusAsync(
            string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<string?>("APPROVED");
        }
    }

    /// <summary>An in-memory <see cref="IRecordedHoursReader"/> over time-entry rows (no absences).</summary>
    private sealed class InMemoryHours : IRecordedHoursReader
    {
        private readonly IReadOnlyList<TimeEntryProjectionRow> _rows;

        public InMemoryHours(params TimeEntryProjectionRow[] rows) => _rows = rows;

        public Task<RecordedHours> ReadCurrentAsync(
            string employeeId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct) =>
            Task.FromResult(new RecordedHours(
                _rows.Where(r => r.EmployeeId == employeeId && r.Date >= fromInclusive && r.Date <= toInclusive).ToList(),
                Array.Empty<AbsenceProjectionRow>()));

        public Task<RecordedHours> ReadByEventIdsAsync(
            string employeeId, IReadOnlyList<Guid> entryEventIds, IReadOnlyList<Guid> absenceEventIds, CancellationToken ct) =>
            Task.FromResult(new RecordedHours(
                _rows.Where(r => entryEventIds.Contains(r.EventId)).ToList(),
                Array.Empty<AbsenceProjectionRow>()));
    }

    /// <summary>
    /// A stub Rule Engine that records the request paths and answers every endpoint with an empty success, the
    /// timeline endpoint with its full response shape (three result rows, the flex block, a stub fingerprint and the
    /// evaluation-model marker), so a calculation and its replay both complete.
    /// </summary>
    private sealed class StubEngine : HttpMessageHandler
    {
        private readonly List<string> _paths = new();

        public IReadOnlyList<string> Paths
        {
            get { lock (_paths) return _paths.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            lock (_paths) _paths.Add(path);

            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var employeeId = root.TryGetProperty("employeeId", out var eid) ? eid.GetString() ?? Emp
                : root.TryGetProperty("profile", out var prof) && prof.TryGetProperty("employeeId", out var pid) ? pid.GetString() ?? Emp
                : Emp;

            if (path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal))
            {
                return Json(new
                {
                    results = new object[]
                    {
                        Empty("OVERTIME_CALC", employeeId),
                        Empty("NORM_CHECK_37H", employeeId),
                        Empty("FLEX_BALANCE", employeeId),
                    },
                    flex = new
                    {
                        ruleId = "FLEX_BALANCE", employeeId, success = true, lineItems = Array.Empty<object>(),
                        previousBalance = 0m, newBalance = 0m, delta = 0m, workedHours = 0m,
                        absenceNormCredits = 0m, effectiveNorm = 0m, excessForPayout = 0m,
                    },
                    configFingerprints = new[] { "stub" },
                    evaluationModel = WeeklyTimelineCore.EvaluationModel,
                });
            }

            var ruleId = path.EndsWith("/evaluate-flex", StringComparison.Ordinal) ? "FLEX_BALANCE"
                : path.EndsWith("/evaluate-absence", StringComparison.Ordinal) ? "ABSENCE"
                : root.TryGetProperty("ruleId", out var rid) ? rid.GetString() ?? "UNKNOWN" : "UNKNOWN";
            return Json(Empty(ruleId, employeeId));
        }

        private static object Empty(string ruleId, string employeeId) =>
            new { ruleId, employeeId, success = true, lineItems = Array.Empty<object>() };

        private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8, "application/json"),
        };
    }

    private sealed class Classifications : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public Classifications(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
