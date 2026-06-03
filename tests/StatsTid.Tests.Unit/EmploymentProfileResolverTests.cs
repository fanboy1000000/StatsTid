using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S62 / TASK-6202 — unit coverage for the <c>EmploymentProfileResolver.GetFractionHistoryAsync</c>
/// projection/ordering contract (the SharedKernel <see cref="FractionPeriod"/> invariants the
/// resolver's reader-loop relies on).
///
/// <para>
/// The resolver itself opens a real PostgreSQL connection, so a true DB round-trip — exercising the
/// row-overlap SQL predicate, <c>ORDER BY effective_from</c>, the nullable <c>effective_to</c> read,
/// and the empty-list-on-no-rows / never-throw guarantee — is a Docker-gated integration test owned
/// by TASK-6204 (see the <c>Skip</c>-ped placeholder at the bottom for the exact assertions it must
/// pin). This suite stays honest: it does NOT fake a DB. Instead it locks the pure value-type
/// contract that <c>GetFractionHistoryAsync</c> projects each row into and orders by —
/// <see cref="FractionPeriod"/> round-tripping its <c>(From, To, Fraction)</c> tuple and the
/// end-exclusive <see cref="FractionPeriod.Covers"/> boundary (ADR-018 D8, end-exclusive
/// <c>effective_to</c>). If these invariants drift, the resolver's history projection is wrong even
/// before it reaches a DB.
/// </para>
/// </summary>
public class EmploymentProfileResolverTests
{
    // ──────────────────────────────────────────────────────────────────────────────────────────
    // FractionPeriod projection contract — the resolver reads each employee_profiles row into
    // exactly FractionPeriod(effective_from, effective_to?, part_time_fraction). These pin that the
    // value type carries the tuple losslessly (incl. NUMERIC(4,3) scale and a null open-ended To).
    // ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FractionPeriod_RoundTripsTheProjectedTuple_ClosedPeriod()
    {
        // A closed (effective_to NOT NULL) row, e.g. full-time Sep–Dec 2025.
        var from = new DateOnly(2025, 9, 1);
        var to = new DateOnly(2026, 1, 1);
        var period = new FractionPeriod(from, to, 1.0m);

        Assert.Equal(from, period.From);
        Assert.Equal(to, period.To);
        Assert.Equal(1.0m, period.Fraction);
    }

    [Fact]
    public void FractionPeriod_RoundTripsTheProjectedTuple_OpenEndedLiveRow()
    {
        // The live (current) row has effective_to NULL ⇒ To is null (open-ended).
        var from = new DateOnly(2026, 1, 1);
        var period = new FractionPeriod(from, null, 0.5m);

        Assert.Equal(from, period.From);
        Assert.Null(period.To);
        Assert.Equal(0.5m, period.Fraction);
    }

    [Fact]
    public void FractionPeriod_PreservesNumeric4_3Scale()
    {
        // part_time_fraction is NUMERIC(4,3); a scale-3 fraction must survive the projection exactly.
        var period = new FractionPeriod(new DateOnly(2025, 9, 1), null, 0.375m);
        Assert.Equal(0.375m, period.Fraction);
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // Covers — the half-open [From, To) boundary the SQL overlap predicate mirrors
    // (effective_from < @to AND (effective_to IS NULL OR effective_to > @from)). From inclusive,
    // To exclusive, null-To open-ended. Adjacent periods abut without overlap when successor.From
    // == predecessor.To.
    // ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Covers_FromIsInclusive()
    {
        var period = new FractionPeriod(new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 1), 1.0m);
        Assert.True(period.Covers(new DateOnly(2025, 9, 1))); // first day IS covered
    }

    [Fact]
    public void Covers_ToIsExclusive()
    {
        var period = new FractionPeriod(new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 1), 1.0m);
        // The To day is the FIRST day NO LONGER covered.
        Assert.False(period.Covers(new DateOnly(2026, 1, 1)));
        Assert.True(period.Covers(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void Covers_BeforeFrom_IsNotCovered()
    {
        var period = new FractionPeriod(new DateOnly(2025, 9, 1), new DateOnly(2026, 1, 1), 1.0m);
        Assert.False(period.Covers(new DateOnly(2025, 8, 31)));
    }

    [Fact]
    public void Covers_NullTo_IsOpenEnded_CoversEveryDateAtOrAfterFrom()
    {
        var period = new FractionPeriod(new DateOnly(2026, 1, 1), null, 0.5m);
        Assert.True(period.Covers(new DateOnly(2026, 1, 1)));    // From inclusive
        Assert.True(period.Covers(new DateOnly(2099, 12, 31)));  // far future still covered
        Assert.False(period.Covers(new DateOnly(2025, 12, 31))); // before From still excluded
    }

    [Fact]
    public void Covers_AdjacentPeriodsAbutWithoutOverlapOrGap()
    {
        // The ordered-history shape the resolver returns: a closed period immediately followed by
        // the live one, successor.From == predecessor.To. The boundary day belongs to the successor
        // ONLY — no date is covered by both, none falls in a gap.
        var boundary = new DateOnly(2026, 1, 1);
        var earlier = new FractionPeriod(new DateOnly(2025, 9, 1), boundary, 1.0m);
        var later = new FractionPeriod(boundary, null, 0.5m);

        Assert.False(earlier.Covers(boundary)); // predecessor's To is exclusive
        Assert.True(later.Covers(boundary));     // belongs to the successor
        Assert.NotEqual(earlier.Fraction, later.Fraction);
    }

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // DB round-trip — Docker-gated, owned by TASK-6204. Documented here (not faked) so the intended
    // assertions are pinned in one place next to the contract they exercise.
    // ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact(Skip = "Docker-gated — see TASK-6204 integration coverage")]
    public void GetFractionHistoryAsync_DbRoundTrip_PinsOverlapOrderingAndEmptyListSemantics()
    {
        // Intended integration assertions against a seeded employee_profiles table:
        //  (1) Returns EVERY period overlapping [from, to) under the row-overlap predicate
        //      effective_from < @to AND (effective_to IS NULL OR effective_to > @from) — including
        //      a period that straddles the window's left edge (effective_from < from < effective_to)
        //      and the open-ended live row (effective_to IS NULL).
        //  (2) Result is ordered ascending by effective_from.
        //  (3) The open-ended live row projects To == null; a closed row projects its exact
        //      effective_to (end-exclusive, ADR-018 D8) and part_time_fraction at NUMERIC(4,3) scale.
        //  (4) No row overlapping the window ⇒ EMPTY list (never null), and the call NEVER throws —
        //      no users JOIN, no agreement-code lookup, no EmployeeProfileNotFoundException (contrast
        //      GetByEmployeeIdAtAsync, which fail-loud-throws on a missing agreement-code row).
        //  (5) A row entirely outside the window (effective_to <= from, or effective_from >= to) is
        //      excluded.
        Assert.True(true);
    }
}
