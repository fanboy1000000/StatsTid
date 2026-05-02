using System.Text.Json;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Validators;

/// <summary>
/// Pure save-time validator for <see cref="LocalAgreementProfile"/> writes. Today's only
/// policy is the per-field <c>effective_from</c> alignment check sourced from
/// <see cref="LocalAgreementProfileAlignmentPolicies.ByFieldName"/> (ADR-017 D9a) — for
/// example, <c>WeeklyNormHours</c> requires Monday-aligned <c>effective_from</c> to match
/// the weekly NormCheck window edge.
///
/// The validator is intentionally narrow: no I/O, no static dispatch beyond the alignment-
/// policy map lookup, no rule-engine imports (PAT-005). Caller (PUT endpoint) maps a failing
/// <see cref="ValidationResult"/> to HTTP 400 with the structured per-field error body
/// described in ADR-017 D9a.
/// </summary>
public sealed class ProfileAlignmentValidator
{
    /// <summary>
    /// Validates the profile's effective_from date against per-field alignment policies
    /// (ADR-017 D9a). Today only WeeklyNormHours has an alignment policy (Monday-only).
    /// Returns a structured result; the caller maps to HTTP 400 with per-field errors.
    ///
    /// Fields without an entry in <see cref="LocalAgreementProfileAlignmentPolicies.ByFieldName"/>
    /// are treated as "no alignment requirement" — the static map is the single source-of-truth
    /// per ADR-017 D9a's design.
    /// </summary>
    public ValidationResult Validate(LocalAgreementProfile profile, IReadOnlyDictionary<string, JsonElement> changedFields)
    {
        var errors = new List<FieldValidationError>();

        foreach (var (fieldName, _) in changedFields)
        {
            if (LocalAgreementProfileAlignmentPolicies.ByFieldName.TryGetValue(fieldName, out var policy))
            {
                var result = policy(profile.EffectiveFrom);
                if (!result.IsAligned)
                {
                    errors.Add(new FieldValidationError(
                        Field: fieldName,
                        Code: result.ErrorCode!,
                        NearestValid: result.NearestValidDates));
                }
            }
            // Fields with no alignment policy in the static map: validate as aligned
            // (no entry = no alignment requirement). This matches D9a's design: the map
            // is the single source-of-truth.
        }

        return errors.Count == 0
            ? new ValidationResult(true, Array.Empty<FieldValidationError>())
            : new ValidationResult(false, errors);
    }
}

/// <summary>
/// Outcome of <see cref="ProfileAlignmentValidator.Validate"/>. <see cref="IsValid"/> is the
/// AND of all per-field checks; <see cref="Errors"/> carries the failing fields.
/// </summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<FieldValidationError> Errors);

/// <summary>
/// Per-field alignment failure record. <see cref="Code"/> is the machine-readable error code
/// from <see cref="FieldAlignmentResult.ErrorCode"/> (e.g. <c>NOT_MONDAY_ALIGNED</c>).
/// <see cref="NearestValid"/> carries the nearest-valid candidate dates surfaced in the
/// 400 error body (ISO-8601 dates per ADR-017 D9a).
/// </summary>
public sealed record FieldValidationError(string Field, string Code, IReadOnlyList<DateOnly>? NearestValid);
