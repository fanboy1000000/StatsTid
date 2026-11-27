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
/// (<c>G1-events.golden</c>, one line per type key: <c>key TAB json</c>). One fixed instance per type, every
/// public settable member set (see <see cref="GoldenInstanceFactory"/>), so the golden covers the null-omission
/// and each member's text form.
/// </para>
///
/// <para>
/// <b>Kind: R</b> (a regression pin of existing behaviour; the event bytes do not change). Red conditions: a
/// different byte in any key's JSON fails the serialize fact; a different byte on the read-then-write path fails
/// the replay fact; a map key added or removed without its golden line fails the coverage fact. Proved by the
/// mutation M-1 (drop <c>DefaultIgnoreCondition</c> from the serializer options) at the wave gate.
/// </para>
/// </summary>
public sealed class G1EventSerializerGoldenTests
{
    private const string File = "G1-events.golden";

    private static IReadOnlyDictionary<string, Type> Map()
    {
        var field = typeof(EventSerializer).GetField("EventTypeMap", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("EventSerializer.EventTypeMap not found");
        return (IReadOnlyDictionary<string, Type>)field.GetValue(null)!;
    }

    [Fact]
    public void Serialize_of_the_fixed_instance_of_every_event_type_equals_its_literal()
    {
        var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, type) in Map())
            actual[key] = EventSerializer.Serialize((IDomainEvent)GoldenInstanceFactory.Create(type));

        GoldenFile.AssertMatches(File, actual);
    }

    [Fact]
    public void Reading_the_literal_and_writing_it_again_gives_the_same_literal_for_every_event_type()
    {
        var golden = GoldenFile.Load(File);
        var problems = new List<string>();
        foreach (var (key, json) in golden)
        {
            var again = EventSerializer.Serialize(EventSerializer.Deserialize(key, json));
            if (!string.Equals(json, again, StringComparison.Ordinal))
                problems.Add($"{key}\n   golden: {json}\n   again:  {again}");
        }
        Assert.True(problems.Count == 0, $"{problems.Count} event types do not survive a replay read:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void The_golden_keys_are_exactly_the_keys_of_the_event_type_map()
    {
        var golden = GoldenFile.Load(File).Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var map = Map().Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(map, golden);
    }

    [Fact]
    public void The_literals_exercise_the_text_forms_the_goldens_exist_to_pin()
    {
        var values = GoldenFile.Load(File).Values.ToArray();
        var all = string.Join("\n", values);
        Assert.Contains("\\u00C6r\\u00F8 \\u00C5/\\u00F8", all);   // Danish letters, escaped by the default encoder
        Assert.Contains(values, v => v.Contains("\"correlationId\""));   // an optional member that is set
        Assert.Contains(values, v => !v.Contains("\"actorRole\""));      // an optional member that is omitted when null
        Assert.Contains("\\u003C", all);                 // '<' escaped by the default encoder
        Assert.Contains("\\u0026", all);                 // '&' escaped
        Assert.Contains("\\u0027", all);                 // apostrophe escaped
        Assert.Contains("\\u002B", all);                 // '+' escaped
        Assert.Contains("7.40", all);                    // decimal scale kept
        Assert.Contains("-0.25", all);
        Assert.Matches("\"occurredAt\":\"2026-03-0\\dT08:30:15\\.1234567Z\"", all);   // Utc
        Assert.Matches("\"[A-Za-z]+\":\"2026-03-\\d\\dT08:30:15\\.1234567\"", all);   // Unspecified (no zone)
        Assert.DoesNotContain("0000-0000-0000-0000-000000000000", all);
    }
}
