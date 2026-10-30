using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S147 TASK-14706-T (QUAL-234 interim, refinement T7) - when a period is split into segments (an
/// OK-version or agreement change inside the month), each segment's rest check names the days it
/// could not measure. The merger must carry ALL of them to the one result the screens read: the
/// union of the segments' lists, each date once, in date order.
///
/// <para>
/// This is a NEW file: the merger had no unit test file before S147 (the brief's "existing file,
/// if one exists" did not apply). Expected values are the contract text ("the union, ordered by
/// date" at both build sites), not observed output. The union facts are kind <b>N</b> (they name
/// the member this sprint creates) and fail under the one named mutation "keep only the first
/// segment's list", because each asserts a date that only a later segment holds; the
/// pass-through fact is kind B.
/// </para>
/// </summary>
public class ComplianceCheckResultMergerNotCheckedTests
{
    private const string RuleId = "REST_PERIOD_CHECK";
    private const string Employee = "EMP001";

    private static ComplianceCheckResult Segment(params DateOnly[] notChecked) => new()
    {
        RuleId = RuleId,
        EmployeeId = Employee,
        Success = true,
        Violations = Array.Empty<ComplianceViolation>(),
        Warnings = Array.Empty<ComplianceViolation>(),
        RestNotCheckedDays = notChecked,
    };

    private static DateOnly D(int day) => new(2026, 3, day);

    /// <summary>
    /// Kind <b>N</b>. UnionDedupe (the default for compliance). Segment A holds the LATER dates
    /// [16, 20], segment B the earlier [2, 16]: the union is [2, 16, 20] - 16 once, and ordered by
    /// date, not by segment. Red conditions: under "keep only the first segment's list" the actual
    /// is [16, 20] and the exact-sequence assertion fails.
    /// </summary>
    [Fact]
    public void UnionDedupe_UnionsTheSegmentLists_EachDateOnce_InDateOrder()
    {
        var merged = ComplianceCheckResultMerger.Apply(
            new[] { Segment(D(16), D(20)), Segment(D(2), D(16)) }, MergeStrategy.UnionDedupe);

        Assert.Equal(new[] { D(2), D(16), D(20) }, merged.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. Concatenate (the other build site): the member is a set of calendar days, so
    /// it is unioned, deduplicated and sorted here too, not concatenated. Same inputs and the same
    /// exact expectation; red under the same mutation.
    /// </summary>
    [Fact]
    public void Concatenate_UnionsTheSegmentLists_EachDateOnce_InDateOrder()
    {
        var merged = ComplianceCheckResultMerger.Apply(
            new[] { Segment(D(16), D(20)), Segment(D(2), D(16)) }, MergeStrategy.Concatenate);

        Assert.Equal(new[] { D(2), D(16), D(20) }, merged.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>N</b>. A segment with nothing unchecked does not hide a later segment's days, and the
    /// order of segments does not matter (only the first segment is empty here). Red conditions:
    /// under "keep only the first segment's list" the actual is empty and the assertion on [D(9)]
    /// fails.
    /// </summary>
    [Fact]
    public void UnionDedupe_EmptyFirstSegment_DoesNotHideALaterSegmentsDays()
    {
        var merged = ComplianceCheckResultMerger.Apply(
            new[] { Segment(), Segment(D(9)) }, MergeStrategy.UnionDedupe);

        Assert.Equal(new[] { D(9) }, merged.RestNotCheckedDays);
    }

    /// <summary>
    /// Kind <b>B</b> - boundary pin, holds on the implementation (RejectIfMultipleSegments hands its
    /// single segment back unchanged, so the member survives). It does not fail under the
    /// first-segment mutation and is not claimed as RED; it would fail if a later change rebuilt that
    /// result without the member ([D(4), D(5)] would become empty).
    /// </summary>
    [Fact]
    public void RejectIfMultipleSegments_SingleSegment_PassesItsListThrough()
    {
        var merged = ComplianceCheckResultMerger.Apply(
            new[] { Segment(D(4), D(5)) }, MergeStrategy.RejectIfMultipleSegments);

        Assert.Equal(new[] { D(4), D(5) }, merged.RestNotCheckedDays);
    }
}
