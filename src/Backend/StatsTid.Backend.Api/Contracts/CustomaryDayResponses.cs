using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// [property: AllowedValues] discriminators (PAT-012 strict types), each the init.sql CHECK on the
// customary_days column of the same name: kind {PAID_DAY_OFF, CLOSURE_DAY}; part {WHOLE_DAY,
// FROM_TIME, HOURS}; paid_mode {GIVEN_TO_ALL, ON_REQUEST} (customary_days_paid_mode_vocabulary_check).

/// <summary>
/// One customary day of an institution (ADR-043 D9): the rows of the list, the create and reinstate
/// 201s and the withdraw 200. The day is either recurring (<c>recurringMonth</c> +
/// <c>recurringDay</c>) or a <c>singleDate</c>. <c>partFromTime</c> is set exactly for
/// <c>FROM_TIME</c> (<c>"HH:mm:ss"</c> on the wire), <c>partHours</c> exactly for <c>HOURS</c>,
/// <c>paidMode</c> exactly for <c>PAID_DAY_OFF</c>. <c>validTo</c> is end-exclusive (ADR-018 D8)
/// and null while the custom is in force. Nullable members are present as null.
/// </summary>
public sealed record CustomaryDayResponse(
    Guid CustomId,
    string OrgId,
    string? AgreementCode,
    int? RecurringMonth,
    int? RecurringDay,
    DateOnly? SingleDate,
    [property: AllowedValues("PAID_DAY_OFF", "CLOSURE_DAY")] string Kind,
    [property: AllowedValues("WHOLE_DAY", "FROM_TIME", "HOURS")] string Part,
    TimeOnly? PartFromTime,
    decimal? PartHours,
    [property: AllowedValues("GIVEN_TO_ALL", "ON_REQUEST")] string? PaidMode,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    long Version,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);
