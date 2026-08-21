namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// The single source of truth for "today, as a Copenhagen calendar date".
///
/// Several statutory rules in StatsTid key on the DANISH business day, not on UTC — most
/// importantly the §21 stk.2 vacation-transfer deadline (31 Dec of the ferieafholdelsesperiode)
/// and the settlement/leaver boundaries (ADR-033 D3). "Which calendar day is it in Copenhagen?"
/// must therefore be answered against the real <c>Europe/Copenhagen</c> zone, which is UTC+1
/// (CET) in winter and UTC+2 (CEST) under daylight-saving time from late March to late October.
/// A midnight-adjacent instant lands on a different calendar day depending on that offset, so a
/// hardcoded +01:00 assumption mis-decides the day for half the year (the QUAL-005 bug: the §21
/// guard's fixed <c>+01:00</c> fallback was wrong every summer).
///
/// This class replaces six copy-pasted "today in Copenhagen" blocks (S131 QUAL-005 / S132
/// TASK-132-3b) with one DST-correct implementation. Like <see cref="OkVersionResolver"/>, it is
/// a dependency-free SharedKernel citizen so both the write/endpoint boundary and the
/// Infrastructure settlement services can reach it without violating integration isolation
/// (PAT-005).
///
/// The clock is injected as a <see cref="TimeProvider"/> (the project's established test seam —
/// DI default <c>TimeProvider.System</c>, overridden with a fixed provider in tests; PAT-008), so
/// the Copenhagen date is deterministically testable — including across DST boundaries.
/// </summary>
public static class CopenhagenBusinessDate
{
    /// <summary>
    /// The resolved <c>Europe/Copenhagen</c> time zone (DST-aware). Resolved once at type
    /// initialization: the IANA id first (canonical; Linux/macOS + .NET's ICU-backed Windows
    /// runtime), the Windows registry id as a fallback, and UTC as a never-crash terminal for a
    /// stripped host with neither a tz database nor ICU (degraded but deterministic — never a
    /// hardcoded seasonal offset).
    /// </summary>
    public static readonly TimeZoneInfo Zone = ResolveCopenhagenZone();

    /// <summary>
    /// The current calendar date in Copenhagen, derived from the injected <paramref name="timeProvider"/>.
    /// DST-correct: the UTC instant is converted through the real <see cref="Zone"/>, so a
    /// midnight-adjacent instant is attributed to the correct Copenhagen day in both CET and CEST.
    /// </summary>
    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var copenhagenNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Zone);
        return DateOnly.FromDateTime(copenhagenNow.DateTime);
    }

    private static TimeZoneInfo ResolveCopenhagenZone()
    {
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
