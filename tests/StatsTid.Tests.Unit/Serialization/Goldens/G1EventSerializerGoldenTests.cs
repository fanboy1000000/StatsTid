using System.Reflection;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Serialization.Goldens;

/// <summary>
/// G-1 (TASK-14900, ADR-045; DEP-003, ADR-005) — the stored bytes of every event type the event store knows.
///
/// <para>
/// <b>Plain language.</b> Every event ever written is the text <c>EventSerializer.Serialize</c> produced, and a
/// replay reads that text back. If a runtime or library move changed how a date, a decimal, an enum, a Danish
/// letter or an omitted null is written, today's tests would not notice, because they compute their expected
/// text with the same serializer. These facts compare against literals captured before the move
/// (<c>G1-events.golden</c>). There are two lines for every key of the event type map:
/// <list type="bullet">
/// <item><c>&lt;key&gt;</c>: one fixed instance with every public settable member set, no member null, every
/// collection holding two elements. This is the line that pins the text form of every member, the optional ones
/// included (for example the frozen plan inputs an event carries).</item>
/// <item><c>&lt;key&gt;#nulls</c>: the same type, but every second nullable member left null, so the writer's
/// omission of a null member is part of the literal.</item>
/// </list>
/// The instances come from <see cref="GoldenInstanceFactory"/>; the <c>#nulls</c> lines depend on its reading of
/// nullable annotations. If a serialize fact is red while the replay fact (literal back to literal) stays green,
/// the factory drifted; it does not mean the stored bytes changed.
/// </para>
///
/// <para>
/// <b>Kind: R</b> (a regression pin of existing behaviour; the event bytes do not change). Red conditions: a
/// different byte in any line fails the serialize fact; a different byte on the read-then-write path fails the
/// replay fact; a map key added or removed without its two golden lines fails the coverage fact. Proved by the
/// mutation M-1 (the serializer options write nulls) at the wave gate.
/// </para>
/// </summary>
public sealed class G1EventSerializerGoldenTests
{
    private const string File = "G1-events.golden";
    private const string NullsSuffix = "#nulls";

    private static IReadOnlyDictionary<string, Type> Map()
    {
        var field = typeof(EventSerializer).GetField("EventTypeMap", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("EventSerializer.EventTypeMap not found");
        return (IReadOnlyDictionary<string, Type>)field.GetValue(null)!;
    }

    [Fact]
    public void Serialize_of_the_fixed_instances_of_every_event_type_equals_their_literals()
    {
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, type) in Map())
        {
            actual[key] = EventSerializer.Serialize((IDomainEvent)GoldenInstanceFactory.Create(type, alternateNulls: false));
            actual[key + NullsSuffix] = EventSerializer.Serialize((IDomainEvent)GoldenInstanceFactory.Create(type, alternateNulls: true));
        }

        GoldenFile.AssertMatches(File, actual);
    }

    [Fact]
    public void Reading_the_literals_and_writing_them_again_gives_the_same_literals_for_every_event_type()
    {
        var golden = GoldenFile.Load(File);
        var problems = new List<string>();
        foreach (var (line, json) in golden)
        {
            var key = line.EndsWith(NullsSuffix, StringComparison.Ordinal) ? line[..^NullsSuffix.Length] : line;
            var again = EventSerializer.Serialize(EventSerializer.Deserialize(key, json));
            if (!string.Equals(json, again, StringComparison.Ordinal))
                problems.Add($"{line}\n   golden: {json}\n   again:  {again}");
        }
        Assert.True(problems.Count == 0, $"{problems.Count} lines do not survive a replay read:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void The_golden_lines_are_exactly_two_for_every_key_of_the_event_type_map()
    {
        var golden = GoldenFile.Load(File).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var map = Map().Keys.SelectMany(k => new[] { k, k + NullsSuffix }).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(map, golden);
    }

    /// <summary>
    /// Capture sanity: reads only the data file, so it is not a pin. It fails if a capture lost the text forms the
    /// goldens exist to pin (Danish letters and encoder escapes, decimal scale, both DateTime kinds, both booleans,
    /// set and omitted optional members, a populated frozen-inputs member on the event path).
    /// </summary>
    [Fact]
    public void Capture_sanity_the_literals_exercise_the_text_forms_the_goldens_exist_to_pin()
    {
        var golden = GoldenFile.Load(File);
        var values = golden.Values.ToArray();
        var all = string.Join("\n", values);
        Assert.Contains("\\u00C6r\\u00F8 \\u00C5/\\u00F8", all);   // Danish letters, escaped by the default encoder
        Assert.Contains("\\u003C", all);                 // '<' escaped by the default encoder
        Assert.Contains("\\u0026", all);                 // '&' escaped
        Assert.Contains("\\u0027", all);                 // apostrophe escaped
        Assert.Contains("\\u002B", all);                 // '+' escaped
        Assert.Contains("7.40", all);                    // decimal scale kept
        Assert.Contains("-0.25", all);
        Assert.Matches("\"occurredAt\":\"2026-03-\\d\\dT08:30:15\\.1234567Z\"", all);   // Utc
        Assert.Matches("\"occurredAt\":\"2026-03-\\d\\dT08:30:15\\.1234567\"", all);    // Unspecified (no zone)
        Assert.Contains(":true", all);
        Assert.Contains(":false", all);
        Assert.Contains(values, v => v.Contains("\"actorRole\""));       // an optional member that is set
        Assert.Contains(values, v => !v.Contains("\"actorRole\""));      // an optional member omitted when null
        Assert.Contains(golden, kv => !kv.Key.EndsWith(NullsSuffix, StringComparison.Ordinal) && kv.Value.Contains("\"planInputs\""));
        Assert.Contains("\"WtmNaturalKey\":{\"okVersion\":\"OK24\"", all);
        Assert.Contains("\"ZeroValue\":null", all);
        Assert.DoesNotContain("0000-0000-0000-0000-000000000000", all);
    }
}
