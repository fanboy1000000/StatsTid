using System.Text.RegularExpressions;
using StatsTid.Infrastructure;
using StatsTid.Tools.DemoSeed.Generation;

namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14839-T (AC-C12c) — every demo person's overtime regime is one the product's own rules allow for that
/// person's agreement.
///
/// <para><b>Why, in plain language.</b> The demo writes an HR-facts row for every person, with a regime chosen by a mapping
/// inside the demo tool. The product decides which regimes an agreement allows
/// (<see cref="EmployeeHrFactsRules.AllowedOvertimeRegimes"/>) and treats a regime outside that set as "not recorded". This
/// fact runs each emitted row through the product's rule, not through a copy, so the demo goes red if either side changes
/// under the other. The DemoSeed test project cannot reach that rule (it references only the tool), which is why this lives
/// here.</para>
///
/// <para>It reads the rows from <see cref="SqlEmitter.Emit"/>, the file the demo stack loads: PIN-1's two worlds (seed 47 at
/// 15 January 2026, seed 45 at 15 July 2026, smoke) and the full world at 15 June 2026. Each row's agreement is the
/// user's own (looked up by the row's employee id in the dataset); <c>demo_admin</c>, which is not in the dataset, is
/// <c>AC</c> (a literal).</para>
///
/// <para><b>Kind: F</b> (Regression, non-Docker). <b>Product code between the readings:</b> the generator, the emitter and
/// <see cref="EmployeeHrFactsRules"/>. <b>Red conditions:</b> on the base of TASK-14839 no HR-facts section exists, so the
/// section-presence assertion fails (the at-least-one-row assertion after it is not reached); afterwards a regime
/// outside the user's agreement's set, an employee id that is not a
/// user of the dataset, or a user without a row fails.</para>
/// </summary>
public sealed class DemoHrFactsAllowedSetTests
{
    private static readonly Regex Row = new(@"^\('(?<id>[^']+)', (?<regime>NULL|'[A-Z]+'), (TRUE|FALSE), DATE '[0-9-]+'\)$");

    [Fact]
    public void EveryDemoRegime_IsAllowedForItsAgreement()
    {
        var worlds = new (string Scale, int Seed, DateOnly Reference)[]
        {
            (PinnedWorld.Scale, PinnedWorld.December2025.Seed, PinnedWorld.December2025.ReferenceDate),
            (PinnedWorld.Scale, PinnedWorld.June2026.Seed, PinnedWorld.June2026.ReferenceDate),
            ("full", 42, new DateOnly(2026, 6, 15)),
        };

        foreach (var (scale, seed, reference) in worlds)
        {
            var dataset = new DemoGenerator(scale, seed, reference).Generate();
            var agreementOf = dataset.Users.ToDictionary(u => u.UserId, u => u.AgreementCode, StringComparer.Ordinal);
            agreementOf["demo_admin"] = "AC";

            var lines = SqlEmitter.Emit(dataset).Replace("\r\n", "\n").Split('\n').ToList();
            var insert = lines.FindIndex(l => l.StartsWith("INSERT INTO employee_hr_facts ", StringComparison.Ordinal));
            var world = $"{scale} seed {seed} at {reference:yyyy-MM-dd}";
            Assert.True(insert >= 0, $"{world}: the emitted SQL carries no employee_hr_facts section");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var checkedNonNull = 0;
            for (var i = insert + 1; i < lines.Count && lines[i] != "ON CONFLICT DO NOTHING;"; i++)
            {
                var m = Row.Match(lines[i].Trim().TrimEnd(','));
                Assert.True(m.Success, $"{world}: unparseable HR-facts row '{lines[i]}'");

                var id = m.Groups["id"].Value;
                Assert.True(agreementOf.TryGetValue(id, out var agreement), $"{world}: HR-facts row for '{id}', who is not a user of the file");
                Assert.True(seen.Add(id), $"{world}: two HR-facts rows for '{id}'");

                var regime = m.Groups["regime"].Value;
                if (regime == "NULL") continue;
                regime = regime.Trim('\'');
                checkedNonNull++;
                Assert.True(EmployeeHrFactsRules.AllowedOvertimeRegimes(agreement).Contains(regime, StringComparer.Ordinal),
                    $"{world}: '{id}' ({agreement}) has regime {regime}, which the product's rule does not allow for that agreement");
            }

            Assert.True(seen.Count > 0, $"{world}: the HR-facts section holds no row");
            Assert.True(checkedNonNull > 0, $"{world}: no HR-facts row carries a regime");
            Assert.Equal(dataset.Users.Count + 1, seen.Count); // every user and demo_admin has a row
        }
    }
}
