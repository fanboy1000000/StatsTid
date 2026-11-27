using System.Text.Json;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Serialization.Goldens;

/// <summary>
/// G-4 (TASK-14900, ADR-045) — the month's frozen calculation inputs as they are stored.
///
/// <para>
/// <b>Plain language.</b> When a month is calculated, the inputs it was paid on (the pieces of employment, the
/// agreement configuration sent for each piece, the ids of the hour rows, the two digests) are stored as JSON in
/// <c>segment_manifests</c>, and a replay reads them back. <c>G4-planinputs.golden</c> holds the exact text of one
/// fully populated <see cref="PlanInputs"/> (every optional member set, collections of two) under the calculation
/// service's JSON options, and the text produced by reading that literal and writing it again.
/// </para>
///
/// <para>
/// <b>Kind: R.</b> Red conditions: a change to the options (naming, enum form, number or date form) fails the
/// serialize literal; a read that no longer restores every member fails the replay literal. Proved by the mutation
/// M-4 (the string-enum converter dropped from the options) at the wave gate.
/// </para>
/// </summary>
public sealed class G4PlanInputsGoldenTests
{
    private const string File = "G4-planinputs.golden";

    private static PlanInputs Instance() => GoldenInstanceFactory.Create<PlanInputs>(alternateNulls: false);

    [Fact]
    public void The_fully_populated_plan_inputs_serialize_to_their_literal()
    {
        var json = JsonSerializer.Serialize(Instance(), PeriodCalculationService.JsonOptions);
        GoldenFile.AssertMatches(File, new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["1.serialize"] = json,
            ["2.reserialize"] = JsonSerializer.Serialize(
                JsonSerializer.Deserialize<PlanInputs>(json, PeriodCalculationService.JsonOptions)!,
                PeriodCalculationService.JsonOptions),
        });
    }

    [Fact]
    public void Reading_the_literal_and_writing_it_again_gives_the_literal()
    {
        var golden = GoldenFile.Load(File);
        var read = JsonSerializer.Deserialize<PlanInputs>(golden["1.serialize"], PeriodCalculationService.JsonOptions)!;
        Assert.Equal(golden["1.serialize"], JsonSerializer.Serialize(read, PeriodCalculationService.JsonOptions));
        Assert.Equal(golden["1.serialize"], golden["2.reserialize"]);
    }
}
