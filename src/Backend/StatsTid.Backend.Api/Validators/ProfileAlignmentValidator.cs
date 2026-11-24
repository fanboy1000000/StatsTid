using System.Text.Json;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Validators;

/// <summary>
/// Pure save-time validator for <see cref="LocalAgreementProfile"/> writes. It checks two things:
/// <list type="bullet">
/// <item><description>every settable value the profile carries, changed or not: the range and
/// storage rules of the numbers, the vocabularies, the office-hours windows and the credit
/// basis (SEC-061; ADR-043 D2, D3, D6); and</description></item>
/// <item><description>the per-field <c>effective_from</c> alignment of the changed fields,
/// sourced from <see cref="LocalAgreementProfileAlignmentPolicies.ByFieldName"/> (ADR-017 D9a);
/// for example, <c>WeeklyNormHours</c> requires a Monday <c>effective_from</c> to match the
/// weekly NormCheck window edge.</description></item>
/// </list>
///
/// The validator is intentionally narrow: no I/O, no clock, no static dispatch beyond the
/// alignment-policy map lookup, no rule-engine imports (PAT-005). Caller (PUT endpoint) maps a
/// failing <see cref="ValidationResult"/> to HTTP 400 with the structured per-field error body
/// described in ADR-017 D9a, before its transaction opens.
/// </summary>
public sealed class ProfileAlignmentValidator
{
    // The columns' own limits, not domain ceilings: SEC-033 (owner ruling OQ-2) allows no invented
    // ceiling, but a value its column cannot hold cannot be stored at all. NUMERIC(p,2) holds at
    // most two decimals and p-2 integer digits (init.sql, local_agreement_profiles).
    private const decimal Numeric5Scale2Max = 999.99m;   // NUMERIC(5,2)
    private const decimal Numeric6Scale2Max = 9999.99m;  // NUMERIC(6,2)

    /// <summary>
    /// Validates the profile's settable values (every member, whether or not it changed) and the
    /// changed fields' <c>effective_from</c> alignment (ADR-017 D9a; today only WeeklyNormHours
    /// has an alignment policy, Monday-only). Returns a structured result with at most one error
    /// per field; the caller maps it to HTTP 400 with per-field errors.
    ///
    /// Fields without an entry in <see cref="LocalAgreementProfileAlignmentPolicies.ByFieldName"/>
    /// are treated as "no alignment requirement" — the static map is the single source-of-truth
    /// per ADR-017 D9a's design.
    /// </summary>
    public ValidationResult Validate(LocalAgreementProfile profile, IReadOnlyDictionary<string, JsonElement> changedFields)
    {
        var errors = new List<FieldValidationError>();

        AddValueErrors(profile, errors);

        foreach (var (fieldName, _) in changedFields)
        {
            // At most one error per field: the editor keys errors by field, so a second one would
            // overwrite the first. A value error wins over the field's alignment error.
            if (errors.Exists(e => string.Equals(e.Field, fieldName, StringComparison.Ordinal)))
                continue;

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

    /// <summary>
    /// The value check of every settable member (SEC-061; ADR-043 D2, D3, D6), first failure per
    /// field only. Null is never refused: it means "inherit central" for the four legacy members
    /// and "not set" for the rest. Without these rules a bad value reaches the table's CHECKs or
    /// column types and comes back as a 500, or is rounded on store while the audit delta keeps
    /// what was sent. The office-hours sum against the norm is never refused here; the editor warns
    /// (ADR-043 D3).
    /// </summary>
    private static void AddValueErrors(LocalAgreementProfile p, List<FieldValidationError> errors)
    {
        CheckNumber(errors, nameof(p.WeeklyNormHours), p.WeeklyNormHours, mustBePositive: true, Numeric5Scale2Max);            // weekly_norm_hours NUMERIC(5,2)
        CheckNumber(errors, nameof(p.MaxFlexBalance), p.MaxFlexBalance, mustBePositive: false, Numeric6Scale2Max);             // max_flex_balance NUMERIC(6,2)
        CheckNumber(errors, nameof(p.MaxOvertimeHoursPerPeriod), p.MaxOvertimeHoursPerPeriod, mustBePositive: false, Numeric6Scale2Max); // max_overtime_hours_per_period NUMERIC(6,2)

        // The vocabularies are their init.sql CHECKs' values, compared ordinally.
        CheckVocabulary(errors, nameof(p.FlexSettlementPeriod), p.FlexSettlementPeriod, "MONTH");
        CheckVocabulary(errors, nameof(p.FlexCapTestPeriod), p.FlexCapTestPeriod, "MONTH");
        CheckNumber(errors, nameof(p.FlexPlusCapHours), p.FlexPlusCapHours, mustBePositive: false, Numeric6Scale2Max);         // flex_plus_cap_hours NUMERIC(6,2)
        CheckNumber(errors, nameof(p.FlexMinusCapHours), p.FlexMinusCapHours, mustBePositive: false, Numeric6Scale2Max);       // flex_minus_cap_hours NUMERIC(6,2)
        CheckNumber(errors, nameof(p.FlexDailyCreditCapHours), p.FlexDailyCreditCapHours, mustBePositive: false, Numeric5Scale2Max); // flex_daily_credit_cap_hours NUMERIC(5,2)
        CheckVocabulary(errors, nameof(p.FlexAboveCapAction), p.FlexAboveCapAction, "FORFEIT");
        CheckVocabulary(errors, nameof(p.FlexLeavingAction), p.FlexLeavingAction, "FORFEIT_POSITIVE_RECORD_NEGATIVE");

        var anyWindow = false;
        anyWindow |= CheckWindow(errors, nameof(p.OfficeHoursMonStart), p.OfficeHoursMonStart, nameof(p.OfficeHoursMonEnd), p.OfficeHoursMonEnd);
        anyWindow |= CheckWindow(errors, nameof(p.OfficeHoursTueStart), p.OfficeHoursTueStart, nameof(p.OfficeHoursTueEnd), p.OfficeHoursTueEnd);
        anyWindow |= CheckWindow(errors, nameof(p.OfficeHoursWedStart), p.OfficeHoursWedStart, nameof(p.OfficeHoursWedEnd), p.OfficeHoursWedEnd);
        anyWindow |= CheckWindow(errors, nameof(p.OfficeHoursThuStart), p.OfficeHoursThuStart, nameof(p.OfficeHoursThuEnd), p.OfficeHoursThuEnd);
        anyWindow |= CheckWindow(errors, nameof(p.OfficeHoursFriStart), p.OfficeHoursFriStart, nameof(p.OfficeHoursFriEnd), p.OfficeHoursFriEnd);

        if (CheckVocabulary(errors, nameof(p.PaidAbsenceCreditBasis), p.PaidAbsenceCreditBasis, "PARAGRAPH_7_NR_2", "OFFICE_HOURS")
            && string.Equals(p.PaidAbsenceCreditBasis, "OFFICE_HOURS", StringComparison.Ordinal)
            && !anyWindow)
        {
            // At least one weekday window, not all five: a day without office hours falls back to
            // § 7 nr. 2 (ADR-043 D2), so an office closed on Fridays stays valid.
            errors.Add(new FieldValidationError(nameof(p.PaidAbsenceCreditBasis), "OFFICE_HOURS_WITHOUT_WINDOWS", null));
        }
    }

    /// <summary>Sign, then decimals, then the column's own limit; the first failure only.</summary>
    private static void CheckNumber(
        List<FieldValidationError> errors, string field, decimal? value, bool mustBePositive, decimal columnMax)
    {
        if (value is not { } v)
            return;

        string? code =
            mustBePositive && v <= 0 ? "MUST_BE_POSITIVE"
            : !mustBePositive && v < 0 ? "MUST_NOT_BE_NEGATIVE"
            : decimal.Round(v, 2) != v ? "TOO_MANY_DECIMALS"
            : v > columnMax ? "EXCEEDS_STORAGE_LIMIT"
            : null;
        if (code is not null)
            errors.Add(new FieldValidationError(field, code, null));
    }

    /// <summary>True when the value is null or one of <paramref name="allowed"/> (ordinal; an empty
    /// string is not null); otherwise records <c>UNKNOWN_VALUE</c> and returns false.</summary>
    private static bool CheckVocabulary(
        List<FieldValidationError> errors, string field, string? value, params string[] allowed)
    {
        if (value is null || allowed.Contains(value, StringComparer.Ordinal))
            return true;
        errors.Add(new FieldValidationError(field, "UNKNOWN_VALUE", null));
        return false;
    }

    /// <summary>
    /// One weekday's office-hours window: each time a whole minute (PostgreSQL <c>TIME</c> would
    /// otherwise round a finer value on store while the audit delta keeps the original); both
    /// times or neither (the missing one is named); start before end, checked only when neither
    /// time has a minute error and named on the end. Returns true when both times are set, which
    /// is what the <c>OFFICE_HOURS</c> basis needs.
    /// </summary>
    private static bool CheckWindow(
        List<FieldValidationError> errors, string startField, TimeOnly? start, string endField, TimeOnly? end)
    {
        var startWhole = CheckWholeMinute(errors, startField, start);
        var endWhole = CheckWholeMinute(errors, endField, end);

        if (start is null && end is null)
            return false;
        if (start is null || end is null)
        {
            errors.Add(new FieldValidationError(start is null ? startField : endField, "WINDOW_INCOMPLETE", null));
            return false;
        }
        if (startWhole && endWhole && start.Value >= end.Value)
            errors.Add(new FieldValidationError(endField, "WINDOW_START_NOT_BEFORE_END", null));
        return true;
    }

    private static bool CheckWholeMinute(List<FieldValidationError> errors, string field, TimeOnly? value)
    {
        if (value is not { } t || t.Ticks % TimeSpan.TicksPerMinute == 0)
            return true;
        errors.Add(new FieldValidationError(field, "NOT_WHOLE_MINUTE", null));
        return false;
    }

    /// <summary>
    /// "No scheduled-future" check (ADR-017 D2; D11 fixture #15). Returns a structured error
    /// when <paramref name="effectiveFrom"/> is strictly after <paramref name="today"/>; null
    /// otherwise. The endpoint maps a non-null result to HTTP 400.
    /// Static so unit tests can pin the predicate without spinning up the full pipeline.
    /// </summary>
    public static FieldValidationError? ValidateEffectiveFromTemporality(DateOnly effectiveFrom, DateOnly today)
    {
        if (effectiveFrom > today)
        {
            return new FieldValidationError(
                Field: "EffectiveFrom",
                Code: "EFFECTIVE_FROM_NOT_TODAY_OR_PAST",
                NearestValid: new[] { today });
        }
        return null;
    }
}

/// <summary>
/// Outcome of <see cref="ProfileAlignmentValidator.Validate"/>. <see cref="IsValid"/> is the
/// AND of all per-field checks; <see cref="Errors"/> carries the failing fields.
/// </summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<FieldValidationError> Errors);

/// <summary>
/// Per-field validation failure record. <see cref="Code"/> is the machine-readable error code:
/// an alignment code from <see cref="FieldAlignmentResult.ErrorCode"/> (e.g.
/// <c>NOT_MONDAY_ALIGNED</c>) or a value code (e.g. <c>MUST_NOT_BE_NEGATIVE</c>).
/// <see cref="NearestValid"/> carries the nearest-valid candidate dates surfaced in the
/// 400 error body (ISO-8601 dates per ADR-017 D9a); null for a value error.
/// </summary>
public sealed record FieldValidationError(string Field, string Code, IReadOnlyList<DateOnly>? NearestValid);
