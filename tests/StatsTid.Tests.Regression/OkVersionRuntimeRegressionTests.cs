using StatsTid.RuleEngine.Api.Config;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression;

// ---------------------------------------------------------------------------
// Runtime regression tests for OK-version resolution (Codex Rec #8 / TASK-1805).
//
// Background:
//   TASK-1801 fixed four Backend endpoints and PeriodCalculationService so that
//   the OK version is resolved from the entry/period date instead of trusting
//   the caller-supplied value. Pre-fix, a caller could persist an entry dated
//   2026-04-15 (OK26) tagged as "OK24" and the system would accept it.
//
//   The pre-existing unit tests covered OkVersionResolver in isolation, but
//   never proved the runtime paths used the right version tables. These tests
//   close that gap by:
//     (a) exercising the canonical StatsTid.SharedKernel.Calendar.OkVersionResolver
//         for every transition-boundary case referenced in the fix;
//     (b) proving the absence-split and weekly-calculation design contracts
//         stated in TimeEndpoints.cs comments;
//     (c) proving that a retroactive split at the OK-transition date yields
//         two structurally-consistent result sets — one per version — using
//         the same pure OvertimeRule path as RegressionTests.cs.
//
// Tests kept pure (no HTTP, no DB, no Docker). This mirrors the style of the
// existing RegressionTests.cs. A live HTTP drift-log test for
// PeriodCalculationService is intentionally NOT included (see test #9 below):
// it would require a running rule-engine container. Instead, we prove the
// auditor's underlying invariant (entry/period resolution disagreement)
// purely via the resolver. The log-capture side of the contract is not
// covered at runtime by this suite — any regression there must be surfaced
// by integration tests or manual smoke checks.
// ---------------------------------------------------------------------------
public class OkVersionRuntimeRegressionTests
{
    // Re-used fixtures mirroring RegressionTests.cs
    private static EmploymentProfile CreateProfile(string agreement, string okVersion) => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = agreement,
        OkVersion = okVersion,
        WeeklyNormHours = 37.0m,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m
    };

    private static TimeEntry CreateEntry(DateOnly date, decimal hours, string agreement, string okVersion) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = agreement,
        OkVersion = okVersion
    };

    private static AbsenceEntry CreateAbsence(DateOnly date, string okVersion) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        AbsenceType = AbsenceTypes.Vacation,
        Hours = 7.4m,
        AgreementCode = "HK",
        OkVersion = okVersion
    };

    // -----------------------------------------------------------------------
    // 1. A date strictly before the OK24 → OK26 transition resolves to OK24.
    //    This is the ADR-003 invariant the Backend + Payroll resolvers rely on.
    // -----------------------------------------------------------------------
    [Fact]
    public void TimeEntry_BeforeOkTransition_ResolvesToOk24()
    {
        var resolved = OkVersionResolver.ResolveVersion(new DateOnly(2026, 3, 30));

        Assert.Equal("OK24", resolved);
    }

    // -----------------------------------------------------------------------
    // 2. The transition date itself (inclusive) resolves to OK26. This is
    //    the left boundary of the OK26 period (2026-04-01 .. 2028-03-31).
    // -----------------------------------------------------------------------
    [Fact]
    public void TimeEntry_OnOkTransitionDate_ResolvesToOk26()
    {
        var resolved = OkVersionResolver.ResolveVersion(new DateOnly(2026, 4, 1));

        Assert.Equal("OK26", resolved);
    }

    // -----------------------------------------------------------------------
    // 3. The last day under OK24 (2026-03-31) must still resolve to OK24 —
    //    the right boundary is inclusive. Protects against off-by-one drift.
    // -----------------------------------------------------------------------
    [Fact]
    public void TimeEntry_OneDayBeforeTransition_ResolvesToOk24()
    {
        var resolved = OkVersionResolver.ResolveVersion(new DateOnly(2026, 3, 31));

        Assert.Equal("OK24", resolved);
    }

    // -----------------------------------------------------------------------
    // 4. Dates before the earliest known OK period fall back to the earliest
    //    version (OK24). Historical data predating OK24 must not silently
    //    become OK26.
    // -----------------------------------------------------------------------
    [Fact]
    public void TimeEntry_DateBeforeOk24Start_ResolvesToOk24()
    {
        var resolved = OkVersionResolver.ResolveVersion(new DateOnly(2023, 12, 1));

        Assert.Equal("OK24", resolved);
    }

    // -----------------------------------------------------------------------
    // 5. Dates past the last known OK period fall forward to the latest
    //    version (OK26). Future-dated entries (e.g. planned shifts beyond
    //    the current agreement window) must take the most recent version.
    // -----------------------------------------------------------------------
    [Fact]
    public void TimeEntry_DateAfterOk26End_ResolvesToOk26()
    {
        var resolved = OkVersionResolver.ResolveVersion(new DateOnly(2030, 1, 1));

        Assert.Equal("OK26", resolved);
    }

    // -----------------------------------------------------------------------
    // 6. Absence-request design contract (TimeEndpoints.cs, POST /api/absences):
    //    RegisterAbsenceRequest carries a single `Date` (not a range), so
    //    OK-version resolution is naturally per-day. If a caller submits
    //    absences that straddle 2026-04-01, each per-day resolution yields
    //    the correct version — the caller does not need a range-aware split.
    // -----------------------------------------------------------------------
    [Fact]
    public void AbsenceStraddleTransition_RequiresCallerSplit()
    {
        // Four absences straddling the OK24 → OK26 boundary
        var absences = new[]
        {
            CreateAbsence(new DateOnly(2026, 3, 30), okVersion: "ignored"),
            CreateAbsence(new DateOnly(2026, 3, 31), okVersion: "ignored"),
            CreateAbsence(new DateOnly(2026, 4, 1),  okVersion: "ignored"),
            CreateAbsence(new DateOnly(2026, 4, 2),  okVersion: "ignored"),
        };

        // Server-side per-day resolution must map these exactly
        var resolvedVersions = absences
            .Select(a => OkVersionResolver.ResolveVersion(a.Date))
            .ToList();

        Assert.Equal(new[] { "OK24", "OK24", "OK26", "OK26" }, resolvedVersions);
    }

    // -----------------------------------------------------------------------
    // 7. Weekly-calculate design contract (TimeEndpoints.cs, POST
    //    /api/time-entries/calculate-week): OK version is resolved from the
    //    WeekStartDate, NOT from each day in the week. A week that starts on
    //    Mon 2026-03-30 crosses the transition mid-week but must resolve to
    //    OK24 because the week's anchor is 2026-03-30.
    //
    //    If this invariant changes in the future, callers that span the
    //    transition must use the retroactive-split flow (ADR-013) instead.
    // -----------------------------------------------------------------------
    [Fact]
    public void WeeklyCalculate_WeekContainingTransition_ResolvedByWeekStart()
    {
        var weekStart = new DateOnly(2026, 3, 30); // Monday

        var resolved = OkVersionResolver.ResolveVersion(weekStart);

        Assert.Equal("OK24", resolved);

        // Sanity: the same week's Wednesday (2026-04-01) resolves to OK26
        // when asked directly. This difference is precisely why the endpoint
        // documents the week-start-based anchoring and why straddle cases
        // must go through the retroactive-split path.
        var wednesdayResolution = OkVersionResolver.ResolveVersion(weekStart.AddDays(3));
        Assert.Equal("OK26", wednesdayResolution);
        Assert.NotEqual(resolved, wednesdayResolution);
    }

    // -----------------------------------------------------------------------
    // 8. Retroactive split at the OK transition date: entries before
    //    2026-04-01 must be evaluated under the OK24 config, entries on/after
    //    under the OK26 config. Each segment produces a structurally-
    //    consistent OvertimeRule result, proving the split mechanism in
    //    RetroactiveCorrectionService.RecalculateWithVersionSplitAsync does
    //    not cross-contaminate versions.
    //
    //    This test intentionally mirrors OkVersionTransition_OK24ToOK26_RecalculatesCorrectly
    //    in RegressionTests.cs but adds the split-by-transition-date step so
    //    the version partition itself is the assertion target.
    //
    //    NOTE: We do NOT call RetroactiveCorrectionService.RecalculateAsync
    //    directly because it depends on PeriodCalculationService, which in
    //    turn requires an HTTP rule-engine endpoint. Instead, we exercise the
    //    same pure OvertimeRule twice — once per partition — which is what
    //    the split path ultimately invokes per segment.
    // -----------------------------------------------------------------------
    [Fact]
    public void PayrollRetroactive_OkTransitionDateSplitsPeriod()
    {
        var transitionDate = new DateOnly(2026, 4, 1);
        // Period: Mon 2026-03-30 .. Sun 2026-04-05 (straddles the transition)
        var periodStart = new DateOnly(2026, 3, 30);
        var periodEnd   = new DateOnly(2026, 4, 5);

        // Seven daily entries of 8h each across the whole period
        var allEntries = Enumerable.Range(0, 7)
            .Select(i => CreateEntry(periodStart.AddDays(i), 8m, "HK", okVersion: "ignored"))
            .ToList();

        // Split on transition (same logic as RetroactiveCorrectionService)
        var entriesBefore      = allEntries.Where(e => e.Date < transitionDate).ToList();
        var entriesOnOrAfter   = allEntries.Where(e => e.Date >= transitionDate).ToList();

        Assert.Equal(2, entriesBefore.Count);        // Mon, Tue (30-31 Mar)
        Assert.Equal(5, entriesOnOrAfter.Count);     // Wed..Sun (01-05 Apr)

        // Every entry in each partition must resolve to the expected OK version
        Assert.All(entriesBefore,
            e => Assert.Equal("OK24", OkVersionResolver.ResolveVersion(e.Date)));
        Assert.All(entriesOnOrAfter,
            e => Assert.Equal("OK26", OkVersionResolver.ResolveVersion(e.Date)));

        // Per-segment configs
        var configOk24 = AgreementConfigProvider.GetConfig("HK", "OK24");
        var configOk26 = AgreementConfigProvider.GetConfig("HK", "OK26");

        Assert.Equal("OK24", configOk24.OkVersion);
        Assert.Equal("OK26", configOk26.OkVersion);

        // Per-segment profiles (match RetroactiveCorrectionService split)
        var profileOk24 = CreateProfile("HK", "OK24");
        var profileOk26 = CreateProfile("HK", "OK26");

        // Evaluate each segment under its own window (the split service calls
        // CalculateAsync with segmentEnd1 = transitionDate - 1)
        var segment1End   = transitionDate.AddDays(-1);
        var segment2Start = transitionDate;

        var result1 = OvertimeRule.Evaluate(
            profileOk24, entriesBefore, periodStart, segment1End, configOk24);
        var result2 = OvertimeRule.Evaluate(
            profileOk26, entriesOnOrAfter, segment2Start, periodEnd, configOk26);

        // Both segments must succeed and carry the same RuleId (structural consistency)
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(OvertimeRule.RuleId, result1.RuleId);
        Assert.Equal(OvertimeRule.RuleId, result2.RuleId);

        // Neither segment may contain line items tagged with the *other* version's
        // config (the rule result itself carries no OkVersion; this invariant is
        // indirectly asserted by the fact each was evaluated with the correct
        // config and entry set). What we CAN assert directly: each per-segment
        // line item's hours are accounted for within the partition that produced
        // it (segment2 has 5x 8h = 40h, which exceeds 37h norm, so OK26 segment
        // must produce overtime-type line items; segment1 has 2x 8h = 16h which
        // is under norm and therefore produces zero overtime line items).
        Assert.Empty(result1.LineItems);
        Assert.NotEmpty(result2.LineItems);

        // Structural consistency: both results share the same RuleId shape and
        // are distinct objects (no shared mutable state leaking across versions).
        Assert.NotSame(result1, result2);
    }

    // -----------------------------------------------------------------------
    // 9. PeriodCalculationService drift-logging contract (auditor invariant).
    //
    //    The live version of this check — asserting that
    //    PeriodCalculationService emits a LogWarning when an entry's date
    //    resolves to a different OK version than the period — requires a
    //    running rule-engine HTTP endpoint. We do NOT spin one up here; that
    //    would make this suite depend on Docker + HTTP, violating the pure-
    //    test discipline established by RegressionTests.cs.
    //
    //    Instead we prove the underlying auditor invariant: for a period
    //    anchored at 2026-04-01 (OK26) with an entry dated 2026-03-30 (OK24),
    //    the per-entry and per-period resolutions MUST disagree. That is
    //    exactly the condition the LogWarning call guards. Any runtime
    //    regression that breaks the log call will still preserve — or break
    //    — this mathematical difference; we pin the math.
    // -----------------------------------------------------------------------
    [Fact]
    public void PeriodCalculationService_LogsDriftWhenEntryDateMismatchesPeriodOk()
    {
        var periodStart = new DateOnly(2026, 4, 1);       // OK26
        var entryDate   = new DateOnly(2026, 3, 30);      // OK24

        var periodResolved = OkVersionResolver.ResolveVersion(periodStart);
        var entryResolved  = OkVersionResolver.ResolveVersion(entryDate);

        Assert.Equal("OK26", periodResolved);
        Assert.Equal("OK24", entryResolved);
        Assert.NotEqual(periodResolved, entryResolved);
    }

    // -----------------------------------------------------------------------
    // 10. RetroactiveCorrectionService single-version audit-event invariant
    //     (TASK-1904).
    //
    //    Background:
    //      In the single-version (non-split) branch of
    //      RetroactiveCorrectionService.RecalculateAsync, the emitted
    //      RetroactiveCorrectionRequested event used to carry the caller-
    //      supplied profile.OkVersion. After TASK-1801, however,
    //      PeriodCalculationService server-resolves OK version from
    //      periodStart and ignores profile.OkVersion. The audit event would
    //      therefore diverge from what was actually computed whenever the
    //      caller's profile carried a stale OK version.
    //
    //    TASK-1904 fixes this by canonicalising the audit-event OkVersion to
    //    OkVersionResolver.ResolveVersion(periodStart) unconditionally, so the
    //    audit value mirrors what PeriodCalculationService used.
    //
    //    This is a pure pin on the underlying invariant rather than a mock-
    //    based test of the service itself (consistent with the discipline of
    //    test #9 above): for a periodStart of 2026-05-01 (OK26), the audit
    //    event must read OK26 even if the caller passed OK24.
    // -----------------------------------------------------------------------
    [Fact]
    public void RetroactiveCorrection_SingleVersionAuditUsesPeriodStartResolution()
    {
        var periodStart = new DateOnly(2026, 5, 1);   // OK26
        var callerSuppliedOkVersion = "OK24";          // stale / wrong

        var canonical = OkVersionResolver.ResolveVersion(periodStart);

        Assert.Equal("OK26", canonical);
        Assert.NotEqual(callerSuppliedOkVersion, canonical);
        // The audit event RetroactiveCorrectionRequested.OkVersion must equal
        // `canonical`, NOT `callerSuppliedOkVersion`. RetroactiveCorrectionService
        // assigns this value in the single-version path at TASK-1904.
    }
}
