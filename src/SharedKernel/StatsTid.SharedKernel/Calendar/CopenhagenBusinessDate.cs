namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// The single source of truth for "today, as a Copenhagen calendar date".
///
/// Every BUSINESS DATE in StatsTid keys on the DANISH business day, not on UTC — employment
/// start/end dates, the §21 stk.2 vacation-transfer deadline (31 Dec of the
/// ferieafholdelsesperiode) and the settlement/leaver boundaries (ADR-033 D3). "Which calendar
/// day is it in Copenhagen?" must therefore be answered against the real
/// <c>Europe/Copenhagen</c> zone, which is UTC+1 (CET) in winter and UTC+2 (CEST) under
/// daylight-saving time from late March to late October. A midnight-adjacent instant lands on a
/// different calendar day depending on that offset, so a hardcoded +01:00 assumption mis-decides
/// the day for half the year (the QUAL-005 bug: the §21 guard's fixed <c>+01:00</c> fallback was
/// wrong every summer). INSTANTS — <c>created_at</c>, audit timestamps, outbox ordering, JWT
/// expiry — stay UTC and must never be routed through this class.
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
///
/// <para><b>S142 / owner ruling OQ-11 — there is no degraded mode.</b> This class used to fall
/// back to <see cref="TimeZoneInfo.Utc"/> when neither zone id resolved, documented as a
/// "never crash" choice. That was defensible only while UTC WAS the business calendar: falling
/// back then produced the same answer as everything else. Once business dates moved to the
/// Copenhagen calendar day (S142), a UTC fallback stopped degrading and started silently
/// reinstating the exact defect the sprint removed — a Danish HR user working at 00:30 local
/// would have their change recorded as effective YESTERDAY, with no signal anywhere. Domain
/// correctness is an inviolable invariant and availability is not on the trade-off list, so
/// resolution now THROWS: see <see cref="EnsureZoneIsCopenhagen"/> and
/// <see cref="EnsureHostZoneResolves"/>. The trade accepted is that a host without timezone data
/// refuses to start rather than starting and recording wrong dates.</para>
/// </summary>
public static class CopenhagenBusinessDate
{
    /// <summary>The canonical IANA id — Linux/macOS, and .NET's ICU-backed Windows runtime.</summary>
    public const string IanaZoneId = "Europe/Copenhagen";

    /// <summary>The Windows registry id naming the same zone (CET/CEST).</summary>
    public const string WindowsZoneId = "Romance Standard Time";

    /// <summary>The offset Copenhagen must report outside daylight-saving time (CET).</summary>
    private static readonly TimeSpan ExpectedWinterOffset = TimeSpan.FromHours(1);

    /// <summary>The offset Copenhagen must report under daylight-saving time (CEST).</summary>
    private static readonly TimeSpan ExpectedSummerOffset = TimeSpan.FromHours(2);

    // The two probe instants. They are deliberately:
    //   • months away from the DST transitions (last Sunday of March / last Sunday of October),
    //     so no clock-change edge case can make a correct zone look wrong; and
    //   • in the PAST, where the tz database is immutable. The probe asks "does this zone carry
    //     real Danish DST machinery?", NOT "is this year's law what we assumed" — so if the EU
    //     ever abolishes seasonal clock changes, a correct future zone still passes (its 2024
    //     history is unchanged) while `Today` transparently follows the new rules.
    private static readonly DateTimeOffset WinterProbeInstant = new(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SummerProbeInstant = new(2024, 7, 15, 12, 0, 0, TimeSpan.Zero);

    // Lazy, not a static field initializer: a throwing field initializer poisons the type with a
    // TypeInitializationException that BURIES the actionable message one InnerException deep.
    // Lazy<T> in its default ExecutionAndPublication mode caches the exception and rethrows the
    // real one, unwrapped, every time.
    private static readonly Lazy<TimeZoneInfo> LazyZone = new(ResolveCopenhagenZone);

    /// <summary>
    /// The resolved <c>Europe/Copenhagen</c> time zone (DST-aware). Resolved once, on first use:
    /// the IANA id first (canonical), then the Windows registry id. A host where neither resolves
    /// — or where the id resolves to something that does NOT observe Danish DST — throws
    /// <see cref="InvalidTimeZoneException"/> with an actionable message. There is no UTC
    /// fallback (S142 / OQ-11): see the class remarks.
    /// </summary>
    /// <exception cref="InvalidTimeZoneException">
    /// The host has no usable Europe/Copenhagen zone. Normally surfaced at boot by
    /// <see cref="EnsureHostZoneResolves"/> rather than mid-request.
    /// </exception>
    public static TimeZoneInfo Zone => LazyZone.Value;

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

    /// <summary>
    /// The STARTUP GATE. Forces zone resolution + validation now, so a host that cannot answer
    /// "which day is it in Copenhagen?" fails at boot with a clear message instead of serving
    /// traffic that silently records Danish business dates a day early.
    ///
    /// Call this as the first statement of every host's composition root. It is idempotent and
    /// costs one cached lookup.
    /// </summary>
    /// <exception cref="InvalidTimeZoneException">The host has no usable Europe/Copenhagen zone.</exception>
    public static void EnsureHostZoneResolves() => _ = Zone;

    /// <summary>
    /// The PROBE, exposed so it can be exercised against an injected zone (that is the only way
    /// to prove it actually rejects a wrong one — a guard that cannot fail is the same defect as
    /// a test that cannot fail).
    ///
    /// It checks BOTH offsets, and that is the whole point: a winter-only check is PASSED by a
    /// zone hardcoded to +01:00, which is precisely the QUAL-005 bug this class was written to
    /// prevent. Requiring +01:00 in winter AND +02:00 in summer rejects
    /// <see cref="TimeZoneInfo.Utc"/> (0/0), a fixed +01:00 zone (1/1) and a fixed +02:00 zone
    /// (2/2) alike; only a zone carrying real Danish DST rules passes.
    /// </summary>
    /// <param name="zone">The candidate zone.</param>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    /// <exception cref="InvalidTimeZoneException">
    /// The zone does not observe Danish CET/CEST. The message names the zone, both observed
    /// offsets, and the likely host fix.
    /// </exception>
    public static void EnsureZoneIsCopenhagen(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var winter = zone.GetUtcOffset(WinterProbeInstant);
        var summer = zone.GetUtcOffset(SummerProbeInstant);

        if (winter == ExpectedWinterOffset && summer == ExpectedSummerOffset)
        {
            return;
        }

        throw new InvalidTimeZoneException(
            $"StatsTid resolved a time zone for '{IanaZoneId}' (id '{zone.Id}') but it does NOT " +
            $"observe Danish daylight-saving time: it reported UTC{Describe(winter)} on " +
            $"{WinterProbeInstant:yyyy-MM-dd} (expected UTC{Describe(ExpectedWinterOffset)}, CET) and " +
            $"UTC{Describe(summer)} on {SummerProbeInstant:yyyy-MM-dd} " +
            $"(expected UTC{Describe(ExpectedSummerOffset)}, CEST). " +
            "A zone reporting the same offset in both seasons is a fixed-offset or UTC placeholder, " +
            "not the real Europe/Copenhagen zone. " +
            HostFixAdvice +
            BusinessDateRationale);
    }

    private static TimeZoneInfo ResolveCopenhagenZone()
    {
        foreach (var id in new[] { IanaZoneId, WindowsZoneId })
        {
            TimeZoneInfo? candidate = null;
            try
            {
                candidate = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }

            if (candidate is not null)
            {
                // Validate HERE, not only at the host's startup gate, so the guarantee holds for
                // every process that links SharedKernel — including hosts whose composition root
                // has not (yet) been given the gate.
                EnsureZoneIsCopenhagen(candidate);
                return candidate;
            }
        }

        throw new InvalidTimeZoneException(
            $"StatsTid requires the '{IanaZoneId}' time zone and this host cannot resolve it. " +
            $"Tried the IANA id '{IanaZoneId}' and the Windows id '{WindowsZoneId}'; neither is " +
            "present. The host is almost certainly missing its timezone database (tzdata) or its " +
            "ICU globalization data — typical of a slim/distroless container image, or of a build " +
            "with InvariantGlobalization enabled. " +
            HostFixAdvice +
            BusinessDateRationale);
    }

    private const string HostFixAdvice =
        "Fix: install tzdata in the image (Debian/Ubuntu: `apt-get install -y tzdata`; " +
        "Alpine: `apk add --no-cache tzdata`), keep ICU present (do not use the 'invariant' " +
        "runtime-deps image variants), and leave <InvariantGlobalization> false (the default). ";

    private const string BusinessDateRationale =
        "StatsTid deliberately refuses to start rather than degrade (owner ruling OQ-11, S142): " +
        "every Danish business date — employment start/end, the §21 stk.2 transfer deadline, " +
        "settlement and leaver boundaries — is the Copenhagen calendar day, and a UTC fallback " +
        "would silently record those dates one day early for anyone working after local midnight.";

    private static string Describe(TimeSpan offset) =>
        (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm");
}
