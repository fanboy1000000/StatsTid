using System.Globalization;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Models;

/// <summary>
/// S138 / TASK-13806 — THE fencepost matrix for <see cref="EmploymentWindow"/>'s date
/// arithmetic (<see cref="EmploymentWindow.Overlaps"/>, <see cref="EmploymentWindow.ClipTo"/>,
/// <see cref="EmploymentWindow.FirstEmployedDayWithin(DateOnly, DateOnly)"/>,
/// <see cref="EmploymentWindow.FirstNotEmployedDay"/>).
///
/// <para>
/// Plain-language: an employment window is "[first employed day, LAST employed day]" — the end
/// date is inclusive (ADR-040 D1), and a missing side means "no limit on that side" (D2). Three
/// production sites used to compare these dates against a range by hand (the resolver's range
/// filter, the planner's segment typing, the compliance check's "first employed day of the
/// month"), and each hand-rolled copy was a fresh chance to be off by one day — which at the
/// payroll boundary means a paid day silently dropped or added. The helpers pinned here are now
/// the ONE implementation those sites call, so this matrix is the single place the fenceposts
/// are proven. The S137 payroll/segmentation/compliance pins stay green unchanged — that is the
/// byte-identity proof for the call sites; THIS file proves the helper itself, edge by edge.
/// </para>
/// </summary>
public sealed class EmploymentWindowTests
{
    // The range under test is March 2026 unless a row says otherwise.
    private const string From = "2026-03-01";
    private const string To = "2026-03-31";

    // ═════════════════════════════════════════════════════════════════════
    // 1. The matrix: (window) × [from, to] → overlaps? clipped window?
    //    Every row asserts Overlaps, ClipTo and FirstEmployedDayWithin
    //    together, so the three helpers cannot disagree on an edge.
    // ═════════════════════════════════════════════════════════════════════

    [Theory]
    // ── unbounded sides (D2) ──
    [InlineData(null, null, From, To, true, From, To)]                       // both null covers everything
    [InlineData("2025-01-01", null, From, To, true, From, To)]               // hired before, open-ended
    [InlineData(null, "2027-01-01", From, To, true, From, To)]               // leaves after, unbounded past
    // ── the start edge (day Start IS employed) ──
    [InlineData("2026-03-15", null, From, To, true, "2026-03-15", To)]       // mid-range hire
    [InlineData("2026-03-31", null, From, To, true, To, To)]                 // hire ON the last range day → one day
    [InlineData("2026-04-01", null, From, To, false, null, null)]            // hire the day AFTER → nothing
    // ── the end edge (day End IS employed — D1 inclusive) ──
    [InlineData(null, "2026-03-15", From, To, true, From, "2026-03-15")]     // mid-range leaver
    [InlineData(null, "2026-03-01", From, To, true, From, From)]             // leaves ON the first range day → one day
    [InlineData(null, "2026-02-28", From, To, false, null, null)]            // left the day BEFORE → nothing
    // ── closed spells ──
    [InlineData("2026-03-05", "2026-03-10", From, To, true, "2026-03-05", "2026-03-10")] // inside
    [InlineData("2026-03-12", "2026-03-12", From, To, true, "2026-03-12", "2026-03-12")] // single day inside
    [InlineData("2026-03-01", "2026-03-31", From, To, true, From, To)]                   // exactly the range
    [InlineData("2025-01-01", "2026-03-10", From, To, true, From, "2026-03-10")]         // straddles the start
    [InlineData("2026-03-20", "2027-01-01", From, To, true, "2026-03-20", To)]           // straddles the end
    [InlineData("2025-01-01", "2026-02-28", From, To, false, null, null)]                // entirely before
    [InlineData("2026-05-10", null, From, To, false, null, null)]                        // entirely after
    // ── a single-day range (from == to) — the tightest fenceposts ──
    [InlineData(null, "2026-03-15", "2026-03-15", "2026-03-15", true, "2026-03-15", "2026-03-15")]  // ends ON the day
    [InlineData("2026-03-15", null, "2026-03-15", "2026-03-15", true, "2026-03-15", "2026-03-15")]  // starts ON the day
    [InlineData("2026-03-16", null, "2026-03-15", "2026-03-15", false, null, null)]               // starts the day after
    [InlineData(null, "2026-03-14", "2026-03-15", "2026-03-15", false, null, null)]               // ended the day before
    public void Matrix_Overlaps_ClipTo_FirstEmployedDay_AgreeOnEveryEdge(
        string? start, string? end, string from, string to,
        bool expectOverlap, string? expectClipStart, string? expectClipEnd)
    {
        var window = new EmploymentWindow(Date(start), Date(end));
        var rangeFrom = Date(from)!.Value;
        var rangeTo = Date(to)!.Value;

        Assert.Equal(expectOverlap, window.Overlaps(rangeFrom, rangeTo));

        var clipped = window.ClipTo(rangeFrom, rangeTo);
        if (!expectOverlap)
        {
            Assert.Null(clipped);
            Assert.Null(window.FirstEmployedDayWithin(rangeFrom, rangeTo));
            return;
        }

        Assert.NotNull(clipped);
        // A non-null clip is CLOSED: both sides concrete, inside the range, in order.
        Assert.NotNull(clipped!.Start);
        Assert.NotNull(clipped.End);
        Assert.Equal(Date(expectClipStart), clipped.Start);
        Assert.Equal(Date(expectClipEnd), clipped.End);
        Assert.True(clipped.Start!.Value >= rangeFrom);
        Assert.True(clipped.End!.Value <= rangeTo);
        Assert.True(clipped.Start.Value <= clipped.End.Value);

        // FirstEmployedDayWithin IS the clipped start.
        Assert.Equal(clipped.Start, window.FirstEmployedDayWithin(rangeFrom, rangeTo));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. FirstNotEmployedDay — the EmploymentEnded boundary date (End + 1)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>THE leaver fencepost in one line: a last employed day of 03-15 means the first
    /// NOT-employed day — and the segment boundary — is 03-16, never 03-15.</summary>
    [Fact]
    public void FirstNotEmployedDay_IsEndPlusOne()
    {
        var window = new EmploymentWindow(null, new DateOnly(2026, 3, 15));
        Assert.Equal(new DateOnly(2026, 3, 16), window.FirstNotEmployedDay);
    }

    [Fact]
    public void FirstNotEmployedDay_RollsOverTheMonthEdge()
    {
        var window = new EmploymentWindow(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31));
        Assert.Equal(new DateOnly(2026, 4, 1), window.FirstNotEmployedDay);
    }

    [Fact]
    public void FirstNotEmployedDay_OpenEnded_IsNull()
    {
        Assert.Null(new EmploymentWindow(null, null).FirstNotEmployedDay);
        Assert.Null(new EmploymentWindow(new DateOnly(2026, 1, 1), null).FirstNotEmployedDay);
    }

    /// <summary>A sentinel 9999-12-31 end has no day after it: the answer is "open-ended"
    /// (null), not an ArgumentOutOfRangeException from AddDays — the guard PCS's hydration
    /// loop carries by hand today.</summary>
    [Fact]
    public void FirstNotEmployedDay_MaxValueEnd_IsNull_NotAThrow()
    {
        var window = new EmploymentWindow(null, DateOnly.MaxValue);
        Assert.Null(window.FirstNotEmployedDay);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. Fail-loud on an inverted range — a quiet false/null would read as the
    //    legitimate answer "no employed day" and hide a caller's arithmetic bug
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void InvertedRange_Throws_OnEveryHelper()
    {
        var window = new EmploymentWindow(null, null);
        var from = new DateOnly(2026, 3, 31);
        var to = new DateOnly(2026, 3, 1);

        Assert.Throws<ArgumentException>(() => window.Overlaps(from, to));
        Assert.Throws<ArgumentException>(() => window.ClipTo(from, to));
        Assert.Throws<ArgumentException>(() => window.FirstEmployedDayWithin(from, to));
        Assert.Throws<ArgumentException>(() =>
            EmploymentWindow.FirstEmployedDayWithin(new[] { window }, from, to));
    }

    [Fact]
    public void Union_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EmploymentWindow.FirstEmployedDayWithin(null!, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31)));
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static DateOnly? Date(string? iso)
        => iso is null ? null : DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
