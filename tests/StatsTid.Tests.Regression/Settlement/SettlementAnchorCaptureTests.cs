using System.Data;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S141 / TASK-14103 (Part A — the settlement anchor, TASK-14101's fix) — Docker-gated pins for
/// <see cref="VacationSettlementService"/>'s capture path, for an employee hired PART-WAY THROUGH
/// the holiday year (ferieår) being settled.
///
/// <para>
/// <b>The plain-language problem.</b> Every year, a closed holiday year gets "settled" — the system
/// works out how many days someone earned, used and has left over. To do that it has to ask three
/// questions about the employee: which pay agreement covered them, which version of that agreement
/// (an "OK-version" — Denmark's public-sector agreements get renegotiated on a schedule, e.g. OK24
/// then OK26), and what job title they held. Before this fix the system asked all three questions
/// about the FIRST DAY of the holiday year. For an employee hired in the middle of that year — say
/// May, when the year started in September — that date is BEFORE they even worked here, so the
/// answer is "no record found" and the whole calculation throws an error instead of settling. The
/// fix asks the three questions about the LATER of "the year started" and "this person's first day",
/// which is always a date the person actually has a record for.
/// </para>
///
/// <para>
/// <b>Why these particular pins matter (the load-bearing reason for this whole file).</b> Before
/// this task, NO test anywhere asserted <c>agreementCode</c>, <c>okVersion</c> or <c>position</c> as
/// CAPTURED by a real settlement pass against a real database — every existing assertion of those
/// three fields is fixture-constructed (a test builds the JSON snapshot by hand and asserts its own
/// literal), which can never catch a wrong read. Every pin below instead runs the REAL capture code
/// against a REAL employee record and reads back what it actually wrote.
/// </para>
///
/// <para>
/// <b>Pins 5-6, added after a post-dispatch owner ruling.</b> "Later of the two dates" has no upper
/// bound on its own: if the employee's hire date falls AFTER the period being settled has already
/// ended, "the later of the two" is the hire date — a date OUTSIDE the period entirely, which would
/// otherwise resolve happily and record a fabricated ZERO-earned settlement as if it were a genuine
/// one. The owner ruled this must throw instead, the same way the original missing-history case did.
/// Pins 5 and 6 pin exactly that, one per settlement path.
/// </para>
///
/// <para>
/// <b>RED-FIRST, reasoned from the spec.</b> Docker is unavailable on the authoring machine, so
/// every fact below is derived from <c>VacationSettlementService.cs</c> as read (TASK-14101's
/// target), not from an observed run. Each fact's doc comment states why it fails today and why it
/// is expected to pass once TASK-14101's anchor fix lands — these first execute, and are CI-verified
/// (never claimed green locally), in the sprint-close watched CI run.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementAnchorCaptureTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string SpecialHolidayType = "SPECIAL_HOLIDAY";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boots the seeders (the VACATION/SPECIAL_HOLIDAY entitlement configs).
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private VacationSettlementService Service => _factory.Services.GetRequiredService<VacationSettlementService>();

    // ════════════════════════════════════════════════════════════════════════
    // Pin 1 — capture-derived agreementCode/okVersion/position, anchored at the hire, PLUS
    // Earned pinned unchanged OUTSIDE the OK24/OK26 divergence window, PLUS the YEAR_END boundary.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves.</b> An employee hired 1 Mar 2025 — inside ferieår 2024 (1 Sep 2024 –
    /// 31 Aug 2025), five months after it started — has their §24 wage-mapping key (agreement code,
    /// OK-version, job title) captured from the HIRE date, not the ferieår start that predates their
    /// employment. This is OUTSIDE the OK24→OK26 cutover (1 Apr 2026), so the OK-version is OK24
    /// either way — this fact isolates the plain "anchor to the hire" behaviour from the
    /// version-divergence question <see cref="CaptureSnapshot_DivergenceWindowHire_AnchorsOkVersionToHire_NotFerieaarStart"/>
    /// covers separately.
    /// </summary>
    /// <remarks>
    /// <b>RED today.</b> The current code reads the dated agreement code at the ferieår start
    /// (1 Sep 2024). This employee's dated history starts at their hire (1 Mar 2025), so that read
    /// finds no covering row and <c>CaptureSnapshotAsync</c> throws
    /// (<c>VacationSettlementService.cs</c> ~:1495-1500) before a settlement row is ever written —
    /// this fact's <c>SettleAsync</c> call throws, so every assertion below never runs. <b>GREEN</b>
    /// once TASK-14101 anchors the read at <c>max(ferieårStart, hire) = 2025-03-01</c>, which this
    /// employee's history does cover.
    /// </remarks>
    [Fact]
    public async Task CaptureSnapshot_MidFerieaarHire_YearEnd_AnchorsToHire_OutsideDivergenceWindow()
    {
        var hire = new DateOnly(2025, 3, 1);
        const int ferieaar = 2024; // Sep 2024 .. Aug 2025 — the hire falls inside it, mid-year.
        var employeeId = await SeedEmployeeWithHireAsync(
            "emp_s141_anchor", hire, position: "S141_MidYearRole");

        var outcome = await SettleAsync(employeeId, ferieaar, YearEnd);
        Assert.True(outcome.DidSettle);

        var json = await ReadSnapshotJsonAsync(employeeId, ferieaar, VacationType);
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;

        // Capture-derived, not fixture-constructed: all three come from dated reads anchored at
        // the HIRE (2025-03-01), which this employee's history actually covers — not the ferieår
        // start (2024-09-01), which it does not.
        Assert.Equal("AC", root.GetProperty("agreementCode").GetString());
        Assert.Equal("OK24", root.GetProperty("okVersion").GetString());
        Assert.Equal("S141_MidYearRole", root.GetProperty("position").GetString());

        // Outside the OK24/OK26 divergence window the two versions' seeded configs are identical
        // (both 25 days, MONTHLY_ACCRUAL), so the anchor choice cannot move Earned: it is pinned to
        // the ordinary accrual value — 6 whole months (Mar..Aug) of a 25-day quota = 12.5.
        Assert.Equal(12.5m, root.GetProperty("earned").GetDecimal());

        // The YEAR_END path stores the FERIEÅR-END boundary (31 Aug 2025) — not the hire date, and
        // not the §21 31-Dec deadline. Pin 3 below stores a DIFFERENT date for the TERMINATION path
        // on the very same ferieår, so this one must name its own value precisely.
        Assert.Equal("2025-08-31", root.GetProperty("settlementBoundaryDate").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Pin 2 — the SAME anchor logic on the TERMINATION path, pinning its OWN stored boundary
    // (the employment end date) — separately from Pin 1's YEAR_END boundary (the ferieår end).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves.</b> The year-end close and a termination crystallization share the same
    /// capture code but store DIFFERENT dates as <c>settlementBoundaryDate</c>: year-end stores the
    /// ferieår's own end (31 Aug); a termination stores the employee's LAST employed day. A pin that
    /// only ever exercises one of the two paths would not notice the other one storing the wrong
    /// date — this fact exercises the TERMINATION path specifically, on an employee hired mid-year
    /// exactly like Pin 1's, so both pins together prove BOTH stored boundaries are correct.
    /// </summary>
    /// <remarks>
    /// <b>RED today</b> for the same reason as Pin 1: the dated agreement-code read at the ferieår
    /// start (1 Sep 2024) misses for an employee whose history starts at their 1 Jan 2025 hire, and
    /// <c>CaptureSnapshotAsync</c> throws before any row is written. <b>GREEN</b> once TASK-14101
    /// anchors that read at the hire.
    /// </remarks>
    [Fact]
    public async Task CaptureSnapshot_MidFerieaarHire_Termination_StoresEndDateAsBoundary_NotFerieaarEnd()
    {
        var hire = new DateOnly(2025, 1, 1);
        var endDate = new DateOnly(2025, 6, 20);
        const int ferieaar = 2024; // ResolveLeaverFerieaar(2025-06-20): month 6 < 9 ⇒ 2024.

        var employeeId = await SeedEmployeeWithHireAsync(
            "emp_s141_term_anchor", hire, position: "S141_TermRole");
        await MarkLeaverAsync(employeeId, endDate);

        var outcome = await SettleAsync(employeeId, ferieaar, Termination);
        Assert.True(outcome.DidSettle);

        var json = await ReadSnapshotJsonAsync(employeeId, ferieaar, VacationType);
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;

        // Same hire-anchored capture as Pin 1 — the two paths share the code, so they must agree.
        Assert.Equal("AC", root.GetProperty("agreementCode").GetString());
        Assert.Equal("OK24", root.GetProperty("okVersion").GetString());
        Assert.Equal("S141_TermRole", root.GetProperty("position").GetString());

        // The decisive difference from Pin 1: TERMINATION stores the EMPLOYMENT END DATE as the
        // boundary (2025-06-20), never the ferieår end (2025-08-31) — even though this is the SAME
        // ferieår (2024) Pin 1 settled for a YEAR_END trigger.
        Assert.Equal("2025-06-20", root.GetProperty("settlementBoundaryDate").GetString());
        Assert.NotEqual("2025-08-31", root.GetProperty("settlementBoundaryDate").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Pin 3 — the OK24/OK26 divergence window: a pin that can actually fail. See the long comment
    // below for why "Earned unchanged" would NOT be able to fail here.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves, and why it is shaped this way.</b> Denmark's public-sector agreements
    /// are versioned (OK24, then OK26 from 1 Apr 2026). Ferieår 2025 (1 Sep 2025 – 31 Aug 2026)
    /// straddles that cutover: its START is under OK24, but a hire between 1 Apr and 31 Aug 2026 —
    /// this fact uses 15 May 2026 — is under OK26. Anchoring at the hire (the fix) resolves OK26;
    /// anchoring at the ferieår start (today's bug) resolves OK24 — a DIFFERENT answer to "which
    /// version governed this employee's first accruing day of the year".
    /// </summary>
    /// <remarks>
    /// <b>Why this is NOT written as "Earned differs".</b> In the REAL seeded data the OK24 and
    /// OK26 VACATION configs are byte-identical (both 25 days, MONTHLY_ACCRUAL) — so on real data
    /// <c>Earned</c> comes out the same number whichever version is picked, and a pin asserting a
    /// specific Earned VALUE here would pass for the wrong reason (it cannot tell "picked OK26
    /// correctly" from "picked OK24 by the old bug"). So this fact seeds its OWN pair of configs
    /// under a dedicated agreement code — OK24 with a 25-day quota, OK26 with a DELIBERATELY
    /// DIFFERENT 30-day quota — and asserts <c>okVersion</c> and <c>annualQuota</c> DIRECTLY. Only
    /// the correct anchor produces "OK26" / 30; the old anchor would produce "OK24" / 25 — so this
    /// pin fails for a real reason under either direction of mistake.
    /// </remarks>
    [Fact]
    public async Task CaptureSnapshot_DivergenceWindowHire_AnchorsOkVersionToHire_NotFerieaarStart()
    {
        const string agreementCode = "S141ANCHOR"; // test-owned — never the shared seeded 'AC' row.
        var hire = new DateOnly(2026, 5, 15); // inside OK26 (starts 2026-04-01); the ferieår start is OK24.
        const int ferieaar = 2025; // Sep 2025 .. Aug 2026 — the divergence window named in the task.

        // Two DIFFERING configs under the SAME agreement code, one per OK version, so the pin can
        // only pass if the CORRECT version's config was genuinely selected.
        await SeedVacationConfigAsync(agreementCode, "OK24", annualQuota: 25m);
        await SeedVacationConfigAsync(agreementCode, "OK26", annualQuota: 30m);

        var employeeId = await SeedEmployeeWithHireAsync(
            "emp_s141_divergence", hire, agreementCode: agreementCode, okVersion: "OK24",
            position: "S141_DivergencePos");

        var outcome = await SettleAsync(employeeId, ferieaar, YearEnd);
        Assert.True(outcome.DidSettle);

        var json = await ReadSnapshotJsonAsync(employeeId, ferieaar, VacationType);
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;

        // The decisive assertion: anchored at the hire (2026-05-15, OK26's window), not the ferieår
        // start (2025-09-01, OK24's window). Wrongly anchoring at the ferieår start would read
        // "OK24" / 25 here instead.
        Assert.Equal("OK26", root.GetProperty("okVersion").GetString());
        Assert.Equal(30m, root.GetProperty("annualQuota").GetDecimal());
        Assert.Equal(agreementCode, root.GetProperty("agreementCode").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Pin 4 — SPECIAL_HOLIDAY capture-derived agreementCode/okVersion/position, anchored at the
    // hire — the SPECIAL_HOLIDAY mirror of Pin 1. Added on request: Pin 4b (the fail-closed change,
    // below) proves the path REFUSES bad input, but nothing proved it captures the RIGHT values
    // from GOOD input — that asymmetry is what this pin closes.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves, in plain language.</b> SPECIAL_HOLIDAY (særlige feriedage) accrues on
    /// the plain calendar year. This employee is hired 1 Jul 2021 — six months into accrual year
    /// 2021 (1 Jan – 31 Dec 2021) — so the same anchor question Pin 1 asks for VACATION applies here
    /// too: is the agreement code, OK-version and job title captured as of the HIRE, or as of the
    /// accrual year's first day, five months before this person worked here?
    /// </summary>
    /// <remarks>
    /// <b>Why this needs a SUPERSESSION, not just a gap — the thing that makes the pin able to
    /// fail.</b> Unlike VACATION's dated agreement-code read, SPECIAL_HOLIDAY's has a FALLBACK today
    /// (<c>?? user.AgreementCode</c>) — so a mere "no row before the hire" gap does not, on its own,
    /// make old and new code disagree on the CAPTURED VALUE: both would resolve to whatever single
    /// agreement code the employee has ever had. To make this pin capable of failing, the employee's
    /// agreement code CHANGES after the hire: dated from the hire they were on
    /// <c>S141SHOLD</c>; from 1 Jun 2022 they are on <c>S141SHNEW</c> (today's live code). Anchoring
    /// at the hire (the fix) must capture <c>S141SHOLD</c> — the code true when they started
    /// accruing this year — never the employee's CURRENT code, which the old fallback path would
    /// return instead.
    /// </remarks>
    /// <remarks>
    /// <b>RED today.</b> The current fallback resolves <c>agreementCode</c> to the employee's LIVE
    /// code (<c>S141SHNEW</c>) rather than the one true at the hire, so this fact's
    /// <c>Assert.Equal("S141SHOLD", ...)</c> fails against what the code returns today. <c>Position</c>
    /// resolves to <c>null</c> today (the dated profile read also misses and there is no fallback for
    /// it), rather than the seeded job title. <b>GREEN</b> once TASK-14101 anchors both reads at the
    /// hire, which this employee's history covers.
    /// </remarks>
    [Fact]
    public async Task CaptureSnapshot_SpecialHoliday_MidAccrualYearHire_AnchorsToHire_NotAccrualStart()
    {
        const int accrualYear = 2021; // calendar 2021 — accrual start 2021-01-01.
        var hire = new DateOnly(2021, 7, 1);
        var supersededOn = new DateOnly(2022, 6, 1); // well after the hire; the exact date is not load-bearing.
        const string oldAgreementCode = "S141SHOLD"; // true at the hire — what the anchor must capture.
        const string newAgreementCode = "S141SHNEW"; // the employee's CURRENT/live code — the fallback trap.

        // The dated config the anchored read must resolve to (under oldAgreementCode/OK24); no
        // config exists for newAgreementCode, so the OLD fallback path (which resolves to
        // newAgreementCode) has nothing to fall back to and throws — a different, but still
        // correctly RED, failure mode than a wrong-value assertion.
        await SeedSpecialHolidayConfigAsync(oldAgreementCode, "OK24", annualQuota: 5m, carryoverMax: 0m);

        var employeeId = await SeedEmployeeWithHireAsync(
            "emp_s141_sh_anchor", hire, agreementCode: oldAgreementCode, okVersion: "OK24",
            position: "S141_SH_MidYearRole");
        await SupersedeAgreementCodeAsync(employeeId, supersededOn, newAgreementCode);

        var outcome = await SettleAsync(employeeId, accrualYear, YearEnd, SpecialHolidayType);
        Assert.True(outcome.DidSettle);

        var json = await ReadSnapshotJsonAsync(employeeId, accrualYear, SpecialHolidayType);
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        var root = doc.RootElement;

        // Capture-derived, anchored at the hire (1 Jul 2021) — the code and title true THEN, not
        // the employee's code TODAY (S141SHNEW) and not a missing (null) title.
        Assert.Equal(oldAgreementCode, root.GetProperty("agreementCode").GetString());
        Assert.Equal("OK24", root.GetProperty("okVersion").GetString());
        Assert.Equal("S141_SH_MidYearRole", root.GetProperty("position").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Pin 4b — the SPECIAL_HOLIDAY (særlige feriedage) fail-closed change.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves, in plain language.</b> The VACATION capture already refuses to guess: if
    /// it cannot find the dated record it needs, it throws rather than silently using today's live
    /// data. The SPECIAL_HOLIDAY capture (særlige feriedage — the Danish public sector's agreement-
    /// based "6th week") does the SAME kind of read but, today, quietly falls back to the employee's
    /// CURRENT live agreement code (and a null job title) when the dated read misses — no error, no
    /// signal, just a silently wrong key on a real payroll line. TASK-14101 makes this fail closed
    /// too, matching VACATION's existing behaviour.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately NOT a hire scenario.</b> This seeds a plain data-integrity gap — the
    /// employee's dated history starts a year after the SPECIAL_HOLIDAY accrual year in question,
    /// with NO <c>employment_start_date</c> set — the exact shape the sibling VACATION regression
    /// (<c>SettlementExportEmitterTests.SnapshotCapture_NoDatedAgreementCoveringFerieaarStart_FailsClosed_NoSettlement</c>)
    /// already pins. This keeps the fail-closed assertion independent of the hire-anchor question
    /// Pins 1-3 cover, isolating exactly one thing: does a missing dated read throw, or silently
    /// substitute live data?
    /// </remarks>
    /// <remarks>
    /// <b>RED today.</b> The current code's <c>datedAgreement</c> read falls back to
    /// <c>user.AgreementCode</c> on a miss (no throw), so <c>SettleAsync</c> SUCCEEDS today —
    /// <c>Assert.ThrowsAsync</c> below fails because no exception is thrown. <b>GREEN</b> once
    /// TASK-14101 removes that fallback (and the position's silent <c>null</c>), matching VACATION's
    /// existing fail-closed shape.
    /// </remarks>
    [Fact]
    public async Task CaptureSnapshot_SpecialHoliday_NoDatedAgreementCoveringAccrualStart_FailsClosed_NoSettlement()
    {
        const int accrualYear = 2021; // long-closed SPECIAL_HOLIDAY accrual year (calendar 2021).
        var employeeId = "emp_s141_sh_failclosed_" + Guid.NewGuid().ToString("N")[..8];

        // Dated history effective ONLY from 2022-01-01 — AFTER the accrual start (1 Jan 2021) — so
        // the strict dated read at the accrual start finds no covering row. No employment_start_date
        // is set, so this is a genuine data gap, not a hire.
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, "AC", "OK24",
            effectiveFrom: new DateOnly(2022, 1, 1));

        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Service.SettleAsync(employeeId, SpecialHolidayType, accrualYear, YearEnd, conn, tx));

        await tx.RollbackAsync();

        // Fail-closed means NO partial settlement — the row must not exist at all.
        Assert.Equal(0L, await CountActiveSettlementsAsync(employeeId, accrualYear, SpecialHolidayType));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Pin 5 — VACATION: a hire AFTER the ferieår has already ended must throw, not settle at zero.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves, in plain language.</b> "Anchor at the later of the ferieår start and the
    /// hire" quietly assumes the hire falls SOMEWHERE INSIDE the ferieår. If it does not — this
    /// employee's recorded hire date is 1 Oct 2025, a month AFTER ferieår 2024 (Sep 2024 – Aug 2025)
    /// already closed — "the later of the two" is the hire date itself, which sits outside the
    /// period being settled altogether. Silently accepting that would record a settlement for a year
    /// this person could not possibly have held a single day of, earning exactly zero, and audit it
    /// as if it were a deliberate, genuine result. The owner ruled this must fail loudly instead.
    /// </summary>
    /// <remarks>
    /// <b>Isolated from Pins 1-3's own question.</b> This employee's dated profile/agreement history
    /// is seeded OPEN SINCE YEAR 1 (not anchored to the hire), so a dated read would succeed at ANY
    /// candidate anchor. The only possible reason for a throw here is the NEW out-of-period guard
    /// itself — never a coincidental "missing dated history" throw, which is a DIFFERENT defect
    /// Pins 1-3 already cover.
    /// </remarks>
    /// <remarks>
    /// <b>The comparand, stated so a future edit does not quietly break it.</b> The guard compares
    /// the anchor against the ferieår's OWN end (31 Aug 2025 here) — never the valuation boundary,
    /// which a TERMINATION call pulls earlier to the employment end date. Using the valuation
    /// boundary would reject strictly more hires than the ferieår-end comparand does, which is why
    /// this fact deliberately drives the plain YEAR_END path, where the two coincide and cannot be
    /// confused with one another.
    /// </remarks>
    [Fact]
    public async Task CaptureSnapshot_Vacation_HireAfterFerieaarEnd_FailsClosed_NoSettlement()
    {
        const int ferieaar = 2024; // Sep 2024 .. Aug 2025 — ferieår end 2025-08-31.
        var employeeId = "emp_s141_afterperiod_vac_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2025, 10, 1)); // after 2025-08-31.

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SettleAsync(employeeId, ferieaar, YearEnd));

        Assert.Equal(0L, await CountActiveSettlementsAsync(employeeId, ferieaar, VacationType));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Pin 6 — SPECIAL_HOLIDAY: the SAME guard, on its OWN (earlier) boundary — the accrual end,
    // NOT the much later godtgørelse settlement deadline.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>What this proves, and why the date chosen matters.</b> SPECIAL_HOLIDAY (særlige feriedage)
    /// accrues on the plain calendar year (2021 here: 1 Jan – 31 Dec 2021) but is not PAID until a
    /// godtgørelse deadline almost 16 months later (30 Apr 2023). This employee's hire, 1 Mar 2022,
    /// sits AFTER the accrual year ended but well BEFORE that later deadline. If the guard compared
    /// against the later deadline it would let this hire through — the wrong answer, per the owner's
    /// ruling that the comparison must be against the ACCRUAL end (the period the employee actually
    /// had to overlap for anything to accrue), not the later settlement boundary. So this one hire
    /// date is chosen SPECIFICALLY to fall on the side that only the correct comparand catches.
    /// </summary>
    /// <remarks>
    /// Isolated the same way as Pin 5: dated history is seeded open since year 1, so the only
    /// possible cause of a throw is the new out-of-period guard.
    /// </remarks>
    [Fact]
    public async Task CaptureSnapshot_SpecialHoliday_HireAfterAccrualEnd_FailsClosed_NoSettlement()
    {
        const int accrualYear = 2021; // calendar 2021 — accrual end 2021-12-31 (NOT the 30-Apr-2023 payout deadline).
        var employeeId = "emp_s141_afterperiod_sh_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2022, 3, 1)); // after 2021-12-31, before 2023-04-30.

        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Service.SettleAsync(employeeId, SpecialHolidayType, accrualYear, YearEnd, conn, tx));

        await tx.RollbackAsync();
        Assert.Equal(0L, await CountActiveSettlementsAsync(employeeId, accrualYear, SpecialHolidayType));
    }

    // ─────────────────────────────── drivers ───────────────────────────────

    /// <summary>One settlement pass in its OWN ReadCommitted tx, committed — the exact
    /// SettlementCloseService shape (open conn, begin tx, SettleAsync, commit).</summary>
    private async Task<SettlementOutcome> SettleAsync(
        string employeeId, int year, string trigger, string entitlementType = VacationType)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await Service.SettleAsync(employeeId, entitlementType, year, trigger, conn, tx);
        await tx.CommitAsync();
        return outcome;
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    /// <summary>Seeds a fresh employee whose dated <c>employee_profiles</c> / <c>user_agreement_codes</c>
    /// history starts EXACTLY at <paramref name="hire"/> (not the RegressionSeed default of year 1),
    /// and stamps <c>users.employment_start_date</c> to the same date — RegressionSeed itself never
    /// touches that column. Together these make <paramref name="hire"/> a genuine mid-ferieår hire:
    /// a dated read anchored BEFORE it misses; a dated read anchored AT or AFTER it hits.</summary>
    private async Task<string> SeedEmployeeWithHireAsync(
        string prefix, DateOnly hire, string agreementCode = "AC", string okVersion = "OK24",
        string? position = null)
    {
        var employeeId = prefix + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, agreementCode, okVersion,
            partTimeFraction: 1.000m, effectiveFrom: hire, position: position);
        await SetEmploymentStartDateAsync(employeeId, hire);
        return employeeId;
    }

    private async Task SetEmploymentStartDateAsync(string employeeId, DateOnly hire)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @d WHERE user_id = @u", conn);
        cmd.Parameters.AddWithValue("d", hire);
        cmd.Parameters.AddWithValue("u", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>The post-Step-A leaver state (end date + lifecycle deactivation) — the
    /// TerminationSettlementTests convention.</summary>
    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("endDate", endDate);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A dedicated test-owned VACATION config, distinct from the shared seeded 'AC'/'HK'/
    /// 'PROSA' rows, so a divergence test can make its two OK-version rows differ WITHOUT perturbing
    /// any other test's shared agreement-code data. reset_month is pinned at 9 (the VACATION-only DB
    /// CHECK); open from year 1 so it resolves at any anchor date this file uses.</summary>
    private async Task SeedVacationConfigAsync(
        string agreementCode, string okVersion, decimal annualQuota, decimal carryoverMax = 5m,
        string accrualModel = "MONTHLY_ACCRUAL")
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_configs
                (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model,
                 reset_month, carryover_max, pro_rate_by_part_time, effective_from, effective_to)
            VALUES ('VACATION', @a, @ok, @quota, @model, 9, @cap, false, '0001-01-01', NULL)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("quota", annualQuota);
        cmd.Parameters.AddWithValue("model", accrualModel);
        cmd.Parameters.AddWithValue("cap", carryoverMax);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A dedicated test-owned SPECIAL_HOLIDAY config, for the SAME reason
    /// <see cref="SeedVacationConfigAsync"/> exists: an isolated agreement code so this pin's data
    /// cannot collide with any other test's. reset_month is 1 by convention (SPECIAL_HOLIDAY's own
    /// geometry ignores it — <c>EntitlementPeriodResolver</c> — but the column is NOT NULL).</summary>
    private async Task SeedSpecialHolidayConfigAsync(
        string agreementCode, string okVersion, decimal annualQuota, decimal carryoverMax = 0m,
        string accrualModel = "MONTHLY_ACCRUAL")
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_configs
                (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model,
                 reset_month, carryover_max, pro_rate_by_part_time, effective_from, effective_to)
            VALUES ('SPECIAL_HOLIDAY', @a, @ok, @quota, @model, 1, @cap, false, '0001-01-01', NULL)
            ON CONFLICT DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("a", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("quota", annualQuota);
        cmd.Parameters.AddWithValue("model", accrualModel);
        cmd.Parameters.AddWithValue("cap", carryoverMax);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Closes the employee's CURRENT (RegressionSeed-written) <c>user_agreement_codes</c>
    /// row at <paramref name="supersededOn"/> and opens a NEW one from that date carrying
    /// <paramref name="newAgreementCode"/> — also updating <c>users.agreement_code</c> to match,
    /// exactly as a real agreement-code change does. This is what makes a fallback-to-LIVE-data read
    /// distinguishable from a correctly-DATED read in a test: after this call, the DATED history at
    /// the hire says one thing, while the employee's CURRENT/live code says another.</summary>
    private async Task SupersedeAgreementCodeAsync(string employeeId, DateOnly supersededOn, string newAgreementCode)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var closeCmd = new NpgsqlCommand(
            """
            UPDATE user_agreement_codes SET effective_to = @to, updated_at = NOW()
            WHERE user_id = @u AND effective_to IS NULL
            """, conn))
        {
            closeCmd.Parameters.AddWithValue("u", employeeId);
            closeCmd.Parameters.AddWithValue("to", supersededOn);
            await closeCmd.ExecuteNonQueryAsync();
        }
        await using (var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes
                (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @code, @from, NULL, 1)
            """, conn))
        {
            insertCmd.Parameters.AddWithValue("u", employeeId);
            insertCmd.Parameters.AddWithValue("code", newAgreementCode);
            insertCmd.Parameters.AddWithValue("from", supersededOn);
            await insertCmd.ExecuteNonQueryAsync();
        }
        await using (var userCmd = new NpgsqlCommand(
            "UPDATE users SET agreement_code = @code, updated_at = NOW() WHERE user_id = @u", conn))
        {
            userCmd.Parameters.AddWithValue("u", employeeId);
            userCmd.Parameters.AddWithValue("code", newAgreementCode);
            await userCmd.ExecuteNonQueryAsync();
        }
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<string?> ReadSnapshotJsonAsync(string employeeId, int year, string entitlementType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT snapshot::text FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        var v = await cmd.ExecuteScalarAsync();
        return v as string;
    }

    private async Task<long> CountActiveSettlementsAsync(string employeeId, int year, string entitlementType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND settlement_state <> 'REVERSED'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
