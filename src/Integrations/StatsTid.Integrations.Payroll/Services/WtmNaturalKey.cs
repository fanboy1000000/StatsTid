namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Wage-type-mapping natural-key triple captured at planner time and read back at
/// export time (ADR-020 D1 component 2 — replay-stable input).
///
/// <para>
/// Since S145 (TASK-14512, A4) the hydrator is the dated overload of the enrollment seam: it
/// receives each EMPLOYED segment's start and builds this record from the employee's dated record
/// in force on that day (OK version, agreement code, position), never from the request body, so
/// the export uses the same record the rule engine evaluated with (ADR-020 D1 amendment; it
/// replaced the D1.5 uniform-per-plan binding). A NOT_EMPLOYED segment carries no key. The planner
/// lands the record at
/// <c>SegmentSnapshot.Values["WtmNaturalKey"]</c>; <c>MapSegmentToExportLinesAsync</c>
/// reads it back to drive the dated wage-type-mapping lookup via
/// <c>PayrollMappingService.GetMappingAsync(..., asOfDate: segmentStart)</c>.
/// </para>
///
/// <para>
/// Using a named record (over an anonymous type) preserves type-safety across the
/// hydrator-to-consumer boundary; the consumer casts <c>object?</c> back to this
/// record without <c>dynamic</c>.
/// </para>
/// </summary>
/// <param name="OkVersion">OK version the mapping was resolved against.</param>
/// <param name="AgreementCode">Agreement code (e.g. "AC", "HK", "PROSA").</param>
/// <param name="Position">Position code or empty string for the generic-fallback row
/// (canonical convention per <c>PayrollMappingService.GetMappingAsync</c>).</param>
public sealed record WtmNaturalKey(string OkVersion, string AgreementCode, string Position);
