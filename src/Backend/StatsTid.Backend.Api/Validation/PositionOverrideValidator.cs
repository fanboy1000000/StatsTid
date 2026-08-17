using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Validation;

/// <summary>
/// SEC-033 server-side range validation for the position-override write surfaces (POST create +
/// PUT update). Every numeric field on these bodies is NULLABLE, and <c>null</c> means "do not
/// override this dimension" — a fully legitimate state — so ONLY supplied (non-null) values are
/// checked; a null field is never rejected. Returned as a <c>(IsValid, Error)</c> pair the
/// endpoint maps to <b>400 Bad Request</b> on the first violation.
///
/// Bounds are non-negativity + the shared valid norm-period set ONLY. There is deliberately NO
/// upper ceiling: 200h is a real seeded flex value (AC DEPARTMENT_HEAD), so a cap would reject
/// legitimate config (owner ruling, SEC-033). The norm-period set is the SharedKernel single
/// source of truth (<see cref="AgreementRuleConfig.ValidNormPeriodWeeks"/>) shared with the Rule
/// Engine. Kept pure so it is unit-testable without a database container.
/// </summary>
public static class PositionOverrideValidator
{
    /// <summary>
    /// Validates the four nullable numeric override fields. Null values are skipped (they mean
    /// "inherit the base config"); supplied values are range-checked.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateRanges(
        decimal? weeklyNormHours, int? normPeriodWeeks, decimal? maxFlexBalance, decimal? flexCarryoverMax)
    {
        // Relational patterns on a nullable only match when the value is non-null → null is skipped.
        if (weeklyNormHours is <= 0)
            return (false, "WeeklyNormHours must be > 0 when supplied");
        if (normPeriodWeeks is int weeks && !AgreementRuleConfig.ValidNormPeriodWeeks.Contains(weeks))
            return (false, $"NormPeriodWeeks must be one of: {string.Join(", ", AgreementRuleConfig.ValidNormPeriodWeeks.OrderBy(w => w))} when supplied");
        if (maxFlexBalance is < 0)
            return (false, "MaxFlexBalance must be >= 0 when supplied");
        if (flexCarryoverMax is < 0)
            return (false, "FlexCarryoverMax must be >= 0 when supplied");
        return (true, null);
    }
}
