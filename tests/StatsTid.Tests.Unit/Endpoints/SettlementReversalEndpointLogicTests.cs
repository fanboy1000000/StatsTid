using Microsoft.Extensions.Configuration;
using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S71 / TASK-7102 — the reversal endpoint's D13 go-live resolution
/// (<see cref="SettlementReversalEndpoints.ResolveGoLiveDate"/>): the
/// <c>SettlementCloseService</c> parse shape VERBATIM — strict ISO <c>yyyy-MM-dd</c> only;
/// unconfigured OR present-but-unparseable FAILS CLOSED to null (DORMANT — the endpoint then
/// refuses a REVERSE_AND_SUPERSEDE 409 rather than waiving the go-live clause by forwarding a
/// null floor; the S69 Step-7a W5 lesson: a permissive parse could ACTIVATE settlement on a
/// locale-misread date).
/// </summary>
public class SettlementReversalEndpointLogicTests
{
    private static IConfiguration Config(string? goLive)
    {
        var values = new Dictionary<string, string?>();
        if (goLive is not null)
            values["Settlement:GoLiveDate"] = goLive;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Unconfigured_Null_Dormant()
    {
        Assert.Null(SettlementReversalEndpoints.ResolveGoLiveDate(Config(null)));
    }

    [Fact]
    public void EmptyOrWhitespace_Null_Dormant()
    {
        Assert.Null(SettlementReversalEndpoints.ResolveGoLiveDate(Config("")));
        Assert.Null(SettlementReversalEndpoints.ResolveGoLiveDate(Config("   ")));
    }

    [Fact]
    public void StrictIso_Parses()
    {
        Assert.Equal(new DateOnly(2026, 1, 1),
            SettlementReversalEndpoints.ResolveGoLiveDate(Config("2026-01-01")));
    }

    /// <summary>A present-but-unparseable value fails CLOSED to dormant — never a guessed
    /// activation (locale-ambiguous, garbage, and non-exact ISO forms all refused).</summary>
    [Theory]
    [InlineData("06/08/2026")]
    [InlineData("2026-1-1")]
    [InlineData("2026-13-01")]
    [InlineData("not-a-date")]
    [InlineData("2026-01-01T00:00:00")]
    public void Unparseable_Null_FailsClosedToDormant(string raw)
    {
        Assert.Null(SettlementReversalEndpoints.ResolveGoLiveDate(Config(raw)));
    }
}
