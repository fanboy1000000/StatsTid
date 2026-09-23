using StatsTid.Backend.Api.Contracts;
using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S143 / TASK-14300 — <c>GET /api/calendar/today</c>: <b>the server's day</b>, and the frontend's
/// startup bootstrap read.
///
/// <para>
/// <b>Why this exists, in plain terms.</b> Several screens computed "which month do I open on" from
/// the BROWSER's clock. That month is sent as the period envelope of the skema save and the approval
/// send, so it decides which month a person's hours are filed under: a device in the wrong time zone
/// (or simply with a wrong clock) filed real work into the wrong period, silently. S142 / ADR-041
/// settled the rule — every BUSINESS DATE in StatsTid is the Europe/Copenhagen calendar day, while
/// INSTANTS (created_at, audit timestamps, outbox ordering) stay UTC — and this endpoint is how the
/// frontend obtains that day rather than deriving one of its own. Owner ruling OQ-1a/1b (2026-09-23):
/// the server is the authority, delivered as a bootstrap read. Owner ruling OQ-1d: the app shell is
/// GATED on this read, so it is a startup dependency and is treated as one.
/// </para>
///
/// <para>
/// <b>Why a NEW endpoint class rather than a member of <see cref="TimeEndpoints"/>.</b>
/// <c>TimeEndpoints</c> is the time-ENTRIES family (<c>/api/time-entries</c>, absences, flex) — a
/// per-employee, DB-backed, write-bearing surface. A calendar bootstrap read has no employee, no
/// repository and no write; folding it in would make the entries family mean two unrelated things
/// and would drag a startup dependency into a domain module.
/// </para>
///
/// <para>
/// <b>★ Access: authenticated, NOT anonymous.</b> The frontend restores its session locally and
/// instantly, so a logged-in shell can issue this read before it renders anything — there is no
/// bootstrap window that needs an anonymous surface. Making it anonymous would have introduced the
/// first anonymous PRODUCT endpoint in the codebase: the Backend's only two unauthenticated routes
/// are <c>GET /health</c> (an infrastructure liveness probe, not a product contract) and
/// <c>POST /api/auth/login</c> (which necessarily precedes authentication). That is a
/// security-surface precedent worth far more than the nothing it would buy.
/// </para>
///
/// <para>
/// <b>That two-route census is counted, not assumed</b> (it was an unverified assertion when first
/// written, and review caught it). There is no fallback authorization policy in <c>Program.cs</c>,
/// so a route is anonymous by OMISSION — which makes the census a counting question. As of S143 the
/// Backend registers <b>151</b> routes across <c>Endpoints/*.cs</c> + <c>ApiEndpoints.cs</c> and
/// carries <b>149</b> <c>.RequireAuthorization</c> calls, all 149 naming a policy string (31
/// EmployeeOrAbove, 27 GlobalAdminOnly, 59 HROrAbove, 15 LeaderOrAbove, 16 LocalAdminOrAbove, 1
/// Authenticated — this one). Per-file the two counts are equal everywhere except the two files
/// holding the two routes named above. Re-run that count before repeating the claim; it is a
/// point-in-time measurement, not an invariant anything enforces.
/// </para>
///
/// <para>
/// The policy is the string-named any-authenticated policy
/// <c>"Authenticated"</c> (<c>AuthorizationPolicies.AddStatsTidPolicies</c>): NAMED rather than a
/// bare <c>RequireAuthorization()</c>, because <c>PolicyDenialClassifier</c> classifies a denial as
/// a routine read only when every policy on the endpoint is a KNOWN non-admin NAME — an unnamed
/// policy is deny-by-default routed to the auditable bucket, which would file every expired-token
/// app boot as a security-relevant event.
/// </para>
///
/// <para>
/// <b>No org scope, and why that is correct.</b> The response contains no employee, no organisation
/// and no personal data whatsoever — it is the wall clock. There is nothing for an org-scope
/// validator to bind to, and inventing a scope check here would be decoration that a future reader
/// would mistake for a real containment guarantee.
/// </para>
/// </summary>
public static class CalendarEndpoints
{
    public static WebApplication MapCalendarEndpoints(this WebApplication app)
    {
        // GET /api/calendar/today — the app-shell bootstrap read (ADR-041, owner ruling OQ-1a/1b/1d).
        app.MapGet("/api/calendar/today", (TimeProvider timeProvider) =>
                Results.Ok(ComputeServerDay(timeProvider)))
            .RequireAuthorization("Authenticated")
            .Produces<CalendarTodayResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>
    /// The whole computation, as ONE pure function of the injected clock.
    ///
    /// <para><b>Public on purpose.</b> The endpoint itself can only be exercised through the
    /// Postgres-backed test host (Docker-gated), and the two facts that actually need pinning here —
    /// the 23-hour and the 25-hour Danish day — are pure arithmetic that deserves a test tier that
    /// always runs. Exposing the computation lets the Unit suite pin it with no container, while the
    /// Docker-gated regression class still proves the wire carries it.</para>
    ///
    /// <para><b>ONE captured instant, used for BOTH members.</b> <c>timeProvider.GetUtcNow()</c> is
    /// read exactly once and then frozen (<see cref="CapturedInstantTimeProvider"/>) so that
    /// <c>CopenhagenBusinessDate.Today</c> and the remaining-seconds subtraction describe the SAME
    /// moment. Reading the clock twice would, in the microsecond window around Danish midnight,
    /// return a <c>today</c> derived from before the rollover and a duration measured from after it
    /// — i.e. a zero-or-negative duration on the wire. The freeze makes the atomicity structural
    /// rather than a comment, and costs one tiny allocation per request.</para>
    ///
    /// <para>Pinned by <c>CalendarServerDayTests.ComputeServerDay_ReadsTheClockExactlyOnce_…</c>,
    /// which feeds a COUNTING provider whose second read has crossed midnight and asserts the read
    /// count is exactly one. That test exists because review caught that every other test in the
    /// suite uses a FROZEN provider — under which a two-read implementation is indistinguishable
    /// from this one, so the guarantee was unproven.</para>
    ///
    /// <para><b>The DST-safe duration, and why the obvious implementations are wrong.</b> The value
    /// is derived as: take tomorrow's Copenhagen calendar date → its LOCAL midnight as an
    /// <see cref="DateTimeKind.Unspecified"/> <see cref="DateTime"/> (unspecified is required:
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> rejects a UTC- or
    /// Local-kinded input against a named zone) → convert it to UTC through the real
    /// <c>CopenhagenBusinessDate.Zone</c> → subtract the captured instant. Two shortcuts that look
    /// right both fail:
    /// <list type="bullet">
    ///   <item><description><b>Adding 24 hours</b> to the start of the day is wrong on both
    ///     transition days — the Danish day is 23 hours on the last Sunday of March (02:00 CET jumps
    ///     to 03:00 CEST) and 25 hours on the last Sunday of October (03:00 CEST falls back to
    ///     02:00 CET).</description></item>
    ///   <item><description><b>Reusing TODAY's UTC offset</b> to place tomorrow's midnight is wrong
    ///     across either transition for the same reason: tomorrow's midnight is governed by
    ///     TOMORROW's offset, and on a transition day the two differ by an hour. Converting the
    ///     tomorrow-local value through the zone asks the zone which offset applies THEN, which is
    ///     the only correct question.</description></item>
    /// </list>
    /// Both wrong answers are pinned RED by <c>CalendarServerDayTests</c>.</para>
    ///
    /// <para><b>Midnight is never a skipped or ambiguous local time on any date this endpoint can be
    /// asked about</b> — under the modern EU rule the transitions happen at 02:00 and 03:00 local, so
    /// the conversion above can neither throw on an invalid time nor have to disambiguate a repeated
    /// one. The input here is always "now", so "any date this endpoint can be asked about" means
    /// today and tomorrow, forever forward.</para>
    ///
    /// <para>The qualifier is deliberate and was added after review, because the UNQUALIFIED claim
    /// this sentence used to make ("midnight is never invalid in Copenhagen") is historically false:
    /// the IANA database records Denmark starting daylight-saving time at 00:00 on <b>15 May
    /// 1940</b>, so local midnight genuinely did not exist that day. For every date the product can
    /// serve the guarantee is solid — both zone sources this codebase can resolve (IANA
    /// <c>Europe/Copenhagen</c> and the Windows <c>Romance Standard Time</c>) transition at 01:00Z,
    /// i.e. 02:00 CET / 03:00 CEST, nowhere near midnight. (Independently measured on the Windows
    /// host this was written on: a scan of every local midnight from 1900 to 2099 found zero invalid
    /// and zero ambiguous.) No invalid-midnight branch is added here, deliberately — there is
    /// nothing to handle, and a branch for a case this endpoint cannot reach would be unreachable
    /// code claiming to handle something. Only a future reuse of this derivation for an ARBITRARY
    /// historical date would need <see cref="TimeZoneInfo.IsInvalidTime"/> first.</para>
    ///
    /// <para><b>Rounded UP.</b> The client consumes the value as a timer delay. Truncating would let
    /// the timer fire a fraction of a second BEFORE the rollover, re-read the day it already had, and
    /// re-schedule against a near-zero delay. Ceiling guarantees the refresh lands on or after the
    /// new day. The result is always strictly positive (the captured instant is by construction
    /// inside today) and at most 90000 (the 25-hour day).
    ///
    /// <para>Pinned by <c>ComputeServerDay_OneMillisecondBeforeRollover_RoundsUpToOneSecond</c>
    /// (truncation would answer 0 and spin) and
    /// <c>ComputeServerDay_FractionalRemainder_RoundsUpNotToNearest</c> (round-to-nearest would fire
    /// 0.4 s early). Both were added at review: every earlier instant in the suite fell on a whole
    /// second, so truncation kept the whole suite green and the rounding rule was unproven.</para>
    /// </summary>
    public static CalendarTodayResponse ComputeServerDay(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        // ── The single capture. Everything below is derived from THIS instant and no other. ──
        var capturedUtc = timeProvider.GetUtcNow();
        var today = CopenhagenBusinessDate.Today(new CapturedInstantTimeProvider(capturedUtc));

        // Tomorrow's LOCAL midnight, kind-unspecified, then asked of the real zone what UTC instant
        // that is. The zone answers with TOMORROW's offset, which is what makes both transition days
        // come out right.
        var tomorrowLocalMidnight = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var tomorrowMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrowLocalMidnight, CopenhagenBusinessDate.Zone);

        var remaining = tomorrowMidnightUtc - capturedUtc.UtcDateTime;
        return new CalendarTodayResponse(today, (int)Math.Ceiling(remaining.TotalSeconds));
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> pinned to one already-captured instant. It exists for a single
    /// reason: <c>CopenhagenBusinessDate.Today</c> takes a provider (the project's clock seam) and
    /// reads it itself, so handing it the frozen instant is how "derive the day from the SAME moment
    /// the duration is measured from" is enforced by construction instead of by convention. The
    /// alternative — re-deriving the Copenhagen day inline from the captured instant — would put a
    /// second copy of the product's business-date rule in an endpoint file, which is exactly the
    /// duplication <c>CopenhagenBusinessDate</c> was created (S132 / QUAL-005) to eliminate.
    /// </summary>
    private sealed class CapturedInstantTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _instant;
        public CapturedInstantTimeProvider(DateTimeOffset instant) => _instant = instant;
        public override DateTimeOffset GetUtcNow() => _instant;
    }
}
