namespace StatsTid.Infrastructure;

/// <summary>
/// S141 / TASK-14116 (refinement B0, owner requirement 2026-09-11) — the ONE place the system states
/// what counts as a SCHEDULED change on an employment timeline, and the reusable read that finds the
/// next one.
///
/// <para>
/// <b>Plain language, because this is a product rule before it is a SQL rule.</b> HR can now date an
/// employment change ahead — "she moves to 0.6 on 1 November". Until that day arrives the change is
/// real but not in force. Every screen that shows an employment value therefore owes the reader a
/// second fact: <i>somebody has already scheduled a change, and here is the day it lands.</i> A screen
/// that shows today's value without that heads-up invites HR to act on a number that is about to stop
/// being true — which is precisely the defect the owner raised when they asked whether it should not
/// be visible that another has scheduled a change.
/// </para>
///
/// <para>
/// <b>The rule, stated once.</b> A row is a scheduled change when it starts STRICTLY AFTER today AND
/// it is not zero-width. Both halves matter:
/// <list type="bullet">
///   <item><c>effective_from &gt; today</c> — a row starting today or earlier is in force, not
///     scheduled. (Intervals are end-exclusive <c>[from, to)</c> per ADR-018 D9, so "covers today"
///     is <c>from &lt;= today &lt; to</c>.)</item>
///   <item><c>effective_to IS NULL OR effective_to &gt; effective_from</c> — a ZERO-WIDTH row
///     <c>[f, f)</c> covers no day at all. It is the trace a RETIRED scheduled change leaves behind
///     (S141 B4 soft-delete of a scheduled row). Reporting one as a scheduled change would show HR a
///     change that has already been CALLED OFF — worse than showing nothing, because it is
///     confidently wrong.</item>
/// </list>
/// This is the same rule wave 1 wrote into
/// <see cref="EmployeeProfileRepository.GetByEmployeeIdWithScheduledAsync"/> and
/// <see cref="UserAgreementCodeRepository.GetAsOfTodayWithScheduledAsync"/> for the single-employee
/// surfaces, which spell it out inline because those statements also SELECT the scheduled row's
/// VALUES. It lives here so the list surfaces cannot drift into a second, subtly different answer to
/// the same question — a drift nobody would notice until HR saw a cancelled change on the roster.
/// </para>
///
/// <para>
/// <b>Why these are <c>const</c> and spliced with <c>+</c> rather than interpolated.</b> The build
/// enforces CA2100 as an ERROR: a command text that is not a compile-time constant fails the build,
/// which is the guard rail that keeps SQL out of reach of request data. Constant concatenation keeps
/// that guarantee intact — the analyzer can still see one literal — at the cost of a fixed
/// correlation alias, which is why the fragment below hard-codes <c>u</c> as the outer row and
/// <c>@today</c> as the bound date. A caller splices it in and must supply both.
/// </para>
/// </summary>
internal static class EmploymentTimelineSql
{
    /// <summary>
    /// The canonical <b>"this row covers at least one real day"</b> predicate, written against the
    /// alias <c>s</c>. The inverse — a ZERO-WIDTH row <c>[f, f)</c> — is this system's idiom for a
    /// RETIRED row: <c>EmployeeProfileRepository.SoftDeleteAsync</c> retires a scheduled change with
    /// <c>SET effective_to = effective_from</c> rather than deleting it, because no timeline table
    /// here has ever hard-deleted a row and the retirement is separately audited.
    ///
    /// <para>
    /// <b>Why this is its own constant (S141 sprint-end review, TASK-14113).</b> It was extracted from
    /// <see cref="ScheduledRowPredicate"/> — whose text is unchanged, byte for byte — because a SECOND
    /// caller needs this half WITHOUT the "starts after today" half: the employment-history read
    /// (<see cref="EmploymentHistoryReadRepository"/>) reports past, current AND scheduled intervals,
    /// so it cannot reuse the scheduled predicate, but it must drop retired rows for exactly the same
    /// reason the marker does. Sharing the halves keeps ONE definition of "retired" in the system; the
    /// alternative — a second inline copy in the history read — is how the two surfaces would
    /// eventually give different answers about the same cancelled change, and nobody would notice
    /// until HR saw one.
    /// </para>
    ///
    /// <para>Applies unchanged to BOTH dated employment tables: <c>employee_profiles</c> and
    /// <c>user_agreement_codes</c> share the <c>effective_from</c>/<c>effective_to</c> column shape and
    /// the ADR-018 D9 end-exclusive semantics.</para>
    /// </summary>
    public const string CoversAtLeastOneDayPredicate =
        "(s.effective_to IS NULL OR s.effective_to > s.effective_from)";

    /// <summary>
    /// The canonical "this row is a scheduled change" predicate, written against the alias <c>s</c>
    /// and the bound parameter <c>@today</c>: it starts strictly after today AND it is not retired
    /// (<see cref="CoversAtLeastOneDayPredicate"/>). Applies unchanged to BOTH dated employment
    /// tables — <c>employee_profiles</c> and <c>user_agreement_codes</c> share the
    /// <c>effective_from</c>/<c>effective_to</c> column shape and the ADR-018 D9 end-exclusive
    /// semantics.
    /// </summary>
    public const string ScheduledRowPredicate =
        "s.effective_from > @today AND " + CoversAtLeastOneDayPredicate;

    /// <summary>
    /// A <c>LEFT JOIN LATERAL … ON TRUE</c> block yielding ONE column,
    /// <c>scheduled_change_from</c>: the EARLIEST date on which anything about this employee's
    /// employment record changes, or <c>NULL</c> when nothing is scheduled (which, until Increment
    /// 4's date picker ships, is every employee).
    ///
    /// <para>
    /// <b>Contract for the caller.</b> The outer row must be aliased <c>u</c> and expose
    /// <c>u.user_id</c>; the statement must bind <c>@today</c> (the server day off the injected
    /// <see cref="TimeProvider"/>, never <c>CURRENT_DATE</c> — QUAL-157's fixed-clock seam). The
    /// block is newline-padded at both ends so it can be spliced between two raw string literals
    /// without the caller having to get the joins right.
    /// </para>
    ///
    /// <para>
    /// <b>Why BOTH timelines and not just the profile.</b> The list surfaces display a POSITION, which
    /// lives on <c>employee_profiles</c> — but the marker does not answer "does the job title change",
    /// it answers "is it safe to act on what I am looking at". A scheduled AGREEMENT-CODE change is
    /// exactly as much a reason to look before editing: the edit drawer writes the agreement code on
    /// every save, dated, so an HR user who opens a person from an unmarked roster row and re-sends
    /// what they see can drag a not-yet-effective agreement into force early. Covering only the
    /// profile timeline would leave the owner's defect intact one field over — the same argument
    /// <see cref="UserAgreementCodeRepository.GetAsOfTodayWithScheduledAsync"/> makes for the drawer.
    /// One date over both timelines is also what the screens need: they say "this changes on 1
    /// November", they do not render the new values.
    /// </para>
    ///
    /// <para>
    /// <b>Why an aggregate rather than <c>ORDER BY … LIMIT 1</c>, and why that matters here.</b> A
    /// <c>MIN()</c> over an empty set is one row containing NULL, so this lateral returns EXACTLY one
    /// row per outer row BY CONSTRUCTION — it cannot fan out even if the data were malformed. The
    /// single-employee reads could rely on <c>LIMIT 1</c> because they return one row anyway; these
    /// three callers cannot. One of them is PAGED with its total counted in a separate CTE before the
    /// join, so a fan-out there would not merely slow the page down, it would return more rows than
    /// the count it ships alongside them. This is also why the marker rides the existing statement
    /// instead of becoming a per-row lookup: a second query per roster row would be N+1 against a
    /// whole styrelse subtree.
    /// </para>
    ///
    /// <para>
    /// <b>Cost.</b> Each branch is an index seek — <c>idx_employee_profiles_history
    /// (employee_id, effective_from)</c> and <c>idx_user_agreement_codes_history
    /// (user_id, effective_from)</c> both lead on the correlated id and then the date, so the
    /// <c>&gt; @today</c> bound is a range start and the branch touches only that employee's future
    /// rows, of which there are ordinarily zero.
    /// </para>
    ///
    /// <para>
    /// <b>ADR-040 D7.</b> The date returned is a PROFILE or AGREEMENT-CODE effective date — when a
    /// fraction / position / category / agreement starts applying. It is never the employee's hire or
    /// termination date: no row on either timeline carries an employment date, and no writer dates a
    /// row at the hire. All three callers are HROrAbove surfaces besides, so no employee-facing DTO
    /// reaches this.
    /// </para>
    /// </summary>
    public const string EarliestScheduledChangeLateral = "\n" +
        """
        LEFT JOIN LATERAL (
            SELECT MIN(x.effective_from) AS scheduled_change_from
            FROM (
                SELECT s.effective_from
                FROM employee_profiles s
                WHERE s.employee_id = u.user_id
                  AND
        """ + " " + ScheduledRowPredicate + "\n" +
        """
                UNION ALL
                SELECT s.effective_from
                FROM user_agreement_codes s
                WHERE s.user_id = u.user_id
                  AND
        """ + " " + ScheduledRowPredicate + "\n" +
        """
            ) x
        ) sched ON TRUE
        """ + "\n";
}
