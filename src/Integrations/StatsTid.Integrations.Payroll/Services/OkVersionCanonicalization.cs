using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Pure helper that canonicalises the OK versions emitted on
/// <c>RetroactiveCorrectionRequested</c> so the audit event mirrors what
/// <see cref="PeriodCalculationService"/> actually computed (ADR-003 /
/// TASK-1904 / TASK-1801). Lives outside <see cref="RetroactiveCorrectionService"/>
/// because that service depends on <c>IEventStore</c> and a real
/// <see cref="PeriodCalculationService"/>; pulling the resolution into a
/// pure function lets unit tests pin the per-branch behaviour without
/// standing up either dependency. Codex first-pass S19 review flagged that
/// the previous regression test only pinned <see cref="OkVersionResolver"/>'s
/// resolver invariant, not the service-level choice between the resolver
/// output and <c>profile.OkVersion</c> — moving the choice here closes the
/// gap.
/// </summary>
public static class OkVersionCanonicalization
{
    /// <summary>
    /// Resolves the canonical (current, previous) OK versions and reports
    /// whether the caller-supplied values drifted from the date-resolved
    /// truth. Branches:
    ///
    ///   * Split path  — both <paramref name="okTransitionDate"/> and
    ///     <paramref name="callerPreviousOkVersion"/> are present. Current OK
    ///     version is resolved from the transition date itself (segment 2);
    ///     previous from <c>transitionDate.AddDays(-1)</c> (segment 1).
    ///   * Single-version path — neither side of the split is present.
    ///     Current OK version is resolved from <paramref name="periodStart"/>;
    ///     previous OK version is null. PeriodCalculationService server-
    ///     resolves from periodStart (TASK-1801) and ignores the caller's
    ///     <c>profile.OkVersion</c>; the audit event must mirror that, NOT
    ///     the (potentially stale) caller value.
    ///
    /// In both branches the helper compares the caller-supplied value against
    /// the date-resolved truth and surfaces drift via the
    /// <see cref="OkVersionCanonical.CurrentDrifted"/> /
    /// <see cref="OkVersionCanonical.PreviousDrifted"/> flags so the service
    /// can log a warning without re-doing the comparison.
    ///
    /// The mixed-arguments case (transition date present but
    /// <paramref name="callerPreviousOkVersion"/> null, or vice versa) is
    /// rejected upstream by the service before it gets here; this helper
    /// treats it as the single-version path defensively.
    /// </summary>
    public static OkVersionCanonical Resolve(
        string callerCurrentOkVersion,
        DateOnly periodStart,
        DateOnly? okTransitionDate,
        string? callerPreviousOkVersion)
    {
        if (okTransitionDate.HasValue && callerPreviousOkVersion is not null)
        {
            var resolvedPrevious = OkVersionResolver.ResolveVersion(okTransitionDate.Value.AddDays(-1));
            var resolvedCurrent = OkVersionResolver.ResolveVersion(okTransitionDate.Value);

            return new OkVersionCanonical(
                CurrentOkVersion: resolvedCurrent,
                PreviousOkVersion: resolvedPrevious,
                CurrentDrifted: !string.Equals(callerCurrentOkVersion, resolvedCurrent, StringComparison.Ordinal),
                PreviousDrifted: !string.Equals(callerPreviousOkVersion, resolvedPrevious, StringComparison.Ordinal));
        }

        var resolvedFromPeriodStart = OkVersionResolver.ResolveVersion(periodStart);

        return new OkVersionCanonical(
            CurrentOkVersion: resolvedFromPeriodStart,
            PreviousOkVersion: null,
            CurrentDrifted: !string.Equals(callerCurrentOkVersion, resolvedFromPeriodStart, StringComparison.Ordinal),
            PreviousDrifted: false);
    }
}

/// <summary>
/// Result of <see cref="OkVersionCanonicalization.Resolve"/>. The two
/// drift flags are advisory — they indicate that the caller-supplied value
/// disagreed with the date-resolved truth and the service should log a
/// warning, but the canonical (resolved) values are always the source of
/// truth for the audit event.
/// </summary>
public sealed record OkVersionCanonical(
    string CurrentOkVersion,
    string? PreviousOkVersion,
    bool CurrentDrifted,
    bool PreviousDrifted);
