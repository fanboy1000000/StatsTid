namespace StatsTid.Backend.Api.Contracts;

/// <summary>
/// S143 / TASK-14300 — the response of <c>GET /api/calendar/today</c>: <b>the server's day</b>.
///
/// <para>
/// <b>What this is for, in plain terms.</b> Four screens used to open on "the current month" as
/// computed from the BROWSER's clock. That month is not cosmetic — it is sent as the period
/// envelope of the skema save and the approval send, so it decides which month a person's time is
/// filed under. A device whose clock is wrong, or simply set to a non-Danish time zone, therefore
/// filed real hours into the wrong period. S142 (ADR-041) settled that every BUSINESS DATE in
/// StatsTid is the <b>Europe/Copenhagen</b> calendar day; this endpoint is how the frontend learns
/// that day instead of guessing it. The owner ruling (OQ-1a/1b, 2026-09-23) is that the server is
/// the authority and the frontend reads it once at startup; OQ-1d gates the app shell on the read,
/// so a failure here renders no page at all rather than a page dated from the device.
/// </para>
/// </summary>
/// <param name="Today">
/// The Copenhagen calendar day, as <c>yyyy-MM-dd</c> (<c>DateOnly</c> serialises as OpenAPI
/// <c>format: date</c>, so no bespoke string member is invented here). Derived through
/// <c>CopenhagenBusinessDate.Today</c> off the injected <c>TimeProvider</c> — the same single
/// derivation every business-date writer in the product uses, which is the point: a client that
/// opens on this month and a writer that validates against this day can never disagree.
///
/// <para>Denmark is UTC+1 in winter (CET) and UTC+2 in summer (CEST), so between Danish midnight
/// and UTC midnight the UTC calendar still reads YESTERDAY. This member is deliberately the DANISH
/// day, never the UTC one.</para>
/// </param>
/// <param name="SecondsUntilNextMidnight">
/// How long, in whole seconds, until the Copenhagen calendar day rolls over — <b>a duration, not an
/// absolute timestamp</b>, and that choice is the whole design.
///
/// <para><b>Why a duration.</b> The client schedules a refresh at the day rollover. Given an
/// absolute <c>nextMidnightUtc</c> it would have to compute <c>nextMidnight - Date.now()</c> to get
/// the delay — reading the device clock again, and so reintroducing exactly the dependency this
/// endpoint exists to remove. A duration is consumed as <c>setTimeout(refresh, seconds * 1000)</c>
/// with no clock read at all.</para>
///
/// <para><b>Why it is not simply 86400 at midnight.</b> On the last Sunday of March the Danish day
/// is <b>23 hours</b> long (02:00→03:00 local) and on the last Sunday of October it is <b>25 hours</b>
/// (03:00→02:00 local). The value is derived from the real zone, so it is 82800 and 90000
/// respectively at the start of those two days — see <c>CalendarEndpoints.ComputeServerDay</c> for
/// the derivation, and for why the two obvious implementations (add 24 hours; reuse today's UTC
/// offset) are both wrong.</para>
///
/// <para>Always strictly positive, and at most 90000 (the 25-hour day). Rounded UP to the next whole
/// second: a client timer must never fire a fraction of a second BEFORE the rollover, which would
/// re-read the old day and then immediately re-schedule against a near-zero delay.</para>
/// </param>
public sealed record CalendarTodayResponse(
    DateOnly Today,
    int SecondsUntilNextMidnight);
