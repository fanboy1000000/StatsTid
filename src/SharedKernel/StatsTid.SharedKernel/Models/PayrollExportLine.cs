namespace StatsTid.SharedKernel.Models;

public sealed class PayrollExportLine
{
    public required string EmployeeId { get; init; }
    public required string WageType { get; init; }
    public required decimal Hours { get; init; }
    public required decimal Amount { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required string OkVersion { get; init; }
    public string? SourceRuleId { get; init; }
    public string? SourceTimeType { get; init; }

    // ADR-016 D10 (manifest linkage, amended 2026-04-29): the manifest id flows file-side
    // (SLS export content) and in-memory (this property) end-to-end. Additive, init-only,
    // default Guid.Empty so existing producers/consumers that pre-date the manifest plumbing
    // remain valid; new producers populate it. NO payroll_export_lines DB column — D10
    // amendment scopes manifest_id to in-memory + audit_log + SLS file content only.
    public Guid ManifestId { get; init; } = Guid.Empty;
}
