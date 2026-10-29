using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

/// <summary>
/// S147 / TASK-14703-T (QUAL-230 T5a; R-30) — the pure departed-subject predicate, derived from the
/// SPEC (refinement AC-230-16; owner OQ-2: "the leaver's own access ends the day after the last
/// day"), never from the code.
///
/// <para><b>Kind N</b> for every fact: each names <see cref="DepartedSubject"/>, a type this sprint
/// creates. RED is proven by NAMED MUTATION at the wave gate (never by reverting):
/// <b>M-11</b> changes <c>d &lt; today</c> to <c>d &lt;= today</c> (the last day then counts as departed);
/// <b>M-12</b> drops the <c>!isActive</c> arm (a deactivated subject is then not departed);
/// <b>M-14</b> (G1) makes <c>IsSelf</c> case-insensitive / treats empty ids as equal.</para>
/// </summary>
public class DepartedSubjectTests
{
    private static readonly DateOnly Today = new(2026, 3, 16);

    private static User Subject(bool isActive, DateOnly? end) => new()
    {
        UserId = "u1", Username = "u1", PasswordHash = "x", DisplayName = "U1",
        PrimaryOrgId = "ORG1", AgreementCode = "AC", OkVersion = "OK24",
        IsActive = isActive, EmploymentEndDate = end,
    };

    /// <summary>Kind N. Red conditions: any mutation that makes an open-ended employee departed.</summary>
    [Fact]
    public void ActiveNoEndDate_IsNotDeparted()
        => Assert.False(DepartedSubject.IsDeparted(Subject(true, null), Today));

    /// <summary>Kind N; mutation M-11 (&lt; to &lt;=). Red condition: the last day itself reads as departed.
    /// The last day is still employed (ADR-040 D1; OQ-2: access ends the day AFTER).</summary>
    [Fact]
    public void ActiveEndDateToday_IsNotDeparted_LastDayIsStillEmployed()
        => Assert.False(DepartedSubject.IsDeparted(Subject(true, Today), Today));

    /// <summary>Kind N. Red condition: end date yesterday not reported departed (e.g. a mutation
    /// comparing with the wrong direction or dropping the end-date arm).</summary>
    [Fact]
    public void ActiveEndDateYesterday_IsDeparted()
        => Assert.True(DepartedSubject.IsDeparted(Subject(true, Today.AddDays(-1)), Today));

    /// <summary>Kind N; mutation M-12 (drop the deactivated arm). Red condition: a deactivated
    /// subject with no end date reads as not departed.</summary>
    [Fact]
    public void DeactivatedNoEndDate_IsDeparted()
        => Assert.True(DepartedSubject.IsDeparted(Subject(false, null), Today));

    /// <summary>Kind N; M-12. Deactivated with a FUTURE end date is still departed (the flag governs).</summary>
    [Fact]
    public void DeactivatedFutureEndDate_IsDeparted()
        => Assert.True(DepartedSubject.IsDeparted(Subject(false, Today.AddDays(5)), Today));

    /// <summary>Kind N. The column overload agrees with the User overload on the four spec cases.</summary>
    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, -1, true)]
    [InlineData(false, null, true)]
    public void ColumnOverload_AgreesWithSpec(bool active, int? endOffset, bool expected)
    {
        DateOnly? end = endOffset is { } o ? Today.AddDays(o) : null;
        Assert.Equal(expected, DepartedSubject.IsDeparted(active, end, Today));
        Assert.Equal(expected, DepartedSubject.IsDeparted(Subject(active, end), Today));
    }

    /// <summary>Kind N (G1; mutation M-14: Ordinal to OrdinalIgnoreCase). Red condition: ids that
    /// differ only by case are treated as the same person.</summary>
    [Fact]
    public void IsSelf_CaseDifferentIds_DoNotMatch()
        => Assert.False(DepartedSubject.IsSelf("Mette", "mette"));

    /// <summary>Kind N (G1; M-14). Red condition: a null actor id matches (e.g. a null subject).</summary>
    [Fact]
    public void IsSelf_NullActor_NeverMatches()
    {
        Assert.False(DepartedSubject.IsSelf(null, "mette"));
        Assert.False(DepartedSubject.IsSelf(null, null));
    }

    /// <summary>Kind N (G1; M-14 variant: drop the IsNullOrEmpty check). Red condition: two empty ids match.</summary>
    [Fact]
    public void IsSelf_EmptyActor_NeverMatches()
    {
        Assert.False(DepartedSubject.IsSelf("", ""));
        Assert.False(DepartedSubject.IsSelf("", "mette"));
    }

    /// <summary>Kind N (G1). Whitespace is not trimmed: " mette" is another id. Red condition: a trim.</summary>
    [Fact]
    public void IsSelf_WhitespaceIds_DoNotMatchAcrossTrim()
    {
        Assert.False(DepartedSubject.IsSelf(" mette", "mette"));
        Assert.False(DepartedSubject.IsSelf("mette ", "mette"));
    }

    /// <summary>Kind N. The positive: identical ids match.</summary>
    [Fact]
    public void IsSelf_ExactSameId_Matches()
        => Assert.True(DepartedSubject.IsSelf("mette", "mette"));

    /// <summary>Kind N. The reason is non-empty and date-free (ADR-040 D7).</summary>
    [Fact]
    public void DenyReason_IsDateFree()
    {
        Assert.False(string.IsNullOrWhiteSpace(DepartedSubject.DenyReason));
        Assert.DoesNotMatch(@"\d{2,4}[-./]\d{2}", DepartedSubject.DenyReason);
    }
}
