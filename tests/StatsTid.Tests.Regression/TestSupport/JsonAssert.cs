using System.Text.Json;
using Xunit;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S146 / TASK-14603 — reads a required member of a JSON response body so that a missing member fails as a
/// NAMED xUnit assertion (member + members present), not as a <see cref="KeyNotFoundException"/> crash from
/// <c>JsonElement.GetProperty</c> whose message names nothing the reader can act on.
/// </summary>
public static class JsonAssert
{
    public static JsonElement RequiredMember(JsonElement root, string name)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var member))
            return member;

        var present = root.ValueKind == JsonValueKind.Object
            ? string.Join(", ", root.EnumerateObject().Select(p => p.Name))
            : $"(not an object: {root.ValueKind})";
        Assert.Fail($"Expected the JSON body to carry member '{name}', but it is missing. Members present: [{present}].");
        return default; // unreachable: Assert.Fail throws
    }
}
