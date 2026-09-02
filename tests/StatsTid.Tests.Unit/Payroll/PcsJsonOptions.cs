using System.Reflection;
using System.Text.Json;
using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S137 Step-5a (Reviewer WARNING 3) — the ONE accessor for the REAL
/// <c>PeriodCalculationService.JsonOptions</c> object, the production <c>segments_jsonb</c>
/// writer/reader (and, per QUAL-146, also the rule-engine HTTP wire format).
///
/// <para>
/// Plain-language: the byte-parity claim of ADR-040 D5 ("a windowless employee's manifest
/// serializes exactly as before") is only worth something if the test serializes with the
/// SAME options object production uses. Until this accessor existed, three tests each held a
/// hand-copied replica with a "KEEP IN SYNC" comment — a promise, not a check: had someone
/// added a <c>DefaultIgnoreCondition</c> to the real options, the replicas would have kept
/// passing while production bytes changed. Binding to the real private field by reflection
/// closes that gap; renaming or re-shaping the field fails every consumer loudly (the throw
/// below) instead of silently diverging.
/// </para>
///
/// <para>
/// Reflection is unavoidable: the Web-SDK Payroll assembly cannot expose internals to the test
/// projects without a <c>Program</c>-type clash against <c>Backend.Api</c> (the standing
/// rationale from the Regression suite's <c>TestFixtures.ManifestReadOptions</c>, which binds
/// the same way). Do NOT add a replica alongside this — it is exactly the drift this exists
/// to remove. And do NOT mutate the returned instance: it is production's live singleton.
/// </para>
/// </summary>
internal static class PcsJsonOptions
{
    private const string FieldName = "JsonOptions";

    /// <summary>The real, live <c>PeriodCalculationService.JsonOptions</c> instance.</summary>
    public static JsonSerializerOptions Real { get; } = Resolve();

    private static JsonSerializerOptions Resolve()
    {
        var field = typeof(PeriodCalculationService).GetField(
            FieldName, BindingFlags.NonPublic | BindingFlags.Static);

        return field?.GetValue(null) as JsonSerializerOptions
            ?? throw new InvalidOperationException(
                $"PeriodCalculationService.{FieldName} (private static JsonSerializerOptions) was not " +
                "found by reflection — the field was renamed, made non-static, or changed type. Update " +
                "PcsJsonOptions.FieldName; do NOT reintroduce a hand-copied replica of the options.");
    }
}
