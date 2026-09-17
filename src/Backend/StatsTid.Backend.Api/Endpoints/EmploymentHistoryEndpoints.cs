using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S141 / TASK-14113 (refinement C2) — the EMPLOYMENT HISTORY read:
/// <c>GET /api/hr/employees/{employeeId}/history</c>. One endpoint over the two range reads in
/// <see cref="EmploymentHistoryReadRepository"/>.
///
/// <para>
/// <b>What this is for, in plain terms.</b> HR needs to answer "what has changed for this employee,
/// and when did it take effect?" Before this endpoint they could not. The facts were already stored —
/// every profile change and every agreement-code change is a DATED row — but nothing read them back
/// as a history, and the audit log cannot stand in: it has no subject filter, and it orders by when a
/// change was RECORDED rather than when it took EFFECT. Those two orderings are genuinely different
/// (a correction to March typed in September files under September), and the second one is what the
/// question means. This read is ordered by effective date.
/// </para>
///
/// <para>
/// <b>★ Access: HR-gated and organisation-scoped on the subject's CURRENT organisation.</b> An
/// employment history discloses position changes, working-time changes and agreement changes for a
/// NAMED person, so both halves of the gate are load-bearing and neither is decoration:
/// <list type="number">
///   <item><description><c>RequireAuthorization("HROrAbove")</c> proves the actor's ROLE SHAPE — an
///     Employee or Leader token never reaches the handler at all (there is no own-data branch here:
///     this is an HR surface, and an employee reading their own history is a separate product
///     decision nobody has taken).</description></item>
///   <item><description><see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/>
///     with a <see cref="StatsTidRoles.LocalHR"/> FLOOR binds that role to THIS subject's
///     organisation. The floor is the part people forget: without it a MIXED-ROLE token (HR in org A,
///     Leader in org B) passes the policy on its primary role and is then admitted by its Leader scope
///     for a subject in B — FAIL-001, the exact leak S76 closed. Passing the floor makes the ADMITTING
///     scope itself prove HR.</description></item>
/// </list>
/// This is the established helper, used unchanged and on purpose: re-implementing "may this actor see
/// this employee" is precisely how access bugs enter this codebase.
/// </para>
///
/// <para>
/// <b>Why the scope is automatically the subject's CURRENT organisation.</b> The validator resolves
/// the subject through <c>users.primary_org_id</c> — their current home — and neither
/// <c>employee_profiles</c> nor <c>user_agreement_codes</c> carries an organisation column at all. So
/// the stamped-org drift that the S140 follow-up reads had to guard against (a transferred employee
/// still visible to the OLD organisation's HR) cannot arise here: there is no stamped org to drift.
/// The consequence for a transferred employee is worth stating plainly, because it is a policy and not
/// an accident: their WHOLE history, including the part lived in the previous organisation, is read by
/// the CURRENT organisation's HR and by nobody else.
/// </para>
///
/// <para>
/// <b>Terminated-inclusive, matching the profile GET.</b> A leaver's history is exactly what HR asks
/// for, and the profile GET and PUT on the same records were already widened this way (S138 /
/// TASK-13802) with the same LocalHR floor doing the binding. An active-only read here would make the
/// leaver case — the common one — unreachable through the product while adding no safety, since the
/// floor, not the target's active flag, is what binds privilege.
/// </para>
///
/// <para>
/// <b>An unknown employee answers 403, not 404 — deliberately.</b> The validator cannot distinguish
/// "no such employee" from "out of your scope" without telling an out-of-scope caller which employee
/// ids exist. Both come back as 403 with the validator's own reason string. An employee who EXISTS and
/// is in scope but has no dated rows answers 200 with empty tracks: the history read reports what is
/// there, and a missing row is a data hole for the HRP-015 detector (TASK-14105) to surface, not
/// something for this read to dress up as an error.
/// </para>
///
/// <para>
/// <b>★ Scheduled (not yet in force) intervals ARE included, clearly marked.</b> S141 wave 1 made a
/// future-dated change legal (ADR-040 D8), so an employee's records may now contain an interval that
/// has not started. The alternative — showing only what has taken effect — was considered and
/// rejected: the owner's standing requirement for this sprint is that a scheduled change must be
/// visible wherever a profile is read, and a HISTORY view is the most surprising possible place to
/// hide one. HR asking "what has changed for this person" and not being shown the change that is
/// already booked for next month is the defect this sprint exists to prevent, one screen over. So the
/// interval is returned with <c>status = "SCHEDULED"</c>, and the caller can render it as pending
/// rather than as fact.
/// </para>
///
/// <para>
/// <b>"Today" is the COPENHAGEN business day (S142 / TASK-14205, census row 17).</b> The two
/// calendars agree except for a late-evening window (Denmark is UTC+1 in winter, UTC+2 in summer, so
/// between Danish midnight and UTC midnight the UTC calendar is still on yesterday), and in that
/// window the choice decides whether a change saved as "today" reads back as CURRENT or as SCHEDULED.
/// <br/>
/// This paragraph used to argue the opposite — that the UTC day was right HERE because it was what
/// every profile / agreement-code WRITER used, and a view whose "today" disagreed with the writer's
/// would mark a just-saved change as not-yet-in-force. That reasoning was sound and its premise has
/// now moved: S142 puts every one of those writers
/// (<c>EmployeeProfileEndpoints</c>, <c>AdminEndpoints</c>' users and agreement-code PUTs,
/// <c>EmployeeProfileRepository</c>'s dated writes) on
/// <see cref="StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate"/>, so matching the writers is
/// now exactly what this line does. The rule the old comment was really stating — <i>this read must
/// use the same calendar as the writers it renders</i> — is preserved verbatim; only the calendar
/// both sides use has changed. If a future change moves the writers again, move this with them.
/// </para>
///
/// <para>
/// <b>No concurrency token is served here.</b> ADR-019 gives an employee's timeline ONE client token —
/// the open row's version, handed out as the ETag on <c>GET /api/admin/employee-profiles/{id}</c>. This
/// response carries no <c>version</c> on any interval and sets no ETag, so nothing here can be mistaken
/// for an If-Match value. A read-only screen that handed out a per-row token would be inviting a write
/// against the wrong row.
/// </para>
///
/// <para><b>Read-only.</b> No write, no event, no audit row — there is nothing to audit, because
/// nothing changes (ADR-026 covers state changes; a read emits none).</para>
/// </summary>
public static class EmploymentHistoryEndpoints
{
    public static WebApplication MapEmploymentHistoryEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // GET /api/hr/employees/{employeeId}/history?from=&to=
        // ═══════════════════════════════════════════
        app.MapGet("/api/hr/employees/{employeeId}/history", async (
            string employeeId,
            DateOnly? from,
            DateOnly? to,
            EmploymentHistoryReadRepository historyRepo,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // ── The access gate (see the class doc). HROrAbove has already proved the role SHAPE;
            // this binds it to THIS subject's current organisation, with the LocalHR floor so the
            // ADMITTING scope must itself be HR+ (FAIL-001 / the S76 mixed-role leak).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // An inverted window is a caller mistake, not an empty history — answering 200 with two
            // empty tracks would let a mistyped filter read as "this employee has never changed".
            if (from is not null && to is not null && from.Value >= to.Value)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "Invalid history window",
                    reason = "'from' must be earlier than 'to' ('to' is end-exclusive, so from == to selects nothing).",
                });
            }

            // ONE date for the whole response (PAT-028): both tracks describe the same day, or the two
            // halves of one screen could disagree about what is in force. S142 / TASK-14205 (census
            // row 17) — the COPENHAGEN business day, matching the writers this view renders (see the
            // class doc). On the old UTC day, an HR user opening the history screen at 00:30 Danish
            // time saw a change that took effect TODAY labelled "not yet in force".
            var today = CopenhagenBusinessDate.Today(timeProvider);

            var profileRows = await historyRepo.GetProfileIntervalsAsync(employeeId, from, to, ct);
            var agreementRows = await historyRepo.GetAgreementCodeIntervalsAsync(employeeId, from, to, ct);

            // "What changed here?" is answered by comparing an interval with the one before it — and
            // with a bounded window the first interval returned usually HAS a predecessor that the
            // window excluded. Fetch that one row as the comparison baseline; it is never returned.
            //
            // Skipped when `from` is unbounded, because then the first returned interval provably IS
            // the employee's earliest (the `to` filter only bounds the high end), so the query could
            // only ever come back empty. Skipped on an empty window because there is nothing to
            // compare. Both mappers take the baseline as an option either way, so the two cases share
            // one code path rather than branching on how the caller happened to ask.
            var profileBaseline = from is not null && profileRows.Count > 0
                ? await historyRepo.GetProfileIntervalBeforeAsync(employeeId, profileRows[0].EffectiveFrom, ct)
                : null;
            var agreementBaseline = from is not null && agreementRows.Count > 0
                ? await historyRepo.GetAgreementCodeIntervalBeforeAsync(employeeId, agreementRows[0].EffectiveFrom, ct)
                : null;

            return Results.Ok(new EmploymentHistoryResponse(
                EmployeeId: employeeId,
                Today: today,
                WindowFrom: from,
                WindowTo: to,
                ProfileHistory: ToProfileIntervals(profileRows, profileBaseline, today),
                AgreementCodeHistory: ToAgreementIntervals(agreementRows, agreementBaseline, today)));
        }).RequireAuthorization("HROrAbove")
        .Produces<EmploymentHistoryResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>
    /// Where an interval sits relative to <paramref name="today"/>, END-EXCLUSIVE throughout
    /// (ADR-018 D9): an interval ending ON today has already ended, because its last covered day was
    /// yesterday. Getting this backwards would show a superseded interval as the current one for one
    /// day at every boundary — the kind of off-by-one nobody notices until a payroll question.
    ///
    /// <para><b>This deliberately has no "retired" branch.</b> A cancelled scheduled change is a
    /// ZERO-WIDTH row, and those are filtered out at the READ
    /// (<see cref="EmploymentHistoryReadRepository"/>, via the shared
    /// <c>EmploymentTimelineSql.CoversAtLeastOneDayPredicate</c>) so none ever reaches this method.
    /// Doing it there rather than here is the point: the rule is "an interval covering no days is not
    /// part of a history of effective periods", which is a statement about what belongs in the list,
    /// not about how to label something that does. A fourth status would have put a cancelled change
    /// back on screen under a different name.</para>
    /// </summary>
    private static string StatusOf(DateOnly effectiveFrom, DateOnly? effectiveTo, DateOnly today)
    {
        if (effectiveFrom > today) return EmploymentHistoryIntervalStatus.Scheduled;
        if (effectiveTo is not null && effectiveTo.Value <= today) return EmploymentHistoryIntervalStatus.Past;
        return EmploymentHistoryIntervalStatus.Current;
    }

    /// <summary>
    /// Maps profile rows to wire intervals, computing "what changed" by comparing each row with the one
    /// BEFORE it in effective-date order. The comparison is done here rather than in SQL on purpose: it
    /// is the question the user is asking ("what changed"), it is pure, and it is trivially testable
    /// without a database.
    ///
    /// <para><b><paramref name="baseline"/> is the predecessor of row 0 when the window excluded it</b>
    /// (S141 sprint-end review). It is used ONLY for comparison and never appears in the output. When it
    /// is non-null, row 0 is NOT the employee's first record, so <c>IsInitial</c> is false and its
    /// <c>ChangedFields</c> are computed against it — which is the whole difference between a screen
    /// saying "first registration, nothing changed" and one saying what actually changed that day.</para>
    /// </summary>
    private static IReadOnlyList<EmploymentProfileHistoryInterval> ToProfileIntervals(
        IReadOnlyList<EmploymentProfileHistoryRow> rows,
        EmploymentProfileHistoryRow? baseline,
        DateOnly today)
    {
        var result = new List<EmploymentProfileHistoryInterval>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var previous = i == 0 ? baseline : rows[i - 1];
            var changed = new List<string>(3);
            if (previous is not null)
            {
                if (previous.PartTimeFraction != row.PartTimeFraction)
                    changed.Add(EmploymentHistoryFields.PartTimeFraction);
                if (!string.Equals(previous.Position, row.Position, StringComparison.Ordinal))
                    changed.Add(EmploymentHistoryFields.Position);
                if (!string.Equals(previous.EmploymentCategory, row.EmploymentCategory, StringComparison.Ordinal))
                    changed.Add(EmploymentHistoryFields.EmploymentCategory);
            }

            result.Add(new EmploymentProfileHistoryInterval(
                EffectiveFrom: row.EffectiveFrom,
                EffectiveTo: row.EffectiveTo,
                Status: StatusOf(row.EffectiveFrom, row.EffectiveTo, today),
                IsInitial: i == 0 && baseline is null,
                ChangedFields: changed,
                PartTimeFraction: row.PartTimeFraction,
                Position: row.Position,
                EmploymentCategory: row.EmploymentCategory));
        }
        return result;
    }

    /// <summary>Agreement-code sibling of <see cref="ToProfileIntervals"/>; same rules, including the
    /// out-of-window <paramref name="baseline"/>, one field.</summary>
    private static IReadOnlyList<AgreementCodeHistoryInterval> ToAgreementIntervals(
        IReadOnlyList<AgreementCodeHistoryRow> rows,
        AgreementCodeHistoryRow? baseline,
        DateOnly today)
    {
        var result = new List<AgreementCodeHistoryInterval>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var previous = i == 0 ? baseline : rows[i - 1];
            var changed = previous is not null && !string.Equals(previous.AgreementCode, row.AgreementCode, StringComparison.Ordinal)
                ? new List<string> { EmploymentHistoryFields.AgreementCode }
                : new List<string>();

            result.Add(new AgreementCodeHistoryInterval(
                EffectiveFrom: row.EffectiveFrom,
                EffectiveTo: row.EffectiveTo,
                Status: StatusOf(row.EffectiveFrom, row.EffectiveTo, today),
                IsInitial: i == 0 && baseline is null,
                ChangedFields: changed,
                AgreementCode: row.AgreementCode));
        }
        return result;
    }
}
