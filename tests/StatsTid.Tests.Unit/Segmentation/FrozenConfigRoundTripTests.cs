using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Unit.Payroll;
using Xunit.Abstractions;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S148 / TASK-14830-T (ADR-043 D12, ADR-016 D10; the wave-5 batch-C adjudication, § 3, and batch-D ruling 20) — a
/// frozen agreement configuration keeps every one of its members through storage, on both storage paths, and goes to
/// the Rule Engine in the shape the engine reads.
///
/// <para>
/// <b>In plain language.</b> Each month's stored proof now holds a full copy of the agreement rules the month was paid
/// on (41 settings). The real risk is a setting the serializer silently drops: the proof would be written happily and
/// the month would replay on a different value than the one paid. So these facts write a rule set into a proof, read it
/// back, and compare every setting, by reflection, so a setting added later is covered without editing the pin. They do
/// it for a rule set whose every value is unusual (arm 1), for one whose values sit at their CLR defaults (0, false,
/// the enum's zero, null; arm 2: a serializer that skipped default values would lose those), and for the shape the
/// engine is sent (arm 3: its one enumerated setting, <c>normModel</c>, as a number). The size fact prints how big a
/// proof has become.
/// </para>
///
/// <para>
/// <b>Both storage paths.</b> (i) The event path: <see cref="EventSerializer"/> writes and reads the
/// <c>SegmentManifestCreated</c> event. (ii) The projection path: the serialized event's <c>planInputs</c> object is
/// what <c>PostgresSegmentManifestStore</c> stores in <c>plan_inputs_jsonb</c>, and it is read with the payroll
/// service's own <c>JsonOptions</c> (<see cref="PcsJsonOptions.Real"/>).
/// </para>
///
/// <para>
/// <b>Kind N</b> (both facts name <c>PlanInputs.ConfigRuns</c>, which TASK-14830 creates), RED by the local mutation
/// <b>M-51</b> (<c>[JsonIgnore]</c> on one <c>AgreementRuleConfig</c> property, <c>MinimumRestHours</c>), run at the
/// TASK-14830-T gate in a worktree detached at the branch tip. Red conditions: arm 1 fails on
/// <c>MinimumRestHours</c> (the decoded value is the initializer's 11.0, not the frozen one); arm 2 fails on it too; the
/// size fact fails on the same member in its first entry.
/// </para>
/// </summary>
public sealed class FrozenConfigRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public FrozenConfigRoundTripTests(ITestOutputHelper output) => _output = output;

    private static readonly PropertyInfo[] ConfigProperties =
        typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    // ─────────────────────────────────────────────────────────────────────────
    // The round-trip pin
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Arm 1: a config with every public instance property non-default round-trips every member on both paths. Arm 2: a
    /// config whose members with a non-default initializer sit at their CLR default round-trips every member on both
    /// paths. Arm 3: the decoded config, written with the options payroll posts engine requests with, carries
    /// <c>normModel</c> as a JSON number.
    ///
    /// <para>
    /// <b>Arm 2's one exclusion.</b> A member with a non-default initializer is set to its CLR default (0, <c>false</c>,
    /// the enum's zero, <c>null</c>) <b>except a non-nullable reference-type member, which keeps a non-null value</b>.
    /// Today that is only <c>DefaultCompensationModel</c> (a <c>string</c> with the initializer <c>"AFSPADSERING"</c>).
    /// The event serializer omits nulls by design (<c>WhenWritingNull</c>, project-wide), so a null there decodes to the
    /// initializer; its declared type excludes null, and so does the database (<c>init.sql</c>: NOT NULL with a CHECK of
    /// its two values), and the resolution copies the base value, so a null never occurs in a resolved configuration.
    /// The arm's purpose, a guard against a later switch to default-skipping serialization, still holds for every value
    /// type and nullable member. <c>NormModel</c>'s initializer already is the enum's zero, so it is set to zero as an enum
    /// (a default-skipping serializer would drop it too). Every member is still compared, and the number compared equals the
    /// type's public instance property count.
    /// </para>
    /// </summary>
    [Fact]
    public void FrozenConfig_RoundTripsEveryMember()
    {
        var fresh = Fresh();

        // Arm 1.
        var nonDefault = NonDefault();
        AssertEveryMemberDiffersFromFresh(nonDefault, fresh);
        var (viaEvent, viaProjection) = StoreBothWays(nonDefault);
        AssertEveryMemberEqual(nonDefault, viaEvent, "arm 1, event path");
        AssertEveryMemberEqual(nonDefault, viaProjection, "arm 1, projection path");

        // Arm 2.
        var atDefaults = AtClrDefaults(nonDefault, fresh);
        var (defaultsViaEvent, defaultsViaProjection) = StoreBothWays(atDefaults);
        AssertEveryMemberEqual(atDefaults, defaultsViaEvent, "arm 2, event path");
        AssertEveryMemberEqual(atDefaults, defaultsViaProjection, "arm 2, projection path");
        // The arm is not vacuous: at least the value-type members with a non-default initializer really sit at the default,
        // among them the enum (NormModel at zero) and a decimal.
        Assert.Equal(NormModel.WEEKLY_HOURS, atDefaults.NormModel);
        Assert.Equal(0m, atDefaults.MinimumRestHours);
        Assert.Equal(0, atDefaults.EveningStart);

        // Arm 3: normModel goes to the engine as a number (ANNUAL_ACTIVITY is 1), from both decoded configs.
        foreach (var decoded in new[] { viaEvent, viaProjection })
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(decoded, EngineRequestOptions()));
            var normModel = doc.RootElement.GetProperty("normModel");
            Assert.Equal(JsonValueKind.Number, normModel.ValueKind);
            Assert.Equal(1, normModel.GetInt32());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The manifest size
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records, through the test output, the byte length of the <c>planInputs</c> object as <see cref="EventSerializer"/>
    /// writes it, with the configurations and with the member null, for (a) one piece and (b) eight pieces (two segments
    /// and six context days). No threshold: whether the figure is large is the Orchestrator's call (O-18).
    ///
    /// <para>
    /// Two discriminating assertions: the configurations member holds one entry per piece, and each entry round-trips
    /// every member (the same every-member assertion as arm 1, so M-51 trips it too).
    /// </para>
    /// </summary>
    [Fact]
    public void FrozenInputs_ManifestSize_Recorded()
    {
        // The seeded HK/OK26 configuration gives the representative figure; the all-non-default one drives M-51's red.
        foreach (var (configLabel, config) in new[]
                 {
                     ("HK OK26 as compiled", StatsTid.Tests.Shared.EngineConfigRequests.Config("HK", "OK26")),
                     ("every member non-default", NonDefault()),
                 })
        foreach (var (pieceLabel, pieces) in new[] { ("(a) one piece", 1), ("(b) eight pieces", 8) })
        {
            var label = $"{configLabel}, {pieceLabel}";
            var inputs = PlanInputsWith(config, pieces, withConfigRuns: true);
            var without = PlanInputsWith(config, pieces, withConfigRuns: false);

            var withText = PlanInputsText(inputs);
            var withoutText = PlanInputsText(without);
            _output.WriteLine(
                $"manifest size {label}: planInputs with configRuns = {Encoding.UTF8.GetByteCount(withText)} bytes, " +
                $"with the member null = {Encoding.UTF8.GetByteCount(withoutText)} bytes, " +
                $"configRuns adds {Encoding.UTF8.GetByteCount(withText) - Encoding.UTF8.GetByteCount(withoutText)} bytes");

            var decoded = JsonSerializer.Deserialize<PlanInputs>(withText, PcsJsonOptions.Real)!;
            Assert.NotNull(decoded.ConfigRuns);
            Assert.Equal(pieces, decoded.Pieces.Count);
            Assert.Equal(pieces, decoded.ConfigRuns!.Count);
            for (var i = 0; i < pieces; i++)
                AssertEveryMemberEqual(config, decoded.ConfigRuns[i].Config, $"{label}, entry {i}");

            Assert.Null(JsonSerializer.Deserialize<PlanInputs>(withoutText, PcsJsonOptions.Real)!.ConfigRuns);
            Assert.False(JsonNode.Parse(withoutText)!.AsObject().ContainsKey("configRuns"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A config built only by its initializers (required members at their CLR defaults).</summary>
    private static AgreementRuleConfig Fresh() =>
        (AgreementRuleConfig)Activator.CreateInstance(typeof(AgreementRuleConfig), nonPublic: true)!;

    private static AgreementRuleConfig NonDefault()
    {
        var fresh = Fresh();
        var config = Fresh();
        for (var i = 0; i < ConfigProperties.Length; i++)
        {
            var property = ConfigProperties[i];
            var current = property.GetValue(fresh);
            object value = current switch
            {
                decimal d => d + (i + 2) * 0.75m,
                int n => n + i + 2,
                bool b => !b,
                Enum e => Enum.GetValues(e.GetType()).Cast<object>().First(v => !v.Equals(e)),
                null when property.PropertyType == typeof(string) => property.Name + "-frozen",
                string s => s + "-frozen",
                _ => throw new NotSupportedException(
                    $"AgreementRuleConfig.{property.Name} has the type {property.PropertyType}; teach this pin to give it a non-default value."),
            };
            Set(config, property, value);
        }
        return config;
    }

    /// <summary>
    /// <paramref name="nonDefault"/> with every member whose initializer differs from its CLR default (and every enum
    /// member) set to that default, except a non-nullable reference-type member, which keeps its non-null value (see the fact's docstring).
    /// </summary>
    private static AgreementRuleConfig AtClrDefaults(AgreementRuleConfig nonDefault, AgreementRuleConfig fresh)
    {
        var nullability = new NullabilityInfoContext();
        var config = Fresh();
        foreach (var property in ConfigProperties)
        {
            var value = property.GetValue(nonDefault);
            var hasNonDefaultInitializer = !Equals(property.GetValue(fresh), ClrDefault(property.PropertyType));
            var nonNullableReference = !property.PropertyType.IsValueType
                && nullability.Create(property).ReadState == NullabilityState.NotNull;
            if ((hasNonDefaultInitializer || property.PropertyType.IsEnum) && !nonNullableReference)
                value = ClrDefault(property.PropertyType);
            Set(config, property, value);
        }
        return config;
    }

    private static object? ClrDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static void Set(AgreementRuleConfig config, PropertyInfo property, object? value) =>
        (property.SetMethod ?? throw new InvalidOperationException($"AgreementRuleConfig.{property.Name} cannot be set."))
        .Invoke(config, [value]);

    /// <summary>Non-vacuity of arm 1: no member holds the value a freshly built config would hold.</summary>
    private static void AssertEveryMemberDiffersFromFresh(AgreementRuleConfig config, AgreementRuleConfig fresh)
    {
        foreach (var property in ConfigProperties)
            Assert.False(Equals(property.GetValue(config), property.GetValue(fresh)),
                $"AgreementRuleConfig.{property.Name} must differ from a freshly built config in arm 1");
    }

    /// <summary>
    /// Every public instance property of <paramref name="actual"/> equals the original's, by reflection, and the number
    /// compared equals the type's public instance property count.
    /// </summary>
    private static void AssertEveryMemberEqual(AgreementRuleConfig expected, AgreementRuleConfig actual, string where)
    {
        var compared = 0;
        foreach (var property in typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.True(Equals(property.GetValue(expected), property.GetValue(actual)),
                $"{where}: AgreementRuleConfig.{property.Name} was {property.GetValue(expected) ?? "null"} and read back as {property.GetValue(actual) ?? "null"}");
            compared++;
        }
        Assert.Equal(typeof(AgreementRuleConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance).Length, compared);
    }

    /// <summary>The config frozen into a one-piece, one-entry <c>PlanInputs</c>, written and read back both ways.</summary>
    private static (AgreementRuleConfig ViaEvent, AgreementRuleConfig ViaProjection) StoreBothWays(AgreementRuleConfig config)
    {
        var inputs = PlanInputsWith(config, pieces: 1, withConfigRuns: true);
        var json = EventSerializer.Serialize(Manifest(inputs));

        // (i) The event path.
        var evt = Assert.IsType<SegmentManifestCreated>(EventSerializer.Deserialize("SegmentManifestCreated", json));
        var viaEvent = Assert.Single(evt.PlanInputs!.ConfigRuns!).Config;

        // (ii) The projection path: the event's own planInputs object, read with payroll's JsonOptions.
        var planInputs = JsonNode.Parse(json)!["planInputs"]!.ToJsonString();
        var viaProjection = Assert.Single(JsonSerializer.Deserialize<PlanInputs>(planInputs, PcsJsonOptions.Real)!.ConfigRuns!).Config;
        return (viaEvent, viaProjection);
    }

    private static SegmentManifestCreated Manifest(PlanInputs planInputs) => new()
    {
        ManifestId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        EmployeeId = "EMP-1",
        PeriodStart = new DateOnly(2026, 6, 1),
        PeriodEnd = new DateOnly(2026, 6, 30),
        CalculationKind = "forward-calc",
        CreatedAt = new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero),
        Segments = Array.Empty<PlannedSegment>(),
        PlanInputs = planInputs,
    };

    /// <summary>The <c>planInputs</c> object exactly as <see cref="EventSerializer"/> wrote it into the event.</summary>
    private static string PlanInputsText(PlanInputs inputs)
    {
        using var doc = JsonDocument.Parse(EventSerializer.Serialize(Manifest(inputs)));
        return doc.RootElement.GetProperty("planInputs").GetRawText();
    }

    /// <summary>
    /// A <c>PlanInputs</c> of <paramref name="pieces"/> pieces: with eight, six one-day context pieces and two segments of
    /// June 2026; each piece carries the same position and organisation, and (when asked) one config entry.
    /// </summary>
    private static PlanInputs PlanInputsWith(AgreementRuleConfig config, int pieces, bool withConfigRuns)
    {
        var ranges = new List<(DateOnly From, DateOnly To)>();
        if (pieces == 1)
        {
            ranges.Add((new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        }
        else
        {
            for (var d = new DateOnly(2026, 5, 25); d <= new DateOnly(2026, 5, 30); d = d.AddDays(1))
                ranges.Add((d, d));
            ranges.Add((new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15)));
            ranges.Add((new DateOnly(2026, 6, 16), new DateOnly(2026, 6, 30)));
        }

        var framePieces = ranges
            .Select(r => new FrozenPiece(r.From, r.To, 1.0m, "HK", "DEPARTMENT_HEAD") { OrgId = "STY01" })
            .ToList();
        return new PlanInputs(
            Pieces: framePieces,
            SettlementCutoff: null,
            OpeningFlexBalance: 0m,
            EntryEventIds: Array.Empty<Guid>(),
            AbsenceEventIds: Array.Empty<Guid>(),
            AsOfOutboxId: 1,
            EntriesDigest: new string('a', 64),
            AbsencesDigest: new string('b', 64),
            EvaluationModel: "weekly-timeline-v3")
        {
            ConfigRuns = withConfigRuns
                ? ranges.Select(r => new FrozenConfigRun(r.From, r.To, "HK", "OK26", "DEPARTMENT_HEAD", "STY01", config)).ToList()
                : null,
        };
    }

    /// <summary>
    /// The options the payroll service writes Rule Engine request bodies with, reached by field name through reflection the
    /// way <see cref="PcsJsonOptions"/> reaches <c>JsonOptions</c>; a renamed field fails loudly here.
    /// </summary>
    private static JsonSerializerOptions EngineRequestOptions() =>
        typeof(StatsTid.Integrations.Payroll.Services.PeriodCalculationService)
            .GetField("EngineRequestOptions", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as JsonSerializerOptions
        ?? throw new InvalidOperationException(
            "PeriodCalculationService.EngineRequestOptions (private static JsonSerializerOptions) was not found by reflection: " +
            "it was renamed or re-shaped. Update this accessor; do not hand-copy the options.");
}
