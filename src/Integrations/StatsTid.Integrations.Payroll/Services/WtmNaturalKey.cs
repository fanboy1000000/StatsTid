namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Wage-type-mapping natural-key triple captured at planner time and read back at
/// export time (ADR-020 D1 component 2 — replay-stable input).
///
/// <para>
/// The hydrator registered in <c>PeriodCalculationService.BuildPlanForLegacyCallersAsync</c>
/// at the PCS L585 enrollment seam constructs this record from the in-scope
/// <see cref="StatsTid.SharedKernel.Models.EmploymentProfile"/> once per plan
/// (ADR-020 D1.5 uniform-per-plan binding). The planner lands the record at
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
