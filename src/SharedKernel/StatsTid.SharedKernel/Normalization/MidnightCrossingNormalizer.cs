using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Normalization;

/// <summary>
/// ADR-039 (S132 TASK-132-1b-1) — midnight-crossing time-entry normalization for the
/// calculation / rule / compliance INPUT path.
///
/// <para>
/// <strong>Plain-language what &amp; why.</strong> A work shift that crosses midnight
/// (e.g. <c>23:00 → 02:00</c>) is filed as ONE row under a single calendar date. Its
/// post-midnight hours therefore land on the wrong day — and, once the OK24→OK26 agreement
/// boundary at 2026-04-01 matters, under the wrong collective-agreement version. This is the
/// latent "silent cross-OK-version leak" ADR-039 fixes. The fix keeps the raw shift untouched
/// in the immutable event and in every DISPLAY surface (the Skema grid, the Time-entries list,
/// Balance still show ONE crossing row — ADR-039 D1/D5a); it derives a per-calendar-day split
/// ONLY on the path that feeds the calculation / rule engine, BEFORE that input is split into
/// per-OK-version segments, so each half routes to the correct segment.
/// </para>
///
/// <para>
/// <strong>Jargon, first use.</strong> <em>OK-version</em> = which collective-agreement edition
/// applies (OK24 vs OK26); resolved from a date by <see cref="OkVersionResolver"/> (ADR-003:
/// an entry's version is fixed by the wall-clock day it is worked). <em>Segment</em> = a slice
/// of a calculation period lying entirely within one OK-version. <em>Continuity link</em> =
/// <see cref="TimeEntry.SourceStintId"/>, the shared id both halves carry so a rest check can
/// see them as one stint (ADR-039 D4).
/// </para>
///
/// <para>
/// This is a PURE, DETERMINISTIC, VERSIONED function of the input (ADR-039 D2/D6) — no I/O, no
/// clock, no randomness. It is the ONE shared implementation used by every calc/compliance input
/// boundary (PeriodCalculationService before its segment filter; the compliance read that builds
/// the TimeEntry list); a second divergent copy would recreate the QUAL-002 writer/rebuilder
/// split-encoding defect. Because it is deterministic and derived, a replay under the same
/// <see cref="PolicyVersion"/> reproduces the same rows without ever touching the event stream.
/// </para>
/// </summary>
public static class MidnightCrossingNormalizer
{
    /// <summary>
    /// ADR-039 D2 — the normalization POLICY version tag. Bumped only when the split rule or the
    /// D7 <c>Hours</c>-allocation policy changes (e.g. a Phase-B expert sign-off replaces the
    /// proportional default). Kept as a constant so audit/debug surfaces and future replay-policy
    /// gating can record WHICH normalization produced a given derived row. The current policy:
    /// crossing = <c>End ≤ Start</c> (with real post-midnight work), split at midnight, allocate
    /// <c>Hours</c> proportionally to each half's elapsed wall-clock duration.
    /// </summary>
    public const string PolicyVersion = "v1-2026-08-20-proportional-elapsed";

    /// <summary>
    /// Normalizes a list of time entries: each midnight-crossing entry becomes two per-calendar-day
    /// rows; every non-crossing entry passes through UNCHANGED and in its original position.
    /// Order is stable (each source entry's output(s) appear where the source was, pre-half then
    /// post-half). Returns a new list; the input is never mutated.
    /// </summary>
    public static IReadOnlyList<TimeEntry> Normalize(IReadOnlyList<TimeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Fast path: nothing crosses → return the input untouched (no allocation churn, and the
        // dominant case by far — crossing shifts are rare).
        var anyCrossing = false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (IsMidnightCrossing(entries[i]))
            {
                anyCrossing = true;
                break;
            }
        }
        if (!anyCrossing)
            return entries;

        var result = new List<TimeEntry>(entries.Count + 1);
        foreach (var entry in entries)
        {
            if (!IsMidnightCrossing(entry))
            {
                result.Add(entry);
                continue;
            }

            var (pre, post) = Split(entry);
            result.Add(pre);
            result.Add(post);
        }
        return result;
    }

    /// <summary>
    /// ADR-039 D2 — a shift crosses midnight when it carries both a start and an end time and the
    /// end is at-or-before the start on the clock (<c>End ≤ Start</c>) AND there is genuine
    /// post-midnight work (<c>End ≠ 00:00</c>).
    ///
    /// <para>
    /// The <c>End ≠ 00:00</c> guard excludes a shift that ends EXACTLY at midnight (e.g.
    /// <c>17:00 → 00:00</c> = 7h all on day D — no next-day portion), and it makes the transform
    /// IDEMPOTENT: a pre-midnight half is emitted as <c>Start → 00:00</c>, which this predicate no
    /// longer treats as a crossing, so re-running Normalize on already-normalized rows is a no-op.
    /// </para>
    /// </summary>
    public static bool IsMidnightCrossing(TimeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.StartTime is not { } start || entry.EndTime is not { } end)
            return false;
        return end <= start && end != TimeOnly.MinValue;
    }

    /// <summary>
    /// Splits ONE crossing entry into its pre-midnight (day D) and post-midnight (day D+1) halves.
    ///
    /// <list type="bullet">
    /// <item><description><strong>D2 conservation.</strong> Each half's clock window is
    /// <c>[Start, 24:00)</c> and <c>[00:00, End)</c>; their elapsed durations sum EXACTLY to the
    /// original crossing duration. The post-half's <c>Hours</c> is computed as the exact remainder
    /// of the pre-half's, so <c>preHours + postHours == original.Hours</c> to the last decimal —
    /// nothing dropped, nothing double-counted.</description></item>
    /// <item><description><strong>D7 Hours≠elapsed allocation.</strong> <c>Hours</c> is supplied
    /// independently of Start/End (breaks, rounding, manual entry) so we do NOT assume Hours == the
    /// wall-clock span. The default policy allocates Hours PROPORTIONALLY to each half's elapsed
    /// duration (ticks), which reduces to the exact clock split when Hours does equal the span.
    /// </description></item>
    /// <item><description><strong>D3 per-half OK-version.</strong> Each half's OkVersion is
    /// re-resolved from ITS OWN Date via <see cref="OkVersionResolver"/>, so the D+1 half is OK26
    /// at the 2026-04-01 boundary even though the source was filed OK24 under day D.</description></item>
    /// <item><description><strong>D4 continuity link.</strong> Both halves inherit the source's
    /// <see cref="TimeEntry.SourceStintId"/> unchanged — the shared id that lets a rest check
    /// rejoin them into one continuous stint.</description></item>
    /// </list>
    ///
    /// <para>
    /// <strong>Clock-field encoding of the split (the contract downstream consumers rely on).</strong>
    /// The pre-half is <c>[Start → 00:00]</c> and the post-half is <c>[00:00 → End]</c>, where the
    /// shared <c>00:00</c> is the midnight boundary (TimeOnly cannot hold "24:00", so the end of
    /// day D is written as <c>00:00</c>). Consumers must NOT read a half's inner midnight boundary
    /// as a same-day instant: per-day hours consumers use Date + Hours; supplement overlap
    /// (<c>SupplementRule.CalculateOverlapHours</c>) already treats <c>end == 00:00</c> as 24:00 via
    /// its own crossing branch, so evening/night supplements stay conserved and correctly per-day;
    /// the continuous-stint / rest-check consumer reconstructs the absolute interval as
    /// <c>[firstHalf.Date + firstHalf.StartTime, lastHalf.Date + lastHalf.EndTime]</c>, keyed by the
    /// shared <see cref="TimeEntry.SourceStintId"/> (TASK-1b-3).
    /// </para>
    /// </summary>
    public static (TimeEntry Pre, TimeEntry Post) Split(TimeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IsMidnightCrossing(entry))
            throw new ArgumentException(
                "MidnightCrossingNormalizer.Split called on a non-crossing entry. " +
                "Guard the call with IsMidnightCrossing first.", nameof(entry));

        var start = entry.StartTime!.Value;
        var end = entry.EndTime!.Value;
        var midnight = TimeOnly.MinValue; // 00:00

        var dayD = entry.Date;
        var dayD1 = entry.Date.AddDays(1);

        // Elapsed wall-clock spans of each half. TimeOnly.ToTimeSpan() is the time-of-day; the
        // pre-half runs from Start to end-of-day (24:00), the post-half from midnight to End.
        // Both spans are strictly positive here (start < 24:00 always; end > 00:00 by the crossing
        // guard), so the denominator is never zero.
        var preSpan = TimeSpan.FromDays(1) - start.ToTimeSpan();
        var postSpan = end.ToTimeSpan();

        // D7 proportional allocation on integer ticks (exact decimal arithmetic, no floating point).
        // Post is the exact remainder → D2 conservation holds bit-for-bit.
        long preTicks = preSpan.Ticks;
        long totalTicks = preTicks + postSpan.Ticks;
        decimal preHours = entry.Hours * preTicks / totalTicks;
        decimal postHours = entry.Hours - preHours;

        // D4 continuity link — the SHARED id both halves carry so a rest check rejoins them into
        // ONE continuous stint. Prefer the source's own id (populated from the immutable event id
        // at a read boundary, e.g. ComplianceEndpoints). When the caller did not set one (some
        // caller-supplied TimeEntry lists — e.g. the payroll request contract), we DERIVE a
        // deterministic shared id from the source's stable fields. This MUST be deterministic (not
        // Guid.NewGuid) so the transform stays pure and replay-reproducible AND so both halves
        // always rejoin — otherwise the rest checks would see two singletons and read a false
        // 0-hour gap at midnight.
        var stintId = entry.SourceStintId ?? DeriveStintId(entry);

        var preHalf = CloneWith(entry, date: dayD, startTime: start, endTime: midnight, hours: preHours, stintId: stintId);
        var postHalf = CloneWith(entry, date: dayD1, startTime: midnight, endTime: end, hours: postHours, stintId: stintId);
        return (preHalf, postHalf);
    }

    /// <summary>
    /// Derives a DETERMINISTIC continuity-link id from a source entry's stable identifying fields,
    /// used only when the source carries no <see cref="TimeEntry.SourceStintId"/>. Same input →
    /// same id on every process and every replay (a stable SHA-256 over the field tuple, first 16
    /// bytes folded into a Guid — this is an identity fingerprint, NOT a security primitive; SHA-256
    /// simply avoids the broken-hash analyzer rule). Both halves of the same crossing therefore
    /// share one id and always rejoin.
    /// </summary>
    private static Guid DeriveStintId(TimeEntry entry)
    {
        var key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"midnight-stint|{entry.EmployeeId}|{entry.Date:O}|{entry.StartTime:O}|{entry.EndTime:O}|{entry.Hours}");
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Produces a derived half from the source entry: overrides Date / StartTime / EndTime / Hours,
    /// re-resolves OkVersion from the new Date (D3), stamps the shared continuity link (D4) and
    /// copies every other domain field verbatim.
    /// </summary>
    private static TimeEntry CloneWith(
        TimeEntry source, DateOnly date, TimeOnly startTime, TimeOnly endTime, decimal hours, Guid stintId) =>
        new()
        {
            EmployeeId = source.EmployeeId,
            Date = date,
            Hours = hours,
            StartTime = startTime,
            EndTime = endTime,
            TaskId = source.TaskId,
            ActivityType = source.ActivityType,
            AgreementCode = source.AgreementCode,
            OkVersion = OkVersionResolver.ResolveVersion(date), // D3: version by each half's own date
            RegisteredAt = source.RegisteredAt,
            VoluntaryUnsocialHours = source.VoluntaryUnsocialHours,
            SourceStintId = stintId, // D4: shared continuity link (source id, or derived when absent)
        };
}
