using System.Globalization;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14800 (PIN-1b, R-4) — the canonicaliser that makes PIN-1's golden text can fail.
///
/// <para>
/// <b>Why this exists, in plain language.</b> PIN-1 compares what payroll produced with a recorded text. If the
/// text-maker silently left out a field (the amount, say), two payslips that differ in that field would give the same
/// text, and the pin could never notice a pay change in it. These facts break that on purpose: two lines that differ in
/// only one field must give different texts, and each text must be exactly the one the format says (the format is
/// written out in <see cref="PayLineCanonicaliser"/>'s comment, and the expected strings below are written from it by
/// hand, not produced by the code).
/// </para>
///
/// <para>
/// <b>Kind: N</b> (names a type this sprint creates). Not Docker; runs in the local gate (BC-P13).
/// </para>
/// </summary>
public sealed class PayLineCanonicaliserTests
{
    private static PayrollExportLine Line(decimal amount = 1000.50m, DateOnly? periodEnd = null) => new()
    {
        EmployeeId = "E1",
        WageType = "SLS_0110",
        Hours = 7.40m,
        Amount = amount,
        PeriodStart = new DateOnly(2026, 6, 1),
        PeriodEnd = periodEnd ?? new DateOnly(2026, 6, 30),
        OkVersion = "OK26",
        SourceRuleId = "R1",
        SourceTimeType = "NORMAL_HOURS",
        ManifestId = Guid.NewGuid(),
    };

    private static string Text(PayrollExportLine line) =>
        PayLineCanonicaliser.Canonicalise(Array.Empty<string>(), new[] { line }, Array.Empty<PayRecord>());

    /// <summary>
    /// Two lines that differ only in <c>Amount</c> give different texts, each exactly the format's text.
    ///
    /// <para><b>Kind: N.</b> <b>Red conditions:</b> mutation M-1a, the canonicaliser drops <c>Amount</c> from the line
    /// row: both exact-text assertions fail (the rows lose their <c>|1000.5</c> / <c>|2000.5</c> column) and the two
    /// texts become equal.</para>
    /// </summary>
    [Fact]
    public void LinesDifferingOnlyInAmount_GiveDifferentText()
    {
        var low = Text(Line(amount: 1000.50m));
        var high = Text(Line(amount: 2000.50m));

        Assert.Equal("line|E1|2026-06-01|2026-06-30|SLS_0110|R1|NORMAL_HOURS|7.4|1000.5|OK26\n", low);
        Assert.Equal("line|E1|2026-06-01|2026-06-30|SLS_0110|R1|NORMAL_HOURS|7.4|2000.5|OK26\n", high);
        Assert.NotEqual(low, high);
    }

    /// <summary>
    /// The header's bytes do not depend on the machine's culture or calendar.
    ///
    /// <para><b>Kind: R</b> (today's behaviour under the invariant culture is kept). <b>Red conditions:</b> a
    /// culture-dependent format in the header: under <c>th-TH</c> (Buddhist calendar, Thai digits possible) the reference
    /// date or a number prints differently from the invariant text.</para>
    /// </summary>
    [Fact]
    public void Header_IsCultureInvariant()
    {
        string[] Build() => PayLineCanonicaliser.Header(
            "smoke", new DateOnly(2026, 1, 15), 2025, 12, 47, 3, 3, 3, 1234);

        var original = CultureInfo.CurrentCulture;
        string[] invariant, thai;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariant = Build();
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            thai = Build();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Equal("world: scale=smoke referenceDate=2026-01-15 activityMonth=2025-12 seed=47", invariant[1]);
        Assert.Equal(invariant, thai);
    }

    /// <summary>
    /// The text depends only on the lines' values, not on the order they arrive in. The set holds two lines identical
    /// except <c>PeriodEnd</c>, and two identical except a null versus an empty <c>SourceRuleId</c> (which print
    /// differently).
    ///
    /// <para><b>Kind: R.</b> The tie-breakers (the period end, then the printed line) are appended after the brief's
    /// sort keys, so lines those keys already order keep their order. <b>Red conditions:</b> input order leaking into the
    /// output: with no tie-breaker the forward and reversed inputs print the tied pairs in different orders.</para>
    /// </summary>
    [Fact]
    public void ReversedInput_GivesIdenticalText()
    {
        PayrollExportLine L(DateOnly end, string? rule) => new()
        {
            EmployeeId = "E1", WageType = "SLS_0110", Hours = 7.4m, Amount = 100m,
            PeriodStart = new DateOnly(2026, 6, 1), PeriodEnd = end, OkVersion = "OK26",
            SourceRuleId = rule, SourceTimeType = "NORMAL_HOURS",
        };
        var set = new[]
        {
            L(new DateOnly(2026, 6, 30), "R1"),
            L(new DateOnly(2026, 6, 29), "R1"),
            L(new DateOnly(2026, 6, 30), null),
            L(new DateOnly(2026, 6, 30), ""),
        };

        var forward = PayLineCanonicaliser.Canonicalise(Array.Empty<string>(), set, Array.Empty<PayRecord>());
        var reversed = PayLineCanonicaliser.Canonicalise(Array.Empty<string>(), set.Reverse(), Array.Empty<PayRecord>());

        Assert.Equal(
            "line|E1|2026-06-01|2026-06-30|SLS_0110||NORMAL_HOURS|7.4|100|OK26\n" +
            "line|E1|2026-06-01|2026-06-30|SLS_0110|~|NORMAL_HOURS|7.4|100|OK26\n" +
            "line|E1|2026-06-01|2026-06-29|SLS_0110|R1|NORMAL_HOURS|7.4|100|OK26\n" +
            "line|E1|2026-06-01|2026-06-30|SLS_0110|R1|NORMAL_HOURS|7.4|100|OK26\n",
            forward);
        Assert.Equal(forward, reversed);
    }

    /// <summary>
    /// Two lines that differ only in <c>PeriodEnd</c> give different texts, each exactly the format's text.
    ///
    /// <para><b>Kind: N.</b> <b>Red conditions:</b> mutation M-1b, the canonicaliser drops <c>PeriodEnd</c> from the
    /// line row: both exact-text assertions fail (the row loses its end-date column) and the two texts become
    /// equal.</para>
    /// </summary>
    [Fact]
    public void LinesDifferingOnlyInPeriodEnd_GiveDifferentText()
    {
        var month = Text(Line(periodEnd: new DateOnly(2026, 6, 30)));
        var shorter = Text(Line(periodEnd: new DateOnly(2026, 6, 29)));

        Assert.Equal("line|E1|2026-06-01|2026-06-30|SLS_0110|R1|NORMAL_HOURS|7.4|1000.5|OK26\n", month);
        Assert.Equal("line|E1|2026-06-01|2026-06-29|SLS_0110|R1|NORMAL_HOURS|7.4|1000.5|OK26\n", shorter);
        Assert.NotEqual(month, shorter);
    }
}
