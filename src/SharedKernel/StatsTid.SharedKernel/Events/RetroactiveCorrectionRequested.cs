namespace StatsTid.SharedKernel.Events;

public sealed class RetroactiveCorrectionRequested : DomainEventBase
{
    public override string EventType => "RetroactiveCorrectionRequested";

    public required string EmployeeId { get; init; }
    public required DateOnly OriginalPeriodStart { get; init; }
    public required DateOnly OriginalPeriodEnd { get; init; }

    /// <summary>
    /// The agreement the recalculation ran under: that of the period's first EMPLOYED segment, as the
    /// calculation's frozen inputs record it, never the request body (S145 R-11). Empty when no day of the
    /// period was employed. The full per-piece record is reachable through <see cref="ManifestId"/>
    /// (<c>PlanInputs.Pieces</c>).
    /// </summary>
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
    /// The position the recalculation ran under: that of the period's first EMPLOYED segment, as the
    /// calculation's frozen inputs record it, never the request body (S145 R-10, R-11). Null when the dated
    /// record carries no position or no day of the period was employed. The full per-piece record is
    /// reachable through <see cref="ManifestId"/> (<c>PlanInputs.Pieces</c>).
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

    /// <summary>
    /// First day of the next pay period whose settled week this recalculation changed (S145,
    /// Q7 (b)). In plain terms: a week that straddles two months is settled in the later month,
    /// so correcting the earlier month can change a figure the later month already used, and
    /// this names that later period so the follow-up recalculation is visible in the audit trail.
    /// Pay-period dates, never employment dates. Optional and NOT required: null when no such
    /// period applies, and every earlier event keeps deserializing to null.
    /// </summary>
    public DateOnly? NextPeriodToRecalculateStart { get; init; }

    /// <summary>
    /// Last day of the next pay period whose settled week this recalculation changed (S145,
    /// Q7 (b)); set together with <see cref="NextPeriodToRecalculateStart"/>. Pay-period dates,
    /// never employment dates. Optional and NOT required.
    /// </summary>
    public DateOnly? NextPeriodToRecalculateEnd { get; init; }
}
