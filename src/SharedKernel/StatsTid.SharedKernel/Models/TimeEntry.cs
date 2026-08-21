namespace StatsTid.SharedKernel.Models;

public sealed class TimeEntry
{
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Hours { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }
    public string? TaskId { get; init; }
    public string? ActivityType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public DateTime RegisteredAt { get; init; } = DateTime.UtcNow;
    public bool VoluntaryUnsocialHours { get; init; }

    /// <summary>
    /// ADR-039 D4 — continuity link ("source-stint identity"). When a midnight-crossing
    /// shift is split into two per-calendar-day rows on the calculation/rule INPUT path
    /// (see <see cref="Normalization.MidnightCrossingNormalizer"/>), BOTH halves carry the
    /// SAME value here so a downstream rest check can recognise them as ONE continuous work
    /// stint (rather than two separate work periods with a false 0-hour gap at midnight).
    ///
    /// <para>
    /// It is the identity of the SOURCE entry — populated from the immutable
    /// <c>TimeEntryRegistered</c> event id at the read boundary that builds the consumed
    /// <see cref="TimeEntry"/> list (e.g. <c>ComplianceEndpoints</c> maps
    /// <c>TimeEntryProjectionRow.EventId</c> → this field). A non-crossing entry keeps its
    /// own source id; for a crossing whose source id is null, the normalizer DERIVES a
    /// deterministic shared id (a pure SHA-256 fingerprint of the source's stable fields, never a
    /// random <c>Guid</c>) and stamps it on both halves, so they always rejoin and the transform
    /// stays pure and replay-deterministic. Null when the read boundary did not populate it (the
    /// hours-summing and supplement paths do not need it; only the rest-check stint reconstruction
    /// does).
    /// </para>
    /// </summary>
    public Guid? SourceStintId { get; init; }
}
