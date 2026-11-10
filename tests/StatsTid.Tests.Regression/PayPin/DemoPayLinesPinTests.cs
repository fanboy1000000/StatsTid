namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14800 (PIN-1) — the pay-unchanged pin: every demo payslip comes out the same before and after S148.
///
/// <para>
/// <b>What it does, in plain language.</b> Each fact builds one demo world with the project's own generator (a fixed
/// seed, a fixed reference date), loads it through the same Skema-and-approval calls the demo loader makes, sends every
/// month the loader approved to payroll (the real Rule Engine behind it), and writes what payroll produced as one fixed
/// text (<see cref="PayLineCanonicaliser"/>). That text is compared, byte for byte, with the golden file committed from
/// the base code's own run. An hour or a krone that moves on any demo payslip turns the fact red. S149 re-pins it.
/// </para>
///
/// <para>
/// <b>It cannot pass on an empty world</b> (R-2, A-7, NR-14): before the golden step each fact fails, naming the
/// employee-month and what happened, when an APPROVED month is refused, when fewer months were calculated than the
/// loader approved, when the world holds fewer than three APPROVED months, when an APPROVED month yields no pay line, or
/// when the loader's approved count differs from the generator's plan or it reports an outcome failure (FAIL-006).
/// </para>
///
/// <para>
/// <b>Capture.</b> While the golden file is absent the fact FAILS, on purpose, with the words <c>golden capture</c> and
/// the whole text between markers in its message; CI keeps it in <c>test-results</c> and the Orchestrator commits it
/// unchanged. When the file exists the fact compares and never writes anything.
/// </para>
///
/// <para>
/// <b>Kind: R</b> (a regression pin on existing behaviour, captured on base code), Docker-gated
/// (<c>[Trait("Category","Docker")]</c>): <b>CI-pending</b>, never locally green. <b>Red conditions</b> (argued, not run
/// locally): at the capture run each fact fails at <c>golden capture</c> by design; afterwards each fails on any change
/// to a demo payslip line (hours, amount, wage type, period, rule, time type, OK version), to an export record's opening
/// or closing balance or provenance, or to the number of approved months; M-1a/M-1b-style canonicaliser breakage is
/// covered by PIN-1b.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DemoPayLinesPinTests
{
    [Fact]
    public async Task World20260115_December2025_EqualsGolden() =>
        DemoWorldPayRun.AssertEqualsGolden(PinnedWorld.December2025, await DemoWorldPayRun.RunAsync(PinnedWorld.December2025));

    [Fact]
    public async Task World20260715_June2026_EqualsGolden() =>
        DemoWorldPayRun.AssertEqualsGolden(PinnedWorld.June2026, await DemoWorldPayRun.RunAsync(PinnedWorld.June2026));
}
