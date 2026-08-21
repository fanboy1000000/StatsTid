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
}
