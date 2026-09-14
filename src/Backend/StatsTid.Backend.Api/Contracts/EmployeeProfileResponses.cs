namespace StatsTid.Backend.Api.Contracts;

// S112 / TASK-11201 (Fork B retrofit, PAT-010/PAT-012) — the named response record for the
// employee-profile admin read/edit (GET 200 + PUT 200 on /api/admin/employee-profiles/{employeeId}
// — both handlers previously returned the SAME anonymous 5-field shape). EXACT shape-copy: member
// order mirrors the prior anonymous order (employeeId … version), serialized camelCase via the
// .NET 8 JsonSerializerDefaults.Web default — NO [JsonPropertyName].
//
// S141 / TASK-14104 (refinement B0, owner requirement 2026-09-11) — ONE additive, nullable member:
// `scheduled`. Additive means every existing client keeps reading the same five fields in the same
// order and is unaffected; nullable means "nothing is scheduled", which until Increment 4's date
// picker ships is every employee.

/// <summary>The employee-profile admin body (GET read / PUT edit result). <paramref name="Position"/>
/// null = no position override (base agreement config applies); <paramref name="IsPartTime"/> is
/// derived (<c>partTimeFraction &lt; 1.0</c>); <paramref name="Version"/> matches the ETag
/// header.
///
/// <para>
/// <b><paramref name="Version"/> and the ETag are the SAME token, and that is load-bearing.</b>
/// Since S141 / TASK-14102 (owner ruling OQ-3 (a)) it is <c>users.version</c> — the one concurrency
/// token for the employee aggregate — not any row's own version. The frontend's profile client falls
/// back to formatting this field into its next <c>If-Match</c> when it cannot read the header, so a
/// body carrying a different number than the header would send a precondition that means something
/// else entirely. Both handlers stamp them from one value; do not let them diverge.
/// </para>
///
/// <para>
/// <b><paramref name="Scheduled"/> — the change already dated ahead (B0).</b> Plain language: the
/// owner asked whether an HR employee looking at a page should be able to see that a colleague has
/// scheduled a change. They should. A screen that shows today's 0.8 without saying it becomes 0.6 on
/// 1 November is not merely incomplete — it invites HR to act on a number that is about to stop being
/// true, and to re-send it and drag the scheduled change forward by accident. Carrying it in the
/// payload rather than leaving each screen to fetch it means every present and future consumer gets
/// it and none can forget to ask.
/// </para>
/// </summary>
public sealed record EmployeeProfileResponse(
    string EmployeeId,
    decimal PartTimeFraction,
    string? Position,
    bool IsPartTime,
    long Version,
    ScheduledProfileChange? Scheduled = null);

/// <summary>
/// S141 / TASK-14104 (refinement B0) — a profile change that is already scheduled to take effect
/// AFTER today: the next profile row starting strictly after today, with the interval it will occupy
/// and the values it will bring.
///
/// <para>
/// <b>Why an interval and not just a date.</b> <paramref name="EffectiveTo"/> is <c>null</c> in the
/// ordinary case — the scheduled change runs indefinitely. A non-null value means yet another change
/// follows it, so a consumer that wants the whole future must read the timeline rather than this one
/// hop. Saying so in the payload is cheaper than letting a screen assume one hop is all there is.
/// </para>
///
/// <para>
/// <b>ADR-040 D7 check, stated because it is the rule most easily broken by accident.</b> These are
/// PROFILE effective dates — when a fraction/position/category change takes effect — never the
/// employee's hire or termination date. No profile row carries an employment date, and no writer
/// dates a profile row at the hire, so nothing here can leak one. A zero-width row is excluded
/// upstream: it covers no day and is the trace a retired scheduled change leaves behind, so reporting
/// it would show HR a change that was deliberately cancelled.
/// </para>
/// </summary>
public sealed record ScheduledProfileChange(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal PartTimeFraction,
    string? Position,
    string? EmploymentCategory);
