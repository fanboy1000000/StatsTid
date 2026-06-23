namespace StatsTid.SharedKernel.Events;

public sealed class ReportingLineBulkImported : DomainEventBase
{
    public override string EventType => "ReportingLineBulkImported";

    public required Guid BatchId { get; init; }
    public required string OrganisationId { get; init; }
    public required int LineCount { get; init; }
    public required string Source { get; init; }
}
