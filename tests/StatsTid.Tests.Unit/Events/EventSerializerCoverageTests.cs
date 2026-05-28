using System.Reflection;
using System.Runtime.CompilerServices;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Events;

/// <summary>
/// Guards against the class of bug where a new <see cref="DomainEventBase"/> descendant is
/// appended to the event store but never registered in <c>EventSerializer._eventTypeMap</c>,
/// causing <see cref="EventSerializer.Deserialize"/> to throw at replay time (Codex WARNING — S18).
/// </summary>
public class EventSerializerCoverageTests
{
    [Fact]
    public void EventSerializer_RegistersAllDomainEventBaseDescendants()
    {
        // Reflection route: access the private static EventTypeMap without widening the public API
        // of EventSerializer. This keeps the test honest to the shape of production code.
        var mapField = typeof(EventSerializer).GetField(
            "EventTypeMap",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(mapField);
        var map = (IReadOnlyDictionary<string, Type>)mapField!.GetValue(null)!;

        var domainEventBaseAssembly = typeof(DomainEventBase).Assembly;
        var descendants = domainEventBaseAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(DomainEventBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name)
            .ToList();

        Assert.NotEmpty(descendants);

        var unregistered = new List<string>();
        var unroundtrippable = new List<string>();

        foreach (var type in descendants)
        {
            // 1. Registration check — simple type name must map back to the type.
            if (!map.TryGetValue(type.Name, out var mappedType) || mappedType != type)
            {
                unregistered.Add(type.FullName ?? type.Name);
                continue;
            }

            // 2. Round-trip check — actually exercise Serialize/Deserialize for each type.
            DomainEventBase instance;
            try
            {
                instance = ConstructMinimalInstance(type);
            }
            catch (Exception ex)
            {
                unroundtrippable.Add($"{type.FullName}: construction failed — {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            try
            {
                var json = EventSerializer.Serialize(instance);
                var roundTripped = EventSerializer.Deserialize(type.Name, json);
                Assert.IsType(type, roundTripped);
            }
            catch (Exception ex)
            {
                unroundtrippable.Add($"{type.FullName}: round-trip failed — {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(
            unregistered.Count == 0,
            "The following DomainEventBase descendants are NOT registered in EventSerializer.EventTypeMap. " +
            "Every event appended to the event store must round-trip through Deserialize at replay time; " +
            "an unregistered type is a latent production bug. Add each to _eventTypeMap in EventSerializer.cs:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, unregistered.Select(n => "  - " + n)));

        Assert.True(
            unroundtrippable.Count == 0,
            "The following DomainEventBase descendants failed round-trip serialization:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, unroundtrippable.Select(n => "  - " + n)));

        // Reverse direction: no stale or typo'd map entries. A discriminator that no longer
        // corresponds to a concrete DomainEventBase descendant, or whose key is not the type's
        // simple name, silently breaks replay for any stream written under the old discriminator.
        var staleEntries = new List<string>();
        foreach (var kvp in map)
        {
            var mappedType = kvp.Value;
            if (mappedType.Assembly != domainEventBaseAssembly
                || mappedType.IsAbstract
                || mappedType.IsInterface
                || !typeof(DomainEventBase).IsAssignableFrom(mappedType))
            {
                staleEntries.Add($"{kvp.Key} -> {mappedType.FullName} (not a concrete DomainEventBase descendant in SharedKernel)");
                continue;
            }
            if (kvp.Key != mappedType.Name)
            {
                staleEntries.Add($"{kvp.Key} -> {mappedType.FullName} (discriminator must equal type.Name; got '{kvp.Key}')");
            }
        }

        Assert.True(
            staleEntries.Count == 0,
            "The following EventTypeMap entries are stale or misnamed. Every registration must map " +
            "discriminator=TypeName to a concrete DomainEventBase descendant in SharedKernel:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, staleEntries.Select(n => "  - " + n)));
    }

    [Fact]
    public void EventSerializer_RoundTrip_UserUpdated_PreservesAllFields()
    {
        var correlationId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 4, 18, 12, 34, 56, DateTimeKind.Utc);
        var eventId = Guid.NewGuid();

        var original = new UserUpdated
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "ADMIN001",
            ActorRole = "GlobalAdmin",
            CorrelationId = correlationId,
            UserId = "EMP042",
            DisplayName = "Anders Andersen",
            Email = "anders@example.dk",
            PrimaryOrgId = "ORG-001",
            AgreementCode = "AC"
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("UserUpdated", json);

        var result = Assert.IsType<UserUpdated>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("UserUpdated", result.EventType);
        Assert.Equal(1, result.Version);
        Assert.Equal("ADMIN001", result.ActorId);
        Assert.Equal("GlobalAdmin", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal("EMP042", result.UserId);
        Assert.Equal("Anders Andersen", result.DisplayName);
        Assert.Equal("anders@example.dk", result.Email);
        Assert.Equal("ORG-001", result.PrimaryOrgId);
        Assert.Equal("AC", result.AgreementCode);
    }

    /// <summary>
    /// Build a minimal instance of the given event type. We use
    /// <see cref="RuntimeHelpers.GetUninitializedObject"/> to bypass the <c>required</c>
    /// initializer enforcement that object-initializer syntax would require — this lets us
    /// generically instantiate any future event type without hard-coding per-type factories.
    /// We then populate every non-nullable string property with an empty string so the JSON
    /// payload is non-null and round-trippable under the serializer's default options.
    /// </summary>
    private static DomainEventBase ConstructMinimalInstance(Type type)
    {
        var instance = (DomainEventBase)RuntimeHelpers.GetUninitializedObject(type);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite) continue;

            // Only fill reference-type string properties that aren't nullable-annotated;
            // leaving other nullable fields as their default (null / zero) is acceptable
            // since JSON serialization handles them via WhenWritingNull.
            if (prop.PropertyType == typeof(string))
            {
                // Skip if already set (e.g. EventType getter-only wouldn't be CanWrite anyway,
                // but defensive guard against any default-initialised value).
                if (prop.GetValue(instance) is null)
                {
                    prop.SetValue(instance, string.Empty);
                }
            }
            // Non-nullable collection properties (e.g. WorkTimeRegistered.Intervals,
            // a required IReadOnlyList<WorkInterval>) serialize to JSON null when left
            // unset, which then fails to round-trip through a `required` initializer.
            // Populate them with an empty collection so the minimal instance is
            // round-trippable. Generic across closed generic collection interfaces /
            // concrete list types so future events need no per-type handling.
            else if (prop.GetValue(instance) is null
                     && IsSupportedCollection(prop.PropertyType, out var emptyValue))
            {
                prop.SetValue(instance, emptyValue);
            }
        }

        return instance;
    }

    /// <summary>
    /// True when <paramref name="type"/> is a closed generic <c>IReadOnlyList&lt;T&gt;</c>,
    /// <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, or
    /// <c>List&lt;T&gt;</c>; <paramref name="emptyValue"/> receives a fresh empty
    /// <c>List&lt;T&gt;</c> assignable to it. Lets <see cref="ConstructMinimalInstance"/>
    /// populate required non-nullable collection properties generically.
    /// </summary>
    private static bool IsSupportedCollection(Type type, out object? emptyValue)
    {
        emptyValue = null;
        if (!type.IsGenericType)
            return false;

        var def = type.GetGenericTypeDefinition();
        if (def != typeof(IReadOnlyList<>) && def != typeof(IList<>)
            && def != typeof(ICollection<>) && def != typeof(IEnumerable<>)
            && def != typeof(IReadOnlyCollection<>) && def != typeof(List<>))
        {
            return false;
        }

        var elementType = type.GetGenericArguments()[0];
        emptyValue = Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        return true;
    }
}
