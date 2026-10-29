using StatsTid.Auth;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints.Helpers;

/// <summary>
/// S147 / TASK-14704 (SEC-053; owner ruling OQ-1 = (a), strict four-eyes, no exception) — the
/// endpoint half of the rule <b>whoever sent a month can neither approve nor reject it.</b>
///
/// <para><b>Why a second guard beside <see cref="ApprovalSelfGuard"/>.</b> SEC-009's guard compares
/// the approver with the <i>employee</i>. That leaves one hole: an HR user who sends a month on an
/// employee's behalf is not the employee, so the self-guard lets them approve the month they just
/// sent — one person signing both parts. The state tenders (EM A1 (e)) and the owner's ruling say
/// the two signatures must come from two people. This guard compares the approver with the
/// <i>sender</i>.</para>
///
/// <para><b>Who the sender is.</b> <see cref="ApprovalPeriod.EmployeeApprovedBy"/> (the send /
/// employee-approve stamp), or <see cref="ApprovalPeriod.SubmittedBy"/> on a legacy row where the
/// former is null — exactly <c>COALESCE(employee_approved_by, submitted_by)</c>, the expression the
/// transactional predicate in <c>ApprovalPeriodRepository.BuildUpdateStatusCommand</c> uses. The two
/// must stay the same expression: RES-003 item 4 records that a C# mirror of a SQL predicate that
/// drifts from it silently fails open.</para>
///
/// <para><b>What it covers, and what it deliberately does not.</b> Approve and reject only. A reopen
/// is not a signature: it returns the month to DRAFT and clears both stamps, so the ruling leaves it
/// unguarded (R-6). It is one decision fewer than SEC-009, which also refuses reopening one's own
/// APPROVED month.</para>
///
/// <para><b>The three checkpoints.</b> This guard is the first (early, readable 403). The second is
/// the predicate on the conditional UPDATE inside the approve/reject transaction, which closes the
/// race where the month is reopened and re-sent between this check and the write. The third is the
/// payroll export's re-assertion under <c>FOR UPDATE</c>, which refuses to pay a month whose
/// approver is its sender or its employee.</para>
/// </summary>
internal static class ApprovalSenderGuard
{
    /// <summary>
    /// The date-free reason every four-eyes 403 carries (ADR-040 D7: no employment date in any
    /// error body). The server log names the clause (SEC-053).
    /// </summary>
    public const string DenialReason = "the sender of a month cannot approve or reject it (four-eyes)";

    /// <summary>
    /// The month's sender: <see cref="ApprovalPeriod.EmployeeApprovedBy"/>, else the legacy
    /// <see cref="ApprovalPeriod.SubmittedBy"/>; <c>null</c> when neither is stamped.
    /// </summary>
    public static string? SenderOf(ApprovalPeriod period) =>
        period.EmployeeApprovedBy ?? period.SubmittedBy;

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="actor"/> sent <paramref name="period"/> — the actor id
    /// is present AND string-equal (Ordinal, as <see cref="ApprovalSelfGuard"/>) to the sender. A null
    /// or empty sender never matches, so a month with no sender stamp is not blocked by this guard.
    /// A <c>true</c> result at approve or reject MUST deny (403).
    /// </summary>
    public static bool IsSender(ActorContext actor, ApprovalPeriod period)
    {
        var sender = SenderOf(period);
        return !string.IsNullOrEmpty(actor.ActorId)
            && !string.IsNullOrEmpty(sender)
            && string.Equals(actor.ActorId, sender, StringComparison.Ordinal);
    }
}
