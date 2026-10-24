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

    /// <summary>
    /// The flex balance the month opened at (S146 flex ledger). In plain terms: the hours
    /// of flex the employee carried into this month. Optional and NOT required: null on the
    /// raw exports and on every pre-S146 event, which keep deserializing to null.
    /// </summary>
    public decimal? OpeningFlexBalance { get; init; }

    /// <summary>
    /// The flex balance the month closed at (S146 flex ledger); the next month's opening.
    /// Optional and NOT required: null on raw exports and pre-S146 events.
    /// </summary>
    public decimal? ClosingFlexBalance { get; init; }

    /// <summary>
    /// Where the opening balance came from: <c>LEDGER_START</c>, <c>CHAINED</c> or
    /// <c>REHIRE</c> (S146). Optional and NOT required: null on raw exports and pre-S146 events.
    /// </summary>
    public string? FlexProvenance { get; init; }

    /// <summary>
    /// The manifest of the calculation whose figures this export stores (ADR-016 D10). Set on
    /// <c>/calculate-and-export</c> only; null on the raw <c>/export</c> + <c>/export-period</c> (their lines
    /// carry what their caller sent) and on every pre-fix event, which keep deserializing to null. A later
    /// correction's manifest is on its own <c>RetroactiveCorrectionRequested</c>. Optional and NOT required.
    /// </summary>
    public Guid? ManifestId { get; init; }
}
