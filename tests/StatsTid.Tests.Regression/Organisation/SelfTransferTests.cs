using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Organisation;

/// <summary>
/// S148 / TASK-14820-T (AC-C13h part 1) — nobody moves themselves between institutions.
///
/// <para>
/// <b>In plain language.</b> The institution a person belongs to decides which local rules (weekly
/// norm, flex cap) apply to that person's own pay, so moving yourself would let you choose your own
/// pay rules. The move is refused for every role and a second administrator must do it. The refusal is
/// narrow: the same administrator can still move someone else between the same two institutions.
/// </para>
///
/// <para>
/// <b>Fixture.</b> Fixed clock Tuesday 10 November 2026. The actor is a LocalHR who is also a person:
/// a <see cref="TestSupport.RegressionSeed"/> user homed in STY01, managing nobody, whose token
/// (employee id = that user) carries ORG_ONLY LocalHR scope over STY01 and STY02. Docker-gated and
/// CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SelfTransferTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _host = _factory.WithFixedToday(Clock);
        _ = _host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>F.</b> The acting LocalHR moves THEMSELVES STY01 → STY02: 403 with the date-free reason
    /// (<c>error</c> "Access denied", <c>reason</c> "Own organisation cannot be changed; a second
    /// administrator must perform this change"); nothing is written (organisation rows, live column,
    /// events, users version).
    /// <para><b>Red conditions.</b> Before C-13 a self-transfer is allowed: the request answers 200 and
    /// <c>Assert.Equal(Forbidden, status)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task HrTransfersThemselves_403_NothingWritten()
    {
        var cs = _harness.ConnectionString;
        var self = await SeedPersonAsync(cs, "STY01");
        var client = LocalHrClient(_host, self, "STY01", "STY02");
        var before = await SnapshotAsync(cs, self);

        var rsp = await PutUserAsync(client, cs, self, new { primaryOrgId = "STY02" });

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Access denied", body.GetProperty("error").GetString());
        Assert.Equal("Own organisation cannot be changed; a second administrator must perform this change",
            body.GetProperty("reason").GetString());
        Assert.Equal(before, await SnapshotAsync(cs, self));
    }

    /// <summary>
    /// <b>R.</b> The same actor moves ANOTHER person STY01 → STY02: 200 and the new organisation row.
    /// <b>Discriminating assertion:</b> the 200 with the two-row history. It breaks if the self refusal
    /// is widened to every transfer by an HR user who is a person, or if it compares organisations
    /// instead of identities.
    /// </summary>
    [Fact]
    public async Task HrTransfersSomeoneElse_SameOrganisations_200()
    {
        var cs = _harness.ConnectionString;
        var self = await SeedPersonAsync(cs, "STY01");
        var other = await SeedPersonAsync(cs, "STY01");
        var client = LocalHrClient(_host, self, "STY01", "STY02");

        var rsp = await PutUserAsync(client, cs, other, new { primaryOrgId = "STY02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadOrgRowsAsync(cs, other);
        Assert.Equal(2, rows.Count);
        Assert.Equal((Clock, (DateOnly?)null, "STY02"), (rows[1].From, rows[1].To, rows[1].OrgId));
    }
}
