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
}
