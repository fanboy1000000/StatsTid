using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S143 / TASK-14300 — the always-runnable pins for <c>GET /api/calendar/today</c>'s computation:
/// <see cref="CalendarEndpoints.ComputeServerDay"/>.
///
/// <para><b>What is being proved, in plain language.</b> The frontend asks the server two things at
/// startup: which Danish calendar day is it, and how many seconds until that day rolls over (so it
/// can schedule its own refresh without ever reading the device clock — the whole point of the
/// sprint). The second number is the interesting one: it is NOT always 86400. Denmark's clocks move
/// twice a year, so one day in March is 23 hours long and one day in October is 25. If the server
/// answered 86400 on those days the frontend would refresh an hour late (March) or an hour early
/// (October) and, for that hour, render and FILE time against the wrong Danish day.</para>
///
/// <para><b>Why these tests live in the Unit suite.</b> The endpoint itself can only be exercised
/// through the Postgres-backed test host, which is Docker-gated and therefore does not run on every
/// developer machine. The arithmetic under test is pure, so it gets a tier that always runs; the
/// Docker-gated <c>S143CalendarSpecRuntimeTests</c> separately proves the wire carries these same
/// values.</para>
///
/// <para><b>Expected values are LITERALS derived from the published transition rule</b> — the EU
/// changes clocks on the last Sunday of March (02:00 CET → 03:00 CEST) and the last Sunday of
/// October (03:00 CEST → 02:00 CET), which in 2026 are 29 March and 25 October. Nothing below is
/// obtained by calling the code under test, or by re-deriving an offset in test code; that
/// self-referential shape is exactly what S142 spent a sprint deleting, because a test that asks the
/// implementation what it thinks cannot fail when the implementation is wrong.</para>
/// </summary>
public class CalendarServerDayTests
{
    /// <summary>
    /// A deterministic <see cref="TimeProvider"/> returning one fixed UTC instant, time-of-day
    /// included. Mirrors the helper in <c>CopenhagenBusinessDateTests</c>; the WAF
    /// <c>FixedTimeProvider</c> pins UTC MIDNIGHT of a date, which is the one moment of the day where
    /// the UTC and Danish calendars agree and so is useless for anything here.
    /// </summary>
    private sealed class FixedInstantTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedInstantTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private static CalendarTodayResponse Compute(DateTimeOffset instant) =>
        CalendarEndpoints.ComputeServerDay(new FixedInstantTimeProvider(instant));

    // ════════════════════════════════════════════════════════════════════════════════
    //  The two DST days — the reason this computation is specified rather than improvised.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The 23-hour day.</b> 2026-03-29 is the last Sunday of March, when Danish clocks jump from
    /// 02:00 CET straight to 03:00 CEST — that Danish day contains only 23 hours.
    ///
    /// <para>The instant pinned is 2026-03-28 23:00:00Z. Copenhagen is still CET (+01:00) at that
    /// moment, so local time is exactly 2026-03-29 00:00 — the first instant of the short day. The
    /// next Danish midnight is 2026-03-30 00:00 local, which under CEST (+02:00) is
    /// 2026-03-29 22:00:00Z. The gap is 23 hours = <b>82800</b> seconds.</para>
    ///
    /// <para><b>What this kills.</b> An "add 24 hours" implementation answers 86400 — an hour too
    /// long, so the client would keep rendering 29 March for the first hour of 30 March. A "reuse
    /// today's UTC offset" implementation places tomorrow's local midnight at +01:00, i.e.
    /// 2026-03-29 23:00:00Z, and also answers 86400. Only converting tomorrow's LOCAL midnight
    /// through the real zone — which applies TOMORROW's offset — produces 82800.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_AtStartOfThe23HourDay_Returns82800Seconds()
    {
        var facts = Compute(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 3, 29), facts.Today);
        Assert.Equal(82800, facts.SecondsUntilNextMidnight); // 23 h — NOT 86400
    }

    /// <summary>
    /// <b>The 25-hour day.</b> 2026-10-25 is the last Sunday of October, when Danish clocks fall back
    /// from 03:00 CEST to 02:00 CET — that Danish day contains 25 hours.
    ///
    /// <para>The instant pinned is 2026-10-24 22:00:00Z. Copenhagen is still CEST (+02:00), so local
    /// time is exactly 2026-10-25 00:00 — the first instant of the long day. The next Danish midnight
    /// is 2026-10-26 00:00 local, which under CET (+01:00) is 2026-10-25 23:00:00Z. The gap is
    /// 25 hours = <b>90000</b> seconds.</para>
    ///
    /// <para><b>What this kills.</b> "Add 24 hours" answers 86400 — an hour too short, so the client
    /// would refresh while it is still 25 October, get the same day back, and (without the ceiling
    /// rule) re-schedule against a near-zero delay. "Reuse today's UTC offset" places tomorrow's
    /// local midnight at +02:00, i.e. 2026-10-25 22:00:00Z, and also answers 86400. This case and the
    /// March one fail the same two wrong implementations in OPPOSITE directions, which is why both
    /// are pinned rather than one.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_AtStartOfThe25HourDay_Returns90000Seconds()
    {
        var facts = Compute(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 25), facts.Today);
        Assert.Equal(90000, facts.SecondsUntilNextMidnight); // 25 h — NOT 86400
    }

    /// <summary>
    /// <b>The 23-hour day, observed from the middle of it</b> — the shape a real client actually
    /// hits, since almost nobody loads the app at the exact first instant of a day.
    ///
    /// <para>2026-03-29 10:00:00Z is 12:00 local (CEST, the clocks having already moved). The next
    /// Danish midnight is 2026-03-29 22:00:00Z, so the answer is 12 hours = <b>43200</b> seconds.
    /// Included because the two start-of-day cases above would both still pass an implementation that
    /// returned "the LENGTH of today" rather than "the time REMAINING in today" — a plausible mistake
    /// that a day-length-shaped test cannot see.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_MiddayOnThe23HourDay_ReturnsTimeRemainingNotDayLength()
    {
        var facts = Compute(new DateTimeOffset(2026, 3, 29, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 3, 29), facts.Today);
        Assert.Equal(43200, facts.SecondsUntilNextMidnight); // 12 h remaining, not the 23 h day length
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The ordinary days — the control, and the UTC-vs-Danish boundary.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The summer boundary instant, the marquee case for the DAY member.</b>
    /// 2026-07-15 22:30:00Z: Copenhagen is CEST (+02:00) in July, so local time is already
    /// 2026-07-16 00:30 — the Danish calendar day has rolled over while the UTC day still reads the
    /// 15th. The endpoint must answer <b>2026-07-16</b>.
    ///
    /// <para>This single instant fails BOTH plausible day bugs: a raw-UTC-day implementation answers
    /// the 15th, and a hardcoded +01:00 offset (22:30 + 1h = 23:30) also answers the 15th. Only
    /// conversion through the real Europe/Copenhagen zone crosses midnight here. The remaining
    /// seconds are 23 h 30 m = <b>84600</b> (00:30 local into a normal 24-hour day).</para>
    ///
    /// <para>The same instant is <c>BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen</c> in
    /// the Regression suite, where the Docker-gated class asserts the same literal date over the
    /// wire. It is restated here rather than referenced because the Unit project does not (and should
    /// not) reference the Regression project.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_SummerInstantAlreadyTomorrowInCopenhagen_IsTheDanishDay()
    {
        var facts = Compute(new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 7, 16), facts.Today);
        Assert.Equal(84600, facts.SecondsUntilNextMidnight); // 23 h 30 m left of a normal 24 h day
    }

    /// <summary>
    /// <b>The guard against over-correction.</b> 2026-01-15 22:30:00Z — one hour earlier in the day
    /// than the winter rollover. Copenhagen is CET (+01:00), so local time is 2026-01-15 23:30: still
    /// the 15th, and the two calendars AGREE.
    ///
    /// <para>Without this, a hardcoded +02:00 (the summer offset applied year-round) would pass every
    /// case above: 22:30 + 2h rolls the day over a full hour before the real CET offset does. Moving
    /// business dates to Copenhagen must not move them a day too FAR — the rule is "the correct Danish
    /// day", not "always tomorrow". Remaining: 30 minutes = <b>1800</b> seconds.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_WinterInstantWhereCalendarsAgree_IsStillTheFifteenth()
    {
        var facts = Compute(new DateTimeOffset(2026, 1, 15, 22, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 1, 15), facts.Today);
        Assert.Equal(1800, facts.SecondsUntilNextMidnight); // 30 min to Danish midnight
    }

    /// <summary>
    /// <b>An ordinary winter day, mid-morning</b> — the boring control that proves the two DST cases
    /// are not the implementation's only correct answers. 2026-01-15 09:00:00Z is 10:00 local (CET);
    /// the next Danish midnight is 2026-01-15 23:00:00Z, so the answer is 14 hours = <b>50400</b>
    /// seconds, and the day length that bounds it is the plain 24.
    /// </summary>
    [Fact]
    public void ComputeServerDay_OrdinaryWinterMorning_ReturnsTheRemainderOfANormalDay()
    {
        var facts = Compute(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 1, 15), facts.Today);
        Assert.Equal(50400, facts.SecondsUntilNextMidnight); // 14 h
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The contract the client relies on.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The value is a usable timer delay at every minute of both transition days.</b> The client
    /// does <c>setTimeout(refresh, seconds * 1000)</c>, so two properties must hold everywhere, not
    /// merely at the instants hand-picked above: the delay is strictly positive (a zero or negative
    /// delay fires immediately and spins), and it never exceeds the longest possible Danish day.
    ///
    /// <para>Sweeping both transition days minute by minute is what makes this a property rather than
    /// a spot check — it walks straight through the 02:00–03:00 window where the clocks actually move,
    /// including the repeated hour in October. The bound 90000 is the 25-hour day stated as a literal,
    /// not read back from the code.</para>
    /// </summary>
    [Theory]
    [InlineData(2026, 3, 28)] // the UTC day containing the start of the 23-hour Danish day
    [InlineData(2026, 3, 29)] // the 23-hour Danish day itself
    [InlineData(2026, 10, 24)] // the UTC day containing the start of the 25-hour Danish day
    [InlineData(2026, 10, 25)] // the 25-hour Danish day itself
    public void ComputeServerDay_AcrossEveryMinuteOfATransitionDay_IsAlwaysAUsableDelay(int year, int month, int day)
    {
        var start = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);

        for (var minute = 0; minute < 24 * 60; minute++)
        {
            var instant = start.AddMinutes(minute);
            var facts = Compute(instant);

            Assert.True(
                facts.SecondsUntilNextMidnight > 0,
                $"{instant:O} produced a non-positive delay ({facts.SecondsUntilNextMidnight}s), " +
                "which a client timer would fire on immediately and then re-schedule in a loop.");
            Assert.True(
                facts.SecondsUntilNextMidnight <= 90000,
                $"{instant:O} produced {facts.SecondsUntilNextMidnight}s, longer than the longest " +
                "possible Danish day (25 h = 90000 s).");
        }
    }

    /// <summary>
    /// <b>The delay lands on the next Danish midnight, stated in UTC.</b> Advancing the clock by
    /// exactly the returned number of seconds must arrive at the literal UTC instant at which the
    /// Danish calendar day rolls over — that is what the number MEANS, and it is the invariant a
    /// client depends on when it schedules its refresh.
    ///
    /// <para><b>Rewritten after review to remove a self-referential oracle.</b> This test used to
    /// advance the clock and then call <c>Compute</c> AGAIN to ask what day it had landed on — i.e.
    /// it compared the code against itself, so a consistently-wrong implementation agreed with its
    /// own wrong answer and the test passed. That is the exact shape S142 spent a sprint deleting.
    /// The expected arrival instant is now a hand-derived UTC literal with no second call:
    /// 2026-03-29 22:00Z IS 2026-03-30 00:00 local (CEST), 2026-10-25 23:00Z IS 2026-10-26 00:00
    /// local (CET), 2026-07-16 22:00Z IS 2026-07-17 00:00 local (CEST), and 2026-01-15 23:00Z IS
    /// 2026-01-16 00:00 local (CET).</para>
    ///
    /// <para><b>The rewrite also made it strictly sharper, which is worth recording because the
    /// review of the OLD version reasonably concluded otherwise.</b> Asserting a landing DAY could
    /// not catch the March case: an add-24-hours implementation overshoots to 2026-03-29 23:00Z,
    /// which is 01:00 local on the 30th — the right DAY, so the old assertion passed and only the
    /// October direction discriminated. Asserting the landing INSTANT catches it, because 23:00Z is
    /// not 22:00Z. Measured against an injected add-24-hours implementation, both the March and the
    /// October rows now fail (and both fail again under a reuse-today's-offset implementation).</para>
    /// </summary>
    [Theory]
    // from (UTC)                     → arrives at (UTC) = the next Danish midnight
    [InlineData(2026, 3, 28, 23, 0, 2026, 3, 29, 22, 0)]   // start of the 23-hour day → 30 Mar 00:00 local
    [InlineData(2026, 10, 24, 22, 0, 2026, 10, 25, 23, 0)] // start of the 25-hour day → 26 Oct 00:00 local
    [InlineData(2026, 7, 15, 22, 30, 2026, 7, 16, 22, 0)]  // already 16 Jul locally   → 17 Jul 00:00 local
    [InlineData(2026, 1, 15, 22, 30, 2026, 1, 15, 23, 0)]  // still 15 Jan locally     → 16 Jan 00:00 local
    public void ComputeServerDay_AdvancingByTheReturnedDelay_ArrivesAtTheNextDanishMidnight(
        int y, int mo, int d, int h, int mi,
        int expectedY, int expectedMo, int expectedD, int expectedH, int expectedMi)
    {
        var instant = new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero);
        var facts = Compute(instant);

        var arrival = instant.AddSeconds(facts.SecondsUntilNextMidnight);

        Assert.Equal(
            new DateTimeOffset(expectedY, expectedMo, expectedD, expectedH, expectedMi, 0, TimeSpan.Zero),
            arrival);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The rounding rule — the safeguard the first version of this suite could not see.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>One millisecond before the rollover, the answer is 1 — not 0.</b> 2026-07-15 21:59:59.999Z
    /// is 23:59:59.999 local (CEST); the next Danish midnight is 2026-07-15 22:00:00.000Z, exactly
    /// one millisecond later.
    ///
    /// <para><b>Why this test exists.</b> Every other instant in this suite falls on a whole second,
    /// so the round-UP rule was invisible: truncation would have kept all of them green. It would
    /// also have returned <b>0</b> here, and a client that schedules <c>setTimeout(refresh, 0)</c>
    /// fires immediately, re-reads the day it already had, and schedules 0 again — a spin loop for
    /// the last second of every day. The ceiling is the thing that guarantees the refresh lands on or
    /// AFTER the new day, and this is the only test that can tell the difference.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_OneMillisecondBeforeRollover_RoundsUpToOneSecond()
    {
        var facts = Compute(new DateTimeOffset(2026, 7, 15, 21, 59, 59, 999, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 7, 15), facts.Today);
        Assert.Equal(1, facts.SecondsUntilNextMidnight); // truncation would answer 0 and spin
    }

    /// <summary>
    /// <b>A fractional remainder rounds UP, not to the NEAREST.</b> 2026-07-15 21:29:59.600Z leaves
    /// 1800.4 seconds until the Danish midnight at 22:00:00.000Z. The answer must be <b>1801</b>.
    ///
    /// <para>Round-to-nearest would answer 1800 and truncation would answer 1800 — both firing the
    /// client's timer 0.4 s BEFORE the day changes, which is the same defect as the case above, just
    /// small enough to look harmless. Pinning the .4 case fixes the rule as "ceiling" rather than
    /// leaving "some kind of rounding" as the contract.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_FractionalRemainder_RoundsUpNotToNearest()
    {
        var facts = Compute(new DateTimeOffset(2026, 7, 15, 21, 29, 59, 600, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 7, 15), facts.Today);
        Assert.Equal(1801, facts.SecondsUntilNextMidnight); // nearest/truncate would both say 1800
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The single-read rule — the other safeguard nothing could see.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A <see cref="TimeProvider"/> that COUNTS its reads and returns a DIFFERENT instant each time,
    /// so an implementation that reads the clock more than once is observable. Every other provider
    /// in this suite is frozen, which means every other test passes whether the clock is read once or
    /// five times — the property <c>CapturedInstantTimeProvider</c> exists to guarantee was the one
    /// thing nothing could see.
    /// </summary>
    private sealed class CountingTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset[] _instants;
        public CountingTimeProvider(params DateTimeOffset[] instants) => _instants = instants;
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            var value = _instants[Math.Min(Reads, _instants.Length - 1)];
            Reads++;
            return value;
        }
    }

    /// <summary>
    /// <b>The clock is read exactly once, and the two members therefore describe the same moment.</b>
    ///
    /// <para><b>The scenario.</b> The first read is 2026-07-15 21:59:59.999Z — 23:59:59.999 local, so
    /// the Danish day is the 15th with 1 second to go. The second read is 2026-07-15 22:00:00.001Z,
    /// two milliseconds later and just PAST the Danish midnight, so it belongs to the 16th. A
    /// production request really can straddle that boundary; it is a microsecond window, but it is
    /// the window in which the response would become self-contradictory.</para>
    ///
    /// <para><b>What a two-read implementation produces.</b> If it derived the day from the first
    /// read and the duration from the second, the duration is 22:00:00.000 − 22:00:00.001 =
    /// −0.001 s → <b>0</b> on the wire (a zero-delay timer, i.e. the spin loop again). If it derived
    /// the day from the second read, it answers the <b>16th</b> while the client's own clock still
    /// says the 15th. Both are pinned below, so the test fails on the OUTCOME as well as on the read
    /// count — the count assertion alone would be satisfiable by caching the reads somewhere and
    /// staying wrong.</para>
    /// </summary>
    [Fact]
    public void ComputeServerDay_ReadsTheClockExactlyOnce_EvenWhenASecondReadWouldCrossMidnight()
    {
        var provider = new CountingTimeProvider(
            new DateTimeOffset(2026, 7, 15, 21, 59, 59, 999, TimeSpan.Zero),  // 23:59:59.999 local
            new DateTimeOffset(2026, 7, 15, 22, 0, 0, 1, TimeSpan.Zero));     // 00:00:00.001 local, next day

        var facts = CalendarEndpoints.ComputeServerDay(provider);

        Assert.Equal(1, provider.Reads);
        Assert.Equal(new DateOnly(2026, 7, 15), facts.Today); // a second-read day would be the 16th
        Assert.Equal(1, facts.SecondsUntilNextMidnight);      // a second-read duration would be 0
    }
}
