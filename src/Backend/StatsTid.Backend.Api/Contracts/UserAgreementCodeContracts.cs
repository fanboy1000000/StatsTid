namespace StatsTid.Backend.Api.Contracts;

// S138 / TASK-13802 (PAT-012 typed contracts) — the request + response shapes of the DEDICATED
// agreement-code endpoint `PUT /api/admin/users/{userId}/agreement-code`.
//
// Why a dedicated endpoint at all (the plain-language version): the general
// `PUT /api/admin/users/{userId}` can only address an ACTIVE user — deliberately, because that
// handler also owns the `is_active` switch and widening its lock would create a side-door for
// reactivating a departed employee. But HR genuinely needs to correct a DEPARTED employee's
// agreement code (their final months are exactly the ones payroll still has to settle). So the
// leaver case gets its own narrow surface: no lifecycle switch, terminated-inclusive reads behind a
// LocalHR floor. Nothing here can reactivate anyone.
//
// NOTE deliberately NOT declared with [AllowedValues]: `agreementCode` is agreement data, an open
// set by design (the same reasoning recorded on the user admin contracts).
//
// S141 / TASK-14104 (ADR-040 Increment 4) — two changes to this contract, both driven by the fact
// that a change may now be dated AHEAD. The date's upper bound is gone (see EffectiveFrom), and a
// third, optional field carries HR's answer to owner ruling OQ-6's question: when a code change is
// already scheduled, does an edit made today apply only until that change, or carry into it?

/// <summary>
/// The PUT /api/admin/users/{userId}/agreement-code request body — the two fields the correction
/// needs, plus S141's optional answer to the scheduled-change question.
/// </summary>
/// <param name="AgreementCode">The collective-agreement code that applies from
/// <paramref name="EffectiveFrom"/>. Required, non-blank.</param>
/// <param name="EffectiveFrom">The date the code takes effect. <b>S141 / TASK-14104 (ADR-040
/// Increment 4): ANY date is now accepted — past, today, or FUTURE.</b> The future was refused up to
/// S138 because a not-yet-effective row would have been read as "current" by the login token and the
/// live <c>users.*</c> caches; S141's wave 1 converted every such read to as-of-today, so dating a
/// change ahead is now the feature rather than the hazard. What remains: an omitted date is a
/// malformed request (a non-nullable <c>DateOnly</c> binds <c>0001-01-01</c>, which would route as a
/// correction covering all recorded history), and a date before the employee's employment start is
/// refused with a DATE-FREE 422 — that refusal must never echo the hire date (ADR-040 D7).</param>
/// <param name="CarryForwardToScheduledChange">S141 / TASK-14104 (owner ruling OQ-6 (a)) — OPTIONAL.
/// HR's answer to "what did you mean by today?" when a code change is already scheduled ahead.
/// <c>true</c> = also carry this code into that scheduled change; <c>null</c> / <c>false</c> = apply
/// until it only, which is the pre-S141 behaviour. Absent means the client did not know about the
/// prompt, so silence must read as the incomplete-but-never-destructive option — defaulting the
/// other way would let a client that never asked HR anything overwrite a colleague's scheduled
/// decision. It has an effect only when this write is actually truncated by a row starting after
/// today, so it may be sent unconditionally.</param>
public sealed record UpdateUserAgreementCodeRequest(
    string AgreementCode,
    DateOnly EffectiveFrom,
    bool? CarryForwardToScheduledChange = null);

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
