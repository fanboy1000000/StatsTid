using StatsTid.RuleEngine.Api.Rules;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// The evaluation-model marker a replay must match (<c>replay-evaluation-model-changed</c>, ADR-016 D10).
/// Re-homed here from the retired fingerprint tests (S148 TASK-14830: the fingerprint check is gone, the
/// marker stays). Kind R: a regression pin on an existing constant.
/// </summary>
public sealed class EvaluationModelMarkerTests
{
    [Fact]
    public void Timeline_EvaluationModel_IsWeeklyTimelineV3()
    {
        // The marker a replay must match (`replay-evaluation-model-changed`, refinement A7). Changing
        // the arithmetic means bumping this string on purpose; changing the string by accident would
        // make every stored manifest read as "arithmetic changed".
        //
        // S147 T3 (QUAL-231): v1 -> v2 (test renamed from ..._IsWeeklyTimelineV1). Kind R (a regression pin, re-pinned).
        // Why v2: a stored SICK_DAY and the stored child's-sick-day codes are now credited toward the norm
        // (Statens arbejdstidsaftale § 7 nr. 2; AbsenceClassification), so the same hours give a different
        // flex closing than under v1, and a manifest recorded under v1 must be refused on replay, never
        // silently re-read under the new rule (ADR-016 D10).
        //
        // S148 S0-2 (R-24): v2 -> v3 (renamed from ..._IsWeeklyTimelineV2). Kind F ("v3" fails on the base, which
        // returns v2). Why v3: the child's call-home day (CHILD_SICK_CALL_HOME) is a credited absence code, so a
        // period holding call-home hours closes differently than v2 would close it; a manifest recorded under
        // v2 is refused on replay (ADR-016 D10).
        Assert.Equal("weekly-timeline-v3", WeeklyTimelineCore.EvaluationModel); // S148 S0-2 (R-24): v2 -> v3
    }
}
