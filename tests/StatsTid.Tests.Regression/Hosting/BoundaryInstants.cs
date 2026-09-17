namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S142 / TASK-14200 — the three canonical UTC instants for pinning
/// <see cref="StatsTidWebApplicationFactory.WithFixedInstant"/> against the UTC-vs-Copenhagen
/// business-date boundary. Defined ONCE here so every subsystem task's discriminating test uses the
/// SAME instants (and the same reasoning about which wrong implementation each one kills) instead of
/// each re-deriving its own — five downstream tasks in this sprint need exactly this.
///
/// <para>
/// <b>Why three, not one.</b> A single pin can prove "this code path reads the injected clock at
/// all" but cannot, by itself, prove WHICH conversion the code performs — a no-conversion (raw UTC
/// day) implementation, a plausible-looking hardcoded <c>+01:00</c> (winter-only) offset, and a
/// hardcoded <c>+02:00</c> (summer-only) offset are three DIFFERENT wrong implementations, and a
/// test suite that only ever exercised one of them could pass against either of the other two bugs.
/// Each instant below is chosen to kill exactly one of these, and each is DELIBERATELY silent about
/// (does not discriminate) at least one other — see each field's own doc comment for its RED
/// condition.
/// </para>
///
/// <para>
/// <b>Why not near a DST transition.</b> Denmark's clocks change on the last Sunday of March
/// (CET→CEST, +01:00→+02:00) and the last Sunday of October (CEST→CET). An instant chosen near
/// either transition would risk asserting the transition itself (a genuinely ambiguous or
/// non-existent local time) rather than the plain seasonal-offset rule these three instants exist to
/// pin. All three sit at least ten weeks from the nearest transition.
/// </para>
/// </summary>
public static class BoundaryInstants
{
    /// <summary>
    /// Summer, 22:30 UTC (2026-07-15). Copenhagen is CEST (UTC+02:00) in July, so local time is
    /// already 2026-07-16 00:30 — the Copenhagen calendar day has rolled over to the 16th while the
    /// UTC calendar day is still the 15th.
    ///
    /// <para>
    /// <b>Kills a no-conversion (raw UTC day) implementation</b> — it would answer "the 15th",
    /// wrong by one day. <b>Also kills a hardcoded <c>+01:00</c> implementation</b> — 22:30 + 1h =
    /// 23:30, STILL the 15th in that arithmetic, also wrong; only the real +02:00 CEST offset
    /// crosses midnight here. This is the one instant of the three that fails both of those wrong
    /// implementations at once.
    /// </para>
    /// </summary>
    public static readonly DateTimeOffset SummerEveningAlreadyTomorrowInCopenhagen =
        new(2026, 7, 15, 22, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Winter, 23:30 UTC (2026-01-15). Copenhagen is CET (UTC+01:00) in January, so local time is
    /// already 2026-01-16 00:30 — the Copenhagen calendar day has rolled over to the 16th while the
    /// UTC calendar day is still the 15th.
    ///
    /// <para>
    /// <b>Kills a no-conversion (raw UTC day) implementation</b> — same shape as the summer instant
    /// above: it would answer "the 15th", wrong by one day. <b>Deliberately does NOT kill a
    /// hardcoded <c>+01:00</c> implementation</b> — 23:30 + 1h = 00:30 on the 16th, which happens to
    /// be the CORRECT answer this time of year, so a fixed +01:00 offset passes here even though it
    /// is wrong every summer. That blind spot is exactly why the third instant below exists: on its
    /// own, this instant cannot distinguish "correctly converts through the real Europe/Copenhagen
    /// zone" from "hardcodes winter's offset year-round".
    /// </para>
    /// </summary>
    public static readonly DateTimeOffset WinterEveningAlreadyTomorrowInCopenhagen =
        new(2026, 1, 15, 23, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Winter, 22:30 UTC (2026-01-15) — the SAME calendar day as
    /// <see cref="WinterEveningAlreadyTomorrowInCopenhagen"/>, one hour earlier. Copenhagen is CET
    /// (UTC+01:00), so local time is 2026-01-15 23:30 — still the 15th. The UTC day and the
    /// Copenhagen day AGREE here (both the 15th).
    ///
    /// <para>
    /// <b>Kills a hardcoded <c>+02:00</c> implementation</b> (the summer offset applied year-round):
    /// 22:30 + 2h = 00:30 on the 16th — that arithmetic rolls the day over a full hour before the
    /// real CET offset does, so it answers "the 16th" where the correct Copenhagen day is still the
    /// 15th. A no-conversion or a correct/hardcoded <c>+01:00</c> implementation all agree with the
    /// correct answer at this instant (none of them is what this instant is testing) — it exists
    /// specifically to catch the +02:00 mistake that the other two instants cannot.
    /// </para>
    ///
    /// <para>
    /// The task brief that introduced this instant suggested "e.g. mid-morning" as an example of a
    /// UTC/Copenhagen-agreeing moment — but a genuine mid-morning instant (say, 10:00 UTC) does NOT
    /// discriminate a hardcoded +02:00 bug from a correct implementation, because adding either one
    /// or two extra hours to a mid-morning time stays well clear of any midnight boundary. To
    /// actually fail a +02:00 implementation the instant must sit within the one-hour window where
    /// the correct (+01:00) conversion has not yet crossed midnight but a +02:00 conversion has —
    /// hence 22:30, not mid-morning. Recorded here rather than silently deviating from the brief.
    /// </para>
    /// </summary>
    public static readonly DateTimeOffset WinterEveningCalendarsStillAgree =
        new(2026, 1, 15, 22, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// <b>A fourth instant, added at the wave-2 merge because two independent tasks needed it and
    /// each had to define its own copy.</b> 2026-07-31 22:30 UTC — in Copenhagen (CEST, +02:00) it
    /// is already <b>1 August 00:30</b>, so the two calendars disagree about the MONTH, not merely
    /// the day.
    ///
    /// <para>
    /// <b>Why the three instants above cannot serve this.</b> All three sit mid-month, so any code
    /// that clips a date to its month — an export window, a settlement period, an approval period —
    /// gets the same answer from the UTC day and the Copenhagen day, and a test built on them
    /// passes under either. <b>The month-boundary form is the payroll-visible shape of this
    /// defect</b>: every export, settlement and approval period in StatsTid is month-bounded, so a
    /// day that lands in the wrong month lands in the wrong PERIOD.
    /// </para>
    ///
    /// <para>
    /// <b>RED condition — and why 22:30 rather than 23:30.</b> A raw-UTC implementation answers
    /// "31 July" (wrong month). A hardcoded <c>+01:00</c> answers 23:30 on the 31st — also "July",
    /// so it dies here too. Only the real CEST offset rolls into August. One pin therefore kills
    /// both the no-conversion bug AND the winter-offset-year-round bug. It is DELIBERATELY silent
    /// about a hardcoded <c>+02:00</c>, which agrees with the correct answer here —
    /// <see cref="WinterEveningCalendarsStillAgree"/> is what catches that one. At 23:30 the
    /// <c>+01:00</c> implementation would also roll into August and the pin would lose half its
    /// discriminating power.
    /// </para>
    ///
    /// <para>Twelve weeks from the nearest DST transition, consistent with the three above.</para>
    /// </summary>
    public static readonly DateTimeOffset SummerMonthEndAlreadyNextMonthInCopenhagen =
        new(2026, 7, 31, 22, 30, 0, TimeSpan.Zero);
}
