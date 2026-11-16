namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14818-T — AC-C2d: a publish's audit row records the date it applies from, and the archival
/// audit of the version it replaces (SEC-035) still fires. Plain language: the audit trail must say "this
/// version applies from 9 November", not only "it was published", and replacing a version must still leave
/// the replaced version's own ARCHIVED trace. Docker-gated: CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigPublishAuditTests : DatedConfigDockerFixture
{
    /// <summary>
    /// AC-C2d. Clock Monday 9 November 2026. K / OK26: v1 published dated Monday 2 November; v2 (same norm)
    /// published dated Monday 9 November. v2's PUBLISHED audit row: <c>new_data</c> holds <c>2026-11-09</c> and
    /// <c>archivedConfigId</c> = v1. v1's ARCHIVED audit row (<c>previous_data</c> <c>{"status":"ACTIVE"}</c>,
    /// <c>new_data</c> <c>{"status":"ARCHIVED"}</c>) and its <c>AgreementConfigArchived</c> outbox row exist.
    ///
    /// <para>
    /// <b>Kind: F (CI)</b>. <b>Red conditions</b> (argued against the base): the base's PUBLISHED row's
    /// <c>new_data</c> is <c>{"archivedConfigId":…}</c> only, so the first assertion (<c>appliesFrom</c> =
    /// <c>2026-11-09</c>) fails; the archival half rides along as the second assertion.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AuditRecordsTheDate_ArchivalAuditStillFires()
    {
        using var admin = AdminAt(new DateOnly(2026, 11, 9));
        var code = UniqueCode("K_AUD_");

        var v1 = await CreateAndPublishAsync(admin, ConfigBody(code), "2026-11-02");
        var v2 = await CreateAndPublishAsync(admin, ConfigBody(code), "2026-11-09");

        Assert.Equal("2026-11-09", await ScalarAsync<string>(
            "SELECT new_data ->> 'appliesFrom' FROM agreement_config_audit WHERE config_id = @id AND action = 'PUBLISHED'",
            ("id", v2)));
        Assert.Equal(v1.ToString(), await ScalarAsync<string>(
            "SELECT new_data ->> 'archivedConfigId' FROM agreement_config_audit WHERE config_id = @id AND action = 'PUBLISHED'",
            ("id", v2)));

        Assert.Equal(1L, await CountAsync(
            """
            SELECT COUNT(*) FROM agreement_config_audit
            WHERE config_id = @id AND action = 'ARCHIVED'
              AND previous_data = '{"status":"ACTIVE"}'::jsonb
              AND new_data = '{"status":"ARCHIVED"}'::jsonb
            """, ("id", v1)));
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @stream AND event_type = 'AgreementConfigArchived'",
            ("stream", $"agreement-config-{v1}")));
    }
}
