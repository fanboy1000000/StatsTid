using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Tests.Unit;

/// <summary>
/// S132 TASK-132-3b (QUAL-005) — the shared Copenhagen business-date helper.
///
/// Plain-language: "which calendar day is it in Copenhagen?" must be answered against the REAL
/// Europe/Copenhagen zone, which is UTC+1 (CET) in winter and UTC+2 (CEST) under daylight-saving
/// time from late March to late October. An instant just before UTC midnight therefore belongs to
/// a DIFFERENT calendar day depending on the season. The former §21 stk.2 deadline guard used a
/// hardcoded +01:00 fallback, so every summer it attributed a midnight-adjacent instant to the
/// wrong day — mis-deciding a statutory deadline. These tests pin the clock (the injectable
/// TimeProvider seam) so the behaviour is deterministic across the DST boundary.
/// </summary>
public class CopenhagenBusinessDateTests
{
    /// <summary>
    /// A deterministic <see cref="TimeProvider"/> that returns a fixed UTC instant — including a
    /// specific time-of-day (unlike the WAF <c>FixedTimeProvider</c>, which pins UTC midnight of a
    /// date). This lets us place the clock just before UTC midnight, where the Copenhagen day
    /// depends on the DST offset.
    /// </summary>
    private sealed class FixedInstantTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedInstantTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    // ── RED-on-old: summer (CEST, +02:00) instant near local midnight ──
    // 2026-07-15 22:30:00Z. Copenhagen in July is CEST (+02:00) ⇒ local 2026-07-16 00:30 ⇒ the
    // Copenhagen calendar day is the 16th. The OLD §21 copy's fixed +01:00 fallback would compute
    // 2026-07-15 23:30 ⇒ the 15th — off by one day, mis-deciding the §21 stk.2 deadline. The
    // shared helper converts through the REAL zone, so it lands on the correct day.
    [Fact]
    public void Today_SummerInstantNearMidnight_ReturnsCorrectCopenhagenDate()
    {
        var provider = new FixedInstantTimeProvider(
            new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero));

        var copenhagenToday = CopenhagenBusinessDate.Today(provider);

        // Correct Copenhagen date under CEST (+02:00). The old fixed +01:00 copy returned the 15th.
        Assert.Equal(new DateOnly(2026, 7, 16), copenhagenToday);
    }

    /// <summary>
    /// Characterizes the QUAL-005 bug directly: for the SAME summer instant, the old fixed +01:00
    /// arithmetic lands on the 15th, while the DST-correct shared helper lands on the 16th. This
    /// locks in WHY the consolidation is a correctness fix, not just a de-duplication.
    /// </summary>
    [Fact]
    public void OldFixedPlusOneOffset_DivergesFromSharedHelper_InSummer()
    {
        var utcNow = new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero);
        var provider = new FixedInstantTimeProvider(utcNow);

        // The exact arithmetic the removed §21 fallback used (DateTime.UtcNow.AddHours(1)).
        var oldFixedPlusOne = DateOnly.FromDateTime(utcNow.UtcDateTime.AddHours(1));
        var shared = CopenhagenBusinessDate.Today(provider);

        Assert.Equal(new DateOnly(2026, 7, 15), oldFixedPlusOne); // WRONG (the old copy's answer)
        Assert.Equal(new DateOnly(2026, 7, 16), shared);          // correct (CEST +02:00)
        Assert.NotEqual(oldFixedPlusOne, shared);                 // the day the bug mis-decided
    }

    // ── Winter control: CET (+01:00) instant near local midnight ──
    // 2026-01-15 23:30:00Z. Copenhagen in January is CET (+01:00) ⇒ local 2026-01-16 00:30 ⇒ the
    // 16th. Here the old +01:00 offset happened to be CORRECT — which is exactly why the summer bug
    // hid: the fixed offset is only right for the winter half of the year.
    [Fact]
    public void Today_WinterInstantNearMidnight_ReturnsCorrectCopenhagenDate()
    {
        var provider = new FixedInstantTimeProvider(
            new DateTimeOffset(2026, 1, 15, 23, 30, 0, TimeSpan.Zero));

        var copenhagenToday = CopenhagenBusinessDate.Today(provider);

        Assert.Equal(new DateOnly(2026, 1, 16), copenhagenToday);
    }

    // ── Deterministic seam: a midday instant maps to its own day in both seasons ──
    [Fact]
    public void Today_MiddayInstant_IsClockDriven_AndDeterministic()
    {
        var summer = new FixedInstantTimeProvider(
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var winter = new FixedInstantTimeProvider(
            new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 7, 15), CopenhagenBusinessDate.Today(summer));
        Assert.Equal(new DateOnly(2026, 1, 15), CopenhagenBusinessDate.Today(winter));
    }

    // =====================================================================================
    // S143 TASK-14311 -- the INSTANT overload: CopenhagenBusinessDate.FromInstant(instant)
    //
    // Plain-language: Today(TimeProvider) reads the clock itself, which is right when the day is
    // all a caller needs and wrong when the caller needs the day AND the instant, because then the
    // caller has to read the clock a second time. Two reads can straddle local midnight, so the row
    // gets stamped with one day and displayed under another (PAT-028). FromInstant takes an
    // ALREADY-CAPTURED instant, so one clock read can serve both.
    //
    // The instants below are the four S142 boundary instants
    // (tests/StatsTid.Tests.Regression/Hosting/BoundaryInstants.cs). They are re-stated here as
    // literals rather than referenced, because the Unit tier must not take a project reference on
    // the Regression tier -- and because the pin's whole value is that the expected day is a
    // HAND-COMPUTED literal, not a second call into the code under test.
    // =====================================================================================

    /// <summary>
    /// The discriminating pin for the new overload. Each row kills a DIFFERENT plausible-wrong
    /// implementation, and no single row kills them all -- which is why all four are here.
    /// </summary>
    [Theory]
    // Summer, 22:30Z. Copenhagen is CEST (+02:00) => local 2026-07-16 00:30, so the Danish day has
    // already rolled over while the UTC day is still the 15th.
    // RED for a raw-UTC implementation (answers the 15th) AND for a hardcoded +01:00 one
    // (22:30 + 1h = 23:30, still the 15th). This row alone kills two of the three wrong shapes.
    [InlineData(2026, 7, 15, 22, 30, 2026, 7, 16)]
    // Winter, 22:30Z. Copenhagen is CET (+01:00) => local 2026-01-15 23:30: still the 15th, and the
    // two calendars AGREE here.
    // RED for a hardcoded +02:00 implementation (22:30 + 2h = 00:30 => it answers the 16th, rolling
    // the day over a full hour before the real CET offset does). Nothing else catches that one.
    [InlineData(2026, 1, 15, 22, 30, 2026, 1, 15)]
    // Winter, 23:30Z. CET => local 2026-01-16 00:30. RED for a raw-UTC implementation; deliberately
    // SILENT about a hardcoded +01:00, which is accidentally correct in January.
    [InlineData(2026, 1, 15, 23, 30, 2026, 1, 16)]
    // Summer month end, 22:30Z. CEST => local 2026-08-01 00:30: the calendars disagree about the
    // MONTH, which is the payroll-visible shape of this defect (every export, settlement and
    // approval period in StatsTid is month-bounded, so a wrong day is a wrong PERIOD).
    // RED for raw-UTC and for hardcoded +01:00 (both answer 31 July).
    [InlineData(2026, 7, 31, 22, 30, 2026, 8, 1)]
    public void FromInstant_AtEachBoundaryInstant_ReturnsTheHandComputedCopenhagenDay(
        int utcYear, int utcMonth, int utcDay, int utcHour, int utcMinute,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var instant = new DateTimeOffset(
            utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, TimeSpan.Zero);

        var copenhagenDay = CopenhagenBusinessDate.FromInstant(instant);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), copenhagenDay);
    }

    /// <summary>
    /// Delegation equivalence: Today(TimeProvider) is implemented AS a clock read followed by
    /// FromInstant, so for any instant the two must agree. This is what stops a second copy of the
    /// conversion arithmetic growing back later -- the defect this sprint keeps finding.
    ///
    /// Note the assertion order. Each side is pinned to the SAME hand-written literal FIRST, and
    /// only then to each other. An assertion that compares two calls into the code under test
    /// cannot disagree with itself however wrong both sides are; S142 deleted five tests of exactly
    /// that shape. The literal is what makes this test able to fail.
    /// </summary>
    [Theory]
    [InlineData(2026, 7, 15, 22, 30, 2026, 7, 16)]
    [InlineData(2026, 1, 15, 22, 30, 2026, 1, 15)]
    [InlineData(2026, 1, 15, 23, 30, 2026, 1, 16)]
    [InlineData(2026, 7, 31, 22, 30, 2026, 8, 1)]
    public void Today_AndFromInstant_AgreeAtEveryBoundaryInstant(
        int utcYear, int utcMonth, int utcDay, int utcHour, int utcMinute,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var instant = new DateTimeOffset(
            utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, TimeSpan.Zero);
        var expected = new DateOnly(expectedYear, expectedMonth, expectedDay);

        var viaTimeProvider = CopenhagenBusinessDate.Today(new FixedInstantTimeProvider(instant));
        var viaInstant = CopenhagenBusinessDate.FromInstant(instant);

        Assert.Equal(expected, viaTimeProvider);   // the literal, not the other side
        Assert.Equal(expected, viaInstant);        // the literal, not the other side
        Assert.Equal(viaTimeProvider, viaInstant); // and therefore: one implementation, not two
    }

    /// <summary>
    /// The overload converts an INSTANT, so it must key on the absolute moment and ignore whatever
    /// offset the caller's <see cref="DateTimeOffset"/> happens to carry. A caller holding
    /// <c>2026-07-16 03:30+05:00</c> is holding the very same moment as <c>2026-07-15 22:30Z</c>,
    /// and both are the 16th in Copenhagen.
    ///
    /// RED for the easy mistake of reading <c>instant.DateTime</c> or <c>instant.Date</c> (the
    /// caller's wall-clock text) instead of converting the instant: that implementation answers the
    /// 16th for the +05:00 form by luck and the 15th for the -04:00 form, which is wrong.
    /// </summary>
    [Fact]
    public void FromInstant_KeysOnTheAbsoluteInstant_NotTheCallersOffsetText()
    {
        // Three spellings of ONE moment: 2026-07-15 22:30 UTC.
        var asUtc = new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero);
        var asPlusFive = new DateTimeOffset(2026, 7, 16, 3, 30, 0, TimeSpan.FromHours(5));
        var asMinusFour = new DateTimeOffset(2026, 7, 15, 18, 30, 0, TimeSpan.FromHours(-4));

        // Copenhagen is CEST (+02:00) at that moment => local 2026-07-16 00:30.
        var expected = new DateOnly(2026, 7, 16);

        Assert.Equal(expected, CopenhagenBusinessDate.FromInstant(asUtc));
        Assert.Equal(expected, CopenhagenBusinessDate.FromInstant(asPlusFive));
        Assert.Equal(expected, CopenhagenBusinessDate.FromInstant(asMinusFour));
    }
}
