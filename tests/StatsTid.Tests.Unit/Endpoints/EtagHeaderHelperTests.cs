using Microsoft.AspNetCore.Http;
using StatsTid.Backend.Api.Endpoints.Helpers;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S133 / TASK-13306 (QUAL-111) — the ONE direct unit test of the shared
/// <see cref="EtagHeaderHelper"/> If-Match parser.
///
/// <para>This replaces the legitimate part of the eight identically-bodied
/// <c>*_MissingIfMatch_HelperRejects</c> clones that were removed from the three Concurrency
/// regression files. Those clones each called <c>TryParseIfMatch</c> on a header-less request and
/// were NAMED for an endpoint they never drove (QUAL-111 verification theater, PAT-014). The
/// per-endpoint 428 behavior now lives in the genuine HTTP tests
/// (<c>Hosting.*PreconditionHttpTests</c> + <c>Config.WageTypeMappingEndpointTests</c>); THIS file
/// keeps a single, honestly-scoped unit test of the helper's own branches — it is NOT endpoint
/// coverage and must never be counted as such.</para>
///
/// <para>Falsifiability: each branch is asserted at the helper surface (missing → false + hint;
/// quoted version → parsed long; admin-strict rejects If-None-Match; malformed → false; the
/// flexible mode accepts <c>If-None-Match: *</c> as first-create). RED if any branch's decision or
/// parsed value regresses.</para>
/// </summary>
public class EtagHeaderHelperTests
{
    // ── admin-strict TryParseIfMatch ────────────────────────────────────────────

    [Fact]
    public void TryParseIfMatch_MissingHeader_ReturnsFalse_WithMissingHint()
    {
        var parsed = EtagHeaderHelper.TryParseIfMatch(Request(), out var version, out var error);

        Assert.False(parsed);
        Assert.Equal(0, version);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseIfMatch_QuotedVersion_ReturnsTrue_WithParsedVersion()
    {
        var parsed = EtagHeaderHelper.TryParseIfMatch(
            Request(r => r.Headers["If-Match"] = "\"5\""), out var version, out var error);

        Assert.True(parsed);
        Assert.Equal(5, version);
        Assert.Null(error);
    }

    [Fact]
    public void TryParseIfMatch_IfNoneMatchSupplied_ReturnsFalse_AdminStrictRejects()
    {
        var parsed = EtagHeaderHelper.TryParseIfMatch(
            Request(r => r.Headers["If-None-Match"] = "*"), out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("If-None-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseIfMatch_NonNumericBody_ReturnsFalse()
    {
        var parsed = EtagHeaderHelper.TryParseIfMatch(
            Request(r => r.Headers["If-Match"] = "\"not-a-version\""), out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("not a valid version", error, StringComparison.Ordinal);
    }

    // ── flexible TryParseIfMatchOrIfNoneMatchStar ───────────────────────────────

    [Fact]
    public void TryParseIfMatchOrIfNoneMatchStar_FirstCreateStar_ReturnsTrue_NullVersion()
    {
        var parsed = EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar(
            Request(r => r.Headers["If-None-Match"] = "*"), out var version, out var error);

        Assert.True(parsed);
        Assert.Null(version); // null = first-create precondition
        Assert.Null(error);
    }

    [Fact]
    public void TryParseIfMatchOrIfNoneMatchStar_MissingBoth_ReturnsFalse()
    {
        var parsed = EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar(
            Request(), out _, out var error);

        Assert.False(parsed);
        Assert.NotNull(error);
    }

    // ── helper ──────────────────────────────────────────────────────────────────

    private static HttpRequest Request(Action<HttpRequest>? configure = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "PUT";
        configure?.Invoke(ctx.Request);
        return ctx.Request;
    }
}
