using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Loading;

/// <summary>
/// S128 / TASK-12802 — the PURE per-activity period-stage decision, split out (UnitLoadPlanner
/// style) so the probe-first idempotency logic is unit-testable without HTTP.
///
/// <para>Why this exists (S127 FU-C): the period stage used to be merely CONFLICT-TOLERANT — it
/// re-sent every outcome-bearing month and let a 409 mean "already there". But REJECTED is a
/// legitimate send source (<c>AllowedSendSourceStates = {DRAFT, SUBMITTED, REJECTED}</c>,
/// ApprovalEndpoints.cs), so a re-run over an already-loaded DB re-sent and re-rejected every
/// REJECTED month: final statuses identical, but one extra send/reject event PAIR per rejected
/// month per re-run polluting the event stream. Probe-first (one <c>GET /api/approval/by-month</c>
/// per distinct month) makes the re-run plan ZERO writes when the observed status already equals
/// the target — INCLUDING the REJECTED case.</para>
///
/// <para>Server state machine the plans are derived from (verified S128 in ApprovalEndpoints.cs):
/// send {DRAFT, SUBMITTED, REJECTED} → EMPLOYEE_APPROVED; approve {SUBMITTED, EMPLOYEE_APPROVED}
/// → APPROVED; reject {SUBMITTED, EMPLOYEE_APPROVED} → REJECTED.</para>
/// </summary>
public static class PeriodLoadPlanner
{
    /// <summary>One period row as observed by the by-month probe: the SERVER period id + its
    /// current <c>approval_periods.status</c>. The id is what lets a resume plan approve/reject
    /// WITHOUT a fresh send (the send response used to be the only id source).</summary>
    public sealed record ObservedPeriod(Guid PeriodId, string Status);

    /// <summary>The executable steps. <see cref="Send"/> yields a fresh period id; on a plan that
    /// STARTS with <see cref="Approve"/>/<see cref="Reject"/> the loader targets the OBSERVED id.</summary>
    public enum PeriodStep { Send, Approve, Reject }

    /// <summary>Why a plan carries zero steps (so the loader can count the two no-op kinds apart).</summary>
    public enum PeriodSkipReason
    {
        /// <summary>The plan has steps — not a skip.</summary>
        NotSkipped,

        /// <summary>Outcome "NONE": the month never enters the send stage, in ANY observed state.</summary>
        NoneOutcome,

        /// <summary>The observed status already equals the manifest target — the genuine re-run
        /// no-op, counted in <c>LoadResult.PeriodsAlreadyInTargetState</c>.</summary>
        AlreadyInTargetState,
    }

    /// <summary>The plan for one activity row: the ordered steps to execute, or a reasoned skip.</summary>
    public sealed record PeriodPlan(IReadOnlyList<PeriodStep> Steps, PeriodSkipReason Skip)
    {
        public static PeriodPlan Skipped(PeriodSkipReason reason) =>
            new(Array.Empty<PeriodStep>(), reason);
    }

    /// <summary>
    /// Manifest outcome ⇒ the <c>approval_periods.status</c> the row must END in. Null ⇒ no row at
    /// all. THE single source of the outcome→status mapping — the loader plans toward it and the
    /// verifier (<c>DemoVerifier.VerifyPeriodStatusCountsAsync</c>) checks the database against it.
    ///
    /// <para>(Moved here from DemoVerifier in S128 so the two consumers cannot drift.)
    /// <c>"SUBMITTED"</c> is the PRE-S127 spelling and maps to EMPLOYEE_APPROVED, because that is
    /// where <c>POST /api/approval/send</c> actually leaves the row — an old manifest on disk still
    /// describes the world truthfully. An UNKNOWN outcome throws rather than defaulting to "no
    /// expectation": a silent default is how a whole class of months would drop out of the check
    /// (and, since S128, out of the load plan).</para>
    /// </summary>
    public static string? ExpectedPeriodStatus(string outcome) => outcome switch
    {
        "NONE" => null,
        "EMPLOYEE_APPROVED" => "EMPLOYEE_APPROVED",
        "SUBMITTED" => "EMPLOYEE_APPROVED",
        "APPROVED" => "APPROVED",
        "REJECTED" => "REJECTED",
        _ => throw new InvalidOperationException(
            $"Unknown manifest periodOutcome '{outcome}'. The status-count check cannot be derived, " +
            "and defaulting it to 'no expectation' would silently drop those months from verification."),
    };

    /// <summary>
    /// Plans the period actions for one activity row given what the by-month probe observed for
    /// that (employee, year, month) — <c>null</c> = no row exists (the fresh-load case, and also
    /// the probe-failed fallback, where the 409 branch remains the safety net).
    ///
    /// <para>Decision table:
    /// outcome NONE → zero steps always (never enters the send stage, as before S128);
    /// observed == target → ZERO steps (<see cref="PeriodSkipReason.AlreadyInTargetState"/>) —
    /// including target REJECTED, the S127 FU-C case;
    /// no row → the full sequence (Send, then Approve/Reject per outcome);
    /// observed EMPLOYEE_APPROVED, target APPROVED/REJECTED → the direct manager act ONLY (both
    /// accept EMPLOYEE_APPROVED as source; the probe supplied the period id, so no re-send —
    /// re-sending would 409 and orphan the outcome);
    /// anything else (a sendable source such as DRAFT/SUBMITTED/REJECTED with a DIFFERENT target,
    /// or a locked APPROVED row whose target drifted) → the full sequence, deliberately: sendable
    /// sources re-send legitimately, and a locked row's send 409s into the loader's existing
    /// PeriodsAlreadySent safety net — exactly the pre-S128 behaviour for that row.</para>
    /// </summary>
    public static PeriodPlan PlanPeriodActions(DemoActivity activity, ObservedPeriod? observed)
    {
        var target = ExpectedPeriodStatus(activity.PeriodOutcome);
        if (target is null)
            return PeriodPlan.Skipped(PeriodSkipReason.NoneOutcome);

        if (observed is not null && observed.Status == target)
            return PeriodPlan.Skipped(PeriodSkipReason.AlreadyInTargetState);

        // Resume without a re-send: the row is already sitting in the send's destination state and
        // only the manager act is missing. Approve AND reject both accept EMPLOYEE_APPROVED
        // (ApprovalEndpoints.cs approve/reject allowedSourceStates), and the observed period id
        // makes the act addressable. A re-send here would 409 (EMPLOYEE_APPROVED is not a send
        // source) and the outcome would never be applied.
        if (observed is { Status: "EMPLOYEE_APPROVED" })
        {
            return target switch
            {
                "APPROVED" => new PeriodPlan(new[] { PeriodStep.Approve }, PeriodSkipReason.NotSkipped),
                "REJECTED" => new PeriodPlan(new[] { PeriodStep.Reject }, PeriodSkipReason.NotSkipped),
                _ => PeriodPlan.Skipped(PeriodSkipReason.AlreadyInTargetState), // unreachable: == handled above
            };
        }

        // Fresh load (no row), a sendable source in the wrong state (DRAFT/SUBMITTED/REJECTED with
        // a different target — the send legitimately moves it), or a locked APPROVED row whose
        // manifest target drifted (the send 409s into the PeriodsAlreadySent safety net).
        var steps = new List<PeriodStep> { PeriodStep.Send };
        switch (target)
        {
            case "APPROVED":
                steps.Add(PeriodStep.Approve);
                break;
            case "REJECTED":
                steps.Add(PeriodStep.Reject);
                break;
            // "EMPLOYEE_APPROVED" → the send itself lands there; no further act.
        }
        return new PeriodPlan(steps, PeriodSkipReason.NotSkipped);
    }
}
