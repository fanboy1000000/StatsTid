using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Worklist;

/// <summary>
/// S148 / TASK-14821-T (AC-PIL-h (1)-(5), (7)-(11); P-27, B-3, NR-17, NR-19, NR-P2, NR-P12, NR-P20) - the employment
/// start date, the end date and a settlement reversal that corrects the end date tell HR.
///
/// <para>
/// <b>In plain language.</b> An employment start or end date decides which days count as employed, and the end
/// date also decides when a leaver's holiday is settled. When HR moves one of them into a month already sent to
/// payroll, or into a holiday year already settled, the figures sent or settled may be stale, so that month or year
/// now lands on HR's worklist with the reason "employment-window change", dated from the earliest day the change
/// can have affected, and pointing at the event that caused it. An end date saved again unchanged lists nothing.
/// </para>
///
/// <para>
/// <b>Worked example (part (8)).</b> Employee B has no recorded start date, June 2026 is already sent to payroll
/// and her registrations begin on the 15th. HR sets her start to Monday 15 June 2026. Before: nothing on the
/// worklist. After: June is listed, dated Monday 1 June 2026 (June opens on a Monday, the earliest day an unknown
/// start can have affected).
/// </para>
///
/// <para>
/// <b>Fixture.</b> A Backend host with the clock fixed at Tuesday 10 November 2026 and no leaver-flip job, one fresh
/// database per fact. The actor is a LocalHR scoped to STY01 and is not the subject. Each person is seeded by
/// <c>RegressionSeed</c> (users.version 1) with the dates set directly. Every listed row is asserted by its key,
/// its single trigger of kind <c>EMPLOYMENT_WINDOW_CHANGE</c> (a literal, never the production constant), its
/// <c>effectiveFrom</c>, its <c>eventId</c> (read from <c>outbox_events</c>) and that it derives no recalculation block
/// (<c>BackdateWorklistDerivation.RecalcBlockedBy</c>
/// is empty). Every expected value is the brief's, not the code's. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentWindowHandOffTests : IAsyncLifetime
{
    private const string KindLiteral = "EMPLOYMENT_WINDOW_CHANGE";
    private const string ActorId = "hr_s148_hand_off";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;
    private long _outboxSeq = 950_000;

    private string Cs => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
        // No leaver-flip job: the leavers seeded here are inactive with a past end date and must stay as seeded.
        _factory = new StatsTidWebApplicationFactory(Cs, withoutLeaverJob: true);
        _host = _factory.WithFixedToday(Clock);
        _ = _host.CreateClient(); // boot seeders before any fact seeds (PAT-008)
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ─── (1)-(4): the dated sides ────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI)</b> (part (1)). Employment starts Sunday 15 March 2026; March 2026 exported; the start-date PUT to
    /// Monday 2 March 2026 answers 200; one EXPORTED_MONTH row (2026, 3), <c>effectiveFrom</c> 2026-03-02, the
    /// start-date event's id.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> The start-date PUT writes no worklist row:
    /// <c>Assert.Single(exportedRows)</c> fails with zero rows.</para>
    /// </summary>
    [Fact]
    public async Task StartMovedFromSun15MarToMon2Mar2026_MarchExported_ListsMarch_EffectiveFrom2March_StartDateEvent()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: new DateOnly(2026, 3, 15), end: null);
        await SeedExportRecordAsync(emp, 2026, 3);

        var rsp = await PutDateAsync(emp, "employment-start-date", new { employmentStartDate = "2026-03-02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var march = Assert.Single(rows, r => r.Kind == "EXPORTED_MONTH");
        Assert.Equal((2026, 3), (march.Year, march.Month));
        AssertSingleTrigger(march, new DateOnly(2026, 3, 2), await EventIdAsync(emp, "EmployeeEmploymentStartDateSet"));
        Assert.DoesNotContain(rows, r => r.Kind == "SETTLED_YEAR");
    }

    /// <summary>
    /// <b>F (CI)</b> (part (2)). No registrations after the 15th and no settlement in the span; March 2026 exported;
    /// the end-date PUT from Tuesday 31 March to Sunday 15 March 2026 answers 200; March listed, <c>effectiveFrom</c>
    /// 2026-03-15, the end-date event's id.
    /// <para><b>Red conditions.</b> The end-date PUT writes no worklist row: the single-row assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task EndMovedFromTue31MarToSun15Mar2026_MarchExported_ListsMarch_EffectiveFrom15March_EndDateEvent()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: new DateOnly(2026, 3, 31));
        await SeedExportRecordAsync(emp, 2026, 3);

        var rsp = await PutDateAsync(emp, "employment-end-date", new { employmentEndDate = "2026-03-15" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var march = Assert.Single(rows, r => r.Kind == "EXPORTED_MONTH");
        Assert.Equal((2026, 3), (march.Year, march.Month));
        AssertSingleTrigger(march, new DateOnly(2026, 3, 15), await EventIdAsync(emp, "EmployeeEmploymentEndDateSet"));
        Assert.DoesNotContain(rows, r => r.Kind == "SETTLED_YEAR");
    }

    /// <summary>
    /// <b>F (CI)</b> (part (3)). No registrations after 30 June, no settlement in the span; June 2026 exported; the
    /// end-date PUT from none to Tuesday 30 June 2026 answers 200; June listed, <c>effectiveFrom</c> 2026-06-30.
    /// <para><b>Red conditions.</b> No worklist row is written: the single-row assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task EndSetFromNoneToTue30Jun2026_JuneExported_ListsJune_EffectiveFrom30June()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: null);
        await SeedExportRecordAsync(emp, 2026, 6);

        var rsp = await PutDateAsync(emp, "employment-end-date", new { employmentEndDate = "2026-06-30" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var june = Assert.Single(rows, r => r.Kind == "EXPORTED_MONTH");
        Assert.Equal((2026, 6), (june.Year, june.Month));
        AssertSingleTrigger(june, new DateOnly(2026, 6, 30), await EventIdAsync(emp, "EmployeeEmploymentEndDateSet"));
        Assert.DoesNotContain(rows, r => r.Kind == "SETTLED_YEAR");
    }

    /// <summary>
    /// <b>F (CI)</b> (part (4)). June exported, no settlement in the span; the end-date PUT from Tuesday 30 June to
    /// Friday 31 July 2026 answers 200; June listed (<c>effectiveFrom</c> 2026-06-30).
    /// <para><b>Red conditions.</b> No worklist row is written: the single-row assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task EndMovedFromTue30JunToFri31Jul2026_JuneExported_ListsJune()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: new DateOnly(2026, 6, 30));
        await SeedExportRecordAsync(emp, 2026, 6);

        var rsp = await PutDateAsync(emp, "employment-end-date", new { employmentEndDate = "2026-07-31" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var june = Assert.Single(rows, r => r.Kind == "EXPORTED_MONTH");
        Assert.Equal((2026, 6), (june.Year, june.Month));
        AssertSingleTrigger(june, new DateOnly(2026, 6, 30), await EventIdAsync(emp, "EmployeeEmploymentEndDateSet"));
        Assert.DoesNotContain(rows, r => r.Kind == "SETTLED_YEAR");
    }

    // ─── (5): the reversal ───────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI)</b> (part (5); BC-2, dates ruled 2.9). A leaver whose end date is Tuesday 30 June 2026 with an ACTIVE
    /// TERMINATION settlement (ferieår 2025) and June 2026 exported; a reverse-and-supersede reversal with the end
    /// date corrected to Friday 31 July 2026 answers 200; June is listed with the end-date event's id; there is NO
    /// SETTLED_YEAR row (the successor was written after the calls, and the reversed row no longer counts).
    /// <para><b>Red conditions.</b> The reversal writes no worklist row: the single-row assertion fails. A call placed
    /// after the re-settlement would flag the successor: <c>Assert.DoesNotContain(SETTLED_YEAR)</c> fails.</para>
    /// <para><b>(unproven)</b> that the dates give an eligible successor (the clock at 10 November 2026 is not the
    /// precedent's 5 March): the TERMINATION due predicate needs only an inactive user, an end date before today and
    /// after the go-live floor, and the same ferieår; CI settles it. If the reversal answers 409
    /// <c>SupersedeNotEligible</c>, the dates are the finding (BC-2: stop and report).</para>
    /// </summary>
    [Fact]
    public async Task ReversalWithEndDateCorrection_ReachingAnExportedMonth_ListsIt_NoSettledYearRowForTheSuccessor()
    {
        var goLive = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Settlement:GoLiveDate"] = "2025-01-01",
                }));
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock)));
        });
        _ = goLive.CreateClient(); // boot before seeding

        var emp = await SeedEmployeeWithDatesAsync(start: null, end: new DateOnly(2026, 6, 30));
        await SettleInOwnTxAsync(goLive, emp, 2025, "TERMINATION");
        await SeedExportRecordAsync(emp, 2026, 6);

        var client = LocalHrClient(goLive, ActorId, "STY01");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/employees/{emp}/settlement-reversal")
        {
            Content = JsonContent.Create(new
            {
                entitlementYear = 2025,
                expectedSettlementSequence = 1,
                mode = "REVERSE_AND_SUPERSEDE",
                hasEndDateCorrection = true,
                correctedEndDate = "2026-07-31",
                expectedUserVersion = 1,
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        var rsp = await client.SendAsync(req);

        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");
        var rows = await ReadRowsAsync(emp);
        var june = Assert.Single(rows, r => r.Kind == "EXPORTED_MONTH");
        Assert.Equal((2026, 6), (june.Year, june.Month));
        AssertSingleTrigger(june, new DateOnly(2026, 6, 30), await EventIdAsync(emp, "EmployeeEmploymentEndDateSet"));
        Assert.DoesNotContain(rows, r => r.Kind == "SETTLED_YEAR");
        Assert.Equal(1L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM vacation_settlements WHERE employee_id = @e AND sequence = 3 AND settlement_state = 'SETTLED'",
            ("e", emp)));
    }

    // ─── (7): R, M-42 ────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>R (CI), M-42 at CI-M4b</b> (part (7); ruling 5.3). The end date Tuesday 30 June 2026 saved again as Tuesday
    /// 30 June 2026; June exported. Three discriminating assertions: the 200; the end-date event emitted (one new
    /// <c>EmployeeEmploymentEndDateSet</c> on <c>employee-{id}</c>); ZERO worklist rows for the person.
    /// <para><b>Why R.</b> An unchanged end date lists nothing on today's code too (nothing calls the worklist), so no
    /// assertion fails there.</para>
    /// <para><b>M-42 argued (the must-trip cell, NR-P29).</b> M-42 makes the end-date PUT call the worklist writers when
    /// old = new (in <c>EmploymentDateEndpoints.cs</c>, the <c>EmploymentEndDateChangeInterval</c> no-call rule removed, the
    /// interval built as <c>[30 June, 1 July)</c>). With June exported, the exported-month writer reads the settling
    /// window of June, which that interval touches, so it writes one EXPORTED_MONTH row for (2026, 6): the zero-rows
    /// assertion <c>Assert.Empty(await ReadRowsAsync(emp))</c> fails. The 200 and the event assertions still hold, so the
    /// zero-rows assertion is the one that trips.</para>
    /// </summary>
    [Fact]
    public async Task EndResavedUnchanged_Tue30Jun2026_JuneExported_200_NoRow()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: new DateOnly(2026, 6, 30));
        await SeedExportRecordAsync(emp, 2026, 6);
        var eventsBefore = await CountEventsAsync(emp, "EmployeeEmploymentEndDateSet");

        var rsp = await PutDateAsync(emp, "employment-end-date", new { employmentEndDate = "2026-06-30" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(eventsBefore + 1, await CountEventsAsync(emp, "EmployeeEmploymentEndDateSet"));
        Assert.Empty(await ReadRowsAsync(emp));
    }

    // ─── (8)-(11): the start date open towards the past ──────────────────────────

    /// <summary>
    /// <b>F (CI)</b> (part (8); the worked example). No recorded start; registrations only from 15 June (one time
    /// entry on Monday 15 June 2026); June 2026 exported; the start-date PUT from none to Monday 15 June 2026: June
    /// listed, <c>effectiveFrom</c> 2026-06-01 (the Monday of June's opening week; June opens on a Monday).
    /// <para><b>Red conditions.</b> The start-date PUT writes no worklist row: the single-row assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task StartSetFromNoneToMon15Jun2026_JuneExported_ListsJune_EffectiveFromMon1June()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: null);
        await SeedTimeEntryAsync(emp, new DateOnly(2026, 6, 15));
        await SeedExportRecordAsync(emp, 2026, 6);

        var rsp = await PutDateAsync(emp, "employment-start-date", new { employmentStartDate = "2026-06-15" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var june = Assert.Single(rows, r => r.Kind == "EXPORTED_MONTH");
        Assert.Equal((2026, 6), (june.Year, june.Month));
        AssertSingleTrigger(june, new DateOnly(2026, 6, 1), await EventIdAsync(emp, "EmployeeEmploymentStartDateSet"));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-PIL-h (12); the adjudication's W3). No recorded start and no end, no registrations; July 2026
    /// exported, June not; the start-date PUT from none to Tuesday 30 June 2026 answers 200: exactly one worklist row,
    /// EXPORTED_MONTH (2026, 7), one trigger whose <c>effectiveFrom</c> is Monday 29 June 2026 (July opens on a
    /// Wednesday, so the Monday of its opening week is 29 June, the first day July's settlement reads) and whose event
    /// is the start-date event.
    /// <para><b>Mutant it kills.</b> Part (8)'s June opens on a Monday, so it cannot tell "the Monday of the opening
    /// week" from "the 1st of the month". Here the two differ: a writer that passed "the 1st of the month" (1 July) as the
    /// look-from day would hand the exported-month writer the interval <c>[1 Jul, 30 Jun)</c>, which is empty, and list
    /// nothing, so the single-row assertion fails; one that listed July with 1 July would fail the
    /// <c>effectiveFrom</c> assertion.</para>
    /// <para><b>Red conditions.</b> On <c>ac47d9f1</c> the start-date PUT writes no worklist row: the single-row
    /// assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task StartSetFromNoneToTue30Jun2026_JulyExported_ListsJuly_EffectiveFromMon29June()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: null);
        await SeedExportRecordAsync(emp, 2026, 7);

        var rsp = await PutDateAsync(emp, "employment-start-date", new { employmentStartDate = "2026-06-30" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var row = Assert.Single(await ReadRowsAsync(emp));
        Assert.Equal("EXPORTED_MONTH", row.Kind);
        Assert.Equal((2026, 7), (row.Year, row.Month));
        AssertSingleTrigger(row, new DateOnly(2026, 6, 29), await EventIdAsync(emp, "EmployeeEmploymentStartDateSet"));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-PIL-h (13); the adjudication's NOTE 3). No recorded start; June 2026 exported; the start-date PUT
    /// with a null start (cleared when already none) answers 200, emits exactly one new
    /// <c>EmployeeEmploymentStartDateSet</c> event and writes zero worklist rows.
    /// <para><b>Discrimination.</b> The 200 and the zero rows discriminate the early return on old = new (both null):
    /// without it the open-towards-the-past branch has no dated side and would fault (500) or list June. The event
    /// assertion is the one that is red on the base.</para>
    /// <para><b>Red conditions.</b> On <c>ac47d9f1</c> the start-date PUT emits no event at all (nothing emits
    /// <c>EmployeeEmploymentStartDateSet</c>): <c>Assert.Equal(eventsBefore + 1, …)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task StartClearedWhenAlreadyNone_JuneExported_200_OneEvent_NoRow()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: null);
        await SeedExportRecordAsync(emp, 2026, 6);
        var eventsBefore = await CountEventsAsync(emp, "EmployeeEmploymentStartDateSet");

        var rsp = await PutDateAsync(emp, "employment-start-date", new { employmentStartDate = (string?)null });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(eventsBefore + 1, await CountEventsAsync(emp, "EmployeeEmploymentStartDateSet"));
        Assert.Empty(await ReadRowsAsync(emp));
    }

    /// <summary>
    /// <b>F (CI)</b> (part (9); NR-P12). An ACTIVE VACATION settlement for ferieår 2025 (YEAR_END, valuation
    /// boundary 2026-12-31); no export record; no registrations before 2 March 2026; the start set from none to
    /// Monday 2 March 2026 answers 200: exactly one SETTLED_YEAR row (VACATION, 2025), <c>effectiveFrom</c>
    /// <b>2025-09-01</b> (its accrual window's first day), the start-date event's id; no EXPORTED_MONTH row; no
    /// <c>EntitlementBalanceRevalued</c> event and the person's <c>entitlement_balances</c> unchanged.
    /// <para><b>Red conditions.</b> The start-date PUT writes no worklist row: the single-row assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task StartSetFromNone_ActiveSettlementNoExportedMonth_OneSettledYearRow_NoExportedMonthRow_NothingRevalued()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: null);
        await SeedActiveSettlementAsync(emp, "VACATION", 2025, new DateOnly(2026, 12, 31));
        var balancesBefore = await BalancesAsync(emp);

        var rsp = await PutDateAsync(emp, "employment-start-date", new { employmentStartDate = "2026-03-02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var year = Assert.Single(rows);
        Assert.Equal("SETTLED_YEAR", year.Kind);
        Assert.Equal(("VACATION", 2025), (year.EntitlementType, year.EntitlementYear));
        AssertSingleTrigger(year, new DateOnly(2025, 9, 1), await EventIdAsync(emp, "EmployeeEmploymentStartDateSet"));
        Assert.DoesNotContain(rows, r => r.Kind == "EXPORTED_MONTH");
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'EntitlementBalanceRevalued' AND event_payload::text LIKE '%' || @e || '%'",
            ("e", emp)));
        Assert.Equal(balancesBefore, await BalancesAsync(emp));
    }

    /// <summary>
    /// <b>F (CI)</b> (part (10); NR-P12). June 2026 exported; ACTIVE VACATION settlements for ferieår 2025 and ferieår
    /// 2026; the start set from none to Monday 2 March 2026 (before June's opening Monday, 1 June): one SETTLED_YEAR
    /// row (VACATION, 2025), <c>effectiveFrom</c> 2025-09-01; NO row for (VACATION, 2026), whose window begins Tuesday
    /// 1 September 2026, after the new start; no EXPORTED_MONTH row.
    /// <para><b>Red conditions.</b> No worklist row is written: <c>Assert.Single(rows)</c> fails with zero rows.</para>
    /// </summary>
    [Fact]
    public async Task StartBeforeEarliestExportedMonth_SettledYearRowOnly_LaterWindowNotFlagged()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: null);
        await SeedExportRecordAsync(emp, 2026, 6);
        await SeedActiveSettlementAsync(emp, "VACATION", 2025, new DateOnly(2026, 12, 31));
        await SeedActiveSettlementAsync(emp, "VACATION", 2026, new DateOnly(2027, 12, 31));

        var rsp = await PutDateAsync(emp, "employment-start-date", new { employmentStartDate = "2026-03-02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var year = Assert.Single(rows);
        Assert.Equal("SETTLED_YEAR", year.Kind);
        Assert.Equal(("VACATION", 2025), (year.EntitlementType, year.EntitlementYear));
        AssertSingleTrigger(year, new DateOnly(2025, 9, 1), await EventIdAsync(emp, "EmployeeEmploymentStartDateSet"));
        Assert.DoesNotContain(rows, r => r.EntitlementYear == 2026);
        Assert.DoesNotContain(rows, r => r.Kind == "EXPORTED_MONTH");
    }

    /// <summary>
    /// <b>F (CI)</b> (part (11); NR-P20). In this isolated database, an ACTIVE settlement for entitlement year 2025 of
    /// a type with no live <c>entitlement_configs</c> row for the person's agreement and OK version (the VACATION rows
    /// of (AC, OK24) end-dated, the <c>HrBackdateWorklistRepositoryTests</c> technique; SPECIAL_HOLIDAY does not
    /// qualify, its window never needs a reset month); the start set from none to Monday 2 March 2026 answers 200:
    /// one SETTLED_YEAR row for that settlement, <c>effectiveFrom</c> <b>2025-01-01</b> (1 January of its year).
    /// <para><b>Red conditions.</b> No worklist row is written: the single-row assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task SettlementWithNoLiveResetMonth_StartSetFromNone_200_EffectiveFrom1JanuaryOfItsYear()
    {
        var emp = await SeedEmployeeWithDatesAsync(start: null, end: null);
        await SeedActiveSettlementAsync(emp, "VACATION", 2025, new DateOnly(2026, 12, 31));
        await using (var conn = new NpgsqlConnection(Cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE entitlement_configs SET effective_to = DATE '2020-01-01'
                WHERE entitlement_type = 'VACATION' AND agreement_code = 'AC' AND ok_version = 'OK24' AND effective_to IS NULL
                """, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var rsp = await PutDateAsync(emp, "employment-start-date", new { employmentStartDate = "2026-03-02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadRowsAsync(emp);
        var year = Assert.Single(rows);
        Assert.Equal("SETTLED_YEAR", year.Kind);
        Assert.Equal(("VACATION", 2025), (year.EntitlementType, year.EntitlementYear));
        AssertSingleTrigger(year, new DateOnly(2025, 1, 1), await EventIdAsync(emp, "EmployeeEmploymentStartDateSet"));
    }

    // ─── assertions and fixtures ─────────────────────────────────────────────────

    private sealed record WorklistRow(
        string Kind, int? Year, int? Month, string? EntitlementType, int? EntitlementYear,
        IReadOnlyList<StoredWorklistTrigger> Triggers);

    /// <summary>One trigger, kind <c>EMPLOYMENT_WINDOW_CHANGE</c> (the literal), the given look-from day and event id,
    /// and a derivation that blocks no recalculation.</summary>
    private static void AssertSingleTrigger(WorklistRow row, DateOnly effectiveFrom, Guid eventId)
    {
        var trigger = Assert.Single(row.Triggers);
        Assert.Equal(KindLiteral, trigger.Kind);
        Assert.Equal(effectiveFrom, trigger.EffectiveFrom);
        Assert.Equal(eventId, trigger.EventId);
        Assert.Empty(BackdateWorklistDerivation.RecalcBlockedBy(row.Kind, row.Year, row.Month, row.Triggers));
    }

    private async Task<IReadOnlyList<WorklistRow>> ReadRowsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT kind, year, month, entitlement_type, entitlement_year, triggers::text
            FROM hr_backdate_worklist WHERE employee_id = @e ORDER BY created_at, worklist_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var rows = new List<WorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WorklistRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                WorklistTriggerJson.Deserialize(reader.GetString(5))));
        }
        return rows;
    }

    private async Task<Guid> EventIdAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id FROM outbox_events
            WHERE stream_id = @s AND event_type = @t ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("t", eventType);
        var value = await cmd.ExecuteScalarAsync();
        Assert.True(value is Guid, $"expected a {eventType} event on employee-{employeeId}.");
        return (Guid)value!;
    }

    private Task<long> CountEventsAsync(string employeeId, string eventType) =>
        ScalarLongAsync(Cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t",
            ("s", $"employee-{employeeId}"), ("t", eventType));

    private Task<string?> BalancesAsync(string employeeId) => ScalarStringAsync(Cs,
        "SELECT COALESCE(md5(string_agg(b::text, '|' ORDER BY b::text)), '') FROM entitlement_balances b WHERE b.employee_id = @e",
        ("e", employeeId));

    private async Task<HttpResponseMessage> PutDateAsync(string employeeId, string route, object body)
    {
        var client = LocalHrClient(_host, ActorId, "STY01");
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{employeeId}/{route}")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.TryAddWithoutValidation("If-Match", "\"1\""); // the seed leaves users.version at 1
        return await client.SendAsync(req);
    }

    /// <summary>A person from <c>RegressionSeed</c> in STY01 with the employment dates set directly (version stays 1).
    /// A person with an end date before the fixed clock is a leaver: inactive, deactivated by the end date.</summary>
    private async Task<string> SeedEmployeeWithDatesAsync(DateOnly? start, DateOnly? end)
    {
        var employeeId = await SeedPersonAsync(Cs, "STY01");
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET employment_start_date = @start, employment_end_date = @end,
                             is_active = @active, end_date_deactivated = @leaver, updated_at = NOW()
            WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("start", (object?)start ?? DBNull.Value);
        cmd.Parameters.AddWithValue("end", (object?)end ?? DBNull.Value);
        var leaver = end is { } e && e < Clock;
        cmd.Parameters.AddWithValue("active", !leaver);
        cmd.Parameters.AddWithValue("leaver", leaver);
        await cmd.ExecuteNonQueryAsync();
        return employeeId;
    }

    private async Task SeedExportRecordAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month,
                 original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @e, @y, @m, '[]'::jsonb, '[]'::jsonb, @hash)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        cmd.Parameters.AddWithValue("hash", $"hash-{year}-{month}");
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>An ACTIVE (SETTLED, non-REVERSED) YEAR_END settlement whose snapshot carries the valuation boundary.</summary>
    private async Task SeedActiveSettlementAsync(string employeeId, string entitlementType, int year, DateOnly boundaryDate)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot)
            VALUES (@e, @t, @y, 1, 'SETTLED', 'YEAR_END',
                    jsonb_build_object('settlementBoundaryDate', to_char(@b::date, 'YYYY-MM-DD')))
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("b", boundaryDate);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A registration on the given weekday (the strand guard reads the projection tables).</summary>
    private async Task SeedTimeEntryAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, activity_type, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, 7.4, 'NORMAL', 'AC', 'OK26', FALSE, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SettleInOwnTxAsync(
        WebApplicationFactory<Program> app, string employeeId, int year, string trigger)
    {
        var service = app.Services.GetRequiredService<VacationSettlementService>();
        await using var conn = app.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await service.SettleAsync(employeeId, "VACATION", year, trigger, conn, tx);
        await tx.CommitAsync();
        Assert.True(outcome.DidSettle, "the TERMINATION settle drive must produce the settled row");
    }
}
