using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S70 / TASK-7002 (ADR-033 slice 3a, SPRINT-70 R1 + R6 + R7a) — unit pins on the PURE
/// lifecycle helpers behind the admin employment-end-date endpoint:
/// <see cref="EmploymentDateEndpoints.ComputeEndDateLifecycle"/> (the R1(a)–(d) decision table),
/// <see cref="EmploymentDateEndpoints.FerieaarOf"/> (R6, with the pinned 31 Jan / 31 Aug / 1 Sep
/// boundaries) and <see cref="EmploymentDateEndpoints.AffectedFerieaar"/> (the R7a guard's
/// affected-year set). The HTTP / transactional / lock behavior is pinned in the regression
/// class <c>Settlement.EmploymentEndDateLifecycleTests</c>.
/// </summary>
public class EmploymentEndDateLifecycleLogicTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    // ────────────────────────── R1(a) — set, date already passed, active row ──────────────────────────

    [Fact]
    public void R1a_SetPassedDate_OnActiveUser_DeactivatesWithProvenance()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: new DateOnly(2026, 5, 31), oldIsActive: true,
            oldEndDateDeactivated: false, copenhagenToday: Today);

        Assert.False(isActive);
        Assert.True(provenance);
    }

    /// <summary>Boundary: <c>employment_end_date</c> is the LAST day employed — the flip
    /// requires today STRICTLY after it. endDate == today ⇒ still employed, no flip (R1(b)
    /// side of the boundary); endDate == yesterday ⇒ flip (R1(a)).</summary>
    [Fact]
    public void R1_Boundary_EndDateToday_NoFlip_EndDateYesterday_Flips()
    {
        var onBoundary = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            Today, oldIsActive: true, oldEndDateDeactivated: false, copenhagenToday: Today);
        Assert.True(onBoundary.IsActive);
        Assert.False(onBoundary.EndDateDeactivated);

        var pastBoundary = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            Today.AddDays(-1), oldIsActive: true, oldEndDateDeactivated: false, copenhagenToday: Today);
        Assert.False(pastBoundary.IsActive);
        Assert.True(pastBoundary.EndDateDeactivated);
    }

    // ────────────────────────── R1(b) — future-dated, no flip ──────────────────────────

    [Fact]
    public void R1b_SetFutureDate_OnActiveUser_StoresWithoutFlipOrProvenance()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: new DateOnly(2027, 1, 31), oldIsActive: true,
            oldEndDateDeactivated: false, copenhagenToday: Today);

        Assert.True(isActive);
        Assert.False(provenance);
    }

    // ────────────────────────── R1(c) — clear ──────────────────────────

    [Fact]
    public void R1c_Clear_OnLifecycleDeactivatedUser_ReactivatesAndResetsProvenance()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: null, oldIsActive: false,
            oldEndDateDeactivated: true, copenhagenToday: Today);

        Assert.True(isActive);
        Assert.False(provenance);
    }

    [Fact]
    public void R1c_Clear_OnManuallyDeactivatedUser_DoesNotReactivate()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: null, oldIsActive: false,
            oldEndDateDeactivated: false, copenhagenToday: Today);

        Assert.False(isActive);
        Assert.False(provenance);
    }

    [Fact]
    public void R1c_Clear_OnActiveUserWithFutureDate_StaysActive()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: null, oldIsActive: true,
            oldEndDateDeactivated: false, copenhagenToday: Today);

        Assert.True(isActive);
        Assert.False(provenance);
    }

    // ────────────────────────── R1(d) — set on a manually-inactive user ──────────────────────────

    [Fact]
    public void R1d_SetPassedDate_OnManuallyInactiveUser_RecordsWithoutProvenanceOrFlip()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: new DateOnly(2026, 1, 31), oldIsActive: false,
            oldEndDateDeactivated: false, copenhagenToday: Today);

        Assert.False(isActive);
        Assert.False(provenance); // no provenance claim — the deactivation was manual
    }

    [Fact]
    public void R1d_SetFutureDate_OnManuallyInactiveUser_RecordsWithoutProvenanceOrFlip()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: new DateOnly(2027, 6, 30), oldIsActive: false,
            oldEndDateDeactivated: false, copenhagenToday: Today);

        Assert.False(isActive);
        Assert.False(provenance);
    }

    // ─────────────── correction on a lifecycle-deactivated row (re-evaluation) ───────────────

    [Fact]
    public void Correction_LifecycleDeactivated_ToStillPassedDate_StaysDeactivatedWithProvenance()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: new DateOnly(2026, 4, 30), oldIsActive: false,
            oldEndDateDeactivated: true, copenhagenToday: Today);

        Assert.False(isActive);
        Assert.True(provenance);
    }

    /// <summary>Correcting a lifecycle-deactivated leaver's end date to an UNPASSED date removes
    /// the lifecycle's deactivation basis → reactivate + reset provenance (the Step-A poller
    /// re-flips when the new date passes). The alternative tuple (inactive, provenance=true,
    /// future date) would be unreachable by every other writer — the admin general PUT filters
    /// is_active=TRUE (R1(f)) and the poller only flips active rows.</summary>
    [Fact]
    public void Correction_LifecycleDeactivated_ToFutureDate_Reactivates()
    {
        var (isActive, provenance) = EmploymentDateEndpoints.ComputeEndDateLifecycle(
            newEndDate: new DateOnly(2027, 3, 31), oldIsActive: false,
            oldEndDateDeactivated: true, copenhagenToday: Today);

        Assert.True(isActive);
        Assert.False(provenance);
    }

    // ────────────────────────── R6 — ferieår resolution boundaries ──────────────────────────

    [Theory]
    [InlineData(2026, 1, 31, 2025)]  // 31 Jan → previous ferieår (started 1 Sep 2025)
    [InlineData(2026, 8, 31, 2025)]  // 31 Aug → LAST day of ferieår 2025
    [InlineData(2026, 9, 1, 2026)]   // 1 Sep → FIRST day of ferieår 2026
    public void R6_FerieaarOf_Boundaries(int y, int m, int d, int expectedYear)
    {
        Assert.Equal(expectedYear, EmploymentDateEndpoints.FerieaarOf(new DateOnly(y, m, d)));
    }

    // ────────────────────────── R7a — affected-ferieår set ──────────────────────────

    [Fact]
    public void R7a_AffectedFerieaar_BothNull_Empty()
    {
        Assert.Empty(EmploymentDateEndpoints.AffectedFerieaar(null, null));
    }

    [Fact]
    public void R7a_AffectedFerieaar_SetOnly_NewYearOnly()
    {
        var years = EmploymentDateEndpoints.AffectedFerieaar(null, new DateOnly(2026, 6, 30));
        Assert.Equal(new[] { 2025 }, years);
    }

    [Fact]
    public void R7a_AffectedFerieaar_ClearOnly_OldYearOnly()
    {
        var years = EmploymentDateEndpoints.AffectedFerieaar(new DateOnly(2026, 10, 1), null);
        Assert.Equal(new[] { 2026 }, years);
    }

    [Fact]
    public void R7a_AffectedFerieaar_Correction_AcrossFerieaar_BothYears()
    {
        var years = EmploymentDateEndpoints.AffectedFerieaar(
            new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 1));
        Assert.Equal(2, years.Length);
        Assert.Contains(2025, years);
        Assert.Contains(2026, years);
    }

    [Fact]
    public void R7a_AffectedFerieaar_Correction_SameFerieaar_Deduplicated()
    {
        var years = EmploymentDateEndpoints.AffectedFerieaar(
            new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31));
        Assert.Equal(new[] { 2025 }, years);
    }
}
