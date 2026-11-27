using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;

namespace StatsTid.Tests.Unit.Serialization.Goldens;

/// <summary>
/// Builds one fixed, deterministic instance of a type for the persisted-bytes goldens (TASK-14900).
///
/// <para>
/// Every public settable member is set to a value taken from a fixed pool by a running counter, so the same
/// type always gets the same instance. The order is explicit and never the runtime's reflection order:
/// members are visited in ordinal name order, and a constructor is chosen by parameter count (most first), then
/// by the ordinal order of its parameter type names. That includes the members that otherwise default to "now" or a random id
/// (<c>EventId</c>, <c>OccurredAt</c>). Every collection gets two elements. Across the pools: Danish letters
/// and characters the default JSON encoder escapes, decimals with different scales (<c>7.40</c>, <c>8.0</c>,
/// <c>-0.25</c>), a UTC and an unspecified <c>DateTime</c>, <c>DateOnly</c>, <c>TimeOnly</c> with 100-ns
/// precision, enums, and Guids. With <c>alternateNulls</c> every second nullable member is left null (when it is
/// null on a fresh instance), so the writer's null-omission is part of the literal; without it every member is
/// set.
/// </para>
/// </summary>
internal sealed class GoldenInstanceFactory
{
    private static readonly string[] Strings =
    {
        "Ærø Å/ø <&'\"+>", "Næstved", "ÆØÅ-æøå", "plain-text", "tab\there", "a|b\\c",
    };
    private static readonly decimal[] Decimals = { 7.40m, 8.0m, -0.25m, 0.5m, 37.00m, 0.25m };
    private static readonly double[] Doubles = { 1.5, 0.1, -2.75 };
    private static readonly TimeOnly[] Times =
    {
        new TimeOnly(8, 30, 15).Add(TimeSpan.FromTicks(1234567)), new TimeOnly(16, 45, 0), new TimeOnly(22, 0, 0).Add(TimeSpan.FromTicks(1)),
    };

    private readonly bool _alternateNulls;
    private readonly NullabilityInfoContext _nullability = new();
    private int _n;
    private int _nullable;

    private GoldenInstanceFactory(bool alternateNulls) => _alternateNulls = alternateNulls;

    public static object Create(Type type, bool alternateNulls = true) =>
        new GoldenInstanceFactory(alternateNulls).Build(type, 0)!;

    public static T Create<T>(bool alternateNulls = true) where T : class => (T)Create(typeof(T), alternateNulls);

    private object? Build(Type type, int depth)
    {
        if (depth > 10) throw new InvalidOperationException($"golden factory: nesting too deep at {type}");
        type = Nullable.GetUnderlyingType(type) ?? type;
        var i = _n++;

        if (type == typeof(string)) return Strings[i % Strings.Length];
        if (type == typeof(decimal)) return Decimals[i % Decimals.Length];
        if (type == typeof(double)) return Doubles[i % Doubles.Length];
        if (type == typeof(float)) return (float)Doubles[i % Doubles.Length];
        if (type == typeof(int)) return 2 + i;
        if (type == typeof(long)) return 1000L + i;
        if (type == typeof(short)) return (short)(2 + i);
        if (type == typeof(byte)) return (byte)(2 + i);
        if (type == typeof(bool)) return i % 3 != 2;   // true, true, false: the writer writes both forms
        if (type == typeof(Guid)) return Guid.Parse($"{i + 1:x8}-aaaa-4bbb-8ccc-{i + 1:x12}");
        if (type == typeof(DateTime))
            return DateTime.SpecifyKind(new DateTime(2026, 3, 2, 8, 30, 15).AddDays(i).AddTicks(1234567),
                i % 2 == 0 ? DateTimeKind.Utc : DateTimeKind.Unspecified);
        if (type == typeof(DateTimeOffset))
            return new DateTimeOffset(2026, 3, 2, 8, 30, 15, TimeSpan.FromHours(1)).AddDays(i);
        if (type == typeof(DateOnly)) return new DateOnly(2026, 3, 2).AddDays(i);
        if (type == typeof(TimeOnly)) return Times[i % Times.Length];
        if (type == typeof(TimeSpan)) return TimeSpan.FromMinutes(444 + i);
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            var pick = values[i % values.Length];
            if (values.Length > 1 && Convert.ToInt64(pick) == 0) pick = values[(i + 1) % values.Length];
            return pick;
        }
        if (type == typeof(byte[])) return new byte[] { 1, 2, 3 };
        if (type == typeof(System.Text.Json.JsonElement))
            return (i % 3) switch
            {
                0 => System.Text.Json.JsonSerializer.SerializeToElement(Strings[i % Strings.Length]),
                1 => System.Text.Json.JsonSerializer.SerializeToElement(Decimals[i % Decimals.Length]),
                _ => System.Text.Json.JsonSerializer.SerializeToElement(true),
            };

        if (TryDictionary(type, depth, out var dictionary)) return dictionary;
        if (TryCollection(type, depth, out var collection)) return collection;
        return BuildObject(type, depth);
    }

    private bool TryDictionary(Type type, int depth, out object? result)
    {
        result = null;
        var generic = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
        if (generic != typeof(Dictionary<,>) && generic != typeof(IDictionary<,>) && generic != typeof(IReadOnlyDictionary<,>))
            return false;
        var args = type.GetGenericArguments();
        if (args[0] == typeof(string) && args[1] == typeof(object))
        {
            // An object-typed slot has no shape to build from. The one such slot in the event map is
            // SegmentSnapshot.Values: the production shape is one WtmNaturalKey value (OK version, agreement
            // code, position) beside a null; the null makes the object slot's null form part of the literal.
            result = new Dictionary<string, object?>
            {
                ["WtmNaturalKey"] = new Integrations.Payroll.Services.WtmNaturalKey("OK24", "HK", "Fuldmægtig"),
                ["ZeroValue"] = null,
            };
            return true;
        }
        var dictionary =(IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args))!;
        for (var k = 0; k < 2; k++)
        {
            var key = args[0].IsEnum
                ? Enum.GetValues(args[0]).GetValue(k % Enum.GetValues(args[0]).Length)!
                : Build(args[0], depth + 1)!;
            dictionary[key] = Build(args[1], depth + 1);
        }
        result = dictionary;
        return true;
    }

    private bool TryCollection(Type type, int depth, out object? result)
    {
        result = null;
        Type? element = null;
        if (type.IsArray) element = type.GetElementType();
        else if (type.IsGenericType)
        {
            var generic = type.GetGenericTypeDefinition();
            if (generic == typeof(List<>) || generic == typeof(IList<>) || generic == typeof(IReadOnlyList<>)
                || generic == typeof(IEnumerable<>) || generic == typeof(ICollection<>) || generic == typeof(IReadOnlyCollection<>))
                element = type.GetGenericArguments()[0];
        }
        if (element is null) return false;

        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
        for (var k = 0; k < 2; k++) list.Add(Build(element, depth + 1));
        if (type.IsArray)
        {
            var array = Array.CreateInstance(element, 2);
            list.CopyTo(array, 0);
            result = array;
        }
        else result = list;
        return true;
    }

    private static bool Ignored(PropertyInfo p) =>
        p.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always };

    private object BuildObject(Type type, int depth)
    {
        object instance;
        var coveredByConstructor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameterless = type.GetConstructor(Type.EmptyTypes);
        if (parameterless is not null && !type.IsAbstract)
            instance = parameterless.Invoke(null);
        else
        {
            var ctor = type.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .ThenBy(c => string.Join(",", c.GetParameters().Select(p => p.ParameterType.FullName)), StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new NotSupportedException($"golden factory: no public constructor on {type}");
            var args = new List<object?>();
            foreach (var parameter in ctor.GetParameters())
            {
                coveredByConstructor.Add(parameter.Name!);
                args.Add(Member(parameter.ParameterType, _nullability.Create(parameter), depth, current: null));
            }
            instance = ctor.Invoke(args.ToArray());
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0 && !Ignored(p))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (coveredByConstructor.Contains(p.Name)) continue;
            var value = Member(p.PropertyType, _nullability.Create(p), depth, p.GetValue(instance));
            if (value is not null) p.SetValue(instance, value);
        }
        return instance;
    }

    /// <summary>A member's value: null for every second nullable member (when it is null already), else built.</summary>
    private object? Member(Type type, NullabilityInfo info, int depth, object? current)
    {
        var optional = Nullable.GetUnderlyingType(type) is not null
            || (!type.IsValueType && info.WriteState == NullabilityState.Nullable);
        if (optional && _alternateNulls && current is null && _nullable++ % 2 == 1) return null;
        return Build(type, depth + 1);
    }
}
