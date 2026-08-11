using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api;

/// <summary>
/// S128 / TASK-12803 — THE single spelling of "this month is closed to registration writes", lifted
/// VERBATIM out of <c>SkemaEndpoints</c> (where S127/TASK-12704 minted it as two private statics)
/// so <c>POST /api/time-entries</c> can enforce the SAME lock without a hand-copied status list.
///
/// <para>Precedent: <see cref="ApprovalVisibility"/> — the S124/TASK-12405 read-side lift that exists
/// because two hand-copied status literals bound only by a comment are a drift hazard. This is the
/// write-side sibling. PAT-015's checklist item ("the in-lock authoritative check shares ONE predicate
/// and ONE response-construction site with the fast path") now spans processes-of-asking in TWO
/// endpoints: Skema's save asks twice (pre-tx fast path + in-lock authoritative), and the time-entry
/// POST asks once (in-lock only). All askings route through here.</para>
///
/// <para>The set is the one Skema's pre-transaction check has always used — <c>EMPLOYEE_APPROVED</c>
/// and <c>APPROVED</c>, the two manager-visible/locked states (owner ruling R3, S128). A null period
/// (no row for the month) is NOT locked: nothing has been sent. <c>SUBMITTED</c> stays writable —
/// legacy status, deliberately NOT blocked (S127 owner ruling R6); <c>DRAFT</c> and <c>REJECTED</c>
/// are editable by design. Status-only, ALL actors including HR — no actor tiering on the write side.</para>
/// </summary>
internal static class ApprovalPeriodSaveLock
{
    /// <summary>
    /// S127 / TASK-12704 — true when the period's status locks the month against registration
    /// writes. The two manager-visible/locked states only; null (no row) is NOT locked.
    /// </summary>
    internal static bool IsPeriodLockedForSave(ApprovalPeriod? period)
        => period is not null && period.Status is "EMPLOYEE_APPROVED" or "APPROVED";

    /// <summary>
    /// S127 / TASK-12704 — THE single construction site for the period-locked 409, so every asking
    /// (Skema's pre-transaction fast path, Skema's in-lock re-read, and the time-entry POST's in-lock
    /// check) returns byte-identically the same status code, body shape and message. Callers must
    /// have established <see cref="IsPeriodLockedForSave"/>.
    /// </summary>
    internal static IResult PeriodLockedForSaveConflict(ApprovalPeriod period)
        => Results.Conflict(new { error = $"Cannot save entries for a period with status {period.Status}" });
}
