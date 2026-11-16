using StatsTid.Backend.Api.Validators;
using StatsTid.Infrastructure;

namespace StatsTid.Backend.Api.Validation;

/// <summary>
/// The applies-from date of the five dated configuration writes (QUAL-182, ADR-043 D12; TASK-14818 item 4):
/// the agreement-config publish and the position override's create, edit, deactivate and activate.
///
/// <para>
/// <b>In plain words.</b> Every such write says from which day it applies. Left out, it means today in
/// Danish time (A-14), never the empty-date trap <c>0001-01-01</c>. It may lie in the past (agreements are
/// often signed after they take effect) but never in the future (R-18, ADR-017 D2): a future date is
/// refused with today as the suggestion. The two rules that need the database, "not before the version it
/// replaces" and "a Monday when a week-read field changes", are decided inside the write's transaction
/// under its key lock (<see cref="AppliesFromRefusedException"/>); this class maps them to the same 400.
/// </para>
///
/// <para>
/// The 400 body is the profile PUT's (ADR-017 D9a, <c>ConfigEndpoints.cs</c>):
/// <c>{ error, fields: [{ field, code, nearestValid }] }</c>, ISO dates. The date's own errors name the
/// requests' member, <c>AppliesFrom</c>; a Monday error names the changed field.
/// </para>
/// </summary>
public static class AppliesFromValidation
{
    /// <summary>
    /// The write's date: <paramref name="requested"/>, or <paramref name="today"/> (the Copenhagen business day
    /// the handler read once from its injected <see cref="TimeProvider"/>, PAT-028) when it is omitted; and a
    /// 400 when that date is after today, through <see cref="ProfileAlignmentValidator.ValidateEffectiveFromTemporality"/>
    /// (code <c>EFFECTIVE_FROM_NOT_TODAY_OR_PAST</c>, <c>nearestValid = [today]</c>).
    /// </summary>
    public static (DateOnly AppliesFrom, IResult? Refusal) Resolve(DateOnly? requested, DateOnly today)
    {
        var appliesFrom = requested ?? today;
        var future = ProfileAlignmentValidator.ValidateEffectiveFromTemporality(appliesFrom, today);
        if (future is null)
            return (appliesFrom, null);

        return (appliesFrom, BadRequest(new[]
        {
            new AppliesFromFieldError(
                AppliesFromRefusedException.DateField, future.Code, future.NearestValid ?? Array.Empty<DateOnly>()),
        }));
    }

    /// <summary>The 400 for a date refused inside the write's transaction (nothing was written).</summary>
    public static IResult BadRequest(AppliesFromRefusedException refusal) => BadRequest(refusal.Errors);

    private static IResult BadRequest(IReadOnlyList<AppliesFromFieldError> errors) =>
        Results.BadRequest(new
        {
            error = "Applies-from date validation failed",
            fields = errors.Select(e => new
            {
                field = e.Field,
                code = e.Code,
                nearestValid = e.NearestValid.Select(d => d.ToString("O")).ToArray(),
            }).ToArray(),
        });
}
