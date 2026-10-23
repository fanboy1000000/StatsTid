using System.Text.Json;
using Xunit;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.TestSupport;

public class JsonAssertTests
{
    /// <summary>
    /// S146 / TASK-14603, kind N (names a helper this sprint creates). A body without <c>reason</c> must fail
    /// as an xUnit assertion that names <c>reason</c> and the members present - never as a
    /// <see cref="KeyNotFoundException"/>. Red condition: with the helper's <c>TryGetProperty</c> replaced by
    /// <c>GetProperty</c> the call throws <c>KeyNotFoundException</c>, so <c>Assert.ThrowsAny&lt;XunitException&gt;</c> fails.
    /// </summary>
    [Fact]
    public void RequiredMember_MissingMember_FailsAsAssertion_NotKeyNotFound()
    {
        using var doc = JsonDocument.Parse("""{"kind":"recalc-blocked","success":false}""");

        var ex = Assert.ThrowsAny<XunitException>(() => JsonAssert.RequiredMember(doc.RootElement, "reason"));

        Assert.IsNotType<KeyNotFoundException>(ex);
        Assert.Contains("'reason'", ex.Message);
        Assert.Contains("kind", ex.Message);
        Assert.Contains("success", ex.Message);
    }

    /// <summary>S146 / TASK-14603, kind R - the helper returns the member when present.</summary>
    [Fact]
    public void RequiredMember_PresentMember_ReturnsIt()
    {
        using var doc = JsonDocument.Parse("""{"reason":"mid-week"}""");

        Assert.Equal("mid-week", JsonAssert.RequiredMember(doc.RootElement, "reason").GetString());
    }
}
