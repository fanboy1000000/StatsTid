using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// Tests for <see cref="OkVersionCanonicalization.Resolve"/> — the pure helper
/// the <see cref="RetroactiveCorrectionService"/> uses to choose between
/// caller-supplied OK versions and date-resolved truth before stamping the
/// <c>RetroactiveCorrectionRequested</c> audit event.
///
/// Background: TASK-1801 made <c>PeriodCalculationService</c> server-resolve
/// OK version from <c>periodStart</c>, ignoring <c>profile.OkVersion</c>.
/// TASK-1904 ensures the audit event mirrors that resolution so the audit
/// chain doesn't diverge from what was computed. Codex first-pass S19 review
/// flagged that the original regression test only pinned
/// <c>OkVersionResolver</c>'s underlying invariant — if the service-level
/// branch reverted to writing <c>profile.OkVersion</c> in the single-version
/// path, no test would fail. Pulling the choice into a pure helper closes
/// that gap: the per-branch behaviour pins HERE, and the service is a thin
/// wrapper.
///
/// Effective dates from <c>OkVersionResolver</c>:
///   * OK24: 2024-04-01 .. 2026-03-31
///   * OK26: 2026-04-01 .. 2028-03-31
/// </summary>
public class OkVersionCanonicalizationTests
{
    // -----------------------------------------------------------------------
    // Single-version path — periodStart resolution wins (TASK-1904 BLOCKER fix)
    // -----------------------------------------------------------------------

    [Fact]
    public void SingleVersion_PeriodStartInOk26_UsesResolvedOk26_EvenIfCallerSaysOk24()
    {
        // The exact scenario the TASK-1904 validation criterion calls out:
        // periodStart=2026-05-01 (OK26) but caller-supplied profile.OkVersion=OK24
        // (stale). PeriodCalculationService uses OK26; the audit event must too.
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK24",
            periodStart: new DateOnly(2026, 5, 1),
            okTransitionDate: null,
            callerPreviousOkVersion: null);

        Assert.Equal("OK26", canonical.CurrentOkVersion);
        Assert.Null(canonical.PreviousOkVersion);
        Assert.True(canonical.CurrentDrifted);   // caller said OK24, resolver said OK26
        Assert.False(canonical.PreviousDrifted);
    }

    [Fact]
    public void SingleVersion_PeriodStartInOk24_UsesResolvedOk24()
    {
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK24",
            periodStart: new DateOnly(2025, 9, 15),  // OK24
            okTransitionDate: null,
            callerPreviousOkVersion: null);

        Assert.Equal("OK24", canonical.CurrentOkVersion);
        Assert.Null(canonical.PreviousOkVersion);
        Assert.False(canonical.CurrentDrifted);  // caller and resolver agree
        Assert.False(canonical.PreviousDrifted);
    }

    [Fact]
    public void SingleVersion_CallerMatchesResolved_NoDriftReported()
    {
        // No drift case — caller passed the right OK version. CurrentDrifted
        // must be false so the service doesn't log a spurious warning.
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK26",
            periodStart: new DateOnly(2026, 5, 1),
            okTransitionDate: null,
            callerPreviousOkVersion: null);

        Assert.Equal("OK26", canonical.CurrentOkVersion);
        Assert.False(canonical.CurrentDrifted);
    }

    // -----------------------------------------------------------------------
    // Split path — transition-date resolution wins on both sides
    // -----------------------------------------------------------------------

    [Fact]
    public void Split_OnTransitionDate_ResolvesCurrentFromTransitionPreviousFromDayBefore()
    {
        // OK24 → OK26 transition is 2026-04-01. Segment 2 (post-transition)
        // starts on the transition date itself; segment 1 (pre-transition)
        // ends the day before.
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK26",
            periodStart: new DateOnly(2026, 3, 25),
            okTransitionDate: new DateOnly(2026, 4, 1),
            callerPreviousOkVersion: "OK24");

        Assert.Equal("OK26", canonical.CurrentOkVersion);   // resolved from 2026-04-01
        Assert.Equal("OK24", canonical.PreviousOkVersion);  // resolved from 2026-03-31
        Assert.False(canonical.CurrentDrifted);
        Assert.False(canonical.PreviousDrifted);
    }

    [Fact]
    public void Split_CallerCurrentDriftsFromResolved_FlagSet()
    {
        // Caller said OK24 for the post-transition segment — wrong, resolver
        // returns OK26 for 2026-04-01. The flag must surface so the service
        // can log a warning; the canonical value is still the resolver's.
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK24",   // wrong
            periodStart: new DateOnly(2026, 3, 25),
            okTransitionDate: new DateOnly(2026, 4, 1),
            callerPreviousOkVersion: "OK24");

        Assert.Equal("OK26", canonical.CurrentOkVersion);
        Assert.True(canonical.CurrentDrifted);
        Assert.False(canonical.PreviousDrifted);  // caller's previous IS OK24, matches resolver
    }

    [Fact]
    public void Split_CallerPreviousDriftsFromResolved_FlagSet()
    {
        // Caller said OK26 for the pre-transition segment — wrong, resolver
        // returns OK24 for 2026-03-31.
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK26",
            periodStart: new DateOnly(2026, 3, 25),
            okTransitionDate: new DateOnly(2026, 4, 1),
            callerPreviousOkVersion: "OK26");  // wrong

        Assert.Equal("OK26", canonical.CurrentOkVersion);
        Assert.Equal("OK24", canonical.PreviousOkVersion);
        Assert.True(canonical.PreviousDrifted);
        Assert.False(canonical.CurrentDrifted);
    }

    // -----------------------------------------------------------------------
    // Defensive — mixed/incomplete arguments fall back to single-version path
    // -----------------------------------------------------------------------

    [Fact]
    public void TransitionDateWithoutPreviousOkVersion_TreatedAsSingleVersionPath()
    {
        // Service-layer pre-validation rejects this combination, but the
        // helper must not throw — fall back to single-version semantics
        // (resolve from periodStart) so a future caller pattern doesn't
        // hit a NullReferenceException.
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK26",
            periodStart: new DateOnly(2026, 5, 1),
            okTransitionDate: new DateOnly(2026, 4, 1),
            callerPreviousOkVersion: null);

        Assert.Equal("OK26", canonical.CurrentOkVersion);
        Assert.Null(canonical.PreviousOkVersion);
    }

    [Fact]
    public void PreviousOkVersionWithoutTransitionDate_TreatedAsSingleVersionPath()
    {
        var canonical = OkVersionCanonicalization.Resolve(
            callerCurrentOkVersion: "OK26",
            periodStart: new DateOnly(2026, 5, 1),
            okTransitionDate: null,
            callerPreviousOkVersion: "OK24");

        Assert.Equal("OK26", canonical.CurrentOkVersion);
        Assert.Null(canonical.PreviousOkVersion);
    }
}
