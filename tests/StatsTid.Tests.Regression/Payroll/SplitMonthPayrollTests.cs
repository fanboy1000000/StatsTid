using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S145 Stage B / TASK-14524 - the calculation pins for a month with a mid-month change, at PCS level
/// (<see cref="PeriodCalculationService"/>) over an IN-PROCESS REAL Rule Engine, a REAL Postgres, the REAL
/// <see cref="ProjectionHoursReader"/> over seeded projection rows and the REAL
/// <see cref="ApprovalPeriodPriorPeriodReader"/> with every previous month seeded APPROVED (R26). Every fact
/// plans through the PLANLESS SHIM with the FULL live registry set (C-B6), so the evaluated-set filter
/// (TASK-14522 contract 1, MB-7) is on the path of each. Ten facts: WS1, WS2, QA1, QP1, FX1, FX2, FX3, RP-B1.
///
/// <para>
/// <b>The story, in plain language.</b> Until Stage B a month in which an employee's hours, position or
/// agreement changed on a day other than the 1st was REFUSED, because overtime is judged over a whole
/// Monday-Sunday week and the planner would not cut it in two. Stage B pays such months, part by part:
/// each weekday carries the norm of the contract in force that day (owner ruling Q2 = A: weekly x fraction
/// / 5, rounded per day), a week is paid by the month holding its Sunday and dated that Sunday (Q3 = F1),
/// and each part of the month maps its wage-type codes under its own agreement, position and OK version
/// (QUAL-150). Flex is settled ONCE for the month at the cap in force on its last day. And such a month
/// can be replayed later from its stored record and give the same answer even after the live data changed
/// (RP-B1, the auditability proof path).
/// </para>
///
/// <para>
/// <b>Worked split week (Q2 example).</b> 37 h to 30 h (fraction 0.811) from Wed 16 Sep 2026, every weekday
/// of Mon 14 - Fri 18 Sep worked 8 h. Per-day norm = Math.Round(37 x fraction / 5, 2): Mon-Tue 7.40,
/// Wed-Fri 6.00 (37 x 0.811 / 5 = 6.0014), so the week's norm is 2 x 7.40 + 3 x 6.00 = 32.80. Per-day 100%
/// threshold = Math.Round(40 x fraction / 5, 2): 8.00 and 6.49 (6.488), so 2 x 8.00 + 3 x 6.49 = 35.47.
/// Worked 40.00: HK pays OVERTIME_50 min(40 - 32.80, 35.47 - 32.80) = 2.67 and OVERTIME_100 40 - 35.47 =
/// 4.53; AC pays merarbejde 40 - 32.80 = 7.20. All dated Sunday 20 Sep.
/// </para>
///
/// <para>
/// <b>Fixture conditions.</b> C-B5: FX2 and FX3 assert flex (and, for FX3, the absence line) only, never a
/// wage type. C-B6: all facts plan through the planless shim with the live set. C-B7: WS1, WS2, FX1, FX3 and
/// RP-B1 keep one agreement and position across the split, so their key never differs (MB-2 cannot reach
/// them). C-B8: every weekday not named carries exactly its per-day norm, and every context day too.
/// </para>
///
/// Docker-gated (Testcontainers Postgres + full init.sql): CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class SplitMonthPayrollTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S145_SPLITPAY";

    /// <summary>The full-time day norm (37 / 5).</summary>
    private const decimal DayNorm = 7.4m;

    /// <summary>The per-day norm at fraction 0.811: Math.Round(37 x 0.811 / 5, 2).</summary>
    private const decimal DayNormAfter = 6.00m;

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _db = null!;
    private PostgresEventStore _store = null!;
    private RuleEngineHost _engine = null!;
    private long _outboxSeq = 9000;

    private static readonly IReadOnlyList<RuleClassification> LiveSet = new RuleRegistry().GetAll();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        var cs = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(cs);
        _db = new DbConnectionFactory(cs);
        _store = new PostgresEventStore(_db);
        await TestFixtures.SeedWageTypeMappingsAsync(_db);
        _engine = new RuleEngineHost();
    }

    public async Task DisposeAsync()
    {
        if (_engine is not null)
            await _engine.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // =====================================================================
    // WS1 / WS2 - the Q2 worked split week
    // =====================================================================

    /// <summary>
    /// WS1. HK, fraction 1.0 to 0.811 effective Wed 16 Sep 2026 (a mid-month split, two EMPLOYED segments).
    /// August APPROVED. 14-18 Sep worked 8 h; every other weekday (31 August included) at its per-day norm
    /// (7.40 before the change, 6.00 after). September pays EXACTLY OVERTIME_50 2.67 and OVERTIME_100 4.53,
    /// both dated Sunday 20 Sep, exported under SLS_0210 / SLS_0220; the engine is called ONCE for the whole
    /// month. <b>Flex (R-59, QUAL-189):</b> the month's flex is +7.20 from an opening 0, read from the
    /// engine's own timeline answer: worked 154.60 (82.60 for 1-15 Sep + 72.00 for 16-30 Sep, overtime hours
    /// included, which is today's rule) minus the month's norm 147.40 (11 x 7.40 + 11 x 6.00).
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-7 (the evaluated-set filter disabled, TASK-14522) lets
    /// the AlignedWindow rules into the planner, which then refuses the split month, so the shim throws
    /// before any figure exists. MB-3 (Unit; the per-plan lane covers only the first EMPLOYED segment,
    /// <c>SplitMonthPcsTests.TwoEmployedSegments_TimelinePiecesCoverBothSegments</c>) drops 16-30 Sep from the
    /// timeline: the week's lines and the flex figure change.
    /// </summary>
    [Fact]
    public async Task SplitWeek_FractionChangeWed16Sep_HK_Pays267And453_Dated20Sep()
    {
        const string emp = "EMP-S145-WS1";
        await SeedEmployeeAsync(emp, "HK");
        await SupersedeProfileAsync(emp, fraction: 0.811m, position: null, effectiveFrom: Sep16);
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Sep30, SplitWeekHours);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30);

        Assert.Multiple(
            () => Assert.Equal(
                new[] { ("OVERTIME_100", 4.53m, Sep20), ("OVERTIME_50", 2.67m, Sep20) },
                OvertimeLines(september.Result)),
            () => Assert.Equal(
                new[] { ("SLS_0210", 2.67m), ("SLS_0220", 4.53m) },
                ExportedOvertime(september.Result)),
            () => Assert.Single(september.Recorder.Timeline),
            () => Assert.Equal(7.20m, september.Recorder.Timeline.Single().FlexNewBalance()));
    }

    /// <summary>
    /// WS2. The AC twin of WS1: the same days, AC pays the whole excess over the norm as MERARBEJDE: 40.00 -
    /// 32.80 = 7.20, dated Sunday 20 Sep, and nothing else.
    ///
    /// Red conditions (Docker-gated, CI-verified): as WS1 (MB-7; MB-3 at Unit level).
    /// </summary>
    [Fact]
    public async Task SplitWeek_FractionChangeWed16Sep_AC_Merarbejde720()
    {
        const string emp = "EMP-S145-WS2";
        await SeedEmployeeAsync(emp, "AC");
        await SupersedeProfileAsync(emp, fraction: 0.811m, position: null, effectiveFrom: Sep16);
        await SeedApprovedAsync(emp, "AC", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "AC", Aug31, Sep30, SplitWeekHours);

        var september = await CalculateAsync(emp, ProfileOf(emp, "AC", 1.0m), Sep01, Sep30);

        Assert.Equal(
            new[] { ("MERARBEJDE", 7.20m, Sep20) },
            OvertimeLines(september.Result));
    }

    // =====================================================================
    // QA1 / QP1 - QUAL-150 end to end (the wage-type key per part)
    // =====================================================================

    /// <summary>
    /// QA1 (refinement :693-697, danish-agreements.md:145-147). HK becomes AC effective MONDAY 16 March 2026
    /// (a Monday change starts a clean week, so the month is payable); February APPROVED. 8 h on 9-13 Mar and
    /// 16-20 Mar, every other weekday (the context days 23-27 Feb included) at the norm 7.40. The week 9-15
    /// Mar is HK's: 40.00 against norm 37.00 and 100% threshold 40.00 pays OVERTIME_50 3.00 dated Sunday 15
    /// Mar, exported under SLS_0210. The week 16-22 Mar is AC's: 40.00 - 37.00 = MERARBEJDE 3.00 dated
    /// Sunday 22 Mar, exported under SLS_0310. The AC mapping row the shared fixture lacks is seeded here
    /// (MERARBEJDE, OK24, AC). March is entirely OK24.
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-2 (every EMPLOYED segment hydrated with the FIRST
    /// EMPLOYED segment's wage-type key, <c>PeriodCalculationService.cs:1675-1677</c>) maps the AC part's
    /// MERARBEJDE under HK, where no MERARBEJDE row exists, so the line is dropped and the exact export-lines
    /// assertion trips. MB-7 (the filter disabled) makes the shim throw (the planner refuses the split).
    /// </summary>
    [Fact]
    public async Task AgreementChangeMon16Mar_HkOvertimeThenAcMerarbejde_MapsSls0210ThenSls0310()
    {
        const string emp = "EMP-S145-QA1";
        await SeedEmployeeAsync(emp, "HK", okVersion: "OK24");
        await SupersedeAgreementCodeAsync(emp, "AC", Mar16);
        await SeedMappingAsync("MERARBEJDE", "SLS_0310", "OK24", "AC", position: "");
        await SeedApprovedAsync(emp, "HK", Feb01, Feb28);
        await SeedWeekdaysAsync(emp, "HK", Feb23, Mar31, d =>
            (d >= Mar09 && d <= Mar13) || (d >= Mar16 && d <= Mar20) ? 8.0m : DayNorm, okVersion: "OK24");

        var march = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m, okVersion: "OK24"), Mar01, Mar31);

        Assert.Multiple(
            () => Assert.Equal(
                new[] { ("MERARBEJDE", 3.00m, Mar22), ("OVERTIME_50", 3.00m, Mar15) },
                OvertimeLines(march.Result)),
            () => Assert.Equal(
                new[] { ("MERARBEJDE", "SLS_0310", 3.00m), ("OVERTIME_50", "SLS_0210", 3.00m) },
                ExportedByTimeType(march.Result, "MERARBEJDE", "OVERTIME_50")));
    }

    /// <summary>
    /// QP1 (refinement :693-697). AC, the position becomes DEPARTMENT_HEAD on Monday 16 March 2026 (the only
    /// position with an override, <c>PositionOverrideConfigs.cs:27-36</c>). A position-specific MERARBEJDE
    /// mapping with a test-only code (SLS_0310_DEPTHEAD) is seeded beside the base row (SLS_0310), both
    /// OK24. The same hours as QA1: merarbejde 3.00 dated 15 Mar (before the change) and 3.00 dated 22 Mar
    /// (after it). The line before the change maps under the BASE row, the line after it under DEPARTMENT_HEAD's
    /// own row; the export lines' period starts tell the two parts apart (1 Mar, 16 Mar).
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-2 (every segment hydrated with the first segment's key)
    /// maps both lines under the base row, so the DEPARTMENT_HEAD code never appears and the exact
    /// export-lines assertion trips. MB-7 (the filter disabled) makes the shim throw.
    /// </summary>
    [Fact]
    public async Task PositionChangeMon16Mar_MapsEachSegmentUnderItsOwnPositionWageTypes()
    {
        const string emp = "EMP-S145-QP1";
        await SeedEmployeeAsync(emp, "AC", okVersion: "OK24");
        await SupersedeProfileAsync(emp, fraction: 1.0m, position: "DEPARTMENT_HEAD", effectiveFrom: Mar16);
        await SeedMappingAsync("MERARBEJDE", "SLS_0310", "OK24", "AC", position: "");
        await SeedMappingAsync("MERARBEJDE", "SLS_0310_DEPTHEAD", "OK24", "AC", position: "DEPARTMENT_HEAD");
        await SeedApprovedAsync(emp, "AC", Feb01, Feb28);
        await SeedWeekdaysAsync(emp, "AC", Feb23, Mar31, d =>
            (d >= Mar09 && d <= Mar13) || (d >= Mar16 && d <= Mar20) ? 8.0m : DayNorm, okVersion: "OK24");

        var march = await CalculateAsync(emp, ProfileOf(emp, "AC", 1.0m, okVersion: "OK24"), Mar01, Mar31);

        var merarbejde = march.Result.ExportLines
            .Where(l => l.SourceTimeType == "MERARBEJDE")
            .OrderBy(l => l.PeriodStart)
            .Select(l => (l.WageType, l.Hours, l.PeriodStart))
            .ToList();
        Assert.Multiple(
            () => Assert.Equal(
                new[] { ("MERARBEJDE", 3.00m, Mar15), ("MERARBEJDE", 3.00m, Mar22) },
                OvertimeLines(march.Result)),
            () => Assert.Equal(
                new[] { ("SLS_0310", 3.00m, Mar01), ("SLS_0310_DEPTHEAD", 3.00m, Mar16) },
                merarbejde));
    }

    // =====================================================================
    // FX1 - FX3 - flex over a split month
    // =====================================================================

    /// <summary>
    /// FX1 (refinement :707, R9). HK, opening balance 95. A profile row that keeps the VALUES but is dated
    /// after week 1 (Monday 7 September) makes the split. Week 1 (1-4 Sep in the period) is worked at 9.90 h,
    /// +10.00 over the norm (4 x 2.50); week 14-18 Sep is worked at 5.80 h (-8.00 under the norm); every
    /// other weekday is at the norm 7.40 (31 August too). Flex is settled ONCE for the month: 95 + 10.00 -
    /// 8.00 = <b>97.00</b>, below the HK cap 100, so there is NO FLEX_PAYOUT. (The retired per-segment carry
    /// closed segment 1 at 95 + 10 = 105, capped it at 100 and PAID 5 out, then carried 100 into the second
    /// part; the new design pays nothing.)
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-7 (the filter disabled) makes the shim throw. MB-3 (Unit;
    /// the timeline covers only the first part) drops 7-30 Sep from the flex: the balance is 100.00 with a 5.00
    /// payout (95 + 10 = 105 capped), tripping both assertions.
    /// </summary>
    [Fact]
    public async Task Flex_SplitAfterWeek1_95Plus10Minus8_ClosesAt97_NoPayout()
    {
        const string emp = "EMP-S145-FX1";
        await SeedEmployeeAsync(emp, "HK");
        await SupersedeProfileAsync(emp, fraction: 1.0m, position: null, effectiveFrom: Sep07);
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Sep30, d =>
            d >= Sep01 && d <= Sep04 ? 9.90m
            : d >= Sep14 && d <= Sep18 ? 5.80m
            : DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30, previousFlex: 95m);

        var timeline = september.Recorder.Timeline.Single();
        Assert.Multiple(
            // Non-vacuity: the month really was split at 7 September.
            () => Assert.Contains(timeline.Pieces(), p => p.From == Sep07),
            () => Assert.Equal(97.00m, timeline.FlexNewBalance()),
            () => Assert.Empty(LineItemsOf(september.Result, "FLEX_PAYOUT")));
    }

    /// <summary>
    /// FX2 (refinement :708). AC, the position becomes DEPARTMENT_HEAD on Monday 14 September 2026 (AC cap 150;
    /// DEPARTMENT_HEAD cap 200). Opening balance 170; 21-25 Sep worked 11.40 h (+4.00 a day = +20.00), every
    /// other weekday at the norm. The month's flex 170 + 20 = 190 is settled on the LAST day at the cap in
    /// force that day, 200: NewBalance <b>190.00</b>, no FLEX_PAYOUT. (A cap taken from the first part, AC's
    /// 150, would close at 150 and pay 40.) <b>C-B5: flex figures only, never a wage type.</b>
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-7 (the filter disabled) makes the shim throw. MB-3 (Unit;
    /// the timeline covers only the first part) leaves the DEPARTMENT_HEAD days out of the timeline: the
    /// balance is 170.00 (no hours judged), not 190.00.
    /// </summary>
    [Fact]
    public async Task Flex_AcToDepartmentHeadMidMonth_Cap200Applies()
    {
        const string emp = "EMP-S145-FX2";
        await SeedEmployeeAsync(emp, "AC");
        await SupersedeProfileAsync(emp, fraction: 1.0m, position: "DEPARTMENT_HEAD", effectiveFrom: Sep14);
        await SeedApprovedAsync(emp, "AC", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "AC", Aug31, Sep30, d =>
            d >= Sep21 && d <= Sep25 ? 11.40m : DayNorm);

        var september = await CalculateAsync(emp, ProfileOf(emp, "AC", 1.0m), Sep01, Sep30, previousFlex: 170m);

        Assert.Multiple(
            () => Assert.Equal(190.00m, september.Recorder.Timeline.Single().FlexNewBalance()),
            () => Assert.Empty(LineItemsOf(september.Result, "FLEX_PAYOUT")));
    }

    /// <summary>
    /// FX3 (R-62; refinement :709, "an absence day after a mid-month fraction change credits 6.00").
    /// HK, fraction 1.0 to 0.811 effective Wed 16 Sep 2026; August APPROVED; every weekday at its per-day norm
    /// except 21, 22, 23 and 25 September at 8.00 h. Thursday 24 September carries a VACATION row seeded
    /// directly into <c>absences_projection</c> with <c>hours = 0</c> (source SKEMA, no time entry), which the
    /// schema accepts (<c>hours NUMERIC(8,4) NOT NULL</c>, no lower bound). A zero-hour absence stands for one
    /// working day at the fraction IN FORCE THAT DAY (R35): <b>6.00</b> after the change (a one-profile
    /// evaluation under the first part's fraction would give 7.40).
    /// <list type="bullet">
    /// <item>the absence rule's VACATION line dated 24 Sep carries 6.00;</item>
    /// <item>the month's flex closes at <b>8.00</b> from an opening 0. Flex settles over every in-period
    /// covered day: worked 149.40 = 81.40 (1-15 Sep, 11 x 7.40) + 36.00 (16-18 and 28-30 Sep at 6.00) + 32.00
    /// (21, 22, 23 and 25 Sep at 8.00); plus the 6.00 credit; minus the month's norm 147.40 (11 x 7.40 + 11 x
    /// 6.00) = 8.00. The surplus sits in 21-25 Sep only because every other weekday is at its norm (C-B8).</item>
    /// </list>
    /// Only flex and the absence line are asserted (C-B5). Skema refuses 0 hours (R24), so the row is
    /// fixture-only.
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-3 (Unit; segment 2 uncovered) leaves 16-30 Sep out of
    /// the timeline, so there is no credit and no norm for them (1-15 Sep are exactly at norm) and the balance
    /// closes at 0.00 rather than 8.00. MB-7 (the filter disabled) makes the shim throw.
    /// </summary>
    [Fact]
    public async Task Flex_FractionChangeWed16Sep_ZeroHourVacationThu24Sep_CreditsSixAndFlexCloses800()
    {
        const string emp = "EMP-S145-FX3";
        await SeedEmployeeAsync(emp, "HK");
        await SupersedeProfileAsync(emp, fraction: 0.811m, position: null, effectiveFrom: Sep16);
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Sep30, d =>
            d == Sep21 || d == Sep22 || d == Sep23 || d == Sep25 ? 8.00m
            : d < Sep16 ? DayNorm
            : DayNormAfter, skip: d => d == Sep24);
        await SeedZeroHourSkemaAbsenceAsync(emp, "HK", Sep24, "VACATION");

        var september = await CalculateAsync(emp, ProfileOf(emp, "HK", 1.0m), Sep01, Sep30);

        var vacation = LineItemsOf(september.Result, "VACATION");
        Assert.Multiple(
            () => Assert.Equal(new[] { (6.00m, Sep24) }, vacation.Select(l => (l.Hours, l.Date)).ToArray()),
            () => Assert.Equal(8.00m, september.Recorder.Timeline.Single().FlexNewBalance()));
    }

    // =====================================================================
    // RP-B1 - a split month replays byte-identically from its frozen pieces (R-54)
    // =====================================================================

    /// <summary>
    /// RP-B1 (R-54; the auditability proof path). WS1's September forward, then the live state is changed so
    /// that a re-read WOULD differ, then the month is replayed from its manifest alone:
    /// <list type="number">
    /// <item>forward: keep <c>outcome.ManifestId</c>, the serialized <c>Result</c>, the recorder's first
    /// timeline request and the count of events on the employee's manifest and period streams;</item>
    /// <item>change the live state: the fraction row of 16 September is changed again (0.811 to 0.500) and one
    /// of the 14-18 September projection rows is superseded to 9.00 h;</item>
    /// <item><c>pcs.ReplayAsync(manifestId, authorizationHeader: Bearer ...)</c> (R-99: the Rule Engine's
    /// timeline endpoint requires an authenticated caller, and PCS sends the header only when it is given
    /// one; without it the engine answers 401 and the fact is red on correct code);</item>
    /// <item>assert: the replayed <c>Result</c> serializes byte-identically to the forward one; the replay's
    /// timeline request carries the FROZEN pieces (1-15 September at 1.0, 16-30 September at 0.811; the
    /// month also holds the context piece for Monday 31 August first, so the whole list is compared with the
    /// forward call's pieces, never with a two-element list); each of the three timeline rows is ONE merged
    /// row; and no new <c>SegmentManifestCreated</c> or <c>PeriodCalculationCompleted</c> event was appended.</item>
    /// </list>
    /// One recorder and ONE PCS serve both steps (R-87), so <c>Timeline[0]</c> is the forward call and
    /// <c>Timeline[1]</c> the replay.
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-13 (EB-1: on replay the timeline is handed only the
    /// frozen pieces that start on or before the first EMPLOYED segment's start) drops the 16-30 September
    /// piece from the replay request, tripping the frozen-pieces assertion and the byte-identity assertion.
    /// MB-7 (the filter disabled) reaches the FORWARD step: the shim throws.
    /// </summary>
    [Fact]
    public async Task Replay_SplitMonth_FractionChangeWed16Sep_ByteIdentical_FrozenPieces_NoNewAuditEvents()
    {
        const string emp = "EMP-S145-RPB1";
        await SeedEmployeeAsync(emp, "HK");
        await SupersedeProfileAsync(emp, fraction: 0.811m, position: null, effectiveFrom: Sep16);
        await SeedApprovedAsync(emp, "HK", Aug01, Aug31);
        await SeedWeekdaysAsync(emp, "HK", Aug31, Sep30, SplitWeekHours);

        // One recorder, one PCS, for both steps (R-87). The forward step plans through the planless shim.
        var recorder = new RecordingHandler(_engine.Server.CreateHandler());
        var pcs = BuildPcs(recorder);
        var bearer = "Bearer " + MintGlobalAdminToken();
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var forward = await pcs.CalculateWithOutcomeAsync(
            ProfileOf(emp, "HK", 1.0m), Sep01, Sep30, previousFlexBalance: 0m,
            authorizationHeader: bearer);
#pragma warning restore CS0618
        Assert.True(forward.Result.Success, forward.Result.ErrorMessage);

        var forwardJson = JsonSerializer.Serialize(forward.Result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var forwardPieces = recorder.Timeline.First().Pieces();
        var manifestStream = $"segment-manifest-{forward.ManifestId}";
        var periodStream = $"period-calc-{emp}-2026-09-01";
        var manifestEventsBefore = (await _store.ReadStreamAsync(manifestStream)).Count;
        var periodEventsBefore = (await _store.ReadStreamAsync(periodStream)).Count;
        Assert.Equal(1, manifestEventsBefore); // non-vacuity: the forward step wrote its manifest and period events
        Assert.Equal(1, periodEventsBefore);

        // Change the live state so that a re-read would differ.
        await ExecuteAsync(
            $"UPDATE employee_profiles SET part_time_fraction = 0.500 WHERE employee_id = '{emp}' AND effective_from = DATE '2026-09-16'");
        await SupersedeTimeEntryAsync(emp, Sep16, 9.00m);

        var replay = await pcs.ReplayAsync(forward.ManifestId, authorizationHeader: bearer);

        Assert.Equal(2, recorder.Timeline.Count); // [0] the forward call, [1] the replay
        var replayPieces = recorder.Timeline[1].Pieces();
        var replayJson = JsonSerializer.Serialize(replay, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Multiple(
            () => Assert.True(replay.Success, replay.ErrorMessage),
            () => Assert.Equal(forwardJson, replayJson),
            // The frozen pieces, as named: 1-15 September at 1.0 and 16-30 September at 0.811.
            () => Assert.Contains(replayPieces, p => p.From == Sep01 && p.To == Sep15 && p.PartTimeFraction == 1.0m),
            () => Assert.Contains(replayPieces, p => p.From == Sep16 && p.To == Sep30 && p.PartTimeFraction == 0.811m),
            () => Assert.Equal(forwardPieces, replayPieces),
            // Each timeline rule is ONE merged row, successful.
            () => Assert.Single(replay.RuleResults, r => r.RuleId == "OVERTIME_CALC" && r.Success),
            () => Assert.Single(replay.RuleResults, r => r.RuleId == "NORM_CHECK_37H" && r.Success),
            () => Assert.Single(replay.RuleResults, r => r.RuleId == "FLEX_BALANCE" && r.Success));

        // No new audit event: the replay appends nothing to the manifest or period streams.
        Assert.Equal(manifestEventsBefore, (await _store.ReadStreamAsync(manifestStream)).Count);
        Assert.Equal(periodEventsBefore, (await _store.ReadStreamAsync(periodStream)).Count);
    }

    // =====================================================================
    // Hours
    // =====================================================================

    /// <summary>
    /// The Q2 split-week hours (WS1, WS2, RP-B1): 14-18 Sep at 8.00 h, every other weekday at its per-day norm
    /// (7.40 before the change on 16 Sep, 6.00 from it). 31 August carries 7.40 (C-B8).
    /// </summary>
    private static decimal SplitWeekHours(DateOnly d) =>
        d >= Sep14 && d <= Sep18 ? 8.0m
        : d < Sep16 ? DayNorm
        : DayNormAfter;

    // =====================================================================
    // Calculation harness (copied from WeeklyPayrollTests: the two files may not share a private harness)
    // =====================================================================

    private sealed record MonthRun(PeriodCalculationResult Result, RecordingHandler Recorder);

    /// <summary>One PCS built per calculation over a fresh recording wrapper of the in-process real engine,
    /// planning through the PLANLESS SHIM with the full live set (C-B6).</summary>
    private async Task<MonthRun> CalculateAsync(
        string employeeId,
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousFlex = 0m)
    {
        var recorder = new RecordingHandler(_engine.Server.CreateHandler());
        var pcs = BuildPcs(recorder);
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var outcome = await pcs.CalculateWithOutcomeAsync(
            profile, periodStart, periodEnd, previousFlexBalance: previousFlex,
            authorizationHeader: "Bearer " + MintGlobalAdminToken());
#pragma warning restore CS0618
        Assert.True(outcome.Result.Success, $"{employeeId} {periodStart:yyyy-MM}: {outcome.Result.ErrorMessage}");
        return new MonthRun(outcome.Result, recorder);
    }

    /// <summary>The production DI shape, hand-built: every optional dependency the Payroll host registers, the
    /// REAL hours reader and the REAL approval adapter (R26, R25).</summary>
    private PeriodCalculationService BuildPcs(HttpMessageHandler engineHandler)
    {
        var wtmRepo = new WageTypeMappingRepository(_db);
        var mappingService = new PayrollMappingService(_db, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceUrls:RuleEngine"] = "http://rule-engine.test" })
            .Build();
        var agreementCodes = new UserAgreementCodeRepository(_db);

        return new PeriodCalculationService(
            new SingleClientFactory(engineHandler),
            mappingService,
            _store,
            _db,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new LiveClassifications(),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_db, agreementCodes),
            employmentWindowResolver: new EmploymentWindowResolver(_db),
            employeeProfileRepo: new EmployeeProfileRepository(_db),
            userAgreementCodeRepo: agreementCodes,
            hoursReader: new ProjectionHoursReader(_db,
                new TimeEntryProjectionRepository(_db), new AbsenceProjectionRepository(_db)),
            priorPeriodApprovalReader: new ApprovalPeriodPriorPeriodReader(new ApprovalPeriodRepository(_db)));
    }

    // =====================================================================
    // Reading results
    // =====================================================================

    private static List<CalculationLineItem> LineItemsOf(PeriodCalculationResult result, string timeType) =>
        result.RuleResults.SelectMany(r => r.LineItems).Where(li => li.TimeType == timeType).ToList();

    /// <summary>Every overtime / merarbejde line, ordered by time type (ordinal) then date.</summary>
    private static List<(string TimeType, decimal Hours, DateOnly Date)> OvertimeLines(PeriodCalculationResult result) =>
        result.RuleResults
            .SelectMany(r => r.LineItems)
            .Where(li => li.TimeType is "OVERTIME_50" or "OVERTIME_100" or "MERARBEJDE")
            .OrderBy(li => li.TimeType, StringComparer.Ordinal).ThenBy(li => li.Date)
            .Select(li => (li.TimeType, li.Hours, li.Date))
            .ToList();

    /// <summary>The export lines that carry overtime: (wage type, hours), ordered by wage type.</summary>
    private static List<(string WageType, decimal Hours)> ExportedOvertime(PeriodCalculationResult result) =>
        result.ExportLines
            .Where(l => l.SourceTimeType is "OVERTIME_50" or "OVERTIME_100")
            .OrderBy(l => l.WageType, StringComparer.Ordinal)
            .Select(l => (l.WageType, l.Hours))
            .ToList();

    /// <summary>The export lines of the given source time types: (source time type, wage type, hours), ordered by
    /// source time type then wage type.</summary>
    private static List<(string SourceTimeType, string WageType, decimal Hours)> ExportedByTimeType(
        PeriodCalculationResult result, params string[] timeTypes) =>
        result.ExportLines
            .Where(l => l.SourceTimeType is not null && timeTypes.Contains(l.SourceTimeType))
            .OrderBy(l => l.SourceTimeType, StringComparer.Ordinal).ThenBy(l => l.WageType, StringComparer.Ordinal)
            .Select(l => (l.SourceTimeType!, l.WageType, l.Hours))
            .ToList();

    // =====================================================================
    // Dates
    // =====================================================================

    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb23 = new(2026, 2, 23);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar09 = new(2026, 3, 9);
    private static readonly DateOnly Mar13 = new(2026, 3, 13);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);
    private static readonly DateOnly Mar20 = new(2026, 3, 20);
    private static readonly DateOnly Mar22 = new(2026, 3, 22);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Aug01 = new(2026, 8, 1);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep04 = new(2026, 9, 4);
    private static readonly DateOnly Sep07 = new(2026, 9, 7);
    private static readonly DateOnly Sep14 = new(2026, 9, 14);
    private static readonly DateOnly Sep15 = new(2026, 9, 15);
    private static readonly DateOnly Sep16 = new(2026, 9, 16);
    private static readonly DateOnly Sep18 = new(2026, 9, 18);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);
    private static readonly DateOnly Sep21 = new(2026, 9, 21);
    private static readonly DateOnly Sep22 = new(2026, 9, 22);
    private static readonly DateOnly Sep23 = new(2026, 9, 23);
    private static readonly DateOnly Sep24 = new(2026, 9, 24);
    private static readonly DateOnly Sep25 = new(2026, 9, 25);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);

    private static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            yield return d;
        }
    }

    // =====================================================================
    // Seeding
    // =====================================================================

    private static EmploymentProfile ProfileOf(
        string employeeId, string agreementCode, decimal fraction, string? position = null, string okVersion = "OK26") => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = fraction,
        Position = position,
        OrgId = OrgId,
    };

    /// <summary>users + employee_profiles + user_agreement_codes with the history-covering '0001-01-01' rows (the
    /// dated record equals the request profile at the period start) + the organization.</summary>
    private async Task SeedEmployeeAsync(string employeeId, string agreementCode, string okVersion = "OK26")
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, agreementCode, okVersion, partTimeFraction: 1.0m);
    }

    /// <summary>Closes the live profile row at <paramref name="effectiveFrom"/> (end-exclusive) and inserts the
    /// successor with the given fraction and position.</summary>
    private async Task SupersedeProfileAsync(string employeeId, decimal fraction, string? position, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            "UPDATE employee_profiles SET effective_to = @from WHERE employee_id = @id AND effective_to IS NULL", conn, tx))
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
            VALUES (gen_random_uuid(), @id, @fraction, @position, @from, NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @id))
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("fraction", fraction);
            insert.Parameters.AddWithValue("position", (object?)position ?? DBNull.Value);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    /// <summary>Closes the live agreement-code row at <paramref name="effectiveFrom"/> (end-exclusive) and
    /// inserts the successor.</summary>
    private async Task SupersedeAgreementCodeAsync(string employeeId, string newAgreementCode, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            "UPDATE user_agreement_codes SET effective_to = @from WHERE user_id = @id AND effective_to IS NULL", conn, tx))
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

    /// <summary>A wage-type mapping row for an (agreement, OK version, position); position '' is the generic row.</summary>
    private async Task SeedMappingAsync(string timeType, string wageType, string okVersion, string agreementCode, string position)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from)
            VALUES (@t, @w, @ok, @ac, @pos, 'S145 TASK-14524 test row', DATE '2020-01-01')
            ON CONFLICT (time_type, ok_version, agreement_code, position) WHERE effective_to IS NULL DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("t", timeType);
        cmd.Parameters.AddWithValue("w", wageType);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("pos", position);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>An APPROVED monthly approval period (R26: the previous month, seeded at repository level).</summary>
    private async Task SeedApprovedAsync(string employeeId, string agreementCode, DateOnly periodStart, DateOnly periodEnd)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                NOW(), @emp, 'ROOT-S145', NOW(), @agreement, 'OK26')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("agreement", agreementCode);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>One RAW time-entry projection row per WEEKDAY in [from, to] (a current TIME_POST row), except the
    /// days <paramref name="skip"/> names.</summary>
    private async Task SeedWeekdaysAsync(
        string employeeId, string agreementCode, DateOnly from, DateOnly to, Func<DateOnly, decimal> hoursFor,
        string okVersion = "OK26", Func<DateOnly, bool>? skip = null)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        foreach (var date in Weekdays(from, to))
        {
            if (skip is not null && skip(date))
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, @hours, @agreement, @ok, FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", date);
            cmd.Parameters.AddWithValue("hours", hoursFor(date));
            cmd.Parameters.AddWithValue("agreement", agreementCode);
            cmd.Parameters.AddWithValue("ok", okVersion);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    /// <summary>A SKEMA absence row with <c>hours = 0</c> (fixture-only: Skema refuses 0 hours, R24; the schema
    /// accepts it).</summary>
    private async Task SeedZeroHourSkemaAbsenceAsync(string employeeId, string agreementCode, DateOnly date, string absenceType)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, outbox_id, source)
            VALUES
                (gen_random_uuid(), @emp, @date, @type, 0, NULL, @agreement, 'OK26', NOW(), @outbox, 'SKEMA')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("type", absenceType);
        cmd.Parameters.AddWithValue("agreement", agreementCode);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>Supersedes the day's current time-entry row: a new row with <paramref name="hours"/> and a later
    /// outbox id, and the old row marked <c>superseded_by_outbox_id</c> = the new one (the old row stays
    /// readable by its event id, which is what a replay does).</summary>
    private async Task SupersedeTimeEntryAsync(string employeeId, DateOnly date, decimal hours)
    {
        var newOutbox = _outboxSeq++;
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var mark = new NpgsqlCommand(
            """
            UPDATE time_entries_projection SET superseded_by_outbox_id = @new
            WHERE employee_id = @emp AND date = @date AND superseded_by_outbox_id IS NULL
            """, conn, tx))
        {
            mark.Parameters.AddWithValue("new", newOutbox);
            mark.Parameters.AddWithValue("emp", employeeId);
            mark.Parameters.AddWithValue("date", date);
            Assert.Equal(1, await mark.ExecuteNonQueryAsync());
        }
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, 'HK', 'OK26', FALSE, NOW(), @outbox)
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("emp", employeeId);
            insert.Parameters.AddWithValue("date", date);
            insert.Parameters.AddWithValue("hours", hours);
            insert.Parameters.AddWithValue("outbox", newOutbox);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): the only caller passes a compile-time test constant
        // interpolated with a compile-time employee id (RP-B1's live-state change) - never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    // =====================================================================
    // The in-process real engine, with a recording wrapper
    // =====================================================================

    private static string MintGlobalAdminToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: "ROOT-S145",
            name: "ROOT-S145",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    /// <summary>The REAL Rule Engine hosted in-process (copied from <c>WeeklyPayrollTests</c>). JWT settings go into
    /// host configuration so the bearer token PCS forwards validates against the dev key.</summary>
    private sealed class RuleEngineHost : WebApplicationFactory<RuleRegistry>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = PayrollHostFactory.DevSigningKey,
                    ["Jwt:Issuer"] = "statstid",
                    ["Jwt:Audience"] = "statstid",
                }));
            return base.CreateHost(builder);
        }
    }

    /// <summary>One <c>/api/rules/evaluate-timeline</c> call: the request and the response exactly as sent and
    /// received (the real engine's own answer).</summary>
    private sealed record TimelineCall(string RequestJson, string ResponseJson)
    {
        public sealed record Piece(DateOnly From, DateOnly To, decimal PartTimeFraction, string AgreementCode, string? Position);

        public IReadOnlyList<Piece> Pieces()
        {
            using var doc = JsonDocument.Parse(RequestJson);
            return doc.RootElement.GetProperty("pieces").EnumerateArray()
                .Select(p => new Piece(
                    DateOnly.Parse(p.GetProperty("from").GetString()!, CultureInfo.InvariantCulture),
                    DateOnly.Parse(p.GetProperty("to").GetString()!, CultureInfo.InvariantCulture),
                    p.GetProperty("partTimeFraction").GetDecimal(),
                    p.GetProperty("agreementCode").GetString()!,
                    p.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.String ? pos.GetString() : null))
                .ToList();
        }

        public decimal FlexNewBalance()
        {
            using var doc = JsonDocument.Parse(ResponseJson);
            return doc.RootElement.GetProperty("flex").GetProperty("newBalance").GetDecimal();
        }
    }

    /// <summary>Forwards every call to the real engine and keeps the timeline calls (200 replies only).</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly List<TimelineCall> _timeline = new();

        public RecordingHandler(HttpMessageHandler inner) : base(inner) { }

        public IReadOnlyList<TimelineCall> Timeline
        {
            get { lock (_timeline) return _timeline.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isTimeline = request.RequestUri?.AbsolutePath.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal) == true;
            var requestJson = isTimeline && request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;

            var response = await base.SendAsync(request, cancellationToken);

            if (isTimeline && response.StatusCode == HttpStatusCode.OK)
            {
                await response.Content.LoadIntoBufferAsync();
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                lock (_timeline) _timeline.Add(new TimelineCall(requestJson ?? "{}", responseJson));
            }
            return response;
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class LiveClassifications : IRuleClassificationProvider
    {
        public IReadOnlyList<RuleClassification> GetClassifications() => LiveSet;
    }
}
