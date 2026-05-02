namespace StatsTid.SharedKernel.Events;

public sealed class RetroactiveCorrectionRequested : DomainEventBase
{
    public override string EventType => "RetroactiveCorrectionRequested";

    public required string EmployeeId { get; init; }
    public required DateOnly OriginalPeriodStart { get; init; }
    public required DateOnly OriginalPeriodEnd { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string Reason { get; init; }
    public required string CorrectedByActorId { get; init; }
    public required int CorrectionLineCount { get; init; }
    public required decimal TotalDifferenceHours { get; init; }
    public Guid? IdempotencyToken { get; init; }

    /// <summary>
    /// The previous OK version when an OK version transition occurred mid-period.
    /// Null when no version split was needed.
    /// </summary>
    public string? PreviousOkVersion { get; init; }

    /// <summary>
    /// The employee's position code when the correction was made.
    /// Null when no position-based override applied.
    /// </summary>
    public string? Position { get; init; }

    /// <summary>
    /// Manifest id of the planner-driven recalculation that produced these correction lines
    /// (ADR-016 D10). Audit consumers join to <c>SegmentManifestCreated</c> via this id to
    /// recover the actual N-segment plan; the legacy <c>OkVersion</c>/<c>PreviousOkVersion</c>
    /// pair only describes the canonicalized 2-version view and under-reports N&gt;2 cases.
    /// Default <see cref="Guid.Empty"/> for pre-S20 events.
    /// </summary>
    public Guid ManifestId { get; init; } = Guid.Empty;
}
