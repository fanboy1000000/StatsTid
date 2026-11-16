using System.Net;
using System.Text.Json;
using StatsTid.Tests.Regression.Contracts;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14818-T — AC-C2k's Docker half: only a draft can be archived. Plain language: once versions are
/// dated, archiving the active configuration would quietly bring an older version back into force, so the
/// system refuses and says "publish a successor". Docker-gated: CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigArchiveTests : DatedConfigDockerFixture
{
    private const string RefusalText =
        "Only a draft can be archived. Publish a successor to replace the active configuration.";

    private static Task<HttpResponseMessage> ArchiveAsync(HttpClient client, Guid configId, long version)
        => client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{configId}/archive", jsonBody: null, ifMatchVersion: version));

    /// <summary>
    /// AC-C2k. A published K / OK26 version (ACTIVE) is archived with its current ETag: 409 with the remedy in
    /// the body; afterwards the row is ACTIVE at the same version, with no ARCHIVED audit row and no
    /// <c>AgreementConfigArchived</c> outbox row for it. The version check is not what answers (ruling B7): the
    /// If-Match is the current one.
    ///
    /// <para>
    /// <b>Kind: F (CI)</b>. <b>Red conditions</b> (argued against the base): the base archives an ACTIVE row and
    /// answers 200 with status ARCHIVED, so the 409 assertion fails first.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ArchiveActive_409PublishASuccessor_NothingWritten()
    {
        using var client = AdminAt(new DateOnly(2026, 11, 9));
        var code = UniqueCode("K_ARCA_");
        var (id, draftVersion) = await CreateDraftAsync(client, ConfigBody(code));
        var published = await PublishOkAsync(client, id, draftVersion, "2026-11-02");
        Assert.Equal("ACTIVE", published.GetProperty("status").GetString());
        var activeVersion = await ScalarAsync<long>("SELECT version FROM agreement_configs WHERE config_id = @id", ("id", id));

        using var response = await ArchiveAsync(client, id, activeVersion);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict,
            $"archiving an ACTIVE config must answer 409, got {(int)response.StatusCode}: {text}");
        Assert.Equal(RefusalText, JsonDocument.Parse(text).RootElement.GetProperty("error").GetString());

        Assert.Equal("ACTIVE", await StatusOfConfigAsync(id));
        Assert.Equal(activeVersion, await ScalarAsync<long>("SELECT version FROM agreement_configs WHERE config_id = @id", ("id", id)));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM agreement_config_audit WHERE config_id = @id AND action = 'ARCHIVED'", ("id", id)));
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @stream AND event_type = 'AgreementConfigArchived'",
            ("stream", $"agreement-config-{id}")));
    }

    /// <summary>
    /// AC-C2k. A K / OK26 DRAFT is archived: 200, ARCHIVED, and the audit row records
    /// <c>previous_data</c> <c>{"status":"DRAFT"}</c>.
    ///
    /// <para>
    /// <b>Kind: R (CI)</b>, green on the base. <b>Discriminating assertion:</b> the 200 and the DRAFT audit row
    /// fail against a refusal that also caught drafts (the ACTIVE-only refusal must not widen).
    /// </para>
    /// </summary>
    [Fact]
    public async Task ArchiveDraft_200()
    {
        using var client = AdminAt(new DateOnly(2026, 11, 9));
        var code = UniqueCode("K_ARCD_");
        var (id, version) = await CreateDraftAsync(client, ConfigBody(code));

        using var response = await ArchiveAsync(client, id, version);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"archiving a DRAFT must answer 200, got {(int)response.StatusCode}: {text}");

        Assert.Equal("ARCHIVED", await StatusOfConfigAsync(id));
        Assert.Equal(1L, await CountAsync(
            """
            SELECT COUNT(*) FROM agreement_config_audit
            WHERE config_id = @id AND action = 'ARCHIVED'
              AND previous_data = '{"status":"DRAFT"}'::jsonb
              AND new_data = '{"status":"ARCHIVED"}'::jsonb
            """, ("id", id)));
    }
}
