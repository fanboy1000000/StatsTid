namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14800 (PIN-1; NR-14, B-1) — the two demo worlds the pay-unchanged pin measures: the generator's scale,
/// reference date and the pinned seed of each. Both reference dates give an activity month that opens on a Monday, so
/// neither month needs a previous month's approval before payroll calculates it.
///
/// <para>
/// The seed of a world is the FIRST seed from 43 to 142 at which the smoke world's manifest holds at least
/// <see cref="MinApprovedMonths"/> activity months with <c>PeriodOutcome == "APPROVED"</c> (a smoke world at the default
/// seed holds about two, too few for the pin to measure anything). <c>DemoPinSeedSelectionTests</c> proves each pinned
/// seed is that first one.
/// </para>
/// </summary>
internal sealed record PinnedWorld(string File, DateOnly ReferenceDate, int Seed)
{
    internal const string Scale = "smoke";
    internal const int MinApprovedMonths = 3;
    internal const int FirstSeed = 43;
    internal const int LastSeed = 142;

    /// <summary>December 2025 (OK24; opens on Monday 1 December; holds Christmas).</summary>
    internal static readonly PinnedWorld December2025 = new("demo-2026-01-15.golden.txt", new DateOnly(2026, 1, 15), 47);

    /// <summary>June 2026 (OK26; opens on Monday 1 June; holds Grundlovsdag, Friday 5 June).</summary>
    internal static readonly PinnedWorld June2026 = new("demo-2026-07-15.golden.txt", new DateOnly(2026, 7, 15), 45);

    /// <summary>
    /// Seeds a world may not take although the manifest qualifies them, each with the refusal kind and the CI run that
    /// showed it (a month payroll refuses at the base is a finding, never a golden). Empty today; the one place such a
    /// change would land.
    /// </summary>
    internal static readonly IReadOnlySet<int> ExcludedSeeds = new HashSet<int>();
}
