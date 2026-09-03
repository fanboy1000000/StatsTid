namespace StatsTid.Backend.Api.Contracts;

// S138 / TASK-13802 (PAT-012 typed contracts) — the request + response shapes of the DEDICATED
// agreement-code endpoint `PUT /api/admin/users/{userId}/agreement-code`.
//
// Why a dedicated endpoint at all (the plain-language version): the general
// `PUT /api/admin/users/{userId}` can only address an ACTIVE user — deliberately, because that
// handler also owns the `is_active` switch and widening its lock would create a side-door for
// reactivating a departed employee. But HR genuinely needs to correct a DEPARTED employee's
// agreement code (their final months are exactly the ones payroll still has to settle). So the
// leaver case gets its own narrow surface: two fields, no lifecycle switch, terminated-inclusive
// reads behind a LocalHR floor. Nothing here can reactivate anyone.
//
// NOTE deliberately NOT declared with [AllowedValues]: `agreementCode` is agreement data, an open
// set by design (the same reasoning recorded on the user admin contracts).

/// <summary>
/// The PUT /api/admin/users/{userId}/agreement-code request body — deliberately just the two
/// fields the correction needs.
/// </summary>
/// <param name="AgreementCode">The collective-agreement code that actually applied from
/// <paramref name="EffectiveFrom"/>. Required, non-blank.</param>
/// <param name="EffectiveFrom">The date the code actually took effect. Any past-or-today date
/// (UTC); a FUTURE date is refused with a date-free 422 (ADR-040 D8 as amended — future-dating is
/// Increment 4). A date before the employee's employment start is refused the same way.</param>
public sealed record UpdateUserAgreementCodeRequest(
    string AgreementCode,
    DateOnly EffectiveFrom);

/// <summary>
/// The PUT /api/admin/users/{userId}/agreement-code 200 body.
/// </summary>
/// <param name="UserId">The subject.</param>
/// <param name="AgreementCode">The code as of TODAY — i.e. the live <c>users.agreement_code</c>
/// cache after the write. For a purely HISTORICAL correction this is UNCHANGED (the correction
/// altered a closed interval, not today's truth), which is the honest answer and the point of the
/// field being here rather than echoing the request.</param>
/// <param name="EffectiveFrom">The start of the interval the write produced.</param>
/// <param name="EffectiveTo">Its end, EXCLUSIVE; <c>null</c> = the row is open (it runs to
/// today and beyond).</param>
/// <param name="NoOp">True when the request equalled the row already covering that date, so
/// nothing at all was written — no row, no event, no version bump, and
/// <paramref name="Version"/> (the ETag) is unchanged.</param>
/// <param name="Version">The new <c>users.version</c> — the single client concurrency token for
/// this aggregate, and the value stamped on the ETag header for the next If-Match.</param>
public sealed record UserAgreementCodeUpdatedResponse(
    string UserId,
    string AgreementCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool NoOp,
    long Version);
