namespace StatsTid.SharedKernel.Events;

/// <summary>
/// The per-(employee, year, month) payroll-export fact — emitted once when a
/// month is handed off to payroll and the <c>payroll_export_records</c> lock row
/// commits (ADR-034 / SPRINT-90). Rides <c>employee-{EmployeeId}</c>.
///
/// <para>
/// RESHAPED in S90 (TASK-9001) from the vestigial never-emitted S22-era trace
/// event. The type NAME is preserved (it is the EventSerializer key), but the
/// payload now carries the export-lock identity. Caller-census confirmed ZERO
/// historical emit sites for the old shape, so the reshape is replay-safe (no
/// stream contains a <c>PayrollExportGenerated</c> event of the old shape to
/// deserialize). Emission lands in TASK-9002; this task defines the contract +
/// the audit mapper only.
/// </para>
/// </summary>
public sealed class PayrollExportGenerated : DomainEventBase
{
    public override string EventType => "PayrollExportGenerated";

    public required string EmployeeId { get; init; }
    public required int Year { get; init; }
    public required int Month { get; init; }
    public required Guid ExportId { get; init; }
    public required string ContentHash { get; init; }
    public required DateTimeOffset ExportedAt { get; init; }

    /// <summary>
    /// The approval period this export covers. Null for the raw
    /// <c>/export</c> + <c>/export-period</c> endpoints, which bypass approval.
    /// </summary>
    public Guid? PeriodId { get; init; }
}
