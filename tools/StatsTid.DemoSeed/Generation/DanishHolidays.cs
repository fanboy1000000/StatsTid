using System.Collections.Concurrent;
namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>
/// S127 / TASK-12701a — the Danish public holidays for a given year, COMPUTED (Easter-derived).
///
/// <para><b>Why the generator needs this.</b> The submit-time coverage gate
/// (<c>ApprovalEndpoints.cs:1404-1413</c>) builds its expected-workday list as
/// <i>weekdays minus rows in <c>danish_public_holidays</c></i>, and rejects the send if any expected
/// workday carries neither a time entry nor an absence. So a coverage-complete generated month must
/// register work on exactly the same day set the gate expects.</para>
///
/// <para><b>Why computed rather than copied.</b> The <c>danish_public_holidays</c> seed
/// (<c>init.sql:374-416</c>) only covers 2024–2026; a hard-coded copy would silently stop matching
/// the day the table gains a year. The Easter rule reproduces every seeded row exactly for all three
/// years — pinned by <c>DanishHolidaysTests</c>, which asserts this calculator against the literal
/// init.sql rows — and keeps producing the right answer for later years.</para>
///
/// <para><b>Drift direction, stated.</b> Only a day this calculator marks as a holiday while the DB
/// does NOT would break a generated month (that weekday becomes expected-but-unregistered). The
/// converse — the DB holding a holiday this calculator misses — is harmless: the day is simply
/// registered and stays balanced, and an unexpected registration never fails coverage. The pinned
/// test is what keeps the harmful direction closed.</para>
///
/// <para>Pure arithmetic: no wall-clock, no RNG, no I/O — safe inside the deterministic generator.</para>
/// </summary>
internal static class DanishHolidays
{
    /// <summary>
    /// Year → that year's holidays. <b>Concurrent by necessity.</b>
    ///
    /// <para>This was a plain <see cref="Dictionary{TKey,TValue}"/> from S127 until S142's close,
    /// when CI failed with <i>"Operations that change non-concurrent collections must have exclusive
    /// access"</i> — its first appearance in fifteen runs. Nothing about the sprint touched this
    /// file; xunit simply happened to schedule two test classes that both reach the generator at the
    /// same moment, and an unsynchronised write into a shared static dictionary can corrupt its
    /// internal buckets rather than merely losing an entry.</para>
    ///
    /// <para>The computation is pure and idempotent — the same year always yields the same set — so
    /// two threads racing to populate one key is harmless; only the <i>write</i> needed protecting.
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> is the smallest fix that makes that true, and
    /// `GetOrAdd` keeps the read path allocation-free on the hit that matters.</para>
    ///
    /// <para><b>A latent race is not a flake.</b> It was tempting to re-run CI and watch it go green,
    /// which it very likely would have — leaving the next sprint to rediscover this at a worse
    /// moment.</para>
    /// </summary>
    private static readonly ConcurrentDictionary<int, HashSet<DateOnly>> Cache = new();

    /// <summary>Every Danish public holiday in <paramref name="year"/> (weekend-falling ones
    /// included — the gate filters weekends separately, so membership here is the only thing that
    /// matters).</summary>
    internal static HashSet<DateOnly> For(int year) => Cache.GetOrAdd(year, Build);

    private static HashSet<DateOnly> Build(int year)
    {
        var easter = EasterSunday(year);
        return new HashSet<DateOnly>
        {
            new(year, 1, 1),            // Nytaarsdag
            easter.AddDays(-3),         // Skaertorsdag
            easter.AddDays(-2),         // Langfredag
            easter,                     // Paaskedag
            easter.AddDays(1),          // 2. Paaskedag
            easter.AddDays(39),         // Kristi Himmelfartsdag
            easter.AddDays(49),         // Pinsedag
            easter.AddDays(50),         // 2. Pinsedag
            new(year, 6, 5),            // Grundlovsdag
            new(year, 12, 25),          // Juledag
            new(year, 12, 26),          // 2. Juledag
        };
    }

    /// <summary>True when <paramref name="date"/> is neither a weekend nor a public holiday — i.e.
    /// exactly the gate's "expected workday" predicate.</summary>
    internal static bool IsExpectedWorkday(DateOnly date)
        => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
           && !For(date.Year).Contains(date);

    /// <summary>Gregorian Easter Sunday (the anonymous Gauss / Meeus-Butcher algorithm).</summary>
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
