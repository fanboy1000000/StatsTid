using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Services;

/// <summary>
/// S136 / TASK-13603 (ADR-040 D1–D3; closes SEC-046's date dimension) — THE single spelling of
/// "this date lies outside the subject's employment window" for the registration write paths,
/// copying the <see cref="StatsTid.Backend.Api.ApprovalPeriodSaveLock"/> shape exactly: ONE
/// predicate + ONE response-construction site, shared by BOTH registration writers
/// (<c>POST /api/time-entries</c> and <c>POST /api/skema/{employeeId}/save</c>) so the refusal
/// can never drift between them — the same anti-drift rationale that lifted the approval-period
/// predicate out of <c>SkemaEndpoints</c> in S128 (and <c>ApprovalVisibility</c> before it).
///
/// <para>
/// <b>What the gate means (ADR-040 D3, plain language):</b> the employment window governs WHAT
/// DATES are registrable — a time entry, absence, or work-time day dated before the subject's
/// employment start or after their employment end is refused with a 422 for EVERY writer,
/// employee and admin alike. If the window is wrong, HR corrects the window — never the data
/// past it. WHO may write for a deactivated leaver is a different gate (the D3 role floor at the
/// endpoints); this class owns only the date question.
/// </para>
///
/// <para>
/// <b>THE BODY IS DATE-FREE — DO NOT ADD date, echo, or window fields (hard rule, ADR-040 D3).</b>
/// Every sibling Skema 422 echoes the offending <c>date</c> back to the caller; this one
/// DELIBERATELY does not, and the message is ONE uniform Danish class message for BOTH the
/// before-start and the after-end case. Employment dates are HR-scoped data
/// (<c>User.cs:38-62</c>: <c>employment_start_date</c>/<c>employment_end_date</c> are read-gated
/// to HROrAbove and must never reach an Employee-facing DTO, JWT, export — or, per D3, an error
/// body). A 422 that echoed the probed date, or that distinguished "before start" from "after
/// end", would let any employee binary-search their own (or, for a leaking admin surface, a
/// colleague's) employment boundaries by probing registration dates. A future "helpful" field
/// here re-opens that oracle — do not add one.
/// </para>
/// </summary>
internal static class EmploymentWindowGate
{
    /// <summary>
    /// TRUE when the resolved window status refuses registration for the date. The status itself
    /// comes from the <c>IEmploymentWindowResolver</c> family (D1 inclusive end, D2 NULL =
    /// unbounded, no <c>is_active</c> filter) — this predicate adds no window semantics of its
    /// own, it only names the refusal condition once for both writers.
    /// </summary>
    internal static bool IsOutsideEmploymentWindow(EmploymentWindowStatus status)
        => status == EmploymentWindowStatus.NOT_EMPLOYED;

    /// <summary>
    /// THE single construction site for the outside-employment-period 422, so both registration
    /// writers return byte-identically the same status code, body shape, and message. Callers
    /// must have established <see cref="IsOutsideEmploymentWindow"/>.
    ///
    /// <para>House-style body: <c>error</c> (machine key) + <c>kind</c> (the 422-family
    /// discriminator, per the send command's allocation-422 precedent) + <c>message</c> (one
    /// Danish class message). NO date-valued fields, uniform for before-start and after-end —
    /// see the class doc's hard rule before adding ANY field here.</para>
    /// </summary>
    internal static IResult OutsideEmploymentPeriod()
        => Results.Json(new
        {
            error = "outside_employment_period",
            kind = "employment-window",
            message = "Datoen ligger uden for ansættelsesperioden."
        }, statusCode: 422);
}
